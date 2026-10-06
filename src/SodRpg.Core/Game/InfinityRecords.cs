using System;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>A secured-return aggregate for one fixed Infinity configuration; never a room history.</summary>
    public sealed class InfinityRecord
    {
        public string FixedZoneId { get; }
        public int Interval { get; }
        public int DreamDepth { get; }
        public string DifficultyId { get; }
        public long ReturnCount { get; }
        public long BestReturnedRooms { get; }
        public int PressureAtBestReturn { get; }
        public long? LastReturnedRooms { get; }
        public int? LastPressure { get; }

        internal InfinityRecord(string zone, int interval, int depth, string difficulty, long returns,
            long bestRooms, int bestPressure, long? lastRooms, int? lastPressure)
        {
            FixedZoneId = zone;
            Interval = interval;
            DreamDepth = depth;
            DifficultyId = difficulty;
            ReturnCount = returns;
            BestReturnedRooms = bestRooms;
            PressureAtBestReturn = bestPressure;
            LastReturnedRooms = lastRooms;
            LastPressure = lastPressure;
        }
    }

    public static class InfinityRecords
    {
        // Bound configuration cardinality, not play duration. Existing configurations remain updatable at capacity.
        public const int MaximumConfigurations = 1024;
        public const int MaximumIdentifierLength = 256;
        // Historical receipt acceptance is independent of today's game pressure cap.
        internal const int MaximumRecordedPressureStage = 100;
        internal const int MaximumRecordedDreamDepth = 5;

        public static string ConfigurationKey(string zone, int interval, int depth, string difficulty)
        {
            difficulty = string.IsNullOrEmpty(difficulty) ? null : difficulty;
            return zone.Length.ToString(CultureInfo.InvariantCulture) + ":" + zone + ":"
                + interval.ToString(CultureInfo.InvariantCulture) + ":" + depth.ToString(CultureInfo.InvariantCulture)
                + ":" + (difficulty == null ? "0:" : difficulty.Length.ToString(CultureInfo.InvariantCulture) + ":" + difficulty);
        }

        // Return receipts retain their original interval identity even after tuning changes.
        internal static bool ValidConfiguration(string zone, int interval, int depth, string difficulty)
            => ValidIdentifier(zone) && (InfinityRunState.ValidInterval(interval) || interval == 10 || interval == 15 || interval == 20)
                && depth >= 0 && depth <= MaximumRecordedDreamDepth && (difficulty == null || ValidIdentifier(difficulty));

        private static bool ValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumIdentifierLength) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return false;
            return true;
        }

        internal static int Pressure(long rooms, int interval) => (int)Math.Min((long)MaximumRecordedPressureStage, rooms / interval);

        /// <summary>Called only from the committed secured-return path, before Secure mutates or clears the run.</summary>
        public static bool RecordReturn(Profile profile, RunState run)
        {
            var infinity = run?.Infinity;
            if (profile == null || infinity == null || !ReferenceEquals(profile.Run, run)
                || !run.AwaitingChoice || infinity.Phase != InfinityPhase.AwaitingChoice
                || infinity.ChoiceRevision == long.MaxValue || infinity.ClearedCombatTotal < 0) return false;
            string difficulty = string.IsNullOrEmpty(infinity.DifficultyId) ? null : infinity.DifficultyId;
            if (!ValidConfiguration(infinity.FixedZoneId, infinity.Interval, run.DreamDepth, difficulty)) return false;
            string key = ConfigurationKey(infinity.FixedZoneId, infinity.Interval, run.DreamDepth, difficulty);
            profile.InfinityRecords.TryGetValue(key, out var previous);
            if (previous == null && profile.InfinityRecords.Count >= MaximumConfigurations) return false;
            long rooms = infinity.ClearedCombatTotal;
            int pressure = infinity.PressureStage;
            long best = previous == null ? rooms : Math.Max(previous.BestReturnedRooms, rooms);
            int bestPressure = previous == null || rooms > previous.BestReturnedRooms ? pressure : previous.PressureAtBestReturn;
            long returns = previous == null ? 1 : previous.ReturnCount == long.MaxValue ? long.MaxValue : previous.ReturnCount + 1;
            profile.InfinityRecords[key] = new InfinityRecord(infinity.FixedZoneId, infinity.Interval, run.DreamDepth,
                difficulty, returns, best, bestPressure, rooms, pressure);
            profile.InfinityRecordsRevision = unchecked(profile.InfinityRecordsRevision + 1);
            return true;
        }

        public static void CloneInto(Profile source, Profile target)
        {
            // Entries are immutable; only the settings map needs copying.
            foreach (var pair in source.InfinityRecords) target.InfinityRecords.Add(pair.Key, pair.Value);
            target.InfinityRecordsRevision = unchecked(target.InfinityRecordsRevision + 1);
        }
    }
}
