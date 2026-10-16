using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    [Collection("Generated hero registry")]
    public sealed class Issue606StarPrerequisiteNameTests
    {
        [Theory]
        [InlineData("husk.bridge.b2.e1", true)]
        [InlineData("husk.bridge.b3.n2", true)]
        [InlineData("husk.bridge.b2.e1", false)]
        [InlineData("husk.bridge.b3.n2", false)]
        public void Missing_prerequisite_uses_the_name_shown_on_the_husk_map(string candidateId, bool japanese)
        {
            const string hero = "Hero_Husk", prerequisiteId = "h.husk.ring.insight";
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                StarClusters.RegisterGeneratedHero(hero);
                var layout = HeroTreeLayout.ForHero(hero);
                var nodes = layout.Nodes.ToList();
                var byId = nodes.Where(n => n.Talent != null).ToDictionary(n => n.Id);
                foreach (var node in byId.Values)
                {
                    var authored = node.Talent.AuthoredStar;
                    if (authored == null) continue;
                    foreach (string id in authored.RequiredStarIds.Concat(authored.RequiredAnyStarIds))
                        Assert.True(byId.ContainsKey(id), node.Id + " references an unregistered prerequisite: " + id);
                }
                var candidate = byId[candidateId].Talent;
                Assert.Contains(prerequisiteId, candidate.AuthoredStar.RequiredStarIds);
                var prerequisite = byId[prerequisiteId].Talent;

                // DreamforgeUi.RebuildStarTree and EnsureStarTooltip display the pair name for bridge stars.
                var pair = PairCombos.ForBridge(prerequisite.Id);
                string visibleName = pair != null ? pair.Name.ToString() : prerequisite.Name.ToString();
                string text = StarMapPresentation.MissingRequirements(layout,
                    nodes.IndexOf(byId[candidateId]), new HeroState(), 500, true);
                string expected = japanese ? "前提の星『" + visibleName + "』" : "Prerequisite star “" + visibleName + "”";
                Assert.True(text.Contains(expected, StringComparison.Ordinal),
                    candidateId + " (" + candidate.Name + "): " + prerequisiteId
                    + " is registered and displayed as “" + visibleName + "”, but the missing-requirements text is:\n" + text);
            }
            finally
            {
                Loc.Japanese = previous;
                StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
            }
        }
    }
}
