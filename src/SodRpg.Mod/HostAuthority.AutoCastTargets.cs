using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class EnemyContactComparer : IEqualityComparer<KeyValuePair<Entity, Hero>>
        {
            public bool Equals(KeyValuePair<Entity, Hero> x, KeyValuePair<Entity, Hero> y) =>
                ReferenceEquals(x.Key, y.Key) && ReferenceEquals(x.Value, y.Value);
            public int GetHashCode(KeyValuePair<Entity, Hero> pair) =>
                unchecked(RuntimeHelpers.GetHashCode(pair.Key) * 397 ^ RuntimeHelpers.GetHashCode(pair.Value));
        }

        // Host-observed hits also cover allies without a MOD build/runtime. The comparer
        // avoids ValueType.Equals boxing on each candidate lookup in the frame scan.
        private readonly Dictionary<KeyValuePair<Entity, Hero>, float> _enemyContacts =
            new Dictionary<KeyValuePair<Entity, Hero>, float>(new EnemyContactComparer());
        private readonly List<KeyValuePair<Entity, Hero>> _expiredEnemyContacts =
            new List<KeyValuePair<Entity, Hero>>();
        private bool _autoCastContactsFailed;

        private void RecordAutoCastContact(EventInfoDamage info)
        {
            if (_autoCastContactsFailed || !NetworkServer.active || DewPlayer.allHumanPlayers.Count <= 1) return;
            try
            {
                var victim = info.victim;
                var actor = info.actor;
                Hero hero = null;
                for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                {
                    if (actor is Hero source) { hero = source; break; }
                    if (actor is SkillTrigger skill && skill.owner is Hero skillHero) { hero = skillHero; break; }
                    if (actor is AbilityInstance ability && ability.info.caster is Hero caster) { hero = caster; break; }
                    if (actor is Entity entity && entity.owner != null && entity.owner.isHumanPlayer)
                    {
                        hero = entity.owner.hero;
                        break;
                    }
                }
                if (victim == null || hero == null || info.damage.amount <= 0f
                    || victim.GetRelation(hero) != EntityRelation.Enemy) return;
                _enemyContacts[new KeyValuePair<Entity, Hero>(victim, hero)] = Time.time;
            }
            catch (Exception ex)
            {
                _autoCastContactsFailed = true;
                _enemyContacts.Clear();
                Log.Warn("Host: automatic casting hit history unavailable; keeping aggro/nearest targeting: " + ex.Message);
            }
        }

        internal int AutoCastTargetPriority(Hero hero, Entity target, float now)
        {
            if (target.GetRelation(hero) != EntityRelation.Enemy) return 1;
            bool ownHit = !_autoCastContactsFailed
                && _enemyContacts.TryGetValue(new KeyValuePair<Entity, Hero>(target, hero), out float ownTime)
                && now - ownTime <= CoopBalance.AutoCastOwnHitSeconds;
            bool allyHit = false;
            if (!ownHit && !_autoCastContactsFailed)
            {
                var players = DewPlayer.allHumanPlayers;
                for (int i = 0; i < players.Count; i++)
                {
                    var ally = players[i] != null ? players[i].hero : null;
                    if (ally == null || ally == hero || ally.GetRelation(hero) != EntityRelation.Ally) continue;
                    if (_enemyContacts.TryGetValue(new KeyValuePair<Entity, Hero>(target, ally), out float time)
                        && now - time <= CoopBalance.AutoCastAllyHitSeconds)
                    {
                        allyHit = true;
                        break;
                    }
                }
            }
            // Preserve ally-only hit history as the fallback, including when ownership is off.
            // The ownership hook can promote this hero's own assignment to the first rank.
            int priority = allyHit ? 2 : ownHit || target.AI != null && target.AI.context.targetEnemy == hero
                || target.Control.attackTarget == hero ? 0 : 1;
            ApplyAutoCastOwnershipPriority(hero, target, ref priority);
            return priority;
        }

        partial void ApplyAutoCastOwnershipPriority(Hero hero, Entity target, ref int priority);

        private void PruneAutoCastContacts(float now)
        {
            _expiredEnemyContacts.Clear();
            float keep = Math.Max(CoopBalance.AutoCastOwnHitSeconds, CoopBalance.AutoCastAllyHitSeconds);
            foreach (var pair in _enemyContacts)
                if (pair.Key.Key == null || !pair.Key.Key.isActive || pair.Key.Key.currentHealth <= 0f
                    || pair.Key.Value == null || !pair.Key.Value.isActive
                    || pair.Key.Value.owner == null || pair.Key.Value.owner.hero != pair.Key.Value
                    || now - pair.Value > keep)
                    _expiredEnemyContacts.Add(pair.Key);
            foreach (var key in _expiredEnemyContacts) _enemyContacts.Remove(key);
        }

        private void ClearAutoCastContacts()
        {
            _enemyContacts.Clear();
            _expiredEnemyContacts.Clear();
        }

        partial void OnAutoCastRoomLoaded() => ClearAutoCastContacts();
    }
}
