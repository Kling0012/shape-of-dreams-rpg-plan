using System;
using System.Globalization;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class PressureSuffixCompatibilityTests
    {
        private const string Id = "0123456789abcdef0123456789abcdef";

        [Fact]
        public void New_host_preserves_released_clients_pressure_thinning()
        {
            const double scale = 1.0 / 3;
            string encoded = PressureCountRewards.EncodeEventId(Id, scale, 1.5);
            Assert.Equal(scale, ReleasedScaleFromEventId(encoded));
            Assert.Equal(scale, PressureCountRewards.ScaleFromEventId(encoded));
            Assert.Equal(1.5, PressureCountRewards.ShardDropMultiplierFromEventId(encoded));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void New_reader_accepts_both_suffix_orders(bool pressureLast)
        {
            const double scale = 1.0 / 3;
            string pressure = "|pressure:" + Bits(scale);
            string shards = "|shards:" + Bits(1.25);
            string encoded = Id + (pressureLast ? shards + pressure : pressure + shards);
            Assert.Equal(scale, PressureCountRewards.ScaleFromEventId(encoded));
            Assert.Equal(1.25, PressureCountRewards.ShardDropMultiplierFromEventId(encoded));
        }

        private static string Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value))
            .ToString("x16", CultureInfo.InvariantCulture);

        // Frozen v2.9 decoder, copied from 571fabadf29b3961a77fcc76904e0e3b3cf6761d.
        // Keep its wire assumptions unchanged: mixed versions are allowed by Hello.
        private static double ReleasedScaleFromEventId(string id)
        {
            const string EventSuffix = "|pressure:";
            const int EncodedScaleLength = 16;
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
    }
}
