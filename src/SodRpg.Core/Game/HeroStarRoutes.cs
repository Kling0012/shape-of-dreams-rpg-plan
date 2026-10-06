using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 日本語の記憶・旅人・星座データを照合した記憶別ルート。
    /// Bismuthの固有のアイデンティティは1つ（プリズムの視界）なので、v1.28で共通のアイデンティティ「華麗なる芸術家」のルートを外した。合計62ルート・434星と夢の輪72星。
    /// v1.28：ルートの「同調」は「記憶の冴え」（その記憶で与えるダメージ+X%）に置き換え、「守り」は盾役の記憶とダメージを出さない記憶のルートだけに残す。
    /// v1.28：仕掛けのあるルート（54ルート）は星2・星4・頂点が「記憶の仕掛け」を持つ（設計表 docs/specs/v1.28-memory-gimmicks-table.md）。
    /// 星2・星4は仕掛け専用の星（能力値・連携・固有効果なし）。頂点は「記憶の冴え」と仕掛けを両方持ってよい。
    /// Contentの初期化中に登録されるため、構築時にContentの値は参照しない。
    /// </summary>
    public static class HeroStarRoutes
    {
        public static readonly IReadOnlyList<TalentDef> All = Create();

        private struct Route
        {
            private readonly List<TalentDef> nodes;
            private readonly string hero, id, memory;
            private int order;

            public Route(List<TalentDef> nodes, string hero, string slug, string memory)
            {
                this.nodes = nodes;
                this.hero = "Hero_" + hero;
                id = "h." + hero.ToLowerInvariant() + ".route." + slug;
                this.memory = memory;
                order = 0;
            }

            private string NextId => id + "." + (order + 1);
            private int NextRanks => order == 6 ? 1 : 3;

            private void Add(TalentDef node)
            {
                node.HeroKey = hero;
                node.Tier = 2;
                node.RouteId = id;
                node.RouteMemory = memory;
                node.RouteOrder = ++order;
                node.RankCost = order == 7 ? 3 : 1;
                nodes.Add(node);
            }

            public void S(string ja, string en, Stat stat, int value)
                => Add(new TalentDef(NextId, Line.Offense, new Txt(ja, en), stat, value, NextRanks));

            public void P(string ja, string en, Power power, int value)
                => Add(new TalentDef(NextId, Line.Offense, new Txt(ja, en), power, value, NextRanks));

            public void L(string ja, string en, LinkKind kind, decimal value)
            {
                if (kind != LinkKind.MemoryDamage)
                {
                    if (value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                        throw new InvalidOperationException("Route " + NextId + ": non-MemoryDamage link values must be exact Int32 integers.");
                    value = checked((int)value);
                }
                Add(new TalentDef(NextId, Line.Offense, new Txt(ja, en),
                    new LinkDef { Kind = kind, Value = value, Requires = new[] { memory } }, NextRanks));
            }

            /// <summary>仕掛けの星（v1.28）。能力値・連携・固有効果を持たず、Buildの予算にも数えない。</summary>
            public void G(string ja, string en, GimmickTrigger trigger, GimmickEffect effect, int value, int arg = 0, float cooldown = 0f)
                => Add(new TalentDef(NextId, Line.Offense, new Txt(ja, en), default(Stat), 0, NextRanks)
                {
                    Gimmick = new GimmickDef { Trigger = trigger, Effect = effect, Value = value, Arg = arg, Cooldown = cooldown },
                });

            /// <summary>頂点の星。「記憶の冴え」（keen、既存の値）と仕掛けを両方持つ。</summary>
            public void CapG(string ja, string en, decimal keen, GimmickTrigger trigger, GimmickEffect effect, int value, int arg = 0, float cooldown = 0f)
                => Add(new TalentDef(NextId, Line.Offense, new Txt(ja, en),
                    new LinkDef { Kind = LinkKind.MemoryDamage, Value = keen, Requires = new[] { memory } }, NextRanks)
                {
                    Gimmick = new GimmickDef { Trigger = trigger, Effect = effect, Value = value, Arg = arg, Cooldown = cooldown },
                });
        }

        private static IReadOnlyList<TalentDef> Create()
        {
            var nodes = new List<TalentDef>(506);
            Route r;

            // Vesper: HP-based resolve/charge, AD discipline, AP channel and sanctuary.
            r = new Route(nodes, "Vesper", "resolve", "St_D_Resolve");
            r.L("不退の誓い", "Unyielding Oath", LinkKind.Guard, 2);
            r.G("威圧の一撃", "Intimidating Strike", GimmickTrigger.OnCrit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_vesper_route_resolve_2_gimmick_value);
            r.L("跳ね除ける一撃の冴え", "Keen Repelling Blow", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_resolve_3_link_value);
            r.G("決意の障壁", "Barrier of Resolve", GimmickTrigger.OnCrit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_vesper_route_resolve_4_gimmick_value);
            r.L("揺るがぬ足場", "Unshaken Ground", LinkKind.Guard, 1);
            r.P("決意の鉄槌", "Hammer of Resolve", Power.Fetters, MemoryDamageBalance.Effect_legacy_h_vesper_route_resolve_6_power_perRank);
            r.G("不動の誓い", "Unmoved Vow", GimmickTrigger.OnCrit, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_vesper_route_resolve_7_gimmick_value, 1);

            r = new Route(nodes, "Vesper", "mercy", "St_D_MercyOfEl");
            r.L("四撃の聖光の冴え", "Keen Fourth-Shot Light", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_mercy_1_link_value);
            r.G("光の兆し", "Sign of Light", GimmickTrigger.OnCrit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_vesper_route_mercy_2_gimmick_value, 2);
            r.S("慈悲の連打", "Merciful Flurry", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_vesper_route_mercy_3_stat_perRank);
            r.G("慈悲の残光", "Lingering Mercy", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_vesper_route_mercy_4_gimmick_value);
            r.L("重なる連撃の冴え", "Keen Stacking Flurry", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_mercy_5_link_value);
            r.S("聖光の芯", "Heart of Holy Light", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_vesper_route_mercy_6_stat_perRank);
            r.CapG("慈悲の分け与え", "Shared Mercy", MemoryDamageBalance.Effect_h_vesper_route_mercy_7_link_value, GimmickTrigger.OnCrit, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_vesper_route_mercy_7_gimmick_value, 1);

            r = new Route(nodes, "Vesper", "charge", "St_M_Charge");
            r.L("盾の助走", "Shielded Run-Up", LinkKind.Guard, 2);
            r.G("衝突の隙", "Impact Opening", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_vesper_route_charge_2_gimmick_value);
            r.L("突進の呼吸", "Charging Breath", LinkKind.Guard, 1);
            r.G("衝突の火花", "Impact Sparks", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_vesper_route_charge_4_gimmick_value);
            r.L("体当たりの冴え", "Keen Ramming Blow", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_charge_5_link_value);
            r.S("重装の盾", "Heavy Bulwark", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_vesper_route_charge_6_stat_perRank);
            r.G("盾突きの構え", "Shield Bash Stance", GimmickTrigger.OnHit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_vesper_route_charge_7_gimmick_value);

            r = new Route(nodes, "Vesper", "cruel-sun", "St_Q_CruelSun");
            r.L("日輪の蓄熱", "Solar Heat Reserve", LinkKind.MemorySurge, 2);
            r.G("日輪の火種", "Solar Ember", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_vesper_route_cruel_sun_2_gimmick_value);
            r.L("日輪の打撃の冴え", "Keen Solar Smash", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_cruel_sun_3_link_value);
            r.G("日輪の還り", "Return of the Sun", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_vesper_route_cruel_sun_4_gimmick_value);
            r.P("灼ける裁き", "Searing Judgment", Power.Fetters, MemoryDamageBalance.Effect_legacy_h_vesper_route_cruel_sun_5_power_perRank);
            r.S("太陽を支える体", "Sun-Bearing Body", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_vesper_route_cruel_sun_6_stat_perRank);
            r.G("太陽の余波", "Solar Aftermath", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_vesper_route_cruel_sun_7_gimmick_value);

            r = new Route(nodes, "Vesper", "discipline", "St_Q_Discipline");
            r.L("律動の再起", "Rhythmic Renewal", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_vesper_route_discipline_1_link_value);
            r.G("律の光", "Light of Order", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_vesper_route_discipline_2_gimmick_value, 2);
            r.L("踏み込む決意", "Resolute Advance", LinkKind.MemorySurge, 2);
            r.G("律する二撃", "Disciplined Double", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_vesper_route_discipline_4_gimmick_value);
            r.L("踏み込む打撃の冴え", "Keen Advancing Smash", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_discipline_5_link_value);
            r.S("連打の息吹", "Breath of Blows", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_vesper_route_discipline_6_stat_perRank);
            r.G("裁きの再突撃", "Judgment Recharge", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_vesper_route_discipline_7_gimmick_value);

            r = new Route(nodes, "Vesper", "baptism", "St_R_BaptismOfSun");
            r.L("陽炎の余力", "Heat Haze Reserve", LinkKind.MemorySurge, 2);
            r.G("洗礼の薄衣", "Veil of Baptism", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_vesper_route_baptism_2_gimmick_value);
            r.L("誘いの鎧", "Challenger's Armor", LinkKind.Guard, 2);
            r.G("陽炎の火種", "Heat Haze Ember", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_vesper_route_baptism_4_gimmick_value);
            r.L("洗礼の巡り", "Baptism Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_vesper_route_baptism_5_link_value);
            r.S("爆心の魔力", "Blast Core Power", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_vesper_route_baptism_6_stat_perRank);
            r.G("洗礼の昂ぶり", "Fervor of Baptism", GimmickTrigger.OnUse, GimmickEffect.Empower, MemoryDamageBalance.Effect_legacy_h_vesper_route_baptism_7_gimmick_value);

            r = new Route(nodes, "Vesper", "sanctuary", "St_R_SanctuaryOfEl");
            r.L("聖域の爆光の冴え", "Keen Holy Burst", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_sanctuary_1_link_value);
            r.G("光に晒される", "Exposed by Light", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_vesper_route_sanctuary_2_gimmick_value);
            r.L("降り注ぐ祝福の冴え", "Keen Descending Blessing", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_vesper_route_sanctuary_3_link_value);
            r.G("聖域の慈雨", "Sanctuary Rain", GimmickTrigger.OnUse, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_vesper_route_sanctuary_4_gimmick_value, 1);
            r.L("聖域の門", "Sanctuary Gate", LinkKind.Guard, 1);
            r.S("聖域を包む光", "Light Enfolding the Sanctum", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_vesper_route_sanctuary_6_stat_perRank);
            r.G("聖域の連携", "Sanctuary Link", GimmickTrigger.OnUse, GimmickEffect.RechargeOther, MemoryDamageBalance.Effect_legacy_h_vesper_route_sanctuary_7_gimmick_value);

            // Lacerta: distinguish AD double shots from AP fire; defense fills the kit's weakness.
            r = new Route(nodes, "Lacerta", "double-tap", "St_D_DoubleTap");
            r.L("二連射の冴え", "Keen Double Shot", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_double_tap_1_link_value);
            r.G("二射の残響", "Twin-Shot Echo", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_lacerta_route_double_tap_2_gimmick_value);
            r.L("重なる弾道の冴え", "Keen Twin Trajectories", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_double_tap_3_link_value);
            r.G("火の弾道", "Fire Trajectory", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_lacerta_route_double_tap_4_gimmick_value);
            r.L("追い撃ちの冴え", "Keen Follow-Up Shot", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_double_tap_5_link_value);
            r.S("反動の受け身", "Recoil Brace", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_lacerta_route_double_tap_6_stat_perRank);
            r.CapG("速射の連携", "Rapid Link", MemoryDamageBalance.Effect_h_lacerta_route_double_tap_7_link_value, GimmickTrigger.OnHit, GimmickEffect.RechargeOther, MemoryDamageBalance.Effect_legacy_h_lacerta_route_double_tap_7_gimmick_value);

            r = new Route(nodes, "Lacerta", "powder", "St_D_SalamanderPowder");
            r.L("発火の冴え", "Keen Ignition", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_powder_1_link_value);
            r.G("火薬の種火", "Powder Spark", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_lacerta_route_powder_2_gimmick_value);
            r.L("四射目の炸裂の冴え", "Keen Fourth-Shot Blast", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_powder_3_link_value);
            r.G("火薬庫の誘爆", "Powder Keg Sympathy", GimmickTrigger.OnCrit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_lacerta_route_powder_4_gimmick_value);
            r.L("燃え広がる粉の冴え", "Keen Spreading Powder", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_powder_5_link_value);
            r.S("燃焼の濃度", "Burning Intensity", Stat.FireAmp, MemoryDamageBalance.Effect_legacy_h_lacerta_route_powder_6_stat_perRank);
            r.CapG("延焼", "Wildfire", MemoryDamageBalance.Effect_h_lacerta_route_powder_7_link_value, GimmickTrigger.OnKill, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_lacerta_route_powder_7_gimmick_value);

            r = new Route(nodes, "Lacerta", "nimble-dodge", "St_M_NimbleDodge");
            r.L("射線の離脱", "Leaving the Firing Line", LinkKind.Guard, 1);
            r.S("返し撃ちの腕", "Countershot Arm", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_lacerta_route_nimble_dodge_2_stat_perRank);
            r.L("回避の仕切り直し", "Dodge Reset", LinkKind.Guard, 1);
            r.P("跳び撃ちの止め", "Leaping Finisher", Power.Executioner, MemoryDamageBalance.Effect_legacy_h_lacerta_route_nimble_dodge_4_power_perRank);
            r.L("回避の構え", "Evasive Stance", LinkKind.Guard, 1);
            r.S("身軽な防弾服", "Light Ballistic Vest", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_lacerta_route_nimble_dodge_6_stat_perRank);
            r.L("滑らかな回避", "Silken Dodge", LinkKind.Guard, 6);

            r = new Route(nodes, "Lacerta", "hand-cannon", "St_Q_HandCannon");
            r.L("砲口の熱", "Muzzle Heat", LinkKind.MemorySurge, 2);
            r.G("至近の弱点", "Point-Blank Weakness", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_lacerta_route_hand_cannon_2_gimmick_value);
            r.L("砲声の冴え", "Keen Cannon Roar", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_hand_cannon_3_link_value);
            r.G("弾込めの手際", "Quick Reload", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_lacerta_route_hand_cannon_4_gimmick_value);
            r.L("砲身の冷却", "Barrel Cooling", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_lacerta_route_hand_cannon_5_link_value);
            r.S("火炎砲の芯", "Flame Cannon Core", Stat.FireAmp, MemoryDamageBalance.Effect_legacy_h_lacerta_route_hand_cannon_6_stat_perRank);
            r.G("二重の火薬", "Double Powder", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_lacerta_route_hand_cannon_7_gimmick_value);

            r = new Route(nodes, "Lacerta", "incendiary", "St_Q_IncendiaryRounds");
            r.L("焼夷の手際", "Incendiary Handling", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_lacerta_route_incendiary_1_link_value);
            r.G("装填の余裕", "Reload Composure", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_lacerta_route_incendiary_2_gimmick_value);
            r.L("火線の余熱", "Firing Line Heat", LinkKind.MemorySurge, 2);
            r.G("焼夷の誘爆", "Incendiary Detonation", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_lacerta_route_incendiary_4_gimmick_value);
            r.P("焼夷の追い火", "Incendiary Follow-up", Power.Ember, MemoryDamageBalance.Effect_legacy_h_lacerta_route_incendiary_5_power_perRank);
            r.S("焼夷の濃度", "Incendiary Concentrate", Stat.FireAmp, MemoryDamageBalance.Effect_legacy_h_lacerta_route_incendiary_6_stat_perRank);
            r.G("弾薬補給", "Ammo Resupply", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_lacerta_route_incendiary_7_gimmick_value);

            r = new Route(nodes, "Lacerta", "precision", "St_R_PrecisionShot");
            r.L("狙いの蓄積", "Gathered Aim", LinkKind.MemorySurge, 2);
            r.G("徹甲の傷", "Armor-Piercing Wound", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_lacerta_route_precision_2_gimmick_value);
            r.L("貫く弾芯の冴え", "Keen Piercing Core", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_precision_3_link_value);
            r.G("照準の木霊", "Aiming Echo", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_lacerta_route_precision_4_gimmick_value);
            r.L("照準の復帰", "Sight Recovery", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_lacerta_route_precision_5_link_value);
            r.P("狙い澄ました一撃", "Steadied Shot", Power.Fetters, MemoryDamageBalance.Effect_legacy_h_lacerta_route_precision_6_power_perRank);
            r.G("必中の再装填", "Sure-Hit Reload", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_lacerta_route_precision_7_gimmick_value);

            r = new Route(nodes, "Lacerta", "quick-trigger", "St_R_QuickTrigger");
            r.L("三射の呼吸", "Triple-Shot Breath", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_lacerta_route_quick_trigger_1_link_value);
            r.G("跳弾", "Ricochet", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_lacerta_route_quick_trigger_2_gimmick_value);
            r.L("抜き撃ちの勢い", "Quickdraw Momentum", LinkKind.MemorySurge, 2);
            r.G("熱を帯びた弾", "Heated Rounds", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_lacerta_route_quick_trigger_4_gimmick_value);
            r.L("三連貫通の冴え", "Keen Triple Pierce", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_lacerta_route_quick_trigger_5_link_value);
            r.S("射撃の持久力", "Shooting Endurance", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_lacerta_route_quick_trigger_6_stat_perRank);
            r.G("三射の再装填", "Triple-Shot Reload", GimmickTrigger.OnKill, GimmickEffect.Reload, MemoryDamageBalance.Effect_legacy_h_lacerta_route_quick_trigger_7_gimmick_value);

            // Cetus: HP feeds bonus-health scaling; cold and skill stuns sustain shields.
            r = new Route(nodes, "Cetus", "icy-veins", "St_D_IcyVeins");
            r.L("冷血の炸裂の冴え", "Keen Coldblood Burst", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_cetus_route_icy_veins_1_link_value);
            r.G("冷血の兆し", "Cold-Blood Sign", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_cetus_route_icy_veins_2_gimmick_value, 1);
            r.P("氷の外殻", "Ice Carapace", Power.Barrier, MemoryDamageBalance.Effect_legacy_h_cetus_route_icy_veins_3_power_perRank);
            r.G("冷血の薄氷", "Cold-Blooded Rime", GimmickTrigger.OnHit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_cetus_route_icy_veins_4_gimmick_value);
            r.L("五連の霜爆の冴え", "Keen Five-Stack Frostburst", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_cetus_route_icy_veins_5_link_value);
            r.S("凍れる脈", "Frozen Pulse", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_cetus_route_icy_veins_6_stat_perRank);
            r.G("冷血の脆化", "Cold Brittleness", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_cetus_route_icy_veins_7_gimmick_value);

            r = new Route(nodes, "Cetus", "charged", "St_D_ChargedAnguillian");
            r.L("蓄電の冴え", "Keen Stored Charge", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_cetus_route_charged_1_link_value);
            r.G("帯電の火花", "Charged Spark", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_cetus_route_charged_2_gimmick_value, 2);
            r.S("帯電の鱗", "Charged Scales", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_cetus_route_charged_3_stat_perRank);
            r.G("雷の余韻", "Thunder Reverb", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_cetus_route_charged_4_gimmick_value);
            r.L("放電の冴え", "Keen Discharge", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_cetus_route_charged_5_link_value);
            r.S("帯電の器", "Charged Vessel", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_cetus_route_charged_6_stat_perRank);
            r.CapG("電光の飛び火", "Lightning Leap", MemoryDamageBalance.Effect_h_cetus_route_charged_7_link_value, GimmickTrigger.OnKill, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_cetus_route_charged_7_gimmick_value, 2);

            r = new Route(nodes, "Cetus", "frost-charge", "St_M_FrostyCharge");
            r.L("氷走りの備え", "Frost Run Readiness", LinkKind.Guard, 1);
            r.G("霜の足跡", "Frost Footprint", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_cetus_route_frost_charge_2_gimmick_value, 1);
            r.L("氷走りの再起", "Frost Run Renewal", LinkKind.Guard, 1);
            r.G("氷走りの薄氷", "Frost-Run Rime", GimmickTrigger.OnHit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_cetus_route_frost_charge_4_gimmick_value);
            r.L("氷突進の冴え", "Keen Frost Ram", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_cetus_route_frost_charge_5_link_value);
            r.S("氷山の質量", "Iceberg Mass", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_cetus_route_frost_charge_6_stat_perRank);
            r.CapG("氷の轍", "Icy Wake", MemoryDamageBalance.Effect_h_cetus_route_frost_charge_7_link_value, GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_cetus_route_frost_charge_7_gimmick_value);

            r = new Route(nodes, "Cetus", "embrace-chill", "St_Q_EmbracingTheChill");
            r.L("冷気の循環", "Chill Circulation", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_cetus_route_embrace_chill_1_link_value);
            r.G("凍土の守り", "Frozen Ground Ward", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_cetus_route_embrace_chill_2_gimmick_value);
            r.L("薄氷の重なり", "Layered Thin Ice", LinkKind.Guard, 2);
            r.G("凍える領域", "Freezing Domain", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_cetus_route_embrace_chill_4_gimmick_value);
            r.L("氷域の余力", "Ice Field Reserve", LinkKind.MemorySurge, 2);
            r.S("氷域の厚み", "Ice Field Depth", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_cetus_route_embrace_chill_6_stat_perRank);
            r.CapG("寒気の癒やし", "Chill's Mercy", MemoryDamageBalance.Effect_h_cetus_route_embrace_chill_7_link_value, GimmickTrigger.OnUse, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_cetus_route_embrace_chill_7_gimmick_value, 1);

            r = new Route(nodes, "Cetus", "boreal-chunk", "St_Q_BigBorealChunk");
            r.L("氷河の蓄積", "Glacier Accumulation", LinkKind.MemorySurge, 2);
            r.G("氷河の種", "Glacier Seed", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_cetus_route_boreal_chunk_2_gimmick_value, 1);
            r.L("崩れ氷の炸裂の冴え", "Keen Collapsing Ice", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_cetus_route_boreal_chunk_3_link_value);
            r.G("氷河の被膜", "Glacier Film", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_cetus_route_boreal_chunk_4_gimmick_value);
            r.L("氷河の再成", "Glacier Renewal", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_cetus_route_boreal_chunk_5_link_value);
            r.S("寒波の深さ", "Depth of the Cold Wave", Stat.ColdAmp, MemoryDamageBalance.Effect_legacy_h_cetus_route_boreal_chunk_6_stat_perRank);
            r.G("氷河の砕片", "Glacier Shatter", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_cetus_route_boreal_chunk_7_gimmick_value);

            r = new Route(nodes, "Cetus", "back-off", "St_R_BackOff");
            r.L("氷盾の備え", "Ice Shield Readiness", LinkKind.Guard, 2);
            r.G("退かせる圧", "Pressure to Back Off", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_cetus_route_back_off_2_gimmick_value);
            r.L("薙ぎ払いの余波", "Sweeping Aftershock", LinkKind.MemorySurge, 2);
            r.G("氷盾の補強", "Reinforced Ice Shield", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_cetus_route_back_off_4_gimmick_value);
            r.L("氷盾の再展開", "Ice Shield Redeployment", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_cetus_route_back_off_5_link_value);
            r.S("仲間を包む氷", "Ice Enfolding Allies", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_cetus_route_back_off_6_stat_perRank);
            r.G("爽快な追い打ち", "Refreshing Follow-Up", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_cetus_route_back_off_7_gimmick_value);

            r = new Route(nodes, "Cetus", "frozen-fists", "St_R_FrozenFists");
            r.L("近接乱打の冴え", "Keen Melee Barrage", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_cetus_route_frozen_fists_1_link_value);
            r.G("凍てつく拳", "Freezing Fist", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_cetus_route_frozen_fists_2_gimmick_value, 1);
            r.L("近接の氷殻", "Melee Ice Shell", LinkKind.Guard, 2);
            r.G("拳の二連", "Double Fist", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_cetus_route_frozen_fists_4_gimmick_value);
            r.L("拳の仕切り直し", "Fist Reset", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_cetus_route_frozen_fists_5_link_value);
            r.S("凍拳の氷盾", "Frozen Fist Shield", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_cetus_route_frozen_fists_6_stat_perRank);
            r.CapG("礼儀の氷膜", "Courtesy Frost", MemoryDamageBalance.Effect_h_cetus_route_frozen_fists_7_link_value, GimmickTrigger.OnKill, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_cetus_route_frozen_fists_7_gimmick_value);

            // Yubar: AP explosions and defensive windows, not more generic crit/haste.
            r = new Route(nodes, "Yubar", "converging-stars", "St_D_ConvergencePoint");
            r.L("星弾の冴え", "Keen Starshot", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_converging_stars_1_link_value);
            r.G("星の目印", "Star Marker", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_yubar_route_converging_stars_2_gimmick_value);
            r.L("跳ねる星屑の冴え", "Keen Ricocheting Stardust", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_converging_stars_3_link_value);
            r.G("跳ね返る星屑", "Rebounding Stardust", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_yubar_route_converging_stars_4_gimmick_value);
            r.L("巡る星芒の冴え", "Keen Orbiting Starlight", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_converging_stars_5_link_value);
            r.S("星弾を継ぐ手", "Starshot Relay", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_yubar_route_converging_stars_6_stat_perRank);
            r.CapG("星の連なり", "Star Chain", MemoryDamageBalance.Effect_h_yubar_route_converging_stars_7_link_value, GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_yubar_route_converging_stars_7_gimmick_value, 2);

            r = new Route(nodes, "Yubar", "exotic-matter", "St_D_ExoticMatter");
            r.L("未知物質の冴え", "Keen Exotic Matter", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_exotic_matter_1_link_value);
            r.G("未知の輝き", "Unknown Glow", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_yubar_route_exotic_matter_2_gimmick_value, 2);
            r.L("凝縮する塊の冴え", "Keen Condensed Mass", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_exotic_matter_3_link_value);
            r.G("未知の重み", "Unknown Weight", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_yubar_route_exotic_matter_4_gimmick_value);
            r.L("四重の光爆の冴え", "Keen Fourfold Lightburst", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_exotic_matter_5_link_value);
            r.S("異質な光", "Exotic Light", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_yubar_route_exotic_matter_6_stat_perRank);
            r.CapG("物質の還流", "Matter Reflux", MemoryDamageBalance.Effect_h_yubar_route_exotic_matter_7_link_value, GimmickTrigger.OnKill, GimmickEffect.RechargeOther, MemoryDamageBalance.Effect_legacy_h_yubar_route_exotic_matter_7_gimmick_value);

            r = new Route(nodes, "Yubar", "flicker", "St_M_Flicker");
            r.L("転移の備え", "Blink Readiness", LinkKind.Guard, 1);
            r.S("転移の星核", "Blink Star Core", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_yubar_route_flicker_2_stat_perRank);
            r.L("光路の再接続", "Lightpath Reconnection", LinkKind.Guard, 1);
            r.P("転移の星光", "Blinking Starlight", Power.Radiance, MemoryDamageBalance.Effect_legacy_h_yubar_route_flicker_4_power_perRank);
            r.L("転移の目くらまし", "Blinding Blink", LinkKind.Guard, 1);
            r.S("薄明の器", "Twilight Vessel", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_yubar_route_flicker_6_stat_perRank);
            r.L("二重の星影", "Double Starshadow", LinkKind.Guard, 6);

            r = new Route(nodes, "Yubar", "ethereal", "St_Q_EtherealInfluence");
            r.L("往復する光", "Returning Light", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_yubar_route_ethereal_1_link_value);
            r.G("光の残り香", "Scent of Light", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_yubar_route_ethereal_2_gimmick_value, 2);
            r.L("爆光の余韻", "Radiant Blast Echo", LinkKind.MemorySurge, 2);
            r.G("往復する残光", "Returning Afterglow", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_yubar_route_ethereal_4_gimmick_value);
            r.L("往復光波の冴え", "Keen Round-Trip Lightwave", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_ethereal_5_link_value);
            r.S("光束の密度", "Light Beam Density", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_yubar_route_ethereal_6_stat_perRank);
            r.G("光の呼び戻し", "Recall of Light", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_yubar_route_ethereal_7_gimmick_value);

            r = new Route(nodes, "Yubar", "supernova", "St_Q_SuperNova");
            r.L("星核の蓄積", "Star Core Accumulation", LinkKind.MemorySurge, 2);
            r.G("星核の殻", "Star Core Shell", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_yubar_route_supernova_2_gimmick_value);
            r.L("星核の爆発の冴え", "Keen Stellar Detonation", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_supernova_3_link_value);
            r.G("超新星の余波", "Supernova Aftershock", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_yubar_route_supernova_4_gimmick_value);
            r.P("超新星の昂り", "Supernova Surge", Power.Overload, MemoryDamageBalance.Effect_legacy_h_yubar_route_supernova_5_power_perRank);
            r.S("星光の圧縮", "Starlight Compression", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_yubar_route_supernova_6_stat_perRank);
            r.G("星の残響", "Starry Reverb", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_yubar_route_supernova_7_gimmick_value);

            r = new Route(nodes, "Yubar", "cataclysm", "St_R_Cataclysm");
            r.L("隕石の軌道の冴え", "Keen Meteor Track", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_cataclysm_1_link_value);
            r.G("火球の兆し", "Sign of the Fireball", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_yubar_route_cataclysm_2_gimmick_value);
            r.L("降下する天罰の冴え", "Keen Falling Judgment", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_yubar_route_cataclysm_3_link_value);
            r.G("隕石の火の粉", "Meteor Sparks", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_yubar_route_cataclysm_4_gimmick_value);
            r.L("隕石の余熱", "Meteor Afterheat", LinkKind.MemorySurge, 2);
            r.S("天体を支える器", "Celestial Vessel", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_yubar_route_cataclysm_6_stat_perRank);
            r.G("天災の連鎖", "Chain of Calamity", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_yubar_route_cataclysm_7_gimmick_value);

            r = new Route(nodes, "Yubar", "tranquility", "St_R_Tranquility");
            r.L("静寂の巡り", "Quiet Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_yubar_route_tranquility_1_link_value);
            r.G("静寂の薄衣", "Veil of Calm", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_yubar_route_tranquility_2_gimmick_value);
            r.L("静寂の後光", "Quiet Halo", LinkKind.MemorySurge, 2);
            r.G("静寂の呼吸", "Breath of Calm", GimmickTrigger.OnUse, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_yubar_route_tranquility_4_gimmick_value);
            r.L("静寂の外殻", "Quiet Shell", LinkKind.Guard, 1);
            r.S("静かな殻", "Quiet Shell", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_yubar_route_tranquility_6_stat_perRank);
            r.G("凪のあとの閃き", "Spark After Calm", GimmickTrigger.OnUse, GimmickEffect.Empower, MemoryDamageBalance.Effect_legacy_h_yubar_route_tranquility_7_gimmick_value);

            // Husk: attack-speed conversion belongs to the identity; Death Mark and the
            // annihilation shockwave explicitly carry AP markers in the shipped data.
            r = new Route(nodes, "Husk", "killing-flow", "St_D_TheKillingFlow");
            r.L("一撃の冴え", "Keen Single Strike", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_killing_flow_1_link_value);
            r.G("殺意の染み", "Taint of Intent", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_husk_route_killing_flow_2_gimmick_value, 3);
            r.L("加速の刃の冴え", "Keen Swiftsteel", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_killing_flow_3_link_value);
            r.G("一歩一殺の残像", "Afterimage of the Flow", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_husk_route_killing_flow_4_gimmick_value);
            r.L("歩みの一閃の冴え", "Keen Striding Flash", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_killing_flow_5_link_value);
            r.S("一歩の剛力", "One-Step Strength", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_husk_route_killing_flow_6_stat_perRank);
            r.CapG("殺気の飛び火", "Spark of Killing Intent", MemoryDamageBalance.Effect_h_husk_route_killing_flow_7_link_value, GimmickTrigger.OnKill, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_husk_route_killing_flow_7_gimmick_value, 3);

            r = new Route(nodes, "Husk", "wind-scar", "St_D_ScarOfTheWind");
            r.L("風刃の冴え", "Keen Wind Blade", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_wind_scar_1_link_value);
            r.G("風傷の闇", "Dark of the Windscar", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_husk_route_wind_scar_2_gimmick_value, 3);
            r.L("切り裂く風の冴え", "Keen Slashing Wind", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_wind_scar_3_link_value);
            r.G("風傷の癒やし", "Windscar Mending", GimmickTrigger.OnHit, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_husk_route_wind_scar_4_gimmick_value);
            r.L("追い風の一閃の冴え", "Keen Tailwind Flash", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_wind_scar_5_link_value);
            r.S("深い風傷", "Deep Wind Scar", Stat.DarkAmp, MemoryDamageBalance.Effect_legacy_h_husk_route_wind_scar_6_stat_perRank);
            r.CapG("風の傷痕", "Wind's Wound", MemoryDamageBalance.Effect_h_husk_route_wind_scar_7_link_value, GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_husk_route_wind_scar_7_gimmick_value);

            r = new Route(nodes, "Husk", "flash-step", "St_M_FlashStep");
            r.L("瞬歩の備え", "Flash Step Readiness", LinkKind.Guard, 1);
            r.S("間合いを断つ腕", "Gap-Cutting Arm", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_husk_route_flash_step_2_stat_perRank);
            r.L("影道の再接続", "Shadowpath Reconnection", LinkKind.Guard, 1);
            r.P("瞬歩の初撃", "Flash-Step Opener", Power.OpeningStrike, MemoryDamageBalance.Effect_legacy_h_husk_route_flash_step_4_power_perRank);
            r.L("残影の目くらまし", "Blinding Afterimage", LinkKind.Guard, 1);
            r.S("影走りの体幹", "Shadow Runner's Core", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_husk_route_flash_step_6_stat_perRank);
            r.L("影走りの極み", "Pinnacle of Shadow Running", LinkKind.Guard, 6);

            r = new Route(nodes, "Husk", "laceration", "St_Q_Laceration");
            r.L("二色の巡り", "Two-Color Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_husk_route_laceration_1_link_value);
            r.G("裂傷の闇", "Shadow of the Slash", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_husk_route_laceration_2_gimmick_value, 3);
            r.L("赤刃の余勢", "Red Blade Momentum", LinkKind.MemorySurge, 3);
            r.G("傷の巡り", "Wound Cycle", GimmickTrigger.OnHit, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_husk_route_laceration_4_gimmick_value);
            r.L("赤青の斬撃の冴え", "Keen Red-Blue Slashes", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_laceration_5_link_value);
            r.S("裂け目の闇", "Darkness in the Rift", Stat.DarkAmp, MemoryDamageBalance.Effect_legacy_h_husk_route_laceration_6_stat_perRank);
            r.G("二度裂く", "Cut Twice", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_husk_route_laceration_7_gimmick_value);

            r = new Route(nodes, "Husk", "death-mark", "St_Q_DeathMark");
            r.L("楔の再来", "Returning Wedge", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_husk_route_death_mark_1_link_value);
            r.G("刻印の闇", "Shadow of the Mark", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_husk_route_death_mark_2_gimmick_value, 3);
            r.L("刻印の余波", "Mark Aftershock", LinkKind.MemorySurge, 3);
            r.G("刻印の疼き", "Aching Mark", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_husk_route_death_mark_4_gimmick_value);
            r.L("呪楔の弾道の冴え", "Keen Cursed Wedge", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_death_mark_5_link_value);
            r.S("刻み込む闇", "Engraved Darkness", Stat.DarkAmp, MemoryDamageBalance.Effect_legacy_h_husk_route_death_mark_6_stat_perRank);
            r.G("刻印の回収", "Mark Reclaimed", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_husk_route_death_mark_7_gimmick_value);

            r = new Route(nodes, "Husk", "annihilation", "St_R_AnnihilationStance");
            r.L("剣気の冴え", "Keen Sword Wave", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_annihilation_1_link_value);
            r.G("滅殺の備え", "Annihilation Readiness", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_husk_route_annihilation_2_gimmick_value);
            r.L("覚醒の余勢", "Awakening Momentum", LinkKind.MemorySurge, 2);
            r.G("剣気の余波", "Sword Aura Aftermath", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_husk_route_annihilation_4_gimmick_value, 0, MemoryDamageBalance.Effect_legacy_h_husk_route_annihilation_4_gimmick_cooldown);
            r.L("滅びの波動の冴え", "Keen Ruinous Wave", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_annihilation_5_link_value);
            r.S("剣気を放つ腕", "Wave-Releasing Arm", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_husk_route_annihilation_6_stat_perRank);
            r.G("剣気の吸命", "Aura Lifesteal", GimmickTrigger.OnKill, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_husk_route_annihilation_7_gimmick_value);

            r = new Route(nodes, "Husk", "deception", "St_R_Deception");
            r.L("隠れ身の巡り", "Concealment Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_husk_route_deception_1_link_value);
            r.G("隠れ身の闇", "Shadow of Stealth", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_husk_route_deception_2_gimmick_value, 3);
            r.L("奇襲の余波", "Ambush Aftershock", LinkKind.MemorySurge, 3);
            r.G("解除の波紋", "Reveal Ripple", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_husk_route_deception_4_gimmick_value);
            r.L("影裂きの炸裂の冴え", "Keen Shadow-Rending Blast", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_husk_route_deception_5_link_value);
            r.S("潜む闇の濃さ", "Lurking Dark Intensity", Stat.DarkAmp, MemoryDamageBalance.Effect_legacy_h_husk_route_deception_6_stat_perRank);
            r.G("影の侵食", "Creeping Shadow", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_husk_route_deception_7_gimmick_value, 3);

            // Mist: AP opening shields/parries versus AD marked-target and thrust damage.
            r = new Route(nodes, "Mist", "en-garde", "St_D_AstridsMasterpieceEnGarde");
            r.L("初太刀の冴え", "Keen Opening Thrust", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_en_garde_1_link_value);
            r.G("初撃の余韻", "Opening Echo", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_mist_route_en_garde_2_gimmick_value);
            r.L("三撃の守り", "Three-Strike Guard", LinkKind.Guard, 2);
            r.G("見切りの構え", "Reading the Opening", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_mist_route_en_garde_4_gimmick_value);
            r.L("仕掛けの連撃の冴え", "Keen Engaging Flurry", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_en_garde_5_link_value);
            r.S("受け止める器", "Shield-Bearing Vessel", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_mist_route_en_garde_6_stat_perRank);
            r.G("構えの連携", "Stance Link", GimmickTrigger.OnHit, GimmickEffect.RechargeOther, MemoryDamageBalance.Effect_legacy_h_mist_route_en_garde_7_gimmick_value);

            r = new Route(nodes, "Mist", "priorite", "St_D_AstridsMasterpiecePriorite");
            r.L("標的を穿つ冴え", "Keen Mark Piercing", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_priorite_1_link_value);
            r.G("標的の戦利", "Spoils of the Mark", GimmickTrigger.OnKill, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_mist_route_priorite_2_gimmick_value);
            r.L("決闘刃の冴え", "Keen Duelist's Blade", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_priorite_3_link_value);
            r.G("追い立ての刃", "Driving Blade", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_mist_route_priorite_4_gimmick_value);
            r.L("重なる刺突の冴え", "Keen Stacking Thrusts", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_priorite_5_link_value);
            r.S("刻み続ける剣", "Relentless Marking Blade", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_mist_route_priorite_6_stat_perRank);
            r.CapG("標的の連鎖爆発", "Marked Chain Blast", MemoryDamageBalance.Effect_h_mist_route_priorite_7_link_value, GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_mist_route_priorite_7_gimmick_value);

            r = new Route(nodes, "Mist", "fast-feet", "St_M_FastFeet");
            r.L("足運びの備え", "Footwork Readiness", LinkKind.Guard, 1);
            r.S("返し刃の腕", "Counterblade Arm", Stat.AttackPct, MemoryDamageBalance.Effect_legacy_h_mist_route_fast_feet_2_stat_perRank);
            r.L("踏み直す呼吸", "Resetting Breath", LinkKind.Guard, 1);
            r.P("足跡の守り", "Footstep Ward", Power.Barrier, MemoryDamageBalance.Effect_legacy_h_mist_route_fast_feet_4_power_perRank);
            r.L("足さばきのかく乱", "Befuddling Footwork", LinkKind.Guard, 1);
            r.S("剣舞の体幹", "Sword Dance Core", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_mist_route_fast_feet_6_stat_perRank);
            r.L("風のような歩み", "Windlike Strides", LinkKind.Guard, 6);

            r = new Route(nodes, "Mist", "fleche", "St_Q_Fleche");
            r.L("突剣の巡り", "Thrusting Blade Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_mist_route_fleche_1_link_value);
            r.G("閃きの光", "Flash of Light", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_mist_route_fleche_2_gimmick_value, 2);
            r.L("突剣の余勢", "Thrusting Momentum", LinkKind.MemorySurge, 2);
            r.G("突剣の二段", "Double Thrust", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_mist_route_fleche_4_gimmick_value);
            r.L("飛び込み突きの冴え", "Keen Lunging Thrust", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_fleche_5_link_value);
            r.S("傷を塞ぐ体", "Wound-Mending Body", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_mist_route_fleche_6_stat_perRank);
            r.G("突き抜けの再突撃", "Breakthrough Charge", GimmickTrigger.OnKill, GimmickEffect.Reload, MemoryDamageBalance.Effect_legacy_h_mist_route_fleche_7_gimmick_value);

            r = new Route(nodes, "Mist", "lunge", "St_Q_Lunge");
            r.L("踏み込みの巡り", "Advancing Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_mist_route_lunge_1_link_value);
            r.G("踏み込みの守り", "Advancing Guard", GimmickTrigger.OnHit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_mist_route_lunge_2_gimmick_value);
            r.L("刺突の余韻", "Thrust Echo", LinkKind.MemorySurge, 2);
            r.G("突きの衝撃", "Thrust Impact", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_mist_route_lunge_4_gimmick_value);
            r.L("深く刺す冴え", "Keen Deep Stab", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_lunge_5_link_value);
            r.S("突剣の下支え", "Thrust Support", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_mist_route_lunge_6_stat_perRank);
            r.G("霧雨の追い突き", "Mist-Rain Follow-Thrusts", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_mist_route_lunge_7_gimmick_value);

            r = new Route(nodes, "Mist", "parry", "St_R_Parry");
            r.L("受け流す間合い", "Parrying Distance", LinkKind.Guard, 2);
            r.G("跳ね返された痛み", "Reflected Pain", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_mist_route_parry_2_gimmick_value);
            r.L("反撃の余韻", "Riposte Echo", LinkKind.MemorySurge, 2);
            r.G("反響の雷", "Thunder Echo", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_mist_route_parry_4_gimmick_value, 2);
            r.L("構え直す呼吸", "Reforming Stance", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_mist_route_parry_5_link_value);
            r.S("受け流しの体幹", "Parrying Core", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_mist_route_parry_6_stat_perRank);
            r.G("受け流しの構え", "Parry Guard", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_mist_route_parry_7_gimmick_value);

            r = new Route(nodes, "Mist", "determination", "St_R_UnbreakableDetermination");
            r.L("覚醒の雷光の冴え", "Keen Awakened Lightning", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_determination_1_link_value);
            r.G("覚醒の守り", "Guard of Awakening", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_mist_route_determination_2_gimmick_value);
            r.L("意志の余光", "Resolve Afterglow", LinkKind.MemorySurge, 2);
            r.G("電撃の余韻", "Lightning Reverberation", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_mist_route_determination_4_gimmick_value);
            r.L("立ち上がる剣の冴え", "Keen Rising Blade", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_mist_route_determination_5_link_value);
            r.S("再起する体", "Renewing Body", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_mist_route_determination_6_stat_perRank);
            r.G("覚醒の鼓動", "Awakened Heartbeat", GimmickTrigger.OnKill, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_mist_route_determination_7_gimmick_value);

            // Nachia: transferable armor/speed and AP summons. Moonlight Pact can lose
            // its active cast through a constellation, so its links are equipment-based.
            r = new Route(nodes, "Nachia", "pack-heart", "St_D_HeartOfThePack");
            r.L("団結の光爆の冴え", "Keen Unity Lightburst", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_pack_heart_1_link_value);
            r.G("団結の光", "Light of Unity", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_nachia_route_pack_heart_2_gimmick_value, 2);
            r.L("心音の炸裂の冴え", "Keen Heartbeat Blast", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_pack_heart_3_link_value);
            r.G("団結の追い爆発", "Unity Afterburst", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_nachia_route_pack_heart_4_gimmick_value);
            r.L("重なる団結の冴え", "Keen Stacking Unity", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_pack_heart_5_link_value);
            r.S("団結の癒やし", "Healing of Unity", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_nachia_route_pack_heart_6_stat_perRank);
            r.CapG("団結の木霊", "Unity Echo", MemoryDamageBalance.Effect_h_nachia_route_pack_heart_7_link_value, GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_nachia_route_pack_heart_7_gimmick_value);

            r = new Route(nodes, "Nachia", "circle-life", "St_D_CircleOfLife");
            r.L("巡る命の守り", "Life Cycle Guard", LinkKind.Guard, 2);
            r.S("群れに渡す速さ", "Speed Shared with the Pack", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_nachia_route_circle_life_2_stat_perRank);
            r.L("巡る命の壁", "Circling Life Ward", LinkKind.Guard, 1);
            r.S("群れに渡す鎧", "Armor Shared with the Pack", Stat.Armor, MemoryDamageBalance.Effect_legacy_h_nachia_route_circle_life_4_stat_perRank);
            r.L("共に生きる備え", "Shared Life Readiness", LinkKind.Guard, 1);
            r.S("巡り続ける恵み", "Ever-Circling Grace", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_nachia_route_circle_life_6_stat_perRank);
            r.L("巡り続ける命脈", "Unbroken Lifeline", LinkKind.Guard, 8);

            r = new Route(nodes, "Nachia", "dreamy-waltz", "St_M_DreamyWaltz");
            r.L("輪舞の守り", "Waltz Guard", LinkKind.Guard, 2);
            r.S("舞う器", "Dancing Vessel", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_nachia_route_dreamy_waltz_2_stat_perRank);
            r.L("舞い戻る呼吸", "Returning Dance Breath", LinkKind.Guard, 1);
            r.P("ワルツの守り", "Waltz Ward", Power.Barrier, MemoryDamageBalance.Effect_legacy_h_nachia_route_dreamy_waltz_4_power_perRank);
            r.L("舞いの波紋", "Waltz Ripples", LinkKind.Guard, 1);
            r.S("群れを包む薄衣", "Veil Enfolding the Pack", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_nachia_route_dreamy_waltz_6_stat_perRank);
            r.L("群れを包む輪舞", "Pack-Enfolding Waltz", LinkKind.Guard, 9);

            r = new Route(nodes, "Nachia", "sylvan-call", "St_Q_SylvanCall");
            r.L("呼び声の巡り", "Calling Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_nachia_route_sylvan_call_1_link_value);
            r.G("森の薄衣", "Forest Veil", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_nachia_route_sylvan_call_2_gimmick_value);
            r.L("群れの余勢", "Pack Momentum", LinkKind.MemorySurge, 2);
            r.G("森の癒やし", "Forest Mending", GimmickTrigger.OnUse, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_nachia_route_sylvan_call_4_gimmick_value, 1);
            r.L("飛びかかる群れの冴え", "Keen Leaping Pack", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_sylvan_call_5_link_value);
            r.S("若葉の牙", "Fangs of New Leaves", Stat.SummonPower, MemoryDamageBalance.Effect_legacy_h_nachia_route_sylvan_call_6_stat_perRank);
            r.G("猟犬の補充", "Hound Resupply", GimmickTrigger.OnKill, GimmickEffect.Reload, MemoryDamageBalance.Effect_legacy_h_nachia_route_sylvan_call_7_gimmick_value);

            r = new Route(nodes, "Nachia", "moonlight-pact", "St_Q_MoonlightPact");
            r.L("月下の跳躍の冴え", "Keen Moonlit Leap", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_moonlight_pact_1_link_value);
            r.G("爪痕", "Claw Mark", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_nachia_route_moonlight_pact_2_gimmick_value);
            r.L("月獣の爪痕の冴え", "Keen Moonbeast Claws", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_moonlight_pact_3_link_value);
            r.G("月光の癒やし", "Moonlit Mending", GimmickTrigger.OnHit, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_nachia_route_moonlight_pact_4_gimmick_value, 1, MemoryDamageBalance.Effect_legacy_h_nachia_route_moonlight_pact_4_gimmick_cooldown);
            r.L("月光の斬撃の冴え", "Keen Moonlight Slash", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_moonlight_pact_5_link_value);
            r.S("月獣の牙", "Moonbeast Fangs", Stat.SummonPower, MemoryDamageBalance.Effect_legacy_h_nachia_route_moonlight_pact_6_stat_perRank);
            r.CapG("月下の再跳躍", "Moonlit Re-Leap", MemoryDamageBalance.Effect_h_nachia_route_moonlight_pact_7_link_value, GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_nachia_route_moonlight_pact_7_gimmick_value);

            r = new Route(nodes, "Nachia", "natures-whisper", "St_R_NaturesWhisper");
            r.L("指揮の巡り", "Command Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_nachia_route_natures_whisper_1_link_value);
            r.G("号令の薄衣", "Command Veil", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_nachia_route_natures_whisper_2_gimmick_value);
            r.L("号令の護り", "Command Ward", LinkKind.Guard, 2);
            r.G("指揮の昂揚", "Commander's Elation", GimmickTrigger.OnUse, GimmickEffect.Quicken, MemoryDamageBalance.Effect_legacy_h_nachia_route_natures_whisper_4_gimmick_value);
            r.L("指揮者の守り", "Commander's Guard", LinkKind.Guard, 1);
            r.S("群れの猛威", "Might of the Pack", Stat.SummonPower, MemoryDamageBalance.Effect_legacy_h_nachia_route_natures_whisper_6_stat_perRank);
            r.G("号令の鼓舞", "Rallying Cry", GimmickTrigger.OnUse, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_nachia_route_natures_whisper_7_gimmick_value, 1);

            r = new Route(nodes, "Nachia", "serpent-blessing", "St_R_SerpentineBlessing");
            r.L("蛇鱗の守り", "Serpent Scale Guard", LinkKind.Guard, 2);
            r.G("蛇の余韻", "Serpent Reverb", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_nachia_route_serpent_blessing_2_gimmick_value);
            r.L("祝福の余韻", "Blessing Echo", LinkKind.MemorySurge, 2);
            r.G("蛇の追い咬み", "Serpent's Second Bite", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_nachia_route_serpent_blessing_4_gimmick_value, 0, MemoryDamageBalance.Effect_legacy_h_nachia_route_serpent_blessing_4_gimmick_cooldown);
            r.L("蛇の追い撃ちの冴え", "Keen Serpent Follow-Up", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_nachia_route_serpent_blessing_5_link_value);
            r.P("蛇の守り", "Serpent Ward", Power.StarShield, MemoryDamageBalance.Effect_legacy_h_nachia_route_serpent_blessing_6_power_perRank);
            r.G("蛇鱗の加護", "Serpent Scale Ward", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_nachia_route_serpent_blessing_7_gimmick_value);

            // Aurena: AP healing does not scale with the HP sacrificed. AD claws/theory
            // remain separate; no extra max-HP tax is imposed on the sacrifice branches.
            r = new Route(nodes, "Aurena", "claw", "St_D_DisintegratingClaw");
            r.L("分解爪の冴え", "Keen Dismantling Claw", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_claw_1_link_value);
            r.G("分解の光", "Dismantling Light", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_aurena_route_claw_2_gimmick_value, 2);
            r.L("解きほぐす一撃の冴え", "Keen Unraveling Blow", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_claw_3_link_value);
            r.G("分解の二度爪", "Double Claw", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_aurena_route_claw_4_gimmick_value);
            r.L("金色の爪痕の冴え", "Keen Golden Clawmarks", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_claw_5_link_value);
            r.S("傷を塞ぐ爪", "Wound-Mending Claw", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_aurena_route_claw_6_stat_perRank);
            r.CapG("分解の脆化", "Dismantling Brittleness", MemoryDamageBalance.Effect_h_aurena_route_claw_7_link_value, GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_aurena_route_claw_7_gimmick_value);

            r = new Route(nodes, "Aurena", "beautiful-threat", "St_D_BeautifulThreat");
            r.L("金羽の冴え", "Keen Golden Plume", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_beautiful_threat_1_link_value);
            r.G("金羽の薄衣", "Golden Feather Veil", GimmickTrigger.OnHit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_aurena_route_beautiful_threat_2_gimmick_value);
            r.L("羽撃ちの冴え", "Keen Feather Strike", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_beautiful_threat_3_link_value);
            r.G("金羽の二射", "Golden Double", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_aurena_route_beautiful_threat_4_gimmick_value);
            r.L("羽根の嵐の冴え", "Keen Feather Storm", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_beautiful_threat_5_link_value);
            r.S("命を返す羽根", "Life-Restoring Feather", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_aurena_route_beautiful_threat_6_stat_perRank);
            r.CapG("金羽の輝き", "Golden Feather Radiance", MemoryDamageBalance.Effect_h_aurena_route_beautiful_threat_7_link_value, GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_aurena_route_beautiful_threat_7_gimmick_value, 2);

            r = new Route(nodes, "Aurena", "feathery-dash", "St_M_FeatheryDash");
            r.L("羽ばたきの備え", "Wingbeat Readiness", LinkKind.Guard, 1);
            r.S("飛翔の魔力", "Flight Power", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_aurena_route_feathery_dash_2_stat_perRank);
            r.L("羽根の舞い戻り", "Returning Feather Dance", LinkKind.Guard, 1);
            r.P("舞い降りる光", "Descending Light", Power.Radiance, MemoryDamageBalance.Effect_legacy_h_aurena_route_feathery_dash_4_power_perRank);
            r.L("羽ばたきのかく乱", "Fluttering Confusion", LinkKind.Guard, 1);
            r.S("羽根の加速", "Feather Haste", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_aurena_route_feathery_dash_6_stat_perRank);
            r.L("黄金の風切り", "Golden Windcut", LinkKind.Guard, 6);

            r = new Route(nodes, "Aurena", "golden-burst", "St_Q_GoldenBurst");
            r.L("金光の炸裂の冴え", "Keen Golden Blast", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_golden_burst_1_link_value);
            r.G("黄金の被膜", "Golden Film", GimmickTrigger.OnUse, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_aurena_route_golden_burst_2_gimmick_value, 0, MemoryDamageBalance.Effect_legacy_h_aurena_route_golden_burst_2_gimmick_cooldown);
            r.L("爆光の余力", "Golden Blast Reserve", LinkKind.MemorySurge, 2);
            r.G("腐食の脆化", "Corroded Weakness", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_aurena_route_golden_burst_4_gimmick_value);
            r.L("腐食の輝きの冴え", "Keen Corroded Shine", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_golden_burst_5_link_value);
            r.S("緩やかな献身", "Gentle Devotion", Stat.SacrificeReduction, MemoryDamageBalance.Effect_legacy_h_aurena_route_golden_burst_6_stat_perRank);
            r.G("金光の余波", "Golden Reverberation", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_aurena_route_golden_burst_7_gimmick_value);

            r = new Route(nodes, "Aurena", "reduction", "St_Q_Reduction");
            r.L("金片の巡り", "Golden Shard Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_aurena_route_reduction_1_link_value);
            r.G("金片の光", "Shard Light", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_aurena_route_reduction_2_gimmick_value, 2);
            r.L("還流の余力", "Return Flow Reserve", LinkKind.MemorySurge, 2);
            r.G("金片の薄衣", "Shard Veil", GimmickTrigger.OnHit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_aurena_route_reduction_4_gimmick_value, 0, MemoryDamageBalance.Effect_legacy_h_aurena_route_reduction_4_gimmick_cooldown);
            r.L("金片の弾道の冴え", "Keen Shard Trajectory", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_reduction_5_link_value);
            r.S("欠片に残す命", "Life Spared in the Shards", Stat.SacrificeReduction, MemoryDamageBalance.Effect_legacy_h_aurena_route_reduction_6_stat_perRank);
            r.G("金片の破裂", "Shard Burst", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_aurena_route_reduction_7_gimmick_value);

            r = new Route(nodes, "Aurena", "dangerous-theory", "St_R_DangerousTheory");
            r.L("危険な一撃の冴え", "Keen Perilous Strike", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_dangerous_theory_1_link_value);
            r.G("理論の閃き", "Flash of Theory", GimmickTrigger.OnCrit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_aurena_route_dangerous_theory_2_gimmick_value, 2);
            r.L("論証の余波", "Proof Aftershock", LinkKind.MemorySurge, 2);
            r.G("危険な副作用", "Dangerous Side Effect", GimmickTrigger.OnCrit, GimmickEffect.Heal, MemoryDamageBalance.Effect_legacy_h_aurena_route_dangerous_theory_4_gimmick_value);
            r.L("再検証の呼吸", "Reappraisal Breath", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_aurena_route_dangerous_theory_5_link_value);
            r.S("命を戻す知", "Life-Restoring Wisdom", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_aurena_route_dangerous_theory_6_stat_perRank);
            r.G("危険な結論", "Dangerous Conclusion", GimmickTrigger.OnCrit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_aurena_route_dangerous_theory_7_gimmick_value);

            r = new Route(nodes, "Aurena", "chain-reaction", "St_R_ChainReaction");
            r.L("連鎖環の炸裂の冴え", "Keen Chain-Ring Blast", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_chain_reaction_1_link_value);
            r.G("分解の光", "Dismantle Light", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_aurena_route_chain_reaction_2_gimmick_value, 2, MemoryDamageBalance.Effect_legacy_h_aurena_route_chain_reaction_2_gimmick_cooldown);
            r.L("連鎖の余光", "Chain Afterglow", LinkKind.MemorySurge, 2);
            r.G("分解の脆弱化", "Dissolving Weakness", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_aurena_route_chain_reaction_4_gimmick_value);
            r.L("巡る分解光の冴え", "Keen Circling Dissolution", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_aurena_route_chain_reaction_5_link_value);
            r.S("陣を巡る命", "Life Circling the Array", Stat.HealPower, MemoryDamageBalance.Effect_legacy_h_aurena_route_chain_reaction_6_stat_perRank);
            r.G("連鎖の加速", "Accelerated Chain", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_aurena_route_chain_reaction_7_gimmick_value);

            // Bismuth: travelers.json loadoutTrait contains only PrismaticEyes. Books
            // use AP for light/fire and AD for sword/dark arrows; none is an Ultimate.
            r = new Route(nodes, "Bismuth", "prismatic-eyes", "St_D_PrismaticEyes");
            r.L("頁の刃の冴え", "Keen Page Blade", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_bismuth_route_prismatic_eyes_1_link_value);
            r.G("光の読み進め", "Reading the Light", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_bismuth_route_prismatic_eyes_2_gimmick_value, 2);
            r.L("翻る頁撃ちの冴え", "Keen Flipping-Page Strike", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_bismuth_route_prismatic_eyes_3_link_value);
            r.G("頁の余韻", "Page Reverb", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_bismuth_route_prismatic_eyes_4_gimmick_value);
            r.L("彩の追撃の冴え", "Keen Prismatic Follow-Up", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_bismuth_route_prismatic_eyes_5_link_value);
            r.S("頁を送る拍子", "Page-Turning Tempo", Stat.AttackSpeedPct, MemoryDamageBalance.Effect_legacy_h_bismuth_route_prismatic_eyes_6_stat_perRank);
            r.CapG("読了の連携", "Reading Link", MemoryDamageBalance.Effect_h_bismuth_route_prismatic_eyes_7_link_value, GimmickTrigger.OnKill, GimmickEffect.RechargeOther, MemoryDamageBalance.Effect_legacy_h_bismuth_route_prismatic_eyes_7_gimmick_value);

            r = new Route(nodes, "Bismuth", "distorting-sprint", "St_M_Sprint");
            r.L("書架の退避", "Bookshelf Retreat", LinkKind.Guard, 1);
            r.S("駆ける頁の知", "Racing Page Wisdom", Stat.PowerPct, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorting_sprint_2_stat_perRank);
            r.L("頁路の再接続", "Pagepath Reconnection", LinkKind.Guard, 1);
            r.P("歪む冷気", "Warped Frost", Power.Frost, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorting_sprint_4_power_perRank);
            r.L("疾走の目くらまし", "Sprinting Confusion", LinkKind.Guard, 1);
            r.S("旅する装丁", "Traveling Binding", Stat.MaxHealthPct, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorting_sprint_6_stat_perRank);
            r.L("頁の疾風", "Page Gale", LinkKind.Guard, 6);

            r = new Route(nodes, "Bismuth", "innocence", "St_QR_Innocence");
            r.L("光文の巡り", "Light Script Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_bismuth_route_innocence_1_link_value);
            r.G("冷たい余白", "Cold Margin", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_bismuth_route_innocence_2_gimmick_value, 1);
            r.L("霊弾の余光", "Soul Missile Afterglow", LinkKind.MemorySurge, 2);
            r.G("光文の注釈", "Light Annotation", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_bismuth_route_innocence_4_gimmick_value);
            r.L("霊弾の炸裂の冴え", "Keen Soul-Missile Burst", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_bismuth_route_innocence_5_link_value);
            r.S("光文字の濃さ", "Light Script Intensity", Stat.LightAmp, MemoryDamageBalance.Effect_legacy_h_bismuth_route_innocence_6_stat_perRank);
            r.G("魂の木霊", "Soul Echo", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_bismuth_route_innocence_7_gimmick_value);

            r = new Route(nodes, "Bismuth", "infernal-tales", "St_QR_InfernalTales");
            r.L("炎文の余熱", "Fire Script Afterheat", LinkKind.MemorySurge, 2);
            r.G("炎文の火種", "Script Ember", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_bismuth_route_infernal_tales_2_gimmick_value, 0, MemoryDamageBalance.Effect_legacy_h_bismuth_route_infernal_tales_2_gimmick_cooldown);
            r.L("業火の輪の冴え", "Keen Hellfire Ring", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_bismuth_route_infernal_tales_3_link_value);
            r.G("炎の飛び火", "Flying Sparks", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_bismuth_route_infernal_tales_4_gimmick_value, 0, MemoryDamageBalance.Effect_legacy_h_bismuth_route_infernal_tales_4_gimmick_cooldown);
            r.L("炎文の巡り", "Fire Script Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_bismuth_route_infernal_tales_5_link_value);
            r.S("火文字の濃さ", "Fire Script Intensity", Stat.FireAmp, MemoryDamageBalance.Effect_legacy_h_bismuth_route_infernal_tales_6_stat_perRank);
            r.G("焼け付く頁", "Scorching Page", GimmickTrigger.OnHit, GimmickEffect.Expose, MemoryDamageBalance.Effect_legacy_h_bismuth_route_infernal_tales_7_gimmick_value);

            r = new Route(nodes, "Bismuth", "valiant-heart", "St_QR_ValiantHeart");
            r.L("剣文の巡り", "Sword Script Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_bismuth_route_valiant_heart_1_link_value);
            r.G("剣の護り", "Sword's Guard", GimmickTrigger.OnHit, GimmickEffect.Shield, MemoryDamageBalance.Effect_legacy_h_bismuth_route_valiant_heart_2_gimmick_value);
            r.L("剣閃の余韻", "Sword Flash Echo", LinkKind.MemorySurge, 2);
            r.G("魔剣の斬撃波", "Blade Wave", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_bismuth_route_valiant_heart_4_gimmick_value);
            r.L("勇剣の冴え", "Keen Valiant Edge", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_bismuth_route_valiant_heart_5_link_value);
            r.S("心を守る装丁", "Heart-Warding Binding", Stat.ShieldPower, MemoryDamageBalance.Effect_legacy_h_bismuth_route_valiant_heart_6_stat_perRank);
            r.G("勇気の追い風", "Courage's Tailwind", GimmickTrigger.OnKill, GimmickEffect.Recharge, MemoryDamageBalance.Effect_legacy_h_bismuth_route_valiant_heart_7_gimmick_value);

            r = new Route(nodes, "Bismuth", "distorted-mind", "St_QR_DistortedMind");
            r.L("闇文の蓄積", "Dark Script Accumulation", LinkKind.MemorySurge, 2);
            r.G("闇文の染み", "Dark Script Stain", GimmickTrigger.OnHit, GimmickEffect.Element, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorted_mind_2_gimmick_value, 3);
            r.L("精神矢の冴え", "Keen Mind Arrow", LinkKind.MemoryDamage, MemoryDamageBalance.Effect_h_bismuth_route_distorted_mind_3_link_value);
            r.G("精神の木霊", "Mind Echo", GimmickTrigger.OnHit, GimmickEffect.Echo, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorted_mind_4_gimmick_value);
            r.L("闇文の巡り", "Dark Script Cycle", LinkKind.MemoryHaste, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorted_mind_5_link_value);
            r.S("闇文字の濃さ", "Dark Script Intensity", Stat.DarkAmp, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorted_mind_6_stat_perRank);
            r.G("精神の矢の爆ぜ", "Burst of Mind Arrows", GimmickTrigger.OnHit, GimmickEffect.Burst, MemoryDamageBalance.Effect_legacy_h_bismuth_route_distorted_mind_7_gimmick_value);

            foreach (string hero in new[] { "Vesper", "Lacerta", "Cetus", "Yubar", "Husk", "Mist", "Nachia", "Aurena", "Bismuth" })
                AddRing(nodes, hero);
            AddEssenceSlots(nodes);
            return nodes.AsReadOnly();
        }

        /// <summary>
        /// アイデンティティ記憶のルートと移動の記憶のルートの頂点の先に、エッセンスの枠を1つ増やす星を置く（1段・5ポイント）。
        /// アイデンティティのルートは2本あるが、両方取っても増える枠は1つまで（EssenceSlots で抑える）。
        /// </summary>
        private static void AddEssenceSlots(List<TalentDef> nodes)
        {
            var capstones = new List<TalentDef>();
            foreach (var t in nodes)
                if (t.RouteId != null && t.RouteOrder == 7 && t.RouteMemory != null
                    && (t.RouteMemory.StartsWith("St_D_", System.StringComparison.Ordinal) || t.RouteMemory.StartsWith("St_M_", System.StringComparison.Ordinal)))
                    capstones.Add(t);
            foreach (var cap in capstones)
            {
                bool identity = cap.RouteMemory.StartsWith("St_D_", System.StringComparison.Ordinal);
                var node = identity
                    ? new TalentDef(cap.RouteId + ".slot", Line.Offense, new Txt("記憶の器を広げる", "Widen the Memory's Vessel"), Stat.EssenceSlotIdentity, 1, 1)
                    : new TalentDef(cap.RouteId + ".slot", Line.Offense, new Txt("回避の器を広げる", "Widen the Dodge's Vessel"), Stat.EssenceSlotMovement, 1, 1);
                node.HeroKey = cap.HeroKey;
                node.Tier = 2;
                node.RouteId = cap.RouteId;
                node.RouteMemory = cap.RouteMemory;
                node.RouteOrder = 8;
                node.RankCost = EssenceSlotCost;
                nodes.Add(node);
            }
        }

        /// <summary>エッセンスの枠を増やす星の費用。</summary>
        public const int EssenceSlotCost = 5;

        private static void AddRing(List<TalentDef> nodes, string hero)
        {
            Ring(nodes, hero, "force", "夢輪の剛力", "Dream Ring Strength", Stat.AttackPct, 1);
            Ring(nodes, hero, "insight", "夢輪の叡智", "Dream Ring Wisdom", Stat.PowerPct, 1);
            Ring(nodes, hero, "vessel", "夢輪の器", "Dream Ring Vessel", Stat.MaxHealthPct, 1);
            Ring(nodes, hero, "armor", "夢輪の鎧", "Dream Ring Armor", Stat.Armor, 2);
            Ring(nodes, hero, "recall", "夢輪の生命", "Dream Ring Life", Stat.MaxHealthFlat, 6);
            Ring(nodes, hero, "rhythm", "夢輪の鼓動", "Dream Ring Rhythm", Stat.AttackSpeedPct, 1);
            Ring(nodes, hero, "resolve", "夢輪の不屈", "Dream Ring Resolve", Stat.Tenacity, hero == "Bismuth" ? MemoryDamageBalance.Effect_legacy_h_bismuth_ring_resolve_stat_perRank : 2);
            int renewal;
            switch (hero)
            {
                case "Vesper": renewal = MemoryDamageBalance.Effect_legacy_h_vesper_ring_renewal_stat_perRank; break;
                case "Lacerta": renewal = MemoryDamageBalance.Effect_legacy_h_lacerta_ring_renewal_stat_perRank; break;
                case "Cetus": renewal = MemoryDamageBalance.Effect_legacy_h_cetus_ring_renewal_stat_perRank; break;
                case "Yubar": renewal = MemoryDamageBalance.Effect_legacy_h_yubar_ring_renewal_stat_perRank; break;
                case "Husk": renewal = MemoryDamageBalance.Effect_legacy_h_husk_ring_renewal_stat_perRank; break;
                case "Mist": renewal = MemoryDamageBalance.Effect_legacy_h_mist_ring_renewal_stat_perRank; break;
                case "Nachia": renewal = MemoryDamageBalance.Effect_legacy_h_nachia_ring_renewal_stat_perRank; break;
                case "Aurena": renewal = MemoryDamageBalance.Effect_legacy_h_aurena_ring_renewal_stat_perRank; break;
                case "Bismuth": renewal = MemoryDamageBalance.Effect_legacy_h_bismuth_ring_renewal_stat_perRank; break;
                default: throw new ArgumentOutOfRangeException(nameof(hero));
            }
            Ring(nodes, hero, "renewal", "夢輪の再生", "Dream Ring Renewal", Stat.HealthRegen, renewal);
        }

        private static void Ring(List<TalentDef> nodes, string hero, string id, string ja, string en, Stat stat, int value)
        {
            bool outer = id == "renewal" || (hero == "Bismuth" && id == "resolve");
            nodes.Add(new TalentDef("h." + hero.ToLowerInvariant() + ".ring." + id, Line.Resonance,
                new Txt(ja, en), stat, outer ? value : 0, outer ? 5 : 3)
                { HeroKey = "Hero_" + hero, Tier = 2, IsDreamRing = true });
        }
    }
}
