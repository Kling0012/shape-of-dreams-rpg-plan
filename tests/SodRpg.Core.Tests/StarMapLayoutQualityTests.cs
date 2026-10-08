using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 全旅人の星図の配置の検査：星の重なり・線が星の上を通る・線の交差・長すぎる線を自動で数える。
    /// 配置は tools/StarMapRender --optimize が作る座標表（StarMapPlacements.Generated.cs）で、
    /// 星・線・取得条件は変えず座標だけを動かす。星を足した／消したときは表が古くなるので再生成する。
    /// </summary>
    public sealed class StarMapLayoutQualityTests : IClassFixture<StarMapLayoutQualityTests.Installed>
    {
        public sealed class Installed : IDisposable
        {
            public Installed()
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
            }

            public void Dispose()
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
            }
        }

        public static IEnumerable<object[]> Heroes()
        {
            if (StarClusters.GeneratedHeroes.Count == 0) { yield return new object[] { "(no generated hero)" }; yield break; }
            foreach (string hero in StarClusters.GeneratedHeroes) yield return new object[] { hero };
        }

        private static HeroTreeLayout Default(string hero) => HeroTreeLayout.ForTalents(HeroSigils.TreeFor(hero), false);

        private static bool Moved(HeroTreeLayout tuned, HeroTreeLayout baseline)
        {
            for (int i = 0; i < tuned.Nodes.Count; i++)
                if (tuned.Nodes[i].X != baseline.Nodes[i].X || tuned.Nodes[i].Y != baseline.Nodes[i].Y) return true;
            return false;
        }

        [Theory, MemberData(nameof(Heroes))]
        public void Tuned_placements_move_stars_only_and_never_touch_the_lines(string hero)
        {
            var tuned = HeroTreeLayout.ForHero(hero);
            var baseline = Default(hero);
            Assert.Equal(baseline.Nodes.Count, tuned.Nodes.Count);
            for (int i = 0; i < baseline.Nodes.Count; i++)
            {
                Assert.Equal(baseline.Nodes[i].Id, tuned.Nodes[i].Id);
                Assert.Equal(baseline.Nodes[i].Kind, tuned.Nodes[i].Kind);
                Assert.Equal(baseline.Nodes[i].Talent?.Id, tuned.Nodes[i].Talent?.Id);
                Assert.Equal(baseline.Nodes[i].Neighbors.ToArray(), tuned.Nodes[i].Neighbors.ToArray());
            }
            Assert.Equal(baseline.Edges.Select(e => e.A + ":" + e.B), tuned.Edges.Select(e => e.A + ":" + e.B));
            Assert.Equal(0f, tuned.Nodes[tuned.StartIndex].X);
            Assert.Equal(0f, tuned.Nodes[tuned.StartIndex].Y);
        }

        [Theory, MemberData(nameof(Heroes))]
        public void No_two_stars_overlap_and_no_star_is_hidden_under_a_line_it_does_not_belong_to(string hero)
        {
            var report = StarMapQuality.Measure(HeroTreeLayout.ForHero(hero));
            Assert.True(report.DiscOverlaps == 0 && report.PairsUnderMinimumSpacing == 0, hero + ": " + string.Join("; ", report.Examples));
            // 線が星の円盤にかかる数は、旧配置では73〜216。調整後は旅人ごとに1桁〜20台。
            Assert.True(report.EdgeStarPasses <= 40, hero + ": " + report + "; " + string.Join("; ", report.Examples));
        }

        [Theory, MemberData(nameof(Heroes))]
        public void The_tuned_layout_is_clearly_easier_to_read_than_the_unoptimized_one(string hero)
        {
            var tuned = HeroTreeLayout.ForHero(hero);
            var baseline = Default(hero);
            if (!Moved(tuned, baseline)) return; // 星の集合が表と違う場合は次のテスト（表の鮮度）が知らせる
            var after = StarMapQuality.Measure(tuned);
            var before = StarMapQuality.Measure(baseline);
            Assert.True(after.Crossings * 100 <= before.Crossings * 80, hero + " crossings " + before.Crossings + " -> " + after.Crossings);
            Assert.True(after.EdgeStarPasses * 100 <= before.EdgeStarPasses * 30, hero + " edge-over-star " + before.EdgeStarPasses + " -> " + after.EdgeStarPasses);
            Assert.True(after.LongestEdge * 100 <= before.LongestEdge * 85, hero + " longest edge " + before.LongestEdge + " -> " + after.LongestEdge);
            Assert.True(after.MeanEdge * 100 <= before.MeanEdge * 90, hero + " mean edge " + before.MeanEdge + " -> " + after.MeanEdge);
        }

        [Theory, MemberData(nameof(Heroes))]
        public void The_placement_table_matches_the_current_stars(string hero)
        {
            // 星を足した・消した・IDを変えたあとにこれが落ちたら：
            //   dotnet run -c Release --project tools/StarMapRender -- --optimize src/SodRpg.Core/Game/StarMapPlacements.Generated.cs
            Assert.True(Moved(HeroTreeLayout.ForHero(hero), Default(hero)),
                hero + ": the tuned placement table does not match the current stars (regenerate it with tools/StarMapRender --optimize)");
        }
    }
}
