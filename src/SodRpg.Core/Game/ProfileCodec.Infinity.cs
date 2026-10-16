using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        internal static JsonObject WriteInfinity(InfinityRunState state, bool protocol24Projection = false)
        {
            if (state == null) return null;
            // The legacy key identifies the current graph's zone, not the expedition's starting zone.
            long compatibleCycle = protocol24Projection && state.FinalePromoted
                ? state.Interval : state.ClearsInCycle;
            long compatibleTotal = protocol24Projection && state.FinalePromoted
                ? Math.Max(state.ClearedCombatTotal, compatibleCycle) : state.ClearedCombatTotal;
            return new JsonObject().Add("version", 1L).Add("fixedZone", state.FixedZoneId)
                .Add("interval", (long)state.Interval).Add("total", compatibleTotal)
                .Add("actualTotal", state.ClearedCombatTotal).Add("difficulty", state.DifficultyId)
                // Protocol 24 readers require total >= cycle == interval in a due phase. Snapshot
                // encoding requests that projection; profile storage keeps total/cycle as real values.
                .Add("cycle", compatibleCycle).Add("actualCycle", (long)state.ClearsInCycle)
                .Add("finalePromoted", state.FinalePromoted).Add("graph", state.GraphEpoch)
                .Add("segment", state.SegmentEpoch).Add("room", state.RoomEpoch).Add("phase", (long)state.Phase)
                .Add("lastNode", (long)state.LastCountedNode).Add("soul", state.SoulObserved)
                .Add("intent", state.TransitionIntent).Add("choice", state.ChoiceRevision)
                .Add("settledGraph", state.SettledGraphEpoch).Add("settledSegment", state.SettledSegmentEpoch)
                .Add("clearedNodes", state.ClearedNodes.OrderBy(x => x).Select(x => (object)(long)x).ToList());
        }

        internal static long InfinityLong(JsonObject obj, string key, long min = 0, long max = long.MaxValue)
        {
            if (!obj.TryGet(key, out object value) || !(value is long number) || number < min || number > max)
                throw new LedgerFormatException("Invalid infinity " + key);
            return number;
        }

        internal static InfinityRunState ReadInfinity(JsonObject parent, string key = "infinity")
        {
            if (!parent.TryGet(key, out object value) || value == null) return null;
            if (!(value is JsonObject j) || InfinityLong(j, "version", 1, 1) != 1)
                throw new LedgerFormatException("Unknown infinity state format");
            string zone = Str(j, "fixedZone");
            int interval = (int)InfinityLong(j, "interval", InfinityRunState.ShortInterval, InfinityRunState.LongInterval);
            if (string.IsNullOrEmpty(zone) || zone.Length > 256 || !InfinityRunState.ValidInterval(interval))
                throw new LedgerFormatException("Invalid infinity zone or interval");
            if (!j.TryGet("intent", out object intent) || (intent != null && !(intent is string)))
                throw new LedgerFormatException("Invalid infinity transition intent");
            if (j.TryGet("difficulty", out object difficulty) && difficulty != null
                && (!(difficulty is string difficultyName) || difficultyName.Length == 0 || difficultyName.Length > 256))
                throw new LedgerFormatException("Invalid infinity difficulty");
            bool hasFinalePromotion = j.TryGet("finalePromoted", out object promoted);
            if (hasFinalePromotion && !(promoted is bool))
                throw new LedgerFormatException("Invalid infinity finale promotion");
            bool hasActualCycle = j.TryGet("actualCycle", out object actualCycle);
            if (hasActualCycle && (!(actualCycle is long actual) || actual < 0 || actual > interval))
                throw new LedgerFormatException("Invalid infinity actual cycle");
            bool hasActualTotal = j.TryGet("actualTotal", out object actualTotal);
            if (hasActualTotal && (!(actualTotal is long realTotal) || realTotal < 0))
                throw new LedgerFormatException("Invalid infinity actual total");
            long compatibleTotal = InfinityLong(j, "total");
            int compatibleCycle = (int)InfinityLong(j, "cycle", 0, interval);
            var state = new InfinityRunState
            {
                FixedZoneId = zone, Interval = interval,
                ClearedCombatTotal = hasActualTotal ? (long)actualTotal : compatibleTotal,
                DifficultyId = Str(j, "difficulty"),
                ClearsInCycle = hasActualCycle ? (int)(long)actualCycle : compatibleCycle,
                FinalePromoted = hasFinalePromotion && (bool)promoted,
                GraphEpoch = InfinityLong(j, "graph"),
                SegmentEpoch = InfinityLong(j, "segment"), RoomEpoch = InfinityLong(j, "room"),
                Phase = (InfinityPhase)InfinityLong(j, "phase", 0, (long)InfinityPhase.Returning),
                LastCountedNode = (int)InfinityLong(j, "lastNode", -1, InfinityRunState.MaximumGraphNodes - 1),
                SoulObserved = RequiredInfinityBool(j, "soul"), ChoiceRevision = InfinityLong(j, "choice"),
                SettledGraphEpoch = InfinityLong(j, "settledGraph", -1),
                SettledSegmentEpoch = InfinityLong(j, "settledSegment", -1), TransitionIntent = Str(j, "intent"),
            };
            if (!j.TryGet("clearedNodes", out object nodesObject) || !(nodesObject is List<object> nodes)
                || nodes.Count > InfinityRunState.MaximumGraphNodes) throw new LedgerFormatException("Invalid infinity cleared nodes");
            foreach (object node in nodes)
                if (!(node is long n) || n < 0 || n >= InfinityRunState.MaximumGraphNodes || !state.ClearedNodes.Add((int)n))
                    throw new LedgerFormatException("Invalid infinity cleared node");

            bool currentCounters = hasFinalePromotion || hasActualCycle || hasActualTotal;
            if (currentCounters && (!hasFinalePromotion || !hasActualCycle || !hasActualTotal))
                throw new LedgerFormatException("Incomplete infinity counters");
            bool countersProjected = compatibleTotal != state.ClearedCombatTotal
                || compatibleCycle != state.ClearsInCycle;
            bool promotedFinale = false;
            if (currentCounters)
            {
                if (state.FinalePromoted)
                {
                    promotedFinale = (!countersProjected
                            || compatibleCycle == state.Interval
                                && compatibleTotal == Math.Max(state.ClearedCombatTotal, state.Interval))
                        && state.ClearsInCycle < state.Interval
                        && state.Phase >= InfinityPhase.BossDue && state.Phase <= InfinityPhase.Returning;
                }
                else if (countersProjected)
                    throw new LedgerFormatException("Inconsistent infinity compatible counters");
            }
            // v2.10.8-v3.0.1 filled Primus' cycle counter with the chosen 10/15/20 interval.
            // A finale graph starts after Delve reset the cycle, so its deduplicated current-graph
            // receipt is the real count. Normalize to that receipt instead of exempting the saved
            // interval: editing interval/cycle cannot leave an inconsistent counter admitted.
            else if (state.FixedZoneId == "Zone_Primus" && compatibleCycle == state.Interval
                && state.ClearedNodes.Count < state.Interval
                && state.Phase >= InfinityPhase.BossDue && state.Phase <= InfinityPhase.AwaitingChoice)
            {
                state.ClearsInCycle = state.ClearedNodes.Count;
                state.FinalePromoted = true;
                promotedFinale = true;
            }
            if (state.ClearedCombatTotal < state.ClearsInCycle
                || (state.FinalePromoted && !promotedFinale)
                || state.SettledGraphEpoch > state.GraphEpoch
                || state.SettledSegmentEpoch > state.SegmentEpoch
                || (state.TransitionIntent != null && state.TransitionIntent != "" && state.TransitionIntent != "delve"
                    && state.TransitionIntent != "regenerate" && state.TransitionIntent != "return")
                || (state.Phase == InfinityPhase.Exploring && state.BossDue)
                || ((state.Phase == InfinityPhase.BossDue || state.Phase == InfinityPhase.BossFight
                    || state.Phase == InfinityPhase.WaitingSoulFinish || state.Phase == InfinityPhase.AwaitingChoice)
                    && !state.BossDue)) throw new LedgerFormatException("Inconsistent infinity state");
            if (state.ClearedNodes.Count > state.ClearedCombatTotal
                || (state.LastCountedNode >= 0 && !state.ClearedNodes.Contains(state.LastCountedNode)))
                throw new LedgerFormatException("Missing infinity last node");
            return state;
        }

        private static bool RequiredInfinityBool(JsonObject obj, string key)
        {
            if (!obj.TryGet(key, out object value) || !(value is bool flag)) throw new LedgerFormatException("Invalid infinity " + key);
            return flag;
        }

        private static void ReadInfinitySettings(Profile profile, JsonObject body)
        {
            if (body.TryGet("lastInfinityEnabled", out object enabled))
            {
                if (!(enabled is bool flag)) throw new LedgerFormatException("Invalid infinity enabled setting");
                profile.LastInfinityEnabled = flag;
            }
            if (body.TryGet("lastInfinityInterval", out object interval))
            {
                if (!(interval is long n) || n > int.MaxValue || n < 0 || !InfinityRunState.ValidInterval((int)n))
                    throw new LedgerFormatException("Invalid infinity interval setting");
                profile.LastInfinityInterval = (int)n;
            }
        }
    }
}
