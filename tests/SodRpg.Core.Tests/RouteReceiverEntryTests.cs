using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 移動ルートの受け手強化（空殻の瞬歩の備えなど）は、その記憶を再充填する星がまだ無くても取れる。
    /// ルートの再充填元はこの強化星の奥にあるので、「受け手なし＝無効」で断ると、ルートに入れなくなる。
    /// </summary>
    public sealed class RouteReceiverEntryTests
    {
        [Theory]
        [InlineData("Hero_Husk", "h.husk.route.flash-step.1")]
        [InlineData("Hero_Mist", "h.mist.route.fast-feet.1")]
        public void The_first_receiver_boost_of_a_movement_route_can_be_bought_before_any_recharge_source(string hero, string starId)
        {
            try
            {
                foreach (string generated in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(generated);
                var p = Profile.CreateNew(61);
                for (int i = 0; i < 25; i++) p.Codex.Add("codex." + i);
                var h = p.Hero(hero);
                h.Kills = 20000;
                h.StarXp = StarProgression.TotalXpForPoints(150 - p.CodexBonusPoints);
                TreeTestPaths.Connect(p, hero, starId);
                Content.TryGetTalent(hero, starId, out var talent);
                Assert.Equal(ScopeKind.Receiver, talent.ScopedModifier.ScopeKind);
                for (int rank = 1; rank <= talent.MaxRank; rank++)
                {
                    Rules.AddTalentRank(p, hero, starId);
                    Assert.Equal(rank, h.Talents[starId]);
                }
            }
            finally { foreach (string generated in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(generated, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
