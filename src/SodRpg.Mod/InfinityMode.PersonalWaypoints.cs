using System;
using System.Collections.Generic;
using Mirror;
using Newtonsoft.Json;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal static partial class InfinityMode
    {
        private const string PersonalWaypointsKey = "dreamforge.infinity.personal-waypoints";
        private sealed class PersonalWaypointReceipt
        {
            public string RunId;
            public long SegmentEpoch;
            public List<InfinityPersonalChoice> Choices;
        }

        private static string _personalWaypointText;
        private static PersonalWaypointReceipt _personalWaypointReceipt;
        private static readonly Dictionary<string, Waypoints.Totals> PersonalWaypointTotals =
            new Dictionary<string, Waypoints.Totals>(StringComparer.Ordinal);
        private static int _personalWaypointVersion;

        internal static int PersonalWaypointVersion
        {
            get { ReadPersonalWaypoints(); return _personalWaypointVersion; }
        }

        internal static void CommitPersonalWaypoints(InfinityChoice choice, RunState run)
        {
            if (!NetworkServer.active || choice == null || choice.Boundary || choice.Secure
                || run?.Infinity == null || run.RunId != choice.RunId || run.Infinity.ChoiceRevision != choice.Revision) return;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null) return;
            settings.customData[PersonalWaypointsKey] = JsonConvert.SerializeObject(new PersonalWaypointReceipt
            {
                RunId = run.RunId, SegmentEpoch = run.Infinity.SegmentEpoch, Choices = choice.PersonalChoices,
            });
            ReadPersonalWaypoints();
        }

        private static void ReadPersonalWaypoints()
        {
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            string text = null;
            settings?.customData.TryGetValue(PersonalWaypointsKey, out text);
            if (text == _personalWaypointText) return;
            _personalWaypointText = text;
            _personalWaypointReceipt = null;
            PersonalWaypointTotals.Clear();
            _personalWaypointVersion++;
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                _personalWaypointReceipt = JsonConvert.DeserializeObject<PersonalWaypointReceipt>(text);
                if (_personalWaypointReceipt?.Choices == null) return;
                foreach (var selection in _personalWaypointReceipt.Choices)
                    if (selection != null && !string.IsNullOrEmpty(selection.PlayerId))
                        PersonalWaypointTotals[selection.PlayerId] = Waypoints.Sum(selection.Skipped ? Waypoint.None : selection.Waypoint);
            }
            catch (JsonException ex) { WarnPersonalChoices("unreadable personal waypoint receipt: " + ex.Message); }
        }

        internal static Waypoints.Totals WaypointTotalsForPlayer(string playerId)
        {
            ReadPersonalWaypoints();
            var run = ClientSession.HostRun;
            if (run?.Infinity == null) return Waypoints.Sum(run?.ActiveWaypoint ?? Waypoint.None);
            if (_personalWaypointReceipt != null && _personalWaypointReceipt.RunId == run.RunId
                && _personalWaypointReceipt.SegmentEpoch == run.Infinity.SegmentEpoch
                && playerId != null && PersonalWaypointTotals.TryGetValue(playerId, out var totals)) return totals;
            // A missing guest receipt is no personal waypoint, never a copy of the host's.
            return Waypoints.Sum(playerId == DewPlayer.local?.guid ? run.ActiveWaypoint : Waypoint.None);
        }

        internal static void ResetPersonalWaypoints(bool clearSettings)
        {
            _personalWaypointText = null; _personalWaypointReceipt = null;
            PersonalWaypointTotals.Clear(); _personalWaypointVersion++;
            if (clearSettings && NetworkServer.active)
                NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.Remove(PersonalWaypointsKey);
        }
    }
}
