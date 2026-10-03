# Issue #19 第2段調査：方式A（実行時GLB読み込み）の設計材料

2026-10-03。読み取りのみ（ゲーム本体・Workshop MOD・セーブは一切変更していない）。第1段（docs/specs/issue19-model-import-research.md:41）で利用者が方式Aを選択したことを受けての、実装前の最終調査。根拠はすべて `sod-decomp/Dew.Core`（%LOCALAPPDATA%）、`.ref/dump/*.txt`、本repo `src/SodRpg.Mod`、ワークショップMOD `…/2444750/3807706235`（Shape of Dreams VRM 0.4.0）、およびゲームのビルド成果物（読み取り）から行ごとに示す。確認できなかった点は 要確認 と記す。

前提（第1段で確認済み）: Unity 6000.0.77f1 / URP / Mirror / Addressables（docs/specs/issue19-model-import-research.md:10-13）。EntityModel の各フィールドと `LoadModelLocal` の制約（初期化済みインスタンス不可）も同文書（docs/specs/issue19-model-import-research.md:20-22）。

## 1. EntityAnimation：本体がモンスターのアニメを再生する仕組みと、差し替えモデルの追従方法

**再生方式は Animator（+AnimatorOverrideController）。Playables は不使用。**

- `EntityAnimation.animator` は public フィールドで、`InitAnimator()` が `model.GetComponentInChildren<Animator>()` を取得し、`AnimatorOverrideController` を被せて差し替え可能にする（EntityAnimation.cs:90, EntityAnimation.cs:529-541）。
- Idle／8方向ラン／Stagger／Death は、**クリップ名が enum 名と一致するステートのクリップをオーバーライド**する方式（`AssignBaseClipReferences` がクリップ名 `Idle` `RunForward` … `Stagger` `Death` で照合、EntityAnimation.cs:360-375。適用は `ReplaceAnimationLocal`、EntityAnimation.cs:390-409）。`EntityModel.locomotion` が Simple/FourDirections/EightDirections のどれかで使うクリップが決まる（EntityAnimation.cs:212-275、EntityModel.cs:31）。
- 攻撃（能力）アニメはレイヤー1で再生: サーバーの `PlayAbilityAnimation` → ClientRpc → `ReplaceAnimationLocal(Ability, entry.rawClip)` とレイヤー1の重みを0.1秒でブレンド（EntityAnimation.cs:433-467, EntityAnimation.cs:663-669, EntityAnimation.cs:776-806）。進行度は public な `abilityAnimStatus`（isPlaying / normalizedTime / currentClip 等、EntityAnimation.cs:15-36, EntityAnimation.cs:108）から毎フレーム読める。
- **毎フレーム書き込まれる Animator パラメータ**（これが差し替え側が読むもの）: `isWalking` / `isDead`（bool、EntityAnimation.cs:628-640）、`walkSpeedMultiplier`（float、`walkStrength × model.walkAnimationSpeed × movementSpeedMultiplier ÷ etScaleMultiplier`、EntityAnimation.cs:292-298）、`walkDirX` / `walkDirY`（LateUpdate で `Control.agentVelocity` から、EntityAnimation.cs:686-695）、`abilityAnimationSpeed` / `abilityAnimationNormalizedTime`（EntityAnimation.cs:657-661）、`locomotionType`（int、EntityAnimation.cs:275）。パラメータ名の全体リストは静的コンストラクタで Hash 化（EntityAnimation.cs:698-711）。
- スタガーは `Entity.Stagger`（サーバー）→ `RpcPlayStaggerAnimation` → クライアントで `animator.SetTrigger("Stagger")`（Entity.cs:609-621, EntityAnimation.cs:347-358, EntityAnimation.cs:723-729）。**Trigger は読み取り不可**（Unity API に getter が無い）ため後述の代替で拾う。
- 死亡は `isDead` bool で制御: モンスターでは `!entity.isActive` で true（EntityAnimation.cs:629）。`isDeathAnimationForced` は SyncVar の `CounterBool` でクライアントから読める（EntityAnimation.cs:128-144）。見た目の溶解は EntityVisual 側（4章）。
- 元ソース値も直接読める: `entity.Control.isWalking`（EntityControl.cs:635）、`Control.walkStrength`（EntityControl.cs:619）、`Control.agentVelocity`（EntityControl.cs:637）、`entity.Animation` / `entity.Visual` / `entity.Control` の各ショートカット（Entity.cs:215-225）、`Actor.isActive`（Actor.cs:330）。
- 人型（`animator.isHuman`）の場合のみ Spline 用の `_abilitySampler` 複製と Spine 回転補正が走る（EntityAnimation.cs:542-597, EntityAnimation.cs:672-685）。モンスターは多くが generic rig と想定されるが、**個別の Monster prefab の Avatar 種別は 要確認**。

**差し替え視覚の追従設計（推奨）**: GLB側に独自の AnimatorController（状態名を本体と同じ `Idle/RunForward/…/Stagger/Death/Ability`、パラメータ名も本体と同一で作る）を持たせ、MODコンポーネントが毎フレーム `EntityAnimation.animator` から `GetFloat/GetBool/GetInteger` で読んで GLB 側へ転送する。Trigger 系（Stagger / StartAbilityAnimation / StopAbilityAnimation）は読めないため、(a) `GetCurrentAnimatorStateInfo(0).shortNameHash` の変化を監視して状態遷移を複製する、(b) 能力は `abilityAnimStatus.isPlaying` とレイヤー1重み（EntityAnimation.cs:663-669）で判定する、の併用。元の Animator は renderer を隠しても動き続ける（隠すのは renderer.enabled のみ、2章）。注意: 本体 Animator の cullingMode は prefab 依存で未確認（**要確認**）。安全策として差し替え有効中は `animator.cullingMode = AlwaysAnimate` を設定し、解除時に戻す（本体は sampler にのみ AlwaysAnimate を設定している、EntityAnimation.cs:548）。

## 2. 差し替えのフック地点：既存の変種処理・生成・プール・破棄・renderer の隠蔽と復元

**既存の変種ビジュアル経路（本repo）**: ホストは `MakeVariant` で `DreamforgeVariantMsg { netId, variantId }` を全員へ送り（HostAuthority.cs:799-828）、5秒間隔で再送する（HostAuthority.cs:279-284, HostAuthority.cs:349-378）。クライアントは `OnVariant` で受信し（ClientSession.cs:675-693）、`ApplyVariantVisual` で `visual.GetNewColorModifier()` / `GetNewTransformModifier()` により色と大きさを付ける（ClientSession.cs:719-746）。モデル未生成で届いた通知は10秒間保持して `UpdateVariantVisuals` のポーリングで後から適用する（ClientSession.cs:695-717）。**このポーリング方式はプール再利用と生成順の両方に強く、モデル差し替えも同じ地点（`visual.model != null` を確認した後）に載せるのが定石**。メッセージの登録は型名で行う（ClientSession.cs:309-316）。

**生成・破棄・プール（本体）**:

- クライアントでは Mirror の spawn handler `SpawnFromDewDatabaseHandler` が prefab を引き、`SpawnManager.Create` → `CreateInternal` で生成（SpawnManager.cs:68-72, SpawnManager.cs:74-115, SpawnManager.cs:238-309）。
- **プールが実在する**: `prewarmEnabled = true`（既定、SpawnManager.cs:13）で部屋開始時に monster prefab ごとにインスタンスを作って `DontDestroyOnLoad` + 非アクティブ化して park する（RoomMonsters による prewarm、RoomMonsters.cs:451-457、SpawnManager.cs:400-485）。`usePooling=false` でも **prewarm 済み prefab はプール経路になる**（`CreateInternal` は `pool.ContainsKey` なら parked を再利用、SpawnManager.cs:258, SpawnManager.cs:281-300）。つまり同じ GameObject が新しい netId で再利用され得る。
- despawn は `CustomDespawnHandler` → `SpawnManager.Destroy` → park（SetActive(false）。コンポーネントは消えない）（SpawnManager.cs:726-742, SpawnManager.cs:487-527）。
- 生成・破棄のイベントは `ActorManager.ClientEvent_OnEntityAdd / OnEntityRemove`（ActorManager.cs:19-21, ActorManager.cs:192-215。ホスト側で本repoも使用、HostAuthority.cs:400-415）。
- **`ClientEvent_OnModelLoaded` の注意**: 発火は `LoadModelLocal` の末尾のみ（EntityVisual.cs:856。`.ref/dump/EntityVisual.txt:4`）。初回スポーンは `OnStartClient → PreloadVisuals → LoadEntityModelLocal`（EntityVisual.cs:1023-1057, Entity.cs:916-919）で通るが、**プール再利用時は model が既に載っているため発火しない**（EntityVisual.cs:1046-1047）。さらにモンスターは `usePooling=false ＆ reuseInRoom=false` なので `DewPool.ClearEventsAndProcessors` の購読清算が走らず（DewPool.cs:50-76。呼び出し元 Actor.cs:641-654, EntityComponent.cs:6-9）、**同インスタンスへ subscribe を繰り返すと溜まり続ける**。したがって OnModelLoaded は「モデル差し替え（スキン変更等）での再アタッチ検出」にのみ使い、購読管理（idempotent な unsubscribe）を自前で行う。第一のフックは 1章 のポーリングとする。

**元 renderer の隠蔽と復元**:

- `EntityVisual.renderers` / `solidRenderers` は public の `List<Renderer>`（EntityVisual.cs:97-103、`.ref/dump/EntityVisual.txt:26-27`）。
- 正規の隠蔽 API は `DisableRenderersLocal()` / `EnableRenderersLocal()`（ローカルカウンタ）とネットワーク同期版（EntityVisual.cs:1725-1760）。中身は `renderers` の `renderer.enabled=false/true` と `ClientEvent_OnRendererEnabledChanged`（EntityVisual.cs:501-533）で、付属エフェクトの一時停止・アクセサリ整理も連動する。カウンタは**コンポーネント实例に残るため、プール再利用でも消えない**——detach 時に必ず同数だけ `EnableRenderersLocal` を呼ばないと、次にその pooled GameObject を使ったモンスターが透明になる。
- VRM MOD の実績: bodyRenderers 個別ではなく「元モデルと EntityVisual の全 renderer を隠す」方式に改めた経緯（衣装・武器・アクセサリの残留防止、CHANGELOG.md:36-38 の 0.2.0 項）、および `EntityVisual.isRendererOff` を読み続けて隠密・復元に追従（CHANGELOG.md:39-40 の 0.2.0 項、README.md:24）。MOD 無効化時は renderer・材質・Harmony を戻す（README.md:26）。
- 復元のタイミング（すべて実装必須）: (1) netId が `NetworkClient.spawned` から消えた（ClientSession.cs:703-716 の除去経路）, (2) `ClientEvent_OnModelLoaded` でモデルが載せ替わった（GLBを `visual.modelTransform`（= animator の transform、EntityVisual.cs:2317-2332）配下に置いている場合、旧モデル GameObject の DestroyImmediate（EntityVisual.cs:727-731）でGLBも壊れるため再アタッチ）, (3) MOD の reload/無効化（DreamforgeMod.cs:251-268 の OnDestroy で現在は RelicIcons 等を破棄——ここに差し替え解除を追加）, (4) セッション（サーバーActor）切替（ClientSession.cs:274-299 で ClearVariants 済みの経路に合わせる）。

## 3. glTF ライブラリの選定

**VRM MOD が同梱しているもの（実測）**: ディスク上は `bin/Release/netstandard2.1/ShapeOfDreamsVRM.dll` 1ファイルのみ（854,528 byte）。内部に `VRM.dll / UniHumanoid.dll / UniGLTF.dll / UniGLTF.Utils.dll / UniGLTF.UniUnlit.dll / SpringBoneJobs.dll / MToon.dll / ShapeOfDreamsVRM.Runtime.dll` をリソースとして埋め込む（バイナリ中の `embedded.*.dll` リソース名で確認）。バージョン文字列 `UniVRM-0.129.34` を同 DLL 内で確認。由来は Celeste-twinkle/valheim-vrm 由来の VRM 0.x ランタイム（THIRD_PARTY_NOTICES.md:3-6）。ライセンスは licenses/UniVRM-MIT.txt（VRM Consortium / Masataka SUMI、MIT）と licenses/ValheimVRM-MIT.txt（Yoshihiro Ito、MIT）。

- **プレーンGLB（非VRM）を読めるか**: UniGLTF は glTF 2.0 コアであり、VRM MOD 自身が「既存 UniGLTF で glTF を読み、VRM1.0 の Humanoid を後から構築」という使い方をしている（CHANGELOG.md:11-13 の 0.3.0 項）。スキン（SkinnedMeshRenderer）と AnimationClip は glTF コアの機能で UniGLTF が対応する。ただし**VRM MOD の埋め込みコピーは他MODから再利用できない**（private リソース）。
- **Unity 6000.0 + URP での実績**: VRM 0.4.0 が r.1.4.0.13_s（= Unity 6000.0.77f1 ビルド）で協力含め動作（README.md:3, README.md:39-43）。ただし同 MOD はこのランタイムを「互換性試験用」と位置づけ、長期は Unity 6000.0.77f1 ビルドの player-only UniVRM に替える予定と明記（README.md:76）。URP材質は同MODが自前で URP Lit/Unlit を構築（MToon shader 変換は不要な経路、README.md:71-72）。
- **再配布**: UniVRM/UniGLTF/MToon は MIT（licenses/UniVRM-MIT.txt）なので、NOTICES 同梱で Workshop MOD 内の再配布は可能。VRM MOD の作法（THIRD_PARTY_NOTICES.md に明記 + licenses/ に全文）がそのまま鋳型になる。
- **DLL 名衝突の実態**: ゲームは MOD の `metadata.json の assemblies に列挙された DLL` だけを読み、**読み込み時に Mono.Cecil でアセンブリ名に `_` + DateTime.Now.Ticks を付与してから Assembly.Load する**（DewMod.cs:497-513、列挙の収集は DewMod.cs:704-709。VRM MOD の metadata は自DLLのみ、about/metadata.json）。したがって (a) ゲーム経由ではMOD間の主DLL名衝突は起きないが、(b) 列挙されていない依存 DLL を mod フォルダに置いても**ゲームは一切読まない**（読ませれば tick 改名によりMOD主DLLからの参照解決が壊れる）。VRM MOD はだから依存を埋め込み、bootstrap のリゾルバ（allowlist + MVID 照合）で自行ロードし、同名異版 DLL をブロック・固定 MVID で再 enable 時の再ロード問題を回避している（README.md:27、CHANGELOG.md:10 の 0.1.0 項「帶有 allowlist、MVID 衝突檢查」、CHANGELOG.md:9 の 0.4.0 項「固定內嵌 Runtime 的 MVID」）。**両MOD導入時の懸念**: Mono AppDomain はアセンブリ名で一意化されるため、当MODも素の `UniGLTF` 名で Assembly.Load すると、先に VRM MOD が読んだ `UniGLTF`（0.129.34 由来）が返り、版違いが混ざる。回避策は (i) ライブラリ側の名が重ならない glTFast を使う、(ii) UniGLTF を使うなら自ビルド時にアセンブリ名・名前空間を一意リネーム（ILRepack/Cecil）して埋め込む、のどちらか。MVID 衝突チェックの借り物は相手実装に依存するため設計前提にはしない。

**比較（2案以上）**:

| | UniGLTF（UniVRM 構成、自前埋め込み） | glTFast |
| --- | --- | --- |
| 実績 | 同ゲーム・同バージョンで VRM MOD が協力まで動作（README.md:3）。VRM も視野 | Unity 公式 fork（com.unity.cloud.gltfast、docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.13）。Unity 6 対応ドキュメントあり |
| ライセンス | MIT（licenses/UniVRM-MIT.txt） | MIT（github.com/atteneder/glTFast）。Unity fork の配布条項の確認は採用時 要確認 |
| GLB + スキン + AnimationClip | glTF コア対応 | ランタイム読み込み・スキニング・アニメに対応（ImportRuntime ドキュメント） |
| URP | 自前で URP Lit/Unlit 材質を作る必要（VRM MOD が組んだ経路の自作） | URP 材質生成が内蔵（Lit/Unlit） |
| DLL 名 | `UniGLTF` 等が VRM MOD と衝突し得る → リネーム必須 | `GlTFast` 系で衝突なし |
| 依存規模 | UniGLTF 単体でも sqlite3 等の依存あり（VRM 不要なら除去可。構成は 要確認） | Jobs/Collections/Burst 依存（ゲーム同梱の Unity.Collections 等で賄えるかは 要確認） |

推奨: まず glTFast（名前衝突なし・URP材質内蔵）を第一候補とし、Burst/Jobs 依存が mod 環境で解決できない場合に UniGLTF（リネーム埋め込み）へ切る。決定は実装第1歩のスパイク（7章 S2）で行う。

## 4. マテリアル：GLB→URP 変換と、溶解・被弾・エリート演出との整合

**ビルドに存在するシェーダー名（証拠）**: `Shape of Dreams_Data/globalgamemanagers` の文字列から、常駐シェーダーとして `Universal Render Pipeline/Lit`、`Universal Render Pipeline/Simple Lit`、`Universal Render Pipeline/Unlit`、`Universal Render Pipeline/Particles/{Lit,Simple Lit,Unlit}`、`Universal Render Pipeline/Terrain/Lit`、post-process 系一式を確認。管理DLLにも `Unity.RenderPipelines.Universal.Shaders.dll` 等 URP 一式（`Shape of Dreams_Data/Managed` の一覧）。**キャラ用カスタムシェーダーの表示名は抽出できなかった（要確認）**が、そのプロパティ表は確認済み: 本体は `Shader.PropertyToID` で `_DissolveStrength` `_CMBaseColor` `_CMEmission` `_CMOpacity` `_CMDissolveColor` `_FireStrength` `_ColdStrength` `_VoidStrength` `_LightStrength` を使う（EntityVisual.cs:2451-2462）。メイン AssetBundle（aa/StandaloneWindows64/defaultlocalgroup_assets_all_*.bundle、約1.9GB）内に `_DissolveStrength` を含むシェーダーブロックが6箇所あり、近傍に `_WorkflowMode/_BaseMap/_BaseColor/_Smoothness` 等の URP Lit 系プロパティが並ぶ。

**変換方針**: GLB の各 primitve/material を `Shader.Find("Universal Render Pipeline/Lit")`（不透明）を既定に `new Material(shader)` で生成し、`_BaseMap`/`_BaseColor`/`_Smoothness`/`_BumpMap`（normalMap がある場合）を流し込む。VRM MOD の実績: 既定 URPUnlit・ファイル名による URPLit 指定（README.md:62-63）、Unlit 時は metallic/occlusion 統合テクスチャの欠落を安全にスキップ（README.md:22, CHANGELOG.md:14-15 の 0.3.0 項）。

**溶解（死亡）との相互作用**: 本体の溶解は `EntityVisual.RpcHandleDeath` → `deathBehavior==Dissolve` なら `RoutineAnimateDissolve(model.dissolveDelay, model.dissolveDuration)` で `EntityColorModifier.dissolveAmount` を0→1し、集計結果を `solidRenderers` 全部へ MaterialPropertyBlock で `_DissolveStrength` 等として貼る（EntityVisual.cs:2498-2516, EntityVisual.cs:1097-1110, EntityVisual.cs:1508-1545, EntityVisual.cs:1628-1656）。**URP Lit はこれらのプロパティを持たないため、GLB側へ溶解は反映されない**。対応: (1) MOD側で同じ delay/duration（`model.dissolveDelay/dissolveDuration`、EntityModel.cs:19-21）の coroutine を回し、GLB の renderer に `_BaseColor` のアルファ（Transparent 化）またはスケール縮小でフェードする。または (2) GLB の renderer を `visual.solidRenderers` に追加すれば MPB は当たるが URP Lit では視覚効果が無い（`renderers` への追加だけでも `isRendererOff`（隠密）連動は得られる、EntityVisual.cs:501-533）。deathBehavior が `HideModel` の敵は `DisableRenderersLocal` で消えるため、GLB 側も `ClientEvent_OnRendererEnabledChanged` / `isRendererOff` のポーリングで追従させる（EntityVisual.cs:2498-2508、VRM MOD も同じ追従、README.md:24）。

**被弾フラッシュ**: 本体は `RpcShowHitEffect` → `highlight.ShowHit()`（EntityVisual.cs:1240-1244, EntityVisual.cs:2578-2581）。highlight は HighlightPlus の `HighlightEffect`（EntityHighlightProvider.cs:6-13, EntityHighlightProvider.cs:80-83）。**動的に追加した renderer を HighlightPlus が拾うかは 要確認**（拾わなければ被弾フラッシュはGLBに乗らない。MOD側で `RpcShowHitEffect` 相当を自前実装するのは不可——ClientRpc は他MODから受信できないため、`abilityAnimStatus` 的な public 値も無い。代替: エミッション一時ブーストを自前トリガにする場合、ダメージ演出は `model.fxTakeDamage` のパーティクル（EntityVisual.cs:1245-1257）が位置ベースで乗ることのみ期待できる）。

**エリート（悪夢・変種）色と予告**: 変種の色は `EntityColorModifier.baseColor` → 集計 → `_CMBaseColor` の MPB（EntityVisual.cs:1521-1545。本repoでは ClientSession.cs:735-738 で設定、予告は `_CMEmission`、HostAuthority.Monsters.cs:238）。これも URP Lit には届かないため、**GLB側は MOD が同じ VariantDef の RGB（src/SodRpg.Core/Game/Variants.cs:68）を `_BaseColor` 乗算として自前適用**し、予告色は `_EmissionColor` で再現する。元素状態（火/氷/光/虚）の `_FireStrength` 系も同様に MPB のみで、GLB側へは乗らない（対応するなら自前でエミッション/ティントを書く。優先度は低く初回スコープ外）。

## 5. 多人数：メッセージ設計・途中参加・欠落時挙動・ホスト権限

- **メッセージ**: 既存 `DreamforgeVariantMsg { uint netId; string variantId; }`（NetMessages.cs:73-77）を拡張し、`string modelId`（例: `dream_eater`）と `string sha256`（ファイル指紋）を追加する。ハンドラ登録は型名照合で、フィールド追加は既存経路のままで良い（登録 ClientSession.cs:313、送信 HostAuthority.cs:828 / 再送 HostAuthority.cs:374。文字列フィールド追加の前例として variantId と DreamforgeMonsterCueMsg がある、NetMessages.cs:81-85）。**旧クライアントとの互換**: 未知の CustomRpc 通知を旧クライアントが安全に無視する保証はないため Protocol 版を上げて古い版を締め出す運用は v1.24 当時の判断と同じ（src/SodRpg.Mod/README.md:99）。mirror serializer の手動登録が必要だったのは VRM MOD（Weaver 未通過の独自ランタイムから、CHANGELOG.md:6 の 0.4.0 項）。本MODは今日既に CustomRpc メッセージを送受信しているため追加不要。
- **同期するのは識別のみ**: VRM MOD と同じく「ファイル名（modelId）と SHA-256」だけをホスト経由で配り、.glb ファイル自体は game network で送らない（README.md:13-14, README.md:73）。各PCは自分の MOD フォルダ（例: `models/<modelId>.glb`。アイコンの `icons/` 読み込みと同じ仕組み、RelicIcons.cs:19-22, RelicIcons.cs:52-78）から読む。SHA-256 が一致しない/ファイルが無い場合は**そのクライアントだけ元の見た目のままにし、ログで理由（欠 file / 指紋不一致 / 読み込み失敗）を出す**（VRM MOD の挙動、README.md:42, README.md:74）。
- **途中参加**: ホストは悪夢・変種を5秒間隔で再送するので（HostAuthority.cs:279-284, HostAuthority.cs:349-378）、遅joinも最長5秒＋クライアント側10秒のスポーン待ち（ClientSession.cs:701-712）で追いつく。既存の `ResyncNightmares` に modelId/sha256 を乗せるだけで良い。
- **ホスト権限は不変**: 敵の生成・能力・ダメージは一切触らず、visual は各クライアントのローカル適用のまま（既存 ApplyVariantVisual と同じ構造、ClientSession.cs:719-746）。ホストは variant 割当と modelId の紐付け（VariantDef 拡張）だけ決める。

## 6. ライフサイクル：読み込み时机・キャッシュ・インスタンス・後始末

- **読み込み时机**: 最初にその modelId が必要になった瞬間（初回 DreamforgeVariantMsg 受信 or ポーリングで `visual.model` が揃った時）に1回だけ非同期読み込み。VRM MOD はユーザー選択後の主スレッド読み込みで「大型モデルで短い停止が出る」ことを明示しており（README.md:75）、切替のカクつき回避のため部屋開始時の先読み（ゾーン内で使う変種のモデル preorder）を optional とする。失敗時は元の見た目を維持し negative cache（二度探さない。RelicIcons の null キャッシュと同じ、RelicIcons.cs:10-12）。
- **キャッシュ（モデル単位の共有）**: (modelId, sha256) ごとに「読み込み結果（メッシュ・テクスチャ・AnimationClip・マテリアル）」を1式だけ保持し、Monster ごとにはシーンインスタンス（Instantiate）だけ作る。VRM MOD の実績: 同じ VRM は1回だけ import し、参照カウントで管理、プレイヤーごとに独立インスタンス（README.md:15, CHANGELOG.md:4-5 の 0.4.0 項）。破棄は参照が0になったら `Object.Destroy`（RelicIcons.Dispose の作法、RelicIcons.cs:42-48）。
- **インスタンス（モンスター単位）**: GLB インスタンス + パラメータ転送コンポーネントを `visual.modelTransform` 配下に作る（EntityVisual.cs:2317-2332）。netId → state の辞書は既存 `_variantVisuals` と同じ管理（ClientSession.cs:70, ClientSession.cs:765-781）。
- **プール再利用**: park（SetActive(false)）でも GLB 子は破棄されず残る（SpawnManager.cs:518-526）。再利用で netId が変わるため、(a) netId 消失で detach した時点で GLB インスタンスを Destroy（プールに空き子を残さない）、かつ (b) 再 attach 時に「この GameObject に前の残骸が無いか」をタグ付き子の掃除で保証する、の2段構え。renderer の復元は 2章 の4時点で必ず実行（カウンタ残存は次の pooled spawn が透明化する）。
- **MOD reload / 無効化**: `DreamforgeMod.OnDestroy` で現在 RelicIcons・PerformanceTuner・Host・Session・Harmony を順に破棄している（DreamforgeMod.cs:251-268）。ここに「全差し替えインスタンス破棄・renderer 復元・モデルキャッシュ破棄」を1段追加する。セッション切替（サーバーActor 変更）時は ClientSession.Wire 内の `ClearVariants()` 経路に乗せる（ClientSession.cs:296-299, ClientSession.cs:774-781）。ゾーン遷移で entity が消えた場合は netId 消失経路が自然に処理する。
- **アセンブリの再読み込み**: MOD主DLLはゲームが tick 改名して読むため reload 毎に新しい型になるが（DewMod.cs:504-513）、MODが自行ロードした GLB ライブラリ（Assembly.Load）は Mono で unload 不能。VRM MOD は同一 build の再 enable では loaded assembly を再利用し、DLL を差し替えた時は完全再起動を求めている（README.md:66, CHANGELOG.md:9 の 0.4.0 項）。当MODも同じ規約（ライブラリ更新版の配布=完全再起動前提）と明記する。

## 7. 実装の作業分解（順序つき）と PoC 計画

各ステップの验收（acceptance）は、対応する自動テストまたは手動確認手順で示す。本 issue の実装開始指示を受けるまではコードは書かない（第1段と同じ規約、docs/specs/issue19-model-import-research.md:3）。

| # | 作業 | 内容とゲームAPI | 验收テスト |
| --- | --- | --- | --- |
| S1 | モデル契約確定 | 11体の GLB に求める仕様（スクリーンショット参照）: 三角数・テクスチャサイズ上限・`Idle/RunForward(×8dir)/Attack/Stagger/Death` クリップ名・原点と向き・スケール単位（Aurena 基準の必要性は VRM MOD の実績、README.md:18-20） | 仕様書レビュー。モデル到着後、各GLBにクリップ名が揃っていることのスクリプト検査 |
| S2 | ライブラリスパイク | glTFast（優先）と UniGLTF（リネーム埋め込み）の2系統で「素のコンソールで GLB→GameObject 化」を確認。採用確定（3章の比較を実測で裏付け） | テスト用小GLB（スキン+AnimationClip入り）を mod フォルダから読み、ヒエラルキに SkinnedMeshRenderer と AnimationClip ができることをログで証明 |
| S3 | 読み込み基盤 | `ModelStore`: (modelId, sha256) キャッシュ・negative cache・Dispose（RelicIcons.cs:10-48 を鋳型） | 同一 modelId を2回要求して import が1回であること（ログの import 回数）。存在しない modelId で元の見た目＋警告ログ1回（毎フレーム再試しない） |
| S4 | 材質変換 | URP Lit/Unlit 生成・テクスチャ流し込み・`_BaseColor` 乗算API（4章） | 変換後 Material の shader 名と _BaseMap が null でないことの単体テスト |
| S5 | アタッチ/デタッチ | `ApplyVariantVisual` と同地点へ差し替えを実装: renderer 隠蔽（DisableRenderersLocal）・復元4時点・プール残骸掃除（2章） | ソロで変種 spawn→撃破→同型モンスター再 spawn（プール再利用）を連続し、2体目が透明にならないこと。MOD reload 後に全モンスターが元見た目であること |
| S6 | アニメ転送 | 1章のパラメータ転送コンポーネント（isWalking/isDead/walkSpeedMultiplier/walkDir/ability/statusHash） | 変種が巡回・攻撃・被弾スタガー・死亡各状態でGLB側クリップが切り替わること（目視＋転送ログの smoke） |
| S7 | 死亡演出・隠密追従 | dissolveDelay/Duration 同期フェード、isRendererOff 追従（4章） | 死亡時に元の敵と同じ timing でGLBが消えること。隠密化スキル/予告でGLBが同期して消え/戻ること |
| S8 | メッセージ拡張 | DreamforgeVariantMsg に modelId+sha256、ResyncNightmares へ搭載、Protocol 版上げ（5章） | 旧プロトコルのクライアントが参加した際に host が拒否/無視し、ゲームが続行すること（既存 protocol 生成りのテストに準拠） |
| S9 | 通信結合 | 途中参加・欠 file・指紋不一致の3分岐（元見た目＋ログ、5章） | 2台PoCのシナリオ3（下記） |
| S10 | ライフサイクル統合 | OnDestroy への完全破棄・ゾーン遷移・セッション切替（6章） | MOD disable→enable（同一 build）でリーク無し（ヒエラルキとキャッシュ数の assert）。完全再起動が必要な条件の明記 |

**PoC 計画（モデルファイル到着後）**: 対象は dream_eater 1体（第1段の計画を継承、docs/specs/issue19-model-import-research.md:47）。

1. **ソロ**: 作業フォルダのゲーム起動＋MOD配置（第1段同様、実施前に利用者の許可を取る、docs/specs/issue19-model-import-research.md:47）。dream_eater の変種 spawn → GLB 表示・アニメ追従・死亡フェード・元 renderer 復元を Player.log と目視で確認。受入: 攻撃・移動・死亡で GLB が追従し、撃破後にゲーム本来の後続 spawn が壊れない。
2. **協力（2台）**: ホスト＋客户端の2台。シナリオ: (a) 両台に同 GLB → 両者の画面で差し替わる、(b) クライアント側のみファイル削除 → そのクライアントだけ元見た目で host 側は差し替わる＋欠 file ログ、(c) 途中参加 → 遅joinした画面でも最長5秒+スポーン待ちで差し替わる。受入: 3シナリオとも通信切断・例外ログなし。
3. **共存確認**: Shape of Dreams VRM 0.4.0 と同時有効にして両MODが動くこと（DLL 名衝突の実証、3章）。受入: VRM MOD の F8 差し替えと本MODの敵差し替えが同時に機能する。

## 未解決（要確認リスト）

1. モンスター prefab ごとの Avatar 種別（humanoid / generic）と Animator の cullingMode（1章）。
2. キャラ用カスタムシェーダーの表示名（4章。プロパティ表のみ確認）。
3. HighlightPlus が動的追加 renderer を拾うか（4章）。
4. glTFast の Unity fork 配布条項と、Burst/Jobs 依存の mod 環境での解決（3章。S2で実測）。
5. UniGLTF 単体構成の依存セット（sqlite3 等の除去可否、3章）。

（本書は調査記録であり、ゲーム本体・`D:\app\stm\steamapps\common\Shape of Dreams`・Workshop MOD フォルダへは書き込んでいない。）
