# Issue #692 の修正前計測

報告されたゲーム内の長い読み込みは未再現。起動処理の一部である星図登録に、手元の環境で約1.6〜2.6秒かかることを確認した。製品コードは修正していない。

## 対象と判断

Issue本文は「MODの読み込みが非常に長いので，改善可能か?」のみで、場面・所要時間・版・環境は書かれておらず、コメントもなかった。起動時の一括登録を最初の調査対象とした。`src/SodRpg.Mod/DreamforgeMod.cs:70` で、プロフィール読み込みより前に `StarClusters.RegisterAllGenerated` を同期実行しているためである。

このツールは同じCoreの登録APIを呼ぶ。ゲームのMODローダー、Harmonyのパッチ、画像の先読み、保存データの読み込み、タイトル画面の描画は測らない。1500 msは前回追加されたツールの仮基準で、報告者の許容時間や仕様上の性能要件ではない。基準超過の終了コード3だけでIssueの症状を再現したとは判定しない。

## 実行条件

- 製品コードの基点: `f31ce810`。計測ツールは前回の `e7c8a15c` を引き継ぎ、今回、再現判定の表示と測定条件の説明だけを訂正した。
- Linux x86_64、omarchy 4.0.2、Intel Core i7-6700HQ @ 2.60GHz、共有ホスト。
- SDK 10.0.111、実行ランタイム10.0.11。net8.0を対象にビルドし、`RollForward=Major` で実行した。
- Releaseビルド後、毎回新規プロセスを起動。通常JITと `DOTNET_TieredCompilation=0` を交互に測定した。ファイルキャッシュは消していない。
- 段階JITなしの結果は別条件の参考値で、Monoの所要時間や上限には換算しない。#459の.NET 8の測定とも直接比較しない。

```sh
dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
DOTNET_TieredCompilation=0 dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
DOTNET_TieredCompilation=0 dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
```

`total` は星図登録、初回BuildLimits、一覧取得1000回の順で測る。`stages` は旅人ごとの解析を実行してから同じ旅人の登録を行うため、解析のJITや型初期化が温まる。`total` の内訳や差分として扱わない。

## 測定結果

表示訂正後の全出力と終了コードは `issue-692-baseline.txt` に保存した。

| 新規プロセス | JIT条件 | 星図登録 | 初回BuildLimits | 一覧取得1000回 | 終了コード |
| --- | --- | --- | --- | --- | --- |
| 1 | 通常 | 2203 ms | 89 ms | 0 ms | 3 |
| 2 | 段階JITなし | 1637 ms | 58 ms | 0 ms | 3 |
| 3 | 通常 | 2039 ms | 99 ms | 0 ms | 3 |
| 4 | 段階JITなし | 1650 ms | 57 ms | 0 ms | 3 |

全4回で1500 ms超過を確認した。登録時の当該スレッドの累積メモリ確保は各172 MBだった。常駐メモリの量ではない。一覧取得の0 msは整数ミリ秒での表示で、処理時間がゼロであることを示さない。

表示訂正前の同条件の予備計測は通常2349／2624 ms、段階JITなし1695／1651 msで、すべて終了コード3だった。ホスト負荷が変動するため、単発の差を改善とみなさない。

`stages` は両条件とも9人を登録して終了コード0だった。通常JITで解析の合計228 ms、解析を含む登録の合計1877 ms、段階JITなしでそれぞれ319 ms／1367 msだった。登録前に解析を実行する測定順序の影響を含むため、差し引いて各処理の所要時間を確定できない。

## 内容の比較基準

`total` の全4回で次の値が一致し、`stages` の両回でも登録指紋が一致した。

- `AuthoredRegistryFingerprint`: `607b55b25354e173`
- `ContentFingerprint.Value`: `13792-e4617232a7ce91e6`
- `HeroSigils.All.Count`: `7642`
- `BuildLimits.Registered`: talents=`7657`、entries=`584`

今回、生成データや製品コードは変えていない。次工程で変更する際は、この版の修正前後で指紋と件数を比較する。#459の古い指紋とは比較しない。ID・順序・座標の全件出力による独立比較は未実行。

## 既存の対策と次工程の参照先

#459の候補はすでに実装されている。今回の未対応案として重ねて適用しない。

- 文字列表と共通リーダー: `src/SodRpg.Core/Game/StarClusters/Aurena.Generated.cs:9` の `ReadManifest`。
- TargetEffectIdsの配列共有: `src/SodRpg.Core/Game/StarClusters/ManifestReader.Generated.cs:11`。
- 調整済み配置が適用できる場合の計算省略: `src/SodRpg.Core/Game/HeroTreeLayout.Clusters.cs:154`。
- NormalizeAuthoredの辞書: `src/SodRpg.Core/Game/Mechanisms/AuthoredStarRegistration.cs:236`。
- 一覧の指紋キャッシュ: 同ファイル223行以降と `src/SodRpg.Core/Game/HeroSigils.cs:183`。

登録経路は `GeneratedRegistration.cs:82` → `RegisterGeneratedHero` → `RegisterAuthored` と `RegisterMigrations`。`RegisterAuthored` は正規化・検証・配置・指紋作成などを行う（`AuthoredStarRegistration.cs:114`）。各処理の時間は今回分離していない。次工程ではこの経路の残るコストを調べるために、本記録を修正前の比較値として使える。個別処理が遅延の主因であるとはまだ確定していない。

## 未実行

- Unity/Monoでのゲーム起動、タイトル画面到達、Player.log計測、報告者と同じMOD構成での再現。実機環境と報告者の構成情報がないため。
- プロフィール読み込み・ロビー・遠征開始・ゾーン移動の計測。今回のツールの対象外で、Issue本文にも場面の指定がないため。
- 改善後との比較。再現工程のため、製品コードを修正していない。
- MOD本体のビルド、既存の単体テスト、full/slowスイート。今回は計測ツールの表示と説明のみを変更し、ツールのビルドと実行で検証したため。

結果は `NOT_REPRODUCED` とする。ゲーム内の症状が未再現でも調査を止めず、次工程でコード上の因果を調べるための測定値を残した。
