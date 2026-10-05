using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 記憶の元々のダメージを増やす刻印（空殻の風傷の芯など）の効果はホスト側で働き、星の割り当てには現れない。
    /// 割り当ての変化だけで効果を判定すると「無効」と断られ、選べなくなっていた。
    /// </summary>
    public sealed class KeystoneNativeDamageTests
    {
        [Fact]
        public void A_keystone_that_scales_native_damage_can_be_selected()
        {
            const string hero = "Hero_Husk", keystone = "husk.key.wind-cut";
            try
            {
                foreach (string generated in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(generated);
                var p = Profile.CreateNew(71);
                for (int i = 0; i < 80; i++) p.Codex.Add("codex." + i);
                var h = p.Hero(hero);
                h.Kills = 1000000;
                h.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                foreach (string id in new[]
                {
                    "h.husk.route.wind-scar.4", "husk.mem.wind-scar.c4.e1", "husk.mem.wind-scar.c4.e2", "husk.mem.wind-scar.c4.e3", "husk.mem.wind-scar.c4.n1",
                    "husk.mem.wind-scar.c4.e4", "husk.mem.wind-scar.c4.e5", "husk.mem.wind-scar.c4.e6", "husk.mem.wind-scar.c4.n2", "husk.mem.wind-scar.c4.choice",
                })
                {
                    TreeTestPaths.Connect(p, hero, id);
                    Rules.AddTalentRank(p, hero, id, id.EndsWith("choice") ? 0 : (int?)null);
                }
                var plan = Rules.PreviewAllocationChange(p, hero, new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = keystone });
                Assert.True(plan.CanApply, string.Join(",", plan.SaturatedChannels));
                Rules.SetKeystone(p, hero, keystone);
                Assert.True(h.HasKeystone(keystone));
            }
            finally { foreach (string generated in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(generated, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
