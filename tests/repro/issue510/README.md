# Issue #510 の再現と修正の記録

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

## 修正後の検証（2026-10-16）

- `OnApplicationQuit` は既存の `_stopped` 付き `Stop()` を呼ぶ。終了時保存は
  `ClientSession.Unwire` の `SaveNow` / `FlushSaves` に集約し、後続の `OnDestroy` では繰り返さない。
  これにより同じ終了処理による2回目のファイル置換をなくし、`.bak` の世代が余分に進む原因を除く。
- 警告文3種は `Txt` を保持し getter で現在の言語へ解決する。ホストの `VersionWarnings` も
  `Txt` へそろえた。協力取引の既知理由コードは日英を保持する。旧ホストの自由文 status、例外文、
  Core から文字列として届く復元メモ・取引保留理由の原文はそのまま保持する（既存の翻訳範囲）。
- PackMend は既存の深さ別ディスパッチバッファに召喚獣リストを追加し、入口と返却時に Clear する。
  全実体のコピー後に Dispatch し、再入時は別深さのバッファを使う。返却は finally で保証する。
  初期化済み経路のクロージャ確保も分離し、既存再現テストで温まった発動の確保量 **0バイト**を確認した。
- 指定コマンド `python3 tests/repro/issue510/run.py` は **exit 0、5件すべて成功**。
  記録：`artifacts/20261016T063646735318Z/`。
- revert check：変更15ファイルを実ファイルと patch に退避し、一時的に HEAD の修正前へ戻した状態で
  同じコマンドを実行。**exit 1、5件すべて失敗**を確認した。その後全変更を再適用し、上記の成功を確認した。
  修正前記録：`artifacts/20261016T063515547367Z/`。
  退避と結果：`artifacts/revert-check-20261016T063515055326Z/`。
- 変更箇所に絞った既存回帰テスト：Startup **108成功・1スキップ**、Native **64成功**、Core **183成功**。
  スキップは既存の `ModReloadTests.SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch` で、
  属性に記された理由は Release の JIT インライン化により Harmony パッチを観測できないこと。
  新規テストは追加していない。既存境界は製品側の getter・フィールド抽出と型変更へ追従させた。

## 未実行の検証と実機確認項目

ゲーム DLL・Unity 実行環境がなく、以下の実機確認と MOD 本体ビルドは未実行。
フル／slow テストも依頼の範囲外として未実行で、後段の検証へ引き継ぐ。

- 終了中に `OnDestroy` が呼ばれるか、Quit だけでも保存が完了するか、終了中の Harmony 解除に問題が
  ないかを確認する。`OnApplicationQuit` と `OnDestroy` の間にプロフィールを変える処理が走らないか、
  とくに他 MOD・本体の終了コールバックも含めて確認する（R4）。
- 定期保存後にプロフィールを更新して終了し、最新ファイルが終了直前の内容、`.bak` が直前の定期保存
  の内容であることを実ファイルで確認する。自動テストは保存呼出回数を検証し、実ファイル世代は未確認。
- `ContinueWarning`・`CoopTradeWarning`・`HostVersionWarning`（互換差と無応答）およびホスト側の
  `VersionWarnings` を表示して日英を切り替え、現在の言語に追従するか確認する（R9・R11）。
- PackMend が所有する生存召喚獣を回復し、Dispatch 中の実体増減や入れ子発動でも例外・対象混入・
  二重回復が起きないこと、連続発動でリストが蓄積しないことを確認する（R9・R13）。
  実ゲームでの発動頻度と GC への体感影響も未確認。ナキアの星（`h.nachia.power`、
  `circle-life.1`）の通常攻撃発動は Core 側の既存テストが担う。
