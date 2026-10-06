using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MemoryDamageBalanceTests
    {
        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "tools", "balance", "stars.json"))) return dir.FullName;
            throw new InvalidOperationException("tools/balance/stars.json was not found");
        }

        [Fact]
        public void Generated_and_registered_memory_links_match_the_raw_table_without_duplicate_migrations()
        {
            string root = Root();
            using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "balance", "stars.json")));
            var effects = table.RootElement.GetProperty("effects");
            var multipliers = table.RootElement.GetProperty("multipliers");
            decimal Effective(string key, string hero, bool shared)
            {
                decimal value = effects.GetProperty(key).GetDecimal();
                if (multipliers.TryGetProperty("byKind", out var kinds) && kinds.TryGetProperty("MemoryDamage", out var kind))
                    value *= kind.GetDecimal();
                if (!shared && multipliers.TryGetProperty("byHero", out var heroes) && heroes.TryGetProperty(hero, out var owner)
                    && owner.TryGetProperty("MemoryDamage", out var multiplier)) value *= multiplier.GetDecimal();
                return value;
            }
            var legacy = HeroStarRoutes.All.Where(t => t.LinkPerRank?.Kind == LinkKind.MemoryDamage).ToArray();
            foreach (var node in legacy)
                Assert.Equal(Effective(node.Id + "/link/value", node.HeroKey, false), node.LinkPerRank.Value);

            using var outer = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "star-manifest", "outer.json")));
            foreach (string hero in StarClusters.GeneratedHeroes)
            {
                string name = hero.Substring("Hero_".Length).ToLowerInvariant();
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "star-manifest", name + ".json")));
                var expected = new Dictionary<string, decimal>(StringComparer.Ordinal);
                foreach (var node in legacy.Where(t => t.HeroKey == hero)) expected.Add(node.Id, node.LinkPerRank.Value);
                void Read(JsonElement row, bool shared)
                {
                    string id = row.GetProperty("id").GetString();
                    expected.Remove(id); // A migration replaces the old effect, even with a different kind.
                    void Effect(JsonElement effect, string path)
                    {
                        if (effect.GetProperty("kind").GetString() == "MemoryDamage")
                            expected.Add(path, Effective(effect.GetProperty("valueRef").GetString(), hero, shared));
                        if (effect.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                        {
                            int index = 0;
                            foreach (var option in options.EnumerateArray()) Effect(option, path + "/options/" + index++);
                        }
                    }
                    Effect(row, id);
                }
                foreach (var row in manifest.RootElement.GetProperty("stars").EnumerateArray()) Read(row, false);
                foreach (var row in outer.RootElement.GetProperty("stars").EnumerateArray()) Read(row, true);
                var tree = StarClusters.GenerateAuthored(StarClusters.CreateGeneratedAuthored(hero), HeroSigils.BaselineTreeFor(hero)).TreeFor(hero);
                var actual = new Dictionary<string, decimal>(StringComparer.Ordinal);
                void Capture(TalentDef node, string path)
                {
                    if (node.LinkPerRank?.Kind == LinkKind.MemoryDamage) actual.Add(path, node.LinkPerRank.Value);
                    for (int i = 0; i < node.Choices.Count; i++) Capture(node.Choices[i], path + "/options/" + i);
                }
                foreach (var node in tree) Capture(node, node.Id);
                Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
                foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
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
