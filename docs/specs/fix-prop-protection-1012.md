# 石の恒久無敵の調査（2026-10-12、v2.10.27）

報告された「約4割・ロビーに戻って潜り直すと解除」の原因は未確定。続報で敵にも発生したため、以下の初回調査に加えて Entity 共通の寿命・判定・同期を再調査した。末尾の続報に現時点の修正と確度を記録する。実ゲーム・協力プレイの再現ログはなく、報告症状の解消確認は実ゲームで必要。

## 本体で確認したこと

参照元: `/home/wang/dev/sod-gamedata/decompiled/sod-decomp`、本体 r.1.4.0.13。

- `Dew.Core/PropEntity.cs:41`: 被弾成功時だけ `prop_iframe` の InvulnerableEffect を `hitInvulnerableTime`（初期値 0.1 秒）で作成する。
- `Dew.Core/Actor.cs:911`: 無敵で拒否された攻撃は damage-negated 通知だけで return する。被弾通知まで進まないため、継続ダメージだけで `prop_iframe` を更新し続ける経路ではない。
- `Dew.Core/StatusEffect.cs:354`: サーバの FrameUpdate でタイマーが進み、満了時に Destroy する。同ファイル :472 で BasicEffect の登録を解除する。
- `Dew.Core/Se_GenericEffectContainer.cs:15`: 共通コンテナは部屋内で再利用されるが、`Actor.ApplyPendingBasicEffect` が effect・duration・id を新しく上書きし、OnCreate が SetTimer を呼ぶ。商人の無限時間がそのまま石へコピーされる処理は確認できない。
- `Dew.Core/PropEnt_Stone_Gold.cs`・`PropEnt_Stone_DreamDust.cs` は PropEntity。`Shrine_GoldBit` 等は Shrine（Actor）であり、壊す石とは型が異なる。
- `PropEnt_Stone_Nightmare` は存在するが、本体の部屋 Unity 資産とローカライズ本文が手元にないため、「不快な記憶 / Unpleasant Memory」の部屋 ID・正式表記と配置される石型の対応は未確認。CHANGELOG の部屋名は依頼時の表記を保持した。

## MOD の候補と判定

| 候補 | 調査結果・今回の扱い |
| --- | --- |
| `InfinityBossArena.cs:252,259,309` の Invulnerable / Untargetable | 唯一の直接付与元。型付きの Polaris ボスだけに付与する。石を直接対象にする経路なし。導入は `c40a78a5`（Infinity ボス抽選）。作成時に MOD 識別子を追加し、安全策で BasicEffect 自体の所有を記録できるようにした。 |
| `HostAuthority.UnbowedMind.cs:44` の Unstoppable | 明示的に Hero のみ。導入は `d64e744a`。石への直接付与なし。既存の `DreamforgeRPG.UnbowedMind` ID で安全策の対象を識別する。 |
| `HostAuthority.BossMechanisms.cs:974` の Unstoppable | HeroRuntime.Hero のみ。BossBasicEffectReuse はこの executor の専用インスタンスだけを初期化し、本体の石・商人の InvulnerableEffect には触れない。既存の `DreamforgeRPG.Boss.Container.*` ID で識別する。 |
| `NativeActorLifetime.cs:79,124` の更新停止・Retire | `6b673a6f`（v2.10.20）。被害者が生存・activeInHierarchy・Status 有りなら更新を許可し、無効なら破棄する。プール OnDisable で独自状態を除去する。普通の石のタイマーを止め続ける条件は特定できなかった。 |
| `RoomVisualSweep.cs`・`ClientSession.RoomSweep.cs` | `f0934244`（v2.10.20）。destroyed Monster の renderer を隠すだけ。石の無敵・チーム・targetable は書き換えない。 |
| StatusEffect タイマーのパッチ | EternalFlame は `Se_U_EternalFlame_Curse` のみ。HerWorld の満了ラッパーは他型ではそのまま Actor.Destroy を呼ぶ。共通コンテナの duration を延長する処理なし。 |
| `InfinityServiceRooms.cs` | 未訪問の通常 Combat ノードを Merchant に変えるが、Entity の型・所有者・無敵・チームは変更しない。イベントノードは対象外。 |
| ProcessReceivedDamage / Actor.DealDamage パッチ | ダメージ補正・発動元識別・HP 減少記録。石を商人扱いにする処理、チーム変更、無敵付与なし。 |
| 道標・悪夢化・出来事・Limbo・星図・エッセンス・ロードアウト | MOD の敵補正は Monster、装備補正は Hero。PropEntity に対する恒久無敵・Untargetable の付与元やチーム変更は見つからなかった。熱病・棘・下がれ！等の追加ダメージも、本体の被弾成功時無敵の条件を変えない。 |

Infinity のガード解除に再利用済み参照を Destroy する別の懸念はあるが、他の効果を早期解除する方向であり、石へ恒久無敵を付与する説明にはならない。今回その独立した箇所の変更は行っていない。

## 初回の限定安全策

- `Actor.CreateBasicEffect` で `DreamforgeRPG.*` ID を持つ Invulnerable / Unstoppable / Untargetable だけを識別する。石への作成を拒否する。失敗時もアプリ・MOD 全体を止めない。
- 作成から3秒以上経過した MOD の保護効果が石の EntityStatus に登録されている場合、サーバで1秒間隔の走査により、その BasicEffect の登録だけを本体 API で解除する。3秒は石への登録時刻ではなく、MOD による効果作成時刻から数える。共有コンテナは破棄しない。
- MOD が作った BasicEffect 自体を弱参照で記録するため、コンテナの再利用だけで所有情報を失わない。同じ MOD の BasicEffect の再利用は既存記録の時刻を更新し、反復による記録オブジェクトの割り当てを増やさない。
- `prop_iframe`、本体の無限 Unstoppable、識別できない効果は変更しない。商人の基底型 `PropEnt_Merchant_Base` 全体と MockEntity は対象外。
- 走査は既存 allEntities / _basicEffects を使用し、毎フレームの一覧作成・割り当ては追加しない。解除 API が見つからない場合は回復機能だけを無効にする。
- 発動した場合は拒否・回復の型をログに残す。このログが次回の実ゲーム原因調査の根拠になる。

## 初回の検証

最終変更後の Release ビルド成功（0エラー、既存警告6件）。`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all` は3,164件合格・失敗0件・既存スキップ5件。テスト追加・変更なし。実ゲーム・協力プレイの再現、部屋名・石型の資産上の対応、約4割となる抽選条件、報告症状に対する効果は未確認。

## 続報: 敵にも発生（2026-10-12）

### 被弾・対象判定の共通条件

- `Dew.Core/Actor.cs:864,876,911`: ダメージはサーバで実行。inactive・死亡・knockout の対象を除外し、`hasDamageImmunity`（Invulnerable / Protected）または個別の `DamageData.isBlockedByImmunity` で拒否する。`IgnoreDamageImmunity` 属性はこの拒否を回避する。石の `takeOneDamageOnHit` はダメージを1に丸めるだけで、無敵拒否より前に処理する。
- `Dew.Core/EntityStatus.cs:387,437,1623`: Invulnerable・Protected・Untargetable・Uncollidable は `_basicEffects` の mask の OR から算出し、集計は `isAlive` を検査しない。参照カウントではなくリストが根拠。`RemoveBasicEffect:1533` と `CalculateStatsIfDirty` が必要で、SyncVar mask を直接書き換えてはいけない。
- `Dew.Core/DewCollider.cs:271` / `DewPhysics.cs:194`: proxy collider の範囲判定に加え active・validator・Uncollidable を検査。`AbilityTargetValidator.cs:18` は所有者のチーム関係（Enemy / Neutral が標準）を検査。`ControlManager.cs:1981` / `ActionCast.cs:93` は Untargetable によって選択を拒否する。共通 `Entity.isHittable` プロパティはなく、`DamageInstance.hittable` は validator。
- `Dew.Core/Entity.cs:479` は破棄時に proxy collider を除去。`EntityControl.cs:957` は初期化時に proxy を作成し、位置を更新する。描画 renderer の非表示だけでは proxy は消えない。
- `Dew.Core/StatusEffect.cs:21,57,62` の victim・時間、`EntityStatus.cs:738` の mask、HP、Actor の active / owner は本体 Mirror の SyncVar。BasicEffect 自体の登録解除はサーバ専用。参加者の効果をローカル破棄してもサーバの無敵は解除・付与されない。

### 候補と確度

1. **#357 `6b673a6f` の参加者側ローカル破棄に具体的な問題（高確度）**。旧 `NativeActorLifetime.CanUpdate` は未解決 victim を2フレームだけ待ち、以後 `Retire` で `Network_isActive=false` と `OnStop` を実行。本体の `DewNetworkBehaviour.cs:194` は更新管理から unregister する。後から victim SyncVar が解決しても `CleanupRequested` が残り、更新を再開しない。部屋遷移・初期スポーンの順序で起き得る。修正は、参加者の未解決参照では更新だけ保留し、サーバの active=false / despawn を待つ。サーバでも生きた victim の GameObject が一時非活性なら永久 Retire にしない。**ホストの無敵判定が止まる単独原因との因果関係は未確認**。
2. **本体の登録解除漏れ経路（コード上は高確度、報告原因として未確認）**。`StatusEffect.cs:466` は victim が null なら `_basicEffects` の解除・Clear より先に return。`OnDisable:406` はタイマーを初期化するが `_basicEffects` をクリアしない。`EntityStatus.UpdateStatusEffectInfo:1623` は古い登録も集計するので、親コンテナが破棄・再利用された保護が残れば敵・石両方がサーバ上で無敵／非対象化になる。#357 はこうした無効 victim の Retire を追加したが、通常の生きた敵・石でこの参照不整合が生じる具体的条件は未特定。今回、破棄時に BasicEffect 自体の victim へ保護を解除する処理と、登録漏れの回復を追加。
3. **#355 `f0934244` / `942a464a` は根拠なし**。RoomSweep は destroyed Monster の Renderer だけ、DissolveReset は `_isDissolving` だけ、色・scale の解除も描画 modifier だけ。active な Entity・proxy・チーム・mask を変更する経路なし。
4. **#354 `1d4e2784` netlite は本体同期への干渉なし**。変更は Dreamforge の成長・ボス予兆等の独自メッセージのみ。`ActorTransformSync` へのパッチ・送信間引きは現行リポジトリに存在しない。敵・石の HP / mask / owner / 位置 SyncVar を省略するコードは見つからない。
5. **#358 AutocastPressure / #368 Back Off は根拠なし**。前者は性能通知と Hero の自動発動制限、後者は Hero の SafeAction に handler を追加・削除。Enemy / Prop の無敵・所有者・衝突判定を変更しない。

### 続報の修正と安全策

- 既存 `6bb15dc6` の MOD 保護拒否・回復は維持し、既存テストにも変更を加えない。
- `NativeEntityProtectionRecovery.cs` は Invulnerable / Protected / Untargetable / Uncollidable の登録だけを記録。弱参照の記録は初回登録時に作成し、通常の1秒走査は既存 allEntities / BasicEffect リストと索引ループを使い、一覧や LINQ を作らない。
- サーバの生きた Monster / PropEntity に対し、登録から3秒以上経過し、保護の parent / victim / 親の生成時刻 / 親の BasicEffect リストが一致しないか、`isAlive=false` なのにリストに残っている場合、その登録だけ解除。商人基底型全体・MockEntity・BossMonster・Boss / MiniBoss 型は走査対象外。
- 親が正しく生きている場合は、本体 `prop_iframe` または EntityVisual の `_spawnProtections` に入っている出現保護だけを対象とする。本来の duration + 3秒以上経過し、残り時間が少なくとも3秒進まず／更新されず残留している時だけ解除。型・時間一致だけで出現保護と推測しない。
- 通常のスクリプト保護、タイマーが進んでいる保護、無期限保護、石の恒久 Unstoppable、Hero の効果は期限による走査解除をしない。生きたボスの正当な演出を維持。破棄処理にある別 victim への登録漏れは本体の破棄意図に従って即時解除する。
- 本体 API でリストから解除して mask を再集計・通常同期する。共有コンテナを Destroy せず、他の効果・Entity 本体・コライダー・チームには触れない。Reflection が取れない場合は該当回復だけ無効となる。

### 続報の検証と残件

指定 Release ビルドは0エラー（既存警告6件）。既存全テストは3,167件合格・失敗0件・既存スキップ5件。今回テスト追加・変更なし。実ゲーム、ホスト／参加者の同時観測、約4割の発生条件、故障時の effect / victim / mask / proxy 情報は未確認。報告症状全体の根本原因と修正の実機での効果は確定していない。
