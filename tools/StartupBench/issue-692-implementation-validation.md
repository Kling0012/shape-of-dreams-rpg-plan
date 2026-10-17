# Issue #692 実装工程の検証記録

引き継いだ製品コードの修正を確認し、同じ条件で修正前後を測り直した。星図登録の中央値は通常JITで5396.5→4706.5 ms、段階JITなしで2178.5→2016.5 msだった。生成星図と旧星図の出力は修正前後で一致し、関連する既存テスト165件が成功した。ゲーム内の読み込み症状は未再現で、ゲーム全体の読み込み時間は未計測である。

## 原因候補と修正

`DreamforgeMod.Awake` はプロフィール読み込みより前に `StarClusters.RegisterAllGenerated` を同期実行する（`src/SodRpg.Mod/DreamforgeMod.cs:72`）。この経路で生成星図の配置を登録するとき、`HeroTreeLayout` の型初期化が使わない旧星図9人分の配置も作っていた。旧星図の配置を、必要な旅人について初めて要求されたときだけ作るようにした（`src/SodRpg.Core/Game/HeroTreeLayout.cs:58`）。登録済み配置を優先し、汎用配置の初期化は残した。

`FractionalScopedModifiers.ValidateTree` は修飾星ごとに候補を調べるたび、同じ星の `GimmickEntry`、`AuthoredMechanismEntry` と寄与ID配列を繰り返し作っていた。修飾星と候補が多い生成星図では確保量が増える。照合用オブジェクトを検証1回の中で共有するようにした（`src/SodRpg.Core/Game/FractionalScopedModifiers.cs:539`）。候補の順序、旅人の条件、照合関数と各検証条件は変えていない。

製品コードの変更は引き継いだ `71d684cc` の2ファイルのみ。今回の工程では追加変更を要する不具合は見つからず、検証記録と案内を追加した。

## 前後比較の条件と結果

- 修正前は `3cdf2a83` のCoreを一時領域へ展開した。修正後は作業開始時の `a45f006f` のCore。両方に同じ `Program.cs` と `StartupBench.csproj` を使用した。
- Linux x86_64、SDK 10.0.111、実行ランタイム10.0.11、Release。net8.0を `RollForward=Major` で実行した。
- NuGetのキャッシュは既存の `/home/wang/.cache/gh-auto/nuget` を使用した。比較用ビルドと大きな出力は `/tmp/issue692-implementation-20261018-TI19wH` に保存した。
- 通常JIT、段階JITなしの順に各4対を計測した。各条件で奇数回は修正前→修正後、偶数回は修正後→修正前の順に、新規プロセスを起動した。ファイルキャッシュは消していない。
- 全コマンド、出力、終了コードは [生の計測記録](issue-692-implementation-validation.txt) に記録した。

| JIT条件 | 回 | 登録・修正前→後 | 初回BuildLimits・修正前→後 |
| --- | --- | --- | --- |
| 通常 | 1 | 5561→5714 ms | 166→252 ms |
| 通常 | 2 | 4868→4626 ms | 178→145 ms |
| 通常 | 3 | 6359→4787 ms | 127→424 ms |
| 通常 | 4 | 5232→4076 ms | 221→141 ms |
| 段階JITなし | 1 | 2093→1755 ms | 64→53 ms |
| 段階JITなし | 2 | 2189→1939 ms | 57→68 ms |
| 段階JITなし | 3 | 2168→2281 ms | 58→80 ms |
| 段階JITなし | 4 | 2219→2094 ms | 79→54 ms |

| 条件 | 登録の中央値・前→後 | 登録＋初回BuildLimitsの中央値・前→後 |
| --- | --- | --- |
| 通常JIT | 5396.5→4706.5 ms（約13%短縮） | 5590→4991 ms（約11%短縮） |
| 段階JITなし | 2178.5→2016.5 ms（約7%短縮） | 2236→2077.5 ms（約7%短縮） |

通常JITの1回目と段階JITなしの3回目は修正後の方が遅かった。共有ホストでの測定値には幅があり、過去の記録とも絶対値が異なる。短縮率は今回の測定に限る。合計の中央値は各回の合計から求めた。

登録中の当該スレッドの累積確保量は全回172→118 MB（約31%減）。常駐メモリではない。全16プロセスが比較用の仮基準1500 msを超え、終了コード3だった。仮基準の達成やゲーム内症状の解消は報告しない。

## 読み込み内容と更新の反映

両方のビルドで `snapshot` と `snapshot-baseline` を実行し、出力を全バイト比較した。生成星図は15,514,554バイト、旧星図と汎用配置は251,195バイトが完全一致した。生成星図9人分の定義、ID、ランク、コスト、登録順、座標、接続とその順序、移行規則を比較した。MODローダー全体の依存関係解決は計測ツールの対象外で、その処理は変更していない。

全16回で登録指紋 `607b55b25354e173`、内容指紋 `13792-e4617232a7ce91e6`、星数7642、容量のtalents=7657・entries=584が一致した。既存APIの出力を読み、追加のハッシュ計算は行っていない。

更新反映の懸念は以下のコード経路と既存テストで確認した。

- `HeroTreeLayout.ForHero` は毎回、現在の登録済み配置を先に取得する（`HeroTreeLayout.cs:83`）。旧星図用の辞書は登録状態に依存しない `BaselineTreeFor` から配置を作るため、登録解除後に生成星図の配置を返さない（同`:93`）。
- `RegisterAuthored` は渡された定義から新しい配置を作り、登録を置き換え、一覧キャッシュを無効にする（`Mechanisms/AuthoredStarRegistration.cs:114`）。修飾対象の共有は `ValidateTree` のローカル変数で、次回の登録では作り直す。照合関数は共有オブジェクトを書き換えない（`FractionalScopedModifiers.cs:511`、`Mechanisms/AuthoredMechanisms.cs:266`）。
- 既存の `AuthoredClusterPlacementTests` は同じ定義を再登録したときの配置を検証し、`MovementChargeBonusTests` は生成星図を登録・解除する経路を検証する。いずれも今回の実行で成功した。

永続キャッシュ、MODファイルの走査、依存関係の解決、登録順は変更していない。利用者のMODファイルや設定にも手を加えていない。

## 実行した検証

- `dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false`: exit 0、警告0・エラー0。修正前Coreと同じ計測ツールのビルドもexit 0、警告0・エラー0。
- `total --threshold-ms 1500`: 修正前後各4回×2条件。全16回がexit 3、仮基準超過。`snapshot`、`snapshot-baseline`、全バイト比較、`stages`、`initializers` はすべてexit 0。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~AuthoredClusterPlacementTests|FullyQualifiedName~StarMapClusterTests|FullyQualifiedName~AuthoredStarContractTests|FullyQualifiedName~BuildLimitsV131Tests|FullyQualifiedName~FractionalScopedModifiersTests|FullyQualifiedName~ScopedKeystoneModifiersTests|FullyQualifiedName~AuthoredMechanismIntegrationTests|FullyQualifiedName~HeroSigilTests|FullyQualifiedName~StarCarryoverV131Tests|FullyQualifiedName~StarMigrationRefundTests|FullyQualifiedName~StarMapLayoutQualityTests.Tuned_placements_move_stars_only_and_never_touch_the_lines' --nologo -p:NuGetAudit=false`: exit 0、134件成功・失敗0・スキップ0。既存のテスト用スタブ等の警告あり。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~GeneratedHeroAcceptanceTests.Registration_succeeds_with_zero_rejections_and_installs_the_migration_rules|FullyQualifiedName~GeneratedHeroAcceptanceTests.Every_baseline_star_retains_its_id_ranks_and_keystone_definition|FullyQualifiedName~GeneratedHeroAcceptanceTests.Every_star_is_reachable_from_the_start|FullyQualifiedName~MovementChargeBonusTests' --nologo`: exit 0、31件成功・失敗0・スキップ0。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release --nologo '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' '-p:ModDeployDir=/tmp/issue692-implementation-20261018-TI19wH/mod-build' -p:NuGetAudit=false`: exit 0、既存警告6件・エラー0。参照用ゲームDLLを使用し、配置先は一時領域に指定した。

## 残るコストと未実行

修正後も定義の解析・正規化、所有記憶や参照の検証、容量解析、配置、移行規則の登録が残る。追加の `stages` 計測は解析のみ253 ms、解析＋登録＋移行1629 msだった。解析後に登録するためJITと型初期化が温まっており、`total` の内訳には換算できない。`initializers` は旧星図定義73 ms、最初のCetus配置281 msだったが、他の初期化も含むため単独の短縮効果は判定していない。

- 未実行: Unity/Monoでのゲーム起動、タイトル画面到達、Player.log計測、報告者と同じMOD構成での再現。実機環境と構成情報がないため。
- 未実行: プロフィール読み込み、ロビー、遠征開始、ゾーン移動の計測。計測ツールの対象外で、Issueに場面の指定もないため。
- 未実行: full/slowスイート、全旅人の最大点数購入の網羅テスト、変更範囲外のテスト。依頼に従い、変更部分に絞った既存テストのみ実行したため。
- 未実行: 修正を戻して再現テストを失敗させる確認。ゲーム内症状の再現テストがブランチにないため。

## 判断したこと

FR-1とFR-2は同じ要望と解釈し、起動時の同期的な星図登録で内容を変えずに省ける処理を対象にした。#459で導入済みの文字列表化や配置計算の省略は今回の変更に数えていない。検証を省いて時間を短くする方法や、処理をプロフィール読み込み後へ移す方法は採用していない。処理の軽量化は `changelog.d/README.md:9` で断片の対象外とされているため、更新履歴の断片は追加していない。
