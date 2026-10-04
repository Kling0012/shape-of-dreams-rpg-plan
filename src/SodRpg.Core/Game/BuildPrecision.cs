using System;

namespace SodRpg.Core.Game
{
    /// <summary>Build percentages use thousandths until the host applies them to game floats.</summary>
    public static class BuildPrecision
    {
        public const int Scale = 1000;

        public static int FromDecimal(decimal value)
        {
            decimal scaled = value * Scale;
            if (scaled != decimal.Truncate(scaled))
                throw new ArgumentOutOfRangeException(nameof(value), "Build values must be exact thousandths.");
            return checked((int)scaled);
        }
    }
}
