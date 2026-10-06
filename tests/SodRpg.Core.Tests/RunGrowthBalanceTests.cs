using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class RunGrowthBalanceTests
    {
        [Fact]
        public void Compiled_entry_and_choice_quantities_match_the_independent_growth_original()
        {
            string path = null;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "tools", "balance", "run-growth.json");
                if (File.Exists(candidate)) { path = candidate; break; }
            }
            Assert.NotNull(path);
            var expected = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            using (var document = JsonDocument.Parse(File.ReadAllText(path)))
                foreach (var field in document.RootElement.GetProperty("effects").EnumerateObject())
                    expected.Add(field.Name, field.Value.GetDecimal());
            var actual = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            void Collect(string prefix, ClusterStarDef effect)
            {
                if (effect.RunGrowth != null)
                {
                    var growth = effect.RunGrowth;
                    actual.Add(prefix + "growth/threshold", growth.Threshold);
                    actual.Add(prefix + "growth/cap", growth.Cap);
                    for (int i = 0; i < growth.Effects.Count; i++)
                        actual.Add(prefix + "growth/effects/" + i + "/amount", growth.Effects[i].AmountMilli / 1000m);
                }
                if (effect.RunGrowthModifier != null)
                {
                    actual.Add(prefix + "growth/capBonus", effect.RunGrowthModifier.CapBonus);
                    actual.Add(prefix + "growth/effectPct", effect.RunGrowthModifier.EffectPercent);
                }
            }
            foreach (string hero in new[] { "Hero_Cetus", "Hero_Husk", "Hero_Mist", "Hero_Vesper" })
                foreach (var star in StarClusters.CreateGeneratedAuthored(hero))
                {
                    Collect(star.LocalStarId + "/", star.Effect);
                    if (star.Effect.Options != null)
                        for (int i = 0; i < star.Effect.Options.Count; i++)
                            Collect(star.LocalStarId + "/options/" + i + "/", star.Effect.Options[i]);
                }
            Assert.Equal(expected.ToArray(), actual.ToArray());
        }
    }
}
