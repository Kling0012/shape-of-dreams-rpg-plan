using UnityEngine;

// NativePerfPatches.cs（取り込み位置の軽量化パッチ）を Issue73.Native.Tests で実行するための
// 本体 API 境界。Ai_EmbraceNewIdentity.UpdatePositions は本体 Dew.Core r1.4.0.13 の逆コンパイル
// 結果（ilspycmd の IL と同じ分岐）の忠実な転写で、等価性テストの「パッチ無効側」として動く。
// ここには本体の処理を書き直さない: 補間と配布の計算はリンクされた実ソース側が実行する。
namespace UnityEngine.AI
{
    internal enum ObstacleAvoidanceType { NoAvoidance, LowQualityObstacleAvoidance, GoodQualityObstacleAvoidance, HighQualityObstacleAvoidance }
    internal class NavMeshAgent
    {
        public ObstacleAvoidanceType obstacleAvoidanceType { get; set; }
        // NativeAgentVelocityIdlePerf の差し替え先 SetVelocity が読み書きする本体 API の形
        // （実機 NavMeshAgent.velocity。実機の getter/setter はどちらもネイティブ呼び出し）。
        // AgentVelocityIdlePerfTests が等価性を検証できるよう、この模倣では setter の呼ばれた
        // 回数を数え、getter が例外を出す切り替え（実機のネイティブ呼び出しの失敗に対応）を
        // 持たせる。
        private Vector3 _velocity;
        public Vector3 velocity
        {
            get
            {
                if (throwOnVelocityGet) throw new System.InvalidOperationException("velocity getter failed");
                return _velocity;
            }
            set { _velocity = value; velocityWrites++; }
        }
        internal int velocityWrites;
        internal bool throwOnVelocityGet;
    }

    // NativeAgentVelocityIdlePerf の Transpiler に渡す IL の見本。WriteVelocityOnce は実機の
    // DoMovementProcessorFrameUpdate と同じ形（NavMeshAgent の set_velocity の呼び出しが
    // ちょうど1か所）、WriteVelocityTwice は呼び出しが2か所に増えた拒否の見本
    // （FxCastTelegraph.UpdatePositionTwoSites と同じ使い方。実行はせず、IL の取り出しだけ）。
    internal static class AgentVelocityIlSamples
    {
        public static void WriteVelocityOnce(NavMeshAgent agent) { agent.velocity = Vector3.zero; }
        public static void WriteVelocityTwice(NavMeshAgent agent) { agent.velocity = Vector3.zero; agent.velocity = Vector3.zero; }
    }
}
namespace UnityEngine
{
    // 実機 UnityEngine.Transform の親子関係のうち、取り込み位置パッチ（HasAncestorElement）が
    // 読む範囲の境界。実機の Transform.position への書き込みが子を連れて動く座標の連鎖は
    // 模かない（境界の position はただの値）。そのため「要素が skill.transform の祖先なら
    // 本体ループの後続要素が別の値を受け取る」という差そのものはこの模倣では再現できず、
    // Prefix がその場面で本体経路へ戻る合図を出すこと（AncestorElement… のテスト）で担保する。
    internal partial class Transform
    {
        internal Transform parent { get; set; }
    }
    // NativeProjectileIdleRpcPerf の Prefix が読む効果プレハブ字段の境界。実機では
    // UnityEngine.GameObject（ prefab 参照）。中身は読まない。
    // （#983）PropEnt_Merchant_Base の境界ループが各 props の transform を読むため、
    // 自身の Transform を返す受け口を足した（実機の GameObject.transform と同じ意味）。
    internal class GameObject
    {
        public Transform transform;
        // NativeGemResponsibilityHitPerf の Prefix が初回だけ走らせる子走査の受け口。
        // 実機と同じ includeInactive=false の意味。境界では子を children に置いた要素の
        // うち T に当たるものを返し、走査回数も数える（GemResponsibilityHitPerfTests が
        // キャッシュで再走査が起きないことの検証に使う）。
        internal readonly System.Collections.Generic.List<Component> children
            = new System.Collections.Generic.List<Component>();
        internal int componentScans;
        public T[] GetComponentsInChildren<T>() where T : Component
        {
            componentScans++;
            System.Collections.Generic.List<T> found = null;
            foreach (Component child in children)
                if (child is T typed) (found ??= new System.Collections.Generic.List<T>()).Add(typed);
            return found?.ToArray() ?? new T[0];
        }
    }
    // NativeSkillWorldModelLoadPerf と NativeGemWorldModelLoadPerf が初回の読み込みと
    // 設定無効時のフォールバックで呼ぶ本体 Resources.Load の境界。prefab 参照は中身を
    // 読まないので、返す値（next。既定は null）と呼ばれた回数・パス（loads）の記録だけを持つ。
    // SkillWorldModelLoadPerfTests と GemWorldModelLoadPerfTests がこの記録で、
    // キャッシュが Load を呼ばずに同じ参照を返すこと・null を保持しないこと・
    // 設定無効時に毎回呼ぶことを確かめる。
    internal static class Resources
    {
        internal static GameObject next;
        internal static readonly System.Collections.Generic.List<string> loads
            = new System.Collections.Generic.List<string>();
        public static T Load<T>(string path)
        {
            loads.Add(path);
            return next is T typed ? typed : default;
        }
    }
    // NativeCustomRpcDispatchPerf がハンドラの例外を記録する先（実物は UnityEngine.Debug.LogException）。
    // 検証では何を記録したか見られるように、呼ばれた例外をため込むだけ。
    // Log は NativeObliviaxKidnapLogPerf の差し替え先 MaybeLog が設定無効時に呼ぶ本体の呼び出し。
    // 検証では渡された引数をそのままため込み、ObliviaxKidnapLogPerfTests が読む。
    internal static class Debug
    {
        internal static readonly System.Collections.Generic.List<System.Exception> Logged
            = new System.Collections.Generic.List<System.Exception>();
        internal static readonly System.Collections.Generic.List<object> LoggedMessages
            = new System.Collections.Generic.List<object>();
        public static void LogException(System.Exception exception) => Logged.Add(exception);
        public static void Log(object message) => LoggedMessages.Add(message);
    }
}
namespace SodRpg.Mod
{
    // 本体のグローバル名前空間にあるデリゲートと同じ形。
    internal delegate float EaseFunction(float s, float e, float v);

    // 本体 EntityVisual.model（EntityModel.holsteredWeapon）と付属エフェクトの最小限の形。
    internal class EntityModel { public Transform holsteredWeapon; }
    // NativeFxAttachIdlePerf の書き込み指紋の見本（FxAttachIdlePerfBoundary.cs）を足せるよう
    // partial にする（この部分宣言自体の中身は従来どおり空のまま）。
    internal partial class FxAttachToEntity { }
    // NativeCastTelegraphIdlePerf の対象 FxCastTelegraph（本体 Dew.Core）の、
    // Transpiler が見る書き込みの形だけの転写。実機の UpdatePosition は位置・向きの
    // 追従計算を経て、最後に followPosition のとき transform.position へ、
    // followRotation のとき transform.rotation へそれぞれ1回ずつ書く
    // （docs/specs/perf-loop/20261024-0950.md。実 DLL の IL で set_position 1・
    // set_rotation 1 を確認済み）。Transpiler は Transform への書き込みの数だけを
    // チェックして差し替えるため、ここでは計算を省いてその形を再現する。
    // UpdatePositionTwoSites は位置の書き込みが2つ並んだ拒否の見本
    // （実行はせず、Transpiler に渡す IL の取り出しだけに使う）。
    internal class FxCastTelegraph
    {
        public Transform transform = new Transform();
        public bool followPosition = true, followRotation = true;
        public Vector3 targetPosition;
        public Quaternion targetRotation;

        public void UpdatePosition()
        {
            if (followPosition)
            {
                transform.position = targetPosition;
            }
            if (followRotation)
            {
                transform.rotation = targetRotation;
            }
        }

        internal void UpdatePositionTwoSites()
        {
            transform.position = targetPosition;
            transform.position = targetPosition;
            transform.rotation = targetRotation;
        }
    }

    internal enum SyncTransformChannel : byte { None = 0, Position = 1, Rotation = 2, Scale = 4 }
    internal struct SyncMovementData
    {
        public SyncTransformChannel channels;
        public bool isTeleport;
        public double timestamp;
        public Vector3 position, velocity;
        public Quaternion rotation;
        public Vector3 angularVelocity, scale, scaleVelocity;
        // チャンネルの有無を確かめる本体 API。NativeTransformSyncWriteSkipPerf の
        // ValidateBody はこのメソッドを名前で解決して IL 命令で呼び出し回数を数える
        // （実機の FrameUpdate / LogicUpdate は3回使う）。中身は本体と同じ channels の
        // ビット判定で、テストから実行しても同じ意味で動く。
        public bool Has(SyncTransformChannel channel) => (channels & channel) != SyncTransformChannel.None;
    }
    internal class ActorTransformSync
    {
        private SyncMovementData _syncData;
        public SyncMovementData Network_syncData { get => _syncData; set => _syncData = value; }
        // NativeTransformSyncHoldPerf（収束姿勢の FrameUpdate 省略）が読む本体側の状態と
        // 受け口。判定そのものはテストしていない（実行するテストは無い）が、リンクした
        // NativePerfPatches.cs がこの名前を参照するので、本体と同じ名前で置いておく。
        // isServer は NetworkBehaviour、transform は Component の受け口。
        internal bool _hasData;
        internal bool _serverTickPending;
        internal Vector3 _renderPos, _renderPosCv, _renderScale, _renderScaleCv;
        internal Quaternion _renderRot, _renderRotCv;
        public bool isServer => false;
        public Transform transform = new Transform();
        public void FrameUpdate() { }
    }

    // NativeTransformSyncWriteSkipPerf の ValidateBody / Apply に渡す IL の見本。
    // WritesLikeShippedUpdate は本体 Dew.Core r1.4.1.2 の FrameUpdate / LogicUpdate と
    // 同じ書き込みの形（SyncMovementData.Has が3つ、Transform への set_position /
    // set_rotation / set_localScale が各1つ。docs/specs/perf-loop/20261025-0353.md で
    // 実 DLL の IL を確認）。SecondPositionWrite は set_position を2つに増やした拒否の
    // 見本、TwoHasCalls は Has の呼び出しを2つに減らした拒否の見本
    // （どちらも実行はせず、IL の取り出しだけに使う）。
    internal static class TransformSyncWriteSkipIlShapes
    {
        internal static void WritesLikeShippedUpdate(Transform tr, SyncMovementData data)
        {
            if (data.Has(SyncTransformChannel.Position)) tr.position = data.position;
            if (data.Has(SyncTransformChannel.Rotation)) tr.rotation = data.rotation;
            if (data.Has(SyncTransformChannel.Scale)) tr.localScale = data.scale;
        }

        internal static void SecondPositionWrite(Transform tr, SyncMovementData data)
        {
            if (data.Has(SyncTransformChannel.Position)) tr.position = data.position;
            if (data.Has(SyncTransformChannel.Rotation)) tr.rotation = data.rotation;
            if (data.Has(SyncTransformChannel.Scale)) tr.localScale = data.scale;
            tr.position = data.position;
        }

        internal static void TwoHasCalls(Transform tr, SyncMovementData data)
        {
            if (data.Has(SyncTransformChannel.Position)) tr.position = data.position;
            if (data.Has(SyncTransformChannel.Rotation)) tr.rotation = data.rotation;
            tr.localScale = data.scale;
        }
    }

    // NativePerfPatches.cs の DreamforgeMod 部分が設定を読むための最小限の形。
    // ApplyNativePerfConfig（NativePerfPatches.cs）に設定を足したら、ここにも同じ名前の
    // フィールドを足す。足し忘れると、リンクした NativePerfPatches.cs がこの模倣にない
    // 設定フィールドを参照して、このテストプロジェクトのコンパイルが壊れる。
    internal class DreamforgeConfig
    {
        public bool nativeStatusDurationSync = true, nativeWeaponFxScan = true, nativeObstacleAvoidance = true,
            nativeGemCooldownSync = true, nativeGemWorldModelCache = true,
            nativeAggroPropagation = true, nativeOwnershipSpawnCounts = true, nativeProcCoalesce = true,
            nativeGuestPublishLimit = true,
            nativeAttachFaceIdle = true, nativeAttachFaceSharedVerts = true,
            nativeEmbracePositionReuse = true, nativeAccessoryOffsetCache = true,
            nativeCooldownIdleMultiplier = true, nativeTransformSyncIdle = true,
            nativeTransformSyncWriteSkip = true,
            nativeTransformSyncHold = true,
            nativeSleepPositionReuse = true, nativeCastIndicatorCircle = true, nativeCastIndicatorLine = true, nativeCameraZoomRotation = true,
            nativeOcclusionIdle = true,
            nativeCharacterModelDisplayBounds = true, nativeKillTrackerCullThrottle = true,
            nativeCursorIdle = true, nativeStatsCalcRpcIdle = true, nativeStatsRatioIdle = true, nativePositionSyncIdle = true,
            nativeChargeRpcIdle = true, nativeSkillWorldModelCache = true,
            nativeProjectileIdleRpc = true,
            nativeCustomRpcDispatch = true,
            nativeSampleUpdateIdle = true,
            nativeDashSweepStep = true,
            nativeHeroWeaponIdle = true,
            nativePlayerTimerIdle = true,
            nativeEntityRotationIdle = true,
            nativeRoomStartDependency = true,
            nativeRoomPlayerPathableScan = true,
            nativeChatMuteCheckAlloc = true,
            nativeProxyPositionIdle = true,
            nativePickupIdle = true,
            nativeEnemyHitFxBudget = true,
            nativeTimescaleIdle = true,
            nativeRedGiantDamageText = true,
            nativeStealthRevealOverlapScan = true,
            nativeMerchantPropIdle = true,
            nativeGenericTransformIdle = true,
            nativeObliviaxKidnapLog = true,
            nativeMirageVolumeIdle = true,
            nativeDamageShakeIdle = true,
            nativeAudioSetupIdle = true,
            nativeCastTelegraphIdle = true,
            nativeFxAttachIdle = true,
            nativeLobbyAccessoryIdle = true, nativeUltChargeRpcIdle = true,
            nativeChaosShrineHitScan = true,
            nativeChaosShrineChoiceCache = true,
            nativeChaosPetalIdle = true,
            nativeGamepadRumbleIdle = true,
            nativeGemResponsibilityHitCache = true,
            nativeMusicSourceIdle = true,
            nativeTransitionIdle = true,
            nativeAgentVelocityIdle = true,
            nativeMidJoinLoadoutIdle = true,
            nativeEntityAnimLayerWeightIdle = true,
            nativeChaosStatBonusTooltipCache = true,
            nativeEntityOwnerClientFast = true,
            nativeProjectileTargetClientFast = true,
            nativeEntityAbilitySubscribeDedup = true,
            nativeBossPoolSpawnLog = true,
            nativeRoomPrewarmResolveOnce = true,
            nativeBossSoulShrineScan = true,
            guestSkillLockRecovery = true;
        // #1050: 抽出した記録タブの描画（DrawAutoSalvageSettings）が読む設定。既定値は本物と同じ。
        public AutoSalvageRarity autoSalvageRarity = AutoSalvageRarity.None;
        public int toastMaxLines = 7;
    }
    // （#1123 以降）NativeSkillLockRecovery は本物（リンク）がコンパイルされるため、
    // ここに Enabled だけの模倣は置かない。
    public partial class DreamforgeMod
    {
        internal DreamforgeConfig config = new DreamforgeConfig();
        partial void ApplyNativePerfConfig();
    }

    // NativeObliviaxKidnapLogPerf が typeof で参照するだけの本体 Se_ObliviaxKidnap の境界。
    // 実機はグローバル名前空間の StatusEffect 派生で、Prepare が RPC コルーチンの状態機械
    // <<RpcPlayAnimation>g__Routine|9_0>d（実 DLL の IL で確認）を探す。ここでは型の存在だけ。
    internal class Se_ObliviaxKidnap : StatusEffect { }

    // NativeProjectileIdleRpcPerf が typeof と Prefix で参照する本体 Projectile の境界。
    // 実機ではグローバル名前空間の Mirror.NetworkBehaviour 派生。ここには送る・省くの判定が
    // 読む 4 つの公開効果プレハブ字段と、Prepare が探す weaver 送信メソッドの名前と引数の形
    // だけを置く（中身は呼ばれないので空）。実機の受信側 UserCode_* は対応する字段が null の
    // とき最初に return する（実 DLL 逆コンパイルで確認済み）。
    // NativeProjectileTargetSyncVarPerf（リンク）の Prefix もこの境界に対して動く。Prepare が
    // Network_targetEntity の getter と _targetEntity・____targetEntityNetId の 2 字段を実物と
    // 同じ名前で解決し、ProjectileTargetSyncVarPerfTests が Prefix を直接呼ぶ。実機の
    // isClient は netIdentity.isClient。実機の Network_targetEntity getter は
    // GetSyncVarNetworkBehaviour の辞書引きを呼び出しごとにやり直すが、境界の getter は
    // 解決済みの _targetEntity をそのまま返す（その辞書引きの置き換えこそがこのパッチ）。
    internal class Projectile
    {
        public struct EntityHit { }

        // Prefix が読む NetworkBehaviour 継承面。
        // ProjectileIdleRpcPerfTests が typeof(Projectile).GetFields() を GameObject 設定で
        // 回すため、フィールドではなくプロパティにして列挙対象外にする。
        public Mirror.NetworkIdentity netIdentity { get; set; }
        public bool isServer { get; set; }
        public bool isClient { get; set; }

        // NativeProjectileTargetSyncVarPerf の Prepare と Prefix が読む標的 SyncVar の対
        // （実物の weaver 生成の私有字段と同じ名前）。公開字段にすると同じく
        // ProjectileIdleRpcPerfTests の GetFields() 列挙で GameObject を詰められるため、
        // 列挙対象外の内部可視性にする（AccessTools.Field は内部字段も解決する）。
        internal Entity _targetEntity;
        internal Mirror.NetworkBehaviourSyncVar ____targetEntityNetId;
        // Prepare の AccessTools.PropertyGetter の解決先。本体の中身（辞書引きからの解決）は
        // Prefix の差し替え対象のため、境界では Prefix と同じ解決済み参照を返すだけ。
        internal Entity Network_targetEntity => _targetEntity;

        public GameObject effectOnFly;
        public GameObject effectOnEntity;
        public GameObject effectOnComplete;
        public GameObject effectOnDissipate;

        private void RpcPlayOnEntityEffect(EntityHit hit) { }
        private void RpcPlayOnDissipateEffect() { }
        private void RpcStopOnFlyEffect() { }
        private void RpcPlayOnCompleteEffect() { }
    }

    // 本体 Dew.Core r1.4.0.13 Ai_EmbraceNewIdentity。UpdatePositions は IL 転写（ilspycmd の
    // 逆コンパイルと -il 出力の双方で同じ分岐であることを確認）: NetworktargetSkill の null 早期 return、
    // Clamp01((Time.time - creationTime) / channelTime)、
    // Lerp(_targetSkillInitPos, info.caster.position, _easeFunc(0f, 1f, v)) を skill.transform へ書き、
    // null 要素を飛ばして各要素へ NetworktargetSkill.position（Actor.position）を配る。
    // 本体の Actor.position は r1.4.0.13 では get { return transform.position; }（ilspycmd で確認）。
    // Prefix も配布値を同じ Actor.position から読むため、getter の定義が変わっても
    // パッチと本体の配布座標は一致し続ける。
    // 前提（実機の Transform のみ成立）: 本体ループは要素ごとに NetworktargetSkill.position を
    // 読み直すため、要素が skill.transform 自身かその祖先だと、その要素への書き込みが skill の
    // ワールド座標を動かし、後の要素は移動後の別の値を受け取る。Prefix の一括配布はこの差を
    // 再現できないため、Prefix（NativePerfPatches.cs の HasAncestorElement）はこの場面を検出して
    // 本体経路へ戻る。この模倣の Transform は親子の座標の連鎖を模かないため上記の差は再現できず、
    // テストは「祖先の要素があるとき Prefix が本体経路へ戻る合図を出す」ことで確かめる。
    // 指紋が変わったときの見直し手順は docs/specs/perf-loop/20261018-1653.md。
    internal class Ai_EmbraceNewIdentity : AbilityInstance
    {
        public SkillTrigger NetworktargetSkill { get; set; }
        public float channelTime;
        public Transform[] onSkillTransform;
        public float creationTime;
        private Vector3 _targetSkillInitPos;
        private EaseFunction _easeFunc;

        internal void UpdatePositions()
        {
            if (!(NetworktargetSkill != null))
            {
                return;
            }
            float v = Mathf.Clamp01((Time.time - creationTime) / channelTime);
            NetworktargetSkill.transform.position = Vector3.Lerp(_targetSkillInitPos, info.caster.position, _easeFunc(0f, 1f, v));
            Transform[] array = onSkillTransform;
            foreach (Transform transform in array)
            {
                if (!(transform == null))
                {
                    transform.position = NetworktargetSkill.position;
                }
            }
        }
    }
}
