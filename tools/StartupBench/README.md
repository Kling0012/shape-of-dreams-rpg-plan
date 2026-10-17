# Issue #692 の起動処理の計測

起動時の星図登録で、旧星図9人分の不要な配置計算と、修飾対象を表すオブジェクトの繰り返し生成を省いた。同条件の新規プロセスで前後を各4回測定し、星図登録と初回容量計算の合計中央値は通常JITで2168→1800.5 ms、段階JITなしで1738→1627 msだった。ゲーム内の長い読み込みは未再現で、ゲーム全体の読み込み時間は測っていない。

## 対象と実行条件

Issue本文は「MODの読み込みが非常に長いので，改善可能か?」のみで、場面・所要時間・版・環境は書かれておらず、コメントもなかった。`src/SodRpg.Mod/DreamforgeMod.cs` の `Awake` がプロフィール読み込みより前に同期実行する `StarClusters.RegisterAllGenerated` を対象とした。

このツールは同じCoreの登録APIを呼ぶ。MODローダー、Harmonyのパッチ、画像の先読み、保存データの読み込み、タイトル画面の描画は測らない。1500 msは比較用の仮基準で、報告者の許容時間や仕様上の性能要件ではない。終了コード3は基準超過を表し、Issueの症状を再現したとは判定しない。

- Linux x86_64、omarchy 4.0.2、Intel Core i7-6700HQ @ 2.60GHz、共有ホスト。
- SDK 10.0.111、実行ランタイム10.0.11。net8.0を対象にビルドし、`RollForward=Major` で実行した。
- Releaseビルド後、毎回新規プロセスを起動。ファイルキャッシュは消していない。
- 通常JITと `DOTNET_TieredCompilation=0` は別条件。Monoの性能や#459の.NET 8の測定へ換算しない。

```sh
dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
DOTNET_TieredCompilation=0 dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll snapshot > generated.txt
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll snapshot-baseline > baseline.txt
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll initializers
```

`total` は星図登録、初回BuildLimits、一覧取得1000回の順で測る。`stages` は旅人ごとの解析を実行してから登録するため、解析のJITや型初期化が温まる。`total` の内訳や差分として扱わない。

`snapshot` は生成星図の定義、ID、ランク、コスト、登録順、座標、接続とその順序、移行規則、内容指紋を出す。`snapshot-baseline` は生成星図を登録せず、旧星図全員と汎用配置を出す。同じ条件の変更前後で出力全体を比較する。`initializers` は旧星図の定義を先に読み、その後Cetusの配置を初めて要求する時間を分けて測る。他の初期化も含むため、登録時間の短縮量には換算しない。

## 修正前の記録

再現工程で保存した `issue-692-baseline.txt` は、製品コード `f31ce810` の修正前計測である。通常JITの登録は2203／2039 ms、初回BuildLimitsは89／99 ms。段階JITなしでは登録1637／1650 ms、初回BuildLimits58／57 msだった。登録時の当該スレッドの累積確保量は各172 MB、全4回が終了コード3だった。

当時の判定は `NOT_REPRODUCED`。ゲーム内の症状は未再現でも調査を続け、以下のコード上の原因候補を修正した。

## 変更した処理

`HeroTreeLayout` は初回利用時、旧星図全9人の配置を作っていた。生成星図の登録は別途その旅人の配置を作るため、旧星図の配置は通常の登録経路では使われない。旧星図は `ForHero` で必要になった旅人だけ生成し、`Lazy` と並行辞書で保持するようにした。登録済み配置を優先し、フォールバックには `HeroSigils.BaselineTreeFor` を使う。登録解除後に生成星図の配置がキャッシュから戻ることを避けるためである。汎用配置は従来どおり初期化し、`Content` の初期化を後段へ移さない。

`FractionalScopedModifiers.ValidateTree` は修飾星ごとに全星を調べ、候補ごとに `GimmickEntry`、`AuthoredMechanismEntry` と寄与ID配列を作っていた。候補を仕掛けのある星に絞り、照合用オブジェクトを検証1回の中で共有するようにした。照合関数、旅人の条件、候補の順序、対象なし・不正な対象・上限の検証は保つ。登録をまたぐキャッシュは追加しない。

## 同条件の前後比較

2026-10-17に実施。修正前は `677c8431` のCoreを一時領域へ展開し、今回追加した計測コマンドと同じ `Program.cs` でビルドした。修正後は本ブランチのCoreを使った。製品変更は上記2ファイルに限る。

各回で通常JIT、段階JITなしの順に測定した。奇数回は修正前→修正後、偶数回は修正後→修正前の順に、新規プロセスを起動した。次の全出力と終了コードは `issue-692-comparison.txt` に保存した。

| JIT条件 | 回 | 登録・修正前 | 登録・修正後 | 初回BuildLimits・修正前 | 初回BuildLimits・修正後 |
| --- | --- | --- | --- | --- | --- |
| 通常 | 1 | 2040 ms | 1730 ms | 89 ms | 77 ms |
| 通常 | 2 | 1940 ms | 1666 ms | 96 ms | 95 ms |
| 通常 | 3 | 2467 ms | 1845 ms | 75 ms | 74 ms |
| 通常 | 4 | 2114 ms | 1705 ms | 93 ms | 89 ms |
| 段階JITなし | 1 | 1832 ms | 1797 ms | 64 ms | 50 ms |
| 段階JITなし | 2 | 1541 ms | 1502 ms | 75 ms | 53 ms |
| 段階JITなし | 3 | 1689 ms | 1588 ms | 46 ms | 61 ms |
| 段階JITなし | 4 | 1694 ms | 1559 ms | 47 ms | 46 ms |

| 条件 | 登録の中央値・前→後 | 登録＋初回BuildLimitsの中央値・前→後 |
| --- | --- | --- |
| 通常JIT | 2077→1717.5 ms（約17%短縮） | 2168→1800.5 ms（約17%短縮） |
| 段階JITなし | 1691.5→1573.5 ms（約7%短縮） | 1738→1627 ms（約6%短縮） |

全16プロセスで仮基準1500 msを超え、終了コード3だった。仮基準を満たしたとは報告しない。共有ホストの負荷による変動があり、短縮率はこの測定の範囲に限る。登録だけでなく初回容量計算との合計も短縮した。なお、合計の中央値は各回の合計から求めており、区間ごとの中央値を足した値とは限らない。

その後、フォールバック取得に使うデリゲートも静的に保持した。最終ビルドの通常JITでの追加確認は登録2076 ms、初回BuildLimits86 ms、累積確保量118 MB、終了コード3だった。この追加確認は修正前と対にしていないため、上表の中央値には含めない。最終ビルドでも内容の全出力一致を確認した。

登録時の当該スレッドの累積確保量は全回で172→118 MB（整数MB表示、約31%減）。常駐メモリの量ではない。一覧取得1000回は全回0 msだったが、整数ミリ秒表示のため処理時間がゼロという意味ではない。

汎用配置も遅延生成した途中案では、初回BuildLimitsが300〜500 msへ増えたため採用しなかった。旧星図の遅延生成だけの比較では短縮が測定のばらつきに埋もれたため、上表は修飾対象の再利用を含む最終案全体の効果として扱う。

## 内容と検証結果

前後の `snapshot` と `snapshot-baseline` の出力全体がそれぞれ一致した。生成星図9人分、旧星図9人分と汎用配置の出力を比較した。照合した出力は約15.5 MB／0.25 MBのためリポジトリには追加していない。再実行する場合は上記コマンドを同じデータ版の前後で実行する。

計測全16プロセスで以下も一致した。

- `AuthoredRegistryFingerprint`: `607b55b25354e173`
- `ContentFingerprint.Value`: `13792-e4617232a7ce91e6`
- `HeroSigils.All.Count`: `7642`
- `BuildLimits.Registered`: talents=`7657`、entries=`584`

StartupBenchとCoreのReleaseビルドは警告0・エラー0。参照用ゲームDLLでMOD本体もビルド成功した（既存警告6件）。`ModDeployDir` は `/tmp/issue692-implementation-20261017/mod-build` に指定し、利用者のMODファイルや設定は変更していない。

関連する既存テスト108件が成功した。対象は `AuthoredClusterPlacementTests`、`StarMapClusterTests`、`AuthoredStarContractTests`、`BuildLimitsV131Tests`、`FractionalScopedModifiersTests`、`ScopedKeystoneModifiersTests`、`AuthoredMechanismIntegrationTests` と、`StarMapLayoutQualityTests` の `Tuned_placements_move_stars_only_and_never_touch_the_lines`。新規テストは追加していない。

## 残るコストと未実行

登録は修正後も約1.5〜1.8秒かかり、定義の解析・正規化、所有記憶や参照の検証、容量解析、配置、移行規則の登録が残る。個々の処理を飛ばさず、これらの処理全体を対象にした追加の短縮は今回行わない。#459の文字列表化、TargetEffectIds共有、調整済み配置がある場合の計算省略、正規化の辞書、一覧キャッシュは既存の対策であり、今回の変更として数えない。

- Unity/Monoでのゲーム起動、タイトル画面到達、Player.log計測、報告者と同じMOD構成での再現は未実行。実機環境と構成情報がないため。
- プロフィール読み込み・ロビー・遠征開始・ゾーン移動の計測は未実行。ツールの対象外で、Issue本文にも場面の指定がないため。
- full/slowスイートは未実行。依頼に従って変更部分に関係する既存テストに限定したため。
- 修正を戻して再現テストを失敗させる検証は未実行。報告されたゲーム内症状の再現テストがないため。
