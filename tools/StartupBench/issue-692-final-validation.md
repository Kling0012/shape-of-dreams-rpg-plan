# Issue #692 最終コードの検証記録

2026-10-18、修正コミット `7886a827` の読み込み結果と起動処理をmain `ff9af9cf895a75494900ed1342c42d34ee4ecaa4` と比較した。生成星図・旧星図・汎用配置の出力は全バイトが一致した。今回の差分による追加の時間短縮は確認できなかった。

## 修正内容

- `HeroTreeLayout.ForHero` の判定を `HeroSigils.HasTree` に戻し、汎用配置は静的フィールド `Generic` で即時初期化する。登録済み配置を優先し、旧星図のフォールバックは `BaselineTreeFor` から `Lazy` と並行辞書で保持する。
- `FractionalScopedModifiers.ValidateTree` の `byId` と `TargetEffectIds` による事前の候補絞り込みを外した。照合用オブジェクトの再利用は残し、全候補をツリー順に走査する。`Matches` が一致と判定した全対象へ、対象なし・未対応パラメーター・Reload・未知ID・上限の検証を適用する。
- 工程記録22件をリポジトリ外へ保管し、文書はREADMEとこの記録に絞った。容量・uid・ローカルパスを含む環境情報や失敗ビルドのログはPRに残していない。

旧星図の遅延生成と修飾対象オブジェクトの再利用は、比較対象のmainにも取り込み済み。過去の版との比較結果を現在のPRの改善率として引用しない。

## 計測条件

- mainのCoreを一時領域へ展開し、修正後と同じ `Program.cs` と `StartupBench.csproj` でビルドした。
- Linux x86_64、SDK 10.0.111、実行ランタイム10.0.11、Release、`DOTNET_ROLL_FORWARD=Major`、`DOTNET_EnableDiagnostics=0`。パッケージキャッシュは既存のworktree外の `NUGET_PACKAGES` を使った。
- 各回は新規プロセス。通常JITと `DOTNET_TieredCompilation=0` を別条件として各4対測定した。奇数回はmain→修正後、偶数回は修正後→main。ファイルキャッシュは消していない。
- `total --threshold-ms 1500` を使用。1500 msは計測ツールの仮基準で、ゲーム全体の性能要件ではない。

| JIT条件 | 回 | 登録・main→修正後 | 初回BuildLimits・main→修正後 |
| --- | --- | --- | --- |
| 通常 | 1 | 1129→1536 ms | 316→78 ms |
| 通常 | 2 | 1086→1429 ms | 284→96 ms |
| 通常 | 3 | 1744→1357 ms | 401→94 ms |
| 通常 | 4 | 1108→1386 ms | 302→85 ms |
| 段階JITなし | 1 | 1096→1550 ms | 399→51 ms |
| 段階JITなし | 2 | 1120→1451 ms | 424→42 ms |
| 段階JITなし | 3 | 1117→1358 ms | 373→47 ms |
| 段階JITなし | 4 | 1116→1537 ms | 395→53 ms |

| 条件 | 登録の中央値・main→修正後 | 初回BuildLimitsの中央値・main→修正後 | 合計の中央値・main→修正後 |
| --- | --- | --- | --- |
| 通常JIT | 1118.5→1407.5 ms | 309→89.5 ms | 1427.5→1498 ms |
| 段階JITなし | 1116.5→1494 ms | 397→49 ms | 1503→1541.5 ms |

合計の中央値は各回の合計から算出した。汎用配置の即時初期化によって、後続の容量計算に含まれていたコストも登録側で測られる。登録と容量計算の両方を比べても、今回の4対では短縮していない。共有ホストの負荷による変動があり、個々のゲーム起動へ換算しない。

登録時の当該スレッドの累積確保量は全回で120→118 MBだった。常駐メモリではない。一覧取得1000回は整数ミリ秒表示で全回0 ms。mainの通常JIT3回目と、修正後の通常JIT1回目・段階JITなし1回目および4回目は終了コード3、残る12回は0だった。

修正後の `stages` は解析のみの合計235 ms、解析＋登録＋移行の合計1218 msだった。解析を先に実行するため `total` の内訳には換算できない。`initializers` は旧星図663件の取得75 ms、Cetusの初回配置取得283 msだった。他の型初期化も含み、単独の改善量を示す値ではない。

## 読み込み結果

修正コミットで再ビルドして、新規プロセスの全出力を比較した。

| モード | 比較した出力 | mainとの比較 | 終了コード |
| --- | --- | --- | --- |
| `snapshot` | 生成星図9人分、15,514,924バイト | 全バイト一致 | 両方0 |
| `snapshot-baseline` | 旧星図9人分と汎用配置、251,195バイト | 全バイト一致 | 両方0 |

修正前のマージコミット `1be573c3` で取得した同じモードの出力とも一致した。比較にはバイト列の等値判定を使い、ハッシュを追加で計算していない。

全16回の `total` で登録指紋 `ff472bc99d0a58e9`、内容指紋 `13792-a11e6dd41b95a76f`、星数7642、容量のtalents=7657・entries=584が一致した。

更新を妨げる永続キャッシュは追加していない。`ForHero` は現在の登録済み配置を先に取得し、フォールバック専用の辞書には旧星図だけを保持する。`RegisterAuthored` は定義から配置を作り直して登録を置き換える。修飾対象の照合用オブジェクトは検証ごとのローカル変数である。MODの列挙、依存関係の解決、登録順と、利用者のMODファイルや設定への書き込みは変更していない。

## 実行した検証

- `dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false`: main比較用と修正後の両方で成功。警告0・エラー0。
- `DOTNET_ROLL_FORWARD=Major DOTNET_EnableDiagnostics=0 dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --nologo --filter FILTER -p:NuGetAudit=false`: 108件成功、失敗0、スキップ0。FILTERは `AuthoredClusterPlacementTests`、`StarMapClusterTests`、`AuthoredStarContractTests`、`BuildLimitsV131Tests`、`FractionalScopedModifiersTests`、`ScopedKeystoneModifiersTests`、`AuthoredMechanismIntegrationTests` と `StarMapLayoutQualityTests.Tuned_placements_move_stars_only_and_never_touch_the_lines` を `FullyQualifiedName~` と `|` で結んだもの。既存のスタブ等の警告あり。
- 同じCoreテストを `--no-build --no-restore` と追加FILTERで実行: 57件成功、失敗0、スキップ0。対象は `HeroSigilTests`、`StarCarryoverV131Tests`、`StarMigrationRefundTests`、`MovementChargeBonusTests` と `GeneratedHeroAcceptanceTests` の登録成功・旧星の保持・全星の到達可能性の3メソッド。
- `DOTNET_ROLL_FORWARD=Major DOTNET_EnableDiagnostics=0 dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj -c Release --nologo -p:NuGetAudit=false --filter 'FullyQualifiedName~ModReloadTests|FullyQualifiedName~ReloadStartupTests'`: 4件成功、失敗0、既存のSkip指定1件。ReleaseのJITインライン化によりHarmonyのパッチを観測できないため、`SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch` は実行されなかった。既存警告あり。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release --nologo -p:GameDir=GAME_DIR -p:ModDeployDir=TEMP_OUTPUT -p:NuGetAudit=false`: 参照用ゲームDLLで成功。既存警告6件・エラー0。出力はリポジトリ外の一時領域に指定した。
- `total` の16プロセス、`snapshot`、`snapshot-baseline`、`stages`、`initializers`: 上記の結果。新規テストは追加していない。

## CI の起動テスト失敗への対応

CI run `39057378154` では `ContinueRetainsVisitedShopAndSavedWellWhileGuestOnlyProjectsMirroredNodes` の最初の到着後に、次ノードが期待値27に対して-1になった。

原因候補はテスト用 `ZoneManager.SetCurrentNodeIndexAndRevealAdjacent` のインライン化によって、Harmony の `InfinityRevealArrival` が呼ばれない経路である。生成時に公開されたノード1へ到着すると、スタブ本体はそのノードを訪問済みにする。到着後の `RefreshReveal` を通らなければ次の `RevealedFull` がなくなり、`RevealedNext` は-1を返す。既存のパッチ用スタブと同様に、この到着メソッドへ `MethodImplOptions.NoInlining` を付けた。期待値、テスト本体、製品の部屋選択処理は変更していない。

このラウンドではCIと同じSDK 8.0.426・ランタイム8.0.32を一時領域に用意した。修正前は単独ケース、関連88件、起動プロジェクト全体のいずれも成功し、CIの断続的な失敗は再現できなかった。したがって原因の確定や失敗から成功への変化を確認したとは扱わない。修正前のJIT逆アセンブルでも、確認した条件では到着メソッドへの呼び出しが残っていた。

修正後に、このラウンドで実行した検証は次のとおり。`DOTNET_ROOT` と実行コマンドの `dotnet` は一時領域の.NET 8を指定し、NuGetキャッシュはworktree外の既存パスを使った。

- `dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj -c Release --nologo -p:NuGetAudit=false --filter FullyQualifiedName~ContinueRetainsVisitedShopAndSavedWellWhileGuestOnlyProjectsMirroredNodes`: 1件成功、失敗0、スキップ0。ビルドには既存警告がある。
- `dotnet test -c Release tests/SodRpg.Mod.Startup.Tests --no-build`: CIで失敗したジョブと同じコマンドで149件成功、失敗0、既存スキップ1件。
- `DOTNET_TieredCompilation=0 dotnet test tests/SodRpg.Mod.Startup.Tests -c Release --no-build --no-restore --nologo --filter FullyQualifiedName~InfinityLobbyStartTests`: 88件成功、失敗0、スキップ0。

このラウンドの変更はテスト用スタブと本記録のみ。読み込み時間や内容比較の値は前ラウンドの記録を保持しており、今回の再計測値ではない。新しいテストと更新履歴の断片は追加していない。

## 未実行

- Unity/Monoでのゲーム起動、タイトル画面到達、Player.log計測、報告者と同じMOD構成での再現: 実機環境と構成情報がないため。
- プロフィール読み込み、ロビー、遠征開始、ゾーン移動の計測: ツールの対象外で、Issueにも場面の指定がないため。
- full/slowスイートと全旅人の最大点数購入の網羅テスト: 依頼に従い、変更部分の既存テストに限定したため。
- 修正を戻して再現テストを失敗させる確認: 報告されたゲーム内症状と今回の分岐・検証順の専用再現テストがないため。

## 判断したこと

FR-1とFR-2は同じ起動時の読み込み短縮要求として扱った。原因候補だった旧星図9人分の先行生成と修飾対象の重複生成はmainに取り込み済みのため、現在の差分では新たな改善として数えない。F1・F4に従ってフォールバックと検証の意味を優先し、候補の事前絞り込みを外した。残るコストは定義の解析・正規化、参照検証、容量解析、配置と移行規則の登録であり、検証や読み込み内容を省略する修正は加えていない。F3に従って最終の記録だけを残した。変更は内部処理と検証用ツール・文書に限るため、更新履歴の断片は追加していない。
