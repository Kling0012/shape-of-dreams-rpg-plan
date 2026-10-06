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

        private static TalentDef DeepCostly(string hero, string id, string ja, string en, Stat stat, int perRank)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), stat, perRank, 1) { HeroKey = hero, Tier = 2, RankCost = CostlyRankCost };

        private static TalentDef Key(string hero, string id, string ja, string en, Power power, int value, string descJa, string descEn)
            => new TalentDef("h." + id, Line.Offense, new Txt(ja, en), power, value, new Txt(descJa, descEn)) { HeroKey = hero };

        private static readonly TalentDef[] Core = new[]
        {
            DeepCostly("Hero_Vesper", "vesper.fourth", "四の型", "Fourth Form", Stat.FourthAttackShift, MemoryDamageBalance.Effect_legacy_h_vesper_fourth_stat_perRank),
            Node("Hero_Vesper", "vesper.fire", "聖なる剛力", "Holy Strength", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_vesper_fire_stat_perRank),
            Node("Hero_Vesper", "vesper.wall", "誓いの体", "Oath-Bound Body", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_vesper_wall_stat_perRank),
            Node("Hero_Vesper", "vesper.light", "光の加護", "Blessing of Light", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_vesper_light_stat_perRank),
            Key("Hero_Vesper", "vesper.key", "審問の構え", "Inquisitor's Stance", Power.Retaliation, MemoryDamageBalance.Effect_legacy_h_vesper_key_power_perRank,
                "挑発で敵を集めるほど活きる。", "Thrives when you taunt enemies onto you."),
            Node("Hero_Vesper", "vesper.vigor", "祈りの連打", "Prayerful Flurry", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_vesper_vigor_stat_perRank),
            Key("Hero_Vesper", "vesper.key2", "鉄壁の審問", "Unbreakable Inquisition", Power.Frenzy, MemoryDamageBalance.Effect_legacy_h_vesper_key2_power_perRank,
                "挑発で集めた敵が多いほど、速く攻撃できる（8体まで）。", "The more enemies you taunt, the faster you swing (up to 8)."),
            DeepNode("Hero_Vesper", "vesper.deep.tenacity", "聖なる鎧", "Holy Armor", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_vesper_deep_tenacity_stat_perRank),
            DeepPower("Hero_Vesper", "vesper.deep.thorns", "審問の盾", "Shield of Inquisition", Power.Aegis, MemoryDamageBalance.Effect_legacy_h_vesper_deep_thorns_power_perRank),
            DeepNode("Hero_Vesper", "vesper.deep.regen", "規律の剛力", "Disciplined Might", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_vesper_deep_regen_stat_perRank),
            DeepNode("Hero_Vesper", "vesper.deep.body", "審判の一撃", "Judgment Blow", Stat.CritDamagePct, MemoryDamageBalance.Effect_legacy_h_vesper_deep_body_stat_perRank),
            DeepPower("Hero_Vesper", "vesper.deep.bulwark", "鉄の陣", "Iron Formation", Power.Bulwark, MemoryDamageBalance.Effect_legacy_h_vesper_deep_bulwark_power_perRank),
            DeepPower("Hero_Vesper", "vesper.deep.radiance", "光の烙印", "Brand of Light", Power.Radiance, MemoryDamageBalance.Effect_legacy_h_vesper_deep_radiance_power_perRank),

            DeepCostly("Hero_Lacerta", "lacerta.fourth", "連装", "Double Load", Stat.FourthAttackShift, MemoryDamageBalance.Effect_legacy_h_lacerta_fourth_stat_perRank),
            Node("Hero_Lacerta", "lacerta.powder", "火薬", "Gunpowder", Stat.FireAmp, MemoryDamageBalance.Effect_legacy_h_lacerta_powder_stat_perRank),
            Node("Hero_Lacerta", "lacerta.range", "防弾の外套", "Armored Coat", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_lacerta_range_stat_perRank),
            Node("Hero_Lacerta", "lacerta.rapid", "速射", "Rapid Fire", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_lacerta_rapid_stat_perRank),
            Key("Hero_Lacerta", "lacerta.key", "サラマンダーの火種", "Salamander Ember", Power.Ember, MemoryDamageBalance.Effect_legacy_h_lacerta_key_power_perRank,
                "通常攻撃で敵を燃やしておくと、サラマンダーパウダーの4発目の爆発が2倍になる。", "Keep foes burning with basic attacks so Salamander Powder's 4th-shot blast deals double."),
            Node("Hero_Lacerta", "lacerta.crit", "狙撃", "Marksman", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_lacerta_crit_stat_perRank),
            Key("Hero_Lacerta", "lacerta.key2", "一番手の銃声", "Opening Shot", Power.OpeningStrike, MemoryDamageBalance.Effect_legacy_h_lacerta_key2_power_perRank,
                "離れた所から先に撃つほど活きる。", "Rewards striking first from afar."),
            DeepNode("Hero_Lacerta", "lacerta.deep.atk", "火薬の調合", "Powder Mixing", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_lacerta_deep_atk_stat_perRank),
            DeepNode("Hero_Lacerta", "lacerta.deep.crit", "照準", "Steady Aim", Stat.CritDamagePct, MemoryDamageBalance.Effect_legacy_h_lacerta_deep_crit_stat_perRank),
            DeepNode("Hero_Lacerta", "lacerta.deep.haste", "鉄の銃床", "Iron Stock", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_lacerta_deep_haste_stat_perRank),
            DeepPower("Hero_Lacerta", "lacerta.deep.blaze", "炸裂弾", "Explosive Rounds", Power.Blaze, MemoryDamageBalance.Effect_legacy_h_lacerta_deep_blaze_power_perRank),
            DeepPower("Hero_Lacerta", "lacerta.deep.ember", "耐火の外套", "Fireproof Coat", Power.Aegis, MemoryDamageBalance.Effect_legacy_h_lacerta_deep_ember_power_perRank),
            DeepPower("Hero_Lacerta", "lacerta.deep.opening", "初弾必中", "First Shot", Power.OpeningStrike, MemoryDamageBalance.Effect_legacy_h_lacerta_deep_opening_power_perRank),

            Node("Hero_Cetus", "cetus.cold", "深海の冷気", "Abyssal Cold", Stat.ColdAmp, MemoryDamageBalance.Effect_legacy_h_cetus_cold_stat_perRank),
            Node("Hero_Cetus", "cetus.shell", "氷の殻", "Ice Shell", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_cetus_shell_stat_perRank),
            Node("Hero_Cetus", "cetus.tide", "潮の呼吸", "Tidal Breath", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_cetus_tide_stat_perRank),
            Node("Hero_Cetus", "cetus.steady", "深海の魔力", "Abyssal Power", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_cetus_steady_stat_perRank),
            Key("Hero_Cetus", "cetus.key", "凍てつく潮", "Freezing Tide", Power.Frost, MemoryDamageBalance.Effect_legacy_h_cetus_key_power_perRank,
                "氷の血脈のスタックと噛み合う。", "Feeds your Icy Veins stacks."),
            Node("Hero_Cetus", "cetus.regen", "潮の鱗", "Tidal Scales", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_cetus_regen_stat_perRank),
            Key("Hero_Cetus", "cetus.key2", "凪", "Calm Sea", Power.StillWater, MemoryDamageBalance.Effect_legacy_h_cetus_key2_power_perRank,
                "技で敵を止めるたびに、障壁を張り直す。", "Each time your skills stun a foe, your shield is renewed."),
            DeepNode("Hero_Cetus", "cetus.deep.power", "深淵の知", "Abyssal Wisdom", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_cetus_deep_power_stat_perRank),
            DeepNode("Hero_Cetus", "cetus.deep.armor", "氷の守り", "Ice Ward", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_cetus_deep_armor_stat_perRank),
            DeepNode("Hero_Cetus", "cetus.deep.weight", "深海の器", "Vessel of the Deep", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_cetus_deep_weight_stat_perRank),
            DeepPower("Hero_Cetus", "cetus.deep.frost", "満ち潮", "High Tide", Power.Overload, MemoryDamageBalance.Effect_legacy_h_cetus_deep_frost_power_perRank),
            DeepPower("Hero_Cetus", "cetus.deep.shatter", "氷の枷", "Ice Shackles", Power.Fetters, MemoryDamageBalance.Effect_legacy_h_cetus_deep_shatter_power_perRank),
            DeepPower("Hero_Cetus", "cetus.deep.aegis", "氷壁", "Ice Wall", Power.Aegis, MemoryDamageBalance.Effect_legacy_h_cetus_deep_aegis_power_perRank),

            Node("Hero_Yubar", "yubar.haste", "星の加速", "Stellar Haste", Stat.Haste, MemoryDamageBalance.Effect_legacy_h_yubar_haste_stat_perRank),
            Node("Hero_Yubar", "yubar.light", "星明かり", "Starlight", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_yubar_light_stat_perRank),
            Node("Hero_Yubar", "yubar.power", "宇宙の理", "Cosmic Law", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_yubar_power_stat_perRank),
            Node("Hero_Yubar", "yubar.reach", "星の脈動", "Stellar Pulse", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_yubar_reach_stat_perRank),
            Key("Hero_Yubar", "yubar.key", "収束する星", "Converging Light", Power.UltimateSurge, MemoryDamageBalance.Effect_legacy_h_yubar_key_power_perRank,
                "エキゾチック物質を溜めてから奥義を撃つ流れと合わせる。", "Pair it with charging Exotic Matter before your Ultimate."),
            Node("Hero_Yubar", "yubar.crit", "星の器", "Stellar Vessel", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_yubar_crit_stat_perRank),
            Key("Hero_Yubar", "yubar.key2", "星の盾", "Stellar Ward", Power.StarShield, MemoryDamageBalance.Effect_legacy_h_yubar_key2_power_perRank,
                "奥義を撃つ瞬間の守りを固める。", "Shields you the moment you unleash your Ultimate."),
            DeepNode("Hero_Yubar", "yubar.deep.critdmg", "超新星", "Supernova", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_yubar_deep_critdmg_stat_perRank),
            DeepNode("Hero_Yubar", "yubar.deep.life", "星の鎧", "Star Armor", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_yubar_deep_life_stat_perRank),
            DeepNode("Hero_Yubar", "yubar.deep.move", "星の導き", "Guiding Star", Stat.CritDamagePct, MemoryDamageBalance.Effect_legacy_h_yubar_deep_move_stat_perRank),
            DeepPower("Hero_Yubar", "yubar.deep.surge", "重力の昂り", "Gravity Surge", Power.UltimateSurge, MemoryDamageBalance.Effect_legacy_h_yubar_deep_surge_power_perRank),
            DeepPower("Hero_Yubar", "yubar.deep.shield", "事象の地平", "Event Horizon", Power.StarShield, MemoryDamageBalance.Effect_legacy_h_yubar_deep_shield_power_perRank),
            DeepPower("Hero_Yubar", "yubar.deep.radiance", "星屑の輝き", "Stardust Glow", Power.Radiance, MemoryDamageBalance.Effect_legacy_h_yubar_deep_radiance_power_perRank),

            Node("Hero_Husk", "husk.dark", "深い影", "Deep Shadow", Stat.DarkAmp, MemoryDamageBalance.Effect_legacy_h_husk_dark_stat_perRank),
            Node("Hero_Husk", "husk.critdmg", "急所", "Vital Strike", Stat.CritDamagePct, MemoryDamageBalance.Effect_legacy_h_husk_critdmg_stat_perRank),
            Node("Hero_Husk", "husk.swift", "連撃", "Flurry", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_husk_swift_stat_perRank),
            Node("Hero_Husk", "husk.crit", "空ろな鎧", "Hollow Armor", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_husk_crit_stat_perRank),
            Key("Hero_Husk", "husk.key", "虚ろの刃", "Hollow Blade", Power.Umbra, MemoryDamageBalance.Effect_legacy_h_husk_key_power_perRank,
                "通常攻撃のたびに闇が重なり、会心ならもう1つ重なる。", "Every hit layers Dark, and a critical hit adds one more."),
            Node("Hero_Husk", "husk.atk", "空洞の牙", "Hollow Fang", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_husk_atk_stat_perRank),
            Key("Hero_Husk", "husk.key2", "瞬歩の刃", "Flash-Step Blade", Power.ShadowStep, MemoryDamageBalance.Effect_legacy_h_husk_key2_power_perRank,
                 $"回避・ダッシュ・瞬間移動の後3秒以内の次の通常攻撃に、攻撃力・魔力の高い方の{MemoryDamageBalance.Effect_legacy_h_husk_key2_power_perRank}%分のダメージを上乗せする（重ならず、移動するたびに時間を延長）。「一歩一殺」の確定会心や「風の傷」と組み合わせ、移動から一撃へつなげる。", $"After a dodge, dash or teleport, your next basic attack within 3s deals +{MemoryDamageBalance.Effect_legacy_h_husk_key2_power_perRank}% of the higher of AD or AP (does not stack; each movement refreshes it). Pair it with One Step, One Kill's guaranteed critical hit and Scar of the Wind to turn movement into a decisive strike."),
            DeepNode("Hero_Husk", "husk.deep.speed", "影の牙", "Shadow Fang", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_husk_deep_speed_stat_perRank),
            DeepNode("Hero_Husk", "husk.deep.tenacity", "空ろな器", "Hollow Vessel", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_husk_deep_tenacity_stat_perRank),
            DeepNode("Hero_Husk", "husk.deep.haste", "闇の深み", "Depth of Darkness", Stat.DarkAmp, MemoryDamageBalance.Effect_legacy_h_husk_deep_haste_stat_perRank),
            DeepPower("Hero_Husk", "husk.deep.umbra", "闇を纏う", "Shroud of Dark", Power.Umbra, MemoryDamageBalance.Effect_legacy_h_husk_deep_umbra_power_perRank),
            DeepPower("Hero_Husk", "husk.deep.exec", "とどめの刃", "Finishing Blade", Power.Executioner, MemoryDamageBalance.Effect_legacy_h_husk_deep_exec_power_perRank),
            DeepPower("Hero_Husk", "husk.deep.momentum", "狩りの勢い", "Hunting Momentum", Power.Momentum, MemoryDamageBalance.Effect_legacy_h_husk_deep_momentum_power_perRank),

            Node("Hero_Mist", "mist.dance", "剣舞", "Blade Dance", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_mist_dance_stat_perRank),
            Node("Hero_Mist", "mist.read", "剣気", "Sword Spirit", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_mist_read_stat_perRank),
            Node("Hero_Mist", "mist.crit", "一閃", "Flash Cut", Stat.CritDamagePct, MemoryDamageBalance.Effect_legacy_h_mist_crit_stat_perRank),
            Node("Hero_Mist", "mist.duel", "決闘者", "Duelist", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_mist_duel_stat_perRank),
            Key("Hero_Mist", "mist.key", "先手の型", "Form of the First Strike", Power.OpeningStrike, MemoryDamageBalance.Effect_legacy_h_mist_key_power_perRank,
                "無傷の敵への最初の一撃が重くなり、各敵への最初の数撃を強くする戦い方と噛み合う。", "Your opening blow on an unhurt foe hits harder, pairing with styles that empower the first strikes on each enemy."),
            Node("Hero_Mist", "mist.haste", "構えの守り", "Guarded Stance", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_mist_haste_stat_perRank),
            Key("Hero_Mist", "mist.key2", "受けて返す型", "Form of the Counter", Power.Retaliation, MemoryDamageBalance.Effect_legacy_h_mist_key2_power_perRank,
                "攻撃を受けた直後の3秒間、攻撃力と魔力が上がる。", "For 3s after taking a hit, your attack and ability power rise."),
            DeepNode("Hero_Mist", "mist.deep.critdmg", "鋭い切っ先", "Keen Point", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_mist_deep_critdmg_stat_perRank),
            DeepNode("Hero_Mist", "mist.deep.life", "鍛えた体", "Trained Body", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_mist_deep_life_stat_perRank),
            DeepPower("Hero_Mist", "mist.deep.stance", "受け流しの構え", "Parry Stance", Power.StillWater, MemoryDamageBalance.Effect_legacy_h_mist_deep_stance_power_perRank),
            DeepNode("Hero_Mist", "mist.deep.whirl", "傑作の盾", "Masterwork Shield", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_mist_deep_whirl_stat_perRank),
            DeepPower("Hero_Mist", "mist.deep.tail", "先手の構え", "First-Strike Stance", Power.OpeningStrike, MemoryDamageBalance.Effect_legacy_h_mist_deep_tail_power_perRank),
            DeepPower("Hero_Mist", "mist.deep.frenzy", "受けて返す", "Take and Return", Power.Retaliation, MemoryDamageBalance.Effect_legacy_h_mist_deep_frenzy_power_perRank),

            Node("Hero_Nachia", "nachia.light", "祝福の光", "Blessed Light", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_nachia_light_stat_perRank),
            Node("Hero_Nachia", "nachia.power", "絆の力", "Bond Power", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_nachia_power_stat_perRank),
            Node("Hero_Nachia", "nachia.haste", "呼び声", "Calling", Stat.Haste, MemoryDamageBalance.Effect_legacy_h_nachia_haste_stat_perRank),
            Node("Hero_Nachia", "nachia.ward", "守護の誓い", "Guardian Vow", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_nachia_ward_stat_perRank),
            Key("Hero_Nachia", "nachia.key", "共に在る", "Together", Power.Resonance, MemoryDamageBalance.Effect_legacy_h_nachia_key_power_perRank,
                "仲間と一緒に戦うほど活きる。", "Best alongside your allies."),
            Node("Hero_Nachia", "nachia.move", "群れの足並み", "Pack Pace", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_nachia_move_stat_perRank),
            Key("Hero_Nachia", "nachia.key2", "祈りの過負荷", "Overflowing Prayer", Power.Overload, MemoryDamageBalance.Effect_legacy_h_nachia_key2_power_perRank,
                "スキルを続けて使う戦い方に向く。", "Suits chaining your skills."),
            DeepNode("Hero_Nachia", "nachia.deep.regen", "癒やしの歌", "Healing Song", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_nachia_deep_regen_stat_perRank),
            DeepNode("Hero_Nachia", "nachia.deep.tenacity", "仲間を想う心", "Thinking of Friends", Stat.SummonPower, MemoryDamageBalance.Effect_legacy_h_nachia_deep_tenacity_stat_perRank),
            DeepNode("Hero_Nachia", "nachia.deep.armor", "盾の祈り", "Prayer of Shields", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_nachia_deep_armor_stat_perRank),
            DeepPower("Hero_Nachia", "nachia.deep.resonance", "響き合う心", "Hearts in Tune", Power.Resonance, MemoryDamageBalance.Effect_legacy_h_nachia_deep_resonance_power_perRank),
            DeepPower("Hero_Nachia", "nachia.deep.barrier", "守りの灯", "Guarding Light", Power.OverflowingLife, MemoryDamageBalance.Effect_legacy_h_nachia_deep_barrier_power_perRank),
            DeepPower("Hero_Nachia", "nachia.deep.second", "灯を絶やさず", "Keep the Light", Power.StillWater, MemoryDamageBalance.Effect_legacy_h_nachia_deep_second_power_perRank),

            Node("Hero_Aurena", "aurena.life", "羽ばたき", "Wingbeat", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_aurena_life_stat_perRank),
            Node("Hero_Aurena", "aurena.light", "黄金の光", "Golden Light", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_aurena_light_stat_perRank),
            Node("Hero_Aurena", "aurena.regen", "聖杯の守り", "Chalice Guard", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_aurena_regen_stat_perRank),
            Node("Hero_Aurena", "aurena.power", "献身", "Devotion", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_aurena_power_stat_perRank),
            Key("Hero_Aurena", "aurena.key", "血の献身", "Blood Devotion", Power.Overload, MemoryDamageBalance.Effect_legacy_h_aurena_key_power_perRank,
                "HPを捧げる技を使うたびに、魔力が高まる。", "Each HP-sacrifice skill you cast sharpens your ability power."),
            Node("Hero_Aurena", "aurena.armor", "黄金の爪", "Golden Claw", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_aurena_armor_stat_perRank),
            Key("Hero_Aurena", "aurena.key2", "満ちた聖杯", "Brimming Chalice", Power.OverflowingLife, MemoryDamageBalance.Effect_legacy_h_aurena_key2_power_perRank,
                "仲間と自分を癒やしてあふれた分が、障壁になる。", "Healing that overflows becomes a shield."),
            DeepNode("Hero_Aurena", "aurena.deep.haste", "再生の循環", "Cycle of Renewal", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_aurena_deep_haste_stat_perRank),
            DeepNode("Hero_Aurena", "aurena.deep.flat", "揺るがぬ祈り", "Steadfast Prayer", Stat.SacrificeReduction, MemoryDamageBalance.Effect_legacy_h_aurena_deep_flat_stat_perRank),
            DeepNode("Hero_Aurena", "aurena.deep.crit", "聖杯の叡智", "Chalice Wisdom", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_aurena_deep_crit_stat_perRank),
            DeepPower("Hero_Aurena", "aurena.deep.lifesteal", "血の羽ばたき", "Bloodied Wings", Power.Bloodlust, MemoryDamageBalance.Effect_legacy_h_aurena_deep_lifesteal_power_perRank),
            DeepPower("Hero_Aurena", "aurena.deep.siphon", "命を啜る", "Drink of Life", Power.SoulSiphon, MemoryDamageBalance.Effect_legacy_h_aurena_deep_siphon_power_perRank),
            DeepPower("Hero_Aurena", "aurena.deep.bloodlust", "最後の祈り", "Last Prayer", Power.SecondWind, MemoryDamageBalance.Effect_legacy_h_aurena_deep_bloodlust_power_perRank),

            Node("Hero_Bismuth", "bismuth.fire", "炎の章", "Chapter of Flame", Stat.FireAmp, MemoryDamageBalance.Effect_legacy_h_bismuth_fire_stat_perRank),
            Node("Hero_Bismuth", "bismuth.light", "速読", "Speed Reading", Stat.Haste, MemoryDamageBalance.Effect_legacy_h_bismuth_light_stat_perRank),
            Node("Hero_Bismuth", "bismuth.dark", "厚い装丁", "Thick Binding", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_bismuth_dark_stat_perRank),
            Node("Hero_Bismuth", "bismuth.story", "物語の続き", "Next Chapter", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_bismuth_story_stat_perRank),
            Key("Hero_Bismuth", "bismuth.key", "四つの物語", "Four Tales", Power.Convergence, MemoryDamageBalance.Effect_legacy_h_bismuth_key_power_perRank,
                "本ごとの属性を重ねて発動させる。", "Layer each book's element to trigger it."),
            Node("Hero_Bismuth", "bismuth.cold", "めくる手", "Turning Pages", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_bismuth_cold_stat_perRank),
            Key("Hero_Bismuth", "bismuth.key2", "雷鳴の章", "Chapter of Thunder", Power.ChainLightning, MemoryDamageBalance.Effect_legacy_h_bismuth_key2_power_perRank,
                "本の魔法に雷を添えて、群れを一度に削る。", "Adds lightning to thin out packs."),
            DeepNode("Hero_Bismuth", "bismuth.deep.power", "博識", "Erudition", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_bismuth_deep_power_stat_perRank),
            DeepNode("Hero_Bismuth", "bismuth.deep.crit", "光と闇の章", "Chapters of Light and Dark", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_bismuth_deep_crit_stat_perRank),
            DeepNode("Hero_Bismuth", "bismuth.deep.life", "頁の守り", "Page Ward", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_bismuth_deep_life_stat_perRank),
            DeepPower("Hero_Bismuth", "bismuth.deep.ember", "表紙の守り", "Cover Guard", Power.Barrier, MemoryDamageBalance.Effect_legacy_h_bismuth_deep_ember_power_perRank),
            DeepPower("Hero_Bismuth", "bismuth.deep.frost", "氷の頁", "Page of Ice", Power.Frost, MemoryDamageBalance.Effect_legacy_h_bismuth_deep_frost_power_perRank),
            DeepPower("Hero_Bismuth", "bismuth.deep.umbra", "闇の頁", "Page of Shadow", Power.Umbra, MemoryDamageBalance.Effect_legacy_h_bismuth_deep_umbra_power_perRank),
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
