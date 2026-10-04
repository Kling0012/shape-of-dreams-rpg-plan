using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public static class ElementReactions
    {
        private static readonly Txt[] Epithets =
        {
            new Txt("蒸気の", "Steaming"), new Txt("蝕の", "Eclipsing"),
            new Txt("燃え殻の", "Cindered"), new Txt("氷晶の", "Frostbound"),
        };
        public static bool IsPower(Power power) => power >= Power.Steam && power <= Power.FrostCrystal;
        public static Txt Epithet(Power power) => IsPower(power) ? Epithets[(int)power - (int)Power.Steam] : null;
    }

    /// <summary>A read-only snapshot: detecting a pair never removes native elemental stacks.</summary>
    public readonly struct ElementSnapshot
    {
        public readonly int Fire, Light, Dark;
        public readonly bool Cold;
        public ElementSnapshot(int fire, bool cold, int light, int dark)
        {
            Fire = Math.Max(0, fire);
            Cold = cold;
            Light = Math.Max(0, light);
            Dark = Math.Max(0, dark);
        }

        public bool HasPair(Power reaction)
        {
            switch (reaction)
            {
                case Power.Steam: return Fire > 0 && Cold;
                case Power.Eclipse: return Light > 0 && Dark > 0;
                case Power.Cinder: return Fire > 0 && Dark > 0;
                case Power.FrostCrystal: return Light > 0 && Cold;
                default: return false;
            }
        }
    }

    /// <summary>Only Steam and the shield require deferred native work. Expose and Cinder are stored immediately.</summary>
    public readonly struct ElementReactionResult
    {
        public readonly float SteamDamage, Shield;
        public readonly bool Steam;
        public readonly int ExposePercent, CinderStacks;
        public ElementReactionResult(bool steam, float steamDamage, int expose, int cinder, float shield)
        {
            Steam = steam;
            SteamDamage = steamDamage;
            ExposePercent = expose;
            CinderStacks = cinder;
            Shield = shield;
        }
    }

    /// <summary>One runtime per hero. The native adapter supplies snapshots and dispatches isolated effects.</summary>
    public sealed class ElementReactionRuntime
    {
        public const float Interval = 6f;
        public const float SteamRadius = 3f;
        public const float SteamSlowPercent = 30f;
        public const float SteamSlowDuration = 2f;
        public const float ExposeDuration = 4f;
        public const float CinderRadius = 4f;
        public const float ShieldDuration = 4f;

        private sealed class EnemyState
        {
            public float SteamReady = float.NegativeInfinity;
            public float EclipseReady = float.NegativeInfinity;
            public float CinderReady = float.NegativeInfinity;
            public float CrystalReady = float.NegativeInfinity;
            public float ExposeUntil;
            public int Expose, Cinder;
        }

        private readonly Dictionary<int, EnemyState> _enemies = new Dictionary<int, EnemyState>();

        public void Clear() => _enemies.Clear();

        public ElementReactionResult Apply(Build build, int victimId, ElementSnapshot elements, float now,
            float attack, float abilityPower, float maxHealth, float multiplier = 1f, bool isolatedEffect = false)
        {
            if (build == null || victimId == 0 || isolatedEffect || float.IsNaN(now) || float.IsInfinity(now)) return default;
            int steam = Value(build, Power.Steam), eclipse = Value(build, Power.Eclipse);
            int cinder = Value(build, Power.Cinder), crystal = Value(build, Power.FrostCrystal);
            if (!(steam > 0 && elements.HasPair(Power.Steam))
                && !(eclipse > 0 && elements.HasPair(Power.Eclipse))
                && !(cinder > 0 && elements.HasPair(Power.Cinder))
                && !(crystal > 0 && elements.HasPair(Power.FrostCrystal))) return default;
            if (!_enemies.TryGetValue(victimId, out var state))
                _enemies[victimId] = state = new EnemyState();
            // The zone rule can double output, never shorten the safety interval or extend control duration.
            multiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier) ? 1f : Math.Max(0f, Math.Min(2f, multiplier));
            bool burst = steam > 0 && elements.HasPair(Power.Steam) && Take(ref state.SteamReady, now);
            int expose = 0, spread = 0;
            float shield = 0f;
            if (eclipse > 0 && elements.HasPair(Power.Eclipse) && Take(ref state.EclipseReady, now))
            {
                expose = (int)Math.Round(eclipse * multiplier, MidpointRounding.AwayFromZero);
                state.Expose = expose;
                state.ExposeUntil = now + ExposeDuration;
            }
            if (cinder > 0 && elements.HasPair(Power.Cinder) && Take(ref state.CinderReady, now))
            {
                spread = (int)Math.Round(cinder * multiplier, MidpointRounding.AwayFromZero);
                state.Cinder = Math.Max(state.Cinder, spread);
            }
            if (crystal > 0 && elements.HasPair(Power.FrostCrystal) && Take(ref state.CrystalReady, now))
                shield = Positive(maxHealth) * crystal / 100f * multiplier;
            float damage = burst ? Math.Max(Positive(attack), Positive(abilityPower)) * steam / 100f * multiplier : 0f;
            return new ElementReactionResult(burst, damage, expose, spread, shield);
        }

        public int ExposePercent(int victimId, float now) =>
            _enemies.TryGetValue(victimId, out var state) && now < state.ExposeUntil ? state.Expose : 0;

        /// <summary>Forget all target state even when an isolated kill cannot spread its mark.</summary>
        public int OnDeath(int victimId, bool isolatedEffect = false)
        {
            if (!_enemies.TryGetValue(victimId, out var state)) return 0;
            _enemies.Remove(victimId);
            return isolatedEffect ? 0 : state.Cinder;
        }

        private static int Value(Build build, Power power) => Math.Max(0, Math.Min(Content.PowerCap(power), build.Get(power)));
        private static float Positive(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
        private static bool Take(ref float ready, float now)
        {
            if (now < ready) return false;
            ready = now + Interval;
            return true;
        }
    }
}
