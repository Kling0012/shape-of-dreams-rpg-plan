using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class StarMapOptimizeCommandTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "starmap-optimize-" + Guid.NewGuid().ToString("N"));
        private string OutputPath => Path.Combine(directory, "placements.cs");

        public StarMapOptimizeCommandTests() => Directory.CreateDirectory(directory);

        public void Dispose() => Directory.Delete(directory, true);

        private (int Code, string Error) Run(params string[] args)
        {
            // Exercise the real entry point without spending RegisterAllGenerated's one-shot flag in the test process.
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "StarMapRender.dll"));
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("The zero-iteration StarMapRender command did not finish.");
            }
            output.GetAwaiter().GetResult();
            return (process.ExitCode, error.GetAwaiter().GetResult());
        }

        [Fact]
        public void Partial_regeneration_refuses_to_replace_an_existing_table()
        {
            var original = Encoding.UTF8.GetBytes("// Keep all other heroes, including their exact bytes.\r\ncase \"Hero_Aurena\":\r\ncase \"Hero_Cetus\":\r\n");
            File.WriteAllBytes(OutputPath, original);
            var result = Run("--optimize", OutputPath, "--hero", "Cetus", "--iterations", "0", "--quiet");
            Assert.Equal(1, result.Code);
            Assert.Equal(original, File.ReadAllBytes(OutputPath));
            Assert.Contains("existing", result.Error);
            Assert.Contains("--hero", result.Error);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Unknown_hero_filter_never_creates_or_replaces_a_table(bool existing)
        {
            if (existing) File.WriteAllText(OutputPath, "keep this table");
            var result = Run("--optimize", OutputPath, "--hero", "not-a-hero", "--iterations", "0", "--quiet");
            Assert.Equal(1, result.Code);
            Assert.Contains("matched no", result.Error);
            if (existing) Assert.Equal("keep this table", File.ReadAllText(OutputPath));
            else Assert.False(File.Exists(OutputPath));
        }

        [Fact]
        public void Partial_regeneration_can_write_a_new_scratch_table()
        {
            var result = Run("--optimize", OutputPath, "--hero", "Cetus", "--iterations", "0", "--quiet");
            Assert.Equal(0, result.Code);
            var cases = File.ReadLines(OutputPath).Where(line => line.TrimStart().StartsWith("case ")).ToArray();
            Assert.Single(cases);
            Assert.Contains("\"Hero_Cetus\"", cases[0]);
        }

        [Fact]
        public void Partial_metrics_only_run_does_not_write_a_table()
        {
            var result = Run("--optimize", "-", "--hero", "Cetus", "--iterations", "0", "--quiet");
            Assert.Equal(0, result.Code);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [Fact]
        public void Full_regeneration_still_replaces_an_existing_table_with_every_hero()
        {
            File.WriteAllText(OutputPath, "old table");
            var result = Run("--optimize", OutputPath, "--iterations", "0", "--quiet");
            Assert.Equal(0, result.Code);
            var cases = File.ReadLines(OutputPath).Where(line => line.TrimStart().StartsWith("case ")).ToArray();
            Assert.Equal(StarClusters.GeneratedHeroes.Count, cases.Length);
            foreach (string hero in StarClusters.GeneratedHeroes)
                Assert.Contains(cases, line => line.Contains("\"" + hero + "\""));
        }
    }
}
