using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private readonly HashSet<string> _nearbyBondHeroes = new HashSet<string>();
        private readonly Dictionary<string, float> _bondCoefficients = new Dictionary<string, float>();

        /// <summary>All allied heroes count, including heroes without a received Dreamforge build.</summary>
        private ICollection<string> CollectNearbyBondHeroes(Hero hero, bool alive)
        {
            _nearbyBondHeroes.Clear();
            _bondCoefficients.Clear();
            var actors = NetworkedManagerBase<ActorManager>.softInstance;
            if (!alive || hero == null || actors == null) return _nearbyBondHeroes;
            foreach (var ally in actors.allHeroes)
            {
                if (ally == null || ally == hero) continue;
                float coefficient = CoopDistanceNative.Coefficient(hero, ally);
                if (coefficient <= 0f) continue;
                string key = ally.GetType().Name;
                _nearbyBondHeroes.Add(key);
                if (!_bondCoefficients.TryGetValue(key, out float previous) || coefficient > previous)
                    _bondCoefficients[key] = coefficient;
            }
            return _nearbyBondHeroes;
        }
    }
}
