using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private bool _summonOwnershipFailed;

        private readonly struct SummonGuardTarget
        {
            internal readonly Hero Hero;
            internal readonly Entity Enemy;
            internal readonly float Until;
            internal SummonGuardTarget(Hero hero, Entity enemy, float until)
            { Hero = hero; Enemy = enemy; Until = until; }
        }

        private readonly Dictionary<Summon, SummonGuardTarget> _summonGuardTargets =
            new Dictionary<Summon, SummonGuardTarget>();
        private readonly List<Summon> _summonGuardScratch = new List<Summon>();

        partial void RecordSummonGuardTarget(Summon summon, Entity enemy, float seconds)
        {
            if (_summonOwnershipFailed || !OwnershipRunning())
            { _summonGuardTargets.Remove(summon); return; }
            // Value records are refreshed by the existing scan, including unchanged targets.
            _summonGuardTargets[summon] = new SummonGuardTarget(summon.hero, enemy, Time.time + seconds);
        }

        private void ReleaseSummonGuardTarget(Entity entity)
        {
            if (entity is Summon summon) _summonGuardTargets.Remove(summon);
            _summonGuardScratch.Clear();
            foreach (var entry in _summonGuardTargets)
                if (entry.Value.Enemy == entity || entry.Value.Hero == entity || Time.time >= entry.Value.Until)
                    _summonGuardScratch.Add(entry.Key);
            foreach (var expired in _summonGuardScratch) _summonGuardTargets.Remove(expired);
            _summonGuardScratch.Clear();
        }

        internal void SelectSummonOwnershipAggro(EntityAI ai, ref Entity target)
        {
            if (_summonOwnershipFailed) return;
            try
            {
                if (!OwnershipRunning() || !(ai?.entity is Summon summon) || summon.hero == null
                    || summon.hero.owner == null || !summon.hero.owner.isHumanPlayer) return;
                if (_summonGuardTargets.TryGetValue(summon, out var guard))
                {
                    if (Time.time < guard.Until && guard.Hero == summon.hero && summon.isActive
                        && summon.currentHealth > 0f && SummonCandidate(ai, guard.Enemy))
                    { target = guard.Enemy; return; }
                    _summonGuardTargets.Remove(summon);
                }
                var owner = summon.hero.owner;
                // Keep native retaliation against an enemy currently attacking this summon or its master.
                if (SummonRetaliation(summon, target)) return;
                var current = ai.context.targetEnemy;
                if (SummonRetaliation(summon, current) && SummonCandidate(ai, current))
                { target = current; return; }
                Entity best = SummonCandidate(ai, target) ? target : null;
                int rank = best != null ? SummonOwnershipRank(best, owner) : int.MaxValue;
                float distance = best != null ? (best.agentPosition.ToXY() - summon.agentPosition.ToXY()).sqrMagnitude : float.PositiveInfinity;
                if (_am == null) return;
                foreach (var enemy in _am.allEntities)
                {
                    if (enemy == null || !SummonCandidate(ai, enemy)) continue;
                    float range = Vector2.Distance(summon.position.ToXY(), enemy.position.ToXY()) - enemy.Control.outerRadius;
                    if (range > ai.detectionRange) continue;
                    int candidateRank = SummonOwnershipRank(enemy, owner);
                    float candidateDistance = (enemy.agentPosition.ToXY() - summon.agentPosition.ToXY()).sqrMagnitude;
                    if (candidateRank < rank || candidateRank == rank && candidateDistance < distance)
                    { best = enemy; rank = candidateRank; distance = candidateDistance; }
                }
                if (best != null) target = best;
            }
            catch (Exception ex)
            {
                _summonOwnershipFailed = true;
                Log.Warn("Host: summon ownership preference disabled; native targeting retained: " + ex.Message);
            }
        }

        private static bool SummonRetaliation(Summon summon, Entity enemy)
        {
            var target = enemy?.AI?.context.targetEnemy;
            return enemy != null && (target == summon || target == summon.hero
                || enemy.Control != null && (enemy.Control.attackTarget == summon || enemy.Control.attackTarget == summon.hero));
        }

        private static bool SummonCandidate(EntityAI ai, Entity enemy)
        {
            return enemy != null && enemy.isActive && enemy.Status != null && enemy.Status.isAlive
                && !(enemy is Hero hero && hero.isKnockedOut) && !enemy.Status.isUndetectableByNonAllies
                && ai.entity.GetRelation(enemy) == EntityRelation.Enemy && ai.entity.CanAggroTo(enemy)
                && (ai.context.targetEnemy == enemy || Dew.GetNavMeshPathStatusCached(ai.entity, enemy,
                    ai.entity.agentPosition, enemy.agentPosition) == NavMeshPathStatus.PathComplete);
        }

        private int SummonOwnershipRank(Entity enemy, DewPlayer owner)
        {
            if (!TryOwnership(enemy, OwnershipPart.Targeting, out var state)) return 1;
            if (state.Owner == owner) return 0;
            return OwnershipProtected(state) ? 3 : 2;
        }
    }
}
