using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

// Native API doubles for the boss stage-A code paths that the linked production files call.
// The boss combat runtime itself (BossRuntime/BossMechanisms/BossDemon/BossNativeAdapters) cannot
// be linked here: its BossCombatState is a private nested type of HostAuthority, while the shared
// test stub declares HeroRuntime as internal, and C# accessibility domains admit no field
// declaration for rt.Boss that both the linked production partials and this assembly accept.
// Reported in the stage-A test notes; only the two entry points the already-linked files call are
// stubbed here.
namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // KillSync boss-kill replay lives with the kill-fact ledger; the linked Hello negotiation
        // only needs the replay entry point to exist.
        private void ReplayAuthoritativeBossKills(DewPlayer player) { }

        // The boss memory-use hook is inert without the linked boss runtime.
        internal void PublishBossConfirmedMemoryUse(NativeAttributedMemoryCast.Cast cast) { }
    }
}
