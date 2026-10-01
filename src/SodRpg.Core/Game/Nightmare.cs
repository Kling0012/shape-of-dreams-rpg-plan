using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>悪夢化した敵の接頭効果（ビット集合）。複数を併せ持つことがある。</summary>
    [Flags]
    public enum NightmareAffix
    {
        None = 0,
        /// <summary>鋼殻：防御+60</summary>
        Ironclad = 1 << 0,
        /// <summary>狂暴：攻撃力+40%・攻撃速度+30%</summary>
        Berserk = 1 << 1,
        /// <summary>巨躯：最大HP+150%</summary>
        Colossal = 1 << 2,
        /// <summary>疾風：移動速度+35%・攻撃速度+20%</summary>
        Swift = 1 << 3,
        /// <summary>再生：毎秒 最大HPの2%回復</summary>
        Regenerating = 1 << 4,
        /// <summary>魔力：魔力+50%・スキル加速+40</summary>
        Arcane = 1 << 5,
    }

    /// <summary>
    /// 悪夢化エリート（Diablo のチャンピオンに相当）。パーティの最大の夢の深度に応じて、ホストが一部の敵を悪夢化する。
    /// 悪夢化した敵は強く、倒すとエリート相当の戦利品・欠片・経験値が出る。
    /// </summary>
    public static class Nightmares
    {
        /// <summary>悪夢化の基礎体力の上乗せ（%）。</summary>
        public const int BaseHealthPct = 80;

        public static readonly NightmareAffix[] AllAffixes =
        {
            NightmareAffix.Ironclad, NightmareAffix.Berserk, NightmareAffix.Colossal,
            NightmareAffix.Swift, NightmareAffix.Regenerating, NightmareAffix.Arcane,
        };

        /// <summary>悪夢化の確率。深度0では起きない。通常の敵は深度1で2%・以後+2%、エリートは深度1で20%・以後+8%。ボスは対象外。</summary>
        public static double Chance(MonsterTier tier, int depth)
        {
            depth = Loot.ClampHeat(depth);
            if (depth <= 0) return 0;
            switch (tier)
            {
                case MonsterTier.Lesser: return 0.01 * depth;
                case MonsterTier.Normal: return 0.02 * depth;
                case MonsterTier.MiniBoss: return 0.20 + 0.08 * (depth - 1);
                default: return 0;
            }
        }

        /// <summary>悪夢化するか抽選し、するなら接頭効果（深度3以上で2つ、5で3つ）を返す。</summary>
        public static NightmareAffix Roll(Rng rng, MonsterTier tier, int depth)
        {
            if (!rng.Chance(Chance(tier, depth))) return NightmareAffix.None;
            int count = depth >= 5 ? 3 : depth >= 3 ? 2 : 1;
            var pool = new List<NightmareAffix>(AllAffixes);
            var result = NightmareAffix.None;
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int k = rng.Range(0, pool.Count - 1);
                result |= pool[k];
                pool.RemoveAt(k);
            }
            return result;
        }

        public static int Count(NightmareAffix a)
        {
            int n = 0;
            foreach (var x in AllAffixes)
                if ((a & x) != 0) n++;
            return n;
        }

        /// <summary>悪夢化した敵へ付ける能力（表示単位）。HP回復は割合なので別に返す。</summary>
        public static List<StatLine> MonsterStats(NightmareAffix a, out float regenPctPerSecond)
        {
            var list = new List<StatLine> { new StatLine(Stat.MaxHealthPct, BaseHealthPct) };
            regenPctPerSecond = 0;
            if ((a & NightmareAffix.Ironclad) != 0) list.Add(new StatLine(Stat.Armor, 60));
            if ((a & NightmareAffix.Berserk) != 0)
            {
                list.Add(new StatLine(Stat.AttackPct, 40));
                list.Add(new StatLine(Stat.AttackSpeedPct, 30));
            }
            if ((a & NightmareAffix.Colossal) != 0) list.Add(new StatLine(Stat.MaxHealthPct, 150));
            if ((a & NightmareAffix.Swift) != 0)
            {
                list.Add(new StatLine(Stat.MoveSpeedPct, 35));
                list.Add(new StatLine(Stat.AttackSpeedPct, 20));
            }
            if ((a & NightmareAffix.Regenerating) != 0) regenPctPerSecond = 2f;
            if ((a & NightmareAffix.Arcane) != 0)
            {
                list.Add(new StatLine(Stat.PowerPct, 50));
                list.Add(new StatLine(Stat.Haste, 40));
            }
            return list;
        }

        public static string AffixName(NightmareAffix a)
        {
            switch (a)
            {
                case NightmareAffix.Ironclad: return Loc.T("鋼殻", "Ironclad");
                case NightmareAffix.Berserk: return Loc.T("狂暴", "Berserk");
                case NightmareAffix.Colossal: return Loc.T("巨躯", "Colossal");
                case NightmareAffix.Swift: return Loc.T("疾風", "Swift");
                case NightmareAffix.Regenerating: return Loc.T("再生", "Regenerating");
                case NightmareAffix.Arcane: return Loc.T("魔力", "Arcane");
                default: return "";
            }
        }

        /// <summary>頭上に出す名札。例：「悪夢・鋼殻・狂暴」。</summary>
        public static string Label(NightmareAffix a)
        {
            var parts = new List<string> { Loc.T("悪夢", "Nightmare") };
            foreach (var x in AllAffixes)
                if ((a & x) != 0) parts.Add(AffixName(x));
            return string.Join(Loc.T("・", " "), parts);
        }

        /// <summary>悪夢化した敵の撃破を、何の格として抽選するか。通常・雑魚はエリート相当、エリートはボス相当。</summary>
        public static MonsterTier RewardTier(MonsterTier tier)
        {
            switch (tier)
            {
                case MonsterTier.Lesser:
                case MonsterTier.Normal: return MonsterTier.MiniBoss;
                case MonsterTier.MiniBoss: return MonsterTier.Boss;
                default: return tier;
            }
        }

        /// <summary>通信用：接頭効果を検証して返す（未知のビットは落とす）。</summary>
        public static NightmareAffix Sanitize(int raw)
        {
            var all = NightmareAffix.None;
            foreach (var x in AllAffixes) all |= x;
            return (NightmareAffix)raw & all;
        }
    }
}
