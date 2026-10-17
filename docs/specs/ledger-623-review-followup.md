# #623 レビュー提案の対応記録

2026-10-18、PR #508 と #512 のレビュー提案15件を、現行ブランチで確認した。
両PRは GitHub 上で `CLOSED`、`mergedAt=null` だった。取り込まれた変更は現在のコードで判断した。

## テストの選択

L1・L2・L3・L6 に対応した。`select_tests` と `is_production_cs` へ root を渡し、
参照ツールとリンク入力のキャッシュも同じ root で参照するようにした。
入れ子のツールは参照ディレクトリの末尾を名前に使う。
`tools/balance/BalanceSim/Program.cs` なら `balance` ではなく `BalanceSim` を照合する。

`tests/` の C# を処理したあとも、生成済み C# のパス参照を照合する規則へ進めるようにした。
広く使われる型の全件選択は製品コードの規則であり、テスト用の補助型には従来どおり利用者を選ぶ。
`bin` と `obj` は変更ファイルと照合対象の両方から拡張子にかかわらず除外する。

修正前後の選択を、その場で Python から呼び出して比較した。検証用のファイルは追加していない。

| ケース | 修正前 | 修正後 |
| --- | --- | --- |
| 別の root でツールを判定 | 既定のリポジトリを参照 | 指定した root を参照 |
| 入れ子の BalanceSim を名前で読むテスト | 選択されない | 選択される |
| 宣言型のない Factory.Generated.cs をパスで読むテスト | 選択されない | 選択される |
| bin/obj 内の C# が宣言するテストクラス | 選択される | 選択されない |
| テストの補助型を使うテスト | 選択される | 選択される |

L7 は [テストガイド](../testing.md) に制限と回避方法を追記した。
変更からの選択漏れには `--all` を使う。ただしプロジェクト検出自体が失敗した場合は、
`--all` でも同じ検出処理を使うため、対象の `.csproj` を `dotnet test` で直接実行する。

## 星図ソースのキャッシュの測定

L4 は `legacy_numeric_source` の処理時間を測定した。キャッシュの実装は変更していない。
対象は `HeroSigils.cs`、`HeroStarRoutes.cs`、`PairCombos.cs`、`StarClusters.cs` の4ファイル。
同じ Python プロセスで各ファイルを事前に読み、4ファイルの処理を5回ずつ測った。
毎回 `_LEGACY_SOURCE_CACHE.clear()` を呼ぶ場合と、呼ばずに再利用する場合を比較した。
時間には `time.perf_counter()`、呼び出し回数には別の1巡を `cProfile` で使った。

| 条件 | 4ファイルの所要秒数、5回 | 中央値 |
| --- | --- | --- |
| 毎回キャッシュを空にする | 0.662782 / 0.662634 / 0.624686 / 0.607079 / 0.563320 | 0.624686秒 |
| キャッシュを再利用する | 0.554997 / 0.626715 / 0.580846 / 0.551469 / 0.612197 | 0.580846秒 |

両条件とも1巡で `load_table` と `load_manifests` を各4回呼んでいた。
キャッシュを再利用しても入力の読み込みは残り、この測定で中央値の差は約0.044秒だった。
この差は4ファイルの呼び出しに限る。生成処理全体の「約8秒の改善」を裏付ける測定ではない。
別に `python3 tools/balance/gen_cs.py --check` を1回実行し、18.613秒、終了コード0だった。
同コマンドのキャッシュなし比較は実行していない。

## 対応済みの提案とCI

L10・L11・L12・L15 はコミット `f1afa85c` ですでに対応していたため、変更を加えなかった。

- L10 は既知のストリームの集合をメッセージごとに1回構築する。32件以降の既知の受領も保持する。
- L11 は参加履歴と既存の受領にも残らない列を退役とする理由をコメントに記している。
- L12 は取引データのデコード失敗時に例外型だけを診断へ記録する。
- L15 は未知の受領の件数と保留中の要求数を接続ごとに30秒以上の間隔で記録する。
  未知の受領を再送要求へ追加しない。

L9 は [Actions run 38846635381](https://github.com/Kling0012/shape-of-dreams-rpg-plan/actions/runs/38846635381)
の `core-tests` を `gh run view` で確認した。
`python tools/balance/gen_cs.py --check` と `dotnet build -c Release tools/WikiGen` は
どちらも実際のCIで `success` だった。今回のブランチのCIは未実行。

## 実行した検証

- `python3 -m unittest discover -s tools/tests -p 'test_test_changed.py'`：修正前後とも25件成功。
- 上記の修正前後の選択結果の比較と、キャッシュの測定：記載した結果を確認。
- `python3 tools/balance/gen_cs.py --check`：終了コード0。
- `python3 tools/test_changed.py --base HEAD --list`：関係する変更なしとして終了コード0。Pythonの変更だけでは.NETのテストを選択しない。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~HostCoopTradeTests|FullyQualifiedName~V272ProgressionTests|FullyQualifiedName~NativeAcceptanceTests.DifferingVersionPeerWithoutHelloSharesFrameBudgetAndEventuallyReceivesEveryFact|FullyQualifiedName~NativeAcceptanceTests.EmptyProvisionalPeerIsNotRestoredOrPersistedAndDisconnectLeavesNoPeer'`：9件成功。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~NativeAcceptanceTests.SecureArrivalAndInfinityBoundaryProgressWithReceivedAbsentOrLateHello|FullyQualifiedName~NativeAcceptanceTests.DisconnectingUnresponsivePeerCannotPinFutureInfinityGraphs|FullyQualifiedName~NativeAcceptanceTests.RestoringV272NonRemoteDebtDoesNotBlockButKeepsUnsavedHostFacts|FullyQualifiedName~NativeAcceptanceTests.A_changed_client_identity_leaves_no_debt_that_pins_later_kills'`：8件成功。

最初の.NETコマンドの `V272ProgressionTests` はファイル名であり、クラス名ではなかった。
`--list-tests` で選択された9件を確認したあと、実際のクラス名で残り8件を実行した。
これらのテストは取引と撃破同期の既存の回帰を確認するもので、L10〜L15専用の検証ではない。

## 保留と未実行

- L5：未知の入力を拡張子で除外するには、扱う拡張子と、拡張子のない入力の方針が必要。
  `.json` だけに絞るとテキストやバイナリの入力を取りこぼすため、全件選択を維持した。
- L8：性能テストの安定性を判断するには、負荷を管理した単独実行の反復が必要。
  今回は低速テストを実行しない指示のため保留。
  既存の [#519 の記録](issue-519-codex-followup.md) に撃破0.1msのしきい値超過と単独実行の扱いがある。
- L13・L14：どちらもPR #512の本文の重複を指す。過去のPR本文の変更はコミットまでという今回の範囲外。
  今回のPR本文案では検証の記載を1か所にまとめる。
- 未実行：全件テストと低速テスト。変更部分に絞る指示に従い、後続工程に委ねる。
- 未実行：32件超の受領、未知の受領のログ抑制、デコード失敗の例外型ログに専用の新規テストを追加する検証。新規テストは禁止されているため、既存コードの確認にとどめた。
- 未実行：Unityでの実協力通信。ゲームを起動できる環境がない。
- 未実行：`/usr/bin/time` を使った測定。実行ファイルがなかったため、Pythonで測定した。
