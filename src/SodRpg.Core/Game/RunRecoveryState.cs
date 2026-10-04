using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Unsettled expedition facts and their original zone context, independent of a connection.</summary>
    public sealed class RunRecoveryState
    {
        public string RunId { get; set; }
        public int ZoneIndex { get; set; } = -1;
        public int LastArrival { get; set; } = -1;
        public List<int> Arrivals { get; } = new List<int>();
        public List<string> CommittedChoices { get; } = new List<string>();
        public List<PendingRunKill> PendingKills { get; } = new List<PendingRunKill>();
        public string PendingResultRunId { get; set; }
        public bool? PendingVictory { get; set; }
        public List<string> PublisherHistory { get; } = new List<string>();
        public string PublisherTerminalChoices { get; set; }
        public bool? PublisherVictory { get; set; }
        public string DividendRunId { get; set; }
        public List<PressureDividendReward> PendingDividends { get; } = new List<PressureDividendReward>();
        public List<string> DividendNonces { get; } = new List<string>();
        public List<string> DividendDeaths { get; } = new List<string>();

        public RunRecoveryState Clone()
        {
            var copy = new RunRecoveryState
            {
                RunId = RunId, ZoneIndex = ZoneIndex, LastArrival = LastArrival,
                PendingResultRunId = PendingResultRunId, PendingVictory = PendingVictory,
                PublisherTerminalChoices = PublisherTerminalChoices, PublisherVictory = PublisherVictory,
                DividendRunId = DividendRunId,
            };
            copy.Arrivals.AddRange(Arrivals);
            copy.CommittedChoices.AddRange(CommittedChoices);
            copy.PendingKills.AddRange(PendingKills);
            copy.PublisherHistory.AddRange(PublisherHistory);
            copy.PendingDividends.AddRange(PendingDividends);
            copy.DividendNonces.AddRange(DividendNonces);
            copy.DividendDeaths.AddRange(DividendDeaths);
            return copy;
        }
    }
}
