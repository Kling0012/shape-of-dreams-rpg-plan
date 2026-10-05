using System;

namespace SodRpg.Core.Game
{
    /// <summary>One host session's identity, revision sequence, and cached shared-choice payload.</summary>
    public sealed partial class RunChoicePublisher
    {
        private RunChoiceSnapshot _encodedState;
        private string _encoded;
        private int _revision;

        public ulong AuthorityGeneration { get; }

        public RunChoicePublisher()
        {
            ulong generation;
            do { generation = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); }
            while (generation == 0);
            AuthorityGeneration = generation;
        }

        public string Encode(RunState run, int selectedDepth, int zoneIndex)
        {
            TrackHistoryRun(run?.RunId);
            int depth = DreamDepth.Clamp(run?.DreamDepth ?? selectedDepth);
            int zone = run == null ? -1 : zoneIndex;
            var old = _encodedState;
            bool unchanged = old != null && old.RunId == (run?.RunId ?? "") && old.Depth == depth
                && old.ZoneIndex == zone && old.Generation == (run?.WaypointGeneration ?? 0)
                && SameInfinity(old.Infinity, run?.Infinity)
                && old.Active == (run?.ActiveWaypoint ?? Waypoint.None)
                && old.Pending == (run?.PendingWaypoint ?? Waypoint.None)
                && old.Chosen == (run?.WaypointChosen ?? false)
                && old.Settled == (run != null && run.WaypointGeneration > 0 && !run.AwaitingChoice)
                && old.Offers.Count == (run?.OfferedWaypoints.Count ?? 0);
            if (unchanged && run != null)
                for (int i = 0; i < old.Offers.Count; i++)
                    if (old.Offers[i] != run.OfferedWaypoints[i]) { unchanged = false; break; }
            if (unchanged) return _encoded;
            _encodedState = RunChoiceSnapshot.Capture(run, depth, zone, checked(++_revision), AuthorityGeneration);
            return _encoded = _encodedState.Encode();
        }

        public string EncodeFinalizedZone(RunState run, int selectedDepth, int zoneIndex)
        {
            var snapshot = RunChoiceSnapshot.Capture(run, selectedDepth, zoneIndex,
                checked(++_revision), AuthorityGeneration);
            string encoded = snapshot.Encode();
            RememberFinalized(snapshot, encoded);
            Invalidate();
            return encoded;
        }

        private static bool SameInfinity(InfinityRunState a, InfinityRunState b) =>
            a == null ? b == null : b != null && a.FixedZoneId == b.FixedZoneId && a.Interval == b.Interval
                && a.GraphEpoch == b.GraphEpoch && a.SegmentEpoch == b.SegmentEpoch && a.RoomEpoch == b.RoomEpoch
                && a.ClearedCombatTotal == b.ClearedCombatTotal && a.ClearsInCycle == b.ClearsInCycle
                && a.Phase == b.Phase && a.SoulObserved == b.SoulObserved && a.ChoiceRevision == b.ChoiceRevision
                && a.TransitionIntent == b.TransitionIntent && a.LastCountedNode == b.LastCountedNode
                && a.SettledGraphEpoch == b.SettledGraphEpoch && a.SettledSegmentEpoch == b.SettledSegmentEpoch
                && a.ClearedNodes.Count == b.ClearedNodes.Count;

        /// <summary>Resend freshly captured state without forgetting this host session's identity or sequence.</summary>
        public void Invalidate()
        {
            _encodedState = null;
            _encoded = null;
        }
    }
}
