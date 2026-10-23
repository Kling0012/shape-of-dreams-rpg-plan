# Issue #1055 再現記録

修正前の処理を抽出したハーネスで、棘による同時反射の致死ケースと、Pure Soul の生成数が0になるケースを確認した。後述の修正後の検証では、同じ6ケースを使って改善を確認した。ゲーム実機で報告の4症状を再現した結果ではない。

## 実行方法

```sh
python3 tests/repro/issue1055/run.py
```

.NET SDK と net8.0 を動かせるランタイムが必要。今回の環境は SDK 10.0.111、ランタイム10.0.11。スクリプトは `DOTNET_ROLL_FORWARD=Major` を未設定時に指定する。パッケージキャッシュの場所は変更しない。

本体コードをリポジトリに複製しないため、実行時に以下の読み取り用資料を使う。別の環境では環境変数で場所を指定する。

| 環境変数 | 今回の場所・用途 |
| --- | --- |
| `SOD_DECOMPILED` | `/home/wang/dev/sod-gamedata/decompiled/sod-decomp`。`Dew.Core/DamageData.cs` の `ApplyAmplification` を抽出する。 |
| `SOD_MANAGED` | `/home/wang/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed`。`Dew.Contents.dll` から `Ai_QR_Innocence_Spawner` を読む。 |
| `ILSPYCMD` | `/home/wang/.dotnet/tools/ilspycmd`。該当型だけを標準出力へ逆コンパイルする。 |

製品側の `HostAuthority.ApplyPressure`、`OnMonsterDamageTaken`、棘のタイマー処理を実行時に抽出する。反射量と夢の圧の計算には、参照した `SodRpg.Core` を使う。Pure Soul は本体から抽出した `clampedCount` と `OnCreateSequenced` を使い、製品の `NativePureSoulCount` をHarmonyで適用する。ハーネスのHarmonyは.NET 10でコルーチンのfault節を扱える2.4.2、製品の参照はゲーム付属の2.3.6。生成ファイルは `obj/` に置く。資料がない場合や抽出・コンパイルに失敗した場合は、再現成功に数えず終了コード2になる。

## 修正前の実行結果

2026-10-23、ブランチ `ai/issue-1055`、製品コードの基点 `a1d0b9e9` で実行した。

- コマンド: `python3 tests/repro/issue1055/run.py`
- 終了コード: **1**
- 6ケース中、対照3件は成功、期待動作を満たさない3件は失敗。
- ログ: `.ci-logs/issue1055/20261023T114639490023Z/repro.log`
- ケースごとの結果: `.ci-logs/issue1055/20261023T114639490023Z/results.json`

ログと結果JSONは作業ツリー内の無視対象に保存し、コミットには含めない。次回の実行では別の時刻のフォルダへ保存する。

| ケース | 入力 | 観測結果 |
| --- | --- | --- |
| 棘の対照 | 圧なし、HP1000、同じ敵へ同時刻に2回命中 | 反射は1回分の20。HP980。同一敵のクールダウンは機能した。 |
| 棘の致死 | 夢30・星500・深度5・Infinity圧100・waypoint倍率1・ソロ。HP1000、防御補正なし。別々の棘の敵3体へ同時刻に各10000ダメージ | 圧の与ダメージ倍率23.8。反射は増幅前に各20だが、増幅後は各476、合計1428となりHP0。生存を期待する再現ケースが失敗した。 |
| Pure Soul の対照 | サーバ、1人、生成数8、offset 0、対象あり、性能負荷0 | 8発生成して終了。 |
| Pure Soul の対照 | 同条件、性能負荷0.9 | 1発生成して終了。 |
| Pure Soul の生成欠落 | 同条件、性能負荷0.95 | 0発で終了。1発以上の生成を期待するケースが失敗した。 |
| Pure Soul の生成欠落 | 同条件、性能負荷1.0 | 0発で終了。同じ期待が失敗した。 |

### 棘の再現範囲

各反射のクールダウンは敵と旅人の組ごとに管理される。反射量の計算後、敵を攻撃元にして `PureDamage(...).Dispatch(...)` するため、敵に登録された圧の与ダメージプロセッサが適用される。

ハーネスの `Dispatch` は読み取った本体 `Actor.DealDamage` の順序に合わせ、攻撃元のプロセッサを呼んでHPを減らすダブルである。本体のダメージ処理全体を実行したものではない。祖先Actor、被ダメージ補正、シールド、免疫は再現していない。Pure が防御を無視することと、与ダメージプロセッサの適用順は、外部資料の `Actor.cs`、`DamageData.cs`、`FinalDamageData.cs` で確認した。

このケースは設定上限内の高圧と複数敵の反射で、軽減なしの旅人が同時刻に死亡する経路を示す。画面外の敵の配置、ビスマス固有の攻撃、報告者の装備や圧の値は再現していない。また、現在の説明文にも圧で反射が増える旨があるため、2%が最終ダメージの仕様上限だとは断定しない。再現ケースの生存期待は報告された致死ケースを検出するためのものであり、新しいバランス仕様ではない。

### Pure Soul の再現範囲

本体は `RoundToInt(count * (1 - perfPressureStrength))` で生成数を減らす。生成数8、性能負荷0.95では丸めた結果が0となり、生成ループに入らず終了する。性能負荷1.0でも同じ結果になる。

本体 `GraphicsManager` は適応型の性能調整が有効なとき、目標FPSの30%から15%の間で負荷を0から1へ変える。したがって1は本体の計算で取り得る値だが、今回Limbo開始時にその値を観測したわけではない。

コルーチンは待機を実時間で再現せず最後まで進め、弾の生成呼び出し数を記録した。Unityの描画、実ネットワーク、MODの自動詠唱パッチの接続は実行していない。攻撃が描かれない原因と一致する可能性のある生成欠落を確認したが、「Limbo開始後2〜3秒」の症状との一致は未確認。生成数0の処理はMOD側ではなく、本体の処理である。

## その他の試行

| 報告 | 手段・対象 | 結果と限界 |
| --- | --- | --- |
| 5枠以上のエッセンスの重なり | `LevelEssenceRuntime.EnsureHud` と、本体 `UI_InGame_SkillButton_GemGroup`・`UI_InGame_GemSlot` のソースを読む。Coreの増枠テストを実行する。 | MODは5〜8枠を複製し、LayoutGroupを無効化して4列2行に配置する。実際のPrefabの矩形・pivot・親の位置を入手できず、重なりは未再現。任意の矩形を仮定した失敗は作っていない。 |
| guide shrine の破壊演出 | 本体 `Shrine_Guidance` の型を逆コンパイルし、`OnInteract` と `DewNetworkBehaviour.FxPlayNewNetworked` を読む。MODのFX対象と絞り込み条件を読む。 | 本体は破壊時に `FxPlayNewNetworked(fxBreak)` を呼んでから報酬と破棄へ進む。`NativeFxBudget` の対象型に祠はなく、`NativeProcImpactFx` は既知の3種の攻撃由来だけを扱い、`FrostFxLifetime` はFrost専用。演出を消す条件は特定できず、未再現。 |
| ビスマスの開始直後の攻撃 | `NativeBismuthAutoCast` と本体の同名の星効果を読む。AutoCastの既存テストを実行する。 | 本体の敵検出待ちは0.25〜0.75秒で、MODにも生成中の敵を除外する条件がある。既存テストはcadenceなどを扱い、MODのScan全体や描画は扱わない。これだけでは2〜3秒の攻撃欠落を再現できなかった。 |

`NativeProcSpawnSpread` も確認したが、対象はCharcoal・Blade・Accuracy・SpiralでPure Soulは含まれない。重いフレームの遅延には0.07秒経過時の処理経路があり、今回の2〜3秒の症状との因果は確認できなかった。

## 既存テスト

以下をこのセッションで実行し、計68件の成功を確認した。これらの成功は報告された4症状の否定を意味しない。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --filter 'FullyQualifiedName~ThornsV131Tests|FullyQualifiedName~LevelEssenceSlotsTests' --nologo -v quiet -p:PythonCommand=python3 -p:WarningLevel=0
# 34 passed
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.AutoCast.Tests/SodRpg.Mod.AutoCast.Tests.csproj --nologo -v quiet -p:WarningLevel=0
# 19 passed
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.FxBudget.Tests/SodRpg.Mod.FxBudget.Tests.csproj --nologo -v quiet -p:WarningLevel=0
# 12 passed
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.DeathFx.Tests/SodRpg.Mod.DeathFx.Tests.csproj --nologo -v quiet -p:WarningLevel=0
# 3 passed
```

最初のCore・AutoCast実行は.NET 8ランタイムがなく起動に失敗した。上記のロールフォワード設定で成功を確認した。ランタイムの追加インストールは行っていない。

## 修正後の検証（2026-10-23）

製品の修正を適用した同じ6ケースは全て成功した。高圧の棘の反射は合計20、HP980。Pure Soul は性能負荷0、0.9、0.95、1でそれぞれ8、1、1、1発となった。

修正を一時的に外す確認も実行した。`HostAuthority.cs` と `Nightmare.cs` をHEADの内容へ戻し、`NativePureSoulCount.RoundCount` を元の丸めだけにすると、同じ3件が失敗した（終了コード1）。保存した修正を再適用すると6件全て成功した（終了コード0）。ハーネスのケースと期待値は変えていない。

- 修正なし: `.ci-logs/issue1055/20261023T121458240127Z/results.json`
- 再適用: `.ci-logs/issue1055/20261023T121530311195Z/results.json`
- 手順のログ: `.ci-logs/issue1055/revert-check/reverted.log` と `restored.log`

修正工程で関連する既存テストを再実行し、Core 34件、AutoCast 19件、FxBudget 12件、DeathFx 3件、NativeStartupTests 16件、計84件が成功した。ゲームDLL付きの製品ビルドも成功した。参照用のゲームディレクトリに `Mods` がないことを確認し、ゲームへの配置は実行していない。

最初の `--no-restore` 付きビルドはassetsファイルがなく失敗し、通常のビルドで成功した。ハーネスへのHarmony 2.3.6の適用は.NET 10で `InvalidProgramException` となり、2.4.2へ変更して成功した。これは実ゲームでのパッチ適用の成功を示すものではない。

## 修正ラウンドの検証（2026-10-23）

棘耐性を専用の装備能力値へ変更し、協力プレイの敵担当別の圧補正にも反射の除外を加えた。製品メソッドが参照する旅人の実行状態に合わせて、既存のAPIダブルに辞書と型だけを追加した。再現ケースと期待値は変えていない。追加した棘耐性と敵担当別補正そのものを、この6ケースが検証するわけではない。

- 再現ハーネス: 6件成功。`.ci-logs/issue1055/20261023T124918180121Z`。
- 戻し確認: `HostAuthority.cs` と `Nightmare.cs` を基点 `a1d0b9e9` の内容にし、Pure Soulの最小生成数の補正を外すと、棘の致死ケースと性能負荷0.95・1の計3件が失敗した。終了コード1。`.ci-logs/issue1055/20261023T125207455519Z`。
- 修正の復元後: 6件成功、終了コード0。`.ci-logs/issue1055/20261023T125217665847Z`。戻し確認の保存先は `.ci-logs/issue1055/revision-revert-check/`。
- Coreの関連テスト216件、自動詠唱19件、演出予算12件、死亡演出3件、`NativeStartupTests` 16件、計266件成功。
- `python3 -m unittest tools/tests/test_equipment_pools_balance.py -v`: 5件成功。
- ゲームDLL付きの製品ビルド成功。参照用ゲームディレクトリに `Mods` がないことを確認して実行し、ゲームへは配置していない。
- `python3 tools/balance/gen_cs.py --check`: 成功。

ゲーム実機での4症状、HUDの実描画と1920×1080のスクリーンショット、演出の実通信、実際の被ダメージ・当たり判定・FPSは未実行。この環境でゲームを起動できないため。全テストと遅いテストは依頼に従い未実行。新しいテストは作成していない。

## 再現工程での未実行

以下は製品を修正する前の工程の記録。修正後の検証は上記を参照。

- ゲーム実機の起動、Limboでのプレイ、画面外の棘への攻撃、`dreamforge_simkill`。この環境ではゲームを動かせないため。
- Unity上のHUD描画とスクリーンショット、祠の破壊演出と通信の実行。実行環境と実際のPrefab配置がないため。
- 全テストスイート、Pythonテスト一式、`tools/test_changed.py`、MOD全体のゲームDLL付きビルド。製品コードを変更せず、再現対象の検証に絞ったため。
- 修正後の検証。この工程は再現のみで、修正していないため。
