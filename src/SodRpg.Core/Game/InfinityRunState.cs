using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum InfinityPhase { Exploring, BossDue, BossFight, WaitingSoulFinish, AwaitingChoice, Transitioning, Returning }

    /// <summary>Native graph identity is independent of zone, waypoint and authority generations.</summary>
    public sealed class InfinityRunState
    {
        public const int MaximumGraphNodes = 4096;
        public bool Enabled => true;
        public string FixedZoneId { get; set; }
        public string DifficultyId { get; set; }
        public int Interval { get; set; } = 10;
        public long ClearedCombatTotal { get; set; }
        public int ClearsInCycle { get; set; }
        public long GraphEpoch { get; set; }
        public long SegmentEpoch { get; set; }
        public long RoomEpoch { get; set; }
        public InfinityPhase Phase { get; set; }
        public int LastCountedNode { get; set; } = -1;
        public bool SoulObserved { get; set; }
        public string TransitionIntent { get; set; }
        public long ChoiceRevision { get; set; }
        public long SettledGraphEpoch { get; set; } = -1;
        public long SettledSegmentEpoch { get; set; } = -1;
        public HashSet<int> ClearedNodes { get; } = new HashSet<int>();
        public int PressureStage => ValidInterval(Interval) ? (int)Math.Min(100L, ClearedCombatTotal / Interval) : 0;
        public bool BossDue => ClearsInCycle >= Interval;
        public static bool ValidInterval(int interval) => interval == 10 || interval == 15 || interval == 20;

        public bool TryCountCombatClear(long graph, int node, bool active, bool transitioning, bool revisit)
        {
            if (!active || transitioning || revisit || graph != GraphEpoch || node < 0 || node >= MaximumGraphNodes
                || !ValidInterval(Interval) || Phase != InfinityPhase.Exploring || ClearedCombatTotal == long.MaxValue
                || !ClearedNodes.Add(node)) return false;
            LastCountedNode = node;
            ClearedCombatTotal++;
            ClearsInCycle++;
            if (BossDue) Phase = InfinityPhase.BossDue;
            return true;
        }

        public bool TryEnterBoss()
        {
            if (Phase != InfinityPhase.BossDue || !BossDue) return false;
            Phase = InfinityPhase.BossFight;
            SoulObserved = false;
            return true;
        }

        public bool ObserveBossClear()
        {
            if (Phase != InfinityPhase.BossFight) return false;
            Phase = InfinityPhase.WaitingSoulFinish;
            return true;
        }

        public bool ObserveSoul(bool present, bool roomClear, bool riftUnlocked)
        {
            if (Phase != InfinityPhase.WaitingSoulFinish) return false;
            if (present) SoulObserved = true;
            if (!SoulObserved || present || !roomClear || !riftUnlocked) return false;
            Phase = InfinityPhase.AwaitingChoice;
            return true;
        }

        public bool BeginGraphTransition(string intent)
        {
            if (GraphEpoch == long.MaxValue || RoomEpoch == long.MaxValue
                || (intent != "delve" && intent != "regenerate")) return false;
            if (intent == "regenerate" && Phase != InfinityPhase.Exploring && Phase != InfinityPhase.BossDue) return false;
            if (intent == "delve" && Phase != InfinityPhase.Transitioning) return false;
            TransitionIntent = intent;
            Phase = InfinityPhase.Transitioning;
            return true;
        }

        public bool CompleteGraphTransition(long graph)
        {
            if (Phase != InfinityPhase.Transitioning || GraphEpoch == long.MaxValue || graph != GraphEpoch + 1
                || RoomEpoch == long.MaxValue) return false;
            GraphEpoch = graph;
            RoomEpoch++;
            LastCountedNode = -1;
            ClearedNodes.Clear();
            TransitionIntent = null;
            Phase = BossDue ? InfinityPhase.BossDue : InfinityPhase.Exploring;
            return true;
        }

        public InfinityRunState Clone()
        {
            var copy = new InfinityRunState {
                FixedZoneId = FixedZoneId, Interval = Interval, ClearedCombatTotal = ClearedCombatTotal,
                DifficultyId = DifficultyId,
                ClearsInCycle = ClearsInCycle, GraphEpoch = GraphEpoch, SegmentEpoch = SegmentEpoch,
                RoomEpoch = RoomEpoch, Phase = Phase, LastCountedNode = LastCountedNode, SoulObserved = SoulObserved,
                TransitionIntent = TransitionIntent, ChoiceRevision = ChoiceRevision,
                SettledGraphEpoch = SettledGraphEpoch, SettledSegmentEpoch = SettledSegmentEpoch,
            };
            foreach (int node in ClearedNodes) copy.ClearedNodes.Add(node);
            return copy;
        }
    }
}
