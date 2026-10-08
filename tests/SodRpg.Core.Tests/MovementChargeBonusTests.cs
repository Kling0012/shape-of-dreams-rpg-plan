using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MovementChargeBonusTests
    {
        private static void WithGeneratedHero(string hero, Action<Profile> body)
        {
            StarClusters.RegisterGeneratedHero(hero);
            try { body(FundedProfile(hero)); }
            finally { StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>()); }
        }

        private static Profile FundedProfile(string hero)
        {
            var p = Profile.CreateNew(71UL);
            var h = p.Hero(hero);
            h.Kills = 1000000;
            h.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            return p;
        }

        private static int Bonus(Profile p, string hero) => Build.MovementChargeBonus(hero, Build.Compute(p, hero, 0));

        [Fact]
        public void An_authored_cluster_star_adds_one_movement_charge()
        {
            const string hero = "Hero_Husk";
            WithGeneratedHero(hero, p =>
            {
                TreeTestPaths.Connect(p, hero, "husk.mem.killing-flow.c1.e1");
                Assert.Equal(0, Bonus(p, hero));
                Rules.AddTalentRank(p, hero, "husk.mem.killing-flow.c1.e1");
                Assert.Equal(1, Bonus(p, hero));
            });
        }

        [Fact]
        public void An_authored_cluster_keystone_raises_the_bonus_to_two()
        {
            const string hero = "Hero_Husk";
            WithGeneratedHero(hero, p =>
            {
                foreach (string id in new[]
                {
                    "h.husk.route.wind-scar.4", "husk.mem.wind-scar.c4.e1", "husk.mem.wind-scar.c4.e2", "husk.mem.wind-scar.c4.e3", "husk.mem.wind-scar.c4.n1",
                    "husk.mem.wind-scar.c4.e4", "husk.mem.wind-scar.c4.e5", "husk.mem.wind-scar.c4.e6", "husk.mem.wind-scar.c4.n2", "husk.mem.wind-scar.c4.choice",
                })
                {
                    TreeTestPaths.Connect(p, hero, id);
                    Rules.AddTalentRank(p, hero, id, id.EndsWith("choice", StringComparison.Ordinal) ? 0 : (int?)null);
                }
                Assert.Equal(1, Bonus(p, hero));
                Rules.SetKeystone(p, hero, "husk.key.wind-cut");
                Assert.Equal(2, Bonus(p, hero));
                Rules.RemoveKeystone(p, hero, "husk.key.wind-cut");
                Assert.Equal(1, Bonus(p, hero));
            });
        }

        [Fact]
        public void Removing_the_authored_cluster_star_restores_the_baseline()
        {
            const string hero = "Hero_Husk";
            WithGeneratedHero(hero, p =>
            {
                TreeTestPaths.Connect(p, hero, "husk.mem.killing-flow.c1.e1");
                Rules.AddTalentRank(p, hero, "husk.mem.killing-flow.c1.e1");
                Assert.Equal(1, Bonus(p, hero));
                Rules.RemoveTalentRank(p, hero, "husk.mem.killing-flow.c1.e1");
                Assert.Equal(0, Bonus(p, hero));
            });
        }

        [Fact]
        public void Other_travelers_keep_the_baseline()
        {
            const string hero = "Hero_Cetus";
            WithGeneratedHero(hero, p =>
            {
                TreeTestPaths.Connect(p, hero, "cetus.mem.icy-veins.c1.e1");
                Rules.AddTalentRank(p, hero, "cetus.mem.icy-veins.c1.e1");
                var build = Build.Compute(p, hero, 0);
                Assert.True(build.AuthoredClusterStars > 0);
                Assert.Equal(0, Build.MovementChargeBonus(hero, build));
            });
        }
    }
}
