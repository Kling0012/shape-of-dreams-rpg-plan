using System;
using System.IO;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class ForgeBalanceTests
    {
        private static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "tools", "balance", "forge.json"))) return dir.FullName;
            throw new InvalidOperationException("tools/balance/forge.json was not found above " + AppContext.BaseDirectory);
        }

        internal static (int Offset, int PerLevel, int Maximum) ReadRawFailureCoefficients()
        {
            using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "balance", "forge.json"))))
            {
                var failure = document.RootElement.GetProperty("enhanceFailure");
                return (failure.GetProperty("currentLevelOffset").GetInt32(),
                    failure.GetProperty("percentPerLevel").GetInt32(),
                    failure.GetProperty("maximumPercent").GetInt32());
            }
        }

        internal static int ExpectedFailureChance((int Offset, int PerLevel, int Maximum) coefficients, int level, int cap)
        {
            long uncapped = ((long)level - coefficients.Offset) * coefficients.PerLevel;
            return level >= cap || uncapped <= 0 ? 0
                : uncapped >= coefficients.Maximum ? coefficients.Maximum : (int)uncapped;
        }

        [Fact]
        public void Failure_chance_matches_raw_coefficients_at_every_legal_rarity_break_and_level()
        {
            var coefficients = ReadRawFailureCoefficients();
            foreach (Rarity rarity in Enum.GetValues(typeof(Rarity)))
                for (int breaks = 0; breaks <= Content.MaxLimitBreaks(rarity); breaks++)
                {
                    var relic = new Relic { Rarity = rarity, LimitBreaks = breaks };
                    int cap = Content.MaxEnhanceFor(relic);
                    for (int level = 0; level <= cap; level++)
                    {
                        relic.Enhance = level;
                        int expected = ExpectedFailureChance(coefficients, level, cap);
                        Assert.Equal(expected, Rules.EnhanceFailureChance(relic));
                    }
                }
        }
    }
}
