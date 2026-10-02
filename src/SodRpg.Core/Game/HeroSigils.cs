using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 旅人の刻印：核（手前・奥・到達刻印）、記憶ごとのルート、夢の輪。
    /// 本体の星座（汎用の恒久強化）と重ならないよう、その旅人の戦い方に関わる値だけを扱う。
    /// 本体以外の旅人（他MODのキャラ）は従来の汎用ツリー（Content.Talents）を使う。
    /// </summary>
    public static class HeroSigils
    {
        /// <summary>到達刻印に必要な、その旅人での熟練度。</summary>
        public const int KeystoneMastery = 3;

        private static TalentDef Node(string hero, string id, string ja, string en, Stat stat, int perRank, int maxRank = 3)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), stat, perRank, maxRank) { HeroKey = hero };

        public static TalentDef DeepNode(string hero, string id, string ja, string en, Stat stat, int perRank, int maxRank = 3)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), stat, perRank, maxRank) { HeroKey = hero, Tier = 2 };

        /// <summary>手前の星のうち、段ごとに固有効果を伸ばすもの。</summary>
        private static TalentDef PowerNode(string hero, string id, string ja, string en, Power power, int perRank, int maxRank = 3)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), power, perRank, maxRank) { HeroKey = hero };

        public static TalentDef DeepPower(string hero, string id, string ja, string en, Power power, int perRank, int maxRank = 3)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), power, perRank, maxRank) { HeroKey = hero, Tier = 2 };

        /// <summary>奥の星のうち、強すぎるので1段だけ・高い費用にしたもの（連装・四の型）。</summary>
        public const int CostlyRankCost = 4;

        private static TalentDef DeepCostly(string hero, string id, string ja, string en, Stat stat)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), stat, 1, 1) { HeroKey = hero, Tier = 2, RankCost = CostlyRankCost };

        private static TalentDef Key(string hero, string id, string ja, string en, Power power, int value, string descJa, string descEn)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), power, value, new Txt(descJa, descEn)) { HeroKey = hero };

        private static readonly TalentDef[] Core = new[]
        {
            DeepCostly("Hero_Vesper", "vesper.fourth", "四の型", "Fourth Form", Stat.FourthAttackShift),
            Node("Hero_Vesper", "vesper.fire", "聖なる剛力", "Holy Strength", Stat.AttackPct, 3),
            Node("Hero_Vesper", "vesper.wall", "審判の一撃", "Judgment Blow", Stat.CritDamagePct, 8),
            Node("Hero_Vesper", "vesper.light", "光の加護", "Blessing of Light", Stat.LightAmp, 6),
            Key("Hero_Vesper", "vesper.key", "審問の構え", "Inquisitor's Stance", Power.Retaliation, 25,
                "挑発で敵を集めるほど活きます。", "Thrives when you taunt enemies onto you."),
            Node("Hero_Vesper", "vesper.vigor", "誓いの体", "Oath-Bound Body", Stat.MaxHealthPct, 4),
            Key("Hero_Vesper", "vesper.key2", "鉄壁の審問", "Unbreakable Inquisition", Power.Frenzy, 3,
                "挑発で集めた敵が多いほど、速く攻撃できます。", "The more enemies you taunt, the faster you swing."),
            DeepNode("Hero_Vesper", "vesper.deep.tenacity", "祈りの連打", "Prayerful Flurry", Stat.AttackSpeedPct, 3),
            DeepPower("Hero_Vesper", "vesper.deep.thorns", "審問の残響", "Echo of Judgment", Power.CriticalEcho, 2),
            DeepNode("Hero_Vesper", "vesper.deep.regen", "規律", "Discipline", Stat.Haste, 4),
            DeepNode("Hero_Vesper", "vesper.deep.body", "聖別の体", "Consecrated Body", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Vesper", "vesper.deep.bulwark", "鉄の陣", "Iron Formation", Power.Bulwark, 6),
            DeepPower("Hero_Vesper", "vesper.deep.radiance", "光の烙印", "Brand of Light", Power.Radiance, 15),

            DeepCostly("Hero_Lacerta", "lacerta.fourth", "連装", "Double Load", Stat.FourthAttackShift),
            Node("Hero_Lacerta", "lacerta.powder", "火薬", "Gunpowder", Stat.FireAmp, 8),
            Node("Hero_Lacerta", "lacerta.range", "防弾の外套", "Armored Coat", Stat.MaxHealthPct, 4),
            Node("Hero_Lacerta", "lacerta.rapid", "速射", "Rapid Fire", Stat.AttackSpeedPct, 3),
            Key("Hero_Lacerta", "lacerta.key", "サラマンダーの火種", "Salamander Ember", Power.Ember, 60,
                "4発目の爆発と一緒に、火を重ねられます。", "Stacks Fire alongside your 4th-shot blast."),
            Node("Hero_Lacerta", "lacerta.crit", "狙撃", "Marksman", Stat.AttackPct, 3),
            Key("Hero_Lacerta", "lacerta.key2", "一番手の銃声", "Opening Shot", Power.OpeningStrike, 70,
                "離れた所から先に撃つほど活きます。", "Rewards striking first from afar."),
            DeepNode("Hero_Lacerta", "lacerta.deep.atk", "火薬の調合", "Powder Mixing", Stat.PowerPct, 4),
            DeepNode("Hero_Lacerta", "lacerta.deep.crit", "照準", "Steady Aim", Stat.CritDamagePct, 8),
            DeepNode("Hero_Lacerta", "lacerta.deep.haste", "装填の手際", "Quick Reload", Stat.Haste, 4),
            DeepPower("Hero_Lacerta", "lacerta.deep.blaze", "炸裂弾", "Explosive Rounds", Power.Blaze, 15),
            DeepPower("Hero_Lacerta", "lacerta.deep.ember", "耐火の外套", "Fireproof Coat", Power.Aegis, 5),
            DeepPower("Hero_Lacerta", "lacerta.deep.opening", "初弾必中", "First Shot", Power.OpeningStrike, 15),

            Node("Hero_Cetus", "cetus.cold", "深海の冷気", "Abyssal Cold", Stat.ColdAmp, 8),
            Node("Hero_Cetus", "cetus.shell", "氷の殻", "Ice Shell", Stat.MaxHealthPct, 5),
            Node("Hero_Cetus", "cetus.tide", "潮の呼吸", "Tidal Breath", Stat.Haste, 5),
            Node("Hero_Cetus", "cetus.steady", "深海の魔力", "Abyssal Power", Stat.PowerPct, 3),
            Key("Hero_Cetus", "cetus.key", "凍てつく潮", "Freezing Tide", Power.Frost, 70,
                "氷の血脈のスタックと噛み合います。", "Feeds your Icy Veins stacks."),
            Node("Hero_Cetus", "cetus.regen", "潮の拳", "Tidal Fist", Stat.AttackPct, 3),
            Key("Hero_Cetus", "cetus.key2", "凪", "Calm Sea", Power.StillWater, 6,
                "技で敵を止めるたびに、障壁を張り直します。", "Each time your skills stun a foe, your shield is renewed."),
            DeepNode("Hero_Cetus", "cetus.deep.power", "深淵の知", "Abyssal Wisdom", Stat.PowerPct, 3),
            DeepNode("Hero_Cetus", "cetus.deep.armor", "氷の鱗", "Ice Scales", Stat.Armor, 5),
            DeepNode("Hero_Cetus", "cetus.deep.weight", "荒波", "Rough Waves", Stat.AttackSpeedPct, 3),
            DeepPower("Hero_Cetus", "cetus.deep.frost", "満ち潮", "High Tide", Power.Overload, 4),
            DeepPower("Hero_Cetus", "cetus.deep.shatter", "氷の枷", "Ice Shackles", Power.Fetters, 4),
            DeepPower("Hero_Cetus", "cetus.deep.aegis", "氷壁", "Ice Wall", Power.Aegis, 5),

            Node("Hero_Yubar", "yubar.haste", "星の加速", "Stellar Haste", Stat.Haste, 6),
            Node("Hero_Yubar", "yubar.light", "星明かり", "Starlight", Stat.LightAmp, 8),
            Node("Hero_Yubar", "yubar.power", "宇宙の理", "Cosmic Law", Stat.PowerPct, 4),
            Node("Hero_Yubar", "yubar.reach", "星の脈動", "Stellar Pulse", Stat.AttackSpeedPct, 3),
            Key("Hero_Yubar", "yubar.key", "収束する星", "Converging Light", Power.UltimateSurge, 20,
                "エキゾチック物質を溜めてから Ultimate を撃つ流れと合わせます。", "Pair it with charging Exotic Matter before your Ultimate."),
            Node("Hero_Yubar", "yubar.crit", "星の導き", "Guiding Star", Stat.CritDamagePct, 8),
            Key("Hero_Yubar", "yubar.key2", "星の盾", "Stellar Ward", Power.StarShield, 20,
                "Ultimate を撃つ瞬間の守りを固めます。", "Shields you the moment you unleash your Ultimate."),
            DeepPower("Hero_Yubar", "yubar.deep.critdmg", "超新星", "Supernova", Power.Overload, 8),
            DeepNode("Hero_Yubar", "yubar.deep.life", "星の器", "Stellar Vessel", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Yubar", "yubar.deep.move", "流れ星", "Shooting Star", Power.Finale, 8),
            DeepPower("Hero_Yubar", "yubar.deep.surge", "重力の昂り", "Gravity Surge", Power.UltimateSurge, 6),
            DeepPower("Hero_Yubar", "yubar.deep.shield", "事象の地平", "Event Horizon", Power.StarShield, 4),
            DeepPower("Hero_Yubar", "yubar.deep.radiance", "星屑の輝き", "Stardust Glow", Power.Radiance, 20),

            Node("Hero_Husk", "husk.dark", "深い影", "Deep Shadow", Stat.DarkAmp, 8),
            Node("Hero_Husk", "husk.critdmg", "急所", "Vital Strike", Stat.CritDamagePct, 8),
            Node("Hero_Husk", "husk.swift", "影走り", "Shadow Step", Stat.Haste, 5),
            Node("Hero_Husk", "husk.crit", "空ろな鎧", "Hollow Armor", Stat.Armor, 6),
            Key("Hero_Husk", "husk.key", "虚ろの刃", "Hollow Blade", Power.Umbra, 100,
                "通常攻撃のたびに闇が重なり、会心ならもう1つ重なります。", "Every hit layers Dark, and a critical hit adds one more."),
            Node("Hero_Husk", "husk.atk", "空洞の牙", "Hollow Fang", Stat.AttackPct, 3),
            Key("Hero_Husk", "husk.key2", "瞬歩の刃", "Flash-Step Blade", Power.EchoingDodge, 60,
                "回避の直後の一撃が重くなり、回避後の確定会心と噛み合います。", "The strike right after a dodge hits harder, pairing with your guaranteed post-dodge crit."),
            DeepNode("Hero_Husk", "husk.deep.speed", "連撃", "Flurry", Stat.AttackSpeedPct, 3),
            DeepPower("Hero_Husk", "husk.deep.tenacity", "魂喰い", "Soul Eater", Power.SoulSiphon, 5),
            DeepPower("Hero_Husk", "husk.deep.haste", "影の呼吸", "Shadow Breath", Power.CriticalEcho, 3),
            DeepPower("Hero_Husk", "husk.deep.umbra", "闇を纏う", "Shroud of Dark", Power.Umbra, 15),
            DeepPower("Hero_Husk", "husk.deep.exec", "とどめの刃", "Finishing Blade", Power.Executioner, 12),
            DeepPower("Hero_Husk", "husk.deep.momentum", "狩りの勢い", "Hunting Momentum", Power.Momentum, 2),

            Node("Hero_Mist", "mist.dance", "剣舞", "Blade Dance", Stat.AttackSpeedPct, 4),
            Node("Hero_Mist", "mist.read", "剣気", "Sword Spirit", Stat.PowerPct, 3),
            Node("Hero_Mist", "mist.crit", "一閃", "Flash Cut", Stat.CritDamagePct, 8),
            Node("Hero_Mist", "mist.duel", "決闘者", "Duelist", Stat.AttackPct, 3),
            Key("Hero_Mist", "mist.key", "残像の決闘", "Afterimage Duel", Power.EchoingDodge, 80,
                "回避してすぐ斬り返すと、大きな一撃になります。", "Strike right after a dodge for a heavy counter."),
            Node("Hero_Mist", "mist.haste", "呼吸", "Breathing", Stat.Haste, 5),
            Key("Hero_Mist", "mist.key2", "見切りの極意", "Art of Foresight", Power.PerfectRead, 15,
                "パリィや無敵で受け止めた直後、斬撃が速くなります。", "Right after a parry or invulnerable block, your blade speeds up."),
            DeepPower("Hero_Mist", "mist.deep.critdmg", "剣闘士の集中", "Gladiator's Focus", Power.Vigor, 4),
            DeepNode("Hero_Mist", "mist.deep.life", "鍛えた体", "Trained Body", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Mist", "mist.deep.stance", "受け流しの構え", "Parry Stance", Power.StillWater, 3),
            DeepPower("Hero_Mist", "mist.deep.whirl", "旋の太刀", "Spinning Blade", Power.Whirlwind, 15),
            DeepPower("Hero_Mist", "mist.deep.tail", "風を読む", "Read the Wind", Power.CriticalEcho, 3),
            DeepPower("Hero_Mist", "mist.deep.frenzy", "乱れ斬り", "Wild Slashes", Power.Frenzy, 1),

            Node("Hero_Nachia", "nachia.light", "祝福の光", "Blessed Light", Stat.LightAmp, 8),
            Node("Hero_Nachia", "nachia.power", "絆の力", "Bond Power", Stat.PowerPct, 4),
            Node("Hero_Nachia", "nachia.haste", "呼び声", "Calling", Stat.Haste, 5),
            Node("Hero_Nachia", "nachia.ward", "守護の誓い", "Guardian Vow", Stat.MaxHealthPct, 4),
            Key("Hero_Nachia", "nachia.key", "共に在る", "Together", Power.Resonance, 8,
                "仲間と一緒に戦うほど活きます。", "Best alongside your allies."),
            Node("Hero_Nachia", "nachia.move", "群れの足並み", "Pack Pace", Stat.AttackSpeedPct, 4),
            Key("Hero_Nachia", "nachia.key2", "祈りの過負荷", "Overflowing Prayer", Power.Overload, 20,
                "スキルを続けて使う戦い方に向いています。", "Suits chaining your skills."),
            DeepNode("Hero_Nachia", "nachia.deep.regen", "癒やしの歌", "Healing Song", Stat.PowerPct, 3),
            DeepNode("Hero_Nachia", "nachia.deep.tenacity", "仲間を想う心", "Thinking of Friends", Stat.Haste, 4),
            DeepNode("Hero_Nachia", "nachia.deep.armor", "盾の祈り", "Prayer of Shields", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Nachia", "nachia.deep.resonance", "響き合う心", "Hearts in Tune", Power.Resonance, 2),
            DeepPower("Hero_Nachia", "nachia.deep.barrier", "守りの灯", "Guarding Light", Power.OverflowingLife, 10),
            DeepPower("Hero_Nachia", "nachia.deep.second", "灯を絶やさず", "Keep the Light", Power.StillWater, 3),

            Node("Hero_Aurena", "aurena.life", "羽ばたき", "Wingbeat", Stat.AttackSpeedPct, 4),
            Node("Hero_Aurena", "aurena.light", "黄金の光", "Golden Light", Stat.LightAmp, 8),
            Node("Hero_Aurena", "aurena.regen", "再生の循環", "Cycle of Renewal", Stat.Haste, 4),
            Node("Hero_Aurena", "aurena.power", "献身", "Devotion", Stat.PowerPct, 4),
            Key("Hero_Aurena", "aurena.key", "血の献身", "Blood Devotion", Power.Overload, 20,
                "HPを捧げる技を使うたびに、魔力が高まります。", "Each HP-sacrifice skill you cast sharpens your ability power."),
            Node("Hero_Aurena", "aurena.armor", "黄金の爪", "Golden Claw", Stat.AttackPct, 3),
            Key("Hero_Aurena", "aurena.key2", "満ちた聖杯", "Brimming Chalice", Power.OverflowingLife, 30,
                "仲間と自分を癒やしてあふれた分が、障壁になります。", "Healing that overflows becomes a shield."),
            DeepNode("Hero_Aurena", "aurena.deep.haste", "巡る血", "Flowing Blood", Stat.Haste, 5),
            DeepNode("Hero_Aurena", "aurena.deep.flat", "揺るがぬ祈り", "Steadfast Prayer", Stat.Tenacity, 6),
            DeepNode("Hero_Aurena", "aurena.deep.crit", "聖杯の叡智", "Chalice Wisdom", Stat.PowerPct, 3),
            DeepPower("Hero_Aurena", "aurena.deep.lifesteal", "羽根の舞", "Feather Dance", Power.Sprint, 6),
            DeepPower("Hero_Aurena", "aurena.deep.siphon", "命を啜る", "Drink of Life", Power.SoulSiphon, 4),
            DeepPower("Hero_Aurena", "aurena.deep.bloodlust", "捧げる一撃", "Offered Strike", Power.EchoingDodge, 20),

            Node("Hero_Bismuth", "bismuth.fire", "炎の章", "Chapter of Flame", Stat.FireAmp, 6),
            Node("Hero_Bismuth", "bismuth.light", "光の章", "Chapter of Light", Stat.LightAmp, 6),
            Node("Hero_Bismuth", "bismuth.dark", "闇の章", "Chapter of Shadow", Stat.DarkAmp, 6),
            Node("Hero_Bismuth", "bismuth.story", "物語の続き", "Next Chapter", Stat.Haste, 6),
            Key("Hero_Bismuth", "bismuth.key", "四つの物語", "Four Tales", Power.Convergence, 100,
                "本ごとの属性を重ねて発動させます。", "Layer each book's element to trigger it."),
            Node("Hero_Bismuth", "bismuth.cold", "めくる手", "Turning Pages", Stat.AttackSpeedPct, 4),
            Key("Hero_Bismuth", "bismuth.key2", "雷鳴の章", "Chapter of Thunder", Power.ChainLightning, 50,
                "本の魔法に雷を添えて、群れを一度に削ります。", "Adds lightning to thin out packs."),
            DeepNode("Hero_Bismuth", "bismuth.deep.power", "博識", "Erudition", Stat.PowerPct, 3),
            DeepNode("Hero_Bismuth", "bismuth.deep.crit", "勇ましい章", "Brave Chapter", Stat.AttackPct, 3),
            DeepNode("Hero_Bismuth", "bismuth.deep.life", "厚い装丁", "Thick Binding", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Bismuth", "bismuth.deep.ember", "表紙の守り", "Cover Guard", Power.Barrier, 3),
            DeepPower("Hero_Bismuth", "bismuth.deep.frost", "氷の頁", "Page of Ice", Power.Frost, 10),
            DeepPower("Hero_Bismuth", "bismuth.deep.umbra", "闇の頁", "Page of Shadow", Power.Umbra, 15),
        };

        /// <summary>核のIDを維持したまま、記憶ルートと夢の輪を加える。</summary>
        public static readonly IReadOnlyList<TalentDef> All = CreateAll();
        private static readonly Dictionary<string, IReadOnlyList<TalentDef>> Trees = CreateTrees();

        private static IReadOnlyList<TalentDef> CreateAll()
        {
            var routes = HeroStarRoutes.All;
            var all = new TalentDef[Core.Length + routes.Count];
            Array.Copy(Core, all, Core.Length);
            for (int i = 0; i < routes.Count; i++) all[Core.Length + i] = routes[i];
            return all;
        }

        private static Dictionary<string, IReadOnlyList<TalentDef>> CreateTrees()
        {
            var groups = new Dictionary<string, List<TalentDef>>(StringComparer.Ordinal);
            foreach (var t in All)
            {
                if (!groups.TryGetValue(t.HeroKey, out var tree))
                {
                    tree = new List<TalentDef>();
                    groups.Add(t.HeroKey, tree);
                }
                tree.Add(t);
            }
            var trees = new Dictionary<string, IReadOnlyList<TalentDef>>(StringComparer.Ordinal);
            foreach (var group in groups) trees.Add(group.Key, group.Value.ToArray());
            return trees;
        }

        public static bool HasTree(string heroKey) => heroKey != null && Trees.ContainsKey(heroKey);

        /// <summary>旅人ごとに一度だけ分類する。未知の旅人は従来の汎用ツリー。</summary>
        public static IReadOnlyList<TalentDef> TreeFor(string heroKey) =>
            heroKey != null && Trees.TryGetValue(heroKey, out var tree) ? tree : Content.Talents;
    }
}
