# Issue #692 再現工程の記録 2026-10-18

判定は `NOT_REPRODUCED`。修正前コードの星図登録は新規プロセスで3801〜5257 msかかった。4回とも計測ツールの仮基準1500 msを超え、終了コード3だった。ゲームの読み込み画面で報告された症状は再現していない。

## Issueと引き継いだ状態

`gh issue view 692 -R Kling0012/shape-of-dreams-rpg-plan --comments` は出力がなく、`rtk bypass` を付けても同じだった。`gh api` で取得した本文は「MODの読み込みが非常に長いので，改善可能か?」で、コメントは0件。場面、所要時間、ゲームとMODの版、MOD構成の記載はない。

開始時のブランチは `ai/issue-692`、HEADは `0c426af4` で、作業ツリーはcleanだった。既存の再現ツール、製品コード2ファイルの修正、前後比較の記録を引き継いだ。本工程では製品コードと再現ツールを変更していない。既存の修正を戻す操作も行っていない。

## 修正前の再現試行

`git diff --exit-code f31ce810 3cdf2a83 -- src` が終了コード0になることを確認した。修正前の製品コードと既存の `tools/StartupBench` をコミット `3cdf2a83` から `/tmp/issue692-repro-20261018-3_wvjxh4` へ展開し、Releaseでビルドした。警告0件・エラー0件、終了コード0だった。

環境はLinux x86_64、omarchy 4.0.2、SDK 10.0.111、実行ランタイム10.0.11。net8.0を対象に、既存の `RollForward=Major` で実行した。パッケージキャッシュはworktree外の `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使った。毎回新規プロセスを起動し、ファイルキャッシュは消していない。

```sh
dotnet build /tmp/issue692-repro-20261018-3_wvjxh4/tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false
dotnet /tmp/issue692-repro-20261018-3_wvjxh4/tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
DOTNET_TieredCompilation=0 dotnet /tmp/issue692-repro-20261018-3_wvjxh4/tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
dotnet /tmp/issue692-repro-20261018-3_wvjxh4/tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
```

通常JITと段階JITなしを交互に各2回実行した。コマンド、全出力、終了コードは [計測ログ](issue-692-reproduction-20261018.txt) に保存した。

| 条件 | 星図登録 | 初回BuildLimits | 登録時の累積確保量 | 終了コード |
| --- | --- | --- | --- | --- |
| 通常JIT・1回目 | 5257 ms | 205 ms | 172 MB | 3 |
| 段階JITなし・1回目 | 3927 ms | 115 ms | 172 MB | 3 |
| 通常JIT・2回目 | 4811 ms | 190 ms | 172 MB | 3 |
| 段階JITなし・2回目 | 3801 ms | 105 ms | 172 MB | 3 |

全4回で `RESULT: SLOW` を確認した。失敗内容は仮基準1500 msの超過で、この基準はIssueの受け入れ要件ではない。ゲームの症状の再現成功とは扱わない。今回は修正後を測っていないため、過去の記録との差から改善や悪化を判定しない。

`stages` は終了コード0。解析のみの合計577 ms、解析・登録・移行規則の合計4492 msだった。解析を先に実行するためJITや型初期化が温まり、`total` の内訳には使えない。

全4回で登録指紋 `607b55b25354e173`、内容指紋 `13792-e4617232a7ce91e6`、星一覧7642件、BuildLimitsのtalents=7657・entries=584が一致した。既存APIの出力を記録したもので、追加のハッシュ検証は行っていない。この一致は修正前コードの繰り返し実行に限る。

## 次工程へ渡す事実

`src/SodRpg.Mod/DreamforgeMod.cs:72` はプロフィールを読む前に星図登録を同期実行する。初期化経路にはパッチの適用、画像の先読み、プロフィールの処理もある。今回のCore用ツールはこれらを測らないため、星図登録の秒数をMOD全体の読み込み時間へ換算できない。

#459の対策として、調整済み配置がそろう場合の計算省略は `HeroTreeLayout.Clusters.cs:154`、正規化の辞書は `Mechanisms/AuthoredStarRegistration.cs:236`、一覧キャッシュは同ファイルの223行以降に既にある。`RegisterAuthored` は同じ内容の再登録でも136行で一覧キャッシュを無効にする。既存対策を未対応として重ねて実装しない。

受け付け審査の内容・順序・更新反映の懸念について、本工程では製品コードやキャッシュを追加・変更していない。引き継いだ修正の前後比較と内容一致の記録は [既存の最終検証記録](issue-692-final-validation.md) にある。本セッションで再実行した検証としては扱わず、次工程では既存差分を確認し、採用する最終コードの内容・順序・更新反映を検証する。

## 未実行と未完了

- Unity/Monoでのゲーム起動、読み込み画面、Player.log計測、報告者と同じMOD構成での再現は未実行。実機環境と構成情報がないため。
- パッチの適用、画像の先読み、プロフィールの読み込みは未計測。既存のCore用ツールの対象外のため。
- 修正後のビルドと前後比較、snapshotの比較、既存テスト、full/slowスイートは未実行。本工程は修正前の再現確認に限定し、full/slowスイートは依頼で禁止されているため。
- FR-1・FR-2の改善完了は本工程では判定しない。次工程へ修正前の事実と既存差分を引き継ぐ。
- push失敗の復旧、push、PR作成は未実行。本ワーカーの担当範囲外で、公開操作は禁止されているため。
