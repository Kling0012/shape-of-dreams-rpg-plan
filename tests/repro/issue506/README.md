# Issue #506: ボス実体の寿命番号が枯渇してマークが読めなくなる

修正前の `HostAuthority.BossNativeAdapters.cs` と `HostAuthority.BossRuntime.cs` を既存のテストプロジェクトへリンクしたまま、マーク対象の生成・寿命終了を 4096 回繰り返す。常時アクティブな対象は 1 体だけで、同時に 4096 体を生存させる必要はない。

## 実行

リポジトリのルートで実行する。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed' -p:PythonCommand=python3
```

`DOTNET_ROLL_FORWARD=Major` は、.NET 8 の testhost を起動できない環境（SDK が新しい版だけの環境）のために指定している。.NET 8 がある環境では省略できる。パッケージキャッシュは環境変数 `NUGET_PACKAGES` の指定（未設定なら既定のグローバルパッケージ場所）を使い、worktree 内へ変更していない。

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

調査元は逆コンパイルした本体ソース（`sod-decomp` 展開先の `Dew.Core/`）。本体のソースや DLL はこの変更に含めていない。

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
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release '-p:GameDir=<Shape of Dreams のインストール先>' -v:q`: exit 0、警告6・エラー0。本体DLLを参照したMOD全体のビルドはこの環境で実行できた。`Mods` ディレクトリのない参照用データを使い、実ゲームへの配置はしていない。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj -c Release --filter 'FullyQualifiedName~NativeStartupTests|FullyQualifiedName~ModReloadTests|FullyQualifiedName~ReloadStartupTests|FullyQualifiedName~InfinityLobbyStartTests' --logger 'console;verbosity=minimal' -v:q`: 最初は既存ハーネスに近接設定4フィールドと `ConfigureMeleeCoop` がなくコンパイル失敗（exit 1）。そのAPI境界と今回の診断メソッドだけ追随し、再実行は exit 0、107成功・失敗0・既存skip1。新規テストやアサーション変更はしていない。

開発コマンド `dreamforge_bosslives 1` で現在件数・最大件数・累計解放数を直後と30秒ごとに記録する。`0` で現在値を1回出して停止。実行場所と確認方法は [MODの診断案内](../../../src/SodRpg.Mod/README.md#ボス実体の寿命診断issue-506) に記載した。

## 動作確認失敗の追修正（2026-10-16 修正ラウンド）

前ラウンドの `tools/test_changed.py` が `tests/Issue73.Native.Tests/obj/Release/net8.0/StarRefreshUi.g.cs(102,38): error CS0103: The name '_linkCoefficients' does not exist` で失敗した。原因は本Issueと無関係な契約ずれで、main 系の近接協力変更（PR #515）が `DreamforgeUi.cs` に `_linkCoefficients` を追加し、`RefreshStarState` の署名計算で使った一方、`NativePersistence.targets` の `CompileStarClusterDiffUpdate` 抽出フィールド一覧へ追記していなかった。`ExtractNativePersistence` はメソッド本文を実ソースのまま切り出すため、一覧から漏れたフィールドは生成コード内で未定義になる。`Fields=` 一覧に `_linkCoefficients` を1つ追加し、生成コードが実宣言（`Dictionary<string, float>`、Unity依存なし）を含むようにしただけである。本体実装・テストのアサーションは変更していない。（この修正は既に main へ取り込み済み。）

- `dotnet build tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release -v:q`（環境の NUGET_PACKAGES を使用）: exit 0、エラー0。`StarRefreshUi.g.cs` の15行目に `_linkCoefficients` 宣言が生成される。
- gh-auto と同じ `tools/test_changed.py ordinary`（diff 選択、`DOTNET_ROLL_FORWARD=Major`）: exit 0、2483成功・失敗0（9プロジェクト）。修正前は同じコマンドが上記CS0103で exit 1。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~BossNativeActorLifetimeReproTests'`: exit 0、3成功。

**未実行**: ゲーム起動プログラム・実アセットを利用できないため、Unityでの実破棄コールバックとHarmony適用、fake-null、Mirror実通信、診断コマンドの実コンソール呼び出し・ログ推移、ボス装備付きの長時間Infinityと高密度部屋は未確認。実投射物の症状は既存ボス関連テストまでの確認。全テスト・遅い網羅検証は依頼に従い後工程へ残す。上の再現は本体境界を代替するコード上の検証で、実プレイ済みとは扱わない。

## タイミングテスト失敗の調査（2026-10-16 修正ラウンド2）

前ラウンドの動作確認で `SodRpg.Core.Tests.PerformanceTests.Build_and_encode_are_fast`（1ビルド0.2ms未満を要求）がPR側だけで失敗し、main での再実行は成功したため「PRでのみ失敗」と判定された。同じコマンドを同一時刻の負荷でPRとベース（`eb807f5e`）の両方へ実行して比較したところ、**ベースも同じ失敗**を再現した。共有マシンの他セッション由来のCPU過負荷によるスケジューラ遅延で、PRのコード起因ではない。

| 実行対象 | ロードアベレージ(1分) | 測定値（上限0.2ms） | 結果 |
| --- | ---: | ---: | --- |
| PR（通常優先度） | 112 | 1.17 / 1.20 / 1.14 ms | 失敗 |
| PR（通常優先度） | 47 | 0.74 ms 台 | 失敗 |
| ベース eb807f5e（通常優先度） | 80〜47 | 0.95 ms | 失敗 |
| PR（通常優先度） | 25 | 0.226 ms（上限直上） | 失敗 |
| PR（優先度を上げて実行） | 52 | 上限内（テスト時間1秒） | 成功 |

測定値はおおむねロードアベレージに連動して変化し、優先度を上げるだけで成功する。コード速度ではなく、他セッションのCPU使用の割り込みが測定値を変えている。

根拠は以下の通りで、測定の遅延要因として環境負荷以外が残らない。

- `git diff eb807f5e..HEAD -- src/SodRpg.Core` は空。`Build.Compute` の実装はベースと同一で、本PRは `src/SodRpg.Core` を一切変更していない。
- 同一バイナリ・同一負荷でCPU優先度だけを上げると成功する。
- 同ファイルの兄弟テスト `A_kill_takes_well_under_a_tenth_of_a_millisecond` は前ラウンドで main でも失敗しており、gh-auto 自身が環境起因と判定済みである。
- したがって本ラウンドではコードを変更していない。ウォールクロック閾値は過負荷時にブランチを問わず失敗するため、テスト側の変更では防止できず、負荷の静穏時に再実行することが対処になる。

## 再現工程の再確認（2026-10-17）

今回の再現工程は、修正と既存の再現テストを含む `6f668509` から開始した。実装・テストは変更せず、再現テスト追加時点の `475d3657` にあるアダプタとテストの2ファイルを `git show` で `tests/SodRpg.Core.Tests/obj/issue506-pre-fix/` へ取り出した。チェックアウトや履歴の巻き戻しは行っていない。Issue 本文・コメントとルートの README を確認し、追跡対象の AGENTS.md / CLAUDE.md は存在しなかった。

`PreFix.targets` を同じディレクトリへ以下の内容で置き、既存プロジェクトのコンパイル入力だけを差し替えた。コピーしたファイルはそれぞれ `475d3657:src/SodRpg.Mod/HostAuthority.BossNativeAdapters.cs` と `475d3657:tests/SodRpg.Core.Tests/BossNativeActorLifetimeReproTests.cs` の内容そのままである。`obj` 内の検証ファイル・ログは追跡対象外。

```xml
<Project>
  <Target Name="Issue506UsePreFixSources" BeforeTargets="CoreCompile" Condition="'$(MSBuildProjectName)' == 'SodRpg.Core.Tests'">
    <ItemGroup>
      <Compile Remove="../../src/SodRpg.Mod/HostAuthority.BossNativeAdapters.cs;BossNativeActorLifetimeReproTests.cs" />
      <Compile Include="$(MSBuildThisFileDirectory)HostAuthority.BossNativeAdapters.cs;$(MSBuildThisFileDirectory)BossNativeActorLifetimeReproTests.cs" />
    </ItemGroup>
    <Message Importance="high" Text="Issue506: compiling the original adapter and reproduction test from 475d3657" />
  </Target>
</Project>
```

実行した修正前コマンド（`CustomAfterMicrosoftCommonTargets` はリポジトリルートからの相対パスに展開して指定する）:

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed' -p:PythonCommand=python3 -p:CustomAfterMicrosoftCommonTargets="$PWD/tests/SodRpg.Core.Tests/obj/issue506-pre-fix/PreFix.targets"
```

.NET SDK 10.0.111 / Runtime 10.0.11、環境の `NUGET_PACKAGES` の環境で、終了コード **1**、**2成功・1失敗**を今回も確認した。コンパイル入力の差し替えメッセージが出ており、ビルドは成功し、非プール条件のマーク件数アサーションが失敗した。

- `usePooling=false, reuseInRoom=false`: 1024/2048/3072/4096回の寿命終了後、辞書が同じ件数で増加。4097体目では `retained=4096, nextLife=0, nextMarks=0`。
- `usePooling=true, reuseInRoom=false` と `usePooling=false, reuseInRoom=true`: 4096回終了後の辞書は0件。次の対象は `nextLife=4097, nextMarks=1`。
- 生ログ: `tests/SodRpg.Core.Tests/obj/issue506-pre-fix/pre-fix.log`。終了コードは同ディレクトリの `pre-fix.exit` に保存した。

逆コンパイル物も再確認した。`DewPool.cs:44–63` の条件により、`Actor.cs:641–654` の `OnDisable` を通っても、両フラグがfalseなら `ClearPooledEventsAndProcessors` が呼ばれない。`SpawnManager.cs:511–516` はその条件で Unity の破棄へ進む。品質懸念の「解放漏れで4096件に達する」「その後の新規寿命が0になりマークが読めない」は、この条件について再現済み。異なる実体の寿命0を使った投射物の重複判定の衝突そのものは今回再現していない。

比較のため、持ち越し済み修正を含む現在ソースでも既存の対象テストだけを実行した。最初の通常 `dotnet test` は増分ビルドで修正前のバイナリを再利用し、修正前テストの `obj/issue506-pre-fix/` パスから同じ失敗を出した（exit 1、`current.log`）。その出力を現在ソースの結果とは扱わず、以下で明示的に再コンパイルした。

```sh
DOTNET_ROLL_FORWARD=Major dotnet build tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-incremental -p:PythonCommand=python3
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed'
```

ビルドはexit 0（95警告・エラー0）、対象テストはexit 0（**3成功・失敗0**）。全条件で終了後の辞書は0件、次の寿命4097・マーク1件。ログは同ディレクトリの `current-rebuild.log` と `current-rebuilt-test.log`。今回追加した変更はこの記録のみで、修正実装は後工程の判断に委ねる。

**今回の未実行**: 実ゲーム起動、`dreamforge_simkill`、Unity実破棄コールバック・Harmony実適用・fake-null・Mirror実通信、既定フラグの実行時値、長時間Infinity・高密度部屋、診断コマンドの実ログ推移、実投射物の重複判定、実DLL参照のMOD全体ビルド、他のテストと全体・低速スイート。再現は逆コンパイル由来の本体境界と実MODソースによるものであり、実プレイの再現や今回の修正完了を意味しない。

## 実装工程の完了確認（2026-10-17）

持ち越しの解放フックと開発用ログを維持し、次の不足を補った。

- R1・R2: `BossNativeDisabledLifetime.TargetMethods` は、Actor本体と本体コンテンツのアセンブリからActor派生型が宣言する `OnDisable()` も起動時に列挙する。基底処理を呼ばないoverrideも同じpostfixの対象となる。提供された `Dew.Contents` の2型はメソッド本文が省略された参照用コードなので、基底呼出しがあると推測せず対象へ含めた。`Actor.ClearPooledEventsAndProcessors` prefixも維持し、重複解放は `Dictionary.Remove` によって冪等。`Room.ClearRoom` の `Destroy`、`ZoneManager` の移動時 `Destroy`、`SpawnManager.Destroy` のプール返却／Unity破棄が共通の無効化境界へ進むことは本体コードで確認した。終了攻撃が親を参照する `OnDestroyActor` 中には消さない。
- R3: 同時に4096件必要となる場合も黙って未登録の番号0を使わせず、ホストの寿命中に警告を1回出す。`MarkTarget` は番号が正でなければ登録せず、投射物の直接命中候補と連鎖先候補は番号0を除外する。`BossNativeSameLife` も正の番号を必須とする。既存記録・本体攻撃・他機能は続行し、空きが戻れば新規登録を再開する。上限増加、生存記録の追い出し、毎フレームの割り当ては追加していない。
- R4: 既存の `dreamforge_bosslives` と30秒間隔のCount・peak・released出力を維持した。診断OFFなら文字列生成や辞書走査は行わない。

### この工程で実行した検証

- 前節の `CustomAfterMicrosoftCommonTargets=.../PreFix.targets` 付き指定コマンド: **exit 1、2成功・1失敗**。再度 `--no-build` で出力を確認し、非プール条件だけ `retained=4096, nextLife=0, nextMarks=0`。修正前の既存ソースをコンパイル入力として使った結果である。
- **revert check**: 現在の既存テストを変更せず、`BossNativeDisabledLifetime.Postfix` の解放呼び出しだけを一時的に空にした。`DOTNET_ROLL_FORWARD=Major dotnet build tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-incremental -p:PythonCommand=python3 -v:q '-clp:ErrorsOnly;Summary'` はexit 0。続く `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed'` は **exit 1、2成功・1失敗**。同じ非プール条件で1024/2048/3072/4096件へ蓄積した。
- 解放呼び出しを復元し、同じ `--no-incremental` ビルド（exit 0、95警告・エラー0）と再現テストを実行: **exit 0、3成功・失敗0**。すべての条件で1024/2048/3072/4096回終了後は0件、次の対象登録後は `retained=1, nextLife=4097, nextMarks=1`。修正を一時的に戻すと再現テストが失敗し、戻すと成功することをこの工程で確認した。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~Boss&FullyQualifiedName!~BossNativeActorLifetimeReproTests' --logger 'console;verbosity=minimal'`: **exit 0、169成功・失敗0**。最終コードへ復元した後の結果。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release '-p:GameDir=<Shape of Dreams のインストール先>' -p:PythonCommand=python3 -v:q '-clp:ErrorsOnly;Summary'`: **exit 0、6警告・エラー0**。最終コードで実行。参照用フォルダには `Mods` が存在しないことを確認し、ゲームへの配置なし。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj -c Release --filter 'FullyQualifiedName~NativeStartupTests|FullyQualifiedName~ModReloadTests|FullyQualifiedName~ReloadStartupTests|FullyQualifiedName~InfinityLobbyStartTests' --logger 'console;verbosity=minimal' -p:PythonCommand=python3 -v:q '-clp:ErrorsOnly;Summary'`: **exit 0、107成功・失敗0・既存skip1**。起動・再読込・開発コマンド側の既存検証であり、今回の実Actor派生型へのHarmony適用自体はこのハーネスの対象ではない。
- 中間ビルドではテスト用 `AccessTools.DeclaredMethod` に3引数のオーバーロードがなくCS1501でexit 1となった。標準の `Type.GetMethod` へ変更し、上記の最終ビルドで解消。新規テスト・既存テストの変更は行っていない。

### この工程の未実行

- R5の長時間Infinity・高密度部屋、実ゲーム起動・プレイ、診断コマンドの実コンソール呼び出しとログ推移: 明示された対象外の実機確認として未実行。
- Unity実破棄コールバック・派生型へのHarmony実適用・fake-null・Mirror実通信: 本体実行環境なし。ソース経路と参照DLLビルドまでの確認。
- 同時4096件の生存記録による上限警告・投射物の番号0除外・印登録拒否と空き後の再開: 既存テストに直接のケースがなく、新規テスト作成禁止のためコード点検まで。逐次4096回の解放漏れは上記再現テストで確認済み。
- 全テストスイート、低速・タイミング依存の性能試験、他Issueの検証: 変更範囲に絞る指示に従い後工程へ残す。再読込テストの1件は既存のRelease/JIT理由のskip。

## Startup CI の重複定義修正（2026-10-17）

CI run `38982798048` の Startup ジョブは、PR の先端 `c7658151` 単体ではなく、main `1f67ee988d394e3d668890a1765d9101744c1152` とのマージ結果 `5f409cbf10188f7405ac661a4533d264e6dc362e` をビルドしていた。main とこのブランチがそれぞれ別の位置へ近接設定4フィールドと `ConfigureMeleeCoop` を追加したため、自動マージは成功しても C# の重複定義になる。作業ブランチ単体のビルド成功ではこの不具合を検出できなかった。

`StartupGameApi.cs` の近接設定フィールドは `netCompact` の次、`ConfigureMeleeCoop` は `Detach` の次へ揃えた。値と引数は変更していない。この Issue 固有の `ConfigureBossNativeLifetimeDiagnostics` スタブは維持した。現在の main `acf1c241` も取り込み、更新履歴は双方の記載を残して競合を解消した。main との最終差分では近接設定と `ConfigureMeleeCoop` の追加差分が消え、診断スタブ1行だけが残る。

### このラウンドで実行した検証

- `gh pr view 524 --comments`、`gh pr diff 524`、`gh run view 38982798048 --log-failed --job 117247301877`（いずれも `-R Kling0012/shape-of-dreams-rpg-plan`）を確認。
- **CI の失敗再現**: `git merge-tree --write-tree 1f67ee988d394e3d668890a1765d9101744c1152 c7658151` のマージツリーを一時ディレクトリへ展開し、`dotnet build -c Release <展開先>/tests/SodRpg.Mod.Startup.Tests` を実行。**exit 1、報告と同じ CS0102 が4件・CS0111 が1件**。ログは同ディレクトリの `build.log`。
- **同じ CI ベースに対する修正後**: スタブ位置修正後の同ベースとのマージツリー `16552382ac0e819d537ef23ba1264df15d0bf46f` を別の一時ディレクトリへ展開し、同じ `dotnet build -c Release <展開先>/tests/SodRpg.Mod.Startup.Tests` を実行。**exit 0、39警告・エラー0**。
- **現在の main 統合後**: `dotnet build -c Release tests/SodRpg.Mod.Startup.Tests` は **exit 0、39警告・エラー0**。`DOTNET_ROLL_FORWARD=Major dotnet test -c Release tests/SodRpg.Mod.Startup.Tests --no-build --filter 'FullyQualifiedName~NativeStartupTests|FullyQualifiedName~ModReloadTests|FullyQualifiedName~ReloadStartupTests|FullyQualifiedName~InfinityLobbyStartTests' --logger 'console;verbosity=minimal'` は **107成功・失敗0・既存skip1**。
- **寿命管理の修正前比較**: `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed' -p:PythonCommand=python3 -v:q '-clp:ErrorsOnly;Summary' -p:CustomAfterMicrosoftCommonTargets=<worktree>/tests/SodRpg.Core.Tests/obj/issue506-pre-fix/PreFix.targets` は **exit 1、2成功・1失敗**。既存の修正前アダプタと再現テストを用い、非プール条件で `retained=4096, nextLife=0, nextMarks=0` を確認。ログは `tests/SodRpg.Core.Tests/obj/issue506-pre-fix/round-ci-pre-fix.log`。
- **現在コードへの復帰**: `DOTNET_ROLL_FORWARD=Major dotnet build tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-incremental -p:PythonCommand=python3 -v:q '-clp:ErrorsOnly;Summary'` は **exit 0、110警告・エラー0**。続く `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed'` は **exit 0、3成功**。全条件で4096回終了後は0件、次の対象は寿命4097・印1件。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~Boss&FullyQualifiedName!~BossNativeActorLifetimeReproTests' --logger 'console;verbosity=minimal'` は **exit 0、169成功**。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release '-p:GameDir=<Shape of Dreams のインストール先>' -p:ModDeployDir=<一時配置先> -p:PythonCommand=python3 -v:q '-clp:ErrorsOnly;Summary'` は **exit 0、6警告・エラー0**。配置先は一時ディレクトリ。
- 中間実行では `DOTNET_ROLL_FORWARD` なしのテストが .NET 8 不在により開始不能。インストール済み .NET 10 へのロールフォワード付きで上記対象テストを成功させた。また `dotnet test --no-incremental` は未対応引数で MSB1001 となったため、`dotnet build --no-incremental` と `dotnet test --no-build` に分けて実行した。
- マージ時に旧先端に対して実行した `git diff --cached --check` は main 由来の既存 CRLF 行を末尾空白として検出した。`git diff --cached --check origin/main` と統合後の `git diff --check origin/main...HEAD` は成功し、この PR の変更行には該当しない。

### このラウンドの未実行

- 実ゲーム起動・長時間Infinity・高密度部屋・診断コマンドの実ログ推移（R5）、Unity実破棄・派生型Harmony適用・Mirror実通信: 明示された対象外の実機確認。
- 同時4096件上限の警告・番号0の投射物除外・印登録拒否と解放後の再開の直接実行: 既存テストに該当ケースがなく、新規テスト禁止のためコード点検まで。
- 全体・低速・性能スイート、その他のテストプロジェクト: 変更範囲に絞る指示に従い後工程へ残す。Startup の再読込1件は既存のRelease/JIT理由のskip。
- GitHub Actions の修正後実行: push禁止で後工程が行うため未実行。同じ失敗ベースと現在の main との統合結果はローカルでビルド確認済み。

## PR #524 の取込実装（2026-10-17）

元 PR #524 の変更を新しい main へ移植した。寿命解放フック、上限時の警告と番号0の追跡除外、`dreamforge_bosslives` 診断、再現テスト、Startup ハーネスの診断スタブ、更新履歴断片を同じ構成で持ってきた。main 側に近接設定スタブは既にあるため、追加したスタブは `ConfigureBossNativeLifetimeDiagnostics` の1行だけである。あわせて受け付け審査の指摘へ対応した。

- **GetTypes の例外処理**: `BossNativeDisabledLifetime.TargetMethods` の型列挙を `ReflectionTypeLoadException` で保護した。読み込みに失敗した型があっても `LoaderExceptions` を捨てて読み込み済みの型だけへパッチを適用し、この解放フック全体が失敗しないようにする。
- **一時無効化の保持**: postfix は `Entity.isActive` が false の実体だけを解放する。破棄の経路では `ICleanup.OnCleanup` が `Network_isActive=false` を設定してから GameObject が無効になるため、破棄・プール返却は今までどおり解放される。`SetActive(false)` しただけの実体は isActive が true のままなので寿命番号を保持し、再び有効にしたときに印と命中履歴が同じ番号で続く。
- **上限警告の再武装**: 上限到達時の警告は1回だけだが、解放で件数が上限未満へ戻ると警告フラグを解除し、次の到達で再び出るようにした。長期運用で上限の再到達に気づきやすくする。
- **この文書の移植性**: 検証マシン固有の絶対パス（ゲームデータのインストール先、NUGET_PACKAGES、一時展開先）をプレースホルダーか相対パスへ置き換えた。記録した検証の内容と結果は変えていない。

### この工程の検証

この節の内容は結果ファイル（`.gh-auto-result.json`）と PR 本文へ記載する。この文書は追跡対象のため、コミット時点の結果を次節以降へ追記していく。

### この工程の未実行

- Unity実破棄コールバック・派生型へのHarmony実適用・一時無効化した実体の再有効化・fake-null・Mirror実通信: 実行環境なし。コード点検とリンク済み再現テストまで。
- 同時4096件上限の再警告・番号0の投射物除外・印登録拒否と解放後の再開の直接実行: 既存テストに該当ケースがなく、新規テスト作成禁止のためコード点検まで。
- R5の長時間Infinity・高密度部屋・診断コマンドの実ログ推移: 明示された対象外の実機確認。
- 全体・低速・性能スイート、他のテストプロジェクト: 変更範囲に絞る指示に従い後工程へ残す。
