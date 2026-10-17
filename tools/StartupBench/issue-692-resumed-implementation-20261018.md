# Issue #692 再開した実装工程の検証記録

引き継いだ製品修正 `71d684cc` を確認し、現在のコードで前後比較と関連テストを実行した。通常JITで星図登録の中央値は4801.5→4265.5 msに短縮した。段階JITなしでは3318→3581 msとなり、今回の測定では短縮を確認できなかった。登録中の累積確保量は全回172→118 MBに減った。生成星図と旧星図の出力はそれぞれ全バイト一致し、既存テスト165件が成功した。ゲーム内症状は未再現で、MOD全体の読み込み時間は測っていない。

## 確認した差分と原因候補

開始時のブランチは `ai/issue-692`、HEADは `23b9a1fa`、作業ツリーはクリーンだった。`gh issue view 692 -R Kling0012/shape-of-dreams-rpg-plan --comments` は出力がなく、`rtk bypass` でも同じだった。`gh api` で本文「MODの読み込みが非常に長いので，改善可能か?」、状態open、コメント0件を確認した。読み込みの場面や所要時間、報告者の環境は書かれていない。

`git diff 3cdf2a83 HEAD -- src` の製品変更は次の2ファイルだけだった。今回の工程では追加修正を要する不具合は見つからず、引き継いだ実装は変更していない。

- `src/SodRpg.Core/Game/HeroTreeLayout.cs:58`: 生成星図の登録で別途配置を作る際、型初期化が未使用の旧星図9人分の配置も作っていた。旧配置を必要な旅人について初めて要求されたときだけ生成し、同じ旅人で再利用する変更で、通常の登録時の不要な計算を省いた。汎用配置の初期化は残している。
- `src/SodRpg.Core/Game/FractionalScopedModifiers.cs:539`: 修飾星ごとに全星を走査し、候補の照合用オブジェクトと寄与ID配列を毎回作っていた。仕掛けのある星だけを候補にして、検証1回の中で照合用オブジェクトを共有する変更で、繰り返しの確保を減らした。照合関数と検証条件は同じである。

`src/SodRpg.Mod/DreamforgeMod.cs:70` ではプロフィール読み込み前に星図を同期登録するため、この処理のコストが起動時の待ち時間に加わる。原因候補はこの不要な計算と確保であり、ゲーム内で報告された待ち時間の全原因が分かったという意味ではない。

## 同条件の前後比較

修正前は `3cdf2a83` のCoreを一時領域に展開し、修正後は開始時のHEADのCoreを使用した。計測の `Program.cs` と `StartupBench.csproj` は前後で同じものを使った。コマンド、出力、終了コードは [ビルド・計測・内容比較のログ](issue-692-resumed-implementation-20261018.txt) に保存した。

- Linux x86_64、SDK 10.0.111、実行ランタイム10.0.11、Release。net8.0を `RollForward=Major` で実行した。
- 通常JITと `DOTNET_TieredCompilation=0` を各4対測定した。奇数回は修正前→後、偶数回は修正後→前の順とし、毎回新規プロセスを起動した。ファイルキャッシュは消していない。
- 前後とも `DOTNET_EnableDiagnostics=0` と同じ `TMPDIR` を使用した。パッケージキャッシュは既存の `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使用した。
- 比較用ソースと大きな出力は `/home/wang/.cache/gh-auto/issue692-implementation-resume-20261018-efadeeo6` に保存した。利用者のMODファイルや設定は変更していない。

| 条件 | 回 | 星図登録・修正前→後 | 初回BuildLimits・修正前→後 |
| --- | --- | --- | --- |
| 通常JIT | 1 | 4608→3871 ms | 184→144 ms |
| 通常JIT | 2 | 5239→4344 ms | 163→164 ms |
| 通常JIT | 3 | 4995→5212 ms | 143→169 ms |
| 通常JIT | 4 | 4576→4187 ms | 145→191 ms |
| 段階JITなし | 1 | 3279→3687 ms | 113→94 ms |
| 段階JITなし | 2 | 3810→3475 ms | 150→89 ms |
| 段階JITなし | 3 | 3319→3847 ms | 92→121 ms |
| 段階JITなし | 4 | 3317→3265 ms | 101→111 ms |

| 条件 | 登録の中央値・修正前→後 | 登録＋初回BuildLimitsの中央値・修正前→後 |
| --- | --- | --- |
| 通常JIT | 4801.5→4265.5 ms（約11%短縮） | 4965→4443 ms（約11%短縮） |
| 段階JITなし | 3318→3581 ms（約8%増加） | 3414.5→3672.5 ms（約8%増加） |

合計の中央値は各回の合計から求めた。全16回が仮基準1500 msを超え、exit 3だった。この基準は比較用であり、Issueの受け入れ基準ではない。累積確保量172→118 MBは当該スレッドの整数MB表示で、常駐メモリの量ではない。

過去の [回復工程の記録](issue-692-recovery-validation-20261018.md) より所要時間が大きく変動している。ホスト負荷などの影響は分離していないため、今回の結果から段階JITなしでの短縮やUnity/Monoでの効果は主張しない。通常JITで確認できた短縮も、この条件と実行回数の範囲に限る。

## 読み込み内容と更新の反映

前後の `snapshot` は生成星図15,514,554バイト、`snapshot-baseline` は旧星図と汎用配置251,195バイトがそれぞれ全バイト一致した。生成星図9人分の定義、ID、ランク、コスト、登録順、座標、接続とその順序、移行規則を比較した。全16回の登録指紋は `607b55b25354e173`、内容指紋は `13792-e4617232a7ce91e6`、星一覧は7642件、容量はtalents=7657・entries=584で一致した。既存APIの指紋を確認したもので、追加のハッシュ検証は行っていない。

更新の扱いはコードと関連テストで確認した。`HeroTreeLayout.ForHero` は現在の登録済み配置を毎回優先し、旧配置だけを `BaselineTreeFor` から生成して保持する（`HeroTreeLayout.cs:83`、同`:93`）。`RegisterAuthored` は渡された定義から新しい配置を作り、登録を置き換え、一覧キャッシュを無効にする（`Game/Mechanisms/AuthoredStarRegistration.cs:114`）。`ValidateTree` の照合用オブジェクトは呼び出しごとに作り直し、照合関数はそれらを書き換えない。

既存の `AuthoredClusterPlacementTests` の再登録、`AuthoredMechanismIntegrationTests.Captured_purchase_cannot_commit_after_shared_registry_or_cap_identity_changes` の定義変更、`MovementChargeBonusTests` の生成星図の登録・解除も今回成功した。永続キャッシュ、MODファイルの走査、依存関係の解決、起動時の登録順は変更していない。ゲームのMODローダー全体を比較した結果とは扱わない。

## 実行した検証

- 修正前後の `dotnet build .../tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false`: 両方exit 0、警告0・エラー0。
- `total --threshold-ms 1500`: 前後各4回×2条件、全16回exit 3。仮基準超過として記録した。
- 前後の `snapshot`、`snapshot-baseline` と全バイト比較: 各コマンドと比較処理がexit 0、両方一致。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter '<関連クラス・メソッド>' --nologo -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false`: exit 0、165件成功・失敗0・スキップ0。[テストログ](issue-692-resumed-tests-20261018.txt) にフィルター全文と既存の警告を保存した。新規テストは追加していない。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release --nologo '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' '-p:ModDeployDir=/home/wang/.cache/gh-auto/issue692-implementation-resume-20261018-efadeeo6/mod-build' -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false`: exit 0、既存警告6件・エラー0。[MODビルドログ](issue-692-resumed-mod-build-20261018.txt) に保存した。参照用ゲームDLLでビルドし、利用者のゲームフォルダへ配置していない。

## 未実行と残るコスト

- 未実行: Unity/Monoでのゲーム起動、Player.logの計測、報告者と同じ構成での症状再現。実機環境と構成情報がないため。
- 未実行: MOD全体、パッチ適用、画像の先読み、プロフィール読み込み、ロビー、遠征開始、ゾーン移動の時間計測。既存のCore用ツールの対象外で、Issueに場面の指定もないため。
- 未実行: full/slowスイートと変更範囲外のテスト。依頼で禁止され、変更部分の検証に限定したため。
- 未実行: `stages`、`initializers` の再実行。最終コードの前後時間と内容の比較には不要で、内訳は過去の記録を参照するため。
- 未実行: 修正を戻して再現テストを失敗させる確認。報告されたゲーム内症状の再現テストがないため。`verification.revert_check` は空文字とする。
- 未実行: push、PR作成、公開済みブランチの修復。担当範囲はcommitと結果ファイルの保存までのため。

修正後も定義の解析・正規化、所有記憶や参照の検証、容量解析、配置、移行規則の登録は残る。内容を読み飛ばさず、検証も省略していない。

## 判断したこと

FR-1とFR-2は同じ要望と解釈し、起動時に同期実行する星図登録を対象に、内容を変えずに省ける計算と確保を減らした。引き継いだ実装は作り直さず、今回の検証記録と案内を追加した。通常JITの短縮と内容の一致を根拠に、この対象範囲の改善を完了とする。段階JITなしでは今回短縮を確認できず、ゲーム全体の読み込み改善は断定しない。処理の軽量化は `changelog.d/README.md:9` で断片の対象外とされているため、更新履歴の断片は追加していない。
