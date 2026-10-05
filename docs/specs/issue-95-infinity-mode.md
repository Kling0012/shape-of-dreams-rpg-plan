# Issue #95 インフィニティモード：実現性調査・推奨設計

## 1. 結論と調査範囲
- **実現可能と判断する。ただし静的根拠に基づく判断で、Unity実機・協力プレイ・長時間動作の保証ではない。**
- 推奨：選択したゾーンの本体生成グラフを有限サイズで使い、同じゾーンを `TravelToZone(zone, noAdvance:true)` で繰り返し生成する。無限ノード追加、Limbo深度の上限解除は採用しない。
- 「ひとつの世界」はゾーンasset・背景・通常部屋／ボスのプールを固定する意味。地図と部屋配置は世代ごとに変わる。本体のゾーン番号・tier・loopは進めない。
- 読んだ要求：[Issue #95](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues/95)、関連：[ボス限定セット #48](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues/48)。本書は段階1の実装仕様を兼ねる。
- 本体は r.1.4.0.13（`~/dev/sod-gamedata/README.md:3-10`）。以下 **C/** は同ディレクトリの `decompiled/sod-decomp/Dew.Core/`、**N/** は `decompiled/sod-decomp/Dew.Contents/`、**R/** はリポジトリ相対パス。
- 根拠は現在の逆コンパイルソースの行番号。実DLLに `ilspycmd -t ZoneManager` を実行し、同ゾーン再生成分岐・seed更新・BossRush分岐の存在も確認した。DLL再出力とは行番号が異なる。本体本文は転載しない。

## 2. 本体の生成・遷移・終了：根拠
| 対象 | 確認した挙動／設計上の意味 | ファイル:行 |
|---|---|---|
| ゾーン | start/combat/boss/shop/eventのsceneプール、通常／特殊生成、ノード数を持つ | C/Zone.cs:7-39 |
| 世界生成 | seed更新後、有限ノードを生成。Start=0とExitBossを置き、bossRoomsから選ぶ。ボスの部屋数ゲートはない | C/ZoneManager.cs:2048-2079,2377-2419 |
| 部屋選択 | 未割当の通常部屋をcombatRooms、商人をshopRoomsから選ぶ。プールを使い切ると補充する | C/ZoneManager.cs:1342-1366 |
| 同ゾーン更新 | `TravelToZone(Zone,bool)` がLoadNodeへ。noAdvanceはゾーン番号とambientLevelの加算を止めるが、生成・ゾーンイベントは止めない | C/ZoneManager.cs:981-998,1299-1333 |
| 旧部屋の終了 | serialize→StopRoom→部屋変更で破棄するActorをDestroy→新scene読込→保存部屋ならrevisit復元 | C/ZoneManager.cs:1230-1244,1283-1290,1430-1442 |
| 地図の置換 | nodes、visitedNodesSaveData、部屋プールを置換／消去。距離行列はノード数の二乗。modifierServerDataは生成時に追加され、同じ場所では消えない | C/ZoneManager.cs:2523-2561,2841-2844 |
| 通常ゾーン進行 | 実プレイの派生LoadNextZoneからtier／loop／次ゾーン選択へ進む。基底LoadNextZoneは空 | C/PlayGameManager.cs:174-186; C/ZoneManager.cs:3387-3432; C/GameManager.cs:926-928 |
| Rift・投票 | Cmdの実server bodyが隣接・投票・遷移状態を確認。次ゾーン投票完了もGameManager.LoadNextZoneへ | C/ZoneManager.cs:1722-1790,3692-3746 |
| 実クリア | RoomMonstersの全戦闘区画完了からRoom.ClearRoomへ。ClearRoomは一度だけ通知しRiftを開く | C/RoomMonsters.cs:436-489,515-522; C/Room.cs:389-426 |
| 計数の罠 | clearedCombatRoomsはCombatからの退去時加算で、実クリアを確認せず再訪も数える。停止処理もspawnのonFinishを呼ぶ | C/ZoneManager.cs:1125-1136; C/RoomMonsters.cs:563-578 |
| ボス部屋 | Room_BossArenaは位置の補助だけ。boss spawn ruleではwave=1・hunter無し。ボス日程のフックではない | C/Room_BossArena.cs:3-17; C/RoomMonsters.cs:2142-2149 |
| ボス後 | 死亡後に時間表示停止・雑魚掃討・復活、4秒後に魂を出す。魂の強化選択→報酬→Rift解錠→魂破棄は別の非同期処理 | C/BossMonster.cs:98-121,160-199; C/Shrine_BossSoul.cs:108-130,134-201 |
| 特殊終結 | 最終ゾーンではTheDreamへのRiftも生成。Riftの実client bodyは通常／最終ゾーンのUIを分ける | C/RoomRifts.cs:23-98; C/Rift_RoomExit.cs:153-228 |
| 遠征終了 | WrapUpAndShowResultが結果同期と全員ready後のRestartSessionを管理。全員KOのGameOverは別経路で残す | C/GameManager.cs:780-879 |
| 終了種別 | Concededは非勝利。UnknownFateは名前に反して勝利。本体勝利・Limbo進捗を付ける安全帰還として使わない | C/DewGameResult.cs:190-197; C/ResultTypeExtensions.cs:3-5; C/DewSave.cs:383-490 |

## 3. 実現案：Harmony候補と比較
1. **A：有限グラフの同ゾーン再生成（推奨、中～大規模）**。
   - 本体の生成・部屋寿命・scene読込・spawn・Mirror地図同期を残す。通常部屋枯渇時と潜行時だけ同assetを更新する。
   - `Room.OnStartServer` PostfixでserverのonRoomClearを購読（C/Room.cs:238-247）。`ZoneManager.TravelToNode` Prefix／既存 `AddTravelToNodeInterrupt`（C/ZoneManager.cs:925-954）でボス早期入場と選択待ち移動を止める。
   - `PlayGameManager.LoadNextZone` Prefixで初回は固定ゾーンを開始、以後は許可された同ゾーン更新に置換。`GenerateWorldAuto` Postfixで新地図から参照されないmodifierServerDataを掃除する。
   - `Rift_RoomExit.UserCode_TpcInteract__NetworkConnectionToClient` Prefixでモード専用の次部屋／確保UIへ。`RoomRifts.CreateSidetrackRift` PrefixでTheDream等の別世界・終結入口を対象限定で止める。
   - `GenerateWorld_Imp`への既存 `R/src/SodRpg.Mod/DepthRooms.cs:15-45` と生成方針を一本化する。必要なら `GameManager.WrapUpAndShowResult` PrefixもInfinity限定で置き、許可した帰還／GameOverは残し、通常endingへの進入を防ぐ。
2. **B：小さなローリング地図をMODが構築（大規模、非推奨）**。
   - `GenerateWorld_Imp` Prefixで固定数のStart/Combat/ExitBossと距離行列を用意し、`TravelToNode` Prefixでslotを再利用。ロード／RoomMonstersは残す。
   - node/hunter/visited/modifier/投票中index/部屋乱数を同時に世代更新する必要がある。boundedにはできるが、地図・狩り・既存GameModの前提をMODが所有して更新耐性が低い。
3. **C：本体loop／Limboを固定ゾーンへ誘導（小さく見えるが不採用）**。
   - `LoadNextZoneByContentSettings` Prefixで行き先を固定、`GameMod_Limbo`の深度処理へ介入する候補。
   - ゾーン番号を進めれば経済・進捗・tierとの意味がずれ、止めればAと同じ追加管理が必要。Limboは6段階と専用ロビー制約を持ち、部屋周期／任意ボス／確保終了は提供しない。
- Harmony注意：MirrorのCmd/TargetRpc送信wrapperと `UserCode_*` 実行bodyを混同しない。LoadNodeはvoid内のcoroutineなので通常Postfixは完了通知ではない。ready／room-loaded／遷移解除を使い、iterator MoveNextのTranspilerは避ける。

## 4. 既存GameModの流用可否
- **Limbo：不可**。depthを保存・同期するが、通常地図を装飾するだけ。Evil夢数一致、解放条件、diffLimbo、RejoinOnly、勝利時進捗更新がある。上限超過を無限カウンタにしない（C/GameMod_Limbo.cs:69-75,145-175,315-380,431-498）。
- **MirageSkin／CorruptedChaos：補助のみ**。前者は通常敵装飾で固定zoneIndexでは段階が伸びない。後者はExitBossへの経路を前提にするためExitBossを保持し、周期前の入場だけ禁止する（C/GameMod_MirageSkin.cs:71-157; N/GameMod_CorruptedChaos.cs:7-118）。
- **StarlessPath：特殊招待／隠しルート、GoldenLizard：Startでの珍しい敵、DespairFixStuck：限定的な詰まり修正、MorasDomain：関連処理が空**。無限進行の代用品ではない（N/GameMod_StarlessPath.cs:117-135; N/GameMod_GoldenLizard.cs:20-45; N/GameMod_DespairFixStuck.cs:7-41; N/GameMod_MorasDomain.cs:3-27）。
- GameModifierBaseは仕組みの参考にはなるが、新型のresource/prefab登録は別問題。既存managerのcustomDataを使う方が小さい（C/GameModifierBase.cs:1-18; C/GameSettingsManager.cs:636-642）。console bossRushModeも隣接ボス化で目的と逆、保存設定にもならない。

## 5. 推奨案の設定・状態・遷移
- Infinityは通常本体モード上のMOD追加モードとして選び、Limboとは排他。Starlessの自動修飾子の存在だけではロビー開始を拒否せず、Infinity中の招待・sidetrack・別ゾーン遷移を止める。本体の通常難易度と通常設定保存keyは維持し、MODプロフィールの独立した省略可能設定と名前空間付きcustomDataでInfinityを保存・同期する。
- ロビー：既存「夢の深さ」付近に「インフィニティモードON/OFF」「ボス周期10/15/20戦闘部屋」を追加。初期値はOFF・10。OFFは通常遠征。ボス出現のON/OFFは作らない。ホストだけ編集し、参加者は確定値を表示、遠征中は固定。
- 既存UI／権限：R/src/SodRpg.Mod/DreamforgeUi.cs:733-750、ClientSession.RunChoices.cs:39-71。初回生成はRun生成より前なので、ロビー確定値を使うDepthRoomsと同じ順序にする。
- 最初の通常生成ゾーンを固定し、その本来のbossRoomsを使用する。start/combat/bossプールがある通常ゾーンのみ対象。特殊生成・終結ゾーンは対象外。共有assetのプールは書き換えない。
- 状態は `FixedZoneId, Interval, ClearedCombatTotal, ClearsInCycle, GraphEpoch, SegmentEpoch, RoomEpoch, Phase` とRunId・選択revision・確定境界を保存する。Infinity状態の有無がON/OFFを表す。BossDueは周期内の実クリア数から決まる。
- Phaseは `Exploring → BossDue → BossFight → WaitingSoulFinish → AwaitingChoice → Transitioning/Returning`。魂生成後の消滅と実クリアを順に観測し、単なる「魂がない」判定を使わない。
- GraphEpoch＝技術的な地図再生成、SegmentEpoch＝選択と報酬規則の区間、RoomEpoch＝新規部屋の識別。native zoneIndex、WaypointGeneration、AuthorityGenerationの代用にしない。
- 通常クリアはserverのRoom.onRoomClearで、active・非遷移・非revisit・Combat・未計数の当世代nodeだけを加算。MiniBossを含むCombatも1部屋。Start/Merchant/Event/再訪/ボス部屋は周期に数えない。
- モードONではN部屋クリアでBossDueを立て、**次の新規戦闘部屋を固定ゾーンのExitBossにする**。早期入場を止めるだけでは足りない。clientから遠いboss indexをCmd送信せず、ホスト側で世代／Dueを再確認してserver TravelToNodeへ送る。
- 新規Combatの到達候補が枯渇したら、同ゾーンをnoAdvanceで再生成。技術更新では新遠征・確保・イベント抽選・道標更新・ZoneTraveler依頼を発火させず、累計と周期を維持する。再生成中にDueだったら更新後もDueのまま。潜行確定時だけClearsInCycleとBossDueを戻す。
- ボス撃破→魂生成待ち→全員の魂選択／報酬完了→魂消滅・Rift解錠・実クリア→MODの未決撃破／配当精算→確保画面。魂がまだ存在しない4秒間を「完了」と誤認しない。
- **ボス以外の出口・N部屋ごとの確保は作らない。** ボス撃破と魂報酬完了後だけ既存確保画面を出す。確保は帰還して終了、潜行は継続。
- **本体の同ゾーン更新に伴うKO復活・hunter/turn局所リセットを採用する。** 全員全回復ではなく、累計由来の圧は下げない（C/ZoneManager.cs:1307-1327,2372-2376）。Start再抽選やGoldenLizardの反復も段階2の報酬制限に含める。

## 6. 確保画面と終了の意味
- 既存 `Rules.ReachSecurePoint`／AwaitingChoice／GearWindow／契約・出来事・道標・予約品保護を再利用する（R/src/SodRpg.Core/Game/Rules.cs:306-326,339-430）。
- **現行Secureは持ち帰った後も続き、Heatを戻す。Infinityの「確保して帰還」は別の終了理由 `SecuredReturn` が必要。** `Secure→EndRun(true)` は二重確保／勝利報酬、`EndRun(false)` は敗北記録を混ぜる（同:370-430,536-600）。
- 推奨はホストがパーティ全体の「確保して帰還／深く潜る」を決定。荷物、取引予約、出来事、契約は個人のまま。参加者個別退出は採らない。通常モードの個人確保は変更しない。
- 確保：各人の未決撃破・配当・取引の終了条件を満たして既存の確保を1回適用し、CompletedRunIdと帰還結果を耐久保存。踏破XP・勝利／敗北・本体ending解放は付けない。
- その後ホストで `GameManager.WrapUpAndShowResult(Conceded)` を呼び、本体の結果同期／ready／ロビー再開を残す。MODはSecuredReturnを優先して本体Concededを敗北精算しない。本体のConceded履歴・mastery等の精算と「放棄」表示は残るため、表示上はInfinity帰還と区別する。
- 潜行：本体の終結APIを呼ばず、各人の選択確定後に既存Delveを1回適用、鞄を維持、次Segmentへ。同ゾーン再生成の開始前に確定状態を同期・保存する。
- Infinity選択待ちを戦闘／遅着報酬による既存の自動Delveで解除しない（R/src/SodRpg.Mod/ClientSession.RunChoices.cs:153-177）。全員KOは従来のGameOver、切断は帰還・勝利とみなさない。

## 7. 難しさの伸び
- ロビーDreamDepthと本体Limboは変更しない。DreamDepthは最大5で敵倍率だけでなくluck・覚醒・星XP・部屋数にも効く（R/src/SodRpg.Core/Game/DreamDepth.cs:8-17）。これを無限加算すると報酬方針も壊れる。
- 潜行ごとのHeat増加は既存上限5を維持。悪夢確率、非悪夢の底上げ、変種最大1体／部屋を既存処理へ載せる（R/src/SodRpg.Core/Game/Nightmare.cs:92-137; Variants.cs:353-365）。ボス悪夢化はしない。
- 部屋由来の圧段階を `b=min(100, floor(ClearedCombatTotal/Interval))` とし、既存夢の圧の最終HPに `1+0.10b`、攻撃に `1+0.04b` を掛ける提案。周期末から次の敵に適用し、潜行を選べばHeatでも難化する。
- 人数は本体、夢レベル／星は既存平均、深さ／道標／悪夢は既存経路のまま。native zoneIndexやambientLevelを難度カウンタにしない（R/src/SodRpg.Core/Game/DreamPressure.cs:16-51; R/src/SodRpg.Mod/HostAuthority.cs:394-464）。
- 100段で圧倍率はHP11・攻撃5まで。その後も部屋は続くが難化は頭打ち、HUDに表示する。係数・上限は推奨初期値で本体の既存値ではない。全敵同時数やwave数を累計部屋数で増やさない。

## 8. 報酬速度の上限・#48
- 通常の抽選・費用・天井無し・高レア低確率を維持。悪夢／変種の報酬格昇格でも高レアは出る（R/src/SodRpg.Core/Game/Loot.cs:24,384-425）。無限専用のレア保証・救済を追加しない。
- 推奨は**獲得機会の時間予算＋変換後の無料新規出力量予算**。各参加者ごとに保存し、戦闘中の実経過時間だけ補充する。ロビー／pause／loading／再接続／技術更新で補充・初期burst再付与をせず、遠征再開始でも予算をリセットしない。予算は固定個数のscalar、無限の未配当queueは作らない。
- 段階2の開始値：通常20戦闘部屋／35分を比較モデルとし、Infinityの撃破機会は**1部屋につき通常1部屋の50%**、さらに戦闘時間35分につき小型200・通常160・MiniBoss5・実ボス4撃破相当を上限とする。両方の予算が必要。部屋で割り引き、速い周回でも時間予算を超えない。burstは通常1部屋相当、ボス1回まで。昇格は昇格後の枠を消費する。
- **Infinityの撃破による高レア期待値は同条件の通常遠征と同じ時間あたりを上限にする。** 比較モデルは4ゾーン×5部屋、小型10／通常8／エリート25%、末尾ボス4体。35分は実測ではないため、通常遠征の実測で低い供給が判明すれば上限を下げる。現段階ではこの数値による実時間の保証は未検証（R/docs/specs/rare-gear-difficulty.md:92-110）。
- 道標のEpic保証・2倍／3倍・保留解放・素材／星XP／覚醒への変換は後段にある。Loot.RollKillだけの制限は不可。変換後・鞄overflow素材化前で上限を適用し、Hoardは生成時に最終増幅分も予約する（R/src/SodRpg.Core/Game/Waypoints.cs:143-229; Rules.cs:267-275）。
- 全新規無料遺物は仮に戦闘時間1時間あたり24個、Epic以上の保証付き遺物は別枠で同0.25個を上限とする提案。レアを抽選し直すのではなく保証経路の実行前に認可し、最低レア度の保証自体は下げない。初期無料保証枠は付けず、選べない間は理由を表示する。Rare保証だけの経路はこの高レア保証枠で抑えない。
- 欠片・調律石・夢XP・星XP・覚醒にも通常同条件の供給を基準とするrate／burstを持つ。具体値は資料に全経路実測がないため未決。**確保時のHeatボーナス、配当、無料イベント、容量overflowも純増として予算に含める**。抑止分を別素材へ換えて逃がさない。
- 支払済み商人の対価を上限で没収しない。新しい取引・有償イベントに制限を掛けるなら代金消費前に枠を予約する。旧所持品の移動／回収／素材を使う製作は無料供給と分離し、既存資源を使った総取得量まで時間上限で保証するとは言わない。
- native Gold/Dust→商人／欠片換金が抜け道になる。R/src/SodRpg.Mod/HostAuthority.Currency.cs:27-45,95-149とRules.cs:890-927,1030-1047へ、Infinityで増やす通貨と換金機会の予算を接続する。通常の本体収入を含む総供給の数値上限はClaudeが範囲と値を確定する。
- #48はmainに実装済み（Protocol 18、保存形式5）。既存のBossTypeNameとボス限定共通抽選をそのまま使用する。
- 推奨はそのゾーンの本来のbossRoomsを使い、実ボスの安定IDを撃破事実／報酬へ保持して#48の共通抽選に接続。悪夢エリートのBoss相当報酬は限定セット対象外。ボス・セットの任意選択や他ゾーンボスを普通部屋へ直spawnする案は採らない。

## 9. 協力同期・セーブ・記録
- mode／固定ゾーン／間隔／Phase／累計はホスト権威。全員のMOD対応版を開始前に確認し、未導入・非互換の参加者ではInfinity開始／途中参加を認可しない。クライアントの部屋数を信用しない。
- RunChoiceProgressにGraph/Segmentの到着を追加。同じnative zoneIndexの技術更新では通常の確保・道標／出来事再抽選・ZoneTraveler依頼を発火させない。本体zoneIndexは偽装しない。
- mode・NativeZoneIndex・Graph/Segment/RoomEpochをSnapshot／Publisher／Progress／Stream／履歴、PendingRunKill／撃破事実／分類／再送、PressureDividend、codec／cloneへ伝搬。通常はmode OFFと従来ゾーン遷移を維持する。
- 次区間の契約／イベントを変える前に旧区間の撃破を旧規則で精算する。Heat・WaypointだけでなくPact/EventLuck等も現在run値を参照するため、境界を越える保留が必要なら戦闘時のimmutable補正も保持する。
- 保存ブリッジは名前空間付き `GameSettingsManager.customData`（SyncDictionary＋SaveVar）。設定は `dreamforge.infinity.enabled/interval`、実行envelopeは `dreamforge.infinity.runtime`、共有選択は `dreamforge.infinity.choice`。実行状態・選択・停止keyは新規遠征開始時に消し、ロビー設定の混入で通常continueをInfinityへ変更しない。
- native continueはmanager→actor→LoadNodeSettingsの順で復元する。`SerializeGameData` Prefixで更新予定epoch／Phase／次遷移intentをsnapshot前に入れ、復元はApplyGameDataのonFinishとready後に照合する（C/DewPersistence.cs:693-769,824-945）。
- **本体とMODの2ファイルは原子的な一括保存ではない**。本体SaveContinueDataはvoid・例外catch、DewSaveの通常書込は遅延で成功receiptもない（C/GameManager.cs:982-1023; C/DewSave.cs:1471-1581）。SaveProfileContinue(immediate:true)でも原子性の証明にならない。
- RunId／epoch／確定境界／遷移intentを両方に保存。native envelopeを地図の権威、MODの保存済receiptを報酬の権威とする。不一致では新規報酬と進行を止め、既存回復経路で未精算を解く。曖昧な保存から部屋や報酬を推測して作らない。
- 着手時はProfile版5、Protocol18。段階1は保存形式5の省略可能項目追加・Protocol19。段階2も保存形式5の省略可能予算・記録を追加し、報酬制限のない参加者を拒否するためProtocol20へ更新。旧保存はOFF・epoch0・予算0、未知の新形式は拒否する。
- 途中参加は現区間と生存敵を分割同期し、入場前の撃破は新規配布しない。ホスト交代のライブ移行は前提にせず、本体continue＋同ホストプロフィールから復帰する。
- 段階2の記録は、固定ゾーン・周期・選択DreamDepth・本体難易度別に、帰還時最大累計Combat部屋数とその帰還の圧段階、帰還回数、直近帰還の部屋数／圧を保存する。未帰還・敗北・切断の到達は帰還記録を更新しない。Heatは既存RunReportに残す。最高未帰還到達・専用累計ボス／敗北カウンタは今回の記録指標には追加しない。
- ProfileStats／RunReport／Codec／記録UIへ集約値と直近結果だけ追加。部屋ごとの全履歴は保存しない（R/src/SodRpg.Core/Game/Profile.cs:210-247; R/src/SodRpg.Mod/DreamforgeUi.cs:3008-3011）。

## 10. 長時間性能・本体更新への弱さ
- 1地図分のnodes・距離行列・visitedだけを持ち、更新後のmodifier参照を残して旧registryを退役。旧room停止／modifier snapshot保存前の無条件clearはしない（C/RoomModifierBase.cs:321-324; C/RoomModifiers.cs:35-69）。
- WaypointLootRooms、選択history、配当nonce／死亡集合、期限切れ墓標も部屋・区間の確定境界以下を拒否できるようにしてから退役。同zoneでは現行のzone退役が進まない（R/src/SodRpg.Mod/ClientSession.KillSync.cs:278-299; R/src/SodRpg.Core/Game/Mechanisms/PendingPressureDividends.cs:10-46）。
- #55/#73の再利用buffer、保存集約、保存済ACK、未参加区間の切断、32再送RPC／frameを維持。TTL/LRUで正当な未決を捨てない（R/docs/specs/issue-55-perf.md:64-114）。
- 未ACK事実／未決区間は固定の上限を置き、上限到達時は新しい報酬付き部屋へ進まず精算待ち。推奨開始値は2048事実／参加者、旧未決区間2まで。切断peerへ将来の撃破を積まない。固定集合の退役には拒否境界の保存が必要。
- 敵・wave・投射物／DoT・spawn待ちを累計に比例させず、既存room停止と現役効果の寿命を守る。鞄・保管庫・保留Hoardは既存容量、記録はscalar。未決保存を含むプロフィール全体の厳密固定bytesは保証しない。
- 新しい累計／epochは64bit、既存int報酬累計は飽和、revision／modifier IDは安全な世代境界で再基準化する設計が必要。現役／未決参照が残る間は再利用せず、数値限界では破損より明示停止を優先する。
- 更新に特に弱い箇所：Mirror `UserCode_*` 名、LoadNode順序／flag、魂報酬coroutine、特殊boss ending、modifier保存、customData復元、結果種別。公開APIとイベントを優先し、対象signature／前提が一致しない版ではInfinityを選べなくする。通常モードまで壊す包括patchは避ける。

## 11. Claudeが確定すべき選択肢（太字が推奨）
| 判断 | 選択肢・推奨／理由 |
|---|---|
| 同じ世界 | **固定assetの有限地図再生成**／固定slot／本体loop。Aが寿命管理を最も残す |
| モードと出口 | **モードOFF＝通常遠征、ON＝周期ボス必須、魂報酬後のみ確保**。ボス出現スイッチ・ボス以外の出口は作らない |
| 帰還と協力 | **全員共通ホスト選択・SecuredReturn＋native Conceded**／個別退出／通常勝利。個別退出は別設計、勝利は進捗誤付与 |
| 世界・ボスの選択 | **通常ゾーンと本来のボス**／特殊ゾーンも許可／ボス指名。特殊scene・Primusの途中参加禁止とendingは追加調査が必要 |
| 周期・難度 | **10（選択10/15/20）、圧HP+10%／攻撃+4%・100段上限**／別係数／無制限。数値は提案で、有限域の安定を優先 |
| 更新の副作用 | **本体のKO復活・狩り局所リセットを明示して採用**／技術更新時だけ抑止。後者はLoadNode内部介入が増える |
| 報酬上限 | **機会＋変換後無料出力、保証別枠**。部屋予算50%も併用。段階2の確定値・期待値の評価範囲は§15 |
| 上限の評価範囲 | **Infinityの新規無料供給＋増加通貨／換金機会**／旧資産支出込みの総取得。後者は通常の製作・支払契約まで変更する |
| 保存不一致・台帳上限 | **進行停止＋既存回復**／未決破棄／保存から推測。保存耐久性と二重付与防止を優先 |
| 最深の記録 | **設定別の帰還時累計部屋数＋圧段階**／Heatだけ。Heat5頭打ちでは長時間到達を区別できない |

## 12. 実装規模と調査の限界
- 見積もりは**中～大：本体連携・Core状態／報酬・同期／回復・UIを合わせて20～30ファイル、2～4人週程度（コード実装、検証担当の作業を含めない推定）**。単にゾーン移動を止めるpatch数個では完了しない。
- 主なリスクは同ゾーンepochの全経路伝搬、魂と終結の順序、台帳の安全な退役、2系統の保存耐久性、保証道標・通貨を含む報酬上限。#48の既存ID／抽選契約を維持する。
- この環境の本体資料はManaged DLLと逆コンパイルのみで起動プログラムがない（R/docs/specs/issue-55-perf.md:116-118）。実画面・実coop・長時間負荷・scene固有scriptは未確認。テストの設計・追加・提案は本書の範囲外。

## 13. 段階1の確定範囲
- ロビーON/OFF・周期10/15/20の日英表示、ホスト同期、固定通常ゾーン再生成、実クリア累計、必須周期ボス、魂報酬後の既存確保画面と共通選択、圧HP+10%／攻撃+4%・100段、省略可能保存項目、Protocol19、長時間の台帳退役と上限管理。
- 段階1時点では報酬速度は未制限。段階2で機会＋変換後無料出力＋保証別枠、設定別の帰還累計部屋数／圧段階と記録タブを追加した。確定範囲と検証結果は§15。
- 検証は指定Releaseビルドと既存テストのみ。テストの設計・追加は別担当。CHANGELOG変更・本体資料のコピー・pushは行わない。

## 14. 段階1の実装と検証結果
- 本体割り込みは `InfinityMode.cs` に集約：PlayGameManager.LoadNextZone、ZoneManager.GenerateWorldAuto／TravelToNode／TravelToZone、Room.OnStartServer／StartRoom、RoomRifts.CreateSidetrackRift、Rift_RoomExitの実TargetRpc body、GameManager.WrapUpAndShowResult、DewPersistence.SerializeGameData／ApplyGameData、GameMod_StarlessPathの招待処理。ロビー開始条件は `ClientSession.InfinitySettings.cs`。既存DepthRoomsの深さ補正を残し、共有Zone assetは変更しない。
- 部屋数はserverの実Combatクリアを当世代node集合で重複排除。到達可能な新規Combatが尽きたら有限グラフを置換する。RoomEpochは本体の部屋開始前に更新し、敵の撃破／配当にはspawn時のGraph/Segment/Roomを保持する。
- 魂の実生成後の消滅・実クリア・Rift解錠を確認して確保画面へ。共通選択と技術更新は参加中全員の耐久保存ACK後に実行し、帰還はCompletedRunId＋CompletedRunSecuredReturnで二重確保／Conceded敗北精算を防ぐ。
- 確定済みの分類墓標・配当nonce・部屋報酬集合・選択履歴を拒否境界付きで退役。地図modifierは生成後に新地図から参照されないものだけ除去。未ACK撃破2048件（全体上限、参加者別より厳しい）または2世代前の未精算で次部屋への進行を停止する。既存32再送RPC／frame、buffer再利用、敵／wave数を増やさない方針を維持する。最後の部屋内で発生した未精算は捨てず、プロフィール全体の固定bytesは保証しない。
- 本体continueとMODプロフィールのRunId・seed・ゾーン・世代・クリア数・phase・選択receiptが一致しなければ進行と新規報酬を停止する。既存のcontinue再読込と一致するホストプロフィール選択で回復する。一致する保存が失われた場合は推測で修復しない。保存済み参加者が古い区間／地図へ戻り、必要な共有選択receiptがない場合も明示停止し、潜行を推測して再生しない。
- 指定Releaseビルド：成功、エラー0・警告5。出力先 `/tmp/sod-deploy-i95`。
- 指定既存テストコマンド：終了コード1。`SodRpg.Mod.Startup.Tests` は5件成功。`Issue73.Native.Tests` は11件、`SodRpg.Core.Tests` は8件のコンパイルエラーでテスト実行に到達せず。既存の部分ソース取込／本体代替APIに新しいInfinity部分クラスとMonsterRuntimeの世代項目が含まれないため、InfinityMode、TickInfinity、TryInfinitySecure／TryInfinityDelve、GraphEpoch／SegmentEpoch／RoomEpochが未定義。テストの作成・変更・追加は行っていない。
- 本体起動プログラムがないため、実画面・実coop・実continue・長時間の負荷とscene固有挙動は未確認。ビルド成功は本体内での動作保証ではない。

## 15. 段階2の実装・上限・検証
- `InfinityRewards` と保存済み `Profile.InfinityRewardBudget` の固定数scalarで制限する。初期時間creditはすべて0、遠征・地図・再接続でリセットしない。部屋入場の重複はRunId／Graph／RoomEpochで拒否し、実Combatの新規入場だけで小型5・通常4・MiniBoss0.125・実ボス0.1の部屋creditを補充する（通常モデルの50%）。時間creditは35戦闘分につき200／160／5／4、保有上限10／8／1／1。昇格は昇格後の枠を使い、部屋と時間の両方が必要。
- `ClientSession.InfinityRewards.cs` は本体の同期済み `elapsedGameTime` を観測する。接続・ホスト確認済み、実Combat／ExitBossの未クリア部屋、ローカルHeroの戦闘中・非KO、非pause・非transition・非技術更新・非選択待ちだけを計上する。ロビー・切断・ロード・再接続の時刻差を補充しない。0.25秒ごとと撃破前に補充し、遅延frameの差分は最大1秒に切り下げる。部屋の再訪やボス部屋では部屋creditを追加しない。
- 抽選前に、その撃破の現Heat・深さ・契約・出来事・道標・昇格・複製・宝庫増幅・登録ボス限定セット確率を含む **Epic以上とLegendaryの期待値を独立して予約**する。抽選後のレア度別没収・再抽選は行わない。補充の基準は全設定共通のDreamDepth0・Heat0・通常20Combat＋4ボス／35戦闘分、限定セット寄与なし。`Loot` の実重み・TierLuck・DropChance・ボス追加抽選率を共有し、別の抽選表を持たない。補充値は戦闘時間1時間につきEpic以上 **0.586805個**、Legendary **0.052875個**。高深度で貯めたcreditを低深度へ持ち越して上限を引き上げない。各期待値creditの保有上限7、初期0。
- 無料新規出力のrate／保有上限：

  | 出力・機会 | 1戦闘時間の補充 | 保有上限 |
  |---|---:|---:|
  | 新規無料遺物 | 24個 | 24 |
  | Epic以上保証の実行機会 | 0.25回 | 1 |
  | Epic以上保証の最終出力予約 | 0.25個 | 2 |
  | 欠片 | 180 | 30 |
  | 調律石 | 6 | 3 |
  | 夢XP | 1200 | 50 |
  | 星XP | 780 | 20 |
  | 装備中遺物の覚醒ポイント合計 | 780 | 20 |
  | native Dust→欠片の束 | 6 | 1 |
  | MOD商人の購入試行 | 6 | 1 |

- 道標変換後に出力を予約し、宝庫は生成時に最終×3を予約、解放時に二重計上しない。新規無料荷物の容量overflow・確保時Heatボーナス・配当・依頼・無料イベントの純増を含め、抑止分を別素材に振り替えない。保管／回収後の既存資産の移動・任意分解・素材製作・有償イベント・支払い済みの対価・有限一度限りの偉業報酬は別扱いで、支出込み総取得の数値上限は主張しない。
- Epic以上保証は生成前に機会と最終出力の両方を予約し、最低レア度は下げない。単発保証は初回4戦闘時間、ボスの最大2出力を保証するBossTributeは8戦闘時間分が必要。空振りや最大数未満の生成でも予約は戻さず保守的に扱う。選択不可理由を日英で表示する。Rare保証だけのFirstClaimはEpic保証枠を使わない。
- native追加通貨は、参加者別の拾得RPC・永続台帳を増やさず **MOD追加Gold倍率／エリート追加Gold／追加Dustの上限を0** とした。本体通常収入は変更しない。MOD商人購入・Dust換金は代金消費前に各人の保存済み枠を予約し、換金では最終欠片10／束も同時予約する。失敗・結果不明でも枠は返さず、支払い済みの商品や欠片を後から没収しない。本体の通貨・本体商店の総経済までの絶対上限は対象外。通常モードの通貨効果・取引は従来どおり。
- #48の実ボス限定セットは **保証ではなく無料撃破報酬の上限に含める**。登録 `BossTypeName` と既存 `BossSets.RollDrop` を使用し、認可後の確率（通常10%、本体悪夢＋5%、選択深さ＋1%／段、最大20%）・6部位の共通抽選・重複許可は変更しない。悪夢エリートのBoss相当昇格は限定セット対象外。新規無料遺物の最終枠も必要で、短い新規プロフィールの比較では機会不足で抽選されない場合がある。
- 記録は `InfinityRecords` の設定別集約（最大1024設定）と省略可能codecで保存する。`Rules.SecuredReturn` の耐久帰還処理で確保／Run削除より前に更新する。記録タブは日英のゾーン・本体難易度・周期・選択深さ、最大帰還部屋数と対応する圧、帰還回数と直近結果を表示する。旧保存の未記録難易度を推測しない。行文字列はプロフィール／言語／記録revisionが変わったときだけ作る。
- PC要件を上げる敵・wave・履歴queue・新規通貨RPCは追加しない。予算は固定scalar、記録は設定別の有限集約、通常の地図／台帳退役を維持する。実本体のFPS・長時間負荷の測定結果ではない。
- 指定Releaseビルドは成功、エラー0・警告5、配備先 `/tmp/sod-deploy-i95`。既存テストの代替API／取込不備は別担当の範囲として変更・再実行せず、テストの作成・設計・追加は行っていない。
- BalanceSimの実Core経路を2000人・seed95で実行し、通常の深さ0／5とInfinityの全10／15／20周期×30／60／120分、通常速度・4倍速・悪夢昇格・宝庫を比較した。全比較の抽選Epic以上／Legendaryの予約期待値は上記補充値以下。300分の保証認可ケースでは初回4時間後に各人1回の予約、実際の遺物生成は別集計。帰還→clone→codec往復で帰還部屋10・圧1・帰還回数1・終了済み・Runなし・codec注記なしを観測した。結果は [`tools/BalanceSim/result-infinity.md`](../../tools/BalanceSim/result-infinity.md)。
- 135分の戦闘creditを実APIで蓄積した認可済み実ボスの分離観測では、2000人の2000機会が認可され、限定セット200個（10%）を観測した。このケースもEpic以上予約0.074913／時・Legendary予約0.047579／時で基準以下。通常速度の30／60／120分ケースで限定セット0だったのは初期0と期待値予算の認可不足によるもので、抽選経路を削除・確率を0にした結果ではない。
- この環境の.NET runtimeは10.0.11のみのため、net8.0シミュレーターは `DOTNET_ROLL_FORWARD=Major` で実行した。通常20部屋／35分は実測ではない。上限は蓄積した戦闘時間に対する累積期待値／出力のrateと保有burstであり、持越しを含む任意の短時間窓の厳密上限ではない。実測の通常供給が低ければ基準の引下げが必要。本体起動プログラムがないため実画面・実coop・実continue・長時間負荷は未確認。

## 16. 最新main・#97の統合
- v2.1.1の警告のみのpreflight、クラス単位のパッチ適用・巻き戻し、`SafeReflection`を維持する。#48の14ボスセット、Polaris強化、装備アイコン、#97/#99の変更はmain側の意図を残したmergeで取り込んだ。通信はProtocol20、保存形式5の省略可能項目を維持する。
- 作業中に追加されたmainの#104も再mergeした。ロビーの乱数使用変更を残す際のRNG状態保持と遺物Uid重複回避を維持し、Infinityのチェックポイント復元へ統合した。上流の既存テスト補助関数も実際のnative完了callbackへ追従させた。
- #97の非再帰チェックポイントは、Infinityの固定ゾーン・難易度・周期・累計／区間クリア数・世代・phase・選択receiptと、プロフィールの報酬予算・入場重複排除・帰還記録を保存する。復元は同じProfile参照へ所有済み状態を移し、帰還receiptと記録表示のrevisionも戻す。次回ロビー設定は現在の選択を残し、再開するrunの周期を変えない。
- native保存前にクリア状態と未反映の戦闘creditを同期する。native完了callbackと地図ready後に対応チェックポイントを適用・照合し、同RunIdでもInfinity初期化・ミラー・クリア観測・ACK・予算時刻の一時状態を捨てる。ロード前の最新Profileとの誤照合や、ロード中の時刻差による補充を避ける。
- Infinityの13パッチ（ロビー開始条件を含む）の対象・実適用を公開Harmony APIで確認し、欠落・適用失敗・実行例外ではInfinityだけをプロセス中無効にする。既に入ったフックも無効時は本体処理へ戻す。MOD全体の停止やゲーム全体のpauseは行わず、保存済みInfinityデータは保持する。
- 無効時はロビーONを拒否し、OFFへの切替と通常モードは残す。HelloのInfinity可否は通常のProtocol／内容照合と分離し、対応が欠ける参加者とのInfinity開始だけを拒否する。ホストの無効化は参加者のInfinity割り込み・新規報酬にも反映する。
- 最終Releaseビルドは成功（エラー0・警告5）、配備先 `/tmp/sod-deploy-i95`。指定 `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=Major python tools/test_changed.py --all` は追加mainも含めて完走・終了コード0：Core 3237成功／既存2skip、Native 31成功、Startup 5成功、計3273成功・失敗0。main側の既存ケースを保持し、こちらでは部分クラス代役・反射呼出し・旧保存fixture補助関数だけを追従した。テストケース本体・期待値の変更や新規ケース作成は行っていない。残存コンパイル不備なし。
- 既存BalanceSimを `--mode infinity --players 1 --seed 95` で実行し、通常／Infinity経済経路と合法帰還・clone・codec往復が完走した。帰還部屋10・圧1・帰還回数1・終了済み・Runなし・codec注記なしを観測し、既存2000人の結果ファイルは上書きしていない。本体起動プログラムがないため、実画面・実coop・実本体continueとInfinity native割り込み失敗時の実ゲーム挙動は未確認。
