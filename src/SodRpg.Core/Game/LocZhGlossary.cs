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
        // Co-Star / Co-star: 共演 → 联袂（固有効果名も同じ）
        // Steam / White Mist: 湯気・白い霧 → 白雾（銘品名）。属性反応の蒸気は Content.PowerName / ElementReactions で 蒸汽。
        // Guardian: 守護霊 → 守护灵
        // Heirloom / Heir: 跡目・相続 → 继承
        // Boon: 恵み → 恩泽（複合名では「恩」）
        // Ember: 残り火・燠火 → 余烬（装備名）。固有効果の火種は Content.PowerName で 火种。
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
        // 星図・遠征で繰り返し使う訳語（#960）。複合語を訳すときの参照用。
        // Keystone: 刻印 → 刻印、Cluster: 星団 → 星团、Bridge: 記憶の橋 → 记忆之桥
        // Rank: 段 → 阶、Loadout: 構成 → 配置、Training: 鍛錬 → 锻炼
        // Nightmare: 悪夢 → 梦魇、Mastery: 熟練度 → 熟练度、Identity memory: 固有記憶 → 身份记忆
        // Shield: 障壁 → 护盾、Ability Power: 魔力 → 法术强度、Crit: 会心 → 暴击
        // Keen: 冴え → 精妙、Dream Ring: 夢輪 → 梦环、Rime: 薄氷 → 霜华
        // このうち #993 で完全一致辞書の #960 欄から外した語は Keystone・Cluster・Bridge・Rank・Loadout・Training・
        // Mastery・Shield・Crit・Keen・Rime の 11 語（Lookup に渡る呼び出しがなく、参照用にのみ残す）。
        // Nightmare・Identity memory・Ability Power・Dream Ring は下の辞書に登録したまま残す
        // （Nightmare の登録は #962 欄。星団名の " Cluster" 接尾辞は英語名だけの組み立てで、
        //   中国語は suffix: "星团" を直接足す）。
        // 装備データ 7/8（Content.cs の固有品、#957）で繰り返し使う訳語。同じく参照用で辞書には登録しない。
        // Pack: 群れ → 兽群
        // Ward / 護り: 守护（障壁は #961 に合わせて护盾）
        // Vanguard / 前衛: 前卫
        // Thorns: 棘 → 荆棘（Power と同じ）
        // Pilgrim / 巡礼: 朝圣
        // Strafing / 歩き撃ち: 移动射击
        // Ultimate / 奥義: 奥义
        // Sunpin / 陽飾り: 阳针
        // 部位の訳し分け: 手袋→手套、拳→拳甲、籠手・手甲→手甲、腕当て→护腕、手巻き→缠手、指→指套、
        // 指輪・指環→戒指・指环、長靴→长靴、鉄靴→铁靴、脛当て・鉄脛→胫甲、草履→草履、下駄→木屐、
        // 上履き→便鞋、鋲靴→钉鞋、歩み靴・追い足→疾行靴、ロケット→坠盒、首飾り→项链、飾り→坠饰
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
        // Ward: 障壁 → 护盾、護り → 守护（説明文の障壁も #961 の効果用語にそろえる）
        // Bracers: 腕輪 → 护腕
        // Locket: ロケット → 坠饰（複合名では「坠」）
        // Greaves / Treads: 足甲・脚絆 → 胫甲
        // #961 で追加した主な用語（ボス・賞金首・部屋の予兆・実績・禁忌・エッセンス・家系）:
        // 攻撃力・魔力の高い方のX% → 攻击力与法术强度中较高一方的X%
        // 再発動までX秒かかる → 冷却X秒 / 予告 → 预警 / 障壁 → 护盾
        // 通常攻撃 → 通常攻击 / セット効果 → 套装效果（2セット効果 → 2件套效果）
        // 悪夢の契約 → 梦魇契约 / 悪夢化 → 梦魇化 / 夢の出来事 → 梦境事件 / 夢の変種 → 梦境变种
        // 部屋の兆し → 房间预兆 / 潜行 → 深度 / 経験値 → 经验值 / 家系 → 元素族（Frost=冰霜・Dark=暗）
        // 樹芽 → 树芽 / 踏みつけ → 踩踏 / 樹界 → 树界 / 樹界装備 → 树界装备（Demon 系）
        // 霞衣 → 霞衣・群れの守り → 群守・明滅 → 明灭・最後の殻 → 最后之壳（Nightmare の性質）
        // 冷気（Cold）→ 冰（冰属性伤害・冰属性治疗）/ 通常記憶 → 普通记忆 / 奥義記憶 → 奥义记忆
        // 相（Phase）→ 相态 / 相紋 → 相纹 / 分身 → 分身 / 氷印 → 冰印 / 星印 → 星印
        // 白蓮 → 白莲 / 黒月 → 黑月 / 魂の牢獄 → 灵魂牢狱 / 雪嶺の核 → 雪岭之核
        // 固有報酬の段階 → 首领专属奖励阶段 / 超過回復 → 溢出治疗 / 残像砲台 → 残影炮台
        // 出来事・連携・変種の説明で繰り返し使う訳語（#959）。
        // Power: 固有効果 → 固有效果、Unique relic: 固有品 → 固有遗物（レア度とは区別）
        // Milestone: 節目 → 里程碑、Keystone: 到達刻印 → 到达刻印
        // Barrier: 障壁 → 护盾、Guard reduction: 軽減 → 减伤
        // Weak to fire / cold / light / dark: 火・冷気・光・闇に弱い → 畏火・畏寒・畏光・畏暗
        // Unsecured: 未確保 → 未存入、まだ持ち帰っていない → 尚未带回
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
        // 外套: Cloak / Drake Hide → 斗篷、Mantle → 披风、纏い: Shroud → 披风（#953 の固有品の既訳）。
        // armor.flowing_cloak / armor.star_cloak は未訳のため、固有品の使い分けを参照する。
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
        // Ultimate: 奥義・大技・切り札（技） → 奥义（#953・#954 共通）
        // Overflowing healing: 溢れた癒し → 溢出的治疗
        // Blizzard: 吹雪・雪嵐 → 暴风雪 / Pure White: 純白 → 纯白 / Gold Coin: 金貨 → 金币
        // Doubt・Suspicion: 疑心・疑い → 疑心 / Cage: 檻 → 牢笼 / Furnace: 炉・溶鉱炉 → 熔炉
        // Tidecaller's（セット名）: 潮鳴り → 潮鸣 / Prism（光学の語）: プリズム → 棱镜
        // Dodge: 回避 → 闪避 / Crimson（紅蓮の名）: 紅蓮 → 红莲 / Banquet: 祝宴・饗宴 → 盛宴
        // #953 で追加した主な用語（Content.cs 1466〜2066 の固有品で2回以上使う訳語）:
        // Star shield: 星の盾 → 星辰之盾
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

                // 星図で共有する複合語と名前（#960）。汎用の単語は上の参照表だけに残す。
                ["Identity memory"] = "身份记忆",
                ["Ability Power"] = "法术强度",
                ["Dream Ring"] = "梦环",
                ["Dream Ring Strength"] = "梦环之力",
                ["Dream Ring Wisdom"] = "梦环之智",
                ["Dream Ring Vessel"] = "梦环之器",
                ["Dream Ring Armor"] = "梦环之甲",
                ["Dream Ring Life"] = "梦环生命",
                ["Dream Ring Rhythm"] = "梦环律动",
                ["Dream Ring Resolve"] = "梦环不屈",
                ["Dream Ring Renewal"] = "梦环再生",

                // 記憶別ルートの星名（#960）。
                ["Unyielding Oath"] = "不退之誓",
                ["Intimidating Strike"] = "威慑一击",
                ["Keen Repelling Blow"] = "精妙击退",
                ["Barrier of Resolve"] = "决意屏障",
                ["Unshaken Ground"] = "稳固立足",
                ["Hammer of Resolve"] = "决意之锤",
                ["Unmoved Vow"] = "不动之誓",
                ["Keen Fourth-Shot Light"] = "精妙四击圣光",
                ["Sign of Light"] = "光明预兆",
                ["Merciful Flurry"] = "慈悲连击",
                ["Lingering Mercy"] = "慈悲余光",
                ["Keen Stacking Flurry"] = "精妙叠加连击",
                ["Heart of Holy Light"] = "圣光之心",
                ["Shared Mercy"] = "共享慈悲",
                ["Shielded Run-Up"] = "持盾助跑",
                ["Impact Opening"] = "冲撞破绽",
                ["Charging Breath"] = "冲锋呼吸",
                ["Impact Sparks"] = "冲撞火花",
                ["Keen Ramming Blow"] = "精妙冲撞",
                ["Heavy Bulwark"] = "重装壁垒",
                ["Shield Bash Stance"] = "盾击架势",
                ["Solar Heat Reserve"] = "日轮蓄热",
                ["Solar Ember"] = "日轮余烬",
                ["Keen Solar Smash"] = "精妙日轮重击",
                ["Return of the Sun"] = "太阳回归",
                ["Searing Judgment"] = "灼热审判",
                ["Sun-Bearing Body"] = "承日之躯",
                ["Solar Aftermath"] = "日轮余波",
                ["Rhythmic Renewal"] = "律动回气",
                ["Light of Order"] = "秩序之光",
                ["Resolute Advance"] = "决意前进",
                ["Disciplined Double"] = "律动双击",
                ["Keen Advancing Smash"] = "精妙前进重击",
                ["Breath of Blows"] = "连击之息",
                ["Judgment Recharge"] = "审判再冲锋",
                ["Heat Haze Reserve"] = "热浪余力",
                ["Veil of Baptism"] = "洗礼薄纱",
                ["Challenger's Armor"] = "挑战者护甲",
                ["Heat Haze Ember"] = "热浪余烬",
                ["Baptism Cycle"] = "洗礼巡回",
                ["Blast Core Power"] = "爆心魔力",
                ["Fervor of Baptism"] = "洗礼狂热",
                ["Keen Holy Burst"] = "精妙圣域爆光",
                ["Exposed by Light"] = "光照暴露",
                ["Keen Descending Blessing"] = "精妙降临祝福",
                ["Sanctuary Rain"] = "圣域恩雨",
                ["Sanctuary Gate"] = "圣域之门",
                ["Light Enfolding the Sanctum"] = "笼罩圣域之光",
                ["Sanctuary Link"] = "圣域连携",
                ["Keen Double Shot"] = "精妙双射",
                ["Twin-Shot Echo"] = "双射回响",
                ["Keen Twin Trajectories"] = "精妙双重弹道",
                ["Fire Trajectory"] = "火焰弹道",
                ["Keen Follow-Up Shot"] = "精妙追射",
                ["Recoil Brace"] = "反冲支撑",
                ["Rapid Link"] = "速射连携",
                ["Keen Ignition"] = "精妙点火",
                ["Powder Spark"] = "火药火种",
                ["Keen Fourth-Shot Blast"] = "精妙第四射爆破",
                ["Powder Keg Sympathy"] = "火药桶殉爆",
                ["Keen Spreading Powder"] = "精妙扩散火药",
                ["Burning Intensity"] = "燃烧浓度",
                ["Leaving the Firing Line"] = "脱离射线",
                ["Countershot Arm"] = "反击射手",
                ["Dodge Reset"] = "闪避重整",
                ["Leaping Finisher"] = "跃射终结",
                ["Evasive Stance"] = "闪避架势",
                ["Light Ballistic Vest"] = "轻便防弹衣",
                ["Silken Dodge"] = "流畅闪避",
                ["Muzzle Heat"] = "炮口热量",
                ["Point-Blank Weakness"] = "近身弱点",
                ["Keen Cannon Roar"] = "精妙炮鸣",
                ["Quick Reload"] = "快速装填",
                ["Barrel Cooling"] = "炮管冷却",
                ["Flame Cannon Core"] = "火焰炮核心",
                ["Double Powder"] = "双重火药",
                ["Incendiary Handling"] = "燃烧弹操作",
                ["Reload Composure"] = "装填余裕",
                ["Firing Line Heat"] = "火线余热",
                ["Incendiary Detonation"] = "燃烧弹引爆",
                ["Incendiary Follow-up"] = "燃烧弹追火",
                ["Incendiary Concentrate"] = "燃烧弹浓缩",
                ["Ammo Resupply"] = "弹药补给",
                ["Gathered Aim"] = "瞄准积累",
                ["Armor-Piercing Wound"] = "穿甲创伤",
                ["Keen Piercing Core"] = "精妙贯穿弹芯",
                ["Aiming Echo"] = "瞄准回响",
                ["Sight Recovery"] = "准星复位",
                ["Steadied Shot"] = "凝神一射",
                ["Sure-Hit Reload"] = "必中再装填",
                ["Triple-Shot Breath"] = "三射呼吸",
                ["Ricochet"] = "跳弹",
                ["Quickdraw Momentum"] = "拔枪之势",
                ["Heated Rounds"] = "灼热弹丸",
                ["Keen Triple Pierce"] = "精妙三连贯穿",
                ["Shooting Endurance"] = "射击耐力",
                ["Triple-Shot Reload"] = "三射再装填",
                ["Keen Coldblood Burst"] = "精妙冷血爆破",
                ["Cold-Blood Sign"] = "冷血预兆",
                ["Ice Carapace"] = "寒冰甲壳",
                ["Cold-Blooded Rime"] = "冷血霜华",
                ["Keen Five-Stack Frostburst"] = "精妙五层霜爆",
                ["Frozen Pulse"] = "冰冻脉动",
                ["Cold Brittleness"] = "冷血脆化",
                ["Keen Stored Charge"] = "精妙蓄电",
                ["Charged Spark"] = "带电火花",
                ["Charged Scales"] = "带电鳞片",
                ["Thunder Reverb"] = "雷鸣余韵",
                ["Keen Discharge"] = "精妙放电",
                ["Charged Vessel"] = "蓄电之器",
                ["Lightning Leap"] = "电光飞跃",
                ["Frost Run Readiness"] = "冰驰戒备",
                ["Frost Footprint"] = "霜之足迹",
                ["Frost Run Renewal"] = "冰驰回气",
                ["Frost-Run Rime"] = "冰驰薄霜",
                ["Keen Frost Ram"] = "精妙冰霜冲撞",
                ["Iceberg Mass"] = "冰山质量",
                ["Icy Wake"] = "寒冰尾迹",
                ["Chill Circulation"] = "寒气循环",
                ["Frozen Ground Ward"] = "冻土守护",
                ["Layered Thin Ice"] = "层叠薄冰",
                ["Freezing Domain"] = "冰冻领域",
                ["Ice Field Reserve"] = "冰域余力",
                ["Ice Field Depth"] = "冰域厚度",
                ["Chill's Mercy"] = "寒气恩泽",
                ["Glacier Accumulation"] = "冰川积累",
                ["Glacier Seed"] = "冰川种子",
                ["Keen Collapsing Ice"] = "精妙崩冰",
                ["Glacier Film"] = "冰川覆膜",
                ["Glacier Renewal"] = "冰川再生",
                ["Depth of the Cold Wave"] = "寒潮深度",
                ["Glacier Shatter"] = "冰川碎裂",
                ["Ice Shield Readiness"] = "冰盾戒备",
                ["Pressure to Back Off"] = "逼退之压",
                ["Sweeping Aftershock"] = "横扫余震",
                ["Reinforced Ice Shield"] = "冰盾强化",
                ["Ice Shield Redeployment"] = "冰盾再展开",
                ["Ice Enfolding Allies"] = "笼罩队友的寒冰",
                ["Refreshing Follow-Up"] = "畅快追击",
                ["Keen Melee Barrage"] = "精妙近战乱打",
                ["Freezing Fist"] = "冰冻之拳",
                ["Melee Ice Shell"] = "近战冰壳",
                ["Double Fist"] = "双重拳击",
                ["Fist Reset"] = "拳击重整",
                ["Frozen Fist Shield"] = "冻拳冰盾",
                ["Courtesy Frost"] = "礼仪霜膜",
                ["Keen Starshot"] = "精妙星弹",
                ["Star Marker"] = "星之标记",
                ["Keen Ricocheting Stardust"] = "精妙跳跃星尘",
                ["Rebounding Stardust"] = "弹跳星尘",
                ["Keen Orbiting Starlight"] = "精妙环绕星芒",
                ["Starshot Relay"] = "星弹接力",
                ["Star Chain"] = "群星相连",
                ["Keen Exotic Matter"] = "精妙异质物质",
                ["Unknown Glow"] = "未知辉光",
                ["Keen Condensed Mass"] = "精妙凝缩团块",
                ["Unknown Weight"] = "未知重量",
                ["Keen Fourfold Lightburst"] = "精妙四重光爆",
                ["Exotic Light"] = "异质之光",
                ["Matter Reflux"] = "物质回流",
                ["Blink Readiness"] = "闪现戒备",
                ["Blink Star Core"] = "闪现星核",
                ["Lightpath Reconnection"] = "光路重连",
                ["Blinking Starlight"] = "闪现星光",
                ["Blinding Blink"] = "致盲闪现",
                ["Twilight Vessel"] = "薄暮之器",
                ["Double Starshadow"] = "双重星影",
                ["Returning Light"] = "往返之光",
                ["Scent of Light"] = "光之余香",
                ["Radiant Blast Echo"] = "爆光回响",
                ["Returning Afterglow"] = "往返余光",
                ["Keen Round-Trip Lightwave"] = "精妙往返光波",
                ["Light Beam Density"] = "光束密度",
                ["Recall of Light"] = "唤回之光",
                ["Star Core Accumulation"] = "星核积累",
                ["Star Core Shell"] = "星核外壳",
                ["Keen Stellar Detonation"] = "精妙星核爆炸",
                ["Supernova Aftershock"] = "超新星余震",
                ["Supernova Surge"] = "超新星激涌",
                ["Starlight Compression"] = "星光压缩",
                ["Starry Reverb"] = "星辰回响",
                ["Keen Meteor Track"] = "精妙陨石轨迹",
                ["Sign of the Fireball"] = "火球预兆",
                ["Keen Falling Judgment"] = "精妙降临天罚",
                ["Meteor Sparks"] = "陨石火花",
                ["Meteor Afterheat"] = "陨石余热",
                ["Celestial Vessel"] = "天体之器",
                ["Chain of Calamity"] = "天灾连锁",
                ["Quiet Cycle"] = "静寂巡回",
                ["Veil of Calm"] = "静寂薄纱",
                ["Quiet Halo"] = "静寂光环",
                ["Breath of Calm"] = "静寂呼吸",
                ["Quiet Shell"] = "静寂外壳",
                ["Spark After Calm"] = "静寂后的灵光",
                ["Keen Single Strike"] = "精妙一击",
                ["Taint of Intent"] = "杀意浸染",
                ["Keen Swiftsteel"] = "精妙疾刃",
                ["Afterimage of the Flow"] = "一步一杀残影",
                ["Keen Striding Flash"] = "精妙步进闪斩",
                ["One-Step Strength"] = "一步之力",
                ["Spark of Killing Intent"] = "杀意飞火",
                ["Keen Wind Blade"] = "精妙风刃",
                ["Dark of the Windscar"] = "风痕之暗",
                ["Keen Slashing Wind"] = "精妙撕裂之风",
                ["Windscar Mending"] = "风痕愈合",
                ["Keen Tailwind Flash"] = "精妙顺风闪斩",
                ["Deep Wind Scar"] = "深邃风痕",
                ["Wind's Wound"] = "风之伤痕",
                ["Flash Step Readiness"] = "瞬步戒备",
                ["Gap-Cutting Arm"] = "斩断间距之臂",
                ["Shadowpath Reconnection"] = "影路重连",
                ["Flash-Step Opener"] = "瞬步初击",
                ["Blinding Afterimage"] = "致盲残影",
                ["Shadow Runner's Core"] = "影行躯干",
                ["Pinnacle of Shadow Running"] = "影行极致",
                ["Two-Color Cycle"] = "双色巡回",
                ["Shadow of the Slash"] = "裂伤之暗",
                ["Red Blade Momentum"] = "赤刃余势",
                ["Wound Cycle"] = "伤痕巡回",
                ["Keen Red-Blue Slashes"] = "精妙赤蓝斩击",
                ["Darkness in the Rift"] = "裂隙之暗",
                ["Cut Twice"] = "二度撕裂",
                ["Returning Wedge"] = "楔子再临",
                ["Shadow of the Mark"] = "印记之暗",
                ["Mark Aftershock"] = "印记余震",
                ["Aching Mark"] = "印记隐痛",
                ["Keen Cursed Wedge"] = "精妙咒楔",
                ["Engraved Darkness"] = "铭刻黑暗",
                ["Mark Reclaimed"] = "印记回收",
                ["Keen Sword Wave"] = "精妙剑气",
                ["Annihilation Readiness"] = "灭杀戒备",
                ["Awakening Momentum"] = "觉醒余势",
                ["Sword Aura Aftermath"] = "剑气余波",
                ["Keen Ruinous Wave"] = "精妙毁灭波",
                ["Wave-Releasing Arm"] = "放波之臂",
                ["Aura Lifesteal"] = "剑气吸命",
                ["Concealment Cycle"] = "隐身巡回",
                ["Shadow of Stealth"] = "潜行之暗",
                ["Ambush Aftershock"] = "奇袭余震",
                ["Reveal Ripple"] = "现身涟漪",
                ["Keen Shadow-Rending Blast"] = "精妙裂影爆破",
                ["Lurking Dark Intensity"] = "潜伏黑暗浓度",
                ["Creeping Shadow"] = "暗影侵蚀",
                ["Keen Opening Thrust"] = "精妙初刺",
                ["Opening Echo"] = "初击回响",
                ["Three-Strike Guard"] = "三击守护",
                ["Reading the Opening"] = "识破架势",
                ["Keen Engaging Flurry"] = "精妙起手连击",
                ["Shield-Bearing Vessel"] = "承盾之器",
                ["Stance Link"] = "架势连携",
                ["Keen Mark Piercing"] = "精妙穿靶",
                ["Spoils of the Mark"] = "标记战利品",
                ["Keen Duelist's Blade"] = "精妙决斗之刃",
                ["Driving Blade"] = "驱赶之刃",
                ["Keen Stacking Thrusts"] = "精妙叠加刺击",
                ["Relentless Marking Blade"] = "连绵刻痕之剑",
                ["Marked Chain Blast"] = "标记连锁爆炸",
                ["Footwork Readiness"] = "步法戒备",
                ["Counterblade Arm"] = "反击刃手",
                ["Resetting Breath"] = "重整呼吸",
                ["Footstep Ward"] = "足迹守护",
                ["Befuddling Footwork"] = "扰乱步法",
                ["Sword Dance Core"] = "剑舞躯干",
                ["Windlike Strides"] = "如风步伐",
                ["Thrusting Blade Cycle"] = "刺剑巡回",
                ["Flash of Light"] = "光明一闪",
                ["Thrusting Momentum"] = "刺击余势",
                ["Double Thrust"] = "双重刺击",
                ["Keen Lunging Thrust"] = "精妙突进刺击",
                ["Wound-Mending Body"] = "愈伤之躯",
                ["Breakthrough Charge"] = "突破再冲锋",
                ["Advancing Cycle"] = "前进巡回",
                ["Advancing Guard"] = "前进守护",
                ["Thrust Echo"] = "刺击回响",
                ["Thrust Impact"] = "刺击冲击",
                ["Keen Deep Stab"] = "精妙深刺",
                ["Thrust Support"] = "刺剑支撑",
                ["Mist-Rain Follow-Thrusts"] = "雾雨追刺",
                ["Parrying Distance"] = "招架距离",
                ["Reflected Pain"] = "反弹之痛",
                ["Riposte Echo"] = "反击回响",
                ["Thunder Echo"] = "雷鸣回响",
                ["Reforming Stance"] = "重整架势",
                ["Parrying Core"] = "招架躯干",
                ["Parry Guard"] = "招架守护",
                ["Keen Awakened Lightning"] = "精妙觉醒雷光",
                ["Guard of Awakening"] = "觉醒守护",
                ["Resolve Afterglow"] = "意志余光",
                ["Lightning Reverberation"] = "电击余韵",
                ["Keen Rising Blade"] = "精妙起身之刃",
                ["Renewing Body"] = "回气之躯",
                ["Awakened Heartbeat"] = "觉醒心跳",
                ["Keen Unity Lightburst"] = "精妙团结光爆",
                ["Light of Unity"] = "团结之光",
                ["Keen Heartbeat Blast"] = "精妙心跳爆破",
                ["Unity Afterburst"] = "团结追爆",
                ["Keen Stacking Unity"] = "精妙叠加团结",
                ["Healing of Unity"] = "团结治疗",
                ["Unity Echo"] = "团结回响",
                ["Life Cycle Guard"] = "生命巡回守护",
                ["Speed Shared with the Pack"] = "共享群体速度",
                ["Circling Life Ward"] = "生命环绕屏障",
                ["Armor Shared with the Pack"] = "共享群体护甲",
                ["Shared Life Readiness"] = "共生戒备",
                ["Ever-Circling Grace"] = "循环不息的恩泽",
                ["Unbroken Lifeline"] = "不息生命线",
                ["Waltz Guard"] = "圆舞守护",
                ["Dancing Vessel"] = "舞动之器",
                ["Returning Dance Breath"] = "归舞呼吸",
                ["Waltz Ward"] = "华尔兹守护",
                ["Waltz Ripples"] = "舞之涟漪",
                ["Veil Enfolding the Pack"] = "笼罩群体的薄纱",
                ["Pack-Enfolding Waltz"] = "笼罩群体的圆舞",
                ["Calling Cycle"] = "呼唤巡回",
                ["Forest Veil"] = "森林薄纱",
                ["Pack Momentum"] = "群体余势",
                ["Forest Mending"] = "森林愈合",
                ["Keen Leaping Pack"] = "精妙飞扑群体",
                ["Fangs of New Leaves"] = "嫩叶之牙",
                ["Hound Resupply"] = "猎犬补给",
                ["Keen Moonlit Leap"] = "精妙月下飞跃",
                ["Claw Mark"] = "爪痕",
                ["Keen Moonbeast Claws"] = "精妙月兽爪痕",
                ["Moonlit Mending"] = "月光愈合",
                ["Keen Moonlight Slash"] = "精妙月光斩击",
                ["Moonbeast Fangs"] = "月兽之牙",
                ["Moonlit Re-Leap"] = "月下再跃",
                ["Command Cycle"] = "指挥巡回",
                ["Command Veil"] = "号令薄纱",
                ["Command Ward"] = "号令守护",
                ["Commander's Elation"] = "指挥高昂",
                ["Commander's Guard"] = "指挥者守护",
                ["Might of the Pack"] = "群体猛威",
                ["Rallying Cry"] = "号令鼓舞",
                ["Serpent Scale Guard"] = "蛇鳞守护",
                ["Serpent Reverb"] = "蛇之余韵",
                ["Blessing Echo"] = "祝福回响",
                ["Serpent's Second Bite"] = "蛇之追咬",
                ["Keen Serpent Follow-Up"] = "精妙蛇之追击",
                ["Serpent Ward"] = "蛇之守护",
                ["Serpent Scale Ward"] = "蛇鳞庇护",
                ["Keen Dismantling Claw"] = "精妙分解爪",
                ["Dismantling Light"] = "分解之光",
                ["Keen Unraveling Blow"] = "精妙解构一击",
                ["Double Claw"] = "双重爪击",
                ["Keen Golden Clawmarks"] = "精妙金色爪痕",
                ["Wound-Mending Claw"] = "愈伤之爪",
                ["Dismantling Brittleness"] = "分解脆化",
                ["Keen Golden Plume"] = "精妙金羽",
                ["Golden Feather Veil"] = "金羽薄纱",
                ["Keen Feather Strike"] = "精妙羽击",
                ["Golden Double"] = "金羽双射",
                ["Keen Feather Storm"] = "精妙羽毛风暴",
                ["Life-Restoring Feather"] = "返命之羽",
                ["Golden Feather Radiance"] = "金羽辉光",
                ["Wingbeat Readiness"] = "振翅戒备",
                ["Flight Power"] = "飞翔魔力",
                ["Returning Feather Dance"] = "羽毛归舞",
                ["Descending Light"] = "降临之光",
                ["Fluttering Confusion"] = "振翅扰乱",
                ["Feather Haste"] = "羽毛加速",
                ["Golden Windcut"] = "黄金切风",
                ["Keen Golden Blast"] = "精妙金光爆破",
                ["Golden Film"] = "黄金覆膜",
                ["Golden Blast Reserve"] = "金光余力",
                ["Corroded Weakness"] = "腐蚀脆化",
                ["Keen Corroded Shine"] = "精妙腐蚀辉光",
                ["Gentle Devotion"] = "温柔献身",
                ["Golden Reverberation"] = "金光余波",
                ["Golden Shard Cycle"] = "金片巡回",
                ["Shard Light"] = "金片之光",
                ["Return Flow Reserve"] = "回流余力",
                ["Shard Veil"] = "金片薄纱",
                ["Keen Shard Trajectory"] = "精妙金片弹道",
                ["Life Spared in the Shards"] = "碎片留命",
                ["Shard Burst"] = "金片破裂",
                ["Keen Perilous Strike"] = "精妙危险一击",
                ["Flash of Theory"] = "理论灵光",
                ["Proof Aftershock"] = "论证余震",
                ["Dangerous Side Effect"] = "危险副作用",
                ["Reappraisal Breath"] = "再验证呼吸",
                ["Life-Restoring Wisdom"] = "返命之智",
                ["Dangerous Conclusion"] = "危险结论",
                ["Keen Chain-Ring Blast"] = "精妙连锁环爆破",
                ["Dismantle Light"] = "分解之光",
                ["Chain Afterglow"] = "连锁余光",
                ["Dissolving Weakness"] = "溶解弱化",
                ["Keen Circling Dissolution"] = "精妙环绕分解光",
                ["Life Circling the Array"] = "环阵之命",
                ["Accelerated Chain"] = "连锁加速",
                ["Keen Page Blade"] = "精妙书页之刃",
                ["Reading the Light"] = "循光阅读",
                ["Keen Flipping-Page Strike"] = "精妙翻页打击",
                ["Page Reverb"] = "书页余韵",
                ["Keen Prismatic Follow-Up"] = "精妙棱彩追击",
                ["Page-Turning Tempo"] = "翻页节拍",
                ["Reading Link"] = "读毕连携",
                ["Bookshelf Retreat"] = "书架退避",
                ["Racing Page Wisdom"] = "疾行书页之智",
                ["Pagepath Reconnection"] = "页路重连",
                ["Warped Frost"] = "扭曲寒气",
                ["Sprinting Confusion"] = "疾驰扰乱",
                ["Traveling Binding"] = "旅行装帧",
                ["Page Gale"] = "书页疾风",
                ["Light Script Cycle"] = "光文巡回",
                ["Cold Margin"] = "冰冷留白",
                ["Soul Missile Afterglow"] = "灵弹余光",
                ["Light Annotation"] = "光文注释",
                ["Keen Soul-Missile Burst"] = "精妙灵弹爆破",
                ["Light Script Intensity"] = "光文浓度",
                ["Soul Echo"] = "灵魂回响",
                ["Fire Script Afterheat"] = "炎文余热",
                ["Script Ember"] = "炎文余烬",
                ["Keen Hellfire Ring"] = "精妙业火之环",
                ["Flying Sparks"] = "飞溅火花",
                ["Fire Script Cycle"] = "炎文巡回",
                ["Fire Script Intensity"] = "火文浓度",
                ["Scorching Page"] = "灼热书页",
                ["Sword Script Cycle"] = "剑文巡回",
                ["Sword's Guard"] = "剑之守护",
                ["Sword Flash Echo"] = "剑闪回响",
                ["Blade Wave"] = "魔剑斩击波",
                ["Keen Valiant Edge"] = "精妙勇剑",
                ["Heart-Warding Binding"] = "护心装帧",
                ["Courage's Tailwind"] = "勇气顺风",
                ["Dark Script Accumulation"] = "暗文积累",
                ["Dark Script Stain"] = "暗文浸染",
                ["Keen Mind Arrow"] = "精妙精神之箭",
                ["Mind Echo"] = "精神回响",
                ["Dark Script Cycle"] = "暗文巡回",
                ["Dark Script Intensity"] = "暗文浓度",
                ["Burst of Mind Arrows"] = "精神之箭爆裂",
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
                ["Pair it with charging Exotic Matter before your Ultimate."] = "适合先积蓄奇异物质，再使用奥义。",
                ["Stellar Vessel"] = "星辰之器",
                ["Stellar Ward"] = "星辰之盾",
                ["Shields you the moment you unleash your Ultimate."] = "释放奥义的瞬间为你提供护盾。",
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

                // 図鑑の固有効果名は Content.PowerName の明示訳を使う。
                // Barrier / Second Wind などの固有名訳を一般語へ適用しない。
                // 図鑑以外の2引数の翻訳が参照する既訳は残す。
                ["Wildfire"] = "飞火",
                ["Vigor"] = "万全",

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
                ["That pact is not on offer."] = "该契约不在可选之列。",
                ["Salvage Dream Dust from relics has reached this world's cap (it recovers in the next world)."] = "本世界中分解获得的梦尘已达上限（下一个世界会恢复）。",
                ["The trade was not made because its record could not be saved. Check the save location and try again."] = "因无法保存交易记录，未执行交易。请确认保存位置后重试。",
            };
        }
    }
}
