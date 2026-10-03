using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>Only the host's shared run rules travel here; personal loot and pact choices stay local.</summary>
    public sealed class RunChoiceSnapshot
    {
        public string RunId { get; set; } = "";
        public int Depth { get; set; }
        public int ZoneIndex { get; set; } = -1;
        public int Generation { get; set; }
        public int Revision { get; set; }
        public Waypoint Active { get; set; }
        public Waypoint Pending { get; set; }
        public bool Chosen { get; set; }
        public bool Settled { get; set; }
        public List<Waypoint> Offers { get; } = new List<Waypoint>();

        public static RunChoiceSnapshot Capture(RunState run, int selectedDepth, int zoneIndex, int revision)
        {
            var snapshot = new RunChoiceSnapshot
            {
                RunId = run?.RunId ?? "", Depth = DreamDepth.Clamp(run?.DreamDepth ?? selectedDepth),
                ZoneIndex = run == null ? -1 : zoneIndex, Revision = Math.Max(0, revision),
                Generation = run?.WaypointGeneration ?? 0, Active = run?.ActiveWaypoint ?? Waypoint.None,
                Pending = run?.PendingWaypoint ?? Waypoint.None, Chosen = run?.WaypointChosen ?? false,
                Settled = run != null && run.WaypointGeneration > 0 && !run.AwaitingChoice,
            };
            if (run != null) snapshot.Offers.AddRange(run.OfferedWaypoints);
            return snapshot;
        }

        public string Encode()
        {
            var text = new StringBuilder(128);
            text.Append("1|").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(RunId ?? "")))
                .Append('|').Append(DreamDepth.Clamp(Depth).ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(ZoneIndex.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(Generation.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(Revision.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(((int)Active).ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(((int)Pending).ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(Chosen ? '1' : '0').Append('|').Append(Settled ? '1' : '0').Append('|');
            for (int i = 0; i < Offers.Count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(((int)Offers[i]).ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        public static bool TryDecode(string encoded, out RunChoiceSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(encoded) || encoded.Length > 2048) return false;
            var parts = encoded.Split('|');
            if (parts.Length != 11 || parts[0] != "1") return false;
            var numbers = new int[9];
            for (int i = 2; i < 10; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i - 2])) return false;
            if (numbers[1] < -1 || numbers[2] < 0 || numbers[3] < 0
                || !ValidWaypoint(numbers[4], true) || !ValidWaypoint(numbers[5], true)
                || (numbers[6] != 0 && numbers[6] != 1) || (numbers[7] != 0 && numbers[7] != 1)) return false;
            string runId;
            try { runId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])); }
            catch (FormatException) { return false; }
            if (runId.Length > 256) return false;
            var result = new RunChoiceSnapshot
            {
                RunId = runId, Depth = DreamDepth.Clamp(numbers[0]), ZoneIndex = numbers[1],
                Generation = numbers[2], Revision = numbers[3], Active = (Waypoint)numbers[4], Pending = (Waypoint)numbers[5],
                Chosen = numbers[6] == 1, Settled = numbers[7] == 1,
            };
            if (parts[10].Length > 0)
            {
                var offers = parts[10].Split(',');
                if (offers.Length > 3) return false;
                foreach (var offer in offers)
                {
                    if (!int.TryParse(offer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                        || !ValidWaypoint(id, false) || result.Offers.Contains((Waypoint)id)) return false;
                    result.Offers.Add((Waypoint)id);
                }
            }
            if (result.Chosen && result.Pending != Waypoint.None && !result.Settled && !result.Offers.Contains(result.Pending)) return false;
            snapshot = result;
            return true;
        }

        private static bool ValidWaypoint(int id, bool allowNone) =>
            (allowNone && id == 0) || Waypoints.Get((Waypoint)id) != null;

        /// <summary>Zone identity prevents a delayed packet from reviving the previous zone's rule.</summary>
        public bool AppliesTo(RunState run, int zoneIndex) =>
            run != null && !string.IsNullOrEmpty(RunId) && RunId == run.RunId && ZoneIndex == zoneIndex;

        public bool IsNewerThan(RunChoiceSnapshot previous) => previous == null
            || RunId != previous.RunId || Revision > previous.Revision;

        public bool ApplyTo(RunState run, int zoneIndex)
        {
            if (!AppliesTo(run, zoneIndex)) return false;
            if (Generation > run.WaypointGeneration) Waypoints.Expire(run);
            run.DreamDepth = DreamDepth.Clamp(Depth);
            run.WaypointGeneration = Generation;
            run.ActiveWaypoint = Active;
            // A client's personal secure/delve can finish after the host's. Preserve the shared selection through it.
            run.PendingWaypoint = Settled ? Active : Pending;
            run.WaypointChosen = Settled || Chosen;
            run.OfferedWaypoints.Clear();
            run.OfferedWaypoints.AddRange(Offers);
            return true;
        }
    }
}
