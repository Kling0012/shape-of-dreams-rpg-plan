# Infinity 帰還前の依頼進捗の確定

対象は #1026。確保帰還の前にシールド・味方回復・属性撃破の進捗を適用し、最後の1件で達成する依頼の統計と報酬を精算に含める。

## 確定の順序

`ClientSession.TickInfinity` は `Rules.SecuredReturn` の前に `SettleInfinityBounties` を呼ぶ。ホスト自身の集計報告は通常の送信に加えて同期適用する。集計報告には通番を付け、後から届くループバックを読み捨てる。これにより、定期フラッシュで送信済みだが未受信の報告も帰還前に適用される。

参加者は帰還の選択を受け取ると、遠征ID・旅人のネットワークID・選択の改訂番号・グラフと区間の世代を付けて確定を要求する。ホストは対象の未送信分をフラッシュし、同じ Actor の reliable ordered TargetRpc チャネルで確定応答を送る。参加者は報告を適用した後にこの応答を受け取り、受領確認を返してから自分の精算を進める。

ホストが受領確認を待つのは、その選択の `PersonalChoices` に含まれ、現在 hero を持つ人間の参加者だけとする。待機時間は既存の選択ACKと同じく `PersonalAckGraceSeconds` の範囲で最大30秒とし、`PersonalTimedOut` の場合は追加で待たない。期限を過ぎた相手には、ホストの遠征状態が残っているうちに未送信分をフラッシュし、その後に確定応答を送る。受領未確認で待機を打ち切ることをログに残して、ホストの精算を進める。参加者自身は確定応答を受け取るまで精算しない。

確定応答を出した参加者のシールド・回復・属性撃破は、その遠征では以後積まない。確定状態は装備変更によるランタイムの作り直しを越えて保持する。通常の決着・保存・再起動・Primus 結末のフラッシュ対象と1秒間隔は変えない。

## 消費と拒否

集計カウンタは送信とホスト自身への適用が終わった後に消費する。個別報告へフォールバックする場合は、送信を終えた件数だけ消費する。通信が例外で失敗したときに `finally` で未送信分を消さない。

`OnBountyCounts` の遠征中判定と遠征ID・旅人IDの一致判定は維持する。確定応答と受領確認にも遠征ID・旅人ID・選択番号・世代の一致を要求する。遠征終了後や別の遠征の報告を受け付ける範囲は広げない。

## 互換性

保存形式は変えない。帰還時の確定要求・応答を通信に加えたため、協力プレイでは全員が同じ版に更新する。通常の集計報告は通番のない従来形式も受け付ける。

## 要件と対応箇所

2026-10-21 の修正ラウンドで FR-1〜FR-12 を既存実装と照合した。前の完了報告では `requirements_done` が欠けていたため、対応先を各 ID の `where` に記載する。今回の追加変更は対応記録のみで、実行時コードとテストは変更していない。

| 要件 | 対応箇所と動作 |
|---|---|
| FR-1 | `src/SodRpg.Mod/ClientSession.Infinity.cs` の `TickInfinity` は `SettleInfinityBounties` が成功するまで `Rules.SecuredReturn` と `ActiveRunId` の消去を行わない。 |
| FR-2 | `src/SodRpg.Mod/BountyReportBatching.cs` の `SettleInfinityBounties` は、ホスト自身には同期適用を要求し、参加者には報告に続く確定応答の受信を要求する。 |
| FR-3 | `tests/Issue73.Native.Tests/Issue1026SecureBountyLossTests.cs` は `_nextBountyFlush` を未来に固定し、最後の1件を保留したまま3フレーム進めて実際の `TickInfinity` を呼ぶ。達成数・最終報告・確保済み報酬を先に受信した場合と比較する。 |
| FR-4 | `src/SodRpg.Mod/BountyReportBatching.cs` の `ReceiveLocalBountyCounts` がホストのループバックに先行して適用する。参加者の `OnBountyReturn` と `SettleInfinityBounties` は、遅れて届く報告の後の確定応答を待つ。 |
| FR-5 | `src/SodRpg.Mod/BountyReportBatching.cs` の `QueueBountyReport` は同じ遠征のシールド・回復・属性撃破をフレーム間で蓄積し、`FlushBountyReports` が全件を送る。 |
| FR-6 | `src/SodRpg.Mod/BountyReportBatching.cs` は送信後にカウンタを消費し、`OnBountyCounts` が適用済みの通番を拒否する。`src/SodRpg.Mod/NetMessages.cs` が通番を保持する。 |
| FR-7 | `src/SodRpg.Mod/BountyReportBatching.cs` の `BountyReportsBeforeBoundary` は `WrapUp`・`SerializeGameData`・`RestartSession`・`StartPrimusDeath` を引き続き対象とし、定期フラッシュも1秒間隔を保つ。 |
| FR-8 | `src/SodRpg.Mod/BountyReportBatching.cs` の `OnBountyCounts` は `RunActive`・遠征ID・旅人IDの判定を保つ。確定要求と応答にも遠征・旅人・選択・世代の一致を要求する。 |
| FR-9 | `src/SodRpg.Mod/BountyReportBatching.cs` は既存の `Rules.OnSupportApplied`・`Rules.OnElementalKill` を呼ぶ。`src/SodRpg.Core/Game/Rules.cs` の `CompleteBounty` が統計・報酬を更新した後、確保精算が最終報告と持ち帰る報酬に反映する。 |
| FR-10 | `src/SodRpg.Mod/ClientSession.Infinity.cs` と `src/SodRpg.Mod/BountyReportBatching.cs` の精算前判定・同期適用・確定応答により、各参加者の適用完了をその参加者の精算より先にする。 |
| FR-11 | `tests/Issue73.Native.Tests/Issue1026SecureBountyLossTests.cs` が実際の Infinity 確保経路で、最後の1件による3依頼の達成数・報告・欠片・調律石を比較する。`tests/Issue73.Native.Tests/NativeBoundary.cs` は帰還完了の呼び出しを数える。 |
| FR-12 | `src/SodRpg.Mod/BountyReportBatching.cs` と `src/SodRpg.Mod/NetMessages.cs` の蓄積・同期適用・確定応答・カウンタ消費・通番で対応する。既存の再現テストはソロのループバックと後の定期／境界フラッシュを通す。 |

## 今回の検証

- 既存の #1026 再現テストは1件成功した。精算前の `SettleInfinityBounties` 呼び出しだけを一時的に外すと1件失敗し、復元後は同じテストを含む Native 関連49件が成功した。
- Core の依頼・Infinity 関連137件、Startup の選択ACK・個人選択の期限／切断に関する8件が成功した。
- ゲーム本体 DLL を参照した Release ビルドが成功した。エラー0件・警告6件。
- ゲーム側の `Actor.CustomRpc_SendMessageToClient` と `TpcHandleRpc_Imp` を点検し、報告と確定応答が同じ Actor の TargetRpc チャネル0を使うことを確認した。ホストの受領待ちが期限切れになっても、参加者自身の精算は確定応答を受け取るまで進まない。

既存の再現テストは3種類のカウンタを積んでから複数フレーム進めるもので、各フレームで新たに積むケースや参加者の遅延受信を直接実行するものではない。これらと受領待ちの期限切れ分岐は、対応する既存テストがなく、新規テストを作らない指定のためコード点検で確認した。実ゲーム・実通信での確認とフル／遅いテストは未実行。前者はゲームを起動できないため、後者は後続の検証工程で実行する指定のためである。
