using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Runs the production HostAuthority.IdentityStrikes.cs / MemoryTunings.cs against native API doubles whose DealDamage ordering mirrors the
    /// decompiled Actor.DealDamage, and a faithful copy of Gem_L_DivineFaith's tracker/amplifier registration (Gem_L_DivineFaith.OnEquipSkill):
    ///   _tracker = skill.TrackKills(6 s)   -> KillTracker subscribes skill.ActorEvent_OnDealDamage
    ///   skill.dealtDamageProcessor.Add(Amplify)
    /// </summary>
    public sealed class IdentityStrikeNativeTests
    {
        private sealed class DivineFaithSim
        {
            internal int Stack;
            internal readonly Dictionary<Entity, float> Tracked = new Dictionary<Entity, float>();
            internal DivineFaithSim(SkillTrigger skill)
            {
                skill.ActorEvent_OnDealDamage += info => { if (info.victim.currentHealth > 0f) Tracked[info.victim] = UnityEngine.Time.time; };
                skill.SimDealtDamage.Add(Amplify);
            }
            private void Amplify(ref DamageData data, Actor actor, Entity target)
            {
                if (data.IsAmountModifiedBy(typeof(DivineFaithSim))) return;
                data.ApplyAmplification(Stack * 0.004f);
                data = data.SetAmountModifiedBy(typeof(DivineFaithSim));
            }
            internal void Dies(Entity victim)
            {
                if (Tracked.TryGetValue(victim, out float at) && UnityEngine.Time.time - at <= 6f) { Tracked.Remove(victim); Stack++; }
            }
        }

        private static Build BuildOf(params AuthoredMechanismSpec[] specs)
        {
            var build = new Build();
            foreach (var spec in specs) build.Mechanisms.Add(new AuthoredMechanismEntry { StarId = "test." + spec.ChannelId, ContributorIds = new[] { "test." + spec.ChannelId }, Spec = spec });
            return Build.Decode(build.Encode());
        }

        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime, Hero Hero, SkillTrigger Identity) Setup(Build build, bool killingFlow)
        {
            Mirror.NetworkServer.active = true; UnityEngine.Time.time = 0; DewPlayer.gamePlayers.Clear(); DewPhysics.Entities.Clear();
            NativeAttributedDamagePacket.Current = null; NativeMemoryPayloadScope.Current = null;
            Actor.SimDealerLog.Clear(); Actor.SimDamageLog.Clear();
            var hero = new Hero();
            hero.Status.attackDamage = 100; hero.Status.abilityPower = 100;
            SkillTrigger identity = killingFlow ? (SkillTrigger)new St_D_TheKillingFlow { owner = hero, parentActor = hero } : new St_D_ScarOfTheWind { owner = hero, parentActor = hero };
            hero.Skill.Skills[HeroSkillLocation.Identity] = identity;
            hero.owner = new DewPlayer { hero = hero, isHumanPlayer = true };
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, HeroKey = "Hero_Husk", Powers = new PowerRuntime(build, 0) };
            var host = new HostAuthority(); host.BindAuthored(runtime, build);
            return (host, runtime, hero, identity);
        }

        private static Entity Enemy(float x, float z)
        {
            var enemy = new Entity { Relation = EntityRelation.Enemy, position = new UnityEngine.Vector3(x, 0, z) };
            DewPhysics.Entities.Add(enemy);
            return enemy;
        }

        [Fact]
        public void Wind_scar_strike_is_dealt_by_the_identity_memory_so_divine_faith_tracks_and_amplifies_it_but_hero_damage_is_invisible()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.WindStrike())), killingFlow: false);
            var faith = new DivineFaithSim(skill);
            var front = Enemy(0, 2); var side = Enemy(1, 3); var behind = Enemy(0, -3); var far = Enemy(0, 9);
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, front, 1);
            Assert.Empty(Actor.SimDamageLog); // deferred: never inside the basic attack's own DealDamage
            host.UpdateIdentityStrikes();
            // 1. The damage Actor is the identity SkillTrigger itself (what KillTracker / Amplify are bound to), never the hero.
            Assert.NotEmpty(Actor.SimDamageLog);
            Assert.All(Actor.SimDamageLog, entry => Assert.Same(skill, entry.Dealer));
            Assert.DoesNotContain(Actor.SimDealerLog, dealer => ReferenceEquals(dealer, hero));
            // 2. Forward fan only; 60% of the higher of AD/AP, dark.
            Assert.Equal(new[] { front, side }, Actor.SimDamageLog.Select(e => e.Victim).ToArray());
            Assert.All(Actor.SimDamageLog, entry => { Assert.Equal(60f, entry.Amount, 3); Assert.Equal(ElementalType.Dark, entry.Element); });
            Assert.Equal(940f, front.currentHealth, 3);
            Assert.Equal(1000f, behind.currentHealth); Assert.Equal(1000f, far.currentHealth);
            // 3. DivineFaith: both struck enemies are tracked, a kill within 6 s grows the stack ...
            Assert.Contains(front, faith.Tracked.Keys); Assert.Contains(side, faith.Tracked.Keys);
            UnityEngine.Time.time = 3f; faith.Dies(front);
            Assert.Equal(1, faith.Stack);
            UnityEngine.Time.time = 7f; faith.Dies(side); // tracked at t=0: outside the 6 s grace
            Assert.Equal(1, faith.Stack);
            // 4. ... and the next strike is amplified by 0.4% per stack, exactly like native memory damage.
            Actor.SimDamageLog.Clear();
            var next = Enemy(0, 2);
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, next, 2);
            host.UpdateIdentityStrikes();
            Assert.Equal(60f * 1.004f, Actor.SimDamageLog.First(e => e.Victim == next).Amount, 3);
            // 5. Control: the mod's former path (hero as the damage Actor) is invisible to the tracker and the amplifier.
            var control = Enemy(0, 2);
            hero.PureDamage(60f, 0f).Dispatch(control);
            Assert.DoesNotContain(control, faith.Tracked.Keys);
            Assert.Same(hero, Actor.SimDamageLog.Last().Dealer);
            Assert.Equal(60f, Actor.SimDamageLog.Last().Amount, 3);
        }

        [Fact]
        public void Wind_scar_strike_needs_a_displacement_and_fires_once_per_displacement_within_the_window()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.WindStrike())), killingFlow: false);
            var target = Enemy(0, 2);
            host.OnIdentityStrikeBasicHit(hero, target, 1); host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog); // no displacement
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 2); host.OnIdentityStrikeBasicHit(hero, target, 3); host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog);
            UnityEngine.Time.time = 10f;
            host.OnIdentityStrikeDisplacement(hero);
            UnityEngine.Time.time = 15f; // 5 s later: past the 4 s window
            host.OnIdentityStrikeBasicHit(hero, target, 4); host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog);
        }

        [Fact]
        public void Higher_of_ad_ap_is_the_basis_and_a_swapped_or_missing_identity_never_fires()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.WindStrike())), killingFlow: false);
            hero.Status.attackDamage = 100; hero.Status.abilityPower = 300;
            var target = Enemy(0, 2);
            host.OnIdentityStrikeDisplacement(hero); host.OnIdentityStrikeBasicHit(hero, target, 1); host.UpdateIdentityStrikes();
            Assert.Equal(180f, Actor.SimDamageLog.Single().Amount, 3);
            // The identity memory is replaced by another one before the next attack: nothing fires and the channel does not keep stale state.
            Actor.SimDamageLog.Clear();
            hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_TheKillingFlow { owner = hero, parentActor = hero };
            host.OnIdentityStrikeDisplacement(hero); host.OnIdentityStrikeBasicHit(hero, target, 2); host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog);
            hero.Skill.Skills.Remove(HeroSkillLocation.Identity);
            host.OnIdentityStrikeBasicHit(hero, target, 3); host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog);
        }

        [Fact]
        public void Killing_flow_strikes_every_basic_hit_with_the_converted_speed_term_and_hits_several_enemies()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.FlowStrike())), killingFlow: true);
            ((St_D_TheKillingFlow)skill).gainedAd = 10; // 20% converted bonus attack speed
            var faith = new DivineFaithSim(skill);
            var a = Enemy(0, 2); var b = Enemy(1, 2); var c = Enemy(-1, 4); var behind = Enemy(0, -2);
            for (long attack = 1; attack <= 3; attack++) { host.OnIdentityStrikeBasicHit(hero, a, attack); host.UpdateIdentityStrikes(); }
            // 25% + 0.2% x 20 = 29% of 100, on three enemies each time, always by the memory.
            Assert.Equal(9, Actor.SimDamageLog.Count);
            Assert.All(Actor.SimDamageLog, entry => { Assert.Same(skill, entry.Dealer); Assert.Equal(29f, entry.Amount, 3); Assert.Null(entry.Element); });
            Assert.Equal(1000f, behind.currentHealth);
            Assert.Contains(c, faith.Tracked.Keys);
        }

        [Fact]
        public void Every_third_basic_hit_counts_activations_once_and_a_build_retransmission_keeps_the_count()
        {
            var build = BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.FlowStrike(everyN: 3)));
            var (host, runtime, hero, skill) = Setup(build, killingFlow: true);
            var target = Enemy(0, 2);
            host.OnIdentityStrikeBasicHit(hero, target, 1); host.OnIdentityStrikeBasicHit(hero, target, 1);
            host.OnIdentityStrikeBasicHit(hero, target, 2);
            host.BindAuthored(runtime, Build.Decode(build.Encode())); // same build again: counters survive
            host.OnIdentityStrikeBasicHit(hero, target, 3);
            host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog);
            host.OnIdentityStrikeBasicHit(hero, target, 4); host.OnIdentityStrikeBasicHit(hero, target, 5); host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog);
            host.OnIdentityStrikeBasicHit(hero, target, 6); host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count);
            // Removing the star removes the channel.
            host.BindAuthored(runtime, new Build());
            host.OnIdentityStrikeBasicHit(hero, target, 7); host.OnIdentityStrikeBasicHit(hero, target, 8); host.OnIdentityStrikeBasicHit(hero, target, 9); host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count);
        }

        // ---- the dash attack bonus portion ----------------------------------------------------------------------------------------------

        private static List<CodeInstruction> Disassemble(MethodInfo method)
        {
            var table = new Dictionary<short, OpCode>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) { var code = (OpCode)field.GetValue(null); table[code.Value] = code; }
            byte[] il = method.GetMethodBody().GetILAsByteArray();
            var result = new List<CodeInstruction>();
            for (int i = 0; i < il.Length;)
            {
                short value = il[i++];
                if (value == 0xFE) value = unchecked((short)(0xFE00 | il[i++]));
                var code = table[value];
                object operand = null; int size;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: size = 4; break;
                }
                if (code.OperandType == OperandType.InlineMethod) operand = method.Module.ResolveMethod(BitConverter.ToInt32(il, i), null, null);
                i += size;
                result.Add(new CodeInstruction(code, operand));
            }
            return result;
        }

        private static readonly MethodInfo Wrapper = typeof(NativeWindScarBonusAsMemory).GetMethod("DispatchBonus", BindingFlags.NonPublic | BindingFlags.Static);

        [Fact]
        public void Dash_bonus_patch_replaces_exactly_the_one_damage_dispatch_of_the_native_on_hit_il()
        {
            var original = typeof(Ai_D_ScarOfTheWind_DashAtk).GetMethod(nameof(Ai_D_ScarOfTheWind_DashAtk.OnHit));
            var code = Disassemble(original);
            var dispatch = typeof(DamageData).GetMethod(nameof(DamageData.Dispatch), new[] { typeof(Entity), typeof(ReactionChain) });
            Assert.Equal(1, code.Count(i => i.Calls(dispatch)));
            var patched = NativeWindScarBonusAsMemory.Transpiler(code).ToList();
            Assert.True(NativeWindScarBonusAsMemory.Bound);
            Assert.Equal(code.Count, patched.Count);
            Assert.Equal(0, patched.Count(i => i.Calls(dispatch)));
            var replaced = patched.Single(i => Equals(i.operand, Wrapper));
            Assert.Equal(OpCodes.Call, replaced.opcode);
            // The wrapper has the exact stack shape of the instance struct call it replaces: DamageData&, Entity, ReactionChain.
            Assert.Equal(new[] { typeof(DamageData).MakeByRefType(), typeof(Entity), typeof(ReactionChain) }, Wrapper.GetParameters().Select(p => p.ParameterType).ToArray());
            // A changed native method (a second damage dispatch) is refused outright.
            var changed = new List<CodeInstruction>(code); changed.AddRange(code.Where(i => i.Calls(dispatch)));
            Assert.Throws<InvalidOperationException>(() => NativeWindScarBonusAsMemory.Transpiler(changed).ToList());
            Assert.False(NativeWindScarBonusAsMemory.Bound);
        }

        private static void DispatchBonus(Ai_D_ScarOfTheWind_DashAtk ai, Entity victim, ref DamageData damage)
        {
            object[] args = { damage, victim, default(ReactionChain) };
            Wrapper.Invoke(null, args);
            damage = (DamageData)args[0];
        }

        [Fact]
        public void Dash_bonus_counts_as_wind_scar_memory_damage_only_when_the_star_is_taken_and_the_identity_is_equipped()
        {
            var on = BuildOf(IdentityStrikeTests.Spec(IdentityStrikeDefinition.DashBonusAsMemory("test.dash")));
            var (host, runtime, hero, skill) = Setup(on, killingFlow: false);
            Assert.True(host.IdentityDashBonusEnabled(hero));
            var faith = new DivineFaithSim(skill);
            var attack = new AttackTrigger { owner = hero, parentActor = hero }; // At_D_ScarOfTheWind_DashAtk's parent is the ENTITY, not the memory
            var ai = new Ai_D_ScarOfTheWind_DashAtk { parentActor = attack, info = new CastInfo(hero) };
            var victim = Enemy(0, 2);
            var scope = new NativeMemoryPayloadScope { Source = ai }; NativeMemoryPayloadScope.Current = scope;
            var damage = ai.Damage(75f).SetElemental(ElementalType.Dark);
            DispatchBonus(ai, victim, ref damage);
            Assert.Same(skill, Actor.SimDamageLog.Single().Dealer);
            Assert.Equal(75f, Actor.SimDamageLog.Single().Amount, 3); // the same single packet, no extra damage
            Assert.Equal(ElementalType.Dark, Actor.SimDamageLog.Single().Element);
            Assert.Contains(victim, faith.Tracked.Keys);
            Assert.Same(ai, scope.Source); // C02's exact scope is restored for the rest of the native OnHit
            // Star not taken: the native dispatch is untouched (attributed to the Ai, which hangs under the hero, not the memory).
            var (offHost, offRuntime, offHero, offSkill) = Setup(new Build(), killingFlow: false);
            var offFaith = new DivineFaithSim(offSkill);
            var offAi = new Ai_D_ScarOfTheWind_DashAtk { parentActor = new AttackTrigger { owner = offHero, parentActor = offHero }, info = new CastInfo(offHero) };
            var offVictim = Enemy(0, 2);
            var offDamage = offAi.Damage(75f).SetElemental(ElementalType.Dark);
            DispatchBonus(offAi, offVictim, ref offDamage);
            Assert.Same(offAi, Actor.SimDamageLog.Last().Dealer);
            Assert.DoesNotContain(offVictim, offFaith.Tracked.Keys);
            // Star taken but another identity equipped: also native.
            var (wrongHost, wrongRuntime, wrongHero, wrongSkill) = Setup(on, killingFlow: true);
            var wrongAi = new Ai_D_ScarOfTheWind_DashAtk { parentActor = new AttackTrigger { owner = wrongHero, parentActor = wrongHero }, info = new CastInfo(wrongHero) };
            var wrongDamage = wrongAi.Damage(75f);
            DispatchBonus(wrongAi, Enemy(0, 2), ref wrongDamage);
            Assert.Same(wrongAi, Actor.SimDamageLog.Last().Dealer);
        }

        // ---- Killing Flow tunings ---------------------------------------------------------------------------------------------------------

        private static void NativeKillingFlow(Hero hero, ref FinalStats data)
        {
            // Se_D_TheKillingFlow.Processor (priority 10), ratio 0.5.
            var flow = (St_D_TheKillingFlow)hero.Skill.GetSkill(HeroSkillLocation.Identity);
            if (data.attackSpeedMultiplier <= 1f) { flow.gainedAd = 0; return; }
            float bonus = (data.attackSpeedMultiplier - 1f) * 100f;
            data.attackSpeedMultiplier = 1f;
            float gained = bonus * 0.5f;
            data.attackDamage += gained;
            flow.gainedAd = (int)Math.Round(gained);
        }

        private static FinalStats Run(Hero hero, float speed, bool nativeFirst)
        {
            var native = new DataProcessor<FinalStats>((ref FinalStats d) => NativeKillingFlow(hero, ref d));
            var processors = hero.Status.finalStatsProcessors;
            if (nativeFirst) processors.Add(native, 10);
            var data = new FinalStats { attackDamage = 100, abilityPower = 50, attackSpeedMultiplier = speed };
            if (!nativeFirst) processors.Add(native, 10); // inserted after the tuning's processors: the priority, not insertion order, decides
            foreach (var processor in processors.InOrder()) processor(ref data);
            processors.Remove(native);
            return data;
        }

        [Fact]
        public void Kept_speed_runs_around_the_native_conversion_by_priority_and_leaves_a_consistent_gained_ad()
        {
            var build = BuildOf(IdentityStrikeTests.Spec(MemoryTuningDefinition.KeepSpeed("test.keep", 4000)));
            var (host, runtime, hero, skill) = Setup(build, killingFlow: true);
            Assert.Equal(2, hero.Status.finalStatsProcessors.Entries.Count);
            Assert.Equal(new[] { 9, 11 }, hero.Status.finalStatsProcessors.Priorities.ToArray());
            foreach (bool first in new[] { true, false })
            {
                var stats = Run(hero, 1.8f, first);
                Assert.Equal(124f, stats.attackDamage, 3); // 100 + 40 converted - 16 kept
                Assert.Equal(1.32f, stats.attackSpeedMultiplier, 3);
                Assert.Equal(24, ((St_D_TheKillingFlow)skill).gainedAd);
            }
            var none = Run(hero, 1f, true); // nothing to convert
            Assert.Equal(100f, none.attackDamage, 3); Assert.Equal(1f, none.attackSpeedMultiplier, 3);
            // Another identity: the processors do nothing.
            hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_ScarOfTheWind { owner = hero, parentActor = hero };
            var wind = new FinalStats { attackDamage = 100, attackSpeedMultiplier = 1.8f };
            foreach (var processor in hero.Status.finalStatsProcessors.InOrder()) processor(ref wind);
            Assert.Equal(100f, wind.attackDamage, 3); Assert.Equal(1.8f, wind.attackSpeedMultiplier, 3);
            // Removing the star removes the processors.
            hero.Skill.Skills[HeroSkillLocation.Identity] = skill;
            host.BindAuthored(runtime, new Build());
            Assert.Empty(hero.Status.finalStatsProcessors.Entries);
        }

        [Fact]
        public void On_hit_heal_scale_multiplies_only_the_on_hit_heal_by_the_lost_speed_multiplier_within_the_cap()
        {
            var build = BuildOf(IdentityStrikeTests.Spec(MemoryTuningDefinition.HealScale("test.heal", 20000)));
            var (host, runtime, hero, skill) = Setup(build, killingFlow: true);
            Run(hero, 1.8f, true); // records the original multiplier at priority 9 (no kept speed: effective 1)
            var field = typeof(HostAuthority).GetField("_onHitHealDepth", BindingFlags.NonPublic | BindingFlags.Instance);
            var processor = hero.dealtHealProcessor.Entries.Single();
            var heal = new HealData { Amount = 10f };
            processor(ref heal, hero, hero);
            Assert.Equal(10f, heal.Amount, 3); // not an on-hit heal
            field.SetValue(host, 1);
            processor(ref heal, hero, hero);
            Assert.Equal(18f, heal.Amount, 3); // x1.8
            Run(hero, 3.0f, true);
            heal = new HealData { Amount = 10f };
            processor(ref heal, hero, hero);
            Assert.Equal(20f, heal.Amount, 3); // capped x2
            hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_ScarOfTheWind { owner = hero, parentActor = hero };
            heal = new HealData { Amount = 10f };
            processor(ref heal, hero, hero);
            Assert.Equal(10f, heal.Amount, 3); // identity not equipped
            host.BindAuthored(runtime, new Build());
            Assert.Empty(hero.dealtHealProcessor.Entries);
        }
    }
}
