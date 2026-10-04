# Issue #48：ボス限定の全部位セットと任意連携

## 1. 概要・確定方針

設計のみ。14セット・84部位を追加し、既存48セット・既存ID・本体報酬を変更しない。以下の数値・名称は採用候補であり、実装済み／戦闘で較正済みの値ではない。

| 項目 | 方針 |
| --- | --- |
| 対象 | `BossMonster` 派生15型から空実装の `Mon_Despair_BossCrawler` を除く14型。白夜・暗月は別セット |
| 構成 | Weapon / Armor / Charm / Head / Hands / Feet 各1点。`SetDef` の2/3/6ピース効果を累積適用。4/5専用効果なし |
| 入手 | 対応ボス撃破ごと、各プレイヤーが独立抽選。当選時、そのセットからランダムな1部位のみ |
| 限定 | 他ボス・通常敵・汎用プール・宝箱・商人・製作等から新規取得不可 |
| 強さ | 同テーマの既存セットより2/3ピースの値を約25%増し、6ピース完成報酬を別枠で上乗せ |
| 任意連携 | 対応する本体の記憶／エッセンスを自分が装着している間だけ追加。セットにも本体報酬にも相互必須条件を付けない |
| 報酬なし | PrimusAeron / Polaris は本体報酬との連携なし。追加の任意要素も今回は採用しない |
| 同期 | ボス型名・抽選条件はホストの撃破factが正。抽選結果は個人別、戦闘効果はホストが適用 |

### 根拠と読み分け

- MOD調査：`~/dev/sod-prompts/i48-research.md`。本体15型・報酬・戦闘テーマ：`~/dev/sod-prompts/i48-bosses-src.md`（r1.4.0.13）。本体データは転記せず、以下は型名と短い要約のみ。
- 現行定義：[Content.cs](../../src/SodRpg.Core/Game/Content.cs)（`UniqueDef` / `SetDef` / `Sets` / 上限）、[Links.cs](../../src/SodRpg.Core/Game/Links.cs)、[Loot.cs](../../src/SodRpg.Core/Game/Loot.cs)、[KillClassificationLedger.cs](../../src/SodRpg.Core/Game/KillClassificationLedger.cs)。
- [付録A A6/A10](../appendix-a-content.md)：限定固有品・非必須性の方針を継承。ただし同付録の独自ボスを本体14型と同一視しない。[付録B](../appendix-b-numbers.md)は初期設計値であり、現行ドロップ率の根拠にしない。
- [既存6点バランス](v1.32-six-piece-balance.md)と [SetBalance.cs](../../tools/BalanceSim/SetBalance.cs) の上限正規化を数値予算の参考にする。代理値はDPS・実効耐久ではない。

## 2. 共通仕様

### 2.1 ID・定義・名称

| 項目 | 追加仕様 |
| --- | --- |
| ID | §3のslugに対しセット `set.<slug>`、部位 `set.<slug>.<slot小文字>`。例：`set.boss_demon.weapon`。14＋84 IDを追加し、既存IDを改名・再採番しない |
| スロット | 保存値 Weapon=0 / Armor=1 / Charm=2 / Head=3 / Hands=4 / Feet=5を維持。表示順は既存 `Content.SlotOrder` |
| 土台・レア度 | 既存600土台から対応枠・テーマに合う `BaseId` を選ぶ。新土台・レア度なし。Legendary、通常のimplicit・特性3本・アイテムレベル・強化・覚醒を継承 |
| 出所 | 提案：`SetDef` に任意の `BossTypeName` を追加。既存セットはnull。型名→セットと限定Unique ID集合を定義から導出し、手書きの別ID台帳を増やさない |
| 部位固有効果 | 現行のセット用 `UniqueDef` は `Powers` が空。ボスセット用の定義経路を追加し、既存Powerを各部位1行だけ持たせる。旧セットの空配列は維持 |
| 名称 | 推奨はボス名を出さないテーマ名（日／英、§3）。公式ボス日本語名は未取得。型名由来の「スコル」等を仮名として出す案は採用しない |
| 表示 | 出所表示もテーマ名＋ボス型名の内部対応を使用。名前・効果・連携は `Txt` と既存説明機構。公式名取得後もIDは不変 |

部位固有効果は `SetId` とPower1行を同時指定できる `UniqueDef` 定義入口の追加で表す。現行 `Loot.RollUnique` は定義Powerをコピーし、`Build.Compute` は部位Powerとセット効果を独立集計、`HostGearValidation` は定義Power数・種類・順序・値を照合できるため、新しい集計／検証規則は不要。ビルド装備14欄も維持。部位Powerの強化・覚醒、Linkの覚醒は既存倍率に従う（Linkに強化倍率は掛からない）。既存48セットの空Power定義とセットボーナスの倍率は変更しない。

### 2.2 専用ドロップ

提案式：**p = min(50%, 25% + 悪夢加算15ポイント + 2ポイント × clamp(撃破時深度, 0, 5))**。

| 撃破時深度 | 通常 | 悪夢扱い |
| ---: | ---: | ---: |
| 0 | 25% | 40% |
| 1 | 27% | 42% |
| 3 | 31% | 46% |
| 5以上 | 35% | 50% |

- 「悪夢」は本体の難易度Nightmare、Limboも同じ加算とする案。MODの敵 `NightmareAffix` とは別：通常の悪夢化抽選ではボスが対象外なので、それだけでは確率上昇を満たせない。本体 `GameManager` / `DewDifficultySettings` が難易度を保持し、`DewSave` もNightmareとLimboを同群として扱うことを参照した。Limboを含むかは末尾で確認する。
- 深度はホストが遠征前に共有選択した**夢の深さ `ClientSession.HostRun.DreamDepth`（0〜5）**を使用。個人の潜行Heat、本体Limbo深度、ゾーン番号、危険度とは別軸であり、重ね掛けしない。難易度／夢の深さは撃破時ホストがfactへ凍結し、保留報酬の解放時に取り直さない。
- ホストで `m is BossMonster`、本来のtier=Boss、許可14型との完全一致を満たす撃破だけ型名を付ける。`disableLoot`／無報酬ハンター等の既存除外を維持。幻影・召喚雑魚・Polarisの石・Crawlerは対象外。同じ許可型の変種も、そのボス自身の報酬として扱う。
- `Rules.OnKill` の `Waypoints.ApplyKill` 後、`Profile.StoreRng` 前に個人の既存RNGで専用抽選を1回行う。当選時は6部位を各1/6、重複ありで1点選び、`Loot.RollUnique` 生成品を `reward.Relics` 末尾へ加える。通常の図鑑・統計・通知・未確保鞄・満杯時処理を共用。対象外／型名欠落では追加乱数を消費しない。所持数・狙い系統・人数・幸運でpや部位比率を変えない。
- 既存のボス通常ドロップは率100%、2点目60%、floor=Uncommon、エピックpity `min(1, 0.05 + 0.035k)`。これを残す。専用当選はpityを進行／リセットせず、通常側の救済にも失敗回数にも数えない。専用pity・重複救済は追加しない。
- 既存 `SetPieceWeight=4` / `SetCompletionWeight=60` は汎用セット専用。新セットは対象外。専用品は道標変換の後置きなので、BossTribute等の格上げ、強制部位、個数制限、複製、SupplyLine等の分解、覚醒への変換に通さない（鞄満杯時の既存処理は残す）。本体の魂の祠（固有報酬10%／隠し100%）とも独立、祠の使用は不要。
- 均等重複ありで6種完成までの期待当選数は `6H6=14.7`。固定pでの期待撃破数は通常深度0=58.8、悪夢深度0=36.75、通常深度5=42、悪夢深度5=29.4。保証回数ではなく、出現機会が少ないボスでは収集が長期化するリスクがある。

**汎用プール除外**：`Loot.RollRelic` の伝説候補で、Uniqueの `SetId` から解決したセットに `BossTypeName` がある品を候補化前に除く（重み0で代用しない）。通常固有品・既存セットは候補のまま。通常ボスの既存2抽選、商人、箱、遺物交換、製作・合成、道標再抽選は同じ入口を通るため一括除外できる。限定IDの新規生成は専用ボス報酬入口のみとし、直接指定の開発コマンド `GiveUniqueCommand` も限定IDを拒否する。図鑑登録・既存品の保管／強化は従来どおり。プレイヤー間取引を許すかは末尾の未確定事項。

### 2.3 「一段強い」の数値基準

以下は強化0・覚醒0・連携なしの定義値。2/3点は対応する既存セットの各行を**1.25倍、四捨五入**（0.5は切上げ）。§3の2/3点はこの規則で算出。整数幅のため完全な25%にはならない。

| 対応例 | 2点：既存 → 案 | 3点：既存 → 案 | 6点：既存 → 案（正規化予算） |
| --- | --- | --- | --- |
| bastion → boss_demon | Armor 8→10、MaxHealthPct 6→8 | Bulwark 35→44、Aegis 25→31 | ShieldBash42＋ShieldbreakBurst8（0.867）→Breakout13＋UnbowedMind8（1.389） |
| winter → boss_skoll | ColdAmp 10→13、Armor 6→8 | Frost 45→56、Bulwark 30→38 | ImmovableStance10＋BrittleIce40（0.944）→14＋63（1.400） |
| cinder → boss_infernus | FireAmp 10→13、AttackPct 5→6 | Ember 45→56、Blaze 50→63 | Wildfire32（0.533）→Wildfire42＋Shatter105（1.400） |
| lamp → boss_white_night | LightAmp 10→13、MaxHealthPct 8→10 | Radiance 75→94、SecondWind 25→31 | OverflowingLife40＋WatchfulHand18（1.000）→StardustCycle21＋GleamingWard13（1.422） |
| dusk → boss_dark_moon | DarkAmp 10→13、CritChancePct 4→5 | Umbra 75→94、Executioner 40→50 | UmbralHeritage50（0.500）→UmbralHeritage70＋WeakPointWound112（1.400） |
| firmament → boss_nyx | PowerPct 6→8、Haste 8→10 | UltimateSurge 20→25、StarShield 12→15 | AceInHand12＋OpeningSalvo20（0.944）→17＋32（1.419） |

- 6点追加の予算 `Σ(Value / Content.PowerCap)` は全14案で**1.389〜1.422**を狙う。既存48組の記録は中央値0.925、最大約1.156。完成ボーナス自体を中央値の約1.5倍、既存最大より約20%以上高くする。ただし上限適用前の算術値であり、新セットの `Build.Compute` 測定結果ではない。
- 各部位は既存セットの固有効果0に対し、正規化予算約0.025〜0.083の小効果1行（6点合計約0.24〜0.32）。通常固有品の2大効果は載せず、単品だけで既存Legendaryを全面置換しない。
- 同種Powerの部位・3点・6点合算は既存上限で切る。条件付きAD/AP総上限・軽減・属性反応の世代／内部CDを緩めない。「一段強い」はテーマ対応の数値予算であり、全旅人・全戦況で必ずDPS25%増を保証する意味ではない。
- `SetBalance.PowerScore` / `SixBonusGain` は実装後の予算較正に使用できる。既存48組を比較母集団として固定し、ボスセットを混ぜて中央値を上げない。連携を基礎強さへ算入しない。上限飽和や条件不成立を含む実効出力は未較正。

### 2.4 任意連携・効果機構

連携を持つ12セットは**Charmにだけ**既存 `UniqueDef.Link` を付ける。Charm＋対応報酬の装着で発動し、セット点数は条件にしない。他5部位に複製しないため6倍加算を避ける。`Requires` は対応型名1つだけ。所持／祠使用／味方の装着では発動しない。既存 `Links.Validate` / `Satisfied` / `EquippedCap`、ホストの装着走査・起点記憶の帰属判定を再利用する。

| 表記 | 既存効果（以下の数値はLink.Value） |
| --- | --- |
| Attune 8 | 条件装着中、自分の攻撃力・魔力+8% |
| Guard 8 | 条件装着中、最大HP+8%・防御+8 |
| MemorySurge 12 | 対象記憶使用後5秒、攻撃力・魔力+12% |
| MemoryHaste 15 | 対象記憶を使った際、そのCDの戻りを15%加速（既存の意味のまま） |
| MemoryDamage 12 | 対象記憶によるダメージ+12% |

新しい**戦闘Power／Stat／LinkKind／トリガーは0件**。必要な基盤拡張は**2件**：①ボス専用取得経路（出所・汎用除外・fact伝達を含む）、②固有Powerを持つボスセット部位の定義入口。生成・集計・ホスト検証は既存の定義参照経路を再利用する。召喚そのもの・吸引・武器切替・フェーズ変身を新設せず、既存効果でテーマを表す。白夜／暗月の対ボーナスも今回は追加しない。

### 2.5 KillSync・Protocol・保存

| 経路 | 設計上の変更／不変条件 |
| --- | --- |
| ホスト捕捉 | `CaptureAuthoritativeRunKill` で許可ボス型名 `BossTypeName`、撃破時 `BossDropNightmare`（bool）、`BossDropDepth`（0〜5）をfactへ追加。他の撃破はnull/false/0 |
| 配信 | `AuthoritativeRunKill` → `DreamforgeMonsterKillMsg` → ホスト自身の `PublishHostKillFact` と全参加者。再送も同じ3フィールドを保持。本体側の新ネットワーク型は不要 |
| 結合・保留 | `KillClassificationLedger.TryResolve` → `PendingRunKill` → `PendingRunRewards` → `GrantPendingKill` → `Rules.OnKill`。型名・pの条件はfactからのみ採用し、local死亡側の推定で上書きしない |
| 権威と参加 | ローカル死亡は既存の敵対／報酬資格・HeroKey・level等の情報源。専用抽選資格はホストが許可型を付けたfact。とどめ／与ダメージの有無で人数分の抽選を減らさない。参加中の既存報酬資格を持つ各プレイヤーに同じpで1回ずつ |
| 重複・順序 | 既存 RunId / EventId / MonsterNetId の結合・解決済み記録を共用。通常＋専用を同じ撃破処理に含め、保留・再送・復元で専用だけ再抽選しない。抽選結果をホストと全員で一致させる必要はない |
| Protocol | 現在14→**15**。KillSyncメッセージの線形式が変わるため、ID追加だけの場合と異なり更新必須。Mirrorの型ルーティングを継承し、別の「メッセージ番号」は作らない |
| 内容整合 | ID追加で `ContentFingerprint` は変化する。ただし現行指紋は出所や率の値を含まないため、型名→セット対応・専用pの係数を対象へ追加する。Helloで旧版／異内容の効果適用を拒否し、専用fact受理にも内容一致を要求する（現行 `OnMonsterKill` のprotocol/run/generation確認だけに頼らない） |
| 永続保存 | 装備は既存 BaseId / UniqueId で保存し、SetIdはUnique定義から参照。`Profile.CurrentVersion=4`、`ResetBeforeVersion=3`、`LedgerState.CurrentSchemaVersion=1`を維持。プロフィールリセットなし |
| 撃破の保存 | `ProfileCodec.RunRecovery.cs` のpending killとkill分類fact両経路に任意キー `bossTypeName` / `bossDropNightmare` / `bossDropDepth` を追加。未結合 `KillClassificationCheckpoint.Facts`／`Deaths[].Kill`、結合済み未払い `RunRecoveryState.PendingKills` とclone・capture・restoreも保持。旧保存の欠落はnull/false/0＝通常報酬のみ継続、専用抽選なし。旧撃破を推定して遡及支給しない |
| 戻し運用 | 追加IDを知らない旧MODでは未知Uniqueの既存除外処理が働くため、新品を含む保存を旧版へ戻して使う互換は保証しない。旧版対応の別名／shimは設けない |

## 3. 14セット案

部位順は依頼順。IDは見出しのslugから§2.1で導出。部位行の数値・2/3/6点は既存Power／Statの入力単位であり、すべてが%ではない（例：Lifesteal 1＝最大HP0.1%回復、CriticalEcho 1＝0.1秒短縮）。発動条件・CD・対象範囲は現行 `Content.FormatPower` / `NewPowersV129.Describe` を変更しない。「本体単独」は全対象共通で従来の本体固有効果をそのまま使用でき、数値・挙動の変更なし。

### 3.1 `boss_demon`：根踏みの戦装 / Rootstomp Wargear
`Mon_Forest_BossDemon`：無属性。踏みつけ、樹木弾、瞬間移動、取り巻き、常時Unstoppable。群れを受け止める近接型。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 根砕きの槌 / Rootbreaker Maul | 被弾後の反撃強化：Retaliation 3 |
| Armor | 年輪の胴鎧 / Growthring Mail | 3体以上に囲まれる間防御：Bulwark 3 |
| Charm | 森震の核 / Forestquake Core | 低HP時の緊急回復：SecondWind 3 |
| Head | 樹冠の面 / Canopy Mask | 高HP時の攻撃力・魔力：Vigor 2 |
| Hands | 棘根の拳 / Thornroot Fists | 被ダメージ反射：Thorns 3 |
| Feet | 踏み固めの長靴 / Stompbound Boots | 回避時の周囲攻撃：Whirlwind 8 |

| 2点（基準bastion） | 3点 | 6点 |
| --- | --- | --- |
| Armor 10、MaxHealthPct 8 | Bulwark 44、Aegis 31（包囲防御・大被弾時障壁） | Breakout 13、UnbowedMind 8（包囲増加時障壁・敵スタン後の不撓） |

連携：ヒステリー / Hysteria `St_U_Hysteria`、Charm装着＋対象記憶装着で MemorySurge 12。役割：セット単独＝包囲下の反撃と生存、本体単独＝従来の記憶、併用＝その記憶使用後の短い反撃窓を補強。

### 3.2 `boss_skoll`：氷刃の王装 / Iceblade Regalia
`Mon_SnowMountain_BossSkoll`：Cold。オーラ刃、剣／矢召喚、旋風、落下、捕縛。冷気と会心の継続攻撃。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 氷雨の剣 / Icerain Sword | 通常攻撃で冷気付与：Frost 4 |
| Armor | 氷刃の帷子 / Iceblade Mail | 定期障壁：Barrier 1 |
| Charm | 雪嶺の核 / Snowcrest Core | 低HP時回復：SecondWind 3 |
| Head | 氷輪の冠 / Icering Crown | 高HP時攻撃力・魔力：Vigor 2 |
| Hands | 霜裂きの手甲 / Frostrend Grips | 冷気敵への会心追撃：BrittleIce 4 |
| Feet | 氷渦の鉄靴 / Icewhorl Sabatons | 回避時の周囲攻撃：Whirlwind 8 |

| 2点（基準winter） | 3点 | 6点 |
| --- | --- | --- |
| ColdAmp 13、Armor 8 | Frost 56、Bulwark 38（冷気付与・包囲防御） | BrittleIce 63、ImmovableStance 14（冷気会心・静止時攻防） |

連携：氷河のコア / Glacial Core `Gem_U_GlacialCore`、Charm＋対象エッセンス装着で Guard 8。役割：セット単独＝冷気会心型、本体単独＝従来のエッセンス、併用＝装着による耐久を足し、静止攻撃の隙を支える。

### 3.3 `boss_infernus`：噴火炉の軍装 / Eruptionforge Warplate
`Mon_LavaLand_BossInfernus`：Fire。火炎息、隕石、炎柱、噴火、跳躍。火を重ねて群れへ広げる。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 噴火の大剣 / Eruption Greatsword | 通常攻撃で火付与：Ember 6 |
| Armor | 炉壁の鎧 / Furnacewall Plate | 大被弾時障壁：Aegis 2 |
| Charm | 不滅炉の火種 / Undying Furnace Spark | 瀕死敵への通常攻撃追撃：Executioner 5 |
| Head | 熔冠 / Molten Crown | 通常記憶使用後の強化：Overload 2 |
| Hands | 赤熱の拳 / Redheat Fists | 4回目の通常攻撃追撃：Blaze 6 |
| Feet | 火柱越えの靴 / Flamepillar Treads | 回避後移動・攻撃速度：Sprint 3 |

| 2点（基準cinder） | 3点 | 6点 |
| --- | --- | --- |
| FireAmp 13、AttackPct 6 | Ember 56、Blaze 63（火蓄積・4撃目追撃） | Wildfire 42、Shatter 105（火の伝播・撃破爆発） |

連携：永遠の炎 / Eternal Flame `Gem_U_EternalFlame`、Charm＋対象エッセンス装着で Attune 8。役割：セット単独＝蓄積火・掃討、本体単独＝従来のエッセンス、併用＝共通攻撃力・魔力の小上乗せ。新しい炎柱は生成しない。

### 3.4 `boss_white_night`：白蓮の守装 / White Lotus Vestments
`Mon_Ink_BossWhiteNight`：Light。65%／35%移行、日蝕と安全域、掌撃、幻影、暗月との対戦。光と防衛。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 白蓮の錫杖 / White Lotus Staff | 通常攻撃で光付与：Radiance 8 |
| Armor | 安息域の法衣 / Haven Robe | 定期障壁：Barrier 1 |
| Charm | 光秤の印 / Lightscale Seal | 奥義使用時障壁：StarShield 2 |
| Head | 白暁の冠 / White Dawn Crown | 高HP時攻撃力・魔力：Vigor 2 |
| Hands | 蓮掌の手巻き / Lotus Palm Wraps | 通常攻撃命中回復：Lifesteal 1 |
| Feet | 光界の歩み / Lightward Steps | 回避後移動・攻撃速度：Sprint 3 |

| 2点（基準lamp） | 3点 | 6点 |
| --- | --- | --- |
| LightAmp 13、MaxHealthPct 10 | Radiance 94、SecondWind 31（光付与・緊急回復） | StardustCycle 21、GleamingWard 13（光5スタック時CD短縮・障壁中強化） |

連携：均衡の光線 / Beam of Balance `St_U_BeamOfBalance`、Charm＋対象記憶装着で Guard 8。役割：セット単独＝光を重ねて守る、本体単独＝従来の記憶、併用＝光線の構成に常時の耐久を足す。暗月装備は不要。

### 3.5 `boss_dark_moon`：黒月の刃装 / Black Moon Armament
`Mon_Ink_BossDarkMoon`：Dark。刃／槍／槌の切替、投槍、瞬間移動攻撃、自我の剣、怒り。攻勢と会心。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 黒月の三叉刃 / Black Moon Trident | 通常攻撃で闇付与：Umbra 8 |
| Armor | 怒影の鎧 / Wrathshadow Mail | 被弾後の攻撃力・魔力：Retaliation 3 |
| Charm | 月欠けの環 / Waning Moon Ring | 通常攻撃命中回復：Lifesteal 1 |
| Head | 自我の面 / Mask of the Self | 通常記憶使用後の強化：Overload 2 |
| Hands | 切替の握り / Shifting Grips | 通常攻撃会心時CD短縮：CriticalEcho 1 |
| Feet | 跳影の靴 / Shadowleap Boots | 回避・ダッシュ・瞬間移動後の次通常攻撃：ShadowStep 6 |

| 2点（基準dusk） | 3点 | 6点 |
| --- | --- | --- |
| DarkAmp 13、CritChancePct 5 | Umbra 94、Executioner 50（闇付与・処刑追撃） | UmbralHeritage 70、WeakPointWound 112（闇の撃破伝播・同敵3会心追撃） |

連携：均衡の光線 / Beam of Balance `St_U_BeamOfBalance`、Charm＋対象記憶装着で MemorySurge 12。役割：セット単独＝闇会心の攻勢、本体単独＝従来の記憶、併用＝光線使用後の攻撃窓。白夜とは防御／攻撃で分け、同じ報酬でも両セットを必須にしない。

### 3.6 `boss_nyx`：星海の主衣 / Starsea Sovereign Raiment
`Mon_Sky_BossNyx`：Light。3段階、星落とし、ブラックホール、星のダッシュ、レーザー、星柱。奥義と通常記憶の交代。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 星柱の杖 / Starpillar Staff | 通常攻撃で光付与：Radiance 8 |
| Armor | 星海の外套 / Starsea Mantle | 奥義使用時障壁：StarShield 2 |
| Charm | 夜空の種 / Nightseed | 通常記憶使用後の強化：Overload 2 |
| Head | 星軌の冠 / Starorbit Crown | 高HP時攻撃力・魔力：Vigor 2 |
| Hands | 星を結ぶ指 / Starbinding Fingers | 通常記憶後の次通常攻撃を周囲へ波及：Spellsweep 4 |
| Feet | 星渡りの靴 / Starcrossing Shoes | 回避後移動・攻撃速度：Sprint 3 |

| 2点（基準firmament） | 3点 | 6点 |
| --- | --- | --- |
| PowerPct 8、Haste 10 | UltimateSurge 25、StarShield 15（奥義後強化・障壁） | AceInHand 17、OpeningSalvo 32（奥義温存時の記憶火力・最初の記憶CD短縮） |

連携：彼女の世界 / Her World `St_U_HerWorld`、Charm＋対象記憶装着で MemoryDamage 12。役割：セット単独＝奥義温存と使用の循環、本体単独＝従来の記憶、併用＝対象記憶だけの火力補強。新しい吸引処理なし。

### 3.7 `boss_erebos`：終星の流衣 / Laststar Vesture
`Mon_Special_BossErebos`：Light＋隕石Fire。反重力、ブラックホール、星雨、召喚、視線、波紋、50%移行。多属性の面攻撃。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 終星の波琴 / Laststar Waveharp | 通常攻撃で光付与：Radiance 8 |
| Armor | 重力なき肩衣 / Weightless Mantle | 定期障壁：Barrier 1 |
| Charm | 残照の星核 / Afterglow Starcore | 低HP時回復：SecondWind 3 |
| Head | 隕光の冠 / Meteorlight Crown | 通常記憶使用後の強化：Overload 2 |
| Hands | 星雨の指環 / Starshower Fingerbands | 通常攻撃で火付与：Ember 6 |
| Feet | 波紋の履 / Ripple Shoes | 回避時の周囲攻撃：Whirlwind 8 |

| 2点（基準reverie） | 3点 | 6点 |
| --- | --- | --- |
| PowerPct 8、Haste 10 | Resonance 13、Radiance 110（ソロ半量の共鳴・光付与） | ElementalHarvest 42、Spellsweep 63（多属性撃破爆発・記憶後の掃討） |

連携：最後の星明かり / Last Starlight `Gem_U_LastStarlight`、Charm＋対象エッセンス装着で Attune 8。役割：セット単独＝火／光の掃討、本体単独＝従来のエッセンス、併用＝掃討火力の小上乗せ。Nyxの記憶は条件にしない。

### 3.8 `boss_seeker`：幻彩の追装 / Mirage Spectrum Gear
`Mon_DarkCave_BossSeeker`：多色オーブ・Fire裂け目、瞬間移動弾幕、分身、狭窄視、幻覚。多色を既存属性と移動で表現。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 幻彩の球杖 / Mirage Orb Rod | 通常攻撃で火付与：Ember 6 |
| Armor | 分身の外套 / Double Mantle | 大被弾時障壁：Aegis 2 |
| Charm | 魂窓の珠 / Soulwindow Pearl | 通常記憶使用後の強化：Overload 2 |
| Head | 狭界の面 / Narrowworld Mask | 通常攻撃で光付与：Radiance 8 |
| Hands | 紫影の手袋 / Violetshadow Gloves | 通常攻撃で闇付与：Umbra 8 |
| Feet | 裂け目の歩み / Fissure Steps | 回避後移動・攻撃速度：Sprint 3 |

| 2点（基準steamweave） | 3点 | 6点 |
| --- | --- | --- |
| FireAmp 10、ColdAmp 10 | Steam 75、Frost 63、Ember 56（火冷気反応・付与） | PrismShift 3、RunUp 96（異属性付与時障壁・歩行12m後の追撃） |

連携：魂の牢獄 / Soul Prison `Gem_U_SoulPrison`、Charm＋対象エッセンス装着で Guard 8。役割：セット単独＝属性切替と歩行攻撃、本体単独＝従来のエッセンス、併用＝移動型の耐久補完。幻覚・分身は生成しない。

### 3.9 `boss_azurak`：轟召の重装 / Roarcall Heavy Gear
`Mon_Despair_BossAzurak`：無属性。転がり、二重踏み、咆哮、砲撃、取り巻き／Displacer召喚。前線と召喚獣の共闘。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 轟召の戦棍 / Roarcall Mace | 被弾後の反撃強化：Retaliation 3 |
| Armor | 砲塁の鎧 / Artillery Rampart | 大被弾時障壁：Aegis 2 |
| Charm | 呼び声の角笛 / Calling Horn | 最大HP超過回復を障壁へ：OverflowingLife 4 |
| Head | 召集の兜 / Muster Helm | 高HP時攻撃力・魔力：Vigor 2 |
| Hands | 地鳴りの拳 / Earthrumble Fists | 通常攻撃命中回復：Lifesteal 1 |
| Feet | 転輪の鉄靴 / Rolling Sabatons | 回避時の周囲攻撃：Whirlwind 8 |

| 2点（基準vanguardline） | 3点 | 6点 |
| --- | --- | --- |
| MaxHealthPct 8、Tenacity 13 | VanguardsOath 31、TollOfGrudge 25、Bulwark 38（前線障壁・実HP損失の反撃・包囲防御） | PackFeast 6、DeathBloom 104（自召喚獣の撃破で障壁・被撃破で爆発） |

連携：穴掘り / Burrow `St_U_Burrow`、Charm＋対象記憶装着で MemoryHaste 15。役割：セット単独＝3点まで前線、6点は任意の召喚構成を選んだとき完成効果、本体単独＝従来の記憶、併用＝対象記憶の再使用を補助。セット自体は召喚獣を新規生成しない。

### 3.10 `boss_primus_aeron`：三相の武装 / Threefold Armament
`Mon_Primus_BossPrimusAeron`：Force／Adapt／Rage、二種の剣、多属性、沈黙・幻惑。複数属性と記憶の使い分け。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 三相の大剣 / Threefold Greatsword | 通常攻撃で光付与：Radiance 8 |
| Armor | 適応の胸甲 / Adaptation Cuirass | 定期障壁：Barrier 1 |
| Charm | 相転の核 / Phasechange Core | 通常記憶使用後の強化：Overload 2 |
| Head | 激情の面 / Rage Mask | 通常攻撃で闇付与：Umbra 8 |
| Hands | 流火の双手 / Flowfire Grips | 通常攻撃で火付与：Ember 6 |
| Feet | 氷爪の脛当て / Iceclaw Greaves | 通常攻撃で冷気付与：Frost 4 |

| 2点（基準prismdance） | 3点 | 6点 |
| --- | --- | --- |
| Haste 10、PowerPct 6 | PrismShift 4、ElementalHarvest 38、StardustCycle 18（属性切替障壁・多属性撃破・光5段CD短縮） | Convergence 210、Steam 84（4属性爆発・火冷気反応） |

連携なし（固有報酬なし）。役割：セット単独＝全部位で4属性付与が可能、任意の属性記憶で蓄積を補う。記憶／エッセンス単独＝従来どおり、併用＝通常の属性相乗だけ。専用のフェーズ切替・代替必須条件は作らない。

### 3.11 `boss_light_elemental`：光裂の法装 / Radiant Fracture Raiment
`Mon_Special_BossLightElemental`：Light。ビーム、弾幕、雷、召喚、HP閾値で技解禁。光の集中攻撃と通常記憶循環。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 光裂の杖 / Radiant Fracture Staff | 通常攻撃で光付与：Radiance 8 |
| Armor | 光片の法衣 / Radiant Shard Robe | 定期障壁：Barrier 1 |
| Charm | 世界罅の結晶 / Worldfissure Crystal | 奥義後の強化：UltimateSurge 2 |
| Head | 雷光の冠 / Lightning Halo | 通常記憶使用後の強化：Overload 2 |
| Hands | 光束の手巻き / Beam Wraps | 通常攻撃の連鎖追撃：ChainLightning 5 |
| Feet | 閃光の履 / Flash Shoes | 回避後移動・攻撃速度：Sprint 3 |

| 2点（基準daybreak） | 3点 | 6点 |
| --- | --- | --- |
| LightAmp 13、AttackPct 8 | Radiance 94、Vigor 19（光付与・高HP強化） | FocusFire 84、StardustCycle 21（同敵5通常攻撃追撃・光5段CD短縮） |

連携：世界の破壊者 / World Cracker `St_U_WorldCracker`、Charm＋対象記憶装着で MemoryDamage 12。役割：セット単独＝光の単体集中型、本体単独＝従来の記憶、併用＝対象記憶の火力を補強。新規レーザーは作らない。

### 3.12 `boss_maw`：飢影の狩装 / Ravenous Shadow Gear
`Mon_Special_BossMaw`：Dark。噛みつき、滅殺態勢、煉獄、影歩き、月光の誓約、75%／35%移行。低HPで攻めて吸収する。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 飢影の牙刃 / Ravenous Fangblade | 通常攻撃で闇付与：Umbra 8 |
| Armor | 煉獄の革鎧 / Purgatory Leathers | 被弾後の強化：Retaliation 3 |
| Charm | 大顎の護符 / Greatjaw Talisman | 撃破時の回復：SoulSiphon 2 |
| Head | 飢えの面 / Hunger Mask | 高HP時の強化：Vigor 2 |
| Hands | 貪りの爪 / Devouring Claws | 通常攻撃命中回復：Lifesteal 1 |
| Feet | 影歩きの靴 / Shadowwalk Boots | 移動技後の次通常攻撃：ShadowStep 6 |

| 2点（基準asura） | 3点 | 6点 |
| --- | --- | --- |
| AttackPct 10、MaxHealthFlat 50 | Bloodlust 25、Lifesteal 13（低HP攻撃速度・命中回復） | Executioner 84、SpilloverStrike 84（瀕死敵追撃・過剰撃破ダメージの転送） |

連携：大噛みつき / Big Chomp `St_U_BigChomp`、Charm＋対象記憶装着で MemorySurge 12。役割：セット単独＝リスクを伴う吸収近接、本体単独＝従来の記憶、併用＝対象記憶後の攻め時を補強。魂の祠ではなく専用祠を使う本体報酬経路には介入しない。

### 3.13 `boss_obliviax`：忘針の襲装 / Oblivion Needle Gear
`Mon_Special_BossObliviax`：Dark。影歩き、砲台姿勢、針、突撃連鎖、巣への誘拐。記憶と通常攻撃の待ち伏せ。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 忘針の連刃 / Oblivion Needleblade | 通常攻撃で闇付与：Umbra 8 |
| Armor | 巣影の外套 / Nestshadow Mantle | 大被弾時障壁：Aegis 2 |
| Charm | 忘却の喉石 / Oblivion Throatstone | 通常記憶使用後の強化：Overload 2 |
| Head | 砲座の面 / Turret Mask | 静止時攻防：ImmovableStance 1 |
| Hands | 針継ぎの手袋 / Needlestitch Gloves | 高HP敵への初撃追撃：OpeningStrike 6 |
| Feet | 攫い影の靴 / Abducting Shadow Boots | 移動技後の次通常攻撃：ShadowStep 6 |

| 2点（基準phantom） | 3点 | 6点 |
| --- | --- | --- |
| DarkAmp 13、MoveSpeedPct 6 | Umbra 110、EchoingDodge 98（闇付与・回避後追撃） | Spellsweep 63、UmbralHeritage 70（記憶後の掃討・闇の撃破伝播） |

連携：忘却の咆哮 / Shout of Oblivion `St_U_ShoutOfOblivion`、Charm＋対象記憶装着で MemoryHaste 15。役割：セット単独＝影歩きと掃討、本体単独＝従来の記憶、併用＝対象記憶の再使用を補助。誘拐・砲台変身の機構は追加しない。

### 3.14 `boss_polaris`：墜聖の双装 / Fallen Sanctity Regalia
`Mon_Special_BossPolaris`：聖形／獣形の2形態。光線・黄金槍・降火・対呪文、呪いの飛込み・雷・踏みつけ。光と炎、防御後の攻勢。

| 部位 | 装備名（日 / 英） | 固有効果の概要・Power値 |
| --- | --- | --- |
| Weapon | 墜聖の金槍 / Fallen Golden Spear | 通常攻撃で光付与：Radiance 8 |
| Armor | 聖獣の胸甲 / Sacred Beast Cuirass | 大被弾時障壁：Aegis 2 |
| Charm | 星無き聖印 / Starless Sacred Seal | 奥義使用時障壁：StarShield 2 |
| Head | 反呪の冠 / Counterspell Crown | 通常記憶使用後の強化：Overload 2 |
| Hands | 降火の手甲 / Rainfire Gauntlets | 通常攻撃で火付与：Ember 6 |
| Feet | 呪降の鉄靴 / Cursed Descent Sabatons | 回避時の周囲攻撃：Whirlwind 8 |

| 2点（基準daybreak） | 3点 | 6点 |
| --- | --- | --- |
| LightAmp 13、AttackPct 8 | Radiance 94、Vigor 19（光付与・高HP強化） | ShieldBash 63、OpeningSalvo 32（障壁中の通常攻撃追撃・記憶の口火） |

連携なし（固有報酬なし）。役割：セット単独＝被弾／奥義で障壁を張り攻勢へ移る、記憶／エッセンス単独＝従来どおり、併用＝任意の障壁系記憶による通常の相乗だけ。形態変身や報酬の捏造なし。

## 4. マルチプレイの整合条件・実装段階・既存への影響

確認項目は仕様上の整合条件のみ。テスト設計・計画・新規ケース提案は本書の対象外で、テスト担当に委ねる。

| 確認項目 | 必須の整合条件 |
| --- | --- |
| 識別 | ホスト自身と参加者が同じfactの型名・難易度区分・深度を使用。白夜／暗月は個別撃破、同時死亡の雑魚には限定抽選なし |
| 抽選 | 各参加者に1回ずつ独立抽選。同じpであって、当否・部位・特性が同一である必要はない。ホストの当選を全員へ配らない |
| 保留・再送・継続 | 解決済みEventIdを共有する通常＋専用報酬の処理単位を維持。ルール選択待ち・再接続・チェックポイント復元で型名やpを変えず、支給を増殖させない |
| 効果 | 同じ6部位・強化・覚醒・星図ならホストと参加者の `Build.Compute` 集計は同じ。装備交換で2/3/6の累積と部位Powerを再計算 |
| 連携 | 自分のCharm＋自分の対象報酬だけで成立。解除時に消え、味方の装着だけでは成立しない。対象記憶の起点帰属・既存上限はホストが適用 |
| 互換 | Protocol15と内容指紋一致が前提。旧版混在時に「同じ表示なのにホストだけ効果不反映」を正常運用扱いしない |

| 実装段階 | 範囲 |
| --- | --- |
| 段階1 | 基盤拡張2件・Protocol15・任意保存キー・汎用除外・表示対応。Demon／Skollの2セット12部位（記憶連携／エッセンス連携を各1種）を登録し、既存の保存・装備経路へ接続 |
| 段階2 | 残り12セット72部位を追加。白夜／暗月を独立登録、Primus／PolarisはLinkなし。§2.3の同じ予算基準で全14組を較正 |
| 段階間 | 最終IDを最初から使用。コードに未実装の仮定義・無効な完成効果・互換aliasを残さない。内容追加だけの段階2ではProtocolをさらに上げず、指紋で区別 |

既存テストへの影響の見込み（変更・追加の具体的なテスト設計は行わない）：`SetsV122Tests` 等のセット件数48の前提は62へ、セット部位288は372へ増える。`GameRulesTests` のセット部位Power0、`LinksV126Tests` の全連携品Power2本・連携件数357（追加後369）、`EventSetTests` の全セットが汎用抽選可能という既存前提と衝突する。`SixPieceBalanceV132Tests` の全 `Content.Sets` を同じ許容帯で扱う前提もボス上位予算と衝突する。`KillClassificationTests` / `PendingRunRewardsTests` / `RunDurabilityTests` はfact・保存、`HostGearValidationTests` / `SixPieceSetTests` は部位PowerとLinkの併存が影響範囲。図鑑分母はUnique84点追加で増えるが、土台数・既存ID・装備線形式・セーブ版の不変条件は維持する。

## 5. 未確定事項（利用者への確認）

1. ドロップ案25%／40%、深度ごと+2ポイント・上限50%を採用するか。重複あり・専用pityなしでは完成期待29.4〜58.8撃破となる収集期間を許容するか。
2. 悪夢の意味を「本体Nightmare難易度」とし、Limboも同じ加算に含めるか（MODの敵悪夢化とは区別する）。
3. 「一段強い」を2/3点約25%増、6点予算約1.4＋小部位効果とするか。戦闘の実効出力は未較正であり、通常セット最大よりも完成予算を高くする方針の承認が必要。
4. テーマ名の日／英案、各部位名を採用するか。ボス公式日本語名を待たずにテーマ名で公開するか。
5. Charm1点で任意連携を有効にする案を採用するか。セット点数条件を追加する場合は既存LinkDefだけでは表せず、今回は数えていない別の基盤拡張が必要になる。
6. ボス限定を「新規生成元のみの限定」として既存のプレイヤー間取引を許すか、取引も禁止するか。汎用抽選・商人・箱からの新規生成不可はどちらでも確定。
7. Azurakの6点完成効果を召喚構成向けにするか（穴掘りは不要だが、6点効果の発動には別途任意の召喚手段が要る）。
