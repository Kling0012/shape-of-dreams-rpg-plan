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

        /// <summary>The authenticated opaque event identity is already carried by replay and pending-kill saves.</summary>
        public static string EncodeEventId(string id, double scale)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A kill event identity is required.", nameof(id));
            scale = Normalize(scale);
            return scale == 1 ? id : id + EventSuffix
                + unchecked((ulong)BitConverter.DoubleToInt64Bits(scale)).ToString("x16", CultureInfo.InvariantCulture);
        }

        public static double ScaleFromEventId(string id)
        {
            if (string.IsNullOrEmpty(id)) return 1;
            int marker = id.Length - EventSuffix.Length - EncodedScaleLength;
            if (marker <= 0 || string.CompareOrdinal(id, marker, EventSuffix, 0, EventSuffix.Length) != 0) return 1;
            ulong bits = 0;
            for (int i = marker + EventSuffix.Length; i < id.Length; i++)
            {
                char c = id[i];
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
                if (digit < 0) return 1;
                bits = (bits << 4) | (uint)digit;
            }
            double scale = BitConverter.Int64BitsToDouble(unchecked((long)bits));
            return scale >= 0 && scale <= 1 ? scale : 1;
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
