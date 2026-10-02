using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 旅人の刻印：本体の旅人ごとのキットに効く刻印ツリー（手前の星・奥の星・到達刻印）。
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

        public static TalentDef DeepPower(string hero, string id, string ja, string en, Power power, int perRank, int maxRank = 3)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), power, perRank, maxRank) { HeroKey = hero, Tier = 2 };

        /// <summary>奥の星のうち、強すぎるので1段だけ・高い費用にしたもの（連装・四の型）。</summary>
        public const int CostlyRankCost = 4;

        private static TalentDef DeepCostly(string hero, string id, string ja, string en, Stat stat)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), stat, 1, 1) { HeroKey = hero, Tier = 2, RankCost = CostlyRankCost };

        private static TalentDef Key(string hero, string id, string ja, string en, Power power, int value, string descJa, string descEn)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), power, value, new Txt(descJa, descEn)) { HeroKey = hero };

        public static readonly IReadOnlyList<TalentDef> All = new[]
        {
            DeepCostly("Hero_Vesper", "vesper.fourth", "四の型", "Fourth Form", Stat.FourthAttackShift),
            Node("Hero_Vesper", "vesper.fire", "聖なる力", "Holy Might", Stat.PowerPct, 4),
            Node("Hero_Vesper", "vesper.wall", "不動", "Immovable", Stat.Armor, 6),
            Node("Hero_Vesper", "vesper.light", "光の加護", "Blessing of Light", Stat.LightAmp, 6),
            Key("Hero_Vesper", "vesper.key", "審問の構え", "Inquisitor's Stance", Power.Retaliation, 25,
                "挑発で敵を集めるほど活きます。", "Thrives when you taunt enemies onto you."),
            Node("Hero_Vesper", "vesper.vigor", "誓いの体", "Oath-Bound Body", Stat.MaxHealthPct, 4),
            Key("Hero_Vesper", "vesper.key2", "鉄壁の審問", "Unbreakable Inquisition", Power.Frenzy, 3,
                "挑発で集めた敵が多いほど、速く攻撃できます。", "The more enemies you taunt, the faster you swing."),
            DeepNode("Hero_Vesper", "vesper.deep.tenacity", "聖なる不屈", "Holy Resolve", Stat.Tenacity, 8),
            DeepPower("Hero_Vesper", "vesper.deep.thorns", "棘の誓い", "Oath of Thorns", Power.Thorns, 8),
            DeepNode("Hero_Vesper", "vesper.deep.regen", "審問の光", "Inquisitor's Light", Stat.PowerPct, 3),
            DeepNode("Hero_Vesper", "vesper.deep.body", "聖別の体", "Consecrated Body", Stat.MaxHealthFlat, 12),
            DeepPower("Hero_Vesper", "vesper.deep.bulwark", "鉄の陣", "Iron Formation", Power.Bulwark, 6),
            DeepPower("Hero_Vesper", "vesper.deep.radiance", "光の烙印", "Brand of Light", Power.Radiance, 15),

            DeepCostly("Hero_Lacerta", "lacerta.fourth", "連装", "Double Load", Stat.FourthAttackShift),
            Node("Hero_Lacerta", "lacerta.powder", "火薬", "Gunpowder", Stat.FireAmp, 8),
            Node("Hero_Lacerta", "lacerta.range", "長銃身", "Long Barrel", Stat.AttackRangePct, 5),
            Node("Hero_Lacerta", "lacerta.rapid", "速射", "Rapid Fire", Stat.AttackSpeedPct, 3),
            Key("Hero_Lacerta", "lacerta.key", "サラマンダーの火種", "Salamander Ember", Power.Ember, 60,
                "4発目の爆発と一緒に、火を重ねられます。", "Stacks Fire alongside your 4th-shot blast."),
            Node("Hero_Lacerta", "lacerta.crit", "狙撃", "Marksman", Stat.CritDamagePct, 8),
            Key("Hero_Lacerta", "lacerta.key2", "一番手の銃声", "Opening Shot", Power.OpeningStrike, 70,
                "離れた所から先に撃つほど活きます。", "Rewards striking first from afar."),
            DeepNode("Hero_Lacerta", "lacerta.deep.atk", "火薬の調合", "Powder Mixing", Stat.PowerPct, 4),
            DeepNode("Hero_Lacerta", "lacerta.deep.crit", "照準", "Steady Aim", Stat.CritChancePct, 2),
            DeepNode("Hero_Lacerta", "lacerta.deep.haste", "装填の手際", "Quick Reload", Stat.Haste, 4),
            DeepPower("Hero_Lacerta", "lacerta.deep.blaze", "炸裂弾", "Explosive Rounds", Power.Blaze, 15),
            DeepPower("Hero_Lacerta", "lacerta.deep.ember", "焼夷弾", "Incendiary Rounds", Power.Ember, 12),
            DeepPower("Hero_Lacerta", "lacerta.deep.opening", "初弾必中", "First Shot", Power.OpeningStrike, 15),

            Node("Hero_Cetus", "cetus.cold", "深海の冷気", "Abyssal Cold", Stat.ColdAmp, 8),
            Node("Hero_Cetus", "cetus.shell", "氷の殻", "Ice Shell", Stat.MaxHealthPct, 5),
            Node("Hero_Cetus", "cetus.tide", "潮の呼吸", "Tidal Breath", Stat.Haste, 5),
            Node("Hero_Cetus", "cetus.steady", "不屈", "Unyielding", Stat.Tenacity, 8),
            Key("Hero_Cetus", "cetus.key", "凍てつく潮", "Freezing Tide", Power.Frost, 70,
                "氷の血脈のスタックと噛み合います。", "Feeds your Icy Veins stacks."),
            Node("Hero_Cetus", "cetus.regen", "潮の恵み", "Tidal Grace", Stat.HealthRegen, 1),
            Key("Hero_Cetus", "cetus.key2", "渦潮", "Whirlpool", Power.Whirlwind, 60,
                "敵の中に飛び込んで回避する戦い方に向いています。", "Suits diving in and dodging through enemies."),
            DeepNode("Hero_Cetus", "cetus.deep.power", "深淵の知", "Abyssal Wisdom", Stat.PowerPct, 3),
            DeepNode("Hero_Cetus", "cetus.deep.armor", "氷の鱗", "Ice Scales", Stat.Armor, 5),
            DeepNode("Hero_Cetus", "cetus.deep.weight", "深海の器", "Vessel of the Deep", Stat.MaxHealthPct, 4),
            DeepPower("Hero_Cetus", "cetus.deep.frost", "凍える指先", "Freezing Touch", Power.Frost, 10),
            DeepPower("Hero_Cetus", "cetus.deep.shatter", "砕氷", "Icebreaker", Power.Shatter, 15),
            DeepPower("Hero_Cetus", "cetus.deep.aegis", "氷壁", "Ice Wall", Power.Aegis, 5),

            Node("Hero_Yubar", "yubar.haste", "星の加速", "Stellar Haste", Stat.Haste, 6),
            Node("Hero_Yubar", "yubar.light", "星明かり", "Starlight", Stat.LightAmp, 8),
            Node("Hero_Yubar", "yubar.power", "宇宙の理", "Cosmic Law", Stat.PowerPct, 4),
            Node("Hero_Yubar", "yubar.reach", "重力の井戸", "Gravity Well", Stat.Haste, 4),
            Key("Hero_Yubar", "yubar.key", "収束する星", "Converging Light", Power.UltimateSurge, 20,
                "エキゾチック物質を溜めてから Ultimate を撃つ流れと合わせます。", "Pair it with charging Exotic Matter before your Ultimate."),
            Node("Hero_Yubar", "yubar.crit", "星の魔力", "Star Power", Stat.PowerPct, 3),
            Key("Hero_Yubar", "yubar.key2", "星の盾", "Stellar Ward", Power.StarShield, 20,
                "Ultimate を撃つ瞬間の守りを固めます。", "Shields you the moment you unleash your Ultimate."),
            DeepNode("Hero_Yubar", "yubar.deep.critdmg", "超新星", "Supernova", Stat.PowerPct, 3),
            DeepNode("Hero_Yubar", "yubar.deep.life", "星の器", "Stellar Vessel", Stat.MaxHealthPct, 3),
            DeepNode("Hero_Yubar", "yubar.deep.move", "流れ星", "Shooting Star", Stat.MoveSpeedPct, 3),
            DeepPower("Hero_Yubar", "yubar.deep.surge", "重力の昂り", "Gravity Surge", Power.UltimateSurge, 6),
            DeepPower("Hero_Yubar", "yubar.deep.shield", "事象の地平", "Event Horizon", Power.StarShield, 5),
            DeepPower("Hero_Yubar", "yubar.deep.radiance", "星屑の輝き", "Stardust Glow", Power.Radiance, 20),

            Node("Hero_Husk", "husk.dark", "深い影", "Deep Shadow", Stat.DarkAmp, 8),
            Node("Hero_Husk", "husk.critdmg", "急所", "Vital Strike", Stat.CritDamagePct, 8),
            Node("Hero_Husk", "husk.swift", "影走り", "Shadow Step", Stat.MoveSpeedPct, 3),
            Node("Hero_Husk", "husk.crit", "暗殺術", "Assassination", Stat.CritChancePct, 2),
            Key("Hero_Husk", "husk.key", "虚ろの刃", "Hollow Blade", Power.Umbra, 100,
                "通常攻撃のたびに闇が重なり、会心ならもう1つ重なります。", "Every hit layers Dark, and a critical hit adds one more."),
            Node("Hero_Husk", "husk.atk", "空洞の牙", "Hollow Fang", Stat.AttackPct, 3),
            Key("Hero_Husk", "husk.key2", "魂喰い", "Soul Eater", Power.SoulSiphon, 15,
                "倒し続けるほど、長く戦い続けられます。", "Keep killing to keep going."),
            DeepNode("Hero_Husk", "husk.deep.speed", "連撃", "Flurry", Stat.AttackSpeedPct, 3),
            DeepNode("Hero_Husk", "husk.deep.tenacity", "空ろな体", "Hollow Body", Stat.Tenacity, 6),
            DeepNode("Hero_Husk", "husk.deep.haste", "影の呼吸", "Shadow Breath", Stat.Haste, 5),
            DeepPower("Hero_Husk", "husk.deep.umbra", "闇を纏う", "Shroud of Dark", Power.Umbra, 15),
            DeepPower("Hero_Husk", "husk.deep.exec", "とどめの刃", "Finishing Blade", Power.Executioner, 12),
            DeepPower("Hero_Husk", "husk.deep.momentum", "狩りの勢い", "Hunting Momentum", Power.Momentum, 1),

            Node("Hero_Mist", "mist.dance", "剣舞", "Blade Dance", Stat.AttackSpeedPct, 4),
            Node("Hero_Mist", "mist.read", "剣気", "Sword Spirit", Stat.PowerPct, 3),
            Node("Hero_Mist", "mist.crit", "一閃", "Flash Cut", Stat.CritChancePct, 2),
            Node("Hero_Mist", "mist.duel", "決闘者", "Duelist", Stat.AttackPct, 3),
            Key("Hero_Mist", "mist.key", "残像の決闘", "Afterimage Duel", Power.EchoingDodge, 80,
                "回避してすぐ斬り返すと、大きな一撃になります。", "Strike right after a dodge for a heavy counter."),
            Node("Hero_Mist", "mist.haste", "呼吸", "Breathing", Stat.Haste, 5),
            Key("Hero_Mist", "mist.key2", "疾風の歩法", "Gale Step", Power.Sprint, 25,
                "回避の後、すばやく間合いを詰めて斬りかかれます。", "After a dodge, close in and strike faster."),
            DeepNode("Hero_Mist", "mist.deep.critdmg", "鋭い切っ先", "Keen Point", Stat.CritDamagePct, 8),
            DeepNode("Hero_Mist", "mist.deep.life", "鍛えた体", "Trained Body", Stat.MaxHealthPct, 3),
            DeepNode("Hero_Mist", "mist.deep.stance", "揺るがぬ構え", "Unshaken Stance", Stat.Tenacity, 6),
            DeepPower("Hero_Mist", "mist.deep.whirl", "旋の太刀", "Spinning Blade", Power.Whirlwind, 15),
            DeepPower("Hero_Mist", "mist.deep.tail", "風を読む", "Read the Wind", Power.Tailwind, 6),
            DeepPower("Hero_Mist", "mist.deep.frenzy", "乱れ斬り", "Wild Slashes", Power.Frenzy, 1),

            Node("Hero_Nachia", "nachia.light", "祝福の光", "Blessed Light", Stat.LightAmp, 8),
            Node("Hero_Nachia", "nachia.power", "絆の力", "Bond Power", Stat.PowerPct, 4),
            Node("Hero_Nachia", "nachia.haste", "呼び声", "Calling", Stat.Haste, 5),
            Node("Hero_Nachia", "nachia.ward", "守護の誓い", "Guardian Vow", Stat.MaxHealthPct, 4),
            Key("Hero_Nachia", "nachia.key", "共に在る", "Together", Power.Resonance, 8,
                "仲間と一緒に戦うほど活きます。", "Best alongside your allies."),
            Node("Hero_Nachia", "nachia.move", "軽やかな足取り", "Light Steps", Stat.MoveSpeedPct, 3),
            Key("Hero_Nachia", "nachia.key2", "祈りの過負荷", "Overflowing Prayer", Power.Overload, 20,
                "スキルを続けて使う戦い方に向いています。", "Suits chaining your skills."),
            DeepNode("Hero_Nachia", "nachia.deep.regen", "癒やしの歌", "Healing Song", Stat.HealthRegen, 1),
            DeepNode("Hero_Nachia", "nachia.deep.tenacity", "仲間を想う心", "Thinking of Friends", Stat.Tenacity, 8),
            DeepNode("Hero_Nachia", "nachia.deep.armor", "盾の祈り", "Prayer of Shields", Stat.Armor, 5),
            DeepPower("Hero_Nachia", "nachia.deep.resonance", "響き合う心", "Hearts in Tune", Power.Resonance, 2),
            DeepPower("Hero_Nachia", "nachia.deep.barrier", "守りの灯", "Guarding Light", Power.Barrier, 3),
            DeepPower("Hero_Nachia", "nachia.deep.second", "灯を絶やさず", "Keep the Light", Power.SecondWind, 8),

            Node("Hero_Aurena", "aurena.life", "生命の器", "Vessel of Life", Stat.MaxHealthPct, 5),
            Node("Hero_Aurena", "aurena.light", "黄金の光", "Golden Light", Stat.LightAmp, 8),
            Node("Hero_Aurena", "aurena.regen", "再生", "Regrowth", Stat.HealthRegen, 1),
            Node("Hero_Aurena", "aurena.power", "献身", "Devotion", Stat.PowerPct, 3),
            Key("Hero_Aurena", "aurena.key", "血の献身", "Blood Devotion", Power.Overload, 20,
                "HPを捧げる技を使うたびに、魔力が高まります。", "Each HP-sacrifice skill you cast sharpens your ability power."),
            Node("Hero_Aurena", "aurena.armor", "聖杯の守り", "Chalice Guard", Stat.Armor, 6),
            Key("Hero_Aurena", "aurena.key2", "満ちた聖杯", "Brimming Chalice", Power.Vigor, 15,
                "HPを高く保つ戦い方に向いています。", "Suits keeping your health high."),
            DeepNode("Hero_Aurena", "aurena.deep.haste", "巡る血", "Flowing Blood", Stat.Haste, 5),
            DeepNode("Hero_Aurena", "aurena.deep.flat", "満ちる器", "Filling Vessel", Stat.MaxHealthFlat, 12),
            DeepNode("Hero_Aurena", "aurena.deep.crit", "聖杯の叡智", "Chalice Wisdom", Stat.PowerPct, 3),
            DeepPower("Hero_Aurena", "aurena.deep.lifesteal", "血の還流", "Blood Return", Power.Lifesteal, 3),
            DeepPower("Hero_Aurena", "aurena.deep.siphon", "命を啜る", "Drink of Life", Power.SoulSiphon, 4),
            DeepPower("Hero_Aurena", "aurena.deep.bloodlust", "捧げる祈り", "Offered Prayer", Power.Overload, 4),

            Node("Hero_Bismuth", "bismuth.fire", "炎の章", "Chapter of Flame", Stat.FireAmp, 6),
            Node("Hero_Bismuth", "bismuth.light", "光の章", "Chapter of Light", Stat.LightAmp, 6),
            Node("Hero_Bismuth", "bismuth.dark", "闇の章", "Chapter of Shadow", Stat.DarkAmp, 6),
            Node("Hero_Bismuth", "bismuth.story", "物語の続き", "Next Chapter", Stat.Haste, 5),
            Key("Hero_Bismuth", "bismuth.key", "四つの物語", "Four Tales", Power.Convergence, 100,
                "本ごとの属性を重ねて発動させます。", "Layer each book's element to trigger it."),
            Node("Hero_Bismuth", "bismuth.cold", "氷の章", "Chapter of Ice", Stat.ColdAmp, 6),
            Key("Hero_Bismuth", "bismuth.key2", "雷鳴の章", "Chapter of Thunder", Power.ChainLightning, 50,
                "本の魔法に雷を添えて、群れを一度に削ります。", "Adds lightning to thin out packs."),
            DeepNode("Hero_Bismuth", "bismuth.deep.power", "博識", "Erudition", Stat.PowerPct, 3),
            DeepNode("Hero_Bismuth", "bismuth.deep.crit", "名文", "Fine Prose", Stat.CritChancePct, 2),
            DeepNode("Hero_Bismuth", "bismuth.deep.life", "厚い装丁", "Thick Binding", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Bismuth", "bismuth.deep.ember", "炎の頁", "Page of Flame", Power.Ember, 10),
            DeepPower("Hero_Bismuth", "bismuth.deep.frost", "氷の頁", "Page of Ice", Power.Frost, 10),
            DeepPower("Hero_Bismuth", "bismuth.deep.umbra", "闇の頁", "Page of Shadow", Power.Umbra, 15),
        };

        public static bool HasTree(string heroKey)
        {
            foreach (var t in All)
                if (t.HeroKey == heroKey) return true;
            return false;
        }

        /// <summary>その旅人のツリー（本体の旅人なら刻印、それ以外は汎用）。</summary>
        public static IEnumerable<TalentDef> TreeFor(string heroKey)
        {
            if (HasTree(heroKey))
            {
                foreach (var t in All)
                    if (t.HeroKey == heroKey) yield return t;
            }
            else
            {
                foreach (var t in Content.Talents) yield return t;
            }
        }
    }
}
