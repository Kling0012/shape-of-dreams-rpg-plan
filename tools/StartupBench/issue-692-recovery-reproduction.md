# Issue #692 再現工程の再確認

判定は `NOT_REPRODUCED`。今回の再計測では、修正前の星図登録に新規プロセスで1668〜2152 msかかった。全4回で仮基準1500 msを超え、終了コード3だった。ゲーム起動から `Loaded DreamforgeRPG` までの症状は再現していない。1500 msは既存ツールの比較用の仮基準であり、Issueの受け入れ基準ではない。

## 2026-10-18の再計測

開始時のブランチは `ai/issue-692`、HEADは `6cc00966`、作業ツリーはクリーンだった。過去の修正と検証記録は引き継ぎ、本工程では製品コード・計測ツール・テストを変更していない。以下の「引き継いだ状態」以降は前回の再現工程の記録として残した。

指定の `gh issue view 692 -R Kling0012/shape-of-dreams-rpg-plan --comments` は出力がなかった。同じIssueを `--json number,title,body,comments,state` で取得し、本文「MODの読み込みが非常に長いので，改善可能か?」、コメント0件、状態OPENを確認した。所要時間、版、構成、読み込みの場面は書かれていない。

`git diff --exit-code f31ce810 3cdf2a83 -- src` は終了コード0だった。修正前コミット `3cdf2a83` のCoreと既存のStartupBenchを `/home/wang/.cache/gh-auto/issue692-repro-resume-20261018-jg75hrz_` へ展開した。Releaseビルドは終了コード0、警告0件・エラー0件だった。

環境はLinux x86_64、omarchy 4.0.2、SDK 10.0.111、ランタイム10.0.11。net8.0を既存の `RollForward=Major` で動かした。`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`、`TMPDIR=/home/wang/.cache/gh-auto/issue692-repro-resume-20261018-jg75hrz_/tmp`、`DOTNET_EnableDiagnostics=0` を全実行に指定した。通常JITと `DOTNET_TieredCompilation=0` を交互に各2回、新規プロセスで測定し、ファイルキャッシュは消していない。

コマンド・全出力・終了コードは [計測ログ](issue-692-recovery-baseline.txt) の `RECOVERY REPRODUCTION SESSION 2026-10-18; inherited HEAD=6cc00966` 以降に追記した。

| 条件 | 星図登録 | 初回BuildLimits | 登録時の累積確保量 | 終了コード |
| --- | --- | --- | --- | --- |
| 通常JIT・1回目 | 1906 ms | 99 ms | 172 MB | 3 |
| 段階JITなし・1回目 | 1668 ms | 55 ms | 172 MB | 3 |
| 通常JIT・2回目 | 2152 ms | 81 ms | 172 MB | 3 |
| 段階JITなし・2回目 | 1707 ms | 46 ms | 172 MB | 3 |

`stages` は終了コード0で、解析のみの合計195 ms、解析・登録・移行規則の合計1679 msだった。解析を先に実行するため、`total` の内訳には使わない。全4回で登録指紋 `607b55b25354e173`、内容指紋 `13792-e4617232a7ce91e6`、星一覧7642件、BuildLimitsのtalents=7657・entries=584が一致した。登録中の累積確保量172 MBは常駐メモリを表さない。

`DreamforgeMod.cs:72` の星図登録は、プロフィール読み込みと画像の先読みより前に同期実行される。既存ツールはこのCore処理を測り、MODローダー、Harmony、画像、保存データ、タイトル画面までの時間は測らない。ゲーム内症状の再現と修正前に失敗する再現テストは確認できていない。

受け付け審査の懸念について、本工程で内容・順序・更新反映を変える修正は行っていない。前後の全出力比較と関連テスト165件成功は [前回の検証記録](issue-692-recovery-validation-20261018.md) にある。本セッションでは再実行していないため、読み込み結果の同一性を今回確認済みとは扱わない。次工程では既存差分を確認し、最終コードの検証結果として判断する必要がある。

未実行はUnity/Monoでのゲーム起動とPlayer.log計測、報告者と同じ構成での再現（実機環境と構成情報がないため）、修正後のビルド・前後比較・内容比較・既存テスト（再現工程のため）、full/slowスイート（依頼で禁止）、push失敗の復旧とPR公開（本ワーカーには禁止）。FR-1・FR-2の完了は判定していない。再現工程の結果を `.gh-auto-result.json` に保存し、未再現でも次のコード解析・修正工程へ進めるための事実を残した。

## 引き継いだ状態

ブランチ `ai/issue-692` の開始時点は `71d684cc`。再現用ツールと記録のコミット `e7c8a15c`、`3cdf2a83` に加え、製品コード2ファイルの修正と前後比較の記録が含まれていた。本工程では既存の修正を変更せず、修正前コミット `3cdf2a83` を一時領域に展開して再確認した。

`gh issue view 692 -R Kling0012/shape-of-dreams-rpg-plan --comments` は出力がなかったため、`gh api` で本文とコメントを取得した。本文は「MODの読み込みが非常に長いので，改善可能か?」で、コメントは0件。読み込みの場面、版、所要時間、MOD構成の記載はなかった。

## 再現手段と結果

既存の `tools/StartupBench` を使い、修正前コミットの `src/SodRpg.Core` とともに `/tmp/issue692-repro-recheck-dt_liad1` に展開した。Releaseビルドは警告0件・エラー0件で成功した。パッケージキャッシュは既定の `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使用した。

環境はLinux x86_64、omarchy 4.0.2、SDK 10.0.111、実行ランタイム10.0.11。net8.0を対象にビルドし、既存の `RollForward=Major` で実行した。毎回新規プロセスを起動し、ファイルキャッシュは消していない。

```sh
dotnet build /tmp/issue692-repro-recheck-dt_liad1/tools/StartupBench/StartupBench.csproj -c Release --nologo -p:NuGetAudit=false
dotnet /tmp/issue692-repro-recheck-dt_liad1/tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
DOTNET_TieredCompilation=0 dotnet /tmp/issue692-repro-recheck-dt_liad1/tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
dotnet /tmp/issue692-repro-recheck-dt_liad1/tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
```

通常JITと段階JITなしを交互に各2回実行した。全出力と終了コードは `issue-692-recovery-baseline.txt` に保存した。

| 条件 | 星図登録 | 初回BuildLimits | 登録時の累積確保量 | 終了コード |
| --- | --- | --- | --- | --- |
| 通常JIT・1回目 | 2100 ms | 93 ms | 172 MB | 3 |
| 段階JITなし・1回目 | 1900 ms | 65 ms | 172 MB | 3 |
| 通常JIT・2回目 | 2080 ms | 86 ms | 172 MB | 3 |
| 段階JITなし・2回目 | 1722 ms | 45 ms | 172 MB | 3 |

全4回で `RESULT: SLOW` と終了コード3を確認した。失敗内容は星図登録の仮基準1500 ms超過であり、ゲーム内症状の再現成功とは扱わない。

`stages` は終了コード0。解析のみの合計は212 ms、解析・登録・移行規則の合計は1848 msだった。登録の前に解析するためJITと初期化が温まり、`total` の内訳や差分には使えない。

全4回で登録指紋 `607b55b25354e173`、内容指紋 `13792-e4617232a7ce91e6`、星一覧7642件、BuildLimitsのtalents=7657・entries=584が一致した。これは修正前コードを繰り返した結果であり、変更前後の同一性を今回検証したという意味ではない。

## 次工程へ渡す事実

`src/SodRpg.Mod/DreamforgeMod.cs:70` で、プロフィール読み込み前に星図登録を同期実行する。`Awake` はほかにもパッチの適用、画像の先読み、プロフィールの処理を行うため、星図登録だけの結果をMOD全体の読み込み時間へ換算できない。

#459の対策は現在のコードにもある。`HeroTreeLayout.Clusters.cs:154` は調整済み配置の適用成功時に `PlaceSectors` を省き、`Mechanisms/AuthoredStarRegistration.cs:236` の `NormalizeAuthored` は事前辞書を使う。同ファイルの一覧取得処理は登録指紋によるキャッシュを持つ。これらを未対応の対策として重ねて実装しない。

受け付け審査の読み込み内容・順序・更新反映の懸念について、本工程では製品コードとキャッシュを変更していない。引き継いだ修正の前後比較・全出力一致・既存テスト108件成功は `README.md` と `issue-692-comparison.txt` に前回の記録があるが、今回の実行結果としては扱わない。次の修正工程では既存差分を確認し、採用する最終コードの内容・順序・更新反映を検証する必要がある。

## 未実行と未完了

- Unity/Monoでのゲーム起動、Player.log計測、報告者と同じMOD構成での再現は未実行。実機環境と構成情報がないため。
- Harmonyのパッチ適用、画像の先読み、プロフィール読み込みの時間は未計測。既存のCore用ツールの対象外のため。
- 引き継いだ修正のビルド、前後比較、内容の全出力比較、既存テストの再実行は未実行。本工程は修正前の再現確認に限定したため。
- full/slowスイートは未実行。依頼で禁止されているため。
- FR-1・FR-2の改善完了は本工程で判定していない。未再現でも停止せず、次工程でコード解析と既存差分の検証を続ける。
- push失敗の復旧とPRの公開は未実行。本ワーカーにはpush・PR作成の権限がないため。
