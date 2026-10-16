# #493 / PR #496 ホスト互換性の対応・検証記録

2026-10-16 の修正ラウンドで、Issue #493、PR #496 のコメント全文とベースブランチとの差分を照合した。
R1〜R7 は既存のコミットで対応済みだったため、このラウンドでは製品コードとテストを変更していない。

## 要件と実装箇所

| 要件 | 対応 |
| --- | --- |
| R1 | `BuildInputCapability.cs` の `OnBuildInputCapability` で、禁忌装備対応を初めて確認したときに `ForbiddenGearHostNotice` を null、`_forbiddenGearStripNotice` を false にし、ビルド再送とキャッシュ更新を予約する。通常の能力応答を受信済みでも処理できるよう、早期 return より前に置く。 |
| R2 | `HostBuildValidation.Compatibility.cs` の `RequiresForbiddenGearSupport` を `WithoutUnsupportedGear` と `ClientSession.cs` の `RefreshForbiddenGearNotice` から使用する。禁忌化または旧上限を超える再調律を同じ条件で判定する。 |
| R3 | `tools/test_changed.py` の `_LINKED_INPUT_RE` で None / Content の Include / Update を収集する。`linked_test_inputs` の docstring に、対象が `tests/*/*.csproj` のリテラルパスで、MSBuild import・property・glob は評価しないことを記載する。 |
| R4 | `ClientSession.Hello.cs` の `TickHello` で `HostVersionWarning == null` の場合だけ無応答警告を生成する。表示言語が変わっても他経路の警告を上書きしない契約をコメントで明記する。 |
| R5 | `DreamforgeUi.cs` の `RowTexts` に、`h.Equipped` の添字と `Slot` 列挙値が一致する前提を記載する。 |
| R6 | `Economy.cs` の `TradeLedger.EnsureRoom` に、上限表示が `CanBegin` の `ManualHeldCount < MaxHeld` と `HeldCount < MaxRestored` の2条件に依存することを記載する。 |
| R7 | `ClientSession.cs` の `SendBuildIfNeeded` に、通知更新は LocalHero・RPC・接続・復元完了を前提とし、ロビー等でこの経路を通らない間は新しい警告を出さない契約を記載する。 |
| R8 | PR の1件目の追加コメント全文を確認した。Release ビルド、生成コードチェック、通常・SLOW・Performance/Timing の成功ログであり、追加の変更要求・失敗報告はない。今回の検証結果とは区別する。 |
| R9 | PR の2件目の追加コメント全文も確認した。Release ビルド、生成コードチェック、通常・SLOW・Performance/Timing の成功ログであり、追加の変更要求・失敗報告はない。今回の検証結果とは区別する。 |

## 判断したこと

- R8・R9 の引用は依頼文では途中までだったが、`gh pr view 496 -R Kling0012/shape-of-dreams-rpg-plan --comments` で全文を確認した。両方とも成功結果の報告であるため、追加の製品挙動変更を導入せず、指定のビルドと関連する既存テストを再実行する。
- R1 の通知解除と再送予約、R2 の判定共通化は現在の差分に存在する。受け付け審査の2件について追加のコード修正は不要と判断した。ただし、以下のテスト成功を遅延応答場面の直接再現・解消の証明として扱わない。
- セーブ形式・通信プロトコル・画面レイアウトは変更せず、毎フレーム処理のメモリ確保も増やさない。

## このラウンドで実行した検証

以下はこのセッションで実行した結果であり、PR コメントにある過去の成功ログの転載ではない。

- `gh pr view 496 -R Kling0012/shape-of-dreams-rpg-plan --comments`、`gh pr diff 496 -R Kling0012/shape-of-dreams-rpg-plan`: exit 0。コメント全文、7ファイルの製品・ツール差分を確認した。
- `gh issue view 493 -R Kling0012/shape-of-dreams-rpg-plan --json title,body,comments`: exit 0。R1〜R7 の原文を照合した。
- `python3 -m unittest discover -s tools/tests -p 'test_test_changed.py'`: exit 0、25件成功。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests -c Release --filter 'FullyQualifiedName~HostBuildValidationTests|FullyQualifiedName~HostGearValidationTests|FullyQualifiedName~EconomyTests|FullyQualifiedName~EconomyV131Tests|FullyQualifiedName~TradeLedgerLostAndLimitTests|FullyQualifiedName~AutocastPressureReportTests'`: exit 0、108件成功、失敗0、スキップ0。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests -c Release --filter 'FullyQualifiedName~CoopTradeClientTests|FullyQualifiedName~PolarisNextRunTests|FullyQualifiedName~RelicRowCacheTests|FullyQualifiedName~ContinueSaveTests'`: exit 0、46件成功、失敗0、スキップ0。境界スタブ等の未使用フィールドに関するコンパイル警告は出た。
- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/gh-auto-moddeploy`: exit 0、警告0、エラー0。PR コメントと同じコマンドを実行した。
- `python3 tools/balance/gen_cs.py --check`: exit 0。入力と生成物が未変更のため、キャッシュ判定で生成をスキップした。

## 未実行と確認の限界

- 未実行: 全件スイート、`tools/test_changed.py --all`、`SODRPG_SLOW=1`、Performance/Timing 全件。作業範囲の指示に従い、後続の検証工程に委ねる。
- 未実行: 実ゲームで20秒経過後に対応応答が届く場面、通知表示、協力通信。実機確認不可という作業前提のため。
- 未実行: 遅延応答時の通知解除、`TickHello` の他経路警告保持、Content Include / None Update の専用再現ケース。直接再現する既存テストは見つからず、新規テストの設計・作成は禁止されているため。対象を絞った既存テストの成功だけで各場面の修正を直接証明したとは扱わない。
- 未実行: 一時差し戻しによる比較。このラウンドでは検証記録のみを追加し、製品挙動を変更していないため。
