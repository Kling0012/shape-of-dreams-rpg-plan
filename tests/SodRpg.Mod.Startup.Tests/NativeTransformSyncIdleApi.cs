// リンクした本物の NativeTransformSyncIdle.cs（静止アクターの座標同期省略の判定）のための
// 本体 API 境界。本体 Dew.Core のグローバル名前空間にある 2 型のフィールド並びだけを模倣し、
// 判定それ自体はリンクされた実ソース側がそのまま実行する。UnityEngine の Vector3 / Quaternion の
// 境界スタブ（StartupGameApi.cs / FrostFxLifetimeApi.cs）に Unity と同じ近似の == 演算子を足して
// あるため、リンクされた判定は本体と同じ比較意味で動く。
using UnityEngine;

public enum SyncTransformChannel : byte
{
    None = 0,
    Position = 1,
    Rotation = 2,
    Scale = 4
}

public struct SyncMovementData
{
    public SyncTransformChannel channels;
    public bool isTeleport;
    public double timestamp;
    public Vector3 position;
    public Vector3 velocity;
    public Quaternion rotation;
    public Vector3 angularVelocity;
    public Vector3 scale;
    public Vector3 scaleVelocity;
}
