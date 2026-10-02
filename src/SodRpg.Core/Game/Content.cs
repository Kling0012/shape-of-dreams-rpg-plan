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
        public AffixDef(Stat stat, int min, int max, int weight = 10)
        {
            Stat = stat;
            Min = min;
            Max = max;
            Weight = weight;
        }

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
        /// <summary>小ノードが1段ごとに伸ばす固有効果。能力値ノードは None。</summary>
        public Power RankPower { get; set; }
        public bool IsPowerNode => !IsKeystone && RankPower != Power.None;
        /// <summary>1は手前の星、2は奥の星。</summary>
        public int Tier { get; set; } = 1;
        public bool IsKeystone { get; }
        public Power Power { get; }
        public int PowerValue { get; }
        public Txt Description { get; }
        /// <summary>旅人の刻印なら旅人の型名（例：Hero_Vesper）。汎用ノードは null。</summary>
        public string HeroKey { get; set; }

        /// <summary>星図に表示する効果。小ノードは1段あたりの値。</summary>
        public string Describe()
        {
            if (IsKeystone) return Content.FormatPower(Power, PowerValue) + "\n" + Description;
            string effect = IsPowerNode ? Content.FormatPower(RankPower, PerRank) : Content.FormatStat(Stat, PerRank);
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
        public const int DeepStarRequirement = 6;
        public const int KeystoneCost = 3;
        public const int RoomsToRecoverLost = 3;
        public const int CodexPerPoint = 6;
        public const int MaxCodexBonus = 4;
        /// <summary>強化の節目。+3で特性が1行、+5で固有効果（なければ）か特性1行。</summary>
        public const int EnhanceMilestoneFirst = 3;
        public const int EnhanceMilestoneSecond = 5;
        /// <summary>再調律で出す候補の数。</summary>
        public const int RetuneChoices = 3;
        /// <summary>合成の結果の枠を選ぶときの欠片の倍率（%）。</summary>
        public const int TransmuteTargetCostPct = 150;
        public const int AwakenThreshold = 500;
        public const int AwakenPowerPct = 150;
        public const int AwakenAffixPct = 120;

        public static readonly IReadOnlyList<BaseDef> Bases = new[]
        {
            new BaseDef("weapon.chain_sword", Slot.Weapon, Line.Offense, new Txt("連なりの剣", "Chain Sword"), Stat.AttackSpeedPct, 4),
            new BaseDef("weapon.twin_fang", Slot.Weapon, Line.Offense, new Txt("双牙の短刀", "Twin Fang Daggers"), Stat.CritChancePct, 3),
            new BaseDef("weapon.calming_staff", Slot.Weapon, Line.Resonance, new Txt("鎮めの杖", "Calming Staff"), Stat.PowerPct, 6),
            new BaseDef("weapon.longspike_bow", Slot.Weapon, Line.Offense, new Txt("長穂の弓", "Longspike Bow"), Stat.CritDamagePct, 10),
            new BaseDef("weapon.shield_maul", Slot.Weapon, Line.Guard, new Txt("盾打ちの槌", "Shieldbash Maul"), Stat.MaxHealthPct, 5),
            new BaseDef("weapon.blaze_greatsword", Slot.Weapon, Line.Offense, new Txt("烈火の大剣", "Blazing Greatsword"), Stat.AttackPct, 6),

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
            new BaseDef("armor.star_cloak", Slot.Armor, Line.Resonance, new Txt("星読みの外套", "Stargazer's Cloak"), Stat.PowerPct, 5),
            new BaseDef("charm.ember_locket", Slot.Charm, Line.Offense, new Txt("残り火のロケット", "Ember Locket"), Stat.FireAmp, 10),
            new BaseDef("charm.moon_bell", Slot.Charm, Line.Resonance, new Txt("月の鈴", "Moon Bell"), Stat.ColdAmp, 10),
            new BaseDef("charm.iron_feather", Slot.Charm, Line.Guard, new Txt("鉄の羽根", "Iron Feather"), Stat.Armor, 6),
            new BaseDef("weapon.hunting_bow", Slot.Weapon, Line.Offense, new Txt("狩人の短弓", "Hunting Shortbow"), Stat.AttackSpeedPct, 5),
            new BaseDef("weapon.war_axe", Slot.Weapon, Line.Offense, new Txt("戦斧", "War Axe"), Stat.AttackPct, 7),
            new BaseDef("weapon.tower_lance", Slot.Weapon, Line.Guard, new Txt("城壁の槍", "Rampart Lance"), Stat.Armor, 6),
            new BaseDef("weapon.oath_mace", Slot.Weapon, Line.Guard, new Txt("誓いの戦棍", "Oath Mace"), Stat.MaxHealthFlat, 25),
            new BaseDef("weapon.dream_wand", Slot.Weapon, Line.Resonance, new Txt("夢見の杖", "Dreamer's Wand"), Stat.Haste, 6),
            new BaseDef("weapon.star_harp", Slot.Weapon, Line.Resonance, new Txt("星の竪琴", "Star Harp"), Stat.PowerPct, 7),
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
            new BaseDef("charm.dream_lens", Slot.Charm, Line.Resonance, new Txt("夢見の水晶", "Dreaming Lens"), Stat.PowerPct, 5),
            new BaseDef("charm.shadow_mask", Slot.Charm, Line.Resonance, new Txt("影の仮面", "Shadow Mask"), Stat.DarkAmp, 10),
            new BaseDef("head.iron_helm", Slot.Head, Line.Guard, new Txt("鉄の兜", "Iron Helm"), Stat.Armor, 6),
            new BaseDef("head.dream_circlet", Slot.Head, Line.Resonance, new Txt("夢見の額冠", "Dreamer's Circlet"), Stat.Haste, 5),
            new BaseDef("head.hunter_hood", Slot.Head, Line.Offense, new Txt("狩人の頭巾", "Hunter's Hood"), Stat.CritChancePct, 3),
            new BaseDef("head.horned_helm", Slot.Head, Line.Offense, new Txt("双角の兜", "Horned Helm"), Stat.AttackPct, 5),
            new BaseDef("head.sage_hat", Slot.Head, Line.Resonance, new Txt("賢者のとんがり帽", "Sage's Hat"), Stat.PowerPct, 5),
            new BaseDef("head.mist_veil", Slot.Head, Line.Resonance, new Txt("霧のヴェール", "Veil of Mist"), Stat.MoveSpeedPct, 3),
            new BaseDef("head.warden_visor", Slot.Head, Line.Guard, new Txt("番人の面頬", "Warden's Visor"), Stat.Tenacity, 8),
            new BaseDef("head.ember_crown", Slot.Head, Line.Offense, new Txt("残り火の冠", "Ember Crown"), Stat.FireAmp, 6),
            new BaseDef("head.moon_hood", Slot.Head, Line.Resonance, new Txt("月影の頭巾", "Moonshadow Hood"), Stat.DarkAmp, 6),
            new BaseDef("head.healer_band", Slot.Head, Line.Guard, new Txt("癒し手の鉢巻", "Healer's Band"), Stat.HealthRegen, 2),
            new BaseDef("hands.leather_gloves", Slot.Hands, Line.Offense, new Txt("革の手袋", "Leather Gloves"), Stat.AttackSpeedPct, 4),
            new BaseDef("hands.iron_gauntlets", Slot.Hands, Line.Guard, new Txt("鉄の籠手", "Iron Gauntlets"), Stat.Armor, 6),
            new BaseDef("hands.archer_bracers", Slot.Hands, Line.Offense, new Txt("射手の腕当て", "Archer's Bracers"), Stat.CritDamagePct, 10),
            new BaseDef("hands.spell_gloves", Slot.Hands, Line.Resonance, new Txt("呪文の手袋", "Spellweave Gloves"), Stat.PowerPct, 5),
            new BaseDef("hands.claw_gauntlets", Slot.Hands, Line.Offense, new Txt("獣爪の手甲", "Beastclaw Gauntlets"), Stat.AttackPct, 5),
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
            new BaseDef("feet.spiked_boots", Slot.Feet, Line.Offense, new Txt("棘付きの長靴", "Spiked Boots"), Stat.AttackPct, 5),
            new BaseDef("feet.sage_slippers", Slot.Feet, Line.Resonance, new Txt("賢者の室内履き", "Sage's Slippers"), Stat.PowerPct, 5),
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
            new BaseDef("weapon.thunder_hammer", Slot.Weapon, Line.Offense, new Txt("雷鳴の戦鎚", "Thunder Hammer"), Stat.AttackPct, 6),
            new BaseDef("head.wolf_pelt", Slot.Head, Line.Offense, new Txt("狼の毛皮かぶり", "Wolf Pelt Hood"), Stat.AttackSpeedPct, 4),
            new BaseDef("head.star_diadem", Slot.Head, Line.Resonance, new Txt("星の髪飾り", "Star Diadem"), Stat.PowerPct, 5),
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
            new BaseDef("armor.star_mantle", Slot.Armor, Line.Resonance, new Txt("星屑の肩掛け", "Stardust Mantle"), Stat.PowerPct, 5),
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
            new BaseDef("charm.war_horn", Slot.Charm, Line.Offense, new Txt("戦の角笛", "War Horn"), Stat.AttackPct, 5),
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
                Power.Convergence, 150, Power.Frost, 30),
            new UniqueDef("unique.afterimage_cloak", "armor.flowing_cloak", new Txt("残像の外套", "Afterimage Cloak"),
                new Txt("避けた先に、もう次の記憶が待っている。", "Where you dodge to, your next memory is already waiting."),
                Power.EchoingDodge, 15, Power.Tailwind, 20),
            new UniqueDef("unique.dawnbreaker", "weapon.calming_staff", new Txt("暁を呼ぶ杖", "Dawncaller"),
                new Txt("三つ重なった光は、決して外れない。", "Light stacked thrice never misses."),
                Power.Radiance, 40, Power.UltimateSurge, 20),
            // 本体の旅人ごとの専用固有品（キットに効く固有効果の組み合わせ。誰でも装備できる）
            new UniqueDef("unique.sig.vesper", "weapon.blaze_greatsword", new Txt("審問官の誓剣", "Inquisitor's Oathblade"),
                new Txt("四度目の祈りは、必ず届く。（Vesper）", "The fourth prayer always lands. (Vesper)"),
                Power.Blaze, 70, Power.Retaliation, 20),
            new UniqueDef("unique.sig.lacerta", "weapon.longspike_bow", new Txt("サラマンダーの銃身", "Salamander Barrel"),
                new Txt("火薬は多いほど良い。（Lacerta）", "More powder is always better. (Lacerta)"),
                Power.Ember, 40, Power.Blaze, 60),
            new UniqueDef("unique.sig.cetus", "armor.guardian_plate", new Txt("深海の外殻", "Abyssal Carapace"),
                new Txt("凍てつく水底に、揺らがぬ殻がある。（Cetus）", "On the frozen seabed rests an unshaken shell. (Cetus)"),
                Power.Frost, 35, Power.Barrier, 8),
            new UniqueDef("unique.sig.yubar", "charm.old_clock", new Txt("星屑の写本", "Stardust Codex"),
                new Txt("記憶を使うたび、星が集まる。（Yubar）", "Every memory used gathers another star. (Yubar)"),
                Power.UltimateSurge, 25, Power.Radiance, 30),
            new UniqueDef("unique.sig.husk", "weapon.twin_fang", new Txt("空殻の牙", "Hollow Fang"),
                new Txt("影の中では、急所しか見えない。（空殻）", "In the shadows, only weak points are visible. (Husk)"),
                Power.Umbra, 40, Power.Executioner, 40),
            new UniqueDef("unique.sig.mist", "weapon.chain_sword", new Txt("霧払いの太刀", "Mistcutter"),
                new Txt("避けた一閃が、次の一閃を呼ぶ。（Mist）", "Each evaded strike calls the next. (Mist)"),
                Power.EchoingDodge, 15, Power.Momentum, 4),
            new UniqueDef("unique.sig.nachia", "charm.resonance_amulet", new Txt("絆の鈴", "Bell of Bonds"),
                new Txt("呼べば応える。光が、仲間が。（Nachia）", "Call, and they answer: the light, and your friends. (Nachia)"),
                Power.Resonance, 10, Power.Radiance, 30),
            new UniqueDef("unique.sig.aurena", "armor.lampkeeper_mantle", new Txt("黄金の聖杯", "Golden Chalice"),
                new Txt("注いだ血は、光となって還る。（Aurena）", "The blood you pour returns as light. (Aurena)"),
                Power.SecondWind, 30, Power.Bloodlust, 25),
            new UniqueDef("unique.sig.bismuth", "charm.pulsing_core", new Txt("四冊目の物語", "The Fourth Tale"),
                new Txt("三つの物語が揃えば、四つ目が始まる。（Bismuth）", "When three tales meet, the fourth begins. (Bismuth)"),
                Power.Convergence, 120, Power.Ember, 25),
            new UniqueDef("unique.glacier_lance", "weapon.frost_spear", new Txt("氷河の槍", "Glacier Lance"),
                new Txt("凍りついた敵は、もう逃げられない。", "A frozen foe has nowhere left to run."),
                Power.Frost, 40, Power.Bulwark, 25),
            new UniqueDef("unique.moon_reaper", "weapon.dusk_scythe", new Txt("月喰いの鎌", "Moon Reaper"),
                new Txt("欠けた月の夜にだけ、刃は研がれる。", "Its edge is honed only on waning-moon nights."),
                Power.Umbra, 40, Power.Bloodlust, 18),
            new UniqueDef("unique.lighthouse", "weapon.lantern_rod", new Txt("夜明けの灯台", "Lighthouse of Dawn"),
                new Txt("迷った夢を、光が岸まで導く。", "Its light guides lost dreams back to shore."),
                Power.Radiance, 35, Power.Barrier, 10),
            new UniqueDef("unique.winter_vow", "armor.frost_coat", new Txt("冬の誓い", "Winter's Vow"),
                new Txt("凍えるほど、守る意志は固くなる。", "The colder it gets, the firmer the resolve."),
                Power.Frost, 30, Power.Aegis, 25),
            new UniqueDef("unique.whirling_veil", "armor.dancer_garb", new Txt("渦巻く舞衣", "Whirling Veil"),
                new Txt("止まらない舞は、刃より速い。", "A dance that never stops outpaces any blade."),
                Power.Momentum, 6, Power.EchoingDodge, 12),
            new UniqueDef("unique.astral_mantle", "armor.star_cloak", new Txt("天球の外套", "Astral Mantle"),
                new Txt("星の巡りを読めば、切り札はいつでも手の中に。", "Read the turning stars, and your trump card is always at hand."),
                Power.UltimateSurge, 25, Power.Resonance, 8),
            new UniqueDef("unique.phoenix_locket", "charm.ember_locket", new Txt("不死鳥のロケット", "Phoenix Locket"),
                new Txt("燃え尽きたと思ったときが、始まりだ。", "The moment you think you have burned out is when it begins."),
                Power.Ember, 40, Power.SecondWind, 30),
            new UniqueDef("unique.moonlit_bell", "charm.moon_bell", new Txt("月夜の鈴", "Moonlit Bell"),
                new Txt("鳴るたびに、冷たい雷が走る。", "Each chime sends cold lightning running."),
                Power.Frost, 35, Power.ChainLightning, 50),
            new UniqueDef("unique.iron_wing", "charm.iron_feather", new Txt("鉄翼", "Iron Wing"),
                new Txt("羽ばたくたびに、刃を弾く。", "Every beat of its wings turns a blade aside."),
                Power.Thorns, 30, Power.Bulwark, 30),
            new UniqueDef("unique.storm_caller", "weapon.chain_sword", new Txt("嵐を呼ぶ剣", "Stormcaller"),
                new Txt("振るえば、空が応える。", "Swing it, and the sky answers."),
                Power.ChainLightning, 70, Power.Tailwind, 20),
            new UniqueDef("unique.hungering_dark", "armor.resonant_robe", new Txt("飢える闇", "Hungering Dark"),
                new Txt("闇は、与えた傷の分だけ満たされる。", "The dark is filled by every wound it gives."),
                Power.Umbra, 35, Power.Lifesteal, 8),
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
                Power.Ember, 40, Power.Shatter, 60),
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
                Power.EchoingDodge, 12, Power.UltimateSurge, 20),
            new UniqueDef("unique.lullaby", "weapon.star_harp", new Txt("子守唄の竪琴", "Lullaby Harp"),
                new Txt("敵を眠らせる調べが、仲間を奮い立たせる。", "A melody that lulls foes to sleep rouses friends."),
                Power.Resonance, 10, Power.Barrier, 8),
            new UniqueDef("unique.constellation", "weapon.star_harp", new Txt("星座を奏でる竪琴", "Constellation Harp"),
                new Txt("弦を弾くたび、星が一つ増える。", "Every plucked string adds a star to the sky."),
                Power.Radiance, 40, Power.Convergence, 100),
            new UniqueDef("unique.frost_fang", "weapon.twin_fang", new Txt("霜牙", "Frostfang"),
                new Txt("噛まれた傷は、凍えて塞がらない。", "Its bite freezes and never closes."),
                Power.Frost, 35, Power.Momentum, 4),
            new UniqueDef("unique.eclipse_scythe", "weapon.dusk_scythe", new Txt("日蝕の鎌", "Eclipse Scythe"),
                new Txt("光が消えた一瞬に、すべてを刈る。", "In the instant the light dies, it reaps all."),
                Power.Umbra, 35, Power.Shatter, 60),
            new UniqueDef("unique.siegebreaker", "weapon.shield_maul", new Txt("攻城槌", "Siegebreaker"),
                new Txt("壁を砕く力は、守るためにある。", "The strength to break walls exists to protect."),
                Power.Bulwark, 30, Power.Retaliation, 25),
            new UniqueDef("unique.phoenix_blade", "weapon.blaze_greatsword", new Txt("鳳凰の大剣", "Phoenix Greatsword"),
                new Txt("倒れても、炎は何度でも立ち上がる。", "Even fallen, the flame rises again and again."),
                Power.Blaze, 60, Power.SecondWind, 25),
            new UniqueDef("unique.tidal_sword", "weapon.chain_sword", new Txt("潮流の剣", "Tidal Sword"),
                new Txt("引いては寄せる、終わりのない連撃。", "Strikes that ebb and flow without end."),
                Power.Frost, 30, Power.Tailwind, 20),
            new UniqueDef("unique.sunlit_rod", "weapon.lantern_rod", new Txt("陽光の杖", "Sunlit Rod"),
                new Txt("朝日は、どんな悪夢も照らし出す。", "The morning sun lays every nightmare bare."),
                Power.Radiance, 40, Power.Ember, 25),
            new UniqueDef("unique.mirror_staff", "weapon.calming_staff", new Txt("鏡の杖", "Mirror Staff"),
                new Txt("受けた力を、そのまま映し返す。", "It reflects back every force it receives."),
                Power.Thorns, 35, Power.Aegis, 25),
            new UniqueDef("unique.void_wand", "weapon.dream_wand", new Txt("虚無の杖", "Void Wand"),
                new Txt("何もない場所から、闇があふれ出す。", "Darkness spills from where nothing was."),
                Power.Umbra, 40, Power.Resonance, 8),
            new UniqueDef("unique.stalker_leather", "armor.hunter_leather", new Txt("追跡者の革鎧", "Stalker's Leathers"),
                new Txt("足音は、獲物が倒れるまで消えない。", "Its footsteps fade only when the prey falls."),
                Power.Executioner, 40, Power.Tailwind, 20),
            new UniqueDef("unique.blood_hide", "armor.hunter_leather", new Txt("血染めの毛皮", "Bloodstained Hide"),
                new Txt("浴びた血が、次の狩りを急かす。", "The blood it soaks up urges the next hunt."),
                Power.Bloodlust, 30, Power.Lifesteal, 8),
            new UniqueDef("unique.forgeheart", "armor.ember_plate", new Txt("鍛冶場の心臓", "Forgeheart Plate"),
                new Txt("打たれるほど、熱く固くなる。", "The more it is struck, the hotter and harder it gets."),
                Power.Retaliation, 30, Power.Ember, 30),
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
                Power.EchoingDodge, 15, Power.Umbra, 30),
            new UniqueDef("unique.dew_robe", "armor.mist_robe", new Txt("朝露の法衣", "Morning Dew Robe"),
                new Txt("夜明けの雫が、仲間の傷を洗う。", "Drops of dawn wash your allies' wounds."),
                Power.Resonance, 8, Power.Barrier, 8),
            new UniqueDef("unique.pilgrim_shawl", "armor.prayer_shawl", new Txt("巡礼の肩掛け", "Pilgrim's Shawl"),
                new Txt("歩いた道が長いほど、祈りは深くなる。", "The longer the road walked, the deeper the prayer."),
                Power.SecondWind, 25, Power.UltimateSurge, 20),
            new UniqueDef("unique.saint_shawl", "armor.prayer_shawl", new Txt("聖女の肩掛け", "Saint's Shawl"),
                new Txt("祈りは、仲間を包む光になる。", "Prayer becomes a light that wraps your allies."),
                Power.Barrier, 10, Power.Radiance, 30),
            new UniqueDef("unique.thundercloud", "armor.flowing_cloak", new Txt("雷雲の外套", "Thundercloud Cloak"),
                new Txt("風に乗って、雷は群れを渡る。", "Riding the wind, lightning leaps through the pack."),
                Power.ChainLightning, 60, Power.Tailwind, 20),
            new UniqueDef("unique.reprisal", "armor.counter_gauntlets", new Txt("報復の籠手", "Gauntlets of Reprisal"),
                new Txt("殴られた分だけ、殴り返す。", "It returns every blow it takes."),
                Power.Retaliation, 30, Power.Momentum, 4),
            new UniqueDef("unique.nightwatch", "armor.lampkeeper_mantle", new Txt("夜番の外衣", "Nightwatch Mantle"),
                new Txt("灯が消えるまで、見張りは終わらない。", "The watch ends only when the lamp goes out."),
                Power.Barrier, 10, Power.Umbra, 30),
            new UniqueDef("unique.thorn_queen", "armor.thorn_mail", new Txt("茨の女王", "Thorn Queen"),
                new Txt("近づく者すべてに、棘の口づけを。", "A thorny kiss for all who come near."),
                Power.Thorns, 40, Power.Bloodlust, 25),
            new UniqueDef("unique.flame_dancer", "armor.dancer_garb", new Txt("炎の舞衣", "Flame Dancer's Garb"),
                new Txt("舞うたびに、火の粉が散る。", "Sparks scatter with every step of the dance."),
                Power.Ember, 35, Power.Momentum, 5),
            new UniqueDef("unique.starfall_cloak", "armor.star_cloak", new Txt("星降る外套", "Starfall Cloak"),
                new Txt("夜空ごと、敵の上に降らせる。", "It brings the whole night sky down on foes."),
                Power.Convergence, 120, Power.Radiance, 30),
            new UniqueDef("unique.blizzard_coat", "armor.frost_coat", new Txt("吹雪の上衣", "Blizzard Coat"),
                new Txt("吹雪の中では、すべてが凍りつく。", "In the blizzard, everything freezes."),
                Power.Frost, 40, Power.Shatter, 50),
            new UniqueDef("unique.wolf_fang", "charm.fang_necklace", new Txt("狼王の牙", "Wolf King's Fang"),
                new Txt("群れの長は、最後まで噛みつく。", "The pack leader bites to the very end."),
                Power.Bloodlust, 30, Power.Executioner, 40),
            new UniqueDef("unique.viper_fang", "charm.fang_necklace", new Txt("毒蛇の牙", "Viper's Fang"),
                new Txt("小さな傷が、やがて命を奪う。", "A tiny wound in time takes a life."),
                Power.Umbra, 35, Power.OpeningStrike, 40),
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
                Power.UltimateSurge, 25, Power.EchoingDodge, 10),
            new UniqueDef("unique.prism_lens", "charm.dream_lens", new Txt("虹の水晶", "Prism Lens"),
                new Txt("一つの光が、四つの夢に分かれる。", "One light splits into four dreams."),
                Power.Convergence, 120, Power.Radiance, 25),
            new UniqueDef("unique.jester_mask", "charm.shadow_mask", new Txt("道化の仮面", "Jester's Mask"),
                new Txt("笑わせているうちに、背後を取る。", "While they laugh, it takes their back."),
                Power.EchoingDodge, 15, Power.Executioner, 40),
            new UniqueDef("unique.nightmare_mask", "charm.shadow_mask", new Txt("悪夢の仮面", "Nightmare Mask"),
                new Txt("仮面の下には、もっと深い闇がある。", "Beneath the mask lies a deeper dark."),
                Power.Umbra, 40, Power.Bloodlust, 25),
            new UniqueDef("unique.sun_locket", "charm.ember_locket", new Txt("太陽のロケット", "Sun Locket"),
                new Txt("小さな太陽が、胸の中で燃えている。", "A tiny sun burns within your chest."),
                Power.Ember, 40, Power.Radiance, 30),
            new UniqueDef("unique.tidebell", "charm.moon_bell", new Txt("潮騒の鈴", "Tidebell"),
                new Txt("波の音が、疲れを洗い流す。", "The sound of waves washes fatigue away."),
                Power.Frost, 30, Power.Resonance, 8),
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
                Power.Radiance, 35, Power.Shatter, 55),
            new UniqueDef("unique.bloodaxe", "weapon.war_axe", new Txt("血斧", "Bloodaxe"),
                new Txt("血を浴びるほど、刃は鋭くなる。", "The more blood it bathes in, the sharper its edge."),
                Power.Lifesteal, 10, Power.Momentum, 5),
            new UniqueDef("unique.icewall_lance", "weapon.tower_lance", new Txt("氷壁の槍", "Icewall Lance"),
                new Txt("凍った壁の向こうへ、敵は届かない。", "No foe reaches past the frozen wall."),
                Power.Frost, 35, Power.Aegis, 20),
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
                Power.Umbra, 35, Power.Frost, 30),
            new UniqueDef("unique.soul_lantern", "weapon.lantern_rod", new Txt("魂の灯籠", "Soul Lantern"),
                new Txt("消えた灯が、持ち主の命になる。", "Every light that goes out becomes the bearer's life."),
                Power.SoulSiphon, 15, Power.Radiance, 30),
            new UniqueDef("unique.cyclone_cloak", "armor.flowing_cloak", new Txt("竜巻の外套", "Cyclone Cloak"),
                new Txt("身をかわすたび、嵐が生まれる。", "Every sidestep gives birth to a storm."),
                Power.Whirlwind, 70, Power.EchoingDodge, 10),
            new UniqueDef("unique.brawler_gauntlets", "armor.counter_gauntlets", new Txt("乱闘者の籠手", "Brawler's Gauntlets"),
                new Txt("囲まれてからが、本番だ。", "The real fight starts once you are surrounded."),
                Power.Frenzy, 3, Power.Bulwark, 25),
            new UniqueDef("unique.first_light", "weapon.longspike_bow", new Txt("一番星の弓", "Bow of the First Star"),
                new Txt("最初の一矢が、すべてを決める。", "The first arrow decides everything."),
                Power.OpeningStrike, 50, Power.Radiance, 25),
            new UniqueDef("unique.starward_mantle", "armor.star_cloak", new Txt("星守りの外套", "Starward Mantle"),
                new Txt("切り札を切るとき、星が身を守る。", "When you play your trump card, the stars guard you."),
                Power.StarShield, 20, Power.UltimateSurge, 20),
            new UniqueDef("unique.hare_boots", "armor.dancer_garb", new Txt("白兎の舞衣", "White Hare Garb"),
                new Txt("跳ねるように逃げ、跳ねるように戻る。", "Bound away, bound back."),
                Power.Sprint, 25, Power.EchoingDodge, 12),
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
                Power.Ember, 32, Power.Blaze, 60),
            new UniqueDef("unique.moonless_hood", "head.moon_hood", new Txt("月なき夜の頭巾", "Hood of the Moonless Night"),
                new Txt("月のない夜は、狩る者の味方をする。", "A moonless night sides with the hunter."),
                Power.Umbra, 32, Power.Executioner, 40),
            new UniqueDef("unique.wardens_oath", "head.warden_visor", new Txt("番人の誓い", "Warden's Oath"),
                new Txt("ここを通りたければ、まず私を倒せ。", "To pass, you must first get through me."),
                Power.Bulwark, 28, Power.Vigor, 14),
            new UniqueDef("unique.mist_bride", "head.mist_veil", new Txt("霧の花嫁", "Bride of the Mist"),
                new Txt("触れようとした手は、いつも霧をつかむ。", "Every hand that reaches for her grasps only mist."),
                Power.Sprint, 22, Power.EchoingDodge, 10),
            new UniqueDef("unique.raging_horns", "head.horned_helm", new Txt("怒れる双角", "Raging Horns"),
                new Txt("囲まれるほど、角は熱くなる。", "The more foes close in, the hotter the horns burn."),
                Power.Frenzy, 3, Power.Bloodlust, 18),
            new UniqueDef("unique.first_dawn", "head.healer_band", new Txt("最初の夜明け", "The First Dawn"),
                new Txt("長い夢にも、朝は来る。", "Even the longest dream has a morning."),
                Power.Barrier, 9, Power.Radiance, 30),
            new UniqueDef("unique.headsman_grip", "hands.claw_gauntlets", new Txt("断頭人の握り", "Headsman's Grip"),
                new Txt("弱った獲物を、この爪は逃さない。", "These claws never let a weakened prey slip away."),
                Power.Executioner, 45, Power.Momentum, 5),
            new UniqueDef("unique.storm_fingers", "hands.spell_gloves", new Txt("嵐を呼ぶ指", "Stormcalling Fingers"),
                new Txt("指を鳴らせば、空が応える。", "Snap your fingers, and the sky answers."),
                Power.ChainLightning, 48, Power.Overload, 16),
            new UniqueDef("unique.frostbite", "hands.frost_mitts", new Txt("凍傷", "Frostbite"),
                new Txt("凍らせて、砕く。それだけのこと。", "Freeze it, then break it. Nothing more."),
                Power.Frost, 34, Power.Shatter, 55),
            new UniqueDef("unique.first_arrow", "hands.archer_bracers", new Txt("一番矢", "The First Arrow"),
                new Txt("戦いは、最初の一射で決まる。", "A battle is decided by the first shot."),
                Power.OpeningStrike, 48, Power.Executioner, 30),
            new UniqueDef("unique.dawnwrap", "hands.radiant_wraps", new Txt("夜明けの手巻き", "Dawnwrap"),
                new Txt("光を巻いた拳は、四つの色を呼び寄せる。", "A fist wrapped in light calls all four colors."),
                Power.Radiance, 34, Power.Convergence, 110),
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
                Power.Whirlwind, 75, Power.EchoingDodge, 10),
            new UniqueDef("unique.windchaser", "feet.wind_sandals", new Txt("風を追う者", "Windchaser"),
                new Txt("風より先に着けば、風は追い風になる。", "Arrive before the wind, and it becomes your tailwind."),
                Power.Sprint, 24, Power.Tailwind, 22),
            new UniqueDef("unique.rooted_oath", "feet.rooted_boots", new Txt("根付く誓い", "Rooted Oath"),
                new Txt("一歩も退かない。根は、退き方を知らない。", "Not one step back. Roots do not know how."),
                Power.Bulwark, 28, Power.Thorns, 28),
            new UniqueDef("unique.firewalker", "feet.ember_treads", new Txt("火渡り", "Firewalker"),
                new Txt("燃える道を選ぶ者にだけ、道は開ける。", "The path opens only for those who choose to walk through fire."),
                Power.Ember, 30, Power.Whirlwind, 60),
            new UniqueDef("unique.silent_step", "feet.stalker_boots", new Txt("音なき足", "Silent Step"),
                new Txt("気づいたときには、もう背後にいる。", "By the time they notice, you are already behind them."),
                Power.OpeningStrike, 45, Power.Umbra, 28),
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
                Power.Radiance, 32, Power.Resonance, 8),
            new UniqueDef("unique.far_sight", "head.scout_goggles", new Txt("千里眼", "Far Sight"),
                new Txt("見えているなら、もう当たっている。", "If you can see it, you have already hit it."),
                Power.OpeningStrike, 45, Power.Executioner, 35),
            new UniqueDef("unique.frozen_thought", "head.frost_helm", new Txt("凍てつく思考", "Frozen Thought"),
                new Txt("冷えた頭は、決して慌てない。", "A cold head never panics."),
                Power.Frost, 32, Power.Aegis, 16),
            new UniqueDef("unique.ash_hands", "hands.ember_gauntlets", new Txt("灰の手", "Hands of Ash"),
                new Txt("触れたものは、すべて灰になる。", "Everything it touches turns to ash."),
                Power.Blaze, 65, Power.Ember, 28),
            new UniqueDef("unique.shadow_stitch", "hands.shadow_gloves", new Txt("影縫い", "Shadowstitch"),
                new Txt("影を縫い止めれば、本体も動けない。", "Pin the shadow, and its owner cannot move."),
                Power.Umbra, 32, Power.Frenzy, 2),
            new UniqueDef("unique.mending_touch", "hands.healer_hands", new Txt("繕いの手", "Mending Touch"),
                new Txt("傷は、触れるそばから閉じていく。", "Wounds close as soon as it touches them."),
                Power.Lifesteal, 9, Power.SecondWind, 26),
            new UniqueDef("unique.duelists_promise", "hands.duelist_gloves", new Txt("決闘者の約束", "Duelist's Promise"),
                new Txt("一対一なら、負けたことはない。", "One on one, it has never lost."),
                Power.Momentum, 5, Power.OpeningStrike, 40),
            new UniqueDef("unique.icewalker", "feet.frost_boots", new Txt("氷を歩む者", "Icewalker"),
                new Txt("氷の上こそ、もっとも速く走れる。", "On ice, you run fastest of all."),
                Power.Frost, 30, Power.Sprint, 20),
            new UniqueDef("unique.shadow_dancer", "feet.shadow_slippers", new Txt("影踊り", "Shadow Dancer"),
                new Txt("影は、踊り手の足元から離れない。", "The shadow never leaves the dancer's feet."),
                Power.Umbra, 30, Power.EchoingDodge, 10),
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
                Power.Umbra, 30, Power.SoulSiphon, 18),
            new UniqueDef("unique.tidepool_crown", "head.coral_crown", new Txt("潮だまりの冠", "Tidepool Crown"),
                new Txt("冷たい潮が、傷口を静かに閉ざす。", "The cold tide quietly closes every wound."),
                Power.Frost, 32, Power.Barrier, 9),
            new UniqueDef("unique.oathbound_helm", "head.knight_helm", new Txt("誓いの大兜", "Oathbound Greathelm"),
                new Txt("退かぬ者の前で、刃は鈍る。", "Blades dull before one who never retreats."),
                Power.Bulwark, 30, Power.Aegis, 18),
            new UniqueDef("unique.ashen_hood", "head.ash_hood", new Txt("灰燼の頭巾", "Hood of Ashes"),
                new Txt("灰の中の火種が、群れごと弾け飛ぶ。", "Embers in the ash burst across the whole pack."),
                Power.Ember, 32, Power.Shatter, 55),
            new UniqueDef("unique.closed_eye", "head.eye_patch", new Txt("閉ざした眼", "The Closed Eye"),
                new Txt("見えぬ目が、最初の一撃を見極める。", "The unseen eye judges the very first strike."),
                Power.OpeningStrike, 48, Power.Vigor, 14),
            new UniqueDef("unique.spring_wreath", "head.leaf_wreath", new Txt("春待つ花冠", "Wreath of Waiting Spring"),
                new Txt("傷んでも、若葉はまた芽吹く。", "Even when bruised, young leaves sprout again."),
                Power.SecondWind, 28, Power.Lifesteal, 9),
            new UniqueDef("unique.hollow_will", "head.void_helm", new Txt("虚ろな意志", "Hollow Will"),
                new Txt("空っぽの器に、闇が魔力を満たす。", "Darkness fills the empty vessel with power."),
                Power.Umbra, 33, Power.Overload, 18),
            new UniqueDef("unique.noon_mask", "head.sun_mask", new Txt("真昼の面", "Mask of High Noon"),
                new Txt("大技を放つ瞬間、日輪が輝く。", "The sun blazes at the moment you unleash your Ultimate."),
                Power.Radiance, 32, Power.UltimateSurge, 24),
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
                Power.Overload, 18, Power.EchoingDodge, 11),
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
                Power.Ember, 33, Power.Lifesteal, 9),
            new UniqueDef("unique.winter_moon_hood", "head.moon_hood", new Txt("寒月の頭巾", "Hood of the Cold Moon"),
                new Txt("冷たい月の下、影も凍りつく。", "Beneath the cold moon, even shadows freeze."),
                Power.Frost, 32, Power.Umbra, 30),
            new UniqueDef("unique.lifeline_band", "head.healer_band", new Txt("命綱の鉢巻", "Lifeline Headband"),
                new Txt("倒れかけても、命綱が引き戻す。", "Even as you fall, the lifeline pulls you back."),
                Power.SecondWind, 28, Power.SoulSiphon, 18),
            new UniqueDef("unique.bleeding_crown", "head.thorn_circlet", new Txt("流血の王冠", "Crown of Bleeding"),
                new Txt("血を流すほど、茨は鋭く食い込む。", "The more you bleed, the deeper the thorns bite."),
                Power.Thorns, 32, Power.Bloodlust, 19),
            new UniqueDef("unique.glacier_mind", "head.frost_helm", new Txt("氷の叡智", "Glacial Wisdom"),
                new Txt("冷えた頭で温存し、一気に解き放つ。", "Keep a cool head, then release it all at once."),
                Power.Frost, 33, Power.UltimateSurge, 23),
            new UniqueDef("unique.guardian_halo", "head.radiant_halo", new Txt("守りの後光", "Halo of Guarding"),
                new Txt("光は傷ついた者を、もう一度立たせる。", "Light lifts the wounded back to their feet."),
                Power.Radiance, 33, Power.SecondWind, 28),
            new UniqueDef("unique.lightning_scope", "head.scout_goggles", new Txt("稲光の遠眼鏡", "Lightning Spyglass"),
                new Txt("遠い獲物へ、雷が先に届く。", "Lightning reaches distant prey first."),
                Power.ChainLightning, 48, Power.OpeningStrike, 45),
            new UniqueDef("unique.prism_diadem", "head.star_diadem", new Txt("虹彩の髪飾り", "Iridescent Diadem"),
                new Txt("四つの色が重なる時、星が爆ぜる。", "When four colors overlap, a star bursts."),
                Power.Convergence, 130, Power.UltimateSurge, 24),
            new UniqueDef("unique.void_fragment", "head.void_helm", new Txt("虚空の砕片", "Shard of the Void"),
                new Txt("倒れた敵の闇が、周囲を砕く。", "The darkness of the fallen shatters all around."),
                Power.Shatter, 55, Power.Umbra, 33),
            new UniqueDef("unique.sunfire_mask", "head.sun_mask", new Txt("灼陽の面", "Mask of the Scorching Sun"),
                new Txt("光と炎が、四拍ごとに降り注ぐ。", "Light and fire rain down every fourth beat."),
                Power.Blaze, 65, Power.Radiance, 33),
            new UniqueDef("unique.briar_embrace", "hands.thorn_wraps", new Txt("茨の抱擁", "Briar Embrace"),
                new Txt("傷つくほどに、この抱擁は強く締まる。", "The more it wounds you, the tighter it clings."),
                Power.Thorns, 32, Power.Bloodlust, 22),
            new UniqueDef("unique.apothecary_touch", "hands.alchemist_gloves", new Txt("調合の指先", "Apothecary's Touch"),
                new Txt("火と氷を混ぜれば、四つ目の元素が目覚める。", "Mix fire with frost, and the other elements wake."),
                Power.Convergence, 130, Power.Frost, 32),
            new UniqueDef("unique.marrow_breaker", "hands.bone_knuckles", new Txt("砕骨の拳", "Marrow Breaker"),
                new Txt("砕いた骨の数だけ、命が戻ってくる。", "For every bone you break, some life returns."),
                Power.Shatter, 60, Power.SoulSiphon, 18),
            new UniqueDef("unique.star_weaver", "hands.star_rings", new Txt("星を紡ぐ指", "Star Weaver"),
                new Txt("光を指に絡め、夜空の守りを織り上げる。", "Thread light through your fingers and weave a shield of stars."),
                Power.StarShield, 18, Power.Radiance, 32),
            new UniqueDef("unique.unyielding_vow", "hands.oath_gauntlets", new Txt("不退の誓拳", "Unyielding Vow"),
                new Txt("一歩も退かぬと誓えば、痛みが力に変わる。", "Vow never to retreat, and pain becomes power."),
                Power.Barrier, 10, Power.Retaliation, 26),
            new UniqueDef("unique.maelstrom_grasp", "hands.tide_gloves", new Txt("渦潮の手", "Maelstrom Grasp"),
                new Txt("身を翻すたび、冷たい渦が敵を呑み込む。", "Every roll draws foes into a freezing whirlpool."),
                Power.Frost, 34, Power.Whirlwind, 72),
            new UniqueDef("unique.hellfire_fist", "hands.flame_grips", new Txt("業火拳", "Hellfire Fist"),
                new Txt("囲まれるほどに、拳の炎は荒れ狂う。", "The more foes surround you, the wilder the flames rage."),
                Power.Blaze, 66, Power.Frenzy, 3),
            new UniqueDef("unique.void_render", "hands.void_claws", new Txt("虚空を裂く爪", "Void Render"),
                new Txt("闇を染み込ませ、とどめの一裂きを狙え。", "Soak them in dark, then go for the final tear."),
                Power.Umbra, 33, Power.Executioner, 44),
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
                Power.Ember, 32, Power.Overload, 19),
            new UniqueDef("unique.famished_claws", "hands.claw_gauntlets", new Txt("喰らいつく爪", "Famished Claws"),
                new Txt("弱った獲物ほど、爪は深く食い込み血を啜る。", "The weaker the prey, the deeper the claws bite and drink."),
                Power.Lifesteal, 9, Power.Executioner, 44),
            new UniqueDef("unique.frozen_night", "hands.frost_mitts", new Txt("凍夜の指", "Frozen Night"),
                new Txt("冷気と闇を重ね、敵を静かな夜に閉じ込める。", "Layer cold upon dark and trap foes in a silent night."),
                Power.Frost, 35, Power.Umbra, 32),
            new UniqueDef("unique.light_binder", "hands.radiant_wraps", new Txt("光を束ねる手", "Light Binder"),
                new Txt("集めた光は、切り札の一撃で解き放たれる。", "The gathered light is released in a single trump strike."),
                Power.Radiance, 34, Power.UltimateSurge, 24),
            new UniqueDef("unique.peak_form", "hands.vigor_grips", new Txt("万全の一撃", "Peak Form"),
                new Txt("体も敵も万全のうちに、最初の一撃を叩き込め。", "Strike first while both you and your foe are unscathed."),
                Power.Vigor, 16, Power.OpeningStrike, 48),
            new UniqueDef("unique.flash_sleight", "hands.quick_fingers", new Txt("稲光の早業", "Flash Sleight"),
                new Txt("身をかわすたびに技が戻り、雷が跳ねる。", "Each dodge cools your skills and sends lightning leaping."),
                Power.EchoingDodge, 12, Power.ChainLightning, 52),
            new UniqueDef("unique.melee_dancer", "hands.quick_fingers", new Txt("乱戦の舞手", "Melee Dancer"),
                new Txt("敵の輪の中でこそ、手は軽やかに踊る。", "Your hands dance lightest in the middle of a ring of foes."),
                Power.Frenzy, 3, Power.Whirlwind, 70),
            new UniqueDef("unique.hounds_reach", "hands.reach_bracers", new Txt("追い込みの腕", "Hound's Reach"),
                new Txt("遠くから仕留め、勢いのまま次の獲物へ走る。", "Finish them from afar and chase down the next target."),
                Power.Executioner, 44, Power.Tailwind, 22),
            new UniqueDef("unique.ember_soul", "hands.ember_gauntlets", new Txt("熾火の魂", "Ember Soul"),
                new Txt("火種を撒き、燃え尽きた命を己の糧とする。", "Sow the embers and feed on the lives they burn out."),
                Power.Ember, 33, Power.SoulSiphon, 18),
            new UniqueDef("unique.shadow_sprinter", "hands.shadow_gloves", new Txt("影走り", "Shadow Sprinter"),
                new Txt("倒すたびに影が濃くなり、手の動きは増す。", "With every kill the shadow deepens and your hands quicken."),
                Power.Umbra, 34, Power.Momentum, 5),
            new UniqueDef("unique.merciful_light", "hands.healer_hands", new Txt("慈光の手", "Merciful Light"),
                new Txt("光を宿した手が、倒れかけた命を引き戻す。", "Hands bright with light pull a failing life back."),
                Power.Radiance, 32, Power.SecondWind, 28),
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
                Power.Frost, 38, Power.Whirlwind, 80),
            new UniqueDef("unique.ebb_and_flow", "feet.tide_sandals", new Txt("満ち引きの歩", "Ebb and Flow"),
                new Txt("冷たい波は、討つたびに背を押す。", "The cold tide pushes you on with each kill."),
                Power.Frost, 36, Power.Tailwind, 22),
            new UniqueDef("unique.citadel_stride", "feet.knight_sabatons", new Txt("城門の騎士", "Citadel Knight"),
                new Txt("敵の波を受け止め、門の前で崩さない。", "Hold the gate and let the wave break on you."),
                Power.Bulwark, 30, Power.Aegis, 22),
            new UniqueDef("unique.vengeful_knight", "feet.knight_sabatons", new Txt("復讐の騎士", "Vengeful Knight"),
                new Txt("傷を負うたび、剣は怒りで重くなる。", "Every wound makes the blade heavier with wrath."),
                Power.Retaliation, 28, Power.Vigor, 16),
            new UniqueDef("unique.first_light_steps", "feet.dawn_steps", new Txt("曙光を踏む", "Dawnstepper"),
                new Txt("駆け出す朝、足跡が光を残す。", "At daybreak your footprints leave light behind."),
                Power.Radiance, 38, Power.Sprint, 22),
            new UniqueDef("unique.morning_reprieve", "feet.dawn_steps", new Txt("朝凪の舞", "Morning Calm"),
                new Txt("光を宿して舞えば、技はすぐ戻る。", "Dance in the light and your skills return swiftly."),
                Power.Radiance, 35, Power.EchoingDodge, 12),
            new UniqueDef("unique.cinder_march", "feet.ash_boots", new Txt("燃え殻の行進", "Cinder March"),
                new Txt("踏み荒らした跡に、火種が弾ける。", "Sparks burst along the path you trample."),
                Power.Ember, 38, Power.Shatter, 65),
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
                Power.Sprint, 25, Power.Umbra, 35),
            new UniqueDef("unique.drifting_mind", "feet.mist_shoes", new Txt("揺蕩う思考", "Drifting Thought"),
                new Txt("避けては唱え、唱えてはまた避ける。", "Dodge, cast, then dodge again."),
                Power.EchoingDodge, 12, Power.Overload, 20),
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
                Power.UltimateSurge, 28, Power.EchoingDodge, 12),
            new UniqueDef("unique.night_sky_walk", "feet.star_slippers", new Txt("夜空の散歩", "Night Sky Walk"),
                new Txt("星の盾を纏い、静かに術を重ねる。", "Wrapped in a star shield, stack spell upon spell."),
                Power.StarShield, 22, Power.Overload, 20),
            new UniqueDef("unique.shadowed_hunt", "feet.stalker_boots", new Txt("闇の足音", "Footfall of Dark"),
                new Txt("足音を消せば、とどめの隙が見える。", "Silence your steps and the killing opening appears."),
                Power.Executioner, 48, Power.EchoingDodge, 12),
            new UniqueDef("unique.gale_spinner", "feet.wind_sandals", new Txt("風車の足", "Gale Spinner"),
                new Txt("回って跳べば、旋風が道を拓く。", "Spin and leap, and a whirlwind clears the way."),
                Power.Whirlwind, 80, Power.Sprint, 26),
            new UniqueDef("unique.dusk_reaper_sickle", "weapon.moon_sickle", new Txt("宵闇の刈り手", "Dusk Reaper"),
                new Txt("影を刈るほど、刃は冴え渡る。", "The more shadows it reaps, the keener it grows."),
                Power.Umbra, 40, Power.SoulSiphon, 20),
            new UniqueDef("unique.cinder_serpent", "weapon.ember_whip", new Txt("火蛇の鞭", "Cinder Serpent"),
                new Txt("先手の一打で、火蛇が牙を剥く。", "The first lash is when the serpent bites."),
                Power.Ember, 40, Power.OpeningStrike, 55),
            new UniqueDef("unique.whiteridge_lance", "weapon.glacier_spear", new Txt("白嶺の槍", "Whiteridge Lance"),
                new Txt("凍らせた敵を、穂先で砕け。", "Freeze them, then shatter them on the tip."),
                Power.Frost, 40, Power.Shatter, 65),
            new UniqueDef("unique.first_daybreak_scepter", "weapon.dawn_scepter", new Txt("曙光の王笏", "Daybreak Scepter"),
                new Txt("傷のない身に、朝日は力を貸す。", "Morning light favors the unscathed."),
                Power.Radiance, 40, Power.Vigor, 18),
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
                Power.Ember, 38, Power.Frenzy, 3),
            new UniqueDef("unique.rimeveil_robe", "armor.frost_robe", new Txt("霜華の法衣", "Rimeveil Robe"),
                new Txt("舞うたびに、霜の花が咲く。", "Frost blooms with every dodge."),
                Power.Frost, 38, Power.Whirlwind, 75),
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
                Power.Umbra, 38, Power.Retaliation, 28),
            new UniqueDef("unique.solar_aegis_plate", "armor.sun_plate", new Txt("日輪の護り", "Solar Aegis"),
                new Txt("触れる者を、光の棘が拒む。", "Thorns of light turn away all who touch it."),
                Power.Radiance, 38, Power.Thorns, 32),
            new UniqueDef("unique.heartwood_bark", "armor.bark_mail", new Txt("芯木の鎧", "Heartwood Mail"),
                new Txt("傷は、年輪のように癒えていく。", "Wounds heal like rings in a tree."),
                Power.Vigor, 17, Power.SecondWind, 32),
            new UniqueDef("unique.windfarer_coat", "armor.traveler_coat", new Txt("風渡りの旅衣", "Windfarer Coat"),
                new Txt("足を止めない者に、道は開く。", "The road opens for those who never stop."),
                Power.Sprint, 27, Power.Tailwind, 22),
            new UniqueDef("unique.hearth_ember_stone", "charm.hearthstone", new Txt("囲炉裏の熾", "Hearth Ember"),
                new Txt("火を絶やさぬ者は、魔術を灯し続ける。", "Keep the fire alive and your magic burns on."),
                Power.Ember, 38, Power.Overload, 22),
            new UniqueDef("unique.rimeheart_pendant", "charm.frost_pendant", new Txt("霜心の首飾り", "Rimeheart Pendant"),
                new Txt("凍てた心が、大技を星の盾に変える。", "A frozen heart turns your ultimate into a starry shield."),
                Power.Frost, 38, Power.StarShield, 22),
            new UniqueDef("unique.noon_brooch", "charm.sun_brooch", new Txt("真昼のブローチ", "Noon Brooch"),
                new Txt("陽が高いうちは、倒れはしない。", "While the sun is high, you will not fall."),
                Power.Radiance, 38, Power.SecondWind, 32),
            new UniqueDef("unique.umbral_band", "charm.shadow_ring", new Txt("影絡みの指輪", "Umbral Band"),
                new Txt("闇をまとい、回避と共に渦を巻け。", "Cloak yourself in dark and spin out of every dodge."),
                Power.Umbra, 38, Power.Whirlwind, 80),
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
                Power.Sprint, 27, Power.EchoingDodge, 12),
            new UniqueDef("unique.rally_horn", "charm.war_horn", new Txt("決起の角笛", "Rally Horn"),
                new Txt("群れの中で吹けば、奥義が燃え上がる。", "Blow it amid the swarm and your ultimate roars."),
                Power.Frenzy, 3, Power.UltimateSurge, 28),
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
        };

        public static readonly IReadOnlyList<SetDef> Sets = new[]
        {
            new SetDef
            {
                Id = "set.tide", Name = new Txt("潮鳴りの装い", "Tidecaller's Regalia"),
                TwoPiece = new[] { new StatLine(Stat.ColdAmp, 10), new StatLine(Stat.MoveSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Frost, 30), new PowerLine(Power.EchoingDodge, 10) },
            },
            new SetDef
            {
                Id = "set.lamp", Name = new Txt("灯守の誓い", "Lampkeeper's Oath"),
                TwoPiece = new[] { new StatLine(Stat.LightAmp, 10), new StatLine(Stat.MaxHealthPct, 8) },
                ThreePiece = new[] { new PowerLine(Power.Radiance, 30), new PowerLine(Power.SecondWind, 25) },
            },
            new SetDef
            {
                Id = "set.cinder", Name = new Txt("残火の誓約", "Cinder Covenant"),
                TwoPiece = new[] { new StatLine(Stat.FireAmp, 10), new StatLine(Stat.AttackPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Ember, 30), new PowerLine(Power.Blaze, 50) },
            },
            new SetDef
            {
                Id = "set.dusk", Name = new Txt("黄昏の狩装", "Dusk Hunter's Garb"),
                TwoPiece = new[] { new StatLine(Stat.DarkAmp, 10), new StatLine(Stat.CritChancePct, 4) },
                ThreePiece = new[] { new PowerLine(Power.Umbra, 30), new PowerLine(Power.Executioner, 40) },
            },
            new SetDef
            {
                Id = "set.winter", Name = new Txt("冬枯れの誓約", "Winterbound Oath"),
                TwoPiece = new[] { new StatLine(Stat.ColdAmp, 10), new StatLine(Stat.Armor, 6) },
                ThreePiece = new[] { new PowerLine(Power.Frost, 30), new PowerLine(Power.Bulwark, 30) },
            },
            new SetDef
            {
                Id = "set.starsong", Name = new Txt("星詠みの装束", "Starsinger's Raiment"),
                TwoPiece = new[] { new StatLine(Stat.Haste, 10), new StatLine(Stat.PowerPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.UltimateSurge, 25), new PowerLine(Power.EchoingDodge, 10) },
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
                ThreePiece = new[] { new PowerLine(Power.Ember, 35), new PowerLine(Power.Shatter, 60) },
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
                ThreePiece = new[] { new PowerLine(Power.Resonance, 10), new PowerLine(Power.Radiance, 35) },
            },
            new SetDef
            {
                Id = "set.phantom", Name = new Txt("幻影の一座", "Phantom Troupe"),
                TwoPiece = new[] { new StatLine(Stat.DarkAmp, 10), new StatLine(Stat.MoveSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Umbra, 35), new PowerLine(Power.EchoingDodge, 12) },
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
                new AffixDef(Stat.AttackPct, 4, 8, 14),
                new AffixDef(Stat.PowerPct, 4, 8, 14),
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
                new AffixDef(Stat.PowerPct, 4, 8, 7), // v1.21
                new AffixDef(Stat.AttackPct, 4, 8, 7), // v1.21
                new AffixDef(Stat.ColdAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.DarkAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.FireAmp, 6, 12, 4), // v1.21
            },
            [Slot.Charm] = new[]
            {
                new AffixDef(Stat.Haste, 4, 8, 12),
                new AffixDef(Stat.MoveSpeedPct, 2, 5, 8),
                new AffixDef(Stat.CritChancePct, 2, 4, 10),
                new AffixDef(Stat.AttackPct, 3, 6, 10),
                new AffixDef(Stat.PowerPct, 3, 6, 10),
                new AffixDef(Stat.MaxHealthPct, 3, 6, 10),
                new AffixDef(Stat.HealthRegen, 1, 2, 6),
                new AffixDef(Stat.ColdAmp, 6, 12, 5),
                new AffixDef(Stat.FireAmp, 6, 12, 4),
                new AffixDef(Stat.LightAmp, 6, 12, 4),
                new AffixDef(Stat.DarkAmp, 6, 12, 4),
                new AffixDef(Stat.Tenacity, 6, 12, 6),
                new AffixDef(Stat.CritDamagePct, 5, 10, 8),
            },
            [Slot.Head] = new[]
            {
                new AffixDef(Stat.PowerPct, 4, 8, 14),
                new AffixDef(Stat.Haste, 3, 6, 6),
                new AffixDef(Stat.CritChancePct, 2, 4, 10),
                new AffixDef(Stat.MaxHealthPct, 4, 8, 14),
                new AffixDef(Stat.Armor, 4, 9, 14),
                new AffixDef(Stat.Tenacity, 6, 12, 8),
                new AffixDef(Stat.LightAmp, 6, 12, 5),
                new AffixDef(Stat.DarkAmp, 6, 12, 5),
                new AffixDef(Stat.HealthRegen, 1, 3, 8),
                new AffixDef(Stat.AttackPct, 4, 8, 14),
                new AffixDef(Stat.MoveSpeedPct, 2, 4, 4), // v1.21
                new AffixDef(Stat.ColdAmp, 6, 12, 4), // v1.21
                new AffixDef(Stat.FireAmp, 6, 12, 4), // v1.21
            },
            [Slot.Hands] = new[]
            {
                new AffixDef(Stat.AttackPct, 4, 8, 14),
                new AffixDef(Stat.AttackSpeedPct, 3, 6, 12),
                new AffixDef(Stat.CritChancePct, 2, 4, 10),
                new AffixDef(Stat.CritDamagePct, 6, 12, 10),
                new AffixDef(Stat.PowerPct, 4, 8, 14),
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
                new PowerRange(Power.Ember, 20, 35),
                new PowerRange(Power.Radiance, 20, 35),
                new PowerRange(Power.UltimateSurge, 15, 25),
                new PowerRange(Power.SoulSiphon, 10, 20),
                new PowerRange(Power.Frenzy, 1, 3),
                new PowerRange(Power.OpeningStrike, 25, 50),
                new PowerRange(Power.Vigor, 8, 16),
                new PowerRange(Power.Overload, 10, 20),
            },
            [Slot.Armor] = new[]
            {
                new PowerRange(Power.Retaliation, 15, 25),
                new PowerRange(Power.Bulwark, 15, 30),
                new PowerRange(Power.Thorns, 15, 30),
                new PowerRange(Power.Barrier, 6, 10),
                new PowerRange(Power.Aegis, 10, 20),
                new PowerRange(Power.EchoingDodge, 6, 12),
                new PowerRange(Power.Whirlwind, 40, 80),
                new PowerRange(Power.Frenzy, 1, 3),
                new PowerRange(Power.StarShield, 10, 20),
                new PowerRange(Power.Sprint, 10, 25),
            },
            [Slot.Charm] = new[]
            {
                new PowerRange(Power.Resonance, 5, 9),
                new PowerRange(Power.Tailwind, 15, 25),
                new PowerRange(Power.SecondWind, 20, 30),
                new PowerRange(Power.Shatter, 30, 60),
                new PowerRange(Power.Frost, 20, 35),
                new PowerRange(Power.Umbra, 20, 35),
                new PowerRange(Power.Convergence, 80, 140),
                new PowerRange(Power.SoulSiphon, 10, 20),
                new PowerRange(Power.Whirlwind, 40, 80),
                new PowerRange(Power.StarShield, 10, 20),
                new PowerRange(Power.Sprint, 10, 25),
                new PowerRange(Power.Vigor, 8, 16),
                new PowerRange(Power.Overload, 10, 20),
            },
            [Slot.Head] = new[]
            {
                new PowerRange(Power.Overload, 10, 20),
                new PowerRange(Power.UltimateSurge, 15, 25),
                new PowerRange(Power.StarShield, 10, 20),
                new PowerRange(Power.Resonance, 5, 9),
                new PowerRange(Power.SecondWind, 20, 30),
                new PowerRange(Power.Radiance, 20, 35),
                new PowerRange(Power.Umbra, 20, 35),
                new PowerRange(Power.Barrier, 6, 10),
                new PowerRange(Power.Vigor, 8, 16),
                new PowerRange(Power.Bloodlust, 12, 20),
            },
            [Slot.Hands] = new[]
            {
                new PowerRange(Power.Executioner, 25, 45),
                new PowerRange(Power.Blaze, 40, 70),
                new PowerRange(Power.ChainLightning, 30, 50),
                new PowerRange(Power.Ember, 20, 35),
                new PowerRange(Power.Frost, 20, 35),
                new PowerRange(Power.Frenzy, 1, 3),
                new PowerRange(Power.Lifesteal, 5, 10),
                new PowerRange(Power.OpeningStrike, 25, 50),
                new PowerRange(Power.Shatter, 30, 60),
                new PowerRange(Power.Momentum, 3, 5),
            },
            [Slot.Feet] = new[]
            {
                new PowerRange(Power.Sprint, 10, 25),
                new PowerRange(Power.Tailwind, 15, 25),
                new PowerRange(Power.Whirlwind, 40, 80),
                new PowerRange(Power.EchoingDodge, 6, 12),
                new PowerRange(Power.Momentum, 3, 5),
                new PowerRange(Power.Aegis, 10, 20),
                new PowerRange(Power.Bulwark, 15, 30),
                new PowerRange(Power.Thorns, 15, 30),
                new PowerRange(Power.SoulSiphon, 10, 20),
                new PowerRange(Power.Retaliation, 15, 25),
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
            [Power.Ember] = 80,
            [Power.Frost] = 80,
            [Power.Radiance] = 80,
            [Power.Umbra] = 80,
            [Power.Convergence] = 300,
            [Power.EchoingDodge] = 30,
            [Power.UltimateSurge] = 50,
            [Power.SoulSiphon] = 40,
            [Power.Whirlwind] = 200,
            [Power.Frenzy] = 6,
            [Power.OpeningStrike] = 150,
            [Power.StarShield] = 40,
            [Power.Sprint] = 60,
            [Power.Vigor] = 40,
            [Power.Overload] = 50,
        };

        /// <summary>MOD由来の能力値の合計上限（計画書 第7章の L2 上限 +120% を基準）。</summary>
        private static readonly Dictionary<Stat, int> StatCaps = new Dictionary<Stat, int>
        {
            [Stat.AttackPct] = 120,
            [Stat.PowerPct] = 120,
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
            [Stat.FourthAttackShift] = 2,
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

        /// <summary>アイテムレベルによる倍率（%）。1で100%、40以上で217%。</summary>
        public static int LevelScalePct(int itemLevel)
        {
            int l = Math.Max(1, Math.Min(itemLevel, ItemLevelScalingCap));
            return 100 + 3 * (l - 1);
        }

        /// <summary>強化段階による倍率（%）。+1ごとに+6%。</summary>
        public static int EnhanceScalePct(int enhance) => 100 + 6 * Math.Max(0, Math.Min(enhance, MaxEnhance));

        public static int EnhanceCost(int currentEnhance)
        {
            switch (currentEnhance)
            {
                case 0: return 20;
                case 1: return 35;
                case 2: return 60;
                case 3: return 90;
                case 4: return 130;
                default: return int.MaxValue;
            }
        }

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
                default: return "-";
            }
        }

        public static string FormatPower(Power p, int v)
        {
            string name = PowerName(p);
            switch (p)
            {
                case Power.Momentum: return Loc.T($"【{name}】敵を倒すたびに攻撃速度が{v}%上がる（4秒間、5回まで重なる）", $"[{name}] Each kill grants +{v}% attack speed for 4s (stacks up to 5 times)");
                case Power.Retaliation: return Loc.T($"【{name}】攻撃を受けると、3秒間 攻撃力が{v}%上がる", $"[{name}] After taking a hit, gain +{v}% attack damage for 3s");
                case Power.Bulwark: return Loc.T($"【{name}】周りに敵が3体以上いる間、防御が{v}上がる", $"[{name}] +{v} armor while 3 or more enemies are nearby");
                case Power.Lifesteal: return Loc.T($"【{name}】通常攻撃が当たるたびに、最大HPの{v / 10f:0.#}%を回復する", $"[{name}] Basic attack hits heal you for {v / 10f:0.#}% of max health");
                case Power.Thorns: return Loc.T($"【{name}】受けたダメージの{v}%を相手に跳ね返す", $"[{name}] Reflect {v}% of damage taken back to the attacker");
                case Power.Executioner: return Loc.T($"【{name}】HPが30%未満の敵への通常攻撃に、攻撃力{v}%分のダメージを上乗せする", $"[{name}] Basic attacks on enemies below 30% health deal +{v}% AD as bonus damage");
                case Power.Resonance: return Loc.T($"【{name}】近くに味方がいる間、自分と味方の攻撃力・魔力が{v}%上がる（一人のときは半分）", $"[{name}] While an ally is near, you and your allies gain +{v}% AD/AP (half when solo)");
                case Power.Tailwind: return Loc.T($"【{name}】敵を倒した後の2秒間、移動速度が{v}%上がる", $"[{name}] +{v}% move speed for 2s after a kill");
                case Power.Barrier: return Loc.T($"【{name}】12秒ごとに、最大HPの{v}%分の障壁を張る", $"[{name}] Every 12s, gain a shield worth {v}% of max health");
                case Power.SecondWind: return Loc.T($"【{name}】HPが30%を切ると、最大HPの{v}%を回復する（60秒に1回）", $"[{name}] When you drop below 30% health, heal {v}% of max health (once per 60s)");
                case Power.Blaze: return Loc.T($"【{name}】通常攻撃4回ごとに、攻撃力{v}%分の魔法ダメージを追加する", $"[{name}] Every 4th basic attack deals +{v}% AD as magic damage");
                case Power.ChainLightning: return Loc.T($"【{name}】通常攻撃が当たると25%の確率で、近くの敵2体に攻撃力{v}%分の魔法ダメージを与える", $"[{name}] Basic attack hits have a 25% chance to deal {v}% AD magic damage to 2 nearby enemies");
                case Power.Shatter: return Loc.T($"【{name}】敵を倒すと、周囲4mの敵に攻撃力{v}%分のダメージを与える", $"[{name}] On kill, deal {v}% AD to enemies within 4m");
                case Power.Aegis: return Loc.T($"【{name}】最大HPの20%以上の大きな一撃を受けると、最大HPの{v}%分の障壁を張る（20秒に1回）", $"[{name}] When a single hit deals 20%+ of max health, gain a shield worth {v}% of max health (once per 20s)");
                case Power.Bloodlust: return Loc.T($"【{name}】HPが50%未満の間、攻撃速度が{v}%上がる", $"[{name}] +{v}% attack speed while below 50% health");
                case Power.Ember: return Loc.T($"【{name}】通常攻撃が当たると{v}%の確率で、敵に火を付ける", $"[{name}] Basic attack hits have a {v}% chance to apply Fire");
                case Power.Frost: return Loc.T($"【{name}】通常攻撃が当たると{v}%の確率で、敵を冷気で冷やす", $"[{name}] Basic attack hits have a {v}% chance to apply Cold");
                case Power.Radiance: return Loc.T($"【{name}】通常攻撃が当たると{v}%の確率で、敵に光を付ける（光が3つ重なると光のダメージは必ず会心）", $"[{name}] Basic attack hits have a {v}% chance to apply Light (at 3 stacks, light damage always crits)");
                case Power.Umbra: return Loc.T($"【{name}】通常攻撃が当たると{v}%の確率で、敵に闇を付ける", $"[{name}] Basic attack hits have a {v}% chance to apply Dark");
                case Power.Convergence: return Loc.T($"【{name}】敵に火・冷気・光・闇がそろった瞬間、攻撃力{v}%分の爆発を起こす（同じ敵には6秒に1回）", $"[{name}] When an enemy has Fire, Cold, Light and Dark at once, it bursts for {v}% AD (once per 6s per enemy)");
                case Power.EchoingDodge: return Loc.T($"【{name}】回避するたびに、記憶（スキル）のクールダウンが{v / 10f:0.#}秒縮む", $"[{name}] Each dodge shortens your Memory (skill) cooldowns by {v / 10f:0.#}s");
                case Power.UltimateSurge: return Loc.T($"【{name}】Ultimateを使った後の5秒間、攻撃力・魔力が{v}%上がる", $"[{name}] +{v}% AD/AP for 5s after using your Ultimate");
                case Power.SoulSiphon: return Loc.T($"【{name}】敵を倒すと、最大HPの{v / 10f:0.0}%を回復する", $"[{name}] Kills heal you for {v / 10f:0.0}% of max health");
                case Power.Whirlwind: return Loc.T($"【{name}】回避すると、周囲4mの敵に攻撃力{v}%分のダメージを与える（2秒に1回）", $"[{name}] Dodging deals {v}% AD to enemies within 4m (once per 2s)");
                case Power.Frenzy: return Loc.T($"【{name}】周りの敵1体につき、攻撃速度が{v}%上がる（5体まで）", $"[{name}] +{v}% attack speed per nearby enemy (up to 5)");
                case Power.OpeningStrike: return Loc.T($"【{name}】HPが90%以上の敵への通常攻撃に、攻撃力{v}%分のダメージを上乗せする", $"[{name}] Basic attacks on enemies above 90% health deal +{v}% AD");
                case Power.StarShield: return Loc.T($"【{name}】Ultimateを使うと、最大HPの{v}%分の障壁を張る", $"[{name}] Using your Ultimate grants a shield worth {v}% of max health");
                case Power.Sprint: return Loc.T($"【{name}】回避した後の3秒間、移動速度が{v}%上がる", $"[{name}] +{v}% move speed for 3s after dodging");
                case Power.Vigor: return Loc.T($"【{name}】HPが80%以上の間、攻撃力が{v}%上がる", $"[{name}] +{v}% attack damage while above 80% health");
                case Power.Overload: return Loc.T($"【{name}】スキルを使った後の4秒間、魔力が{v}%上がる", $"[{name}] +{v}% ability power for 4s after using a skill");
                default: return "-";
            }
        }
    }
}
