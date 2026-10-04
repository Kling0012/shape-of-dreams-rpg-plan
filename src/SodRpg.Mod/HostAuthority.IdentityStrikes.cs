using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // ---------------------------------------------------------------------------------------------------------------------
    // IdentityStrike: damage that the BASE GAME attributes to the equipped identity memory.
    //
    // Why attribution works (decompiled Dew.Core):
    //  * Actor.DealDamage(damage, target) runs ProcessDealtDamage on the dealing Actor and every ancestor (dealtDamageProcessor) and then
    //    InvokeOnDealDamage, which raises ActorEvent_OnDealDamage on the dealing Actor and every ancestor up the parentActor chain.
    //  * Gem_L_DivineFaith.OnEquipSkill(skill) registers skill.TrackKills(6 s) -> KillTracker, which subscribes to
    //    skill.ActorEvent_OnDealDamage, and adds Amplify to skill.dealtDamageProcessor. Both only fire when the damage Actor is that SkillTrigger
    //    or one of its descendants.
    //  * hero.PureDamage(..) makes the HERO the Actor, so neither the tracker nor Amplify ever sees it. The strike therefore builds its
    //    DamageData from the identity SkillTrigger itself (skill.PhysicalDamage / skill.MagicDamage) and Dispatches it.
    //  * The native dash attack is different: At_D_ScarOfTheWind_DashAtk is installed by Se_..._NextAtkDash through AttackOverrideEffect, and
    //    EntityStatus sets its parentActor to the ENTITY (the hero), not to St_D_ScarOfTheWind. Its Ai_D_ScarOfTheWind_DashAtk.OnHit bonus
    //    (Damage(bonusDamage)...Dispatch) is thus attributed to the basic attack. NativeWindScarBonusAsMemory re-targets only that one call.
    // ---------------------------------------------------------------------------------------------------------------------
    internal sealed partial class HostAuthority
    {
        private sealed class IdentityStrikeChannel
        {
            internal string Key;
            internal IdentityStrikeDefinition Definition;
            internal IdentityStrikeState State;
            internal SkillTrigger BoundSkill;
        }

        private readonly Dictionary<Hero, List<IdentityStrikeChannel>> _identityStrikes = new Dictionary<Hero, List<IdentityStrikeChannel>>();
        private readonly HashSet<Hero> _identityDashBonus = new HashSet<Hero>();
        private int _identityStrikeDepth;
        private sealed class PendingIdentityStrike
        {
            internal Hero Hero; internal IdentityStrikeChannel Channel; internal SkillTrigger Skill; internal Entity Victim; internal Vector3 Origin, Direction;
        }
        // The strike is deferred to the host tick: the basic attack's own DealDamage is still running (it kills the target after its events),
        // so the strike must not damage or kill from inside that call.
        private readonly List<PendingIdentityStrike> _pendingIdentityStrikes = new List<PendingIdentityStrike>();
        private readonly List<Entity> _identityStrikeTargets = new List<Entity>();

        /// <summary>Install the build's identity strike channels. A channel that did not change keeps its counters (a Build retransmission must not reset EveryN).</summary>
        private void ConfigureIdentityStrikes(Hero hero, List<KeyValuePair<string, IdentityStrikeDefinition>> channels)
        {
            _identityDashBonus.Remove(hero);
            _identityStrikes.TryGetValue(hero, out var previous);
            var next = new List<IdentityStrikeChannel>();
            foreach (var pair in channels)
            {
                if (!pair.Value.DealsDamage) { _identityDashBonus.Add(hero); continue; }
                IdentityStrikeChannel kept = null;
                if (previous != null) foreach (var old in previous) if (old.Key == pair.Key) kept = old;
                next.Add(kept ?? new IdentityStrikeChannel { Key = pair.Key, Definition = pair.Value, State = new IdentityStrikeState(pair.Value) });
            }
            if (next.Count == 0) _identityStrikes.Remove(hero); else _identityStrikes[hero] = next;
        }

        internal void ForgetIdentityStrikes(Hero hero)
        {
            _identityStrikes.Remove(hero);
            _identityDashBonus.Remove(hero);
            _pendingIdentityStrikes.RemoveAll(p => p.Hero == hero);
        }

        private void ResetIdentityStrikes(Hero hero = null)
        {
            _pendingIdentityStrikes.RemoveAll(p => hero == null || p.Hero == hero);
            foreach (var pair in _identityStrikes)
                if (hero == null || pair.Key == hero) foreach (var channel in pair.Value) { channel.State.Reset(); channel.BoundSkill = null; }
        }

        /// <summary>The equipped identity memory, only when it really is the channel's identity and owned by the hero.</summary>
        private SkillTrigger EquippedIdentity(Hero hero, string identity)
        {
            var skill = hero.Skill.GetSkill(HeroSkillLocation.Identity);
            return skill != null && skill.owner == hero && skill.GetType().Name == identity ? skill : null;
        }

        /// <summary>A channel follows one identity instance; only a real swap to another instance drops its counters (the first binding keeps an earlier arming).</summary>
        private static void BindStrikeSkill(IdentityStrikeChannel channel, SkillTrigger skill)
        {
            if (channel.BoundSkill == skill) return;
            if (channel.BoundSkill != null) channel.State.Reset();
            channel.BoundSkill = skill;
        }

        /// <summary>Own self displacement (dash / teleport): arms Wind Scar style channels.</summary>
        internal void OnIdentityStrikeDisplacement(Hero hero)
        {
            if (!NetworkServer.active || hero == null || !_identityStrikes.TryGetValue(hero, out var channels)) return;
            foreach (var channel in channels)
            {
                var skill = EquippedIdentity(hero, channel.Definition.Identity);
                if (skill == null) { channel.State.Reset(); channel.BoundSkill = null; continue; }
                BindStrikeSkill(channel, skill);
                channel.State.OnDisplacement(Time.time);
            }
        }

        /// <summary>First hit of one own main basic attack activation. Strike damage itself never re-enters (depth guard and not a main packet).</summary>
        internal void OnIdentityStrikeBasicHit(Hero hero, Entity victim, long activationId)
        {
            if (!NetworkServer.active || _identityStrikeDepth != 0 || victim == null || !Alive(hero)
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !_identityStrikes.TryGetValue(hero, out var channels)) return;
            foreach (var channel in channels)
            {
                var skill = EquippedIdentity(hero, channel.Definition.Identity);
                if (skill == null) { channel.State.Reset(); channel.BoundSkill = null; continue; }
                BindStrikeSkill(channel, skill);
                if (!channel.State.OnBasicHit(activationId, Time.time)) continue;
                var direction = victim.agentPosition - hero.agentPosition;
                _pendingIdentityStrikes.Add(new PendingIdentityStrike { Hero = hero, Channel = channel, Skill = skill, Victim = victim,
                    Origin = hero.agentPosition, Direction = direction });
            }
        }

        /// <summary>Run the queued strikes (host tick). A strike whose hero died, whose identity changed or whose channel was replaced is dropped.</summary>
        internal void UpdateIdentityStrikes()
        {
            if (_pendingIdentityStrikes.Count == 0) return;
            var batch = _pendingIdentityStrikes.ToArray();
            _pendingIdentityStrikes.Clear();
            foreach (var pending in batch)
            {
                try
                {
                    var hero = pending.Hero;
                    if (!Alive(hero) || !_identityStrikes.TryGetValue(hero, out var channels) || !channels.Contains(pending.Channel)
                        || EquippedIdentity(hero, pending.Channel.Definition.Identity) != pending.Skill) continue;
                    FireIdentityStrike(hero, pending.Channel.Definition, pending.Skill, pending.Victim, pending.Origin, pending.Direction);
                }
                catch (Exception ex) { Log.Error("Host: identity strike " + pending.Channel.Definition.ChannelId + ": " + ex); }
            }
        }

        private void FireIdentityStrike(Hero hero, IdentityStrikeDefinition def, SkillTrigger skill, Entity victim, Vector3 origin, Vector3 toVictim)
        {
            float attack = hero.Status.attackDamage, ability = hero.Status.abilityPower;
            float speed = 0f;
            if (def.BonusSpeedUnitsPerPercent > 0 && skill is St_D_TheKillingFlow flow)
                speed = IdentityStrikeDefinition.BonusSpeedPercentFromGainedAd(flow.gainedAd);
            float amount = def.Damage(attack, ability, speed);
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            bool magic = def.IsMagic(attack, ability);
            if (toVictim.x * toVictim.x + toVictim.z * toVictim.z < 1e-6f) toVictim = new Vector3(0f, 0f, 1f);
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, origin, def.RangeMetres + 1.5f, EnemyFilter, hero);
            _identityStrikeTargets.Clear();
            try
            {
                // The struck enemy always receives the strike; the rest are the enemies inside the forward line / fan, nearest limit first by the physics order.
                if (victim != null && victim.isActive && victim.currentHealth > 0f) _identityStrikeTargets.Add(victim);
                foreach (var enemy in found)
                {
                    if (_identityStrikeTargets.Count >= def.MaxTargets) break;
                    if (enemy == victim || enemy == null || !enemy.isActive) continue;
                    var p = enemy.agentPosition;
                    if (def.Contains(origin.x, origin.z, toVictim.x, toVictim.z, p.x, p.z, 0.75f)) _identityStrikeTargets.Add(enemy);
                }
                _identityStrikeDepth++;
                try
                {
                    foreach (var enemy in _identityStrikeTargets)
                    {
                        // The identity SkillTrigger is the damage Actor: this is what the base game attributes to the memory.
                        var damage = magic ? skill.MagicDamage(amount, 0f) : skill.PhysicalDamage(amount, 0f);
                        switch (def.Element)
                        {
                            case IdentityStrikeElement.Fire: damage = damage.SetElemental(ElementalType.Fire); break;
                            case IdentityStrikeElement.Cold: damage = damage.SetElemental(ElementalType.Cold); break;
                            case IdentityStrikeElement.Light: damage = damage.SetElemental(ElementalType.Light); break;
                            case IdentityStrikeElement.Dark: damage = damage.SetElemental(ElementalType.Dark); break;
                        }
                        damage.Dispatch(enemy);
                    }
                }
                finally { _identityStrikeDepth--; }
            }
            finally { _identityStrikeTargets.Clear(); handle.Return(); }
        }

        internal bool IdentityDashBonusEnabled(Hero hero) => hero != null && _identityDashBonus.Contains(hero);

        /// <summary>
        /// The native dash attack's bonus packet (75% dark): make the identity memory the damage Actor. Returns false (caller dispatches natively) when
        /// the star is off or the wiring is not exactly the verified one.
        /// </summary>
        internal bool TryDispatchDashBonusAsMemory(ref DamageData damage, Entity victim, ReactionChain chain)
        {
            var ai = damage.actor as Ai_D_ScarOfTheWind_DashAtk;
            var hero = ai?.info.caster as Hero;
            if (ai == null || hero == null || !Alive(hero) || !IdentityDashBonusEnabled(hero)) return false;
            var skill = EquippedIdentity(hero, IdentityStrikeDefinition.WindScar);
            if (skill == null) return false;
            // C02's exact native scope names the Ai as its packet source; the memory takes over as the source for this one dispatch.
            var scope = NativeMemoryPayloadScope.Current;
            Actor restored = null;
            if (scope != null && scope.Source == ai) { restored = scope.Source; scope.Source = skill; }
            try
            {
                damage = damage.SetActor(skill);
                damage.Dispatch(victim, chain);
            }
            finally { if (restored != null) scope.Source = restored; }
            return true;
        }
    }

    // Only the one DamageData.Dispatch in Ai_D_ScarOfTheWind_DashAtk.OnHit is replaced (the HealData Dispatch is another type). The wrapper keeps the
    // exact native dispatch whenever the identity-strike option is not active, so an unmodified build behaves natively.
    [HarmonyPatch(typeof(Ai_D_ScarOfTheWind_DashAtk), "OnHit")]
    internal static class NativeWindScarBonusAsMemory
    {
        private static readonly MethodInfo Dispatch = AccessTools.Method(typeof(DamageData), nameof(DamageData.Dispatch));
        private static readonly MethodInfo Wrapper = AccessTools.Method(typeof(NativeWindScarBonusAsMemory), nameof(DispatchBonus));
        internal static bool Bound { get; private set; }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            Bound = false;
            int index = -1, count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(Dispatch)) { index = i; count++; }
            if (count != 1) throw new InvalidOperationException("Native dash attack bonus callsite changed; the Wind Scar memory attribution was not applied.");
            code[index].opcode = OpCodes.Call;
            code[index].operand = Wrapper;
            Bound = true;
            return code;
        }

        // Instance struct call consumes DamageData&, Entity, ReactionChain; the static wrapper has exactly that signature.
        private static void DispatchBonus(ref DamageData damage, Entity victim, ReactionChain chain)
        {
            var host = NetworkServer.active ? HostAuthority.NativeInstance : null;
            if (host != null && host.TryDispatchDashBonusAsMemory(ref damage, victim, chain)) return;
            damage.Dispatch(victim, chain);
        }
    }
}
