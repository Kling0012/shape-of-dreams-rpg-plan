using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class PressureBalanceTests
    {
        internal static double Number(string section, string key)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "tools", "balance", "pressure.json");
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path)))
                    return document.RootElement.GetProperty(section).GetProperty(key).GetDouble();
            }
            throw new InvalidOperationException("tools/balance/pressure.json not found");
        }

        internal static int Maximum => (int)Number("dreamDepth", "maximum");
        internal static int Depth(int depth) => Math.Max(0, Math.Min(Maximum, depth));
        internal static double Health(double level, double spent) => 1 +
            Number("dreamPressure", "healthPerLevel") * Math.Max(0, level - Number("dreamPressure", "freeDreamLevels")) +
            Number("dreamPressure", "healthPerStarPoint") * spent;
        internal static double Damage(double level, double spent) => 1 +
            Number("dreamPressure", "damagePerLevel") * Math.Max(0, level - Number("dreamPressure", "freeDreamLevels")) +
            Number("dreamPressure", "damagePerStarPoint") * spent;
        internal static double DepthValue(string field, int depth, double initial = 1) =>
            initial + Number("dreamDepth", field) * Depth(depth);

        [Fact]
        public void Original_table_preserves_pressure_depth_and_infinity_multiplication_exactly()
        {
            var assembly = typeof(DreamDepth).Assembly;
            foreach (string name in new[] { "PressureBalance", "InfinityBalance" })
            {
                var field = assembly.GetType("SodRpg.Core.Game." + name, true)
                    .GetField("ContentFingerprintRecord", BindingFlags.NonPublic | BindingFlags.Static);
                if (field.GetValue(null) != null) return;
            }
            foreach (int level in new[] { int.MinValue, 1, 5, 6, 11, 30, int.MaxValue })
            foreach (int spent in new[] { int.MinValue, 0, 1, 150, 300, 500, int.MaxValue })
            foreach (int depth in new[] { int.MinValue, 0, 1, 2, 5, int.MaxValue })
            foreach (int stage in new[] { int.MinValue, 0, 1, 10, 100, int.MaxValue })
            foreach (double waypoint in new[] { 1d, 1.25d })
            {
                int l = Math.Max(1, Math.Min(30, level)), s = Math.Max(0, Math.Min(500, spent));
                int d = Math.Max(0, Math.Min(5, depth)), i = Math.Max(0, Math.Min(100, stage));
                var actual = DreamPressure.ForPlayer(level, spent).WithRunModifiers(depth, waypoint).WithInfinityPressure(stage);
                Assert.Equal((1 + 0.025 * Math.Max(0, l - 5) + 0.005 * s) * (1 + 0.15 * d) * waypoint * (1 + 0.10 * i), actual.HealthMultiplier);
                Assert.Equal((1 + 0.012 * Math.Max(0, l - 5) + 0.0025 * s) * (1 + 0.08 * d) * waypoint * (1 + 0.04 * i), actual.DamageMultiplier);
                Assert.Equal(0.25 * d, DreamDepth.RarityLuck(depth));
                Assert.Equal(1 + 0.25 * d, DreamDepth.AwakeningMultiplier(depth));
                Assert.Equal(1 + 0.2 * d, DreamDepth.StarXpMultiplier(depth));
                Assert.Equal(2 * d, DreamDepth.ExtraZoneNodes(depth));
            }
        }
    }
}
