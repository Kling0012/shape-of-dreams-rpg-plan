using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private readonly HashSet<string> _nearbyBondHeroes = new HashSet<string>();

        /// <summary>All allied heroes count, including heroes without a received Dreamforge build.</summary>
        private ICollection<string> CollectNearbyBondHeroes(Hero hero, bool alive)
        {
            _nearbyBondHeroes.Clear();
            var actors = NetworkedManagerBase<ActorManager>.softInstance;
            if (!alive || hero == null || actors == null) return _nearbyBondHeroes;
            foreach (var ally in actors.allHeroes)
            {
                if (ally == null || ally == hero) continue;
                if (Links.IsBondAlly(Alive(ally) && ally.isAlive, ally.GetRelation(hero) == EntityRelation.Ally,
                    Vector3.Distance(hero.agentPosition, ally.agentPosition)))
                    _nearbyBondHeroes.Add(ally.GetType().Name);
            }
            return _nearbyBondHeroes;
        }
    }
}
