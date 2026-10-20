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
                parts.Add(ja ? $"{string.Join("・", names)}の効果が{PowerBoostPct}%強くなる" : $"{string.Join(", ", names)} are {PowerBoostPct}% stronger");
            }
            if (FeaturedLine.HasValue)
                parts.Add(ja ? $"{Content.LineName(FeaturedLine.Value)}の遺物が出やすくなる（狙い系統が「なし」のとき）" : $"{Content.LineName(FeaturedLine.Value)} relics drop more often (when Focus is None)");
            if (DropBonus > 0) parts.Add(ja ? $"遺物が{(int)Math.Round(DropBonus * 100)}%多く落ちる" : $"{(int)Math.Round(DropBonus * 100)}% more relic drops");
            if (ShardMult > 1.0) parts.Add(ja ? $"敵を倒して得る欠片が{M(ShardMult)}倍になる" : $"x{M(ShardMult)} shards from kills");
            if (XpMult > 1.0) parts.Add(ja ? $"敵を倒して得る経験値が{M(XpMult)}倍になる" : $"x{M(XpMult)} experience from kills");
            if (BountyMult > 1.0) parts.Add(ja ? $"依頼の報酬が{M(BountyMult)}倍になる" : $"x{M(BountyMult)} bounty rewards");
            if (NightmareMult > 1.0) parts.Add(ja ? $"悪夢化する敵が{M(NightmareMult)}倍に増える" : $"x{M(NightmareMult)} nightmares");
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
                Id = 1, Name = new Txt("烈火の日", "Day of Blaze", "烈焰之日"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Blaze, Power.Executioner, Power.Momentum, Power.Ember },
            },
            new DailyDream
            {
                Id = 2, Name = new Txt("鋼の日", "Day of Steel", "钢铁之日"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Bulwark, Power.Thorns, Power.Barrier, Power.Retaliation },
            },
            new DailyDream
            {
                Id = 3, Name = new Txt("共鳴の日", "Day of Resonance", "共鸣之日"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Resonance, Power.Tailwind, Power.SecondWind, Power.Lifesteal, Power.Radiance, Power.Convergence },
            },
            new DailyDream
            {
                Id = 4, Name = new Txt("黄金の夢", "Golden Dream", "黄金之梦"),
                ShardMult = DailyDreamBalance.Day4ShardMult,
            },
            new DailyDream
            {
                Id = 5, Name = new Txt("豊穣の夢", "Bountiful Dream", "丰饶之梦"),
                DropBonus = DailyDreamBalance.Day5DropBonus,
            },
            new DailyDream
            {
                Id = 6, Name = new Txt("悪夢の夜", "Night of Nightmares", "噩梦之夜"),
                NightmareMult = DailyDreamBalance.Day6NightmareMult,
            },
            new DailyDream
            {
                Id = 7, Name = new Txt("星降る夢", "Starfall Dream", "星落之梦"),
                XpMult = DailyDreamBalance.Day7XpMult,
            },
            new DailyDream
            {
                Id = 8, Name = new Txt("静かな夢", "Quiet Dream", "静谧之梦"),
                BountyMult = DailyDreamBalance.Day8BountyMult,
            },
            new DailyDream
            {
                Id = 9, Name = new Txt("影の日", "Day of Shadows", "暗影之日"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Umbra, Power.Executioner, Power.Shatter },
            },
            new DailyDream
            {
                Id = 10, Name = new Txt("霜の日", "Day of Frost", "霜寒之日"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Frost, Power.EchoingDodge, Power.Aegis },
            },
            new DailyDream
            {
                Id = 11, Name = new Txt("灯の日", "Day of Lanterns", "灯火之日"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Radiance, Power.Barrier, Power.SecondWind, Power.UltimateSurge, Power.StarShield },
            },
            new DailyDream
            {
                Id = 12, Name = new Txt("嵐の日", "Day of Storms", "风暴之日"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ChainLightning, Power.Shatter, Power.Tailwind },
            },
            new DailyDream
            {
                Id = 13, Name = new Txt("学びの夢", "Dream of Learning", "求学之梦"),
                XpMult = DailyDreamBalance.Day13XpMult,
            },
            new DailyDream
            {
                Id = 14, Name = new Txt("依頼の夢", "Dream of Errands", "委托之梦"),
                BountyMult = DailyDreamBalance.Day14BountyMult, ShardMult = DailyDreamBalance.Day14ShardMult,
            },
            new DailyDream
            {
                Id = 15, Name = new Txt("疾風の日", "Day of Gales", "疾风之日"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Momentum, Power.Tailwind, Power.EchoingDodge, Power.Sprint, Power.Whirlwind },
            },
            new DailyDream
            {
                Id = 16, Name = new Txt("血の日", "Day of Blood", "鲜血之日"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Lifesteal, Power.Bloodlust, Power.Retaliation, Power.SoulSiphon },
            },
            new DailyDream
            {
                Id = 17, Name = new Txt("星の日", "Day of Stars", "繁星之日"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.UltimateSurge, Power.Resonance, Power.Convergence },
            },
            new DailyDream
            {
                Id = 18, Name = new Txt("棘の日", "Day of Thorns", "荆棘之日"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Thorns, Power.Bulwark, Power.Aegis },
            },
            new DailyDream
            {
                Id = 19, Name = new Txt("火の日", "Day of Embers", "余烬之日"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Ember, Power.Blaze, Power.Shatter },
            },
            new DailyDream
            {
                Id = 20, Name = new Txt("月の日", "Day of the Moon", "月之日"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Umbra, Power.Frost, Power.Radiance },
            },
            new DailyDream
            {
                Id = 21, Name = new Txt("豊作の夢", "Harvest Dream", "丰收之梦"),
                DropBonus = DailyDreamBalance.Day21DropBonus, ShardMult = DailyDreamBalance.Day21ShardMult,
            },
            new DailyDream
            {
                Id = 22, Name = new Txt("悪夢の祭り", "Festival of Nightmares", "噩梦祭典"),
                NightmareMult = DailyDreamBalance.Day22NightmareMult, XpMult = DailyDreamBalance.Day22XpMult,
            },
            new DailyDream
            {
                Id = 23, Name = new Txt("宝の夢", "Treasure Dream", "宝藏之梦"),
                DropBonus = DailyDreamBalance.Day23DropBonus,
            },
            new DailyDream
            {
                Id = 24, Name = new Txt("修練の夢", "Training Dream", "修炼之梦"),
                XpMult = DailyDreamBalance.Day24XpMult, BountyMult = DailyDreamBalance.Day24BountyMult,
            },
            new DailyDream
            {
                Id = 25, Name = new Txt("職人の夢", "Artisan's Dream", "工匠之梦"),
                ShardMult = DailyDreamBalance.Day25ShardMult,
            },
            new DailyDream
            {
                Id = 26, Name = new Txt("静寂の森", "Silent Forest", "寂静森林"),
                BountyMult = DailyDreamBalance.Day26BountyMult, DropBonus = DailyDreamBalance.Day26DropBonus,
            },
            new DailyDream
            {
                Id = 27, Name = new Txt("雷の日", "Day of Thunder", "雷鸣之日"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ChainLightning, Power.Executioner, Power.Momentum, Power.OpeningStrike },
            },
            new DailyDream
            {
                Id = 28, Name = new Txt("守りの日", "Day of Wards", "结界之日"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Barrier, Power.SecondWind, Power.Aegis },
            },
            new DailyDream
            {
                Id = 29, Name = new Txt("響きの日", "Day of Echoes", "回响之日"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.EchoingDodge, Power.Resonance, Power.Tailwind },
            },
            new DailyDream
            {
                Id = 30, Name = new Txt("混沌の夢", "Chaotic Dream", "混沌之梦"),
                NightmareMult = DailyDreamBalance.Day30NightmareMult, DropBonus = DailyDreamBalance.Day30DropBonus,
            },
            new DailyDream
            {
                Id = 31, Name = new Txt("渦の日", "Day of the Maelstrom", "漩涡之日"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Frenzy, Power.Whirlwind, Power.Sprint },
            },
            new DailyDream
            {
                Id = 32, Name = new Txt("満潮の夜", "Night of High Tide", "满潮之夜"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.OverflowingLife, Power.SecondWind, Power.Lifesteal },
            },
            new DailyDream
            {
                Id = 33, Name = new Txt("祈りの日", "Day of Prayer", "祈祷之日"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Devotion, Power.Radiance, Power.Barrier },
            },
            new DailyDream
            {
                Id = 34, Name = new Txt("稲妻の道", "Path of Lightning", "闪电之路"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ChainLightning, Power.CriticalEcho, Power.OpeningStrike },
            },
            new DailyDream
            {
                Id = 35, Name = new Txt("静水の日", "Day of Still Water", "静水之日"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.StillWater, Power.Fetters, Power.Aegis },
            },
            new DailyDream
            {
                Id = 36, Name = new Txt("飛び火の午後", "Wildfire Afternoon", "野火午后"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Wildfire, Power.Ember, Power.Blaze },
            },
            new DailyDream
            {
                Id = 37, Name = new Txt("終曲の夜", "Night of the Finale", "终曲之夜"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Finale, Power.CriticalEcho, Power.UltimateSurge },
            },
            new DailyDream
            {
                Id = 38, Name = new Txt("不屈の朝", "Morning of Resolve", "不屈之晨"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Vigor, Power.Bulwark, Power.Retaliation },
            },
            new DailyDream
            {
                Id = 39, Name = new Txt("瞬歩の夢", "Dream of Shadow Steps", "瞬步之梦"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.ShadowStep, Power.EchoingDodge, Power.Sprint },
            },
            new DailyDream
            {
                Id = 40, Name = new Txt("結晶の夜", "Night of Crystals", "结晶之夜"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.CrystalResonance, Power.Convergence, Power.Radiance },
            },
            new DailyDream
            {
                Id = 41, Name = new Txt("狩りの朝", "Morning of the Hunt", "狩猎之晨"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.PreyPride, Power.OpeningStrike, Power.Momentum },
            },
            new DailyDream
            {
                Id = 42, Name = new Txt("散財の夜", "Night of Spending", "挥霍之夜"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.SpendersWard, Power.Barrier, Power.StarShield },
            },
            new DailyDream
            {
                Id = 43, Name = new Txt("見切りの日", "Day of Perfect Reads", "识破之日"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.PerfectRead, Power.EchoingDodge, Power.Tailwind },
            },
            new DailyDream
            {
                Id = 44, Name = new Txt("明晰の夜", "Lucid Night", "清明之夜"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.LucidBoon, Power.Umbra, Power.Executioner },
            },
            new DailyDream
            {
                Id = 45, Name = new Txt("過負荷の昼", "Overloaded Noon", "过载正午"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Overload, Power.UltimateSurge, Power.Resonance },
            },
            new DailyDream
            {
                Id = 46, Name = new Txt("満月の収穫", "Full Moon Harvest", "满月丰收"),
                ShardMult = DailyDreamBalance.Day46ShardMult, DropBonus = DailyDreamBalance.Day46DropBonus,
            },
            new DailyDream
            {
                Id = 47, Name = new Txt("長い夜の夢", "Long Night Dream", "长夜之梦"),
                XpMult = DailyDreamBalance.Day47XpMult, NightmareMult = DailyDreamBalance.Day47NightmareMult,
            },
            new DailyDream
            {
                Id = 48, Name = new Txt("追い風の市場", "Tailwind Market", "顺风集市"),
                BountyMult = DailyDreamBalance.Day48BountyMult, XpMult = DailyDreamBalance.Day48XpMult,
            },
            new DailyDream
            {
                Id = 49, Name = new Txt("星霜の夢", "Stellar Dream", "星辰之梦"),
                DropBonus = DailyDreamBalance.Day49DropBonus, XpMult = DailyDreamBalance.Day49XpMult,
            },
            new DailyDream
            {
                Id = 50, Name = new Txt("深淵の縁", "Edge of the Abyss", "深渊边缘"),
                NightmareMult = DailyDreamBalance.Day50NightmareMult, DropBonus = DailyDreamBalance.Day50DropBonus,
            },
            new DailyDream
            {
                Id = 51, Name = new Txt("骨の市", "Bone Market", "骸骨集市"),
                ShardMult = DailyDreamBalance.Day51ShardMult, BountyMult = DailyDreamBalance.Day51BountyMult,
            },
            new DailyDream
            {
                Id = 52, Name = new Txt("早朝の霧", "Dawn Mist", "黎明薄雾"),
                FeaturedLine = Line.Guard, DropBonus = DailyDreamBalance.Day52DropBonus,
            },
            new DailyDream
            {
                Id = 53, Name = new Txt("白夜", "White Night", "白夜"),
                FeaturedLine = Line.Resonance, XpMult = DailyDreamBalance.Day53XpMult,
            },
            new DailyDream
            {
                Id = 54, Name = new Txt("黒曜の夜", "Obsidian Night", "黑曜之夜"),
                FeaturedLine = Line.Offense, NightmareMult = DailyDreamBalance.Day54NightmareMult,
            },
            new DailyDream
            {
                Id = 55, Name = new Txt("春の目覚め", "Spring Waking", "春日苏醒"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Aegis, Power.SecondWind, Power.OverflowingLife },
            },
            new DailyDream
            {
                Id = 56, Name = new Txt("夏の呼び声", "Summer Call", "夏日呼唤"),
                FeaturedLine = Line.Offense, BoostedPowers = new[] { Power.Bloodlust, Power.Frenzy, Power.OpeningStrike },
            },
            new DailyDream
            {
                Id = 57, Name = new Txt("秋の実り", "Autumn Yield", "秋日硕果"),
                FeaturedLine = Line.Resonance, ShardMult = DailyDreamBalance.Day57ShardMult, DropBonus = DailyDreamBalance.Day57DropBonus,
            },
            new DailyDream
            {
                Id = 58, Name = new Txt("冬の静けさ", "Winter Stillness", "冬日静谧"),
                FeaturedLine = Line.Guard, BoostedPowers = new[] { Power.Frost, Power.StillWater, Power.Fetters },
            },
            new DailyDream
            {
                Id = 59, Name = new Txt("双子星の夜", "Night of Twin Stars", "双星之夜"),
                FeaturedLine = Line.Resonance, BoostedPowers = new[] { Power.Convergence, Power.CrystalResonance, Power.Devotion },
            },
            new DailyDream
            {
                Id = 60, Name = new Txt("果てしない夢", "Endless Dream", "无尽之梦"),
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
            return Loc.T(ja[level], en[level], level switch { 0 => "新人", 1 => "学徒", 2 => "行者", 3 => "熟手", 4 => "老练者", 5 => "大师", 6 => "梦行者", 7 => "梦境守卫", 8 => "观星者", 9 => "梦境之主", _ => "传奇" });
        }
    }
}
