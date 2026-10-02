using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 日本語の記憶・旅人・星座データを照合した記憶別ルート。
    /// Bismuthの固有のアイデンティティは1つなので、共通のアイデンティティ「華麗なる芸術家」のルートを足す。合計63ルート・441星と夢の輪72星。
    /// v1.28：ルートの「同調」は「記憶の冴え」（その記憶で与えるダメージ+X%）に置き換え、「守り」は盾役の記憶とダメージを出さない記憶のルートだけに残す。
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

            public void L(string ja, string en, LinkKind kind, int value)
            {
                Add(new TalentDef(NextId, Line.Offense, new Txt(ja, en),
                    new LinkDef { Kind = kind, Value = value, Requires = new[] { memory } }, NextRanks));
            }
        }

        private static IReadOnlyList<TalentDef> Create()
        {
            var nodes = new List<TalentDef>(506);
            Route r;

            // Vesper: HP-based resolve/charge, AD discipline, AP channel and sanctuary.
            r = new Route(nodes, "Vesper", "resolve", "St_D_Resolve");
            r.L("不退の誓い", "Unyielding Oath", LinkKind.Guard, 2);
            r.S("鼓動の器", "Vessel of Heartbeats", Stat.MaxHealthPct, 2);
            r.L("跳ね除ける一撃の冴え", "Keen Repelling Blow", LinkKind.MemoryDamage, 4);
            r.P("癒える城壁", "Mending Rampart", Power.OverflowingLife, 4);
            r.L("揺るがぬ足場", "Unshaken Ground", LinkKind.Guard, 1);
            r.P("決意の鉄槌", "Hammer of Resolve", Power.Fetters, 3);
            r.L("折れない誓約", "Unbroken Covenant", LinkKind.Guard, 8);

            r = new Route(nodes, "Vesper", "mercy", "St_D_MercyOfEl");
            r.L("四撃の聖光の冴え", "Keen Fourth-Shot Light", LinkKind.MemoryDamage, 4);
            r.S("慈愛の拳", "Merciful Fist", Stat.AttackPct, 2);
            r.S("慈悲の連打", "Merciful Flurry", Stat.AttackSpeedPct, 2);
            r.P("慈悲の光輪", "Halo of Mercy", Power.Radiance, 5);
            r.L("重なる連撃の冴え", "Keen Stacking Flurry", LinkKind.MemoryDamage, 4);
            r.S("聖光の芯", "Heart of Holy Light", Stat.PowerPct, 2);
            r.L("慈光の冴え", "Keen Merciful Light", LinkKind.MemoryDamage, 18);

            r = new Route(nodes, "Vesper", "charge", "St_M_Charge");
            r.L("盾の助走", "Shielded Run-Up", LinkKind.Guard, 2);
            r.S("重装の心臓", "Armored Heart", Stat.MaxHealthPct, 2);
            r.L("突進の呼吸", "Charging Breath", LinkKind.Guard, 1);
            r.P("衝突の陣", "Collision Formation", Power.Bulwark, 4);
            r.L("体当たりの冴え", "Keen Ramming Blow", LinkKind.MemoryDamage, 4);
            r.S("重装の盾", "Heavy Bulwark", Stat.ShieldPower, 3);
            r.L("生きた破城槌", "Living Battering Ram", LinkKind.Guard, 9);

            r = new Route(nodes, "Vesper", "cruel-sun", "St_Q_CruelSun");
            r.L("日輪の蓄熱", "Solar Heat Reserve", LinkKind.MemorySurge, 2);
            r.S("炉心の魔力", "Furnace Power", Stat.PowerPct, 2);
            r.L("日輪の打撃の冴え", "Keen Solar Smash", LinkKind.MemoryDamage, 4);
            r.P("焼け跡の静寂", "Silence after Fire", Power.StillWater, 2);
            r.P("灼ける裁き", "Searing Judgment", Power.Fetters, 3);
            r.S("太陽を支える体", "Sun-Bearing Body", Stat.MaxHealthPct, 2);
            r.L("沈まぬ日輪", "Unsetting Sun", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Vesper", "discipline", "St_Q_Discipline");
            r.L("律動の再起", "Rhythmic Renewal", LinkKind.MemoryHaste, 3);
            r.S("律する鉄槌", "Hammer of Order", Stat.AttackPct, 2);
            r.L("踏み込む決意", "Resolute Advance", LinkKind.MemorySurge, 2);
            r.P("打ち込む光", "Hammered Light", Power.Radiance, 5);
            r.L("踏み込む打撃の冴え", "Keen Advancing Smash", LinkKind.MemoryDamage, 4);
            r.S("連打の息吹", "Breath of Blows", Stat.AttackSpeedPct, 1);
            r.L("途切れぬ律動", "Unbroken Rhythm", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Vesper", "baptism", "St_R_BaptismOfSun");
            r.L("陽炎の余力", "Heat Haze Reserve", LinkKind.MemorySurge, 2);
            r.S("火を宿す腕", "Fire-Bearing Arm", Stat.AttackPct, 2);
            r.L("誘いの鎧", "Challenger's Armor", LinkKind.Guard, 2);
            r.P("洗礼の火種", "Baptismal Embers", Power.Ember, 5);
            r.L("洗礼の巡り", "Baptism Cycle", LinkKind.MemoryHaste, 2);
            r.S("爆心の魔力", "Blast Core Power", Stat.PowerPct, 2);
            r.L("炎の受け皿", "Vessel of Flame", LinkKind.Guard, 9);

            r = new Route(nodes, "Vesper", "sanctuary", "St_R_SanctuaryOfEl");
            r.L("聖域の爆光の冴え", "Keen Holy Burst", LinkKind.MemoryDamage, 4);
            r.S("祈りの深さ", "Depth of Prayer", Stat.PowerPct, 2);
            r.L("降り注ぐ祝福の冴え", "Keen Descending Blessing", LinkKind.MemoryDamage, 4);
            r.P("輪の内の絆", "Bonds within the Circle", Power.Resonance, 2);
            r.L("聖域の門", "Sanctuary Gate", LinkKind.Guard, 1);
            r.S("聖域を包む光", "Light Enfolding the Sanctum", Stat.ShieldPower, 3);
            r.L("巡る聖域", "Returning Sanctum", LinkKind.MemoryHaste, 20);

            // Lacerta: distinguish AD double shots from AP fire; defense fills the kit's weakness.
            r = new Route(nodes, "Lacerta", "double-tap", "St_D_DoubleTap");
            r.L("二連射の冴え", "Keen Double Shot", LinkKind.MemoryDamage, 4);
            r.S("二重の火薬", "Double Powder Charge", Stat.AttackPct, 2);
            r.L("重なる弾道の冴え", "Keen Twin Trajectories", LinkKind.MemoryDamage, 4);
            r.P("二射の昂り", "Double-Tap Surge", Power.Overload, 4);
            r.L("追い撃ちの冴え", "Keen Follow-Up Shot", LinkKind.MemoryDamage, 4);
            r.S("反動の受け身", "Recoil Brace", Stat.MaxHealthPct, 2);
            r.L("二連火薬の冴え", "Keen Twin Powder", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Lacerta", "powder", "St_D_SalamanderPowder");
            r.L("発火の冴え", "Keen Ignition", LinkKind.MemoryDamage, 4);
            r.S("発火の秘術", "Ignition Art", Stat.PowerPct, 2);
            r.L("四射目の炸裂の冴え", "Keen Fourth-Shot Blast", LinkKind.MemoryDamage, 4);
            r.P("四射の種火", "Fourth-Shot Tinder", Power.Ember, 6);
            r.L("燃え広がる粉の冴え", "Keen Spreading Powder", LinkKind.MemoryDamage, 4);
            r.S("燃焼の濃度", "Burning Intensity", Stat.FireAmp, 2);
            r.L("火薬庫の冴え", "Keen Powder Keg", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Lacerta", "nimble-dodge", "St_M_NimbleDodge");
            r.L("射線の離脱", "Leaving the Firing Line", LinkKind.Guard, 1);
            r.S("返し撃ちの腕", "Countershot Arm", Stat.AttackPct, 2);
            r.L("回避の仕切り直し", "Dodge Reset", LinkKind.Guard, 1);
            r.P("跳び撃ちの止め", "Leaping Finisher", Power.Executioner, 6);
            r.L("回避の構え", "Evasive Stance", LinkKind.Guard, 1);
            r.S("身軽な防弾服", "Light Ballistic Vest", Stat.MaxHealthPct, 2);
            r.L("滑らかな回避", "Silken Dodge", LinkKind.Guard, 6);

            r = new Route(nodes, "Lacerta", "hand-cannon", "St_Q_HandCannon");
            r.L("砲口の熱", "Muzzle Heat", LinkKind.MemorySurge, 2);
            r.S("近接砲の魔力", "Close Cannon Power", Stat.PowerPct, 2);
            r.L("砲声の冴え", "Keen Cannon Roar", LinkKind.MemoryDamage, 5);
            r.P("硝煙の足止め", "Fouled Smoke", Power.Fetters, 3);
            r.L("砲身の冷却", "Barrel Cooling", LinkKind.MemoryHaste, 2);
            r.S("火炎砲の芯", "Flame Cannon Core", Stat.FireAmp, 2);
            r.L("零距離の轟音", "Point-Blank Thunder", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Lacerta", "incendiary", "St_Q_IncendiaryRounds");
            r.L("焼夷の手際", "Incendiary Handling", LinkKind.MemoryHaste, 3);
            r.S("燃える弾芯", "Burning Bullet Core", Stat.PowerPct, 2);
            r.L("火線の余熱", "Firing Line Heat", LinkKind.MemorySurge, 2);
            r.P("延焼する弾道", "Spreading Fire Trajectory", Power.Wildfire, 5);
            r.P("焼夷の追い火", "Incendiary Follow-up", Power.Ember, 6);
            r.S("焼夷の濃度", "Incendiary Concentrate", Stat.FireAmp, 2);
            r.L("燃え尽きぬ弾倉", "Ever-Burning Magazine", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Lacerta", "precision", "St_R_PrecisionShot");
            r.L("狙いの蓄積", "Gathered Aim", LinkKind.MemorySurge, 2);
            r.S("徹甲の秘術", "Armor-Piercing Art", Stat.PowerPct, 2);
            r.L("貫く弾芯の冴え", "Keen Piercing Core", LinkKind.MemoryDamage, 5);
            r.P("着弾の静止", "Impact Stillness", Power.StillWater, 2);
            r.L("照準の復帰", "Sight Recovery", LinkKind.MemoryHaste, 2);
            r.P("狙い澄ました一撃", "Steadied Shot", Power.Fetters, 3);
            r.L("一点を貫く意志", "Piercing Resolve", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Lacerta", "quick-trigger", "St_R_QuickTrigger");
            r.L("三射の呼吸", "Triple-Shot Breath", LinkKind.MemoryHaste, 2);
            r.S("貫通の火薬", "Penetrating Powder", Stat.AttackPct, 2);
            r.L("抜き撃ちの勢い", "Quickdraw Momentum", LinkKind.MemorySurge, 2);
            r.P("撃破の破片", "Defeat Fragments", Power.Shatter, 6);
            r.L("三連貫通の冴え", "Keen Triple Pierce", LinkKind.MemoryDamage, 4);
            r.S("射撃の持久力", "Shooting Endurance", Stat.MaxHealthPct, 2);
            r.L("三重の貫通線", "Triple Piercing Line", LinkKind.MemorySurge, 10);

            // Cetus: HP feeds bonus-health scaling; cold and skill stuns sustain shields.
            r = new Route(nodes, "Cetus", "icy-veins", "St_D_IcyVeins");
            r.L("冷血の炸裂の冴え", "Keen Coldblood Burst", LinkKind.MemoryDamage, 4);
            r.S("氷脈の魔力", "Ice Vein Power", Stat.PowerPct, 2);
            r.P("氷の外殻", "Ice Carapace", Power.Barrier, 2);
            r.P("凍えを留める", "Holding the Chill", Power.Frost, 3);
            r.L("五連の霜爆の冴え", "Keen Five-Stack Frostburst", LinkKind.MemoryDamage, 4);
            r.S("凍れる脈", "Frozen Pulse", Stat.ShieldPower, 3);
            r.L("解けない血脈", "Unmelting Veins", LinkKind.Guard, 8);

            r = new Route(nodes, "Cetus", "charged", "St_D_ChargedAnguillian");
            r.L("蓄電の冴え", "Keen Stored Charge", LinkKind.MemoryDamage, 4);
            r.S("雷を蓄える知", "Charge-Holding Wisdom", Stat.PowerPct, 2);
            r.S("帯電の鱗", "Charged Scales", Stat.LightAmp, 3);
            r.P("反撃の放電", "Retaliatory Discharge", Power.Retaliation, 4);
            r.L("放電の冴え", "Keen Discharge", LinkKind.MemoryDamage, 4);
            r.S("帯電の器", "Charged Vessel", Stat.MaxHealthPct, 2);
            r.L("十二連の雷撃の冴え", "Keen Twelvefold Lightning", LinkKind.MemoryDamage, 18);

            r = new Route(nodes, "Cetus", "frost-charge", "St_M_FrostyCharge");
            r.L("氷走りの備え", "Frost Run Readiness", LinkKind.Guard, 1);
            r.S("氷の推進力", "Frost Propulsion", Stat.PowerPct, 2);
            r.L("氷走りの再起", "Frost Run Renewal", LinkKind.Guard, 1);
            r.P("突進の氷壁", "Charging Ice Wall", Power.Aegis, 4);
            r.L("氷突進の冴え", "Keen Frost Ram", LinkKind.MemoryDamage, 4);
            r.S("氷山の質量", "Iceberg Mass", Stat.MaxHealthPct, 2);
            r.L("奔る氷河の冴え", "Keen Racing Glacier", LinkKind.MemoryDamage, 16);

            r = new Route(nodes, "Cetus", "embrace-chill", "St_Q_EmbracingTheChill");
            r.L("冷気の循環", "Chill Circulation", LinkKind.MemoryHaste, 2);
            r.S("冷気の泉", "Wellspring of Chill", Stat.PowerPct, 2);
            r.L("薄氷の重なり", "Layered Thin Ice", LinkKind.Guard, 2);
            r.P("凍原の足止め", "Frozen Field Snare", Power.Fetters, 2);
            r.L("氷域の余力", "Ice Field Reserve", LinkKind.MemorySurge, 2);
            r.S("氷域の厚み", "Ice Field Depth", Stat.ShieldPower, 3);
            r.L("冬の爆発の冴え", "Keen Winter Blast", LinkKind.MemoryDamage, 16);

            r = new Route(nodes, "Cetus", "boreal-chunk", "St_Q_BigBorealChunk");
            r.L("氷河の蓄積", "Glacier Accumulation", LinkKind.MemorySurge, 2);
            r.S("凝縮する氷", "Condensing Ice", Stat.PowerPct, 2);
            r.L("崩れ氷の炸裂の冴え", "Keen Collapsing Ice", LinkKind.MemoryDamage, 5);
            r.P("凍結後の静水", "Still Water after Frost", Power.StillWater, 1);
            r.L("氷河の再成", "Glacier Renewal", LinkKind.MemoryHaste, 2);
            r.S("寒波の深さ", "Depth of the Cold Wave", Stat.ColdAmp, 2);
            r.L("崩れ落ちる氷河", "Falling Glacier", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Cetus", "back-off", "St_R_BackOff");
            r.L("氷盾の備え", "Ice Shield Readiness", LinkKind.Guard, 2);
            r.S("押し返す潮", "Repelling Tide", Stat.PowerPct, 2);
            r.L("薙ぎ払いの余波", "Sweeping Aftershock", LinkKind.MemorySurge, 2);
            r.P("押し返す枷", "Repelling Shackles", Power.Fetters, 2);
            r.L("氷盾の再展開", "Ice Shield Redeployment", LinkKind.MemoryHaste, 2);
            r.S("仲間を包む氷", "Ice Enfolding Allies", Stat.ShieldPower, 3);
            r.L("退かぬ氷壁", "Unyielding Ice Wall", LinkKind.Guard, 9);

            r = new Route(nodes, "Cetus", "frozen-fists", "St_R_FrozenFists");
            r.L("近接乱打の冴え", "Keen Melee Barrage", LinkKind.MemoryDamage, 4);
            r.S("袖をまくる力", "Rolled-Sleeve Strength", Stat.AttackPct, 2);
            r.L("近接の氷殻", "Melee Ice Shell", LinkKind.Guard, 2);
            r.P("拳圏の守り", "Fist-Reach Defense", Power.Bulwark, 4);
            r.L("拳の仕切り直し", "Fist Reset", LinkKind.MemoryHaste, 2);
            r.S("凍拳の氷盾", "Frozen Fist Shield", Stat.ShieldPower, 3);
            r.L("叩き込む鉄拳の冴え", "Keen Hammering Fists", LinkKind.MemoryDamage, 18);

            // Yubar: AP explosions and defensive windows, not more generic crit/haste.
            r = new Route(nodes, "Yubar", "converging-stars", "St_D_ConvergencePoint");
            r.L("星弾の冴え", "Keen Starshot", LinkKind.MemoryDamage, 4);
            r.S("跳弾の軌道", "Ricochet Orbit", Stat.AttackPct, 2);
            r.L("跳ねる星屑の冴え", "Keen Ricocheting Stardust", LinkKind.MemoryDamage, 4);
            r.S("集う星光", "Gathering Starlight", Stat.LightAmp, 2);
            r.L("巡る星芒の冴え", "Keen Orbiting Starlight", LinkKind.MemoryDamage, 4);
            r.S("星弾を継ぐ手", "Starshot Relay", Stat.AttackSpeedPct, 1);
            r.L("交差する星流の冴え", "Keen Crossing Starstreams", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Yubar", "exotic-matter", "St_D_ExoticMatter");
            r.L("未知物質の冴え", "Keen Exotic Matter", LinkKind.MemoryDamage, 4);
            r.S("物質の魔力", "Matter Power", Stat.PowerPct, 2);
            r.L("凝縮する塊の冴え", "Keen Condensed Mass", LinkKind.MemoryDamage, 4);
            r.P("光子の重なり", "Layered Photons", Power.Radiance, 5);
            r.L("四重の光爆の冴え", "Keen Fourfold Lightburst", LinkKind.MemoryDamage, 4);
            r.S("異質な光", "Exotic Light", Stat.LightAmp, 2);
            r.L("臨界の炸裂の冴え", "Keen Criticality Blast", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Yubar", "flicker", "St_M_Flicker");
            r.L("転移の備え", "Blink Readiness", LinkKind.Guard, 1);
            r.S("転移の星核", "Blink Star Core", Stat.PowerPct, 2);
            r.L("光路の再接続", "Lightpath Reconnection", LinkKind.Guard, 1);
            r.P("転移の星光", "Blinking Starlight", Power.Radiance, 5);
            r.L("転移の目くらまし", "Blinding Blink", LinkKind.Guard, 1);
            r.S("薄明の器", "Twilight Vessel", Stat.MaxHealthPct, 2);
            r.L("二重の星影", "Double Starshadow", LinkKind.Guard, 6);

            r = new Route(nodes, "Yubar", "ethereal", "St_Q_EtherealInfluence");
            r.L("往復する光", "Returning Light", LinkKind.MemoryHaste, 2);
            r.S("エーテルの核", "Ether Core", Stat.PowerPct, 2);
            r.L("爆光の余韻", "Radiant Blast Echo", LinkKind.MemorySurge, 2);
            r.P("光を帯びる手", "Light-Bearing Hand", Power.Radiance, 5);
            r.L("往復光波の冴え", "Keen Round-Trip Lightwave", LinkKind.MemoryDamage, 4);
            r.S("光束の密度", "Light Beam Density", Stat.LightAmp, 2);
            r.L("エーテルの余韻", "Ethereal Afterglow", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Yubar", "supernova", "St_Q_SuperNova");
            r.L("星核の蓄積", "Star Core Accumulation", LinkKind.MemorySurge, 2);
            r.S("爆縮の知", "Implosion Wisdom", Stat.PowerPct, 2);
            r.L("星核の爆発の冴え", "Keen Stellar Detonation", LinkKind.MemoryDamage, 5);
            r.P("超新星の重力", "Supernova Gravity", Power.Fetters, 8);
            r.P("超新星の昂り", "Supernova Surge", Power.Overload, 4);
            r.S("星光の圧縮", "Starlight Compression", Stat.LightAmp, 2);
            r.L("大いなる爆縮", "Great Implosion", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Yubar", "cataclysm", "St_R_Cataclysm");
            r.L("隕石の軌道の冴え", "Keen Meteor Track", LinkKind.MemoryDamage, 4);
            r.S("天体の魔力", "Celestial Power", Stat.PowerPct, 2);
            r.L("降下する天罰の冴え", "Keen Falling Judgment", LinkKind.MemoryDamage, 4);
            r.P("落星の静寂", "Falling Star Stillness", Power.StillWater, 2);
            r.L("隕石の余熱", "Meteor Afterheat", LinkKind.MemorySurge, 2);
            r.S("天体を支える器", "Celestial Vessel", Stat.MaxHealthPct, 2);
            r.L("三度の天変", "Threefold Upheaval", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Yubar", "tranquility", "St_R_Tranquility");
            r.L("静寂の巡り", "Quiet Cycle", LinkKind.MemoryHaste, 2);
            r.S("静謐の知", "Tranquil Wisdom", Stat.PowerPct, 2);
            r.L("静寂の後光", "Quiet Halo", LinkKind.MemorySurge, 2);
            r.S("静けさの器", "Vessel of Calm", Stat.MaxHealthPct, 2);
            r.L("静寂の外殻", "Quiet Shell", LinkKind.Guard, 1);
            r.S("静かな殻", "Quiet Shell", Stat.Armor, 2);
            r.L("途切れぬ静謐", "Unbroken Tranquility", LinkKind.MemoryHaste, 10);

            // Husk: attack-speed conversion belongs to the identity; Death Mark and the
            // annihilation shockwave explicitly carry AP markers in the shipped data.
            r = new Route(nodes, "Husk", "killing-flow", "St_D_TheKillingFlow");
            r.L("一撃の冴え", "Keen Single Strike", LinkKind.MemoryDamage, 4);
            r.S("速さを刃に", "Speed into Steel", Stat.AttackSpeedPct, 2);
            r.L("加速の刃の冴え", "Keen Swiftsteel", LinkKind.MemoryDamage, 4);
            r.P("踏み込みの渇き", "Advancing Thirst", Power.Bloodlust, 4);
            r.L("歩みの一閃の冴え", "Keen Striding Flash", LinkKind.MemoryDamage, 4);
            r.S("一歩の剛力", "One-Step Strength", Stat.AttackPct, 2);
            r.L("一歩ごとの刃の冴え", "Keen Blade per Step", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Husk", "wind-scar", "St_D_ScarOfTheWind");
            r.L("風刃の冴え", "Keen Wind Blade", LinkKind.MemoryDamage, 4);
            r.S("傷を刻む腕", "Scar-Carving Arm", Stat.AttackPct, 2);
            r.L("切り裂く風の冴え", "Keen Slashing Wind", LinkKind.MemoryDamage, 4);
            r.P("傷から溢れる命", "Life beyond the Wound", Power.OverflowingLife, 4);
            r.L("追い風の一閃の冴え", "Keen Tailwind Flash", LinkKind.MemoryDamage, 4);
            r.S("深い風傷", "Deep Wind Scar", Stat.DarkAmp, 2);
            r.L("風の刻印の冴え", "Keen Carved Gale", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Husk", "flash-step", "St_M_FlashStep");
            r.L("瞬歩の備え", "Flash Step Readiness", LinkKind.Guard, 1);
            r.S("間合いを断つ腕", "Gap-Cutting Arm", Stat.AttackPct, 2);
            r.L("影道の再接続", "Shadowpath Reconnection", LinkKind.Guard, 1);
            r.P("瞬歩の初撃", "Flash-Step Opener", Power.OpeningStrike, 10);
            r.L("残影の目くらまし", "Blinding Afterimage", LinkKind.Guard, 1);
            r.S("影走りの体幹", "Shadow Runner's Core", Stat.MaxHealthPct, 2);
            r.L("影走りの極み", "Pinnacle of Shadow Running", LinkKind.Guard, 6);

            r = new Route(nodes, "Husk", "laceration", "St_Q_Laceration");
            r.L("二色の巡り", "Two-Color Cycle", LinkKind.MemoryHaste, 3);
            r.S("裂く剛力", "Rending Strength", Stat.AttackPct, 2);
            r.L("赤刃の余勢", "Red Blade Momentum", LinkKind.MemorySurge, 2);
            r.P("青刃の足止め", "Blue Blade Snare", Power.Fetters, 3);
            r.L("赤青の斬撃の冴え", "Keen Red-Blue Slashes", LinkKind.MemoryDamage, 4);
            r.S("裂け目の闇", "Darkness in the Rift", Stat.DarkAmp, 2);
            r.L("赤と青の輪舞", "Red and Blue Rondo", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Husk", "death-mark", "St_Q_DeathMark");
            r.L("楔の再来", "Returning Wedge", LinkKind.MemoryHaste, 2);
            r.S("呪楔の魔力", "Cursed Wedge Power", Stat.PowerPct, 2);
            r.L("刻印の余波", "Mark Aftershock", LinkKind.MemorySurge, 2);
            r.P("刻印の処刑", "Marked Execution", Power.Executioner, 6);
            r.L("呪楔の弾道の冴え", "Keen Cursed Wedge", LinkKind.MemoryDamage, 4);
            r.S("刻み込む闇", "Engraved Darkness", Stat.DarkAmp, 2);
            r.L("消えない楔", "Undying Wedge", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Husk", "annihilation", "St_R_AnnihilationStance");
            r.L("剣気の冴え", "Keen Sword Wave", LinkKind.MemoryDamage, 4);
            r.S("剣気の魔力", "Sword Wave Power", Stat.PowerPct, 2);
            r.L("覚醒の余勢", "Awakening Momentum", LinkKind.MemorySurge, 2);
            r.P("滅びの昂り", "Ruin Surge", Power.UltimateSurge, 4);
            r.L("滅びの波動の冴え", "Keen Ruinous Wave", LinkKind.MemoryDamage, 4);
            r.S("剣気を放つ腕", "Wave-Releasing Arm", Stat.AttackPct, 2);
            r.L("終わらぬ剣気", "Endless Sword Waves", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Husk", "deception", "St_R_Deception");
            r.L("隠れ身の巡り", "Concealment Cycle", LinkKind.MemoryHaste, 2);
            r.S("影から振るう刃", "Blade from Shadow", Stat.AttackPct, 2);
            r.L("奇襲の余波", "Ambush Aftershock", LinkKind.MemorySurge, 2);
            r.P("闇からの奇襲", "Ambush from Shadow", Power.OpeningStrike, 10);
            r.L("影裂きの炸裂の冴え", "Keen Shadow-Rending Blast", LinkKind.MemoryDamage, 5);
            r.S("潜む闇の濃さ", "Lurking Dark Intensity", Stat.DarkAmp, 2);
            r.L("静寂を裂く影", "Silence-Rending Shadow", LinkKind.MemorySurge, 10);

            // Mist: AP opening shields/parries versus AD marked-target and thrust damage.
            r = new Route(nodes, "Mist", "en-garde", "St_D_AstridsMasterpieceEnGarde");
            r.L("初太刀の冴え", "Keen Opening Thrust", LinkKind.MemoryDamage, 4);
            r.S("初太刀の魔力", "Opening Blade Power", Stat.PowerPct, 2);
            r.L("三撃の守り", "Three-Strike Guard", LinkKind.Guard, 2);
            r.P("無傷の剣勢", "Unscathed Swordplay", Power.Vigor, 3);
            r.L("仕掛けの連撃の冴え", "Keen Engaging Flurry", LinkKind.MemoryDamage, 4);
            r.S("受け止める器", "Shield-Bearing Vessel", Stat.MaxHealthPct, 2);
            r.L("崩れぬ初構え", "Unbroken Opening Stance", LinkKind.Guard, 8);

            r = new Route(nodes, "Mist", "priorite", "St_D_AstridsMasterpiecePriorite");
            r.L("標的を穿つ冴え", "Keen Mark Piercing", LinkKind.MemoryDamage, 4);
            r.S("一点を穿つ腕", "Point-Piercing Arm", Stat.AttackPct, 2);
            r.L("決闘刃の冴え", "Keen Duelist's Blade", LinkKind.MemoryDamage, 4);
            r.P("印の追撃", "Marked Pursuit", Power.Fetters, 8);
            r.L("重なる刺突の冴え", "Keen Stacking Thrusts", LinkKind.MemoryDamage, 4);
            r.S("刻み続ける剣", "Relentless Marking Blade", Stat.AttackSpeedPct, 1);
            r.L("六撃目の炸裂の冴え", "Keen Sixth-Strike Blast", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Mist", "fast-feet", "St_M_FastFeet");
            r.L("足運びの備え", "Footwork Readiness", LinkKind.Guard, 1);
            r.S("返し刃の腕", "Counterblade Arm", Stat.AttackPct, 2);
            r.L("踏み直す呼吸", "Resetting Breath", LinkKind.Guard, 1);
            r.P("足跡の守り", "Footstep Ward", Power.Barrier, 2);
            r.L("足さばきのかく乱", "Befuddling Footwork", LinkKind.Guard, 1);
            r.S("剣舞の体幹", "Sword Dance Core", Stat.MaxHealthPct, 2);
            r.L("風のような歩み", "Windlike Strides", LinkKind.Guard, 6);

            r = new Route(nodes, "Mist", "fleche", "St_Q_Fleche");
            r.L("突剣の巡り", "Thrusting Blade Cycle", LinkKind.MemoryHaste, 3);
            r.S("刺突の剛力", "Thrusting Strength", Stat.AttackPct, 2);
            r.L("突剣の余勢", "Thrusting Momentum", LinkKind.MemorySurge, 2);
            r.P("流れる突き", "Flowing Thrust", Power.Momentum, 2);
            r.L("飛び込み突きの冴え", "Keen Lunging Thrust", LinkKind.MemoryDamage, 4);
            r.S("傷を塞ぐ体", "Wound-Mending Body", Stat.MaxHealthPct, 2);
            r.L("途切れぬ突剣", "Unbroken Thrusts", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Mist", "lunge", "St_Q_Lunge");
            r.L("踏み込みの巡り", "Advancing Cycle", LinkKind.MemoryHaste, 2);
            r.S("伸びる剣先", "Reaching Blade", Stat.AttackPct, 2);
            r.L("刺突の余韻", "Thrust Echo", LinkKind.MemorySurge, 2);
            r.P("突きの止め", "Thrust Finisher", Power.Executioner, 6);
            r.L("深く刺す冴え", "Keen Deep Stab", LinkKind.MemoryDamage, 4);
            r.S("突剣の下支え", "Thrust Support", Stat.Armor, 2);
            r.L("往復する切先", "Returning Blade Point", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Mist", "parry", "St_R_Parry");
            r.L("受け流す間合い", "Parrying Distance", LinkKind.Guard, 2);
            r.S("反射の魔力", "Reflection Power", Stat.PowerPct, 2);
            r.L("反撃の余韻", "Riposte Echo", LinkKind.MemorySurge, 2);
            r.P("受け流しの盾", "Parrying Shield", Power.Aegis, 4);
            r.L("構え直す呼吸", "Reforming Stance", LinkKind.MemoryHaste, 2);
            r.S("受け流しの体幹", "Parrying Core", Stat.MaxHealthPct, 2);
            r.L("刃返しの極点", "Perfect Riposte", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Mist", "determination", "St_R_UnbreakableDetermination");
            r.L("覚醒の雷光の冴え", "Keen Awakened Lightning", LinkKind.MemoryDamage, 4);
            r.S("電剣の魔力", "Lightning Blade Power", Stat.PowerPct, 2);
            r.L("意志の余光", "Resolve Afterglow", LinkKind.MemorySurge, 2);
            r.P("覚醒を護る盾", "Awakening Shield", Power.StarShield, 3);
            r.L("立ち上がる剣の冴え", "Keen Rising Blade", LinkKind.MemoryDamage, 4);
            r.S("再起する体", "Renewing Body", Stat.MaxHealthPct, 2);
            r.L("折れぬ剣の意志", "Unbroken Blade Will", LinkKind.MemorySurge, 10);

            // Nachia: transferable armor/speed and AP summons. Moonlight Pact can lose
            // its active cast through a constellation, so its links are equipment-based.
            r = new Route(nodes, "Nachia", "pack-heart", "St_D_HeartOfThePack");
            r.L("団結の光爆の冴え", "Keen Unity Lightburst", LinkKind.MemoryDamage, 4);
            r.S("絆を照らす力", "Bond-Illuminating Power", Stat.PowerPct, 2);
            r.L("心音の炸裂の冴え", "Keen Heartbeat Blast", LinkKind.MemoryDamage, 4);
            r.P("団結の光", "Light of Unity", Power.Radiance, 5);
            r.L("重なる団結の冴え", "Keen Stacking Unity", LinkKind.MemoryDamage, 4);
            r.S("団結の癒やし", "Healing of Unity", Stat.HealPower, 3);
            r.L("一つに弾ける光の冴え", "Keen United Light", LinkKind.MemoryDamage, 18);

            r = new Route(nodes, "Nachia", "circle-life", "St_D_CircleOfLife");
            r.L("巡る命の守り", "Life Cycle Guard", LinkKind.Guard, 2);
            r.S("群れに渡す速さ", "Speed Shared with the Pack", Stat.AttackSpeedPct, 2);
            r.L("巡る命の壁", "Circling Life Ward", LinkKind.Guard, 1);
            r.S("群れに渡す鎧", "Armor Shared with the Pack", Stat.Armor, 3);
            r.L("共に生きる備え", "Shared Life Readiness", LinkKind.Guard, 1);
            r.S("巡り続ける恵み", "Ever-Circling Grace", Stat.HealPower, 3);
            r.L("巡り続ける命脈", "Unbroken Lifeline", LinkKind.Guard, 8);

            r = new Route(nodes, "Nachia", "dreamy-waltz", "St_M_DreamyWaltz");
            r.L("輪舞の守り", "Waltz Guard", LinkKind.Guard, 2);
            r.S("舞う器", "Dancing Vessel", Stat.MaxHealthPct, 2);
            r.L("舞い戻る呼吸", "Returning Dance Breath", LinkKind.Guard, 1);
            r.P("ワルツの守り", "Waltz Ward", Power.Barrier, 2);
            r.L("舞いの波紋", "Waltz Ripples", LinkKind.Guard, 1);
            r.S("群れを包む薄衣", "Veil Enfolding the Pack", Stat.ShieldPower, 4);
            r.L("群れを包む輪舞", "Pack-Enfolding Waltz", LinkKind.Guard, 9);

            r = new Route(nodes, "Nachia", "sylvan-call", "St_Q_SylvanCall");
            r.L("呼び声の巡り", "Calling Cycle", LinkKind.MemoryHaste, 3);
            r.S("森を呼ぶ力", "Forest-Calling Power", Stat.PowerPct, 2);
            r.L("群れの余勢", "Pack Momentum", LinkKind.MemorySurge, 2);
            r.P("着地の静けさ", "Landing Stillness", Power.StillWater, 1);
            r.L("飛びかかる群れの冴え", "Keen Leaping Pack", LinkKind.MemoryDamage, 4);
            r.S("若葉の牙", "Fangs of New Leaves", Stat.SummonPower, 4);
            r.L("五つの遠吠え", "Five Howls", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Nachia", "moonlight-pact", "St_Q_MoonlightPact");
            r.L("月下の跳躍の冴え", "Keen Moonlit Leap", LinkKind.MemoryDamage, 4);
            r.S("月獣を育む力", "Moonbeast-Nurturing Power", Stat.PowerPct, 2);
            r.L("月獣の爪痕の冴え", "Keen Moonbeast Claws", LinkKind.MemoryDamage, 4);
            r.S("月下の癒やし", "Moonlit Healing", Stat.HealPower, 2);
            r.L("月光の斬撃の冴え", "Keen Moonlight Slash", LinkKind.MemoryDamage, 4);
            r.S("月獣の牙", "Moonbeast Fangs", Stat.SummonPower, 4);
            r.L("消えぬ月牙の冴え", "Keen Lasting Moonfang", LinkKind.MemoryDamage, 18);

            r = new Route(nodes, "Nachia", "natures-whisper", "St_R_NaturesWhisper");
            r.L("指揮の巡り", "Command Cycle", LinkKind.MemoryHaste, 2);
            r.S("群れを率いる拍子", "Pack-Leading Tempo", Stat.AttackSpeedPct, 2);
            r.L("号令の護り", "Command Ward", LinkKind.Guard, 2);
            r.P("号令の余力", "Command Reserve", Power.Overload, 3);
            r.L("指揮者の守り", "Commander's Guard", LinkKind.Guard, 1);
            r.S("群れの猛威", "Might of the Pack", Stat.SummonPower, 4);
            r.L("森全体の号令", "Forest-Wide Command", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Nachia", "serpent-blessing", "St_R_SerpentineBlessing");
            r.L("蛇鱗の守り", "Serpent Scale Guard", LinkKind.Guard, 2);
            r.S("祝福を編む力", "Blessing-Weaving Power", Stat.PowerPct, 2);
            r.L("祝福の余韻", "Blessing Echo", LinkKind.MemorySurge, 2);
            r.S("蛇の再生", "Serpent Renewal", Stat.HealPower, 3);
            r.L("蛇の追い撃ちの冴え", "Keen Serpent Follow-Up", LinkKind.MemoryDamage, 4);
            r.P("蛇の守り", "Serpent Ward", Power.StarShield, 3);
            r.L("脱皮する命", "Life Renewed", LinkKind.MemorySurge, 10);

            // Aurena: AP healing does not scale with the HP sacrificed. AD claws/theory
            // remain separate; no extra max-HP tax is imposed on the sacrifice branches.
            r = new Route(nodes, "Aurena", "claw", "St_D_DisintegratingClaw");
            r.L("分解爪の冴え", "Keen Dismantling Claw", LinkKind.MemoryDamage, 4);
            r.S("解きほぐす爪", "Unraveling Claw", Stat.AttackPct, 2);
            r.L("解きほぐす一撃の冴え", "Keen Unraveling Blow", LinkKind.MemoryDamage, 4);
            r.P("爪から溢れる命", "Life beyond the Claw", Power.OverflowingLife, 4);
            r.L("金色の爪痕の冴え", "Keen Golden Clawmarks", LinkKind.MemoryDamage, 4);
            r.S("傷を塞ぐ爪", "Wound-Mending Claw", Stat.HealPower, 2);
            r.L("命を裂く爪の冴え", "Keen Life-Rending Claw", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Aurena", "beautiful-threat", "St_D_BeautifulThreat");
            r.L("金羽の冴え", "Keen Golden Plume", LinkKind.MemoryDamage, 4);
            r.S("羽根を織る知", "Feather-Weaving Wisdom", Stat.PowerPct, 2);
            r.L("羽撃ちの冴え", "Keen Feather Strike", LinkKind.MemoryDamage, 4);
            r.P("羽ばたく渇き", "Fluttering Thirst", Power.Bloodlust, 4);
            r.L("羽根の嵐の冴え", "Keen Feather Storm", LinkKind.MemoryDamage, 4);
            r.S("命を返す羽根", "Life-Restoring Feather", Stat.HealPower, 2);
            r.L("六羽の輝きの冴え", "Keen Six-Plume Shine", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Aurena", "feathery-dash", "St_M_FeatheryDash");
            r.L("羽ばたきの備え", "Wingbeat Readiness", LinkKind.Guard, 1);
            r.S("飛翔の魔力", "Flight Power", Stat.PowerPct, 2);
            r.L("羽根の舞い戻り", "Returning Feather Dance", LinkKind.Guard, 1);
            r.P("舞い降りる光", "Descending Light", Power.Radiance, 5);
            r.L("羽ばたきのかく乱", "Fluttering Confusion", LinkKind.Guard, 1);
            r.S("羽根の加速", "Feather Haste", Stat.AttackSpeedPct, 2);
            r.L("黄金の風切り", "Golden Windcut", LinkKind.Guard, 6);

            r = new Route(nodes, "Aurena", "golden-burst", "St_Q_GoldenBurst");
            r.L("金光の炸裂の冴え", "Keen Golden Blast", LinkKind.MemoryDamage, 4);
            r.S("腐食の叡智", "Corrosion Wisdom", Stat.PowerPct, 2);
            r.L("爆光の余力", "Golden Blast Reserve", LinkKind.MemorySurge, 2);
            r.S("黄金の治癒", "Golden Healing", Stat.HealPower, 2);
            r.L("腐食の輝きの冴え", "Keen Corroded Shine", LinkKind.MemoryDamage, 4);
            r.S("緩やかな献身", "Gentle Devotion", Stat.SacrificeReduction, 3);
            r.L("命を返す閃光", "Life-Restoring Flash", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Aurena", "reduction", "St_Q_Reduction");
            r.L("金片の巡り", "Golden Shard Cycle", LinkKind.MemoryHaste, 2);
            r.S("分かたれる魔力", "Divided Power", Stat.PowerPct, 2);
            r.L("還流の余力", "Return Flow Reserve", LinkKind.MemorySurge, 2);
            r.S("還流する命", "Returning Life", Stat.HealPower, 2);
            r.L("金片の弾道の冴え", "Keen Shard Trajectory", LinkKind.MemoryDamage, 4);
            r.S("欠片に残す命", "Life Spared in the Shards", Stat.SacrificeReduction, 3);
            r.L("三筋の還流", "Threefold Return Flow", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Aurena", "dangerous-theory", "St_R_DangerousTheory");
            r.L("危険な一撃の冴え", "Keen Perilous Strike", LinkKind.MemoryDamage, 4);
            r.S("理論を貫く爪", "Theory-Piercing Claw", Stat.AttackPct, 2);
            r.L("論証の余波", "Proof Aftershock", LinkKind.MemorySurge, 2);
            r.P("窮地の鼓動", "Desperate Heartbeat", Power.Bloodlust, 4);
            r.L("再検証の呼吸", "Reappraisal Breath", LinkKind.MemoryHaste, 2);
            r.S("命を戻す知", "Life-Restoring Wisdom", Stat.HealPower, 2);
            r.L("境界を越える論証", "Proof beyond the Threshold", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Aurena", "chain-reaction", "St_R_ChainReaction");
            r.L("連鎖環の炸裂の冴え", "Keen Chain-Ring Blast", LinkKind.MemoryDamage, 4);
            r.S("魔法陣の深さ", "Depth of the Magic Circle", Stat.PowerPct, 2);
            r.L("連鎖の余光", "Chain Afterglow", LinkKind.MemorySurge, 2);
            r.P("連鎖の守り", "Chain Ward", Power.StarShield, 3);
            r.L("巡る分解光の冴え", "Keen Circling Dissolution", LinkKind.MemoryDamage, 4);
            r.S("陣を巡る命", "Life Circling the Array", Stat.HealPower, 2);
            r.L("命を巡らす大環", "Great Ring of Life", LinkKind.MemorySurge, 10);

            // Bismuth: travelers.json loadoutTrait contains only PrismaticEyes. Books
            // use AP for light/fire and AD for sword/dark arrows; none is an Ultimate.
            r = new Route(nodes, "Bismuth", "prismatic-eyes", "St_D_PrismaticEyes");
            r.L("頁の刃の冴え", "Keen Page Blade", LinkKind.MemoryDamage, 4);
            r.S("自律する魔力", "Autonomous Power", Stat.PowerPct, 2);
            r.L("翻る頁撃ちの冴え", "Keen Flipping-Page Strike", LinkKind.MemoryDamage, 4);
            r.P("四彩の栞", "Four-Color Bookmark", Power.Convergence, 10);
            r.L("彩の追撃の冴え", "Keen Prismatic Follow-Up", LinkKind.MemoryDamage, 4);
            r.S("頁を送る拍子", "Page-Turning Tempo", Stat.AttackSpeedPct, 1);
            r.L("物語の刃の冴え", "Keen Tale's Edge", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Bismuth", "explosion-artist", "St_D_ExplosionArtist");
            r.L("爆炎の冴え", "Keen Blastflame", LinkKind.MemoryDamage, 4);
            r.S("燃える余白", "Burning Margins", Stat.FireAmp, 3);
            r.L("回避の爆発の冴え", "Keen Evasive Explosion", LinkKind.MemoryDamage, 4);
            r.P("飛び火の章", "Chapter of Spreading Fire", Power.Wildfire, 5);
            r.L("連鎖の大炸裂の冴え", "Keen Chained Detonation", LinkKind.MemoryDamage, 4);
            r.S("火の筆致", "Fiery Strokes", Stat.PowerPct, 2);
            r.L("芸術的爆発の冴え", "Keen Artistic Blast", LinkKind.MemoryDamage, 20);

            r = new Route(nodes, "Bismuth", "distorting-sprint", "St_M_Sprint");
            r.L("書架の退避", "Bookshelf Retreat", LinkKind.Guard, 1);
            r.S("駆ける頁の知", "Racing Page Wisdom", Stat.PowerPct, 2);
            r.L("頁路の再接続", "Pagepath Reconnection", LinkKind.Guard, 1);
            r.P("歪む冷気", "Warped Frost", Power.Frost, 5);
            r.L("疾走の目くらまし", "Sprinting Confusion", LinkKind.Guard, 1);
            r.S("旅する装丁", "Traveling Binding", Stat.MaxHealthPct, 2);
            r.L("頁の疾風", "Page Gale", LinkKind.Guard, 6);

            r = new Route(nodes, "Bismuth", "innocence", "St_QR_Innocence");
            r.L("光文の巡り", "Light Script Cycle", LinkKind.MemoryHaste, 3);
            r.S("清き頁の魔力", "Pure Page Power", Stat.PowerPct, 2);
            r.L("霊弾の余光", "Soul Missile Afterglow", LinkKind.MemorySurge, 2);
            r.P("光弾の足止め", "Light Missile Snare", Power.Fetters, 3);
            r.L("霊弾の炸裂の冴え", "Keen Soul-Missile Burst", LinkKind.MemoryDamage, 4);
            r.S("光文字の濃さ", "Light Script Intensity", Stat.LightAmp, 2);
            r.L("三行の祈り", "Three-Line Prayer", LinkKind.MemoryHaste, 10);

            r = new Route(nodes, "Bismuth", "infernal-tales", "St_QR_InfernalTales");
            r.L("炎文の余熱", "Fire Script Afterheat", LinkKind.MemorySurge, 2);
            r.S("燃える頁の魔力", "Burning Page Power", Stat.PowerPct, 2);
            r.L("業火の輪の冴え", "Keen Hellfire Ring", LinkKind.MemoryDamage, 5);
            r.P("頁を渡る火", "Page-Spreading Fire", Power.Wildfire, 5);
            r.L("炎文の巡り", "Fire Script Cycle", LinkKind.MemoryHaste, 2);
            r.S("火文字の濃さ", "Fire Script Intensity", Stat.FireAmp, 2);
            r.L("燃え続ける物語", "Ever-Burning Tale", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Bismuth", "valiant-heart", "St_QR_ValiantHeart");
            r.L("剣文の巡り", "Sword Script Cycle", LinkKind.MemoryHaste, 2);
            r.S("勇剣の剛力", "Valiant Sword Strength", Stat.AttackPct, 2);
            r.L("剣閃の余韻", "Sword Flash Echo", LinkKind.MemorySurge, 2);
            r.P("無傷の物語", "Unscathed Tale", Power.Vigor, 3);
            r.L("勇剣の冴え", "Keen Valiant Edge", LinkKind.MemoryDamage, 5);
            r.S("心を守る装丁", "Heart-Warding Binding", Stat.ShieldPower, 3);
            r.L("折れない物語", "Unbroken Tale", LinkKind.MemorySurge, 10);

            r = new Route(nodes, "Bismuth", "distorted-mind", "St_QR_DistortedMind");
            r.L("闇文の蓄積", "Dark Script Accumulation", LinkKind.MemorySurge, 2);
            r.S("心矢の張力", "Mind Arrow Tension", Stat.AttackPct, 2);
            r.L("精神矢の冴え", "Keen Mind Arrow", LinkKind.MemoryDamage, 5);
            r.P("矢に宿る闇", "Darkness in the Arrow", Power.Umbra, 5);
            r.L("闇文の巡り", "Dark Script Cycle", LinkKind.MemoryHaste, 2);
            r.S("闇文字の濃さ", "Dark Script Intensity", Stat.DarkAmp, 2);
            r.L("四矢の結末", "Four-Arrow Ending", LinkKind.MemorySurge, 10);

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
            Ring(nodes, hero, "resolve", "夢輪の不屈", "Dream Ring Resolve", Stat.Tenacity, 2);
            Ring(nodes, hero, "renewal", "夢輪の再生", "Dream Ring Renewal", Stat.HealthRegen, 1);
        }

        private static void Ring(List<TalentDef> nodes, string hero, string id, string ja, string en, Stat stat, int value)
            => nodes.Add(new TalentDef("h." + hero.ToLowerInvariant() + ".ring." + id, Line.Resonance,
                new Txt(ja, en), stat, value, 5) { HeroKey = "Hero_" + hero, Tier = 2, IsDreamRing = true });
    }
}
