using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.31 で効果が変わる既存の星は、プロフィール読み込み時に取得を解除して使ったポイントを全額戻す。
    /// 変わらない星は取得したまま。戻すのは一度だけ（保存された移行版で判定）で、ポイントは増えも減りもしない。
    /// </summary>
    public sealed class StarMigrationRefundTests
    {
        private const string Cetus = "Hero_Cetus";

        private static AuthoredStarDef[] SmallSet()
        {
            var a = new AuthoredStarDef
            {
                HeroKey = Cetus, LocalStarId = "outer.migr.s1", ClusterId = "outer.migr", Region = ClusterRegion.Outer,
                AnchorId = "outer.migr.s1", Shape = ClusterShape.Ring,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の守り", "Test Guard"), Stat = Stat.Armor, Amount = 1 },
                Edges = new[] { new AuthoredStarEdge("outer.migr.s1", "outer.migr.s2") }
            };
            var b = new AuthoredStarDef
            {
                HeroKey = Cetus, LocalStarId = "outer.migr.s2", ClusterId = "outer.migr", Region = ClusterRegion.Outer,
                AnchorId = "outer.migr.s1", Shape = ClusterShape.Ring,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の守り", "Test Guard"), Stat = Stat.Armor, Amount = 1 }
            };
            return new[] { a, b };
        }

        private static void Fill(Profile p)
        {
            var tree = HeroSigils.TreeFor(Cetus).OrderBy(n => n.Id.Contains(".cluster.") || n.Id.Contains(".outer.") ? 0 : 1).ToList();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var node in tree)
                {
                    if (node.IsKeystone) continue;
                    try
                    {
                        if (!p.Hero(Cetus).Talents.ContainsKey(node.Id)) TreeTestPaths.Connect(p, Cetus, node.Id);
                        while (p.Hero(Cetus).Talents.GetValueOrDefault(node.Id) < node.MaxRank)
                        {
                            if (Rules.FreePoints(p, Cetus) < node.RankCost) break;
                            Rules.AddTalentRank(p, Cetus, node.Id, node.IsChoice ? (int?)(node.Id.GetHashCode() & 1) : null);
                            progress = true;
                        }
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }

        /// <summary>v1.30 相当の保存（移行版0）。刻印も取得済み。</summary>
        private static Profile LegacyProfile()
        {
            var p = Profile.CreateNew(31);
            for (int i = 0; i < 25; i++) p.Codex.Add("codex." + i);
            var h = p.Hero(Cetus);
            h.Kills = 20000;
            h.StarXp = StarProgression.TotalXpForPoints(150 - p.CodexBonusPoints) + 77;
            Fill(p);
            h.StarXp = StarProgression.TotalXpForPoints(Math.Min(StarProgression.MaxPoints, Rules.SpentPoints(h, Cetus) + 3 - p.CodexBonusPoints));
            Rules.SetKeystone(p, Cetus, "h.cetus.key");
            Assert.Equal("h.cetus.key", h.Keystone);
            Assert.Equal(0, h.AuthoredMigrationVersion);
            return p;
        }

        private static LegacyStarMigration[] MigrationRules(params string[] changed)
        {
            var rules = AuthoredStarMigration.CetusRetained
                .Select(r => new LegacyStarMigration(r.LocalStarId, r.MaxRank, r.RankCost, changed.Contains(r.LocalStarId))).ToList();
            if (changed.Contains("h.cetus.key")) rules.Add(new LegacyStarMigration("h.cetus.key", 1, Content.KeystoneCost, true));
            return rules.ToArray();
        }

        private static void Register(params string[] changed)
        {
            StarClusters.RegisterAuthored(Cetus, SmallSet());
            StarClusters.RegisterMigrations(Cetus, MigrationRules(changed));
        }

        private static void Unregister() => StarClusters.RegisterAuthored(Cetus, Array.Empty<AuthoredStarDef>());

        private static string Pick(Profile p, bool withDependents)
        {
            var h = p.Hero(Cetus);
            var tree = HeroSigils.TreeFor(Cetus);
            foreach (var r in AuthoredStarMigration.CetusRetained)
            {
                if (!h.Talents.ContainsKey(r.LocalStarId) || tree.First(t => t.Id == r.LocalStarId).IsChoice) continue;
                var probe = h.Clone();
                var refund = AuthoredStarMigration.Apply(probe, tree, new[] { new LegacyStarMigration(r.LocalStarId, r.MaxRank, r.RankCost, true) });
                if ((refund.StarIds.Count > 1) == withDependents && !refund.StarIds.Contains("h.cetus.key")) return r.LocalStarId;
            }
            throw new InvalidOperationException("no suitable star in the fixture");
        }

        [Fact]
        public void Changed_star_is_refunded_exactly_and_unchanged_stars_keep_their_ranks()
        {
            var p = LegacyProfile();
            string leaf = Pick(p, false);
            var before = new Dictionary<string, int>(p.Hero(Cetus).Talents);
            int points = p.TalentPoints(Cetus), spent = Rules.SpentPoints(p.Hero(Cetus), Cetus);
            string text = ProfileCodec.Write(p);
            Register(leaf);
            try
            {
                var notes = new List<string>();
                var q = ProfileCodec.Read(text, notes);
                var h = q.Hero(Cetus);
                Assert.False(h.Talents.ContainsKey(leaf));
                Assert.Equal(before.Count - 1, h.Talents.Count);
                foreach (var kv in before.Where(x => x.Key != leaf)) Assert.Equal(kv.Value, h.Talents[kv.Key]);
                Assert.Equal("h.cetus.key", h.Keystone);
                int refunded = before[leaf] * HeroSigils.TreeFor(Cetus).First(t => t.Id == leaf).RankCost;
                Assert.Equal(spent - refunded, Rules.SpentPoints(h, Cetus));
                Assert.Equal(points, q.TalentPoints(Cetus));
                Assert.Equal(refunded, Rules.FreePoints(q, Cetus));
                Assert.Equal(1, h.AuthoredMigrationVersion);
                string note = Assert.Single(notes);
                Assert.Contains(leaf, note);
                Assert.Contains(refunded + "ポイント", note);
                Assert.Contains("効果が変わった", note);
            }
            finally { Unregister(); }
        }

        [Fact]
        public void Changed_star_that_was_already_a_choice_is_refunded_even_with_a_stored_option()
        {
            var p = LegacyProfile();
            var h = p.Hero(Cetus);
            var tree = HeroSigils.TreeFor(Cetus);
            string choice = AuthoredStarMigration.CetusRetained.Select(r => r.LocalStarId)
                .FirstOrDefault(id => h.Talents.ContainsKey(id) && h.TalentChoices.ContainsKey(id) && tree.First(t => t.Id == id).IsChoice);
            if (choice == null) return; // the retained fixture holds no choice star
            Register(choice);
            try
            {
                var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
                Assert.False(q.Hero(Cetus).Talents.ContainsKey(choice));
                Assert.False(q.Hero(Cetus).TalentChoices.ContainsKey(choice));
                Assert.Equal(p.TalentPoints(Cetus), Rules.SpentPoints(q.Hero(Cetus), Cetus) + Rules.FreePoints(q, Cetus));
            }
            finally { Unregister(); }
        }

        [Fact]
        public void Second_load_after_saving_refunds_nothing_more()
        {
            var p = LegacyProfile();
            string leaf = Pick(p, false);
            Register(leaf);
            try
            {
                var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
                int free = Rules.FreePoints(q, Cetus);
                var notes = new List<string>();
                var r = ProfileCodec.Read(ProfileCodec.Write(q), notes);
                Assert.Empty(notes);
                Assert.Equal(free, Rules.FreePoints(r, Cetus));
                // 払い戻し後に同じ星を買い直しても、次の読み込みで再び払い戻されない。
                TreeTestPaths.Connect(r, Cetus, leaf);
                Rules.AddTalentRank(r, Cetus, leaf);
                var s = ProfileCodec.Read(ProfileCodec.Write(r), new List<string>());
                Assert.Equal(r.Hero(Cetus).Talents[leaf], s.Hero(Cetus).Talents[leaf]);
                Assert.Equal(Rules.FreePoints(r, Cetus), Rules.FreePoints(s, Cetus));
            }
            finally { Unregister(); }
        }

        [Fact]
        public void A_hero_created_after_registration_is_not_refunded_on_its_first_load()
        {
            Register("h.cetus.cluster.icy-veins.1");
            try
            {
                var p = Profile.CreateNew(7);
                Assert.Equal(AuthoredStarMigration.CurrentVersion, p.Hero(Cetus).AuthoredMigrationVersion);
                var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
                Assert.Equal(AuthoredStarMigration.CurrentVersion, q.Hero(Cetus).AuthoredMigrationVersion);
            }
            finally { Unregister(); }
        }

        [Fact]
        public void Changed_star_with_dependents_cascades_and_total_points_are_conserved()
        {
            var p = LegacyProfile();
            string root = Pick(p, true);
            int points = p.TalentPoints(Cetus), spent = Rules.SpentPoints(p.Hero(Cetus), Cetus);
            Register(root);
            try
            {
                var notes = new List<string>();
                var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
                var h = q.Hero(Cetus);
                Assert.False(h.Talents.ContainsKey(root));
                Assert.True(HeroTreeLayout.ForHero(Cetus).AllocationsConnected(h, null, h.Keystone));
                Assert.Equal(points, q.TalentPoints(Cetus));
                Assert.Equal(points, Rules.SpentPoints(h, Cetus) + Rules.FreePoints(q, Cetus));
                Assert.True(Rules.SpentPoints(h, Cetus) < spent);
                Assert.True(notes.Count >= 1);
            }
            finally { Unregister(); }
        }

        [Fact]
        public void Changed_keystone_is_unset_and_its_cost_returned()
        {
            var p = LegacyProfile();
            int points = p.TalentPoints(Cetus), spent = Rules.SpentPoints(p.Hero(Cetus), Cetus);
            var ranks = new Dictionary<string, int>(p.Hero(Cetus).Talents);
            Register("h.cetus.key");
            try
            {
                var notes = new List<string>();
                var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
                var h = q.Hero(Cetus);
                Assert.Null(h.Keystone);
                Assert.Equal(ranks.OrderBy(x => x.Key), h.Talents.OrderBy(x => x.Key));
                Assert.Equal(spent - Content.KeystoneCost, Rules.SpentPoints(h, Cetus));
                Assert.Equal(points, q.TalentPoints(Cetus));
                Assert.Equal(Content.KeystoneCost, Rules.FreePoints(q, Cetus));
                Assert.Contains(Content.KeystoneCost + "ポイント", Assert.Single(notes));
                var again = new List<string>();
                var r = ProfileCodec.Read(ProfileCodec.Write(q), again);
                Assert.Empty(again);
                Assert.Null(r.Hero(Cetus).Keystone);
            }
            finally { Unregister(); }
        }

        [Fact]
        public void Unchanged_rules_refund_nothing_and_the_english_note_is_available()
        {
            var p = LegacyProfile();
            var before = new Dictionary<string, int>(p.Hero(Cetus).Talents);
            Register();
            try
            {
                var notes = new List<string>();
                var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
                Assert.Empty(notes);
                Assert.Equal(before.OrderBy(x => x.Key), q.Hero(Cetus).Talents.OrderBy(x => x.Key));
                Assert.Equal("h.cetus.key", q.Hero(Cetus).Keystone);
            }
            finally { Unregister(); }
            string leaf = Pick(p, false);
            Register(leaf);
            bool ja = Loc.Japanese;
            try
            {
                Loc.Japanese = false;
                var notes = new List<string>();
                ProfileCodec.Read(ProfileCodec.Write(p), notes);
                Assert.Contains("effect changed", Assert.Single(notes));
            }
            finally { Loc.Japanese = ja; Unregister(); }
        }

        [Fact]
        public void Registry_fingerprint_includes_the_migration_rules_and_bad_rules_are_rejected()
        {
            StarClusters.RegisterAuthored(Cetus, SmallSet());
            try
            {
                string none = StarClusters.AuthoredRegistryFingerprint;
                StarClusters.RegisterMigrations(Cetus, MigrationRules());
                string retained = StarClusters.AuthoredRegistryFingerprint;
                StarClusters.RegisterMigrations(Cetus, MigrationRules("h.cetus.cluster.icy-veins.1"));
                string changed = StarClusters.AuthoredRegistryFingerprint;
                Assert.NotEqual(none, retained);
                Assert.NotEqual(retained, changed);
                Assert.Throws<InvalidOperationException>(() => StarClusters.RegisterMigrations(Cetus,
                    new[] { new LegacyStarMigration("h.cetus.no-such-star", 1, 1, true) }));
                Assert.Equal(changed, StarClusters.AuthoredRegistryFingerprint);
                // 木を再登録すると規則は消える。
                StarClusters.RegisterAuthored(Cetus, SmallSet());
                Assert.Empty(StarClusters.MigrationsFor(Cetus));
                Assert.Equal(none, StarClusters.AuthoredRegistryFingerprint);
            }
            finally { Unregister(); }
            Assert.Throws<InvalidOperationException>(() => StarClusters.RegisterMigrations(Cetus, MigrationRules()));
        }
    }
}
