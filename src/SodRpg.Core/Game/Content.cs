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
        public bool IsKeystone { get; }
        public Power Power { get; }
        public int PowerValue { get; }
        public Txt Description { get; }
        /// <summary>旅人の刻印なら旅人の型名（例：Hero_Vesper）。汎用ノードは null。</summary>
        public string HeroKey { get; set; }
    }

    /// <summary>
    /// ゲーム内容の定義一式。数値は計画書 付録A・B の設計値を出発点に、
    /// 本MODで遊べる形へ丸めたもの（固有名はすべて仮称）。
    /// </summary>
    public static class Content
    {
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
                Power.Umbra, 40, Power.Executioner, 45),
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
                Power.Momentum, 5, Power.Tailwind, 20),
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
                Power.Frost, 30, Power.Momentum, 5),
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
                Power.Resonance, 10, Power.Radiance, 30),
            new UniqueDef("unique.pilgrim_shawl", "armor.prayer_shawl", new Txt("巡礼の肩掛け", "Pilgrim's Shawl"),
                new Txt("歩いた道が長いほど、祈りは深くなる。", "The longer the road walked, the deeper the prayer."),
                Power.SecondWind, 25, Power.UltimateSurge, 20),
            new UniqueDef("unique.saint_shawl", "armor.prayer_shawl", new Txt("聖女の肩掛け", "Saint's Shawl"),
                new Txt("祈りは、仲間を包む光になる。", "Prayer becomes a light that wraps your allies."),
                Power.Barrier, 10, Power.Resonance, 8),
            new UniqueDef("unique.thundercloud", "armor.flowing_cloak", new Txt("雷雲の外套", "Thundercloud Cloak"),
                new Txt("風に乗って、雷は群れを渡る。", "Riding the wind, lightning leaps through the pack."),
                Power.ChainLightning, 60, Power.Tailwind, 20),
            new UniqueDef("unique.reprisal", "armor.counter_gauntlets", new Txt("報復の籠手", "Gauntlets of Reprisal"),
                new Txt("殴られた分だけ、殴り返す。", "It returns every blow it takes."),
                Power.Retaliation, 30, Power.Momentum, 4),
            new UniqueDef("unique.nightwatch", "armor.lampkeeper_mantle", new Txt("夜番の外衣", "Nightwatch Mantle"),
                new Txt("灯が消えるまで、見張りは終わらない。", "The watch ends only when the lamp goes out."),
                Power.Barrier, 10, Power.Aegis, 20),
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
                Power.Frost, 40, Power.Barrier, 8),
            new UniqueDef("unique.wolf_fang", "charm.fang_necklace", new Txt("狼王の牙", "Wolf King's Fang"),
                new Txt("群れの長は、最後まで噛みつく。", "The pack leader bites to the very end."),
                Power.Bloodlust, 30, Power.Executioner, 40),
            new UniqueDef("unique.viper_fang", "charm.fang_necklace", new Txt("毒蛇の牙", "Viper's Fang"),
                new Txt("小さな傷が、やがて命を奪う。", "A tiny wound in time takes a life."),
                Power.Umbra, 35, Power.Lifesteal, 8),
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
                Power.Convergence, 120, Power.Ember, 25),
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
                Power.Tailwind, 20, Power.EchoingDodge, 12),
            new UniqueDef("unique.hourglass", "charm.old_clock", new Txt("時の砂時計", "Hourglass of Ages"),
                new Txt("落ちる砂が、切り札を早める。", "The falling sand hastens your trump card."),
                Power.UltimateSurge, 25, Power.Momentum, 4),
            new UniqueDef("unique.throbbing_core", "charm.pulsing_core", new Txt("鼓動する核", "Throbbing Core"),
                new Txt("鼓動が速まるほど、力が湧く。", "The faster it beats, the stronger you get."),
                Power.Bloodlust, 30, Power.SecondWind, 25),
            new UniqueDef("unique.chain_choker", "charm.chain_necklace", new Txt("鎖の首輪", "Chain Choker"),
                new Txt("縛られた者ほど、強く抗う。", "The more bound, the harder it resists."),
                Power.Retaliation, 30, Power.Aegis, 20),
            new UniqueDef("unique.lucky_ring", "charm.tailwind_ring", new Txt("幸運の指輪", "Lucky Ring"),
                new Txt("追い風は、勝者にだけ吹く。", "The tailwind blows only for the victor."),
                Power.Tailwind, 20, Power.Shatter, 50),
            new UniqueDef("unique.mark_of_prey", "charm.hunters_seal", new Txt("獲物の刻印", "Mark of the Prey"),
                new Txt("刻まれた獲物は、逃げられない。", "Marked prey cannot escape."),
                Power.Executioner, 45, Power.ChainLightning, 50),
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
                Power.Barrier, 10, Power.SecondWind, 25),
            new UniqueDef("unique.tempest_harp", "weapon.star_harp", new Txt("嵐の竪琴", "Tempest Harp"),
                new Txt("激しい調べが、雷を呼ぶ。", "A fierce melody calls down lightning."),
                Power.ChainLightning, 60, Power.Resonance, 8),
            new UniqueDef("unique.bone_mail", "armor.guardian_plate", new Txt("骨の鎧", "Bone Mail"),
                new Txt("倒した者の骨が、新しい盾になる。", "The bones of the fallen become a new shield."),
                Power.Shatter, 50, Power.Bulwark, 25),
            new UniqueDef("unique.robe_of_winds", "armor.resonant_robe", new Txt("風の衣", "Robe of Winds"),
                new Txt("風をまとえば、刃は届かない。", "Clad in wind, no blade can reach you."),
                Power.Tailwind, 20, Power.EchoingDodge, 12),
            new UniqueDef("unique.dragonscale", "armor.bastion_shell", new Txt("竜鱗の鎧", "Dragonscale Armor"),
                new Txt("竜の鱗は、炎さえ跳ね返す。", "Dragon scales repel even flame."),
                Power.Thorns, 35, Power.Blaze, 50),
            new UniqueDef("unique.shadowstitch", "armor.flowing_cloak", new Txt("影縫いの外套", "Shadowstitch Cloak"),
                new Txt("影を縫い留めれば、敵は動けない。", "Stitch down the shadow, and the foe cannot move."),
                Power.Umbra, 35, Power.Frost, 30),
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
            new UniqueDef("set.wildfire.weapon", "weapon.war_axe", new Txt("燎原の斧", "Wildfire Axe"), "set.wildfire"),
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
                TwoPiece = new[] { new StatLine(Stat.FireAmp, 10), new StatLine(Stat.AttackPct, 5) },
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
            },
            [Slot.Armor] = new[]
            {
                new PowerRange(Power.Retaliation, 15, 25),
                new PowerRange(Power.Bulwark, 15, 30),
                new PowerRange(Power.Thorns, 15, 30),
                new PowerRange(Power.Barrier, 6, 10),
                new PowerRange(Power.Aegis, 10, 20),
                new PowerRange(Power.EchoingDodge, 6, 12),
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
            },
        };

        public static readonly IReadOnlyList<TalentDef> Talents = new[]
        {
            new TalentDef("t.off.edge", Line.Offense, new Txt("鋭刃", "Keen Edge"), Stat.AttackPct, 3, 3),
            new TalentDef("t.off.mind", Line.Offense, new Txt("魔力", "Arcane Mind"), Stat.PowerPct, 3, 3),
            new TalentDef("t.off.swift", Line.Offense, new Txt("迅速", "Swiftness"), Stat.AttackSpeedPct, 3, 3),
            new TalentDef("t.off.vital", Line.Offense, new Txt("急所", "Vital Points"), Stat.CritChancePct, 2, 3),
            new TalentDef("t.off.key", Line.Offense, new Txt("終わらない舞", "Endless Dance"), Power.Momentum, 4,
                new Txt("撃破ごとに攻撃速度+4%（4秒、5重まで）", "+4% attack speed per kill (4s, up to 5 stacks)")),

            new TalentDef("t.grd.hearty", Line.Guard, new Txt("頑健", "Hearty"), Stat.MaxHealthPct, 4, 3),
            new TalentDef("t.grd.iron", Line.Guard, new Txt("鉄皮", "Ironhide"), Stat.Armor, 5, 3),
            new TalentDef("t.grd.regen", Line.Guard, new Txt("再生", "Regrowth"), Stat.HealthRegen, 1, 3),
            new TalentDef("t.grd.steady", Line.Guard, new Txt("不屈", "Unyielding"), Stat.Tenacity, 8, 3),
            new TalentDef("t.grd.key", Line.Guard, new Txt("逆襲の構え", "Counterstance"), Power.Retaliation, 20,
                new Txt("被弾後3秒、攻撃力+20%", "+20% attack damage for 3s after being hit")),

            new TalentDef("t.res.focus", Line.Resonance, new Txt("集中", "Focus"), Stat.Haste, 5, 3),
            new TalentDef("t.res.light", Line.Resonance, new Txt("軽歩", "Lightstep"), Stat.MoveSpeedPct, 3, 3),
            new TalentDef("t.res.tune", Line.Resonance, new Txt("共振", "Attunement"), Stat.AttackPct, 2, 3),
            new TalentDef("t.res.ward", Line.Resonance, new Txt("守護", "Warding"), Stat.MaxHealthFlat, 12, 3),
            new TalentDef("t.res.key", Line.Resonance, new Txt("共鳴の環", "Ring of Resonance"), Power.Resonance, 8,
                new Txt("10m以内に味方がいる間、自分と味方の攻撃力・魔力+8%（ソロは+4%）",
                    "+8% attack and ability power to you and allies within 10m (solo: +4%)")),
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

        public const int SecureXp = 20;
        public const int VictoryXp = 100;

        /// <summary>同じ系統の遺物を count 個装着したときのボーナス（2個・3個）。</summary>
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
        }

        public static Txt SlotName(Slot s)
        {
            switch (s)
            {
                case Slot.Weapon: return new Txt("主装備", "Weapon");
                case Slot.Armor: return new Txt("防具", "Armor");
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
                case Power.EchoingDodge: return Loc.T($"【{name}】回避するたびに、Memory（スキル）のクールダウンが{v / 10f:0.#}秒縮む", $"[{name}] Each dodge shortens your Memory (skill) cooldowns by {v / 10f:0.#}s");
                case Power.UltimateSurge: return Loc.T($"【{name}】Ultimateを使った後の5秒間、攻撃力・魔力が{v}%上がる", $"[{name}] +{v}% AD/AP for 5s after using your Ultimate");
                default: return "-";
            }
        }
    }
}
