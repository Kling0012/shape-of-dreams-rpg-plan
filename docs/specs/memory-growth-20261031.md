# メモリ増加の調査と対応（2026-10-31）

利用者の依頼（2026-10-31）：Dreamforge RPG 入り（協力参加、全14 MOD）の環境で、長く遊ぶほどメモリが増え、部屋の移動で重くなる問題のうち Dreamforge が原因の分を見つけて直す。

分かっている事実（利用者観測）:

- 約2時間でメモリ 4.5GB → 7.6GB。減らない。
- Unity の Loaded Objects が 2.8万（開始時）→ 最大38万。
- 部屋を移るたびの `Resources.UnloadUnusedAssets` が 430〜530ms（内 MarkObjects 370〜430ms）。MarkObjects はマネージドヒープの大きさと、マネージド側から Unity オブジェクトへの参照数に比例する。
- タイトルへ戻るときのシーンのアンロードに 11.5 秒。

前提の確認（本体逆コンパイルコード `sod-decomp` で確認）:

- `Actor.OnDestroyActor` は `OnIsActiveChanged(false)` と `OnStartClient` の非アクティブ化経路でしか呼ばれない。シーン解体による Destroy では **呼ばれない**。したがって「破棄時に外す」形の管理は、遠征の終了やタイトル帰還で必ず残る。
- `Actor.OnDisable` は DewPool のイベント・処理者を毎回空にする。本体側のイベント経由の参照は OnDisable で切れるため、問題は Mod 側の静的な管理（辞書・リスト・購読）に絞れる。

## 1. 調査の方法

`src/SodRpg.Mod` の静的なフィールド（辞書・リスト・購読）を約200ファイルほぼ全部読んで、Unity オブジェクト（Actor / Entity / GameObject / Texture / Sprite / Material など）を保持し続ける可能性があるものを列挙し、追加・削除の経路を本体コードと突き合わせて確認した。

## 2. 原因と直したもの

破棄された Unity オブジェクトを静的な管理が参照し続けていた箇所は3つ。いずれも「破棄時に外す」経路が本体の破棄通知に依存していて、シーン解体（遠征の終了・タイトル帰還）で外れないか、ティックの停止で自己掃除が走らなくなる形だった。

### 2.1 夢の種の記憶の辞書（確からしさ: 高）

`NativeDreamContent.Effects.cs` の `Seedlings`（`Dictionary<SkillTrigger, SeedlingState>`）。値が破棄済みの `SkillTrigger` と `Hero`（装備中の持ち主）への参照を持つ。削除は `Actor.OnDestroyActor` の Postfix だけだったため、シーン解体で破棄された分が遠征ごとに積もる。

**対応**: 破棄済み（Unity null）のキーを外す `PruneDestroyedSeeds` を追加し、既存の部屋遷移後の掃除（`ClientSession.TickRoomVisualSweep`、毎フレームではなく部屋の区切りで1回）から呼ぶ。外すときの処理は OnDestroyActor と同じ（購読を外してから削除）。

### 2.2 ボスバーの束縛リスト（確からしさ: 高）

`DreamforgeUi.BossShares.cs` の `BossShareBarBinding.Bars`。`UI_InGame_BossBars_Item`（LogicBehaviour、シーンと共に破棄）と `Entity`、RectTransform / Canvas / CanvasGroup への参照を持つ。整理は Setup 時と描画ループ内だけで、設定 `showEnemyOwnershipMarks` が無効の間は描画ループが即 return するため、シーンをまたいで破棄済みバーが残る。

**対応**: 同じ判定（Item か Enemy が破棄済み）で外す `PruneDestroyed` を追加し、同じ部屋の区切りの掃除から呼ぶ。

### 2.3 食材の追跡リスト（確からしさ: 高）

`CulinaryMagnet.cs` の `Tracked`（ホスト側のみ、`List<Actor>`）。自己掃除は `SnapToOwners` の中にあるが、これは `HostAuthority` のティックから呼ばれるため、サーバー停止でティックが止まると（`HostAuthority.Tick` は `!NetworkServer.active` で早期 return する）破棄された食材アクターが次にホストになるまで残る。

**対応**: `HostAuthority.Detach()`（サーバー停止時に1回呼ばれる）から `Tracked` を空にする。

いずれも毎フレームの処理は増やさず、部屋の区切り・ホスト権限の終了という既存の節目だけを使う。対象ごとに try/catch で独立に fail-soft（1つ失敗しても他の掃除と本体・他機能は止まらない）。ゲームプレイ・見た目・音・タイミング・保存内容には触れない。

## 3. 確認したが問題なかったもの（抜粋）

`AutocastPressure`（切断時に外す）、`ClientSession.Wire/Unwire`（softInstance の切り替えで張り直し、RPC ハンドラも解除）、`_variantVisuals` などティック中の自己掃除、`HostAuthority` の各種プール（try/finally で返却）、`ModShieldPools`、`MemoryActivationAttribution`、`BossSharesNative`（Finalizer で null）、`InfinityMapPresentation`・`LevelEssenceRuntime`・`NativeMemoryReuse`（ConditionalWeakTable）、`FrostFxLifetime` / `PickupFxLifetime`（購読の対）、`RelicIcons`（IDの種類分だけの上限、Dispose で破棄）、`CoopOmens` / `EnemyOwnershipNative`（固定長のプール）、BossGlacialCore の作業用 static（finally で null）。

## 4. 見送ったもの

- `DreamforgeUi._miniBossShares / _miniBossShareValues`（netId → GUIContent）。マネージド側だけで Unity オブジェクトを保持しない。増え方も遠征ごとの敵の種類分にboundedで、今回の MarkObjects の重さにはほぼ効かないと判断した（判断、測定は未実行）。
- 割り当ての削減（毎フレームの new の抑制）。CPU 軽量化の作業が並行で動いているため、今回の範囲から外した。
- 他の13 MOD と本体側の原因。本環境の観測（2時間で +3.1GB、Loaded Objects +35万）は上記3つの規模（各遠征で数個〜数十個のオブジェクト参照）では説明できず、大部分は今回の範囲の外にあると考える（推定。実機での前後比較は未実行）。

## 5. 効果の見積り（推定）

- 直した3つは、遠征の終了・タイトル帰還・サーバー停止のたびに破棄済みオブジェクトと Hero・RectTransform などの参照が積もっていた分を止める。**見積りとして**、Mod 側のこの分は Loaded Objects 数十〜数百個/遠征程度と見ており、観測されている +35万オブジェクトの大部分は占めない（あくまで推定）。
- MarkObjects の時間は参照数に比例するため、積もり分の解消で長時間游玩時の悪化をいくらか抑える見込み（推定。実機での計測は未実行）。

## 6. 検証

- `dotnet build src/SodRpg.Mod -c Release`（GameDir / ModDeployDir 指定）: 成功（警告6件は既存）。
- `tests/Issue73.Native.Tests`: 947 成功 / 3 スキップ（既存のスキップ）/ 0 失敗。
- `tests/SodRpg.Mod.Startup.Tests`: 238 成功 / 1 スキップ（既存）/ 0 失敗。
- ティック段階の追加はしていない（`ClientTickIsolationTests` が段階の並びを固定しているため、既存段階の中に収めた）。
- 実機（ゲーム内）でのメモリ計測・長時間游玩の前後比較: 未実行。

## 7. コミット

- `931df7b` メモリ: 破棄済みスキルとボスバーの管理を部屋の区切りで掃除する
- `0ee9eff` メモリ: ホスト権限終了時に食材の追跡リストを空にする
