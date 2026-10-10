# 協力プレイ通信の棚卸し（2026-10-10）

対象: `src/SodRpg.Mod`、基準 commit `2ef0411`。送信行は `CustomRpc_SendMessageTo*`、`NetworkClient.Send`、`connectionToClient.Send`、本体 Cmd 呼び出し、反射で呼ぶ TargetRpc、同期辞書への書き込みを検索した。

本体 `Actor.CustomRpc_*` は `DewPersistence.ToJson` → 既存の Mirror Cmd/ClientRpc/TargetRpc（reliable ordered channel 0）。型名の文字列で照合する。以下のサイズは **受信者1人あたりのJSON＋型名＋RPC包絡のおおよそのバイト数**（UTF-8、ASCII ID想定）。TCP/KCP/Steam、Mirror batch の共通ヘッダーや再送は含まない。JSONの数値桁数・ID長・ビルド内容で変わる。実機の通信量測定ではない。

## 原因と変更

- **確定済み選択履歴**: 現在の選択の5秒再送に付随して、過去の全ゾーンの履歴を全員へ再送していた。人数・遠征の長さに比例して増える。受信者ごとに送信済み文字列を比較し、新規履歴と途中参加分だけ送る。30秒の回復再送は残す。RPC Actor・publisher・run の変更で比較状態をリセットし、終了・勝利・ロビー復帰状態の変更も即時に再送する。
- **ボス表示**: 全状態を毎秒送信。盾の中心は毎フレーム更新され、クライアントはすでに本体 target の位置に追従している。対応済み相手には全状態の epoch/revision/count が同じなら送信せず、5秒の回復再送を残す。盾の移動表示だけ0.1秒にまとめる。新規・削除・期限や形状・報酬カウンタの変更は即時。
- **鍛錬**: 共通 ledger revision が変わると、成長していない他人にも通知していた。本人のスタックを固定256要素の配列で比較（範囲外は従来送信へ退避）。キャラ・owner・Actor・run・Build変更は送る。未変更時の再送は5秒→30秒。
- **BuildとApplied**: 同一装備を30秒ごとに送り、ホストから同じ要約が返っていた。送信内容・Actor・hero を比較し、確認済みの未変更Buildは120秒の回復再送だけにする。未確認・authority要求・変更時は即時。
- **Infinity個人選択ACK**: 全員へ送るが受信処理は本人の playerId だけを使う。本人だけへTargetRpcで返す。
- **既存で抑制済み**: 敵分類と予告は5秒の走査でも変わらなければ送らない。撃破 replay はdurable ACKで必要なfactだけ送り、毎フレーム32通の共有予算を持つ。ボスbeam/カウンタには既存0.1秒集約あり。
- **即時維持**: 撃破facts、元素撃破・回復・盾・記憶使用の依頼進行、取引、配当、中断保存barrierは集約しない。これらの前後関係は保存と報酬精算に使われる。

設定 `netSkipUnchanged` / `netCoalesce` / `netOwnerOnly` / `netCompact` はそれぞれ独立、既定true。`netCoalesce` は既存beam/カウンタ集約にも適用する。比較・0.1秒待機にnew/LINQ/文字列生成/クロージャを追加していない。ボス/鍛錬のJSON生成は実送信時だけ。Buildは従来の期限でencodeして比較する。本体JSON RPCの割り当ては残る。追加の配列はセッション開始時に確保（ボス16 owner×64 effect上限、64bit環境で概ね0.5MB未満）。

## 全メッセージと送信経路

`H`=ホスト、`C`=参加者、`本人`=所有者。全員は本体RPCの対象（host localも含む）。表の型名はすべて `Dreamforge` 接頭辞。

| メッセージ | いつ・頻度（変更前 → 変更後） | 目安B/人 | 誰に | 送信箇所（src/SodRpg.Mod） |
|---|---|---:|---|---|
| BuildMsg | 装備/成長変更時即時、未確認5秒、確認後30秒 → 未変更確認後120秒。分割転送 | 150＋Build長（通常数KB、1部約10.9K文字まで（JSONのescapeを考慮して上限を制限）） | C→H（host localも同経路） | ClientSession.cs: SendBuildIfNeeded |
| AppliedMsg | Build適用/再送への返事、必要時分割 | 160＋要約長（数KB） | H→本人 | HostAuthority.cs: SendApplied |
| HelloMsg | Cが5秒、Continue受領前に即時。Hは都度返事 | 350〜650 | C↔Hの本人 | ClientSession.Hello.cs: TickHello/SendContinueReceiptBeforeBuild; HostAuthority.Hello.cs: OnHello |
| BuildInputCapabilityMsg | ExtraPoints時5秒probe → Hello返事後のBuild送信確認時にprobe（最短5秒、確認後は通常30秒）、NetLite確認後は停止。Hは都度返事 | 80〜90 | C↔Hの本人 | BuildInputCapability.cs: ProbeBuildInputCapability; HostAuthority.Hello.cs: OnBuildInputCapability |
| PressureMsg | 圧/人数変更時＋5秒ごと | 140＋runChoices長（約300〜数KB） | H→全員 | HostAuthority.cs: RefreshPressure/StageClassificationResync/SendPressure |
| RunChoicesMsg（現在） | 選択変更/ゾーン確定/終了/ロビー復帰時＋5秒 | 140＋choices長（約200〜数KB） | H→全員 | ClientSession.RunChoices.cs: PublishRunChoices/PublishRunChoicesForZone; ClientSession.LobbyReturn.cs: OnRestartSession |
| RunChoicesMsg（履歴） | 上記5秒再送の度に履歴全件 → 未送信履歴/新規参加者＋30秒回復再送 | 同上×履歴件数 | H→各本人（全員に必要な共有履歴） | ClientSession.RunReplay.cs: PublishRunChoiceHistory/PublishChoiceHistoryTo |
| BountyReportMsg | 元素撃破ごと（参加者それぞれ）、有効回復/盾ごと、記憶使用ごと。リンク増加＋5秒。仕掛けは1tick合計 | 120〜170 | H→本人 | HostAuthority.cs: SendBountyReport/ReportElementalDeath/FlushGimmickReports; HostAuthority.MpFixes.csも呼出し |
| NightmareMsg | 分類変化/除去時、5秒走査は既存差分比較。途中参加は対象へcatch-up | 110〜150 | H→全員/新参加者 | HostAuthority.KillSync.cs: SendMonsterClassification/FlushMonsterSync |
| VariantMsg | 同上（変種ID文字列） | 120〜180 | H→全員/新参加者 | HostAuthority.KillSync.cs: SendMonsterClassification/FlushMonsterSync |
| MonsterCueMsg | 敵の予告状態が変化した時、途中参加、フレーム予算内 | 100〜140 | H→全員/新参加者 | HostAuthority.Monsters.cs: SendMonsterBehaviorCue; HostAuthority.KillSync.cs: FlushMonsterSync |
| CurseMsg | 契約選択/再適用時（序数で重複適用防止） | 55〜75 | C→H | ClientSession.cs: Delve/TickCurseResync; ClientSession.InfinityPersonalChoices.cs: SendInfinityPactCurse |
| CurseClearMsg | 確保/契約終了時 | 45〜60 | C→H | ClientSession.cs: SendCurseClear |
| TradeMsg | 取引準備保存後、未応答台帳照会2秒、結果不明取引のretry | 130〜190 | C→H | ClientSession.cs: SendLedgerProbe/SendDueTradeQueries/SendPreparedTrade |
| TradeResultMsg | 上記要求ごと | 80〜180 | H→本人 | HostAuthority.Trades.cs: OnTrade |
| OverflowBonusMsg | 変更時、未払い2秒/idle5秒 | 130〜180 | C→H（hostは直接呼出し） | ClientSession.OverflowBonus.cs: TickOverflowBonus |
| OverflowBonusResultMsg | 上記への返事 | 130〜180 | H→本人（hostは直接） | HostAuthority.OverflowBonus.cs: ReplyOverflowBonus |
| DreamEventStartedMsg | 個人の夢イベント開始、選択待ち中retry1秒 | 110〜160 | C→H | ClientSession.NewPowers.cs: NotifyPersonalDreamEvent |
| GemSlotConflictMsg | Build反映時に旧警告を解除 | 90〜110 | H→本人 | HostAuthority.GemSlotConflict.cs: ClearGemSlotConflict |
| RunGrowthMsg | stack変更時最大2Hz＋未変更5秒 → 本人値比較＋未変更30秒 | 130＋stacks長（約180〜数KB） | H→本人 | HostAuthority.RunGrowth.cs: SendRunGrowth |
| BossEffectsMsg | 新規/削除/重要変化即時、beam/カウンタ最大10Hz、全状態1Hz → 旧版は従来形式。対応済み相手の未変更全状態は5秒 | 約350＋270×effect数（実際は数値桁数で増える） | H→各人（全員が見る演出） | HostAuthority.BossVisuals.cs: SendBossVisualMessage→反射でActor.TpcHandleRpc_Imp |
| BossLiteMsg（追加） | NetLite確認済みだけ。上記と同じ意味、盾の中心最大10Hz | 約250＋177×effect数 | H→確認済み各人 | 同上、既存TargetRpc内JSON（新Mirror IDなし） |
| MonsterKillMsg | 本体撃破ごとに全員＋未受領facts replay/欠落照会への返事 | 約550〜850 | H→全員/必要な本人 | HostAuthority.KillSync.cs: CaptureAuthoritativeRunKill/SendNextKillReplay |
| KillReplayStartMsg | stream開始/scene変更/clear、未応答5秒retry | 180〜250 | H→本人/clear時全員 | HostAuthority.KillSync.cs: SendKillStreamControl/ClearRemoteMonsterState |
| KillReceiptMsg | durable ACK・欠落照会5秒、最大32欠落 | 260＋receipt約90/個＋missing約130/個 | C→H | ClientSession.KillSync.cs: TickKillSync |
| ContinueCheckpointMsg | 本体中断保存のbarrierとディスクcommit確認時 | 150〜200 | H→全員 | ClientSession.Continue.cs: CaptureNativeContinue/ConfirmNativeContinue |
| PressureDividendMsg | 認可された撃破で抽選成功した時1回 | 300〜400 | H→本人 | HostAuthority.PressureDividend.cs: AdmitPressureDividendNativeKill |
| InfinityAckMsg | choice保存受領、未確認retry（ClientSession.Infinity.csで1秒） | 130〜180 | C→H（hostは直接記録） | InfinityMode.cs: AcknowledgeLocal |
| InfinityPersonalChoiceMsg | 個人選択確定後ACKまで1秒 | 250〜400 | C→H（hostは直接） | InfinityMode.PersonalChoices.cs: SubmitPersonalChoice |
| InfinityPersonalChoiceAckMsg | 個人選択受領ごと | 180〜280 | H→全員 → 本人 | InfinityMode.PersonalChoices.cs: ReceivePersonalChoice |
| CoopTradeUp / CoopTradeCommand | capability新鮮時、probe/recovery5秒、取引操作時、profile分割 | Mirror string包絡4B＋JSON約0.2〜60KB | C→H | ClientSession.CoopTrade.cs: SendCoop→NetworkClient.Send |
| CoopTradeDown / CoopTradeState | 取引状態更新/要求返事/profile分割 | 同上 | H→本人 | HostAuthority.CoopTrade.cs: SendCoopState→connectionToClient.Send |
| 本体ZoneManager.CmdTravelToNode | dev consoleによる移動1回 | 約15〜25 | C→H | DreamforgeMod.cs: TravelNextCommand（本体Cmd） |

`BossEffect` / `BossLiteEffect` は演出packet内要素、`KillStreamReceipt` / `MissingKillFact` は受領packet内要素で、単独では送信しない。MOD自身に `NetworkServer.SendToAll`、新しい `[ClientRpc]/[Command]/[TargetRpc]` 定義はない。

同期辞書も本体Mirror経由で送信される：Infinity enabled/intervalは設定変更時（ClientSession.InfinitySettings.cs）、Infinity runtime envelopeは生成・移動・保存/状態更新時（InfinityMode.WriteEnvelope）、choiceは選択時（PublishChoice）、個人待機は1秒（PersonalChoices.PublishPersonalWait、約200〜300B）、個人道標受領は確定時（PersonalWaypoints）、boss planは生成時（InfinityBosses、数百B）、協力取引capabilityは2秒ごと（HostAuthority.CoopTrade、GUID＋key約60B）。PressureEnemyCountはspawn/補正時に1数値（辞書key込み約40B）、NativeDreamContentは生成時に種ID・進行変化時にprogressをpersistentSyncedDataへ書く。通常の敵HP/位置/本体能力の同期はゲーム側の責任で今回変更しない。

## 旧版互換と失敗時

Helloの型・欄・Protocol 24は変更なし。既存BuildInputCapabilityMsgの任意JSON欄 `netLite=1` で機能確認する。旧版で欄が無い場合は0。クライアントは小さい演出の受信handler登録成功後だけ1を送り、ホストはその接続ActorでHelloを受けていて明示1の時だけ使う。Actor変更・退出で確認を消す。対応未確認・旧版相手はBossEffectsMsgの従来JSONのまま。

新型は既存本体TargetRpc内の型名とJSONとして運び、Mirror NetworkMessage型/IDを追加しない。compact encode/sendが失敗したらそのホストのcompactボス通知だけを無効にして警告1回、同じ内容を従来JSONで再送する。受信handler登録の失敗も警告1回で従来表示を維持する。MOD全体・戦闘判定を停止する条件は追加しない。

## 効果の概算

短いJSONをPython標準jsonで同一値から比較（ASCII 32文字run ID、64文字content、少ない小数桁の例）。本体JSON設定も空白無し。実際のUnity Vector3には派生propertyが入る場合があるため、その分はここで数えず控えめな概算。

| effects数 | 従来JSON B | 小さいJSON B | JSON減少 |
|---:|---:|---:|---:|
| 0 | 310 | 211 | 31.9% |
| 1 | 579 | 387 | 33.2% |
| 8 | 2469 | 1626 | 34.1% |
| 64 | 17589 | 11538 | 34.4% |

RPC包絡の概算を足すと1要素 **618→423B**、8要素 **2508→1662B**。float/doubleや座標精度の損失なし。文字列IDの番号化はregistry差の扱いを広げるため今回導入していない。

20分、リモート3人、5ゾーン、Build 4.5KB＋Applied 2KB、鍛錬180B、履歴350B/件、履歴平均2件、値の変更なし、と仮定すると、対象の定期送信部分は **1,413,600→300,600B（78.7%減）**。Build/Appliedが30→120秒、鍛錬と履歴が5→30秒。初回参加、変更、撃破、取引、圧、現在の選択、ゲーム本体の通信は含まない。

同じ1つの盾表示を60fpsで120秒動かす例では、毎フレーム7200通→最大10Hz＋1Hz全状態で約1320通/人。上記1要素packetなら **4,449,600→558,360B（約87.5%減）**。同じ期限/形状の移動だけの例で、実戦の効果数や重要変化で変わる。現行のbeam10Hzをさらに下げた数値ではない。

## 検証と実機で残る確認

指定Releaseビルドは成功（0エラー、既存警告5件）。`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all` は3032成功・0失敗・既存skip5件（Native 124、Core 2765、Culinary 20、Startup 123）。`git diff --check` は問題なし。既存テストのAPI doublesとCompileリンクのみ更新し、新しいテストは追加していない。履歴の新しい送信処理はproductionビルドで確認したが、既存Native harnessはこのメソッドをstubに置き換えるため、その配送動作は既存テストの直接対象ではない。棚卸しと数値は静的調査/概算で、socket転送量の実測ではない。

実機では新旧版混在・途中参加・scene/Continue再開後、盾追従や短時間予告の見え方、保存前後の報酬、Unity JSON codec、reliable配送時の実通信量を確認する必要がある。全体の通信削減率は本体の位置/HP等も含めた計測が必要。
