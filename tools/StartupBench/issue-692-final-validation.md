# Issue #692 最終コードの検証記録

起動時の星図登録で残っていた重複処理を省く修正を確認した。最終コードの前後比較では、星図登録と初回容量計算の合計中央値が通常JITで2251.5→1855.5 ms、段階JITなしで1761→1668.5 msだった。ゲーム内の症状は未再現で、ゲーム全体の読み込み時間が短くなったとは判定していない。

## 原因候補と修正

`DreamforgeMod.Awake` は保存データを読む前に `StarClusters.RegisterAllGenerated` を同期実行する（`src/SodRpg.Mod/DreamforgeMod.cs:70`）。この経路では生成星図の配置を旅人ごとに登録するが、`HeroTreeLayout` の型初期化でも、使わない旧星図9人分の配置を作っていた。旧星図の配置を、その旅人のフォールバックが必要になったときだけ生成するようにした（`src/SodRpg.Core/Game/HeroTreeLayout.cs:58`）。汎用配置の初期化は残し、登録済み配置の取得を優先する。フォールバックには登録状態に左右されない `BaselineTreeFor` を使う。

`FractionalScopedModifiers.ValidateTree` は修飾星ごとに候補を調べ、候補ごとに照合用の `GimmickEntry`、`AuthoredMechanismEntry`、寄与ID配列を作っていた。修飾星と候補が多い生成星図では同じ候補のラッパーを繰り返し作る。仕掛けを持つ候補のラッパーを検証1回の中で共有するようにした（`src/SodRpg.Core/Game/FractionalScopedModifiers.cs:539`）。候補の順序、旅人の条件、照合関数、対象の存在・パラメーター・上限の検証は変えていない。登録をまたぐ候補キャッシュは追加していない。

## 前後比較の条件

- 修正前のCoreは `3cdf2a83`、修正後は `71d684cc` の製品コード。`21f906bc` までに製品コードの追加変更がないことを差分で確認した。
- 修正前のCoreだけを `/tmp/issue692-final-12tuwtgz` へ展開し、両方に本ブランチの同じ `Program.cs` と `StartupBench.csproj` を使用した。
- Linux x86_64、SDK 10.0.111、実行ランタイム10.0.11、Releaseビルド。net8.0のプログラムを `RollForward=Major` で実行した。NuGetのキャッシュは既存のworktree外の `NUGET_PACKAGES` を使用した。
- 毎回新規プロセスで実行した。通常JITと `DOTNET_TieredCompilation=0` は別条件として各4対を測定した。奇数回は修正前→修正後、偶数回は修正後→修正前。ファイルキャッシュは消していない。
- 生の出力、実行コマンド、終了コードは `issue-692-final-validation.txt` に記録した。

| JIT条件 | 回 | 登録・修正前→後 | 初回BuildLimits・修正前→後 |
| --- | --- | --- | --- |
| 通常 | 1 | 2175→2414 ms | 94→116 ms |
| 通常 | 2 | 2152→1736 ms | 102→74 ms |
| 通常 | 3 | 2013→1795 ms | 94→91 ms |
| 通常 | 4 | 2165→1694 ms | 84→131 ms |
| 段階JITなし | 1 | 1942→1668 ms | 60→52 ms |
| 段階JITなし | 2 | 1775→1566 ms | 57→51 ms |
| 段階JITなし | 3 | 1629→1779 ms | 61→55 ms |
| 段階JITなし | 4 | 1613→1524 ms | 51→50 ms |

| 条件 | 登録の中央値・前→後 | 登録＋初回BuildLimitsの中央値・前→後 |
| --- | --- | --- |
| 通常JIT | 2158.5→1765.5 ms（約18%短縮） | 2251.5→1855.5 ms（約18%短縮） |
| 段階JITなし | 1702→1617 ms（約5%短縮） | 1761→1668.5 ms（約5%短縮） |

共有ホストの負荷による変動があり、通常JITの1回目と段階JITなしの3回目は修正後の方が遅かった。上記はこの測定の中央値であり、個々の起動やゲーム環境で同じ短縮率になることを保証しない。合計の中央値は各回の合計から求めた。

登録時の当該スレッドの累積確保量は全回172→118 MBで、約31%減った。常駐メモリではない。全16プロセスが仮基準1500 msを超え、終了コード3だった。1500 msは報告者の許容時間でも仕様の性能要件でもないため、この超過をゲーム内症状の再現とは扱わない。

## 読み込み内容と更新の反映

両方のビルドで `snapshot` と `snapshot-baseline` を新規プロセスで実行し、出力の全バイトを比較した。どちらも終了コード0で、生成星図は15,514,554バイト、旧星図と汎用配置は251,195バイトが完全一致した。生成星図9人分の定義、ID、登録順、ランク、コスト、座標、接続順、移行規則を比較した。大きな出力は一時領域に残し、リポジトリには追加していない。

全16回の `total` で登録指紋 `607b55b25354e173`、内容指紋 `13792-e4617232a7ce91e6`、星数7642、容量のtalents=7657・entries=584が一致した。指紋は既存APIの出力を比較したもので、追加のハッシュ検証は行っていない。

更新反映について、次の経路を確認した。

- `HeroTreeLayout.ForHero` は毎回、現在の登録済み配置を先に取得する。遅延生成の辞書は旧星図専用で、`BaselineTreeFor` から作るため、登録解除後に生成星図が残らない。
- `RegisterAuthored` は最新の定義から配置を作り、登録を置き換える。同じ内容でも新しいインスタンスを公開するため、既存の一覧キャッシュを無効にする（`src/SodRpg.Core/Game/Mechanisms/AuthoredStarRegistration.cs:113`）。この経路は変更していない。
- 修飾対象のラッパーは `ValidateTree` のローカル変数で、次の登録では作り直す。`Matches` と `AuthoredMechanisms.Matches` はこの照合でラッパーを書き換えない。
- ファイルへのキャッシュ保存、MODの列挙・依存関係解決・登録順の変更、利用者のMODファイルや設定の変更はない。

## #459との関係と残るコスト

#459の文字列表化と対象ID配列の共有は `StarClusters/ManifestReader.Generated.cs`、調整済み配置がそろう場合の計算省略は `HeroTreeLayout.Clusters.cs:154`、正規化の辞書と一覧キャッシュは `Mechanisms/AuthoredStarRegistration.cs:223` 以降にすでに入っている。今回の変更としては数えていない。

修正後も、定義の解析・正規化、所有記憶や参照の検証、容量解析、生成星図の配置、移行規則の登録が残る。`stages` の追加計測では解析のみの合計272 ms、解析＋登録＋移行の合計1724 msだった。解析後に登録するためJITと型初期化が温まっており、`total` の内訳や差分には換算できない。`initializers` も他の型初期化を含み、単独での短縮効果は今回確認できていない。内容の検証を省いたり、読み込みを後ろへ移したりする対策は採用していない。

## 実行した検証

- `dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false`: exit 0、警告0・エラー0。修正前Coreに同じ計測プログラムを組み合わせたビルドもexit 0、警告0・エラー0。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~AuthoredClusterPlacementTests|FullyQualifiedName~StarMapClusterTests|FullyQualifiedName~AuthoredStarContractTests|FullyQualifiedName~BuildLimitsV131Tests|FullyQualifiedName~FractionalScopedModifiersTests|FullyQualifiedName~ScopedKeystoneModifiersTests|FullyQualifiedName~AuthoredMechanismIntegrationTests|FullyQualifiedName~HeroSigilTests|FullyQualifiedName~StarCarryoverV131Tests|FullyQualifiedName~StarMigrationRefundTests|FullyQualifiedName~StarMapLayoutQualityTests.Tuned_placements_move_stars_only_and_never_touch_the_lines' --nologo -p:NuGetAudit=false`: exit 0、134件成功・失敗0・スキップ0。既存のテスト用スタブ等の警告あり。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~GeneratedHeroAcceptanceTests.Registration_succeeds_with_zero_rejections_and_installs_the_migration_rules|FullyQualifiedName~GeneratedHeroAcceptanceTests.Every_baseline_star_retains_its_id_ranks_and_keystone_definition|FullyQualifiedName~GeneratedHeroAcceptanceTests.Every_star_is_reachable_from_the_start|FullyQualifiedName~MovementChargeBonusTests' --nologo`: exit 0、31件成功・失敗0・スキップ0。全9旅人の登録、移行規則、旧星の保持、到達可能性と、登録解除後の旧効果への復帰を確認した。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release --nologo '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' '-p:ModDeployDir=/tmp/issue692-final-12tuwtgz/mod-build' -p:NuGetAudit=false`: exit 0、既存警告6件・エラー0。ゲームDLLを参照し、出力先を一時領域に指定した。
- `total` の前後各4回×2条件、`snapshot`、`snapshot-baseline`、`stages`、`initializers`: 上記と生の出力を参照。新規テストは追加していない。

## 未実行の検証

- Unity/Monoでのゲーム起動、タイトル画面到達、Player.log計測、報告者と同じMOD構成での再現: 実機環境と構成情報がないため。
- プロフィール読み込み、ロビー、遠征開始、ゾーン移動の計測: 計測ツールの対象外で、Issueにも場面の指定がないため。
- full/slowスイートと全旅人の最大点数購入の網羅テスト: 依頼に従い、変更部分に関係する既存テストだけを実行したため。
- 修正を戻して再現テストを失敗させる確認: ゲーム内症状の再現テストがブランチにないため。性能の前後比較は実施した。

## 判断したこと

FR-1とFR-2は同じ要望であり、起動時のMOD初期化のうち、コードで不要な処理を特定できる星図登録を対象にした。実機未再現でも通常の処理短縮として修正し、改善の判定は同条件の計測結果に限定した。登録順、移行規則、プロフィール読み込みの前に登録を済ませる順序は変えていない。処理の軽量化は `changelog.d/README.md` で断片の対象外とされているため、CHANGELOGと更新履歴の断片は変更していない。
