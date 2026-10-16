# Issue #506: ボス実体の寿命番号が枯渇してマークが読めなくなる

修正前の `HostAuthority.BossNativeAdapters.cs` と `HostAuthority.BossRuntime.cs` を既存のテストプロジェクトへリンクしたまま、マーク対象の生成・寿命終了を 4096 回繰り返す。常時アクティブな対象は 1 体だけで、同時に 4096 体を生存させる必要はない。

## 実行

リポジトリのルートで実行する。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed' -p:PythonCommand=python3
```

`DOTNET_ROLL_FORWARD=Major` は、今回の環境に .NET 10.0.11 だけがあり、プロジェクトの対象である .NET 8 の testhost をそのまま起動できなかったために指定している。.NET 8 がある環境では省略できる。パッケージキャッシュは環境の `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使用し、worktree 内へ変更していない。

## 修正前の結果（2026-10-16）

終了コード **1**、**3 ケース中 2 成功・1 失敗**。ビルドエラーやテストホスト起動エラーではなく、4097 体目のマークが 1 件になることを要求するアサーションが失敗する。

| usePooling | reuseInRoom | 4096 体の寿命終了後に残る寿命番号 | 4097 体目の寿命番号 | 読み取ったマーク | 結果 |
| --- | --- | ---: | ---: | ---: | --- |
| true | false | 0 | 4097 | 1 | 成功 |
| false | true | 0 | 4097 | 1 | 成功 |
| false | false | 4096 | 0（未登録） | 0 | 失敗 |

失敗時の出力:

```text
finished=1024, retained actor identities=1024
finished=2048, retained actor identities=2048
finished=3072, retained actor identities=3072
finished=4096, retained actor identities=4096
usePooling=False, reuseInRoom=False, retained=4096, nextLife=0, nextMarks=0
After 4096 completed actor lifetimes (usePooling=False, reuseInRoom=False), the next active target must retain one boss mark; actual marks=0, native life=0, retained identities=4096.
```

## 本体の解放経路の根拠

調査元は `/home/wang/dev/sod-gamedata/decompiled/sod-decomp/Dew.Core/`。本体のソースや DLL はこの変更に含めていない。

- `Actor.cs:2257` の `Destroy()` は `Dew.Destroy(gameObject)` へ進む。`Dew.cs:3179` の `StepDestroy` は `ICleanup.OnCleanup()` を実行し、最終的にカスタム破棄処理か `SpawnManager.Destroy` を呼ぶ。
- `Actor.cs:535` の `ICleanup.OnCleanup()` は `Network_isActive=false` を設定する。`Actor.cs:540` の `OnIsActiveChanged` は `InvokeOnDestroyActorIfDidnt` を呼ぶ。MOD の `BossNativeInstanceCompletion` prefix は `CompleteBossNativeActor` を呼ぶが、このメソッドは `_bossNativeActorLives` を削除しない。
- `Actor.cs:641` の `OnDisable()` は `DewPool.ClearEventsAndProcessors(this)` を呼ぶ。`DewPool.cs:44` は `SpawnManager.softInstance` がなければ return、存在しても `usePooling || actor.reuseInRoom` の場合だけ `actor.ClearPooledEventsAndProcessors()` を呼ぶ。全破棄経路で無条件に通る解放処理ではない。
- `SpawnManager.cs:487` の `Destroy` は、管理者なし・プールに未登録・プール無効かつ部屋内再利用なしの場合に `UnityEngine.Object.Destroy` へ進む。プール返却の場合は `SetActive(false)` による `OnDisable` から上記の解放へ進める。
- `Room.cs:390` の `ClearRoom` は残敵へ `Destroy()` を呼ぶ。`RoomMonsters.cs:512` の片付け、同 `:962` の `RemoveAllCamps` も `Actor.Destroy()` へ進む。
- `ZoneManager.cs:1285` は部屋・ゾーン移動の際、`isDestroyedOnRoomChange && isActive` の Actor に `Destroy()` を呼ぶ。`RoomMonsters.cs:1030` はゾーンが変わると `SpawnManager.ClearMonsterAbilityPools()` も実行する。これらの呼び出し自体は、プールを使わない Actor に `ClearPooledEventsAndProcessors` を強制しない。
- MOD の辞書削除は `ClearBossNativeActor` のみで、呼び出し元は `BossNativePooledLifetime` prefix のみ。登録は 4096 件で新規割り当てを 0 として拒否する。`BossProgressLedger.MarkTarget` はこの番号を記録し、`MarkCount` は辞書との一致を要求するため、未登録の対象のマークが 0 件として返る。

## 再現の範囲と未実行

再現テストは、実際の MOD のマーク処理・辞書・既存 prefix を呼ぶ。本体側のライフサイクルだけは逆コンパイルで確認した条件を小さな境界処理で再現する。既存の API double は Harmony を自動適用しないため、prefix を明示的に呼んでいる。Unity の実際の破棄コールバック、Mirror の同期、fake-null は実行していない。後工程で解放フックの入口を変更した場合には、この境界もその実際の入口を呼ぶよう更新する必要がある。

実機の長時間インフィニティ、敵密度の高い部屋、既定の `SpawnManager.usePooling` の実行時値、ゲーム内 `dreamforge_simkill`、ゲーム内診断ログでの推移、実 DLL を使う MOD 全体のビルド、投射物の命中重複判定の症状、全テストスイートは **未実行**。今回確認したのは「プール無効・部屋内再利用なし」の本体条件が成立する場合に、終了した実体の寿命番号が蓄積し、後続対象のマークが失われること。通常設定の長時間プレイで必ず発生するという結論ではない。

再現工程では本体修正・診断ログ追加は行わなかった。品質懸念のうち、解放漏れによる上限到達とマーク消失を再現し、解放経路の補強・開発用ログを実装工程へ引き継いだ。

## 判断したこと

実機確認不可という依頼条件に従い、逆コンパイルで確認できた非プール破棄経路を、既存のソースリンク型テストの境界として採用した。4096 件を直接辞書に詰めず、実際のマーク入口を 4096 回使い、破棄完了 prefix を毎回実行して解放漏れを観測した。プール有効と部屋内再利用ありの比較ケースも同じ再現テストに含め、常に枯渇するよう作ったテストではないことを確認した。

## 実装修正と再検証（2026-10-16）

`Actor.OnDisable` の postfix に `ClearBossNativeActor` を追加した。プール返却、非プールのUnity破棄、部屋片付け、ゾーン移動による `Destroy()` がここへ到達する。既存の `ClearPooledEventsAndProcessors` prefix は残し、両方通った場合も辞書の `Remove` は冪等で、累計解放数は実際に削除したときだけ増やす。

`CompleteBossNativeActor` の prefix では寿命番号を消さない。`Se_U_Burrow` の終了処理が生成する攻撃などは、親が非アクティブでも同じ寿命と祖先であることを照合するため。共通の `InvokeOnDestroyActorIfDidnt` の直後に消す方法も、遅延する終了攻撃より早く番号を失う可能性があるので採用しなかった。`OnDisable` は本体がプール返却時にも既に使っている寿命境界である。全件リセットや生存実体の追い出し、上限増加は行っていない。

再現テストは新規追加せず、既存の `FinishNativeLifetime` 末尾で実装した postfix を呼ぶよう、本体境界だけを追随した。アサーションと3条件は変更していない。

| 条件 | 4096回終了後の件数 | 次の寿命番号 | 次のマーク件数 |
| --- | ---: | ---: | ---: |
| pooling=true, reuse=false | 0 | 4097 | 1 |
| pooling=false, reuse=true | 0 | 4097 | 1 |
| pooling=false, reuse=false | 0 | 4097 | 1 |

- 冒頭の再現コマンド（修正前）: exit 1、2成功・1失敗。非プール条件で1024/2048/3072/4096件へ蓄積し、次のマーク0件。
- 同じ再現コマンド（修正後）: exit 0、3成功。1024/2048/3072/4096回終了後すべて0件。
- **revert check**: テストを変更せず、追加postfix内の解放呼び出しだけを一時的に戻して空にすると exit 1、2成功・1失敗（非プール条件で4096件、次の寿命0・マーク0）。呼び出しを復元して同じコマンドを再実行すると exit 0、3成功（次の寿命4097・マーク1）。修正を一時的に戻すと再現テストが失敗し、戻すと成功することを確認した。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~Boss' --logger 'console;verbosity=minimal'`: exit 0、既存ボス関連172成功・失敗0。終了攻撃、部屋変更、マーク・資源上限の既存検証を含む。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -v:q`: exit 0、警告6・エラー0。本体DLLを参照したMOD全体のビルドはこの環境で実行できた。`Mods` ディレクトリのない参照用データを使い、実ゲームへの配置はしていない。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj -c Release --filter 'FullyQualifiedName~NativeStartupTests|FullyQualifiedName~ModReloadTests|FullyQualifiedName~ReloadStartupTests|FullyQualifiedName~InfinityLobbyStartTests' --logger 'console;verbosity=minimal' -v:q`: 最初は既存ハーネスに近接設定4フィールドと `ConfigureMeleeCoop` がなくコンパイル失敗（exit 1）。そのAPI境界と今回の診断メソッドだけ追随し、再実行は exit 0、107成功・失敗0・既存skip1。新規テストやアサーション変更はしていない。

開発コマンド `dreamforge_bosslives 1` で現在件数・最大件数・累計解放数を直後と30秒ごとに記録する。`0` で現在値を1回出して停止。実行場所と確認方法は [MODの診断案内](../../../src/SodRpg.Mod/README.md#ボス実体の寿命診断issue-506) に記載した。

## 動作確認失敗の追修正（2026-10-16 修正ラウンド）

前ラウンドの `tools/test_changed.py` が `tests/Issue73.Native.Tests/obj/Release/net8.0/StarRefreshUi.g.cs(102,38): error CS0103: The name '_linkCoefficients' does not exist` で失敗した。原因は本Issueと無関係な契約ずれで、main 系の近接協力変更（PR #515）が `DreamforgeUi.cs` に `_linkCoefficients` を追加し、`RefreshStarState` の署名計算で使った一方、`NativePersistence.targets` の `CompileStarClusterDiffUpdate` 抽出フィールド一覧へ追記していなかった。`ExtractNativePersistence` はメソッド本文を実ソースのまま切り出すため、一覧から漏れたフィールドは生成コード内で未定義になる。`Fields=` 一覧に `_linkCoefficients` を1つ追加し、生成コードが実宣言（`Dictionary<string, float>`、Unity依存なし）を含むようにしただけである。本体実装・テストのアサーションは変更していない。

- `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget dotnet build tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release -v:q`: exit 0、エラー0。`StarRefreshUi.g.cs` の15行目に `_linkCoefficients` 宣言が生成される。
- gh-auto と同じ `tools/test_changed.py ordinary`（diff 選択、`DOTNET_ROLL_FORWARD=Major`）: exit 0、2483成功・失敗0（9プロジェクト）。修正前は同じコマンドが上記CS0103で exit 1。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~BossNativeActorLifetimeReproTests'`: exit 0、3成功。

**未実行**: ゲーム起動プログラム・実アセットを利用できないため、Unityでの実破棄コールバックとHarmony適用、fake-null、Mirror実通信、診断コマンドの実コンソール呼び出し・ログ推移、ボス装備付きの長時間Infinityと高密度部屋は未確認。実投射物の症状は既存ボス関連テストまでの確認。全テスト・遅い網羅検証は依頼に従い後工程へ残す。上の再現は本体境界を代替するコード上の検証で、実プレイ済みとは扱わない。

## タイミングテスト失敗の調査（2026-10-16 修正ラウンド2）

前ラウンドの動作確認で `SodRpg.Core.Tests.PerformanceTests.Build_and_encode_are_fast`（1ビルド0.2ms未満を要求）がPR側だけで失敗し、main での再実行は成功したため「PRでのみ失敗」と判定された。同じコマンドを同一時刻の負荷でPRとベース（`eb807f5e`）の両方へ実行して比較したところ、**ベースも同じ失敗**を再現した。共有マシンの他セッション由来のCPU過負荷（8コアに対しロードアベレージ47〜112）によるスケジューラ遅延で、PRのコード起因ではない。

| 実行対象 | ロードアベレージ(1分) | 測定値（上限0.2ms） | 結果 |
| --- | ---: | ---: | --- |
| PR（通常優先度） | 112 | 1.17 / 1.20 / 1.14 ms | 失敗 |
| PR（通常優先度） | 47 | 0.74 ms 台 | 失敗 |
| ベース eb807f5e（通常優先度） | 80〜47 | 0.95 ms | 失敗 |
| PR（通常優先度） | 25 | 0.226 ms（上限直上） | 失敗 |
| PR（`sudo nice -n -10`） | 52 | 上限内（テスト時間1秒） | 成功 |

測定値はおおむねロードアベレージに連動して変化し（ロード19.6の瞬間も一時的なバーストで0.66msを測定）、優先度を上げるだけで成功する。コード速度ではなく、他セッションのCPU使用の割り込みが測定値を変えている。

根拠は以下の通りで、測定の遅延要因として環境負荷以外が残らない。

- `git diff eb807f5e..HEAD -- src/SodRpg.Core` は空。`Build.Compute` の実装はベースと同一で、本PRは `src/SodRpg.Core` を一切変更していない。
- 同一バイナリ・同一負荷でCPU優先度だけを上げると成功する。
- 同ファイルの兄弟テスト `A_kill_takes_well_under_a_tenth_of_a_millisecond` は前ラウンドで main でも失敗しており、gh-auto 自身が環境起因と判定済みである。
- したがって本ラウンドではコードを変更していない。ウォールクロック閾値は過負荷時にブランチを問わず失敗するため、テスト側の変更では防止できず、負荷の静穏時に再実行することが対処になる。
