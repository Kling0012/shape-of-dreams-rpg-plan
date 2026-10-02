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
        /// <summary>結界：出現時に最大HPの25%の障壁（v1.24）</summary>
        Warded = 1 << 6,
        /// <summary>棘皮：受けたダメージの20%を攻撃者へ返す（v1.24）</summary>
        Thorned = 1 << 7,
        /// <summary>飢渇：与えたダメージの15%だけ回復する（v1.24）</summary>
        Ravenous = 1 << 8,
        /// <summary>破甲：攻撃が当たると、相手の防御を4秒間 20 下げる（v1.24）</summary>
        Sundering = 1 << 9,
    }

    /// <summary>
    /// 悪夢化エリート（Diablo のチャンピオンに相当）。パーティの最大の潜行に応じて、ホストが一部の敵を悪夢化する。
    /// 悪夢化した敵は強く、倒すとエリート相当の戦利品・欠片・経験値が出る。
    /// </summary>
    public static class Nightmares
    {
        /// <summary>悪夢化の基礎体力の上乗せ（%）。見た目と専用攻撃は本体のエリート効果（MirageSkin）が担う。</summary>
        public const int BaseHealthPct = 40;

        public static readonly NightmareAffix[] AllAffixes =
        {
            NightmareAffix.Ironclad, NightmareAffix.Berserk, NightmareAffix.Colossal,
            NightmareAffix.Swift, NightmareAffix.Regenerating, NightmareAffix.Arcane,
            NightmareAffix.Warded, NightmareAffix.Thorned, NightmareAffix.Ravenous, NightmareAffix.Sundering,
        };

        // ───── v1.24：敵側のつり合い ─────

        /// <summary>結界：出現時の障壁（最大HPに対する%）。</summary>
        public const int WardShieldPct = 25;
        /// <summary>棘皮：受けたダメージを返す割合（%）。</summary>
        public const int ThornsReflectPct = 20;
        /// <summary>飢渇：与えたダメージのうち回復する割合（%）。</summary>
        public const int RavenousLeechPct = 15;
        /// <summary>破甲：当たったプレイヤーの防御を下げる量と秒数。</summary>
        public const int SunderArmor = 20;
        public const float SunderSeconds = 4f;

        /// <summary>
        /// 深度に応じた、すべての敵（悪夢化していない敵を含む）への上乗せ。
        /// 通常の敵：深度1ごとに 最大HP+8%・攻撃力+4%、深度3から防御+10。ボス：深度4から 最大HP+10%/深度。
        /// </summary>
        public static List<StatLine> DepthBonus(MonsterTier tier, int depth)
        {
            depth = Loot.ClampHeat(depth);
            var list = new List<StatLine>();
            if (depth <= 0) return list;
            if (tier == MonsterTier.Boss)
            {
                if (depth >= 4) list.Add(new StatLine(Stat.MaxHealthPct, 10 * depth));
                return list;
            }
            list.Add(new StatLine(Stat.MaxHealthPct, 8 * depth));
            list.Add(new StatLine(Stat.AttackPct, 4 * depth));
            if (depth >= 3) list.Add(new StatLine(Stat.Armor, 10));
            return list;
        }

        /// <summary>
        /// 装備の強さから、悪夢化の確率の倍率を出す（1.0〜1.5）。強い装備で潜るほど手応えのある敵が増える。
        /// 強さ＝攻撃力%・魔力%の大きい方＋最大HP%の半分。100で+25%、200以上で+50%。
        /// </summary>
        public static double GearChanceMult(Build b)
        {
            if (b == null) return 1.0;
            int offense = Math.Max(b.Get(Stat.AttackPct), b.Get(Stat.PowerPct));
            int score = offense + b.Get(Stat.MaxHealthPct) / 2;
            return 1.0 + Math.Min(0.5, Math.Max(0, score) / 400.0);
        }

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
        public static NightmareAffix Roll(Rng rng, MonsterTier tier, int depth, double chanceMult = 1.0)
        {
            if (!rng.Chance(Math.Min(1.0, Chance(tier, depth) * chanceMult))) return NightmareAffix.None;
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
            // v1.24：行動に関わる性質（障壁・反射・吸収・防御低下）はホストが処理する。能力値は控えめに足すだけ。
            if ((a & NightmareAffix.Warded) != 0) list.Add(new StatLine(Stat.Armor, 20));
            if ((a & NightmareAffix.Thorned) != 0) list.Add(new StatLine(Stat.Armor, 30));
            if ((a & NightmareAffix.Ravenous) != 0) list.Add(new StatLine(Stat.AttackPct, 15));
            if ((a & NightmareAffix.Sundering) != 0) list.Add(new StatLine(Stat.AttackPct, 10));
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
                case NightmareAffix.Warded: return Loc.T("結界", "Warded");
                case NightmareAffix.Thorned: return Loc.T("棘皮", "Thorned");
                case NightmareAffix.Ravenous: return Loc.T("飢渇", "Ravenous");
                case NightmareAffix.Sundering: return Loc.T("破甲", "Sundering");
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
