using System;
using System.IO;

namespace SodRpg.Core.Game
{
    /// <summary>Fixed-width typed postordinary metadata shared by legacy and authored payload transport.</summary>
    internal static class GimmickRawCodec
    {
        internal const int MaxBytes = 49;
        internal const decimal MaximumValue = decimal.MaxValue;
        internal const long MaximumParameterUnits = long.MaxValue;
        private static long MaximumExtraTargets(GimmickDef def) => int.MaxValue
            - (def.Effect == GimmickEffect.Rampart ? 5L : Math.Max(1, Math.Min(2, def.Arg)));
        internal static bool HasRaw(GimmickDef def) => def.UncappedValue.HasValue || def.UncappedDurationUnits.HasValue
            || def.UncappedRadiusUnits.HasValue || def.UncappedExtraTargets.HasValue || def.UncappedChanceUnits.HasValue;
        internal static bool NeedsExactValue(GimmickDef def)
        {
            if (!def.UncappedValue.HasValue) return false;
            decimal units = Math.Min(def.UncappedValue.Value, Gimmicks.Cap(def.Effect)) * Gimmicks.PreciseValueScale;
            return units != decimal.Truncate(units);
        }

        internal static bool Valid(GimmickDef def) => (!def.UncappedValue.HasValue || def.UncappedValue > 0 && def.UncappedValue <= MaximumValue)
            && ValidUnits(def.UncappedDurationUnits, MaximumParameterUnits) && ValidUnits(def.UncappedRadiusUnits, MaximumParameterUnits)
            && ValidUnits(def.UncappedChanceUnits, MaximumParameterUnits) && ValidUnits(def.UncappedExtraTargets, MaximumExtraTargets(def));
        private static bool ValidUnits(long? value, long maximum) => !value.HasValue || value >= 0 && value <= maximum;
        internal static void Validate(GimmickDef def)
        {
            if (def == null || !Valid(def)) throw new InvalidOperationException("Invalid postordinary gimmick metadata.");
        }
        internal static void Write(BinaryWriter writer, GimmickDef def, bool valueOnly = false)
        {
            Validate(def);
            byte mask = (byte)((def.UncappedValue.HasValue ? 1 : 0) | (!valueOnly && def.UncappedDurationUnits.HasValue ? 2 : 0)
                | (!valueOnly && def.UncappedRadiusUnits.HasValue ? 4 : 0) | (!valueOnly && def.UncappedExtraTargets.HasValue ? 8 : 0)
                | (!valueOnly && def.UncappedChanceUnits.HasValue ? 16 : 0));
            writer.Write(mask);
            if (def.UncappedValue.HasValue) writer.Write(def.UncappedValue.Value);
            if ((mask & 2) != 0) writer.Write(def.UncappedDurationUnits.Value);
            if ((mask & 4) != 0) writer.Write(def.UncappedRadiusUnits.Value);
            if ((mask & 8) != 0) writer.Write(def.UncappedExtraTargets.Value);
            if ((mask & 16) != 0) writer.Write(def.UncappedChanceUnits.Value);
        }
        internal static void Read(BinaryReader reader, GimmickDef def)
        {
            byte mask = reader.ReadByte();
            if ((mask & ~31) != 0) throw new FormatException("Unknown postordinary gimmick fields.");
            decimal? value = (mask & 1) == 0 ? (decimal?)null : reader.ReadDecimal();
            long? duration = (mask & 2) == 0 ? (long?)null : reader.ReadInt64();
            long? radius = (mask & 4) == 0 ? (long?)null : reader.ReadInt64();
            long? targets = (mask & 8) == 0 ? (long?)null : reader.ReadInt64();
            long? chance = (mask & 16) == 0 ? (long?)null : reader.ReadInt64();
            if (value.HasValue && (value <= 0 || value > MaximumValue) || !ValidUnits(duration, MaximumParameterUnits)
                || !ValidUnits(radius, MaximumParameterUnits) || !ValidUnits(targets, MaximumExtraTargets(def))
                || !ValidUnits(chance, MaximumParameterUnits)) throw new FormatException("Invalid postordinary gimmick fields.");
            def.UncappedValue = value; def.UncappedDurationUnits = duration; def.UncappedRadiusUnits = radius;
            def.UncappedExtraTargets = targets; def.UncappedChanceUnits = chance;
            if (value.HasValue) def.EffectiveValue = Math.Min(value.Value, Gimmicks.Cap(def.Effect));
        }
    }
}
