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
            Node("Hero_Vesper", "vesper.fire", "聖なる剛力", "Holy Strength", Stat.AttackPct, 4),
            Node("Hero_Vesper", "vesper.wall", "誓いの体", "Oath-Bound Body", Stat.MaxHealthPct, 4),
            Node("Hero_Vesper", "vesper.light", "光の加護", "Blessing of Light", Stat.LightAmp, 8),
            Key("Hero_Vesper", "vesper.key", "審問の構え", "Inquisitor's Stance", Power.Retaliation, 25,
                "挑発で敵を集めるほど活きる。", "Thrives when you taunt enemies onto you."),
            Node("Hero_Vesper", "vesper.vigor", "祈りの連打", "Prayerful Flurry", Stat.AttackSpeedPct, 3),
            Key("Hero_Vesper", "vesper.key2", "鉄壁の審問", "Unbreakable Inquisition", Power.Frenzy, 4,
                "挑発で集めた敵が多いほど、速く攻撃できる（8体まで）。", "The more enemies you taunt, the faster you swing (up to 8)."),
            DeepNode("Hero_Vesper", "vesper.deep.tenacity", "聖なる鎧", "Holy Armor", Stat.Armor, 6),
            DeepPower("Hero_Vesper", "vesper.deep.thorns", "審問の盾", "Shield of Inquisition", Power.Aegis, 5),
            DeepNode("Hero_Vesper", "vesper.deep.regen", "規律の剛力", "Disciplined Might", Stat.AttackPct, 3),
            DeepNode("Hero_Vesper", "vesper.deep.body", "審判の一撃", "Judgment Blow", Stat.CritDamagePct, 8),
            DeepPower("Hero_Vesper", "vesper.deep.bulwark", "鉄の陣", "Iron Formation", Power.Bulwark, 6),
            DeepPower("Hero_Vesper", "vesper.deep.radiance", "光の烙印", "Brand of Light", Power.Radiance, 15),

            DeepCostly("Hero_Lacerta", "lacerta.fourth", "連装", "Double Load", Stat.FourthAttackShift),
            Node("Hero_Lacerta", "lacerta.powder", "火薬", "Gunpowder", Stat.FireAmp, 8),
            Node("Hero_Lacerta", "lacerta.range", "防弾の外套", "Armored Coat", Stat.MaxHealthPct, 4),
            Node("Hero_Lacerta", "lacerta.rapid", "速射", "Rapid Fire", Stat.AttackSpeedPct, 3),
            Key("Hero_Lacerta", "lacerta.key", "サラマンダーの火種", "Salamander Ember", Power.Ember, 60,
                "通常攻撃で敵を燃やしておくと、サラマンダーパウダーの4発目の爆発が2倍になる。", "Keep foes burning with basic attacks so Salamander Powder's 4th-shot blast deals double."),
            Node("Hero_Lacerta", "lacerta.crit", "狙撃", "Marksman", Stat.AttackPct, 4),
            Key("Hero_Lacerta", "lacerta.key2", "一番手の銃声", "Opening Shot", Power.OpeningStrike, 70,
                "離れた所から先に撃つほど活きる。", "Rewards striking first from afar."),
            DeepNode("Hero_Lacerta", "lacerta.deep.atk", "火薬の調合", "Powder Mixing", Stat.PowerPct, 3),
            DeepNode("Hero_Lacerta", "lacerta.deep.crit", "照準", "Steady Aim", Stat.CritDamagePct, 8),
            DeepNode("Hero_Lacerta", "lacerta.deep.haste", "鉄の銃床", "Iron Stock", Stat.Armor, 6),
            DeepPower("Hero_Lacerta", "lacerta.deep.blaze", "炸裂弾", "Explosive Rounds", Power.Blaze, 15),
            DeepPower("Hero_Lacerta", "lacerta.deep.ember", "耐火の外套", "Fireproof Coat", Power.Aegis, 5),
            DeepPower("Hero_Lacerta", "lacerta.deep.opening", "初弾必中", "First Shot", Power.OpeningStrike, 15),

            Node("Hero_Cetus", "cetus.cold", "深海の冷気", "Abyssal Cold", Stat.ColdAmp, 8),
            Node("Hero_Cetus", "cetus.shell", "氷の殻", "Ice Shell", Stat.MaxHealthPct, 4),
            Node("Hero_Cetus", "cetus.tide", "潮の呼吸", "Tidal Breath", Stat.AttackSpeedPct, 3),
            Node("Hero_Cetus", "cetus.steady", "深海の魔力", "Abyssal Power", Stat.PowerPct, 4),
            Key("Hero_Cetus", "cetus.key", "凍てつく潮", "Freezing Tide", Power.Frost, 70,
                "氷の血脈のスタックと噛み合う。", "Feeds your Icy Veins stacks."),
            Node("Hero_Cetus", "cetus.regen", "潮の鱗", "Tidal Scales", Stat.Armor, 6),
            Key("Hero_Cetus", "cetus.key2", "凪", "Calm Sea", Power.StillWater, 6,
                "技で敵を止めるたびに、障壁を張り直す。", "Each time your skills stun a foe, your shield is renewed."),
            DeepNode("Hero_Cetus", "cetus.deep.power", "深淵の知", "Abyssal Wisdom", Stat.PowerPct, 3),
            DeepNode("Hero_Cetus", "cetus.deep.armor", "氷の守り", "Ice Ward", Stat.ShieldPower, 4),
            DeepNode("Hero_Cetus", "cetus.deep.weight", "深海の器", "Vessel of the Deep", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Cetus", "cetus.deep.frost", "満ち潮", "High Tide", Power.Overload, 4),
            DeepPower("Hero_Cetus", "cetus.deep.shatter", "氷の枷", "Ice Shackles", Power.Fetters, 4),
            DeepPower("Hero_Cetus", "cetus.deep.aegis", "氷壁", "Ice Wall", Power.Aegis, 5),

            Node("Hero_Yubar", "yubar.haste", "星の加速", "Stellar Haste", Stat.Haste, 5),
            Node("Hero_Yubar", "yubar.light", "星明かり", "Starlight", Stat.LightAmp, 8),
            Node("Hero_Yubar", "yubar.power", "宇宙の理", "Cosmic Law", Stat.PowerPct, 4),
            Node("Hero_Yubar", "yubar.reach", "星の脈動", "Stellar Pulse", Stat.AttackSpeedPct, 3),
            Key("Hero_Yubar", "yubar.key", "収束する星", "Converging Light", Power.UltimateSurge, 20,
                "エキゾチック物質を溜めてから奥義を撃つ流れと合わせる。", "Pair it with charging Exotic Matter before your Ultimate."),
            Node("Hero_Yubar", "yubar.crit", "星の器", "Stellar Vessel", Stat.MaxHealthPct, 4),
            Key("Hero_Yubar", "yubar.key2", "星の盾", "Stellar Ward", Power.StarShield, 20,
                "奥義を撃つ瞬間の守りを固める。", "Shields you the moment you unleash your Ultimate."),
            DeepNode("Hero_Yubar", "yubar.deep.critdmg", "超新星", "Supernova", Stat.PowerPct, 3),
            DeepNode("Hero_Yubar", "yubar.deep.life", "星の鎧", "Star Armor", Stat.Armor, 6),
            DeepNode("Hero_Yubar", "yubar.deep.move", "星の導き", "Guiding Star", Stat.CritDamagePct, 8),
            DeepPower("Hero_Yubar", "yubar.deep.surge", "重力の昂り", "Gravity Surge", Power.UltimateSurge, 6),
            DeepPower("Hero_Yubar", "yubar.deep.shield", "事象の地平", "Event Horizon", Power.StarShield, 4),
            DeepPower("Hero_Yubar", "yubar.deep.radiance", "星屑の輝き", "Stardust Glow", Power.Radiance, 20),

            Node("Hero_Husk", "husk.dark", "深い影", "Deep Shadow", Stat.DarkAmp, 8),
            Node("Hero_Husk", "husk.critdmg", "急所", "Vital Strike", Stat.CritDamagePct, 8),
            Node("Hero_Husk", "husk.swift", "連撃", "Flurry", Stat.AttackSpeedPct, 3),
            Node("Hero_Husk", "husk.crit", "空ろな鎧", "Hollow Armor", Stat.Armor, 6),
            Key("Hero_Husk", "husk.key", "虚ろの刃", "Hollow Blade", Power.Umbra, 115,
                "通常攻撃のたびに闇が重なり、会心ならもう1つ重なる。", "Every hit layers Dark, and a critical hit adds one more."),
            Node("Hero_Husk", "husk.atk", "空洞の牙", "Hollow Fang", Stat.AttackPct, 4),
            Key("Hero_Husk", "husk.key2", "瞬歩の刃", "Flash-Step Blade", Power.ShadowStep, 69,
                "回避・ダッシュ・瞬間移動の後3秒以内の次の通常攻撃に、攻撃力か魔力の高い方の69%分のダメージを上乗せする（重ならず、移動するたびに時間を延長）。「一歩一殺」の確定会心や「風の傷」と組み合わせ、移動から一撃へつなげる。", "After a dodge, dash or teleport, your next basic attack within 3s deals +69% of the higher of AD or AP (does not stack; each movement refreshes it). Pair it with One Step, One Kill's guaranteed critical hit and Scar of the Wind to turn movement into a decisive strike."),
            DeepNode("Hero_Husk", "husk.deep.speed", "影の牙", "Shadow Fang", Stat.AttackPct, 3),
            DeepNode("Hero_Husk", "husk.deep.tenacity", "空ろな器", "Hollow Vessel", Stat.MaxHealthPct, 4),
            DeepNode("Hero_Husk", "husk.deep.haste", "闇の深み", "Depth of Darkness", Stat.DarkAmp, 6),
            DeepPower("Hero_Husk", "husk.deep.umbra", "闇を纏う", "Shroud of Dark", Power.Umbra, 15),
            DeepPower("Hero_Husk", "husk.deep.exec", "とどめの刃", "Finishing Blade", Power.Executioner, 14),
            DeepPower("Hero_Husk", "husk.deep.momentum", "狩りの勢い", "Hunting Momentum", Power.Momentum, 2),

            Node("Hero_Mist", "mist.dance", "剣舞", "Blade Dance", Stat.AttackSpeedPct, 3),
            Node("Hero_Mist", "mist.read", "剣気", "Sword Spirit", Stat.PowerPct, 3),
            Node("Hero_Mist", "mist.crit", "一閃", "Flash Cut", Stat.CritDamagePct, 8),
            Node("Hero_Mist", "mist.duel", "決闘者", "Duelist", Stat.AttackPct, 4),
            Key("Hero_Mist", "mist.key", "先手の型", "Form of the First Strike", Power.OpeningStrike, 70,
                "無傷の敵への最初の一撃が重くなり、各敵への最初の数撃を強くする戦い方と噛み合う。", "Your opening blow on an unhurt foe hits harder, pairing with styles that empower the first strikes on each enemy."),
            Node("Hero_Mist", "mist.haste", "構えの守り", "Guarded Stance", Stat.Armor, 6),
            Key("Hero_Mist", "mist.key2", "受けて返す型", "Form of the Counter", Power.Retaliation, 25,
                "攻撃を受けた直後の3秒間、攻撃力と魔力が上がる。", "For 3s after taking a hit, your attack and ability power rise."),
            DeepNode("Hero_Mist", "mist.deep.critdmg", "鋭い切っ先", "Keen Point", Stat.AttackPct, 3),
            DeepNode("Hero_Mist", "mist.deep.life", "鍛えた体", "Trained Body", Stat.MaxHealthPct, 3),
            DeepPower("Hero_Mist", "mist.deep.stance", "受け流しの構え", "Parry Stance", Power.StillWater, 3),
            DeepNode("Hero_Mist", "mist.deep.whirl", "傑作の盾", "Masterwork Shield", Stat.ShieldPower, 3),
            DeepPower("Hero_Mist", "mist.deep.tail", "先手の構え", "First-Strike Stance", Power.OpeningStrike, 15),
            DeepPower("Hero_Mist", "mist.deep.frenzy", "受けて返す", "Take and Return", Power.Retaliation, 4),

            Node("Hero_Nachia", "nachia.light", "祝福の光", "Blessed Light", Stat.LightAmp, 8),
            Node("Hero_Nachia", "nachia.power", "絆の力", "Bond Power", Stat.PowerPct, 4),
            Node("Hero_Nachia", "nachia.haste", "呼び声", "Calling", Stat.Haste, 5),
            Node("Hero_Nachia", "nachia.ward", "守護の誓い", "Guardian Vow", Stat.MaxHealthPct, 4),
            Key("Hero_Nachia", "nachia.key", "共に在る", "Together", Power.Resonance, 8,
                "仲間と一緒に戦うほど活きる。", "Best alongside your allies."),
            Node("Hero_Nachia", "nachia.move", "群れの足並み", "Pack Pace", Stat.AttackSpeedPct, 3),
            Key("Hero_Nachia", "nachia.key2", "祈りの過負荷", "Overflowing Prayer", Power.Overload, 20,
                "スキルを続けて使う戦い方に向く。", "Suits chaining your skills."),
            DeepNode("Hero_Nachia", "nachia.deep.regen", "癒やしの歌", "Healing Song", Stat.HealPower, 3),
            DeepNode("Hero_Nachia", "nachia.deep.tenacity", "仲間を想う心", "Thinking of Friends", Stat.SummonPower, 4),
            DeepNode("Hero_Nachia", "nachia.deep.armor", "盾の祈り", "Prayer of Shields", Stat.ShieldPower, 3),
            DeepPower("Hero_Nachia", "nachia.deep.resonance", "響き合う心", "Hearts in Tune", Power.Resonance, 2),
            DeepPower("Hero_Nachia", "nachia.deep.barrier", "守りの灯", "Guarding Light", Power.OverflowingLife, 10),
            DeepPower("Hero_Nachia", "nachia.deep.second", "灯を絶やさず", "Keep the Light", Power.StillWater, 3),

            Node("Hero_Aurena", "aurena.life", "羽ばたき", "Wingbeat", Stat.AttackSpeedPct, 3),
            Node("Hero_Aurena", "aurena.light", "黄金の光", "Golden Light", Stat.LightAmp, 8),
            Node("Hero_Aurena", "aurena.regen", "聖杯の守り", "Chalice Guard", Stat.Armor, 6),
            Node("Hero_Aurena", "aurena.power", "献身", "Devotion", Stat.PowerPct, 4),
            Key("Hero_Aurena", "aurena.key", "血の献身", "Blood Devotion", Power.Overload, 20,
                "HPを捧げる技を使うたびに、魔力が高まる。", "Each HP-sacrifice skill you cast sharpens your ability power."),
            Node("Hero_Aurena", "aurena.armor", "黄金の爪", "Golden Claw", Stat.AttackPct, 3),
            Key("Hero_Aurena", "aurena.key2", "満ちた聖杯", "Brimming Chalice", Power.OverflowingLife, 30,
                "仲間と自分を癒やしてあふれた分が、障壁になる。", "Healing that overflows becomes a shield."),
            DeepNode("Hero_Aurena", "aurena.deep.haste", "再生の循環", "Cycle of Renewal", Stat.HealPower, 3),
            DeepNode("Hero_Aurena", "aurena.deep.flat", "揺るがぬ祈り", "Steadfast Prayer", Stat.SacrificeReduction, 3),
            DeepNode("Hero_Aurena", "aurena.deep.crit", "聖杯の叡智", "Chalice Wisdom", Stat.PowerPct, 3),
            DeepPower("Hero_Aurena", "aurena.deep.lifesteal", "血の羽ばたき", "Bloodied Wings", Power.Bloodlust, 2),
            DeepPower("Hero_Aurena", "aurena.deep.siphon", "命を啜る", "Drink of Life", Power.SoulSiphon, 4),
            DeepPower("Hero_Aurena", "aurena.deep.bloodlust", "最後の祈り", "Last Prayer", Power.SecondWind, 8),

            Node("Hero_Bismuth", "bismuth.fire", "炎の章", "Chapter of Flame", Stat.FireAmp, 8),
            Node("Hero_Bismuth", "bismuth.light", "速読", "Speed Reading", Stat.Haste, 5),
            Node("Hero_Bismuth", "bismuth.dark", "厚い装丁", "Thick Binding", Stat.MaxHealthPct, 4),
            Node("Hero_Bismuth", "bismuth.story", "物語の続き", "Next Chapter", Stat.PowerPct, 4),
            Key("Hero_Bismuth", "bismuth.key", "四つの物語", "Four Tales", Power.Convergence, 100,
                "本ごとの属性を重ねて発動させる。", "Layer each book's element to trigger it."),
            Node("Hero_Bismuth", "bismuth.cold", "めくる手", "Turning Pages", Stat.AttackSpeedPct, 3),
            Key("Hero_Bismuth", "bismuth.key2", "雷鳴の章", "Chapter of Thunder", Power.ChainLightning, 50,
                "本の魔法に雷を添えて、群れを一度に削る。", "Adds lightning to thin out packs."),
            DeepNode("Hero_Bismuth", "bismuth.deep.power", "博識", "Erudition", Stat.PowerPct, 3),
            DeepNode("Hero_Bismuth", "bismuth.deep.crit", "光と闇の章", "Chapters of Light and Dark", Stat.LightAmp, 6),
            DeepNode("Hero_Bismuth", "bismuth.deep.life", "頁の守り", "Page Ward", Stat.ShieldPower, 3),
            DeepPower("Hero_Bismuth", "bismuth.deep.ember", "表紙の守り", "Cover Guard", Power.Barrier, 3),
            DeepPower("Hero_Bismuth", "bismuth.deep.frost", "氷の頁", "Page of Ice", Power.Frost, 10),
            DeepPower("Hero_Bismuth", "bismuth.deep.umbra", "闇の頁", "Page of Shadow", Power.Umbra, 15),
        };

        /// <summary>核のIDを維持したまま、記憶ルート・夢の輪・星群を加える。</summary>
        private static readonly IReadOnlyList<TalentDef> BaselineAll = CreateAll();
        public static IReadOnlyList<TalentDef> All => StarClusters.InstalledTalents(BaselineAll);
        private static readonly Dictionary<string, IReadOnlyList<TalentDef>> Trees = CreateTrees();

        private static IReadOnlyList<TalentDef> CreateAll()
        {
            var routes = HeroStarRoutes.All;
            var anchors = StarClusters.OuterAnchors;
            var existing = new List<TalentDef>(Core.Length + routes.Count + anchors.Count);
            existing.AddRange(Core);
            existing.AddRange(routes);
            existing.AddRange(anchors);
            var clusters = StarClusters.Generate(StarClusters.All, existing);
            existing.AddRange(clusters);
            return existing.AsReadOnly();
        }

        private static Dictionary<string, IReadOnlyList<TalentDef>> CreateTrees()
        {
            var groups = new Dictionary<string, List<TalentDef>>(StringComparer.Ordinal);
            foreach (var t in BaselineAll)
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
            StarClusters.TryGetRegisteredTree(heroKey, out var registered) ? registered : BaselineTreeFor(heroKey);
        internal static IReadOnlyList<TalentDef> BaselineTreeFor(string heroKey) =>
            heroKey != null && Trees.TryGetValue(heroKey, out var tree) ? tree : Content.Talents;
    }
}
