# Issue #692 回復工程の検証記録

引き継いだ製品コードを確認し、修正前後を同じ条件で測り直した。星図登録の中央値は通常JITで2006.5→1732.5 ms（約14%短縮）、段階JITなしで1630.5→1499.5 ms（約8%短縮）だった。生成星図と旧星図の出力はそれぞれ全バイト一致し、関連する既存テスト165件が成功した。ゲーム内の長い読み込みは未再現で、MOD全体の読み込み時間は未計測である。

## 原因候補と修正

`DreamforgeMod.Awake` はプロフィール読み込みより前に `StarClusters.RegisterAllGenerated` を同期実行する（`src/SodRpg.Mod/DreamforgeMod.cs:72`）。生成星図の配置を登録するとき、修正前の `HeroTreeLayout` は型初期化で旧星図9人分の配置も作っていた。通常の生成星図登録ではこの旧配置を使わない。旧星図の配置を必要な旅人について初めて要求されたときだけ作る変更で、この余分な計算を省いた（`src/SodRpg.Core/Game/HeroTreeLayout.cs:58`）。登録済み配置を優先し、汎用配置の初期化は残している。

`FractionalScopedModifiers.ValidateTree` は修飾星ごとに全星を調べ、照合のたびに同じ候補の `GimmickEntry`、`AuthoredMechanismEntry` と寄与ID配列を作っていた。候補が多い生成星図ではこの確保が繰り返される。照合用オブジェクトを検証1回の中で共有し、仕掛けのある星だけを調べる変更で、繰り返しの確保を減らした（`src/SodRpg.Core/Game/FractionalScopedModifiers.cs:539`）。旅人の条件、対象の順序、照合関数と検証条件は保っている。

製品コードの変更は引き継いだ `71d684cc` の2ファイルのみ。今回の工程では追加修正を要する不具合は見つからず、検証記録と案内を追加した。公開済みブランチやPRの修復は担当範囲外のため行っていない。

## 実行条件

- 修正前は `3cdf2a83` のCore、修正後は作業開始時の `694c5fff` のCore。同じ `Program.cs` と `StartupBench.csproj` を使用した。
- Linux x86_64、SDK 10.0.111、実行ランタイム10.0.11、Release。net8.0を `RollForward=Major` で実行した。
- 通常JITと `DOTNET_TieredCompilation=0` を別条件として各4対測った。各条件の奇数回は修正前→修正後、偶数回は修正後→修正前の順。毎回新規プロセスを起動し、ファイルキャッシュは消していない。
- NuGetキャッシュは既存の `/home/wang/.cache/gh-auto/nuget` を使用した。比較用ソース・ビルド・大きな出力は `/home/wang/.cache/gh-auto/issue692-recovery-validation-20261018-vswj5n_8` に保存した。
- 最初の比較用ビルドは `/tmp` の容量制限でexit 1。ホーム配下へ切り替えた直後のビルドもreturncode -7で終了した。`TMPDIR` をホーム配下へ指定し、`DOTNET_EnableDiagnostics=0` と `UseSharedCompilation=false`、`nodeReuse:false` を前後共通で指定した後は、両方のビルドがexit 0だった。`DOTNET_EnableDiagnostics=0` と同じ `TMPDIR` は計測にも共通で指定した。

失敗時の出力は [最初のビルド](issue-692-recovery-validation-20261018.txt) と [出力先変更後のビルド](issue-692-recovery-build-20261018.txt)、容量情報は [容量の記録](issue-692-capacity.txt)、成功した前後のビルドは [再ビルドの記録](issue-692-recovery-build-isolated-20261018.txt) に残した。コマンドの画面出力を保存する実行環境側にも容量制限エラーが出たため、以後の結果はファイルに直接保存して読み取った。既存ファイルの削除は行っていない。

## 同条件の前後比較

全コマンド、出力と終了コードは [計測記録](issue-692-recovery-timings-20261018.txt) に保存した。

| JIT条件 | 回 | 登録・修正前→後 | 初回BuildLimits・修正前→後 |
| --- | --- | --- | --- |
| 通常 | 1 | 1931→1844 ms | 87→98 ms |
| 通常 | 2 | 2131→1634 ms | 89→89 ms |
| 通常 | 3 | 1880→1790 ms | 96→90 ms |
| 通常 | 4 | 2082→1675 ms | 129→96 ms |
| 段階JITなし | 1 | 1583→1417 ms | 58→50 ms |
| 段階JITなし | 2 | 1678→1490 ms | 50→48 ms |
| 段階JITなし | 3 | 1826→1579 ms | 50→52 ms |
| 段階JITなし | 4 | 1568→1509 ms | 59→49 ms |

| 条件 | 登録の中央値・前→後 | 登録＋初回BuildLimitsの中央値・前→後 |
| --- | --- | --- |
| 通常JIT | 2006.5→1732.5 ms（約14%短縮） | 2114.5→1825.5 ms（約14%短縮） |
| 段階JITなし | 1630.5→1499.5 ms（約8%短縮） | 1684.5→1548 ms（約8%短縮） |

合計の中央値は各回の合計から求めた。当該スレッドの登録中の累積確保量は全回172→118 MB（整数MB表示、約31%減）だった。常駐メモリの量ではない。通常JITでは前後全8回、段階JITなしでは修正前全4回と修正後2回が仮基準1500 msを超えてexit 3。修正後の残り2回はexit 0だった。1500 msは比較用の仮基準で、報告者の許容時間や仕様上の目標ではない。共有ホストの測定値には変動があり、短縮率は今回の条件に限る。過去の測定やUnity/Monoの所要時間へ換算しない。

## 読み込み内容と更新の反映

[内容比較の記録](issue-692-recovery-content-20261018.txt) のとおり、前後それぞれで `snapshot` と `snapshot-baseline` を実行した。生成星図15,514,554バイト、旧星図と汎用配置251,195バイトがそれぞれ全バイト一致した。定義、ID、ランク、コスト、登録順、座標、接続とその順序、移行規則を比較した。

全16回の登録指紋は `607b55b25354e173`、内容指紋は `13792-e4617232a7ce91e6`、星数は7642、容量はtalents=7657・entries=584で一致した。既存APIの出力を確認したもので、追加のハッシュ検証は行っていない。

`ForHero` は毎回、現在の登録済み配置を先に取得する（`HeroTreeLayout.cs:83`）。旧配置のキャッシュは登録状態に依存しない `BaselineTreeFor` だけから作る（同`:93`）。`RegisterAuthored` は渡された定義から新しい配置を作り、登録を置き換え、一覧キャッシュを無効にする（`Mechanisms/AuthoredStarRegistration.cs:114`）。照合用オブジェクトの共有は `ValidateTree` 内だけで、次回の登録では作り直す。照合関数はこれらのオブジェクトを書き換えない（`FractionalScopedModifiers.cs:511`、`Mechanisms/AuthoredMechanisms.cs:266`）。

再登録する既存の `AuthoredClusterPlacementTests` と、生成星図を登録・解除する `MovementChargeBonusTests` も今回成功した。永続キャッシュ、MODファイルの走査と依存関係の解決は変更していない。利用者のMODファイルや設定も変更していない。

## 実行した検証

- 修正前後の `dotnet build .../tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false`: 両方exit 0、警告0・エラー0。失敗した初回のビルドは上記のとおり別記した。
- `total --threshold-ms 1500`: 修正前後各4回×2条件を実行。exit 0が2回、仮基準超過のexit 3が14回。
- 前後の `snapshot`、`snapshot-baseline` と全バイト比較: すべてexit 0、一致。修正後の `stages` と `initializers` もexit 0。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter '<関連クラスとメソッド>' --nologo -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false`: exit 0、165件成功・失敗0・スキップ0。フィルター全文と既存のスタブ等の警告は [テスト記録](issue-692-recovery-tests-20261018.txt) に保存した。新規テストは追加していない。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release --nologo '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' '-p:ModDeployDir=/home/wang/.cache/gh-auto/issue692-recovery-validation-20261018-vswj5n_8/mod-build' -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false`: exit 0、既存警告6件・エラー0。[MODビルド記録](issue-692-recovery-mod-build-20261018.txt) に保存した。参照用ゲームDLLを使用し、利用者のゲームフォルダへ配置していない。

## 残るコストと未実行

修正後も定義の解析・正規化、所有記憶や参照の検証、容量解析、配置、移行規則の登録が残る。追加の `stages` 計測は解析のみ244 ms、解析＋登録＋移行1561 msだった。登録前に解析するため、JITと型初期化が温まっており、`total` の内訳には換算できない。`initializers` は旧星図定義74 ms、最初のCetus配置313 msだったが、他の初期化も含むため単独の短縮効果とは扱わない。

- 未実行: Unity/Monoでのゲーム起動、Player.log計測、報告者と同じMOD構成での症状再現。実機環境と構成情報がないため。
- 未実行: MOD全体、プロフィール読み込み、ロビー、遠征開始、ゾーン移動の時間計測。Core用ツールの対象外で、Issueにも場面の指定がないため。
- 未実行: full/slowスイート、変更範囲外のテスト。依頼に従い、変更部分に関係する既存テストに限定したため。
- 未実行: 修正を戻して再現テストを失敗させる確認。報告された症状の再現テストがないため。`verification.revert_check` は空文字とする。
- 未実行: push、PR作成、公開済みブランチの修復。担当範囲はcommitと結果ファイルの保存までのため。

## 判断したこと

FR-1とFR-2は同じ要望と解釈し、起動時の同期的な星図登録を対象に、結果を変えずに省ける処理を短縮した。引き継いだ修正は作り直さず、今回の検証で内容の一致と同条件での時間短縮を確認した。ゲーム内症状の解消は断定しない。処理の軽量化は `changelog.d/README.md:9` で断片の対象外とされているため、更新履歴の断片は追加していない。
