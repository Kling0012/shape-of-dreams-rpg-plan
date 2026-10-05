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

        [Fact]
        public void Displacement_critical_strike_needs_a_critical_first_hit_and_refunds_the_dodge_memory()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.CritWindStrike())), killingFlow: false);
            var movement = new SkillTrigger { owner = hero };
            movement.currentConfigUnscaledCooldownTime = 4f; // 4 s of the 10 s maximum remaining
            hero.Skill.Skills[HeroSkillLocation.Movement] = movement;
            var target = Enemy(0, 2);

            host.OnIdentityStrikeBasicHit(hero, target, 1, critical: true); host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog); // nothing is primed without a displacement
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 2, critical: false); host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog); // a noncritical first hit consumes the readiness ...
            host.OnIdentityStrikeBasicHit(hero, target, 3, critical: true); host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog); // ... and the consumed preparation never fires
            Assert.Equal(4f, movement.currentConfigUnscaledCooldownTime);

            host.OnIdentityStrikeDisplacement(hero);
            UnityEngine.Time.time = 1f;
            host.OnIdentityStrikeBasicHit(hero, target, 4, critical: true);
            Assert.Empty(Actor.SimDamageLog); // deferred to the host tick
            host.UpdateIdentityStrikes();
            var strike = Assert.Single(Actor.SimDamageLog);
            Assert.Same(skill, strike.Dealer);
            Assert.Equal(120f, strike.Amount, 3); // 120% of the higher of AD/AP, dark
            Assert.Equal(ElementalType.Dark, strike.Element);
            Assert.Equal(2.6f, movement.currentConfigUnscaledCooldownTime, 3); // 35% of the remaining 4 s comes off

            host.OnIdentityStrikeDisplacement(hero); // armed until t = 4
            UnityEngine.Time.time = 5f;
            host.OnIdentityStrikeBasicHit(hero, target, 5, critical: true); host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog); // the 3 s window has closed
            Assert.Equal(2.6f, movement.currentConfigUnscaledCooldownTime, 3); // no strike, no refund

            UnityEngine.Time.time = 10f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 6, critical: true); host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count);
            Assert.Equal(1.69f, movement.currentConfigUnscaledCooldownTime, 3);
            UnityEngine.Time.time = 10.5f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 7, critical: true); host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count); // at most one strike per second
            // a second qualifying hit before the host tick waits instead of queueing twice
            UnityEngine.Time.time = 11.5f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 8, critical: true);
            UnityEngine.Time.time = 13f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 9, critical: true);
            host.UpdateIdentityStrikes(); host.UpdateIdentityStrikes();
            Assert.Equal(3, Actor.SimDamageLog.Count); // one strike ran; the overlapping trigger was dropped
            Assert.All(Actor.SimDamageLog, entry => { Assert.Same(skill, entry.Dealer); Assert.Equal(120f, entry.Amount, 3); Assert.Same(target, entry.Victim); });
        }

        [Fact]
        public void Consecutive_critical_strike_needs_three_crits_on_one_enemy_and_resets_on_any_break()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.CritFlowStrike())), killingFlow: true);
            var movement = new SkillTrigger { owner = hero };
            hero.Skill.Skills[HeroSkillLocation.Movement] = movement;
            var a = Enemy(0, 2); var b = Enemy(5, 0); // b lies outside the 2 m line towards a
            int activation = 0;
            void Hit(Entity victim, float now, bool critical, long lifetime)
            {
                UnityEngine.Time.time = now;
                host.OnIdentityStrikeBasicHit(hero, victim, ++activation, critical, lifetime);
            }

            Hit(a, 0f, false, 11); Hit(a, 0.2f, true, 11); Hit(a, 0.4f, true, 11); Hit(a, 0.6f, false, 11); // a noncritical hit resets
            Hit(a, 0.8f, true, 11); Hit(a, 1f, true, 11);
            host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog); // two crits since the reset: not three yet
            Hit(a, 1.2f, true, 11); // the third consecutive crit on the same enemy
            Assert.Empty(Actor.SimDamageLog); // deferred to the host tick
            host.UpdateIdentityStrikes();
            var strike = Assert.Single(Actor.SimDamageLog);
            Assert.Same(skill, strike.Dealer); Assert.Same(a, strike.Victim);
            Assert.Equal(180f, strike.Amount, 3); // 180% of the higher of AD/AP, dark
            Assert.Equal(ElementalType.Dark, strike.Element);
            Assert.Equal(10f, movement.currentConfigUnscaledCooldownTime); // only the displacement mode refunds the dodge

            Hit(a, 2f, true, 11); Hit(a, 2.2f, true, 11); Hit(b, 2.4f, true, 22); // a different enemy resets
            Hit(b, 7f, true, 22); Hit(b, 7.2f, true, 22); // 7 s after the last crit on b: the 4 s window has closed
            host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog); // neither break ever completes a third crit
            Hit(b, 7.4f, true, 22); // three fresh crits on b inside the window
            host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count);
            Hit(b, 8f, true, 22); Hit(b, 8.2f, true, 22); Hit(b, 8.3f, true, 22); // the third lands inside the 1 s interval
            host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count); // consumed without a strike
            Hit(b, 9f, true, 22); Hit(b, 9.2f, true, 22); Hit(b, 9.4f, true, 22);
            host.UpdateIdentityStrikes();
            Assert.Equal(3, Actor.SimDamageLog.Count);
            Assert.All(Actor.SimDamageLog, entry => { Assert.Same(skill, entry.Dealer); Assert.Equal(180f, entry.Amount, 3); Assert.Equal(ElementalType.Dark, entry.Element); });
        }

        [Fact]
        public void Critical_strikes_reach_at_most_six_enemies_in_their_shape()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.CritWindStrike())), killingFlow: false);
            var target = Enemy(0, 2);
            for (int i = 0; i < 7; i++) Enemy(-1.5f + 0.5f * i, 3); // seven more inside the 120 degree fan
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 1, critical: true);
            host.UpdateIdentityStrikes();
            Assert.Equal(6, Actor.SimDamageLog.Count); // the victim plus five: at most six enemies total
            Assert.All(Actor.SimDamageLog, entry => { Assert.Same(skill, entry.Dealer); Assert.Equal(120f, entry.Amount, 3); Assert.Equal(ElementalType.Dark, entry.Element); });

            var (flowHost, flowRuntime, flowHero, flowSkill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.CritFlowStrike())), killingFlow: true);
            var flowTarget = Enemy(0, 2);
            for (int i = 0; i < 7; i++) Enemy(0, 2.5f + 0.4f * i); // seven more along the 6 m line
            for (int activation = 1; activation <= 3; activation++) flowHost.OnIdentityStrikeBasicHit(flowHero, flowTarget, activation, critical: true, victimLifetime: 77);
            flowHost.UpdateIdentityStrikes();
            Assert.Equal(6, Actor.SimDamageLog.Count); // Setup cleared the log: six along the line
            Assert.All(Actor.SimDamageLog, entry => { Assert.Same(flowSkill, entry.Dealer); Assert.Equal(180f, entry.Amount, 3); Assert.Equal(ElementalType.Dark, entry.Element); });
        }

        [Fact]
        public void A_critical_strike_never_rechains_mechanisms_or_itself()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.CritWindStrike())), killingFlow: false);
            var depth = typeof(HostAuthority).GetField("_gimmickDamageDepth", BindingFlags.NonPublic | BindingFlags.Instance);
            var target = Enemy(0, 2);
            var outside = Enemy(5, 0); // outside the fan: only a nested trigger could ever reach it
            int observed = 0, depthDuringStrike = 0;
            hero.ActorEvent_OnDealDamage += info =>
            {
                observed++;
                depthDuringStrike = (int)depth.GetValue(host);
                host.OnIdentityStrikeDisplacement(hero); // primes a fresh preparation ...
                host.OnIdentityStrikeBasicHit(hero, outside, 999, critical: true, victimLifetime: 55); // ... whose hit must be ignored inside the strike
            };
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 1, critical: true);
            host.UpdateIdentityStrikes();
            Assert.True(observed > 0); // the hook really saw the strike's own damage packets
            Assert.Equal(1, depthDuringStrike); // the strike runs as generated origin: no authored mechanism chains from it
            Assert.Single(Actor.SimDamageLog);
            Assert.Same(target, Actor.SimDamageLog.Single().Victim);
            Assert.Equal(1000f, outside.currentHealth); // the nested trigger was dropped, not queued for later
            host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog);

            // the same guard holds while any other mechanism's damage is running
            host.SetDepths(1, 0, 0);
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 2, critical: true);
            host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog); // generated (gimmick) damage never triggers a strike
            host.SetDepths(0, 0, 0);
            UnityEngine.Time.time = 5f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 3, critical: true);
            host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count); // back outside generated damage the channel works again
            Assert.Equal(1000f, outside.currentHealth);
        }

        [Fact]
        public void A_failing_strike_channel_is_disabled_with_one_warning_and_the_mod_keeps_running()
        {
            Log.Warnings.Clear();
            var (host, runtime, hero, wind) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.CritWindStrike())), killingFlow: false);
            var flowHero = new Hero();
            var flow = new St_D_TheKillingFlow { owner = flowHero, parentActor = flowHero };
            flowHero.Skill.Skills[HeroSkillLocation.Identity] = flow;
            flowHero.owner = new DewPlayer { hero = flowHero, isHumanPlayer = true };
            var flowBuild = BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.FlowStrike("t.flow.other")));
            host.BindAuthored(new HostAuthority.HeroRuntime { Hero = flowHero, HeroKey = "Hero_Husk", Powers = new PowerRuntime(flowBuild, 0) }, flowBuild);
            var target = Enemy(0, 2); var flowTarget = Enemy(0, 3);

            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 1, critical: true); // the critical channel queues its strike ...
            host.OnIdentityStrikeBasicHit(flowHero, flowTarget, 1); // ... and the flow channel queues its own
            hero.Status = null; // the strike's own dispatch now fails
            host.UpdateIdentityStrikes();
            Assert.Equal(2, Actor.SimDamageLog.Count); // the failing channel did not stop the mod's other channel
            Assert.All(Actor.SimDamageLog, entry => Assert.Same(flow, entry.Dealer));
            Assert.Single(Log.Warnings);
            Assert.Contains("t.windcrit", Log.Warnings.Single());
            Assert.Contains("disabled", Log.Warnings.Single());

            host.OnIdentityStrikeDisplacement(hero); // the disabled channel stays silent
            host.OnIdentityStrikeBasicHit(hero, target, 2, critical: true);
            host.OnIdentityStrikeBasicHit(flowHero, flowTarget, 2);
            host.UpdateIdentityStrikes();
            Assert.Equal(4, Actor.SimDamageLog.Count); // the flow channel keeps firing
            Assert.Single(Log.Warnings); // one warning, never repeated
            Assert.All(Actor.SimDamageLog, entry => Assert.Same(flow, entry.Dealer));
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

        /// <summary>
        /// Mirrors the production host damage processor (HostAuthority.cs rt.DamageDealt): the generated-damage gate returns before
        /// the memory correction — the exact block the critical channels cannot reach (issue #136). Records the generated depth each
        /// packet saw, so a closed gate (1) is distinguishable from the normal route (0).
        /// </summary>
        private static void WireHostMemoryCorrection(HostAuthority host, Hero hero, HostAuthority.HeroRuntime runtime, List<int> depthSeen)
        {
            hero.SimDealtDamage.Add((ref DamageData d, Actor a, Entity t) =>
            {
                depthSeen.Add(host.GeneratedDamageDepth);
                if (host.GeneratedDamageDepth != 0 || d.IsAmountModifiedBy(typeof(GimmickRuntime))) return;
                float amp = host.MemoryDamagePercent(runtime, (d.actor ?? a)?.FindFirstOfType<SkillTrigger>()?.GetType().Name);
                if (amp > 0f) d.ApplyAmplification(amp / 100f);
            });
        }

        private static LinkDef MemoryDamageLink(string memory, decimal percent) =>
            new LinkDef { Kind = LinkKind.MemoryDamage, Requires = new[] { memory }, Value = percent };

        [Fact]
        public void Displacement_critical_strike_carries_the_identity_memory_damage_correction_exactly_once()
        {
            // Both Wind Scar channels on one hero: the non-critical echo (60%, open gate) and the critical strike (120%, generated gate).
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.WindStrike()),
                IdentityStrikeTests.Spec(IdentityStrikeTests.CritWindStrike())), killingFlow: false);
            var faith = new DivineFaithSim(skill);
            var depths = new List<int>();
            WireHostMemoryCorrection(host, hero, runtime, depths);
            var target = Enemy(0, 2);

            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 1, critical: true);
            host.UpdateIdentityStrikes();
            Assert.Equal(60f, Actor.SimDamageLog[0].Amount, 3); // no correction acquired yet: the plain echo ...
            Assert.Equal(120f, Actor.SimDamageLog[1].Amount, 3); // ... and the plain critical strike
            Assert.Equal(new[] { 0, 1 }, depths.Take(2).ToArray()); // open gate for the echo, closed gate for the critical strike

            runtime.SatisfiedLinks.Add(MemoryDamageLink(skill.GetType().Name, 40m)); // 記憶の冴え +40% for Wind Scar only
            UnityEngine.Time.time = 2f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 2, critical: true);
            host.UpdateIdentityStrikes();
            Assert.Equal(60f * 1.4f, Actor.SimDamageLog[2].Amount, 3); // the echo keeps its route through the processor ...
            Assert.Equal(120f * 1.4f, Actor.SimDamageLog[3].Amount, 3); // ... and the critical strike carries it exactly once — never 1.4 x 1.4
            Assert.Equal(new[] { 0, 1 }, depths.Skip(2).Take(2).ToArray()); // the gate split did not change
            Assert.Contains(target, faith.Tracked.Keys); // the native tracker still sees the amplified critical strike

            runtime.SatisfiedLinks.Clear();
            runtime.SatisfiedLinks.Add(MemoryDamageLink("St_Q_Fleche", 40m)); // a different memory's correction never rides
            UnityEngine.Time.time = 4f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 3, critical: true);
            host.UpdateIdentityStrikes();
            Assert.Equal(60f, Actor.SimDamageLog[4].Amount, 3);
            Assert.Equal(120f, Actor.SimDamageLog[5].Amount, 3);

            runtime.SatisfiedLinks.Clear();
            runtime.SatisfiedLinks.Add(MemoryDamageLink(skill.GetType().Name, 40m));
            UnityEngine.Time.time = 6f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 4, critical: false); // a noncritical hit: only the echo channel fires
            host.UpdateIdentityStrikes();
            Assert.Equal(7, depths.Count); // one gate visit per packet (2+2+2+1): nothing chained from the strikes
            Assert.Equal(60f * 1.4f, Actor.SimDamageLog[6].Amount, 3); // the normal route through the processor is unchanged
            Assert.Equal(0, depths[6]);

            // Divine Faith's native amplification still composes on top of the memory correction (0.4% per stack).
            faith.Stack = 1;
            UnityEngine.Time.time = 8f;
            host.OnIdentityStrikeDisplacement(hero);
            host.OnIdentityStrikeBasicHit(hero, target, 5, critical: true);
            host.UpdateIdentityStrikes();
            Assert.Equal(60f * 1.4f * 1.004f, Actor.SimDamageLog[7].Amount, 3);
            Assert.Equal(120f * 1.4f * 1.004f, Actor.SimDamageLog[8].Amount, 3);
            UnityEngine.Time.time = 11f; faith.Dies(target);
            Assert.Equal(2, faith.Stack); // the kill within 6 s of the tracked hit still grows the stack
        }

        [Fact]
        public void Consecutive_critical_strike_carries_the_identity_memory_damage_correction_exactly_once()
        {
            var (host, runtime, hero, skill) = Setup(BuildOf(IdentityStrikeTests.Spec(IdentityStrikeTests.CritFlowStrike())), killingFlow: true);
            var depths = new List<int>();
            WireHostMemoryCorrection(host, hero, runtime, depths);
            var a = Enemy(0, 2);
            runtime.SatisfiedLinks.Add(MemoryDamageLink(skill.GetType().Name, 40m)); // 記憶の冴え +40% for the Killing Flow

            for (long activation = 1; activation <= 2; activation++) host.OnIdentityStrikeBasicHit(hero, a, activation, critical: true, victimLifetime: 11);
            host.UpdateIdentityStrikes();
            Assert.Empty(Actor.SimDamageLog); // two crits: not the third yet
            host.OnIdentityStrikeBasicHit(hero, a, 3, critical: true, victimLifetime: 11);
            host.UpdateIdentityStrikes();
            var strike = Assert.Single(Actor.SimDamageLog);
            Assert.Equal(180f * 1.4f, strike.Amount, 3); // once, never 1.4 x 1.4
            Assert.Equal(1, depths.Single()); // the whole strike ran behind the closed generated gate
            host.UpdateIdentityStrikes();
            Assert.Single(Actor.SimDamageLog); // nothing chains from the corrected strike
        }
    }
}
