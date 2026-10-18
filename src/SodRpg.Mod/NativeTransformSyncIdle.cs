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
        /// timestamp. That timestamp feeds client extrapolation, which multiplies the
        /// stored velocity; the equality below keeps every velocity at zero, so the
        /// withheld refresh leaves the rendered pose untouched while Mirror stops
        /// reserializing and resending the unchanged sample. Teleports and the last
        /// sample of a movement always differ and still go through.
        /// </summary>
        internal static bool IsIdleRefresh(in SyncMovementData current, in SyncMovementData next)
        {
            return next.channels == current.channels && next.isTeleport == current.isTeleport
                && next.position == current.position && next.velocity == current.velocity
                && next.rotation == current.rotation && next.angularVelocity == current.angularVelocity
                && next.scale == current.scale && next.scaleVelocity == current.scaleVelocity;
        }
    }
}
