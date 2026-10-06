using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>悪夢の契約。深く潜るときに1つ選べる。代償は本体の呪い（CurseStatusEffect）、見返りは次に確保するまで重なって効く。</summary>
    public enum Pact
    {
        None = 0,
        /// <summary>硝子の心臓：本体の呪い / 遺物ドロップ率+40%</summary>
        GlassHeart = 1,
        /// <summary>鈍き刃：本体の呪い / レア度の幸運+0.6</summary>
        DullBlade = 2,
        /// <summary>無防備：本体の呪い / 欠片×1.5</summary>
        Unguarded = 3,
        /// <summary>重い足：本体の呪い / 経験値×1.5</summary>
        LeadenFeet = 4,
        /// <summary>狂乱：本体の呪い / 攻撃力・魔力+15%（純粋な戦闘の賭け）</summary>
        Frenzy = 5,
        /// <summary>呪われた財宝：本体の呪い / 全滅時の残響なし</summary>
        CursedHoard = 6,
        /// <summary>乾いた夢：本体の呪い / 調律石の入手+1（エリート・ボス）</summary>
        DryDream = 7,
        /// <summary>見えざる重荷：本体の呪い / エピック以上の確率が上がる（幸運+1.0）</summary>
        Burden = 8,
        Glutton = 9,
        Scholar = 10,
        Gambler = 11,
        AbyssEye = 12,
        BloodPrice = 13,
        IronOath = 14,
        HollowCrown = 15,
        ThiefsBargain = 16,
        Stargazer = 17,
        Wanderer = 18,
        Miser = 19,
        BloodMoon = 20,
        /// <summary>塩の契約：本体の呪い / 欠片×1.4・レア度の幸運+0.5</summary>
        SaltOath = 21,
        /// <summary>白紙の地図：本体の呪い / 調律石の入手+2（エリート・ボス）</summary>
        BlankMap = 22,
        /// <summary>蜜の鎖：本体の呪い / 経験値×1.8</summary>
        HoneyedChains = 23,
        /// <summary>星屑の負債：本体の呪い / 遺物ドロップ率+100%</summary>
        StardustDebt = 24,
        /// <summary>骨の賽子：本体の呪い / レア度の幸運+2.0</summary>
        BoneDice = 25,
        /// <summary>帳の夜：本体の呪い / 闇属性効果+12%</summary>
        NightOfVeils = 26,
        /// <summary>夜明けの誓い：本体の呪い / 光属性効果+12%</summary>
        OathOfDawn = 27,
        /// <summary>灰の杯：本体の呪い / 最大HP+50</summary>
        AshenChalice = 28,
        /// <summary>鏡の割れ：本体の呪い / 会心率+4%</summary>
        CrackedMirror = 29,
        /// <summary>早鐘の心：本体の呪い / 攻撃速度+10%</summary>
        ClangoringHeart = 30,
        /// <summary>灯火の重ね：本体の呪い / 与えるシールド+12%</summary>
        LayeredLamps = 31,
        /// <summary>苔むす契約：本体の呪い / HP回復+4/秒</summary>
        MossboundPact = 32,
        /// <summary>糸繰りの契約：本体の呪い / 召喚獣の与ダメージ+20%</summary>
        PuppetStrings = 33,
        /// <summary>慈雨の契約：本体の呪い / 与える回復+12%</summary>
        GraciousRain = 34,
        /// <summary>淀んだ泉：本体の呪い / 欠片×1.75・全滅時の残響なし</summary>
        StagnantSpring = 35,
        /// <summary>砂時計の嘘：本体の呪い / 経験値×1.6・全滅時の残響なし</summary>
        HourglassLie = 36,
        /// <summary>遠回りの契約：本体の呪い / 経験値×1.4・遺物ドロップ率+25%</summary>
        LongWayRound = 37,
        /// <summary>貝殻の約定：本体の呪い / 遺物ドロップ率+30%・欠片×1.2</summary>
        ShellBargain = 38,
        /// <summary>深泥の約束：本体の呪い / 潜行ボーナス2倍・調律石の入手+1（エリート・ボス）</summary>
        DeepmirePromise = 39,
        /// <summary>金箔の傷：本体の呪い / 攻撃力+10・魔力+10</summary>
        GildedWound = 40,
    }

    public sealed class PactDef
    {
        public Pact Id;
        public Txt Name;
        /// <summary>説明文。数値から作るので、数値を変えても説明と食い違わない。</summary>
        public string Description => Pacts.Describe(this);
        public StatLine[] Penalties = Array.Empty<StatLine>();
        public StatLine[] Boons = Array.Empty<StatLine>();
        public double DropBonus;
        public double Luck;
        public double ShardMult = 1.0;
        public double XpMult = 1.0;
        public int TuningOnElite;
        public bool DoubleDepthBonus;
        public bool NoEcho;
        /// <summary>代償として受ける本体の呪いの強度（1=Mild, 2=Potent, 3=Powerful）。</summary>
        public int CurseStrength = 1;

        public static string StrengthName(int s)
        {
            switch (s)
            {
                case 3: return Loc.T("強", "Powerful");
                case 2: return Loc.T("中", "Potent");
                default: return Loc.T("弱", "Mild");
            }
        }
    }

    public static class Pacts
    {
        public const int Offered = PactBalance.Offered;

        public static readonly IReadOnlyList<PactDef> All = new[]
        {
            new PactDef
            {
                Id = Pact.GlassHeart, Name = new Txt("硝子の心臓", "Glass Heart"),
                CurseStrength = PactBalance.GlassHeartCurseStrength, DropBonus = PactBalance.GlassHeartDropBonus,
            },
            new PactDef
            {
                Id = Pact.DullBlade, Name = new Txt("鈍き刃", "Dull Blade"),
                CurseStrength = PactBalance.DullBladeCurseStrength, Luck = PactBalance.DullBladeLuck,
            },
            new PactDef
            {
                Id = Pact.Unguarded, Name = new Txt("無防備", "Unguarded"),
                CurseStrength = PactBalance.UnguardedCurseStrength, ShardMult = PactBalance.UnguardedShardMult,
            },
            new PactDef
            {
                Id = Pact.LeadenFeet, Name = new Txt("重い足", "Leaden Feet"),
                CurseStrength = PactBalance.LeadenFeetCurseStrength, XpMult = PactBalance.LeadenFeetXpMult,
            },
            new PactDef
            {
                Id = Pact.Frenzy, Name = new Txt("狂乱", "Frenzy"),
                CurseStrength = PactBalance.FrenzyCurseStrength,
                Boons = new[] { new StatLine(Stat.AttackPct, PactBalance.FrenzyBoonAttackPct), new StatLine(Stat.PowerPct, PactBalance.FrenzyBoonPowerPct) },
            },
            new PactDef
            {
                Id = Pact.CursedHoard, Name = new Txt("呪われた財宝", "Cursed Hoard"),
                CurseStrength = PactBalance.CursedHoardCurseStrength,
                DoubleDepthBonus = true, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.DryDream, Name = new Txt("乾いた夢", "Dry Dream"),
                CurseStrength = PactBalance.DryDreamCurseStrength, TuningOnElite = PactBalance.DryDreamTuningOnElite,
            },
            new PactDef
            {
                Id = Pact.Burden, Name = new Txt("見えざる重荷", "Unseen Burden"),
                CurseStrength = PactBalance.BurdenCurseStrength, Luck = PactBalance.BurdenLuck,
            },
            new PactDef
            {
                Id = Pact.Glutton, Name = new Txt("暴食", "Gluttony"),
                CurseStrength = PactBalance.GluttonCurseStrength, DropBonus = PactBalance.GluttonDropBonus,
            },
            new PactDef
            {
                Id = Pact.Scholar, Name = new Txt("夜の学徒", "Night Scholar"),
                CurseStrength = PactBalance.ScholarCurseStrength, XpMult = PactBalance.ScholarXpMult, Luck = PactBalance.ScholarLuck,
            },
            new PactDef
            {
                Id = Pact.Gambler, Name = new Txt("賭け師の誓い", "Gambler's Oath"),
                CurseStrength = PactBalance.GamblerCurseStrength, ShardMult = PactBalance.GamblerShardMult, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.AbyssEye, Name = new Txt("深淵の眼", "Eye of the Abyss"),
                CurseStrength = PactBalance.AbyssEyeCurseStrength, DropBonus = PactBalance.AbyssEyeDropBonus, Luck = PactBalance.AbyssEyeLuck, TuningOnElite = PactBalance.AbyssEyeTuningOnElite,
            },
            new PactDef
            {
                Id = Pact.BloodPrice, Name = new Txt("血の代価", "Blood Price"),
                CurseStrength = PactBalance.BloodPriceCurseStrength,
                Boons = new[] { new StatLine(Stat.AttackSpeedPct, PactBalance.BloodPriceBoonAttackSpeedPct) },
            },
            new PactDef
            {
                Id = Pact.IronOath, Name = new Txt("鉄の誓約", "Iron Oath"),
                CurseStrength = PactBalance.IronOathCurseStrength,
                Boons = new[] { new StatLine(Stat.Armor, PactBalance.IronOathBoonArmor) },
            },
            new PactDef
            {
                Id = Pact.HollowCrown, Name = new Txt("虚ろな王冠", "Hollow Crown"),
                CurseStrength = PactBalance.HollowCrownCurseStrength, DropBonus = PactBalance.HollowCrownDropBonus, ShardMult = PactBalance.HollowCrownShardMult,
            },
            new PactDef
            {
                Id = Pact.ThiefsBargain, Name = new Txt("盗人の取引", "Thief's Bargain"),
                CurseStrength = PactBalance.ThiefsBargainCurseStrength, ShardMult = PactBalance.ThiefsBargainShardMult, DropBonus = PactBalance.ThiefsBargainDropBonus,
            },
            new PactDef
            {
                Id = Pact.Stargazer, Name = new Txt("星読みの契約", "Stargazer's Pact"),
                CurseStrength = PactBalance.StargazerCurseStrength, Luck = PactBalance.StargazerLuck, DropBonus = PactBalance.StargazerDropBonus,
            },
            new PactDef
            {
                Id = Pact.Wanderer, Name = new Txt("放浪者の契約", "Wanderer's Pact"),
                CurseStrength = PactBalance.WandererCurseStrength, XpMult = PactBalance.WandererXpMult, DropBonus = PactBalance.WandererDropBonus,
            },
            new PactDef
            {
                Id = Pact.Miser, Name = new Txt("守銭奴", "Miser"),
                CurseStrength = PactBalance.MiserCurseStrength, DoubleDepthBonus = true, ShardMult = PactBalance.MiserShardMult,
            },
            new PactDef
            {
                Id = Pact.BloodMoon, Name = new Txt("血月の契約", "Blood Moon Pact"),
                CurseStrength = PactBalance.BloodMoonCurseStrength,
                Boons = new[] { new StatLine(Stat.AttackPct, PactBalance.BloodMoonBoonAttackPct), new StatLine(Stat.PowerPct, PactBalance.BloodMoonBoonPowerPct) },
            },
            new PactDef
            {
                Id = Pact.SaltOath, Name = new Txt("塩の契約", "Salt Oath"),
                CurseStrength = PactBalance.SaltOathCurseStrength, ShardMult = PactBalance.SaltOathShardMult, Luck = PactBalance.SaltOathLuck,
            },
            new PactDef
            {
                Id = Pact.BlankMap, Name = new Txt("白紙の地図", "Blank Map"),
                CurseStrength = PactBalance.BlankMapCurseStrength, TuningOnElite = PactBalance.BlankMapTuningOnElite,
            },
            new PactDef
            {
                Id = Pact.HoneyedChains, Name = new Txt("蜜の鎖", "Honeyed Chains"),
                CurseStrength = PactBalance.HoneyedChainsCurseStrength, XpMult = PactBalance.HoneyedChainsXpMult,
            },
            new PactDef
            {
                Id = Pact.StardustDebt, Name = new Txt("星屑の負債", "Stardust Debt"),
                CurseStrength = PactBalance.StardustDebtCurseStrength, DropBonus = PactBalance.StardustDebtDropBonus,
            },
            new PactDef
            {
                Id = Pact.BoneDice, Name = new Txt("骨の賽子", "Bone Dice"),
                CurseStrength = PactBalance.BoneDiceCurseStrength, Luck = PactBalance.BoneDiceLuck,
            },
            new PactDef
            {
                Id = Pact.NightOfVeils, Name = new Txt("帳の夜", "Night of Veils"),
                CurseStrength = PactBalance.NightOfVeilsCurseStrength,
                Boons = new[] { new StatLine(Stat.DarkAmp, PactBalance.NightOfVeilsBoonDarkAmp) },
            },
            new PactDef
            {
                Id = Pact.OathOfDawn, Name = new Txt("夜明けの誓い", "Oath of Dawn"),
                CurseStrength = PactBalance.OathOfDawnCurseStrength,
                Boons = new[] { new StatLine(Stat.LightAmp, PactBalance.OathOfDawnBoonLightAmp) },
            },
            new PactDef
            {
                Id = Pact.AshenChalice, Name = new Txt("灰の杯", "Ashen Chalice"),
                CurseStrength = PactBalance.AshenChaliceCurseStrength,
                Boons = new[] { new StatLine(Stat.MaxHealthFlat, PactBalance.AshenChaliceBoonMaxHealthFlat) },
            },
            new PactDef
            {
                Id = Pact.CrackedMirror, Name = new Txt("鏡の割れ", "Cracked Mirror"),
                CurseStrength = PactBalance.CrackedMirrorCurseStrength,
                Boons = new[] { new StatLine(Stat.CritChancePct, PactBalance.CrackedMirrorBoonCritChancePct) },
            },
            new PactDef
            {
                Id = Pact.ClangoringHeart, Name = new Txt("早鐘の心", "Clangoring Heart"),
                CurseStrength = PactBalance.ClangoringHeartCurseStrength,
                Boons = new[] { new StatLine(Stat.AttackSpeedPct, PactBalance.ClangoringHeartBoonAttackSpeedPct) },
            },
            new PactDef
            {
                Id = Pact.LayeredLamps, Name = new Txt("灯火の重ね", "Layered Lamps"),
                CurseStrength = PactBalance.LayeredLampsCurseStrength,
                Boons = new[] { new StatLine(Stat.ShieldPower, PactBalance.LayeredLampsBoonShieldPower) },
            },
            new PactDef
            {
                Id = Pact.MossboundPact, Name = new Txt("苔むす契約", "Mossbound Pact"),
                CurseStrength = PactBalance.MossboundPactCurseStrength,
                Boons = new[] { new StatLine(Stat.HealthRegen, PactBalance.MossboundPactBoonHealthRegen) },
            },
            new PactDef
            {
                Id = Pact.PuppetStrings, Name = new Txt("糸繰りの契約", "Puppet Strings"),
                CurseStrength = PactBalance.PuppetStringsCurseStrength,
                Boons = new[] { new StatLine(Stat.SummonPower, PactBalance.PuppetStringsBoonSummonPower) },
            },
            new PactDef
            {
                Id = Pact.GraciousRain, Name = new Txt("慈雨の契約", "Gracious Rain"),
                CurseStrength = PactBalance.GraciousRainCurseStrength,
                Boons = new[] { new StatLine(Stat.HealPower, PactBalance.GraciousRainBoonHealPower) },
            },
            new PactDef
            {
                Id = Pact.StagnantSpring, Name = new Txt("淀んだ泉", "Stagnant Spring"),
                CurseStrength = PactBalance.StagnantSpringCurseStrength, ShardMult = PactBalance.StagnantSpringShardMult, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.HourglassLie, Name = new Txt("砂時計の嘘", "Hourglass Lie"),
                CurseStrength = PactBalance.HourglassLieCurseStrength, XpMult = PactBalance.HourglassLieXpMult, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.LongWayRound, Name = new Txt("遠回りの契約", "Long Way Round"),
                CurseStrength = PactBalance.LongWayRoundCurseStrength, XpMult = PactBalance.LongWayRoundXpMult, DropBonus = PactBalance.LongWayRoundDropBonus,
            },
            new PactDef
            {
                Id = Pact.ShellBargain, Name = new Txt("貝殻の約定", "Shell Bargain"),
                CurseStrength = PactBalance.ShellBargainCurseStrength, DropBonus = PactBalance.ShellBargainDropBonus, ShardMult = PactBalance.ShellBargainShardMult,
            },
            new PactDef
            {
                Id = Pact.DeepmirePromise, Name = new Txt("深泥の約束", "Deepmire Promise"),
                CurseStrength = PactBalance.DeepmirePromiseCurseStrength, DoubleDepthBonus = true, TuningOnElite = PactBalance.DeepmirePromiseTuningOnElite,
            },
            new PactDef
            {
                Id = Pact.GildedWound, Name = new Txt("金箔の傷", "Gilded Wound"),
                CurseStrength = PactBalance.GildedWoundCurseStrength,
                Boons = new[] { new StatLine(Stat.AttackFlat, PactBalance.GildedWoundBoonAttackFlat), new StatLine(Stat.PowerFlat, PactBalance.GildedWoundBoonPowerFlat) },
            },
        };

        private static readonly Dictionary<Pact, PactDef> ById = Build();

        private static Dictionary<Pact, PactDef> Build()
        {
            var d = new Dictionary<Pact, PactDef>();
            foreach (var p in All) d[p.Id] = p;
            return d;
        }

        /// <summary>レア度の上がり方を言葉にする（幸運の値の大きさ）。</summary>
        private static string LuckWord(double luck, bool ja)
        {
            if (luck >= 1.2) return ja ? "とても" : "much ";
            if (luck >= 0.7) return ja ? "かなり" : "considerably ";
            if (luck >= 0.45) return "";
            return ja ? "少し" : "slightly ";
        }

        private static string Mult(double m) => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        private static readonly string DepthBonusJa =
            $"確保したときの潜行ボーナスが{PactBalance.DoubleDepthBonusMultiplier}倍になる";
        private static readonly string DepthBonusEn =
            (PactBalance.DoubleDepthBonusMultiplier == 2 ? "double" : $"x{PactBalance.DoubleDepthBonusMultiplier}") + " delve bonus when you secure";

        /// <summary>契約の説明を、数値から作る（代償：…。見返り：…。）。</summary>
        public static string Describe(PactDef d)
        {
            bool ja = Loc.Japanese;
            var gains = new List<string>();
            if (d.DropBonus > 0) gains.Add(ja ? $"遺物が{(int)Math.Round(d.DropBonus * 100)}%多く落ちる" : $"{(int)Math.Round(d.DropBonus * 100)}% more relic drops");
            if (d.Luck > 0) gains.Add(ja ? $"レア度の高い遺物が{LuckWord(d.Luck, true)}出やすくなる" : $"{LuckWord(d.Luck, false)}better relic rarity");
            if (d.ShardMult > 1.0) gains.Add(ja ? $"敵を倒して得る欠片が{Mult(d.ShardMult)}倍になる" : $"x{Mult(d.ShardMult)} shards from kills");
            if (d.XpMult > 1.0) gains.Add(ja ? $"敵を倒して得る経験値が{Mult(d.XpMult)}倍になる" : $"x{Mult(d.XpMult)} experience from kills");
            if (d.TuningOnElite > 0) gains.Add(ja ? $"エリートとボスが調律石を{d.TuningOnElite}つ多く落とす" : $"elites and bosses drop {d.TuningOnElite} extra tuning stone{(d.TuningOnElite == 1 ? "" : "s")}");
            if (d.DoubleDepthBonus) gains.Add(ja ? DepthBonusJa : DepthBonusEn);
            foreach (var b in d.Boons) gains.Add(Content.FormatStat(b.Stat, b.Value));
            string curse = PactDef.StrengthName(d.CurseStrength);
            if (ja)
            {
                string cost = d.CurseStrength >= 3 ? "強い" : d.CurseStrength == 2 ? "中くらいの" : "弱い";
                string echo = d.NoEcho ? $"。全滅したときに戻るはずの欠片（{Workshop.EchoPercent(null)}%）も戻らない" : "";
                return $"代償：{cost}呪いを1つ受ける{echo}。見返り：{string.Join("。", gains)}。";
            }
            string echoEn = d.NoEcho ? $", and the {Workshop.EchoPercent(null)}% shard echoes you would recover on a party wipe are lost instead" : "";
            return $"Cost: a {curse.ToLowerInvariant()} curse{echoEn}. Reward: {string.Join("; ", gains)}.";
        }

        public static PactDef Get(Pact p) => ById.TryGetValue(p, out var d) ? d : null;

        /// <summary>まだ結んでいない契約から、重複なしで count 個を提示する。</summary>
        public static List<Pact> Offer(Rng rng, ICollection<Pact> active, int count = Offered)
        {
            var pool = new List<Pact>();
            foreach (var p in All)
                if (!active.Contains(p.Id)) pool.Add(p.Id);
            var result = new List<Pact>();
            while (result.Count < count && pool.Count > 0)
            {
                int i = rng.Range(0, pool.Count - 1);
                result.Add(pool[i]);
                pool.RemoveAt(i);
            }
            return result;
        }

        /// <summary>結んでいる契約の効果を合算したもの。</summary>
        public sealed class Totals
        {
            public double DropBonus;
            public double Luck;
            public double ShardMult = 1.0;
            public double XpMult = 1.0;
            public int TuningOnElite;
            public bool DoubleDepthBonus;
            public bool NoEcho;
        }

        public static Totals Sum(IEnumerable<Pact> active)
        {
            var t = new Totals();
            if (active == null) return t;
            foreach (var id in active)
            {
                var d = Get(id);
                if (d == null) continue;
                t.DropBonus += d.DropBonus;
                t.Luck += d.Luck;
                t.ShardMult *= d.ShardMult;
                t.XpMult *= d.XpMult;
                t.TuningOnElite += d.TuningOnElite;
                t.DoubleDepthBonus |= d.DoubleDepthBonus;
                t.NoEcho |= d.NoEcho;
            }
            return t;
        }
    }
}
