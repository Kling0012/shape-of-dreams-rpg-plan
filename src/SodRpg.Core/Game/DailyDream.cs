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
        public Txt Description;
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
                Description = new Txt("烈火・処刑・連撃が+50%、攻勢の遺物が出やすい", "Blaze, Executioner and Momentum +50%; Offense relics favored"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Blaze, Power.Executioner, Power.Momentum },
            },
            new DailyDream
            {
                Id = 2, Name = new Txt("鋼の日", "Day of Steel"),
                Description = new Txt("鉄の輪・棘・護りの灯・逆襲が+50%、守勢の遺物が出やすい", "Bulwark, Thorns, Barrier and Retaliation +50%; Guard relics favored"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Bulwark, Power.Thorns, Power.Barrier, Power.Retaliation },
            },
            new DailyDream
            {
                Id = 3, Name = new Txt("共鳴の日", "Day of Resonance"),
                Description = new Txt("共鳴・追い風・灯守・吸命が+50%、共鳴の遺物が出やすい", "Resonance, Tailwind, Second Wind and Lifesteal +50%; Resonance relics favored"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Resonance, Power.Tailwind, Power.SecondWind, Power.Lifesteal },
            },
            new DailyDream
            {
                Id = 4, Name = new Txt("黄金の夢", "Golden Dream"),
                Description = new Txt("撃破で得る欠片×1.5", "x1.5 shards from kills"),
                ShardMult = 1.5,
            },
            new DailyDream
            {
                Id = 5, Name = new Txt("豊穣の夢", "Bountiful Dream"),
                Description = new Txt("遺物ドロップ率+25%", "+25% relic drop rate"),
                DropBonus = 0.25,
            },
            new DailyDream
            {
                Id = 6, Name = new Txt("悪夢の夜", "Night of Nightmares"),
                Description = new Txt("悪夢化の確率×2（深く潜るほど危険で実入りが良い）", "x2 nightmare chance (deeper is deadlier and richer)"),
                NightmareMult = 2.0,
            },
            new DailyDream
            {
                Id = 7, Name = new Txt("星降る夢", "Starfall Dream"),
                Description = new Txt("撃破経験値×1.3", "x1.3 xp from kills"),
                XpMult = 1.3,
            },
            new DailyDream
            {
                Id = 8, Name = new Txt("静かな夢", "Quiet Dream"),
                Description = new Txt("依頼の報酬×2", "x2 bounty rewards"),
                BountyMult = 2.0,
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
            string[] ja = { "新参", "見習い", "旅人", "熟練", "練達", "達人", "夢歩き", "夢守り", "星読み", "夢の主", "伝説" };
            string[] en = { "Newcomer", "Apprentice", "Wayfarer", "Adept", "Veteran", "Master", "Dreamwalker", "Dreamwarden", "Stargazer", "Dreamlord", "Legend" };
            level = Math.Max(0, Math.Min(MaxLevel, level));
            return Loc.T(ja[level], en[level]);
        }
    }
}
