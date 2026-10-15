using System;
using System.Collections.Generic;
using UnityEngine;

namespace SodRpg.Mod
{
    // Diagnostic state is bounded and never participates in trade, save or transport decisions.
    internal static class CoopTradeDiagnostics
    {
        private static readonly Dictionary<string, float> _recent = new Dictionary<string, float>();

        internal static void Record(string side, string stage, string id, int revision, string state)
        {
            try
            {
                // Only the random transaction ID is useful for joining the peers' logs. Never
                // write player keys/names, offers, profile JSON or exception messages here.
                string trade = Guid.TryParseExact(id, "N", out var parsed) ? parsed.ToString("N") : "-";
                string line = "CoopTrade diagnostic side=" + side + " stage=" + stage + " trade=" + trade
                    + " revision=" + revision + " mod=" + HostAuthority.ModVersion + " " + state;
                float now = Time.unscaledTime;
                if (_recent.TryGetValue(line, out float previous) && now >= previous && now - previous < 30f) return;
                if (_recent.Count >= 64) _recent.Clear();
                _recent[line] = now;
                Log.Info(line);
            }
            catch (Exception) { } // Logging must never cancel a trade or change its outcome.
        }
    }
}
