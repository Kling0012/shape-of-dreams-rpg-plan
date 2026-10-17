# Issue #692 修正前の起動処理の計測

2026-10-17、前回の再現用コミット `e7c8a15c` を引き継ぎ、星図登録の初回呼び出しを計測した。本番コードは変更していない。再現プログラムのコメントと失敗表示は、計測範囲を明確にするために修正した。

## 対象と判定

Issue #692 の本文は「MODの読み込みが非常に長いので，改善可能か?」で、コメントはなかった。読み込みの段階・所要時間・許容時間は記載されていない。依頼の方針に従い、起動時のMOD初期化を調査対象とした。

`DreamforgeMod.Awake` はプロフィールを読む前に `StarClusters.RegisterAllGenerated` を同期実行する（`src/SodRpg.Mod/DreamforgeMod.cs:70`）。この実際の登録メソッドをReleaseビルドの新規プロセスで1回ずつ呼んだ。星データや保存データの代替入力は作っていない。登録処理には現在のCoreに組み込まれている9人分の星データと移行規則を使う。

結果は `REPRODUCED` とした。再現した範囲は、初回星図登録に数秒を要し、計測プログラムの診断用基準1500 msを超えることに限る。この基準は前回のプログラムが設けた値で、Issueの受け入れ条件ではない。利用者のゲーム環境での待ち時間全体や、#459対応後の性能悪化を再現したという意味ではない。

## 環境

- 作業ブランチ: `ai/issue-692`。本番コードの起点: `f31ce810`。
- OS: Linux、omarchy 4.0.2、arch-x64。
- .NET SDK: 10.0.111。実行ランタイム: 10.0.11。
- 計測プロジェクト: `tools/StartupBench/StartupBench.csproj`。net8.0を対象とし、`RollForward=Major` で実行した。
- NuGetキャッシュ: 環境の `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使用した。worktree内へ変更していない。

毎回別のプロセスを起動した。OSのファイルキャッシュは消していない。共有ホスト上の値で、実行間のばらつきがある。`DOTNET_TieredCompilation=0` は別条件として測ったもので、実機Monoの秒数や上限値には換算できない。

確認時の `origin/main` は `b0336b1e` だった。起点からこのmainまでの差分には、今回測るCoreの星図登録・配置・BuildLimits・HeroSigilsの変更はなかった。ブランチをmainへ付け替える操作はしていない。

## 実行コマンド

```sh
dotnet build tools/StartupBench/StartupBench.csproj -c Release --nologo -v:q
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
DOTNET_TieredCompilation=0 dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll total --threshold-ms 1500
dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
DOTNET_TieredCompilation=0 dotnet tools/StartupBench/bin/Release/net8.0/StartupBench.dll stages
```

最初のtotal計測は通常JIT、段階JITなし、通常JIT、段階JITなしの順に実行した。各コマンドの終了を待ってから次を実行し、totalの終了コード3も記録した。

## 修正前の結果

Releaseビルドは2回実行し、いずれも終了コード0、警告0、エラー0だった。

| 条件 | 星図登録 | 初回BuildLimits.Registered | 登録時の割り当て量 | HeroSigils.All 1000回 | 終了コード |
| --- | ---: | ---: | ---: | ---: | ---: |
| 通常JIT・1回目 | 4698 ms | 247 ms | 172 MiB | 0 ms | 3 |
| 段階JITなし・1回目 | 4732 ms | 87 ms | 172 MiB | 0 ms | 3 |
| 通常JIT・2回目 | 2180 ms | 91 ms | 172 MiB | 0 ms | 3 |
| 段階JITなし・2回目 | 1671 ms | 75 ms | 172 MiB | 0 ms | 3 |
| 判定表示の変更後・通常JIT | 3850 ms | 164 ms | 172 MiB | 0 ms | 3 |

0 msは `Stopwatch.ElapsedMilliseconds` の整数表示で、処理時間が厳密にゼロという意味ではない。割り当て量は `GC.GetAllocatedBytesForCurrentThread` の差を1024で2回割った整数値で、常駐メモリやピークメモリではない。

最後の実行ではビルド成功後に次の出力と終了コード3を確認した。計測対象の本番コードは修正前のままである。

```text
mode=total threshold-ms=1500 runtime=10.0.11 tiered=default
register-all-generated: 3850 ms (alloc 172 MB)
first-build-limits: 164 ms (talents=7657, entries=584)
hero-sigils-all-x1000: 0 ms
authored-registry-fingerprint: 607b55b25354e173
content-fingerprint: 13792-e4617232a7ce91e6
hero-sigils-all-count: 7642
RESULT: SLOW (register 3850 ms > threshold 1500 ms)
```

旅人別のstages計測も両条件で完走し、終了コード0だった。

| 条件 | 解析だけの合計 | 解析・登録・移行規則の合計 |
| --- | ---: | ---: |
| 通常JIT | 203 ms | 1893 ms |
| 段階JITなし | 280 ms | 1291 ms |

stagesは各旅人のデータを先に解析し、そのあと同じ旅人を解析し直して登録する。このため登録側は解析の初回JIT・初期化を済ませた状態であり、totalとの差や、表の2列の差から処理別の厳密な内訳を求めることはできない。

すべてのtotal実行と両stages実行で登録指紋は `607b55b25354e173` だった。totalで得た内容指紋は毎回 `13792-e4617232a7ce91e6`、星一覧は7642件、BuildLimitsはtalents=7657、entries=584だった。これらは次工程の修正前の基準値で、修正後との比較は未実行である。

## #459の対策の確認

#459はCLOSEDで、v2.10.55で対応済みとのコメントがある。今回のコードにも次の対策が存在した。未実施と決めつけて同じ変更を重ねる根拠はない。

- データ表と共通リーダー: `src/SodRpg.Core/Game/StarClusters/Aurena.Generated.cs:10`、`ManifestReader.Generated.cs:326`。共有のTargetEffectIds配列は同ファイル11行から定義されている。
- 調整済み座標を使う場合のPlaceSectors省略: `src/SodRpg.Core/Game/HeroTreeLayout.Clusters.cs:154`。
- NormalizeAuthoredの事前辞書: `src/SodRpg.Core/Game/Mechanisms/AuthoredStarRegistration.cs:236`。
- HeroSigils.Allの一覧キャッシュ: `src/SodRpg.Core/Game/HeroSigils.cs:183`、`Mechanisms/AuthoredStarRegistration.cs:223`。

#459の比較記録は `docs/specs/star-registration-startup-performance-2026-10-16.md` にある。ランタイム・星データ・共有ホストの負荷が異なるため、今回の秒数や指紋を当時の値と直接比較して、退行や互換性破損と判定することはできない。

## 次工程と未実行

星図の初回登録には今回も数秒を要した。登録内のどの処理が支配的かはこの計測だけでは確定していない。次工程では既存対策を前提に登録経路のコードを解析し、修正前後を同じ環境・データで比較する。IDと順序を保つ必要があり、今回記録した登録指紋・内容指紋・件数を修正後と比較する。

- 未実行: ゲーム起動、タイトル画面までの時間、Unity/Mono、Player.logの計測、`dreamforge_simkill`。この環境ではゲームを実行できないため。実機でしか分からない部分を推測して修正していない。
- 未実行: プロフィール読み込み、Harmonyパッチ導入、アイコン先読みなどを含むAwake全体の計測。今回のCore計測プログラムの対象外であり、これらが速いとも遅いとも判定していない。
- 未実行: 既存の.NET/Pythonテストスイート、MODビルド、full/slowスイート。今回は本番コードを変更せず、再現プログラムのビルドと実行に絞ったため。
- 未実行: 修正後とのID・順序・指紋の比較と短縮効果の検証。この工程では修正しないため。

実機での計測方法は既存の性能文書の「実機のPlayer.logで比較する手順」にある。ゲーム本体やタイトル画面までの待ち時間と、`Generated star maps registered in N ms.` の登録時間、`Loaded` 行末の `Startup: N ms.` のAwake内の時間を分けて記録する。実機確認だけを求めるIssueは作成していない。
