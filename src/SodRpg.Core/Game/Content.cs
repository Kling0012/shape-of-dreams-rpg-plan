using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>日本語・英語の二言語テキスト。表示言語は Loc.Japanese で切り替える。</summary>
    public sealed class Txt
    {
        public Txt(string ja, string en)
        {
            Ja = ja;
            En = en;
        }

        public string Ja { get; }
        public string En { get; }

        public override string ToString() => Loc.Japanese ? Ja : En;
    }

    public static class Loc
    {
        public static bool Japanese = true;

        public static string T(string ja, string en) => Japanese ? ja : en;
    }

    /// <summary>特性（ランダムに付く能力値）の定義。値域は アイテムレベル1・レア度補正前。</summary>
    public sealed class AffixDef
    {
        public AffixDef(Stat stat, int min, int max, int weight = 10, Rarity minRarity = Rarity.Common)
        {
            Stat = stat;
            Min = min;
            Max = max;
            Weight = weight;
            MinRarity = minRarity;
        }

        /// <summary>この特性が付く最低のレア度（v1.28：攻撃力%・魔力%はエピック以上）。</summary>
        public Rarity MinRarity { get; }
        public Stat Stat { get; }
        public int Min { get; }
        public int Max { get; }
        public int Weight { get; }
    }

    public sealed class PowerRange
    {
        public PowerRange(Power power, int min, int max)
        {
            Power = power;
            Min = min;
            Max = max;
        }

        public Power Power { get; }
        public int Min { get; }
        public int Max { get; }
    }

    /// <summary>装備の基礎（種類）。固定の基礎能力（implicit）を1つ持つ。</summary>
    public sealed class BaseDef
    {
        public BaseDef(string id, Slot slot, Line line, Txt name, Stat implicitStat, int implicitValue)
        {
            Id = id;
            Slot = slot;
            Line = line;
            Name = name;
            ImplicitStat = implicitStat;
            ImplicitValue = implicitValue;
        }

        public string Id { get; }
        public Slot Slot { get; }
        public Line Line { get; }
        public Txt Name { get; }
        public Stat ImplicitStat { get; }
        public int ImplicitValue { get; }
    }

    /// <summary>固有品。基礎と2つの固有効果が固定で、特性は通常どおり3つ抽選する。</summary>
    public sealed class UniqueDef
    {
        public UniqueDef(string id, string baseId, Txt name, Txt lore, Power p1, int v1, Power p2, int v2)
        {
            Id = id;
            BaseId = baseId;
            Name = name;
            Lore = lore;
            Powers = new[] { new PowerLine(p1, v1), new PowerLine(p2, v2) };
        }

        /// <summary>セット遺物の部位（固有効果を持たない）。</summary>
        public UniqueDef(string id, string baseId, Txt name, string setId)
        {
            Id = id;
            BaseId = baseId;
            Name = name;
            Lore = new Txt("", "");
            SetId = setId;
            Powers = Array.Empty<PowerLine>();
        }

        public string Id { get; }
        public string BaseId { get; }
        public Txt Name { get; }
        public Txt Lore { get; }
        public IReadOnlyList<PowerLine> Powers { get; }
        /// <summary>セット遺物ならセットID、それ以外は null。</summary>
        public string SetId { get; }
        /// <summary>連携（v1.26）を持つならその定義、それ以外は null。</summary>
        public LinkDef Link { get; set; }
    }

    /// <summary>名前付きの3点セット（Diablo のセット装備）。2点・3点でボーナス。</summary>
    public sealed class SetDef
    {
        public string Id;
        public Txt Name;
        public StatLine[] TwoPiece;
        public PowerLine[] ThreePiece;

        public string Describe()
        {
            string two = string.Join(Loc.T("、", ", "), TwoPiece.Select(s => Content.FormatStat(s.Stat, s.Value)));
            string three = string.Join("\n", ThreePiece.Select(p => "　" + Content.FormatPower(p.Power, p.Value)));
            return Loc.T($"2つ装着：{two}\n3つ装着：\n{three}", $"2 pieces: {two}\n3 pieces:\n{three}");
        }

        /// <summary>いま何点そろっているかと、次に何が起きるか（装着画面用）。</summary>
        public string Progress(int count)
        {
            if (count >= 3) return Loc.T("3つそろっています。すべての効果が有効です。", "All 3 pieces equipped: every bonus is active.");
            if (count == 2) return Loc.T("あと1つで、3つ装着の効果が加わります。", "One more piece adds the 3-piece bonus.");
            return Loc.T("あと1つで、2つ装着の効果が有効になります。", "One more piece activates the 2-piece bonus.");
        }
    }

    /// <summary>専門化（星図）のノード。小ノードは段階制、到達ノードは刻印として1つだけ有効。</summary>
    public sealed class TalentDef
    {
        public TalentDef(string id, Line route, Txt name, Stat stat, int perRank, int maxRank)
        {
            Id = id;
            Route = route;
            Name = name;
            Stat = stat;
            PerRank = perRank;
            MaxRank = maxRank;
        }

        public TalentDef(string id, Line route, Txt name, Power power, int perRank, int maxRank)
            : this(id, route, name, default(Stat), perRank, maxRank)
        {
            RankPower = power;
        }

        public TalentDef(string id, Line route, Txt name, LinkDef linkPerRank, int maxRank)
            : this(id, route, name, default(Stat), linkPerRank.Value, maxRank)
        {
            LinkPerRank = linkPerRank;
        }

        public TalentDef(string id, Line route, Txt name, Power power, int powerValue, Txt description)
        {
            Id = id;
            Route = route;
            Name = name;
            IsKeystone = true;
            Power = power;
            PowerValue = powerValue;
            MaxRank = 1;
            Description = description;
        }

        public string Id { get; }
        public Line Route { get; }
        public Txt Name { get; }
        public Stat Stat { get; }
        public int PerRank { get; }
        public int MaxRank { get; }
        /// <summary>記憶ルート内の識別子と順番。核・夢の輪は RouteId が null。</summary>
        public string RouteId { get; set; }
        public int RouteOrder { get; set; }
        /// <summary>ルートに対応する記憶の型名。</summary>
        public string RouteMemory { get; set; }
        public bool IsDreamRing { get; set; }
        /// <summary>1段あたりの連携。能力値とは別に、装着条件をホストで判定する。</summary>
        public LinkDef LinkPerRank { get; set; }
        /// <summary>小ノードが1段ごとに伸ばす固有効果。能力値・連携ノードは None。</summary>
        public Power RankPower { get; set; }
        public bool IsPowerNode => !IsKeystone && LinkPerRank == null && RankPower != Power.None;
        /// <summary>1は核の手前の星、2は奥の星・記憶ルート・夢の輪。</summary>
        public int Tier { get; set; } = 1;
        /// <summary>記憶の仕掛け（v1.28）。ルートの記憶に反応する。なければ null。</summary>
        public GimmickDef Gimmick { get; set; }
        /// <summary>隣接する記憶をつなぐ橋の合わせ技。外側の夢の輪には付かない。</summary>
        public PairComboDef PairCombo => PairCombos.ForBridge(Id);
        /// <summary>1段に要るポイント（ふつうは1。連装・四の型のように強い星だけ高い）。</summary>
        public int RankCost { get; set; } = 1;
        public bool IsKeystone { get; }
        public Power Power { get; }
        public int PowerValue { get; }
        public Txt Description { get; }
        /// <summary>旅人の刻印なら旅人の型名（例：Hero_Vesper）。汎用ノードは null。</summary>
        public string HeroKey { get; set; }

        /// <summary>星図に表示する効果。小ノードは1段あたりの値。</summary>
        public string Describe()
        {
            if (PairCombo != null) return PairCombos.Describe(PairCombo);
            string effect;
            if (IsKeystone)
                effect = Content.FormatPower(Power, PowerValue) + "\n" + Description;
            else
                effect = LinkPerRank != null ? Links.Describe(LinkPerRank)
                    : IsPowerNode ? Content.FormatPower(RankPower, PerRank)
                    : Gimmick != null && PerRank == 0 ? "" : Content.FormatStat(Stat, PerRank);
            string gimmick = Gimmicks.Describe(Gimmick, RouteMemory);
            if (gimmick.Length > 0) effect = effect.Length == 0 ? gimmick : effect + "\n" + gimmick;
            if (IsKeystone) return effect;
            if (RankCost > 1) return effect + Loc.T($"（1段まで・{RankCost}ポイント）", $" (1 rank only, costs {RankCost} points)");
            return effect + Loc.T("（1段ごと）", " (per rank)");
        }
    }

    /// <summary>
    /// ゲーム内容の定義一式。数値は計画書 付録A・B の設計値を出発点に、
    /// 本MODで遊べる形へ丸めたもの（固有名はすべて仮称）。
    /// </summary>
    public static class Content
    {
        public const int SlotCount = 6;
        public static readonly IReadOnlyList<Slot> SlotOrder = new[]
        {
            Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm,
        };

        public const int MaxItemLevel = 60;
        public const int ItemLevelScalingCap = 40;
        public const int StashCapacity = 80;
        public const int SatchelCapacity = 30;
        public const int LostAndFoundCapacity = 10;
        public const int MaxHeat = 5;
        public const int MaxEnhance = 5;
        public const int MaxRetunes = 3;
        public const int MaxDreamLevel = 30;
        public const int KeystoneRouteRequirement = 6;
        public const int KeystoneCost = 3;
        public const int RoomsToRecoverLost = 3;
        public const int CodexPerPoint = 6;
        public const int MaxCodexBonus = 4;
        /// <summary>強化の節目。+3で特性が1行、+5で固有効果（なければ）か特性1行。</summary>
        public const int EnhanceMilestoneFirst = 3;
        public const int EnhanceMilestoneSecond = 5;
        /// <summary>限界突破（v1.27）。1回ごとに強化上限がこの値だけ広がる。</summary>
        public const int EnhanceStepPerBreak = 5;
        /// <summary>限界突破の節目。+10と+15で特性が1行ずつ、+20（伝説のみ）で固有効果1つの値が1.2倍。</summary>
        public const int EnhanceMilestoneThird = 10;
        public const int EnhanceMilestoneFourth = 15;
        public const int EnhanceMilestoneFifth = 20;
        /// <summary>強化の節目の数（+3・+5・+10・+15・+20）。</summary>
        public const int MaxEnhanceMilestones = 5;
        /// <summary>限界突破で固有効果1つの値に掛ける倍率（%）。+20の節目。</summary>
        public const int LimitBreakPowerPct = 120;
        /// <summary>再調律で出す候補の数。</summary>
        public const int RetuneChoices = 3;
        /// <summary>合成の結果の枠を選ぶときの欠片の倍率（%）。</summary>
        public const int TransmuteTargetCostPct = 150;
        /// <summary>覚醒の段の数（v1.27 で1段から3段に）。</summary>
        public const int MaxAwakenLevel = 3;
        private static readonly int[] AwakenThresholds = { 0, 2000, 6000, 15000 }; // 1回の遠征で約460溜まる（BalanceSim の前提）。約4・13・32回
        private static readonly int[] AwakenPowerPcts = { 100, 125, 150, 180 };
        private static readonly int[] AwakenAffixPcts = { 100, 110, 120, 130 };
        /// <summary>v1.26 までに覚醒した遺物が入る段（倍率が当時と同じ）。</summary>
        public const int LegacyAwakenLevel = 2;
        /// <summary>覚醒の力の上限（最後の段に要る累計）。</summary>
        public static int AwakenThreshold => AwakenThresholds[MaxAwakenLevel];

        private static int AwakenClamp(int level) => Math.Max(0, Math.Min(MaxAwakenLevel, level));
        /// <summary>その段に上がるのに要る覚醒の力（累計）。</summary>
        public static int AwakenThresholdFor(int level) => AwakenThresholds[AwakenClamp(level)];
        /// <summary>その段の固有効果（と連携）の倍率（%）。</summary>
        public static int AwakenPowerPctAt(int level) => AwakenPowerPcts[AwakenClamp(level)];
        /// <summary>その段の特性の倍率（%）。</summary>
        public static int AwakenAffixPctAt(int level) => AwakenAffixPcts[AwakenClamp(level)];
        /// <summary>覚醒の力の累計から、届いている段。</summary>
        public static int AwakenLevelFor(int points)
        {
            int level = 0;
            while (level < MaxAwakenLevel && points >= AwakenThresholds[level + 1]) level++;
            return level;
        }
        public static string AwakenNumeral(int level) => level == 1 ? "Ⅰ" : level == 2 ? "Ⅱ" : level == 3 ? "Ⅲ" : "";

        /// <summary>v1.28 までの土台の数。v1.29 の2段目で新土台にも固有品が付くまでの判定に使う（GearVolumeV121Tests）。</summary>
        public const int PreV129BaseCount = 180;

        public static readonly IReadOnlyList<BaseDef> Bases = new[]
        {
            new BaseDef("weapon.chain_sword", Slot.Weapon, Line.Offense, new Txt("連なりの剣", "Chain Sword"), Stat.AttackSpeedPct, 4),
            new BaseDef("weapon.twin_fang", Slot.Weapon, Line.Offense, new Txt("双牙の短刀", "Twin Fang Daggers"), Stat.CritChancePct, 3),
            new BaseDef("weapon.calming_staff", Slot.Weapon, Line.Resonance, new Txt("鎮めの杖", "Calming Staff"), Stat.PowerFlat, 6),
            new BaseDef("weapon.longspike_bow", Slot.Weapon, Line.Offense, new Txt("長穂の弓", "Longspike Bow"), Stat.CritDamagePct, 10),
            new BaseDef("weapon.shield_maul", Slot.Weapon, Line.Guard, new Txt("盾打ちの槌", "Shieldbash Maul"), Stat.MaxHealthPct, 5),
            new BaseDef("weapon.blaze_greatsword", Slot.Weapon, Line.Offense, new Txt("烈火の大剣", "Blazing Greatsword"), Stat.AttackFlat, 6),

            new BaseDef("armor.counter_gauntlets", Slot.Armor, Line.Guard, new Txt("反撃の籠手", "Counter Gauntlets"), Stat.Armor, 6),
            new BaseDef("armor.flowing_cloak", Slot.Armor, Line.Resonance, new Txt("流れの外套", "Flowing Cloak"), Stat.MoveSpeedPct, 3),
            new BaseDef("armor.guardian_plate", Slot.Armor, Line.Guard, new Txt("護りの胸当て", "Guardian Plate"), Stat.Armor, 10),
            new BaseDef("armor.resonant_robe", Slot.Armor, Line.Resonance, new Txt("共鳴の衣", "Resonant Robe"), Stat.Haste, 5),
            new BaseDef("armor.thorn_mail", Slot.Armor, Line.Guard, new Txt("棘の鎧", "Thorn Mail"), Stat.MaxHealthFlat, 25),
            new BaseDef("armor.lampkeeper_mantle", Slot.Armor, Line.Resonance, new Txt("灯守の外衣", "Lampkeeper's Mantle"), Stat.HealthRegen, 2),

            new BaseDef("charm.resonance_amulet", Slot.Charm, Line.Resonance, new Txt("共鳴の護符", "Resonance Amulet"), Stat.Haste, 4),
            new BaseDef("charm.tailwind_ring", Slot.Charm, Line.Resonance, new Txt("追い風の指輪", "Tailwind Ring"), Stat.MoveSpeedPct, 4),
            new BaseDef("charm.hunters_seal", Slot.Charm, Line.Offense, new Txt("狩人の印章", "Hunter's Seal"), Stat.CritChancePct, 3),
            new BaseDef("charm.pulsing_core", Slot.Charm, Line.Guard, new Txt("脈打つ核", "Pulsing Core"), Stat.MaxHealthFlat, 20),
            new BaseDef("charm.chain_necklace", Slot.Charm, Line.Guard, new Txt("鎖の首飾り", "Chain Necklace"), Stat.Tenacity, 10),
            new BaseDef("charm.old_clock", Slot.Charm, Line.Offense, new Txt("古き時計", "Old Clock"), Stat.Haste, 6),
            new BaseDef("weapon.frost_spear", Slot.Weapon, Line.Guard, new Txt("霜穂の槍", "Frostspike Spear"), Stat.AttackRangePct, 8),
            new BaseDef("weapon.dusk_scythe", Slot.Weapon, Line.Offense, new Txt("黄昏の大鎌", "Dusk Scythe"), Stat.CritDamagePct, 14),
            new BaseDef("weapon.lantern_rod", Slot.Weapon, Line.Resonance, new Txt("灯火の杖", "Lantern Rod"), Stat.LightAmp, 10),
            new BaseDef("armor.frost_coat", Slot.Armor, Line.Guard, new Txt("霜の上衣", "Frostweave Coat"), Stat.Tenacity, 15),
            new BaseDef("armor.dancer_garb", Slot.Armor, Line.Offense, new Txt("舞手の衣", "Dancer's Garb"), Stat.AttackSpeedPct, 4),
            new BaseDef("armor.star_cloak", Slot.Armor, Line.Resonance, new Txt("星読みの外套", "Stargazer's Cloak"), Stat.PowerFlat, 5),
            new BaseDef("charm.ember_locket", Slot.Charm, Line.Offense, new Txt("残り火のロケット", "Ember Locket"), Stat.FireAmp, 10),
            new BaseDef("charm.moon_bell", Slot.Charm, Line.Resonance, new Txt("月の鈴", "Moon Bell"), Stat.ColdAmp, 10),
            new BaseDef("charm.iron_feather", Slot.Charm, Line.Guard, new Txt("鉄の羽根", "Iron Feather"), Stat.Armor, 6),
            new BaseDef("weapon.hunting_bow", Slot.Weapon, Line.Offense, new Txt("狩人の短弓", "Hunting Shortbow"), Stat.AttackSpeedPct, 5),
            new BaseDef("weapon.war_axe", Slot.Weapon, Line.Offense, new Txt("戦斧", "War Axe"), Stat.AttackFlat, 7),
            new BaseDef("weapon.tower_lance", Slot.Weapon, Line.Guard, new Txt("城壁の槍", "Rampart Lance"), Stat.Armor, 6),
            new BaseDef("weapon.oath_mace", Slot.Weapon, Line.Guard, new Txt("誓いの戦棍", "Oath Mace"), Stat.MaxHealthFlat, 25),
            new BaseDef("weapon.dream_wand", Slot.Weapon, Line.Resonance, new Txt("夢見の杖", "Dreamer's Wand"), Stat.Haste, 6),
            new BaseDef("weapon.star_harp", Slot.Weapon, Line.Resonance, new Txt("星の竪琴", "Star Harp"), Stat.PowerFlat, 7),
            new BaseDef("armor.hunter_leather", Slot.Armor, Line.Offense, new Txt("狩人の革鎧", "Hunter's Leathers"), Stat.CritChancePct, 3),
            new BaseDef("armor.ember_plate", Slot.Armor, Line.Offense, new Txt("燠火の鎧", "Emberforged Plate"), Stat.FireAmp, 10),
            new BaseDef("armor.root_mail", Slot.Armor, Line.Guard, new Txt("根の鎖帷子", "Rootbound Mail"), Stat.HealthRegen, 3),
            new BaseDef("armor.bastion_shell", Slot.Armor, Line.Guard, new Txt("甲羅の鎧", "Shell Armor"), Stat.MaxHealthPct, 6),
            new BaseDef("armor.mist_robe", Slot.Armor, Line.Resonance, new Txt("霧の法衣", "Mist Robe"), Stat.MoveSpeedPct, 4),
            new BaseDef("armor.prayer_shawl", Slot.Armor, Line.Resonance, new Txt("祈りの肩掛け", "Prayer Shawl"), Stat.Haste, 5),
            new BaseDef("charm.fang_necklace", Slot.Charm, Line.Offense, new Txt("牙の首飾り", "Fang Necklace"), Stat.CritDamagePct, 10),
            new BaseDef("charm.war_drum", Slot.Charm, Line.Offense, new Txt("戦太鼓", "War Drum"), Stat.AttackSpeedPct, 4),
            new BaseDef("charm.guardian_seal", Slot.Charm, Line.Guard, new Txt("守護の封印", "Guardian Seal"), Stat.MaxHealthPct, 5),
            new BaseDef("charm.stone_heart", Slot.Charm, Line.Guard, new Txt("石の心臓", "Stone Heart"), Stat.Tenacity, 12),
            new BaseDef("charm.dream_lens", Slot.Charm, Line.Resonance, new Txt("夢見の水晶", "Dreaming Lens"), Stat.PowerFlat, 5),
            new BaseDef("charm.shadow_mask", Slot.Charm, Line.Resonance, new Txt("影の仮面", "Shadow Mask"), Stat.DarkAmp, 10),
            new BaseDef("head.iron_helm", Slot.Head, Line.Guard, new Txt("鉄の兜", "Iron Helm"), Stat.Armor, 6),
            new BaseDef("head.dream_circlet", Slot.Head, Line.Resonance, new Txt("夢見の額冠", "Dreamer's Circlet"), Stat.Haste, 5),
            new BaseDef("head.hunter_hood", Slot.Head, Line.Offense, new Txt("狩人の頭巾", "Hunter's Hood"), Stat.CritChancePct, 3),
            new BaseDef("head.horned_helm", Slot.Head, Line.Offense, new Txt("双角の兜", "Horned Helm"), Stat.AttackFlat, 5),
            new BaseDef("head.sage_hat", Slot.Head, Line.Resonance, new Txt("賢者のとんがり帽", "Sage's Hat"), Stat.PowerFlat, 5),
            new BaseDef("head.mist_veil", Slot.Head, Line.Resonance, new Txt("霧のヴェール", "Veil of Mist"), Stat.MoveSpeedPct, 3),
            new BaseDef("head.warden_visor", Slot.Head, Line.Guard, new Txt("番人の面頬", "Warden's Visor"), Stat.Tenacity, 8),
            new BaseDef("head.ember_crown", Slot.Head, Line.Offense, new Txt("残り火の冠", "Ember Crown"), Stat.FireAmp, 6),
            new BaseDef("head.moon_hood", Slot.Head, Line.Resonance, new Txt("月影の頭巾", "Moonshadow Hood"), Stat.DarkAmp, 6),
            new BaseDef("head.healer_band", Slot.Head, Line.Guard, new Txt("癒し手の鉢巻", "Healer's Band"), Stat.HealthRegen, 2),
            new BaseDef("hands.leather_gloves", Slot.Hands, Line.Offense, new Txt("革の手袋", "Leather Gloves"), Stat.AttackSpeedPct, 4),
            new BaseDef("hands.iron_gauntlets", Slot.Hands, Line.Guard, new Txt("鉄の籠手", "Iron Gauntlets"), Stat.Armor, 6),
            new BaseDef("hands.archer_bracers", Slot.Hands, Line.Offense, new Txt("射手の腕当て", "Archer's Bracers"), Stat.CritDamagePct, 10),
            new BaseDef("hands.spell_gloves", Slot.Hands, Line.Resonance, new Txt("呪文の手袋", "Spellweave Gloves"), Stat.PowerFlat, 5),
            new BaseDef("hands.claw_gauntlets", Slot.Hands, Line.Offense, new Txt("獣爪の手甲", "Beastclaw Gauntlets"), Stat.AttackFlat, 5),
            new BaseDef("hands.frost_mitts", Slot.Hands, Line.Resonance, new Txt("霜の指なし手袋", "Frost Mitts"), Stat.ColdAmp, 6),
            new BaseDef("hands.radiant_wraps", Slot.Hands, Line.Resonance, new Txt("光の手巻き", "Radiant Wraps"), Stat.LightAmp, 6),
            new BaseDef("hands.vigor_grips", Slot.Hands, Line.Guard, new Txt("活力の握り", "Grips of Vigor"), Stat.MaxHealthFlat, 25),
            new BaseDef("hands.quick_fingers", Slot.Hands, Line.Resonance, new Txt("早業の手袋", "Quickfinger Gloves"), Stat.Haste, 5),
            new BaseDef("hands.reach_bracers", Slot.Hands, Line.Offense, new Txt("遠手の腕輪", "Bracers of Reach"), Stat.AttackRangePct, 5),
            new BaseDef("feet.travel_boots", Slot.Feet, Line.Resonance, new Txt("旅の長靴", "Traveler's Boots"), Stat.MoveSpeedPct, 4),
            new BaseDef("feet.iron_greaves", Slot.Feet, Line.Guard, new Txt("鉄の脛当て", "Iron Greaves"), Stat.Armor, 6),
            new BaseDef("feet.dancer_shoes", Slot.Feet, Line.Offense, new Txt("舞い手の靴", "Dancer's Shoes"), Stat.AttackSpeedPct, 3),
            new BaseDef("feet.wind_sandals", Slot.Feet, Line.Resonance, new Txt("風のサンダル", "Wind Sandals"), Stat.Haste, 4),
            new BaseDef("feet.stalker_boots", Slot.Feet, Line.Offense, new Txt("忍び足の靴", "Stalker's Boots"), Stat.CritChancePct, 3),
            new BaseDef("feet.rooted_boots", Slot.Feet, Line.Guard, new Txt("根を張る靴", "Rooted Boots"), Stat.Tenacity, 10),
            new BaseDef("feet.ember_treads", Slot.Feet, Line.Offense, new Txt("残り火の足甲", "Ember Treads"), Stat.FireAmp, 6),
            new BaseDef("feet.pilgrim_boots", Slot.Feet, Line.Guard, new Txt("巡礼の靴", "Pilgrim's Boots"), Stat.HealthRegen, 2),
            new BaseDef("feet.stone_boots", Slot.Feet, Line.Guard, new Txt("岩の重靴", "Stone Boots"), Stat.MaxHealthPct, 5),
            new BaseDef("feet.star_steps", Slot.Feet, Line.Resonance, new Txt("星渡りの靴", "Starstep Boots"), Stat.LightAmp, 6),
            new BaseDef("head.thorn_circlet", Slot.Head, Line.Guard, new Txt("茨の冠", "Thorn Circlet"), Stat.MaxHealthFlat, 25),
            new BaseDef("head.frost_helm", Slot.Head, Line.Resonance, new Txt("霜の兜", "Frost Helm"), Stat.ColdAmp, 6),
            new BaseDef("head.radiant_halo", Slot.Head, Line.Resonance, new Txt("光輪", "Radiant Halo"), Stat.LightAmp, 6),
            new BaseDef("head.scout_goggles", Slot.Head, Line.Offense, new Txt("斥候の遠眼鏡", "Scout's Goggles"), Stat.AttackRangePct, 5),
            new BaseDef("head.berserker_mask", Slot.Head, Line.Offense, new Txt("狂戦士の面", "Berserker's Mask"), Stat.AttackSpeedPct, 4),
            new BaseDef("hands.ember_gauntlets", Slot.Hands, Line.Offense, new Txt("残り火の手甲", "Ember Gauntlets"), Stat.FireAmp, 6),
            new BaseDef("hands.shadow_gloves", Slot.Hands, Line.Offense, new Txt("影縫いの手袋", "Shadowstitch Gloves"), Stat.DarkAmp, 6),
            new BaseDef("hands.healer_hands", Slot.Hands, Line.Guard, new Txt("癒し手の手袋", "Healer's Gloves"), Stat.HealthRegen, 2),
            new BaseDef("hands.stone_fists", Slot.Hands, Line.Guard, new Txt("岩の拳", "Stone Fists"), Stat.MaxHealthPct, 5),
            new BaseDef("hands.duelist_gloves", Slot.Hands, Line.Offense, new Txt("決闘者の手袋", "Duelist's Gloves"), Stat.CritChancePct, 3),
            new BaseDef("feet.frost_boots", Slot.Feet, Line.Resonance, new Txt("氷上の靴", "Ice Skimmers"), Stat.ColdAmp, 6),
            new BaseDef("feet.shadow_slippers", Slot.Feet, Line.Offense, new Txt("影の上履き", "Shadow Slippers"), Stat.DarkAmp, 6),
            new BaseDef("feet.spiked_boots", Slot.Feet, Line.Offense, new Txt("棘付きの長靴", "Spiked Boots"), Stat.AttackFlat, 5),
            new BaseDef("feet.sage_slippers", Slot.Feet, Line.Resonance, new Txt("賢者の室内履き", "Sage's Slippers"), Stat.PowerFlat, 5),
            new BaseDef("feet.guard_sabatons", Slot.Feet, Line.Guard, new Txt("守りの鉄靴", "Guardian Sabatons"), Stat.MaxHealthFlat, 25),

            // v1.21：各枠 +10
            new BaseDef("weapon.moon_sickle", Slot.Weapon, Line.Resonance, new Txt("月鎌", "Moon Sickle"), Stat.DarkAmp, 6),
            new BaseDef("weapon.ember_whip", Slot.Weapon, Line.Offense, new Txt("残り火の鞭", "Ember Whip"), Stat.FireAmp, 6),
            new BaseDef("weapon.glacier_spear", Slot.Weapon, Line.Resonance, new Txt("凍て穂の槍", "Rimefrost Spear"), Stat.ColdAmp, 6),
            new BaseDef("weapon.dawn_scepter", Slot.Weapon, Line.Resonance, new Txt("暁の笏", "Dawn Scepter"), Stat.LightAmp, 6),
            new BaseDef("weapon.iron_halberd", Slot.Weapon, Line.Guard, new Txt("鉄の斧槍", "Iron Halberd"), Stat.Armor, 8),
            new BaseDef("weapon.dream_tome", Slot.Weapon, Line.Resonance, new Txt("夢綴じの書", "Dreambound Tome"), Stat.Haste, 5),
            new BaseDef("weapon.bone_cleaver", Slot.Weapon, Line.Offense, new Txt("骨断ちの大包丁", "Bone Cleaver"), Stat.CritDamagePct, 10),
            new BaseDef("weapon.twin_rapier", Slot.Weapon, Line.Offense, new Txt("双子の細剣", "Twin Rapiers"), Stat.AttackSpeedPct, 4),
            new BaseDef("weapon.pilgrim_staff", Slot.Weapon, Line.Guard, new Txt("巡礼の錫杖", "Pilgrim's Staff"), Stat.HealthRegen, 2),
            new BaseDef("weapon.thunder_hammer", Slot.Weapon, Line.Offense, new Txt("雷鳴の戦鎚", "Thunder Hammer"), Stat.AttackFlat, 6),
            new BaseDef("head.wolf_pelt", Slot.Head, Line.Offense, new Txt("狼の毛皮かぶり", "Wolf Pelt Hood"), Stat.AttackSpeedPct, 4),
            new BaseDef("head.star_diadem", Slot.Head, Line.Resonance, new Txt("星の髪飾り", "Star Diadem"), Stat.PowerFlat, 5),
            new BaseDef("head.plague_mask", Slot.Head, Line.Guard, new Txt("鳥嘴の面", "Beaked Mask"), Stat.Tenacity, 8),
            new BaseDef("head.coral_crown", Slot.Head, Line.Resonance, new Txt("珊瑚の冠", "Coral Crown"), Stat.ColdAmp, 6),
            new BaseDef("head.knight_helm", Slot.Head, Line.Guard, new Txt("騎士の大兜", "Knight's Greathelm"), Stat.MaxHealthPct, 5),
            new BaseDef("head.ash_hood", Slot.Head, Line.Offense, new Txt("灰かぶりの頭巾", "Ashen Hood"), Stat.FireAmp, 6),
            new BaseDef("head.eye_patch", Slot.Head, Line.Offense, new Txt("片目の眼帯", "Marksman's Eyepatch"), Stat.CritChancePct, 3),
            new BaseDef("head.leaf_wreath", Slot.Head, Line.Guard, new Txt("若葉の花冠", "Leaf Wreath"), Stat.HealthRegen, 2),
            new BaseDef("head.void_helm", Slot.Head, Line.Offense, new Txt("虚ろの兜", "Hollow Helm"), Stat.DarkAmp, 6),
            new BaseDef("head.sun_mask", Slot.Head, Line.Resonance, new Txt("日輪の面", "Sunwheel Mask"), Stat.LightAmp, 6),
            new BaseDef("armor.ember_cuirass", Slot.Armor, Line.Offense, new Txt("残り火の胴鎧", "Ember Cuirass"), Stat.FireAmp, 6),
            new BaseDef("armor.frost_robe", Slot.Armor, Line.Resonance, new Txt("霜の法衣", "Frost Robe"), Stat.ColdAmp, 6),
            new BaseDef("armor.scale_coat", Slot.Armor, Line.Guard, new Txt("竜鱗の外套", "Dragonscale Coat"), Stat.Armor, 9),
            new BaseDef("armor.hunter_vest", Slot.Armor, Line.Offense, new Txt("狩人の胴衣", "Hunter's Vest"), Stat.CritChancePct, 3),
            new BaseDef("armor.star_mantle", Slot.Armor, Line.Resonance, new Txt("星屑の肩掛け", "Stardust Mantle"), Stat.PowerFlat, 5),
            new BaseDef("armor.monk_garb", Slot.Armor, Line.Offense, new Txt("修行者の道着", "Ascetic's Garb"), Stat.AttackSpeedPct, 4),
            new BaseDef("armor.shadow_cloak", Slot.Armor, Line.Offense, new Txt("影織りの外套", "Shadowweave Cloak"), Stat.DarkAmp, 6),
            new BaseDef("armor.sun_plate", Slot.Armor, Line.Resonance, new Txt("陽光の鎧", "Sunlit Plate"), Stat.LightAmp, 6),
            new BaseDef("armor.bark_mail", Slot.Armor, Line.Guard, new Txt("樹皮の鎧", "Barkmail"), Stat.HealthRegen, 2),
            new BaseDef("armor.traveler_coat", Slot.Armor, Line.Resonance, new Txt("旅人の外套", "Traveler's Coat"), Stat.Haste, 5),
            new BaseDef("hands.thorn_wraps", Slot.Hands, Line.Guard, new Txt("茨の手巻き", "Thorn Wraps"), Stat.Armor, 6),
            new BaseDef("hands.alchemist_gloves", Slot.Hands, Line.Resonance, new Txt("錬金術師の手袋", "Alchemist's Gloves"), Stat.Haste, 5),
            new BaseDef("hands.bone_knuckles", Slot.Hands, Line.Offense, new Txt("骨の拳当て", "Bone Knuckles"), Stat.CritDamagePct, 10),
            new BaseDef("hands.star_rings", Slot.Hands, Line.Resonance, new Txt("星環の指輪", "Star-Ring Bands"), Stat.LightAmp, 6),
            new BaseDef("hands.oath_gauntlets", Slot.Hands, Line.Guard, new Txt("誓いの籠手", "Oath Gauntlets"), Stat.Tenacity, 8),
            new BaseDef("hands.tide_gloves", Slot.Hands, Line.Resonance, new Txt("潮の手袋", "Tide Gloves"), Stat.ColdAmp, 6),
            new BaseDef("hands.flame_grips", Slot.Hands, Line.Offense, new Txt("炎の握り", "Flame Grips"), Stat.FireAmp, 6),
            new BaseDef("hands.void_claws", Slot.Hands, Line.Offense, new Txt("虚ろの爪", "Hollow Claws"), Stat.DarkAmp, 6),
            new BaseDef("hands.sling_bracers", Slot.Hands, Line.Offense, new Txt("投げ手の腕輪", "Thrower's Bracers"), Stat.AttackRangePct, 5),
            new BaseDef("hands.prayer_beads", Slot.Hands, Line.Guard, new Txt("祈りの数珠", "Prayer Beads"), Stat.MaxHealthPct, 5),
            new BaseDef("feet.wolf_boots", Slot.Feet, Line.Offense, new Txt("狼の毛皮靴", "Wolfskin Boots"), Stat.AttackSpeedPct, 3),
            new BaseDef("feet.tide_sandals", Slot.Feet, Line.Resonance, new Txt("潮のサンダル", "Tide Sandals"), Stat.ColdAmp, 6),
            new BaseDef("feet.knight_sabatons", Slot.Feet, Line.Guard, new Txt("騎士の鉄靴", "Knight's Sabatons"), Stat.Armor, 6),
            new BaseDef("feet.dawn_steps", Slot.Feet, Line.Resonance, new Txt("暁の足取り", "Dawnsteps"), Stat.LightAmp, 6),
            new BaseDef("feet.ash_boots", Slot.Feet, Line.Offense, new Txt("灰踏みの靴", "Ashwalker Boots"), Stat.FireAmp, 6),
            new BaseDef("feet.root_sandals", Slot.Feet, Line.Guard, new Txt("根のサンダル", "Root Sandals"), Stat.HealthRegen, 2),
            new BaseDef("feet.mist_shoes", Slot.Feet, Line.Resonance, new Txt("霧の靴", "Mist Shoes"), Stat.MoveSpeedPct, 4),
            new BaseDef("feet.hunter_boots", Slot.Feet, Line.Offense, new Txt("追跡者の長靴", "Tracker's Boots"), Stat.CritChancePct, 3),
            new BaseDef("feet.iron_clogs", Slot.Feet, Line.Guard, new Txt("鉄の木靴", "Iron Clogs"), Stat.Tenacity, 10),
            new BaseDef("feet.star_slippers", Slot.Feet, Line.Resonance, new Txt("星座の上履き", "Constellation Slippers"), Stat.Haste, 4),
            new BaseDef("charm.hearthstone", Slot.Charm, Line.Offense, new Txt("炉端の石", "Hearthstone"), Stat.FireAmp, 6),
            new BaseDef("charm.frost_pendant", Slot.Charm, Line.Resonance, new Txt("霜の首飾り", "Frost Pendant"), Stat.ColdAmp, 6),
            new BaseDef("charm.sun_brooch", Slot.Charm, Line.Resonance, new Txt("陽光のブローチ", "Sun Brooch"), Stat.LightAmp, 6),
            new BaseDef("charm.shadow_ring", Slot.Charm, Line.Offense, new Txt("影の指輪", "Shadow Ring"), Stat.DarkAmp, 6),
            new BaseDef("charm.hunter_tooth", Slot.Charm, Line.Offense, new Txt("獲物の牙飾り", "Hunter's Fang"), Stat.CritDamagePct, 10),
            new BaseDef("charm.oak_amulet", Slot.Charm, Line.Guard, new Txt("樫の護符", "Oak Amulet"), Stat.MaxHealthPct, 5),
            new BaseDef("charm.clockwork_charm", Slot.Charm, Line.Resonance, new Txt("ぜんまい仕掛けの飾り", "Clockwork Charm"), Stat.Haste, 5),
            new BaseDef("charm.iron_seal", Slot.Charm, Line.Guard, new Txt("鉄の印章", "Iron Seal"), Stat.Armor, 6),
            new BaseDef("charm.feather_token", Slot.Charm, Line.Resonance, new Txt("風切り羽", "Windfeather Token"), Stat.MoveSpeedPct, 3),
            new BaseDef("charm.war_horn", Slot.Charm, Line.Offense, new Txt("戦の角笛", "War Horn"), Stat.AttackFlat, 5),

            // v1.25：各枠 +5
            new BaseDef("weapon.storm_glaive", Slot.Weapon, Line.Offense, new Txt("嵐の薙刀", "Storm Glaive"), Stat.AttackSpeedPct, 4),
            new BaseDef("weapon.bone_flute", Slot.Weapon, Line.Resonance, new Txt("骨の笛", "Bone Flute"), Stat.Haste, 5),
            new BaseDef("weapon.ember_katar", Slot.Weapon, Line.Offense, new Txt("熾火の短刃", "Ember Katar"), Stat.FireAmp, 6),
            new BaseDef("weapon.tide_trident", Slot.Weapon, Line.Resonance, new Txt("潮騒の三叉槍", "Tidal Trident"), Stat.ColdAmp, 6),
            new BaseDef("weapon.vow_mace", Slot.Weapon, Line.Guard, new Txt("誓約の鎚矛", "Vow Mace"), Stat.MaxHealthPct, 5),
            new BaseDef("head.raven_mask", Slot.Head, Line.Offense, new Txt("鴉の面", "Raven Mask"), Stat.DarkAmp, 6),
            new BaseDef("head.lantern_hat", Slot.Head, Line.Resonance, new Txt("灯籠の笠", "Lantern Hat"), Stat.LightAmp, 6),
            new BaseDef("head.iron_coif", Slot.Head, Line.Guard, new Txt("鎖頭巾", "Chain Coif"), Stat.Armor, 6),
            new BaseDef("head.dream_veil", Slot.Head, Line.Resonance, new Txt("夢見の薄衣", "Dreamer's Veil"), Stat.Haste, 5),
            new BaseDef("head.antler_crown", Slot.Head, Line.Offense, new Txt("大角の冠", "Antler Crown"), Stat.AttackFlat, 5),
            new BaseDef("armor.ink_robe", Slot.Armor, Line.Resonance, new Txt("墨染めの衣", "Ink-Dyed Robe"), Stat.PowerFlat, 5),
            new BaseDef("armor.chain_hauberk", Slot.Armor, Line.Guard, new Txt("鎖帷子", "Chain Hauberk"), Stat.Armor, 9),
            new BaseDef("armor.ember_jacket", Slot.Armor, Line.Offense, new Txt("火の粉の革衣", "Cinder Jacket"), Stat.FireAmp, 6),
            new BaseDef("armor.moon_silk", Slot.Armor, Line.Resonance, new Txt("月絹の衣", "Moonsilk Robe"), Stat.DarkAmp, 6),
            new BaseDef("armor.spiked_plate", Slot.Armor, Line.Guard, new Txt("棘付きの板金鎧", "Spiked Plate"), Stat.Tenacity, 10),
            new BaseDef("hands.ice_bracers", Slot.Hands, Line.Resonance, new Txt("氷の腕輪", "Ice Bracers"), Stat.ColdAmp, 6),
            new BaseDef("hands.chain_wraps", Slot.Hands, Line.Offense, new Txt("鎖巻きの拳", "Chain-Wrapped Fists"), Stat.AttackFlat, 5),
            new BaseDef("hands.sun_gauntlets", Slot.Hands, Line.Guard, new Txt("陽光の籠手", "Sunlit Gauntlets"), Stat.LightAmp, 6),
            new BaseDef("hands.thief_gloves", Slot.Hands, Line.Offense, new Txt("盗賊の手袋", "Thief's Gloves"), Stat.CritChancePct, 3),
            new BaseDef("hands.monk_wraps", Slot.Hands, Line.Guard, new Txt("修行者の手巻き", "Ascetic's Wraps"), Stat.HealthRegen, 2),
            new BaseDef("feet.raven_boots", Slot.Feet, Line.Offense, new Txt("鴉羽の長靴", "Raven-Feather Boots"), Stat.DarkAmp, 6),
            new BaseDef("feet.sun_sandals", Slot.Feet, Line.Resonance, new Txt("陽だまりのサンダル", "Sunwarm Sandals"), Stat.LightAmp, 6),
            new BaseDef("feet.chain_greaves", Slot.Feet, Line.Guard, new Txt("鎖の脛当て", "Chain Greaves"), Stat.MaxHealthFlat, 25),
            new BaseDef("feet.storm_boots", Slot.Feet, Line.Offense, new Txt("嵐駆けの靴", "Stormrunner Boots"), Stat.MoveSpeedPct, 4),
            new BaseDef("feet.ember_slippers", Slot.Feet, Line.Offense, new Txt("燠火の上履き", "Embered Slippers"), Stat.FireAmp, 6),
            new BaseDef("charm.raven_feather", Slot.Charm, Line.Offense, new Txt("鴉の風切り羽", "Raven Quill"), Stat.CritDamagePct, 10),
            new BaseDef("charm.lotus_seal", Slot.Charm, Line.Guard, new Txt("蓮の印", "Lotus Seal"), Stat.HealthRegen, 2),
            new BaseDef("charm.storm_bell", Slot.Charm, Line.Resonance, new Txt("嵐の鈴", "Storm Bell"), Stat.Haste, 5),
            new BaseDef("charm.ice_heart", Slot.Charm, Line.Resonance, new Txt("氷の心臓", "Frozen Heart"), Stat.ColdAmp, 6),
            new BaseDef("charm.ink_stone", Slot.Charm, Line.Offense, new Txt("墨の硯", "Ink Stone"), Stat.PowerFlat, 5),

            // v1.29：各枠 +30（土台を倍に。基礎能力は固定値か控えめな%。攻撃力%・魔力%は使わない）
            new BaseDef("weapon.tide_cutter", Slot.Weapon, Line.Offense, new Txt("波切りの刀", "Tidecutter Blade"), Stat.AttackFlat, 6),
            new BaseDef("weapon.rockbreaker", Slot.Weapon, Line.Offense, new Txt("砕岩のハンマー", "Rockbreaker Hammer"), Stat.AttackFlat, 7),
            new BaseDef("weapon.swallow_kodachi", Slot.Weapon, Line.Offense, new Txt("燕返しの小太刀", "Swallow-Turn Kodachi"), Stat.AttackSpeedPct, 4),
            new BaseDef("weapon.hooked_falchion", Slot.Weapon, Line.Offense, new Txt("鉤爪の曲刀", "Hooked Falchion"), Stat.CritChancePct, 3),
            new BaseDef("weapon.severing_axe", Slot.Weapon, Line.Offense, new Txt("断ち切りの大斧", "Severing Greataxe"), Stat.CritDamagePct, 12),
            new BaseDef("weapon.starlit_knives", Slot.Weapon, Line.Offense, new Txt("連星の投剣", "Starlit Throwing Knives"), Stat.AttackRangePct, 6),
            new BaseDef("weapon.whiteheat_estoc", Slot.Weapon, Line.Offense, new Txt("白熱の刺突剣", "Whiteheat Estoc"), Stat.FireAmp, 8),
            new BaseDef("weapon.northwind_axe", Slot.Weapon, Line.Offense, new Txt("北風の戦斧", "Northwind Battleaxe"), Stat.ColdAmp, 8),
            new BaseDef("weapon.sunlit_blade", Slot.Weapon, Line.Offense, new Txt("陽射しの刃", "Sunlit Blade"), Stat.LightAmp, 8),
            new BaseDef("weapon.gloaming_dagger", Slot.Weapon, Line.Offense, new Txt("宵闇の短剣", "Gloaming Dagger"), Stat.DarkAmp, 8),
            new BaseDef("weapon.lightning_pair", Slot.Weapon, Line.Offense, new Txt("雷光の双刃", "Lightning Paired Blades"), Stat.AttackSpeedPct, 5),
            new BaseDef("weapon.evilbreaker_lance", Slot.Weapon, Line.Offense, new Txt("破邪の槍", "Evil-Breaking Lance"), Stat.AttackFlat, 6),
            new BaseDef("weapon.maelstrom_sword", Slot.Weapon, Line.Offense, new Txt("渦潮の剣", "Maelstrom Sword"), Stat.CritDamagePct, 10),
            new BaseDef("weapon.gatehouse_maul", Slot.Weapon, Line.Guard, new Txt("城門の大槌", "Gatehouse Maul"), Stat.Armor, 8),
            new BaseDef("weapon.lighthouse_cudgel", Slot.Weapon, Line.Guard, new Txt("灯台の棍", "Lighthouse Cudgel"), Stat.LightAmp, 6),
            new BaseDef("weapon.tortoise_bokken", Slot.Weapon, Line.Guard, new Txt("亀甲の木刀", "Tortoiseshell Bokken"), Stat.MaxHealthFlat, 30),
            new BaseDef("weapon.thaw_hammer", Slot.Weapon, Line.Guard, new Txt("雪解けの鎚", "Thawhammer"), Stat.HealthRegen, 3),
            new BaseDef("weapon.anchor_cleaver", Slot.Weapon, Line.Guard, new Txt("錨の鉈", "Anchor Cleaver"), Stat.Tenacity, 10),
            new BaseDef("weapon.granite_cosh", Slot.Weapon, Line.Guard, new Txt("御影の戦棍", "Granite Cosh"), Stat.MaxHealthPct, 5),
            new BaseDef("weapon.stillwater_blade", Slot.Weapon, Line.Guard, new Txt("静水の長刀", "Stillwater Longblade"), Stat.Armor, 6),
            new BaseDef("weapon.chanting_wand", Slot.Weapon, Line.Resonance, new Txt("詠唱の短杖", "Chanting Wand"), Stat.PowerFlat, 6),
            new BaseDef("weapon.astral_staff", Slot.Weapon, Line.Resonance, new Txt("星霜の儀杖", "Astral Ritual Staff"), Stat.Haste, 5),
            new BaseDef("weapon.windhowl_staff", Slot.Weapon, Line.Resonance, new Txt("風唸りの杖", "Windhowl Staff"), Stat.PowerFlat, 7),
            new BaseDef("weapon.conch_scepter", Slot.Weapon, Line.Resonance, new Txt("法螺の聖杖", "Conch Scepter"), Stat.ColdAmp, 6),
            new BaseDef("weapon.firefly_staff", Slot.Weapon, Line.Resonance, new Txt("蛍火の灯杖", "Firefly Staff"), Stat.LightAmp, 6),
            new BaseDef("weapon.evening_fan", Slot.Weapon, Line.Resonance, new Txt("夕薫の扇", "Evening Breeze Fan"), Stat.Haste, 6),
            new BaseDef("weapon.starchart_scroll", Slot.Weapon, Line.Resonance, new Txt("星図の巻物", "Star Chart Scroll"), Stat.PowerFlat, 5),
            new BaseDef("weapon.dewclear_wand", Slot.Weapon, Line.Resonance, new Txt("露払いの細杖", "Dewclear Wand"), Stat.HealPower, 5),
            new BaseDef("weapon.starsinger_bow", Slot.Weapon, Line.Resonance, new Txt("奏星の弓", "Star-Singing Bow"), Stat.Haste, 5),
            new BaseDef("weapon.moonlit_cane", Slot.Weapon, Line.Resonance, new Txt("月明の杖", "Moonlit Cane"), Stat.DarkAmp, 6),

            new BaseDef("armor.stormfront_vest", Slot.Armor, Line.Offense, new Txt("嵐の胸当て", "Stormfront Vest"), Stat.AttackSpeedPct, 4),
            new BaseDef("armor.quicksilver_jacket", Slot.Armor, Line.Offense, new Txt("水銀の短上衣", "Quicksilver Jacket"), Stat.CritChancePct, 3),
            new BaseDef("armor.volcanic_coat", Slot.Armor, Line.Offense, new Txt("火山岩の外套", "Volcanic Coat"), Stat.FireAmp, 8),
            new BaseDef("armor.glacier_harness", Slot.Armor, Line.Offense, new Txt("氷河の胸綱", "Glacier Harness"), Stat.ColdAmp, 8),
            new BaseDef("armor.dawnlight_corset", Slot.Armor, Line.Offense, new Txt("暁光の胴衣", "Dawnlight Corset"), Stat.LightAmp, 8),
            new BaseDef("armor.duskweave_jacket", Slot.Armor, Line.Offense, new Txt("黄昏織りの上着", "Duskweave Jacket"), Stat.DarkAmp, 8),
            new BaseDef("armor.ambush_vest", Slot.Armor, Line.Offense, new Txt("奇襲の胴衣", "Ambush Vest"), Stat.CritDamagePct, 10),
            new BaseDef("armor.skirmisher_coat", Slot.Armor, Line.Offense, new Txt("遊撃手の外套", "Skirmisher's Coat"), Stat.AttackFlat, 5),
            new BaseDef("armor.citadel_plate", Slot.Armor, Line.Guard, new Txt("城砦の板金鎧", "Citadel Plate"), Stat.Armor, 10),
            new BaseDef("armor.rampart_jacket", Slot.Armor, Line.Guard, new Txt("土塁の上着", "Rampart Jacket"), Stat.Armor, 7),
            new BaseDef("armor.deeproot_vest", Slot.Armor, Line.Guard, new Txt("深根の胴着", "Deeproot Vest"), Stat.HealthRegen, 3),
            new BaseDef("armor.tidebound_mail", Slot.Armor, Line.Guard, new Txt("潮縛の鎖帷子", "Tidebound Mail"), Stat.MaxHealthFlat, 30),
            new BaseDef("armor.basalt_cuirass", Slot.Armor, Line.Guard, new Txt("玄武の胴鎧", "Basalt Cuirass"), Stat.Tenacity, 12),
            new BaseDef("armor.buckler_vest", Slot.Armor, Line.Guard, new Txt("小盾の胴衣", "Buckler Vest"), Stat.Armor, 6),
            new BaseDef("armor.oathplate_cuirass", Slot.Armor, Line.Guard, new Txt("誓板の胸当て", "Oathplate Cuirass"), Stat.MaxHealthPct, 6),
            new BaseDef("armor.winterwool_coat", Slot.Armor, Line.Guard, new Txt("冬毛の上衣", "Winterwool Coat"), Stat.MaxHealthFlat, 25),
            new BaseDef("armor.ironbark_vest", Slot.Armor, Line.Guard, new Txt("鉄樹の胴衣", "Ironbark Vest"), Stat.Armor, 8),
            new BaseDef("armor.twistedchain_ply", Slot.Armor, Line.Guard, new Txt("ねじれ鎖の腹当て", "Twisted-Chain Ply"), Stat.Tenacity, 10),
            new BaseDef("armor.mirrorsilk_robe", Slot.Armor, Line.Resonance, new Txt("鏡絹の衣", "Mirrorsilk Robe"), Stat.PowerFlat, 5),
            new BaseDef("armor.aurora_wrap", Slot.Armor, Line.Resonance, new Txt("極光の肩掛け", "Aurora Wrap"), Stat.Haste, 5),
            new BaseDef("armor.seafoam_gown", Slot.Armor, Line.Resonance, new Txt("海沫の長衣", "Seafoam Gown"), Stat.ColdAmp, 6),
            new BaseDef("armor.emberweave_shawl", Slot.Armor, Line.Resonance, new Txt("織り火の肩掛け", "Emberweave Shawl"), Stat.FireAmp, 6),
            new BaseDef("armor.candlelight_robe", Slot.Armor, Line.Resonance, new Txt("燭光の法衣", "Candlelight Robe"), Stat.LightAmp, 6),
            new BaseDef("armor.nightloom_robe", Slot.Armor, Line.Resonance, new Txt("夜織りの衣", "Nightloom Robe"), Stat.DarkAmp, 6),
            new BaseDef("armor.swiftstep_coat", Slot.Armor, Line.Resonance, new Txt("疾歩の外套", "Swiftstep Coat"), Stat.MoveSpeedPct, 4),
            new BaseDef("armor.healing_sash", Slot.Armor, Line.Resonance, new Txt("癒しの飾り帯", "Healing Sash"), Stat.HealPower, 5),
            new BaseDef("armor.wardsigil_vest", Slot.Armor, Line.Resonance, new Txt("加護の胴衣", "Wardsigil Vest"), Stat.ShieldPower, 5),
            new BaseDef("armor.summoners_vest", Slot.Armor, Line.Resonance, new Txt("喚び獣の胸当て", "Summoner's Vest"), Stat.SummonPower, 8),
            new BaseDef("armor.zephyr_robe", Slot.Armor, Line.Resonance, new Txt("西風の法衣", "Zephyr Robe"), Stat.Haste, 6),
            new BaseDef("armor.morningdew_robe", Slot.Armor, Line.Resonance, new Txt("朝露の衣", "Morning Dew Robe"), Stat.PowerFlat, 6),

            new BaseDef("head.talon_crown", Slot.Head, Line.Offense, new Txt("爪の冠", "Talon Crown"), Stat.AttackFlat, 5),
            new BaseDef("head.gale_hood", Slot.Head, Line.Offense, new Txt("疾風の頭巾", "Gale Hood"), Stat.AttackSpeedPct, 4),
            new BaseDef("head.magma_band", Slot.Head, Line.Offense, new Txt("熔岩の鉢巻", "Magma Band"), Stat.FireAmp, 8),
            new BaseDef("head.rimebloom_hood", Slot.Head, Line.Offense, new Txt("霜華の頭巾", "Rimebloom Hood"), Stat.ColdAmp, 8),
            new BaseDef("head.noonlight_circlet", Slot.Head, Line.Offense, new Txt("真昼の額冠", "Noonlight Circlet"), Stat.LightAmp, 8),
            new BaseDef("head.eclipse_mask", Slot.Head, Line.Offense, new Txt("日蝕の面", "Eclipse Mask"), Stat.DarkAmp, 8),
            new BaseDef("head.hawkeye_band", Slot.Head, Line.Offense, new Txt("鷹目の鉢巻", "Hawkeye Band"), Stat.CritChancePct, 3),
            new BaseDef("head.longshot_cap", Slot.Head, Line.Offense, new Txt("遠撃の帽子", "Longshot Cap"), Stat.AttackRangePct, 5),
            new BaseDef("head.crimsonlotus_hood", Slot.Head, Line.Offense, new Txt("紅蓮の頭巾", "Crimson Lotus Hood"), Stat.CritDamagePct, 12),
            new BaseDef("head.thunderveil_hood", Slot.Head, Line.Offense, new Txt("雷帷の頭巾", "Thunderveil Hood"), Stat.AttackSpeedPct, 5),
            new BaseDef("head.vulture_hood", Slot.Head, Line.Offense, new Txt("兀鷹のフード", "Vulture Hood"), Stat.AttackFlat, 6),
            new BaseDef("head.sentry_visor", Slot.Head, Line.Guard, new Txt("歩哨の面頬", "Sentry Visor"), Stat.Armor, 7),
            new BaseDef("head.boulder_helm", Slot.Head, Line.Guard, new Txt("岩塊の兜", "Boulder Helm"), Stat.MaxHealthPct, 6),
            new BaseDef("head.moss_crown", Slot.Head, Line.Guard, new Txt("苔の冠", "Moss Crown"), Stat.HealthRegen, 3),
            new BaseDef("head.icewall_helm", Slot.Head, Line.Guard, new Txt("氷壁の兜", "Icewall Helm"), Stat.Armor, 8),
            new BaseDef("head.oathring_circlet", Slot.Head, Line.Guard, new Txt("誓環の額冠", "Oathring Circlet"), Stat.MaxHealthFlat, 30),
            new BaseDef("head.sunkenbell_helm", Slot.Head, Line.Guard, new Txt("沈鐘の兜", "Sunken Bell Helm"), Stat.Tenacity, 12),
            new BaseDef("head.pillar_crown", Slot.Head, Line.Guard, new Txt("柱石の冠", "Pillar Crown"), Stat.MaxHealthFlat, 25),
            new BaseDef("head.fortress_coif", Slot.Head, Line.Guard, new Txt("城塞の鎖頭巾", "Fortress Coif"), Stat.Armor, 6),
            new BaseDef("head.comet_diadem", Slot.Head, Line.Resonance, new Txt("彗星の髪飾り", "Comet Diadem"), Stat.PowerFlat, 5),
            new BaseDef("head.whisper_veil", Slot.Head, Line.Resonance, new Txt("囁きのヴェール", "Whisper Veil"), Stat.Haste, 5),
            new BaseDef("head.tidal_circlet", Slot.Head, Line.Resonance, new Txt("潮汐の額冠", "Tidal Circlet"), Stat.ColdAmp, 6),
            new BaseDef("head.emberbloom_crown", Slot.Head, Line.Resonance, new Txt("火焔花の冠", "Emberbloom Crown"), Stat.FireAmp, 6),
            new BaseDef("head.sunbeam_hood", Slot.Head, Line.Resonance, new Txt("光条の頭巾", "Sunbeam Hood"), Stat.LightAmp, 6),
            new BaseDef("head.starless_veil", Slot.Head, Line.Resonance, new Txt("無星のヴェール", "Starless Veil"), Stat.DarkAmp, 6),
            new BaseDef("head.meditation_band", Slot.Head, Line.Resonance, new Txt("瞑想の鉢巻", "Meditation Band"), Stat.Haste, 6),
            new BaseDef("head.kindly_circlet", Slot.Head, Line.Resonance, new Txt("慈手の額冠", "Kindly Circlet"), Stat.HealPower, 5),
            new BaseDef("head.wardband", Slot.Head, Line.Resonance, new Txt("護符の額帯", "Ward Band"), Stat.ShieldPower, 5),
            new BaseDef("head.beastcaller_antlers", Slot.Head, Line.Resonance, new Txt("呼獣の飾り角", "Beastcaller Antlers"), Stat.SummonPower, 8),
            new BaseDef("head.moonlace_hood", Slot.Head, Line.Resonance, new Txt("月紗の頭巾", "Moonlace Hood"), Stat.PowerFlat, 6),

            new BaseDef("hands.ripgrip_gloves", Slot.Hands, Line.Offense, new Txt("裂握の手袋", "Ripgrip Gloves"), Stat.AttackFlat, 6),
            new BaseDef("hands.swiftpalm_gloves", Slot.Hands, Line.Offense, new Txt("風掌の手袋", "Swift-Palm Gloves"), Stat.AttackSpeedPct, 5),
            new BaseDef("hands.hunting_sinew", Slot.Hands, Line.Offense, new Txt("狩りの筋帯", "Hunting Sinew Wraps"), Stat.CritDamagePct, 12),
            new BaseDef("hands.blazeknit_gloves", Slot.Hands, Line.Offense, new Txt("火織りの手袋", "Blazeknit Gloves"), Stat.FireAmp, 8),
            new BaseDef("hands.frostbite_knuckles", Slot.Hands, Line.Offense, new Txt("凍手の拳当て", "Frostbite Knuckles"), Stat.ColdAmp, 8),
            new BaseDef("hands.sunfire_grips", Slot.Hands, Line.Offense, new Txt("陽炎の握り", "Sunfire Grips"), Stat.LightAmp, 8),
            new BaseDef("hands.nightpalm_gloves", Slot.Hands, Line.Offense, new Txt("夜掌の手袋", "Nightpalm Gloves"), Stat.DarkAmp, 8),
            new BaseDef("hands.precise_fingerless", Slot.Hands, Line.Offense, new Txt("的確な指抜き", "Precise Fingerless Gloves"), Stat.CritChancePct, 3),
            new BaseDef("hands.bowmaster_bracers", Slot.Hands, Line.Offense, new Txt("弓張りの腕当て", "Bowmaster's Bracers"), Stat.AttackRangePct, 6),
            new BaseDef("hands.storm_knuckles", Slot.Hands, Line.Offense, new Txt("嵐の拳当て", "Storm Knuckles"), Stat.AttackSpeedPct, 4),
            new BaseDef("hands.riven_fists", Slot.Hands, Line.Offense, new Txt("裂罅の拳", "Riven Fists"), Stat.AttackFlat, 5),
            new BaseDef("hands.duelist_bracers", Slot.Hands, Line.Offense, new Txt("決闘の腕当て", "Duelist's Bracers"), Stat.CritDamagePct, 10),
            new BaseDef("hands.viperfang_claws", Slot.Hands, Line.Offense, new Txt("毒牙の爪", "Viper Fang Claws"), Stat.CritChancePct, 3),
            new BaseDef("hands.wardens_grips", Slot.Hands, Line.Guard, new Txt("番人の握り", "Warden's Grips"), Stat.Armor, 7),
            new BaseDef("hands.bark_knuckles", Slot.Hands, Line.Guard, new Txt("樹皮の拳当て", "Bark Knuckles"), Stat.HealthRegen, 3),
            new BaseDef("hands.ironvein_gauntlets", Slot.Hands, Line.Guard, new Txt("鉄脈の籠手", "Ironvein Gauntlets"), Stat.Armor, 9),
            new BaseDef("hands.heavypalm_gloves", Slot.Hands, Line.Guard, new Txt("重掌の手袋", "Heavypalm Gloves"), Stat.MaxHealthFlat, 30),
            new BaseDef("hands.bulwark_wraps", Slot.Hands, Line.Guard, new Txt("壁の手巻き", "Bulwark Wraps"), Stat.Tenacity, 12),
            new BaseDef("hands.reef_gauntlets", Slot.Hands, Line.Guard, new Txt("礁の籠手", "Reef Gauntlets"), Stat.Armor, 6),
            new BaseDef("hands.rootgrip_gloves", Slot.Hands, Line.Guard, new Txt("根握りの手袋", "Rootgrip Gloves"), Stat.MaxHealthPct, 6),
            new BaseDef("hands.snowmelt_mitts", Slot.Hands, Line.Guard, new Txt("雪解の指なし", "Snowmelt Mitts"), Stat.HealthRegen, 2),
            new BaseDef("hands.oathpalm_gloves", Slot.Hands, Line.Guard, new Txt("誓掌の手袋", "Oathpalm Gloves"), Stat.ShieldPower, 5),
            new BaseDef("hands.manuscript_gloves", Slot.Hands, Line.Resonance, new Txt("写本の手袋", "Manuscript Gloves"), Stat.PowerFlat, 6),
            new BaseDef("hands.chime_bracers", Slot.Hands, Line.Resonance, new Txt("鈴鳴りの腕輪", "Chime Bracers"), Stat.Haste, 6),
            new BaseDef("hands.tidecaller_wraps", Slot.Hands, Line.Resonance, new Txt("潮呼びの手巻き", "Tidecaller Wraps"), Stat.ColdAmp, 6),
            new BaseDef("hands.cinderthread_wraps", Slot.Hands, Line.Resonance, new Txt("火糸の手巻き", "Cinderthread Wraps"), Stat.FireAmp, 6),
            new BaseDef("hands.lantern_fingerless", Slot.Hands, Line.Resonance, new Txt("灯火の指抜き", "Lantern Fingerless Gloves"), Stat.LightAmp, 6),
            new BaseDef("hands.duskstitch_gloves", Slot.Hands, Line.Resonance, new Txt("黄昏縫いの手袋", "Duskstitch Gloves"), Stat.DarkAmp, 6),
            new BaseDef("hands.mender_palms", Slot.Hands, Line.Resonance, new Txt("癒し掌の手袋", "Mender's Palms"), Stat.HealPower, 5),
            new BaseDef("hands.summoner_bands", Slot.Hands, Line.Resonance, new Txt("喚び手の指輪", "Summoner's Bands"), Stat.SummonPower, 8),

            new BaseDef("feet.blitz_treads", Slot.Feet, Line.Offense, new Txt("電光の足甲", "Blitz Treads"), Stat.AttackSpeedPct, 4),
            new BaseDef("feet.emberdash_boots", Slot.Feet, Line.Offense, new Txt("火駆けの長靴", "Emberdash Boots"), Stat.FireAmp, 8),
            new BaseDef("feet.froststride_shoes", Slot.Feet, Line.Offense, new Txt("霜踏みの靴", "Froststride Shoes"), Stat.ColdAmp, 8),
            new BaseDef("feet.sunspur_boots", Slot.Feet, Line.Offense, new Txt("陽蹴りの靴", "Sunspur Boots"), Stat.LightAmp, 8),
            new BaseDef("feet.duskstep_boots", Slot.Feet, Line.Offense, new Txt("宵踏みの靴", "Duskstep Boots"), Stat.DarkAmp, 8),
            new BaseDef("feet.hunter_striders", Slot.Feet, Line.Offense, new Txt("追撃の脚当て", "Hunter's Striders"), Stat.CritChancePct, 3),
            new BaseDef("feet.quicksilver_greaves", Slot.Feet, Line.Offense, new Txt("水銀の脛当て", "Quicksilver Greaves"), Stat.MoveSpeedPct, 4),
            new BaseDef("feet.deadeye_leggings", Slot.Feet, Line.Offense, new Txt("必中の脚絆", "Deadeye Leggings"), Stat.CritDamagePct, 10),
            new BaseDef("feet.wolfstride_boots", Slot.Feet, Line.Offense, new Txt("狼歩の長靴", "Wolfstride Boots"), Stat.AttackFlat, 5),
            new BaseDef("feet.gale_greaves", Slot.Feet, Line.Offense, new Txt("烈風の脛当て", "Gale Greaves"), Stat.AttackSpeedPct, 5),
            new BaseDef("feet.thorntread_sabatons", Slot.Feet, Line.Offense, new Txt("棘踏みの足甲", "Thorntread Sabatons"), Stat.AttackRangePct, 5),
            new BaseDef("feet.bastion_sabatons", Slot.Feet, Line.Guard, new Txt("城塞の鉄鞋", "Bastion Sabatons"), Stat.Armor, 8),
            new BaseDef("feet.deeproot_boots", Slot.Feet, Line.Guard, new Txt("深根の長靴", "Deeproot Boots"), Stat.HealthRegen, 3),
            new BaseDef("feet.stoneguard_sabatons", Slot.Feet, Line.Guard, new Txt("石衛の鉄鞋", "Stoneguard Sabatons"), Stat.MaxHealthPct, 6),
            new BaseDef("feet.tortoiseshell_greaves", Slot.Feet, Line.Guard, new Txt("亀甲の脛当て", "Tortoiseshell Greaves"), Stat.MaxHealthFlat, 30),
            new BaseDef("feet.ironwave_greaves", Slot.Feet, Line.Guard, new Txt("鉄波の脛当て", "Ironwave Greaves"), Stat.Armor, 6),
            new BaseDef("feet.ballast_boots", Slot.Feet, Line.Guard, new Txt("沈錘の長靴", "Ballast Boots"), Stat.Tenacity, 12),
            new BaseDef("feet.rampart_greaves", Slot.Feet, Line.Guard, new Txt("土塁の脛当て", "Rampart Greaves"), Stat.Armor, 7),
            new BaseDef("feet.winterhide_boots", Slot.Feet, Line.Guard, new Txt("冬毛の長靴", "Winterhide Boots"), Stat.MaxHealthFlat, 25),
            new BaseDef("feet.wardstep_sandals", Slot.Feet, Line.Guard, new Txt("護りの草鞋", "Wardstep Sandals"), Stat.ShieldPower, 5),
            new BaseDef("feet.mistral_sandals", Slot.Feet, Line.Resonance, new Txt("突風のサンダル", "Mistral Sandals"), Stat.MoveSpeedPct, 4),
            new BaseDef("feet.starlit_moccasins", Slot.Feet, Line.Resonance, new Txt("星履の靴", "Starlit Moccasins"), Stat.Haste, 5),
            new BaseDef("feet.tidepool_sandals", Slot.Feet, Line.Resonance, new Txt("潮溜まりのサンダル", "Tidepool Sandals"), Stat.ColdAmp, 6),
            new BaseDef("feet.firebloom_slippers", Slot.Feet, Line.Resonance, new Txt("火華の上履き", "Firebloom Slippers"), Stat.FireAmp, 6),
            new BaseDef("feet.dawnmist_shoes", Slot.Feet, Line.Resonance, new Txt("暁霧の靴", "Dawnmist Shoes"), Stat.LightAmp, 6),
            new BaseDef("feet.nightveil_slippers", Slot.Feet, Line.Resonance, new Txt("夜帳の上履き", "Nightveil Slippers"), Stat.DarkAmp, 6),
            new BaseDef("feet.mender_shoes", Slot.Feet, Line.Resonance, new Txt("癒しの靴", "Mender's Shoes"), Stat.HealPower, 5),
            new BaseDef("feet.cometstride_shoes", Slot.Feet, Line.Resonance, new Txt("彗星の靴", "Comet-Stride Shoes"), Stat.Haste, 6),
            new BaseDef("feet.whisperweave_shoes", Slot.Feet, Line.Resonance, new Txt("囁き織りの靴", "Whisperweave Shoes"), Stat.PowerFlat, 5),
            new BaseDef("feet.beastpaw_boots", Slot.Feet, Line.Resonance, new Txt("獣趾の長靴", "Beastpaw Boots"), Stat.SummonPower, 8),

            new BaseDef("charm.talon_pendant", Slot.Charm, Line.Offense, new Txt("爪の垂飾り", "Talon Pendant"), Stat.AttackFlat, 5),
            new BaseDef("charm.blaze_brooch", Slot.Charm, Line.Offense, new Txt("焔のブローチ", "Blaze Brooch"), Stat.FireAmp, 8),
            new BaseDef("charm.glacier_charm", Slot.Charm, Line.Offense, new Txt("氷晶の飾り", "Glacier Charm"), Stat.ColdAmp, 8),
            new BaseDef("charm.sunspoke_pin", Slot.Charm, Line.Offense, new Txt("陽光的ピン", "Sunspoke Pin"), Stat.LightAmp, 8),
            new BaseDef("charm.duskbead_necklace", Slot.Charm, Line.Offense, new Txt("宵珠の首飾り", "Duskbead Necklace"), Stat.DarkAmp, 8),
            new BaseDef("charm.keeneye_charm", Slot.Charm, Line.Offense, new Txt("鋭眼の御守り", "Keen-Eye Charm"), Stat.CritChancePct, 3),
            new BaseDef("charm.stormcloud_locket", Slot.Charm, Line.Offense, new Txt("雷雲のロケット", "Stormcloud Locket"), Stat.AttackSpeedPct, 5),
            new BaseDef("charm.pursuit_badge", Slot.Charm, Line.Offense, new Txt("追撃の徽章", "Pursuit Badge"), Stat.CritDamagePct, 12),
            new BaseDef("charm.skirmish_ring", Slot.Charm, Line.Offense, new Txt("遊撃の指輪", "Skirmish Ring"), Stat.AttackFlat, 6),
            new BaseDef("charm.longshot_charm", Slot.Charm, Line.Offense, new Txt("遠当ての御守り", "Longshot Charm"), Stat.AttackRangePct, 6),
            new BaseDef("charm.garnet_ring", Slot.Charm, Line.Offense, new Txt("紅玉の指輪", "Garnet Ring"), Stat.CritDamagePct, 10),
            new BaseDef("charm.hearthside_ring", Slot.Charm, Line.Guard, new Txt("囲炉裏の指輪", "Hearthside Ring"), Stat.HealthRegen, 3),
            new BaseDef("charm.bulwark_seal", Slot.Charm, Line.Guard, new Txt("壁の印章", "Bulwark Seal"), Stat.Armor, 7),
            new BaseDef("charm.deeproot_charm", Slot.Charm, Line.Guard, new Txt("深根の護符", "Deeproot Charm"), Stat.MaxHealthPct, 6),
            new BaseDef("charm.tidewarden_brooch", Slot.Charm, Line.Guard, new Txt("潮衛のブローチ", "Tidewarden Brooch"), Stat.Armor, 6),
            new BaseDef("charm.boulder_pendant", Slot.Charm, Line.Guard, new Txt("岩塊の首飾り", "Boulder Pendant"), Stat.MaxHealthFlat, 30),
            new BaseDef("charm.ironknot_ring", Slot.Charm, Line.Guard, new Txt("鉄結びの指輪", "Ironknot Ring"), Stat.Tenacity, 12),
            new BaseDef("charm.snowbloom_charm", Slot.Charm, Line.Guard, new Txt("雪華の御守り", "Snowbloom Charm"), Stat.ShieldPower, 5),
            new BaseDef("charm.winteroak_charm", Slot.Charm, Line.Guard, new Txt("冬樫の護符", "Winter Oak Charm"), Stat.MaxHealthFlat, 25),
            new BaseDef("charm.comet_pendant", Slot.Charm, Line.Resonance, new Txt("彗星の垂飾り", "Comet Pendant"), Stat.PowerFlat, 6),
            new BaseDef("charm.seafoam_ring", Slot.Charm, Line.Resonance, new Txt("海沫の指輪", "Seafoam Ring"), Stat.ColdAmp, 6),
            new BaseDef("charm.cindercore_locket", Slot.Charm, Line.Resonance, new Txt("火芯のロケット", "Cindercore Locket"), Stat.FireAmp, 6),
            new BaseDef("charm.halo_charm", Slot.Charm, Line.Resonance, new Txt("光環の御守り", "Halo Charm"), Stat.LightAmp, 6),
            new BaseDef("charm.eclipse_ring", Slot.Charm, Line.Resonance, new Txt("蝕の指輪", "Eclipse Ring"), Stat.DarkAmp, 6),
            new BaseDef("charm.windchime_charm", Slot.Charm, Line.Resonance, new Txt("風鈴の飾り", "Wind Chime Charm"), Stat.Haste, 5),
            new BaseDef("charm.mender_locket", Slot.Charm, Line.Resonance, new Txt("癒しのロケット", "Mender's Locket"), Stat.HealPower, 5),
            new BaseDef("charm.beasttongue_charm", Slot.Charm, Line.Resonance, new Txt("獣語の護符", "Beasttongue Charm"), Stat.SummonPower, 8),
            new BaseDef("charm.zephyr_ring", Slot.Charm, Line.Resonance, new Txt("西風の指輪", "Zephyr Ring"), Stat.MoveSpeedPct, 3),
            new BaseDef("charm.dawnsilk_band", Slot.Charm, Line.Resonance, new Txt("暁糸の腕輪", "Dawnsilk Band"), Stat.Haste, 6),
            new BaseDef("charm.stardust_pendant", Slot.Charm, Line.Resonance, new Txt("星屑の垂飾り", "Stardust Pendant"), Stat.PowerFlat, 5),
        };

        public static readonly IReadOnlyList<UniqueDef> Uniques = new[]
        {
            new UniqueDef("unique.endless_dance", "weapon.chain_sword", new Txt("終わらない舞", "Endless Dance"),
                new Txt("止まらなければ、夢は覚めない。", "As long as you never stop, the dream never ends."),
                Power.Momentum, 6, Power.Tailwind, 20),
            new UniqueDef("unique.dreameater", "weapon.blaze_greatsword", new Txt("夢喰いの大剣", "Dreameater"),
                new Txt("燃やした悪夢の分だけ、持ち主は満たされる。", "Every nightmare it burns feeds its wielder."),
                Power.Blaze, 80, Power.Lifesteal, 8),
            new UniqueDef("unique.wrathscale", "armor.thorn_mail", new Txt("逆鱗の鎧", "Wrathscale"),
                new Txt("触れたものに、触れた代償を。", "Whoever touches it, pays for the touch."),
                Power.Thorns, 40, Power.Retaliation, 25),
            new UniqueDef("unique.unbroken", "armor.guardian_plate", new Txt("不落の胸当て", "The Unbroken"),
                new Txt("囲まれるほど、灯は強く燃える。", "The more they surround it, the brighter it burns."),
                Power.Bulwark, 35, Power.Barrier, 10),
            new UniqueDef("unique.starbinder", "charm.resonance_amulet", new Txt("星を結ぶ護符", "Starbinder"),
                new Txt("二つの灯が並ぶとき、星図は一つになる。", "When two lights stand together, their star maps become one."),
                Power.Resonance, 10, Power.SecondWind, 30),
            new UniqueDef("unique.headsman", "charm.hunters_seal", new Txt("処刑人の印章", "Headsman's Seal"),
                new Txt("弱った獲物を、狩人は見逃さない。", "A hunter never lets wounded prey escape."),
                Power.Executioner, 50, Power.Momentum, 4),
            new UniqueDef("unique.thunder_fangs", "weapon.twin_fang", new Txt("雷鳴の双牙", "Thunderfangs"),
                new Txt("一つ斬れば、群れごと痺れる。", "Cut one, and the whole pack trembles."),
                Power.ChainLightning, 60, Power.Momentum, 4),
            new UniqueDef("unique.shattered_star", "charm.pulsing_core", new Txt("砕けた星核", "Shattered Starcore"),
                new Txt("倒れた悪夢は、星屑になって弾ける。", "Fallen nightmares burst into stardust."),
                Power.Shatter, 70, Power.Tailwind, 20),
            new UniqueDef("unique.warding_spirit", "armor.resonant_robe", new Txt("守護霊の衣", "Shroud of the Warding Spirit"),
                new Txt("深い傷ほど、誰かがそっと手を添える。", "The deeper the wound, the gentler the hand that covers it."),
                Power.Aegis, 25, Power.Barrier, 8),
            new UniqueDef("unique.bloodied_maul", "weapon.shield_maul", new Txt("血塗れの大槌", "Bloodied Maul"),
                new Txt("追い詰められた獣ほど、よく暴れる。", "A cornered beast fights the hardest."),
                Power.Bloodlust, 30, Power.Lifesteal, 10),

            new UniqueDef("unique.prism_clock", "charm.old_clock", new Txt("四元の時計", "Prismatic Clock"),
                new Txt("四つの夢が重なる瞬間、時は砕ける。", "When four dreams overlap, time itself shatters."),
                Power.Convergence, 150, Power.Frost, 45),
            new UniqueDef("unique.afterimage_cloak", "armor.flowing_cloak", new Txt("残像の外套", "Afterimage Cloak"),
                new Txt("避けた先に、もう次の記憶が待っている。", "Where you dodge to, your next memory is already waiting."),
                Power.EchoingDodge, 98, Power.Tailwind, 20),
            new UniqueDef("unique.dawnbreaker", "weapon.calming_staff", new Txt("暁を呼ぶ杖", "Dawncaller"),
                new Txt("三つ重なった光は、決して外れない。", "Light stacked thrice never misses."),
                Power.Radiance, 100, Power.UltimateSurge, 20),
            // 本体の旅人ごとの専用固有品（キットに効く固有効果の組み合わせ。誰でも装備できる）
            new UniqueDef("unique.sig.vesper", "weapon.blaze_greatsword", new Txt("審問官の誓剣", "Inquisitor's Oathblade"),
                new Txt("四度目の祈りは、必ず届く。（Vesper）", "The fourth prayer always lands. (Vesper)"),
                Power.Blaze, 70, Power.Retaliation, 20),
            new UniqueDef("unique.sig.lacerta", "weapon.longspike_bow", new Txt("サラマンダーの銃身", "Salamander Barrel"),
                new Txt("火薬は多いほど良い。（Lacerta）", "More powder is always better. (Lacerta)"),
                Power.Ember, 60, Power.Blaze, 60),
            new UniqueDef("unique.sig.cetus", "armor.guardian_plate", new Txt("深海の外殻", "Abyssal Carapace"),
                new Txt("凍てつく水底に、揺らがぬ殻がある。（Cetus）", "On the frozen seabed rests an unshaken shell. (Cetus)"),
                Power.Frost, 52, Power.Barrier, 8),
            new UniqueDef("unique.sig.yubar", "charm.old_clock", new Txt("星屑の写本", "Stardust Codex"),
                new Txt("記憶を使うたび、星が集まる。（Yubar）", "Every memory used gathers another star. (Yubar)"),
                Power.UltimateSurge, 25, Power.Radiance, 75),
            new UniqueDef("unique.sig.husk", "weapon.twin_fang", new Txt("空殻の牙", "Hollow Fang"),
                new Txt("影の中では、急所しか見えない。（空殻）", "In the shadows, only weak points are visible. (Husk)"),
                Power.Umbra, 100, Power.Executioner, 40),
            new UniqueDef("unique.sig.mist", "weapon.chain_sword", new Txt("霧払いの太刀", "Mistcutter"),
                new Txt("避けた一閃が、次の一閃を呼ぶ。（Mist）", "Each evaded strike calls the next. (Mist)"),
                Power.EchoingDodge, 98, Power.Momentum, 4),
            new UniqueDef("unique.sig.nachia", "charm.resonance_amulet", new Txt("絆の鈴", "Bell of Bonds"),
                new Txt("呼べば応える。光が、仲間が。（Nachia）", "Call, and they answer: the light, and your friends. (Nachia)"),
                Power.Resonance, 10, Power.Radiance, 75),
            new UniqueDef("unique.sig.aurena", "armor.lampkeeper_mantle", new Txt("黄金の聖杯", "Golden Chalice"),
                new Txt("注いだ血は、光となって還る。（Aurena）", "The blood you pour returns as light. (Aurena)"),
                Power.SecondWind, 30, Power.Bloodlust, 25),
            new UniqueDef("unique.sig.bismuth", "charm.pulsing_core", new Txt("四冊目の物語", "The Fourth Tale"),
                new Txt("三つの物語が揃えば、四つ目が始まる。（Bismuth）", "When three tales meet, the fourth begins. (Bismuth)"),
                Power.Convergence, 120, Power.Ember, 38),
            new UniqueDef("unique.glacier_lance", "weapon.frost_spear", new Txt("氷河の槍", "Glacier Lance"),
                new Txt("凍りついた敵は、もう逃げられない。", "A frozen foe has nowhere left to run."),
                Power.Frost, 60, Power.Bulwark, 25),
            new UniqueDef("unique.moon_reaper", "weapon.dusk_scythe", new Txt("月喰いの鎌", "Moon Reaper"),
                new Txt("欠けた月の夜にだけ、刃は研がれる。", "Its edge is honed only on waning-moon nights."),
                Power.Umbra, 100, Power.Bloodlust, 18),
            new UniqueDef("unique.lighthouse", "weapon.lantern_rod", new Txt("夜明けの灯台", "Lighthouse of Dawn"),
                new Txt("迷った夢を、光が岸まで導く。", "Its light guides lost dreams back to shore."),
                Power.Radiance, 88, Power.Barrier, 10),
            new UniqueDef("unique.winter_vow", "armor.frost_coat", new Txt("冬の誓い", "Winter's Vow"),
                new Txt("凍えるほど、守る意志は固くなる。", "The colder it gets, the firmer the resolve."),
                Power.Frost, 45, Power.Aegis, 25),
            new UniqueDef("unique.whirling_veil", "armor.dancer_garb", new Txt("渦巻く舞衣", "Whirling Veil"),
                new Txt("止まらない舞は、刃より速い。", "A dance that never stops outpaces any blade."),
                Power.Momentum, 6, Power.EchoingDodge, 78),
            new UniqueDef("unique.astral_mantle", "armor.star_cloak", new Txt("天球の外套", "Astral Mantle"),
                new Txt("星の巡りを読めば、切り札はいつでも手の中に。", "Read the turning stars, and your trump card is always at hand."),
                Power.UltimateSurge, 25, Power.Resonance, 8),
            new UniqueDef("unique.phoenix_locket", "charm.ember_locket", new Txt("不死鳥のロケット", "Phoenix Locket"),
                new Txt("燃え尽きたと思ったときが、始まりだ。", "The moment you think you have burned out is when it begins."),
                Power.Ember, 60, Power.SecondWind, 30),
            new UniqueDef("unique.moonlit_bell", "charm.moon_bell", new Txt("月夜の鈴", "Moonlit Bell"),
                new Txt("鳴るたびに、冷たい雷が走る。", "Each chime sends cold lightning running."),
                Power.Frost, 52, Power.ChainLightning, 50),
            new UniqueDef("unique.iron_wing", "charm.iron_feather", new Txt("鉄翼", "Iron Wing"),
                new Txt("羽ばたくたびに、刃を弾く。", "Every beat of its wings turns a blade aside."),
                Power.Thorns, 30, Power.Bulwark, 30),
            new UniqueDef("unique.storm_caller", "weapon.chain_sword", new Txt("嵐を呼ぶ剣", "Stormcaller"),
                new Txt("振るえば、空が応える。", "Swing it, and the sky answers."),
                Power.ChainLightning, 70, Power.Tailwind, 20),
            new UniqueDef("unique.hungering_dark", "armor.resonant_robe", new Txt("飢える闇", "Hungering Dark"),
                new Txt("闇は、与えた傷の分だけ満たされる。", "The dark is filled by every wound it gives."),
                Power.Umbra, 88, Power.Lifesteal, 8),
            new UniqueDef("unique.last_bastion", "armor.guardian_plate", new Txt("最後の砦", "Last Bastion"),
                new Txt("倒れる寸前こそ、本当の戦いだ。", "The real fight begins just before you fall."),
                Power.Bloodlust, 30, Power.Retaliation, 30),
            new UniqueDef("unique.gale_bow", "weapon.hunting_bow", new Txt("疾風の弓", "Galestring Bow"),
                new Txt("風より速く、矢は獲物を見つける。", "Faster than the wind, the arrow finds its prey."),
                Power.OpeningStrike, 40, Power.Tailwind, 20),
            new UniqueDef("unique.skypiercer", "weapon.longspike_bow", new Txt("天穿ち", "Skypiercer"),
                new Txt("狙った星は、必ず落ちる。", "Any star it aims at will fall."),
                Power.Executioner, 45, Power.ChainLightning, 50),
            new UniqueDef("unique.headhunter_axe", "weapon.war_axe", new Txt("首狩りの斧", "Headhunter's Axe"),
                new Txt("斧は、弱った首から覚えていく。", "The axe learns the weakest necks first."),
                Power.Executioner, 50, Power.Bloodlust, 25),
            new UniqueDef("unique.wildfire_axe", "weapon.war_axe", new Txt("野火の戦斧", "Wildfire Axe"),
                new Txt("一振りで、草原は炎の海になる。", "One swing turns the plains into a sea of fire."),
                Power.Ember, 60, Power.Shatter, 60),
            new UniqueDef("unique.rampart", "weapon.tower_lance", new Txt("城壁の守り槍", "Rampart Guard"),
                new Txt("槍の後ろには、誰一人通さない。", "No one passes behind this spear."),
                Power.Bulwark, 35, Power.Thorns, 30),
            new UniqueDef("unique.oathkeeper", "weapon.oath_mace", new Txt("誓いを守る者", "Oathkeeper"),
                new Txt("立てた誓いが、傷をふさぐ。", "The oaths you swore close your wounds."),
                Power.SecondWind, 30, Power.Barrier, 8),
            new UniqueDef("unique.judgement", "weapon.oath_mace", new Txt("裁きの戦棍", "Mace of Judgement"),
                new Txt("罪の重さだけ、一撃は重くなる。", "Each blow weighs as much as the sin."),
                Power.Retaliation, 30, Power.Shatter, 50),
            new UniqueDef("unique.daydream_wand", "weapon.dream_wand", new Txt("白昼夢の杖", "Daydream Wand"),
                new Txt("目を開けたまま、夢を振るう。", "It wields dreams with eyes wide open."),
                Power.EchoingDodge, 78, Power.UltimateSurge, 20),
            new UniqueDef("unique.lullaby", "weapon.star_harp", new Txt("子守唄の竪琴", "Lullaby Harp"),
                new Txt("敵を眠らせる調べが、仲間を奮い立たせる。", "A melody that lulls foes to sleep rouses friends."),
                Power.Resonance, 10, Power.Barrier, 8),
            new UniqueDef("unique.constellation", "weapon.star_harp", new Txt("星座を奏でる竪琴", "Constellation Harp"),
                new Txt("弦を弾くたび、星が一つ増える。", "Every plucked string adds a star to the sky."),
                Power.Radiance, 100, Power.Convergence, 100),
            new UniqueDef("unique.frost_fang", "weapon.twin_fang", new Txt("霜牙", "Frostfang"),
                new Txt("噛まれた傷は、凍えて塞がらない。", "Its bite freezes and never closes."),
                Power.Frost, 52, Power.Momentum, 4),
            new UniqueDef("unique.eclipse_scythe", "weapon.dusk_scythe", new Txt("日蝕の鎌", "Eclipse Scythe"),
                new Txt("光が消えた一瞬に、すべてを刈る。", "In the instant the light dies, it reaps all."),
                Power.Umbra, 88, Power.Shatter, 60),
            new UniqueDef("unique.siegebreaker", "weapon.shield_maul", new Txt("攻城槌", "Siegebreaker"),
                new Txt("壁を砕く力は、守るためにある。", "The strength to break walls exists to protect."),
                Power.Bulwark, 30, Power.Retaliation, 25),
            new UniqueDef("unique.phoenix_blade", "weapon.blaze_greatsword", new Txt("鳳凰の大剣", "Phoenix Greatsword"),
                new Txt("倒れても、炎は何度でも立ち上がる。", "Even fallen, the flame rises again and again."),
                Power.Blaze, 60, Power.SecondWind, 25),
            new UniqueDef("unique.tidal_sword", "weapon.chain_sword", new Txt("潮流の剣", "Tidal Sword"),
                new Txt("引いては寄せる、終わりのない連撃。", "Strikes that ebb and flow without end."),
                Power.Frost, 45, Power.Tailwind, 20),
            new UniqueDef("unique.sunlit_rod", "weapon.lantern_rod", new Txt("陽光の杖", "Sunlit Rod"),
                new Txt("朝日は、どんな悪夢も照らし出す。", "The morning sun lays every nightmare bare."),
                Power.Radiance, 100, Power.Ember, 38),
            new UniqueDef("unique.mirror_staff", "weapon.calming_staff", new Txt("鏡の杖", "Mirror Staff"),
                new Txt("受けた力を、そのまま映し返す。", "It reflects back every force it receives."),
                Power.Thorns, 35, Power.Aegis, 25),
            new UniqueDef("unique.void_wand", "weapon.dream_wand", new Txt("虚無の杖", "Void Wand"),
                new Txt("何もない場所から、闇があふれ出す。", "Darkness spills from where nothing was."),
                Power.Umbra, 100, Power.Resonance, 8),
            new UniqueDef("unique.stalker_leather", "armor.hunter_leather", new Txt("追跡者の革鎧", "Stalker's Leathers"),
                new Txt("足音は、獲物が倒れるまで消えない。", "Its footsteps fade only when the prey falls."),
                Power.Executioner, 40, Power.Tailwind, 20),
            new UniqueDef("unique.blood_hide", "armor.hunter_leather", new Txt("血染めの毛皮", "Bloodstained Hide"),
                new Txt("浴びた血が、次の狩りを急かす。", "The blood it soaks up urges the next hunt."),
                Power.Bloodlust, 30, Power.Lifesteal, 8),
            new UniqueDef("unique.forgeheart", "armor.ember_plate", new Txt("鍛冶場の心臓", "Forgeheart Plate"),
                new Txt("打たれるほど、熱く固くなる。", "The more it is struck, the hotter and harder it gets."),
                Power.Retaliation, 30, Power.Ember, 45),
            new UniqueDef("unique.cinder_mail", "armor.ember_plate", new Txt("燃え殻の鎧", "Cinder Mail"),
                new Txt("炎が消えたあとにも、熱は残る。", "The heat remains after the fire dies."),
                Power.Blaze, 50, Power.Thorns, 30),
            new UniqueDef("unique.ancient_root", "armor.root_mail", new Txt("古木の根", "Ancient Root"),
                new Txt("根は、倒れた者さえ支える。", "Roots hold up even the fallen."),
                Power.SecondWind, 30, Power.Bulwark, 25),
            new UniqueDef("unique.mossveil", "armor.root_mail", new Txt("苔むした帷子", "Mossveil Mail"),
                new Txt("静かに、確かに、傷は癒える。", "Quietly and surely, wounds heal."),
                Power.Lifesteal, 10, Power.Barrier, 8),
            new UniqueDef("unique.turtle_king", "armor.bastion_shell", new Txt("亀王の甲羅", "Shell of the Turtle King"),
                new Txt("千年の甲羅は、どんな一撃も忘れない。", "A thousand-year shell forgets no blow."),
                Power.Aegis, 25, Power.Thorns, 35),
            new UniqueDef("unique.mistwalker", "armor.mist_robe", new Txt("霧を歩む者", "Mistwalker Robe"),
                new Txt("霧の中では、誰も影を踏めない。", "In the mist, no one can step on your shadow."),
                Power.EchoingDodge, 98, Power.Umbra, 75),
            new UniqueDef("unique.dew_robe", "armor.mist_robe", new Txt("朝露の法衣", "Morning Dew Robe"),
                new Txt("夜明けの雫が、仲間の傷を洗う。", "Drops of dawn wash your allies' wounds."),
                Power.Resonance, 8, Power.Barrier, 8),
            new UniqueDef("unique.pilgrim_shawl", "armor.prayer_shawl", new Txt("巡礼の肩掛け", "Pilgrim's Shawl"),
                new Txt("歩いた道が長いほど、祈りは深くなる。", "The longer the road walked, the deeper the prayer."),
                Power.SecondWind, 25, Power.UltimateSurge, 20),
            new UniqueDef("unique.saint_shawl", "armor.prayer_shawl", new Txt("聖女の肩掛け", "Saint's Shawl"),
                new Txt("祈りは、仲間を包む光になる。", "Prayer becomes a light that wraps your allies."),
                Power.Barrier, 10, Power.Radiance, 75),
            new UniqueDef("unique.thundercloud", "armor.flowing_cloak", new Txt("雷雲の外套", "Thundercloud Cloak"),
                new Txt("風に乗って、雷は群れを渡る。", "Riding the wind, lightning leaps through the pack."),
                Power.ChainLightning, 60, Power.Tailwind, 20),
            new UniqueDef("unique.reprisal", "armor.counter_gauntlets", new Txt("報復の籠手", "Gauntlets of Reprisal"),
                new Txt("殴られた分だけ、殴り返す。", "It returns every blow it takes."),
                Power.Retaliation, 30, Power.Momentum, 4),
            new UniqueDef("unique.nightwatch", "armor.lampkeeper_mantle", new Txt("夜番の外衣", "Nightwatch Mantle"),
                new Txt("灯が消えるまで、見張りは終わらない。", "The watch ends only when the lamp goes out."),
                Power.Barrier, 10, Power.Umbra, 75),
            new UniqueDef("unique.thorn_queen", "armor.thorn_mail", new Txt("茨の女王", "Thorn Queen"),
                new Txt("近づく者すべてに、棘の口づけを。", "A thorny kiss for all who come near."),
                Power.Thorns, 40, Power.Bloodlust, 25),
            new UniqueDef("unique.flame_dancer", "armor.dancer_garb", new Txt("炎の舞衣", "Flame Dancer's Garb"),
                new Txt("舞うたびに、火の粉が散る。", "Sparks scatter with every step of the dance."),
                Power.Ember, 52, Power.Momentum, 5),
            new UniqueDef("unique.starfall_cloak", "armor.star_cloak", new Txt("星降る外套", "Starfall Cloak"),
                new Txt("夜空ごと、敵の上に降らせる。", "It brings the whole night sky down on foes."),
                Power.Convergence, 120, Power.Radiance, 75),
            new UniqueDef("unique.blizzard_coat", "armor.frost_coat", new Txt("吹雪の上衣", "Blizzard Coat"),
                new Txt("吹雪の中では、すべてが凍りつく。", "In the blizzard, everything freezes."),
                Power.Frost, 60, Power.Shatter, 50),
            new UniqueDef("unique.wolf_fang", "charm.fang_necklace", new Txt("狼王の牙", "Wolf King's Fang"),
                new Txt("群れの長は、最後まで噛みつく。", "The pack leader bites to the very end."),
                Power.Bloodlust, 30, Power.Executioner, 40),
            new UniqueDef("unique.viper_fang", "charm.fang_necklace", new Txt("毒蛇の牙", "Viper's Fang"),
                new Txt("小さな傷が、やがて命を奪う。", "A tiny wound in time takes a life."),
                Power.Umbra, 88, Power.OpeningStrike, 40),
            new UniqueDef("unique.battle_drum", "charm.war_drum", new Txt("鬨の太鼓", "Battle-Cry Drum"),
                new Txt("太鼓が鳴れば、足は止まらない。", "When the drum sounds, no foot stands still."),
                Power.Momentum, 6, Power.Resonance, 8),
            new UniqueDef("unique.thunder_drum", "charm.war_drum", new Txt("雷鼓", "Thunder Drum"),
                new Txt("打つたびに、空が裂ける。", "Each beat splits the sky."),
                Power.ChainLightning, 60, Power.Shatter, 50),
            new UniqueDef("unique.citadel_seal", "charm.guardian_seal", new Txt("城塞の封印", "Citadel Seal"),
                new Txt("封じたのは、敵ではなく恐れだ。", "What it seals is not the enemy, but fear."),
                Power.Aegis, 25, Power.Bulwark, 30),
            new UniqueDef("unique.mothers_seal", "charm.guardian_seal", new Txt("母なる封印", "Mother's Seal"),
                new Txt("見えない腕が、そっと抱きとめる。", "Unseen arms gently catch you."),
                Power.SecondWind, 30, Power.Lifesteal, 8),
            new UniqueDef("unique.golem_heart", "charm.stone_heart", new Txt("ゴーレムの心臓", "Golem Heart"),
                new Txt("石の鼓動は、決して乱れない。", "A heartbeat of stone never falters."),
                Power.Bulwark, 35, Power.Retaliation, 25),
            new UniqueDef("unique.living_stone", "charm.stone_heart", new Txt("生きた石", "Living Stone"),
                new Txt("砕かれても、また形を取り戻す。", "Even shattered, it takes shape again."),
                Power.Barrier, 10, Power.Thorns, 30),
            new UniqueDef("unique.oracle_lens", "charm.dream_lens", new Txt("予言者の水晶", "Oracle's Lens"),
                new Txt("次の一手が、もう見えている。", "The next move is already in sight."),
                Power.UltimateSurge, 25, Power.EchoingDodge, 65),
            new UniqueDef("unique.prism_lens", "charm.dream_lens", new Txt("虹の水晶", "Prism Lens"),
                new Txt("一つの光が、四つの夢に分かれる。", "One light splits into four dreams."),
                Power.Convergence, 120, Power.Radiance, 62),
            new UniqueDef("unique.jester_mask", "charm.shadow_mask", new Txt("道化の仮面", "Jester's Mask"),
                new Txt("笑わせているうちに、背後を取る。", "While they laugh, it takes their back."),
                Power.EchoingDodge, 98, Power.Fetters, 15),
            new UniqueDef("unique.nightmare_mask", "charm.shadow_mask", new Txt("悪夢の仮面", "Nightmare Mask"),
                new Txt("仮面の下には、もっと深い闇がある。", "Beneath the mask lies a deeper dark."),
                Power.Umbra, 100, Power.Bloodlust, 25),
            new UniqueDef("unique.sun_locket", "charm.ember_locket", new Txt("太陽のロケット", "Sun Locket"),
                new Txt("小さな太陽が、胸の中で燃えている。", "A tiny sun burns within your chest."),
                Power.Ember, 60, Power.Radiance, 75),
            new UniqueDef("unique.tidebell", "charm.moon_bell", new Txt("潮騒の鈴", "Tidebell"),
                new Txt("波の音が、疲れを洗い流す。", "The sound of waves washes fatigue away."),
                Power.Frost, 45, Power.Resonance, 8),
            new UniqueDef("unique.windcutter", "charm.iron_feather", new Txt("風切り羽根", "Windcutter Feather"),
                new Txt("羽ばたき一つで、戦場を駆け抜ける。", "A single flap carries you across the field."),
                Power.Tailwind, 20, Power.Sprint, 20),
            new UniqueDef("unique.hourglass", "charm.old_clock", new Txt("時の砂時計", "Hourglass of Ages"),
                new Txt("落ちる砂が、切り札を早める。", "The falling sand hastens your trump card."),
                Power.UltimateSurge, 25, Power.Momentum, 4),
            new UniqueDef("unique.throbbing_core", "charm.pulsing_core", new Txt("鼓動する核", "Throbbing Core"),
                new Txt("鼓動が速まるほど、力が湧く。", "The faster it beats, the stronger you get."),
                Power.Bloodlust, 30, Power.SoulSiphon, 15),
            new UniqueDef("unique.chain_choker", "charm.chain_necklace", new Txt("鎖の首輪", "Chain Choker"),
                new Txt("縛られた者ほど、強く抗う。", "The more bound, the harder it resists."),
                Power.Retaliation, 30, Power.Aegis, 20),
            new UniqueDef("unique.lucky_ring", "charm.tailwind_ring", new Txt("幸運の指輪", "Lucky Ring"),
                new Txt("追い風は、勝者にだけ吹く。", "The tailwind blows only for the victor."),
                Power.Tailwind, 20, Power.Vigor, 14),
            new UniqueDef("unique.mark_of_prey", "charm.hunters_seal", new Txt("獲物の刻印", "Mark of the Prey"),
                new Txt("刻まれた獲物は、逃げられない。", "Marked prey cannot escape."),
                Power.Executioner, 45, Power.SoulSiphon, 15),
            new UniqueDef("unique.choir_amulet", "charm.resonance_amulet", new Txt("合唱の護符", "Choir Amulet"),
                new Txt("声が重なるほど、力は大きくなる。", "The more voices join, the greater the power."),
                Power.Resonance, 10, Power.UltimateSurge, 20),
            new UniqueDef("unique.meteor_bow", "weapon.hunting_bow", new Txt("流星の弓", "Meteor Bow"),
                new Txt("放った矢は、星となって降り注ぐ。", "Loosed arrows rain down as stars."),
                Power.Radiance, 88, Power.Shatter, 55),
            new UniqueDef("unique.bloodaxe", "weapon.war_axe", new Txt("血斧", "Bloodaxe"),
                new Txt("血を浴びるほど、刃は鋭くなる。", "The more blood it bathes in, the sharper its edge."),
                Power.Lifesteal, 10, Power.Momentum, 5),
            new UniqueDef("unique.icewall_lance", "weapon.tower_lance", new Txt("氷壁の槍", "Icewall Lance"),
                new Txt("凍った壁の向こうへ、敵は届かない。", "No foe reaches past the frozen wall."),
                Power.Frost, 52, Power.Aegis, 20),
            new UniqueDef("unique.slumber_staff", "weapon.calming_staff", new Txt("眠りの杖", "Slumber Staff"),
                new Txt("安らかな眠りが、仲間を守る盾になる。", "Peaceful sleep becomes a shield for allies."),
                Power.Barrier, 10, Power.Overload, 18),
            new UniqueDef("unique.tempest_harp", "weapon.star_harp", new Txt("嵐の竪琴", "Tempest Harp"),
                new Txt("激しい調べが、雷を呼ぶ。", "A fierce melody calls down lightning."),
                Power.ChainLightning, 60, Power.Resonance, 8),
            new UniqueDef("unique.bone_mail", "armor.guardian_plate", new Txt("骨の鎧", "Bone Mail"),
                new Txt("倒した者の骨が、新しい盾になる。", "The bones of the fallen become a new shield."),
                Power.Shatter, 50, Power.Bulwark, 25),
            new UniqueDef("unique.robe_of_winds", "armor.resonant_robe", new Txt("風の衣", "Robe of Winds"),
                new Txt("風をまとえば、刃は届かない。", "Clad in wind, no blade can reach you."),
                Power.Tailwind, 20, Power.Whirlwind, 60),
            new UniqueDef("unique.dragonscale", "armor.bastion_shell", new Txt("竜鱗の鎧", "Dragonscale Armor"),
                new Txt("竜の鱗は、炎さえ跳ね返す。", "Dragon scales repel even flame."),
                Power.Thorns, 35, Power.Bulwark, 25),
            new UniqueDef("unique.shadowstitch", "armor.flowing_cloak", new Txt("影縫いの外套", "Shadowstitch Cloak"),
                new Txt("影を縫い留めれば、敵は動けない。", "Stitch down the shadow, and the foe cannot move."),
                Power.Umbra, 88, Power.Frost, 45),
            new UniqueDef("unique.soul_lantern", "weapon.lantern_rod", new Txt("魂の灯籠", "Soul Lantern"),
                new Txt("消えた灯が、持ち主の命になる。", "Every light that goes out becomes the bearer's life."),
                Power.SoulSiphon, 15, Power.Radiance, 75),
            new UniqueDef("unique.cyclone_cloak", "armor.flowing_cloak", new Txt("竜巻の外套", "Cyclone Cloak"),
                new Txt("身をかわすたび、嵐が生まれる。", "Every sidestep gives birth to a storm."),
                Power.Whirlwind, 70, Power.EchoingDodge, 65),
            new UniqueDef("unique.brawler_gauntlets", "armor.counter_gauntlets", new Txt("乱闘者の籠手", "Brawler's Gauntlets"),
                new Txt("囲まれてからが、本番だ。", "The real fight starts once you are surrounded."),
                Power.Frenzy, 3, Power.Bulwark, 25),
            new UniqueDef("unique.first_light", "weapon.longspike_bow", new Txt("一番星の弓", "Bow of the First Star"),
                new Txt("最初の一矢が、すべてを決める。", "The first arrow decides everything."),
                Power.OpeningStrike, 50, Power.Radiance, 62),
            new UniqueDef("unique.starward_mantle", "armor.star_cloak", new Txt("星守りの外套", "Starward Mantle"),
                new Txt("切り札を切るとき、星が身を守る。", "When you play your trump card, the stars guard you."),
                Power.StarShield, 20, Power.UltimateSurge, 20),
            new UniqueDef("unique.hare_boots", "armor.dancer_garb", new Txt("白兎の舞衣", "White Hare Garb"),
                new Txt("跳ねるように逃げ、跳ねるように戻る。", "Bound away, bound back."),
                Power.Sprint, 25, Power.EchoingDodge, 78),
            new UniqueDef("unique.unbowed_crown", "charm.guardian_seal", new Txt("屈せぬ冠", "Unbowed Crown"),
                new Txt("傷ひとつない者は、恐れを知らない。", "One without a scratch knows no fear."),
                Power.Vigor, 16, Power.Barrier, 8),
            new UniqueDef("unique.overload_core", "charm.pulsing_core", new Txt("過負荷の核", "Overload Core"),
                new Txt("力を使うほど、次の力が満ちる。", "The more power you spend, the more fills the next."),
                Power.Overload, 20, Power.UltimateSurge, 15),
            new UniqueDef("unique.reaper_harvest", "weapon.dusk_scythe", new Txt("刈り入れの鎌", "Harvest Scythe"),
                new Txt("刈った命は、刃の主に還る。", "The lives it reaps return to its master."),
                Power.SoulSiphon, 20, Power.Executioner, 40),
            new UniqueDef("unique.tempest_dancer", "charm.war_drum", new Txt("嵐舞の太鼓", "Drum of the Storm Dance"),
                new Txt("跳ぶたび、太鼓が雷を呼ぶ。", "Every leap makes the drum call lightning."),
                Power.Whirlwind, 60, Power.Sprint, 15),
            new UniqueDef("unique.berserker_axe", "weapon.war_axe", new Txt("狂戦士の斧", "Berserker's Axe"),
                new Txt("敵が多いほど、斧は軽くなる。", "The more foes, the lighter the axe."),
                Power.Frenzy, 3, Power.Bloodlust, 25),
            new UniqueDef("unique.dawn_herald", "weapon.hunting_bow", new Txt("暁の伝令", "Herald of Dawn"),
                new Txt("夜明けの一矢は、まだ眠る敵を貫く。", "The dawn arrow pierces foes still half asleep."),
                Power.OpeningStrike, 45, Power.Vigor, 12),
            new UniqueDef("unique.last_star_crown", "head.dream_circlet", new Txt("終わりの星冠", "Crown of the Last Star"),
                new Txt("最後に輝く星は、いちばん明るい。", "The last star to shine is the brightest."),
                Power.StarShield, 18, Power.UltimateSurge, 22),
            new UniqueDef("unique.overflowing_mind", "head.sage_hat", new Txt("溢れる思索", "Overflowing Mind"),
                new Txt("考えが止まらないなら、止めなければいい。", "If your thoughts will not stop, let them run."),
                Power.Overload, 18, Power.SecondWind, 25),
            new UniqueDef("unique.unburnt_king", "head.ember_crown", new Txt("燃え尽きぬ王", "The Unburnt King"),
                new Txt("玉座は灰になった。王冠だけが燃え続けている。", "The throne is ash. Only the crown still burns."),
                Power.Ember, 48, Power.Blaze, 60),
            new UniqueDef("unique.moonless_hood", "head.moon_hood", new Txt("月なき夜の頭巾", "Hood of the Moonless Night"),
                new Txt("月のない夜は、狩る者の味方をする。", "A moonless night sides with the hunter."),
                Power.Umbra, 80, Power.Fetters, 15),
            new UniqueDef("unique.wardens_oath", "head.warden_visor", new Txt("番人の誓い", "Warden's Oath"),
                new Txt("ここを通りたければ、まず私を倒せ。", "To pass, you must first get through me."),
                Power.Bulwark, 28, Power.Vigor, 14),
            new UniqueDef("unique.mist_bride", "head.mist_veil", new Txt("霧の花嫁", "Bride of the Mist"),
                new Txt("触れようとした手は、いつも霧をつかむ。", "Every hand that reaches for her grasps only mist."),
                Power.Sprint, 22, Power.EchoingDodge, 65),
            new UniqueDef("unique.raging_horns", "head.horned_helm", new Txt("怒れる双角", "Raging Horns"),
                new Txt("囲まれるほど、角は熱くなる。", "The more foes close in, the hotter the horns burn."),
                Power.Frenzy, 3, Power.Bloodlust, 18),
            new UniqueDef("unique.first_dawn", "head.healer_band", new Txt("最初の夜明け", "The First Dawn"),
                new Txt("長い夢にも、朝は来る。", "Even the longest dream has a morning."),
                Power.Barrier, 9, Power.Radiance, 75),
            new UniqueDef("unique.headsman_grip", "hands.claw_gauntlets", new Txt("断頭人の握り", "Headsman's Grip"),
                new Txt("弱った獲物を、この爪は逃さない。", "These claws never let a weakened prey slip away."),
                Power.Executioner, 45, Power.Momentum, 5),
            new UniqueDef("unique.storm_fingers", "hands.spell_gloves", new Txt("嵐を呼ぶ指", "Stormcalling Fingers"),
                new Txt("指を鳴らせば、空が応える。", "Snap your fingers, and the sky answers."),
                Power.ChainLightning, 48, Power.Overload, 16),
            new UniqueDef("unique.frostbite", "hands.frost_mitts", new Txt("凍傷", "Frostbite"),
                new Txt("凍らせて、砕く。それだけのこと。", "Freeze it, then break it. Nothing more."),
                Power.Frost, 51, Power.Shatter, 55),
            new UniqueDef("unique.first_arrow", "hands.archer_bracers", new Txt("一番矢", "The First Arrow"),
                new Txt("戦いは、最初の一射で決まる。", "A battle is decided by the first shot."),
                Power.OpeningStrike, 48, Power.Executioner, 30),
            new UniqueDef("unique.dawnwrap", "hands.radiant_wraps", new Txt("夜明けの手巻き", "Dawnwrap"),
                new Txt("光を巻いた拳は、四つの色を呼び寄せる。", "A fist wrapped in light calls all four colors."),
                Power.Radiance, 85, Power.Convergence, 110),
            new UniqueDef("unique.heartbeat_grips", "hands.vigor_grips", new Txt("鼓動の握り", "Heartbeat Grips"),
                new Txt("強く握るほど、心臓も強く打つ。", "The tighter the grip, the stronger the heartbeat."),
                Power.Vigor, 15, Power.Lifesteal, 8),
            new UniqueDef("unique.thousand_cuts", "hands.leather_gloves", new Txt("千の切り傷", "A Thousand Cuts"),
                new Txt("一つひとつは浅くても、千を重ねれば深い。", "Each cut is shallow; a thousand run deep."),
                Power.Frenzy, 3, Power.Momentum, 4),
            new UniqueDef("unique.far_hand", "hands.reach_bracers", new Txt("遠き手", "The Far Hand"),
                new Txt("届かない場所など、ほとんどない。", "There is almost nowhere it cannot reach."),
                Power.OpeningStrike, 40, Power.Sprint, 18),
            new UniqueDef("unique.whirling_steps", "feet.dancer_shoes", new Txt("旋風の足取り", "Whirling Steps"),
                new Txt("舞い手が通った後には、風だけが残る。", "Where the dancer passed, only wind remains."),
                Power.Whirlwind, 75, Power.EchoingDodge, 65),
            new UniqueDef("unique.windchaser", "feet.wind_sandals", new Txt("風を追う者", "Windchaser"),
                new Txt("風より先に着けば、風は追い風になる。", "Arrive before the wind, and it becomes your tailwind."),
                Power.Sprint, 24, Power.Tailwind, 22),
            new UniqueDef("unique.rooted_oath", "feet.rooted_boots", new Txt("根付く誓い", "Rooted Oath"),
                new Txt("一歩も退かない。根は、退き方を知らない。", "Not one step back. Roots do not know how."),
                Power.Bulwark, 28, Power.Thorns, 28),
            new UniqueDef("unique.firewalker", "feet.ember_treads", new Txt("火渡り", "Firewalker"),
                new Txt("燃える道を選ぶ者にだけ、道は開ける。", "The path opens only for those who choose to walk through fire."),
                Power.Ember, 45, Power.Whirlwind, 60),
            new UniqueDef("unique.silent_step", "feet.stalker_boots", new Txt("音なき足", "Silent Step"),
                new Txt("気づいたときには、もう背後にいる。", "By the time they notice, you are already behind them."),
                Power.OpeningStrike, 45, Power.Umbra, 70),
            new UniqueDef("unique.pilgrims_end", "feet.pilgrim_boots", new Txt("巡礼の終わり", "Pilgrim's End"),
                new Txt("長い旅の終わりに、倒した者の数だけ祈りがある。", "At the journey's end, a prayer for every fallen foe."),
                Power.SoulSiphon, 18, Power.SecondWind, 28),
            new UniqueDef("unique.mountain_stride", "feet.stone_boots", new Txt("山の歩み", "Mountain Stride"),
                new Txt("山は動かない。動くときは、すべてを押し流す。", "A mountain does not move. When it does, it moves everything."),
                Power.Aegis, 18, Power.Retaliation, 24),
            new UniqueDef("unique.star_wanderer", "feet.star_steps", new Txt("星を渡る者", "Star Wanderer"),
                new Txt("星と星のあいだにも、道はある。", "There are roads between the stars, too."),
                Power.StarShield, 16, Power.Tailwind, 20),
            new UniqueDef("unique.thorn_crown", "head.thorn_circlet", new Txt("茨の王冠", "Crown of Thorns"),
                new Txt("王の痛みは、触れた者にも分け与えられる。", "The king's pain is shared with all who touch him."),
                Power.Thorns, 30, Power.Retaliation, 22),
            new UniqueDef("unique.halo_of_mercy", "head.radiant_halo", new Txt("慈悲の光輪", "Halo of Mercy"),
                new Txt("この光の下では、誰も独りで倒れない。", "Beneath this light, no one falls alone."),
                Power.Radiance, 80, Power.Resonance, 8),
            new UniqueDef("unique.far_sight", "head.scout_goggles", new Txt("千里眼", "Far Sight"),
                new Txt("見えているなら、もう当たっている。", "If you can see it, you have already hit it."),
                Power.OpeningStrike, 45, Power.Executioner, 35),
            new UniqueDef("unique.frozen_thought", "head.frost_helm", new Txt("凍てつく思考", "Frozen Thought"),
                new Txt("冷えた頭は、決して慌てない。", "A cold head never panics."),
                Power.Frost, 48, Power.Aegis, 16),
            new UniqueDef("unique.ash_hands", "hands.ember_gauntlets", new Txt("灰の手", "Hands of Ash"),
                new Txt("触れたものは、すべて灰になる。", "Everything it touches turns to ash."),
                Power.Blaze, 65, Power.Ember, 42),
            new UniqueDef("unique.shadow_stitch", "hands.shadow_gloves", new Txt("影縫い", "Shadowstitch"),
                new Txt("影を縫い止めれば、本体も動けない。", "Pin the shadow, and its owner cannot move."),
                Power.Umbra, 80, Power.Frenzy, 2),
            new UniqueDef("unique.mending_touch", "hands.healer_hands", new Txt("繕いの手", "Mending Touch"),
                new Txt("傷は、触れるそばから閉じていく。", "Wounds close as soon as it touches them."),
                Power.Lifesteal, 9, Power.SecondWind, 26),
            new UniqueDef("unique.duelists_promise", "hands.duelist_gloves", new Txt("決闘者の約束", "Duelist's Promise"),
                new Txt("一対一なら、負けたことはない。", "One on one, it has never lost."),
                Power.Momentum, 5, Power.OpeningStrike, 40),
            new UniqueDef("unique.icewalker", "feet.frost_boots", new Txt("氷を歩む者", "Icewalker"),
                new Txt("氷の上こそ、もっとも速く走れる。", "On ice, you run fastest of all."),
                Power.Frost, 45, Power.Sprint, 20),
            new UniqueDef("unique.shadow_dancer", "feet.shadow_slippers", new Txt("影踊り", "Shadow Dancer"),
                new Txt("影は、踊り手の足元から離れない。", "The shadow never leaves the dancer's feet."),
                Power.Umbra, 75, Power.EchoingDodge, 65),
            new UniqueDef("unique.spiked_charge", "feet.spiked_boots", new Txt("棘の突進", "Spiked Charge"),
                new Txt("止まれと言われて、止まったことがない。", "Told to stop, it never has."),
                Power.Whirlwind, 70, Power.Thorns, 25),
            new UniqueDef("unique.quiet_study", "feet.sage_slippers", new Txt("静かな書斎", "The Quiet Study"),
                new Txt("急がない者ほど、遠くまで行ける。", "Those who do not hurry go the farthest."),
                Power.Overload, 18, Power.Vigor, 12),
            // v1.21：各枠の固有品を増やす
            new UniqueDef("unique.hungry_pack", "head.wolf_pelt", new Txt("飢えた群れ", "The Hungry Pack"),
                new Txt("一頭倒すたび、群れは速くなる。", "Every kill makes the pack run faster."),
                Power.Momentum, 5, Power.Bloodlust, 20),
            new UniqueDef("unique.stargazer_diadem", "head.star_diadem", new Txt("星見の髪飾り", "Stargazer Diadem"),
                new Txt("仲間と見上げる星は、守りにもなる。", "Stars watched beside allies become a shield."),
                Power.Resonance, 9, Power.StarShield, 18),
            new UniqueDef("unique.plague_physician", "head.plague_mask", new Txt("疫病医の面", "Plague Physician's Mask"),
                new Txt("病を診る目は、命の流れも見逃さない。", "An eye that reads sickness never misses the flow of life."),
                Power.Umbra, 75, Power.SoulSiphon, 18),
            new UniqueDef("unique.tidepool_crown", "head.coral_crown", new Txt("潮だまりの冠", "Tidepool Crown"),
                new Txt("冷たい潮が、傷口を静かに閉ざす。", "The cold tide quietly closes every wound."),
                Power.Frost, 48, Power.Barrier, 9),
            new UniqueDef("unique.oathbound_helm", "head.knight_helm", new Txt("誓いの大兜", "Oathbound Greathelm"),
                new Txt("退かぬ者の前で、刃は鈍る。", "Blades dull before one who never retreats."),
                Power.Bulwark, 30, Power.Aegis, 18),
            new UniqueDef("unique.ashen_hood", "head.ash_hood", new Txt("灰燼の頭巾", "Hood of Ashes"),
                new Txt("灰の中の火種が、群れごと弾け飛ぶ。", "Embers in the ash burst across the whole pack."),
                Power.Ember, 48, Power.Shatter, 55),
            new UniqueDef("unique.closed_eye", "head.eye_patch", new Txt("閉ざした眼", "The Closed Eye"),
                new Txt("見えぬ目が、最初の一撃を見極める。", "The unseen eye judges the very first strike."),
                Power.OpeningStrike, 48, Power.Vigor, 14),
            new UniqueDef("unique.spring_wreath", "head.leaf_wreath", new Txt("春待つ花冠", "Wreath of Waiting Spring"),
                new Txt("傷んでも、若葉はまた芽吹く。", "Even when bruised, young leaves sprout again."),
                Power.SecondWind, 28, Power.Lifesteal, 9),
            new UniqueDef("unique.hollow_will", "head.void_helm", new Txt("虚ろな意志", "Hollow Will"),
                new Txt("空っぽの器に、闇が魔力を満たす。", "Darkness fills the empty vessel with power."),
                Power.Umbra, 82, Power.Overload, 18),
            new UniqueDef("unique.noon_mask", "head.sun_mask", new Txt("真昼の面", "Mask of High Noon"),
                new Txt("大技を放つ瞬間、日輪が輝く。", "The sun blazes at the moment you unleash your Ultimate."),
                Power.Radiance, 80, Power.UltimateSurge, 24),
            new UniqueDef("unique.old_soldier_helm", "head.iron_helm", new Txt("老兵の兜", "Veteran's Helm"),
                new Txt("数多の戦を越えた鉄は、割れない。", "Iron that has survived countless wars does not crack."),
                Power.Bulwark, 28, Power.Barrier, 9),
            new UniqueDef("unique.stalking_hood", "head.hunter_hood", new Txt("追い込みの頭巾", "Hood of the Final Chase"),
                new Txt("弱った獲物を仕留め、すぐ次へ駆ける。", "Finish the wounded prey, then run to the next."),
                Power.Executioner, 45, Power.Tailwind, 22),
            new UniqueDef("unique.carnage_mask", "head.berserker_mask", new Txt("修羅の面", "Mask of Carnage"),
                new Txt("囲まれ、斬るほどに刃が走る。", "Surrounded, every cut makes your blade run faster."),
                Power.Frenzy, 3, Power.Momentum, 5),
            new UniqueDef("unique.lucid_circlet", "head.dream_circlet", new Txt("明晰夢の額冠", "Lucid Dream Circlet"),
                new Txt("術を放ち、身をかわし、また術を放つ。", "Cast, dodge, and cast again."),
                Power.Overload, 18, Power.EchoingDodge, 72),
            new UniqueDef("unique.cornered_beast", "head.horned_helm", new Txt("手負いの獣", "Wounded Beast"),
                new Txt("追い詰められた獣ほど、牙を剥く。", "The more cornered the beast, the fiercer its fangs."),
                Power.Retaliation, 24, Power.Bloodlust, 20),
            new UniqueDef("unique.stormweaver_hat", "head.sage_hat", new Txt("雷織りの帽子", "Stormweaver Hat"),
                new Txt("呪文の合間に、雷が枝分かれして走る。", "Between spells, lightning forks and runs."),
                Power.ChainLightning, 48, Power.Overload, 18),
            new UniqueDef("unique.veil_of_gales", "head.mist_veil", new Txt("風裂きの面紗", "Veil of Gales"),
                new Txt("身をかわせば霧が渦を巻き、敵を呑む。", "Dodge, and the mist spirals to swallow your foes."),
                Power.Whirlwind, 75, Power.Sprint, 24),
            new UniqueDef("unique.stone_sentinel", "head.warden_visor", new Txt("石の番兵", "Stone Sentinel"),
                new Txt("殴られても怯まず、殴った者に返す。", "Struck, you do not flinch; you give it back."),
                Power.Aegis, 20, Power.Thorns, 28),
            new UniqueDef("unique.hearth_crown", "head.ember_crown", new Txt("熾火の冠", "Crown of Embers"),
                new Txt("燃える炎が、持ち主の傷を温める。", "The burning flame warms its wearer's wounds."),
                Power.Ember, 50, Power.Lifesteal, 9),
            new UniqueDef("unique.winter_moon_hood", "head.moon_hood", new Txt("寒月の頭巾", "Hood of the Cold Moon"),
                new Txt("冷たい月の下、影も凍りつく。", "Beneath the cold moon, even shadows freeze."),
                Power.Frost, 48, Power.Umbra, 75),
            new UniqueDef("unique.lifeline_band", "head.healer_band", new Txt("命綱の鉢巻", "Lifeline Headband"),
                new Txt("倒れかけても、命綱が引き戻す。", "Even as you fall, the lifeline pulls you back."),
                Power.SecondWind, 28, Power.SoulSiphon, 18),
            new UniqueDef("unique.bleeding_crown", "head.thorn_circlet", new Txt("流血の王冠", "Crown of Bleeding"),
                new Txt("血を流すほど、茨は鋭く食い込む。", "The more you bleed, the deeper the thorns bite."),
                Power.Thorns, 32, Power.Bloodlust, 19),
            new UniqueDef("unique.glacier_mind", "head.frost_helm", new Txt("氷の叡智", "Glacial Wisdom"),
                new Txt("冷えた頭で温存し、一気に解き放つ。", "Keep a cool head, then release it all at once."),
                Power.Frost, 50, Power.UltimateSurge, 23),
            new UniqueDef("unique.guardian_halo", "head.radiant_halo", new Txt("守りの後光", "Halo of Guarding"),
                new Txt("光は傷ついた者を、もう一度立たせる。", "Light lifts the wounded back to their feet."),
                Power.Radiance, 82, Power.SecondWind, 28),
            new UniqueDef("unique.lightning_scope", "head.scout_goggles", new Txt("稲光の遠眼鏡", "Lightning Spyglass"),
                new Txt("遠い獲物へ、雷が先に届く。", "Lightning reaches distant prey first."),
                Power.ChainLightning, 48, Power.OpeningStrike, 45),
            new UniqueDef("unique.prism_diadem", "head.star_diadem", new Txt("虹彩の髪飾り", "Iridescent Diadem"),
                new Txt("四つの色が重なる時、星が爆ぜる。", "When four colors overlap, a star bursts."),
                Power.Convergence, 130, Power.UltimateSurge, 24),
            new UniqueDef("unique.void_fragment", "head.void_helm", new Txt("虚空の砕片", "Shard of the Void"),
                new Txt("倒れた敵の闇が、周囲を砕く。", "The darkness of the fallen shatters all around."),
                Power.Shatter, 55, Power.Umbra, 82),
            new UniqueDef("unique.sunfire_mask", "head.sun_mask", new Txt("灼陽の面", "Mask of the Scorching Sun"),
                new Txt("光と炎が、四拍ごとに降り注ぐ。", "Light and fire rain down every fourth beat."),
                Power.Blaze, 65, Power.Radiance, 82),
            new UniqueDef("unique.briar_embrace", "hands.thorn_wraps", new Txt("茨の抱擁", "Briar Embrace"),
                new Txt("傷つくほどに、この抱擁は強く締まる。", "The more it wounds you, the tighter it clings."),
                Power.Thorns, 32, Power.Bloodlust, 22),
            new UniqueDef("unique.apothecary_touch", "hands.alchemist_gloves", new Txt("調合の指先", "Apothecary's Touch"),
                new Txt("火と氷を混ぜれば、四つ目の元素が目覚める。", "Mix fire with frost, and the other elements wake."),
                Power.Convergence, 130, Power.Frost, 48),
            new UniqueDef("unique.marrow_breaker", "hands.bone_knuckles", new Txt("砕骨の拳", "Marrow Breaker"),
                new Txt("砕いた骨の数だけ、命が戻ってくる。", "For every bone you break, some life returns."),
                Power.Shatter, 60, Power.SoulSiphon, 18),
            new UniqueDef("unique.star_weaver", "hands.star_rings", new Txt("星を紡ぐ指", "Star Weaver"),
                new Txt("光を指に絡め、夜空の守りを織り上げる。", "Thread light through your fingers and weave a shield of stars."),
                Power.StarShield, 18, Power.Radiance, 80),
            new UniqueDef("unique.unyielding_vow", "hands.oath_gauntlets", new Txt("不退の誓拳", "Unyielding Vow"),
                new Txt("一歩も退かぬと誓えば、痛みが力に変わる。", "Vow never to retreat, and pain becomes power."),
                Power.Barrier, 10, Power.Retaliation, 26),
            new UniqueDef("unique.maelstrom_grasp", "hands.tide_gloves", new Txt("渦潮の手", "Maelstrom Grasp"),
                new Txt("身を翻すたび、冷たい渦が敵を呑み込む。", "Every roll draws foes into a freezing whirlpool."),
                Power.Frost, 51, Power.Whirlwind, 72),
            new UniqueDef("unique.hellfire_fist", "hands.flame_grips", new Txt("業火拳", "Hellfire Fist"),
                new Txt("囲まれるほどに、拳の炎は荒れ狂う。", "The more foes surround you, the wilder the flames rage."),
                Power.Blaze, 66, Power.Frenzy, 3),
            new UniqueDef("unique.void_render", "hands.void_claws", new Txt("虚空を裂く爪", "Void Render"),
                new Txt("闇を染み込ませ、とどめの一裂きを狙え。", "Soak them in dark, then go for the final tear."),
                Power.Umbra, 82, Power.Executioner, 44),
            new UniqueDef("unique.stonecaster", "hands.sling_bracers", new Txt("礫打ち", "Stonecaster"),
                new Txt("無傷の的へ先に投げ、風のように駆け出せ。", "Throw first at the unharmed, then run like the wind."),
                Power.OpeningStrike, 50, Power.Tailwind, 22),
            new UniqueDef("unique.rosary_chant", "hands.prayer_beads", new Txt("数珠繰り", "Rosary Chant"),
                new Txt("珠を一つ繰るたび、祈りが傷を塞いでいく。", "With each bead you turn, a prayer closes your wounds."),
                Power.Barrier, 10, Power.Lifesteal, 9),
            new UniqueDef("unique.last_stand_gloves", "hands.leather_gloves", new Txt("背水の手袋", "Last Stand Gloves"),
                new Txt("追い詰められた時こそ、指は最も速く走る。", "When cornered, your fingers move their fastest."),
                Power.Bloodlust, 22, Power.Momentum, 5),
            new UniqueDef("unique.wallbreaker", "hands.iron_gauntlets", new Txt("城砕きの拳", "Wallbreaker"),
                new Txt("囲まれても動じず、倒した敵ごと辺りを砕く。", "Stand firm when surrounded, and shatter all around each kill."),
                Power.Bulwark, 30, Power.Shatter, 56),
            new UniqueDef("unique.backfire_gauntlets", "hands.iron_gauntlets", new Txt("返り火の籠手", "Backfire Gauntlets"),
                new Txt("殴られた痛みを、そのまま炎に変えて返す。", "It turns every blow you take into fire thrown back."),
                Power.Retaliation, 28, Power.Blaze, 60),
            new UniqueDef("unique.lightning_draw", "hands.archer_bracers", new Txt("雷走り", "Lightning Draw"),
                new Txt("最初の一矢が走れば、雷が群れを渡る。", "Once the first arrow flies, lightning leaps through the pack."),
                Power.ChainLightning, 46, Power.OpeningStrike, 46),
            new UniqueDef("unique.cantrip_fingers", "hands.spell_gloves", new Txt("指先の詠唱", "Cantrip Fingers"),
                new Txt("技を放つたびに、指先から火花がこぼれる。", "Sparks spill from your fingertips after every skill."),
                Power.Ember, 48, Power.Overload, 19),
            new UniqueDef("unique.famished_claws", "hands.claw_gauntlets", new Txt("喰らいつく爪", "Famished Claws"),
                new Txt("弱った獲物ほど、爪は深く食い込み血を啜る。", "The weaker the prey, the deeper the claws bite and drink."),
                Power.Lifesteal, 9, Power.Executioner, 44),
            new UniqueDef("unique.frozen_night", "hands.frost_mitts", new Txt("凍夜の指", "Frozen Night"),
                new Txt("冷気と闇を重ね、敵を静かな夜に閉じ込める。", "Layer cold upon dark and trap foes in a silent night."),
                Power.Frost, 52, Power.Umbra, 80),
            new UniqueDef("unique.light_binder", "hands.radiant_wraps", new Txt("光を束ねる手", "Light Binder"),
                new Txt("集めた光は、切り札の一撃で解き放たれる。", "The gathered light is released in a single trump strike."),
                Power.Radiance, 85, Power.UltimateSurge, 24),
            new UniqueDef("unique.peak_form", "hands.vigor_grips", new Txt("万全の一撃", "Peak Form"),
                new Txt("体も敵も万全のうちに、最初の一撃を叩き込め。", "Strike first while both you and your foe are unscathed."),
                Power.Vigor, 16, Power.OpeningStrike, 48),
            new UniqueDef("unique.flash_sleight", "hands.quick_fingers", new Txt("稲光の早業", "Flash Sleight"),
                new Txt("身をかわすたびに技が戻り、雷が跳ねる。", "Each dodge cools your skills and sends lightning leaping."),
                Power.EchoingDodge, 78, Power.ChainLightning, 52),
            new UniqueDef("unique.melee_dancer", "hands.quick_fingers", new Txt("乱戦の舞手", "Melee Dancer"),
                new Txt("敵の輪の中でこそ、手は軽やかに踊る。", "Your hands dance lightest in the middle of a ring of foes."),
                Power.Frenzy, 3, Power.Whirlwind, 70),
            new UniqueDef("unique.hounds_reach", "hands.reach_bracers", new Txt("追い込みの腕", "Hound's Reach"),
                new Txt("遠くから仕留め、勢いのまま次の獲物へ走る。", "Finish them from afar and chase down the next target."),
                Power.Executioner, 44, Power.Tailwind, 22),
            new UniqueDef("unique.ember_soul", "hands.ember_gauntlets", new Txt("熾火の魂", "Ember Soul"),
                new Txt("火種を撒き、燃え尽きた命を己の糧とする。", "Sow the embers and feed on the lives they burn out."),
                Power.Ember, 50, Power.SoulSiphon, 18),
            new UniqueDef("unique.shadow_sprinter", "hands.shadow_gloves", new Txt("影走り", "Shadow Sprinter"),
                new Txt("倒すたびに影が濃くなり、手の動きは増す。", "With every kill the shadow deepens and your hands quicken."),
                Power.Umbra, 85, Power.Momentum, 5),
            new UniqueDef("unique.merciful_light", "hands.healer_hands", new Txt("慈光の手", "Merciful Light"),
                new Txt("光を宿した手が、倒れかけた命を引き戻す。", "Hands bright with light pull a failing life back."),
                Power.Radiance, 80, Power.SecondWind, 28),
            new UniqueDef("unique.stone_resolve", "hands.stone_fists", new Txt("不動の拳", "Stone Resolve"),
                new Txt("敵に囲まれながら殴り、傷を癒して立ち続ける。", "Punch while surrounded, heal as you hit, and stay standing."),
                Power.Bulwark, 30, Power.Lifesteal, 9),
            new UniqueDef("unique.thorned_hammer", "hands.stone_fists", new Txt("棘の鉄拳", "Thorned Fist"),
                new Txt("殴られた痛みを反射し、敵を粉々に砕き散らす。", "Reflect the pain of every blow and scatter foes to pieces."),
                Power.Thorns, 32, Power.Shatter, 56),
            new UniqueDef("unique.riposte_creed", "hands.duelist_gloves", new Txt("返し刃の作法", "Riposte Creed"),
                new Txt("受けた痛みは、次の獲物への先手で返せ。", "Take the hit, then answer with the first strike."),
                Power.Retaliation, 28, Power.OpeningStrike, 48),
            new UniqueDef("unique.companions_road", "feet.travel_boots", new Txt("道連れの足", "Companion's Road"),
                new Txt("誰かと歩く道は、一人の倍だけ速い。", "A road walked together is twice as swift."),
                Power.Sprint, 24, Power.Resonance, 9),
            new UniqueDef("unique.victors_trail", "feet.travel_boots", new Txt("凱旋の道行き", "Victor's Trail"),
                new Txt("倒した数だけ、帰り道は軽くなる。", "Every foe felled lightens the road home."),
                Power.Tailwind, 24, Power.Momentum, 5),
            new UniqueDef("unique.iron_stance", "feet.iron_greaves", new Txt("鉄壁の立ち姿", "Iron Stance"),
                new Txt("囲まれたなら、動かずに立てばいい。", "When surrounded, simply stand your ground."),
                Power.Bulwark, 32, Power.Barrier, 10),
            new UniqueDef("unique.brawlers_greaves", "feet.iron_greaves", new Txt("喧嘩屋の脛", "Brawler's Shins"),
                new Txt("殴られたら、倍にして蹴り返す。", "Take a blow, kick back twice as hard."),
                Power.Retaliation, 28, Power.Frenzy, 3),
            new UniqueDef("unique.unmoving_vow", "feet.guard_sabatons", new Txt("動かざる誓い", "Unmoving Vow"),
                new Txt("膝をつかぬ限り、誓いは破れない。", "The vow holds so long as you do not kneel."),
                Power.Bulwark, 30, Power.SecondWind, 30),
            new UniqueDef("unique.barbed_march", "feet.guard_sabatons", new Txt("棘の行軍", "Barbed March"),
                new Txt("一歩ごとに、近づく者が傷を負う。", "Each step wounds those who draw near."),
                Power.Thorns, 32, Power.Lifesteal, 10),
            new UniqueDef("unique.pack_hunter", "feet.wolf_boots", new Txt("群れ狩りの脚", "Pack Hunter"),
                new Txt("獲物に囲まれても、狼は笑って駆ける。", "Wolves run laughing through a ring of prey."),
                Power.Frenzy, 3, Power.Sprint, 24),
            new UniqueDef("unique.hungry_howl", "feet.wolf_boots", new Txt("飢えた遠吠え", "Hungry Howl"),
                new Txt("血の匂いが濃いほど、足は止まらない。", "The thicker the blood scent, the swifter the paws."),
                Power.Bloodlust, 24, Power.Momentum, 5),
            new UniqueDef("unique.tidebreaker", "feet.tide_sandals", new Txt("波を砕く足", "Tidebreaker"),
                new Txt("身をかわせば、足元から渦が立つ。", "Dodge, and a whirlpool rises at your feet."),
                Power.Frost, 57, Power.Whirlwind, 80),
            new UniqueDef("unique.ebb_and_flow", "feet.tide_sandals", new Txt("満ち引きの歩", "Ebb and Flow"),
                new Txt("冷たい波は、討つたびに背を押す。", "The cold tide pushes you on with each kill."),
                Power.Frost, 54, Power.Tailwind, 22),
            new UniqueDef("unique.citadel_stride", "feet.knight_sabatons", new Txt("城門の騎士", "Citadel Knight"),
                new Txt("敵の波を受け止め、門の前で崩さない。", "Hold the gate and let the wave break on you."),
                Power.Bulwark, 30, Power.Aegis, 22),
            new UniqueDef("unique.vengeful_knight", "feet.knight_sabatons", new Txt("復讐の騎士", "Vengeful Knight"),
                new Txt("傷を負うたび、剣は怒りで重くなる。", "Every wound makes the blade heavier with wrath."),
                Power.Retaliation, 28, Power.Vigor, 16),
            new UniqueDef("unique.first_light_steps", "feet.dawn_steps", new Txt("曙光を踏む", "Dawnstepper"),
                new Txt("駆け出す朝、足跡が光を残す。", "At daybreak your footprints leave light behind."),
                Power.Radiance, 95, Power.Sprint, 22),
            new UniqueDef("unique.morning_reprieve", "feet.dawn_steps", new Txt("朝凪の舞", "Morning Calm"),
                new Txt("光を宿して舞えば、技はすぐ戻る。", "Dance in the light and your skills return swiftly."),
                Power.Radiance, 88, Power.EchoingDodge, 78),
            new UniqueDef("unique.cinder_march", "feet.ash_boots", new Txt("燃え殻の行進", "Cinder March"),
                new Txt("踏み荒らした跡に、火種が弾ける。", "Sparks burst along the path you trample."),
                Power.Ember, 57, Power.Shatter, 65),
            new UniqueDef("unique.pyre_runner", "feet.ash_boots", new Txt("火葬場の走者", "Pyre Runner"),
                new Txt("倒すほど、炎は走る足に絡みつく。", "The more you fell, the more flame clings to you."),
                Power.Blaze, 72, Power.Momentum, 5),
            new UniqueDef("unique.green_rest", "feet.root_sandals", new Txt("緑の休息", "Green Rest"),
                new Txt("根を下ろせば、傷が芽吹き始める。", "Take root and your wounds begin to bud."),
                Power.Barrier, 10, Power.SoulSiphon, 20),
            new UniqueDef("unique.spring_sprout", "feet.root_sandals", new Txt("芽吹きの春", "Spring Sprout"),
                new Txt("倒れかけてなお、一歩で力が戻る。", "Even on the brink, one step restores your strength."),
                Power.SecondWind, 32, Power.Vigor, 16),
            new UniqueDef("unique.fog_runner", "feet.mist_shoes", new Txt("霧に駆ける", "Fogrunner"),
                new Txt("霧に紛れて跳べば、影も追いつけない。", "Leap through the fog and shadows cannot follow."),
                Power.Sprint, 25, Power.Umbra, 88),
            new UniqueDef("unique.drifting_mind", "feet.mist_shoes", new Txt("揺蕩う思考", "Drifting Thought"),
                new Txt("避けては唱え、唱えてはまた避ける。", "Dodge, cast, then dodge again."),
                Power.EchoingDodge, 78, Power.Overload, 20),
            new UniqueDef("unique.trackers_pace", "feet.hunter_boots", new Txt("追い詰める歩調", "Tracker's Pace"),
                new Txt("弱った獲物へは、迷わず駆け寄れ。", "Run without hesitation to the wounded prey."),
                Power.Executioner, 50, Power.Tailwind, 24),
            new UniqueDef("unique.ambush_leap", "feet.hunter_boots", new Txt("待ち伏せの跳躍", "Ambush Leap"),
                new Txt("一撃目を譲らぬ者が、狩りを制す。", "He who strikes first rules the hunt."),
                Power.OpeningStrike, 55, Power.Sprint, 26),
            new UniqueDef("unique.shackled_walk", "feet.iron_clogs", new Txt("枷つきの歩み", "Shackled Walk"),
                new Txt("重い足取りは、痛みを返す刃になる。", "A heavy gait turns pain into a blade."),
                Power.Thorns, 30, Power.Aegis, 22),
            new UniqueDef("unique.stubborn_clogs", "feet.iron_clogs", new Txt("頑固者の木靴", "Stubborn One's Clogs"),
                new Txt("押し寄せる敵に、びくともしない。", "Unmoved by the crowd pressing in."),
                Power.Bulwark, 30, Power.Lifesteal, 10),
            new UniqueDef("unique.zodiac_step", "feet.star_slippers", new Txt("星宮の跳躍", "Zodiac Leap"),
                new Txt("身をかわして、星の奥義へ繋げ。", "Slip away, then chain into a stellar finisher."),
                Power.UltimateSurge, 28, Power.EchoingDodge, 78),
            new UniqueDef("unique.night_sky_walk", "feet.star_slippers", new Txt("夜空の散歩", "Night Sky Walk"),
                new Txt("星の盾を纏い、静かに術を重ねる。", "Wrapped in a star shield, stack spell upon spell."),
                Power.StarShield, 22, Power.Overload, 20),
            new UniqueDef("unique.shadowed_hunt", "feet.stalker_boots", new Txt("闇の足音", "Footfall of Dark"),
                new Txt("足音を消せば、とどめの隙が見える。", "Silence your steps and the killing opening appears."),
                Power.Executioner, 48, Power.EchoingDodge, 78),
            new UniqueDef("unique.gale_spinner", "feet.wind_sandals", new Txt("風車の足", "Gale Spinner"),
                new Txt("回って跳べば、旋風が道を拓く。", "Spin and leap, and a whirlwind clears the way."),
                Power.Whirlwind, 80, Power.Sprint, 26),
            new UniqueDef("unique.dusk_reaper_sickle", "weapon.moon_sickle", new Txt("宵闇の刈り手", "Dusk Reaper"),
                new Txt("影を刈るほど、刃は冴え渡る。", "The more shadows it reaps, the keener it grows."),
                Power.Umbra, 100, Power.SoulSiphon, 20),
            new UniqueDef("unique.cinder_serpent", "weapon.ember_whip", new Txt("火蛇の鞭", "Cinder Serpent"),
                new Txt("先手の一打で、火蛇が牙を剥く。", "The first lash is when the serpent bites."),
                Power.Ember, 60, Power.OpeningStrike, 55),
            new UniqueDef("unique.whiteridge_lance", "weapon.glacier_spear", new Txt("白嶺の槍", "Whiteridge Lance"),
                new Txt("凍らせた敵を、穂先で砕け。", "Freeze them, then shatter them on the tip."),
                Power.Frost, 60, Power.Shatter, 65),
            new UniqueDef("unique.first_daybreak_scepter", "weapon.dawn_scepter", new Txt("曙光の王笏", "Daybreak Scepter"),
                new Txt("傷のない身に、朝日は力を貸す。", "Morning light favors the unscathed."),
                Power.Radiance, 100, Power.Vigor, 18),
            new UniqueDef("unique.stalwart_halberd", "weapon.iron_halberd", new Txt("不動の斧槍", "Stalwart Halberd"),
                new Txt("敵に囲まれても、一歩も退かない。", "Surrounded, it never yields a step."),
                Power.Bulwark, 35, Power.Frenzy, 3),
            new UniqueDef("unique.nightlong_tome", "weapon.dream_tome", new Txt("終夜の綴り", "Nightlong Tome"),
                new Txt("頁をめくるたび、終曲が近づく。", "Each turned page draws the finale closer."),
                Power.Overload, 22, Power.UltimateSurge, 28),
            new UniqueDef("unique.bonesunder", "weapon.bone_cleaver", new Txt("断骨の刃", "Bonesunder"),
                new Txt("弱った獲物に、迷わず振り下ろせ。", "Bring it down on the weakened without hesitation."),
                Power.Executioner, 50, Power.Lifesteal, 11),
            new UniqueDef("unique.mirrored_rapier", "weapon.twin_rapier", new Txt("鏡合わせの細剣", "Mirrored Rapier"),
                new Txt("倒れ際の手応えが、次の突きを速める。", "Each kill quickens the next thrust."),
                Power.Momentum, 5, Power.Bloodlust, 22),
            new UniqueDef("unique.wayfarer_bell_staff", "weapon.pilgrim_staff", new Txt("遍路の鈴杖", "Wayfarer Bell Staff"),
                new Txt("鈴が鳴る間は、まだ倒れない。", "While the bell rings, you are not yet fallen."),
                Power.SecondWind, 32, Power.Lifesteal, 11),
            new UniqueDef("unique.skydrum_hammer", "weapon.thunder_hammer", new Txt("天鳴の鎚", "Skydrum Hammer"),
                new Txt("倒した敵の上に、雷が落ちる。", "Thunder falls where your enemies do."),
                Power.ChainLightning, 55, Power.Shatter, 65),
            new UniqueDef("unique.furnace_vest", "armor.ember_cuirass", new Txt("炉心の胴鎧", "Furnace Vest"),
                new Txt("囲まれるほどに、炉は熱を増す。", "The more they crowd you, the hotter the furnace."),
                Power.Ember, 57, Power.Frenzy, 3),
            new UniqueDef("unique.rimeveil_robe", "armor.frost_robe", new Txt("霜華の法衣", "Rimeveil Robe"),
                new Txt("舞うたびに、霜の花が咲く。", "Frost blooms with every dodge."),
                Power.Frost, 57, Power.Whirlwind, 75),
            new UniqueDef("unique.drake_hide", "armor.scale_coat", new Txt("竜皮の外套", "Drake Hide"),
                new Txt("群れに囲まれたら、炎を返せ。", "When swarmed, answer with flame."),
                Power.Blaze, 55, Power.Bulwark, 32),
            new UniqueDef("unique.huntsmans_vest", "armor.hunter_vest", new Txt("追い込みの胴衣", "Huntsman Vest"),
                new Txt("走って追い詰め、とどめを刺す。", "Run them down, then finish the kill."),
                Power.Executioner, 48, Power.Sprint, 26),
            new UniqueDef("unique.stardust_mantle", "armor.star_mantle", new Txt("降星の肩掛け", "Stardust Mantle"),
                new Txt("仲間と並べば、星屑が守る。", "Stand beside allies and stardust shields you."),
                Power.Resonance, 10, Power.StarShield, 22),
            new UniqueDef("unique.stillfist_garb", "armor.monk_garb", new Txt("静拳の道着", "Stillfist Garb"),
                new Txt("拳を重ねるほど、心は静まる。", "The more strikes you land, the calmer you grow."),
                Power.Momentum, 5, Power.Aegis, 22),
            new UniqueDef("unique.duskwoven_cloak", "armor.shadow_cloak", new Txt("宵織りの外套", "Duskwoven Cloak"),
                new Txt("打たれた痛みを、闇に染めて返せ。", "Dye the pain you suffer in shadow and return it."),
                Power.Umbra, 95, Power.Retaliation, 28),
            new UniqueDef("unique.solar_aegis_plate", "armor.sun_plate", new Txt("日輪の護り", "Solar Aegis"),
                new Txt("触れる者を、光の棘が拒む。", "Thorns of light turn away all who touch it."),
                Power.Radiance, 95, Power.Thorns, 32),
            new UniqueDef("unique.heartwood_bark", "armor.bark_mail", new Txt("芯木の鎧", "Heartwood Mail"),
                new Txt("傷は、年輪のように癒えていく。", "Wounds heal like rings in a tree."),
                Power.Vigor, 17, Power.SecondWind, 32),
            new UniqueDef("unique.windfarer_coat", "armor.traveler_coat", new Txt("風渡りの旅衣", "Windfarer Coat"),
                new Txt("足を止めない者に、道は開く。", "The road opens for those who never stop."),
                Power.Sprint, 27, Power.Tailwind, 22),
            new UniqueDef("unique.hearth_ember_stone", "charm.hearthstone", new Txt("囲炉裏の熾", "Hearth Ember"),
                new Txt("火を絶やさぬ者は、魔術を灯し続ける。", "Keep the fire alive and your magic burns on."),
                Power.Ember, 57, Power.Overload, 22),
            new UniqueDef("unique.rimeheart_pendant", "charm.frost_pendant", new Txt("霜心の首飾り", "Rimeheart Pendant"),
                new Txt("凍てた心が、大技を星の盾に変える。", "A frozen heart turns your ultimate into a starry shield."),
                Power.Frost, 57, Power.StarShield, 22),
            new UniqueDef("unique.noon_brooch", "charm.sun_brooch", new Txt("真昼のブローチ", "Noon Brooch"),
                new Txt("陽が高いうちは、倒れはしない。", "While the sun is high, you will not fall."),
                Power.Radiance, 95, Power.SecondWind, 32),
            new UniqueDef("unique.umbral_band", "charm.shadow_ring", new Txt("影絡みの指輪", "Umbral Band"),
                new Txt("闇をまとい、回避と共に渦を巻け。", "Cloak yourself in dark and spin out of every dodge."),
                Power.Umbra, 95, Power.Whirlwind, 80),
            new UniqueDef("unique.first_blood_fang", "charm.hunter_tooth", new Txt("初撃の牙", "First Blood Fang"),
                new Txt("最初の一撃から、獲物へ駆け出せ。", "From the first strike, give chase."),
                Power.OpeningStrike, 55, Power.Sprint, 26),
            new UniqueDef("unique.elder_oak_amulet", "charm.oak_amulet", new Txt("古樫の護符", "Elder Oak Amulet"),
                new Txt("元気なうちは強く、傷は静かに塞がる。", "Strong while hale, and wounds close quietly."),
                Power.Vigor, 18, Power.Lifesteal, 11),
            new UniqueDef("unique.ticking_gears", "charm.clockwork_charm", new Txt("刻む歯車", "Ticking Gears"),
                new Txt("倒すたびに歯車が回り、術も速く回る。", "As the gears turn, so do your moves."),
                Power.Momentum, 5, Power.Overload, 22),
            new UniqueDef("unique.vow_of_iron", "charm.iron_seal", new Txt("鉄の誓印", "Vow of Iron"),
                new Txt("打たれても揺らがず、そのまま打ち返す。", "Struck, you answer under a shield of light."),
                Power.Retaliation, 28, Power.Barrier, 10),
            new UniqueDef("unique.skydancer_feather", "charm.feather_token", new Txt("空舞いの羽", "Skydancer Feather"),
                new Txt("避けるたび、時間が少し巻き戻る。", "Every dodge winds time back a little."),
                Power.Sprint, 27, Power.EchoingDodge, 78),
            new UniqueDef("unique.rally_horn", "charm.war_horn", new Txt("決起の角笛", "Rally Horn"),
                new Txt("群れの中で吹けば、奥義が燃え上がる。", "Blow it amid the swarm and your ultimate roars."),
                Power.Frenzy, 3, Power.UltimateSurge, 28),
            // v1.23：新しい固有効果を使う固有品
            new UniqueDef("unique.finale_codex", "weapon.dream_tome", new Txt("終幕の写本", "Codex of the Curtain Call"),
                new Txt("三つの技を綴れば、終幕は早く訪れる。", "Chain three skills, and the final act arrives early."),
                Power.Finale, 22, Power.StarShield, 20),
            new UniqueDef("unique.grand_finale_diadem", "head.star_diadem", new Txt("大詰めの髪飾り", "Diadem of the Grand Finale"),
                new Txt("三つの技を重ね、切り札を引き寄せろ。", "Layer Q, W and E to pull your ultimate closer."),
                Power.Finale, 20, Power.UltimateSurge, 22),
            new UniqueDef("unique.intermission_shoes", "feet.dancer_shoes", new Txt("幕間の舞靴", "Intermission Dancers"),
                new Txt("舞い続けて技を巡らせ、最後の幕を開けよ。", "Keep dancing through your skills to raise the last curtain."),
                Power.Finale, 18, Power.EchoingDodge, 65),
            new UniqueDef("unique.last_movement_charm", "charm.clockwork_charm", new Txt("終楽章の歯車", "Gear of the Last Movement"),
                new Txt("歯車が噛み合えば、終曲は目前だ。", "When the gears mesh, the finale is near."),
                Power.Finale, 25, Power.Overload, 20),
            new UniqueDef("unique.echo_longbow", "weapon.longspike_bow", new Txt("谺の長弓", "Echoing Longbow"),
                new Txt("会心の矢が鳴るたび、技はすぐに戻る。", "Each critical arrow rings out and recalls your skills."),
                Power.CriticalEcho, 5, Power.OpeningStrike, 45),
            new UniqueDef("unique.afterglow_fingers", "hands.duelist_gloves", new Txt("残響の指", "Fingers of Afterglow"),
                new Txt("急所を突くたび、余韻が技を呼び戻す。", "Every vital strike leaves an echo that refreshes your skills."),
                Power.CriticalEcho, 5, Power.Momentum, 5),
            new UniqueDef("unique.reverberant_vest", "armor.hunter_vest", new Txt("木霊の胴衣", "Reverberant Vest"),
                new Txt("狙い澄ました一撃が、次の技を早める。", "A well-aimed blow hastens your next skill."),
                Power.CriticalEcho, 4, Power.Sprint, 26),
            new UniqueDef("unique.echoing_tread", "feet.stalker_boots", new Txt("谺を踏む足", "Tread of Echoes"),
                new Txt("忍び寄る一撃の余韻が、技を研ぎ澄ます。", "The echo of a stealthy crit sharpens your skills."),
                Power.CriticalEcho, 5, Power.EchoingDodge, 78),
            new UniqueDef("unique.ice_shackle_spear", "weapon.glacier_spear", new Txt("氷枷の穂先", "Icebound Spearhead"),
                new Txt("凍らせて動きを奪い、渾身の一突きを。", "Freeze them in place, then thrust with everything."),
                Power.Fetters, 16, Power.Frost, 60),
            new UniqueDef("unique.snaring_coral", "head.coral_crown", new Txt("絡め取る珊瑚", "Snaring Coral"),
                new Txt("動けぬ敵ほど、深く刃が食い込む。", "The less they can move, the deeper the blade bites."),
                Power.Fetters, 17, Power.Shatter, 55),
            new UniqueDef("unique.neap_tide_grip", "hands.tide_gloves", new Txt("引き潮の枷", "Shackle of the Ebb Tide"),
                new Txt("冷たい潮で足を絡め、そこを狙え。", "Tangle their legs in cold tide and strike there."),
                Power.Fetters, 18, Power.Frost, 51),
            new UniqueDef("unique.bound_oath", "charm.chain_necklace", new Txt("繋がれた誓い", "Chained Oath"),
                new Txt("鎖に縛られた敵へ、報復は容赦しない。", "Retribution spares no enemy caught in your chains."),
                Power.Fetters, 18, Power.Retaliation, 30),
            new UniqueDef("unique.crystal_chorale", "weapon.star_harp", new Txt("水晶の調べ", "Crystal Chorale"),
                new Txt("結晶を揃えるほど、魔力の音色は澄んでいく。", "The more crystals you gather, the purer the magic rings."),
                Power.CrystalResonance, 2, Power.Overload, 20),
            new UniqueDef("unique.collectors_hat", "head.sage_hat", new Txt("蒐集家の帽子", "Collector's Hat"),
                new Txt("集めた結晶の数だけ、力は高まっていく。", "Your power grows with every crystal you collect."),
                Power.CrystalResonance, 2, Power.Resonance, 9),
            new UniqueDef("unique.crystal_shroud", "armor.star_mantle", new Txt("結晶纏い", "Crystal Shroud"),
                new Txt("結晶の輝きを纏い、星の盾を張れ。", "Wear the crystals' glow and raise a shield of stars."),
                Power.CrystalResonance, 2, Power.StarShield, 22),
            new UniqueDef("unique.crystal_eye", "charm.dream_lens", new Txt("結晶の瞳", "Eye of Crystal"),
                new Txt("磨いた結晶が、四つの元素を呼び合わせる。", "Polished crystals call the four elements together."),
                Power.CrystalResonance, 2, Power.Convergence, 120),
            new UniqueDef("unique.huntmasters_bow", "weapon.hunting_bow", new Txt("狩猟長の弓", "Huntmaster's Bow"),
                new Txt("狩りを重ねるほど、弱った獲物は逃げられない。", "The deeper your hunt, the less your wounded prey can flee."),
                Power.PreyPride, 5, Power.Executioner, 45),
            new UniqueDef("unique.fangking_pelt", "head.wolf_pelt", new Txt("牙王の毛皮", "Pelt of the Fang King"),
                new Txt("獲物を追うほど、群れの誇りが牙を研ぐ。", "Tracking prey sharpens your fangs with the pack's pride."),
                Power.PreyPride, 6, Power.Frenzy, 3),
            new UniqueDef("unique.praying_claws", "hands.claw_gauntlets", new Txt("祈る爪", "Praying Claws"),
                new Txt("祈りを捧げて獲物を追い、危険へ踏み込め。", "Offer prayers, track your prey, and step into danger."),
                Power.PreyPride, 5, Power.Devotion, 3),
            new UniqueDef("unique.proud_tracks", "feet.hunter_boots", new Txt("誇りの足跡", "Tracks of Pride"),
                new Txt("獲物を深く追うほど、足取りは軽くなる。", "The deeper the pursuit, the lighter your stride."),
                Power.PreyPride, 4, Power.Sprint, 26),
            new UniqueDef("unique.overflowing_sap", "armor.bark_mail", new Txt("滴る生命樹", "Dripping Lifetree"),
                new Txt("傷が癒えて余った命は、樹皮の盾となる。", "Surplus healing hardens into a shield of bark."),
                Power.OverflowingLife, 45, Power.Lifesteal, 10),
            new UniqueDef("unique.brimming_chalice_staff", "weapon.pilgrim_staff", new Txt("杯満つる錫杖", "Brimming Chalice Staff"),
                new Txt("倒して満ちた命は、溢れて身を守る。", "Life gathered from kills overflows into protection."),
                Power.OverflowingLife, 40, Power.SoulSiphon, 18),
            new UniqueDef("unique.full_bloom_wreath", "head.leaf_wreath", new Txt("満開の花冠", "Wreath in Full Bloom"),
                new Txt("万全の身で咲く花は、散りぎわに盾となる。", "A flower in full health becomes a shield as it fades."),
                Power.OverflowingLife, 50, Power.Vigor, 14),
            new UniqueDef("unique.spring_water_steps", "feet.root_sandals", new Txt("湧き水の歩", "Steps of the Wellspring"),
                new Txt("癒しが溢れるほど、足元に守りが湧く。", "The more your healing overflows, the more shelter wells up."),
                Power.OverflowingLife, 35, Power.Lifesteal, 9),
            new UniqueDef("unique.votive_shawl", "armor.prayer_shawl", new Txt("奉納の肩掛け", "Votive Shawl"),
                new Txt("聖堂を巡るたび、肩掛けは力を宿していく。", "The shawl gathers power with every shrine you visit."),
                Power.Devotion, 4, Power.SecondWind, 28),
            new UniqueDef("unique.wish_beads", "hands.prayer_beads", new Txt("願掛けの数珠", "Wishing Beads"),
                new Txt("一つ祈るごとに、数珠は強く握られる。", "With each prayer, the beads are held tighter."),
                Power.Devotion, 3, Power.Barrier, 10),
            new UniqueDef("unique.pilgrims_badge", "charm.sun_brooch", new Txt("参詣の徽章", "Pilgrim's Badge"),
                new Txt("聖堂で祈りを重ね、陽の力を高めよ。", "Stack prayers at shrines to raise the power of the sun."),
                Power.Devotion, 4, Power.Radiance, 90),
            new UniqueDef("unique.dedicated_greathelm", "head.knight_helm", new Txt("奉じる大兜", "Dedicated Greathelm"),
                new Txt("誓いを捧げるほど、兜は守りを増す。", "Each vow offered strengthens the helm's guard."),
                Power.Devotion, 3, Power.Aegis, 18),
            new UniqueDef("unique.sparking_crown", "head.ember_crown", new Txt("火の粉の冠", "Crown of Sparks"),
                new Txt("燃える敵から、火の粉が隣へ飛び移る。", "Sparks leap from burning foes to their neighbors."),
                Power.Wildfire, 30, Power.Ember, 48),
            new UniqueDef("unique.sea_of_flame_plate", "armor.ember_plate", new Txt("火の海の鎧", "Plate of the Flame Sea"),
                new Txt("火を重ねれば、戦場ごと燃え広がる。", "Stack the flames and the whole field catches fire."),
                Power.Wildfire, 35, Power.Blaze, 50),
            new UniqueDef("unique.seed_scatterer", "feet.ash_boots", new Txt("火種蒔き", "Seed Scatterer"),
                new Txt("歩いた跡から、火種が次々と飛び移る。", "Embers hop from foe to foe in your wake."),
                Power.Wildfire, 28, Power.Ember, 45),
            new UniqueDef("unique.spreading_fist", "hands.flame_grips", new Txt("燃え移る手", "Hand of Spreading Flame"),
                new Txt("一撃の炎は、隣の敵へ燃え移る。", "One strike's flame leaps on to the next enemy."),
                Power.Wildfire, 32, Power.Blaze, 60),
            // v1.25：新しい土台の固有品
            new UniqueDef("unique.thunderhead_glaive", "weapon.storm_glaive", new Txt("雷雲を裂く刃", "Thunderhead Cleaver"),
                new Txt("稲妻が走るたび、術の巡りが速まる。", "Each bolt that flashes quickens your spells."),
                Power.ChainLightning, 58, Power.CriticalEcho, 5),
            new UniqueDef("unique.eye_of_the_gale", "weapon.storm_glaive", new Txt("颶風の円舞", "Eye of the Gale"),
                new Txt("敵の輪の中心で、薙刀を回し続けよ。", "Spin the glaive at the heart of the ring of foes."),
                Power.Frenzy, 3, Power.Whirlwind, 85),
            new UniqueDef("unique.requiem_flute", "weapon.bone_flute", new Txt("終幕の骨笛", "Requiem Flute"),
                new Txt("術を重ねて、終曲を早く呼べ。", "Chain your spells and summon the finale sooner."),
                Power.Finale, 24, Power.SoulSiphon, 22),
            new UniqueDef("unique.chorus_of_marrow", "weapon.bone_flute", new Txt("骨笛の合唱", "Chorus of Marrow"),
                new Txt("仲間と並んで吹けば、傷が癒えていく。", "Play beside allies and wounds begin to mend."),
                Power.Resonance, 10, Power.Lifesteal, 11),
            new UniqueDef("unique.cinderfang_katar", "weapon.ember_katar", new Txt("火種蒔きの刃", "Cinderfang Katar"),
                new Txt("燃える敵から、隣の敵へ火を移せ。", "Pass the fire from burning foes to their neighbors."),
                Power.Ember, 60, Power.Wildfire, 34),
            new UniqueDef("unique.crimson_katar", "weapon.ember_katar", new Txt("赤熱の一突き", "Crimson Thrust"),
                new Txt("四度目の突きは、必ず熱く貫く。", "Every fourth thrust pierces white-hot."),
                Power.Blaze, 82, Power.CriticalEcho, 5),
            new UniqueDef("unique.drowning_trident", "weapon.tide_trident", new Txt("溺れさせる潮", "Drowning Tide"),
                new Txt("動けぬ敵へ、三叉の穂先を深く突き立てよ。", "Drive the trident deep into foes who cannot move."),
                Power.Fetters, 18, Power.Executioner, 52),
            new UniqueDef("unique.tidecaller_trident", "weapon.tide_trident", new Txt("潮を招く者", "Tidecaller"),
                new Txt("技を放つたび、満ちる冷気が魔力を高める。", "Every skill cast lets the rising chill feed your magic."),
                Power.Frost, 60, Power.Overload, 22),
            new UniqueDef("unique.sworn_hammer", "weapon.vow_mace", new Txt("祈願の聖鎚", "Hammer of Devotion"),
                new Txt("聖堂に通うほど、鎚は重く輝く。", "The more you visit shrines, the brighter it shines."),
                Power.Devotion, 4, Power.Vigor, 18),
            new UniqueDef("unique.chalice_mace", "weapon.vow_mace", new Txt("溢れる誓杯", "Overflowing Vow"),
                new Txt("癒えすぎた命は、盾となって身を包む。", "Excess healing wraps you in a shield."),
                Power.Lifesteal, 11, Power.OverflowingLife, 48),
            new UniqueDef("unique.nightcrow_mask", "head.raven_mask", new Txt("夜烏の嘴", "Nightcrow Beak"),
                new Txt("闇を重ね、会心の余韻で技を回せ。", "Stack darkness and cycle skills on echoing crits."),
                Power.Umbra, 100, Power.CriticalEcho, 5),
            new UniqueDef("unique.carrion_pride", "head.raven_mask", new Txt("屍を啄む誇り", "Carrion Pride"),
                new Txt("追い詰めた獲物ほど、鴉は力を増す。", "The raven grows strong on prey it has cornered."),
                Power.PreyPride, 5, Power.Bloodlust, 22),
            new UniqueDef("unique.paper_lantern_hat", "head.lantern_hat", new Txt("灯籠流しの笠", "Floating Lantern Hat"),
                new Txt("聖堂を巡るほど、灯りは強く輝く。", "Visit shrines and the lanterns burn brighter."),
                Power.Radiance, 105, Power.Devotion, 4),
            new UniqueDef("unique.lamplit_shelter", "head.lantern_hat", new Txt("宵の雨宿り", "Evening Shelter"),
                new Txt("灯の下では、溢れた命が壁になる。", "Beneath the lamps, overflowing life becomes a wall."),
                Power.Barrier, 11, Power.OverflowingLife, 45),
            new UniqueDef("unique.linked_coif", "head.iron_coif", new Txt("連環の守り", "Linked Ward"),
                new Txt("足を止めた敵に、鎖が食い込み続ける。", "The links bite deeper into every slowed foe."),
                Power.Aegis, 22, Power.Fetters, 18),
            new UniqueDef("unique.vigil_coif", "head.iron_coif", new Txt("不寝番の頭巾", "Sleepless Watch"),
                new Txt("囲まれても、祈りが鎖を固くする。", "Even when surrounded, prayer hardens the mail."),
                Power.Bulwark, 32, Power.Devotion, 3),
            new UniqueDef("unique.starlit_veil", "head.dream_veil", new Txt("星屑の幕引き", "Starlit Curtain Call"),
                new Txt("短い間に術を重ね、星の終曲へ急げ。", "Cast in quick succession and hasten the stellar finale."),
                Power.Finale, 25, Power.Resonance, 11),
            new UniqueDef("unique.prism_dream_veil", "head.dream_veil", new Txt("結晶の寝覚め", "Crystal Waking"),
                new Txt("磨いた結晶が、夢の力を育てる。", "Polished crystals nurture the power of dreams."),
                Power.CrystalResonance, 2, Power.Overload, 22),
            new UniqueDef("unique.stag_king_crown", "head.antler_crown", new Txt("森の王の戴冠", "Crown of the Forest King"),
                new Txt("獲物を追い詰め、万全の身で仕留めろ。", "Hunt your prey down and strike in perfect health."),
                Power.PreyPride, 6, Power.Vigor, 18),
            new UniqueDef("unique.rutting_antlers", "head.antler_crown", new Txt("角突き合う闘争", "Locked Antlers"),
                new Txt("囲まれて殴られるほど、角は鋭く燃える。", "The more you are struck while surrounded, the sharper the antlers."),
                Power.Frenzy, 3, Power.Retaliation, 26),
            new UniqueDef("unique.inkflow_robe", "armor.ink_robe", new Txt("滲む墨の記録", "Bleeding Ink Ledger"),
                new Txt("結晶を磨き、術の合間も魔力を絶やすな。", "Polish crystals and keep magic flowing between spells."),
                Power.Overload, 22, Power.CrystalResonance, 2),
            new UniqueDef("unique.last_stroke_robe", "armor.ink_robe", new Txt("筆止めの衣", "Robe of the Last Stroke"),
                new Txt("技を重ね、回避で最後の一筆へ繋げ。", "Link your skills and dodge toward the final stroke."),
                Power.Finale, 22, Power.EchoingDodge, 84),
            new UniqueDef("unique.ring_hauberk", "armor.chain_hauberk", new Txt("縛り鎖の重帷子", "Binding Hauberk"),
                new Txt("囲んだ敵を鎖で縛り、まとめて削れ。", "Bind the foes around you and grind them down."),
                Power.Fetters, 17, Power.Bulwark, 32),
            new UniqueDef("unique.answering_mail", "armor.chain_hauberk", new Txt("応える鎖", "Answering Links"),
                new Txt("打たれた直後に、障壁と反撃で応じろ。", "Answer a blow at once with barrier and counterattack."),
                Power.Retaliation, 28, Power.Barrier, 11),
            new UniqueDef("unique.sparkwoven_jacket", "armor.ember_jacket", new Txt("舞い散る火の粉", "Scattering Sparks"),
                new Txt("燃える敵から燃える敵へ、火が踊り広がる。", "Fire dances from one burning foe to the next."),
                Power.Wildfire, 33, Power.Ember, 57),
            new UniqueDef("unique.scorch_runner_jacket", "armor.ember_jacket", new Txt("焦げ跡の疾走", "Scorched Dash"),
                new Txt("回避で駆け抜け、四撃目に炎を乗せよ。", "Dodge and dash, then ignite the fourth strike."),
                Power.Blaze, 75, Power.Sprint, 28),
            new UniqueDef("unique.moonmilk_silk", "armor.moon_silk", new Txt("月光の羽衣", "Moonmilk Raiment"),
                new Txt("闇を撒き、溢れた癒しを障壁へ変えろ。", "Spread darkness and turn overflowing healing into shields."),
                Power.Umbra, 100, Power.OverflowingLife, 45),
            new UniqueDef("unique.vesper_silk", "armor.moon_silk", new Txt("晩課の月衣", "Vesper Moonrobe"),
                new Txt("聖堂で祈り、切り札の後は星が身を守る。", "Pray at shrines, and stars shield you after your ultimate."),
                Power.Devotion, 4, Power.StarShield, 22),
            new UniqueDef("unique.porcupine_plate", "armor.spiked_plate", new Txt("針鼠の鎧", "Porcupine Plate"),
                new Txt("足を鈍らせた敵ほど、棘の傷が深く刺さる。", "Thorns cut deeper into foes you have slowed."),
                Power.Thorns, 36, Power.Fetters, 17),
            new UniqueDef("unique.retort_plate", "armor.spiked_plate", new Txt("棘返しの甲冑", "Barbed Retort"),
                new Txt("重い一撃を受けたら、障壁と反撃で返せ。", "Take a heavy blow, then answer with barrier and strikes."),
                Power.Aegis, 24, Power.Retaliation, 28),
            new UniqueDef("unique.hands_frozen_pledge", "hands.ice_bracers", new Txt("氷結の契り", "Pact of Frozen Bonds"),
                new Txt("動きを封じた敵に、ためらわず終わりを与えよ。", "Seal them in ice, then end them without hesitation."),
                Power.Fetters, 17, Power.Executioner, 44),
            new UniqueDef("unique.hands_prism_frost", "hands.ice_bracers", new Txt("結晶凍土", "Crystalfrost"),
                new Txt("装着した結晶の輝きが、冷気を研ぎ澄ます。", "Equipped crystals sharpen the cold."),
                Power.Frost, 54, Power.CrystalResonance, 2),
            new UniqueDef("unique.hands_bound_fury", "hands.chain_wraps", new Txt("縛鎖の憤怒", "Fury of the Bound"),
                new Txt("囲まれるほど拳は速まり、縛った敵には重く落ちる。", "Heavier fists fall on bound foes as you wade in."),
                Power.Frenzy, 3, Power.Fetters, 18),
            new UniqueDef("unique.hands_chain_breaker", "hands.chain_wraps", new Txt("鎖砕きの拳", "Chainbreaker Fist"),
                new Txt("一人倒すたび、拳は鎖ごと周囲を砕く。", "Each kill bursts through the chains around you."),
                Power.Shatter, 62, Power.Momentum, 5),
            new UniqueDef("unique.hands_sun_vow", "hands.sun_gauntlets", new Txt("陽の奉納", "Offering to the Sun"),
                new Txt("聖堂に祈るほど、壁となる光は厚くなる。", "The more you pray, the thicker the wall of light."),
                Power.Devotion, 4, Power.Bulwark, 30),
            new UniqueDef("unique.hands_gilded_rebuke", "hands.sun_gauntlets", new Txt("金色の叱責", "Gilded Rebuke"),
                new Txt("打たれたら光を宿し、すぐさま殴り返せ。", "Take the blow, then answer with radiant force."),
                Power.Radiance, 88, Power.Retaliation, 28),
            new UniqueDef("unique.hands_light_fingers", "hands.thief_gloves", new Txt("抜き足の指", "Light Fingers"),
                new Txt("会心のたびに技が戻る。初手の一撃を逃すな。", "Crits refresh your skills. Never waste the opening hit."),
                Power.CriticalEcho, 5, Power.OpeningStrike, 48),
            new UniqueDef("unique.hands_vanishing_act", "hands.thief_gloves", new Txt("消える手口", "Vanishing Act"),
                new Txt("身をかわし、弱った獲物を音もなく仕留める。", "Slip away, then finish the wounded in silence."),
                Power.EchoingDodge, 78, Power.Executioner, 44),
            new UniqueDef("unique.hands_overflow_fast", "hands.monk_wraps", new Txt("断食の巻布", "Fasting Wraps"),
                new Txt("余った癒しは、そのまま身を守る障壁となる。", "Excess healing turns into a protective barrier."),
                Power.OverflowingLife, 45, Power.Lifesteal, 9),
            new UniqueDef("unique.hands_pained_vow", "hands.monk_wraps", new Txt("苦行の拳", "Fist of Penance"),
                new Txt("囲まれ傷つくほど拳は軽くなり、倒れても立つ。", "Pain quickens your fists, and you rise when you fall."),
                Power.Frenzy, 3, Power.SecondWind, 28),
            new UniqueDef("unique.feet_dusk_wing", "feet.raven_boots", new Txt("宵の羽ばたき", "Dusk Wingbeat"),
                new Txt("追い詰めた獲物の闇が、足跡に力を与える。", "Darkness on the hunted lends strength to your tracks."),
                Power.PreyPride, 5, Power.Umbra, 85),
            new UniqueDef("unique.feet_caw_cyclone", "feet.raven_boots", new Txt("鴉の旋風", "Raven Cyclone"),
                new Txt("会心で技を戻し、回避の渦で敵をなぎ払え。", "Crit to refresh skills, then sweep foes with your dodge."),
                Power.CriticalEcho, 4, Power.Whirlwind, 78),
            new UniqueDef("unique.feet_noon_walk", "feet.sun_sandals", new Txt("正午の巡礼", "Noon Pilgrimage"),
                new Txt("祈りを重ねながら、光の中を駆け抜けよ。", "Stack prayers as you dash through the light."),
                Power.Sprint, 26, Power.Devotion, 4),
            new UniqueDef("unique.feet_warm_spring", "feet.sun_sandals", new Txt("温もりの泉", "Warming Spring"),
                new Txt("溢れた癒しを障壁に変え、追い風で進め。", "Turn overflowing heals into barriers and ride the wind."),
                Power.OverflowingLife, 40, Power.Tailwind, 24),
            new UniqueDef("unique.feet_dragged_chain", "feet.chain_greaves", new Txt("引きずる鎖", "Dragging Chains"),
                new Txt("殴られるたび鎖が鳴り、攻撃者に痛みが返る。", "Each blow rattles the chains and stings the attacker."),
                Power.Retaliation, 28, Power.Thorns, 32),
            new UniqueDef("unique.feet_anchor_wall", "feet.chain_greaves", new Txt("錨の重み", "Weight of the Anchor"),
                new Txt("身を守る障壁と溢れる命で、一歩も動かず耐えよ。", "Barriers and overflowing life let you hold your ground."),
                Power.OverflowingLife, 38, Power.Aegis, 22),
            new UniqueDef("unique.feet_thunder_step", "feet.storm_boots", new Txt("雷歩", "Thunderstep"),
                new Txt("倒すたびに加速し、回避の渦で雷鳴を撒く。", "Speed up with every kill and scatter thunder with dodges."),
                Power.Whirlwind, 80, Power.Momentum, 5),
            new UniqueDef("unique.feet_gale_hunter", "feet.storm_boots", new Txt("嵐の狩猟長", "Stormhunt Leader"),
                new Txt("追い風に乗って獲物に迫り、誇りを高めよ。", "Ride the tailwind and grow your pride in the hunt."),
                Power.Tailwind, 25, Power.PreyPride, 5),
            new UniqueDef("unique.feet_spark_trail", "feet.ember_slippers", new Txt("火の粉の轍", "Trail of Sparks"),
                new Txt("走り抜けた跡に火が広がり、敵を飲み込む。", "Fire spreads along your path and swallows enemies."),
                Power.Wildfire, 33, Power.Sprint, 26),
            new UniqueDef("unique.feet_cinder_waltz", "feet.ember_slippers", new Txt("灰のワルツ", "Cinder Waltz"),
                new Txt("身をかわすたび、スキルが早く戻り火が灯る。", "Every dodge hastens your skills and kindles flame."),
                Power.Ember, 57, Power.EchoingDodge, 78),
            new UniqueDef("unique.charm_dusk_quill", "charm.raven_feather", new Txt("宵闇の羽ペン", "Duskquill"),
                new Txt("会心の余韻で技を回し、弱った敵を断て。", "Cycle skills with crits and cut down the weakened."),
                Power.CriticalEcho, 5, Power.Executioner, 50),
            new UniqueDef("unique.charm_omen_wing", "charm.raven_feather", new Txt("凶兆の翼", "Wing of Omens"),
                new Txt("獲物の誇りを胸に、闇を重ねて追い込め。", "Carry the hunter's pride and layer darkness on prey."),
                Power.PreyPride, 6, Power.Umbra, 95),
            new UniqueDef("unique.charm_white_lotus", "charm.lotus_seal", new Txt("白蓮の泉", "White Lotus Spring"),
                new Txt("あふれる癒しを障壁に、倒れそうな時にも立て。", "Convert overflow into barriers and rise at the brink."),
                Power.OverflowingLife, 45, Power.SecondWind, 32),
            new UniqueDef("unique.charm_lotus_prayer", "charm.lotus_seal", new Txt("蓮の祈り", "Lotus Prayer"),
                new Txt("聖堂で祈りを重ね、障壁で静かに身を守る。", "Stack prayers at shrines and shelter behind barriers."),
                Power.Devotion, 4, Power.Barrier, 10),
            new UniqueDef("unique.charm_thunder_finale", "charm.storm_bell", new Txt("雷の終曲", "Thunder Finale"),
                new Txt("三つの技を重ねて奥義を呼び、雷を連ねよ。", "Chain your three skills to hasten the ultimate, trailed by lightning."),
                Power.Finale, 25, Power.ChainLightning, 60),
            new UniqueDef("unique.charm_ringing_storm", "charm.storm_bell", new Txt("鳴り止まぬ嵐", "Unceasing Storm"),
                new Txt("技を放つほど魔力が高まり、足も速くなる。", "Each skill raises your magic and quickens your steps."),
                Power.Overload, 22, Power.Sprint, 27),
            new UniqueDef("unique.charm_cold_bind", "charm.ice_heart", new Txt("凍える枷心", "Frostbound Heart"),
                new Txt("冷気を重ね、動けぬ敵を一方的に叩け。", "Layer cold and batter foes who cannot move."),
                Power.Fetters, 18, Power.Frost, 57),
            new UniqueDef("unique.charm_crystal_pulse", "charm.ice_heart", new Txt("結晶の鼓動", "Crystal Pulse"),
                new Txt("結晶が増えるほど、心臓の力が体を満たす。", "The more crystals you wear, the stronger the pulse."),
                Power.CrystalResonance, 2, Power.Vigor, 18),
            new UniqueDef("unique.charm_ink_prism", "charm.ink_stone", new Txt("墨の万華鏡", "Inkwell Prism"),
                new Txt("四つの色を墨に溶かし、術を重ねて放て。", "Dissolve four colors in ink and cast in layers."),
                Power.Convergence, 140, Power.Overload, 22),
            new UniqueDef("unique.charm_night_script", "charm.ink_stone", new Txt("夜の筆致", "Night Script"),
                new Txt("術を連ね、闇を塗り重ねて奥義を早めよ。", "String your spells, paint darkness, and hasten the finale."),
                Power.Finale, 22, Power.Umbra, 90),
            // v1.26：記憶・エッセンス・旅人に連携する固有品
            new UniqueDef("unique.link_butchers_feast", "weapon.bone_cleaver", new Txt("肉屋の祝宴", "Butcher's Banquet"),
                new Txt("集めた肉は傷を癒し、刃をさらに育てる。", "The meat you gather mends your wounds and feeds the blade."),
                Power.Lifesteal, 10, Power.OpeningStrike, 56) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike" }, Kind = LinkKind.MemorySurge, Value = 26 } },
            new UniqueDef("unique.link_arrow_rain", "weapon.hunting_bow", new Txt("天降る矢雨", "Heaven's Arrow Rain"),
                new Txt("空を覆うほどの矢が、味方の背を押して降り注ぐ。", "Arrows blot out the sky and rain down, driving you onward."),
                Power.Momentum, 5, Power.Executioner, 50) { Link = new LinkDef { Requires = new[] { "St_L_Multishot" }, Kind = LinkKind.MemorySurge, Value = 25 } },
            new UniqueDef("unique.link_fireball_avatar", "weapon.blaze_greatsword", new Txt("火球の化身", "Fireball Incarnate"),
                new Txt("身を火球に変えた魔女の熱が、剣に宿っている。", "The heat of a witch who became a fireball lingers in this blade."),
                Power.Blaze, 70, Power.Shatter, 70) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball" }, Kind = LinkKind.MemoryHaste, Value = 35 } },
            new UniqueDef("unique.link_undying_ember_curse", "weapon.ember_whip", new Txt("消えずの熾", "Unquenched Cinder"),
                new Txt("一度燃え移った呪いの炎は、いつまでも消えない。", "Once the cursed flame catches, it never goes out."),
                Power.Ember, 52, Power.Executioner, 45) { Link = new LinkDef { Requires = new[] { "Gem_U_EternalFlame" }, Kind = LinkKind.Attune, Value = 18 } },
            new UniqueDef("unique.link_white_flash", "weapon.lantern_rod", new Txt("白一色の閃光", "Flash of Pure White"),
                new Txt("十秒ごとに満ちる純白の光が、次の一撃を研ぎ澄ます。", "Every ten seconds a pure white light gathers and sharpens the next strike."),
                Power.Radiance, 88, Power.Executioner, 40) { Link = new LinkDef { Requires = new[] { "Gem_L_PureWhite" }, Kind = LinkKind.Attune, Value = 17 } },
            new UniqueDef("unique.link_dawn_breaker", "weapon.dawn_scepter", new Txt("大地を割る曙光", "Dawn That Splits the Earth"),
                new Txt("世界を割る光は、途切れることなく敵を焼き続ける。", "The light that splits the world keeps burning foes without end."),
                Power.Radiance, 88, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "St_U_WorldCracker" }, Kind = LinkKind.MemorySurge, Value = 29 } },
            new UniqueDef("unique.link_hero_homecoming", "armor.lampkeeper_mantle", new Txt("英雄の還り道", "Hero's Way Home"),
                new Txt("倒れた英雄は還り、力を永遠に刻んで立ち上がる。", "A fallen hero returns, etches their power in stone, and rises."),
                Power.Aegis, 20, Power.OverflowingLife, 40) { Link = new LinkDef { Requires = new[] { "St_L_HerosReturn" }, Kind = LinkKind.Guard, Value = 19 } },
            new UniqueDef("unique.link_suspicion_thorns", "armor.thorn_mail", new Txt("疑心の棘衣", "Mantle of Suspicion"),
                new Txt("疑り深い者ほど、傷を負うたびに記憶が速く巡る。", "The more suspicious you are, the faster memories turn with every wound."),
                Power.Retaliation, 25, Power.EchoingDodge, 78) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia" }, Kind = LinkKind.Guard, Value = 18 } },
            new UniqueDef("unique.link_paired_shell", "armor.bastion_shell", new Txt("対なる殻", "Twin Shell"),
                new Txt("命の灯を一つに絞り、分厚い殻で包み込む。", "You narrow your life to a single flame and wrap it in a thick shell."),
                Power.Barrier, 10, Power.OverflowingLife, 40) { Link = new LinkDef { Requires = new[] { "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = 20 } },
            new UniqueDef("unique.link_dragon_whelp_forge", "armor.ember_plate", new Txt("仔竜の炉心", "Whelp Furnace"),
                new Txt("小さな炉の中で、炎の竜が眠りながら熱を吐く。", "Within the small furnace, a dragon of flame breathes heat in its sleep."),
                Power.Thorns, 30, Power.Whirlwind, 80) { Link = new LinkDef { Requires = new[] { "St_L_SmallMoltenCore" }, Kind = LinkKind.MemorySurge, Value = 27 } },
            new UniqueDef("unique.link_sealed_gold", "armor.spiked_plate", new Txt("封じられし黄金", "Sealed Gold"),
                new Txt("血を捧げて封印を解けば、黄金の爆発が敵を呑む。", "Offer blood to break the seal and a golden blast engulfs your foes."),
                Power.Thorns, 30, Power.Barrier, 10) { Link = new LinkDef { Requires = new[] { "Gem_L_SuppressedArcanum" }, Kind = LinkKind.Attune, Value = 19 } },
            new UniqueDef("unique.link_flawless_vestment", "armor.star_mantle", new Txt("欠けなき衣", "Unblemished Vestment"),
                new Txt("何一つ欠けることなく、あらゆる力が少しずつ高まる。", "Nothing is lacking, and every strength grows a little."),
                Power.Bulwark, 30, Power.Aegis, 20) { Link = new LinkDef { Requires = new[] { "Gem_L_Perfect" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_snowstorm_ward", "charm.frost_pendant", new Txt("雪嵐の護り", "Ward of the Snowstorm"),
                new Txt("吹き荒れる雪嵐が、敵を凍らせ味方を包む。", "A raging snowstorm freezes foes and wraps allies in cover."),
                Power.Frost, 52, Power.Aegis, 20) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard" }, Kind = LinkKind.Guard, Value = 18 } },
            new UniqueDef("unique.link_gold_coin_flicker", "charm.sun_brooch", new Txt("金貨の閃き", "Flash of the Gold Coin"),
                new Txt("投げた金貨が裏か表か、光が弾けて運命が決まる。", "Heads or tails, a coin flips and light bursts to decide your fate."),
                Power.SpendersWard, 10, Power.Shatter, 60) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion" }, Kind = LinkKind.MemoryHaste, Value = 32 } },
            new UniqueDef("unique.link_hollow_her", "charm.shadow_ring", new Txt("虚空の彼女", "She of the Hollow"),
                new Txt("彼女の世界は黒く渦を巻き、すべてを呑み込んでいく。", "Her world coils in black and swallows everything."),
                Power.Umbra, 88, Power.Convergence, 140) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld" }, Kind = LinkKind.MemorySurge, Value = 24 } },
            new UniqueDef("unique.link_priceless_present", "charm.lotus_seal", new Txt("値のない贈り物", "Priceless Present"),
                new Txt("値札のない贈り物は、受け取るほどに持ち主を守る。", "A gift without a price tag protects its owner all the more."),
                Power.SecondWind, 30, Power.Overload, 22) { Link = new LinkDef { Requires = new[] { "Gem_L_CamillasGift" }, Kind = LinkKind.Guard, Value = 16 } },
            new UniqueDef("unique.link_golden_pulse", "charm.iron_seal", new Txt("金色の拍動", "Golden Pulse"),
                new Txt("懐の金貨を燃やすたび、心臓が力強く脈を打つ。", "Each time you burn coin from your purse, your heart beats stronger."),
                Power.SpendersWard, 10, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "Gem_L_HeartOfGold" }, Kind = LinkKind.Attune, Value = 17 } },
            new UniqueDef("unique.link_rainbow_ore", "charm.dream_lens", new Txt("虹色の鉱石", "Iridescent Ore"),
                new Txt("光を受けて不思議な色に輝く結晶が、力を育てる。", "A crystal glowing in uncanny hues under the light nurtures your power."),
                Power.CrystalResonance, 2, Power.Ember, 56) { Link = new LinkDef { Requires = new[] { "Gem_L_MetalCrystal" }, Kind = LinkKind.Attune, Value = 16 } },
            new UniqueDef("unique.link_bursting_halo", "head.radiant_halo", new Txt("炸裂する光輪", "Bursting Halo"),
                new Txt("光が弾けるたび、倒れた敵の跡で再び炸裂する。", "Each burst of light erupts again where a felled enemy lay."),
                Power.Radiance, 88, Power.Finale, 20) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion" }, Kind = LinkKind.MemorySurge, Value = 28 } },
            new UniqueDef("unique.link_balance_pillar", "head.sun_mask", new Txt("天秤の光柱", "Pillar of the Scales"),
                new Txt("巨大な光の柱が、敵を灼き仲間を癒して釣り合う。", "A colossal pillar of light scorches foes and heals allies in balance."),
                Power.Radiance, 88, Power.StarShield, 20) { Link = new LinkDef { Requires = new[] { "St_U_BeamOfBalance" }, Kind = LinkKind.MemorySurge, Value = 22 } },
            new UniqueDef("unique.link_forgetting_cry", "head.raven_mask", new Txt("忘れ去る叫び", "Cry of Forgetting"),
                new Txt("世界の記憶を塗り潰す叫びが、闇となって轟く。", "A cry that paints over the world memory roars forth as darkness."),
                Power.Umbra, 88, Power.UltimateSurge, 25) { Link = new LinkDef { Requires = new[] { "St_U_ShoutOfOblivion" }, Kind = LinkKind.MemorySurge, Value = 24 } },
            new UniqueDef("unique.link_discord_fruit", "head.dream_veil", new Txt("不和の果実", "Fruit of Discord"),
                new Txt("かじるたびに記憶は別の姿へ変わり、気まぐれに力をくれる。", "With every bite a memory changes shape and lends whimsical power."),
                Power.Overload, 20, Power.Finale, 20) { Link = new LinkDef { Requires = new[] { "Gem_L_ChaosApple" }, Kind = LinkKind.Attune, Value = 18 } },
            new UniqueDef("unique.link_stacked_prayers", "head.healer_band", new Txt("信仰の積み重ね", "Stacked Devotion"),
                new Txt("討つたびに信仰が積もり、祈りは確かな力となる。", "Each foe you fell adds to your faith until prayer becomes real power."),
                Power.Devotion, 4, Power.Resonance, 9) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_final_star_lamp", "head.star_diadem", new Txt("終星の灯", "Lamp of the Final Star"),
                new Txt("最後の星明かりが堕ちる場所に、黒い穴が口を開ける。", "Where the last starlight falls, a black hole opens its mouth."),
                Power.StarShield, 20, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "Gem_U_LastStarlight" }, Kind = LinkKind.Attune, Value = 19 } },
            new UniqueDef("unique.link_creeping_dark_claws", "hands.void_claws", new Txt("蝕む闇の爪", "Claws of Creeping Dark"),
                new Txt("心を蝕む闇は、敵が息絶えるまで消えることがない。", "The darkness that corrodes the mind never fades until the enemy dies."),
                Power.Fetters, 15, Power.OpeningStrike, 56) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption" }, Kind = LinkKind.Attune, Value = 17 } },
            new UniqueDef("unique.link_mad_laughter_fists", "hands.shadow_gloves", new Txt("狂笑の乱舞", "Dance of Mad Laughter"),
                new Txt("狂ったように笑いながら、闇の拳を絶え間なく浴びせる。", "Laughing madly, you rain dark fists without pause."),
                Power.Frenzy, 3, Power.Executioner, 60) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria" }, Kind = LinkKind.MemoryHaste, Value = 36 } },
            new UniqueDef("unique.link_rebounding_flametail", "hands.flame_grips", new Txt("跳ね返る火尾", "Rebounding Flametail"),
                new Txt("跳ね返った攻撃が、尾を引く炎となって別の敵を焼く。", "A rebounding strike trails flame and scorches another foe."),
                Power.Ember, 52, Power.ChainLightning, 50) { Link = new LinkDef { Requires = new[] { "Gem_L_Embertail" }, Kind = LinkKind.Attune, Value = 18 } },
            new UniqueDef("unique.link_frozen_heart_mend", "hands.frost_mitts", new Txt("氷核の癒し", "Mending of the Ice Core"),
                new Txt("凍てつく核が時折脈打ち、傷をゆっくり癒していく。", "The frozen core pulses now and then, slowly mending your wounds."),
                Power.Frost, 52, Power.Lifesteal, 10) { Link = new LinkDef { Requires = new[] { "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = 18 } },
            new UniqueDef("unique.link_phantom_pebbles", "hands.sling_bracers", new Txt("亡霊の礫", "Phantom Pebbles"),
                new Txt("撃ち抜いた敵の魂が、夢の塵となって手元に残る。", "The souls of shot foes remain in your hand as dream dust."),
                Power.Executioner, 45, Power.Bloodlust, 20) { Link = new LinkDef { Requires = new[] { "St_L_SpectreBullet" }, Kind = LinkKind.MemoryHaste, Value = 30 } },
            new UniqueDef("unique.link_scorching_sun_gaze", "hands.sun_gauntlets", new Txt("灼陽の瞳", "Gaze of the Scorching Sun"),
                new Txt("二十秒ごとに太陽の眼が開き、燃える敵を一斉に焼く。", "Every twenty seconds the sun eye opens and burns all burning foes at once."),
                Power.Wildfire, 30, Power.Executioner, 56) { Link = new LinkDef { Requires = new[] { "Gem_L_SolarEye" }, Kind = LinkKind.Attune, Value = 19 } },
            new UniqueDef("unique.link_earthdiver_boots", "feet.rooted_boots", new Txt("土潜りの靴", "Earthdiver Boots"),
                new Txt("地の底に潜れば、どんな攻撃も届かない。", "Dive beneath the earth and no attack can reach you."),
                Power.PerfectRead, 20, Power.Bulwark, 30) { Link = new LinkDef { Requires = new[] { "St_U_Burrow" }, Kind = LinkKind.MemoryHaste, Value = 33 } },
            new UniqueDef("unique.link_fang_leap", "feet.wolf_boots", new Txt("牙の跳躍", "Fang Leap"),
                new Txt("大口を開けて飛びつき、獲物の力を丸ごと食らう。", "Leap with jaws wide and devour your prey whole."),
                Power.SoulSiphon, 20, Power.Momentum, 5) { Link = new LinkDef { Requires = new[] { "St_U_BigChomp" }, Kind = LinkKind.MemoryHaste, Value = 30 } },
            new UniqueDef("unique.link_soul_cage_clogs", "feet.iron_clogs", new Txt("魂を閉ざす檻", "Cage Sealing the Soul"),
                new Txt("致命の一撃を檻が受け止め、魂は束の間の無敵を得る。", "The cage takes the fatal blow and the soul gains a brief invulnerability."),
                Power.PerfectRead, 20, Power.Aegis, 20) { Link = new LinkDef { Requires = new[] { "Gem_U_SoulPrison" }, Kind = LinkKind.Guard, Value = 20 } },
            new UniqueDef("unique.link_cook_stride", "feet.pilgrim_boots", new Txt("料理人の足取り", "Stride of the Cook"),
                new Txt("倒れた敵から食材を拾い集め、旅の腹を満たしていく。", "You gather ingredients from fallen foes and feed the journey."),
                Power.SoulSiphon, 20, Power.PreyPride, 5) { Link = new LinkDef { Requires = new[] { "Gem_L_Culinary" }, Kind = LinkKind.Attune, Value = 16 } },
            new UniqueDef("unique.link_unbound_steps", "feet.wind_sandals", new Txt("解き放たれた足", "Unbound Steps"),
                new Txt("枷を解いた足は軽く、記憶を放つたび全身が力にあふれる。", "With shackles shed, your feet are light and each memory floods you with power."),
                Power.Sprint, 25, Power.EchoingDodge, 98) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty" }, Kind = LinkKind.Attune, Value = 17 } },
            new UniqueDef("unique.link_stalled_needle_trail", "feet.dawn_steps", new Txt("止まった針の旅路", "Journey of the Stilled Needle"),
                new Txt("針の止まった羅針盤が、強い力を待ちわびている。", "A compass with a stilled needle waits for a mighty power."),
                Power.Tailwind, 25, Power.OpeningStrike, 62) { Link = new LinkDef { Requires = new[] { "Gem_U_GuidingCompass_NotCharged" }, Kind = LinkKind.Attune, Value = 15 } },
            new UniqueDef("unique.link_vesper_holy_vow", "armor.guardian_plate", new Txt("聖盾の宣誓", "Vow of the Holy Shield"),
                new Txt("エルの名のもとに、誰も倒れさせはしない。", "In El's name, no one shall fall."),
                Power.Barrier, 10, Power.Vigor, 20) { Link = new LinkDef { Requires = new[] { "Hero_Vesper" }, Kind = LinkKind.Guard, Value = 20 } },
            new UniqueDef("unique.link_vesper_charge_rampart", "feet.guard_sabatons", new Txt("鉄壁の突進", "Rampart Rush"),
                new Txt("盾を構えたまま、運命の中へ踏み込め。", "Charge into fate with your shield held high."),
                Power.Aegis, 20, Power.Barrier, 9) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_M_Charge" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_vesper_resolve_mercy", "weapon.shield_maul", new Txt("決意と慈悲", "Resolve and Mercy"),
                new Txt("四度目の一撃で止め、慈悲の光で立たせる。", "The fourth strike halts them; mercy lifts them up."),
                Power.Lifesteal, 10, Power.Vigor, 16) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_D_Resolve", "St_D_MercyOfEl" }, Kind = LinkKind.Attune, Value = 46 } },
            new UniqueDef("unique.link_vesper_cruel_sun", "hands.ember_gauntlets", new Txt("裁きの烈陽", "Sun of Judgment"),
                new Txt("容赦なき陽が、膝をつく者を照らし出す。", "A merciless sun lights up those who kneel."),
                Power.Ember, 52, Power.StillWater, 8) { Link = new LinkDef { Requires = new[] { "St_Q_CruelSun" }, Kind = LinkKind.MemoryHaste, Value = 38 } },
            new UniqueDef("unique.link_lacerta_gunslinger", "armor.hunter_vest", new Txt("銃士の矜持", "Gunslinger's Pride"),
                new Txt("抜く手も見せず、勝負はもう終わっている。", "The duel is over before the hand is even seen."),
                Power.Whirlwind, 80, Power.Sprint, 25) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_lacerta_dodge_draw", "feet.dancer_shoes", new Txt("すれ違いの早撃ち", "Quickdraw in Passing"),
                new Txt("かわした瞬間に、もう銃口は向いている。", "The instant you dodge, the muzzle is already aimed."),
                Power.EchoingDodge, 78, Power.Momentum, 5) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_M_NimbleDodge" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_lacerta_powder_tap", "hands.flame_grips", new Txt("双発の火薬庫", "Twin-Shot Powder Keg"),
                new Txt("二連の銃声のあとに、四度目の爆炎が咲く。", "After two shots, the fourth blooms into flame."),
                Power.Blaze, 70, Power.Momentum, 5) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_D_DoubleTap", "St_D_SalamanderPowder" }, Kind = LinkKind.Attune, Value = 46 } },
            new UniqueDef("unique.link_lacerta_precision", "weapon.longspike_bow", new Txt("必中の一射", "The Sure Shot"),
                new Txt("息を止め、狙いを定め、星を撃ち抜く。", "Hold your breath, take aim, and shoot a star."),
                Power.Executioner, 45, Power.OpeningStrike, 50) { Link = new LinkDef { Requires = new[] { "St_R_PrecisionShot" }, Kind = LinkKind.MemorySurge, Value = 30 } },
            new UniqueDef("unique.link_cetus_glacier_guard", "armor.bastion_shell", new Txt("氷河の守護者", "Glacier Guardian"),
                new Txt("凍りついた背中は、仲間を守る盾になる。", "A frozen back becomes a shield for allies."),
                Power.Bulwark, 30, Power.StillWater, 8) { Link = new LinkDef { Requires = new[] { "Hero_Cetus" }, Kind = LinkKind.Guard, Value = 20 } },
            new UniqueDef("unique.link_cetus_frozen_blood", "hands.frost_mitts", new Txt("凍血の誓い", "Oath of Frozen Blood"),
                new Txt("血が凍るほど、一撃は冷たく重くなる。", "The colder the blood, the heavier the blow."),
                Power.Frost, 52, Power.Executioner, 48) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_D_IcyVeins" }, Kind = LinkKind.Attune, Value = 30 } },
            new UniqueDef("unique.link_cetus_cold_front", "weapon.glacier_spear", new Txt("寒波の砕氷", "Cold Front Breaker"),
                new Txt("巨大な氷塊が落ち、寒気がすべてを閉ざす。", "A giant chunk of ice falls, and the chill seals all."),
                Power.Fetters, 15, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_Q_BigBorealChunk", "St_Q_EmbracingTheChill" }, Kind = LinkKind.MemoryHaste, Value = 80 } },
            new UniqueDef("unique.link_cetus_manners", "head.frost_helm", new Txt("礼節の氷拳", "Courteous Ice Fist"),
                new Txt("礼を尽くした拳は、氷よりも固く痛い。", "A courteous fist is harder and colder than ice."),
                Power.Vigor, 16, Power.Barrier, 10) { Link = new LinkDef { Requires = new[] { "St_R_FrozenFists" }, Kind = LinkKind.MemorySurge, Value = 30 } },
            new UniqueDef("unique.link_yubar_star_traveler", "armor.star_cloak", new Txt("星間の旅人", "Interstellar Traveler"),
                new Txt("星々のあいだを、ひとすじの光が渡っていく。", "A single beam of light crosses between the stars."),
                Power.StarShield, 20, Power.EchoingDodge, 78) { Link = new LinkDef { Requires = new[] { "Hero_Yubar" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_yubar_still_clock", "head.dream_circlet", new Txt("静謐の星時計", "Serene Star Clock"),
                new Txt("心を凍らせれば、時は味方についてくる。", "Still your heart, and time will take your side."),
                Power.Finale, 20, Power.LucidBoon, 7) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_R_Tranquility" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_yubar_nova_end", "weapon.star_harp", new Txt("終焉の新星", "Nova of Endings"),
                new Txt("星は一度だけ満ち、世界を白く塗り替える。", "The star swells only once, painting the world white."),
                Power.UltimateSurge, 25, Power.Resonance, 8) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_Q_SuperNova", "St_R_Cataclysm" }, Kind = LinkKind.MemorySurge, Value = 66 } },
            new UniqueDef("unique.link_yubar_star_echo", "charm.clockwork_charm", new Txt("星々の反響", "Echo of Stars"),
                new Txt("放った光は、星々に跳ね返って戻ってくる。", "The light you cast bounces off the stars and returns."),
                Power.Convergence, 140, Power.Resonance, 8) { Link = new LinkDef { Requires = new[] { "St_D_ConvergencePoint" }, Kind = LinkKind.MemoryHaste, Value = 38 } },
            new UniqueDef("unique.link_husk_one_flash", "weapon.dusk_scythe", new Txt("一閃の旅人", "Traveler of One Flash"),
                new Txt("風より先に、刃のほうが通り過ぎていく。", "The blade passes by before the wind does."),
                Power.Executioner, 45, Power.Frenzy, 3) { Link = new LinkDef { Requires = new[] { "Hero_Husk" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_husk_death_glove", "hands.shadow_gloves", new Txt("死告げの手袋", "Gloves of Foretold Death"),
                new Txt("刻まれた名は、いずれ必ず闇へ還っていく。", "A name once marked will return to the dark."),
                Power.CriticalEcho, 4, Power.Executioner, 40) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_Q_DeathMark" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_husk_wind_scar", "feet.stalker_boots", new Txt("風傷の殺陣", "Wind-Scar Melee"),
                new Txt("ひと走りごとに、一つの影が消えていく。", "With each dash, another shadow disappears."),
                Power.Sprint, 25, Power.Momentum, 5) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_D_TheKillingFlow", "St_D_ScarOfTheWind" }, Kind = LinkKind.Attune, Value = 48 } },
            new UniqueDef("unique.link_husk_vanish", "armor.shadow_cloak", new Txt("霞み消える者", "One Who Fades Away"),
                new Txt("姿を消した者は、いつも背後から現れる。", "Those who vanish always reappear behind you."),
                Power.Sprint, 25, Power.Retaliation, 25) { Link = new LinkDef { Requires = new[] { "St_R_Deception" }, Kind = LinkKind.MemorySurge, Value = 30 } },
            new UniqueDef("unique.link_mist_swordsman", "head.mist_veil", new Txt("霧の剣客", "Swordsman of Mist"),
                new Txt("霧が晴れたとき、剣先だけがそこに残る。", "When the mist clears, only the point remains."),
                Power.Barrier, 10, Power.Aegis, 16) { Link = new LinkDef { Requires = new[] { "Hero_Mist" }, Kind = LinkKind.Guard, Value = 20 } },
            new UniqueDef("unique.link_mist_riposte", "armor.counter_gauntlets", new Txt("受けて返す剣", "Parry and Return"),
                new Txt("弾いた刃の勢いを、そのまま突きに乗せる。", "Ride the force of the deflected blade into a thrust."),
                Power.Retaliation, 25, Power.Bulwark, 30) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_R_Parry" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_mist_masterwork", "weapon.twin_rapier", new Txt("傑作の連剣", "Masterwork Flurry"),
                new Txt("守りと速さを重ねた一突きが、絵になる。", "A thrust of guard and speed becomes a painting."),
                Power.OpeningStrike, 50, Power.Momentum, 5) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "St_D_AstridsMasterpieceEnGarde", "St_D_AstridsMasterpiecePriorite" }, Kind = LinkKind.Attune, Value = 46 } },
            new UniqueDef("unique.link_mist_fleche", "feet.dancer_shoes", new Txt("矢の如き踏み込み", "Arrow-Swift Lunge"),
                new Txt("間合いを一瞬で詰め、ただ一太刀で決める。", "Close the gap in an instant and end it in one cut."),
                Power.Momentum, 5, Power.PreyPride, 5) { Link = new LinkDef { Requires = new[] { "St_Q_Fleche" }, Kind = LinkKind.MemoryHaste, Value = 38 } },
            new UniqueDef("unique.link_nachia_wolf_shaman", "charm.oak_amulet", new Txt("狼の導き手", "Wolf Shaman"),
                new Txt("群れを率いる者は、牙より先に腕を広げる。", "The pack's mother opens her arms before her fangs."),
                Power.Resonance, 9, Power.Lifesteal, 10) { Link = new LinkDef { Requires = new[] { "Hero_Nachia" }, Kind = LinkKind.Guard, Value = 20 } },
            new UniqueDef("unique.link_nachia_leaf_call", "feet.root_sandals", new Txt("葉犬の呼び声", "Call of the Leaf Hound"),
                new Txt("森が呼べば、緑の猟犬がどこからでも駆けてくる。", "When the forest calls, green hounds come running."),
                Power.Bulwark, 30, Power.OverflowingLife, 40) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_Q_SylvanCall" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_nachia_moon_circle", "weapon.bone_flute", new Txt("月光の命環", "Moonlit Circle of Life"),
                new Txt("月夜に走る人狼へ、命の恵みが巡っていく。", "Life flows around the werewolf running under the moon."),
                Power.Frenzy, 3, Power.OpeningStrike, 75) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_Q_MoonlightPact", "St_D_CircleOfLife" }, Kind = LinkKind.Attune, Value = 48 } },
            new UniqueDef("unique.link_nachia_pack_heart", "head.wolf_pelt", new Txt("群れの心音", "Heartbeat of the Pack"),
                new Txt("仲間が強くなるほど、自分の鼓動も高鳴る。", "The stronger your allies, the faster your pulse."),
                Power.Resonance, 9, Power.Bloodlust, 20) { Link = new LinkDef { Requires = new[] { "St_D_HeartOfThePack" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_aurena_light_archer", "weapon.dawn_scepter", new Txt("光の射手", "Archer of Light"),
                new Txt("遠くから放つ光は、敵にも味方にも届く。", "Light shot from afar reaches foe and friend alike."),
                Power.Radiance, 88, Power.Lifesteal, 10) { Link = new LinkDef { Requires = new[] { "Hero_Aurena" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_aurena_golden_grace", "hands.healer_hands", new Txt("黄金の恵み", "Golden Grace"),
                new Txt("光が弾けるたび、傷が静かに癒えていく。", "Each burst of light quietly heals the wounded."),
                Power.Lifesteal, 10, Power.Momentum, 5) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_Q_GoldenBurst" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_aurena_return_chain", "armor.sun_plate", new Txt("還元の連鎖光", "Chain of Returning Light"),
                new Txt("敵に注いだ力が、味方の傷口へ還っていく。", "The power poured on foes returns to mend your allies."),
                Power.OverflowingLife, 40, Power.Bulwark, 32) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "St_Q_Reduction", "St_R_ChainReaction" }, Kind = LinkKind.MemorySurge, Value = 66 } },
            new UniqueDef("unique.link_aurena_claw", "charm.sun_brooch", new Txt("命を啜る爪", "Life-Drinking Claw"),
                new Txt("奪った命を、そのまま自分の力に変える。", "Turn the life you take into your own strength."),
                Power.SoulSiphon, 20, Power.SecondWind, 30) { Link = new LinkDef { Requires = new[] { "St_D_DisintegratingClaw" }, Kind = LinkKind.Guard, Value = 20 } },
            new UniqueDef("unique.link_bismuth_facet", "charm.dream_lens", new Txt("多面の結晶体", "Faceted Crystal Body"),
                new Txt("どの面も光を返し、どの色も嘘をつかない。", "Every facet returns the light, and no color lies."),
                Power.CrystalResonance, 2, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link_bismuth_prism_dash", "feet.wind_sandals", new Txt("歪光の疾走", "Warped-Light Sprint"),
                new Txt("走り抜けた軌跡が、虹色の残像を引く。", "The path you run leaves a rainbow afterimage."),
                Power.EchoingDodge, 78, Power.Tailwind, 25) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_M_Sprint" }, Kind = LinkKind.MemoryHaste, Value = 62 } },
            new UniqueDef("unique.link_bismuth_crimson_white", "head.radiant_halo", new Txt("紅蓮と純白", "Crimson and White"),
                new Txt("燃える炎と清い光が、結晶の中で重なる。", "Blazing flame and pure light overlap inside the crystal."),
                Power.Radiance, 88, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "St_QR_InfernalTales", "St_QR_Innocence" }, Kind = LinkKind.MemorySurge, Value = 66 } },
            new UniqueDef("unique.link_bismuth_prism_eye", "weapon.hunting_bow", new Txt("プリズムの眼", "Prism Eye"),
                new Txt("瞳が光を束ね、勝手に敵を射抜いていく。", "The eye gathers light and pierces foes by itself."),
                Power.ChainLightning, 50, Power.Radiance, 88) { Link = new LinkDef { Requires = new[] { "St_D_PrismaticEyes" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.link2_frostbound_slumber", "armor.frost_coat", new Txt("凍土の眠り", "Frostbound Slumber"),
                new Txt("吹雪の下で土に潜れば、春まで誰にも見つからない。", "Burrow beneath the blizzard and no one finds you until spring."),
                Power.Aegis, 20, Power.Vigor, 16) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard", "St_U_Burrow" }, Kind = LinkKind.MemoryHaste, Value = 55 } },
            new UniqueDef("unique.link2_carnival_of_cleavers", "weapon.bone_cleaver", new Txt("狂宴の肉切り", "Carnival of Cleavers"),
                new Txt("肉を断つ音が重なるほど、笑い声は大きくなる。", "The more the cleaver sings, the louder the laughter grows."),
                Power.Executioner, 45, Power.SecondWind, 30) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike", "St_U_Hysteria" }, Kind = LinkKind.MemorySurge, Value = 42 } },
            new UniqueDef("unique.link2_golden_flash", "head.sun_mask", new Txt("黄金の閃光", "Golden Flash"),
                new Txt("投げた金貨が光となって弾け、闇を押しのける。", "A tossed coin bursts into light and shoves the dark aside."),
                Power.Radiance, 88, Power.LucidBoon, 9) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "St_L_LightExplosion" }, Kind = LinkKind.MemoryHaste, Value = 52 } },
            new UniqueDef("unique.link2_oblivious_madness", "head.void_helm", new Txt("忘却の狂想", "Rhapsody of Oblivion"),
                new Txt("正気を蝕む闇に、忘却の叫びが最後の一押しを添える。", "A shout of oblivion pushes the mind-eating dark over the edge."),
                Power.Umbra, 88, Power.Bloodlust, 20) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption", "St_U_ShoutOfOblivion" }, Kind = LinkKind.MemorySurge, Value = 44 } },
            new UniqueDef("unique.link2_ghost_arrow_rain", "weapon.longspike_bow", new Txt("霊矢の雨", "Rain of Ghost Arrows"),
                new Txt("放った矢は霊となり、倒れた敵の夢まで射抜く。", "Each arrow becomes a spirit and pierces even a fallen foe's dreams."),
                Power.Momentum, 5, Power.Frenzy, 2) { Link = new LinkDef { Requires = new[] { "St_L_Multishot", "St_L_SpectreBullet" }, Kind = LinkKind.MemoryHaste, Value = 58 } },
            new UniqueDef("unique.link2_dragon_hearth", "armor.ember_plate", new Txt("炎龍の炉心", "Heart of the Fire Drake"),
                new Txt("火球と溶鉱炉が重なり、小さな龍が目を覚ます。", "Fireball meets furnace, and a small drake opens its eyes."),
                Power.Retaliation, 25, Power.Vigor, 15) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "St_L_SmallMoltenCore" }, Kind = LinkKind.MemoryHaste, Value = 60 } },
            new UniqueDef("unique.link2_returning_beam", "charm.pulsing_core", new Txt("帰還の光条", "Beam of Return"),
                new Txt("倒れた英雄を、均衡の光が静かに引き戻す。", "A balanced beam of light gently draws the fallen hero back."),
                Power.SecondWind, 30, Power.StarShield, 20) { Link = new LinkDef { Requires = new[] { "St_L_HerosReturn", "St_U_BeamOfBalance" }, Kind = LinkKind.MemorySurge, Value = 46 } },
            new UniqueDef("unique.link2_void_worldbreak", "weapon.dusk_scythe", new Txt("虚空の砕世", "Void Worldbreaker"),
                new Txt("彼女の世界が開き、世界の殻が音もなく砕ける。", "Her world opens, and the shell of the world cracks without a sound."),
                Power.Overload, 20, Power.Executioner, 45) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld", "St_U_WorldCracker" }, Kind = LinkKind.MemorySurge, Value = 48 } },
            new UniqueDef("unique.link2_feast_maw", "hands.bone_knuckles", new Txt("食卓の大顎", "Maw of the Feast"),
                new Txt("大きな一口の後には、必ず豪華な料理が並ぶ。", "After every great bite, a lavish meal is laid out."),
                Power.Lifesteal, 10, Power.OpeningStrike, 56) { Link = new LinkDef { Requires = new[] { "St_U_BigChomp", "Gem_L_Culinary" }, Kind = LinkKind.MemoryHaste, Value = 50 } },
            new UniqueDef("unique.link2_undying_furnace", "charm.hearthstone", new Txt("不滅の炉火", "Undying Furnace"),
                new Txt("消えぬ呪いの炎が、小さな炉をいつまでも熱くする。", "A curse of unending flame keeps the little furnace burning."),
                Power.Shatter, 60, Power.Vigor, 16) { Link = new LinkDef { Requires = new[] { "St_L_SmallMoltenCore", "Gem_U_EternalFlame" }, Kind = LinkKind.MemoryHaste, Value = 55 } },
            new UniqueDef("unique.link2_glacier_waking", "armor.frost_robe", new Txt("氷河の目覚め", "Glacier's Waking"),
                new Txt("吹雪が止むころ、氷河の核が静かに脈を打つ。", "When the blizzard fades, the glacier's core begins to beat."),
                Power.Barrier, 10, Power.StarShield, 20) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard", "Gem_U_GlacialCore" }, Kind = LinkKind.MemoryHaste, Value = 56 } },
            new UniqueDef("unique.link2_golden_wager", "charm.clockwork_charm", new Txt("黄金の賭け", "Golden Wager"),
                new Txt("金貨を弾いて賭けに出れば、心臓が黄金色に脈打つ。", "Flip the coin and wager, and your heart beats gold."),
                Power.SpendersWard, 10, Power.Resonance, 9) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "Gem_L_HeartOfGold" }, Kind = LinkKind.MemorySurge, Value = 44 } },
            new UniqueDef("unique.link2_whiteout_burst", "head.radiant_halo", new Txt("純白の爆光", "Whiteout Burst"),
                new Txt("十秒の静寂ののち、純白の光が戦場を満たす。", "After ten silent seconds, pure white light floods the field."),
                Power.Radiance, 88, Power.SpendersWard, 10) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion", "Gem_L_PureWhite" }, Kind = LinkKind.MemoryHaste, Value = 54 } },
            new UniqueDef("unique.link2_starlit_abyss", "armor.star_mantle", new Txt("星明かりの深淵", "Starlit Abyss"),
                new Txt("最後の星明かりが、彼女の世界へ吸い込まれていく。", "The last starlight is drawn into her world."),
                Power.StarShield, 20, Power.Sprint, 25) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld", "Gem_U_LastStarlight" }, Kind = LinkKind.MemorySurge, Value = 46 } },
            new UniqueDef("unique.link2_sunborn_fireball", "weapon.ember_whip", new Txt("太陽の火球", "Sunborn Fireball"),
                new Txt("太陽の眼が開くとき、火球は昼を連れてくる。", "When the sun's eye opens, the fireball carries the day with it."),
                Power.Ember, 52, Power.Momentum, 5) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "Gem_L_SolarEye" }, Kind = LinkKind.MemorySurge, Value = 42 } },
            new UniqueDef("unique.link2_soul_binding_shot", "feet.raven_boots", new Txt("魂縛の弾丸", "Soulbinding Bullet"),
                new Txt("霊弾は囚われの魂を撃ち、牢の扉を一度だけ開く。", "The spectral bullet strikes a captive soul and opens its cell once."),
                Power.PerfectRead, 20, Power.Retaliation, 25) { Link = new LinkDef { Requires = new[] { "St_L_SpectreBullet", "Gem_U_SoulPrison" }, Kind = LinkKind.MemoryHaste, Value = 52 } },
            new UniqueDef("unique.link2_doubt_frenzy", "hands.void_claws", new Txt("疑心の乱舞", "Dance of Doubt"),
                new Txt("傷つくたび疑いは深まり、刃は速さを増していく。", "Each wound deepens the doubt, and the blade only quickens."),
                Power.Frenzy, 3, Power.CriticalEcho, 4) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria", "Gem_L_Paranoia" }, Kind = LinkKind.MemoryHaste, Value = 60 } },
            new UniqueDef("unique.link2_forbidden_gold_fire", "hands.shadow_gloves", new Txt("禁じられた金炎", "Forbidden Golden Flame"),
                new Txt("闇を蝕む呪いに、黄金の爆発が代償を求める。", "The creeping curse of darkness is met by a golden blast that demands its price."),
                Power.Executioner, 45, Power.Shatter, 60) { Link = new LinkDef { Requires = new[] { "St_L_MentalCorruption", "Gem_L_SuppressedArcanum" }, Kind = LinkKind.MemorySurge, Value = 40 } },
            new UniqueDef("unique.link2_penniless_tycoon", "charm.iron_seal", new Txt("無一文の富豪", "Penniless Tycoon"),
                new Txt("ただで手放した贈り物が、黄金の心臓を満たしていく。", "A gift given away for nothing fills the golden heart."),
                Power.SpendersWard, 10, Power.Tailwind, 25) { Link = new LinkDef { Requires = new[] { "Gem_L_CamillasGift", "Gem_L_HeartOfGold" }, Kind = LinkKind.Attune, Value = 26 } },
            new UniqueDef("unique.link2_stray_star_needle", "feet.star_slippers", new Txt("迷い星の針", "Needle of the Stray Star"),
                new Txt("気まぐれなリンゴが、壊れた羅針盤を逆向きに回す。", "A capricious apple spins the broken compass the wrong way."),
                Power.Sprint, 25, Power.Aegis, 16) { Link = new LinkDef { Requires = new[] { "Gem_L_ChaosApple", "Gem_U_GuidingCompass_NotCharged" }, Kind = LinkKind.Attune, Value = 30 } },
            new UniqueDef("unique.link2_prayer_complete", "armor.prayer_shawl", new Txt("祈りの完成", "Prayer Made Whole"),
                new Txt("揺るがぬ信仰が、欠けのない身体へ力を注ぐ。", "Unshaken faith pours strength into a body without flaw."),
                Power.Barrier, 10, Power.PerfectRead, 20) { Link = new LinkDef { Requires = new[] { "Gem_L_DivineFaith", "Gem_L_Perfect" }, Kind = LinkKind.Guard, Value = 28 } },
            new UniqueDef("unique.link2_unending_embers", "weapon.ember_katar", new Txt("終わらぬ熾火", "Unending Embers"),
                new Txt("跳ね返る一撃が、消えない炎を連れて戻ってくる。", "The rebounding blow returns trailing a flame that will not die."),
                Power.Ember, 52, Power.Convergence, 130) { Link = new LinkDef { Requires = new[] { "Gem_L_Embertail", "Gem_U_EternalFlame" }, Kind = LinkKind.Attune, Value = 31 } },
            new UniqueDef("unique.link2_crystal_whitelight", "head.dream_circlet", new Txt("結晶の白光", "Crystal Whitelight"),
                new Txt("非現実の色に輝く結晶が、純白の光を砕いて返す。", "The surreal crystal shatters pure white light and gives it back."),
                Power.Radiance, 88, Power.CrystalResonance, 2) { Link = new LinkDef { Requires = new[] { "Gem_L_MetalCrystal", "Gem_L_PureWhite" }, Kind = LinkKind.Attune, Value = 27 } },
            new UniqueDef("unique.link2_single_point_release", "armor.guardian_plate", new Txt("解放の一点", "Point of Release"),
                new Txt("体力は一つきり、それでも解き放たれた力は止まらない。", "With a single point of life, the unleashed power still will not stop."),
                Power.Aegis, 20, Power.StarShield, 15) { Link = new LinkDef { Requires = new[] { "Gem_L_Liberty", "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = 30 } },
            new UniqueDef("unique.link2_cage_of_doubt", "feet.iron_clogs", new Txt("疑いの檻", "Cage of Doubt"),
                new Txt("疑念が牢を固く閉ざし、致命の一撃さえ空を切る。", "Doubt seals the cell tight, and even a fatal blow cuts only air."),
                Power.PerfectRead, 20, Power.Barrier, 12) { Link = new LinkDef { Requires = new[] { "Gem_L_Paranoia", "Gem_U_SoulPrison" }, Kind = LinkKind.Guard, Value = 26 } },
            new UniqueDef("unique.link2_saint_covenant", "head.knight_helm", new Txt("聖騎士の誓約", "Saint's Covenant"),
                new Txt("信仰を重ねた者の前で、傷は静かに塞がっていく。", "Before the saint of layered faith, wounds close in quiet."),
                Power.SecondWind, 30, Power.Barrier, 10) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "Gem_L_DivineFaith" }, Kind = LinkKind.Guard, Value = 29 } },
            new UniqueDef("unique.link2_ricochet_tail_fire", "hands.flame_grips", new Txt("跳弾の尾火", "Ricochet Tailfire"),
                new Txt("撃ち返された火の粉が、獲物の背に尾を引く。", "Sparks flung back trail behind the prey like a tail."),
                Power.Blaze, 70, Power.Shatter, 70) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "Gem_L_Embertail" }, Kind = LinkKind.Attune, Value = 30 } },
            new UniqueDef("unique.link2_icefang_warden", "armor.bastion_shell", new Txt("氷牙の守り手", "Warden of the Ice Fang"),
                new Txt("氷河の核を抱く者は、凍える日も倒れない。", "One who holds a glacier's core does not fall even on the coldest day."),
                Power.Barrier, 10, Power.EchoingDodge, 98) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = 30 } },
            new UniqueDef("unique.link2_stardust_observer", "head.star_diadem", new Txt("星屑の観測者", "Stardust Observer"),
                new Txt("最後の星明かりを読み解けば、星が指先で道を示す。", "Reading the last starlight, he lets the stars point the way from his fingertips."),
                Power.Overload, 20, Power.UltimateSurge, 25) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "Gem_U_LastStarlight" }, Kind = LinkKind.Attune, Value = 31 } },
            new UniqueDef("unique.link2_gatecrash_slash", "feet.hunter_boots", new Txt("獄門の一閃", "Prison Gate Slash"),
                new Txt("致命の刃を見切った瞬間、影は牢を抜けて斬り返す。", "The instant he reads a fatal blade, the shadow slips the cell and cuts back."),
                Power.Sprint, 25, Power.PerfectRead, 20) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "Gem_U_SoulPrison" }, Kind = LinkKind.Attune, Value = 29 } },
            new UniqueDef("unique.link2_flawless_thrust", "weapon.twin_rapier", new Txt("完璧な一突き", "Flawless Thrust"),
                new Txt("寸分の狂いなき突きは、決闘の幕を一度で下ろす。", "A thrust without the slightest flaw drops the curtain on a duel at once."),
                Power.Executioner, 45, Power.CriticalEcho, 4) { Link = new LinkDef { Requires = new[] { "Hero_Mist", "Gem_L_Perfect" }, Kind = LinkKind.Attune, Value = 32 } },
            new UniqueDef("unique.link2_pack_unleashed", "armor.root_mail", new Txt("群れの解放", "Pack Unleashed"),
                new Txt("鎖を解かれた群れは、主の声に合わせて牙を剥く。", "Freed from their chains, the pack bares fangs at their master's call."),
                Power.Bulwark, 30, Power.Executioner, 60) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "Gem_L_Liberty" }, Kind = LinkKind.Guard, Value = 28 } },
            new UniqueDef("unique.link2_benevolent_gold", "charm.lotus_seal", new Txt("慈愛の黄金律", "Golden Rule of Mercy"),
                new Txt("惜しみなく払った黄金が、味方の傷を温かく癒す。", "Gold spent without regret warmly mends the wounds of allies."),
                Power.SpendersWard, 10, Power.SecondWind, 30) { Link = new LinkDef { Requires = new[] { "Hero_Aurena", "Gem_L_HeartOfGold" }, Kind = LinkKind.Attune, Value = 27 } },
            new UniqueDef("unique.link2_prismatic_crystal", "hands.radiant_wraps", new Txt("虹彩の結晶体", "Iridescent Crystal Body"),
                new Txt("結晶の表面を光が巡り、四つの色が一つに重なる。", "Light circles the crystal's surface until four colors become one."),
                Power.Ember, 52, Power.Frost, 52) { Link = new LinkDef { Requires = new[] { "Hero_Bismuth", "Gem_L_MetalCrystal" }, Kind = LinkKind.Attune, Value = 32 } },
            new UniqueDef("unique.link3_supernova_starlight", "weapon.star_harp", new Txt("終星の調べ", "Swansong of Stars"),
                new Txt("最後の星明かりが、超新星の閃光を何度でも歌わせる。", "The last starlight sings the supernova's flash again and again."),
                Power.Overload, 20, Power.Ember, 52) { Link = new LinkDef { Requires = new[] { "Hero_Yubar", "St_Q_SuperNova", "Gem_U_LastStarlight" }, Kind = LinkKind.MemorySurge, Value = 68 } },
            new UniqueDef("unique.link3_crimson_cannon", "weapon.ember_katar", new Txt("紅蓮の砲声", "Crimson Report"),
                new Txt("跳ね返る炎の尾が、砲声のたびに敵陣を焼き払う。", "Rebounding tails of flame scorch the enemy line with every shot."),
                Power.Blaze, 70, Power.Wildfire, 30) { Link = new LinkDef { Requires = new[] { "Hero_Lacerta", "St_Q_HandCannon", "Gem_L_Embertail" }, Kind = LinkKind.MemoryHaste, Value = 82 } },
            new UniqueDef("unique.link3_absolute_sanctuary", "armor.guardian_plate", new Txt("絶対聖域", "Absolute Sanctuary"),
                new Txt("聖域に張られた盾は、持ち主の命を一つに束ねて守る。", "The shield woven into the sanctuary binds its bearer's life into one."),
                Power.Aegis, 20, Power.PerfectRead, 16) { Link = new LinkDef { Requires = new[] { "Hero_Vesper", "St_R_SanctuaryOfEl", "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = 46 } },
            new UniqueDef("unique.link3_dawnless_slash", "hands.duelist_gloves", new Txt("無明の一閃", "Flash in the Dark"),
                new Txt("不安に追い立てられるほど、一歩一殺の刃は冴えていく。", "The more dread closes in, the sharper each one-step kill becomes."),
                Power.CriticalEcho, 4, Power.Bloodlust, 20) { Link = new LinkDef { Requires = new[] { "Hero_Husk", "St_D_TheKillingFlow", "Gem_L_Paranoia" }, Kind = LinkKind.MemorySurge, Value = 62 } },
            new UniqueDef("unique.link3_lifeline_circle", "head.healer_band", new Txt("命脈の環", "Ring of Lifeblood"),
                new Txt("氷河の鼓動が命の輪を巡り、仲間の傷を静かに癒やす。", "A glacier's heartbeat circles the pack and quietly closes their wounds."),
                Power.SecondWind, 30, Power.Aegis, 16) { Link = new LinkDef { Requires = new[] { "Hero_Nachia", "St_D_CircleOfLife", "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = 44 } },
            new UniqueDef("unique.link3_glacier_verdict", "weapon.glacier_spear", new Txt("氷嶺の審判", "Verdict of the Ice Ridge"),
                new Txt("巨大な氷塊が落ちるたび、純白の裁きが再び下される。", "Each time the great ice chunk falls, a pure white verdict is passed again."),
                Power.Fetters, 15, Power.Shatter, 70) { Link = new LinkDef { Requires = new[] { "Hero_Cetus", "St_Q_BigBorealChunk", "Gem_L_PureWhite" }, Kind = LinkKind.MemoryHaste, Value = 78 } },
            new UniqueDef("unique.link3_frostfire_feast", "charm.ember_locket", new Txt("氷炎の饗宴", "Frostfire Banquet"),
                new Txt("吹雪と火球が交わる夜、四つの元素が同時に牙を剥く。", "On the night blizzard and fireball meet, every element bares its fangs."),
                Power.Convergence, 140, Power.Wildfire, 26) { Link = new LinkDef { Requires = new[] { "St_L_Blizzard", "St_L_PyranasFireball", "Gem_U_EternalFlame" }, Kind = LinkKind.Attune, Value = 46 } },
            new UniqueDef("unique.link3_coin_throne", "charm.guardian_seal", new Txt("金貨の王座", "Throne of Coin"),
                new Txt("貯めた財貨が山となり、持ち主を揺るがぬ玉座に据える。", "A heap of hoarded gold becomes a throne that no blow can shake."),
                Power.SpendersWard, 10, Power.Lifesteal, 10) { Link = new LinkDef { Requires = new[] { "St_L_CoinExplosion", "St_L_ButchersStrike", "Gem_L_HeartOfGold" }, Kind = LinkKind.Guard, Value = 48 } },
            new UniqueDef("unique.link3_oblivion_blackhole", "head.void_helm", new Txt("忘却の黒洞", "Void of Oblivion"),
                new Txt("彼女の世界が咆哮を呑み、命を削って闇を爆ぜさせる。", "Her world swallows the roar and burns life itself into a burst of dark."),
                Power.UltimateSurge, 25, Power.Bloodlust, 20) { Link = new LinkDef { Requires = new[] { "St_U_HerWorld", "St_U_ShoutOfOblivion", "Gem_L_SuppressedArcanum" }, Kind = LinkKind.MemorySurge, Value = 68 } },
            new UniqueDef("unique.link3_heavenly_round", "hands.radiant_wraps", new Txt("天照の輪舞", "Rondo of Radiance"),
                new Txt("解き放たれた光が輪になって踊り、仲間の傷を撫でていく。", "Liberated light dances in a ring and brushes every wound away."),
                Power.Shatter, 60, Power.Lifesteal, 10) { Link = new LinkDef { Requires = new[] { "St_L_LightExplosion", "St_U_BeamOfBalance", "Gem_L_Liberty" }, Kind = LinkKind.MemoryHaste, Value = 80 } },
            new UniqueDef("unique.link3_hundred_arrows", "weapon.longspike_bow", new Txt("百矢の天蓋", "Canopy of Arrows"),
                new Txt("霊弾が矢の雨に混ざり、完璧な弧を描いて降り注ぐ。", "Spectral bullets mix with the arrow rain and fall in perfect arcs."),
                Power.Momentum, 5, Power.CriticalEcho, 4) { Link = new LinkDef { Requires = new[] { "St_L_Multishot", "St_L_SpectreBullet", "Gem_L_Perfect" }, Kind = LinkKind.Attune, Value = 44 } },
            new UniqueDef("unique.link3_inferno_tempest", "armor.ember_cuirass", new Txt("炎獄の嵐", "Hellfire Tempest"),
                new Txt("永遠の炎が太陽の眼に見つめられ、火球が嵐となって荒れる。", "Watched by the sun's eye, the eternal flame swells into a storm of fireballs."),
                Power.Whirlwind, 80, Power.Retaliation, 25) { Link = new LinkDef { Requires = new[] { "St_L_PyranasFireball", "Gem_U_EternalFlame", "Gem_L_SolarEye" }, Kind = LinkKind.MemoryHaste, Value = 82 } },
            new UniqueDef("unique.link3_invincible_cocoon", "feet.knight_sabatons", new Txt("無敵の繭", "Cocoon of Invincibility"),
                new Txt("地中に潜れば、魂の牢獄が命の殻となって傷を拒む。", "Burrowed deep, the soul prison becomes a shell that refuses every wound."),
                Power.Aegis, 20, Power.Vigor, 20) { Link = new LinkDef { Requires = new[] { "St_U_Burrow", "Gem_U_SoulPrison", "Gem_L_Supersymmetry" }, Kind = LinkKind.Guard, Value = 48 } },
            new UniqueDef("unique.link3_butcher_banquet", "armor.bark_mail", new Txt("饗宴の屠り手", "Banquet Butcher"),
                new Txt("屠った肉は料理となり、氷河の力で傷を癒やす。", "Every butchered cut becomes a dish, and the glacier's chill mends his wounds."),
                Power.OverflowingLife, 40, Power.Thorns, 32) { Link = new LinkDef { Requires = new[] { "St_L_ButchersStrike", "Gem_L_Culinary", "Gem_U_GlacialCore" }, Kind = LinkKind.Guard, Value = 44 } },
            new UniqueDef("unique.link3_frenzied_claws", "hands.void_claws", new Txt("狂乱の爪痕", "Scars of Frenzy"),
                new Txt("ヒステリーが解き放たれ、純白の爪が敵を引き裂き続ける。", "Hysteria unleashed, pure white claws keep tearing at the foe."),
                Power.Frenzy, 3, Power.OpeningStrike, 75) { Link = new LinkDef { Requires = new[] { "St_U_Hysteria", "Gem_L_Liberty", "Gem_L_PureWhite" }, Kind = LinkKind.MemoryHaste, Value = 78 } },
            // v1.27.1：癒やし・シールド・召喚獣・献身を支える固有品。
            new UniqueDef("unique.support_moonlit_lullaby", "weapon.bone_flute", new Txt("月獣の子守唄", "Moonbeast Lullaby"),
                new Txt("傷ついた狼が眠るまで、笛の音は月の下を巡る。", "The flute circles beneath the moon until the wounded wolf sleeps."),
                Power.Resonance, 9, Power.Aegis, 22) { Link = new LinkDef { Requires = new[] { "St_Q_MoonlightPact" }, Kind = LinkKind.Attune, Value = 20 } },
            new UniqueDef("unique.support_golden_recompense", "weapon.calming_staff", new Txt("命返しの金杖", "Golden Staff of Recompense"),
                new Txt("捧げた命の行く先に、誰かの明日が芽吹く。", "Where an offered life flows, another's tomorrow takes root."),
                Power.OverflowingLife, 40, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "St_Q_GoldenBurst" }, Kind = LinkKind.MemorySurge, Value = 24 } },
            new UniqueDef("unique.support_winter_haven", "armor.frost_coat", new Txt("冬の避難所", "Winter Haven"),
                new Txt("冷たい衣の内側だけは、嵐の中でも静かだった。", "Within the cold mantle, even the storm was still."),
                Power.Barrier, 10, Power.StillWater, 8) { Link = new LinkDef { Requires = new[] { "St_Q_EmbracingTheChill" }, Kind = LinkKind.MemoryHaste, Value = 32 } },
            new UniqueDef("unique.support_serpent_cradle", "armor.prayer_shawl", new Txt("蛇環の揺り籠", "Serpent-Ring Cradle"),
                new Txt("古い鱗を脱ぐように、仲間の傷もほどけていく。", "As old scales fall away, the wounds of companions unravel."),
                Power.OverflowingLife, 40, Power.Resonance, 9) { Link = new LinkDef { Requires = new[] { "St_R_SerpentineBlessing" }, Kind = LinkKind.MemoryHaste, Value = 30 } },
            new UniqueDef("unique.support_mending_plume", "head.healer_band", new Txt("傷縫いの金羽根", "Golden Mending Plume"),
                new Txt("爪が奪った命を、羽根は一針ずつ縫い戻す。", "What the claw takes, the feather stitches back one thread at a time."),
                Power.OverflowingLife, 38, Power.Lifesteal, 9) { Link = new LinkDef { Requires = new[] { "St_D_DisintegratingClaw" }, Kind = LinkKind.Attune, Value = 18 } },
            new UniqueDef("unique.support_pack_vigil", "head.wolf_pelt", new Txt("群れ守りの夜番", "Packkeeper's Vigil"),
                new Txt("一匹も置いていかぬよう、夜通し耳を澄ませている。", "All night the keeper listens, so not one of the pack is left behind."),
                Power.Resonance, 9, Power.Frenzy, 3) { Link = new LinkDef { Requires = new[] { "St_D_CircleOfLife" }, Kind = LinkKind.Attune, Value = 18 } },
            new UniqueDef("unique.support_measured_offering", "hands.healer_hands", new Txt("惜しみの献杯", "Measured Offering"),
                new Txt("命を注ぐ手を止めるのは、次の誰かも救うため。", "The hand pauses its offering to save the next soul as well."),
                Power.OverflowingLife, 38, Power.SecondWind, 30) { Link = new LinkDef { Requires = new[] { "St_Q_Reduction" }, Kind = LinkKind.MemorySurge, Value = 24 } },
            new UniqueDef("unique.support_sheltering_tide", "hands.frost_mitts", new Txt("庇い手の氷袖", "Sheltering Ice Sleeves"),
                new Txt("振り払う腕の後ろには、仲間のための凪が残る。", "Behind the sweeping arm, calm water remains for companions."),
                Power.Resonance, 9, Power.StillWater, 8) { Link = new LinkDef { Requires = new[] { "St_R_BackOff" }, Kind = LinkKind.MemoryHaste, Value = 32 } },
            new UniqueDef("unique.support_waltz_shelter", "feet.root_sandals", new Txt("木漏れ日の舞靴", "Dancing Shoes of Dappled Light"),
                new Txt("踏み出すたびに木陰が揺れ、小さな背中を包み込む。", "Each step stirs the shade and shelters the little backs beneath it."),
                Power.Resonance, 9, Power.Barrier, 9) { Link = new LinkDef { Requires = new[] { "St_M_DreamyWaltz" }, Kind = LinkKind.MemoryHaste, Value = 30 } },
            new UniqueDef("unique.support_brink_return", "feet.pilgrim_boots", new Txt("生還の足跡", "Footprints of Return"),
                new Txt("危うい一歩の先にも、戻る道だけは残しておく。", "Beyond the perilous step, always leave a path home."),
                Power.OverflowingLife, 38, Power.Bloodlust, 20) { Link = new LinkDef { Requires = new[] { "St_R_DangerousTheory" }, Kind = LinkKind.MemorySurge, Value = 24 } },
            new UniqueDef("unique.support_sylvan_kinship", "charm.oak_amulet", new Txt("若葉の呼び鈴", "Bell of Young Leaves"),
                new Txt("ひとたび鳴らせば、森の子らが家族の声を思い出す。", "One ring reminds the forest's young of the voice of home."),
                Power.Resonance, 9, Power.Overload, 20) { Link = new LinkDef { Requires = new[] { "St_Q_SylvanCall" }, Kind = LinkKind.MemoryHaste, Value = 32 } },
            new UniqueDef("unique.support_life_confluence", "charm.sun_brooch", new Txt("命の合流点", "Confluence of Life"),
                new Txt("幾筋もの光が交わり、途切れかけた鼓動をつなぐ。", "Strands of light meet and join heartbeats that were fading apart."),
                Power.OverflowingLife, 40, Power.StarShield, 20) { Link = new LinkDef { Requires = new[] { "St_R_ChainReaction" }, Kind = LinkKind.MemoryHaste, Value = 30 } },
            new UniqueDef("set.tide.weapon", "weapon.chain_sword", new Txt("潮鳴りの剣", "Tidecaller's Blade"), "set.tide"),
            new UniqueDef("set.tide.armor", "armor.flowing_cloak", new Txt("潮鳴りの外套", "Tidecaller's Cloak"), "set.tide"),
            new UniqueDef("set.tide.charm", "charm.tailwind_ring", new Txt("潮鳴りの指輪", "Tidecaller's Ring"), "set.tide"),
            new UniqueDef("set.lamp.weapon", "weapon.calming_staff", new Txt("灯守の杖", "Lampkeeper's Staff"), "set.lamp"),
            new UniqueDef("set.lamp.armor", "armor.lampkeeper_mantle", new Txt("灯守の誓衣", "Lampkeeper's Vow"), "set.lamp"),
            new UniqueDef("set.lamp.charm", "charm.resonance_amulet", new Txt("灯守の護符", "Lampkeeper's Charm"), "set.lamp"),
            new UniqueDef("set.cinder.weapon", "weapon.blaze_greatsword", new Txt("残火の大剣", "Cinderbrand"), "set.cinder"),
            new UniqueDef("set.cinder.armor", "armor.thorn_mail", new Txt("残火の鱗鎧", "Cinderscale Mail"), "set.cinder"),
            new UniqueDef("set.cinder.charm", "charm.old_clock", new Txt("残火の懐中時計", "Cinder Pocketwatch"), "set.cinder"),
            new UniqueDef("set.dusk.weapon", "weapon.twin_fang", new Txt("黄昏の双牙", "Duskfang"), "set.dusk"),
            new UniqueDef("set.dusk.armor", "armor.counter_gauntlets", new Txt("黄昏の籠手", "Dusk Gauntlets"), "set.dusk"),
            new UniqueDef("set.dusk.charm", "charm.hunters_seal", new Txt("黄昏の印章", "Dusk Seal"), "set.dusk"),
            new UniqueDef("set.winter.weapon", "weapon.frost_spear", new Txt("冬枯れの槍", "Winterbound Spear"), "set.winter"),
            new UniqueDef("set.winter.armor", "armor.frost_coat", new Txt("冬枯れの上衣", "Winterbound Coat"), "set.winter"),
            new UniqueDef("set.winter.charm", "charm.moon_bell", new Txt("冬枯れの鈴", "Winterbound Bell"), "set.winter"),
            new UniqueDef("set.starsong.weapon", "weapon.lantern_rod", new Txt("星詠みの杖", "Starsinger's Rod"), "set.starsong"),
            new UniqueDef("set.starsong.armor", "armor.star_cloak", new Txt("星詠みの外套", "Starsinger's Cloak"), "set.starsong"),
            new UniqueDef("set.starsong.charm", "charm.old_clock", new Txt("星詠みの時計", "Starsinger's Clock"), "set.starsong"),
            new UniqueDef("set.hunt.weapon", "weapon.hunting_bow", new Txt("狩猟団の弓", "Huntmaster's Bow"), "set.hunt"),
            new UniqueDef("set.hunt.armor", "armor.hunter_leather", new Txt("狩猟団の革鎧", "Huntmaster's Leathers"), "set.hunt"),
            new UniqueDef("set.hunt.charm", "charm.fang_necklace", new Txt("狩猟団の牙", "Huntmaster's Fang"), "set.hunt"),
            new UniqueDef("set.bastion.weapon", "weapon.tower_lance", new Txt("不落城の槍", "Bastion Lance"), "set.bastion"),
            new UniqueDef("set.bastion.armor", "armor.bastion_shell", new Txt("不落城の甲羅", "Bastion Shell"), "set.bastion"),
            new UniqueDef("set.bastion.charm", "charm.guardian_seal", new Txt("不落城の封印", "Bastion Seal"), "set.bastion"),
            new UniqueDef("set.wildfire.weapon", "weapon.war_axe", new Txt("燎原の斧", "Wildfire Cleaver"), "set.wildfire"),
            new UniqueDef("set.wildfire.armor", "armor.ember_plate", new Txt("燎原の鎧", "Wildfire Plate"), "set.wildfire"),
            new UniqueDef("set.wildfire.charm", "charm.war_drum", new Txt("燎原の太鼓", "Wildfire Drum"), "set.wildfire"),
            new UniqueDef("set.grove.weapon", "weapon.oath_mace", new Txt("古森の戦棍", "Grove Mace"), "set.grove"),
            new UniqueDef("set.grove.armor", "armor.root_mail", new Txt("古森の帷子", "Grove Mail"), "set.grove"),
            new UniqueDef("set.grove.charm", "charm.stone_heart", new Txt("古森の心臓", "Grove Heart"), "set.grove"),
            new UniqueDef("set.reverie.weapon", "weapon.star_harp", new Txt("夢想の竪琴", "Reverie Harp"), "set.reverie"),
            new UniqueDef("set.reverie.armor", "armor.prayer_shawl", new Txt("夢想の肩掛け", "Reverie Shawl"), "set.reverie"),
            new UniqueDef("set.reverie.charm", "charm.dream_lens", new Txt("夢想の水晶", "Reverie Lens"), "set.reverie"),
            new UniqueDef("set.phantom.weapon", "weapon.dream_wand", new Txt("幻影の杖", "Phantom Wand"), "set.phantom"),
            new UniqueDef("set.phantom.armor", "armor.mist_robe", new Txt("幻影の法衣", "Phantom Robe"), "set.phantom"),
            new UniqueDef("set.phantom.charm", "charm.shadow_mask", new Txt("幻影の仮面", "Phantom Mask"), "set.phantom"),
            // v1.22：新しい枠を使うセットの部位
            new UniqueDef("set.permafrost.head", "head.frost_helm", new Txt("凍土の兜", "Permafrost Helm"), "set.permafrost"),
            new UniqueDef("set.permafrost.hands", "hands.frost_mitts", new Txt("凍土の手甲", "Permafrost Gauntlets"), "set.permafrost"),
            new UniqueDef("set.permafrost.feet", "feet.frost_boots", new Txt("凍土の脚絆", "Permafrost Greaves"), "set.permafrost"),
            new UniqueDef("set.asura.head", "head.berserker_mask", new Txt("修羅道の面", "Carnage Mask"), "set.asura"),
            new UniqueDef("set.asura.hands", "hands.claw_gauntlets", new Txt("修羅道の爪", "Carnage Claws"), "set.asura"),
            new UniqueDef("set.asura.feet", "feet.spiked_boots", new Txt("修羅道の脛", "Carnage Spurs"), "set.asura"),
            new UniqueDef("set.gale.head", "head.mist_veil", new Txt("風舞の面紗", "Galedancer Veil"), "set.gale"),
            new UniqueDef("set.gale.hands", "hands.quick_fingers", new Txt("風舞の指貫", "Galedancer Gloves"), "set.gale"),
            new UniqueDef("set.gale.feet", "feet.dancer_shoes", new Txt("風舞の靴", "Galedancer Shoes"), "set.gale"),
            new UniqueDef("set.firmament.head", "head.star_diadem", new Txt("天穹の冠", "Firmament Diadem"), "set.firmament"),
            new UniqueDef("set.firmament.hands", "hands.star_rings", new Txt("天穹の指環", "Firmament Rings"), "set.firmament"),
            new UniqueDef("set.firmament.feet", "feet.star_steps", new Txt("天穹の沓", "Firmament Steps"), "set.firmament"),
            new UniqueDef("set.ambush.head", "head.eye_patch", new Txt("闇討ちの眼帯", "Nightstrike Eyepatch"), "set.ambush"),
            new UniqueDef("set.ambush.hands", "hands.duelist_gloves", new Txt("闇討ちの手袋", "Nightstrike Gloves"), "set.ambush"),
            new UniqueDef("set.ambush.feet", "feet.stalker_boots", new Txt("闇討ちの足袋", "Nightstrike Tabi"), "set.ambush"),
            new UniqueDef("set.ashrunner.head", "head.ash_hood", new Txt("灰走りの頭巾", "Ashrunner Hood"), "set.ashrunner"),
            new UniqueDef("set.ashrunner.hands", "hands.flame_grips", new Txt("灰走りの手甲", "Ashrunner Grips"), "set.ashrunner"),
            new UniqueDef("set.ashrunner.feet", "feet.ash_boots", new Txt("灰走りの靴", "Ashrunner Boots"), "set.ashrunner"),
            new UniqueDef("set.mercy.weapon", "weapon.pilgrim_staff", new Txt("施療の錫杖", "Healer's Staff"), "set.mercy"),
            new UniqueDef("set.mercy.hands", "hands.healer_hands", new Txt("施療の手袋", "Healer's Gloves"), "set.mercy"),
            new UniqueDef("set.mercy.charm", "charm.sun_brooch", new Txt("施療の飾り", "Healer's Brooch"), "set.mercy"),
            new UniqueDef("set.ironknight.armor", "armor.scale_coat", new Txt("鉄騎の竜鎧", "Ironknight Scale Mail"), "set.ironknight"),
            new UniqueDef("set.ironknight.head", "head.knight_helm", new Txt("鉄騎の大兜", "Ironknight Greathelm"), "set.ironknight"),
            new UniqueDef("set.ironknight.feet", "feet.knight_sabatons", new Txt("鉄騎の鉄脚", "Ironknight Sabatons"), "set.ironknight"),
            new UniqueDef("set.eclipse.weapon", "weapon.moon_sickle", new Txt("月蝕の鎌", "Eclipse Sickle"), "set.eclipse"),
            new UniqueDef("set.eclipse.head", "head.void_helm", new Txt("月蝕の兜", "Eclipse Helm"), "set.eclipse"),
            new UniqueDef("set.eclipse.charm", "charm.shadow_ring", new Txt("月蝕の指輪", "Eclipse Ring"), "set.eclipse"),
            new UniqueDef("set.thunderclap.weapon", "weapon.thunder_hammer", new Txt("迅雷の戦鎚", "Thunderclap Hammer"), "set.thunderclap"),
            new UniqueDef("set.thunderclap.armor", "armor.monk_garb", new Txt("迅雷の道着", "Thunderclap Gi"), "set.thunderclap"),
            new UniqueDef("set.thunderclap.feet", "feet.wolf_boots", new Txt("迅雷の長靴", "Thunderclap Boots"), "set.thunderclap"),
            new UniqueDef("set.myriad.armor", "armor.resonant_robe", new Txt("万象の法衣", "Myriad Robe"), "set.myriad"),
            new UniqueDef("set.myriad.hands", "hands.alchemist_gloves", new Txt("万象の手袋", "Myriad Gloves"), "set.myriad"),
            new UniqueDef("set.myriad.charm", "charm.clockwork_charm", new Txt("万象の飾り", "Myriad Trinket"), "set.myriad"),
            new UniqueDef("set.daybreak.weapon", "weapon.dawn_scepter", new Txt("払暁の笏", "Daybreak Scepter"), "set.daybreak"),
            new UniqueDef("set.daybreak.head", "head.radiant_halo", new Txt("払暁の光輪", "Daybreak Halo"), "set.daybreak"),
            new UniqueDef("set.daybreak.feet", "feet.dawn_steps", new Txt("払暁の長靴", "Daybreak Treads"), "set.daybreak"),
        };

        public static readonly IReadOnlyList<SetDef> Sets = new[]
        {
            new SetDef
            {
                Id = "set.tide", Name = new Txt("潮鳴りの装い", "Tidecaller's Regalia"),
                TwoPiece = new[] { new StatLine(Stat.ColdAmp, 10), new StatLine(Stat.MoveSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Frost, 45), new PowerLine(Power.EchoingDodge, 65) },
            },
            new SetDef
            {
                Id = "set.lamp", Name = new Txt("灯守の誓い", "Lampkeeper's Oath"),
                TwoPiece = new[] { new StatLine(Stat.LightAmp, 10), new StatLine(Stat.MaxHealthPct, 8) },
                ThreePiece = new[] { new PowerLine(Power.Radiance, 75), new PowerLine(Power.SecondWind, 25) },
            },
            new SetDef
            {
                Id = "set.cinder", Name = new Txt("残火の誓約", "Cinder Covenant"),
                TwoPiece = new[] { new StatLine(Stat.FireAmp, 10), new StatLine(Stat.AttackPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Ember, 45), new PowerLine(Power.Blaze, 50) },
            },
            new SetDef
            {
                Id = "set.dusk", Name = new Txt("黄昏の狩装", "Dusk Hunter's Garb"),
                TwoPiece = new[] { new StatLine(Stat.DarkAmp, 10), new StatLine(Stat.CritChancePct, 4) },
                ThreePiece = new[] { new PowerLine(Power.Umbra, 75), new PowerLine(Power.Executioner, 40) },
            },
            new SetDef
            {
                Id = "set.winter", Name = new Txt("冬枯れの誓約", "Winterbound Oath"),
                TwoPiece = new[] { new StatLine(Stat.ColdAmp, 10), new StatLine(Stat.Armor, 6) },
                ThreePiece = new[] { new PowerLine(Power.Frost, 45), new PowerLine(Power.Bulwark, 30) },
            },
            new SetDef
            {
                Id = "set.starsong", Name = new Txt("星詠みの装束", "Starsinger's Raiment"),
                TwoPiece = new[] { new StatLine(Stat.Haste, 10), new StatLine(Stat.PowerPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.UltimateSurge, 25), new PowerLine(Power.EchoingDodge, 65) },
            },
            new SetDef
            {
                Id = "set.hunt", Name = new Txt("狩猟団の装備", "Huntmaster's Kit"),
                TwoPiece = new[] { new StatLine(Stat.CritChancePct, 4), new StatLine(Stat.AttackSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Executioner, 40), new PowerLine(Power.Momentum, 5) },
            },
            new SetDef
            {
                Id = "set.bastion", Name = new Txt("不落城の装い", "Bastion's Bulwark"),
                TwoPiece = new[] { new StatLine(Stat.Armor, 8), new StatLine(Stat.MaxHealthPct, 6) },
                ThreePiece = new[] { new PowerLine(Power.Bulwark, 35), new PowerLine(Power.Aegis, 25) },
            },
            new SetDef
            {
                Id = "set.wildfire", Name = new Txt("燎原の軍装", "Wildfire Warband"),
                TwoPiece = new[] { new StatLine(Stat.FireAmp, 10), new StatLine(Stat.AttackSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Ember, 52), new PowerLine(Power.Shatter, 60) },
            },
            new SetDef
            {
                Id = "set.grove", Name = new Txt("古森の守り", "Old Grove Ward"),
                TwoPiece = new[] { new StatLine(Stat.HealthRegen, 3), new StatLine(Stat.Tenacity, 12) },
                ThreePiece = new[] { new PowerLine(Power.Barrier, 10), new PowerLine(Power.SecondWind, 30) },
            },
            new SetDef
            {
                Id = "set.reverie", Name = new Txt("夢想の楽団", "Reverie Ensemble"),
                TwoPiece = new[] { new StatLine(Stat.PowerPct, 6), new StatLine(Stat.Haste, 8) },
                ThreePiece = new[] { new PowerLine(Power.Resonance, 10), new PowerLine(Power.Radiance, 88) },
            },
            new SetDef
            {
                Id = "set.phantom", Name = new Txt("幻影の一座", "Phantom Troupe"),
                TwoPiece = new[] { new StatLine(Stat.DarkAmp, 10), new StatLine(Stat.MoveSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Umbra, 88), new PowerLine(Power.EchoingDodge, 78) },
            },
            // v1.22：新しい枠を使うセット
            new SetDef
            {
                Id = "set.permafrost", Name = new Txt("凍土の装い", "Permafrost Garb"),
                TwoPiece = new[] { new StatLine(Stat.ColdAmp, 10), new StatLine(Stat.Armor, 10) },
                ThreePiece = new[] { new PowerLine(Power.Thorns, 30), new PowerLine(Power.Frost, 45) },
            },
            new SetDef
            {
                Id = "set.asura", Name = new Txt("修羅道の装い", "Path of Carnage"),
                TwoPiece = new[] { new StatLine(Stat.AttackPct, 8), new StatLine(Stat.MaxHealthFlat, 40) },
                ThreePiece = new[] { new PowerLine(Power.Bloodlust, 20), new PowerLine(Power.Lifesteal, 10) },
            },
            new SetDef
            {
                Id = "set.gale", Name = new Txt("風舞の装い", "Galedancer's Attire"),
                TwoPiece = new[] { new StatLine(Stat.MoveSpeedPct, 5), new StatLine(Stat.Haste, 10) },
                ThreePiece = new[] { new PowerLine(Power.Whirlwind, 60), new PowerLine(Power.Sprint, 15) },
            },
            new SetDef
            {
                Id = "set.firmament", Name = new Txt("天穹の誓い", "Firmament Vow"),
                TwoPiece = new[] { new StatLine(Stat.PowerPct, 6), new StatLine(Stat.Haste, 8) },
                ThreePiece = new[] { new PowerLine(Power.UltimateSurge, 20), new PowerLine(Power.StarShield, 12) },
            },
            new SetDef
            {
                Id = "set.ambush", Name = new Txt("闇討ちの装い", "Nightstrike Gear"),
                TwoPiece = new[] { new StatLine(Stat.CritChancePct, 4), new StatLine(Stat.AttackSpeedPct, 6) },
                ThreePiece = new[] { new PowerLine(Power.Umbra, 75), new PowerLine(Power.OpeningStrike, 60) },
            },
            new SetDef
            {
                Id = "set.ashrunner", Name = new Txt("灰走りの装い", "Ashrunner's Garb"),
                TwoPiece = new[] { new StatLine(Stat.FireAmp, 10), new StatLine(Stat.MoveSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Ember, 45), new PowerLine(Power.Tailwind, 15) },
            },
            new SetDef
            {
                Id = "set.mercy", Name = new Txt("施療の誓い", "Healer's Pledge"),
                TwoPiece = new[] { new StatLine(Stat.HealthRegen, 3), new StatLine(Stat.LightAmp, 10) },
                ThreePiece = new[] { new PowerLine(Power.Barrier, 10), new PowerLine(Power.Resonance, 10) },
            },
            new SetDef
            {
                Id = "set.ironknight", Name = new Txt("鉄騎の誓い", "Ironknight's Oath"),
                TwoPiece = new[] { new StatLine(Stat.Armor, 10), new StatLine(Stat.MaxHealthPct, 8) },
                ThreePiece = new[] { new PowerLine(Power.Thorns, 35), new PowerLine(Power.Aegis, 20) },
            },
            new SetDef
            {
                Id = "set.eclipse", Name = new Txt("月蝕の装い", "Eclipse Regalia"),
                TwoPiece = new[] { new StatLine(Stat.DarkAmp, 10), new StatLine(Stat.CritDamagePct, 15) },
                ThreePiece = new[] { new PowerLine(Power.Umbra, 75), new PowerLine(Power.SoulSiphon, 20) },
            },
            new SetDef
            {
                Id = "set.thunderclap", Name = new Txt("迅雷の武装", "Thunderclap Arms"),
                TwoPiece = new[] { new StatLine(Stat.AttackPct, 8), new StatLine(Stat.AttackSpeedPct, 6) },
                ThreePiece = new[] { new PowerLine(Power.ChainLightning, 40), new PowerLine(Power.Frenzy, 3) },
            },
            new SetDef
            {
                Id = "set.myriad", Name = new Txt("万象の装い", "Myriad Weaver's Vestments"),
                TwoPiece = new[] { new StatLine(Stat.PowerPct, 7), new StatLine(Stat.Haste, 10) },
                ThreePiece = new[] { new PowerLine(Power.Convergence, 80), new PowerLine(Power.Overload, 15) },
            },
            new SetDef
            {
                Id = "set.daybreak", Name = new Txt("払暁の誓い", "Daybreak Vow"),
                TwoPiece = new[] { new StatLine(Stat.LightAmp, 10), new StatLine(Stat.AttackPct, 6) },
                ThreePiece = new[] { new PowerLine(Power.Radiance, 75), new PowerLine(Power.Vigor, 15) },
            },
        };

        public static SetDef GetSet(string id)
        {
            foreach (var s in Sets)
                if (s.Id == id) return s;
            return null;
        }

        private static readonly Dictionary<Slot, AffixDef[]> AffixPools = new Dictionary<Slot, AffixDef[]>
        {
            [Slot.Weapon] = new[]
            {
                new AffixDef(Stat.AttackFlat, 2, 5, 14),
                new AffixDef(Stat.AttackPct, 2, 4, 4, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.PowerFlat, 2, 5, 14),
                new AffixDef(Stat.PowerPct, 2, 4, 4, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.AttackSpeedPct, 3, 6, 12),
                new AffixDef(Stat.CritChancePct, 2, 4, 10),
                new AffixDef(Stat.CritDamagePct, 6, 12, 10),
                new AffixDef(Stat.Haste, 3, 6, 6),
                new AffixDef(Stat.FireAmp, 6, 12, 5),
                new AffixDef(Stat.LightAmp, 6, 12, 5),
                new AffixDef(Stat.DarkAmp, 6, 12, 5),
                new AffixDef(Stat.AttackRangePct, 4, 8, 5),
                new AffixDef(Stat.ColdAmp, 6, 12, 5),
                new AffixDef(Stat.MaxHealthPct, 4, 8, 7), // v1.21
                new AffixDef(Stat.Tenacity, 6, 12, 4), // v1.21
            },
            [Slot.Armor] = new[]
            {
                new AffixDef(Stat.MaxHealthPct, 4, 8, 14),
                new AffixDef(Stat.MaxHealthFlat, 15, 30, 12),
                new AffixDef(Stat.Armor, 4, 9, 14),
                new AffixDef(Stat.HealthRegen, 1, 3, 8),
                new AffixDef(Stat.Tenacity, 6, 12, 8),
                new AffixDef(Stat.MoveSpeedPct, 2, 4, 6),
                new AffixDef(Stat.Haste, 3, 6, 6),
                new AffixDef(Stat.LightAmp, 5, 10, 4),
                new AffixDef(Stat.PowerFlat, 2, 5, 7), // v1.21
                new AffixDef(Stat.PowerPct, 2, 4, 2, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.AttackFlat, 2, 5, 7), // v1.21
                new AffixDef(Stat.AttackPct, 2, 4, 2, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.ColdAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.DarkAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.FireAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.HealPower, 3, 6, 3),
                new AffixDef(Stat.ShieldPower, 3, 6, 3),
            },
            [Slot.Charm] = new[]
            {
                new AffixDef(Stat.Haste, 4, 8, 12),
                new AffixDef(Stat.MoveSpeedPct, 2, 5, 8),
                new AffixDef(Stat.CritChancePct, 2, 4, 10),
                new AffixDef(Stat.AttackFlat, 2, 5, 10),
                new AffixDef(Stat.AttackPct, 2, 4, 3, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.PowerFlat, 2, 5, 10),
                new AffixDef(Stat.PowerPct, 2, 4, 3, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.MaxHealthPct, 3, 6, 10),
                new AffixDef(Stat.HealthRegen, 1, 2, 6),
                new AffixDef(Stat.ColdAmp, 6, 12, 5),
                new AffixDef(Stat.FireAmp, 6, 12, 4),
                new AffixDef(Stat.LightAmp, 6, 12, 4),
                new AffixDef(Stat.DarkAmp, 6, 12, 4),
                new AffixDef(Stat.Tenacity, 6, 12, 6),
                new AffixDef(Stat.CritDamagePct, 5, 10, 8),
                new AffixDef(Stat.HealPower, 3, 6, 3),
                new AffixDef(Stat.ShieldPower, 3, 6, 3),
            },
            [Slot.Head] = new[]
            {
                new AffixDef(Stat.PowerFlat, 2, 5, 14),
                new AffixDef(Stat.PowerPct, 2, 4, 4, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.Haste, 3, 6, 6),
                new AffixDef(Stat.CritChancePct, 2, 4, 10),
                new AffixDef(Stat.MaxHealthPct, 4, 8, 14),
                new AffixDef(Stat.Armor, 4, 9, 14),
                new AffixDef(Stat.Tenacity, 6, 12, 8),
                new AffixDef(Stat.LightAmp, 6, 12, 5),
                new AffixDef(Stat.DarkAmp, 6, 12, 5),
                new AffixDef(Stat.HealthRegen, 1, 3, 8),
                new AffixDef(Stat.AttackFlat, 2, 5, 14),
                new AffixDef(Stat.AttackPct, 2, 4, 4, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.MoveSpeedPct, 2, 4, 4), // v1.21
                new AffixDef(Stat.ColdAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.FireAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.HealPower, 3, 6, 3),
                new AffixDef(Stat.ShieldPower, 3, 6, 3),
            },
            [Slot.Hands] = new[]
            {
                new AffixDef(Stat.AttackFlat, 2, 5, 14),
                new AffixDef(Stat.AttackPct, 2, 4, 4, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.AttackSpeedPct, 3, 6, 12),
                new AffixDef(Stat.CritChancePct, 2, 4, 10),
                new AffixDef(Stat.CritDamagePct, 6, 12, 10),
                new AffixDef(Stat.PowerFlat, 2, 5, 14),
                new AffixDef(Stat.PowerPct, 2, 4, 4, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.FireAmp, 6, 12, 5),
                new AffixDef(Stat.ColdAmp, 6, 12, 5),
                new AffixDef(Stat.AttackRangePct, 4, 8, 5),
                new AffixDef(Stat.Armor, 4, 9, 14),
                new AffixDef(Stat.MaxHealthFlat, 15, 30, 12),
                new AffixDef(Stat.LightAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.DarkAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.Haste, 3, 6, 4), // v1.21
            },
            [Slot.Feet] = new[]
            {
                new AffixDef(Stat.MoveSpeedPct, 2, 4, 6),
                new AffixDef(Stat.Tenacity, 6, 12, 8),
                new AffixDef(Stat.Armor, 4, 9, 14),
                new AffixDef(Stat.MaxHealthPct, 4, 8, 14),
                new AffixDef(Stat.MaxHealthFlat, 15, 30, 12),
                new AffixDef(Stat.HealthRegen, 1, 3, 8),
                new AffixDef(Stat.AttackSpeedPct, 3, 6, 12),
                new AffixDef(Stat.Haste, 3, 6, 6),
                new AffixDef(Stat.ColdAmp, 6, 12, 5),
                new AffixDef(Stat.FireAmp, 6, 12, 5),
                new AffixDef(Stat.CritChancePct, 2, 4, 5), // v1.21
                new AffixDef(Stat.LightAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.DarkAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.AttackFlat, 2, 5, 8), // v1.27：足にも攻撃力・魔力
                new AffixDef(Stat.AttackPct, 2, 4, 2, Rarity.Epic), // v1.28：%はエピック以上・控えめに
                new AffixDef(Stat.PowerFlat, 2, 5, 8),
                new AffixDef(Stat.PowerPct, 2, 4, 2, Rarity.Epic), // v1.28：%はエピック以上・控えめに
            },
        };

        private static readonly Dictionary<Slot, PowerRange[]> PowerPools = new Dictionary<Slot, PowerRange[]>
        {
            [Slot.Weapon] = new[]
            {
                new PowerRange(Power.Momentum, 3, 5),
                new PowerRange(Power.Lifesteal, 5, 10),
                new PowerRange(Power.Executioner, 25, 45),
                new PowerRange(Power.Blaze, 40, 70),
                new PowerRange(Power.ChainLightning, 30, 50),
                new PowerRange(Power.Bloodlust, 12, 20),
                new PowerRange(Power.Ember, 25, 50),
                new PowerRange(Power.Radiance, 40, 80),
                new PowerRange(Power.UltimateSurge, 15, 25),
                new PowerRange(Power.SoulSiphon, 10, 20),
                new PowerRange(Power.Frenzy, 1, 2),
                new PowerRange(Power.OpeningStrike, 25, 50),
                new PowerRange(Power.Vigor, 8, 16),
                new PowerRange(Power.Overload, 10, 20),
                new PowerRange(Power.Fetters, 8, 15),
                new PowerRange(Power.CriticalEcho, 2, 4),
                new PowerRange(Power.Wildfire, 15, 30),
            },
            [Slot.Armor] = new[]
            {
                new PowerRange(Power.Retaliation, 15, 25),
                new PowerRange(Power.Bulwark, 15, 30),
                new PowerRange(Power.Thorns, 15, 30),
                new PowerRange(Power.Barrier, 6, 10),
                new PowerRange(Power.Aegis, 10, 20),
                new PowerRange(Power.EchoingDodge, 40, 80),
                new PowerRange(Power.Whirlwind, 40, 80),
                new PowerRange(Power.Frenzy, 1, 2),
                new PowerRange(Power.StarShield, 10, 20),
                new PowerRange(Power.Sprint, 10, 25),
                new PowerRange(Power.OverflowingLife, 20, 40),
                new PowerRange(Power.Fetters, 8, 15),
                new PowerRange(Power.StillWater, 4, 8),
            },
            [Slot.Charm] = new[]
            {
                new PowerRange(Power.Resonance, 5, 9),
                new PowerRange(Power.Tailwind, 15, 25),
                new PowerRange(Power.SecondWind, 20, 30),
                new PowerRange(Power.Shatter, 30, 60),
                new PowerRange(Power.Frost, 30, 55),
                new PowerRange(Power.Umbra, 40, 80),
                new PowerRange(Power.Convergence, 80, 140),
                new PowerRange(Power.Steam, 35, 60),
                new PowerRange(Power.Eclipse, 8, 12),
                new PowerRange(Power.Cinder, 1, 1),
                new PowerRange(Power.FrostCrystal, 3, 5),
                new PowerRange(Power.SoulSiphon, 10, 20),
                new PowerRange(Power.Whirlwind, 40, 80),
                new PowerRange(Power.StarShield, 10, 20),
                new PowerRange(Power.Sprint, 10, 25),
                new PowerRange(Power.Vigor, 8, 16),
                new PowerRange(Power.Overload, 10, 20),
                new PowerRange(Power.Devotion, 2, 4),
                new PowerRange(Power.CrystalResonance, 1, 2),
                new PowerRange(Power.PreyPride, 3, 5),
                new PowerRange(Power.SpendersWard, 5, 10),
                new PowerRange(Power.LucidBoon, 1, 3),
            },
            [Slot.Head] = new[]
            {
                new PowerRange(Power.Eclipse, 8, 12),
                new PowerRange(Power.FrostCrystal, 3, 5),
                new PowerRange(Power.Overload, 10, 20),
                new PowerRange(Power.UltimateSurge, 15, 25),
                new PowerRange(Power.StarShield, 10, 20),
                new PowerRange(Power.Resonance, 5, 9),
                new PowerRange(Power.SecondWind, 20, 30),
                new PowerRange(Power.Radiance, 40, 80),
                new PowerRange(Power.Umbra, 40, 80),
                new PowerRange(Power.Barrier, 6, 10),
                new PowerRange(Power.Vigor, 8, 16),
                new PowerRange(Power.Bloodlust, 12, 20),
                new PowerRange(Power.Finale, 10, 20),
                new PowerRange(Power.CrystalResonance, 1, 2),
                new PowerRange(Power.Devotion, 2, 4),
                new PowerRange(Power.LucidBoon, 1, 3),
            },
            [Slot.Hands] = new[]
            {
                new PowerRange(Power.Steam, 35, 60),
                new PowerRange(Power.Cinder, 1, 1),
                new PowerRange(Power.Executioner, 25, 45),
                new PowerRange(Power.Blaze, 40, 70),
                new PowerRange(Power.ChainLightning, 30, 50),
                new PowerRange(Power.Ember, 25, 50),
                new PowerRange(Power.Frost, 30, 55),
                new PowerRange(Power.Frenzy, 1, 2),
                new PowerRange(Power.Lifesteal, 5, 10),
                new PowerRange(Power.OpeningStrike, 25, 50),
                new PowerRange(Power.Shatter, 30, 60),
                new PowerRange(Power.Momentum, 3, 5),
                new PowerRange(Power.CriticalEcho, 2, 4),
                new PowerRange(Power.Fetters, 8, 15),
                new PowerRange(Power.Wildfire, 15, 30),
                new PowerRange(Power.StillWater, 4, 8),
                new PowerRange(Power.Overload, 10, 20), // v1.27：手にもスキルで発動する効果
                new PowerRange(Power.Finale, 10, 20),
            },
            [Slot.Feet] = new[]
            {
                new PowerRange(Power.Sprint, 10, 25),
                new PowerRange(Power.Tailwind, 15, 25),
                new PowerRange(Power.Whirlwind, 40, 80),
                new PowerRange(Power.EchoingDodge, 40, 80),
                new PowerRange(Power.Momentum, 3, 5),
                new PowerRange(Power.Aegis, 10, 20),
                new PowerRange(Power.Bulwark, 15, 30),
                new PowerRange(Power.Thorns, 15, 30),
                new PowerRange(Power.SoulSiphon, 10, 20),
                new PowerRange(Power.Retaliation, 15, 25),
                new PowerRange(Power.PreyPride, 3, 5),
                new PowerRange(Power.OverflowingLife, 20, 40),
                new PowerRange(Power.PerfectRead, 10, 20),
            },
        };

        public static readonly IReadOnlyList<TalentDef> Talents = new[]
        {
            new TalentDef("t.off.edge", Line.Offense, new Txt("鋭刃", "Keen Edge"), Stat.AttackPct, 3, 3),
            new TalentDef("t.off.mind", Line.Offense, new Txt("魔力", "Arcane Mind"), Stat.PowerPct, 3, 3),
            new TalentDef("t.off.swift", Line.Offense, new Txt("迅速", "Swiftness"), Stat.AttackSpeedPct, 3, 3),
            new TalentDef("t.off.vital", Line.Offense, new Txt("急所", "Vital Points"), Stat.CritChancePct, 2, 3),
            new TalentDef("t.off.key", Line.Offense, new Txt("舞い続ける者", "Ceaseless Dancer"), Power.Momentum, 4,
                new Txt("敵を次々に倒す戦い方に向いています。", "Suits fights where you chain kills.")),

            new TalentDef("t.grd.hearty", Line.Guard, new Txt("頑健", "Hearty"), Stat.MaxHealthPct, 4, 3),
            new TalentDef("t.grd.iron", Line.Guard, new Txt("鉄皮", "Ironhide"), Stat.Armor, 5, 3),
            new TalentDef("t.grd.regen", Line.Guard, new Txt("再生", "Regrowth"), Stat.HealthRegen, 1, 3),
            new TalentDef("t.grd.steady", Line.Guard, new Txt("不屈", "Unyielding"), Stat.Tenacity, 8, 3),
            new TalentDef("t.grd.key", Line.Guard, new Txt("逆襲の構え", "Counterstance"), Power.Retaliation, 20,
                new Txt("敵の攻撃を受け止めて反撃する戦い方に向いています。", "Suits a tank that strikes back.")),

            new TalentDef("t.res.focus", Line.Resonance, new Txt("集中", "Focus"), Stat.Haste, 5, 3),
            new TalentDef("t.res.light", Line.Resonance, new Txt("軽歩", "Lightstep"), Stat.MoveSpeedPct, 3, 3),
            new TalentDef("t.res.tune", Line.Resonance, new Txt("共振", "Attunement"), Stat.AttackPct, 2, 3),
            new TalentDef("t.res.ward", Line.Resonance, new Txt("守護", "Warding"), Stat.MaxHealthFlat, 12, 3),
            new TalentDef("t.res.key", Line.Resonance, new Txt("共鳴の環", "Ring of Resonance"), Power.Resonance, 8,
                new Txt("味方の近くで戦うほど活きます。", "Best when fighting close to allies.")),
        };

        /// <summary>同じ固有効果を複数持つ場合の合計上限。</summary>
        private static readonly Dictionary<Power, int> PowerCaps = new Dictionary<Power, int>
        {
            [Power.Momentum] = 10,
            [Power.Retaliation] = 60,
            [Power.Bulwark] = 80,
            [Power.Lifesteal] = 20,
            [Power.Thorns] = 80,
            [Power.Executioner] = 120,
            [Power.Resonance] = 20,
            [Power.Tailwind] = 50,
            [Power.Barrier] = 25,
            [Power.SecondWind] = 60,
            [Power.Blaze] = 150,
            [Power.ChainLightning] = 120,
            [Power.Shatter] = 150,
            [Power.Aegis] = 40,
            [Power.Bloodlust] = 40,
            [Power.Ember] = 150,
            [Power.Frost] = 100,
            [Power.Radiance] = 200,
            [Power.Umbra] = 200,
            [Power.Convergence] = 300,
            [Power.EchoingDodge] = 150,
            [Power.UltimateSurge] = 50,
            [Power.SoulSiphon] = 40,
            [Power.Whirlwind] = 200,
            [Power.Frenzy] = 6,
            [Power.OpeningStrike] = 150,
            [Power.StarShield] = 40,
            [Power.Sprint] = 60,
            [Power.Vigor] = 40,
            [Power.Overload] = 50,
            [Power.Finale] = 50,
            [Power.CriticalEcho] = 12,
            [Power.Fetters] = 40,
            [Power.CrystalResonance] = 6,
            [Power.PreyPride] = 12,
            [Power.OverflowingLife] = 100,
            [Power.Devotion] = 10,
            [Power.Wildfire] = 60,
            [Power.StillWater] = 15,
            [Power.SpendersWard] = 20,
            [Power.PerfectRead] = 40,
            [Power.LucidBoon] = 18,
            [Power.ShadowStep] = 150,
            [Power.Steam] = 120,
            [Power.Eclipse] = 25,
            [Power.Cinder] = 1,
            [Power.FrostCrystal] = 12,
        };

        /// <summary>MOD由来の能力値の合計上限（計画書 第7章の L2 上限 +120% を基準）。</summary>
        private static readonly Dictionary<Stat, int> StatCaps = new Dictionary<Stat, int>
        {
            [Stat.AttackPct] = 100, // v1.28：120 → 100（星図の分が最大約60あるので、装備の分を残す）
            [Stat.PowerPct] = 100,
            [Stat.AttackFlat] = 150,
            [Stat.PowerFlat] = 150,
            [Stat.AttackSpeedPct] = 80,
            [Stat.CritChancePct] = 50,
            [Stat.CritDamagePct] = 150,
            [Stat.MaxHealthPct] = 120,
            [Stat.MaxHealthFlat] = 600,
            [Stat.Armor] = 150,
            [Stat.HealthRegen] = 40,
            [Stat.Haste] = 100,
            [Stat.MoveSpeedPct] = 35,
            [Stat.Tenacity] = 60,
            [Stat.FireAmp] = 100,
            [Stat.ColdAmp] = 100,
            [Stat.LightAmp] = 100,
            [Stat.DarkAmp] = 100,
            [Stat.AttackRangePct] = 30,
            [Stat.FourthAttackShift] = 1, // v1.27：連装・四の型は1段まで
            [Stat.EssenceSlotIdentity] = 1, // v1.27：星図でエッセンス枠+1（能力補正ではなく枠の追加）
            [Stat.EssenceSlotMovement] = 1,
            [Stat.HealPower] = 60,
            [Stat.ShieldPower] = 60,
            [Stat.SummonPower] = 80,
            [Stat.SacrificeReduction] = 40,
        };

        private static readonly Dictionary<string, BaseDef> BaseById = Index(Bases, b => b.Id);
        private static readonly Dictionary<string, UniqueDef> UniqueById = Index(Uniques, u => u.Id);
        private static readonly Dictionary<string, TalentDef> TalentById = Index(Talents.Concat(HeroSigils.All), t => t.Id);

        private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key)
        {
            var d = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var i in items) d.Add(key(i), i);
            return d;
        }

        public static bool TryGetBase(string id, out BaseDef def) => BaseById.TryGetValue(id ?? string.Empty, out def);
        public static bool TryGetUnique(string id, out UniqueDef def) => UniqueById.TryGetValue(id ?? string.Empty, out def);
        public static bool TryGetTalent(string id, out TalentDef def) => TalentById.TryGetValue(id ?? string.Empty, out def);

        public static BaseDef GetBase(string id)
        {
            if (!TryGetBase(id, out var def)) throw new KeyNotFoundException("未知の装備基礎: " + id);
            return def;
        }

        public static IReadOnlyList<AffixDef> AffixPool(Slot slot) => AffixPools[slot];
        public static IReadOnlyList<PowerRange> PowerPool(Slot slot) => PowerPools[slot];

        /// <summary>エピックの銘（1つ目の固有効果から）。v1.22。</summary>
        private static readonly Dictionary<Power, Txt> Epithets = new Dictionary<Power, Txt>
        {
            [Power.Momentum] = new Txt("連撃の", "Relentless"),
            [Power.Retaliation] = new Txt("報復の", "Vengeful"),
            [Power.Bulwark] = new Txt("鉄壁の", "Steadfast"),
            [Power.Lifesteal] = new Txt("吸血の", "Vampiric"),
            [Power.Thorns] = new Txt("逆棘の", "Barbed"),
            [Power.Executioner] = new Txt("断罪の", "Executing"),
            [Power.Resonance] = new Txt("共鳴の", "Resonant"),
            [Power.Tailwind] = new Txt("追い風の", "Windborne"),
            [Power.Barrier] = new Txt("護りの", "Warding"),
            [Power.SecondWind] = new Txt("不倒の", "Undying"),
            [Power.Blaze] = new Txt("猛火の", "Blazing"),
            [Power.ChainLightning] = new Txt("雷鳴の", "Thundering"),
            [Power.Shatter] = new Txt("砕きの", "Shattering"),
            [Power.Aegis] = new Txt("守護の", "Guardian"),
            [Power.Bloodlust] = new Txt("血狂いの", "Bloodmad"),
            [Power.Ember] = new Txt("燻る", "Smoldering"),
            [Power.Frost] = new Txt("凍てつく", "Freezing"),
            [Power.Radiance] = new Txt("輝く", "Radiant"),
            [Power.Umbra] = new Txt("宵闇の", "Dusky"),
            [Power.Convergence] = new Txt("四元の", "Converging"),
            [Power.EchoingDodge] = new Txt("残像の", "Echoing"),
            [Power.UltimateSurge] = new Txt("昂りの", "Surging"),
            [Power.SoulSiphon] = new Txt("魂喰いの", "Soul-eating"),
            [Power.Whirlwind] = new Txt("旋風の", "Whirling"),
            [Power.Frenzy] = new Txt("乱戦の", "Frenzied"),
            [Power.OpeningStrike] = new Txt("先駆けの", "Vanguard"),
            [Power.StarShield] = new Txt("星護りの", "Starward"),
            [Power.Sprint] = new Txt("疾駆の", "Swift"),
            [Power.Vigor] = new Txt("万全の", "Hale"),
            [Power.Overload] = new Txt("溢れる", "Overflowing"),
            [Power.Finale] = new Txt("終曲の", "Final"),
            [Power.CriticalEcho] = new Txt("余韻の", "Lingering"),
            [Power.Fetters] = new Txt("枷の", "Fettering"),
            [Power.CrystalResonance] = new Txt("結晶の", "Crystalline"),
            [Power.PreyPride] = new Txt("誇り高き", "Proud"),
            [Power.OverflowingLife] = new Txt("満ちる", "Brimming"),
            [Power.Devotion] = new Txt("祈りの", "Devout"),
            [Power.Wildfire] = new Txt("飛び火の", "Spreading"),
            [Power.StillWater] = new Txt("止水の", "Stilled"),
            [Power.SpendersWard] = new Txt("散財の", "Lavish"),
            [Power.PerfectRead] = new Txt("見切りの", "Keen-eyed"),
            [Power.LucidBoon] = new Txt("明晰な", "Lucid"),
        };

        public static Txt Epithet(Power p) => Epithets.TryGetValue(p, out var t) ? t : ElementReactions.Epithet(p);

        public static int PowerCap(Power p) => PowerCaps.TryGetValue(p, out int c) ? c : 0;
        public static int StatCap(Stat s) => StatCaps.TryGetValue(s, out int c) ? c : 0;

        public static IEnumerable<BaseDef> BasesFor(Slot slot)
        {
            foreach (var b in Bases)
                if (b.Slot == slot) yield return b;
        }

        public static int AffixCount(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return 1;
                case Rarity.Uncommon: return 2;
                default: return 3;
            }
        }

        /// <summary>レア度による特性値の倍率（%）。</summary>
        public static int RarityValuePct(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return 110;
                case Rarity.Epic: return 120;
                case Rarity.Legendary: return 130;
                default: return 100;
            }
        }

        /// <summary>
        /// アイテムレベルで伸びる能力値か（v1.28）。固定値（攻撃力・魔力・最大HP・防御・HP回復・記憶加速・行動妨害耐性）だけが伸び、
        /// %の能力値はレベルで伸びない（レア度と強化の倍率は掛かる）。
        /// </summary>
        public static bool ScalesWithItemLevel(Stat s)
        {
            switch (s)
            {
                case Stat.AttackFlat:
                case Stat.PowerFlat:
                case Stat.MaxHealthFlat:
                case Stat.Armor:
                case Stat.HealthRegen:
                case Stat.Haste:
                case Stat.Tenacity:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 属性を付ける量の説明（v1.28：平均のスタック数ではなく%で書く）。
        /// 100 ごとに確実に1つ、残りはその%の確率でもう1つ。例：60 →「60%の確率で1つ」、160 →「1つ、さらに60%の確率でもう1つ」。
        /// </summary>
        private static string ElementJa(int v, string element)
        {
            int sure = Math.Max(0, v) / 100, chance = Math.Max(0, v) % 100;
            if (sure == 0) return $"通常攻撃が当たると、{chance}%の確率で敵に{element}を1つ付ける";
            string head = $"通常攻撃が当たるたびに、敵に{element}を{sure}つ付ける";
            return chance > 0 ? head + $"（さらに{chance}%の確率でもう1つ）" : head;
        }

        private static string ElementEn(int v, string element)
        {
            int sure = Math.Max(0, v) / 100, chance = Math.Max(0, v) % 100;
            if (sure == 0) return $"Basic attack hits have a {chance}% chance to apply 1 {element}";
            string head = $"Basic attack hits apply {sure} {element}";
            return chance > 0 ? head + $" (plus a {chance}% chance for 1 more)" : head;
        }

        /// <summary>アイテムレベルによる倍率（%）。1で100%、40以上で217%。</summary>
        public static int LevelScalePct(int itemLevel)
        {
            int l = Math.Max(1, Math.Min(itemLevel, ItemLevelScalingCap));
            return 100 + 3 * (l - 1);
        }

        /// <summary>
        /// 強化段階による倍率（%）。+5までは+1ごとに+6%、そこから先（限界突破）は+1ごとに+4%（+20で190%）。
        /// </summary>
        public static int EnhanceScalePct(int enhance)
        {
            int h = Math.Max(0, Math.Min(enhance, EnhanceMilestoneFifth));
            return h <= MaxEnhance ? 100 + 6 * h : 100 + 6 * MaxEnhance + 4 * (h - MaxEnhance);
        }

        /// <summary>固有効果の強化による倍率（%）。+5までは+1ごとに+5%、そこから先は+1ごとに+3%（+20で170%）。</summary>
        public static int EnhancePowerScalePct(int enhance)
        {
            int h = Math.Max(0, Math.Min(enhance, EnhanceMilestoneFifth));
            return h <= MaxEnhance ? 100 + 5 * h : 100 + 5 * MaxEnhance + 3 * (h - MaxEnhance);
        }

        /// <summary>+6以降の強化1回の欠片（+6〜+10が180・230・290・360・440、+11〜+15は1.5倍、+16〜+20は2倍）。</summary>
        private static readonly int[] LimitBreakEnhanceCosts = { 180, 230, 290, 360, 440 };

        public static int EnhanceCost(int currentEnhance)
        {
            switch (currentEnhance)
            {
                case 0: return 20;
                case 1: return 35;
                case 2: return 60;
                case 3: return 90;
                case 4: return 130;
                default:
                    int beyond = currentEnhance - MaxEnhance; // +6にするときが0
                    if (beyond < 0 || beyond >= LimitBreakEnhanceCosts.Length * 3) return int.MaxValue;
                    int baseCost = LimitBreakEnhanceCosts[beyond % LimitBreakEnhanceCosts.Length];
                    int tier = beyond / LimitBreakEnhanceCosts.Length; // 0:+6〜+10、1:+11〜+15、2:+16〜+20
                    return tier == 0 ? baseCost : tier == 1 ? baseCost * 3 / 2 : baseCost * 2;
            }
        }

        /// <summary>限界突破の回数の上限。レア1回・エピック2回・伝説3回。コモン・アンコモンはできない。</summary>
        public static int MaxLimitBreaks(Rarity r) => r >= Rarity.Legendary ? 3 : r >= Rarity.Epic ? 2 : r >= Rarity.Rare ? 1 : 0;

        /// <summary>その遺物の強化の上限。限界突破1回ごとに+5（レア+10・エピック+15・伝説+20）。</summary>
        public static int MaxEnhanceFor(Relic r) => MaxEnhanceFor(r == null ? Rarity.Common : r.Rarity, r?.LimitBreaks ?? 0);

        public static int MaxEnhanceFor(Rarity rarity, int limitBreaks)
            => MaxEnhance + EnhanceStepPerBreak * Math.Max(0, Math.Min(MaxLimitBreaks(rarity), limitBreaks));

        /// <summary>限界突破 n 回目（1〜3）に要る調律石。</summary>
        public static int LimitBreakTuningCost(int n) => n <= 1 ? 5 : n == 2 ? 10 : 20;

        /// <summary>限界突破 n 回目（1〜3）に要る欠片。</summary>
        public static int LimitBreakShardCost(int n) => n <= 1 ? 200 : n == 2 ? 400 : 800;

        public static int RetuneCost(int retunesDone) => retunesDone + 1;

        public static int SalvageShards(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return 3;
                case Rarity.Uncommon: return 6;
                case Rarity.Rare: return 12;
                case Rarity.Epic: return 30;
                default: return 60;
            }
        }

        public static int SalvageTuning(Rarity r) => r >= Rarity.Epic ? 1 : 0;

        /// <summary>夢のレベル n から n+1 へ必要な経験値。</summary>
        public static int XpToNext(int level) => 60 + 25 * Math.Max(1, level) + 3 * level * level;

        public static int KillXp(MonsterTier tier)
        {
            switch (tier)
            {
                case MonsterTier.Lesser: return 1;
                case MonsterTier.Normal: return 2;
                case MonsterTier.MiniBoss: return 12;
                default: return 50;
            }
        }

        /// <summary>装着中の伝説の遺物にたまる覚醒の力。悪夢化の報酬格ではなく、元の敵の格を使う。</summary>
        public static int AwakenPoints(MonsterTier tier, bool nightmare)
        {
            int points;
            switch (tier)
            {
                case MonsterTier.Boss: points = 20; break;
                case MonsterTier.MiniBoss: points = 5; break;
                default: points = 1; break;
            }
            return nightmare ? points * 2 : points;
        }

        public const int SecureXp = 20;
        public const int VictoryXp = 100;

        /// <summary>同じ系統の遺物を count 個装着したときの累積ボーナス（2個・3個・4個・6個）。</summary>
        public static IEnumerable<StatLine> SetBonus(Line line, int count)
        {
            if (count >= 2)
            {
                switch (line)
                {
                    case Line.Offense:
                        yield return new StatLine(Stat.AttackPct, 5);
                        yield return new StatLine(Stat.PowerPct, 5);
                        break;
                    case Line.Guard:
                        yield return new StatLine(Stat.Armor, 8);
                        break;
                    default:
                        yield return new StatLine(Stat.Haste, 8);
                        break;
                }
            }
            if (count >= 3)
            {
                switch (line)
                {
                    case Line.Offense: yield return new StatLine(Stat.AttackSpeedPct, 6); break;
                    case Line.Guard: yield return new StatLine(Stat.MaxHealthPct, 6); break;
                    default: yield return new StatLine(Stat.MoveSpeedPct, 4); break;
                }
            }
            if (count >= 4)
            {
                switch (line)
                {
                    case Line.Offense: yield return new StatLine(Stat.CritChancePct, 4); break;
                    case Line.Guard: yield return new StatLine(Stat.Tenacity, 12); break;
                    default: yield return new StatLine(Stat.Haste, 8); break;
                }
            }
            if (count >= 6)
            {
                switch (line)
                {
                    case Line.Offense:
                        yield return new StatLine(Stat.AttackPct, 8);
                        yield return new StatLine(Stat.PowerPct, 8);
                        break;
                    case Line.Guard:
                        yield return new StatLine(Stat.Armor, 12);
                        yield return new StatLine(Stat.MaxHealthPct, 6);
                        break;
                    default:
                        yield return new StatLine(Stat.PowerPct, 8);
                        yield return new StatLine(Stat.MoveSpeedPct, 4);
                        break;
                }
            }
        }

        public static Txt SlotName(Slot s)
        {
            switch (s)
            {
                case Slot.Weapon: return new Txt("主装備", "Weapon");
                case Slot.Armor: return new Txt("防具", "Armor");
                case Slot.Head: return new Txt("頭", "Head");
                case Slot.Hands: return new Txt("手", "Hands");
                case Slot.Feet: return new Txt("足", "Feet");
                default: return new Txt("装飾品", "Charm");
            }
        }

        public static Txt RarityName(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return new Txt("コモン", "Common");
                case Rarity.Uncommon: return new Txt("アンコモン", "Uncommon");
                case Rarity.Rare: return new Txt("レア", "Rare");
                case Rarity.Epic: return new Txt("エピック", "Epic");
                default: return new Txt("固有品", "Legendary");
            }
        }

        public static Txt LineName(Line l)
        {
            switch (l)
            {
                case Line.Offense: return new Txt("破壊", "Destruction");
                case Line.Guard: return new Txt("生命", "Life");
                default: return new Txt("想像", "Imagination");
            }
        }

        public static string FormatStat(Stat s, int v)
        {
            string sign = v >= 0 ? "+" : "";
            switch (s)
            {
                case Stat.AttackPct: return Loc.T($"攻撃力 {sign}{v}%", $"{sign}{v}% Attack Damage");
                case Stat.PowerPct: return Loc.T($"魔力 {sign}{v}%", $"{sign}{v}% Ability Power");
                case Stat.AttackSpeedPct: return Loc.T($"攻撃速度 {sign}{v}%", $"{sign}{v}% Attack Speed");
                case Stat.CritChancePct: return Loc.T($"会心率 {sign}{v}%", $"{sign}{v}% Crit Chance");
                case Stat.CritDamagePct: return Loc.T($"会心ダメージ {sign}{v}%", $"{sign}{v}% Crit Damage");
                case Stat.MaxHealthPct: return Loc.T($"最大HP {sign}{v}%", $"{sign}{v}% Max Health");
                case Stat.MaxHealthFlat: return Loc.T($"最大HP {sign}{v}", $"{sign}{v} Max Health");
                case Stat.AttackFlat: return Loc.T($"攻撃力 {sign}{v}", $"{sign}{v} Attack Damage");
                case Stat.PowerFlat: return Loc.T($"魔力 {sign}{v}", $"{sign}{v} Ability Power");
                case Stat.Armor: return Loc.T($"防御 {sign}{v}", $"{sign}{v} Armor");
                case Stat.HealthRegen: return Loc.T($"HP回復 {sign}{v}/秒", $"{sign}{v} Health Regen/s");
                case Stat.Haste: return Loc.T($"スキル加速 {sign}{v}", $"{sign}{v} Ability Haste");
                case Stat.MoveSpeedPct: return Loc.T($"移動速度 {sign}{v}%", $"{sign}{v}% Move Speed");
                case Stat.Tenacity: return Loc.T($"行動妨害耐性 {sign}{v}", $"{sign}{v} Tenacity");
                case Stat.FireAmp: return Loc.T($"火属性効果 {sign}{v}%", $"{sign}{v}% Fire Effect");
                case Stat.ColdAmp: return Loc.T($"冷気属性効果 {sign}{v}%", $"{sign}{v}% Cold Effect");
                case Stat.LightAmp: return Loc.T($"光属性効果 {sign}{v}%", $"{sign}{v}% Light Effect");
                case Stat.AttackRangePct: return Loc.T($"通常攻撃の射程 {sign}{v}%", $"{sign}{v}% Attack Range");
                case Stat.FourthAttackShift: return Loc.T($"4発目の強い攻撃が{v}発早く出る", $"Empowered 4th attack comes {v} hit(s) sooner");
                case Stat.EssenceSlotIdentity: return Loc.T($"アイデンティティ記憶にエッセンスをもう{v}つはめられる", $"You can socket {v} more essence in your Identity memory");
                case Stat.EssenceSlotMovement: return Loc.T($"回避（移動の記憶）にエッセンスをもう{v}つはめられる", $"You can socket {v} more essence in your Dodge (Movement memory)");
                case Stat.HealPower: return Loc.T($"与える回復が{v}%増える（味方への回復も・上限{StatCap(s)}%）", $"Healing you grant increases by {v}% (including allies; cap {StatCap(s)}%)");
                case Stat.ShieldPower: return Loc.T($"与えるシールドが{v}%増える（味方へのシールドも・上限{StatCap(s)}%）", $"Shields you grant increase by {v}% (including allies; cap {StatCap(s)}%)");
                case Stat.SummonPower: return Loc.T($"召喚獣の与えるダメージが{v}%増える（上限{StatCap(s)}%）", $"Your summons deal {v}% more damage (cap {StatCap(s)}%)");
                case Stat.SacrificeReduction: return Loc.T($"HPを捧げる技の消費が{v}%減る（上限{StatCap(s)}%）", $"Skills that sacrifice HP cost {v}% less HP (cap {StatCap(s)}%)");
                default: return Loc.T($"闇属性効果 {sign}{v}%", $"{sign}{v}% Dark Effect");
            }
        }

        public static string PowerName(Power p)
        {
            switch (p)
            {
                case Power.Momentum: return Loc.T("連撃", "Momentum");
                case Power.Retaliation: return Loc.T("逆襲", "Retaliation");
                case Power.Bulwark: return Loc.T("鉄の輪", "Bulwark");
                case Power.Lifesteal: return Loc.T("吸命", "Lifesteal");
                case Power.Thorns: return Loc.T("棘", "Thorns");
                case Power.Executioner: return Loc.T("処刑", "Executioner");
                case Power.Resonance: return Loc.T("共鳴", "Resonance");
                case Power.Tailwind: return Loc.T("追い風", "Tailwind");
                case Power.Barrier: return Loc.T("護りの灯", "Barrier");
                case Power.SecondWind: return Loc.T("灯守", "Second Wind");
                case Power.Blaze: return Loc.T("烈火", "Blaze");
                case Power.ChainLightning: return Loc.T("雷鎖", "Chain Lightning");
                case Power.Shatter: return Loc.T("爆砕", "Shatter");
                case Power.Aegis: return Loc.T("守護霊", "Aegis");
                case Power.Bloodlust: return Loc.T("血の渇き", "Bloodlust");
                case Power.Ember: return Loc.T("火種", "Ember");
                case Power.Frost: return Loc.T("霜", "Frost");
                case Power.Radiance: return Loc.T("輝き", "Radiance");
                case Power.Umbra: return Loc.T("影", "Umbra");
                case Power.Convergence: return Loc.T("四元の共鳴", "Convergence");
                case Power.EchoingDodge: return Loc.T("回避の残響", "Echoing Dodge");
                case Power.UltimateSurge: return Loc.T("終の昂り", "Ultimate Surge");
                case Power.SoulSiphon: return Loc.T("吸魂", "Soul Siphon");
                case Power.Whirlwind: return Loc.T("旋風", "Whirlwind");
                case Power.Frenzy: return Loc.T("乱戦", "Melee Frenzy");
                case Power.OpeningStrike: return Loc.T("先制", "Opening Strike");
                case Power.StarShield: return Loc.T("星の加護", "Star Shield");
                case Power.Sprint: return Loc.T("疾駆", "Sprint");
                case Power.Vigor: return Loc.T("万全", "Vigor");
                case Power.Overload: return Loc.T("過負荷", "Overload");
                case Power.Finale: return Loc.T("終曲", "Finale");
                case Power.CriticalEcho: return Loc.T("会心の余韻", "Critical Echo");
                case Power.Fetters: return Loc.T("足枷", "Fetters");
                case Power.CrystalResonance: return Loc.T("結晶共鳴", "Crystal Resonance");
                case Power.PreyPride: return Loc.T("獲物の誇り", "Prey's Pride");
                case Power.OverflowingLife: return Loc.T("溢れる命", "Overflowing Life");
                case Power.Devotion: return Loc.T("祈願", "Devotion");
                case Power.Wildfire: return Loc.T("飛び火", "Wildfire");
                case Power.StillWater: return Loc.T("止水", "Still Water");
                case Power.SpendersWard: return Loc.T("散財の護り", "Spender's Ward");
                case Power.PerfectRead: return Loc.T("見切り", "Perfect Read");
                case Power.LucidBoon: return Loc.T("明晰", "Lucid Boon");
                case Power.ShadowStep: return Loc.T("瞬歩の刃", "Flash-Step Blade");
                case Power.Steam: return Loc.T("蒸気", "Steam");
                case Power.Eclipse: return Loc.T("蝕", "Eclipse");
                case Power.Cinder: return Loc.T("燃え殻", "Cinder");
                case Power.FrostCrystal: return Loc.T("氷晶", "Frost Crystal");
                default: return "-";
            }
        }

        public static string FormatPower(Power p, int v)
        {
            string name = PowerName(p);
            if (ElementReactions.IsPower(p)) return FormatReaction(p, v, name);
            switch (p)
            {
                case Power.Momentum: return Loc.T($"【{name}】敵を倒すたびに攻撃速度が{v}%上がる（4秒間、5回まで重なる）", $"[{name}] Each kill grants +{v}% attack speed for 4s (stacks up to 5 times)");
                case Power.Retaliation: return Loc.T($"【{name}】攻撃を受けると、3秒間 攻撃力・魔力が{v}%上がる（重ならず時間を延長）", $"[{name}] After taking a hit, gain +{v}% attack damage and ability power for 3s (refreshes, does not stack)");
                case Power.Bulwark: return Loc.T($"【{name}】周りに敵が3体以上いる間、防御が{v}上がる", $"[{name}] +{v} armor while 3 or more enemies are nearby");
                case Power.Lifesteal: return Loc.T($"【{name}】通常攻撃が当たるたびに、最大HPの{v / 10f:0.#}%を回復する（0.15秒に1回まで）", $"[{name}] Basic attack hits heal you for {v / 10f:0.#}% of max health (at most once per 0.15s)");
                case Power.Thorns: return Loc.T($"【{name}】受けたダメージの{v}%を相手に跳ね返す（1秒に1回）", $"[{name}] Reflect {v}% of damage taken back to the attacker (once per second)");
                case Power.Executioner: return Loc.T($"【{name}】HPが30%未満の敵への通常攻撃に、攻撃力{v}%分のダメージを上乗せする", $"[{name}] Basic attacks on enemies below 30% health deal +{v}% AD as bonus damage");
                case Power.Resonance: return Loc.T($"【{name}】近くに味方がいる間、自分の攻撃力・魔力が{v}%、近くの味方は{v / 2}%上がる（一人のときは自分も半分。味方への分は重ならず、いちばん高い人の分）", $"[{name}] While an ally is near, you gain +{v}% AD/AP and nearby allies gain +{v / 2}% (half for yourself when alone; allies take only the highest, no stacking)");
                case Power.Tailwind: return Loc.T($"【{name}】敵を倒した後の2秒間、移動速度が{v}%上がる（重ならず時間を延長）", $"[{name}] +{v}% move speed for 2s after a kill (refreshes, does not stack)");
                case Power.Barrier: return Loc.T($"【{name}】12秒ごとに、最大HPの{v}%分の障壁を4秒間張る", $"[{name}] Every 12s, gain a shield worth {v}% of max health for 4s");
                case Power.SecondWind: return Loc.T($"【{name}】HPが30%を切ると、最大HPの{v}%を回復する（60秒に1回）", $"[{name}] When you drop below 30% health, heal {v}% of max health (once per 60s)");
                case Power.Blaze: return Loc.T($"【{name}】通常攻撃4回ごとに、攻撃力か魔力の高い方の{v}%分の魔法ダメージを追加する", $"[{name}] Every 4th basic attack deals +{v}% of the higher of AD or AP as magic damage");
                case Power.ChainLightning: return Loc.T($"【{name}】通常攻撃が当たると25%の確率で、近くの敵2体に攻撃力か魔力の高い方の{v}%分の魔法ダメージを与える", $"[{name}] Basic attack hits have a 25% chance to deal {v}% of the higher of AD or AP as magic damage to 2 nearby enemies");
                case Power.Shatter: return Loc.T($"【{name}】敵を倒すと、周囲4mの敵に攻撃力か魔力の高い方の{v}%分のダメージを与える（爆砕で倒した敵からは起きない）", $"[{name}] On kill, deal {v}% of the higher of AD or AP to enemies within 4m (kills by Shatter do not chain)");
                case Power.Aegis: return Loc.T($"【{name}】最大HPの10%以上の一撃を受けると、最大HPの{v}%分の障壁を6秒間張る（12秒に1回）", $"[{name}] When a single hit deals 10%+ of your max health, gain a shield worth {v}% of max health for 6s (once per 12s)");
                case Power.Bloodlust: return Loc.T($"【{name}】HPが50%未満の間、攻撃速度が{v}%上がる", $"[{name}] +{v}% attack speed while below 50% health");
                case Power.Ember: return Loc.T($"【{name}】" + ElementJa(v, "火") + "（火は上限なしで重なる）", $"[{name}] " + ElementEn(v, "Fire") + " (Fire has no stack limit)");
                case Power.Frost: return Loc.T($"【{name}】通常攻撃が当たると{v}%の確率で、敵を冷気で冷やす（冷気は重ならない）", $"[{name}] Basic attack hits have a {v}% chance to apply Cold (Cold does not stack)");
                case Power.Radiance: return Loc.T($"【{name}】" + ElementJa(v, "光") + "（光は5つまで。3つ重なると光のダメージは必ず会心）", $"[{name}] " + ElementEn(v, "Light") + " (up to 5; at 3, light damage always crits)");
                case Power.Umbra: return Loc.T($"【{name}】" + ElementJa(v, "闇") + "。会心で当たればもう1つ（闇は5つまで）", $"[{name}] " + ElementEn(v, "Dark") + "; a critical hit adds 1 more (up to 5)");
                case Power.Convergence: return Loc.T($"【{name}】敵に火・冷気・光・闇がそろった瞬間、攻撃力か魔力の高い方の{v}%分の爆発を起こす（同じ敵には6秒に1回）", $"[{name}] When an enemy has Fire, Cold, Light and Dark at once, it bursts for {v}% of the higher of AD or AP (once per 6s per enemy)");
                case Power.EchoingDodge: return Loc.T($"【{name}】回避した後3秒以内の次の通常攻撃に、攻撃力か魔力の高い方の{v}%分のダメージを上乗せする（重ならず、回避するたびに時間を延長）", $"[{name}] After a dodge, your next basic attack within 3s deals +{v}% of the higher of AD or AP (does not stack; each dodge refreshes it)");
                case Power.UltimateSurge: return Loc.T($"【{name}】Ultimateを使った後の5秒間、攻撃力・魔力が{v}%上がる（重ならず時間を延長）", $"[{name}] +{v}% AD/AP for 5s after using your Ultimate (refreshes, does not stack)");
                case Power.SoulSiphon: return Loc.T($"【{name}】敵を倒すと、最大HPの{v / 10f:0.0}%を回復する（0.5秒に1回まで）", $"[{name}] Kills heal you for {v / 10f:0.0}% of max health (at most once per 0.5s)");
                case Power.Whirlwind: return Loc.T($"【{name}】回避すると、周囲4mの敵に攻撃力か魔力の高い方の{v}%分のダメージを与える（2秒に1回）", $"[{name}] Dodging deals {v}% of the higher of AD or AP to enemies within 4m (once per 2s)");
                case Power.Frenzy: return Loc.T($"【{name}】周りの敵1体につき、攻撃速度が{v}%上がる（8体まで）", $"[{name}] +{v}% attack speed per nearby enemy (up to 8)");
                case Power.OpeningStrike: return Loc.T($"【{name}】HPが90%以上の敵への通常攻撃に、攻撃力{v}%分のダメージを上乗せする", $"[{name}] Basic attacks on enemies above 90% health deal +{v}% AD");
                case Power.StarShield: return Loc.T($"【{name}】Ultimateを使うと、最大HPの{v}%分の障壁を5秒間張る", $"[{name}] Using your Ultimate grants a shield worth {v}% of max health for 5s");
                case Power.Sprint: return Loc.T($"【{name}】回避した後の3秒間、移動速度と攻撃速度が{v}%上がる（重ならず時間を延長）", $"[{name}] +{v}% move speed and attack speed for 3s after dodging (refreshes, does not stack)");
                case Power.Vigor: return Loc.T($"【{name}】HPが80%以上の間、攻撃力・魔力が{v}%上がる", $"[{name}] +{v}% attack damage and ability power while above 80% health");
                case Power.Overload: return Loc.T($"【{name}】Q・W・Eを使った後の4秒間、攻撃力・魔力が{v}%上がる（重ならず時間を延長）", $"[{name}] +{v}% AD/AP for 4s after using Q, W or E (refreshes, does not stack)");
                case Power.Finale: return Loc.T($"【{name}】Q・W・Eを8秒以内にすべて使うと、Ultimate の残りクールダウンが{v}%縮む（10秒に1回）", $"[{name}] Using Q, W and E within 8s cuts your Ultimate's remaining cooldown by {v}% (once per 10s)");
                case Power.CriticalEcho: return Loc.T($"【{name}】通常攻撃が会心で当たると、Q・W・Eのクールダウンが{v / 10f:0.#}秒縮む（0.5秒に1回まで）", $"[{name}] Critical basic attacks shorten Q/W/E cooldowns by {v / 10f:0.#}s (at most once per 0.5s)");
                case Power.Fetters: return Loc.T($"【{name}】スタン・スロウ・冷気のどれかが乗った敵へのダメージが{v}%上がる", $"[{name}] +{v}% damage to stunned, slowed or chilled enemies");
                case Power.CrystalResonance: return Loc.T($"【{name}】装着中のエッセンスの品質の合計100%ごとに、攻撃力・魔力が{v}%上がる（8段まで）", $"[{name}] +{v}% AD/AP per 100% total quality of your equipped Essences (up to 8)");
                case Power.PreyPride: return Loc.T($"【{name}】ハンターの追跡度1ごとに、攻撃力・魔力が{v}%上がる（3まで）", $"[{name}] +{v}% AD/AP per hunter tracking level (up to 3)");
                case Power.OverflowingLife: return Loc.T($"【{name}】最大HPを超えた回復の{v}%が、3秒の障壁になる（障壁は最大HPの10%まで）", $"[{name}] {v}% of overhealing becomes a 3s shield (up to 10% of max health)");
                case Power.Devotion: return Loc.T($"【{name}】聖堂を使うたび、そのゾーンの間 攻撃力・魔力が{v}%上がる（5回まで）", $"[{name}] Each shrine you use grants +{v}% AD/AP for the rest of the zone (up to 5)");
                case Power.Wildfire: return Loc.T($"【{name}】火が3つ以上重なった敵に火を付けると、{v}%の確率で近くの敵にも火が1つ移る", $"[{name}] Applying fire to an enemy with 3+ fire stacks has a {v}% chance to spread 1 stack to a nearby enemy");
                case Power.StillWater: return Loc.T($"【{name}】自分の技で敵をスタンさせると、最大HPの{v}%分の障壁を3秒間張る（2秒に1回）", $"[{name}] Stunning an enemy with your own skill grants a {v}% max-health shield for 3s (once per 2s)");
                case Power.SpendersWard: return Loc.T($"【{name}】ゴールドを100使うごとに、最大HPの{v}%分の障壁を10秒間張る（3回分まで重なる）", $"[{name}] Each 100 gold spent grants a {v}% max-health shield for 10s (up to 3 stacks)");
                case Power.PerfectRead: return Loc.T($"【{name}】無敵でダメージを実際に無効化すると、4秒間 攻撃速度が{v}%上がる（1.5秒に1回、重ならず時間を延長）", $"[{name}] Negating damage with invulnerability grants +{v}% attack speed for 4s (once per 1.5s; refreshes without stacking)");
                case Power.LucidBoon: return Loc.T($"【{name}】有効な邪悪な明晰夢1つにつき、攻撃力・魔力が{v}%上がる（6つまで、合計18%まで）", $"[{name}] +{v}% AD/AP per active Evil lucid dream (up to 6 dreams and +18% total)");
                case Power.ShadowStep: return Loc.T($"【{name}】回避・ダッシュ・瞬間移動の後3秒以内の次の通常攻撃に、攻撃力か魔力の高い方の{v}%分のダメージを上乗せする（重ならず、移動するたびに時間を延長）", $"[{name}] After a dodge, dash or teleport, your next basic attack within 3s deals +{v}% of the higher of AD or AP (does not stack; each movement refreshes it)");
                default: return "-";
            }
        }

        private static string FormatReaction(Power p, int v, string name)
        {
            v = Math.Max(0, Math.Min(PowerCap(p), v));
            string effect;
            switch (p)
            {
                case Power.Steam:
                    effect = Loc.T($"【{name}】火と冷気のある敵を中心に、3m以内の敵へ攻撃力か魔力の高い方の{v}%分の無属性ダメージを与え、2秒間30%のスロウを付ける（ダメージの合計上限120%）",
                        $"[{name}] When an enemy has Fire and Cold, deal {v}% of the higher of AD or AP as non-elemental damage to it and enemies within 3m, and slow them by 30% for 2s (damage total capped at 120%)");
                    break;
                case Power.Eclipse:
                    effect = Loc.T($"【{name}】光と闇のある敵は、4秒間 自分から受けるダメージが{v}%増える（合計上限25%。同じ効果は重ならず高い値だけ適用）",
                        $"[{name}] An enemy with Light and Dark takes {v}% more damage from you for 4s (total capped at 25%; uses the highest Expose effect without stacking)");
                    break;
                case Power.Cinder:
                    effect = Loc.T($"【{name}】火と闇のある敵に印を付け、倒れると周囲4mの敵へ火を1つ付ける（合計上限1つ。印は重ならず、味方の撃破でも発動）",
                        $"[{name}] Mark an enemy with Fire and Dark. When it dies, apply 1 Fire stack to enemies within 4m (total capped at 1; marks do not stack; ally kills also trigger it)");
                    break;
                default:
                    effect = Loc.T($"【{name}】光と冷気のある敵に反応し、最大HPの{v}%分の障壁を4秒間得る（合計上限12%）",
                        $"[{name}] When an enemy has Light and Cold, gain a shield worth {v}% of max health for 4s (total capped at 12%)");
                    break;
            }
            return effect + Loc.T("。属性は消費しない。継続・多段の属性付与による連発を防ぐため、同じ敵への同じ反応は6秒に1回。反応の追加効果は連鎖しない。道標の倍率は効果量に適用する。",
                ". Does not consume elements. Each reaction is limited to once per 6s per enemy to prevent repeated triggers from persistent or multi-hit elemental effects. Reaction effects do not chain. Waypoints multiply the effect amount.");
        }
    }
}
