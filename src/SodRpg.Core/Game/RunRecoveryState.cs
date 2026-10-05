using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Unsettled expedition facts and their original zone context, independent of a connection.</summary>
    public sealed class RunRecoveryState
    {
        public string RunId { get; set; }
        public int ZoneIndex { get; set; } = -1;
        public int LastArrival { get; set; } = -1;
        public long GraphEpoch { get; set; }
        public long SegmentEpoch { get; set; }
        public long RetiredBeforeSegment { get; set; }
        public long? ArrivalGraph { get; set; }
        public long ArrivalSegment { get; set; }
        public long PublisherRetiredBeforeSegment { get; set; }
        public long DividendRetiredBeforeGraph { get; set; }
        public SortedDictionary<string, long> DividendReceiptGraphs { get; } = new SortedDictionary<string, long>();
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
        /// <summary>v1.32 遠征の鍛錬（RunGrowth）のスタック。どの遠征のものかと、旅人の持ち主ごとの値。</summary>
        public string GrowthRunId { get; set; }
        public List<RunGrowthSave> Growth { get; } = new List<RunGrowthSave>();

        public RunRecoveryState Clone()
        {
            var copy = new RunRecoveryState
            {
                RunId = RunId, ZoneIndex = ZoneIndex, LastArrival = LastArrival,
                GraphEpoch = GraphEpoch, SegmentEpoch = SegmentEpoch, RetiredBeforeSegment = RetiredBeforeSegment,
                ArrivalGraph = ArrivalGraph, ArrivalSegment = ArrivalSegment,
                PublisherRetiredBeforeSegment = PublisherRetiredBeforeSegment, DividendRetiredBeforeGraph = DividendRetiredBeforeGraph,
                PendingResultRunId = PendingResultRunId, PendingVictory = PendingVictory,
                PublisherTerminalChoices = PublisherTerminalChoices, PublisherVictory = PublisherVictory,
                DividendRunId = DividendRunId, GrowthRunId = GrowthRunId,
            };
            copy.Arrivals.AddRange(Arrivals);
            copy.CommittedChoices.AddRange(CommittedChoices);
            copy.PendingKills.AddRange(PendingKills);
            copy.PublisherHistory.AddRange(PublisherHistory);
            copy.PendingDividends.AddRange(PendingDividends);
            copy.DividendNonces.AddRange(DividendNonces);
            copy.DividendDeaths.AddRange(DividendDeaths);
            foreach (var receipt in DividendReceiptGraphs) copy.DividendReceiptGraphs[receipt.Key] = receipt.Value;
            foreach (var save in Growth)
                copy.Growth.Add(new RunGrowthSave { Owner = save.Owner, GrowthId = save.GrowthId, Stacks = save.Stacks, ProgressMilli = save.ProgressMilli });
            return copy;
        }
    }
}
