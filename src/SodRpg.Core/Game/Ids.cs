namespace SodRpg.Core.Game
{
    /// <summary>装備枠。主装備・頭・防具・手・足・装飾品の6枠。保存用の値は変更しない。</summary>
    public enum Slot
    {
        Weapon = 0,
        Armor = 1,
        Charm = 2,
        Head = 3,
        Hands = 4,
        Feet = 5,
    }

    /// <summary>レア度。固有品は名前付きの固定品で、ボスからのみ落ちる。</summary>
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4,
    }

    /// <summary>系統。狙い系統と素材の区別に使う。</summary>
    public enum Line
    {
        Offense = 0,
        Guard = 1,
        Resonance = 2,
    }

    /// <summary>
    /// 能力値。値はすべて整数の「表示単位」で持つ（%なら%の数値、防御なら防御値）。
    /// ゲームへ渡すときに StatBonus の単位へ変換する（会心率は 1 = 1% = 0.01）。
    /// </summary>
    public enum Stat
    {
        AttackPct = 0,
        PowerPct = 1,
        AttackSpeedPct = 2,
        CritChancePct = 3,
        CritDamagePct = 4,
        MaxHealthPct = 5,
        MaxHealthFlat = 6,
        Armor = 7,
        HealthRegen = 8,
        Haste = 9,
        MoveSpeedPct = 10,
        Tenacity = 11,
        FireAmp = 12,
        ColdAmp = 13,
        LightAmp = 14,
        DarkAmp = 15,
        /// <summary>通常攻撃の射程（%）。</summary>
        AttackRangePct = 16,
        /// <summary>4発目の位置を前へ進める（本体の everyFourAttackStartIndex）。</summary>
        FourthAttackShift = 17,
        /// <summary>アイデンティティ記憶のエッセンス枠の追加数（星図の頂点。最大1。能力補正にはならない）。</summary>
        EssenceSlotIdentity = 18,
        /// <summary>回避（移動の記憶）のエッセンス枠の追加数（星図の頂点。最大1。能力補正にはならない）。</summary>
        EssenceSlotMovement = 19,
        /// <summary>与える回復量（味方への回復も含む、%）。</summary>
        HealPower = 20,
        /// <summary>与えるシールド量（味方へのシールドも含む、%）。</summary>
        ShieldPower = 21,
        /// <summary>自身の召喚獣が与えるダメージ（%）。</summary>
        SummonPower = 22,
        /// <summary>HPを捧げる技の消費軽減（%）。</summary>
        SacrificeReduction = 23,
        /// <summary>攻撃力（固定値）。本体の attackDamageFlat（v1.28）。</summary>
        AttackFlat = 24,
        /// <summary>魔力（固定値）。本体の abilityPowerFlat（v1.28）。</summary>
        PowerFlat = 25,
    }

    /// <summary>
    /// 固有効果（戦闘中に発動する仕組み）。Epic 以上の装備、固有品、専門化の到達ノード（刻印）が持つ。
    /// 実際の発動はホスト側の接続層が行い、ここでは識別子と数値だけを扱う。
    /// </summary>
    public enum Power
    {
        None = 0,
        /// <summary>撃破ごとに攻撃速度+X%（4秒、5重まで）。「終わらない舞」</summary>
        Momentum = 1,
        /// <summary>被弾後3秒、攻撃力+X%。「逆襲の構え」</summary>
        Retaliation = 2,
        /// <summary>周囲6mの敵3体以上で防御+X。「鉄の輪」</summary>
        Bulwark = 3,
        /// <summary>通常攻撃の命中で最大HPのX/10%回復。</summary>
        Lifesteal = 4,
        /// <summary>受けたダメージのX%を攻撃者へ反射。「棘の鎧」</summary>
        Thorns = 5,
        /// <summary>HP30%未満の敵への通常攻撃に攻撃力X%の追加ダメージ。</summary>
        Executioner = 6,
        /// <summary>10m以内に味方がいる間、自分と味方の攻撃力・魔力+X%（ソロは半分）。「共鳴の環」</summary>
        Resonance = 7,
        /// <summary>撃破後2秒、移動速度+X%。「追い風」</summary>
        Tailwind = 8,
        /// <summary>12秒ごとに最大HPのX%の障壁。「護りの灯」</summary>
        Barrier = 9,
        /// <summary>HP30%未満で最大HPのX%回復（60秒に1回）。「灯守」</summary>
        SecondWind = 10,
        /// <summary>4回目ごとの通常攻撃に攻撃力X%の魔法追加ダメージ。「烈火」</summary>
        Blaze = 11,
        /// <summary>通常攻撃の命中時に25%で、近くの敵2体へ攻撃力X%の魔法ダメージ。「雷鎖」</summary>
        ChainLightning = 12,
        /// <summary>撃破時、周囲4mの敵へ攻撃力X%のダメージ。「爆砕」</summary>
        Shatter = 13,
        /// <summary>1回で最大HPの20%以上を受けたら最大HPのX%の障壁（20秒に1回）。「守護霊」</summary>
        Aegis = 14,
        /// <summary>HP50%未満の間、攻撃速度+X%。「血の渇き」</summary>
        Bloodlust = 15,
        /// <summary>通常攻撃の命中時にX%で火を1スタック付与。「火種」</summary>
        Ember = 16,
        /// <summary>通常攻撃の命中時にX%で冷気を付与。「霜」</summary>
        Frost = 17,
        /// <summary>通常攻撃の命中時にX%で光を1スタック付与。「輝き」</summary>
        Radiance = 18,
        /// <summary>通常攻撃の命中時にX%で闇を1スタック付与。「影」</summary>
        Umbra = 19,
        /// <summary>敵に火・冷気・光・闇がすべて乗った瞬間、攻撃力X%の純粋ダメージ（同じ敵へは6秒に1回）。「四元の共鳴」</summary>
        Convergence = 20,
        /// <summary>回避（Movement）の後3秒以内の次の通常攻撃に、攻撃力か魔力の高い方の X% を上乗せ（v1.27）。「回避の残響」</summary>
        EchoingDodge = 21,
        /// <summary>Ultimate（R）を使うと5秒間 攻撃力・魔力+X%。「終の昂り」</summary>
        UltimateSurge = 22,
        /// <summary>撃破で最大HPのX/10%回復（0.5秒に1回）。「吸魂」</summary>
        SoulSiphon = 23,
        /// <summary>回避で周囲4mの敵へ攻撃力X%のダメージ（2秒に1回）。「旋風」</summary>
        Whirlwind = 24,
        /// <summary>周囲6mの敵1体につき攻撃速度+X%（5体まで）。「乱戦」</summary>
        Frenzy = 25,
        /// <summary>HP90%以上の敵への通常攻撃に攻撃力X%の追加ダメージ。「先制」</summary>
        OpeningStrike = 26,
        /// <summary>Ultimateを使うと最大HPのX%の障壁。「星の加護」</summary>
        StarShield = 27,
        /// <summary>回避後3秒、移動速度+X%。「疾駆」</summary>
        Sprint = 28,
        /// <summary>HP80%以上の間、攻撃力+X%。「不屈」</summary>
        Vigor = 29,
        /// <summary>Ultimate・回避以外のスキル使用後4秒、魔力+X%（時間だけ延長）。「過負荷」</summary>
        Overload = 30,
        /// <summary>Q/W/Eを8秒以内に使うとRの残りクールダウンをX%短縮（10秒に1回）。「終曲」</summary>
        Finale = 31,
        /// <summary>通常攻撃の会心でQ/W/EのクールダウンをX/10秒短縮（0.5秒に1回）。「会心の余韻」</summary>
        CriticalEcho = 32,
        /// <summary>スタン・スロウ・冷気のある敵への与ダメージ+X%。「足枷」</summary>
        Fetters = 33,
        /// <summary>装着エッセンスの品質合計100%ごとに攻撃力・魔力+X%（8段まで）。「結晶共鳴」</summary>
        CrystalResonance = 34,
        /// <summary>ハンターの追跡度1ごとに攻撃力・魔力+X%（3まで）。「獲物の誇り」</summary>
        PreyPride = 35,
        /// <summary>超過回復のX%を3秒の障壁にする（1回につき最大HPの10%まで）。「溢れる命」</summary>
        OverflowingLife = 36,
        /// <summary>聖堂を使うたびゾーン内で攻撃力・魔力+X%（5重まで）。「祈願」</summary>
        Devotion = 37,
        /// <summary>火3スタック以上の敵への火付与時、X%で近くの敵1体に火を付与（同じ敵から2秒に1回）。「飛び火」</summary>
        Wildfire = 38,
        /// <summary>自分の技で敵をスタンさせると、最大HPのX%の障壁（3秒、2秒に1回）。「止水」</summary>
        StillWater = 39,
        /// <summary>ゴールドを100使うごとに最大HPのX%の障壁（10秒、3回分まで）。「散財の護り」</summary>
        SpendersWard = 40,
        /// <summary>無敵でダメージを無効化すると攻撃速度+X%（3秒、1.5秒に1回）。「見切り」</summary>
        PerfectRead = 41,
        /// <summary>Evilの明晰夢1つにつき攻撃力・魔力+X%（6つまで、合計18%まで）。「明晰」</summary>
        LucidBoon = 42,
        /// <summary>回避・ダッシュ・瞬間移動の後3秒以内の次の通常攻撃に、攻撃力か魔力の高い方のX%を上乗せ（重ならず時間を延長）。「瞬歩の刃」</summary>
        ShadowStep = 43,
    }

    /// <summary>撃破された敵の格。ゲームの Monster.MonsterType と同じ並び。</summary>
    public enum MonsterTier
    {
        Lesser = 0,
        Normal = 1,
        MiniBoss = 2,
        Boss = 3,
    }

    /// <summary>素材の識別子。</summary>
    public static class Materials
    {
        /// <summary>夢の欠片。強化・製作の主通貨。</summary>
        public const string Shard = "shard";
        /// <summary>調律石。再調律（特性の引き直し）に使う。</summary>
        public const string Tuning = "tuning";
    }
}
