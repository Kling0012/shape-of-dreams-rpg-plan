using System;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// Pact curses live on the hero as native status effects, so a restart, a host-authority change or a respawn
    /// drops them while the pact itself persists in the run. These pure rules decide when the client re-sends the
    /// curses once, and keep the host side idempotent per player. The ordinal rides in the existing strength
    /// field (strength | ordinal &lt;&lt; 4) so the wire layout does not change; ordinal 0 is a normal Delve send.
    /// </summary>
    public static class PactCurseSync
    {
        private const int StrengthMask = 0xF;

        public static int Encode(int strength, int ordinal)
            => (Math.Max(0, Math.Min(StrengthMask, strength))) | (Math.Max(0, Math.Min(0x7FFFFFF, ordinal)) << 4);

        public static int Strength(int encoded) => encoded & StrengthMask;
        public static int Ordinal(int encoded) => encoded >> 4;

        /// <summary>Key of "this run, on this hero, via this host authority". Empty when a send is not possible.</summary>
        public static string Key(string runId, int heroInstanceId, int authorityId)
            => string.IsNullOrEmpty(runId) ? "" : runId + "|" + heroInstanceId + "|" + authorityId;

        /// <summary>The client re-sends the persisted pacts' curses once per key (run/hero/authority).</summary>
        public static bool ShouldResend(int pactCount, string sentKey, string currentKey)
            => pactCount > 0 && !string.IsNullOrEmpty(currentKey) && currentKey != sentKey;

        /// <summary>Host side: a resync curse for the n-th pact (1-based) is applied only if fewer than n live pact curses exist. Ordinal 0 (a fresh Delve) always applies.</summary>
        public static bool ShouldApply(int ordinal, int liveCurses) => ordinal <= 0 || liveCurses < ordinal;
    }
}
