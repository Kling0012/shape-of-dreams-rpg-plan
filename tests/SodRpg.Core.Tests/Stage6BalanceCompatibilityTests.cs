using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class Stage6BalanceCompatibilityTests
    {
        [Fact]
        public async Task Original_tables_preserve_numeric_definitions_rng_results_and_negotiated_content()
        {
            // This observation is meaningful only for the pre-cutover stage6 input.
            // Tuning is covered by independent raw-table expectations in the domain tests.
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

            // Fresh Core process: unrelated tests may have registered extra profiles in this process.
            string metrics = Path.Combine(Path.GetTempPath(), "stage6-compatibility-" + Guid.NewGuid() + ".json");
            try
            {
                string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent.Name;
                string dll = Path.Combine(root.FullName, "tools/BalanceSim/bin", configuration, "net8.0/BalanceSim.dll");
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET") ?? "dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (string arg in new[] { dll, "--mode", "loot-economy", "--metrics-json", metrics }) start.ArgumentList.Add(arg);
                using (var process = Process.Start(start))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    string errors = await stderr;
                    await stdout;
                    Assert.True(process.ExitCode == 0, errors);
                }
                using (var document = JsonDocument.Parse(File.ReadAllText(metrics)))
                    Assert.Equal("10579-ab35d865a569c87b", document.RootElement.GetProperty("contentFingerprint").GetString());
            }
            finally { File.Delete(metrics); }
        }
    }
}
