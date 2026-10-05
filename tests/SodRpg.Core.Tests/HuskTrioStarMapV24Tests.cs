using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
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

        [Theory]
        [InlineData(-1, FaultMode.IoError)]
        [InlineData(0, FaultMode.IoError)]
        [InlineData(1, FaultMode.CrashBefore)]
        [InlineData(1, FaultMode.CrashAfter)]
        public void A_revision_one_husk_store_refunds_and_notifies_once_even_after_continue_or_interrupted_save(int faultAt, FaultMode faultMode)
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
                const string dependent = "husk.mem.wind-scar.c4.choice";
                TreeTestPaths.Connect(p, Husk, dependent);
                Rules.AddTalentRank(p, Husk, dependent, 0);
                Assert.True(h.Talents.ContainsKey(kept)); // Connect bought the path up to the redefined star
                h.AuthoredMigrationVersion = 1; // a save written before revision 2
                int points = p.TalentPoints(Husk), spent = Rules.SpentPoints(h, Husk);

                p.Run = new RunState { RunId = "husk-refund", HeroKey = Husk };
                var checkpoint = RunCheckpoint.Capture(p, "before-revision-two");
                p.ContinueCheckpoints.Add(checkpoint);
                p.ContinueLobbyBaseline = ProfileCodec.WriteCheckpointProfile(p);
                const string path = "/husk/profile.json";
                var disk = new InMemoryFileSystem();
                string oldText = ProfileCodec.Write(p);
                disk.Put(path, oldText);
                disk.Put(path + ".bak", oldText);
                var fs = new FaultyFileSystem(disk);
                if (faultAt >= 0) fs.Arm(faultAt, faultMode);
                var store = new ProfileStore(fs, path, 41);
                var q = store.Load();
                var loaded = q.Hero(Husk);
                Assert.True(loaded.Talents.ContainsKey(kept));
                Assert.False(loaded.Talents.ContainsKey(redefined));
                Assert.False(loaded.Talents.ContainsKey(dependent));
                Assert.Equal(2, loaded.AuthoredMigrationVersion);
                Assert.Equal(points, q.TalentPoints(Husk));
                Assert.True(Rules.SpentPoints(loaded, Husk) < spent);
                Assert.Equal(points, Rules.SpentPoints(loaded, Husk) + Rules.FreePoints(q, Husk));
                Assert.Single(store.Notes, n => n.Contains(redefined));
                Assert.Single(store.Notes, n => n.Contains(dependent));
                int free = Rules.FreePoints(q, Husk);
                int refundCost = Rules.SpentPoints(h, Husk) - Rules.SpentPoints(loaded, Husk);
                Assert.Equal(refundCost, free - Rules.FreePoints(p, Husk));

                // The native Continue path migrates its old snapshot, not the active allocation again.
                var continueNotes = new List<string>();
                checkpoint.Restore(q, notes: continueNotes);
                Assert.Empty(continueNotes);
                Assert.Equal(free, Rules.FreePoints(q, Husk));
                // If Continue is the first migration, report each reason once, not again for the lobby baseline.
                var firstContinue = p.Clone();
                checkpoint.Restore(firstContinue, notes: continueNotes);
                Assert.Single(continueNotes, n => n.Contains(redefined));
                Assert.Single(continueNotes, n => n.Contains(dependent));
                Assert.Equal(free, Rules.FreePoints(firstContinue, Husk));

                fs.Disarm();
                var nextStore = new ProfileStore(fs, path, 41);
                var r = nextStore.Load(); // before any ordinary game save
                Assert.Equal(free, Rules.FreePoints(r, Husk));
                Assert.Equal(2, r.Hero(Husk).AuthoredMigrationVersion);
                bool committed = faultAt < 0 || faultMode == FaultMode.CrashAfter;
                if (committed) Assert.Empty(nextStore.Notes); // the old backup must stay silent
                else
                {
                    Assert.Single(nextStore.Notes, n => n.Contains(redefined));
                    Assert.Single(nextStore.Notes, n => n.Contains(dependent));
                    var afterRetry = new ProfileStore(fs, path, 41);
                    Assert.Equal(free, Rules.FreePoints(afterRetry.Load(), Husk));
                    Assert.Empty(afterRetry.Notes);
                }
                // New effects bought after revision 2 survive the next disk load.
                TreeTestPaths.Connect(r, Husk, redefined);
                Rules.AddTalentRank(r, Husk, redefined);
                nextStore.Save(r);
                var repurchased = new ProfileStore(fs, path, 41);
                var s = repurchased.Load();
                Assert.Equal(1, s.Hero(Husk).Talents[redefined]);
                Assert.Equal(Rules.FreePoints(r, Husk), Rules.FreePoints(s, Husk));
                Assert.Empty(repurchased.Notes);
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
