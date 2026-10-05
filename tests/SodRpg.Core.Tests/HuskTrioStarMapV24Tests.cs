using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v2.4 空殻の三印（docs/specs/v2.4-husk-trio-starmap.md）：風の傷・滅殺態勢・裂傷が輪の上で隣り合い、
    /// 3つを装備したときの連携の星が登録でき、作り替えた星は古い空殻のセーブから一度だけ払い戻される。
    /// </summary>
    public sealed class HuskTrioStarMapV24Tests
    {
        private const string Husk = "Hero_Husk";
        private const string Wind = "St_D_ScarOfTheWind", Aura = "St_R_AnnihilationStance", Rend = "St_Q_Laceration";

        private static string[] RouteOrder(string hero)
            => HeroTreeLayout.ForHero(hero).Nodes.Where(n => n.Talent?.RouteId != null).GroupBy(n => n.Talent.RouteId).Select(g => g.Key).ToArray();

        [Fact]
        public void Wind_scar_annihilation_and_laceration_sit_side_by_side_on_the_ring()
        {
            string[] order = RouteOrder(Husk);
            Assert.Equal(new[] { "killing-flow", "laceration", "annihilation", "wind-scar", "flash-step", "death-mark", "deception" }
                .Select(s => "h.husk.route." + s), order);
            int rend = Array.IndexOf(order, "h.husk.route.laceration"), aura = Array.IndexOf(order, "h.husk.route.annihilation"), wind = Array.IndexOf(order, "h.husk.route.wind-scar");
            Assert.Equal(rend + 1, aura);
            Assert.Equal(aura + 1, wind);
        }

        [Fact]
        public void Bridges_two_to_five_join_the_trio_and_the_flash_step_death_mark_neighbours()
        {
            var pairs = PairCombos.All.Where(d => d.HeroKey == Husk).OrderBy(d => d.BridgeIndex).ToArray();
            Assert.Equal((Rend, Aura), (pairs[1].RouteA, pairs[1].RouteB));
            Assert.Equal((Aura, Wind), (pairs[2].RouteA, pairs[2].RouteB));
            Assert.Equal((Wind, "St_M_FlashStep"), (pairs[3].RouteA, pairs[3].RouteB));
            Assert.Equal(("St_M_FlashStep", "St_Q_DeathMark"), (pairs[4].RouteA, pairs[4].RouteB));
            // Wind's own slash marks, the aura's hit completes it (a mark that opens and pays off on different memories).
            Assert.Equal(PairComboStep.Mark, pairs[2].Step);
            Assert.Equal(Wind, pairs[2].TriggerMemory);
            Assert.Equal(Aura, pairs[2].PayoffMemory);
        }

        private static string RepositoryRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "tools", "star-manifest", "revisions.json"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir;
        }

        private static string[] RevisionIds()
        {
            using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "star-manifest", "revisions.json"))))
                return document.RootElement.GetProperty("husk").GetProperty("stars").EnumerateArray().Select(x => x.GetString()).ToArray();
        }

        [Fact]
        public void Revision_two_lists_the_redefined_trio_stars_and_nothing_else_is_revised()
        {
            string[] ids = RevisionIds();
            Assert.Contains("husk.bridge.b3.n1", ids);
            Assert.Contains("husk.bridge.b5.n2", ids);
            Assert.Contains("husk.mem.wind-scar.c4.n1", ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            try
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
                var revised = StarClusters.MigrationsFor(Husk).Where(r => r.Revision == 2).Select(r => r.LocalStarId).ToArray();
                Assert.Equal(ids.Concat(new[] { "h.husk.ring.vessel", "h.husk.ring.recall" }).OrderBy(x => x, StringComparer.Ordinal), revised.OrderBy(x => x, StringComparer.Ordinal));
                Assert.Equal(2, StarClusters.MigrationVersionFor(Husk));
                foreach (string hero in StarClusters.GeneratedHeroes.Where(h => h != Husk))
                {
                    Assert.DoesNotContain(StarClusters.MigrationsFor(hero), r => r.Revision != 1);
                    Assert.Equal(1, StarClusters.MigrationVersionFor(hero));
                }
            }
            finally { foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void A_revision_one_husk_save_refunds_the_redefined_stars_once_and_keeps_the_rest()
        {
            try
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
                var p = Profile.CreateNew(41);
                var h = p.Hero(Husk);
                for (int i = 0; i < 25; i++) p.Codex.Add("codex." + i);
                h.Kills = 20000;
                h.StarXp = StarProgression.TotalXpForPoints(120 - p.CodexBonusPoints);
                string kept = "husk.mem.wind-scar.c4.e3", redefined = "husk.mem.wind-scar.c4.n1";
                const string route = "h.husk.route.wind-scar.4";
                TreeTestPaths.Connect(p, Husk, route);
                Rules.AddTalentRank(p, Husk, route);
                TreeTestPaths.Connect(p, Husk, redefined);
                Rules.AddTalentRank(p, Husk, redefined);
                Assert.True(h.Talents.ContainsKey(kept)); // Connect bought the path up to the redefined star
                h.AuthoredMigrationVersion = 1; // a save written before revision 2
                int points = p.TalentPoints(Husk), spent = Rules.SpentPoints(h, Husk);

                var notes = new List<string>();
                var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
                var loaded = q.Hero(Husk);
                Assert.True(loaded.Talents.ContainsKey(kept));
                Assert.False(loaded.Talents.ContainsKey(redefined));
                Assert.Equal(2, loaded.AuthoredMigrationVersion);
                Assert.Equal(points, q.TalentPoints(Husk));
                Assert.True(Rules.SpentPoints(loaded, Husk) < spent);
                Assert.Equal(points, Rules.SpentPoints(loaded, Husk) + Rules.FreePoints(q, Husk));
                Assert.Contains(notes, n => n.Contains(redefined));

                var again = new List<string>();
                var r = ProfileCodec.Read(ProfileCodec.Write(q), again);
                Assert.Empty(again);
                Assert.Equal(Rules.FreePoints(q, Husk), Rules.FreePoints(r, Husk));
            }
            finally { foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void A_new_husk_profile_starts_at_the_latest_revision_and_is_not_refunded()
        {
            try
            {
                foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
                var p = Profile.CreateNew(43);
                Assert.Equal(2, p.Hero(Husk).AuthoredMigrationVersion);
                var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
                Assert.Equal(2, q.Hero(Husk).AuthoredMigrationVersion);
            }
            finally { foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
