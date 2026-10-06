using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarGimmicksV128Tests
    {
        // 設計表の「none」ルート（8ルート）。仕掛けを置かず、常時の効果のまま残す。
        private static readonly HashSet<string> NoneRoutes = new HashSet<string>
        {
            "St_M_NimbleDodge", "St_M_Flicker", "St_M_FlashStep", "St_M_FastFeet",
            "St_M_DreamyWaltz", "St_M_FeatheryDash", "St_M_Sprint", "St_D_CircleOfLife",
        };









        [Fact]
        public void Gimmick_stars_add_nothing_to_the_stat_and_power_budgets()
        {
            foreach (var star in HeroStarRoutes.All.Where(t => t.Gimmick != null && t.RouteOrder == 2))
            {
                var p = Profile.CreateNew(1);
                var h = p.Hero(star.HeroKey);
                h.StarXp = StarProgression.TotalXpForPoints(60);
                TreeTestPaths.Connect(p, star.HeroKey, star.Id);
                var before = Build.Compute(p, star.HeroKey, 0);
                h.Talents[star.Id] = star.MaxRank;
                var after = Build.Compute(p, star.HeroKey, 0);
                foreach (var stat in before.Stats.Keys.Concat(after.Stats.Keys).Distinct())
                    Assert.Equal(before.Get(stat), after.Get(stat));
                Assert.Equal(before.Powers.Keys, after.Powers.Keys);
                foreach (var power in before.Powers.Keys.Where(power => !StarDamageScaling.IsDamage(power)))
                    Assert.Equal(before.Get(power), after.Get(power));
                var entry = Assert.Single(after.Gimmicks, g => g.StarId == star.Id);
                Assert.Equal(star.Gimmick.Value * star.MaxRank, entry.Def.Value);
                Assert.Equal(star.RouteMemory, entry.Memory);
            }
        }

        [Fact]
        public void Build_collects_route_gimmicks_with_value_times_ranks()
        {
            foreach (var route in HeroStarRoutes.All.Where(t => t.RouteId != null)
                .GroupBy(t => t.RouteMemory).Where(g => !NoneRoutes.Contains(g.Key)))
            {
                var p = Profile.CreateNew(1);
                var hero = route.First().HeroKey;
                var h = p.Hero(hero);
                h.StarXp = StarProgression.TotalXpForPoints(60);
                var stars = route.OrderBy(t => t.RouteOrder).ToList();
                TreeTestPaths.Connect(p, hero, stars[0].Id);
                foreach (var s in stars.Where(t => t.RouteOrder <= 7))
                    h.Talents[s.Id] = s.MaxRank;
                var b = Build.Compute(p, hero, 0);
                foreach (var s in stars.Where(t => t.Gimmick != null))
                {
                    // Reload・RechargeOther の受け渡し上限は Gimmicks.cs の側で決まる
                    if (Gimmicks.Cap(s.Gimmick.Effect) == 0) continue;
                    var entry = Assert.Single(b.Gimmicks, g => g.StarId == s.Id);
                    Assert.Equal(s.Gimmick.Value * s.MaxRank, entry.Def.Value);
                    Assert.Equal(s.Gimmick.Trigger, entry.Def.Trigger);
                    Assert.Equal(s.Gimmick.Effect, entry.Def.Effect);
                    Assert.Equal(s.Gimmick.Arg, entry.Def.Arg);
                    Assert.Equal(s.Gimmick.Cooldown, entry.Def.Cooldown, 3);
                }
            }
        }

    }
}
