# 本体撃破報酬の5秒アシスト共有（2026-10-16）

対象: `feat/hero-run-growth-1016`。MODの既存5秒アシストと同じ凍結済み判定を、本体の装備効果と通常撃破ゴールドへ適用する。新しい判定窓は作らない。

## 判定・配送

- 敵死亡直前の5秒以内の与ダメージ、敵への制御・弱体、撃破者への有効回復・障壁を使う。撃破者本人を除き、生存している味方だけがアシスト対象。
- `victim.EntityEvent_OnDeath` と一致する `SafeAction<EventInfoKill>.Invoke` のPrefixで既存の `CaptureKillAssists` を呼び、本体の死亡リスナーより先に対象者と装備を凍結する。撃破時回復で新しいアシストを作らない。
- `Actor.InvokeOnKill` の既存Postfixから、凍結した本人の効果だけを呼ぶ。MODの遠征成長・装備効果の共有は既存経路を使う。
- 本体イベント全体の再送はしない。`Hero.ClientHeroEvent_OnKillOrAssist` は本体で30秒の `KillTracker` を起点とし、統計・実績・結果画面・分析も購読するため、イベント再送は対象外への波及が大きい。
- 代わりに下表の効果だけを明示登録する。通常の本体アシスト呼出も受信側で捕捉し、アシスト者の呼出を共有処理まで保留する。受取者・効果・敵の生成世代ごとに1回に制限する。本体の30秒だけに該当する対象者の対象効果は発火させない。
- 撃破者は本来のコールバックを実行する。各アシスト者は自分の装備・倍率・確率・上限・同期処理を使う。召喚源は自身の直近の与ダメージ源を使い、プール再利用を `persistentNetId` で検査する。源を保持できない場合は本人へ戻すため、召喚専用条件は満たさなくなる。
- 死亡記録はActorManagerからの除去では捨てず、プールのイベント解放またはActor無効化まで保持し、遅れて来る本体コールバックの二重発火を防ぐ。プールを使わない破棄でも解放する。
- ホストかつ複数人のみ有効。既存のSyncVar、RPC、拾得物を使う。Protocol・保存形式の追加なし。毎フレーム処理・割り当てを追加しない。死亡・宝珠生成・支給時には記録を確保する。
- 接続失敗・例外でこの共有拡張だけを無効化する。導入途中のパッチ失敗は自身のパッチを巻き戻す。本体の通常報酬と他MOD機能は続行する。

## 調査した本体効果

以下の型名は `Dew.Contents.dll` の逆コンパイルと、`Dew.Core.dll` の呼出元から確認した。効果自身の敵種・確率・装備条件は変更しない。

### KillTrackerを持つエッセンス

| 型 | 本体の購読元 / コールバック | 共有する効果 |
|---|---|---|
| `Gem_E_Predation` | 所有者 `TrackKills(killGraceTime, OnKill)` | 捕食の成長拾得物。ボスは `bossMultiplier` 個（既定3）、ハンターは1個。通常敵は本体仕様で成長しない |
| `Gem_E_Metal` | 所有者 `TrackKills(10f, KillCallback)` | 装着記憶のCD回復、演出 |
| `Gem_L_Culinary` | 装着記憶 `TrackKills(killGraceTime, OnKill)` | 本体確率による料理素材 |
| `Gem_L_DivineFaith` | 装着記憶 `TrackKills(gracePeriod, IncreaseAmp)` | 上限付きスタック、最大HP、ダメージ・回復増幅 |
| `Gem_R_Wealth` | 装着記憶 `TrackKills(gracePeriod, Callback)` | 本体の遅延追加ゴールド |

### ClientHeroEvent_OnKillOrAssistを購読する装備・バフ

| 型 | コールバック / 効果 |
|---|---|
| `Gem_E_OurStory_Unfinished` | `ClientHeroEventOnKillOrAssist`: 撃破蓄積による品質成長・変化 |
| `Gem_U_GuidingCompass_NotCharged` | 同名コールバック: ボス・ミニボスで品質成長用の弾を生成 |
| `Se_E_LizardlyBlessing` | 同名コールバック: バフ中の個人ゴールド |
| `Se_E_UmbralEdge` | 同名コールバック: 条件付きのバフ時間延長 |
| `Se_D_TheKillingFlow` | 同名コールバック: ボス撃破時の回復 |

### ClientHeroEvent_OnKillOrAssistを購読する星図

既定のコールバック名は `ClientHeroEventOnKillOrAssist`。

| 型 | 効果 |
|---|---|
| `Se_Star_L_HealOnKill` | 最大HP比の回復 |
| `Se_Star_I_DreamDustOnKillChance` | 個人の夢の粉抽選 |
| `Se_Star_I_FirstBossGemDrop` | `OnKillOrAssist`: 初回ボスのエッセンス、自身の解除を含む |
| `Se_Star_I_GemOnMiniBossKill` | ミニボスで個人エッセンス抽選 |
| `Se_Star_Bismuth_I_KnowledgeBook` | ボスの個人能力報酬書 |
| `Se_Star_Cetus_F_FR_ChangeToUltHitReset` | 撃破ごとの持続時間増加 |
| `Se_Star_Cetus_I_GetDreamDustOnBossKill` | ボスの個人夢の粉 |
| `Se_Star_Husk_F_AS_NoShockwaveFasterAttack` | ボスごとの恒久的ヘイスト加算 |
| `Se_Star_Husk_I_BossKillPlatinumCoin` | 個人プラチナコイン抽選 |
| `Se_Star_Husk_L_DodgeCooldownOnKillOrAttack` | 回避CD回復 |
| `Se_Star_Husk_L_DodgeCooldownOnKillWithEmptyCharges` | 残チャージなしの場合の回避CD回復 |
| `Se_Star_Lacerta_I_BossBounty` | ボスの個人ゴールド |
| `Se_Star_Nachia_I_ForestKillDreamDust` | 本体の場所条件付き個人夢の粉 |
| `Se_Star_Nachia_I_SummonKillGold` | 本人の召喚源による撃破の個人ゴールド抽選 |
| `Se_Star_Yubar_I_StarCookie` | 敵種別のクッキー抽選 |
| `Star_Global_KillDreamDustBonus` | 旧星図の個人夢の粉 |

### 記憶と特殊CD回復

| 型 | 購読・効果 |
|---|---|
| `SkillTrigger` | `owner.ActorEvent_OnKill` → `ActorEventOnKill`: 究極記憶の撃破チャージ。本体の敵種別の値（小型0.12750001、通常0.63750005、ミニボス12.75、ボス42.5）を元の `ChargeUltimate` へ渡す |
| `Se_Star_Aurena_F_CR_NoHealCooldownReduction` | 連鎖反応の `TrackKills(0.5f, closure)`: 対応記憶の割合CD回復 |
| `Se_Star_Lacerta_F_PS_CooldownReset` | 精密射撃の弾の `TrackKills(killGracePeriod, closure)`: CDリセット |
| `Se_Star_Cetus_F_BA_CdReductionOnKill` | ヒット時刻表 + 敵死亡の `OnVictimDeath`: 対応記憶のCD回復 |

特殊3効果は、既に本体のヒット追跡があればそのクロージャを保持し、本体の弾ごとの状態（精密射撃の一度限りフラグ等）を使う。支援・制御だけなど本体の追跡がないアシストでは、対応する星図の現在値と本体の公開CD APIを使う。

### 既にラストヒットに依存しないもの・対象外

- `Se_E_Harvest_OnVictim`: 対象の死亡確認で自分の術者を回復するため追加呼出不要。
- `Ai_L_MentalCorruption_Projectile`: 敵の死亡イベントからCD返却するため追加呼出不要。
- `Se_Star_Aurena_I_BonusFromStones`、`Se_Star_Lacerta_I_BonusFromStones`: 全体の死亡イベントを購読済み。
- `Se_Star_Nachia_D_CanineLover`: 自分の召喚死亡に関する効果。
- `Se_D_TrueDuelist_Exposed`: 対応する死亡メソッドは空。
- `CurseStatusEffect`: 8秒の追跡は呪い解除クエスト進捗。装備の撃破報酬ではないため変更しない。
- 実績、キル数、統計、結果画面、分析、敵側の撃破処理、MOD独自の夢の種の撃破イベントは再送しない。

## 通常撃破ゴールド

本体は既に全員へ分配している。宝珠の拾得者・ラストヒット担当への独占支給ではない。

1. `PickupManager.HandleDeathDrop` はハンターのドロップ禁止・`disableLoot` を確認し、`GameManager.GetKillGoldAmount(victim)` を一度取得する。
2. 元の総額は、ゾーン倍率 × 敵種の設定額 × 人数。小型・通常は人口・波数補正でも除算し、収入補正・揺らぎ・確率丸めを適用する。具体的な敵種の設定額はUnityアセット依存で、この環境では未取得。
3. 通常は最大3個の宝珠へ分割し、負荷時は即時支給する。
4. `Pickup_BaseGoldOrb.GrantGold` の `target == null` は総額を人数で割り、各人に `DewBuildProfile.current.killGoldMultiplier` と `monsterKillGoldMultiplier` を掛け、それぞれ確率丸めして `EarnGold` する。

変更後も本体の総額算出・宝珠分割・乱数処理を使う。既存の増援敵の報酬縮小もそのまま通す。その支給の撃破者の `EarnGold` 引数を記録し、5秒アシスト者の引数だけを同額へ置換する。ロビー順で撃破者より先に処理されたアシスト者は、元の額を保持して支給を保留し、同じ支給の終了時に撃破者と同額を一度だけ渡す。無効化・撃破者の退室等で撃破者額を得られなければ本体の額を使う。

したがって個人倍率によってアシスト者の従来額が撃破者を上回っていた場合も、指定どおり撃破者と同額になる。アシストでない人は本体額のまま。基礎分を二重に追加しない。エッセンス等の追加ゴールドは本人の装備効果であり、通常撃破ゴールドの同額化とは別に扱う。

死亡時の対象者をコルーチンのiterator・宝珠へ弱参照テーブルで引き継ぐ。宝珠再利用時は古い記録を除去する。個人指定、譲渡、非撃破ゴールド、対応する敵を持たない宝珠は同額化しない。`EarnGold` 本体実行中は支給コンテキストを外し、再入した別の報酬を混同しない。

## 検証

この作業で実行済み:

- `python3 tools/balance/gen_cs.py --check`: 成功。
- ネイティブDLL参照のReleaseビルド: エラー0、既存警告6。
- 既存Core関連フィルター: 87成功、失敗0、スキップ0。
- 既存起動・再読み込みフィルター: 20成功、失敗0、既存スキップ1（ReleaseのJITインライン化に関するテスト）。
- ゲームDLLのIL確認: `PickupManager.DropGoldRoutine` の `IteratorStateMachineAttribute` と `MoveNext` の存在を確認。
- `git diff --check`: 成功。

既存テストはリンクされた5秒判定・既存の成長/通貨・起動部分の検証であり、新しい本体効果・ゴールドフック自身のUnity実行検証ではない。新規テスト・CHANGELOG変更なし。

未実行:

- 報告された2人マルチの捕食成長・同額ゴールドのゲーム内再現。
- 実ゲームのHarmony導入、Mirrorの遅延配送、宝珠回収、シングルでの動作確認。
- 理由: この環境にはManaged DLL・逆コンパイル資料のみで、起動可能なゲーム/Unityランタイムがない。
- 全テスト・低速スイート: 指示に従い実行しない。
