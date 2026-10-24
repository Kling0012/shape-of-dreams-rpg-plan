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
    internal class FxAttachToEntity { }

    // 本体 Dew.Core FxGibs_Kinematic（2026-10-24 逆コンパイル確認）の、NativeGibsRestPerf が
    // 掴む範囲の模倣: Update／DoBurst は DeclaredMethod 探索の受け口（中身は呼ばれないので空）、
    // useGroundPlane は Prefix が読む公開フィールド、_pieces／_velocities／_spins／_burst は
    // FieldRef の受け口。本来は FxInterpolatedEffectBase 経由の MonoBehaviour だが、この境界では
    // Component で足りる（基底の補間はパッチが触らないため）。
    internal sealed class FxGibs_Kinematic : Component
    {
        public bool useGroundPlane = true;
        internal Transform[] _pieces;
        internal Vector3[] _velocities;
        internal float[] _spins;
        internal bool _burst;
        internal void Update() { }
        private void DoBurst() { }
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
    }
    internal class ActorTransformSync
    {
        private SyncMovementData _syncData;
        public SyncMovementData Network_syncData { get => _syncData; set => _syncData = value; }
    }

    // NativePerfPatches.cs の DreamforgeMod 部分が設定を読むための最小限の形。
    // ApplyNativePerfConfig（NativePerfPatches.cs）に設定を足したら、ここにも同じ名前の
    // フィールドを足す。足し忘れると、リンクした NativePerfPatches.cs がこの模倣にない
    // 設定フィールドを参照して、このテストプロジェクトのコンパイルが壊れる。
    internal class DreamforgeConfig
    {
        public bool nativeStatusDurationSync = true, nativeWeaponFxScan = true, nativeObstacleAvoidance = true,
            nativeGemCooldownSync = true,
            nativeAggroPropagation = true, nativeOwnershipSpawnCounts = true, nativeProcCoalesce = true,
            nativeGuestPublishLimit = true,
            nativeAttachFaceIdle = true, nativeAttachFaceSharedVerts = true,
            nativeEmbracePositionReuse = true, nativeAccessoryOffsetCache = true,
            nativeCooldownIdleMultiplier = true, nativeTransformSyncIdle = true,
            nativeSleepPositionReuse = true, nativeCastIndicatorCircle = true, nativeCastIndicatorLine = true, nativeCameraZoomRotation = true,
            nativeOcclusionIdle = true,
            nativeCharacterModelDisplayBounds = true, nativeKillTrackerCullThrottle = true,
            nativeCursorIdle = true, nativeStatsCalcRpcIdle = true, nativeStatsRatioIdle = true, nativePositionSyncIdle = true,
            nativeChargeRpcIdle = true,
            nativeProjectileIdleRpc = true,
            nativeCustomRpcDispatch = true,
            nativeSampleUpdateIdle = true,
            nativeDashSweepStep = true,
            nativeHeroWeaponIdle = true,
            nativePlayerTimerIdle = true,
            nativeEntityRotationIdle = true,
            nativeRoomStartDependency = true,
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
            nativeLobbyAccessoryIdle = true, nativeUltChargeRpcIdle = true,
            nativeChaosShrineHitScan = true,
            nativeChaosShrineChoiceCache = true,
            nativeChaosPetalIdle = true,
            nativeGibsRestSkip = true,
            nativeBossSoulShrineScan = true,
            guestSkillLockRecovery = true;
        // #1050: 抽出した記録タブの描画（DrawAutoSalvageSettings）が読む設定。既定値は本物と同じ。
        public AutoSalvageRarity autoSalvageRarity = AutoSalvageRarity.None;
        public int toastMaxLines = 7;
    }
    // NativePerfPatches.cs が設定を写す先（本物は NativeSkillLockRecovery.cs。本体 API に依存するため、ここでは切り替えだけ模倣）。
    internal static class NativeSkillLockRecovery { internal static bool Enabled = true; }
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
    internal class Projectile
    {
        public struct EntityHit { }

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
