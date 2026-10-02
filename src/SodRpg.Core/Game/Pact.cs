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
    }

    public sealed class PactDef
    {
        public Pact Id;
        public Txt Name;
        public Txt Description;
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
        public const int Offered = 3;

        public static readonly IReadOnlyList<PactDef> All = new[]
        {
            new PactDef
            {
                Id = Pact.GlassHeart, Name = new Txt("硝子の心臓", "Glass Heart"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：遺物が40%多く落ちます。", "Cost: a mild curse. Reward: 40% more relic drops."), CurseStrength = 1, DropBonus = 0.4,
            },
            new PactDef
            {
                Id = Pact.DullBlade, Name = new Txt("鈍き刃", "Dull Blade"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：レア度の高い遺物が出やすくなります。", "Cost: a mild curse. Reward: better relic rarity."), CurseStrength = 1, Luck = 0.6,
            },
            new PactDef
            {
                Id = Pact.Unguarded, Name = new Txt("無防備", "Unguarded"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：敵を倒して得る欠片が1.5倍になります。", "Cost: a mild curse. Reward: x1.5 shards from kills."), CurseStrength = 1, ShardMult = 1.5,
            },
            new PactDef
            {
                Id = Pact.LeadenFeet, Name = new Txt("重い足", "Leaden Feet"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：敵を倒して得る経験値が1.5倍になります。", "Cost: a mild curse. Reward: x1.5 experience from kills."), CurseStrength = 1, XpMult = 1.5,
            },
            new PactDef
            {
                Id = Pact.Frenzy, Name = new Txt("狂乱", "Frenzy"),
                Description = new Txt("代償：中くらいの呪いを1つ受けます。見返り：攻撃力と魔力が15%上がります。", "Cost: a potent curse. Reward: +15% attack damage and ability power."), CurseStrength = 2,
                Boons = new[] { new StatLine(Stat.AttackPct, 15), new StatLine(Stat.PowerPct, 15) },
            },
            new PactDef
            {
                Id = Pact.CursedHoard, Name = new Txt("呪われた財宝", "Cursed Hoard"),
                Description = new Txt("代償：弱い呪いを1つ受け、全滅したときに欠片が残響として戻らなくなります。見返り：確保したときの潜行ボーナスが2倍になります。", "Cost: a mild curse, and no shard echoes if your party falls. Reward: double delve bonus when you secure."), CurseStrength = 1,
                DoubleDepthBonus = true, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.DryDream, Name = new Txt("乾いた夢", "Dry Dream"),
                Description = new Txt("代償：中くらいの呪いを1つ受けます。見返り：エリートとボスが調律石を1つ多く落とします。", "Cost: a potent curse. Reward: elites and bosses drop 1 extra tuning stone."), CurseStrength = 2, TuningOnElite = 1,
            },
            new PactDef
            {
                Id = Pact.Burden, Name = new Txt("見えざる重荷", "Unseen Burden"),
                Description = new Txt("代償：強い呪いを1つ受けます。見返り：エピック以上の遺物がかなり出やすくなります。", "Cost: a powerful curse. Reward: Epic or better relics become much more common."), CurseStrength = 3, Luck = 1.0,
            },
            new PactDef
            {
                Id = Pact.Glutton, Name = new Txt("暴食", "Gluttony"),
                Description = new Txt("代償：中くらいの呪いを1つ受けます。見返り：遺物が60%多く落ちます。", "Cost: a potent curse. Reward: 60% more relic drops."), CurseStrength = 2, DropBonus = 0.6,
            },
            new PactDef
            {
                Id = Pact.Scholar, Name = new Txt("夜の学徒", "Night Scholar"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：経験値が1.3倍になり、レア度の高い遺物も少し出やすくなります。", "Cost: a mild curse. Reward: x1.3 experience and slightly better rarity."), CurseStrength = 1, XpMult = 1.3, Luck = 0.3,
            },
            new PactDef
            {
                Id = Pact.Gambler, Name = new Txt("賭け師の誓い", "Gambler's Oath"),
                Description = new Txt("代償：中くらいの呪いを1つ受け、全滅したときに欠片が残響として戻らなくなります。見返り：敵を倒して得る欠片が2倍になります。", "Cost: a potent curse, and no shard echoes if your party falls. Reward: x2 shards from kills."), CurseStrength = 2, ShardMult = 2.0, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.AbyssEye, Name = new Txt("深淵の眼", "Eye of the Abyss"),
                Description = new Txt("代償：強い呪いを1つ受けます。見返り：遺物が50%多く落ち、良い物が出やすくなり、エリートとボスが調律石を1つ多く落とします。", "Cost: a powerful curse. Reward: 50% more relics, better rarity, and elites and bosses drop 1 extra tuning stone."), CurseStrength = 3, DropBonus = 0.5, Luck = 0.8, TuningOnElite = 1,
            },
            new PactDef
            {
                Id = Pact.BloodPrice, Name = new Txt("血の代価", "Blood Price"),
                Description = new Txt("代償：中くらいの呪いを1つ受けます。見返り：攻撃速度が15%上がります。", "Cost: a potent curse. Reward: +15% attack speed."), CurseStrength = 2,
                Boons = new[] { new StatLine(Stat.AttackSpeedPct, 15) },
            },
            new PactDef
            {
                Id = Pact.IronOath, Name = new Txt("鉄の誓約", "Iron Oath"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：防御が10上がります。", "Cost: a mild curse. Reward: +10 armor."), CurseStrength = 1,
                Boons = new[] { new StatLine(Stat.Armor, 10) },
            },
            new PactDef
            {
                Id = Pact.HollowCrown, Name = new Txt("虚ろな王冠", "Hollow Crown"),
                Description = new Txt("代償：強い呪いを1つ受けます。見返り：遺物が80%多く落ち、敵を倒して得る欠片が1.5倍になります。", "Cost: a powerful curse. Reward: 80% more relics and x1.5 shards from kills."), CurseStrength = 3, DropBonus = 0.8, ShardMult = 1.5,
            },
            new PactDef
            {
                Id = Pact.ThiefsBargain, Name = new Txt("盗人の取引", "Thief's Bargain"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：欠片が1.3倍になり、遺物も20%多く落ちます。", "Cost: a mild curse. Reward: x1.3 shards and 20% more relics."), CurseStrength = 1, ShardMult = 1.3, DropBonus = 0.2,
            },
            new PactDef
            {
                Id = Pact.Stargazer, Name = new Txt("星見の契約", "Stargazer's Pact"),
                Description = new Txt("代償：弱い呪いを1つ受けます。見返り：レア度の高い遺物が出やすくなります。", "Cost: a mild curse. Reward: better relic rarity."), CurseStrength = 1, Luck = 0.5,
            },
            new PactDef
            {
                Id = Pact.Wanderer, Name = new Txt("放浪者の契約", "Wanderer's Pact"),
                Description = new Txt("代償：中くらいの呪いを1つ受けます。見返り：経験値が1.5倍になり、遺物も30%多く落ちます。", "Cost: a potent curse. Reward: x1.5 experience and 30% more relics."), CurseStrength = 2, XpMult = 1.5, DropBonus = 0.3,
            },
            new PactDef
            {
                Id = Pact.Miser, Name = new Txt("守銭奴", "Miser"),
                Description = new Txt("代償：中くらいの呪いを1つ受けます。見返り：確保したときの潜行ボーナスが2倍になり、欠片も1.3倍になります。", "Cost: a potent curse. Reward: double delve bonus when you secure, and x1.3 shards."), CurseStrength = 2, DoubleDepthBonus = true, ShardMult = 1.3,
            },
            new PactDef
            {
                Id = Pact.BloodMoon, Name = new Txt("血月の契約", "Blood Moon Pact"),
                Description = new Txt("代償：強い呪いを1つ受けます。見返り：攻撃力と魔力が20%上がります。", "Cost: a powerful curse. Reward: +20% attack damage and ability power."), CurseStrength = 3,
                Boons = new[] { new StatLine(Stat.AttackPct, 20), new StatLine(Stat.PowerPct, 20) },
            },
        };

        private static readonly Dictionary<Pact, PactDef> ById = Build();

        private static Dictionary<Pact, PactDef> Build()
        {
            var d = new Dictionary<Pact, PactDef>();
            foreach (var p in All) d[p.Id] = p;
            return d;
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
