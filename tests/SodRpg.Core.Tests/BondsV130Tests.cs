using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class BondsV130Tests
    {
        private static LinkDef Bond(params string[] requires) =>
            new LinkDef { Requires = requires, Kind = LinkKind.Guard, Value = 20 };

        [Fact]
        public void Single_traveler_accepts_self_or_nearby_ally_without_changing_solo_links()
        {
            var link = Bond("Hero_Vesper");
            Assert.True(Links.Satisfied(link, "Hero_Vesper", null, null));
            Assert.False(Links.Satisfied(link, "Hero_Nachia", null, null));
            Assert.True(Links.Satisfied(link, "Hero_Nachia", null, null, new[] { "Hero_Vesper" }));
            Assert.False(Links.Satisfied(link, "Hero_Nachia", null, null, new[] { "Hero_Mist" }));
            Assert.False(Links.RequirementSatisfied(null, "Hero_Nachia", null, null));
        }

        [Theory]
        [InlineData(true, true, 0, true)]
        [InlineData(true, true, 10, true)]
        [InlineData(true, true, 10.001, false)]
        [InlineData(false, true, 2, false)]
        [InlineData(true, false, 2, false)]
        [InlineData(true, true, -1, false)]
        public void Bonds_require_a_living_ally_within_ten_metres(bool living, bool allied, double distance, bool expected)
        {
            Assert.Equal(expected, Links.IsBondAlly(living, allied, distance));
            Assert.False(Links.IsBondAlly(true, true, double.NaN));
            Assert.False(Links.IsBondAlly(true, true, double.PositiveInfinity));
        }

        [Fact]
        public void Two_travelers_require_self_to_be_one_of_the_pair()
        {
            var link = Bond("Hero_Husk", "Hero_Mist");
            Assert.True(Links.Validate(link));
            Assert.True(Links.Satisfied(link, "Hero_Husk", null, null, new[] { "Hero_Mist" }));
            Assert.True(Links.Satisfied(link, "Hero_Mist", null, null, new[] { "Hero_Husk" }));
            Assert.False(Links.Satisfied(link, "Hero_Husk", null, null));
            Assert.False(Links.Satisfied(link, "Hero_Husk", null, null, new[] { "Hero_Husk" }));
            Assert.False(Links.Satisfied(link, "Hero_Nachia", null, null, new[] { "Hero_Husk", "Hero_Mist" }));
        }

        [Fact]
        public void Nearby_traveler_does_not_supply_its_memories_or_essences()
        {
            var link = Bond("Hero_Vesper", "St_L_Blizzard", Links.Compass);
            var allies = new[] { "Hero_Vesper", "St_L_Blizzard", Links.Compass };
            Assert.False(Links.Satisfied(link, "Hero_Nachia", null, null, allies));
            Assert.False(Links.Satisfied(link, "Hero_Nachia", new[] { "St_L_Blizzard" }, null, allies));
            Assert.True(Links.Satisfied(link, "Hero_Nachia", new[] { "St_L_Blizzard" },
                new[] { "Gem_U_GuidingCompass_Charged" }, allies));
        }

        [Fact]
        public void Removing_or_losing_a_nearby_ally_immediately_invalidates_the_condition()
        {
            var link = Bond("Hero_Husk", "Hero_Mist");
            var allies = new System.Collections.Generic.HashSet<string> { "Hero_Mist" };
            Assert.True(Links.Satisfied(link, "Hero_Husk", null, null, allies));
            allies.Clear();
            Assert.False(Links.Satisfied(link, "Hero_Husk", null, null, allies));
        }

        [Fact]
        public void Three_traveler_bonds_also_require_membership_and_all_others()
        {
            var link = Bond("Hero_Husk", "Hero_Mist", "Hero_Vesper");
            Assert.True(Links.Satisfied(link, "Hero_Husk", null, null, new[] { "Hero_Mist", "Hero_Vesper" }));
            Assert.False(Links.Satisfied(link, "Hero_Husk", null, null, new[] { "Hero_Mist" }));
            Assert.False(Links.Satisfied(link, "Hero_Nachia", null, null, link.Requires));
        }

        [Fact]
        public void Bond_wire_format_preserves_existing_link_requirements()
        {
            var build = new Build();
            build.Links.Add(Bond("Hero_Husk", "Hero_Mist"));
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            var link = Assert.Single(decoded.Links);
            Assert.Equal(build.Links[0].Requires, link.Requires);
            Assert.True(Links.Satisfied(link, "Hero_Mist", null, null, new[] { "Hero_Husk" }));
        }
    }
}
