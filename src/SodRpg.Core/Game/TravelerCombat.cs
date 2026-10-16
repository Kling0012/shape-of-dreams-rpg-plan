using System;

namespace SodRpg.Core.Game
{
    public static class TravelerCombat
    {
        /// <summary>Equipment attack speed cap as label text; Core tests pin it to <see cref="Content.StatCap"/>.</summary>
        public const string AttackSpeedCapText = "80";

        /// <summary>The four designated travelers qualify only while their current basic attack is melee.</summary>
        public static bool IsMelee(string heroId, bool meleeBasicAttack) => meleeBasicAttack &&
            (heroId == "Hero_Vesper" || heroId == "Hero_Husk" || heroId == "Hero_Mist" || heroId == "Hero_Aurena");

        /// <summary>
        /// Attack speed actually gained from <paramref name="rangePct"/> equipment range, as the host applies it:
        /// range × multiplier is added to the pre-conversion attack speed, then the total is capped.
        /// </summary>
        public static int RangeToSpeedGain(int attackSpeedPct, int rangePct)
        {
            float cap = Content.StatCap(Stat.AttackSpeedPct);
            float before = Math.Min(cap, attackSpeedPct);
            float after = Math.Min(cap, attackSpeedPct + rangePct * CoopBalance.RangeToSpeedMultiplier);
            return (int)Math.Round(after - before);
        }
    }

    /// <summary>Player-facing numbers of the melee co-op features, built from <see cref="CoopBalance"/>.</summary>
    public static class MeleeCoopText
    {
        public static string RangeToSpeed => Loc.T(
            $"近接の旅人は、装備の通常攻撃の射程1%につき攻撃速度が{CoopBalance.RangeToSpeedMultiplier:0.##}%上がる（射程は上がらない）。攻撃速度は装備・星の合計で最大{Content.StatCap(Stat.AttackSpeedPct)}%まで。",
            $"Melee travelers gain {CoopBalance.RangeToSpeedMultiplier:0.##}% attack speed per 1% equipment basic attack range instead of range. Combined equipment and star attack speed is capped at {Content.StatCap(Stat.AttackSpeedPct)}%.");

        public static string Break => Loc.T(
            $"崩し：近接の旅人の通常攻撃か記憶が当たった敵は{CoopBalance.BreakSeconds:0.##}秒間、ほかの味方の旅人から受けるダメージが{CoopBalance.BreakDamagePercent:0.##}%増える。重ならず、当て直すと時間が戻る。",
            $"Break: an enemy hit by a melee traveler's basic attack or memory takes {CoopBalance.BreakDamagePercent:0.##}% more damage from other allied travelers for {CoopBalance.BreakSeconds:0.##}s. It does not stack; hitting again refreshes it.");

        public static string Guard => Loc.T(
            $"近接防御：近接の旅人は、{CoopBalance.MeleeGuardRange:0.##}m以内に敵がいる間、受けるダメージが{CoopBalance.MeleeGuardReductionPercent:0.##}%減る（{CoopBalance.ScanSeconds:0.##}秒ごとに判定）。",
            $"Melee guard: melee travelers take {CoopBalance.MeleeGuardReductionPercent:0.##}% less damage while an enemy is within {CoopBalance.MeleeGuardRange:0.##}m (checked every {CoopBalance.ScanSeconds:0.##}s).");
    }

    /// <summary>
    /// The host's melee/co-op toggles, carried as one optional hello field. 0 means an older host or no reply yet:
    /// the client then estimates with its own settings.
    /// </summary>
    public static class MeleeCoopSettings
    {
        public const int Sent = 1, RangeToSpeed = 2, DistanceFalloff = 4, Break = 8, Guard = 16;

        public static int Encode(bool rangeToSpeed, bool distanceFalloff, bool meleeBreak, bool meleeGuard) => Sent
            | (rangeToSpeed ? RangeToSpeed : 0) | (distanceFalloff ? DistanceFalloff : 0)
            | (meleeBreak ? Break : 0) | (meleeGuard ? Guard : 0);

        public static bool Resolve(int host, int flag, bool local) => (host & Sent) != 0 ? (host & flag) != 0 : local;
    }
}
