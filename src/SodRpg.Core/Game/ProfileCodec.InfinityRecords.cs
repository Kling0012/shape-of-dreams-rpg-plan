using System;
using System.Collections.Generic;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        internal static JsonObject WriteInfinityRecords(Profile profile)
        {
            if (profile.InfinityRecords.Count == 0) return null;
            if (profile.InfinityRecords.Count > InfinityRecords.MaximumConfigurations)
                throw new LedgerFormatException("Too many infinity record configurations");
            var records = new List<object>(profile.InfinityRecords.Count);
            foreach (var pair in profile.InfinityRecords)
            {
                var record = pair.Value;
                ValidateInfinityRecord(record);
                if (pair.Key != InfinityRecords.ConfigurationKey(record.FixedZoneId, record.Interval, record.DreamDepth, record.DifficultyId))
                    throw new LedgerFormatException("Invalid infinity record key");
                var row = new JsonObject().Add("fixedZone", record.FixedZoneId).Add("interval", (long)record.Interval)
                    .Add("dreamDepth", (long)record.DreamDepth).Add("difficulty", record.DifficultyId)
                    .Add("returns", record.ReturnCount).Add("bestRooms", record.BestReturnedRooms)
                    .Add("bestPressure", (long)record.PressureAtBestReturn);
                if (record.LastReturnedRooms.HasValue)
                    row.Add("lastRooms", record.LastReturnedRooms.Value).Add("lastPressure", (long)record.LastPressure.Value);
                records.Add(row);
            }
            return new JsonObject().Add("version", 1L).Add("records", records);
        }

        internal static void ReadInfinityRecords(Profile profile, JsonObject body)
        {
            if (!body.TryGet("infinityRecords", out object value) || value == null) return;
            if (!(value is JsonObject state) || InfinityLong(state, "version", 1, 1) != 1
                || !state.TryGet("records", out object rows) || !(rows is List<object> records)
                || records.Count > InfinityRecords.MaximumConfigurations)
                throw new LedgerFormatException("Invalid infinity records format");
            foreach (var valueRow in records)
            {
                if (!(valueRow is JsonObject row)) throw new LedgerFormatException("Invalid infinity record");
                if (!row.TryGet("fixedZone", out object zone) || !(zone is string zoneId)
                    || !row.TryGet("difficulty", out object difficulty) || (difficulty != null && !(difficulty is string)))
                    throw new LedgerFormatException("Invalid infinity record configuration");
                bool hasLastRooms = row.TryGet("lastRooms", out object lastRooms);
                bool hasLastPressure = row.TryGet("lastPressure", out object lastPressure);
                if (hasLastRooms != hasLastPressure || (hasLastRooms && (!(lastRooms is long) || !(lastPressure is long))))
                    throw new LedgerFormatException("Invalid infinity last return");
                var record = new InfinityRecord(zoneId, (int)InfinityLong(row, "interval", 10, 20),
                    (int)InfinityLong(row, "dreamDepth", 0, DreamDepth.Maximum), (string)difficulty,
                    InfinityLong(row, "returns", 1), InfinityLong(row, "bestRooms"), (int)InfinityLong(row, "bestPressure", 0, 100),
                    hasLastRooms ? (long?)InfinityLong(row, "lastRooms") : null,
                    hasLastPressure ? (int?)InfinityLong(row, "lastPressure", 0, 100) : null);
                ValidateInfinityRecord(record);
                string key = InfinityRecords.ConfigurationKey(record.FixedZoneId, record.Interval, record.DreamDepth, record.DifficultyId);
                if (profile.InfinityRecords.ContainsKey(key)) throw new LedgerFormatException("Duplicate infinity record configuration");
                profile.InfinityRecords.Add(key, record);
            }
            profile.InfinityRecordsRevision = unchecked(profile.InfinityRecordsRevision + 1);
        }

        private static void ValidateInfinityRecord(InfinityRecord record)
        {
            if (record == null || !InfinityRecords.ValidConfiguration(record.FixedZoneId, record.Interval, record.DreamDepth, record.DifficultyId)
                || record.ReturnCount < 1 || record.BestReturnedRooms < 0
                || record.PressureAtBestReturn != InfinityRecords.Pressure(record.BestReturnedRooms, record.Interval)
                || record.LastReturnedRooms.HasValue != record.LastPressure.HasValue
                || (record.LastReturnedRooms.HasValue && (record.LastReturnedRooms.Value < 0
                    || record.LastReturnedRooms.Value > record.BestReturnedRooms
                    || record.LastPressure.Value != InfinityRecords.Pressure(record.LastReturnedRooms.Value, record.Interval))))
                throw new LedgerFormatException("Inconsistent infinity return record");
        }
    }
}
