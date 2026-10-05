using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

// Native API doubles for boss stage-A code paths in files already linked before the boss combat
// runtime itself became linkable (BossCombatState is internal since stage B-2, so the full boss
// runtime now compiles via ModUnderTest links; see BossNativeRuntimeApi.cs).
namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // KillSync boss-kill replay lives with the kill-fact ledger; the linked Hello negotiation
        // only needs the replay entry point to exist.
        private void ReplayAuthoritativeBossKills(DewPlayer player) { }
    }
}
