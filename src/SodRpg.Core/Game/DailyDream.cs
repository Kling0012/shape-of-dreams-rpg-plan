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
        /// <summary>強化される固有効果（強化量は PowerBoostPct）。</summary>
        public Power[] BoostedPowers = Array.Empty<Power>();
        public double DropBonus;
        public double ShardMult = 1.0;
        public double XpMult = 1.0;
        public double BountyMult = 1.0;
        public double NightmareMult = 1.0;

        public const int PowerBoostPct = DailyDreamBalance.PowerBoostPct;

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
                ShardMult = DailyDreamBalance.Day4ShardMult,
            },
            new DailyDream
            {
                Id = 5, Name = new Txt("豊穣の夢", "Bountiful Dream"),
                DropBonus = DailyDreamBalance.Day5DropBonus,
            },
            new DailyDream
            {
                Id = 6, Name = new Txt("悪夢の夜", "Night of Nightmares"),
                NightmareMult = DailyDreamBalance.Day6NightmareMult,
            },
            new DailyDream
            {
                Id = 7, Name = new Txt("星降る夢", "Starfall Dream"),
                XpMult = DailyDreamBalance.Day7XpMult,
            },
            new DailyDream
            {
                Id = 8, Name = new Txt("静かな夢", "Quiet Dream"),
                BountyMult = DailyDreamBalance.Day8BountyMult,
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
                XpMult = DailyDreamBalance.Day13XpMult,
            },
            new DailyDream
            {
                Id = 14, Name = new Txt("依頼の夢", "Dream of Errands"),
                BountyMult = DailyDreamBalance.Day14BountyMult, ShardMult = DailyDreamBalance.Day14ShardMult,
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
                DropBonus = DailyDreamBalance.Day21DropBonus, ShardMult = DailyDreamBalance.Day21ShardMult,
            },
            new DailyDream
            {
                Id = 22, Name = new Txt("悪夢の祭り", "Festival of Nightmares"),
                NightmareMult = DailyDreamBalance.Day22NightmareMult, XpMult = DailyDreamBalance.Day22XpMult,
            },
            new DailyDream
            {
                Id = 23, Name = new Txt("宝の夢", "Treasure Dream"),
                DropBonus = DailyDreamBalance.Day23DropBonus,
            },
            new DailyDream
            {
                Id = 24, Name = new Txt("修練の夢", "Training Dream"),
                XpMult = DailyDreamBalance.Day24XpMult, BountyMult = DailyDreamBalance.Day24BountyMult,
            },
            new DailyDream
            {
                Id = 25, Name = new Txt("職人の夢", "Artisan's Dream"),
                ShardMult = DailyDreamBalance.Day25ShardMult,
            },
            new DailyDream
            {
                Id = 26, Name = new Txt("静寂の森", "Silent Forest"),
                BountyMult = DailyDreamBalance.Day26BountyMult, DropBonus = DailyDreamBalance.Day26DropBonus,
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
                NightmareMult = DailyDreamBalance.Day30NightmareMult, DropBonus = DailyDreamBalance.Day30DropBonus,
            },
            new DailyDream
            {
                Id = 31, Name = new Txt("渦の日", "Day of the Maelstrom"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Frenzy, Power.Whirlwind, Power.Sprint },
            },
            new DailyDream
            {
                Id = 32, Name = new Txt("満潮の夜", "Night of High Tide"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.OverflowingLife, Power.SecondWind, Power.Lifesteal },
            },
            new DailyDream
            {
                Id = 33, Name = new Txt("祈りの日", "Day of Prayer"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Devotion, Power.Radiance, Power.Barrier },
            },
            new DailyDream
            {
                Id = 34, Name = new Txt("稲妻の道", "Path of Lightning"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ChainLightning, Power.CriticalEcho, Power.OpeningStrike },
            },
            new DailyDream
            {
                Id = 35, Name = new Txt("静水の日", "Day of Still Water"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.StillWater, Power.Fetters, Power.Aegis },
            },
            new DailyDream
            {
                Id = 36, Name = new Txt("飛び火の午後", "Wildfire Afternoon"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Wildfire, Power.Ember, Power.Blaze },
            },
            new DailyDream
            {
                Id = 37, Name = new Txt("終曲の夜", "Night of the Finale"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Finale, Power.CriticalEcho, Power.UltimateSurge },
            },
            new DailyDream
            {
                Id = 38, Name = new Txt("不屈の朝", "Morning of Resolve"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Vigor, Power.Bulwark, Power.Retaliation },
            },
            new DailyDream
            {
                Id = 39, Name = new Txt("瞬歩の夢", "Dream of Shadow Steps"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ShadowStep, Power.EchoingDodge, Power.Sprint },
            },
            new DailyDream
            {
                Id = 40, Name = new Txt("結晶の夜", "Night of Crystals"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.CrystalResonance, Power.Convergence, Power.Radiance },
            },
            new DailyDream
            {
                Id = 41, Name = new Txt("狩りの朝", "Morning of the Hunt"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.PreyPride, Power.OpeningStrike, Power.Momentum },
            },
            new DailyDream
            {
                Id = 42, Name = new Txt("散財の夜", "Night of Spending"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.SpendersWard, Power.Barrier, Power.StarShield },
            },
            new DailyDream
            {
                Id = 43, Name = new Txt("見切りの日", "Day of Perfect Reads"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.PerfectRead, Power.EchoingDodge, Power.Tailwind },
            },
            new DailyDream
            {
                Id = 44, Name = new Txt("明晰の夜", "Lucid Night"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.LucidBoon, Power.Umbra, Power.Executioner },
            },
            new DailyDream
            {
                Id = 45, Name = new Txt("過負荷の昼", "Overloaded Noon"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Overload, Power.UltimateSurge, Power.Resonance },
            },
            new DailyDream
            {
                Id = 46, Name = new Txt("満月の収穫", "Full Moon Harvest"),
                ShardMult = DailyDreamBalance.Day46ShardMult, DropBonus = DailyDreamBalance.Day46DropBonus,
            },
            new DailyDream
            {
                Id = 47, Name = new Txt("長い夜の夢", "Long Night Dream"),
                XpMult = DailyDreamBalance.Day47XpMult, NightmareMult = DailyDreamBalance.Day47NightmareMult,
            },
            new DailyDream
            {
                Id = 48, Name = new Txt("追い風の市場", "Tailwind Market"),
                BountyMult = DailyDreamBalance.Day48BountyMult, XpMult = DailyDreamBalance.Day48XpMult,
            },
            new DailyDream
            {
                Id = 49, Name = new Txt("星霜の夢", "Stellar Dream"),
                DropBonus = DailyDreamBalance.Day49DropBonus, XpMult = DailyDreamBalance.Day49XpMult,
            },
            new DailyDream
            {
                Id = 50, Name = new Txt("深淵の縁", "Edge of the Abyss"),
                NightmareMult = DailyDreamBalance.Day50NightmareMult, DropBonus = DailyDreamBalance.Day50DropBonus,
            },
            new DailyDream
            {
                Id = 51, Name = new Txt("骨の市", "Bone Market"),
                ShardMult = DailyDreamBalance.Day51ShardMult, BountyMult = DailyDreamBalance.Day51BountyMult,
            },
            new DailyDream
            {
                Id = 52, Name = new Txt("早朝の霧", "Dawn Mist"),
                FeaturedLine = Line.Guard, DropBonus = DailyDreamBalance.Day52DropBonus,
            },
            new DailyDream
            {
                Id = 53, Name = new Txt("白夜", "White Night"),
                FeaturedLine = Line.Resonance, XpMult = DailyDreamBalance.Day53XpMult,
            },
            new DailyDream
            {
                Id = 54, Name = new Txt("黒曜の夜", "Obsidian Night"),
                FeaturedLine = Line.Offense, NightmareMult = DailyDreamBalance.Day54NightmareMult,
            },
            new DailyDream
            {
                Id = 55, Name = new Txt("春の目覚め", "Spring Waking"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Aegis, Power.SecondWind, Power.OverflowingLife },
            },
            new DailyDream
            {
                Id = 56, Name = new Txt("夏の呼び声", "Summer Call"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Bloodlust, Power.Frenzy, Power.OpeningStrike },
            },
            new DailyDream
            {
                Id = 57, Name = new Txt("秋の実り", "Autumn Yield"),
                FeaturedLine = Line.Resonance, ShardMult = DailyDreamBalance.Day57ShardMult, DropBonus = DailyDreamBalance.Day57DropBonus,
            },
            new DailyDream
            {
                Id = 58, Name = new Txt("冬の静けさ", "Winter Stillness"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Frost, Power.StillWater, Power.Fetters },
            },
            new DailyDream
            {
                Id = 59, Name = new Txt("双子星の夜", "Night of Twin Stars"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Convergence, Power.CrystalResonance, Power.Devotion },
            },
            new DailyDream
            {
                Id = 60, Name = new Txt("果てしない夢", "Endless Dream"),
                XpMult = DailyDreamBalance.Day60XpMult, ShardMult = DailyDreamBalance.Day60ShardMult,
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
