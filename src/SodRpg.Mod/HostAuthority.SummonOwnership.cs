using System;
using UnityEngine;
using UnityEngine.AI;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private bool _summonOwnershipFailed;

        internal void SelectSummonOwnershipAggro(EntityAI ai, ref Entity target)
        {
            if (_summonOwnershipFailed) return;
            try
            {
                if (!OwnershipRunning() || !(ai?.entity is Summon summon) || summon.hero == null
                    || summon.hero.owner == null || !summon.hero.owner.isHumanPlayer) return;
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
