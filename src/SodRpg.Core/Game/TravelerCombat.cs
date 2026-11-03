using System;

namespace SodRpg.Core.Game
{
    public static class TravelerCombat
    {
        /// <summary>Equipment attack speed cap as label text; Core tests pin it to <see cref="Content.StatCap"/>.</summary>
        public const string AttackSpeedCapText = "80";

        /// <summary>The four designated travelers qualify only while their current basic attack is melee.</summary>
        public static bool IsMelee(string heroId, bool meleeBasicAttack) => meleeBasicAttack &&
            (heroId == "Hero_Vesper" || heroId == "Hero_Husk" || heroId == "Hero_Mist" || heroId == "Hero_Aurena"
             // 拡張旅人（ヒーローコネクタ §6-3）。MeleeCoop の符号化は変えない。
             || ExtendedHeroes.IsMelee(heroId));

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

    /// <summary>Player-facing numbers of the melee co-op features, built from <see cref="CoopBalance"/>.
    /// Interpolations are invariant (#542) so decimal points never follow the OS locale (0.25 stays "0.25").</summary>
    public static class MeleeCoopText
    {
        public static string RangeToSpeed => Loc.T(
            FormattableString.Invariant($"近接の旅人は装備の通常攻撃の射程1%につき攻撃速度が{CoopBalance.RangeToSpeedMultiplier:0.##}%上がる（射程は上がらない）。攻撃速度は装備・星の合計で最大{Content.StatCap(Stat.AttackSpeedPct)}%まで。"),
            FormattableString.Invariant($"Melee travelers gain {CoopBalance.RangeToSpeedMultiplier:0.##}% attack speed per 1% equipment basic attack range instead of range. Combined equipment and star attack speed is capped at {Content.StatCap(Stat.AttackSpeedPct)}%."), FormattableString.Invariant($"近战旅人的装备通常攻击范围加成每增加1%，改为获得{CoopBalance.RangeToSpeedMultiplier:0.##}%攻击速度。装备与星提供的攻击速度加成合计上限为{Content.StatCap(Stat.AttackSpeedPct)}%。"));

        public static string Break => Loc.T(
            FormattableString.Invariant($"崩し：近接の旅人の通常攻撃か記憶が当たった敵は{CoopBalance.BreakSeconds:0.##}秒間、ほかの味方の旅人から受けるダメージが{CoopBalance.BreakDamagePercent:0.##}%増える。重ならず、当て直すと時間が戻る。"),
            FormattableString.Invariant($"Break: an enemy hit by a melee traveler's basic attack or memory takes {CoopBalance.BreakDamagePercent:0.##}% more damage from other allied travelers for {CoopBalance.BreakSeconds:0.##}s. It does not stack; hitting again refreshes it."), FormattableString.Invariant($"破防：被近战旅人的通常攻击或记忆命中的敌人，受到其他友方旅人的伤害增加{CoopBalance.BreakDamagePercent:0.##}%，持续{CoopBalance.BreakSeconds:0.##}秒。不叠加，再次命中会刷新持续时间。"));

        public static string Guard => Loc.T(
            FormattableString.Invariant($"近接防御：近接の旅人は、{CoopBalance.MeleeGuardRange:0.##}m以内に敵がいる間、受けるダメージが{CoopBalance.MeleeGuardReductionPercent:0.##}%減る（{CoopBalance.ScanSeconds:0.##}秒ごとに判定）。"),
            FormattableString.Invariant($"Melee guard: melee travelers take {CoopBalance.MeleeGuardReductionPercent:0.##}% less damage while an enemy is within {CoopBalance.MeleeGuardRange:0.##}m (checked every {CoopBalance.ScanSeconds:0.##}s)."), FormattableString.Invariant($"近战防御：近战旅人受到的伤害降低{CoopBalance.MeleeGuardReductionPercent:0.##}%，条件为自身{CoopBalance.MeleeGuardRange:0.##}米内有敌人（每{CoopBalance.ScanSeconds:0.##}秒检查一次）。"));
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
