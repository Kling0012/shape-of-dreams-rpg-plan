using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 旅人の刻印：本体の旅人ごとのキットに効く刻印ツリー（小ノード5つ＋到達刻印2つ）。
    /// 本体の星座（汎用の恒久強化）と重ならないよう、その旅人の戦い方に関わる値だけを扱う。
    /// 本体以外の旅人（他MODのキャラ）は従来の汎用ツリー（Content.Talents）を使う。
    /// </summary>
    public static class HeroSigils
    {
        /// <summary>到達刻印に必要な、その旅人での熟練度。</summary>
        public const int KeystoneMastery = 3;

        private static TalentDef Node(string hero, string id, string ja, string en, Stat stat, int perRank, int maxRank = 3)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), stat, perRank, maxRank) { HeroKey = hero };

        private static TalentDef Key(string hero, string id, string ja, string en, Power power, int value, string descJa, string descEn)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), power, value, new Txt(descJa, descEn)) { HeroKey = hero };

        public static readonly IReadOnlyList<TalentDef> All = new[]
        {
            Node("Hero_Vesper", "vesper.fourth", "四の型", "Fourth Form", Stat.FourthAttackShift, 1, 2),
            Node("Hero_Vesper", "vesper.fire", "炎の誓い", "Oath of Flame", Stat.FireAmp, 6),
            Node("Hero_Vesper", "vesper.wall", "不動", "Immovable", Stat.Armor, 6),
            Node("Hero_Vesper", "vesper.light", "光の加護", "Blessing of Light", Stat.LightAmp, 6),
            Key("Hero_Vesper", "vesper.key", "審問の構え", "Inquisitor's Stance", Power.Retaliation, 25,
                "被弾後3秒、攻撃力+25%（挑発で敵を集めるほど活きる）", "+25% AD for 3s after being hit (thrives on taunting)"),
            Node("Hero_Vesper", "vesper.vigor", "誓いの体", "Oath-Bound Body", Stat.MaxHealthPct, 4),
            Key("Hero_Vesper", "vesper.key2", "鉄壁の審問", "Unbreakable Inquisition", Power.Frenzy, 3,
                "周りの敵1体につき攻撃速度+3%（5体まで。挑発で集めた敵が多いほど速くなる）", "+3% attack speed per nearby enemy, up to 5 (the more you taunt, the faster you swing)"),

            Node("Hero_Lacerta", "lacerta.fourth", "連装", "Double Load", Stat.FourthAttackShift, 1, 2),
            Node("Hero_Lacerta", "lacerta.powder", "火薬", "Gunpowder", Stat.FireAmp, 8),
            Node("Hero_Lacerta", "lacerta.range", "長銃身", "Long Barrel", Stat.AttackRangePct, 5),
            Node("Hero_Lacerta", "lacerta.rapid", "速射", "Rapid Fire", Stat.AttackSpeedPct, 3),
            Key("Hero_Lacerta", "lacerta.key", "サラマンダーの火種", "Salamander Ember", Power.Ember, 30,
                "通常攻撃の命中時30%で火を付与（4発目の爆発と重なる）", "30% on hit: apply Fire (stacks with your 4th-shot blast)"),
            Node("Hero_Lacerta", "lacerta.crit", "狙撃", "Marksman", Stat.CritDamagePct, 8),
            Key("Hero_Lacerta", "lacerta.key2", "一番手の銃声", "Opening Shot", Power.OpeningStrike, 70,
                "HPが90%以上の敵への通常攻撃に、攻撃力70%分のダメージを上乗せ（離れた所から先に撃つほど活きる）", "Basic attacks on enemies above 90% health deal +70% AD (rewards striking first from afar)"),

            Node("Hero_Cetus", "cetus.cold", "深海の冷気", "Abyssal Cold", Stat.ColdAmp, 8),
            Node("Hero_Cetus", "cetus.shell", "氷の殻", "Ice Shell", Stat.MaxHealthPct, 4),
            Node("Hero_Cetus", "cetus.tide", "潮の呼吸", "Tidal Breath", Stat.Haste, 5),
            Node("Hero_Cetus", "cetus.steady", "不屈", "Unyielding", Stat.Tenacity, 8),
            Key("Hero_Cetus", "cetus.key", "凍てつく潮", "Freezing Tide", Power.Frost, 35,
                "通常攻撃の命中時35%で冷気を付与（Icy Veins のスタックと噛み合う）", "35% on hit: apply Cold (feeds Icy Veins)"),
            Node("Hero_Cetus", "cetus.regen", "潮の恵み", "Tidal Grace", Stat.HealthRegen, 1),
            Key("Hero_Cetus", "cetus.key2", "渦潮", "Whirlpool", Power.Whirlwind, 60,
                "回避すると、周囲4mの敵に攻撃力60%分のダメージ（2秒に1回）", "Dodging deals 60% AD to enemies within 4m (once per 2s)"),

            Node("Hero_Yubar", "yubar.haste", "星の加速", "Stellar Haste", Stat.Haste, 6),
            Node("Hero_Yubar", "yubar.light", "星明かり", "Starlight", Stat.LightAmp, 8),
            Node("Hero_Yubar", "yubar.power", "宇宙の理", "Cosmic Law", Stat.PowerPct, 4),
            Node("Hero_Yubar", "yubar.reach", "遠い星", "Distant Star", Stat.AttackRangePct, 5),
            Key("Hero_Yubar", "yubar.key", "収束する星", "Converging Light", Power.UltimateSurge, 20,
                "Ultimate使用後5秒、攻撃力・魔力+20%（Exotic Matter の溜めと合わせる）", "+20% AD/AP for 5s after your Ultimate"),
            Node("Hero_Yubar", "yubar.crit", "星の導き", "Guiding Star", Stat.CritChancePct, 2),
            Key("Hero_Yubar", "yubar.key2", "星の盾", "Stellar Ward", Power.StarShield, 20,
                "Ultimateを使うと、最大HPの20%分の障壁を張る", "Using your Ultimate grants a shield worth 20% of max health"),

            Node("Hero_Husk", "husk.dark", "深い影", "Deep Shadow", Stat.DarkAmp, 8),
            Node("Hero_Husk", "husk.critdmg", "急所", "Vital Strike", Stat.CritDamagePct, 8),
            Node("Hero_Husk", "husk.swift", "影走り", "Shadow Step", Stat.MoveSpeedPct, 3),
            Node("Hero_Husk", "husk.crit", "暗殺術", "Assassination", Stat.CritChancePct, 2),
            Key("Hero_Husk", "husk.key", "虚ろの刃", "Hollow Blade", Power.Umbra, 35,
                "通常攻撃の命中時35%で闇を付与（3重で闇は確定会心）", "35% on hit: apply Dark (3 stacks: dark crits)"),
            Node("Hero_Husk", "husk.atk", "空洞の牙", "Hollow Fang", Stat.AttackPct, 3),
            Key("Hero_Husk", "husk.key2", "魂喰い", "Soul Eater", Power.SoulSiphon, 15,
                "敵を倒すと、最大HPの1.5%を回復する", "Kills heal you for 1.5% of max health"),

            Node("Hero_Mist", "mist.dance", "剣舞", "Blade Dance", Stat.AttackSpeedPct, 4),
            Node("Hero_Mist", "mist.read", "見切り", "Foresight", Stat.MoveSpeedPct, 3),
            Node("Hero_Mist", "mist.crit", "一閃", "Flash Cut", Stat.CritChancePct, 2),
            Node("Hero_Mist", "mist.duel", "決闘者", "Duelist", Stat.AttackPct, 3),
            Key("Hero_Mist", "mist.key", "残像の決闘", "Afterimage Duel", Power.EchoingDodge, 12,
                "回避するたびに Memory のクールダウン-1.2秒（パリィと回避の手数が増える）", "Each dodge: -1.2s Memory cooldowns"),
            Node("Hero_Mist", "mist.haste", "呼吸", "Breathing", Stat.Haste, 5),
            Key("Hero_Mist", "mist.key2", "疾風の歩法", "Gale Step", Power.Sprint, 25,
                "回避した後の3秒間、移動速度+25%", "+25% move speed for 3s after dodging"),

            Node("Hero_Nachia", "nachia.light", "祝福の光", "Blessed Light", Stat.LightAmp, 8),
            Node("Hero_Nachia", "nachia.power", "絆の力", "Bond Power", Stat.PowerPct, 4),
            Node("Hero_Nachia", "nachia.haste", "呼び声", "Calling", Stat.Haste, 5),
            Node("Hero_Nachia", "nachia.ward", "守護の誓い", "Guardian Vow", Stat.MaxHealthPct, 4),
            Key("Hero_Nachia", "nachia.key", "共に在る", "Together", Power.Resonance, 8,
                "近くの味方と共に攻撃力・魔力+8%（ソロは半分）", "+8% AD/AP with a nearby ally (solo: half)"),
            Node("Hero_Nachia", "nachia.move", "軽やかな足取り", "Light Steps", Stat.MoveSpeedPct, 3),
            Key("Hero_Nachia", "nachia.key2", "祈りの過負荷", "Overflowing Prayer", Power.Overload, 20,
                "スキルを使った後の4秒間、魔力+20%", "+20% ability power for 4s after using a skill"),

            Node("Hero_Aurena", "aurena.life", "生命の器", "Vessel of Life", Stat.MaxHealthPct, 5),
            Node("Hero_Aurena", "aurena.light", "黄金の光", "Golden Light", Stat.LightAmp, 8),
            Node("Hero_Aurena", "aurena.regen", "再生", "Regrowth", Stat.HealthRegen, 1),
            Node("Hero_Aurena", "aurena.power", "献身", "Devotion", Stat.PowerPct, 3),
            Key("Hero_Aurena", "aurena.key", "血の献身", "Blood Devotion", Power.Bloodlust, 25,
                "HP50%未満の間、攻撃速度+25%（HPを捧げる技と噛み合う）", "+25% attack speed below 50% health (pairs with HP sacrifice)"),
            Node("Hero_Aurena", "aurena.armor", "聖杯の守り", "Chalice Guard", Stat.Armor, 6),
            Key("Hero_Aurena", "aurena.key2", "満ちた聖杯", "Brimming Chalice", Power.Vigor, 15,
                "HPが80%以上の間、攻撃力+15%", "+15% attack damage while above 80% health"),

            Node("Hero_Bismuth", "bismuth.fire", "炎の章", "Chapter of Flame", Stat.FireAmp, 6),
            Node("Hero_Bismuth", "bismuth.light", "光の章", "Chapter of Light", Stat.LightAmp, 6),
            Node("Hero_Bismuth", "bismuth.dark", "闇の章", "Chapter of Shadow", Stat.DarkAmp, 6),
            Node("Hero_Bismuth", "bismuth.story", "物語の続き", "Next Chapter", Stat.Haste, 5),
            Key("Hero_Bismuth", "bismuth.key", "四つの物語", "Four Tales", Power.Convergence, 100,
                "敵に4属性が揃うと攻撃力100%の爆発（本ごとの属性を重ねる）", "All 4 elements on an enemy: burst for 100% AD"),
            Node("Hero_Bismuth", "bismuth.cold", "氷の章", "Chapter of Ice", Stat.ColdAmp, 6),
            Key("Hero_Bismuth", "bismuth.key2", "終章", "Final Chapter", Power.UltimateSurge, 20,
                "Ultimateを使った後の5秒間、攻撃力・魔力+20%", "+20% AD/AP for 5s after using your Ultimate"),
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
