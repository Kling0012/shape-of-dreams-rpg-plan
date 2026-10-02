using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>旅人ルートの装着条件と、圧を含む通信の境界。</summary>
    public class StarBuildV127Tests
    {
        private const string Hero = "Hero_Mist";
        private const string Memory = "St_Q_Fleche";

        private static Profile Ready()
        {
            var p = Profile.CreateNew(127);
            p.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(150);
            Rules.AddTalentRank(p, Hero, "h.mist.duel");
            Rules.AddTalentRank(p, Hero, "h.mist.duel");
            Rules.AddTalentRank(p, Hero, "h.mist.duel");
            Rules.AddTalentRank(p, Hero, "h.mist.read");
            Rules.AddTalentRank(p, Hero, "h.mist.read");
            Rules.AddTalentRank(p, Hero, "h.mist.read");
            return p;
        }

        private static TalentDef LinkedStar(Profile p)
        {
            var route = HeroSigils.TreeFor(Hero).Where(t => t.RouteMemory == Memory).OrderBy(t => t.RouteOrder);
            foreach (var t in route)
            {
                if (t.LinkPerRank != null) return t;
                Rules.AddTalentRank(p, Hero, t.Id);
            }
            throw new InvalidOperationException("The Flèche route needs an equipped-memory star.");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        public void Linked_ranks_are_conditional_not_unconditional_stats_or_powers(int ranks)
        {
            var p = Ready();
            var star = LinkedStar(p);
            var before = Build.Compute(p, Hero, 0);
            for (int i = 0; i < ranks; i++) Rules.AddTalentRank(p, Hero, star.Id);
            var build = Build.Compute(p, Hero, 0);
            Assert.Equal(before.Stats.ToArray(), build.Stats.ToArray());
            Assert.Equal(before.Powers.ToArray(), build.Powers.ToArray());
            var link = Assert.Single(build.Links);
            Assert.Equal(star.LinkPerRank.Value * ranks, link.Value);
            Assert.True(Links.Satisfied(link, Hero, new[] { Memory }, null));
            Assert.False(Links.Satisfied(link, Hero, new[] { "St_Q_Lunge" }, null));
            Assert.False(Links.Satisfied(link, Hero, Array.Empty<string>(), null));
            Assert.Single(Build.Decode(build.Encode()).Links);
        }

        [Fact]
        public void Locked_or_foreign_links_do_not_activate_and_saved_rank_is_not_erased()
        {
            var p = Ready();
            var route = HeroSigils.TreeFor(Hero).Where(t => t.RouteMemory == Memory).OrderBy(t => t.RouteOrder).ToArray();
            var star = route.Last(t => t.LinkPerRank != null);
            var h = p.Hero(Hero);
            h.Talents[star.Id] = star.MaxRank;
            Assert.Empty(Build.Compute(p, Hero, 0).Links);
            Assert.Equal(star.MaxRank, h.Talents[star.Id]);
            foreach (var previous in route.Where(t => t.RouteOrder < star.RouteOrder)) h.Talents[previous.Id] = 1;
            Assert.Contains(Build.Compute(p, Hero, 0).Links, l => l.Kind == star.LinkPerRank.Kind && l.Value == star.LinkPerRank.Value * star.MaxRank);
            h.Talents.Remove(route[0].Id);
            Assert.Empty(Build.Compute(p, Hero, 0).Links);
            p.Hero("Hero_Cetus").Talents[star.Id] = star.MaxRank;
            Assert.Empty(Build.Compute(p, "Hero_Cetus", 0).Links);
        }

        [Theory]
        [InlineData(-10, -20, 1, 0)]
        [InlineData(17, 93, 17, 93)]
        [InlineData(int.MaxValue, int.MaxValue, 30, 150)]
        public void Build_protocol_round_trips_and_clamps_pressure_metadata(int dream, int stars, int expectedDream, int expectedStars)
        {
            var b = new Build { DreamLevel = dream, SpentStarPoints = stars, Heat = 3 };
            b.Stats[Stat.PowerPct] = 19;
            b.Powers[Power.EchoingDodge] = 30;
            var decoded = Build.Decode(b.Encode());
            Assert.Equal(expectedDream, decoded.DreamLevel);
            Assert.Equal(expectedStars, decoded.SpentStarPoints);
            Assert.Equal(3, decoded.Heat);
            Assert.Equal(19, decoded.Get(Stat.PowerPct));
            Assert.Equal(30, decoded.Get(Power.EchoingDodge));
        }

        [Fact]
        public void Compute_sends_only_selected_travelers_spent_cost_not_free_points()
        {
            var p = Ready();
            p.DreamLevel = 21;
            var star = LinkedStar(p);
            int before = Rules.SpentPoints(p.Hero(Hero));
            Rules.AddTalentRank(p, Hero, star.Id);
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(21, b.DreamLevel);
            Assert.Equal(before + star.RankCost, b.SpentStarPoints);
            Assert.Equal(0, Build.Compute(p, "Hero_Cetus", 0).SpentStarPoints);
            Rules.ResetTalents(p, Hero);
            Assert.Equal(0, Build.Compute(p, Hero, 0).SpentStarPoints);
        }

        [Fact]
        public void Large_route_packet_preserves_all_links_instead_of_the_old_twelve_entry_limit()
        {
            var b = new Build { DreamLevel = 30, SpentStarPoints = 150 };
            var memories = HeroSigils.All.Where(t => t.RouteMemory != null).Select(t => t.RouteMemory).Distinct().Take(Links.MaxLinks).ToArray();
            foreach (string memory in memories)
                b.Links.Add(new LinkDef { Kind = LinkKind.Guard, Value = 4, Requires = new[] { memory, "Hero_Mist", Links.Compass } });
            Assert.True(b.Encode().Length > 2000); // 旧版は、途中で切るのではなく文字列全体を拒否していた。
            var decoded = Build.Decode(b.Encode());
            Assert.Equal(Links.MaxLinks, decoded.Links.Count);
            for (int i = 0; i < memories.Length; i++)
            {
                Assert.Equal(memories[i], decoded.Links[i].Requires[0]);
                Assert.Equal(4, decoded.Links[i].Value);
            }
        }

        [Theory]
        [InlineData("s:;p:;h:0;d:abc;a:5")]
        [InlineData("s:;p:;h:0;d:2;a:99999999999999999")]
        public void Malformed_pressure_numbers_reject_the_packet(string text) => Assert.Null(Build.Decode(text));
    }
}
