using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// 静止したアクターの座標同期の省略判定。NativePerfPatches.cs の Harmony Prefix から
    /// 呼ばれる純粋関数で、本体側の型しか使わないため単体テストへ直接リンクできる。
    /// </summary>
    internal static class NativeTransformSyncIdle
    {
        /// <summary>
        /// A capture of a stationary actor differs from the stored sample only by its
        /// timestamp. Withholding such a refresh stops Mirror from reserializing and
        /// resending the unchanged sample. While the omission lasts the client keeps
        /// extrapolating from the stored timestamp, so every velocity must be exactly
        /// zero component-wise: Unity's approximate == would treat a residual velocity
        /// below 1e-5 as "equal to zero", and the leftover extrapolation term
        /// velocity * (now - timestamp) would then grow with the length of the
        /// omission. With exact zeros that term is zero no matter how stale the
        /// timestamp is, so the rendered pose stays untouched. Teleports and the last
        /// sample of a movement always differ and still go through.
        /// </summary>
        internal static bool IsIdleRefresh(in SyncMovementData current, in SyncMovementData next)
        {
            return next.channels == current.channels && next.isTeleport == current.isTeleport
                && next.position == current.position
                && IsExactlyZero(current.velocity) && IsExactlyZero(next.velocity)
                && next.rotation == current.rotation
                && IsExactlyZero(current.angularVelocity) && IsExactlyZero(next.angularVelocity)
                && next.scale == current.scale
                && IsExactlyZero(current.scaleVelocity) && IsExactlyZero(next.scaleVelocity);
        }

        /// <summary>速度の外挿項が厳密に 0 になるのは成分ごとの float 比較で 0 のときだけ。</summary>
        private static bool IsExactlyZero(Vector3 value) => value.x == 0f && value.y == 0f && value.z == 0f;
    }
}
