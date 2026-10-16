# Issue #510 の再現記録（修正前）

実機確認が必要とされた小修正3件（終了時の二重保存・警告文の言語追従・PackMend の一時リスト）を、
修正前に自動テストで再現した記録である。実施日は 2026-10-16（日本時間）。

## 実行

リポジトリ直下で次を実行する（issue462 と同じ構成）。

```sh
python3 tests/repro/issue510/run.py
```

3プロジェクトの再現テストだけを前景で順に実行し、コマンド・終了コード・TRX・コンソール出力を
`artifacts/<UTC日時>/` に保存する（git管理対象外）。この環境には .NET 10 しかないため
`DOTNET_ROLL_FORWARD=Major` を既定で付ける。

## 再現方法と結果（修正前）

| # | バグ | 再現テスト | 手段 | 結果 |
| --- | --- | --- | --- | --- |
| 1 | 終了時に保存が2回走る | `SodRpg.Mod.Startup.Tests.Issue510DoubleSaveTests` | 製品 `DreamforgeMod.cs` をリンクした harnes で `OnApplicationQuit` → `OnDestroy` を Unity と同じ順で呼び、`ClientSession` ダブルの `Unwire` が製品と同じ保存呼び出しを行うよう計数する | **失敗**（SaveNow 2回、期待1回） |
| 2 | 警告文が言語切替に追従しない | `Issue73.Native.Tests.Issue510ReproductionTests`（3件） | `ClientSession.Continue.cs` はリンク、`DeferCoopSettlement`（CoopTrade）と `OnHello`・`HostVersionWarning`（Hello）は `NativePersistence.targets` で実ソースから抽出。製品コードが警告を設定した後に `Loc.Japanese` を切替 | **3件とも失敗**（元の言語の文字列のまま） |
| 3 | PackMend が発動のたびに一時リストを作る | `SodRpg.Core.Tests.Issue510ReproductionTests` | 製品 `HostAuthority.GimmicksV129.cs` をリンクした harnes で `PendingGimmick` のディスパッチ（`Flush`）を繰り返し、温まった2発目の管理ヒープ確保量を `GC.GetAllocatedBytesForCurrentThread` で計測 | **失敗**（128バイト/発動、期待0） |

統合スクリプトの終了コードは **1**（いずれかの失敗があれば1を返すため、修正前に全再現テストが
失敗することで正常）。各失敗の詳細は `artifacts/` のログと TRX を参照。

## 境界（ダブル）側の整備

再現に先立ち、HEAD 時点で壊れていた2点を直した。いずれもテスト境界側の追従であり、製品コードは
変更していない。

- `SodRpg.Mod.Startup.Tests`：製品 `DreamforgeMod` が参照する `DreamforgeConfig` の4設定
  （`meleeRangeToSpeed` 等）と `HostAuthority.ConfigureMeleeCoop` がダブルに無く、ビルド不能だった。
- `Issue73.Native.Tests`：`DreamforgeUi.RefreshStarState` が参照する `_linkCoefficients` が
  `NativePersistence.targets` の抽出 Fields に無く、ビルド不能だった。追加した。

また `ClientSession` ダブル（StartupGameApi.cs）の `Unwire` は、製品 `ClientSession.Unwire`
（ClientSession.cs）が終了時に `SaveNow` + `FlushSaves` を呼ぶのと同じ形に合わせ、計数用の
`SaveNowCount` / `FlushSavesCount` を持たせた。`NativeBoundary.cs` 側の `HostVersionWarning`
二重定義は、実ソースからの抽出（`CompileIssue510Hello` ターゲット）へ寄せた。

## 実機でしか確認できないこと

- 終了中に `OnDestroy` が呼ばれるか・Harmony 解除の順序・`OnApplicationQuit` と `OnDestroy` の間に
  プロフィールを変える処理が走るか（#1 の保存タイミングの前提）。
- 画面上の警告文の実際の表示と、言語切替の操作タイミング（#2）。
- 実ゲームでの PackMend 発動頻度と GC への体感影響（#3）。ナキアの星（`h.nachia.power`、
  `circle-life.1`）の通常攻撃発動は Core 側の既存テストが担う。
