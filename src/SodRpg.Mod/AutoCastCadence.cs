using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Pure pacing rules of the native Bismuth autocast, split from
    // NativeBismuthAutoCast so the multiplayer limits stay testable without
    // game runtime types. The caller supplies the already-clamped load
    // pressure and the peer facts; nothing here reads game state.
    internal static class AutoCastCadence
    {
        internal const int MaxCastsPerSecond = 30;
        internal const int MinCastsPerSecond = 5;

        // Solo keeps the native pressure curve. In multiplayer the interval is
        // raised to at least CoopBalance.AutoCastIntervalSeconds and the rolling
        // per-second cap is lowered to its floor when the host uses native
        // cadence, or any peer without this MOD build is present.
        // The output is monotonic in pressure: a higher pressure widens the
        // interval and lowers the per-second cap. The early cadence reject in
        // NativeBismuthAutoCast depends on this, so keep it when changing this method.
        internal static void Compute(float pressure, bool coop, bool nativeCadence, bool allPeersSupported,
            out float interval, out int castsPerSecond)
        {
            interval = Mathf.Lerp(0f, 0.3f, pressure);
            castsPerSecond = Mathf.RoundToInt(Mathf.Lerp(MaxCastsPerSecond, MinCastsPerSecond, pressure));
            if (coop && (nativeCadence || !allPeersSupported))
            {
                interval = Mathf.Max(interval, CoopBalance.AutoCastIntervalSeconds);
                castsPerSecond = Math.Min(castsPerSecond, Mathf.FloorToInt(1f / CoopBalance.AutoCastIntervalSeconds));
            }
        }
    }
}
