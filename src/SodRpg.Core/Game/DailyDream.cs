using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 今日の夢：日付（UTC）で決まるその日の変化。遠征の開始日の夢が、その遠征の間ずっと効く。
    /// Slay the Spire のデイリー挑戦のように、毎日違う遊び方を促す。
    /// </summary>
    public sealed class DailyDream
    {
        public int Id;
        public Txt Name;
        /// <summary>説明文。数値から作るので、数値を変えても説明と食い違わない。</summary>
        public string Description => Describe();

        private static string M(double m) => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private string Describe()
        {
            bool ja = Loc.Japanese;
            var parts = new List<string>();
            if (BoostedPowers.Length > 0)
            {
                var names = new List<string>();
                foreach (var pw in BoostedPowers) names.Add(Content.PowerName(pw));
                parts.Add(ja ? $"{string.Join("・", names)}の効果が{PowerBoostPct}%強くなります" : $"{string.Join(", ", names)} are {PowerBoostPct}% stronger");
            }
            if (FeaturedLine.HasValue)
                parts.Add(ja ? $"{Content.LineName(FeaturedLine.Value)}の遺物が出やすくなります（狙い系統が「なし」のとき）" : $"{Content.LineName(FeaturedLine.Value)} relics drop more often (when Focus is None)");
            if (DropBonus > 0) parts.Add(ja ? $"遺物が{(int)Math.Round(DropBonus * 100)}%多く落ちます" : $"{(int)Math.Round(DropBonus * 100)}% more relic drops");
            if (ShardMult > 1.0) parts.Add(ja ? $"敵を倒して得る欠片が{M(ShardMult)}倍になります" : $"x{M(ShardMult)} shards from kills");
            if (XpMult > 1.0) parts.Add(ja ? $"敵を倒して得る経験値が{M(XpMult)}倍になります" : $"x{M(XpMult)} experience from kills");
            if (BountyMult > 1.0) parts.Add(ja ? $"依頼の報酬が{M(BountyMult)}倍になります" : $"x{M(BountyMult)} bounty rewards");
            if (NightmareMult > 1.0) parts.Add(ja ? $"悪夢化する敵が{M(NightmareMult)}倍に増えます" : $"x{M(NightmareMult)} nightmares");
            return ja ? string.Join("。", parts) + "。" : string.Join("; ", parts) + ".";
        }
        /// <summary>狙い系統を選んでいないときに代わりに使う系統。</summary>
        public Line? FeaturedLine;
        /// <summary>強化される固有効果（値+50%）。</summary>
        public Power[] BoostedPowers = Array.Empty<Power>();
        public double DropBonus;
        public double ShardMult = 1.0;
        public double XpMult = 1.0;
        public double BountyMult = 1.0;
        public double NightmareMult = 1.0;

        public const int PowerBoostPct = 50;

        public static readonly IReadOnlyList<DailyDream> All = new[]
        {
            new DailyDream
            {
                Id = 1, Name = new Txt("烈火の日", "Day of Blaze"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Blaze, Power.Executioner, Power.Momentum, Power.Ember },
            },
            new DailyDream
            {
                Id = 2, Name = new Txt("鋼の日", "Day of Steel"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Bulwark, Power.Thorns, Power.Barrier, Power.Retaliation },
            },
            new DailyDream
            {
                Id = 3, Name = new Txt("共鳴の日", "Day of Resonance"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Resonance, Power.Tailwind, Power.SecondWind, Power.Lifesteal, Power.Radiance, Power.Convergence },
            },
            new DailyDream
            {
                Id = 4, Name = new Txt("黄金の夢", "Golden Dream"),
                ShardMult = 1.5,
            },
            new DailyDream
            {
                Id = 5, Name = new Txt("豊穣の夢", "Bountiful Dream"),
                DropBonus = 0.25,
            },
            new DailyDream
            {
                Id = 6, Name = new Txt("悪夢の夜", "Night of Nightmares"),
                NightmareMult = 2.0,
            },
            new DailyDream
            {
                Id = 7, Name = new Txt("星降る夢", "Starfall Dream"),
                XpMult = 1.3,
            },
            new DailyDream
            {
                Id = 8, Name = new Txt("静かな夢", "Quiet Dream"),
                BountyMult = 2.0,
            },
            new DailyDream
            {
                Id = 9, Name = new Txt("影の日", "Day of Shadows"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Umbra, Power.Executioner, Power.Shatter },
            },
            new DailyDream
            {
                Id = 10, Name = new Txt("霜の日", "Day of Frost"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Frost, Power.EchoingDodge, Power.Aegis },
            },
            new DailyDream
            {
                Id = 11, Name = new Txt("灯の日", "Day of Lanterns"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Radiance, Power.Barrier, Power.SecondWind, Power.UltimateSurge, Power.StarShield },
            },
            new DailyDream
            {
                Id = 12, Name = new Txt("嵐の日", "Day of Storms"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ChainLightning, Power.Shatter, Power.Tailwind },
            },
            new DailyDream
            {
                Id = 13, Name = new Txt("学びの夢", "Dream of Learning"),
                XpMult = 1.5,
            },
            new DailyDream
            {
                Id = 14, Name = new Txt("依頼の夢", "Dream of Errands"),
                BountyMult = 1.5, ShardMult = 1.25,
            },
            new DailyDream
            {
                Id = 15, Name = new Txt("疾風の日", "Day of Gales"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Momentum, Power.Tailwind, Power.EchoingDodge, Power.Sprint, Power.Whirlwind },
            },
            new DailyDream
            {
                Id = 16, Name = new Txt("血の日", "Day of Blood"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Lifesteal, Power.Bloodlust, Power.Retaliation, Power.SoulSiphon },
            },
            new DailyDream
            {
                Id = 17, Name = new Txt("星の日", "Day of Stars"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.UltimateSurge, Power.Resonance, Power.Convergence },
            },
            new DailyDream
            {
                Id = 18, Name = new Txt("棘の日", "Day of Thorns"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Thorns, Power.Bulwark, Power.Aegis },
            },
            new DailyDream
            {
                Id = 19, Name = new Txt("火の日", "Day of Embers"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Ember, Power.Blaze, Power.Shatter },
            },
            new DailyDream
            {
                Id = 20, Name = new Txt("月の日", "Day of the Moon"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Umbra, Power.Frost, Power.Radiance },
            },
            new DailyDream
            {
                Id = 21, Name = new Txt("豊作の夢", "Harvest Dream"),
                DropBonus = 0.2, ShardMult = 1.2,
            },
            new DailyDream
            {
                Id = 22, Name = new Txt("悪夢の祭り", "Festival of Nightmares"),
                NightmareMult = 1.5, XpMult = 1.2,
            },
            new DailyDream
            {
                Id = 23, Name = new Txt("宝の夢", "Treasure Dream"),
                DropBonus = 0.35,
            },
            new DailyDream
            {
                Id = 24, Name = new Txt("修練の夢", "Training Dream"),
                XpMult = 1.3, BountyMult = 1.5,
            },
            new DailyDream
            {
                Id = 25, Name = new Txt("職人の夢", "Artisan's Dream"),
                ShardMult = 1.6,
            },
            new DailyDream
            {
                Id = 26, Name = new Txt("静寂の森", "Silent Forest"),
                BountyMult = 1.5, DropBonus = 0.1,
            },
            new DailyDream
            {
                Id = 27, Name = new Txt("雷の日", "Day of Thunder"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ChainLightning, Power.Executioner, Power.Momentum, Power.OpeningStrike },
            },
            new DailyDream
            {
                Id = 28, Name = new Txt("守りの日", "Day of Wards"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Barrier, Power.SecondWind, Power.Aegis },
            },
            new DailyDream
            {
                Id = 29, Name = new Txt("響きの日", "Day of Echoes"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.EchoingDodge, Power.Resonance, Power.Tailwind },
            },
            new DailyDream
            {
                Id = 30, Name = new Txt("混沌の夢", "Chaotic Dream"),
                NightmareMult = 1.5, DropBonus = 0.25,
            },
        };

        public static DailyDream Get(int id)
        {
            foreach (var d in All)
                if (d.Id == id) return d;
            return null;
        }

        /// <summary>その日の夢（日付の文字列から決定的に選ぶ）。</summary>
        public static DailyDream For(DateTime date)
        {
            ulong h = Rng.SeedFrom("dreamforge-daily-" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return All[(int)(h % (ulong)All.Count)];
        }

        public static DailyDream Today => For(DateTime.UtcNow.Date);
    }

    /// <summary>旅人（キャラ）ごとの熟練度。撃破数で上がり、レベルごとに攻撃力・魔力・最大HP+1%。</summary>
    public static class Mastery
    {
        private static readonly int[] Thresholds = { 100, 300, 600, 1000, 1500, 2100, 2800, 3600, 4500, 5500 };

        public const int MaxLevel = 10;

        public static int Level(int kills)
        {
            int lv = 0;
            while (lv < Thresholds.Length && kills >= Thresholds[lv]) lv++;
            return lv;
        }

        /// <summary>次のレベルまでの撃破数（最大なら0）。</summary>
        public static int ToNext(int kills)
        {
            int lv = Level(kills);
            return lv >= MaxLevel ? 0 : Thresholds[lv] - kills;
        }

        public static string Title(int level)
        {
            string[] ja = { "新参", "見習い", "旅慣れ", "熟練", "練達", "達人", "夢歩き", "夢守り", "星読み", "夢の主", "伝説" };
            string[] en = { "Newcomer", "Apprentice", "Wayfarer", "Adept", "Veteran", "Master", "Dreamwalker", "Dreamwarden", "Stargazer", "Dreamlord", "Legend" };
            level = Math.Max(0, Math.Min(MaxLevel, level));
            return Loc.T(ja[level], en[level]);
        }
    }
}
