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

        internal static JsonElement Raw()
        {
            using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "balance", "forge.json"))))
                return document.RootElement.Clone();
        }

        internal static int Number(string section, string key) => Raw().GetProperty(section).GetProperty(key).GetInt32();

        internal static int At(string section, string key, int index)
        {
            var values = Raw().GetProperty(section).GetProperty(key);
            return values[Math.Max(0, Math.Min(values.GetArrayLength() - 1, index))].GetInt32();
        }

        internal static int Milestones(int level)
        {
            int count = 0;
            foreach (var threshold in Raw().GetProperty("enhancement").GetProperty("milestones").EnumerateArray())
                if (level >= threshold.GetInt32()) count++;
            return count;
        }

        internal static (int Shards, int Tuning) RerollCost(Relic relic)
        {
            var raw = Raw().GetProperty("affixReroll");
            int rarity = ((int)relic.Rarity + 1) * (relic.Rarity >= Rarity.Epic
                ? Number("enhancement", "epicMaterialMultiplier") : 1);
            decimal growth = 1m;
            for (int i = 0; i < Math.Max(0, relic.AffixRerolls); i++)
            {
                growth *= raw.GetProperty("growthMultiplier").GetDecimal();
                if (growth >= int.MaxValue) return (raw.GetProperty("baseShards").GetInt32() == 0 ? 0 : int.MaxValue,
                    raw.GetProperty("baseTuning").GetInt32() == 0 ? 0 : int.MaxValue);
            }
            int Ceiling(decimal value) => value >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(value);
            return (Ceiling(raw.GetProperty("baseShards").GetInt32() * rarity * growth),
                Ceiling(raw.GetProperty("baseTuning").GetInt32() * rarity * growth));
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
        public void Enhancement_and_awakening_tables_preserve_boundary_clamping_and_raw_values()
        {
            for (int level = -1; level <= 21; level++)
            {
                Assert.Equal(At("enhancement", "statPercents", level), Content.EnhanceScalePct(level));
                Assert.Equal(At("enhancement", "powerPercents", level), Content.EnhancePowerScalePct(level));
                Assert.Equal(level < 0 || level >= 20 ? int.MaxValue : At("enhancement", "shardCosts", level), Content.EnhanceCost(level));
            }
            for (int level = -1; level <= 4; level++)
            {
                Assert.Equal(At("awakening", "thresholds", level), Content.AwakenThresholdFor(level));
                Assert.Equal(At("awakening", "affixPercents", level), Content.AwakenAffixPctAt(level));
                Assert.Equal(At("awakening", "powerPercents", level), Content.AwakenPowerPctAt(level));
                Assert.Equal(At("limitBreak", "shardCosts", level - 1), Content.LimitBreakShardCost(level));
                Assert.Equal(At("limitBreak", "tuningCosts", level - 1), Content.LimitBreakTuningCost(level));
            }
            foreach (Rarity rarity in Enum.GetValues(typeof(Rarity)))
            {
                int maximum = At("limitBreak", "maxByRarity", (int)rarity);
                Assert.Equal(maximum, Content.MaxLimitBreaks(rarity));
                for (int breaks = -1; breaks <= maximum + 1; breaks++)
                    Assert.Equal(Number("enhancement", "baseCap") + Math.Max(0, Math.Min(maximum, breaks))
                        * Number("enhancement", "stepPerBreak"), Content.MaxEnhanceFor(new Relic { Rarity = rarity, LimitBreaks = breaks }));
            }
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
