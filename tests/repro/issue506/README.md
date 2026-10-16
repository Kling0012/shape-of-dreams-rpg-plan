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

この工程では本体修正・診断ログ追加は行わない。品質懸念のうち、解放漏れによる上限到達とマーク消失を再現済み。解放経路の補強・上限時の回復や警告・開発用ログは後工程の未解決事項。

## 判断したこと

実機確認不可という依頼条件に従い、逆コンパイルで確認できた非プール破棄経路を、既存のソースリンク型テストの境界として採用した。4096 件を直接辞書に詰めず、実際のマーク入口を 4096 回使い、破棄完了 prefix を毎回実行して解放漏れを観測した。プール有効と部屋内再利用ありの比較ケースも同じ再現テストに含め、常に枯渇するよう作ったテストではないことを確認した。
