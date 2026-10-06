using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class Stage6BalanceCompatibilityTests
    {
        [Fact]
        public void Original_tables_preserve_numeric_definitions_and_rng_results()
        {
            // This observation covers stage6 definitions and RNG results, not later tuning in other domains.
            // ContentFingerprintTests covers negotiated content; a frozen whole-game fingerprint is not a stage6 oracle.
            if (LootBalance.ContentFingerprintRecord != null || EconomyBalance.ContentFingerprintRecord != null
                || PactBalance.ContentFingerprintRecord != null || DailyDreamBalance.ContentFingerprintRecord != null
                || WaypointBalance.ContentFingerprintRecord != null || EventsBalance.ContentFingerprintRecord != null
                || BossSets.NormalDropPercent != 10 || BossSets.NightmareBonusPercent != 5
                || BossSets.DepthBonusPercent != 1 || BossSets.MaxDropDepth != 5 || BossSets.MaxDropPercent != 20) return;
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "SodRpg.sln"))) root = root.Parent;
            Assert.NotNull(root);
            string baseline = Path.Combine(root.FullName, "tests/SodRpg.Core.Tests/Stage6OriginalValues.json");
            var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baseline));
            Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(), Stage6CompatibilitySnapshot.Capture().ToArray());
        }
    }
}
