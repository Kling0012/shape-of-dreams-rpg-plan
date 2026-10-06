using System;

namespace SodRpg.Core.Game
{
    /// <summary>Ranks star damage quantities only, after their ordinary source caps. Never changes activation parameters.</summary>
    public static class StarDamageScaling
    {
        public static decimal Multiplier(int spentStarPoints) => 1m + StarRankBalance.DamagePerPoint
            * Math.Max(0, Math.Min(StarProgression.MaxSpendablePoints, spentStarPoints));
        public static int ScaleMilli(int valueMilli, int spentStarPoints) => checked((int)decimal.Round(
            valueMilli * Multiplier(spentStarPoints), 0, MidpointRounding.AwayFromZero));

        public static bool IsDamage(LinkKind kind) => kind == LinkKind.MemoryDamage;

        public static bool IsDamage(GimmickEffect effect)
        {
            switch (effect)
            {
                case GimmickEffect.Burst: case GimmickEffect.Expose: case GimmickEffect.Echo:
                case GimmickEffect.Wound: case GimmickEffect.Ricochet: case GimmickEffect.Primed:
                case GimmickEffect.ElementEdge: return true;
                default: return false;
            }
        }

        public static bool IsDamage(Power power)
        {
            switch (power)
            {
                case Power.Thorns: case Power.Executioner: case Power.Blaze: case Power.ChainLightning:
                case Power.Shatter: case Power.Convergence: case Power.EchoingDodge: case Power.Whirlwind:
                case Power.OpeningStrike: case Power.Fetters: case Power.ShadowStep: case Power.Steam:
                case Power.Eclipse: case Power.ShieldbreakBurst: case Power.ShieldBash: case Power.WanderersEdge:
                case Power.FocusFire: case Power.DuelistsWay: case Power.ImmovableStance: case Power.RunUp:
                case Power.BrittleIce: case Power.ElementalHarvest: case Power.DeathBloom: case Power.Spellsweep:
                case Power.AceInHand: case Power.Lifeline: case Power.RearguardsWay: case Power.TollOfGrudge:
                case Power.WeakPointWound: case Power.CritSplash: case Power.SpilloverStrike: return true;
                default: return false;
            }
        }

        public static int PowerCeiling(Build build, Power power) => checked((int)decimal.Ceiling(Content.PowerCap(power)
            * (IsDamage(power) ? 2.5m + Multiplier(build?.SpentStarPoints ?? 0) : 1m)));

        public static int MaxPowerValue(Power power) => IsDamage(power)
            ? checked((int)decimal.Ceiling(Content.PowerCap(power) * (2.5m + StarRankBalance.MaxMultiplier)))
            : checked((int)(Content.PowerCap(power) * 2.5m));

        public static decimal EffectCeiling(GimmickEffect effect) => Gimmicks.Cap(effect)
            * (IsDamage(effect) ? StarRankBalance.MaxMultiplier : 1m);

        public static decimal PayloadMultiplier(Build build, KeystonePayload payload) =>
            payload.Layer == KeystoneLayer.StarMemoryDamage
            || payload.Layer == KeystoneLayer.ModEffect && (IsDamage(payload.Effect)
                || payload.Kind == KeystonePayloadKind.MemoryPrimed || payload.Kind == KeystonePayloadKind.RelayWindow
                || payload.Kind == KeystonePayloadKind.BridgeSuccess)
                ? Multiplier(build?.SpentStarPoints ?? 0) : 1m;

        /// <summary>Call once on a capped, unranked payload result. Native baseline damage is never ranked.</summary>
        public static KeystoneResult ScaleResult(Build build, KeystonePayload pristine, KeystoneResult result)
        {
            if (pristine == null || result == null) throw new ArgumentNullException(pristine == null ? nameof(pristine) : nameof(result));
            decimal rank = Multiplier(build?.SpentStarPoints ?? 0);
            if (pristine.Layer == KeystoneLayer.NativeDamage || pristine.Layer == KeystoneLayer.GeneratedDamage)
                result.Value = pristine.Value + (result.Value - pristine.Value) * (result.Value > pristine.Value ? rank : 1m);
            else result.Value *= PayloadMultiplier(build, pristine);
            return result;
        }
        internal static void ApplyEffectiveGimmicks(Build build)
        {
            decimal rank = Multiplier(build.SpentStarPoints);
            foreach (var entry in build.Gimmicks)
            {
                var def = entry.Def;
                def.EffectiveValue = Math.Min(def.UncappedValue ?? def.Value, Gimmicks.Cap(def.Effect))
                    * (IsDamage(def.Effect) ? rank : 1m);
            }
            foreach (var pair in build.PairCombos) pair.DamageMultiplier = rank;
        }
    }
}
