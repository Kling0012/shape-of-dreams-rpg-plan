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
                Description = new Txt("呪い（弱）を受ける ／ 遺物ドロップ率+40%", "Take a Mild curse / +40% relic drop rate"), CurseStrength = 1, DropBonus = 0.4,
            },
            new PactDef
            {
                Id = Pact.DullBlade, Name = new Txt("鈍き刃", "Dull Blade"),
                Description = new Txt("呪い（弱）を受ける ／ レア度が上がりやすい", "Take a Mild curse / better rarity"), CurseStrength = 1, Luck = 0.6,
            },
            new PactDef
            {
                Id = Pact.Unguarded, Name = new Txt("無防備", "Unguarded"),
                Description = new Txt("呪い（弱）を受ける ／ 撃破で得る欠片×1.5", "Take a Mild curse / x1.5 shards from kills"), CurseStrength = 1, ShardMult = 1.5,
            },
            new PactDef
            {
                Id = Pact.LeadenFeet, Name = new Txt("重い足", "Leaden Feet"),
                Description = new Txt("呪い（弱）を受ける ／ 撃破経験値×1.5", "Take a Mild curse / x1.5 xp from kills"), CurseStrength = 1, XpMult = 1.5,
            },
            new PactDef
            {
                Id = Pact.Frenzy, Name = new Txt("狂乱", "Frenzy"),
                Description = new Txt("呪い（中）を受ける ／ 攻撃力・魔力+15%", "Take a Potent curse / +15% attack and ability power"), CurseStrength = 2,
                Boons = new[] { new StatLine(Stat.AttackPct, 15), new StatLine(Stat.PowerPct, 15) },
            },
            new PactDef
            {
                Id = Pact.CursedHoard, Name = new Txt("呪われた財宝", "Cursed Hoard"),
                Description = new Txt("呪い（弱）を受ける ／ 確保時の潜行ボーナス×2・全滅すると残響なし", "Take a Mild curse / x2 delve bonus on secure, no echoes on defeat"), CurseStrength = 1,
                DoubleDepthBonus = true, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.DryDream, Name = new Txt("乾いた夢", "Dry Dream"),
                Description = new Txt("呪い（中）を受ける ／ エリートとボスが調律石+1", "Take a Potent curse / elites and bosses drop +1 tuning"), CurseStrength = 2, TuningOnElite = 1,
            },
            new PactDef
            {
                Id = Pact.Burden, Name = new Txt("見えざる重荷", "Unseen Burden"),
                Description = new Txt("呪い（強）を受ける ／ エピック以上がかなり出やすい", "Take a Powerful curse / much better rarity"), CurseStrength = 3, Luck = 1.0,
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
