using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarRoutesV127Tests
    {




        [Fact]
        public void Links_target_only_their_memory_and_remain_inside_single_memory_caps()
        {
            foreach (var route in HeroStarRoutes.All.Where(t => t.RouteId != null).GroupBy(t => t.RouteId))
            {
                var links = route.Where(t => t.LinkPerRank != null).ToArray();
                foreach (var node in links)
                {
                    var link = node.LinkPerRank;
                    Assert.Equal(new[] { node.RouteMemory }, link.Requires);
                    Assert.True(Links.Validate(link), node.Id);
                    if (link.Kind != LinkKind.MemoryDamage)
                        Assert.InRange(link.Value, 1, Links.Cap(link.Kind, 1) / node.MaxRank);
                    // Identities never emit OnSkillUsed, and Moonlight Pact's Lone-Wolf
                    // constellation can disable its cast: OnSkillUse-driven links must not
                    // appear there. v1.28 memory damage only needs the memory equipped,
                    // so it stays valid for identity passives and Fenrir's attacks.
                    if (node.RouteMemory.StartsWith("St_D_", StringComparison.Ordinal)
                        || node.RouteMemory == "St_Q_MoonlightPact")
                        Assert.True(link.Kind != LinkKind.MemoryHaste && link.Kind != LinkKind.MemorySurge, node.Id);
                }
                foreach (var kind in links.GroupBy(t => t.LinkPerRank.Kind))
                {
                    if (kind.Key != LinkKind.MemoryDamage)
                        Assert.InRange(kind.Sum(t => t.LinkPerRank.Value * t.MaxRank), 1, Links.Cap(kind.Key, 1));
                }
            }
        }



        [Fact]
        public void Star_map_has_no_dodge_triggered_effects()
        {
            var banned = new[] { Power.EchoingDodge, Power.Sprint, Power.Whirlwind, Power.PerfectRead };
            foreach (var t in HeroSigils.All.Concat(HeroStarRoutes.All))
            {
                Assert.DoesNotContain(t.Power, banned);
                Assert.DoesNotContain(t.RankPower, banned);
                if (t.Power == Power.ShadowStep || t.RankPower == Power.ShadowStep)
                {
                    Assert.Equal("Hero_Husk", t.HeroKey);
                    Assert.Equal("h.husk.key2", t.Id);
                    Assert.True(t.IsKeystone);
                    Assert.Equal(Power.ShadowStep, t.Power);
                    Assert.Equal(Power.None, t.RankPower);
                }
                if (t.LinkPerRank != null && t.LinkPerRank.Requires.Any(r => r.StartsWith("St_M_", StringComparison.Ordinal)))
                    Assert.True(t.LinkPerRank.Kind == LinkKind.Guard || t.LinkPerRank.Kind == LinkKind.MemoryDamage, t.Id);
            }
        }
    }
}
