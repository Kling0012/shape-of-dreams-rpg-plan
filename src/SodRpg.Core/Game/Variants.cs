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
        /// <summary>HP70%・40%を下回ると3秒間、被ダメージ+20%。新しい守りより優先する。</summary>
        PhaseOpening = 1 << 2,

    }

    /// <summary>
    /// 変種の弱点・耐性（v1.29 wave 2）。どの性質も特定の型を報酬づけるためのもので、
    /// 耐性は1ヒットあたり −30% で打ち止め（どの型も無効化しない）。ホストが takenDamageProcessor で処理する。
    /// 元素の判定は「その元素のダメージ（DamageData.elemental）」か「その元素がかかっている間
    /// （EntityStatus の fireStack/hasCold/lightStack/darkStack）」のどちらか（.ref/dump で両方確認済み）。
    /// </summary>
    [Flags]
    public enum VariantTag
    {
        None = 0,
        /// <summary>火に弱い：火のダメージ、または燃えている間は被ダメージ+30%。</summary>
        WeakFire = 1 << 0,
        /// <summary>冷気に弱い：冷気のダメージ、または凍ている間は被ダメージ+30%。</summary>
        WeakCold = 1 << 1,
        /// <summary>光に弱い：光のダメージ、または光を浴びている間は被ダメージ+30%。</summary>
        WeakLight = 1 << 2,
        /// <summary>闇に弱い：闇のダメージ、または闇を浴びている間は被ダメージ+30%。</summary>
        WeakDark = 1 << 3,
        /// <summary>障壁を割る：障壁を持つ相手へのダメージ+50%。攻撃者が障壁を持っている間は被ダメージ+30%。</summary>
        ShieldBreaker = 1 << 4,
        /// <summary>召喚獣を狩る：召喚獣へのダメージ+50%、召喚獣からの被ダメージ+30%。</summary>
        SummonHunter = 1 << 5,
        /// <summary>光3以上で脆い：光のダメージで被ダメージ+30%（スタックは決して消費しない）。</summary>
        LightEater = 1 << 6,
        /// <summary>堅甲：通常攻撃（記憶でない攻撃）の被ダメージ −30%。記憶は通常どおり。</summary>
        Armored = 1 << 7,
        /// <summary>記憶を弾く：記憶のダメージの被ダメージ −30%。通常攻撃は通常どおり。</summary>
        Spellward = 1 << 8,
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
        /// <summary>弱点・耐性（v1.29 wave 2）。なければ None。</summary>
        public VariantTag Tags;
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
                Description = new Txt("分厚い樹皮の障壁をまとい、殴った相手に棘を返す。火に弱い：火のダメージ、または燃えている間は被ダメージ+30%。", "Shielded by thick bark that thorns its attackers. Weak to fire: takes 30% more from fire damage or while burning."),
                Stats = new[] { S(Stat.MaxHealthPct, 250), S(Stat.Armor, 30) },
                Affixes = NightmareAffix.Warded | NightmareAffix.Thorned, Tags = VariantTag.WeakFire, R = 0.25f, G = 0.55f, B = 0.25f, Scale = 1.3f,
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
                Description = new Txt("障壁に守られ、重い一撃で防御を砕く。障壁を割る：障壁を持つ相手へのダメージ+50%。攻撃者が障壁を持っている間は被ダメージ+30%。", "Shielded, and its heavy blows shatter your armor. Breaks shields: deals 50% more to shielded targets, but takes 30% more while its attacker holds a shield."),
                Stats = new[] { S(Stat.MaxHealthPct, 200), S(Stat.AttackPct, 30) },
                Affixes = NightmareAffix.Sundering | NightmareAffix.Warded, Tags = VariantTag.ShieldBreaker, R = 0.45f, G = 0.2f, B = 0.6f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.hollow_gunner", MonsterType = "Mon_Despair_WretchedArtillery",
                Name = new Txt("虚ろな砲手", "Hollow Gunner"),
                Description = new Txt("遠くから重い砲撃を撃ち込み、障壁で身を守る。記憶を弾く：記憶のダメージを30%軽減。通常攻撃は軽減しない。", "Shelling from afar behind a barrier. Warded against memories: reduces memory damage by 30%; basic attacks are unaffected."),
                Stats = new[] { S(Stat.MaxHealthPct, 200), S(Stat.AttackPct, 40) },
                Affixes = NightmareAffix.Warded, Tags = VariantTag.Spellward, R = 0.6f, G = 0.35f, B = 0.9f, Scale = 1.25f,
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
                Description = new Txt("触れれば焼け、倒れれば爆ぜる。冷気に弱い：冷気のダメージ、または凍ている間は被ダメージ+30%。", "Burns those who strike it, and bursts when it falls. Weak to cold: takes 30% more from cold damage or while chilled."),
                Stats = new[] { S(Stat.MaxHealthPct, 120) },
                Affixes = NightmareAffix.Thorned, Traits = VariantTrait.DeathBurst, Tags = VariantTag.WeakCold, R = 1f, G = 0.4f, B = 0.1f, Scale = 1.25f,
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
            new VariantDef
            {
                Id = "var.mist_spitter", MonsterType = "Mon_Forest_SpiderSpitter",
                Name = new Txt("霞吐き蜘蛛", "Mist Spitter"),
                Description = new Txt("6mより遠い攻撃を30%軽減。吐き出しと後退を避けて6m以内へ。", "Reduces hits from beyond 6m by 30%; dodge its spit and backdash to close within 6m."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Affixes = NightmareAffix.Veiled, R = 0.65f, G = 0.85f, B = 0.7f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.brood_warden", MonsterType = "Mon_Forest_SpiderWarrior",
                Name = new Txt("群巣の番蜘蛛", "Brood Warden"),
                Description = new Txt("同区画5m以内の活動中の味方で被ダメージ-30%。2秒予告後一度だけ最寄りの非ボス味方にHP15%の障壁を6秒（重複不可）。召喚獣を狩る：召喚獣へのダメージ+50%、召喚獣からの被ダメージ+30%。", "An awake ally within 5m in the same section grants 30% reduction. After a 2s warning, once shields the nearest nonboss ally for 15% HP for 6s (nonstacking). Hunts summons: deals 50% more to summons but takes 30% more from summon damage."),
                Stats = new[] { S(Stat.MaxHealthPct, 70) },
                Affixes = NightmareAffix.Packbound | NightmareAffix.Beacon, Tags = VariantTag.SummonHunter, R = 0.45f, G = 0.7f, B = 0.25f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.hollow_elemental", MonsterType = "Mon_DarkCave_DarkElemental",
                Name = new Txt("空洞の影精", "Hollow Shade"),
                Description = new Txt("3m未満の攻撃を30%軽減。光に弱い：光のダメージ、または光を浴びている間は被ダメージ+30%。", "Reduces hits from within 3m by 30%. Weak to light: takes 30% more from light damage or while illuminated."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Affixes = NightmareAffix.Hollow, Tags = VariantTag.WeakLight, R = 0.4f, G = 0.3f, B = 0.7f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.flicker_olm", MonsterType = "Mon_DarkCave_NightOlm",
                Name = new Txt("明滅する洞竜", "Flicker Olm"),
                Description = new Txt("最初の2秒は無防備、以後3秒間30%軽減と3秒間無防備。光3以上で脆い：光のダメージで被ダメージ+30%（スタックは消さない）。", "Open for the first 2s, then cycles 3s of 30% reduction and 3s open. Frail at 3+ light: takes 30% more from light damage while carrying 3 or more light stacks (never consumed)."),
                Stats = new[] { S(Stat.MaxHealthPct, 50) },
                Affixes = NightmareAffix.Pulsing, Tags = VariantTag.LightEater, R = 0.3f, G = 0.8f, B = 0.8f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.timorous_displacer", MonsterType = "Mon_Despair_Displacer",
                Name = new Txt("臆病な跳躍獣", "Timorous Displacer"),
                Description = new Txt("未被弾時は移動+20%、被弾後2秒は-15%。一撃を当てて転移先を追う。", "Moves 20% faster while unhit, but 15% slower for 2s after damage. Tag it, then pursue through its blink."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Affixes = NightmareAffix.Skittish, R = 0.7f, G = 0.35f, B = 0.85f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.overreaching_bug", MonsterType = "Mon_Despair_DreadBug",
                Name = new Txt("大振りの恐虫", "Overreaching Dreadbug"),
                Description = new Txt("詠唱中は30%軽減、攻撃発射後1.5秒は被ダメージ+20%。跳躍や打撃を避けて反撃。", "Reduces damage by 30% while channeling; takes 20% more for 1.5s after firing. Dodge its leap or strike, then punish recovery."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Affixes = NightmareAffix.Committed, R = 0.8f, G = 0.4f, B = 0.45f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.last_thaw", MonsterType = "Mon_SnowMountain_IceElemental",
                Name = new Txt("薄氷の核", "Last Thaw"),
                Description = new Txt("HP35%以下で2秒予告後、一度だけHP15%の障壁を6秒。予告中に集中攻撃するか殻を割る。", "At 35% HP, a 2s warning precedes a once-per-life 15% HP shield for 6s. Save burst for the warning or break its shell."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Affixes = NightmareAffix.LastStand, R = 0.7f, G = 0.9f, B = 1f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.rime_sentinel", MonsterType = "Mon_SnowMountain_VikingWarrior",
                Name = new Txt("霜盾の番兵", "Rime Sentinel"),
                Description = new Txt("正面120度からの被ダメージを30%軽減。通常攻撃に堅い：通常攻撃のダメージを30%軽減。記憶のダメージは通常どおり。", "Reduces damage from its frontal 120-degree cone by 30%. Armored against basics: reduces basic-attack damage by 30%; memory damage passes through normally."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Affixes = NightmareAffix.Facing, Tags = VariantTag.Armored, R = 0.45f, G = 0.7f, B = 0.95f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.furnace_ram", MonsterType = "Mon_LavaLand_Magmadon",
                Name = new Txt("炉殻の突進獣", "Furnace Ram"),
                Description = new Txt("正面120度と詠唱中は30%軽減（合計上限40%）。発射後1.5秒は被ダメージ+20%が優先。突進を横へ避けて反撃。", "Frontal 120-degree hits and channeling each grant 30% reduction, capped at 40%. After firing, 1.5s of 20% extra damage overrides guards. Flank the charge and punish recovery."),
                Stats = new[] { S(Stat.MaxHealthPct, 70) },
                Affixes = NightmareAffix.Facing | NightmareAffix.Committed, R = 1f, G = 0.45f, B = 0.2f, Scale = 1.2f,
            },
            new VariantDef
            {
                Id = "var.ember_mender", MonsterType = "Mon_LavaLand_InfernoSpider",
                Name = new Txt("熾火の繕い蜘蛛", "Ember Mender"),
                Description = new Txt("4秒無傷なら毎秒HP1%回復、一生で10%まで。満タンでは消費しない。溶岩跳躍の合間に当て続ける。", "After 4s without damage heals 1% HP per second, capped at 10% per life with no budget spent at full HP. Keep landing hits between lava jumps."),
                Stats = new[] { S(Stat.MaxHealthPct, 50) },
                Affixes = NightmareAffix.Recuperating, R = 0.95f, G = 0.65f, B = 0.3f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.mist_tiger", MonsterType = "Mon_Ink_Tiger",
                Name = new Txt("霞走りの虎", "Mist Tiger"),
                Description = new Txt("6mより遠い攻撃を30%軽減。移動+20%、被弾後2秒は-15%。光に弱い：光のダメージ、または光を浴びている間は被ダメージ+30%。", "Reduces hits from beyond 6m by 30%; moves +20%, then -15% for 2s after damage. Weak to light: takes 30% more from light damage or while illuminated."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Affixes = NightmareAffix.Veiled | NightmareAffix.Skittish, Tags = VariantTag.WeakLight, R = 0.65f, G = 0.65f, B = 0.85f, Scale = 1.15f,
            },
            new VariantDef
            {
                Id = "var.lantern_seed", MonsterType = "Mon_Sky_StarSeed",
                Name = new Txt("灯籠の星種", "Lantern Seed"),
                Description = new Txt("3m未満の攻撃を30%軽減。2秒予告後、一度だけ同区画5m以内の最寄りの活動中の非ボス味方へHP15%の障壁を6秒（重複不可）。闇に弱い：闇のダメージ、または闇を浴びている間は被ダメージ+30%。", "Reduces hits within 3m by 30%. After a 2s warning, once shields the nearest awake nonboss ally within 5m in the same section for 15% HP for 6s (nonstacking). Weak to dark: takes 30% more from dark damage or while darkened."),
                Stats = new[] { S(Stat.MaxHealthPct, 40) },
                Affixes = NightmareAffix.Beacon | NightmareAffix.Hollow, Tags = VariantTag.WeakDark, R = 1f, G = 0.85f, B = 0.5f, Scale = 1.05f,
            },
            new VariantDef
            {
                Id = "var.eclipse_demon", MonsterType = "Mon_Forest_BossDemon",
                Name = new Txt("月蝕の森魔", "Eclipse Demon"),
                Description = new Txt("最初の2秒は無防備、以後3秒守り・3秒無防備。ボスの軽減上限20%。HP70%と40%を下回ると3秒間被ダメージ+20%が優先。攻撃を避け、節目に集中攻撃。", "Initially open for 2s, then cycles 3s guarded and 3s open; boss reduction caps at 20%. Crossing 70% and 40% HP overrides guards with 3s of 20% extra damage. Bank burst for these phases and evade its attacks."),
                Stats = new[] { S(Stat.MaxHealthPct, 20) },
                Affixes = NightmareAffix.Pulsing, Traits = VariantTrait.PhaseOpening,
                R = 0.55f, G = 0.3f, B = 0.75f, Scale = 1.2f,
            },
            // v1.29 wave 2：弱点を主役にした新変種。性質も悪夢共通の上乗せも持たず、
            // 弱点・耐性だけ（IsWeaknessBuilt）。全てShardBonusPct=100、ランダムMirageSkinなし。
            new VariantDef
            {
                Id = "var.rust_scavenger", MonsterType = "Mon_SnowMountain_Scavenger",
                Name = new Txt("錆の漁り手", "Rust Scavenger"),
                Description = new Txt("雪原で拾った鉄屑を鎧のように纏う漁り手。火に弱い：火のダメージ、または燃えている間は被ダメージ+30%。", "A scavenger wearing the scrap iron it collects. Weak to fire: takes 30% more from fire damage or while burning."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Tags = VariantTag.WeakFire, R = 0.8f, G = 0.55f, B = 0.35f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.broodfly", MonsterType = "Mon_Despair_ParalyticFly",
                Name = new Txt("巣食う生蠅", "Brood-Parasite Fly"),
                Description = new Txt("連れの獣に卵を産みつける生蠅。召喚獣を狩る：召喚獣へのダメージ+50%、召喚獣からの被ダメージ+30%。", "A fly that lays its eggs in companion beasts. Hunts summons: deals 50% more to summons but takes 30% more from summon damage."),
                Stats = new[] { S(Stat.MaxHealthPct, 50) },
                Tags = VariantTag.SummonHunter, R = 0.6f, G = 0.85f, B = 0.4f, Scale = 1.05f,
            },
            new VariantDef
            {
                Id = "var.stardust_shell", MonsterType = "Mon_Sky_StellaMatter",
                Name = new Txt("星屑の殻", "Stardust Shell"),
                Description = new Txt("星の欠片が固まった結晶の塊。通常攻撃に堅い：通常攻撃のダメージを30%軽減。記憶のダメージは通常どおり。", "A mass of fused star fragments. Armored against basics: reduces basic-attack damage by 30%; memory damage passes through normally."),
                Stats = new[] { S(Stat.MaxHealthPct, 60) },
                Tags = VariantTag.Armored, R = 0.75f, G = 0.8f, B = 1f, Scale = 1.1f,
            },
            new VariantDef
            {
                Id = "var.web_ripper", MonsterType = "Mon_DarkCave_CaveSpider",
                Name = new Txt("帷を裂く蜘蛛", "Web-Rending Spider"),
                Description = new Txt("粘りの糸で障壁を引き裂く洞窟蜘蛛。障壁を割る：障壁を持つ相手へのダメージ+50%。攻撃者が障壁を持っている間は被ダメージ+30%。", "A cave spider whose sticky web tears barriers apart. Breaks shields: deals 50% more to shielded targets, but takes 30% more while its attacker holds a shield."),
                Stats = new[] { S(Stat.MaxHealthPct, 50) },
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
                case VariantTag.LightEater: return Loc.T("光3以上で脆い", "light-frail (3+)");
                case VariantTag.Armored: return Loc.T("通常攻撃に堅い", "basic-attack-armored");
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
