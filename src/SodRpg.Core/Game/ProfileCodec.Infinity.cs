using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        internal static JsonObject WriteInfinity(InfinityRunState state)
        {
            if (state == null) return null;
            return new JsonObject().Add("version", 1L).Add("fixedZone", state.FixedZoneId)
                .Add("interval", (long)state.Interval).Add("total", state.ClearedCombatTotal)
                .Add("cycle", (long)state.ClearsInCycle).Add("graph", state.GraphEpoch)
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
            int interval = (int)InfinityLong(j, "interval", 10, 20);
            if (string.IsNullOrEmpty(zone) || zone.Length > 256 || !InfinityRunState.ValidInterval(interval))
                throw new LedgerFormatException("Invalid infinity zone or interval");
            if (!j.TryGet("intent", out object intent) || (intent != null && !(intent is string)))
                throw new LedgerFormatException("Invalid infinity transition intent");
            var state = new InfinityRunState
            {
                FixedZoneId = zone, Interval = interval, ClearedCombatTotal = InfinityLong(j, "total"),
                ClearsInCycle = (int)InfinityLong(j, "cycle", 0, interval), GraphEpoch = InfinityLong(j, "graph"),
                SegmentEpoch = InfinityLong(j, "segment"), RoomEpoch = InfinityLong(j, "room"),
                Phase = (InfinityPhase)InfinityLong(j, "phase", 0, (long)InfinityPhase.Returning),
                LastCountedNode = (int)InfinityLong(j, "lastNode", -1, InfinityRunState.MaximumGraphNodes - 1),
                SoulObserved = RequiredInfinityBool(j, "soul"), ChoiceRevision = InfinityLong(j, "choice"),
                SettledGraphEpoch = InfinityLong(j, "settledGraph", -1),
                SettledSegmentEpoch = InfinityLong(j, "settledSegment", -1), TransitionIntent = Str(j, "intent"),
            };
            if (state.ClearedCombatTotal < state.ClearsInCycle || state.SettledGraphEpoch > state.GraphEpoch
                || state.SettledSegmentEpoch > state.SegmentEpoch
                || (state.TransitionIntent != null && state.TransitionIntent != "" && state.TransitionIntent != "delve"
                    && state.TransitionIntent != "regenerate" && state.TransitionIntent != "return")
                || (state.Phase == InfinityPhase.Exploring && state.BossDue)
                || ((state.Phase == InfinityPhase.BossDue || state.Phase == InfinityPhase.BossFight
                    || state.Phase == InfinityPhase.WaitingSoulFinish || state.Phase == InfinityPhase.AwaitingChoice)
                    && !state.BossDue)) throw new LedgerFormatException("Inconsistent infinity state");
            if (!j.TryGet("clearedNodes", out object nodesObject) || !(nodesObject is List<object> nodes)
                || nodes.Count > InfinityRunState.MaximumGraphNodes) throw new LedgerFormatException("Invalid infinity cleared nodes");
            foreach (object node in nodes)
                if (!(node is long n) || n < 0 || n >= InfinityRunState.MaximumGraphNodes || !state.ClearedNodes.Add((int)n))
                    throw new LedgerFormatException("Invalid infinity cleared node");
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
