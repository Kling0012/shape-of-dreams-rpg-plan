using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>Extra enemies share a fixed expected reward budget; original enemies retain their full rewards.</summary>
    public static class PressureCountRewards
    {
        public const string ActorScaleKey = "DreamforgeRPG.pressureEnemyCount.rewardScale";
        private const string EventSuffix = "|pressure:";
        private const string ShardSuffix = "|shards:";
        private const int EncodedScaleLength = 16;

        public static double ScaleForBonus(double bonus) => bonus > 0 && !double.IsNaN(bonus)
            ? Math.Min(1, PressureBalance.EnemyCountAdditionalRewardBudget / bonus) : 1;

        private static double Normalize(double scale) => double.IsNaN(scale) || double.IsInfinity(scale)
            ? 1 : Math.Max(0, Math.Min(1, scale));

        public static double ScaleFromActorData(IDictionary<string, string> data)
        {
            if (data == null || !data.TryGetValue(ActorScaleKey, out var value)) return 1;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale)
                && scale >= 0 && scale <= 1 ? scale : 1;
        }

        /// <summary>
        /// The authenticated opaque event identity is already carried by replay and pending-kill saves.
        /// Host-decided per-kill facts ride as optional suffixes: thinning scale, then shard drop multiplier.
        /// </summary>
        public static string EncodeEventId(string id, double scale, double shardDropMultiplier = 1)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A kill event identity is required.", nameof(id));
            scale = Normalize(scale);
            shardDropMultiplier = NormalizeShardDrop(shardDropMultiplier);
            if (scale != 1) id += EventSuffix + EncodeDouble(scale);
            if (shardDropMultiplier != 1) id += ShardSuffix + EncodeDouble(shardDropMultiplier);
            return id;
        }

        public static double ScaleFromEventId(string id)
        {
            id = StripSuffix(id, ShardSuffix, out _);
            StripSuffix(id, EventSuffix, out double value);
            return value >= 0 && value <= 1 ? value : 1;
        }

        /// <summary>Shard drop multiplier recorded by the host when the enemy died; 1 for ids without one.</summary>
        public static double ShardDropMultiplierFromEventId(string id)
        {
            StripSuffix(id, ShardSuffix, out double value);
            return NormalizeShardDrop(value);
        }

        private static double NormalizeShardDrop(double multiplier) => double.IsNaN(multiplier) || double.IsInfinity(multiplier)
            ? 1 : Math.Max(1, Math.Min(DreamPressure.ShardDropMaximum, multiplier));

        private static string EncodeDouble(double value) =>
            unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>Removes a trailing "suffix + 16 hex digits" from the id; value is 1 when absent or malformed.</summary>
        private static string StripSuffix(string id, string suffix, out double value)
        {
            value = 1;
            if (string.IsNullOrEmpty(id)) return id;
            int marker = id.Length - suffix.Length - EncodedScaleLength;
            if (marker <= 0 || string.CompareOrdinal(id, marker, suffix, 0, suffix.Length) != 0) return id;
            ulong bits = 0;
            for (int i = marker + suffix.Length; i < id.Length; i++)
            {
                char c = id[i];
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
                if (digit < 0) return id;
                bits = (bits << 4) | (uint)digit;
            }
            value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
            return id.Substring(0, marker);
        }

        /// <summary>Unbiased integer rounding: a one-unit reward must not become a guaranteed extra unit.</summary>
        public static int ScaleInt(Rng rng, int amount, double scale)
        {
            if (amount <= 0) return 0;
            scale = Normalize(scale);
            if (scale == 1) return amount;
            double scaled = amount * scale;
            int whole = (int)scaled;
            return whole + (rng.Chance(scaled - whole) ? 1 : 0);
        }

        /// <summary>Native adapters can retain their own RNG without allocating a callback.</summary>
        public static int ScaleInt(int amount, double scale, double randomUnit)
        {
            if (amount <= 0) return 0;
            scale = Normalize(scale);
            if (scale == 1) return amount;
            double scaled = amount * scale;
            int whole = (int)scaled;
            double fraction = scaled - whole;
            return whole + (fraction > 0 && randomUnit < fraction ? 1 : 0);
        }

        public static double ScaleDouble(double amount, double scale) => amount * Normalize(scale);
        public static bool Retain(Rng rng, double scale) => rng.Chance(Normalize(scale));

        /// <summary>Thin indivisible relics and round final currencies once, before output caps or hoard deferral.</summary>
        public static void ScaleLoot(Rng rng, KillReward reward, double scale)
        {
            scale = Normalize(scale);
            if (scale == 1) return;
            reward.Shards = ScaleInt(rng, reward.Shards, scale);
            reward.Tuning = ScaleInt(rng, reward.Tuning, scale);
            reward.Xp = ScaleInt(rng, reward.Xp, scale);
            int kept = 0;
            for (int i = 0; i < reward.Relics.Count; i++)
                if (Retain(rng, scale)) reward.Relics[kept++] = reward.Relics[i];
            if (kept < reward.Relics.Count) reward.Relics.RemoveRange(kept, reward.Relics.Count - kept);
        }
    }
}
