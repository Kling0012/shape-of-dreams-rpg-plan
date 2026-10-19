# Issue #889 の拾得演出の調査記録

ドリームダストと富のエッセンスのコインを拾うと、獲得演出がプレイヤーにまとわりついたまま消えないという報告を調べた。対象は報告時点の v3.8.3 と本体 r1.4.1.2。2026-10-20 に本体 DLL の逆コンパイル、MOD のコードとの照合、既存テストを実行した。

コードから、拾得物の破棄待ちが終わらないと追従演出の寿命管理も始まらない経路を確認した。ただし、報告環境で破棄待ちを停滞させた直接の要因は未確認。画面上の症状は未再現であり、以下は構造上の原因候補と、その経路を防ぐ理論修正の記録である。

## 生成と追従

両者とも本体の `PickupInstance` を通る。

| 対象 | 生成と獲得の経路 |
|---|---|
| 富のエッセンス | `Gem_R_Wealth`（Dew.Contents）が `PickupManager.DropGold` を呼び、`CreatePickupInstance` で大・中・小のゴールド宝珠を生成する。宝珠を拾うと `Pickup_BaseGoldOrb.OnPickup` → `GrantGold` → `EarnGold` が実行される。生成時の `goldSpawnEffect` は別の `FxPlayNewNetworked` 経路 |
| ドリームダスト | `PickupManager.DropDreamDust` が `CreatePickupInstance` で `Pickup_DreamDust` を生成する。拾うと本体の `EarnDreamDust` / `AddDreamDust` が実行される。MOD の `HostAuthority.Currency.cs:OnDreamDustGiven` は `player.EarnDreamDust(bonus)` で加算し、独自の追従 FX は生成しない |

拾得時の共通経路は次のとおり。

1. `PickupInstance.OnPickup` が `pickupEffect` と `pickupEffectOnHero` を再生する。
2. プレイヤー側の演出は `FxPlayNetworked(pickupEffectOnHero, hero)` から `DewEffect.PlayNetworked(NetworkIdentity, GameObject, Entity)` へ進む。
3. `PlayAttachedEffectMessage` は `isNew=false` で、`isPooled` も立てない。受信側の `HandleEffectMessage` は `DewEffect.Play(effect, hero, pickup)` で元の子オブジェクトをその場再生する。ホストのローカルクライアントと参加クライアントがこの経路を通る。
4. `FxAttachToEntity.OnAttachToEntity(hero)` が対象を保持し、`hero.Visual._attachedEffects` へ登録する。`FrameUpdate` が演出の位置を更新する。

ここで Transform の親をヒーローへ付け替える処理はない。演出は拾得物 Actor の子のまま、位置を更新してヒーローへ追従する。

## 停止・寿命・プール返却

| 再生の種類 | 本体の後始末 |
|---|---|
| 複製のプール再生 | `DewEffect.PlayIntoPool` が `EffectAutoDestroy.Register(clone, pooled: true, parent)` に登録する。`Tick` は再生完了または登録後30秒超を検出し、`DestroyEntry` が停止・追従解除・`SetActive(false)` を行う。部屋替わりでも登録済みの演出を処分する |
| 拾得演出のその場再生 | `DewEffect.Play(GameObject, Entity, NetworkIdentity)` 自体には `EffectAutoDestroy.Register` がない。親 Actor の `CustomDestroyRoutine` が FX 成分を登録し、再生終了またはタイムアウト後の Actor の破棄・プール返却で子も無効になる |

ダストとゴールド宝珠はどちらも `reuseInRoom=true`。`SpawnManager.Destroy` は Actor の粒子を再利用用にリセットし、Actor を無効にしてプールへ返す。子の `FxAttachToEntity.OnDisable` が追従登録を外す。`PickupInstance.OnDestroyActor` が明示的に停止するのは `mainEffect` で、`pickupEffectOnHero` は停止対象に含まれない。

`DewEffect.Stop` は粒子の放出と各 `IEffectComponent` を止める。ただし、粒子の全消去は個々の FX の設定に依存する。停止だけで残留粒子やフェード中の描画が即座に消えるとは限らない。

## 破棄待ちから演出が残る経路

`Dew.StepDestroy` と `Actor` の逆コンパイルから、次の順序を確認した。

1. `InitialPoll` が親を外して `DontDestroyOnLoad` に移し、`ICleanup.OnCleanup` を呼ぶ。
2. RPC の待ち時間のあと、`ICleanup.canDestroy` が true になるまで再確認する。
3. `Finalize` が `NetworkServer.UnSpawn` を呼ぶ。そのあと `ICustomDestroyRoutine.CustomDestroyRoutine` が `EffectAutoDestroy.Register(gameObject, _cachedFxComponents)` に登録する。

`Actor.ICleanup.canDestroy` は `childrenInNetwork` が残っている間 false を返す。destroy lock の60秒上限は、この子の残存条件には適用されない。したがって canDestroy 待ちが終わらない状態では、Actor の FX 登録も30秒タイマーも始まらず、子の追従演出が残り得る。

これはコードで示せる経路であり、報告環境で `childrenInNetwork` が実際に残っていたことを示す実行時記録はない。他の停滞要因も排除していない。最近の軽量化や本体更新が停滞を作ったと断定する根拠は見つからなかった。

## 軽量化ごとの確認

| 対象 | コードから確認した範囲と判断 |
|---|---|
| 66cd6f2a / `NativeEntityRotationIdlePerf` | `EntityControl.DoRotateTowardsDesiredAngleTick` の静止時の回転書き込みを省く。拾得 FX の停止、追従解除、破棄登録を変更しない |
| c6d561f4 / `NativeHeroWeaponIdlePerf` | `Hero.FrameUpdate` の武器 localScale の同値書き込みと SmoothDamp を省く。拾得 FX の寿命管理を変更しない |
| db9cd806 / `NativePlayerTimerIdlePerf` | `DewPlayer.LogicUpdate_InGame_NetworkedOnScreenTimer` の同値再送を省く。拾得物や FX の後始末を変更しない |
| `NativeWeaponFxPerf` | `Hero.FrameUpdate` の武器描画用の読み取りだけを置き換える。holsteredWeapon がない場合に空の一覧を返すが、元の `_attachedEffects` を消去したり追従更新を止めたりしない |
| `NativeAttachFaceIdlePerf` | `AttachToFace.TryGetWorldPositionAndRotation` の新しいメッシュベイクが不要な問い合わせを省く。`FxAttachToEntity.FrameUpdate` や FX の後始末は対象外 |
| `NativeEmbracePositionPerf` | `Ai_EmbraceNewIdentity.UpdatePositions` の位置読み取りをまとめる。拾得物の型や寿命管理は対象外 |
| `NativeSleepPositionPerf` | `ActorManager.CanEntitySleep` の1回の判定中に同じ Entity の位置を再利用する。型の判定、CanSleep、距離のしきい値は残す。拾得物の破棄や追従解除を変更しない |
| `NativePositionSyncIdlePerf` | `EntityControl` の静止時の位置同期 heartbeat を省く。位置・速度・向きの変化による同期は残す。FX の停止や破棄登録を変更しない |
| `NativeActorLifetime.cs` | `BeforeClientStop` / `AfterCleanup` は Actor の `OnDestroyActor` を前倒しし、`Reset` は管理情報を消す。`CanUpdate` は StatusEffect 以外を通す。PickupInstance の更新や canDestroy 待ちを止める処理は見つからない |
| `NativeFxBudget` | 登録された型との完全一致で判定する。`Pickup_DreamDust` と `Pickup_BaseGoldOrb` 派生は対象外で、拾得演出を省かない |
| `NativeProcCoalesce` / `NativeProcSpawnSpread` | 特定の攻撃弾の生成・命中処理をまとめる。生成の置換は Charcoal・Blade・Accuracy・Spiral の `CreateAbilityInstance` 系で、拾得物の `CreatePickupInstance` は変更しない |
| `NativeProcImpactFx` | PlayNetworked の Prefix は攻撃 Actor の種類を選別する。拾得物は kind=0 となり、本体の再生を通す |

これらの範囲では両演出の後始末を止める変更を確認できなかった。修正では軽量化の設定や実装を変更していない。

## 本体 r1.4.0.13 と r1.4.1.2 の比較

2026-10-20 に両版の `Dew.Core.dll` を ilspycmd で逆コンパイルして比較した。

| 型 | 差分 |
|---|---|
| `PickupInstance` / `Pickup_DreamDust` / `Pickup_BaseGoldOrb` | 同一 |
| `EffectAutoDestroy` / `FxAttachToEntity` / `SpawnManager` | 同一 |
| `DewEffect` | `IsDetachable` の追加と `PlayDetached` 周辺の変更 |
| `Actor` | `ClearData` の追加と `ClearPooledEventsAndProcessors` からの呼び出し追加 |

調べた8型では、拾得演出の生成・その場再生・後始末の経路に変更はなかった。本体更新全体の影響を排除した結果ではない。

## 理論修正

`src/SodRpg.Mod/PickupFxLifetime.cs` で、対象の演出の再生時から独立して寿命を監視する。

- `FindPickup` はダストまたはゴールド宝珠の `pickupEffectOnHero` に一致し、Actor 自身ではない子オブジェクトだけを選ぶ。別のプール複製や他の拾得物は対象外。
- `DewEffect.Play(GameObject, Entity, NetworkIdentity)` の Postfix がガードを開始する。通常の生成・再生を通したうえで、本体の30秒上限を再生開始時から数え、0.5秒間隔で確認する。期限後の次の確認で後始末するため、ゲームの時刻でおおむね30〜30.5秒が上限となる。
- 部屋遷移・部屋ロード・元の拾得物の消失・別寿命への再利用でも後始末する。通常の UnSpawn で netId が0になることや Network_isActive が false になることだけでは再利用と判定しない。
- `Retire` が `OnAttachToEntity(null)` で追従を外し、`DewEffect.Stop` を呼ぶ。さらに粒子を `StopEmittingAndClear` で消去し、演出用の子だけを無効にする。停止や追従解除が例外になっても、finally で子を隠す処理を試みる。
- Prefix の `RestoreForPlayback` がこのガードで隠した子を次回の本体再生前に戻す。パッチが失敗して無効になったあとも復元処理は通す。
- `Prepare` の契約確認や実行時処理が失敗したら、警告を出してこのパッチだけを無効にする。MOD 全体や他の軽量化は停止しない。

本体の30秒タイムアウトは Actor の破棄工程で登録されてから数える。今回のガードは再生開始から数えるため、登録が遅れた演出は本体だけの場合より早く打ち切られ得る。通常の短い演出は本体の再生・後始末を通る。新しい毎フレーム処理やシーン走査はなく、粒子配列はガードの初回開始時に取得する。

Actor 本体の無効化・破棄・プール返却は本体に任せる。獲得量、獲得確率、ダメージ、通信形式は変更していない。

## 実行した検証

- 本体 r1.4.1.2 の DLL 参照による Release ビルド。`GameDir` を本体データの場所、`ModDeployDir` を `/tmp/dreamforge-pr893-build` に指定した。成功、0エラー・警告6件。
- `sh tests/repro/issue889/verify_r1412_fx_lifecycle.sh`。構造事実17項目がすべて OK、終了コード0。画面上の症状を再現するテストではない。
- 上記8型の旧版・現行版 DLL 比較。
- `Issue73.Native.Tests` の EntityRotationIdlePerfTests・HeroWeaponIdlePerfTests・PlayerTimerIdlePerfTests。19件成功。
- `SodRpg.Mod.Startup.Tests` の FrostFxLifetimeTests。5件成功。既存の類似ガードの検証であり、今回の拾得ガードの直接検証ではない。
- `SodRpg.Mod.FxBudget.Tests` の NativeFxBudgetTests。6件成功。

検証スクリプトは `~/.dotnet/tools/ilspycmd` と `~/dev/sod-gamedata` の本体 DLL がある環境で動く。ない環境では skipped と出して終了する。今回の実行は skipped ではない。

2026-10-20 の修正ラウンドでも、構造確認17項目、候補3件のテスト19件、類似ガードのテスト5件、演出上限のテスト6件を再実行し、すべて成功した。Release ビルドも `ModDeployDir=/tmp/dreamforge-pr893-repair-build` で成功した（0エラー・警告6件）。旧版と現行版の DLL から8型を改めて逆コンパイルし、上表と同じ差分を確認した。既存の実装と調査は保持し、結果 JSON の `requirements_done` に FR-1〜FR-11 の対応先を個別に報告する。

### 要件報告の再確認

2026-10-20 の自動回復ラウンドでは、実装済みの11要件と対応表を照合した。追加の実行時コード修正は必要なく、寿命ガードと既存の調査を保持した。結果 JSON には `requirements_done` を `id` と `where` の組で11件記載し、PR 本文の未実装表示を置き換える。

- `sh tests/repro/issue889/verify_r1412_fx_lifecycle.sh` は構造事実17項目すべて OK。画面上の症状の再現ではない。
- 本体 r1.4.1.2 の DLL を参照した Release ビルドは成功した。配置先は `/tmp/dreamforge-pr893-recovery-build`。今回は差分ビルドで、0エラー・警告0件だった。
- 対象テストは既定の起動方法では .NET 8 ランタイム不足で中断した。`DOTNET_ROLL_FORWARD=Major` を指定し、導入済みの .NET 10 で再実行すると、候補3件のテスト19件、FrostFxLifetimeTests 5件、NativeFxBudgetTests 6件がすべて成功した。
- 候補3コミットと軽量化の対象範囲、`Actor.ICleanup.canDestroy` の子の残存条件を再確認した。旧版・現行版 DLL の8型比較は再実行しておらず、上表は前ラウンドの結果である。

## 未実行

- ゲーム内での症状再現と修正確認、ソロ・協力プレイ各画面、演出が多い場合と性能の確認。この環境ではゲームを起動できないため。
- 拾得ガードの直接の挙動テストと、修正前に戻して失敗することの確認。既存の直接テストがなく、新規テストは禁止されているため。構造確認スクリプトは修正前後の両方で通る。
- フルテスト、Dreamforge の `--all`、`SODRPG_SLOW=1`。今回の作業は関連部分の検証に限定する指示のため。

## 要件との対応

| FR | 対応する実装・調査 |
|---|---|
| FR-1 | `src/SodRpg.Mod/PickupFxLifetime.cs` の `FindPickup` がダストを選び、`Begin` / `CheckLifetime` / `Retire` で停止・追従解除・粒子消去・子の無効化を行う |
| FR-2 | 本記録の「生成と追従」「停止・寿命・プール返却」と `tests/repro/issue889/verify_r1412_fx_lifecycle.sh` |
| FR-3 | 本記録の「軽量化ごとの確認」。位置・回転、毎フレーム更新、Actor の破棄・寿命、native-fx-budget、NativeProcCoalesce / NativeProcSpawnSpread を個別に確認した |
| FR-4 | 本記録の候補3コミットの確認と「本体 r1.4.0.13 と r1.4.1.2 の比較」 |
| FR-5 | 本記録の「破棄待ちから演出が残る経路」。本体の `Dew.StepDestroy` → `Actor.ICleanup.canDestroy` → `CustomDestroyRoutine` → `EffectAutoDestroy.Register` の順序から原因候補を特定した。報告環境の直接の停滞要因は未確認 |
| FR-6 | `src/SodRpg.Mod/PickupFxLifetime.cs` の `FindPickup` / `Retire`。対象の演出の子だけに消失処理を加え、原因と確認できなかった軽量化は戻さない |
| FR-7 | `src/SodRpg.Mod/PickupFxLifetime.cs` は演出だけを変更する。PR の差分に獲得量・確率を変更する箇所はない |
| FR-8 | `src/SodRpg.Mod/PickupFxLifetime.cs` の `Prepare` / `Disable`、例外時にも子の無効化を試みる `Retire`、パッチ無効化後も復元する Prefix |
| FR-9 | 本記録の「生成と追従」「停止・寿命・プール返却」。MOD 側の `OnDreamDustGiven` と本体側の `Gem_R_Wealth` から両者の共通する拾得経路まで照合した |
| FR-10 | 本記録の「軽量化ごとの確認」と「本体 r1.4.0.13 と r1.4.1.2 の比較」。指定された軽量化、3コミット、本体更新を確認した |
| FR-11 | `src/SodRpg.Mod/PickupFxLifetime.cs` の `Begin` / `CheckLifetime` / `Retire` / `RestoreForPlayback`。ダストとゴールド宝珠の両演出を監視し、再利用前の表示も復元する |
