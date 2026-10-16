# Issue #443 取引の双方確認後の停止：再現調査

この文書は元 PR #450 の再現工程で記録された結果を保持したもの。取り込み修正版の検証は [診断ログ案内](issue-443-trade-diagnostics.md#pr-450-の取り込み修正版2026-10-16) を参照。

調査日：2026-10-15。対象：v2.10.44（調査開始時の HEAD `1b4d111`）。
報告本文：「双方承認済みから進まない」。再開時に本文とコメントを再取得した。2026-10-15 11:18:59 UTC のコメントは「対応を保留しています。」のみで、再現条件の追記はない。

## 結果

**NOT_REPRODUCED**。製品コードは変更していない。
本番の取引処理を抽出したネイティブハーネスで、以下の8条件はすべて成立した。
これらの成功は、報告された不具合が存在しないことや、修正済みであることを意味しない。

| 場所 | 取引する2人 | 確認順 | 結果 |
| --- | --- | --- | --- |
| ロビー | ホスト・参加者 | ホスト→参加者 | 成立 |
| ロビー | ホスト・参加者 | 参加者→ホスト | 成立 |
| 遠征の安全地点 | ホスト・参加者 | ホスト→参加者 | 成立 |
| 遠征の安全地点 | ホスト・参加者 | 参加者→ホスト | 成立 |
| ロビー | 参加者2人 | 申込側→承諾側 | 成立 |
| ロビー | 参加者2人 | 承諾側→申込側 | 成立 |
| 遠征の安全地点 | 参加者2人 | 申込側→承諾側 | 成立 |
| 遠征の安全地点 | 参加者2人 | 承諾側→申込側 | 成立 |

## 実行方法と確認範囲

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter FullyQualifiedName~Issue443TradeCompletionTests --logger 'console;verbosity=normal'
```

.NET SDK 10.0.111／runtime 10.0.11 で実行（プロジェクトは net8.0）。終了コード0、成功8・失敗0。
先行して既存の `HostCoopTradeTests` と `CoopTradeClientTests` を限定実行し、成功10・失敗0、終了コード0も確認した。
最終状態ではフィルタを `FullyQualifiedName~Issue443TradeCompletionTests|FullyQualifiedName~HostCoopTradeTests|FullyQualifiedName~CoopTradeClientTests` にしてまとめて再実行し、成功18・失敗0、終了コード0。実行ログは `/tmp/issue443-final.log`、TRXは `/tmp/issue443-results/issue443-final.trx`。

追加した再現試行は `tests/Issue73.Native.Tests/Issue443TradeCompletionTests.cs`。
`NativePersistence.targets` の既存抽出対象に申込・承諾・確認・取消の公開メソッドを追加した。
実装の複製はせず、本番の `RequestCoopTrade` → `AcceptCoopTrade` → `SetCoopTradeOffer` → `ConfirmCoopTrade` → ホストの受信・予約 → クライアントの予約保存・prepared → ホストの台帳コミット → executed の反映を実行する。

- 双方がレア遺物を1個ずつ提示し、申込側は欠片7、承諾側は調律石3を渡す。
- 遠征では `Rules.BeginRun` と `RunCheckpoint.Capture` で通常の開始状態とチェックポイントを用意する。
- 双方の成立記録、予約と取引画面状態の解消、素材残高、交換した遺物の受領を検査する。
- 本番の保存処理と `RealFileSystem` を使用。調査用保存先は実行時に表示する一時ディレクトリ。取り込み修正版では、各ケースの終了時に保存をflushした後、自分が作った一時ディレクトリだけを削除する。
- Mirrorの通信、ゲームのプレイヤー／世界、時計はハーネスの境界。ホストのローカル配送は本番同様に同期、リモート配送は送信順にキューから渡す。
- 接続済みの前提で能力通知の鮮度を供給する。実際の接続確立・Mirrorのシリアライズ・ゲーム描画は検証範囲に含まない。

再現試行の初回にはテスト自身の型指定と接続初期化に不備があり、コンパイル／取引開始前の接続確認で失敗した。これらはハーネスの不備として修正したもので、Issue #443 の再現成功には数えていない。最終の試行で、修正前の製品コードに対する失敗は得られていない。

## 再開セッションでの検証

既存コミット `561cf91` を引き継ぎ、再現テストを作り直さず次の限定検証を実行した。製品コードは引き続き未変更。

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue443TradeCompletionTests|FullyQualifiedName~HostCoopTradeTests|FullyQualifiedName~CoopTradeClientTests|FullyQualifiedName~Issue400TradeAbortTests|FullyQualifiedName~TradeSendExceptionTests|FullyQualifiedName~CoopTradeSafeTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue443-resumed-native.trx' --results-directory /tmp/issue443-resumed-results
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~Issue400HostTradeIdleAbortTests|FullyQualifiedName~Issue400ClientTradeTransientAbortTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue443-resumed-issue400.trx' --results-directory /tmp/issue443-resumed-results
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~SodRpg.Core.Tests.CoopTradeTests|FullyQualifiedName~SodRpg.Core.Tests.CoopTradeMigrationTests|FullyQualifiedName~SodRpg.Core.Tests.TradeStuckPending389Tests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue443-resumed-core.trx' --results-directory /tmp/issue443-resumed-results
```

| 実行 | 終了コード | 成功 | 失敗 | TRX |
| --- | --- | --- | --- | --- |
| 再現8条件＋ホスト・クライアント・送信例外・安全判定 | 0 | 24 | 0 | `/tmp/issue443-resumed-results/issue443-resumed-native.trx` |
| #400 の承諾後の待機・一時的な保存処理 | 0 | 2 | 0 | `/tmp/issue443-resumed-results/issue443-resumed-issue400.trx` |
| 協力交換の保存整合・移行、#389 の予約停止 | 0 | 44 | 0 | `/tmp/issue443-resumed-results/issue443-resumed-core.trx` |

合計70件（再現試行8件・既存62件）が成功、失敗0・未実行扱い0。最初のフィルタに含めた `Issue400TradeAbortTests` はファイル名で、実際のクラス名には一致しないため、2行目で正しいクラス名を指定して2件を明示的に実行した。ビルド時は既存の未使用フィールド等の警告が出たが、コンパイルエラーはない。

受け付け審査の一括性・保存整合の懸念には、既存テストの保存失敗ロールバック、ジャーナル書込み中断、二重反映防止、成立後の遅延取消、旧保存形式からの復旧を実行して確認した。これらが成功しても、実通信で発生した報告事象の安全性を証明したことにはならない。版・役割・交換品・発生場所・ログ不足は未解決のまま記録し、推測修正は行わない。

再開指示にある診断ログ追加・リリースは、この依頼の「再現工程で修正しない」「push・公開をしない」という範囲に従い未実施。次工程へ渡す結果ファイルを `NOT_REPRODUCED` で確定する。

## 判断したこと

報告の「双方承認済み」は、交換タブで双方が内容を確認した状態（`OwnConfirmed`／`OtherConfirmed`）と解釈した。申込に対する承諾は片側だけが操作するのに対し、READMEと本番UIは双方の内容確認後に成立する設計だからである。
環境や取引品の指定がないため、ロビーと遠征、ホストを含む交換と参加者同士の交換、両方の確認順を調査した。

## 未実行・未解決

- 実ゲームでの2人／3人の協力プレイ・実Mirror通信・描画：この工程は実機を使わない前提のため未実行。
- 全テスト・MOD全体のビルド・`dreamforge_simkill`：再現対象を限定する工程のため未実行。simkillは取引成立の再現手段ではない。
- 報告時のプロフィール・版・ログ・交換品・場所は提供されていない。これら固有の条件や実通信の問題は、この調査では判定できない。
- 不具合の修正は次工程の範囲。この工程では実装していない。
