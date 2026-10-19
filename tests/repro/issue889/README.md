# Issue #889 の再現調査の記録

ドリームダストの獲得演出と、富のエッセンスのコイン獲得演出がプレイヤーに
まとわりついたまま消えない、という報告（報告時点 v3.8.3・本体 r1.4.1.2）について、
ゲームを起動できないこの環境で試した再現の手段と結果、そして修正工程が原因を
絞り込むために必要な、コードから確かめた作られ方・消え方の事実を残す。

## 試した再現の手段と結果

| 手段 | 結果 |
|---|---|
| ゲーム内コマンド `dreamforge_simkill` によるシミュレーション | 未実行。ゲームを起動できない環境のため |
| 実 DLL（r1.4.1.2）からの逆コンパイルによる構造の検証 | 実行した。`verify_r1412_fx_lifecycle.sh` が全事実 OK（17/17） |
| 単体テスト（tests/ の二重境界方式）による再現 | 作らなかった。どの MOD コードが消える処理を止めているかがまだ分かっておらず、仮定を打ち込んだテストは再現にならないため（理由は下記） |

症状そのもの（画面上で演出が残り続ける）は Unity の実行時挙動（パーティクルの
再生、Mirror の破棄順序、Actor プールの再利用）で決まる。この段階では原因の
箇所が特定できていないため、赤になる再現テストは書けず、**未再現**として
事実の記録を残す。スクリプトは本体の前提が今後も崩れていないかを確かめる
検証として、そのまま使える。

## 2つの演出の作られ方（本体 r1.4.1.2・逆コンパイルで確認）

両者とも本体の「拾得物」（PickupInstance）の同じ経路を通る。

- **富のエッセンス（Gem_R_Wealth、Dew.Contents）**: 撃破のたびに
  `PickupManager.DropGold` でゴールド宝珠（Pickup_Large/Medium/SmallGoldOrb）を
  落とし、`goldSpawnEffect` を `FxPlayNewNetworked` で出す。宝珠は
  `ActorManager.CreatePickupInstance` で作られた Actor で、吸引力でプレイヤーへ
  飛び、拾うと `Pickup_BaseGoldOrb.GrantGold` が EarnGold する。
- **ドリームダスト（Pickup_DreamDust）**: `PickupManager.DropDreamDust` から同じく
  Actor として落ちてきて、拾うと EarnDreamDust する。MOD 側
  （`HostAuthority.Currency.cs` の `OnDreamDustGiven`）はこの拾得に上乗せで
  `player.EarnDreamDust(bonus)` を呼ぶだけなので、MOD 独自の演出は別経路ではない。

拾得の瞬間、`PickupInstance.OnPickup` は `pickupEffect` と `pickupEffectOnHero` を
`FxPlayNetworked` で再生する。`pickupEffectOnHero` が「プレイヤーにまとわりつく」
側の演出で、`DewEffect.PlayNetworked(NetworkIdentity, GameObject, Entity)` は
`isNew=false`・`isPooled=false` で全員へ送る。クライアントは親 Actor の子オブジェクト
（演出の元）をその場で再生し、`FxAttachToEntity.OnAttachToEntity(hero)` が
`hero.Visual._attachedEffects` へ登録して、FrameUpdate でプレイヤーに追従させる。

## 2つの演出の消え方（同じく逆コンパイルで確認）

- **プール再生（FxPlayNewNetworked 側）**: `DewEffect.PlayIntoPool` が複製を
  `EffectAutoDestroy.Register(clone, pooled: true, parent)` に登録する。
  `EffectAutoDestroy.Tick` は再生が終わった複製を `DestroyEntry` で始末し、
  `attachables.OnAttachToEntity(null)` で追従を外し、`SetActive(false)` で
  プールへ返す。30 秒の打ち切りと部屋替わりの一掃もあり、登録された複製が
  30 秒を超えて残ることはない。
- **その場再生（FxPlayNetworked 側・拾得演出はこちら）**: `EffectAutoDestroy`
  への登録はこの経路にはない。演出の元は親 Actor（拾得物）の子のまま残り、
  親の破棄と一緒に消える。本体の Actor は `ICustomDestroyRoutine` で自分の
  FX 成分を `EffectAutoDestroy.Register(gameObject, _cachedFxComponents)` に
  預け、FX が止まるまで物体を残す（こちらも 30 秒で打ち切られる）。
  なお `PickupInstance.OnDestroyActor` が止めるのは `mainEffect` だけで、
  `pickupEffectOnHero` は対象外。

## 修正工程が調べるべきこと（この段階で絞り込めた範囲）

- 拾得演出は「登録なしのその場再生」で、消える保証を親 Actor の破棄に預けている。
  親の Actor がプール再生（`reuseInRoom=true`。Pickup_DreamDust と宝珠は両方該当）
  で、破棄の代わりに無効化・再利用されると、`pickupEffectOnHero` の後始末を
  誰もしない状態が起き得る。MOD の `NativeActorClientDespawn`
  （Actor.OnStopClient の Prefix で OnDestroyActor を前倒し実行）と
  `NativeActorLifetimeReset`（Actor.OnDisable の Postfix）がこの順序に
  どう関わるかを最初に確かめる価値がある。
- 残った `FxAttachToEntity` は `hero.Visual._attachedEffects` に登録されたまま
  追従し続ける。`NativeWeaponFxPerf` がこの一覧を触っている（holsteredWeapon が
  無いときに空の覧へ差し替え。この差し替え自体は読む限り等価）。
- 本体 r1.4.1.2 で拾得演出の送信形（isNew/isPooled）や Actor の破棄預けが
  変わった可能性は、旧本体（`/home/wang/archive/sod-gamedata-r1.4.0.13`）との
  比較ではまだ確かめていない。報告が v3.8.3（本体対応版の初版）から出ている
  ため、旧本体との差分を最初に見るのが近道の可能性がある。

## 候補の軽量化について調べた結果

- 66cd6f2a（止まった実体の回転の書き込み省略）: `EntityControl.DoRotateTowardsDesiredAngleTick`
  の Prefix。演出の消え方には触れない。
- c6d561f4（武器スケールの SmoothDamp 省略）: `Hero.FrameUpdate` の武器 localScale。
  演出の消え方には触れない。
- db9cd806（画面内タイマーの再送省略）: `DewPlayer.LogicUpdate_InGame_NetworkedOnScreenTimer`
  の Prefix。演出の消え方には触れない。
- native-fx-budget（NativeFxBudget）: 対象は火属性系の固定覧（Gem_R_Blade 等）で、
  拾得物は載っていない。`DewEffect.Play_Imp` の Prefix/Postfix は拾得演出では
  対象外のため何もしない。
- NativeProcCoalesce / NativeProcSpawnSpread: 飛び道具と攻撃 FX のまとめ処理。
  `NativeProcImpactFx` は `PlayNetworked(NetworkIdentity, GameObject, Entity)` に
  Prefix を置くが、拾得物は種類判定（kind 0）で必ず本体の再生を通す。

いずれも「消える処理を止める」直接的な接点はこの段階では見つかっていない。
上記の破棄の順序（Actor のプール返却と前倒し OnStopClient）が残る疑いである。

## 検証スクリプト

`verify_r1412_fx_lifecycle.sh` は出荷済み DLL から上記の構造事実（A〜G の 17 項目）を
逆コンパイルで確かめる。ilspycmd（`~/.dotnet/tools`）と本体
（`~/dev/sod-gamedata`）が揃った環境でのみ動き、無い環境では skipped を出して
終了する。実行日 2026-10-20 の結果は 17/17 OK（終了コード 0）。
