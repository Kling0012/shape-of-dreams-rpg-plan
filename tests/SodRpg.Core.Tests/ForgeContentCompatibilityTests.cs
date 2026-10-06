using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class ForgeContentCompatibilityTests
    {
        [Fact]
        public async Task Legacy_forge_table_preserves_the_negotiated_content_identity()
        {
            string root = RepositoryRoot();
            string output = Path.GetTempFileName();
            try
            {
                // Isolate the production startup registry from other tests' registered cap profiles.
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                string executable = Path.Combine(root, "tools", "BalanceSim", "bin",
                    directory.Parent.Name, directory.Name, "BalanceSim.dll");
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET") ?? "dotnet")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string argument in new[] { executable, "--mode", "forge", "--metrics-json", output })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start);
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                string report = await stdout;
                string error = await stderr;
                Assert.True(process.ExitCode == 0, error + report);

                using var metrics = JsonDocument.Parse(File.ReadAllText(output));
                using var table = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "balance", "forge.json")));
                var failure = table.RootElement.GetProperty("enhanceFailure");
                bool legacy = failure.GetProperty("currentLevelOffset").GetInt32() == 2
                    && failure.GetProperty("percentPerLevel").GetInt32() == 3
                    && failure.GetProperty("maximumPercent").GetInt32() == 45;
                string identity = metrics.RootElement.GetProperty("contentFingerprint").GetString();
                // Observed from main before the cutover, with all generated heroes installed.
                const string previousIdentity = "10575-81dd841870386d98";
                if (legacy && SodRpg.Core.Game.MemoryDamageBalance.ContentFingerprintRecord == null)
                    Assert.Equal(previousIdentity, identity);
                else Assert.NotEqual(previousIdentity, identity);
            }
            finally { File.Delete(output); }
        }

        private static string RepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "tools", "balance", "forge.json"))) return directory.FullName;
            throw new InvalidOperationException("tools/balance/forge.json was not found above " + AppContext.BaseDirectory);
        }
    }
}
