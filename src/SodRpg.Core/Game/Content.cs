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
            string two = string.Join(Loc.T("・", ", "), TwoPiece.Select(s => Content.FormatStat(s.Stat, s.Value)));
            string three = string.Join(Loc.T("・", ", "), ThreePiece.Select(p => Content.PowerName(p.Power) + " " + p.Value));
            return Loc.T($"2点：{two}／3点：{three}", $"2pc: {two} / 3pc: {three}");
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
            new UniqueDef("set.tide.weapon", "weapon.chain_sword", new Txt("潮鳴りの剣", "Tidecaller's Blade"), "set.tide"),
            new UniqueDef("set.tide.armor", "armor.flowing_cloak", new Txt("潮鳴りの外套", "Tidecaller's Cloak"), "set.tide"),
            new UniqueDef("set.tide.charm", "charm.tailwind_ring", new Txt("潮鳴りの指輪", "Tidecaller's Ring"), "set.tide"),
            new UniqueDef("set.lamp.weapon", "weapon.calming_staff", new Txt("灯守の杖", "Lampkeeper's Staff"), "set.lamp"),
            new UniqueDef("set.lamp.armor", "armor.lampkeeper_mantle", new Txt("灯守の誓衣", "Lampkeeper's Vow"), "set.lamp"),
            new UniqueDef("set.lamp.charm", "charm.resonance_amulet", new Txt("灯守の護符", "Lampkeeper's Charm"), "set.lamp"),
        };

        public static readonly IReadOnlyList<SetDef> Sets = new[]
        {
            new SetDef
            {
                Id = "set.tide", Name = new Txt("潮鳴りの装い", "Tidecaller's Regalia"),
                TwoPiece = new[] { new StatLine(Stat.AttackSpeedPct, 10), new StatLine(Stat.MoveSpeedPct, 5) },
                ThreePiece = new[] { new PowerLine(Power.Momentum, 6), new PowerLine(Power.Tailwind, 25) },
            },
            new SetDef
            {
                Id = "set.lamp", Name = new Txt("灯守の誓い", "Lampkeeper's Oath"),
                TwoPiece = new[] { new StatLine(Stat.MaxHealthPct, 10), new StatLine(Stat.Haste, 10) },
                ThreePiece = new[] { new PowerLine(Power.Barrier, 10), new PowerLine(Power.Resonance, 8) },
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
            },
            [Slot.Armor] = new[]
            {
                new AffixDef(Stat.MaxHealthPct, 4, 8, 14),
                new AffixDef(Stat.MaxHealthFlat, 15, 30, 12),
                new AffixDef(Stat.Armor, 4, 9, 14),
                new AffixDef(Stat.HealthRegen, 1, 3, 8),
                new AffixDef(Stat.Tenacity, 6, 12, 8),
                new AffixDef(Stat.MoveSpeedPct, 2, 4, 6),
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
                case 0: return 15;
                case 1: return 25;
                case 2: return 40;
                case 3: return 60;
                case 4: return 90;
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
        public static int XpToNext(int level) => 60 + 25 * Math.Max(1, level);

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
                case Line.Offense: return new Txt("攻勢", "Offense");
                case Line.Guard: return new Txt("守勢", "Guard");
                default: return new Txt("共鳴", "Resonance");
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
                case Stat.FourthAttackShift: return Loc.T($"4発目の位置 {sign}{v}", $"{sign}{v} Fourth-attack shift");
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
                case Power.Momentum: return Loc.T($"【{name}】撃破ごとに攻撃速度+{v}%（4秒・5重）", $"[{name}] +{v}% attack speed per kill (4s, 5 stacks)");
                case Power.Retaliation: return Loc.T($"【{name}】被弾後3秒、攻撃力+{v}%", $"[{name}] +{v}% attack damage for 3s after being hit");
                case Power.Bulwark: return Loc.T($"【{name}】周囲の敵3体以上で防御+{v}", $"[{name}] +{v} armor while 3+ enemies are near");
                case Power.Lifesteal: return Loc.T($"【{name}】通常攻撃の命中で最大HPの{v / 10f:0.#}%回復", $"[{name}] Basic attack hits heal {v / 10f:0.#}% max health");
                case Power.Thorns: return Loc.T($"【{name}】受けたダメージの{v}%を反射", $"[{name}] Reflect {v}% of damage taken");
                case Power.Executioner: return Loc.T($"【{name}】HP30%未満の敵へ攻撃力{v}%の追撃", $"[{name}] Hits on enemies below 30% deal +{v}% AD");
                case Power.Resonance: return Loc.T($"【{name}】近くの味方と共に攻撃力・魔力+{v}%（ソロ半分）", $"[{name}] +{v}% AD/AP with a nearby ally (solo: half)");
                case Power.Tailwind: return Loc.T($"【{name}】撃破後2秒、移動速度+{v}%", $"[{name}] +{v}% move speed for 2s after a kill");
                case Power.Barrier: return Loc.T($"【{name}】12秒ごとに最大HPの{v}%の障壁", $"[{name}] Shield for {v}% max health every 12s");
                case Power.SecondWind: return Loc.T($"【{name}】HP30%未満で最大HPの{v}%回復（60秒）", $"[{name}] Below 30% health, heal {v}% max health (60s)");
                case Power.Blaze: return Loc.T($"【{name}】4回目ごとの通常攻撃に攻撃力{v}%の魔法追撃", $"[{name}] Every 4th hit deals +{v}% AD magic damage");
                case Power.ChainLightning: return Loc.T($"【{name}】命中時25%で近くの敵2体へ攻撃力{v}%の魔法ダメージ", $"[{name}] 25% on hit: {v}% AD magic damage to 2 nearby enemies");
                case Power.Shatter: return Loc.T($"【{name}】撃破時、周囲4mの敵へ攻撃力{v}%のダメージ", $"[{name}] On kill, deal {v}% AD to enemies within 4m");
                case Power.Aegis: return Loc.T($"【{name}】大きな一撃（最大HP20%以上）で最大HPの{v}%の障壁（20秒）", $"[{name}] A big hit (20%+ max HP) grants a {v}% max HP shield (20s)");
                case Power.Bloodlust: return Loc.T($"【{name}】HP50%未満の間、攻撃速度+{v}%", $"[{name}] +{v}% attack speed below 50% health");
                case Power.Ember: return Loc.T($"【{name}】通常攻撃の命中時{v}%で火を付与", $"[{name}] {v}% on hit: apply Fire");
                case Power.Frost: return Loc.T($"【{name}】通常攻撃の命中時{v}%で冷気を付与", $"[{name}] {v}% on hit: apply Cold");
                case Power.Radiance: return Loc.T($"【{name}】通常攻撃の命中時{v}%で光を付与（3重で光は確定会心）", $"[{name}] {v}% on hit: apply Light (3 stacks: light crits)");
                case Power.Umbra: return Loc.T($"【{name}】通常攻撃の命中時{v}%で闇を付与", $"[{name}] {v}% on hit: apply Dark");
                case Power.Convergence: return Loc.T($"【{name}】敵に4属性が揃うと攻撃力{v}%の爆発（同じ敵へ6秒に1回）", $"[{name}] All 4 elements on an enemy: burst for {v}% AD (6s per enemy)");
                case Power.EchoingDodge: return Loc.T($"【{name}】回避するたびにMemoryのクールダウン-{v / 10f:0.#}秒", $"[{name}] Each dodge: -{v / 10f:0.#}s Memory cooldowns");
                case Power.UltimateSurge: return Loc.T($"【{name}】Ultimate使用後5秒、攻撃力・魔力+{v}%", $"[{name}] +{v}% AD/AP for 5s after using your Ultimate");
                default: return "-";
            }
        }
    }
}
