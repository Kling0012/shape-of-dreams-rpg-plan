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
namespace SodRpg.Mod
{
    // 本体のグローバル名前空間にあるデリゲートと同じ形。
    internal delegate float EaseFunction(float s, float e, float v);

    // 本体 EntityVisual.model（EntityModel.holsteredWeapon）と付属エフェクトの最小限の形。
    internal class EntityModel { public Transform holsteredWeapon; }
    internal class FxAttachToEntity { }

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
    internal class DreamforgeConfig
    {
        public bool nativeStatusDurationSync = true, nativeWeaponFxScan = true, nativeObstacleAvoidance = true,
            nativeAggroPropagation = true, nativeOwnershipSpawnCounts = true, nativeProcCoalesce = true,
            nativeEmbracePositionReuse = true, nativeCooldownIdleMultiplier = true, nativeTransformSyncIdle = true,
            nativeSleepPositionReuse = true, nativeCastIndicatorCircle = true, nativeCameraZoomRotation = true,
            nativeCharacterModelDisplayBounds = true;
    }
    public partial class DreamforgeMod
    {
        internal DreamforgeConfig config = new DreamforgeConfig();
        partial void ApplyNativePerfConfig();
    }

    // 本体 Dew.Core r1.4.0.13 Ai_EmbraceNewIdentity。UpdatePositions は IL 転写（ilspycmd の
    // 逆コンパイルと -il 出力の双方で同じ分岐であることを確認）: NetworktargetSkill の null 早期 return、
    // Clamp01((Time.time - creationTime) / channelTime)、
    // Lerp(_targetSkillInitPos, info.caster.position, _easeFunc(0f, 1f, v)) を skill.transform へ書き、
    // null 要素を飛ばして各要素へ NetworktargetSkill.position（Actor.position）を配る。
    // 本体の Actor.position は r1.4.0.13 では get { return transform.position; }（ilspycmd で確認）。
    // Prefix も配布値を同じ Actor.position から読むため、getter の定義が変わっても
    // パッチと本体の配布座標は一致し続ける。指紋が変わったときの見直し手順は
    // docs/specs/perf-loop/20261018-1653.md。
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
