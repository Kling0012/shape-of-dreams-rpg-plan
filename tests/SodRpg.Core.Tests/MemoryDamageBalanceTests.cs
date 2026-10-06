using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    [Collection("Generated hero registry")]
    public sealed class MemoryDamageBalanceTests
    {
        [Fact]
        public void Every_private_memory_damage_effect_reaches_floor_at_its_earliest_conservative_purchase()
        {
            StarClusters.RegisterAllGenerated();
            foreach (string hero in StarClusters.GeneratedHeroes)
            {
                foreach (var parent in HeroSigils.TreeFor(hero))
                {
                    if (parent.AuthoredStar?.LocalStarId.StartsWith("outer.", StringComparison.Ordinal) == true)
                        continue;
                    foreach (var effect in parent.IsChoice ? parent.Choices : new[] { parent })
                    {
                        if (effect.LinkPerRank?.Kind != LinkKind.MemoryDamage) continue;
                        decimal minimumDamage = StarDamageScaling.ScaleMilli(effect.LinkPerRank.ValueMilli, parent.RankCost) / 1000m;
                        Assert.True(minimumDamage >= 2.5m * parent.RankCost,
                            hero + "/" + parent.Id + ": below 2.5% per point at its minimum purchase cost");
                    }
                }
            }
        }

        [Fact]
        public void Unadjusted_content_keeps_the_pre_migration_fingerprint_in_an_isolated_process()
        {
            string output = Path.Combine(Path.GetTempPath(), "i149-fingerprint-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET") ?? "dotnet")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(typeof(BalanceSim.Options).Assembly.Location);
                start.ArgumentList.Add("--mode"); start.ArgumentList.Add("forge");
                start.ArgumentList.Add("--metrics-json"); start.ArgumentList.Add(output);
                using var process = Process.Start(start);
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, stdout + stderr);
                using var metrics = JsonDocument.Parse(File.ReadAllText(output));
                string fingerprint = metrics.RootElement.GetProperty("contentFingerprint").GetString();
                // Frozen migration compatibility, not a newly pinned identity after a balance edit.
                if (MemoryDamageBalance.ContentFingerprintRecord == null && ForgeBalance.ContentFingerprintRecord == null)
                    Assert.Equal("10575-81dd841870386d98", fingerprint);
                else
                    Assert.NotEqual("10575-81dd841870386d98", fingerprint);
            }
            finally { File.Delete(output); }
        }
    }
}
