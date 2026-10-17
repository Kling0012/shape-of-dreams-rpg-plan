# Issue #692 再現工程の再確認

判定は `NOT_REPRODUCED`。修正前の星図登録は新規プロセスで約1.7〜2.1秒かかったが、ゲーム起動から `Loaded DreamforgeRPG` までの症状は再現していない。1500 msは既存ツールの比較用の仮基準であり、Issueの受け入れ基準ではない。

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
