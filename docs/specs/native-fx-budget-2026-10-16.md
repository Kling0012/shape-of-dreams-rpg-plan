# 本体 FX の描画予算（2026-10-16）

対象: `src/SodRpg.Mod/NativeFxBudget.cs`。既定で有効。F6・保存・Protocol の追加はない。

## 調査資料と限界

`/home/wang/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed/` の `Dew.Core.dll` と `Dew.Contents.dll`、`reflection-dump/_alltypes.txt` を調査した。必要な型だけを ilspycmd で `~/.claude/work/herobal/decomp-fx/` に出力した（リポジトリ外）。

この作業環境には Managed DLL だけがあり、Prefab/AssetBundle と実行可能な Unity プレイヤーはない。したがって各 Prefab のライト数、ParticleSystem 数、loop・寿命・trail の設定値、プレイ中の同時存在数は未測定。下記はコードから確認した生成経路であり、実測の負荷順位ではない。`Ai_R_Fireball.Explode` / `OnComplete` は提供 DLL で本文が `ret` になっており、ILSpy の例外を IL 出力でも確認した。Fireball は公開 FX フィールドと基底 Projectile の再生経路に対して同じ描画予算を適用する。

## 原因と対応

| 対象 | コードで確認した増加経路 | 対応 |
|---|---|---|
| Gem_R_Blade / Ai_Gem_R_Blade | 命中後 `baseCount` 回のインスタンス生成。一発ごとに slashEffect と hitEffect をネットワーク再生 | 実際に再生された各 FX のライト・粒子・trail を制限。連射・ダメージは保持 |
| Gem_C_Charcoal / Ai_Gem_C_Charcoal_Projectile | 与ダメージごとに遅延弾。燃焼中の敵への命中時は fireHitEffect を PlayNewNetworked | 弾の飛行 FX と切り離された命中 FX の両方に予算適用 |
| Ai_Atk_LacertaRifle / _Crit、At_Atk_LacertaRifle | Projectile が一発ごとに effectOnFly と命中・消滅 FX を再生。発射側にも AbilityTrigger の FX がある | 通常・クリティカル・発射側をそれぞれ具体型で限定して予算適用 |
| Ai_R_Fireball | 基底 Projectile の飛行 FX、explodeEffect / hitEffect の公開参照。固有本文は除去済みで未確認 | 飛行と受信後の FX 再生に適用。爆発ロジックは変更しない |
| Ai_R_Ignite / _FakeProjectile | 燃焼中は範囲内の敵ごとに igniteEffect を PlayNewNetworked。別に爆発と疑似弾 | 命中・爆発・疑似弾の FX に適用 |
| Ai_R_PillarOfFlame | delay 後の pillarEffect、ticks × 範囲内の敵数だけ hitEffect を PlayNewNetworked | 持続柱と各命中の描画を制限。tick / range / delay は保持 |
| Ai_R_Cataclysm_Activate / _Meteor / _Meteor_Burn、Se_R_Cataclysm | メテオごとの飛行・衝突 FX、燃焼床の fxLoop / fxPerTick | 各再生と生成 Actor の描画に適用 |
| Ai_E_FlameJet / _Projectile | spawns（コード既定 8）回の弾生成、敵ごとの hitEffect | 弾・命中を制限。spawns / minHitInterval は保持 |
| Ai_D_BurningFist_Attack / _Instance、Se_D_BurningFist_EmpowerAttack | dash 完了後の攻撃 FX、命中ごとの Main/Sub FX、燃焼敵を殴った場合の追加インスタンス | 遅延 RPC 経由も DewEffect のローカル再生で制限。移動・回復・追撃条件は保持 |
| Se_Elm_Fire | ElementalStatusEffect がスタック数に比例して rateOverTime / Distance を増やす | 初回生成に加え UpdateStackScaledEmissions の Postfix で再クランプ（ライト予算は再消費しない） |
| Se_EngulfedInFlame | immolationInterval ごとに範囲内の各敵へ fxImmolationHit を PlayNewNetworked | 持続状態と各命中の描画を制限 |
| Gem_L_Embertail / _Projectile | 攻撃効果ごとの attackEffect と反射弾生成 | 命中 FX と反射弾の描画を制限。反射先選択は保持 |
| Gem_U_EternalFlame / Se_U_EternalFlame_Curse | 被弾反応による炎スタック追加ごとに fxAddFireStack を再生 | 呪い FX と燃焼のスタック増加後の emission を制限。抽選・スタック数は保持 |
| Gem_R_Spiral / Ai_Gem_R_Spiral_Fireball | 複数弾と各弾の飛行・終了 FX | 個々の描画に予算適用 |
| Ai_Q_IncendiaryRounds_Attack / 対応 Se | 追加の飛行 FX、命中・AoE FX | 同じ再生境界で制限 |
| Ai_R_FlamingWhip、Se_L_PyranasFireball、Gem_R_Scorched / _Meteor | 攻撃・持続範囲の敵ごとの命中 FX、隕石の主 FX | 同じ予算を適用 |
| 対応 St_*、Trap_FlameJet、Ai_RoomMod_FireDevil_Tornado | 装備・発射側、罠の各命中、竜巻の持続・tick FX | 具体型を列挙して同じ予算を適用 |
| FireElemental、Infernus、Primus の炎、Maw の火柱、Polaris の炎系 | 連続弾、敵数に応じた命中 FX、波/隕石ごとの追加 FX | 具体型を列挙して同じ予算を適用。攻撃予告コンポーネントを含む root は粒子・trail を維持 |

## 実装境界と上限

- `OnCreate` を具体型から解決し、継承先が同じ場合は一度だけ Postfix する。存在しない型・メソッドはその対象だけスキップする。
- 一瞬で終了する刃、遅延発生する柱、ネットワーク受信後にプールから再生する命中 FX は `DewEffect.Play_Imp` の Postfix で処理する。対象は親 NetworkIdentity の Actor の具体型で限定する。元の再生は必ず実行する。
- ライトは全対象共有で 0.25 秒に 3 灯、同時存在も 3 灯まで。本体の性能圧力中は 1 灯、低品質時は省略。アニメーションドライバの元の値から range 6 / intensity 4 以下に制限する。FxPointLight と Light を両方止めて再点灯を防ぎ、停止したドライバは Stop してプールの終了判定を妨げない。
- 同じ具体型の FX 再生は 0.15 秒内の最初の 6 回を通常予算とする。超過分はライトを省き、粒子と trail に縮小予算を使う。GameObject/Actor の生成・通信を省略するパッチではないので、Instantiate 回数そのものは減らない。
- 通常は ParticleSystem ごとに maxParticles 96、FX root 内で合計 384 を分配（最低各 1）。縮小時は各 24、root 内合計 96 を分配（最低各 1）。元の値より増やさない。burst も maxParticles の上限で制限する。システム数が予算より多い場合の合計値はシステム数になる。
- rateOverTimeMultiplier / rateOverDistanceMultiplier は通常 60 / 12、縮小時 15 / 3 を上限にする。曲線の形と寿命・loop は変えない。ParticleSystem の lights モジュールは無効化する。
- ParticleSystem の trigger・衝突通知・stopAction が有効なら、そのシステムの変更を省略する。Arc/Box/FxCastTelegraph を含む root は粒子・trail の変更を省略する。Transform・Collider・telegraph geometry は変更しない。
- TrailRenderer の残存時間を通常 0.35 秒、縮小時 0.15 秒に制限する（autodestruct が有効なら変更を省略）。FxMeshTrail は trailCount（native 配列のサイズ）を変えず、mesh 更新間隔を最低 0.1 / 0.2 秒にする。
- ConditionalWeakTable でコンポーネントを一度だけ列挙・保持する。初回の状態作成は割り当てを伴うが、同じプール個体のホットパスでは配列を再生成せず、LINQ/毎フレーム走査/Update を追加しない。
- プール FX の再生前に以前変更した状態を復元する。Actor はプール返却時 OnDisable で復元する。無効化済み対象でも復元を続ける。例外は具体型の予算だけを無効にし、ライト用の private API 解決失敗ではライト予算だけを無効にする。パッチ適用失敗は既存のクラス単位の fail-soft 境界を使う。
- 同じプール個体でも、復元後の再生ごとにライトの 0.25 秒窓を消費する。同じ再生への Actor / FX 両方からの適用は二重計上しない。復元は `Play_Imp` の Prefix でも呼ばれるため、点灯中の同時存在枠は復元だけでは解放せず、再生時の固定 3 枠の走査で寿命を確認する。点灯中の再生し直しは同じ枠を使い、時間窓だけを再判定する。

## 寿命管理

本体の Projectile.OnDestroyActor は effectOnFly を止める。AbilityInstance は startEffect を終了後に止め、PillarOfFlame は柱を停止、TickDamageInstance は fxTelegraph / fxLoop を停止する。StatusEffect は startEffectVictim を止める。切り離されたループ FX は EffectAutoDestroy が親 NetworkIdentity の消滅・再利用を検出して停止し、30 秒の timeout も持つ。この範囲では追加修正が必要な所有者消滅漏れは確認していない。新たな毎フレーム掃除は追加しない。

## 検証

- 指定 Release ビルドは成功（0 エラー、既存 6 警告）。
- NativeStartupTests の preflight / 適用失敗の隔離 / rollback の関係 5 件は合格。最初の実行は .NET 8 runtime 不在で Test Run Aborted。その後 `DOTNET_ROLL_FORWARD=LatestMajor` と `--filter` を指定し、既存 .NET 10.0.11 上で 5/5 成功。
- これらの起動テストは本番 NativePatchPreflight と起動処理を API doubles に対して実行するもの。今回の描画を Unity 上で実行した結果ではない。テストは追加・変更していない。
- 未実行: 実ゲームでのマルチプレイ再現、ホスト/クライアント双方の見た目、ライト半径と予告表示、FPS/GC/同時粒子数測定、長時間のプール再利用、完全な NativePatchPreflight の実ゲーム起動。理由: Unity 実行環境とアセットなし。
- 未実行: 全テストスイート。理由: 指示に従い変更関連の filter のみ実行。
