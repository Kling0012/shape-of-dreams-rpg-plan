using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>中国語（簡体字）の用語集（#877）。英語の文字列を鍵にして中国語へ訳す。
    /// ここにない文字列と、数値を埋め込んだ呼び出し側で組み立てた文字列は英語のまま表示する
    /// （未訳は英語へ戻す方針。呼び出し元の Loc.T / Txt の英語引数と完全一致が条件）。
    /// 主な用語の対応: 遺物→遗物、欠片→碎片、調律石→调律石、保管庫→仓库、確保→存入仓库、確保地点→存仓点、
    /// 潜る→下潜、夢の圧→梦境之压、星図→星图、鍛冶→锻造、工房→工坊、記憶→记忆、
    /// エッセンス→精华、遠征→远征、道標→路标、契約→契约、依頼→委托、図鑑→图鉴、遺失物→遗失物。
    /// #939 で追加した主な用語: 偉業→成就、覚醒→觉醒、限界突破→极限突破、ドリームダスト→梦境之尘、
    /// 再調律→再调律、洗い直し→重洗、特性→词条、禁忌→禁忌、製作→制作、変種→变种、兆し→预兆。
    /// 銘品・小セット名の訳語は、クラス内の参照表（#941・#942・#956）にまとめる。
    /// #963 のセット名で繰り返す語: Oath / Vow→之誓、Covenant→契约、Regalia→礼装、Garb→装束、
    /// Raiment / Vestments→法衣、Ensemble→乐团、Eclipse→月蚀。セット名の訳文は sets.json の name.zh に置く。
    /// 銘品・小セット名の訳で繰り返し出る語（#942）: 火の粉→火花、星屑→星屑、蝕→蚀、
    /// 追い風→追风、終曲→终曲、会心→会心、白い霧・湯気→白雾、切札→王牌、分け合い→共享、止水→止水。
    /// 影は通常の影を指すときは「影」、闇や影の力を指すときは「暗影」と訳す。
    /// 継ぎ糸→生命线、余韻→余韵、見守り→守望、備え→常备、飛び火→燎原、旋風→旋风、
    /// 共演→共演、星傘→星伞、乱気→乱流、吸命→吸命、跡目・相続→继承、守護霊→守护灵。
    /// 辞書は最初の参照で組み立て、読み取り専用で使い回す（表示のたびに作り直さない）。
    /// </summary>
    internal static class LocZhGlossary
    {
        // 銘品・小セットの名前・説明で繰り返し使う訳語（#941・#942）。Txt の第3値を書くときの参照用。
        // 名前の一部なので、下の完全一致辞書には登録しない。
        // 影は通常の影なら「影」、闇や影の力なら「暗影」と訳す。
        // Sparks: 火の粉 → 火花 / Stardust: 星屑 → 星屑 / Eclipse: 蝕 → 蚀
        // Finale: 終曲 → 终曲 / Critical: 会心 → 会心 / Share: 分け合い → 共享
        // Stillwater: 止水 → 止水 / Echo: 余韻 → 余韵 / Wildfire: 飛び火 → 燎原
        // Starshade: 星傘 → 星伞
        // Crack: 砕音・砕け音 → 碎响
        // Brittle Ice: 脆氷 → 脆冰
        // Co-Star: 共演 → 联袂
        // Steam / White Mist: 湯気・白い霧 → 白雾
        // Guardian: 守護霊 → 守护灵
        // Heirloom / Heir: 跡目・相続 → 继承
        // Boon: 恵み → 恩泽（複合名では「恩」）
        // Ember: 残り火・燠火 → 余烬
        // Tailwind: 追風・追い風 → 顺风
        // Lifeline: 継ぎ糸 → 生命线
        // Readiness: 備え → 戒备
        // Immovability: 不動 → 不动
        // Whirl / Whirlwind: 旋風 → 旋风
        // Dash: 疾駆 → 疾驰
        // Siphon / Soulreaper: 吸魂 → 摄魂
        // Leech: 吸命 → 吸命
        // Watch: 見守り → 守望
        // Relay: 継ぎ・継ぎ手 → 接力
        // Trump: 切札・切り札 → 王牌
        // Cycle: 巡り → 巡回
        // Circuit: 循環 → 循环
        // Turbulence: 乱気 → 乱流
        // Verdict: 断罪 → 断罪
        // Kind Return / Kindness: 返礼 → 回礼、返し → 回善
        // Second Wind / Second Breath: 再起・二の息・再びの息 → 回气
        // Legendary: 固有品 → 传说（Content.RarityName のレア度表記）
        // 固有品・セット部位で繰り返し使う訳語（#955）。名前の一部なので辞書には登録しない。
        // Tidecaller: 潮鳴り → 潮鸣
        // Lampkeeper: 灯守 → 守灯
        // Cinder / Embercoal: 残火・熾火・燃え殻 → 余烬
        // Dusk: 黄昏 → 黄昏
        // Winterbound: 冬枯れ → 凋冬
        // Starsinger: 星詠み → 咏星
        // Hunting Party: 狩猟団 → 狩猎团
        // Bastion: 不落城 → 不落城
        // Wildfire: 燎原 → 燎原
        // Grove: 古森 → 古森
        // Reverie: 夢想 → 梦想
        // Phantom: 幻影 → 幻影
        // Permafrost: 凍土 → 冻土
        // Carnage: 修羅道 → 修罗
        // Galedancer: 風舞 → 风舞
        // Firmament: 天穹 → 天穹
        // Nightstrike: 闇討ち → 暗袭
        // Ashrunner / Ashrun: 灰走り → 灰行者（セット部位）・灰行（駆け靴）
        // Healer: 施療 → 施疗
        // Ironknight: 鉄騎 → 铁骑
        // Eclipse: 蝕 → 蚀、日蝕 → 日蚀、月蝕 → 月蚀
        // Thunderclap: 迅雷 → 迅雷
        // Myriad: 万象 → 万象
        // Dawnbreak: 払暁 → 破晓
        // Icicrystal: 氷晶 → 冰晶
        // Mistsimmer / Mistwater / Steammist: 霧湯・湯霧 → 雾汤
        // Ashfall / Cinderfall: 灰降り・降り灰 → 落灰
        // Eclipse-Rite: 蝕の儀 → 蚀仪
        // Canticle: 聖歌 → 圣歌
        // Holy-Ice: 聖氷 → 圣冰
        // Crystal: 結晶 → 结晶
        // Ward: 障壁 → 屏障、護り → 守护
        // Bracers: 腕輪 → 护腕
        // Locket: ロケット → 坠饰（複合名では「坠」）
        // Greaves / Treads: 足甲・脚絆 → 胫甲
        // #958 で追加した主な訳語（装備データ 8/8。Txt の第3値を書くときの参照用）:
        // セット名の接頭辞: 砕盾騎士 Shardshield → 碎盾骑士、盾誓い Bashbound → 盾誓、陣頭 Frontline → 阵头、
        //   後陣 Rearline → 后阵、輪守り Ringwarden → 环守、継ぎ歌 Relaysong → 接力歌、宴 Feastpack → 狼宴、
        //   弔い花 Funeral-Bloom → 葬花、一点狙い Single-Mark → 单点锁定、渡り鳥 Migrant → 候鸟、
        //   運刃 Fortune-Edge → 运刃、突撃隊 Charge-Corps → 突击队、楽団 Ensemble → 乐团、勝負師 Gambler → 赌师、
        //   無手の誇り Bare-Hand → 空手之傲、寝ずの番 Vigil → 守夜、供犠 Tithe → 献祭、舞い星 Dancing-Star → 舞星、
        //   突破隊 Breakout → 突破队、荒ぶる樹界 Rampaging Grove → 狂暴树界。
        // 固有効果の説明の語: 攻撃力・魔力の高い方 → 攻击力与法术强度中较高者、障壁 → 护盾、通常攻撃 → 通常攻击、
        //   会心 → 暴击、効果時間が延びる → 刷新持续时间、重ならず → 不叠加、装備・星の同じ効果 → 装备与星的同名效果、
        //   距離係数 → 距离系数、ハンター追跡度 → 猎人追踪等级、ゴールド → 金币、スタン → 眩晕、スロウ → 减速、
        //   冷気（CC）→ 冰缓、聖堂 → 神殿、通常記憶 → 通常记忆、移動の記憶 → 移动记忆、奥義の記憶 → 奥义记忆。
        // 通常セットの説明（#963）: Cold → 冰冷、Shield → 护盾、Critical → 暴击、
        // Basic attack → 通常攻击、ordinary memory → 通常记忆、Movement memory → 移动记忆、
        // Ultimate memory → 奥义记忆、Identity memory → 本体记忆、Overcharge → 过载、
        // cooldown → 冷却时间、overhealing → 过量治疗、distance falloff → 距离衰减。
        // 訳文は sets.json の description.zh に置き、用語の部分一致による置換には使わない。
        // #956 で追加した主な用語（Content.cs 3190〜3788 の固有品で繰り返し使う訳語）:
        // Rearguard: 後衛 → 后卫 / Vanguard・先陣: 前衛・先陣 → 前卫・先锋
        // Pack: 群れ → 兽群 / Omen: 予兆・前兆 → 预兆 / Stardust: 星屑 → 星屑
        // Prism: 七彩 → 七色 / Grudge: 怨嗟 → 怨叹 / Triumph: 凱歌・凱旋 → 凯歌・凯旋
        // Opening fire: 口火 → 开战火（紅蓮の点火は点火）/ Pile-On: 畳み掛け → 乘胜
        // Duelist: 決闘者 → 决斗者 / Glacier core: 氷河核 → 冰河核心 / Return: 戻り・帰還 → 回返・归还
        // Ward: 障壁・護り → 护盾・护佑 / Lampkeeper: 灯守 → 守灯人 / Candlerobe: 灯衣 → 灯衣
        // Skirmisher: 遊撃 → 游击 / Rampart: 土塁 → 土垒 / Ascetic: 修行者 → 行者
        // Moonsilk: 月絹 → 月绢 / Zephyr: 西風 → 西风 / Citadel: 砦・城塞 → 城塞
        // Burrower: 土竜・潜り屋 → 掘地兽 / Buckler: 丸盾・小盾 → 小盾
        // 部位の語: 胸当て→胸甲、胸甲→胸铠、兜→头盔・盔、頭巾→兜帽、胴衣→背心、
        // 鎖帷子→锁甲、樹皮鎧→树皮铠、肩衣→披肩、道着→道服、籠手→笼手、腕当て→护腕
        // #951 の基本装備名・セット説明で繰り返し使う訳語（完全一致辞書への登録は不要）:
        // Resonance / Resonant: 共鳴 → 共鸣 / Dreamer: 夢見 → 织梦者
        // Oath: 誓い → 誓言 / Vow: 誓約 → 誓约 / Warden: 番人 → 守卫
        // Deeproot: 深根 → 深根 / Boulder: 岩塊 → 岩块 / Tortoiseshell: 亀甲 → 龟甲
        // Sunlit: 陽光 → 阳光 / Dawnlight・Dawnmist: 暁光・暁霧 → 晓光・晓雾
        // Dusk: 黄昏 → 黄昏（織り・縫い・踏み・珠との複合名では「暮」）
        // Seafoam: 海沫 → 海沫 / Whisper: 囁き → 低语 / Comet: 彗星 → 彗星
        // Healer / Healing / Mender: 癒し手・癒し → 治愈者・治愈
        // Fingerless Gloves / Mitts: 指抜き・指なし手袋 → 半指手套 / Wraps: 手巻き → 缠手
        // Grips: 握り → 护手 / Knuckles: 拳当て → 拳套 / Greaves: 脛当て → 护胫
        // Sabatons: 鉄靴・鉄鞋 → 铁靴 / Slippers: 上履き・室内履き → 便鞋
        // Locket: ロケット → 吊坠盒 / Pendant: 垂飾り → 吊坠 / Brooch: ブローチ → 胸针
        // Link: 連携 → 联动 / piece stage: 装着段階 → 件套阶段
        // #952 の基本装備・固有品で繰り返し使う訳語（名前の一部なので参照用）:
        // Hoarfrost / Rime: 霧氷 → 雾凇 / Remembrance・Nostalgia: 追憶 → 追忆
        // Keepsake: 形見 → 遗念 / Locket: ロケット → 盒坠 / Pendant: 垂飾り → 吊坠
        // Fingerless / Mitts: 指抜き・指なし → 露指手套 / Wraps: 手巻き → 缠手
        // Band: 鉢巻 → 头带、腕輪 → 腕环 / Greaves: 脛当て → 护胫
        // #954 の固有品で繰り返し使う訳語（Txt の第3値を書くときの参照用）:
        // Shrine: 聖堂 → 圣堂 / Crystal: 結晶 → 结晶 / Fireball: 火球 → 火球
        // Ultimate: 奥義・切り札（技） → 终极技能 / Overflowing healing: 溢れた癒し → 溢出的治疗
        // Blizzard: 吹雪・雪嵐 → 暴风雪 / Pure White: 純白 → 纯白 / Gold Coin: 金貨 → 金币
        // Doubt・Suspicion: 疑心・疑い → 疑心 / Cage: 檻 → 牢笼 / Furnace: 炉・溶鉱炉 → 熔炉
        // Tidecaller's（セット名）: 潮鳴り → 潮鸣 / Prism（光学の語）: プリズム → 棱镜
        // Dodge: 回避 → 闪避 / Crimson（紅蓮の名）: 紅蓮 → 红莲 / Banquet: 祝宴・饗宴 → 盛宴
        // #953 で追加した主な用語（Content.cs 1466〜2066 の固有品で2回以上使う訳語）:
        // Ultimate: 奥義・大技 → 奥义 / Star shield: 星の盾 → 星辰之盾
        // Crystal: 結晶 → 结晶 / Shadowstitch: 影縫い → 缝影
        // Beads: 数珠 → 念珠 / Curtain Call: 終幕 → 终幕 / Prey: 獲物 → 猎物
        private static Dictionary<string, string> _map;

        /// <summary>英語の文字列から中国語を引く。なければそのまま英語を返す（割り当てなし）。</summary>
        public static string Lookup(string en)
        {
            var map = _map ??= Build();
            return map.TryGetValue(en, out string zh) ? zh : en;
        }

        private static Dictionary<string, string> Build()
        {
            return new Dictionary<string, string>
            {
                // ウィンドウとタブ
                ["Dreamforge ─ Relics of the Dream"] = "Dreamforge ─ 梦之遗物",
                ["Gear"] = "装备",
                ["Forge"] = "锻造",
                ["Star Map"] = "星图",
                ["Workshop"] = "工坊",
                ["Records"] = "记录",
                ["Trade"] = "交易",
                ["Trade ●"] = "交易 ●",
                ["Close"] = "关闭",
                ["Close selection"] = "关闭选择",
                ["How to play"] = "玩法说明",
                ["Settings"] = "设置",
                ["Show tips again"] = "再次显示提示",

                // HUD
                ["<b>Dreamforge</b>  Dream Lv "] = "<b>Dreamforge</b>  梦境等级 ",
                ["Dream depth "] = "梦境深度 ",
                [" · Pressure "] = " · 梦境之压 ",
                [" · Shared pressure "] = " · 共享梦境之压 ",
                [" · ATK×"] = " · 攻×",
                ["awaiting host"] = "等待主机确认",
                ["Infinity · cleared "] = "无限 · 已通关 ",
                [" rooms · cycle "] = " 房间 · 周期 ",
                [" · pressure stage "] = " · 压力阶段 ",
                [" (cap)"] = "（上限）",
                ["Save mismatch: progression paused; recovery required"] = "存档不一致：进度暂停，等待恢复",
                ["Waypoint: "] = "路标：",
                ["Today: "] = "今日之梦：",
                ["Delve "] = "下潜 ",
                ["Pacts: "] = "契约：",
                ["Gear bonuses not applied yet (the host may not have this mod)"] = "装备效果尚未生效（主机可能未安装本MOD）",
                ["] Menu"] = "] 菜单",

                // 確保地点のパネル
                ["Secure Point ─ take your loot home, or delve deeper"] = "存仓点 ─ 选择带回战利品，或继续深入",
                ["Secure: "] = "存入仓库：",
                ["Delve: "] = "下潜：",
                ["Finish without a pact"] = "不结契约，完成选择",
                ["Skip (no choices)"] = "跳过（不作选择）",
                ["Skip the waypoint"] = "不选路标",
                ["No waypoint"] = "无路标",
                ["The host chooses the waypoint."] = "路标由主机选择。",
                ["Waiting for the host to confirm a waypoint and choose Secure or Delve."] = "请等待主机确定路标并选择存入仓库或下潜。",

                // 装備・鍛冶の画面でよく出る語
                ["All"] = "全部",
                ["None"] = "无",
                ["Auto"] = "自动",
                ["Solo"] = "单人",
                ["Multi"] = "多人",
                ["Clear"] = "清除",
                ["Equip"] = "装备",
                [" equipped"] = "（已装备）",
                ["Lock"] = "锁定",
                ["Unlock"] = "解锁",
                ["Unlock to salvage"] = "解锁后可分解",
                ["Search name"] = "按名称搜索",
                ["Stats"] = "属性",
                ["Powers"] = "固有效果",
                ["Named"] = "铭品",
                ["Traveler"] = "旅人",
                ["Base: "] = "基底：",
                ["Rarity: "] = "稀有度：",
                ["Slot: "] = "部位：",
                ["Selected: "] = "已选：",
                ["Selected relics"] = "已选遗物",
                ["No relics selected."] = "未选择遗物。",
                ["Tuning"] = "调律石",
                ["Mini set"] = "小套装",
                ["Mini sets"] = "小套装",
                ["Choice stars"] = "二选一星",
                ["Memory essence slots"] = "记忆精华槽",
                ["Effects by memory"] = "各记忆的效果",
                ["Edit slot"] = "编辑部位",
                ["Satchel (unequipped only)"] = "背包（仅未装备）",
                ["World "] = "世界 ",
                [" (chosen)"] = "（已选）",
                [" (unselected)"] = "（未选）",
                [" (maximum)"] = "（上限）",
                ["(Reason: "] = "（原因：",
                [" ("] = "（",
                [")"] = "）",
                ["% less)"] = "%）",
                ["Expedition training"] = "远征锻炼",

                // 交換
                ["Your offer"] = "你付出的物品",
                ["You: "] = "自己：",
                ["Waiting for the trade to complete."] = "正在等待交易完成。",
                ["Relic reserved for this trade."] = "该遗物已为本次交易预留。",

                // 状態・通知で繰り返し出る文
                ["Could not save settings."] = "无法保存设置。",
                ["Only during an expedition."] = "仅限远征中。",
                ["Not connected to a game."] = "未连接到游戏。",
                ["Waiting for save to finish"] = "正在等待保存完成",
                ["Pick a material"] = "选择材料",
                ["Pick a material:"] = "选择材料：",
                ["Enter a nonnegative whole number for each material."] = "请为每种材料输入0以上的整数。",
                ["Cannot change during an expedition"] = "远征中无法更改",
                ["Cannot change during an expedition. Switching is free after returning."] = "远征中无法更改。返回后可免费切换。",
                ["The workshop is closed during expeditions."] = "远征中工坊不可用。请从远征返回后再使用。",
                ["Fusion requires a secure point or being outside an expedition"] = "融合需要在存仓点或远征之外进行",
                ["Not enough materials for fusion"] = "融合材料不足",
                ["Unrecorded difficulty"] = "难度未记录",
                ["Developer grant: "] = "开发者授予：",
                ["Choose a waypoint at a secure point."] = "可在存仓点选择路标。",
                ["Preparing to continue"] = "正在准备继续",
                ["The profile will switch after the expedition."] = "远征结束后切换档案。",
                ["Your choices are complete."] = "你的选择已完成。",
                ["Melee co-op effects"] = "近战协作效果",
                ["Melee co-op effects (while the basic attack is melee)"] = "近战协作效果（通常攻击为近战时）",
                ["Automatic casting"] = "自动施放",

                // その他のゲームデータ（#962）
                ["Road of Blades"] = "刀锋之路",
                ["Road of Armor"] = "护甲之路",
                ["Road of Charms"] = "护符之路",
                ["Road of Crowns"] = "王冠之路",
                ["Road of Gauntlets"] = "护手之路",
                ["Road of Footsteps"] = "足迹之路",
                ["Nightmare Hunt"] = "梦魇狩猎",
                ["Glass Aegis"] = "玻璃护盾",
                ["Road of Reactions"] = "反应小径",
                ["Endless Night"] = "永夜",
                ["Sealed Hoard"] = "封印宝库",
                ["Tempered Road"] = "淬火之路",
                ["Fleeting Memories"] = "疾驰记忆",
                ["Trail of the Pack"] = "兽群小径",
                ["Violet Mirage"] = "紫色蜃景",
                ["River of Shards"] = "碎片之河",
                ["Twin Cache"] = "孪生宝箱",
                ["Offering to the Stars"] = "献星之礼",
                ["Humble Forge"] = "朴素锻炉",
                ["Tribute to the Gate"] = "守门者贡品",
                ["Sixfold Trail"] = "六重足迹",
                ["Pilgrimage of Awakening"] = "觉醒巡礼",
                ["First Claim"] = "首杀之宝",
                ["Trail Supplies"] = "沿途补给",
                ["Deepening Night"] = "渐深之夜",
                ["weapons"] = "武器",
                ["armor"] = "护甲",
                ["charms"] = "饰品",
                ["headgear"] = "头部装备",
                ["hand gear"] = "手部装备",
                ["footwear"] = "脚部装备",
                ["Kindling Crit"] = "引火暴击",
                ["Sunlight Carry-Over"] = "延续阳光",
                ["Charge into Sanctuary"] = "圣域冲锋",
                ["Rekindled Charge"] = "重燃冲锋",
                ["Twofold Mercy"] = "双重慈悲",
                ["Kindled Baptism"] = "燃火洗礼",
                ["Command Crit"] = "号令暴击",
                ["Powder Double-Report"] = "火药双响",
                ["One After Three"] = "三连后的一发",
                ["Barrage Footing"] = "连射立足",
                ["Double-Tap Trail"] = "双射足迹",
                ["Twin Embers"] = "双重余烬",
                ["Sniper's Reload"] = "狙击装填",
                ["Powder Sniper Support"] = "火药狙击支援",
                ["Frozen Ground Recall"] = "冻土召回",
                ["Chill of the Push-Back"] = "击退寒流",
                ["Ice-Run Command"] = "冰行号令",
                ["Thunder Trail"] = "雷鸣之路",
                ["Thunder Called to Ice"] = "冰中唤雷",
                ["Glacier and Fists"] = "冰川与拳",
                ["Icy Fist Mending"] = "冰拳疗愈",
                ["Matter's Lure"] = "物质诱引",
                ["Calamity Afterglow"] = "天灾余辉",
                ["Meteor Blink"] = "陨石闪烁",
                ["Stardust Blink"] = "星尘闪烁",
                ["Marked-Star Reload"] = "星标装填",
                ["Calm Light"] = "宁静之光",
                ["Quiet Blast"] = "静谧爆发",
                ["Laceration Sure Crit"] = "裂伤必暴",
                ["Aura Follow-Slash"] = "剑气追斩",
                ["Wind-Guided Aura"] = "风引剑气",
                ["Windscar Footprints"] = "风伤足迹",
                ["Wedge Footprints"] = "楔印足迹",
                ["Mark Release"] = "刻印解放",
                ["Assassin's Finish"] = "刺杀终结",
                ["First-Strike Insight"] = "初击洞察",
                ["Awakened Light Thrust"] = "觉醒光刺",
                ["Awakened Footwork"] = "觉醒步法",
                ["Cornering Steps"] = "追逼步伐",
                ["Driving Thrust"] = "逼退突刺",
                ["Thrust and Riposte"] = "突刺与还击",
                ["Riposte Stance"] = "还击架势",
                ["Pack's Call"] = "兽群呼唤",
                ["Command Bite"] = "号令追咬",
                ["Command and Dance"] = "号令与舞",
                ["Round Dance Breath"] = "轮舞吐息",
                ["Wolf's Watch"] = "狼之守望",
                ["Claw of Blessing"] = "祝福之爪",
                ["Serpent-Scale Unity"] = "蛇鳞团结",
                ["Golden Claw Mark"] = "黄金爪痕",
                ["Golden Light of Theory"] = "理论金光",
                ["Wings of Theory"] = "理论之翼",
                ["Feather Return"] = "羽翼回旋",
                ["Feather of Shards"] = "碎片之羽",
                ["Shards on the Circle"] = "阵中碎片",
                ["Harvest on the Circle"] = "阵中收获",
                ["Light-Page Follow-Up"] = "光页追击",
                ["Soul Footprints"] = "灵魂足迹",
                ["Flame Sprint"] = "烈焰疾跑",
                ["Flame-Blade Recall"] = "炎刃召回",
                ["Arrow Mending"] = "箭矢疗愈",
                ["Book Finishes the Arrows"] = "书卷终箭",
                ["Saving is paused because the previous data cannot be safely preserved. It will be tried again on the next launch."] = "无法安全保留已有数据，已暂停保存。下次启动时将重试。",
                ["The saved file did not read back identically."] = "保存文件的读取内容不一致。",
                ["The profile did not survive a save/load round trip."] = "档案经过保存和读取后的内容不一致。",
                ["Legendary"] = "传说",
                ["Keystone"] = "刻印",
                ["Normal memory"] = "普通记忆",
                ["Basic attack"] = "通常攻击",
                ["Ability power"] = "法术强度",
                ["Shield"] = "护盾",
                ["Cooldown reduction"] = "冷却缩减",
                ["Follow-up damage"] = "追击伤害",
                ["Bridge combo"] = "桥接组合技",
                ["Cooperative trade"] = "合作交易",
                ["Escrow"] = "托管",
                ["Receipt"] = "收据",
                ["Unsecured relics"] = "尚未存入仓库的遗物",
                ["Awakening points"] = "觉醒点",
                ["Nightmare"] = "梦魇",
                ["Star experience"] = "星之经验",
                ["Overcharge"] = "过载",

                // 旅人の星と刻印（#962）
                ["Fourth Form"] = "第四式",
                ["Holy Strength"] = "神圣巨力",
                ["Oath-Bound Body"] = "誓约之躯",
                ["Blessing of Light"] = "光之庇护",
                ["Inquisitor's Stance"] = "审判者架势",
                ["Thrives when you taunt enemies onto you."] = "适合通过嘲讽将敌人引向自身。",
                ["Prayerful Flurry"] = "祈祷连击",
                ["Unbreakable Inquisition"] = "坚不可摧的审判",
                ["The more enemies you taunt, the faster you swing (up to 8)."] = "嘲讽的敌人越多，攻击越快（最多计算8个）。",
                ["Holy Armor"] = "神圣护甲",
                ["Shield of Inquisition"] = "审判之盾",
                ["Disciplined Might"] = "律令之力",
                ["Judgment Blow"] = "裁决一击",
                ["Iron Formation"] = "钢铁阵势",
                ["Brand of Light"] = "光之烙印",
                ["Double Load"] = "双重装填",
                ["Gunpowder"] = "火药",
                ["Armored Coat"] = "防弹外套",
                ["Rapid Fire"] = "速射",
                ["Salamander Ember"] = "火蜥蜴余烬",
                ["Keep foes burning with basic attacks so Salamander Powder's 4th-shot blast deals double."] = "通过通常攻击使敌人持续燃烧，让火蜥蜴火药的第4发爆炸造成双倍伤害。",
                ["Marksman"] = "神射手",
                ["Opening Shot"] = "开场射击",
                ["Rewards striking first from afar."] = "适合从远处先发制人。",
                ["Powder Mixing"] = "火药调配",
                ["Steady Aim"] = "稳定瞄准",
                ["Iron Stock"] = "钢铁枪托",
                ["Explosive Rounds"] = "爆裂弹",
                ["Fireproof Coat"] = "防火外套",
                ["First Shot"] = "首发必中",
                ["Abyssal Cold"] = "深海寒气",
                ["Ice Shell"] = "冰壳",
                ["Tidal Breath"] = "潮汐吐息",
                ["Abyssal Power"] = "深海魔力",
                ["Freezing Tide"] = "冰冻潮汐",
                ["Feeds your Icy Veins stacks."] = "与冰之血脉的层数相配合。",
                ["Tidal Scales"] = "潮汐之鳞",
                ["Calm Sea"] = "风平浪静",
                ["Each time your skills stun a foe, your shield is renewed."] = "技能每次眩晕敌人时，都会重新获得护盾。",
                ["Abyssal Wisdom"] = "深渊智慧",
                ["Ice Ward"] = "冰之守护",
                ["Vessel of the Deep"] = "深海之器",
                ["High Tide"] = "涨潮",
                ["Ice Shackles"] = "冰之枷锁",
                ["Ice Wall"] = "冰墙",
                ["Stellar Haste"] = "星辰加速",
                ["Starlight"] = "星光",
                ["Cosmic Law"] = "宇宙法则",
                ["Stellar Pulse"] = "星辰脉动",
                ["Converging Light"] = "汇聚之光",
                ["Pair it with charging Exotic Matter before your Ultimate."] = "适合先积蓄奇异物质，再使用Ultimate。",
                ["Stellar Vessel"] = "星辰之器",
                ["Stellar Ward"] = "星辰之盾",
                ["Shields you the moment you unleash your Ultimate."] = "释放Ultimate的瞬间为你提供护盾。",
                ["Supernova"] = "超新星",
                ["Star Armor"] = "星辰护甲",
                ["Guiding Star"] = "指引之星",
                ["Gravity Surge"] = "重力涌动",
                ["Event Horizon"] = "事件视界",
                ["Stardust Glow"] = "星尘辉光",
                ["Deep Shadow"] = "深邃暗影",
                ["Vital Strike"] = "要害打击",
                ["Flurry"] = "连打",
                ["Hollow Armor"] = "空洞护甲",
                ["Hollow Blade"] = "空洞之刃",
                ["Every hit layers Dark, and a critical hit adds one more."] = "每次命中叠加暗属性，暴击时再额外叠加1层。",
                ["Hollow Fang"] = "空洞之牙",
                ["Flash-Step Blade"] = "瞬步之刃",
                ["Shadow Fang"] = "暗影之牙",
                ["Hollow Vessel"] = "空洞之器",
                ["Depth of Darkness"] = "黑暗深处",
                ["Shroud of Dark"] = "暗之帷幕",
                ["Finishing Blade"] = "终结之刃",
                ["Hunting Momentum"] = "狩猎之势",
                ["Blade Dance"] = "剑舞",
                ["Sword Spirit"] = "剑气",
                ["Flash Cut"] = "闪斩",
                ["Duelist"] = "决斗者",
                ["Form of the First Strike"] = "先手之式",
                ["Your opening blow on an unhurt foe hits harder, pairing with styles that empower the first strikes on each enemy."] = "对未受伤敌人的首次打击更强，适合强化对每个敌人最初数次攻击的战斗方式。",
                ["Guarded Stance"] = "守御架势",
                ["Form of the Counter"] = "反击之式",
                ["For 3s after taking a hit, your attack and ability power rise."] = "受到攻击后的3秒内，攻击力与法术强度提高。",
                ["Keen Point"] = "锐利剑尖",
                ["Trained Body"] = "锻炼之躯",
                ["Parry Stance"] = "招架架势",
                ["Masterwork Shield"] = "杰作之盾",
                ["First-Strike Stance"] = "先手架势",
                ["Take and Return"] = "承击还击",
                ["Blessed Light"] = "祝福之光",
                ["Bond Power"] = "羁绊之力",
                ["Calling"] = "呼唤",
                ["Guardian Vow"] = "守护誓言",
                ["Together"] = "相伴而行",
                ["Best alongside your allies."] = "适合与友方并肩作战。",
                ["Pack Pace"] = "兽群步调",
                ["Overflowing Prayer"] = "满溢祈祷",
                ["Suits chaining your skills."] = "适合连续使用技能。",
                ["Healing Song"] = "治愈之歌",
                ["Thinking of Friends"] = "思念友人",
                ["Prayer of Shields"] = "护盾祈祷",
                ["Hearts in Tune"] = "心意共鸣",
                ["Guarding Light"] = "守御之光",
                ["Keep the Light"] = "长明之灯",
                ["Wingbeat"] = "振翅",
                ["Golden Light"] = "黄金之光",
                ["Chalice Guard"] = "圣杯守护",
                ["Devotion"] = "祈愿",
                ["Blood Devotion"] = "鲜血奉献",
                ["Each HP-sacrifice skill you cast sharpens your ability power."] = "每次使用消耗生命值的技能，都会提高法术强度。",
                ["Golden Claw"] = "黄金之爪",
                ["Brimming Chalice"] = "满溢圣杯",
                ["Healing that overflows becomes a shield."] = "过量治疗转为护盾。",
                ["Cycle of Renewal"] = "再生循环",
                ["Steadfast Prayer"] = "坚定祈祷",
                ["Chalice Wisdom"] = "圣杯智慧",
                ["Bloodied Wings"] = "染血之翼",
                ["Drink of Life"] = "汲取生命",
                ["Last Prayer"] = "最后的祈祷",
                ["Chapter of Flame"] = "烈焰之章",
                ["Speed Reading"] = "速读",
                ["Thick Binding"] = "厚重装帧",
                ["Next Chapter"] = "续章",
                ["Four Tales"] = "四篇故事",
                ["Layer each book's element to trigger it."] = "叠加每本书的属性来触发效果。",
                ["Turning Pages"] = "翻页之手",
                ["Chapter of Thunder"] = "雷鸣之章",
                ["Adds lightning to thin out packs."] = "为攻击附加闪电，以削弱成群的敌人。",
                ["Erudition"] = "博学",
                ["Chapters of Light and Dark"] = "光暗之章",
                ["Page Ward"] = "书页守护",
                ["Cover Guard"] = "封面守护",
                ["Page of Ice"] = "寒冰之页",
                ["Page of Shadow"] = "暗影之页",

                // 図鑑の固有効果名：既存の訳を再利用（#962）
                ["Momentum"] = "连击",
                ["Retaliation"] = "反击",
                ["Bulwark"] = "铁壁之环",
                ["Lifesteal"] = "汲血",
                ["Thorns"] = "荆棘",
                ["Executioner"] = "处刑",
                ["Resonance"] = "共鸣",
                ["Tailwind"] = "顺风",
                ["Barrier"] = "守护之灯",
                ["Second Wind"] = "守灯人",
                ["Blaze"] = "烈焰",
                ["Chain Lightning"] = "闪电链",
                ["Shatter"] = "爆碎",
                ["Aegis"] = "守护灵",
                ["Bloodlust"] = "嗜血",
                ["Ember"] = "火种",
                ["Frost"] = "霜寒",
                ["Radiance"] = "辉光",
                ["Umbra"] = "暗影",
                ["Convergence"] = "四元共鸣",
                ["Echoing Dodge"] = "闪避残响",
                ["Ultimate Surge"] = "终局高昂",
                ["Soul Siphon"] = "汲魂",
                ["Whirlwind"] = "旋风",
                ["Melee Frenzy"] = "近战狂乱",
                ["Opening Strike"] = "先制打击",
                ["Star Shield"] = "星辰庇护",
                ["Sprint"] = "疾驰",
                ["Vigor"] = "万全",
                ["Overload"] = "过载",
                ["Finale"] = "终曲",
                ["Critical Echo"] = "暴击余韵",
                ["Fetters"] = "足枷",
                ["Crystal Resonance"] = "结晶共鸣",
                ["Hunter's Pride"] = "猎人的骄傲",
                ["Overflowing Life"] = "满溢的生命",
                ["Wildfire"] = "飞火",
                ["Still Water"] = "止水",
                ["Spender's Ward"] = "散财之护",
                ["Perfect Read"] = "看破",
                ["Lucid Boon"] = "清明",
                ["Steam"] = "蒸汽",
                ["Eclipse"] = "蚀",
                ["Cinder"] = "燃烬",
                ["Frost Crystal"] = "冰晶",
                ["Shieldbreak Burst"] = "破盾冲击",
                ["Shared Ward"] = "共享之护",
                ["Shield Bash"] = "盾击",
                ["Gleaming Ward"] = "护盾光辉",
                ["Co-star"] = "共演",
                ["Triumph Song"] = "凯歌",
                ["Watchful Hand"] = "守望之手",
                ["Kindness Returns"] = "善意的回礼",
                ["Wanderer's Edge"] = "候鸟之刃",
                ["Focus Fire"] = "火力集中",
                ["Breakout"] = "突围",
                ["Duelist's Way"] = "决斗者的流派",
                ["Immovable Stance"] = "不动架势",
                ["Run-Up"] = "助跑",
                ["Strafe Shot"] = "走射",
                ["Relay Hand"] = "接力之手",
                ["Stardust Cycle"] = "星屑循环",
                ["Umbral Heritage"] = "暗影继承",
                ["Brittle Ice"] = "脆冰",
                ["Elemental Harvest"] = "元素收获",
                ["Prism Shift"] = "七色切换",
                ["Pack Feast"] = "群狼之粮",
                ["Vanguard's Oath"] = "前卫之誓",
                ["Death Bloom"] = "死之花",
                ["Medley"] = "连奏",
                ["Spellsweep"] = "咏唱横扫",
                ["Bare-Handed Pride"] = "空手的矜持",
                ["Opening Salvo"] = "开战火",
                ["Pile-On"] = "乘胜追击",
                ["Ace in Hand"] = "手握王牌",
                ["Crystal Circuit"] = "结晶循环",
                ["Shard Boon"] = "碎片的恩惠",
                ["Lifeline"] = "接力生命",
                ["Dream Omen"] = "梦的前兆",
                ["Rearguard's Way"] = "后卫的流派",
                ["Toll of Grudge"] = "怨叹之钟",
                ["Unbowed Mind"] = "不屈之心",
                ["Piling Luck"] = "积攒的运气",
                ["Weak-Point Wound"] = "要害创伤",
                ["Crit Splash"] = "暴击飞溅",
                ["Returning Blade"] = "回返之刃",
                ["Ready Guard"] = "常备之盾",
                ["Apothecary"] = "药师妙手",
                ["Spillover Strike"] = "满溢一击",
                ["Circulating Wealth"] = "循环之富",
                ["Golden Nose"] = "黄金嗅觉",
                ["Dust Gathering"] = "梦尘收集",
                ["Dust Basket"] = "梦尘之篮",

                // 旅人名（#962）
                ["Vesper"] = "维斯珀",
                ["Lacerta"] = "拉塞尔塔",
                ["Cetus"] = "塞图斯",
                ["Yubar"] = "尤巴尔",
                ["Husk"] = "空壳",
                ["Mist"] = "米斯特",
                ["Nachia"] = "娜琪亚",
                ["Aurena"] = "奥蕾娜",
                ["Bismuth"] = "比斯穆特",

                [$"After a dodge, dash or teleport, your next basic attack within 3s deals +{MemoryDamageBalance.Effect_legacy_h_husk_key2_power_perRank}% of the higher of AD or AP (does not stack; each movement refreshes it). Pair it with One Step, One Kill's guaranteed critical hit and Scar of the Wind to turn movement into a decisive strike."] = $"闪避、冲刺或传送后3秒内，下一次通常攻击额外造成攻击力与法术强度中较高者{MemoryDamageBalance.Effect_legacy_h_husk_key2_power_perRank}%的伤害（不叠加，每次移动刷新时限）。可与一步一杀的必定暴击及风之伤配合，将移动衔接为决定性的一击。",

                // 取引・図鑑・星団・敵の担当表示で繰り返し出る語
                ["Cancel"] = "取消",
                ["Dream Dust"] = "梦尘",
                ["Memory cluster"] = "记忆星团",
                ["Bridge cluster"] = "记忆之桥星团",
                ["Outer cluster"] = "外缘星团",
                ["Enemy ownership"] = "敌人归属制",
                ["Dynamic difficulty"] = "动态难度",
                ["Dynamic difficulty: "] = "动态难度：",
                ["Enemy HP "] = "敌人生命 ",
                ["　Enemy HP "] = "　敌人生命 ",
                [" · Attack "] = " · 攻击 ",
                [" · Rewards "] = " · 奖励 ",
                ["standard"] = "标准",
                [" (capped)"] = "（上限）",
                ["  <- this one"] = "  ←此装备",
                ["That pact is not on offer."] = "该契约不在可选范围内。",
                ["Salvage Dream Dust from relics has reached this world's cap (it recovers in the next world)."] = "本世界中分解获得的梦尘已达上限（下一个世界会恢复）。",
                ["The trade was not made because its record could not be saved. Check the save location and try again."] = "因无法保存交易记录，未执行交易。请确认保存位置后重试。",
            };
        }
    }
}
