using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>悪夢の契約。深く潜るときに1つ選べる。次に確保するまで重なって効き続ける。</summary>
    public enum Pact
    {
        None = 0,
        /// <summary>硝子の心臓：最大HP-15% / 遺物ドロップ率+40%</summary>
        GlassHeart = 1,
        /// <summary>鈍き刃：攻撃速度-12% / レア度の幸運+0.6</summary>
        DullBlade = 2,
        /// <summary>無防備：防御-15 / 欠片×1.5</summary>
        Unguarded = 3,
        /// <summary>重い足：移動速度-10% / 経験値×1.5</summary>
        LeadenFeet = 4,
        /// <summary>狂乱：防御-25 / 攻撃力・魔力+15%（純粋な戦闘の賭け）</summary>
        Frenzy = 5,
        /// <summary>呪われた財宝：確保時の深度ボーナス×2 / 全滅時の残響なし</summary>
        CursedHoard = 6,
        /// <summary>乾いた夢：HP回復-3/秒・スキル加速-10 / 調律石の入手+1（エリート・ボス）</summary>
        DryDream = 7,
        /// <summary>見えざる重荷：最大HP-8%・防御-8 / エピック以上の確率が上がる（幸運+1.0）</summary>
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
    }

    public static class Pacts
    {
        public const int Offered = 3;

        public static readonly IReadOnlyList<PactDef> All = new[]
        {
            new PactDef
            {
                Id = Pact.GlassHeart, Name = new Txt("硝子の心臓", "Glass Heart"),
                Description = new Txt("最大HP-15% ／ 遺物ドロップ率+40%", "-15% max health / +40% relic drop rate"),
                Penalties = new[] { new StatLine(Stat.MaxHealthPct, -15) }, DropBonus = 0.4,
            },
            new PactDef
            {
                Id = Pact.DullBlade, Name = new Txt("鈍き刃", "Dull Blade"),
                Description = new Txt("攻撃速度-12% ／ レア度が上がりやすい", "-12% attack speed / better rarity"),
                Penalties = new[] { new StatLine(Stat.AttackSpeedPct, -12) }, Luck = 0.6,
            },
            new PactDef
            {
                Id = Pact.Unguarded, Name = new Txt("無防備", "Unguarded"),
                Description = new Txt("防御-15 ／ 欠片×1.5", "-15 armor / x1.5 shards"),
                Penalties = new[] { new StatLine(Stat.Armor, -15) }, ShardMult = 1.5,
            },
            new PactDef
            {
                Id = Pact.LeadenFeet, Name = new Txt("重い足", "Leaden Feet"),
                Description = new Txt("移動速度-10% ／ 経験値×1.5", "-10% move speed / x1.5 xp"),
                Penalties = new[] { new StatLine(Stat.MoveSpeedPct, -10) }, XpMult = 1.5,
            },
            new PactDef
            {
                Id = Pact.Frenzy, Name = new Txt("狂乱", "Frenzy"),
                Description = new Txt("防御-25 ／ 攻撃力・魔力+15%", "-25 armor / +15% attack and ability power"),
                Penalties = new[] { new StatLine(Stat.Armor, -25) },
                Boons = new[] { new StatLine(Stat.AttackPct, 15), new StatLine(Stat.PowerPct, 15) },
            },
            new PactDef
            {
                Id = Pact.CursedHoard, Name = new Txt("呪われた財宝", "Cursed Hoard"),
                Description = new Txt("確保時の深度ボーナス×2 ／ 全滅すると残響なし", "x2 depth bonus on secure / no echoes on defeat"),
                DoubleDepthBonus = true, NoEcho = true,
            },
            new PactDef
            {
                Id = Pact.DryDream, Name = new Txt("乾いた夢", "Dry Dream"),
                Description = new Txt("HP回復-3/秒・スキル加速-10 ／ エリートとボスが調律石+1", "-3 health regen, -10 haste / elites and bosses drop +1 tuning"),
                Penalties = new[] { new StatLine(Stat.HealthRegen, -3), new StatLine(Stat.Haste, -10) }, TuningOnElite = 1,
            },
            new PactDef
            {
                Id = Pact.Burden, Name = new Txt("見えざる重荷", "Unseen Burden"),
                Description = new Txt("最大HP-8%・防御-8 ／ エピック以上が出やすい", "-8% max health, -8 armor / much better rarity"),
                Penalties = new[] { new StatLine(Stat.MaxHealthPct, -8), new StatLine(Stat.Armor, -8) }, Luck = 1.0,
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
