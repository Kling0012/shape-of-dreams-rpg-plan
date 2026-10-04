# Issue #48：ボス限定の全部位セットと任意連携

## 1. 概要・確定方針

設計のみ。14セット・84部位を追加し、既存48セット・既存ID・本体報酬を変更しない。強さの方針・名称・連携条件は利用者指定で確定、ドロップ率の数値案だけ§5で確認する。実装済み／戦闘で較正済みの値ではない。

| 項目 | 方針 |
| --- | --- |
| 対象 | `BossMonster` 派生15型から空実装の `Mon_Despair_BossCrawler` を除く14型。白夜・暗月は別セット |
| 構成 | Weapon / Armor / Charm / Head / Hands / Feet 各1点。通常の2/3/6ピース効果は累積。4/5専用の通常効果なし（任意連携には4部位段階あり） |
| 入手 | 対応ボス撃破ごと、各プレイヤーが独立抽選。当選時、そのセットからランダムな1部位のみ |
| 限定 | 他ボス・通常敵・汎用プール・宝箱・商人・製作等から新規取得不可 |
| 強さ | 同テーマの既存セットから2/3ピースの値を約1.4倍、6ピース追加の正規化予算を既存中央値の約2倍にする |
| 任意連携 | 自分の対応記憶／エッセンス装着＋対応セット2/4/6部位で段階強化。セット単独で成立、本体報酬単独は本体どおり（このセットの連携なし） |
| 報酬なし | PrimusAeron / Polaris は本体報酬との連携なし。追加の任意要素も今回は採用しない |
| 同期 | ボス型名・抽選条件はホストの撃破factが正。抽選結果は個人別、戦闘効果はホストが適用 |
| 推奨事項の確定 | 悪夢補正はLimboを含む。日英テーマ名・部位名は§3を採用。Azurakの6点は召喚構成向けとし、別途任意の召喚手段が必要（穴掘りは不要） |

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
| 名称 | ボス名を出さないテーマ名（日／英、§3）を採用。公式ボス日本語名は未取得。型名由来の「スコル」等の仮名は採用しない |
| 表示 | 出所表示もテーマ名＋ボス型名の内部対応を使用。名前・効果・連携は `Txt` と既存説明機構。公式名取得後もIDは不変 |

部位固有効果は `SetId` とPower1行を同時指定できる `UniqueDef` 定義入口の追加で表す。現行 `Loot.RollUnique` は定義Powerをコピーし、`Build.Compute` は部位Powerとセット効果を独立集計、`HostGearValidation` は定義Power数・種類・順序・値を照合できるため、部位Power用の新集計／検証規則は不要。ビルド装備14欄も維持。部位Powerの強化・覚醒は既存倍率、セット段階連携は§2.4の固定値。既存48セット・既存単品Linkの倍率は変更しない。

### 2.2 専用ドロップ

数値未確定の提案式：**p = min(20%, 10% +（悪夢なら5ポイント、通常は0）+ 1ポイント × clamp(撃破時深度, 0, 5))**。部位は均等・重複あり、専用pityなし。

| 撃破時深度 | 通常p | 通常の完成期待撃破数 | 悪夢p | 悪夢の完成期待撃破数 |
| ---: | ---: | ---: | ---: | ---: |
| 0（基礎） | 10% | 147 | 15% | 98 |
| 1 | 11% | 約133.64 | 16% | 約91.88 |
| 3 | 13% | 約113.08 | 18% | 約81.67 |
| 5以上 | 15% | 98 | 20%（上限） | 73.5 |

- 「悪夢」は本体の難易度NightmareとLimboを含む方針で確定。MODの敵 `NightmareAffix` とは別：通常の悪夢化抽選ではボスが対象外なので、それだけでは確率上昇を満たせない。本体 `GameManager` / `DewDifficultySettings` が難易度を保持し、`DewSave` もNightmareとLimboを同群として扱うことを参照した。
- 深度はホストが遠征前に共有選択した**夢の深さ `ClientSession.HostRun.DreamDepth`（0〜5）**を使用。個人の潜行Heat、本体Limbo深度、ゾーン番号、危険度とは別軸であり、重ね掛けしない。難易度／夢の深さは撃破時ホストがfactへ凍結し、保留報酬の解放時に取り直さない。
- ホストで `m is BossMonster`、本来のtier=Boss、許可14型との完全一致を満たす撃破だけ型名を付ける。`disableLoot`／無報酬ハンター等の既存除外を維持。幻影・召喚雑魚・Polarisの石・Crawlerは対象外。同じ許可型の変種も、そのボス自身の報酬として扱う。
- `Rules.OnKill` の `Waypoints.ApplyKill` 後、`Profile.StoreRng` 前に個人の既存RNGで専用抽選を1回行う。当選時は6部位を各1/6、重複ありで1点選び、`Loot.RollUnique` 生成品を `reward.Relics` 末尾へ加える。通常の図鑑・統計・通知・未確保鞄・満杯時処理を共用。対象外／型名欠落では追加乱数を消費しない。所持数・狙い系統・人数・幸運でpや部位比率を変えない。
- 既存のボス通常ドロップは率100%、2点目60%、floor=Uncommon、エピックpity `min(1, 0.05 + 0.035k)`。これを残す。専用当選はpityを進行／リセットせず、通常側の救済にも失敗回数にも数えない。専用pity・重複救済は追加しない。
- 既存 `SetPieceWeight=4` / `SetCompletionWeight=60` は汎用セット専用。新セットは対象外。専用品は道標変換の後置きなので、BossTribute等の格上げ、強制部位、個数制限、複製、SupplyLine等の分解、覚醒への変換に通さない（鞄満杯時の既存処理は残す）。本体の魂の祠（固有報酬10%／隠し100%）とも独立、祠の使用は不要。
- 均等重複ありで6種完成までの期待当選数は `6H6=14.7`。表は固定pで `14.7 / p` を算出した期待値であり、当選品を保持できる前提。完成回数の保証ではなく、出現機会が少ないボスでは収集が長期化する。率・補正・上限は§5に確認事項として残す。

**汎用プール除外**：`Loot.RollRelic` の伝説候補で、Uniqueの `SetId` から解決したセットに `BossTypeName` がある品を候補化前に除く（重み0で代用しない）。通常固有品・既存セットは候補のまま。通常ボスの既存2抽選、商人、箱、遺物交換、製作・合成、道標再抽選は同じ入口を通るため一括除外できる。限定IDの新規生成は専用ボス報酬入口のみとし、直接指定の開発コマンド `GiveUniqueCommand` も限定IDを拒否する。図鑑登録・既存品の保管／強化は従来どおり。

### 2.3 「一段強い」の数値基準

以下は強化0・覚醒0・連携なしの定義値。2/3点は対応する既存セットの各行を**1.4倍、四捨五入**（0.5は切上げ）。§3の全14組をこの規則で改訂し、6点は既存中央値の約2倍を目安にする。部位単品の小効果は据置。

| 対応例 | 2点：既存 → 案 | 3点：既存 → 案 | 6点：既存 → 案（正規化予算） |
| --- | --- | --- | --- |
| bastion → boss_demon | Armor 8→11、MaxHealthPct 6→8 | Bulwark 35→49、Aegis 25→35 | ShieldBash42＋ShieldbreakBurst8（0.867）→Breakout17＋UnbowedMind11（1.861） |
| winter → boss_skoll | ColdAmp 10→14、Armor 6→8 | Frost 45→63、Bulwark 30→42 | ImmovableStance10＋BrittleIce40（0.944）→19＋81（1.850） |
| cinder → boss_infernus | FireAmp 10→14、AttackPct 5→7 | Ember 45→63、Blaze 50→70 | Wildfire32（0.533）→Wildfire56＋Shatter138（1.853） |
| lamp → boss_white_night | LightAmp 10→14、MaxHealthPct 8→11 | Radiance 75→105、SecondWind 25→35 | OverflowingLife40＋WatchfulHand18（1.000）→StardustCycle28＋GleamingWard17（1.878） |
| dusk → boss_dark_moon | DarkAmp 10→14、CritChancePct 4→6 | Umbra 75→105、Executioner 40→56 | UmbralHeritage50（0.500）→UmbralHeritage93＋WeakPointWound147（1.849） |
| firmament → boss_nyx | PowerPct 6→8、Haste 8→11 | UltimateSurge 20→28、StarShield 12→17 | AceInHand12＋OpeningSalvo20（0.944）→22＋42（1.850） |

- 6点追加の予算 `Σ(Value / Content.PowerCap)` は目安**1.85**、全14案で**1.849〜1.878**。既存48組の記録中央値0.925の約2.00〜2.03倍、最大約1.156の約1.60倍となる。上限適用前の算術値であり、新セットの `Build.Compute` 測定結果ではない。
- 各部位は既存セットの固有効果0に対し、正規化予算約0.025〜0.083の小効果1行（6点合計約0.24〜0.32）。通常固有品の2大効果は載せず、単品だけで既存Legendaryを全面置換しない。
上限は強さではなく破綻防止の境界として維持する。部位・3点・6点の同種Power合算、条件付きAD/AP・軽減・Linkの既存上限を緩めず、v1.29系Power／属性反応の実行時capも守る。無敵の延長、即死／HP割合処刑、発動CD撤廃、連鎖世代追加は行わない。CD短縮は既存の残りCD割合と発動条件のまま、余韻は加算せず既存の窓・最高値規則を使用し、追加攻撃／反応を自己再発動させない。目標予算へ届かせるためのcap超過は認めず、実効DPS1.4倍や無被弾を保証しない。
- `SetBalance.PowerScore` / `SixBonusGain` は実装後の予算較正に使用できる。既存48組を比較母集団として固定し、ボスセットを混ぜて中央値を上げない。連携を基礎強さへ算入しない。上限飽和や条件不成立を含む実効出力は未較正。

### 2.4 任意連携・効果機構

連携を持つ12セットは、**対応セットの異なる部位数＋自分の対応記憶／エッセンス装着**を条件にする。部位の組合せは任意でCharm必須ではない。0/1部位は連携なし、2/3は第1段階、4/5は第2段階、6は第3段階。最高の1段階だけ有効、段階の累積なし。所持・祠使用・味方の装着だけでは成立しない。白夜／暗月は個別に数え、部位数を合算しない。

- **データ**：`SetDef.LinkStages: SetLinkStage[]` に `SetLinkStage { RequiredPieces, LinkDef Link }` を3件追加（閾値は2/4/6）。全段階のRequiresは同じ対応型名1つ、Kindも同じ、Valueは単調増加・`Links.Cap(kind, 1)`以内で、各Linkは `Links.Validate` を通す。Primus／Polaris・旧48セットは空。ボス部位の `UniqueDef.Link` は全てnull。
- **集計**：`Build.Compute` の既存UniqueId重複除外によるセット集計後に最高段階を選ぶ。同条件の装備連携と同じ集約・上限経路へ入れ、集約確定をセット段階選択後へ移す。MemorySurgeは選択した定義LinkDefを再利用し、既存の参照同一性による余韻保持・独立窓・最高値規則を守る。セットごとに1候補だけ、強化／覚醒倍率なし（単品の倍率起点を新設しない）。
- **判定時点**：装備交換・再接続のビルド再構築で部位段階を更新し、ホストの既存装着走査と各記憶の起点イベントで `Links.Satisfied` を適用する。段階低下／装着解除では旧段階の余韻も既存の保持・失効処理へ反映し、6部位時の高い値を残さない。段階上昇は余韻を自動発動せず、次の対象記憶使用から新しい値。
- **同期**：参加者表示とホスト再構築で同じセット定義・部位数・段階選択を使用。ホストは検証済み装備から計算し、参加者の申告Linkを信用しない。生成結果は既存 `Build.Links` の線形式に載り、部位数や段階専用のwire／保存フィールドは追加しない。
- **説明表示**：日英で対応報酬名、2/4/6の全数値、現在部位数・有効段階・対象装着の可否・次段階までの不足数を表示。単品説明もセット段階への参照にし、「Charmだけで発動」の旧表示は廃止。§3の `a/b/c` は2/4/6部位の値。

| LinkKind | 2部位 | 4部位 | 6部位 | 既存効果（Valueの意味） |
| --- | ---: | ---: | ---: | --- |
| Attune | 8 | 14 | 20 | 条件装着中、自分の攻撃力・魔力+Value% |
| Guard | 8 | 14 | 20 | 条件装着中、最大HP+Value%・防御+Value |
| MemorySurge | 12 | 20 | 30 | 対象記憶使用後5秒、攻撃力・魔力+Value% |
| MemoryHaste | 15 | 25 | 40 | 対象記憶使用時、そのCDの戻りをValue%加速 |
| MemoryDamage | 12 | 20 | 30 | 対象記憶によるダメージ+Value% |

新しい**戦闘Power／Stat／LinkKind／トリガーは0件**。基盤拡張は**3件**：①ボス専用取得経路（出所・汎用除外・fact伝達）、②固有Powerを持つセット部位の定義入口、③セット部位数で既存LinkDefの最高段階を選択・表示する機構。生成・戦闘効果・ホスト装備検証は既存経路を再利用。召喚・吸引・武器切替・変身・白夜／暗月の追加対ボーナスは新設しない。

### 2.5 KillSync・Protocol・保存

| 経路 | 設計上の変更／不変条件 |
| --- | --- |
| ホスト捕捉 | `CaptureAuthoritativeRunKill` で許可ボス型名 `BossTypeName`、撃破時 `BossDropNightmare`（bool）、`BossDropDepth`（0〜5）をfactへ追加。他の撃破はnull/false/0 |
| 配信 | `AuthoritativeRunKill` → `DreamforgeMonsterKillMsg` → ホスト自身の `PublishHostKillFact` と全参加者。再送も同じ3フィールドを保持。本体側の新ネットワーク型は不要 |
| 結合・保留 | `KillClassificationLedger.TryResolve` → `PendingRunKill` → `PendingRunRewards` → `GrantPendingKill` → `Rules.OnKill`。型名・pの条件はfactからのみ採用し、local死亡側の推定で上書きしない |
| 権威と参加 | ローカル死亡は既存の敵対／報酬資格・HeroKey・level等の情報源。専用抽選資格はホストが許可型を付けたfact。とどめ／与ダメージの有無で人数分の抽選を減らさない。参加中の既存報酬資格を持つ各プレイヤーに同じpで1回ずつ |
| 重複・順序 | 既存 RunId / EventId / MonsterNetId の結合・解決済み記録を共用。通常＋専用を同じ撃破処理に含め、保留・再送・復元で専用だけ再抽選しない。抽選結果をホストと全員で一致させる必要はない |
| Protocol | 基点は14、#48単独では15へ更新。#47も14→15を提案中のため、**後からマージする側が先行側の値から次の版へ上げる**（#47先行なら#48は16、逆順なら#47が16）。同じ15を異なる線形式で共用しない。KillSyncの線形式変更で更新必須、ID追加だけとは区別。Mirrorの型ルーティングを継承し、別の「メッセージ番号」は作らない |
| 内容整合 | ID追加で `ContentFingerprint` は変化する。ただし現行指紋は出所や率の値を含まないため、型名→セット対応・専用pの係数・LinkStagesの閾値とLink定義を対象へ追加する。Helloで旧版／異内容の効果適用を拒否し、専用fact受理にも内容一致を要求する（現行 `OnMonsterKill` のprotocol/run/generation確認だけに頼らない） |
| 永続保存 | 装備は既存 BaseId / UniqueId で保存し、SetIdはUnique定義から参照。段階連携も定義・装備から再計算し、保存項目は追加しない。`Profile.CurrentVersion=4`、`ResetBeforeVersion=3`、`LedgerState.CurrentSchemaVersion=1`を維持。プロフィールリセットなし |
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
| Armor 11、MaxHealthPct 8 | Bulwark 49、Aegis 35（包囲防御・大被弾時障壁） | Breakout 17、UnbowedMind 11（包囲増加時障壁・敵スタン後の不撓） |

連携：ヒステリー / Hysteria `St_U_Hysteria`、対応セット2/4/6部位＋対象記憶装着で MemorySurge 12/20/30。役割：セット単独＝包囲下の反撃と生存、本体単独＝従来の記憶、併用＝その記憶使用後の短い反撃窓を補強。

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
| ColdAmp 14、Armor 8 | Frost 63、Bulwark 42（冷気付与・包囲防御） | BrittleIce 81、ImmovableStance 19（冷気会心・静止時攻防） |

連携：氷河のコア / Glacial Core `Gem_U_GlacialCore`、対応セット2/4/6部位＋対象エッセンス装着で Guard 8/14/20。役割：セット単独＝冷気会心型、本体単独＝従来のエッセンス、併用＝装着による耐久を足し、静止攻撃の隙を支える。

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
| FireAmp 14、AttackPct 7 | Ember 63、Blaze 70（火蓄積・4撃目追撃） | Wildfire 56、Shatter 138（火の伝播・撃破爆発） |

連携：永遠の炎 / Eternal Flame `Gem_U_EternalFlame`、対応セット2/4/6部位＋対象エッセンス装着で Attune 8/14/20。役割：セット単独＝蓄積火・掃討、本体単独＝従来のエッセンス、併用＝共通攻撃力・魔力を上乗せ。新しい炎柱は生成しない。

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
| LightAmp 14、MaxHealthPct 11 | Radiance 105、SecondWind 35（光付与・緊急回復） | StardustCycle 28、GleamingWard 17（光5スタック時CD短縮・障壁中強化） |

連携：均衡の光線 / Beam of Balance `St_U_BeamOfBalance`、白夜セット2/4/6部位＋対象記憶装着で Guard 8/14/20。役割：セット単独＝光を重ねて守る、本体単独＝従来の記憶、併用＝光線の構成に常時の耐久を足す。暗月装備は不要。

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
| DarkAmp 14、CritChancePct 6 | Umbra 105、Executioner 56（闇付与・処刑追撃） | UmbralHeritage 93、WeakPointWound 147（闇の撃破伝播・同敵3会心追撃） |

連携：均衡の光線 / Beam of Balance `St_U_BeamOfBalance`、暗月セット2/4/6部位＋対象記憶装着で MemorySurge 12/20/30。役割：セット単独＝闇会心の攻勢、本体単独＝従来の記憶、併用＝光線使用後の攻撃窓。白夜とは防御／攻撃で分け、混成時は各セットの段階を独立適用。

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
| PowerPct 8、Haste 11 | UltimateSurge 28、StarShield 17（奥義後強化・障壁） | AceInHand 22、OpeningSalvo 42（奥義温存時の記憶火力・最初の記憶CD短縮） |

連携：彼女の世界 / Her World `St_U_HerWorld`、対応セット2/4/6部位＋対象記憶装着で MemoryDamage 12/20/30。役割：セット単独＝奥義温存と使用の循環、本体単独＝従来の記憶、併用＝対象記憶だけの火力補強。新しい吸引処理なし。

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
| PowerPct 8、Haste 11 | Resonance 14、Radiance 123（ソロ半量の共鳴・光付与） | ElementalHarvest 56、Spellsweep 83（多属性撃破爆発・記憶後の掃討） |

連携：最後の星明かり / Last Starlight `Gem_U_LastStarlight`、対応セット2/4/6部位＋対象エッセンス装着で Attune 8/14/20。役割：セット単独＝火／光の掃討、本体単独＝従来のエッセンス、併用＝掃討火力の上乗せ。Nyxの記憶は条件にしない。

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
| FireAmp 11、ColdAmp 11 | Steam 84、Frost 70、Ember 63（火冷気反応・付与） | PrismShift 5、RunUp 102（異属性付与時障壁・歩行12m後の追撃） |

連携：魂の牢獄 / Soul Prison `Gem_U_SoulPrison`、対応セット2/4/6部位＋対象エッセンス装着で Guard 8/14/20。役割：セット単独＝属性切替と歩行攻撃、本体単独＝従来のエッセンス、併用＝移動型の耐久補完。幻覚・分身は生成しない。

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
| MaxHealthPct 8、Tenacity 14 | VanguardsOath 35、TollOfGrudge 28、Bulwark 42（前線障壁・実HP損失の反撃・包囲防御） | PackFeast 7、DeathBloom 156（自召喚獣の撃破で障壁・被撃破で爆発） |

連携：穴掘り / Burrow `St_U_Burrow`、対応セット2/4/6部位＋対象記憶装着で MemoryHaste 15/25/40。役割：セット単独＝3点まで前線、6点は任意の召喚構成を選んだとき完成効果、本体単独＝従来の記憶、併用＝対象記憶の再使用を補助。セット自体は召喚獣を新規生成しない。

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
| Haste 11、PowerPct 7 | PrismShift 4、ElementalHarvest 42、StardustCycle 20（属性切替障壁・多属性撃破・光5段CD短縮） | Convergence 278、Steam 111（4属性爆発・火冷気反応） |

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
| LightAmp 14、AttackPct 8 | Radiance 105、Vigor 21（光付与・高HP強化） | FocusFire 111、StardustCycle 28（同敵5通常攻撃追撃・光5段CD短縮） |

連携：世界の破壊者 / World Cracker `St_U_WorldCracker`、対応セット2/4/6部位＋対象記憶装着で MemoryDamage 12/20/30。役割：セット単独＝光の単体集中型、本体単独＝従来の記憶、併用＝対象記憶の火力を補強。新規レーザーは作らない。

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
| AttackPct 11、MaxHealthFlat 56 | Bloodlust 28、Lifesteal 14（低HP攻撃速度・命中回復） | Executioner 111、SpilloverStrike 111（瀕死敵追撃・過剰撃破ダメージの転送） |

連携：大噛みつき / Big Chomp `St_U_BigChomp`、対応セット2/4/6部位＋対象記憶装着で MemorySurge 12/20/30。役割：セット単独＝リスクを伴う吸収近接、本体単独＝従来の記憶、併用＝対象記憶後の攻め時を補強。魂の祠ではなく専用祠を使う本体報酬経路には介入しない。

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
| DarkAmp 14、MoveSpeedPct 7 | Umbra 123、EchoingDodge 109（闇付与・回避後追撃） | Spellsweep 83、UmbralHeritage 93（記憶後の掃討・闇の撃破伝播） |

連携：忘却の咆哮 / Shout of Oblivion `St_U_ShoutOfOblivion`、対応セット2/4/6部位＋対象記憶装着で MemoryHaste 15/25/40。役割：セット単独＝影歩きと掃討、本体単独＝従来の記憶、併用＝対象記憶の再使用を補助。誘拐・砲台変身の機構は追加しない。

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
| LightAmp 14、AttackPct 8 | Radiance 105、Vigor 21（光付与・高HP強化） | ShieldBash 83、OpeningSalvo 42（障壁中の通常攻撃追撃・記憶の口火） |

連携なし（固有報酬なし）。役割：セット単独＝被弾／奥義で障壁を張り攻勢へ移る、記憶／エッセンス単独＝従来どおり、併用＝任意の障壁系記憶による通常の相乗だけ。形態変身や報酬の捏造なし。

## 4. マルチプレイの整合条件・実装段階・既存への影響

確認項目は仕様上の整合条件のみ。テスト設計・計画・新規ケース提案は本書の対象外で、テスト担当に委ねる。

| 確認項目 | 必須の整合条件 |
| --- | --- |
| 識別 | ホスト自身と参加者が同じfactの型名・難易度区分・深度を使用。白夜／暗月は個別撃破、同時死亡の雑魚には限定抽選なし |
| 抽選 | 各参加者に1回ずつ独立抽選。同じpであって、当否・部位・特性が同一である必要はない。ホストの当選を全員へ配らない |
| 保留・再送・継続 | 解決済みEventIdを共有する通常＋専用報酬の処理単位を維持。ルール選択待ち・再接続・チェックポイント復元で型名やpを変えず、支給を増殖させない |
| 効果 | 同じ6部位・強化・覚醒・星図ならホストと参加者の `Build.Compute` 集計は同じ。装備交換で2/3/6の累積と部位Powerを再計算 |
| 連携 | 自分の対応セット部位数で2/4/6の最高段階を1つ選び、自分の対象報酬装着で成立。減装・解除で再判定し旧余韻を失効。白夜／暗月の部位数は別集計。対象起点・上限はホストが適用 |
| 互換 | マージ順で確定するProtocol版と内容指紋の一致が前提。旧版混在時に「同じ表示なのにホストだけ効果不反映」を正常運用扱いしない |

| 実装段階 | 範囲 |
| --- | --- |
| 段階1 | 基盤拡張3件・Protocol更新・任意保存キー・汎用除外・段階連携の集計／説明表示。Demon／Skollの2セット12部位（記憶連携／エッセンス連携を各1種）を登録し、既存の保存・装備経路へ接続 |
| 段階2 | 残り12セット72部位を追加。白夜／暗月を独立登録、Primus／PolarisはLinkなし。§2.3の同じ予算基準で全14組を較正 |
| 段階間 | 最終IDを最初から使用。コードに未実装の仮定義・無効な完成効果・互換aliasを残さない。内容追加だけの段階2ではProtocolをさらに上げず、指紋で区別 |

既存テストへの影響の見込み（テスト設計は行わない）：`SetsV122Tests` 等の件数48は62へ、セット部位288は372へ増える。`GameRulesTests` のセット部位Power0、`EventSetTests` の全セットが汎用抽選可能という前提と衝突する。段階連携はSetDef側の36定義で、`LinksV126Tests` の単品連携件数357と既存単品定義は不変。`SixPieceBalanceV132Tests` の全セット共通許容帯、`KillClassificationTests` / `PendingRunRewardsTests` / `RunDurabilityTests` のfact・保存、`HostGearValidationTests` / `SixPieceSetTests` の部位Power・セット集計・ホスト再構築が影響範囲。図鑑分母はUnique84点増えるが、土台数・既存ID・装備線形式・セーブ版は維持する。

## 5. 未確定事項（利用者への確認）

1. ドロップ案の通常10%／悪夢15%、夢の深さごと+1ポイント・上限20%を採用するか。均等抽選・重複あり・専用pityなしで、完成期待は基礎通常147撃破／基礎悪夢98撃破／上限73.5撃破となる。
