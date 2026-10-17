# Issue #692 の起動処理の計測

`DreamforgeMod.Awake` がプロフィール読み込み前に実行する `StarClusters.RegisterAllGenerated` と、初回の `BuildLimits.Registered` を計測する。MODローダー、Harmony、画像、保存データ、画面描画を含むゲーム全体の起動時間は測らない。

2026-10-18の最終比較はmain `ff9af9cf` と修正コミット `7886a827` のCoreを対象とした。同じ計測プログラムを使い、Releaseの新規プロセスで各4回測定した。今回の差分による追加の時間短縮は確認できなかった。

| 条件 | 登録の中央値・main→修正後 | 登録＋初回BuildLimitsの中央値・main→修正後 |
| --- | --- | --- |
| 通常JIT | 1118.5→1407.5 ms | 1427.5→1498 ms |
| 段階JITなし | 1116.5→1494 ms | 1503→1541.5 ms |

登録時の当該スレッドの累積確保量は120→118 MBだった。共有ホストでの測定であり、Monoや報告者の環境へ換算しない。過去のmainに対する改善率は、現在の差分の効果として扱わない。条件、各回の数値、検証結果は [最終検証記録](issue-692-final-validation.md) にまとめた。

## 実行方法

SDK 10.0.111、実行ランタイム10.0.11、Linux x86_64で確認した。net8.0を対象にビルドし、`RollForward=Major` で実行する。パッケージキャッシュには既存のworktree外の `NUGET_PACKAGES` を使う。

```sh
dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
DOTNET_TieredCompilation=0 dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll snapshot > generated.txt
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll snapshot-baseline > baseline.txt
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll initializers
```

- `total`: 星図登録、初回容量計算、一覧取得1000回を順に測る。1500 msは比較用の仮基準で、終了コード3は登録時間が基準を超えたことだけを表す。
- `stages`: 旅人ごとに解析のみと解析＋登録＋移行を測る。先に解析するためJITが温まり、`total` の内訳には換算できない。
- `snapshot`: 生成星図9人分の定義、登録順、ランク、コスト、座標、接続順、移行規則と内容指紋を出す。
- `snapshot-baseline`: 生成星図を登録せず、旧星図9人分と汎用配置を出す。
- `initializers`: 旧星図の定義取得とCetusの初回配置取得を分けて測る。他の型初期化も含む。

内容の比較では同じ `Program.cs` とデータ版を使ってmainと修正後を別々にビルドする。各モードの出力全体をバイト列として比較し、指紋だけの一致で判定しない。

## 変更と確認範囲

旧星図の遅延生成と修飾対象オブジェクトの再利用はmainに取り込み済み。現在の製品差分は `HeroTreeLayout` の `HasTree` 分岐・即時初期化の `Generic`・並行辞書と `Lazy`、および `ValidateTree` の全候補走査である。修飾対象を `TargetEffectIds` 順に先に絞る処理を外し、照合関数が一致と判定した全対象をツリー順に検証する。

修正コミットで `snapshot` の15,514,924バイトと `snapshot-baseline` の251,195バイトがmainと完全一致した。既存Coreテスト165件と再読み込みテスト4件が成功した。再読み込みテストの1件は既存のSkip指定で実行されなかった。新規テストは追加していない。

Unity/Monoでのゲーム起動と報告者のMOD構成での確認は、実機環境と構成情報がないため未実行。full/slowスイートは依頼に従い未実行。残る登録コストは定義の解析・正規化、参照と修飾対象の検証、容量解析、配置、移行規則の登録である。
