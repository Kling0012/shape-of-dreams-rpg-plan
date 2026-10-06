using System;
using System.Diagnostics;
using System.Collections.Generic;
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

    }
}
