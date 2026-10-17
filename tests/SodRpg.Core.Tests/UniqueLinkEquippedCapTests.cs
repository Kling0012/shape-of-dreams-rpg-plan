using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    public class UniqueLinkEquippedCapTests
    {
        private readonly ITestOutputHelper output;

        public UniqueLinkEquippedCapTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void Non_boss_unique_link_values_fit_the_equipped_base_cap()
        {
            var uniques = Content.Uniques.Where(u => u.Link != null && u.Link.Kind != LinkKind.BossReward).ToArray();
            Assert.NotEmpty(uniques);
            var violations = new List<string>();
            foreach (var unique in uniques)
            {
                var link = unique.Link;
                decimal cap = Links.EquippedCap(link.Kind, link.Requires.Length);
                if (link.Value > cap)
                    violations.Add($"{unique.Id}: {link.Kind}, requirements={link.Requires.Length}, value={link.Value}, cap={cap}");
            }
            output.WriteLine($"Checked {uniques.Length} non-BossReward unique links; {violations.Count} exceed the equipped base cap.");
            Assert.True(violations.Count == 0, string.Join("\n", violations));
        }

        [Theory]
        [InlineData("unique.link_husk_wind_scar", 0)]
        [InlineData("unique.link_husk_wind_scar", 3)]
        [InlineData("unique.link_vesper_resolve_mercy", 0)]
        [InlineData("unique.link_vesper_resolve_mercy", 3)]
        [InlineData("unique.link_lacerta_powder_tap", 0)]
        [InlineData("unique.link_lacerta_powder_tap", 3)]
        [InlineData("unique.link_husk_one_flash", 0)]
        [InlineData("unique.link_husk_one_flash", 3)]
        public void Single_unique_link_delivers_its_authored_value_after_awakening(string uniqueId, int level)
        {
            var unique = Content.Uniques.Single(u => u.Id == uniqueId);
            string hero = unique.Link.Requires.FirstOrDefault(Links.IsTraveler) ?? "Hero_Husk";
            var relic = Loot.RollUnique(new Rng(702), unique, 1);
            relic.AwakenLevel = level;
            var profile = Profile.CreateNew(702);
            profile.Stash.Add(relic);
            Rules.Equip(profile, hero, relic.Uid);
            Assert.Equal(relic.Uid, profile.Hero(hero).Equipped[(int)relic.Slot]);
            Assert.True(Links.Satisfied(unique.Link, hero,
                unique.Link.Requires.Where(Links.IsMemory).ToArray(),
                unique.Link.Requires.Where(Links.IsEssence).ToArray()));

            var actual = Assert.Single(Build.Compute(profile, hero, 0).Links);
            decimal expected = unique.Link.Value * Content.AwakenPowerPctAt(level) / 100m;
            output.WriteLine($"{uniqueId}: awakening={level}, authored={unique.Link.Value}, expected={expected}, actual={actual.Value}");
            output.WriteLine(Links.Describe(unique.Link));
            Assert.Equal(expected, actual.Value);
        }
    }
}
