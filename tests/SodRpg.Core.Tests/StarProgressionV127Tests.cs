using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarProgressionV127Tests
    {
        private const string Hero = "Hero_Vesper";
        private const string Other = "Hero_Husk";

        private static Profile Funded(int points = 150)
        {
            var p = Profile.CreateNew(17);
            p.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(points);
            return p;
        }

        private static void OpenCore(Profile p)
        {
            for (int i = 0; i < 3; i++)
            {
                Rules.AddTalentRank(p, Hero, "h.vesper.fire");
                Rules.AddTalentRank(p, Hero, "h.vesper.wall");
            }
        }

        private static TalentDef[] Route() => HeroSigils.TreeFor(Hero)
            .Where(t => t.RouteId == HeroSigils.TreeFor(Hero).First(n => n.RouteId != null).RouteId)
            .OrderBy(t => t.RouteOrder).ToArray();

        [Fact]
        public void Every_curve_boundary_charges_the_next_point_and_caps_at_150()
        {
            int total = 0;
            Assert.Equal(0, StarProgression.Points(-1));
            Assert.Equal(0, StarProgression.Points(0));
            for (int k = 1; k <= 150; k++)
            {
                Assert.Equal(6 * k + 50, StarProgression.CostForPoint(k));
                total += 6 * k + 50;
                Assert.Equal(total, StarProgression.TotalXpForPoints(k));
                Assert.Equal(k - 1, StarProgression.Points(total - 1));
                Assert.Equal(k, StarProgression.Points(total));
            }
            Assert.Equal(75450, total);
            Assert.Equal(150, StarProgression.Points(int.MaxValue));
            Assert.Equal(75450, StarProgression.TotalXpForPoints(int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => StarProgression.CostForPoint(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => StarProgression.CostForPoint(151));
        }

        [Fact]
        public void Travelers_earn_independently_while_codex_and_test_bonuses_apply_to_each()
        {
            int previous = Profile.TestBonusPoints;
            try
            {
                Profile.TestBonusPoints = 0;
                var p = Funded(8);
                p.DreamLevel = 30;
                Assert.Equal(8, p.TalentPoints(Hero));
                Assert.Equal(0, p.TalentPoints(Other));
                for (int i = 0; i < 30; i++) p.Codex.Add("entry." + i);
                Profile.TestBonusPoints = 9;
                Assert.Equal(21, p.TalentPoints(Hero));
                Assert.Equal(13, p.TalentPoints(Other));
                StarProgression.AddXp(p.Hero(Other), 56);
                Assert.Equal(14, p.TalentPoints(Other));
                Assert.Equal(21, p.TalentPoints(Hero));
                Profile.TestBonusPoints = int.MaxValue;
                Assert.Equal(int.MaxValue, p.TalentPoints(Hero));
                Profile.TestBonusPoints = -1;
                Assert.Equal(12, p.TalentPoints(Hero));
            }
            finally { Profile.TestBonusPoints = previous; }
        }

        [Theory]
        [InlineData(MonsterTier.Lesser, 1)]
        [InlineData(MonsterTier.Normal, 1)]
        [InlineData(MonsterTier.MiniBoss, 5)]
        [InlineData(MonsterTier.Boss, 20)]
        public void Kill_rewards_use_original_tier_and_double_once_for_nightmare_or_variant(MonsterTier tier, int expected)
        {
            foreach (var mode in new[] { 0, 1, 2, 3 })
            {
                var p = Profile.CreateNew(17);
                Rules.BeginRun(p, "run", heroKey: Hero);
                p.Run.Bounties.Clear();
                Rules.OnKill(p, tier, 1, (mode & 1) != 0 ? NightmareAffix.Ironclad : NightmareAffix.None,
                    Hero, variantId: (mode & 2) != 0 ? Variants.All[0].Id : null);
                Assert.Equal(expected * (mode == 0 ? 1 : 2), p.Hero(Hero).StarXp);
                Assert.Equal(1, p.Hero(Hero).Kills);
                Assert.Equal(0, p.Hero(Other).StarXp);
            }
        }

        [Fact]
        public void Explicit_kill_owner_does_not_overwrite_run_completion_owner()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "run", heroKey: Hero);
            Rules.OnKill(p, MonsterTier.Boss, 1, heroKey: Other);
            Rules.OnKill(p, MonsterTier.Normal, 1); // falls back to the recorded run Traveler
            Rules.EndRun(p, true);
            Assert.Equal(101, p.Hero(Hero).StarXp);
            Assert.Equal(20, p.Hero(Other).StarXp);
        }

        [Fact]
        public void Securing_and_victory_reward_once_even_without_kills_and_after_reload()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "run", heroKey: Hero);
            Rules.ReachSecurePoint(p);
            Rules.Secure(p);
            Assert.Equal(20, p.Hero(Hero).StarXp);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Rules.Secure(q);
            Assert.Equal(20, q.Hero(Hero).StarXp);
            Rules.ReachSecurePoint(q);
            Rules.Secure(q);
            Assert.Equal(40, q.Hero(Hero).StarXp);
            Rules.EndRun(q, true);
            Rules.EndRun(q, true);
            Rules.Secure(q);
            Assert.Equal(140, q.Hero(Hero).StarXp);
            Assert.Equal(0, q.Hero(Other).StarXp);
        }

        [Fact]
        public void Victory_is_100_not_an_extra_secure_and_defeat_keeps_only_earned_xp()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "victory", heroKey: Hero);
            Rules.EndRun(p, true);
            Assert.Equal(100, p.Hero(Hero).StarXp);
            Rules.BeginRun(p, "defeat", heroKey: Other);
            Rules.OnKill(p, MonsterTier.MiniBoss, 1);
            Rules.EndRun(p, false);
            Assert.Equal(5, p.Hero(Other).StarXp);
            Rules.OnKill(p, MonsterTier.Boss, 1, heroKey: Other);
            Assert.Equal(5, p.Hero(Other).StarXp);
        }

        [Fact]
        public void Resuming_legacy_run_binds_missing_owner_without_changing_known_owner()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "old");
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Rules.BeginRun(p, "old", heroKey: Hero);
            Rules.BeginRun(p, "old", heroKey: Other);
            Assert.Equal(Hero, p.Run.HeroKey);
            Rules.EndRun(p, true);
            Assert.Equal(100, p.Hero(Hero).StarXp);
            Assert.Equal(0, p.Hero(Other).StarXp);
        }

        [Fact]
        public void Dream_level_progression_does_not_award_star_points_or_the_star_hint()
        {
            var p = Profile.CreateNew(17);
            var events = Rules.AddXp(p, int.MaxValue);
            Assert.Equal(Content.MaxDreamLevel, p.DreamLevel);
            Assert.Equal(0, p.TalentPoints(Hero));
            Assert.DoesNotContain((int)Hint.TalentPoints, p.SeenHints);
            Assert.Contains(events, e => e.Kind == EventKind.LevelUp);
            Rules.BeginRun(p, "run", heroKey: Hero);
            for (int i = 0; i < 3; i++) Rules.OnKill(p, MonsterTier.Boss, 1);
            Assert.Contains((int)Hint.TalentPoints, p.SeenHints);
            Assert.Equal(1, StarProgression.Points(p.Hero(Hero).StarXp));
        }

        [Fact]
        public void Experience_migration_and_reward_arithmetic_saturate_without_wrapping()
        {
            var h = new HeroState { StarXp = -10 };
            Assert.Equal(0, h.StarXp);
            StarProgression.AddXp(h, int.MaxValue);
            StarProgression.AddXp(h, 100);
            StarProgression.AddXp(h, -1);
            Assert.Equal(int.MaxValue, h.StarXp);
            Assert.Equal(int.MaxValue, StarProgression.LegacyXp(int.MaxValue));
            Assert.Equal(0, StarProgression.LegacyXp(-1));
            var p = Profile.CreateNew(17);
            p.Hero(Hero).StarXp = int.MaxValue - 1;
            p.Hero(Hero).Kills = int.MaxValue;
            Rules.BeginRun(p, "run", heroKey: Hero);
            p.Run.Kills = int.MaxValue;
            p.Stats.Kills = int.MaxValue;
            Rules.OnKill(p, MonsterTier.Boss, 1);
            Rules.EndRun(p, true);
            Assert.Equal(int.MaxValue, p.Hero(Hero).StarXp);
            Assert.Equal(int.MaxValue, p.Hero(Hero).Kills);
            Assert.Equal(int.MaxValue, p.Stats.Kills);
            Assert.Equal(int.MaxValue, p.LastReport.Kills);
        }

        [Fact]
        public void Missing_star_xp_migrates_from_each_kill_count_but_explicit_zero_does_not()
        {
            var p = Profile.CreateNew(17);
            p.Hero(Hero).Kills = 101;
            p.Hero(Other).Kills = int.MaxValue;
            var migrated = ProfileCodec.Read(LegacySave(p), new List<string>());
            Assert.Equal(121, migrated.Hero(Hero).StarXp);
            Assert.Equal(int.MaxValue, migrated.Hero(Other).StarXp);
            var explicitZero = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(0, explicitZero.Hero(Hero).StarXp);
            Assert.Equal(0, explicitZero.Hero(Other).StarXp);
        }

        [Fact]
        public void Migration_preserves_affordable_core_ids_and_ranks_and_refunds_only_overbudget_hero()
        {
            var p = Profile.CreateNew(17);
            p.Hero(Hero).Kills = 10000;
            p.Hero(Hero).Talents["h.vesper.fire"] = 3;
            p.Hero(Hero).Talents["h.vesper.deep.thorns"] = 2;
            p.Hero(Hero).Talents["h.vesper.fourth"] = 2; // old two-rank costly node becomes one
            p.Hero(Hero).Keystone = "h.vesper.key";
            p.Hero(Other).Kills = 1;
            p.Hero(Other).Talents["h.husk.dark"] = 3;
            p.Hero(Other).Keystone = "h.husk.key";
            var notes = new List<string>();
            var q = ProfileCodec.Read(LegacySave(p), notes);
            Assert.Equal(3, q.Hero(Hero).Talents["h.vesper.fire"]);
            Assert.Equal(2, q.Hero(Hero).Talents["h.vesper.deep.thorns"]);
            Assert.Equal(1, q.Hero(Hero).Talents["h.vesper.fourth"]);
            Assert.Equal("h.vesper.key", q.Hero(Hero).Keystone);
            Assert.Equal(12, Rules.SpentPoints(q.Hero(Hero)));
            Assert.Empty(q.Hero(Other).Talents);
            Assert.Null(q.Hero(Other).Keystone);
            Assert.Single(notes);
            Assert.Equal(0, q.Material(Materials.Shard));
            Assert.False(Rules.DeepStarsOpen(q, Hero));
            Assert.Equal(0, Build.Compute(q, Hero, 0).Get(Stat.FourthAttackShift));
        }

        [Fact]
        public void Keystone_only_overbudget_saves_are_also_reset_and_respec_is_free()
        {
            var p = Profile.CreateNew(17);
            p.Hero(Hero).Keystone = "h.vesper.key";
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Null(q.Hero(Hero).Keystone);
            Assert.Single(notes);
            p = Funded(12);
            OpenCore(p);
            p.AddMaterial(Materials.Shard, 7);
            Rules.ResetTalents(p, Hero);
            Assert.Equal(12, Rules.FreePoints(p, Hero));
            Assert.Equal(StarProgression.TotalXpForPoints(12), p.Hero(Hero).StarXp);
            Assert.Equal(7, p.Material(Materials.Shard));
        }

        [Fact]
        public void Clone_and_save_keep_independent_xp_and_run_reward_state()
        {
            var p = Funded(17);
            p.Hero(Other).StarXp = 89;
            Rules.BeginRun(p, "run", heroKey: Other);
            Rules.Secure(p);
            var clone = p.Clone();
            StarProgression.AddXp(clone.Hero(Hero), 10);
            Assert.Equal(StarProgression.TotalXpForPoints(17), p.Hero(Hero).StarXp);
            Assert.Equal(StarProgression.TotalXpForPoints(17) + 10, clone.Hero(Hero).StarXp);
            Assert.Equal(Other, clone.Run.HeroKey);
            Assert.True(clone.Run.StarSecureRewarded);
            var q = ProfileCodec.Read(ProfileCodec.Write(clone), new List<string>());
            Assert.Equal(clone.Hero(Hero).StarXp, q.Hero(Hero).StarXp);
            Assert.Equal(109, q.Hero(Other).StarXp);
            Assert.Equal(Other, q.Run.HeroKey);
            Rules.Secure(q);
            Assert.Equal(109, q.Hero(Other).StarXp);
        }

        [Fact]
        public void Routes_require_core_and_every_ancestor_but_only_one_rank_per_predecessor()
        {
            var p = Funded();
            var h = p.Hero(Hero);
            var route = Route();
            Assert.False(Rules.TalentUnlocked(h, Hero, route[0]));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, route[0].Id));
            OpenCore(p);
            Assert.True(Rules.TalentUnlocked(h, Hero, route[0]));
            Assert.False(Rules.TalentUnlocked(h, Other, route[0]));
            h.Talents[route[1].Id] = 1;
            Assert.False(Rules.TalentUnlocked(h, Hero, route[2]));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, route[2].Id));
            Rules.AddTalentRank(p, Hero, route[0].Id);
            Assert.True(Rules.TalentUnlocked(h, Hero, route[2]));
            Rules.AddTalentRank(p, Hero, route[2].Id);
            Assert.Equal(6, Rules.Tier1Ranks(h, Hero));
            h.Talents[route[0].Id] = 0;
            Assert.False(Rules.TalentUnlocked(h, Hero, route[2]));
        }

        [Fact]
        public void Forged_core_rank_cannot_open_deep_stars_and_route_cycles_do_not_unlock()
        {
            var p = Funded();
            var h = p.Hero(Hero);
            h.Talents["h.vesper.fire"] = int.MaxValue;
            Assert.Equal(3, Rules.Tier1Ranks(h, Hero));
            Assert.False(Rules.DeepStarsOpen(p, Hero));
            Assert.Equal(int.MaxValue, Rules.SpentPoints(h));
            h.Talents.Clear();
            OpenCore(p);
            var route = Route();
            h.Talents[route[1].Id] = 1;
            var cycle = new TalentDef(route[1].Id, Line.Offense, new Txt("cycle", "cycle"), Stat.Armor, 1, 3)
            {
                HeroKey = Hero, Tier = 2, RouteId = route[1].RouteId,
                RouteOrder = 2, PrerequisiteId = route[1].Id,
            };
            Assert.False(Rules.TalentUnlocked(h, Hero, cycle));
        }

        [Fact]
        public void Capstone_costs_three_and_remains_separate_from_exclusive_core_keystone()
        {
            var p = Funded(17);
            var h = p.Hero(Hero);
            var route = Route();
            OpenCore(p);
            h.Kills = 600;
            Rules.SetKeystone(p, Hero, "h.vesper.key");
            foreach (var node in route.Take(6)) Rules.AddTalentRank(p, Hero, node.Id);
            Assert.Equal(2, Rules.FreePoints(p, Hero));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, route[6].Id));
            h.StarXp = StarProgression.TotalXpForPoints(18);
            Rules.AddTalentRank(p, Hero, route[6].Id);
            Assert.Equal(0, Rules.FreePoints(p, Hero));
            Assert.Equal(18, Rules.SpentPoints(h));
            Assert.Equal("h.vesper.key", h.Keystone);
            Assert.Equal(1, h.Talents[route[6].Id]);
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, route[6].Id));
            Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, Hero, route[6].Id));
        }

        [Fact]
        public void Saved_locked_routes_and_ring_keep_ranks_but_have_no_effect_until_unlocked()
        {
            var p = Funded();
            var route = Route();
            var ring = HeroSigils.TreeFor(Hero).First(t => t.IsDreamRing && t.Stat == Stat.Armor);
            p.Hero(Hero).Talents[route[2].Id] = 2;
            p.Hero(Hero).Talents[ring.Id] = 5;
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(notes);
            Assert.Equal(2, q.Hero(Hero).Talents[route[2].Id]);
            Assert.Equal(5, q.Hero(Hero).Talents[ring.Id]);
            var locked = Build.Compute(q, Hero, 0);
            Assert.Empty(locked.Stats);
            Assert.Empty(locked.Powers);
            Assert.Empty(locked.Links);
            Assert.False(Rules.TalentUnlocked(q.Hero(Hero), Hero, ring));
            OpenCore(q);
            Assert.True(Rules.TalentUnlocked(q.Hero(Hero), Hero, ring));
            Assert.Equal(ring.PerRank * 5, Build.Compute(q, Hero, 0).Get(Stat.Armor));
            Assert.False(Rules.TalentUnlocked(q.Hero(Hero), Hero, route[2]));
            Rules.AddTalentRank(q, Hero, route[0].Id);
            Rules.AddTalentRank(q, Hero, route[1].Id);
            Assert.True(Rules.TalentUnlocked(q.Hero(Hero), Hero, route[2]));
        }

        private static string LegacySave(Profile p)
        {
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(p));
            Assert.True(root.TryGet("body", out object body));
            var legacy = WithoutStarXp(body);
            string checksum;
            using (var sha = SHA256.Create())
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(legacy))))
                    .Replace("-", "").ToLowerInvariant();
            return Json.Write(new JsonObject().Add("format", ProfileCodec.Format)
                .Add("version", (long)Profile.CurrentVersion).Add("checksum", checksum).Add("body", legacy));
        }

        private static object WithoutStarXp(object value)
        {
            if (value is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var kv in obj.Properties)
                    if (kv.Key != "starXp") copy.Add(kv.Key, WithoutStarXp(kv.Value));
                return copy;
            }
            if (value is List<object> list) return list.Select(WithoutStarXp).ToList();
            return value;
        }
    }
}
