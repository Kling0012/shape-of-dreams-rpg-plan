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
        /// <summary>倒れると周りの旅人に、最大HPの DeathBurstPct% のダメージ（夢の圧力・潜行のダメージ倍率の影響を受ける）。</summary>
        DeathBurst = 1 << 1,
        /// <summary>HPの節目を下回ると一定時間、被ダメージを上乗せ。新しい守りより優先する。</summary>
        PhaseOpening = 1 << 2,

    }

    /// <summary>
    /// 変種の弱点・耐性（v1.29 wave 2）。どの性質も特定の型を報酬づけるためのもので、
    /// 耐性は1ヒットあたり表の上限で打ち止め（どの型も無効化しない）。ホストが takenDamageProcessor で処理する。
    /// 元素の判定は「その元素のダメージ（DamageData.elemental）」か「その元素がかかっている間
    /// （EntityStatus の fireStack/hasCold/lightStack/darkStack）」のどちらか（.ref/dump で両方確認済み）。
    /// </summary>
    [Flags]
    public enum VariantTag
    {
        None = 0,
        /// <summary>火に弱い：火のダメージ、または燃えている間は被ダメージを上乗せ。</summary>
        WeakFire = 1 << 0,
        /// <summary>冷気に弱い：冷気のダメージ、または凍ている間は被ダメージを上乗せ。</summary>
        WeakCold = 1 << 1,
        /// <summary>光に弱い：光のダメージ、または光を浴びている間は被ダメージを上乗せ。</summary>
        WeakLight = 1 << 2,
        /// <summary>闇に弱い：闇のダメージ、または闇を浴びている間は被ダメージを上乗せ。</summary>
        WeakDark = 1 << 3,
        /// <summary>障壁を割る：障壁を持つ相手への与ダメージと、障壁を持つ攻撃者からの被ダメージを上乗せ。</summary>
        ShieldBreaker = 1 << 4,
        /// <summary>召喚獣を狩る：召喚獣への与ダメージと、召喚獣からの被ダメージを上乗せ。</summary>
        SummonHunter = 1 << 5,
        /// <summary>一定数以上の光で脆い：光のダメージで被ダメージを上乗せ（スタックは決して消費しない）。</summary>
        LightEater = 1 << 6,
        /// <summary>堅甲：記憶以外のダメージ（通常攻撃・固有効果・仕掛けなど）の被ダメージを軽減。記憶は通常どおり。</summary>
        Armored = 1 << 7,
        /// <summary>記憶を弾く：記憶のダメージの被ダメージを軽減。通常攻撃は通常どおり。</summary>
        Spellward = 1 << 8,
    }
    /// <summary>
    /// 夢の変種（v1.24）：本体の敵をもとに、名前・色・大きさ・能力・性質を変えた強い敵。
    /// 表の最小深度以降、対応する本体の敵が出たときに、低い確率で変種になる（部屋に1体まで。悪夢化とは重ならない）。
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
        /// <summary>弱点・耐性（v1.29 wave 2）。なければ None。</summary>
        public VariantTag Tags;
        /// <summary>色（0〜1）と大きさ。全員の画面で同じ見た目にする。</summary>
        public float R, G, B, Scale;
        /// <summary>倒したときの欠片の倍率（%）。</summary>
        public int ShardBonusPct = Variants.DefaultShardBonusPct;
    }

    public static class Variants
    {
        public const int MinDepth = MonstersBalance.MinDepth;
        public const int HitCapPct = MonstersBalance.HitCapPct;
        public const int DeathBurstPct = MonstersBalance.DeathBurstPct;
        public const float DeathBurstRadius = MonstersBalance.DeathBurstRadius;

        public const double BaseChance = MonstersBalance.BaseChance;
        public const double ChancePerDepth = MonstersBalance.ChancePerDepth;
        public const int DefaultShardBonusPct = MonstersBalance.DefaultShardBonusPct;
        public const int DevourerShardBonusPct = MonstersBalance.DevourerShardBonusPct;
        public const int BonusShards = MonstersBalance.BonusShards;

        public static readonly IReadOnlyList<VariantDef> All = new[]
        {
            new VariantDef
            {
                Id = "var.corroding_hound", MonsterType = "Mon_Forest_Hound",
                Name = new Txt("蝕む猟犬", "Corroding Hound"),
                Description = new Txt("素早く走り回り、噛みつかれると防御が削られる。距離を取っても追いつかれる。", "Fast and relentless; its bites strip your armor."),
                Stats = MonstersBalance.CreateCorrodingHoundStats(),
                Affixes = NightmareAffix.Sundering, R = 0.45f, G = 0.75f, B = 0.3f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.mirror_scarab", MonsterType = "Mon_Forest_Scarab",
                Name = new Txt("鏡殻のスカラベ", "Mirror Scarab"),
                Description = new Txt("鏡のような殻が、強すぎる一撃を受け流す。手数で削るのが近道。", "Its mirrored shell caps every hit; many small hits win."),
                Stats = MonstersBalance.CreateMirrorScarabStats(),
                Traits = VariantTrait.HitCap, R = 0.85f, G = 0.9f, B = 1f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.elder_treant", MonsterType = "Mon_Forest_Treant",
                Name = new Txt("古老の樹人", "Elder Treant"),
                Description = new Txt($"分厚い樹皮の障壁をまとい、旅人の攻撃に棘を返す。火に弱い：火のダメージ、または燃えている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"Shielded by thick bark that thorns the travelers who attack it. Weak to fire: takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from fire damage or while burning."),
                Stats = MonstersBalance.CreateElderTreantStats(),
                Affixes = NightmareAffix.Warded | NightmareAffix.Thorned, Tags = VariantTag.WeakFire, R = 0.25f, G = 0.55f, B = 0.25f, Scale = 1.3f,
            },
            new VariantDef
            {
                Id = "var.blood_bat", MonsterType = "Mon_DarkCave_CaveBat",
                Name = new Txt("血吸い蝙蝠", "Bloodsucker Bat"),
                Description = new Txt("噛みつくたびに傷が癒える。放っておくと倒しにくくなる。", "Heals with every bite; deal with it quickly."),
                Stats = MonstersBalance.CreateBloodBatStats(),
                Affixes = NightmareAffix.Ravenous, R = 0.9f, G = 0.2f, B = 0.25f, Scale = 1.25f,
            },
            new VariantDef
            {
                Id = "var.abyss_oppressor", MonsterType = "Mon_DarkCave_Oppressor",
                Name = new Txt("深淵の圧制者", "Abyssal Oppressor"),
                Description = new Txt($"障壁に守られ、重い一撃で防御を砕く。障壁を割る：障壁を持つ相手へのダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}%。攻撃者が障壁を持っている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"Shielded, and its heavy blows shatter your armor. Breaks shields: deals {MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}% more to shielded targets, but takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more while its attacker holds a shield."),
                Stats = MonstersBalance.CreateAbyssOppressorStats(),
                Affixes = NightmareAffix.Sundering | NightmareAffix.Warded, Tags = VariantTag.ShieldBreaker, R = 0.45f, G = 0.2f, B = 0.6f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.hollow_gunner", MonsterType = "Mon_Despair_WretchedArtillery",
                Name = new Txt("虚ろな砲手", "Hollow Gunner"),
                Description = new Txt($"遠くから重い砲撃を撃ち込み、障壁で身を守る。記憶を弾く：記憶のダメージを{MonsterBehavior.Number((decimal)MonsterBehavior.MaxTagResistance * 100m)}%軽減。通常攻撃は軽減しない。", $"Shelling from afar behind a barrier. Warded against memories: reduces memory damage by {MonsterBehavior.Number((decimal)MonsterBehavior.MaxTagResistance * 100m)}%; basic attacks are unaffected."),
                Stats = MonstersBalance.CreateHollowGunnerStats(),
                Affixes = NightmareAffix.Warded, Tags = VariantTag.Spellward, R = 0.6f, G = 0.35f, B = 0.9f, Scale = 1.25f,
            },
            new VariantDef
            {
                Id = "var.blazing_martyr", MonsterType = "Mon_Despair_UnstableRat",
                Name = new Txt("焔の殉教者", "Blazing Martyr"),
                Description = new Txt($"倒れると大きく爆ぜ、近くにいた者を最大HPの{DeathBurstPct}%ほど焼く（夢の圧力・潜行で増減）。とどめは離れて。", $"Explodes on death for about {DeathBurstPct}% of nearby travelers' max HP (scaled by dream pressure and delve); finish it from a distance."),
                Stats = MonstersBalance.CreateBlazingMartyrStats(),
                Traits = VariantTrait.DeathBurst, R = 1f, G = 0.55f, B = 0.15f, Scale = 1.3f,
            },
            new VariantDef
            {
                Id = "var.ink_scholar", MonsterType = "Mon_Ink_GhostBlade",
                Name = new Txt("吸命の学徒", "Lifedrain Scholar"),
                Description = new Txt("斬りつけた分だけ命を吸う。長引くほど不利になる。", "Drains life with each cut; the longer the fight, the worse."),
                Stats = MonstersBalance.CreateInkScholarStats(),
                Affixes = NightmareAffix.Ravenous, R = 0.35f, G = 0.2f, B = 0.45f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.thousand_arrows", MonsterType = "Mon_Ink_Archer",
                Name = new Txt("千本射ち", "Thousand Arrows"),
                Description = new Txt("矢継ぎ早に射かけ、当たるたびに防御を削る。", "Rapid volleys that strip your armor."),
                Stats = MonstersBalance.CreateThousandArrowsStats(),
                Affixes = NightmareAffix.Sundering, R = 0.3f, G = 0.45f, B = 0.8f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.molten_core", MonsterType = "Mon_LavaLand_FireElemental",
                Name = new Txt("熔けた核", "Molten Core"),
                Description = new Txt($"触れれば焼け、倒れれば最大HPの{DeathBurstPct}%ほどの爆発（夢の圧力・潜行で増減）を起こす。冷気に弱い：冷気のダメージ、または凍ている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"Burns those who strike it, and bursts for about {DeathBurstPct}% of nearby travelers' max HP when it falls (scaled by dream pressure and delve). Weak to cold: takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from cold damage or while chilled."),
                Stats = MonstersBalance.CreateMoltenCoreStats(),
                Affixes = NightmareAffix.Thorned, Traits = VariantTrait.DeathBurst, Tags = VariantTag.WeakCold, R = 1f, G = 0.4f, B = 0.1f, Scale = 1.25f,
            },
            new VariantDef
            {
                Id = "var.star_stalker", MonsterType = "Mon_Sky_Baam",
                Name = new Txt("星追いの影", "Star Stalker"),
                Description = new Txt("影のように素早く迫り、防御を削り取る。", "Closes in like a shadow and strips your armor."),
                Stats = MonstersBalance.CreateStarStalkerStats(),
                Affixes = NightmareAffix.Sundering, R = 0.25f, G = 0.25f, B = 0.4f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.frost_alpha", MonsterType = "Mon_SnowMountain_SnowWolf",
                Name = new Txt("氷原の群れ長", "Frost Alpha"),
                Description = new Txt("群れを率いる大きな狼。氷の障壁をまとっている。", "A pack leader wrapped in an icy barrier."),
                Stats = MonstersBalance.CreateFrostAlphaStats(),
                Affixes = NightmareAffix.Warded, R = 0.6f, G = 0.85f, B = 1f, Scale = 1.35f,
            },
            new VariantDef
            {
                Id = "var.shard_devourer", MonsterType = "Mon_GoldenLizard_ElusiveLizard",
                Name = new Txt("欠片喰らい", "Shard Devourer"),
                Description = new Txt("夢の欠片を腹いっぱいに溜め込んだトカゲ。逃げ足は速いが、倒せば欠片がたくさん出る。", "A lizard gorged on dream shards. Catch it for a big payout."),
                Stats = MonstersBalance.CreateShardDevourerStats(),
                R = 1f, G = 0.85f, B = 0.3f, Scale = 1.2f, ShardBonusPct = DevourerShardBonusPct,
            },
            new VariantDef
            {
                Id = "var.mist_spitter", MonsterType = "Mon_Forest_SpiderSpitter",
                Name = new Txt("霞吐き蜘蛛", "Mist Spitter"),
                Description = new Txt($"{MonsterBehavior.Number((decimal)MonsterBehavior.Range)}mより遠い攻撃を{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減。吐き出しと後退を避けて{MonsterBehavior.Number((decimal)MonsterBehavior.Range)}m以内へ。", $"Reduces hits from beyond {MonsterBehavior.Number((decimal)MonsterBehavior.Range)}m by {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%; dodge its spit and backdash to close within {MonsterBehavior.Number((decimal)MonsterBehavior.Range)}m."),
                Stats = MonstersBalance.CreateMistSpitterStats(),
                Affixes = NightmareAffix.Veiled, R = 0.65f, G = 0.85f, B = 0.7f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.brood_warden", MonsterType = "Mon_Forest_SpiderWarrior",
                Name = new Txt("群巣の番蜘蛛", "Brood Warden"),
                Description = new Txt($"同区画{MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m以内の活動中の味方で被ダメージ-{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%。{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒予告後一度だけ最寄りの非ボス味方にHP{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}%の障壁を{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}秒（重複不可）。召喚獣を狩る：召喚獣へのダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}%、召喚獣からの被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"An awake ally within {MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m in the same section grants {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% reduction. After a {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s warning, once shields the nearest nonboss ally for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}% HP for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}s (nonstacking). Hunts summons: deals {MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}% more to summons but takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from summon damage."),
                Stats = MonstersBalance.CreateBroodWardenStats(),
                Affixes = NightmareAffix.Packbound | NightmareAffix.Beacon, Tags = VariantTag.SummonHunter, R = 0.45f, G = 0.7f, B = 0.25f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.hollow_elemental", MonsterType = "Mon_DarkCave_DarkElemental",
                Name = new Txt("空洞の影精", "Hollow Shade"),
                Description = new Txt($"{MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m未満の攻撃を{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減。光に弱い：光のダメージ、または光を浴びている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"Reduces hits from within {MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m by {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%. Weak to light: takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from light damage or while illuminated."),
                Stats = MonstersBalance.CreateHollowElementalStats(),
                Affixes = NightmareAffix.Hollow, Tags = VariantTag.WeakLight, R = 0.4f, G = 0.3f, B = 0.7f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.flicker_olm", MonsterType = "Mon_DarkCave_NightOlm",
                Name = new Txt("明滅する洞竜", "Flicker Olm"),
                Description = new Txt($"最初の{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒は無防備、以後{MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}秒間{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減と{MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}秒間無防備。光{MonsterBehavior.LightEaterMinStacks}以上で脆い：光のダメージで被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%（スタックは消さない）。", $"Open for the first {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s, then cycles {MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}s of {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% reduction and {MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}s open. Frail at {MonsterBehavior.LightEaterMinStacks}+ light: takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from light damage while carrying {MonsterBehavior.LightEaterMinStacks} or more light stacks (never consumed)."),
                Stats = MonstersBalance.CreateFlickerOlmStats(),
                Affixes = NightmareAffix.Pulsing, Tags = VariantTag.LightEater, R = 0.3f, G = 0.8f, B = 0.8f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.timorous_displacer", MonsterType = "Mon_Despair_Displacer",
                Name = new Txt("臆病な跳躍獣", "Timorous Displacer"),
                Description = new Txt($"未被弾時は移動+{MonsterBehavior.Number((decimal)MonsterBehavior.UnhitMovementPct)}%、被弾後{MonsterBehavior.Number((decimal)MonsterBehavior.HitSlowSeconds)}秒は-{MonsterBehavior.Number(-(decimal)MonsterBehavior.HitMovementPct)}%。一撃を当てて転移先を追う。", $"Moves {MonsterBehavior.Number((decimal)MonsterBehavior.UnhitMovementPct)}% faster while unhit, but {MonsterBehavior.Number(-(decimal)MonsterBehavior.HitMovementPct)}% slower for {MonsterBehavior.Number((decimal)MonsterBehavior.HitSlowSeconds)}s after damage. Tag it, then pursue through its blink."),
                Stats = MonstersBalance.CreateTimorousDisplacerStats(),
                Affixes = NightmareAffix.Skittish, R = 0.7f, G = 0.35f, B = 0.85f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.overreaching_bug", MonsterType = "Mon_Despair_DreadBug",
                Name = new Txt("大振りの恐虫", "Overreaching Dreadbug"),
                Description = new Txt($"詠唱中は{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減、攻撃発射後{MonsterBehavior.Number((decimal)MonsterBehavior.RecoverySeconds)}秒は被ダメージ+{MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}%。跳躍や打撃を避けて反撃。", $"Reduces damage by {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% while channeling; takes {MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}% more for {MonsterBehavior.Number((decimal)MonsterBehavior.RecoverySeconds)}s after firing. Dodge its leap or strike, then punish recovery."),
                Stats = MonstersBalance.CreateOverreachingBugStats(),
                Affixes = NightmareAffix.Committed, R = 0.8f, G = 0.4f, B = 0.45f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.last_thaw", MonsterType = "Mon_SnowMountain_IceElemental",
                Name = new Txt("薄氷の核", "Last Thaw"),
                Description = new Txt($"HP{MonsterBehavior.Number((decimal)MonsterBehavior.LastStandHealthRatio * 100m)}%以下で{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒予告後、一度だけHP{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}%の障壁を{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}秒。予告中に集中攻撃するか殻を割る。", $"At {MonsterBehavior.Number((decimal)MonsterBehavior.LastStandHealthRatio * 100m)}% HP, a {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s warning precedes a once-per-life {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}% HP shield for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}s. Save burst for the warning or break its shell."),
                Stats = MonstersBalance.CreateLastThawStats(),
                Affixes = NightmareAffix.LastStand, R = 0.7f, G = 0.9f, B = 1f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.rime_sentinel", MonsterType = "Mon_SnowMountain_VikingWarrior",
                Name = new Txt("霜盾の番兵", "Rime Sentinel"),
                Description = new Txt($"正面{MonsterBehavior.Number((decimal)(Math.Acos(MonsterBehavior.FacingDot) * 360.0 / Math.PI))}度からの被ダメージを{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減。記憶以外に堅い：記憶以外のダメージ（通常攻撃・固有効果・仕掛けなど）を{MonsterBehavior.Number((decimal)MonsterBehavior.MaxTagResistance * 100m)}%軽減。記憶のダメージは通常どおり。", $"Reduces damage from its frontal {MonsterBehavior.Number((decimal)(Math.Acos(MonsterBehavior.FacingDot) * 360.0 / Math.PI))}-degree cone by {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%. Armored against non-memory damage: reduces damage not from memories (basic attacks, innate effects, hazards and so on) by {MonsterBehavior.Number((decimal)MonsterBehavior.MaxTagResistance * 100m)}%; memory damage passes through normally."),
                Stats = MonstersBalance.CreateRimeSentinelStats(),
                Affixes = NightmareAffix.Facing, Tags = VariantTag.Armored, R = 0.45f, G = 0.7f, B = 0.95f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.furnace_ram", MonsterType = "Mon_LavaLand_Magmadon",
                Name = new Txt("炉殻の突進獣", "Furnace Ram"),
                Description = new Txt($"正面{MonsterBehavior.Number((decimal)(Math.Acos(MonsterBehavior.FacingDot) * 360.0 / Math.PI))}度と詠唱中は{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減（合計上限{MonsterBehavior.Number((decimal)MonsterBehavior.MaxGuardReduction * 100m)}%）。発射後{MonsterBehavior.Number((decimal)MonsterBehavior.RecoverySeconds)}秒は被ダメージ+{MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}%が優先。突進を横へ避けて反撃。", $"Frontal {MonsterBehavior.Number((decimal)(Math.Acos(MonsterBehavior.FacingDot) * 360.0 / Math.PI))}-degree hits and channeling each grant {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}% reduction, capped at {MonsterBehavior.Number((decimal)MonsterBehavior.MaxGuardReduction * 100m)}%. After firing, {MonsterBehavior.Number((decimal)MonsterBehavior.RecoverySeconds)}s of {MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}% extra damage overrides guards. Flank the charge and punish recovery."),
                Stats = MonstersBalance.CreateFurnaceRamStats(),
                Affixes = NightmareAffix.Facing | NightmareAffix.Committed, R = 1f, G = 0.45f, B = 0.2f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.ember_mender", MonsterType = "Mon_LavaLand_InfernoSpider",
                Name = new Txt("熾火の繕い蜘蛛", "Ember Mender"),
                Description = new Txt($"{MonsterBehavior.Number((decimal)MonsterBehavior.HealDelaySeconds)}秒無傷なら毎秒HP{MonsterBehavior.Number((decimal)MonsterBehavior.HealPctPerSecond)}%回復、一生で{MonsterBehavior.Number((decimal)MonsterBehavior.HealBudgetPct)}%まで。満タンでは消費しない。溶岩跳躍の合間に当て続ける。", $"After {MonsterBehavior.Number((decimal)MonsterBehavior.HealDelaySeconds)}s without damage heals {MonsterBehavior.Number((decimal)MonsterBehavior.HealPctPerSecond)}% HP per second, capped at {MonsterBehavior.Number((decimal)MonsterBehavior.HealBudgetPct)}% per life with no budget spent at full HP. Keep landing hits between lava jumps."),
                Stats = MonstersBalance.CreateEmberMenderStats(),
                Affixes = NightmareAffix.Recuperating, R = 0.95f, G = 0.65f, B = 0.3f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.mist_tiger", MonsterType = "Mon_Ink_Tiger",
                Name = new Txt("霞走りの虎", "Mist Tiger"),
                Description = new Txt($"{MonsterBehavior.Number((decimal)MonsterBehavior.Range)}mより遠い攻撃を{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減。移動+{MonsterBehavior.Number((decimal)MonsterBehavior.UnhitMovementPct)}%、被弾後{MonsterBehavior.Number((decimal)MonsterBehavior.HitSlowSeconds)}秒は-{MonsterBehavior.Number(-(decimal)MonsterBehavior.HitMovementPct)}%。光に弱い：光のダメージ、または光を浴びている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"Reduces hits from beyond {MonsterBehavior.Number((decimal)MonsterBehavior.Range)}m by {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%; moves +{MonsterBehavior.Number((decimal)MonsterBehavior.UnhitMovementPct)}%, then -{MonsterBehavior.Number(-(decimal)MonsterBehavior.HitMovementPct)}% for {MonsterBehavior.Number((decimal)MonsterBehavior.HitSlowSeconds)}s after damage. Weak to light: takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from light damage or while illuminated."),
                Stats = MonstersBalance.CreateMistTigerStats(),
                Affixes = NightmareAffix.Veiled | NightmareAffix.Skittish, Tags = VariantTag.WeakLight, R = 0.65f, G = 0.65f, B = 0.85f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.lantern_seed", MonsterType = "Mon_Sky_StarSeed",
                Name = new Txt("灯籠の星種", "Lantern Seed"),
                Description = new Txt($"{MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m未満の攻撃を{MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%軽減。{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒予告後、一度だけ同区画{MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m以内の最寄りの活動中の非ボス味方へHP{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}%の障壁を{MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}秒（重複不可）。闇に弱い：闇のダメージ、または闇を浴びている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"Reduces hits within {MonsterBehavior.Number((decimal)MonsterBehavior.InnerRange)}m by {MonsterBehavior.Number((decimal)MonsterBehavior.GuardReduction * 100m)}%. After a {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s warning, once shields the nearest awake nonboss ally within {MonsterBehavior.Number((decimal)MonsterBehavior.AllyRadius)}m in the same section for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldPct)}% HP for {MonsterBehavior.Number((decimal)MonsterBehavior.ShieldSeconds)}s (nonstacking). Weak to dark: takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from dark damage or while darkened."),
                Stats = MonstersBalance.CreateLanternSeedStats(),
                Affixes = NightmareAffix.Beacon | NightmareAffix.Hollow, Tags = VariantTag.WeakDark, R = 1f, G = 0.85f, B = 0.5f, Scale = 1.05f,
            },
            new VariantDef
            {
                Id = "var.eclipse_demon", MonsterType = "Mon_Forest_BossDemon",
                Name = new Txt("月蝕の森魔", "Eclipse Demon"),
                Description = new Txt($"最初の{MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}秒は無防備、以後{MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}秒守り・{MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}秒無防備。ボスの軽減上限{MonsterBehavior.Number((decimal)MonsterBehavior.BossGuardReduction * 100m)}%。HP{MonsterBehavior.Number((decimal)MonsterBehavior.FirstPhaseHealthRatio * 100m)}%と{MonsterBehavior.Number((decimal)MonsterBehavior.SecondPhaseHealthRatio * 100m)}%を下回ると{MonsterBehavior.Number((decimal)MonsterBehavior.PhaseOpeningSeconds)}秒間被ダメージ+{MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}%が優先。攻撃を避け、節目に集中攻撃。", $"Initially open for {MonsterBehavior.Number((decimal)MonsterBehavior.WarmupSeconds)}s, then cycles {MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}s guarded and {MonsterBehavior.Number((decimal)MonsterBehavior.PulseHalfPeriod)}s open; boss reduction caps at {MonsterBehavior.Number((decimal)MonsterBehavior.BossGuardReduction * 100m)}%. Crossing {MonsterBehavior.Number((decimal)MonsterBehavior.FirstPhaseHealthRatio * 100m)}% and {MonsterBehavior.Number((decimal)MonsterBehavior.SecondPhaseHealthRatio * 100m)}% HP overrides guards with {MonsterBehavior.Number((decimal)MonsterBehavior.PhaseOpeningSeconds)}s of {MonsterBehavior.Number(((decimal)MonsterBehavior.OpeningIncomingMultiplier - 1m) * 100m)}% extra damage. Bank burst for these phases and evade its attacks."),
                Stats = MonstersBalance.CreateEclipseDemonStats(),
                Affixes = NightmareAffix.Pulsing, Traits = VariantTrait.PhaseOpening,
                R = 0.55f, G = 0.3f, B = 0.75f, Scale = 1.2f,
            },
            // v1.29 wave 2：弱点を主役にした新変種。性質も悪夢共通の上乗せも持たず、
            // 弱点・耐性だけ（IsWeaknessBuilt）。全て既定のShardBonusPct、ランダムMirageSkinなし。
            new VariantDef
            {
                Id = "var.rust_scavenger", MonsterType = "Mon_SnowMountain_Scavenger",
                Name = new Txt("錆の漁り手", "Rust Scavenger"),
                Description = new Txt($"雪原で拾った鉄屑を鎧のように纏う漁り手。火に弱い：火のダメージ、または燃えている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"A scavenger wearing the scrap iron it collects. Weak to fire: takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from fire damage or while burning."),
                Stats = MonstersBalance.CreateRustScavengerStats(),
                Tags = VariantTag.WeakFire, R = 0.8f, G = 0.55f, B = 0.35f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.broodfly", MonsterType = "Mon_Despair_ParalyticFly",
                Name = new Txt("巣食う生蠅", "Brood-Parasite Fly"),
                Description = new Txt($"連れの獣に卵を産みつける生蠅。召喚獣を狩る：召喚獣へのダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}%、召喚獣からの被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"A fly that lays its eggs in companion beasts. Hunts summons: deals {MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}% more to summons but takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more from summon damage."),
                Stats = MonstersBalance.CreateBroodflyStats(),
                Tags = VariantTag.SummonHunter, R = 0.6f, G = 0.85f, B = 0.4f, Scale = 1.05f,
            },
            new VariantDef
            {
                Id = "var.stardust_shell", MonsterType = "Mon_Sky_StellaMatter",
                Name = new Txt("星屑の殻", "Stardust Shell"),
                Description = new Txt($"星の欠片が固まった結晶の塊。記憶以外に堅い：記憶以外のダメージ（通常攻撃・固有効果・仕掛けなど）を{MonsterBehavior.Number((decimal)MonsterBehavior.MaxTagResistance * 100m)}%軽減。記憶のダメージは通常どおり。", $"A mass of fused star fragments. Armored against non-memory damage: reduces damage not from memories (basic attacks, innate effects, hazards and so on) by {MonsterBehavior.Number((decimal)MonsterBehavior.MaxTagResistance * 100m)}%; memory damage passes through normally."),
                Stats = MonstersBalance.CreateStardustShellStats(),
                Tags = VariantTag.Armored, R = 0.75f, G = 0.8f, B = 1f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.web_ripper", MonsterType = "Mon_DarkCave_CaveSpider",
                Name = new Txt("帷を裂く蜘蛛", "Web-Rending Spider"),
                Description = new Txt($"粘りの糸で障壁を引き裂く洞窟蜘蛛。障壁を割る：障壁を持つ相手へのダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}%。攻撃者が障壁を持っている間は被ダメージ+{MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}%。", $"A cave spider whose sticky web tears barriers apart. Breaks shields: deals {MonsterBehavior.Number((decimal)MonsterBehavior.TagHunterDealtBonus * 100m)}% more to shielded targets, but takes {MonsterBehavior.Number((decimal)MonsterBehavior.WeaknessTakenBonus * 100m)}% more while its attacker holds a shield."),
                Stats = MonstersBalance.CreateWebRipperStats(),
                Tags = VariantTag.ShieldBreaker, R = 0.75f, G = 0.65f, B = 0.9f, Scale = 1.15f,
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

        public static bool IsExpanded(VariantDef v) => v != null &&
            (Nightmares.HasBehavior(v.Affixes) || (v.Traits & VariantTrait.PhaseOpening) != 0 || IsWeaknessBuilt(v));

        /// <summary>弱点・耐性だけで構成された新変種（v1.29 wave 2）。記述されていない攻撃を足さないため、MirageSkin も付けない。</summary>
        public static bool IsWeaknessBuilt(VariantDef v) => v != null && v.Tags != VariantTag.None
            && v.Affixes == NightmareAffix.None && v.Traits == VariantTrait.None;

        /// <summary>変種になる確率。調整表の最小深度・基礎確率・深度ごとの増加を参照。</summary>
        public static double Chance(int depth)
        {
            depth = Loot.ClampHeat(depth);
            return depth < MinDepth ? 0 : BaseChance + ChancePerDepth * (depth - MinDepth);
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

        /// <summary>性質の短い名前（HUD の区画の一行知らせに並べる）。両言語で空でない。</summary>
        public static string TagNotice(VariantTag tag)
        {
            switch (tag)
            {
                case VariantTag.WeakFire: return Loc.T("火に弱い", "fire-weak");
                case VariantTag.WeakCold: return Loc.T("冷気に弱い", "cold-weak");
                case VariantTag.WeakLight: return Loc.T("光に弱い", "light-weak");
                case VariantTag.WeakDark: return Loc.T("闇に弱い", "dark-weak");
                case VariantTag.ShieldBreaker: return Loc.T("障壁を割る", "shield-breaking");
                case VariantTag.SummonHunter: return Loc.T("召喚獣を狩る", "summon-hunting");
                case VariantTag.LightEater: return Loc.T($"光{MonsterBehavior.LightEaterMinStacks}以上で脆い", $"light-frail ({MonsterBehavior.LightEaterMinStacks}+)");
                case VariantTag.Armored: return Loc.T("記憶以外に堅い", "non-memory-armored");
                case VariantTag.Spellward: return Loc.T("記憶を弾く", "memory-warded");
                default: return "";
            }
        }

        /// <summary>今の区画に、弱点・耐性を持つ変種がいるときの一行知らせ。tags が空なら null。</summary>
        public static string ZoneNotice(VariantTag tags)
        {
            if (tags == VariantTag.None) return null;
            var parts = new List<string>();
            for (int bit = 1; bit != 0 && bit <= 1 << 8; bit <<= 1)
            {
                var tag = (VariantTag)bit;
                if ((tags & tag) != 0) parts.Add(TagNotice(tag));
            }
            return Loc.T("この区画：", "This section: ") + string.Join(Loc.T("・", ", "), parts)
                + Loc.T("敵がいる", " enemies");
        }

        /// <summary>変種の撃破を、何の格として抽選するか（悪夢と同じく一段上）。</summary>
        public static MonsterTier RewardTier(MonsterTier tier) => Nightmares.RewardTier(tier);
    }
}
