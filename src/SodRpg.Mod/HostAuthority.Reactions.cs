using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Stat = SodRpg.Core.Game.Stat;

    internal sealed partial class HostAuthority
    {
        internal struct PendingReaction
        {
            public Entity Victim;
            public Vector3 Center;
            public ElementReactionResult Result;
            public int CinderStacks;
        }

        private int _reactionEffectDepth;
        private readonly List<int> _reactionPrune = new List<int>();

        private void ClearZoneReactions()
        {
            ClearNewPowerZone();
            ClearZoneGimmicksV129();
            ClearSupportPowerStateV129();
            // Captured positions and target IDs belong to this zone's entity lifetimes.
            foreach (var rt in _runtimes.Values)
            {
                rt.PendingReactions.Clear();
                rt.ReactionVictims.Clear();
                rt.Reactions.Clear();
            }
            _reactionPrune.Clear();
        }

        private void QueueElementReactions(HeroRuntime rt, Entity victim)
        {
            var status = victim.Status;
            // The snapshot is read-only; native Cold/Light/Dark limits and identity resources remain untouched.
            var result = rt.Reactions.Apply(rt.Powers.Build, victim.GetInstanceID(),
                new ElementSnapshot(status.fireStack, status.hasCold, status.lightStack, status.darkStack),
                Time.time, rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower, rt.Hero.maxHealth,
                (float)WaypointTotalsForHero(rt.Hero).ReactionMultiplier, _gimmickDamageDepth != 0 || _reactionEffectDepth != 0);
            if (result.Steam || result.ExposePercent > 0 || result.CinderStacks > 0 || result.Shield > 0)
                rt.ReactionVictims[victim.GetInstanceID()] = victim;
            if (result.Steam || result.Shield > 0)
                rt.PendingReactions.Add(new PendingReaction { Victim = victim, Center = victim.position, Result = result });
        }

        private void OnReactionDeath(Entity victim)
        {
            if (victim == null) return;
            int id = victim.GetInstanceID();
            foreach (var rt in _runtimes.Values)
            {
                // A marked enemy can be finished by a teammate. Isolated reaction/gimmick kills cannot chain.
                int stacks = rt.Reactions.OnDeath(id, _gimmickDamageDepth != 0 || _reactionEffectDepth != 0);
                rt.ReactionVictims.Remove(id);
                if (stacks > 0 && Alive(rt.Hero))
                    rt.PendingReactions.Add(new PendingReaction { Victim = victim, Center = victim.position, CinderStacks = stacks });
            }
        }

        private void UpdateReactions(HeroRuntime rt)
        {
            _reactionPrune.Clear();
            foreach (var pair in rt.ReactionVictims)
                if (pair.Value == null || !pair.Value.isActive) _reactionPrune.Add(pair.Key);
            foreach (int id in _reactionPrune)
            {
                rt.ReactionVictims.Remove(id);
                rt.Reactions.OnDeath(id, isolatedEffect: true);
            }
            var pending = rt.PendingReactions;
            if (!Alive(rt.Hero)) { pending.Clear(); return; }
            // Dispatch on the host update, after the native hit/kill attribution has completed.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var effect = pending[i];
                pending.RemoveAt(i);
                _reactionEffectDepth++;
                try { ApplyReaction(rt, effect); }
                catch (Exception ex) { Log.Error("Host: element reaction " + ex); }
                finally { _reactionEffectDepth--; }
            }
        }

        private void ApplyReaction(HeroRuntime rt, PendingReaction pending)
        {
            var hero = rt.Hero;
            var result = pending.Result;
            if (result.Shield > 0f)
            {
                // Keep native support notifications off the hero's parent chain, as in v1.28 gimmicks.
                var support = _am != null ? _am.serverActor : null;
                var granted = support != null ? support.GiveShield(hero, SupportStats.AmplifyShield(result.Shield, rt.Powers.Build.Get(Stat.ShieldPower)),
                    ElementReactionRuntime.ShieldDuration) : null;
                if (granted != null) CreditShieldGranted(rt, hero, granted.shield != null ? granted.shield.amount : 0f);
            }
            if (!result.Steam && pending.CinderStacks <= 0) return;
            float radius = result.Steam ? ElementReactionRuntime.SteamRadius : ElementReactionRuntime.CinderRadius;
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, pending.Center, radius, EnemyFilter, hero);
            try
            {
                foreach (var enemy in found)
                {
                    if (enemy == null || !enemy.isActive || enemy.currentHealth <= 0f) continue;
                    if (pending.CinderStacks > 0)
                    {
                        if (enemy != pending.Victim) hero.ApplyElemental(ElementalType.Fire, enemy, pending.CinderStacks);
                        continue;
                    }
                    // The native named container refreshes this slow instead of stacking another copy.
                    hero.CreateBasicEffect(enemy, new SlowEffect { strength = ElementReactionRuntime.SteamSlowPercent },
                        ElementReactionRuntime.SteamSlowDuration, "dreamforge.reaction.steam", DuplicateEffectBehavior.UsePrevious);
                    if (result.SteamDamage <= 0f) continue;
                    EnterGenerated(hero);
                    try
                    {
                        var damage = hero.Status.abilityPower > hero.Status.attackDamage
                            ? hero.MagicDamage(result.SteamDamage, 0f) : hero.PhysicalDamage(result.SteamDamage, 0f);
                        // Reuse the final received-damage patch: remove later native elements and attack procs too.
                        damage.SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(enemy);
                    }
                    finally { ExitGenerated(hero); }
                }
            }
            finally { handle.Return(); }
        }
    }
}
