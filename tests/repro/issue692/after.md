# Issue #692 起動時の星図登録の改善

2026-10-17、MOD初期化が同期実行する `StarClusters.RegisterAllGenerated` の重複処理を減らした。同じホストで修正を一時的に戻して測ると登録1789 ms、修正を復元した2回は1159 msと1083 msだった。登録中の割り当て量は172 MiBから120 MiBに減った。

## 原因と変更

`HeroTreeLayout` は型の初期化時に全旅人の基準配置を作っていた。登録済みの配置を使う場合にも、使わない基準配置を作る費用が発生していた。基準配置を旅人ごとに必要時に作って保持する方式へ変更し、汎用配置も必要時に作るようにした。登録済み配置を優先する順序は変えていない。

登録時の所有権検証では、各星のアンカーを探すたびに定義全件を走査していた。旅人とローカルIDを組にした辞書を用意し、参照先を直接調べるようにした。橋IDからペアIDを解決するコピー処理も事前辞書を使う。同じ橋IDに複数の定義がある場合は、従来と同じ先頭の定義を使う。

`FractionalScopedModifiers.ValidateTree` は、各強化星の検証で候補全件を調べ、その都度効果用のエントリを生成していた。エントリを検証中に共用し、対象IDが指定された強化星はID辞書で候補を絞るようにした。同じIDを持つ選択肢はすべて候補に残し、旅人・メモリー・効果の一致は従来の判定で検証する。

これらは初期化と登録の変更である。星のデータ、保存ID、配線、効果量、必要ポイント、通信上限を変更せず、既存の検証と旅人単位の登録失敗処理も残した。

## 同じ条件での比較

環境と計測範囲は [修正前の記録](README.md) と同じ。Linux、.NET 10.0.11、Releaseビルドの新規プロセスを使った。OSのファイルキャッシュは消していない。1500 msは既存プログラムの診断用基準で、Issueの許容時間ではない。

修正した4ファイルをメモリと一時ディレクトリに保全し、`HEAD` の修正前コードへ一時的に戻してビルドした。通常JITと段階JITなしを測定後、修正を復元して再ビルドし、同じ順序で2回ずつ測った。生の出力と終了コードは [revert-check.txt](revert-check.txt) に保存した。

| コードと条件 | 登録 | 初回BuildLimits集計 | 2区間の合計 | 登録中の割り当て量 | 終了コード |
| --- | ---: | ---: | ---: | ---: | ---: |
| 一時取り消し・通常JIT | 1789 ms | 87 ms | 1876 ms | 172 MiB | 3 |
| 復元後1回目・通常JIT | 1159 ms | 334 ms | 1493 ms | 120 MiB | 0 |
| 復元後2回目・通常JIT | 1083 ms | 352 ms | 1435 ms | 120 MiB | 0 |
| 一時取り消し・段階JITなし | 1672 ms | 49 ms | 1721 ms | 172 MiB | 3 |
| 復元後1回目・段階JITなし | 1103 ms | 406 ms | 1509 ms | 120 MiB | 0 |
| 復元後2回目・段階JITなし | 1189 ms | 446 ms | 1635 ms | 120 MiB | 0 |

通常JITの登録は直前の修正前測定に対して約35〜39%短縮し、登録中の割り当て量は約30%減った。初回BuildLimits集計は増えている。原因は今回確定していないため、登録だけの短縮率を起動全体の短縮率とは扱わない。表の2区間の合計では通常JITが約20〜24%、段階JITなしが約5〜12%短縮した。共有ホストの測定値にはばらつきがあり、Unity/Monoやゲーム起動全体の秒数へは換算できない。

最適化途中の値は最終比較から除外した。一時計測出力を除去した後の通常JITは1202 ms、1329 msでも成功し、段階JITなしは1109 msでも成功した。最終比較の前後では毎回次の値が一致した。

- 登録指紋: `607b55b25354e173`
- 内容指紋: `13792-e4617232a7ce91e6`
- 星一覧: 7642件
- BuildLimits: talents=7657、entries=584
- `HeroSigils.All` 1000回: 0 ms。整数表示のため、厳密にゼロという意味ではない。

## 実行した検証

再現コマンドは変更せず、修正を一時的に戻すと終了コード3、復元すると終了コード0になることを実行して確認した。両コードのReleaseビルドは終了コード0、警告0、エラー0だった。

```sh
dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo -v:q
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
DOTNET_TieredCompilation=0 dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
```

既存の関連テストを最終コードでまとめて実行し、204件成功、失敗0件、スキップ0件だった。`DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-restore --nologo --filter '<対象>&Speed!=Slow'` を使った。対象は次のクラスとメソッドで、新しいテストは作っていない。

- `AuthoredStarContractTests`、`FractionalScopedModifiersTests`、`AuthoredMechanismIntegrationTests`、`AuthoredKeystoneIntegrationTests`、`ScopedKeystoneModifiersTests`
- `GeneratedHeroAcceptanceTests`、`BuildLimitsV131Tests`、`ContentFingerprintTests`、`BridgeRenewalAuthoredTests`、`AuthoredClusterPlacementTests`
- `StarMigrationRefundTests`、`ProfileMigrationLoadTests`、`LegacyKeystoneMigrationTests`
- `StarMapLayoutQualityTests.Tuned_placements_move_stars_only_and_never_touch_the_lines` と `The_placement_table_matches_the_current_stars`。ノード・隣接順・辺の順序と座標表の一致を検証した。

MODのReleaseビルドも `-p:GameDir=/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams` を指定して実行し、終了コード0、エラー0だった。既存APIの非推奨などの警告が6件あった。テストのビルドにも既存のテスト用API・アナライザーの警告がある。参照先にはModsディレクトリがないため、ゲームへの配置処理は実行されなかった。

## 判断したこと

Issueには遅い段階の指定がないため、前工程の調査に沿ってMOD初期化内の同期登録を改善対象とした。#459の既存対策が入っていることを確認し、残っていた重複生成と全件走査を減らした。ゲームでの再現が必要な挙動を推測して変更していない。

起動処理の内部的な軽量化に当たるため、`changelog.d/README.md` の規則に従い更新履歴の断片は追加していない。`CHANGELOG.md` も変更していない。

## 修正ラウンドでの再確認

2026-10-18、FR-1・FR-2の完了報告を確認した。最適化は既に4ファイルに実装されていたが、PR本文は旧番号のR1・R2を使い、結果JSONにも現在の要件番号と対応ファイルの報告がなかった。今回の結果JSONには両要件の実装箇所を記載した。本番コードは追加変更していない。

- FR-1: 読み込み処理を調べ、改善できる箇所に対応する。`tools/StartupBench/Program.cs` で本番の登録処理を測り、`HeroTreeLayout.cs` の基準配置、`Mechanisms/AuthoredStarContract.cs` の所有権検索、`Mechanisms/AuthoredStarRegistration.cs` の橋ID検索、`FractionalScopedModifiers.cs` の強化対象検証で重複処理を減らした。原因と比較結果はこの文書に記録している。実装ファイルはいずれも `src/SodRpg.Core/Game/` 配下にある。
- FR-2: MODの読み込み時間を短縮する。同じ4ファイルの最適化が対象。実際の初期化で呼ばれる `RegisterAllGenerated` を同じ条件で測り直し、短縮と登録結果の一致を確認した。

4ファイルを最適化コミット `81c9e22c` の親の状態へ一時的に戻し、Releaseビルド後に既存の `total --threshold-ms 1500` を実行した。最適化したコードを復元して再ビルドし、同じコマンドを実行した。各条件で新規プロセスを2回起動し、OSのファイルキャッシュは消していない。生の出力は [repair-check.txt](repair-check.txt) に保存した。

| コードと条件 | 登録の2回分 | 初回BuildLimits集計の2回分 | 2区間の合計の2回分 | 登録中の割り当て量 | 終了コード |
| --- | --- | --- | --- | --- | --- |
| 修正前・通常JIT | 2022 / 2018 ms | 110 / 99 ms | 2132 / 2117 ms | 172 MiB | 3 / 3 |
| 修正後・通常JIT | 1254 / 1380 ms | 413 / 282 ms | 1667 / 1662 ms | 120 MiB | 0 / 0 |
| 修正前・段階JITなし | 2293 / 1608 ms | 53 / 71 ms | 2346 / 1679 ms | 172 MiB | 3 / 3 |
| 修正後・段階JITなし | 1079 / 1077 ms | 409 / 401 ms | 1488 / 1478 ms | 120 MiB | 0 / 0 |

全8回で登録指紋 `607b55b25354e173`、内容指紋 `13792-e4617232a7ce91e6`、星一覧7642件、BuildLimitsのtalents=7657・entries=584が一致した。初回集計の増加も含め、今回の各条件で登録と集計の合計は短くなった。1500 msは診断用の基準であり、ゲーム起動全体の許容時間ではない。

既存のCore関連テストは前回と同じフィルターで204件、`NativeStartupTests`・`ReloadStartupTests` は18件が成功し、いずれも失敗・スキップは0件だった。ビルド時には既存のテスト用API・アナライザーの警告が出た。新しいテストは作っていない。

## CIの保存確認テストの修正

2026-10-18、CIの `InterruptedRelicClaimTests.Definitive_disk_failure_restores_rights_and_existing_satchel_without_success_notice` が、ディスク失敗を解除した後の再試行で失敗した。成功を期待する箇所に「保存確認待ち」が返っていた。

テストの共通準備は `AsyncProfileWriter.ConfirmTimeoutMs` をすべて100 msへ短縮していた。実際の保存は別スレッドで一時ファイルの書き込み、ディスクへのフラッシュ、読み戻しと置換を行う。100 ms以内に確認できなければ、本番コードは保存確認待ちを返す。この分岐はテストの期待と両立しないが、保存完了の遅延自体は異常ではない。CIでどの処理が遅れたかは特定していない。

`CreateSession` に待機上限の任意指定を追加し、通常ケースは本番既定の3000 msを使うようにした。書き込みを止めて時間切れを確認する既存の2ケースだけ100 msを指定する。保存失敗からの復元、通知前の保存完了、遅延失敗後の再試行などのアサーションは変更していない。本番コードと起動最適化への追加変更はなく、新しいテストも作っていない。テスト準備だけの変更なので更新履歴の断片は追加していない。

修正前の関連9ケースと、CIで失敗したケースを新規テストプロセスで12回実行したが、すべて成功した。手元ではCIの失敗を再現していない。修正後は次のコマンドで9件成功、失敗0件、スキップ0件を確認した。時間切れと遅延失敗のケースも成功し、ビルド時には既存の警告が118件出た。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test -c Release tests/Issue73.Native.Tests --filter 'FullyQualifiedName~InterruptedRelicClaimTests' --nologo --logger 'console;verbosity=normal'
```

最初はroll-forward指定なしでビルドとテストを試みたが、手元に.NET 8ランタイムがなく、テストを開始できなかった。以後は既存の環境と同じ.NET 10.0.11で実行した。CIの.NET 8環境での再実行と、修正を戻した場合の失敗確認は未実行である。

## 未実行

- ゲーム起動、タイトル画面までの時間、Unity/Mono、Player.log、`dreamforge_simkill`。この環境ではゲームを実行できないため。実機だけの確認を求めるIssueは作っていない。
- プロフィール読み込み、Harmonyパッチ導入、アイコン先読みを含むAwake全体の時間。既存のCore計測プログラムの対象外のため。
- full/slowスイート、関連範囲外の.NET/Pythonテスト、配置品質テストの残り。依頼に従い変更箇所に関係する既存テストに絞ったため。
