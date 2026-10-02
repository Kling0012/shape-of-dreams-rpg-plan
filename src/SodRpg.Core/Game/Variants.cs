using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>夢の変種の、行動に関わる性質（ホストが処理する）。悪夢の性質と組み合わせて使う。</summary>
    [Flags]
    public enum VariantTrait
    {
        None = 0,
        /// <summary>1回で受けるダメージが、最大HPの HitCapPct% までになる。</summary>
        HitCap = 1 << 0,
        /// <summary>倒れると周りの旅人に、最大HPの DeathBurstPct% のダメージ。</summary>
        DeathBurst = 1 << 1,
    }

    /// <summary>
    /// 夢の変種（v1.24）：本体の敵をもとに、名前・色・大きさ・能力・性質を変えた強い敵。
    /// 深度2以降、対応する本体の敵が出たときに、低い確率で変種になる（部屋に1体まで。悪夢化とは重ならない）。
    /// </summary>
    public sealed class VariantDef
    {
        public string Id;
        public Txt Name;
        /// <summary>どんな敵か・どう戦うか（記録タブの図鑑や知らせに使う）。</summary>
        public Txt Description;
        /// <summary>もとにする本体の敵の型名（例：Mon_Forest_Hound）。</summary>
        public string MonsterType;
        public StatLine[] Stats;
        /// <summary>悪夢と同じ行動の性質（結界・棘皮・飢渇・破甲など）。</summary>
        public NightmareAffix Affixes;
        public VariantTrait Traits;
        /// <summary>色（0〜1）と大きさ。全員の画面で同じ見た目にする。</summary>
        public float R, G, B, Scale;
        /// <summary>倒したときの欠片の倍率（%）。</summary>
        public int ShardBonusPct = 100;
    }

    public static class Variants
    {
        public const int MinDepth = 2;
        public const int HitCapPct = 8;
        public const int DeathBurstPct = 20;
        public const float DeathBurstRadius = 4f;

        private static StatLine S(Stat s, int v) => new StatLine(s, v);

        public static readonly IReadOnlyList<VariantDef> All = new[]
        {
            new VariantDef
            {
                Id = "var.corroding_hound", MonsterType = "Mon_Forest_Hound",
                Name = new Txt("蝕む猟犬", "Corroding Hound"),
                Description = new Txt("素早く走り回り、噛みつかれると防御が削られる。距離を取っても追いつかれる。", "Fast and relentless; its bites strip your armor."),
                Stats = new[] { S(Stat.MaxHealthPct, 60), S(Stat.MoveSpeedPct, 50), S(Stat.AttackSpeedPct, 30) },
                Affixes = NightmareAffix.Sundering, R = 0.45f, G = 0.75f, B = 0.3f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.mirror_scarab", MonsterType = "Mon_Forest_Scarab",
                Name = new Txt("鏡殻のスカラベ", "Mirror Scarab"),
                Description = new Txt("鏡のような殻が、強すぎる一撃を受け流す。手数で削るのが近道。", "Its mirrored shell caps every hit; many small hits win."),
                Stats = new[] { S(Stat.MaxHealthPct, 80), S(Stat.Armor, 40) },
                Traits = VariantTrait.HitCap, R = 0.85f, G = 0.9f, B = 1f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.elder_treant", MonsterType = "Mon_Forest_Treant",
                Name = new Txt("古老の樹人", "Elder Treant"),
                Description = new Txt("分厚い樹皮の障壁をまとい、殴った相手に棘を返す。", "Shielded by thick bark that thorns its attackers."),
                Stats = new[] { S(Stat.MaxHealthPct, 250), S(Stat.Armor, 30) },
                Affixes = NightmareAffix.Warded | NightmareAffix.Thorned, R = 0.25f, G = 0.55f, B = 0.25f, Scale = 1.3f,
            },
            new VariantDef
            {
                Id = "var.blood_bat", MonsterType = "Mon_DarkCave_CaveBat",
                Name = new Txt("血吸い蝙蝠", "Bloodsucker Bat"),
                Description = new Txt("噛みつくたびに傷が癒える。放っておくと倒しにくくなる。", "Heals with every bite; deal with it quickly."),
                Stats = new[] { S(Stat.MaxHealthPct, 80), S(Stat.AttackSpeedPct, 40), S(Stat.MoveSpeedPct, 30) },
                Affixes = NightmareAffix.Ravenous, R = 0.9f, G = 0.2f, B = 0.25f, Scale = 1.25f,
            },
            new VariantDef
            {
                Id = "var.abyss_oppressor", MonsterType = "Mon_DarkCave_Oppressor",
                Name = new Txt("深淵の圧制者", "Abyssal Oppressor"),
                Description = new Txt("障壁に守られ、重い一撃で防御を砕く。正面から受けると危ない。", "Shielded, and its heavy blows shatter your armor."),
                Stats = new[] { S(Stat.MaxHealthPct, 200), S(Stat.AttackPct, 30) },
                Affixes = NightmareAffix.Sundering | NightmareAffix.Warded, R = 0.45f, G = 0.2f, B = 0.6f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.hollow_gunner", MonsterType = "Mon_Despair_WretchedArtillery",
                Name = new Txt("虚ろな砲手", "Hollow Gunner"),
                Description = new Txt("遠くから重い砲撃を撃ち込み、障壁で身を守る。近づいて崩すこと。", "Shelling from afar behind a barrier; close in to break it."),
                Stats = new[] { S(Stat.MaxHealthPct, 200), S(Stat.AttackPct, 40) },
                Affixes = NightmareAffix.Warded, R = 0.6f, G = 0.35f, B = 0.9f, Scale = 1.25f,
            },
            new VariantDef
            {
                Id = "var.blazing_martyr", MonsterType = "Mon_Despair_UnstableRat",
                Name = new Txt("焔の殉教者", "Blazing Martyr"),
                Description = new Txt("倒れると大きく爆ぜ、近くにいた者を焼く。とどめは離れて。", "Explodes on death; finish it from a distance."),
                Stats = new[] { S(Stat.MaxHealthPct, 60), S(Stat.MoveSpeedPct, 40) },
                Traits = VariantTrait.DeathBurst, R = 1f, G = 0.55f, B = 0.15f, Scale = 1.3f,
            },
            new VariantDef
            {
                Id = "var.ink_scholar", MonsterType = "Mon_Ink_GhostBlade",
                Name = new Txt("吸命の学徒", "Lifedrain Scholar"),
                Description = new Txt("斬りつけた分だけ命を吸う。長引くほど不利になる。", "Drains life with each cut; the longer the fight, the worse."),
                Stats = new[] { S(Stat.MaxHealthPct, 80), S(Stat.AttackPct, 25) },
                Affixes = NightmareAffix.Ravenous, R = 0.35f, G = 0.2f, B = 0.45f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.thousand_arrows", MonsterType = "Mon_Ink_Archer",
                Name = new Txt("千本射ち", "Thousand Arrows"),
                Description = new Txt("矢継ぎ早に射かけ、当たるたびに防御を削る。", "Rapid volleys that strip your armor."),
                Stats = new[] { S(Stat.MaxHealthPct, 60), S(Stat.AttackSpeedPct, 50), S(Stat.AttackPct, 20) },
                Affixes = NightmareAffix.Sundering, R = 0.3f, G = 0.45f, B = 0.8f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.molten_core", MonsterType = "Mon_LavaLand_FireElemental",
                Name = new Txt("熔けた核", "Molten Core"),
                Description = new Txt("触れれば焼け、倒れれば爆ぜる。近づくなら覚悟を。", "Burns those who strike it, and bursts when it falls."),
                Stats = new[] { S(Stat.MaxHealthPct, 120) },
                Affixes = NightmareAffix.Thorned, Traits = VariantTrait.DeathBurst, R = 1f, G = 0.4f, B = 0.1f, Scale = 1.25f,
            },
            new VariantDef
            {
                Id = "var.star_stalker", MonsterType = "Mon_Sky_Baam",
                Name = new Txt("星追いの影", "Star Stalker"),
                Description = new Txt("影のように素早く迫り、防御を削り取る。", "Closes in like a shadow and strips your armor."),
                Stats = new[] { S(Stat.MaxHealthPct, 80), S(Stat.MoveSpeedPct, 40), S(Stat.AttackPct, 30) },
                Affixes = NightmareAffix.Sundering, R = 0.25f, G = 0.25f, B = 0.4f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.frost_alpha", MonsterType = "Mon_SnowMountain_SnowWolf",
                Name = new Txt("氷原の群れ長", "Frost Alpha"),
                Description = new Txt("群れを率いる大きな狼。氷の障壁をまとっている。", "A pack leader wrapped in an icy barrier."),
                Stats = new[] { S(Stat.MaxHealthPct, 150), S(Stat.AttackPct, 30), S(Stat.MoveSpeedPct, 20) },
                Affixes = NightmareAffix.Warded, R = 0.6f, G = 0.85f, B = 1f, Scale = 1.35f,
            },
            new VariantDef
            {
                Id = "var.shard_devourer", MonsterType = "Mon_GoldenLizard_ElusiveLizard",
                Name = new Txt("欠片喰らい", "Shard Devourer"),
                Description = new Txt("夢の欠片を腹いっぱいに溜め込んだトカゲ。逃げ足は速いが、倒せば欠片がたくさん出る。", "A lizard gorged on dream shards. Catch it for a big payout."),
                Stats = new[] { S(Stat.MaxHealthPct, 200), S(Stat.MoveSpeedPct, 30) },
                R = 1f, G = 0.85f, B = 0.3f, Scale = 1.2f, ShardBonusPct = 300,
            },
        };

        private static readonly Dictionary<string, VariantDef> ById = new Dictionary<string, VariantDef>();
        private static readonly Dictionary<string, VariantDef> ByType = new Dictionary<string, VariantDef>();

        static Variants()
        {
            foreach (var v in All)
            {
                ById[v.Id] = v;
                ByType[v.MonsterType] = v;
            }
        }

        public static VariantDef Get(string id) => id != null && ById.TryGetValue(id, out var v) ? v : null;

        public static VariantDef ForMonsterType(string typeName) => typeName != null && ByType.TryGetValue(typeName, out var v) ? v : null;

        /// <summary>変種になる確率。深度2で6%、深度1ごとに+2%。</summary>
        public static double Chance(int depth)
        {
            depth = Loot.ClampHeat(depth);
            return depth < MinDepth ? 0 : 0.06 + 0.02 * (depth - MinDepth);
        }

        /// <summary>この敵を変種にするかを抽選する。すでに部屋に変種がいる、または対応する変種がない場合は null。</summary>
        public static VariantDef Roll(Rng rng, string monsterType, int depth, bool roomAlreadyHasVariant)
        {
            if (roomAlreadyHasVariant) return null;
            var v = ForMonsterType(monsterType);
            if (v == null) return null;
            return rng.Chance(Chance(depth)) ? v : null;
        }

        /// <summary>頭上の名札。例：「夢の変種・蝕む猟犬」。</summary>
        public static string Label(VariantDef v) => v == null ? "" : Loc.T("夢の変種・", "Dream Variant: ") + v.Name;

        /// <summary>変種の撃破を、何の格として抽選するか（悪夢と同じく一段上）。</summary>
        public static MonsterTier RewardTier(MonsterTier tier) => Nightmares.RewardTier(tier);
    }
}
