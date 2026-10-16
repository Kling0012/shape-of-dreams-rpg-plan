using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // #449: transient diagnostics only. Never serialize inputs or log player names/IDs.
    internal sealed class ExpeditionDiagnostics
    {
        private readonly Dictionary<string, string> _last = new Dictionary<string, string>();
        private string _runId;
        private int _lines;
        private const int MaxLinesPerRun = 48;

        internal void Write(string stage, string details, bool boundary = false)
        {
            try
            {
                string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
                if (_runId != runId)
                {
                    _runId = runId;
                    _last.Clear();
                    _lines = 0;
                }
                // Reserve eight lines for conclusion/settlement even in a long expedition.
                if (_lines >= (boundary ? MaxLinesPerRun : MaxLinesPerRun - 8)
                    || (_last.TryGetValue(stage, out var previous) && previous == details)) return;
                _last[stage] = details;
                _lines++;
                Log.Info("diag449 " + stage + " " + details + (_lines == MaxLinesPerRun ? " (run log limit reached)" : ""));
            }
            catch (Exception) { } // Logging must not interrupt settlement, transport or application.
        }

        internal static string BuildSummary(Build build)
        {
            if (build == null) return " build=none";
            return " dream=" + build.DreamLevel + " stars=" + build.SpentStarPoints + " heat=" + build.Heat
                + " stats=" + build.Stats.Count + " powers=" + build.Powers.Count + " sets=" + build.Sets.Count
                + " bossMoves=" + build.BossMoves.Count + " bossRewards=" + build.BossRewards.Count
                + " attackPct=" + build.Get(Stat.AttackPct) + " powerPct=" + build.Get(Stat.PowerPct)
                + " healthPct=" + build.Get(Stat.MaxHealthPct) + " armor=" + build.Get(Stat.Armor);
        }
    }
}
