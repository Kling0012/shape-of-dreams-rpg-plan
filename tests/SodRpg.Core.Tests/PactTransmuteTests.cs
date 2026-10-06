using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class PactTransmuteTests
    {
        private static Profile RunAtSecurePoint(ulong seed = 21)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "p");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            return p;
        }

        [Fact]
        public void Secure_point_offers_three_distinct_unbound_pacts()
        {
            var p = RunAtSecurePoint();
            Assert.Equal(Pacts.Offered, p.Run.OfferedPacts.Count);
            Assert.Equal(Pacts.Offered, p.Run.OfferedPacts.Distinct().Count());
            var first = p.Run.OfferedPacts[0];
            Rules.Delve(p, first);
            Assert.Contains(first, p.Run.Pacts);
            Assert.Empty(p.Run.OfferedPacts);
            Rules.ReachSecurePoint(p);
            Assert.DoesNotContain(first, p.Run.OfferedPacts); // 結んだ契約は再提示しない
        }

        [Fact]
        public void Delving_with_an_unoffered_pact_is_rejected()
        {
            var p = RunAtSecurePoint();
            var notOffered = Pacts.All.Select(x => x.Id).Where(x => !p.Run.OfferedPacts.Contains(x)).DefaultIfEmpty((Pact)999).First();
            Assert.Throws<InvalidOperationException>(() => Rules.Delve(p, notOffered));
            Assert.Empty(p.Run.Pacts);
            Assert.Equal(0, p.Run.Heat);
        }

        [Fact]
        public void Plain_delve_binds_no_pact()
        {
            var p = RunAtSecurePoint();
            Rules.Delve(p);
            Assert.Empty(p.Run.Pacts);
            Assert.Equal(1, p.Run.Heat);
        }

        [Fact]
        public void Securing_dissolves_pacts()
        {
            var p = RunAtSecurePoint();
            Rules.Delve(p, p.Run.OfferedPacts[0]);
            Rules.ReachSecurePoint(p);
            Rules.Delve(p, p.Run.OfferedPacts[0]);
            Assert.Equal(2, p.Run.Pacts.Count);
            var ev = Rules.Secure(p);
            Assert.Empty(p.Run.Pacts);
            Assert.Contains(ev, e => e.Text.Contains("2"));
        }

        [Fact]
        public void Pacts_no_longer_lower_stats_only_boons_apply()
        {
            var p = Profile.CreateNew(1);
            var b = Build.Compute(p, "H", 0, new[] { Pact.GlassHeart, Pact.Frenzy, Pact.Burden });
            Assert.Equal(0, b.Get(Stat.MaxHealthPct));
            Assert.Equal(0, b.Get(Stat.Armor));
            Assert.Equal(PactDailyWaypointTestValues.Integer("pacts", "definitions.Frenzy.boons.AttackPct"), b.Get(Stat.AttackPct));
            Assert.Equal(PactDailyWaypointTestValues.Integer("pacts", "definitions.Frenzy.boons.PowerPct"), b.Get(Stat.PowerPct));
        }

        [Fact]
        public void Every_pact_uses_native_curse_tiers_without_stat_penalties()
        {
            foreach (var d in Pacts.All)
            {
                Assert.InRange(d.CurseStrength, 1, 3);
                Assert.Empty(d.Penalties);
            }
        }

        [Fact]
        public void Glass_heart_raises_drop_rate()
        {
            int Drops(Pacts.Totals mods)
            {
                var rng = new Rng(5);
                int n = 0;
                for (int i = 0; i < 40000; i++) n += Loot.RollKill(rng, MonsterTier.Normal, 10, 0, null, mods).Relics.Count;
                return n;
            }
            int plain = Drops(null);
            int glass = Drops(Pacts.Sum(new[] { Pact.GlassHeart }));
            double plainChance = LootEconomyInputs.DropChance(MonsterTier.Normal, 0);
            double bonus = PactDailyWaypointTestValues.Number("pacts", "definitions.GlassHeart.dropBonus");
            double expectedRatio = Math.Min(1, plainChance * (1 + bonus)) / plainChance;
            Assert.InRange((double)glass / plain, expectedRatio - .15, expectedRatio + .15);
        }

        [Fact]
        public void Shard_xp_and_tuning_modifiers_apply_to_kill_rewards()
        {
            var mods = Pacts.Sum(new[] { Pact.Unguarded, Pact.LeadenFeet, Pact.DryDream });
            var rng = new Rng(9);
            var rw = Loot.RollKill(rng, MonsterTier.Boss, 10, 0, null, mods);
            var materials = LootEconomyInputs.Raw("loot").GetProperty("materials").GetProperty("boss");
            double shardMult = PactDailyWaypointTestValues.Number("pacts", "definitions.Unguarded.shardMult");
            Assert.InRange(rw.Shards,
                (int)Math.Round(materials.GetProperty("shardMin").GetInt32() * shardMult),
                (int)Math.Round(materials.GetProperty("shardMax").GetInt32() * shardMult));
            Assert.Equal(materials.GetProperty("tuning").GetInt32() + PactDailyWaypointTestValues.Integer("pacts", "definitions.DryDream.tuningOnElite"), rw.Tuning);
            Assert.Equal((int)Math.Round(Content.KillXp(MonsterTier.Boss) * PactDailyWaypointTestValues.Number("pacts", "definitions.LeadenFeet.xpMult")), rw.Xp);
        }

        [Fact]
        public void Cursed_hoard_doubles_depth_bonus_and_removes_echoes()
        {
            var p = RunAtSecurePoint();
            p.Run.OfferedPacts.Clear();
            p.Run.OfferedPacts.Add(Pact.CursedHoard);
            Rules.Delve(p, Pact.CursedHoard);
            p.Run.SatchelShards = 40;
            Rules.Secure(p);
            int multiplier = PactDailyWaypointTestValues.Integer("pacts", "doubleDepthBonusMultiplier");
            int divisor = PactDailyWaypointTestValues.Integer("economy", "expedition.secureBonusDivisor");
            Assert.Equal(40 + multiplier * (40 * 1 / divisor), p.Material(Materials.Shard));

            var q = RunAtSecurePoint(33);
            q.Run.OfferedPacts.Clear();
            q.Run.OfferedPacts.Add(Pact.CursedHoard);
            Rules.Delve(q, Pact.CursedHoard);
            q.Run.SatchelShards = 40;
            Rules.EndRun(q, victory: false);
            Assert.Equal(0, q.Material(Materials.Shard));
        }

        [Fact]
        public void Pacts_roundtrip_through_codec()
        {
            var p = RunAtSecurePoint();
            var chosen = p.Run.OfferedPacts[0];
            Rules.Delve(p, chosen);
            Rules.ReachSecurePoint(p);
            string text = ProfileCodec.Write(p);
            var q = ProfileCodec.Read(text, new List<string>());
            Assert.Equal(p.Run.Pacts, q.Run.Pacts);
            Assert.Equal(p.Run.OfferedPacts, q.Run.OfferedPacts);
            Assert.Equal(text, ProfileCodec.Write(q));
        }

        private static Profile WithStash(Rarity r, int count, ulong seed = 3)
        {
            var p = Profile.CreateNew(seed);
            var rng = new Rng(seed + 1000); // プロフィールの乱数と別系列にする（同じ種だと個体IDが重なる）
            for (int i = 0; i < count; i++) p.Stash.Add(Loot.RollRelic(rng, r, 5 + i));
            p.AddMaterial(Materials.Shard, 1000);
            return p;
        }

        [Fact]
        public void Transmute_consumes_three_weakest_and_yields_next_rarity()
        {
            var p = WithStash(Rarity.Rare, Content.TransmuteInputs(Rarity.Rare) + 2);
            var weakest = Rules.TransmuteCandidates(p, Rarity.Rare).Take(Content.TransmuteInputs(Rarity.Rare)).Select(x => x.Uid).ToList();
            Rules.Transmute(p, Rarity.Rare);
            Assert.Equal(3, p.Stash.Count);
            Assert.DoesNotContain(p.Stash, x => weakest.Contains(x.Uid));
            Assert.Single(p.Stash, x => x.Rarity == Rarity.Epic);
            Assert.Equal(1000 - Rules.TransmuteCost(Rarity.Rare), p.Material(Materials.Shard));
        }

        [Fact]
        public void Transmute_skips_locked_and_equipped_relics()
        {
            var p = WithStash(Rarity.Common, Content.TransmuteInputs(Rarity.Common) + 1);
            Rules.ToggleLock(p, p.Stash[0].Uid);
            Rules.Equip(p, "H", p.Stash[1].Uid);
            Assert.Equal(Content.TransmuteInputs(Rarity.Common) - 1, Rules.TransmuteCandidates(p, Rarity.Common).Count);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(p, Rarity.Common));
            Assert.Equal(Content.TransmuteInputs(Rarity.Common) + 1, p.Stash.Count);
        }

        [Fact]
        public void Epics_become_a_legendary_and_legendaries_cannot_transmute()
        {
            var p = WithStash(Rarity.Epic, Content.TransmuteInputs(Rarity.Epic));
            p.AddMaterial(Materials.Tuning, Rules.TransmuteTuning(Rarity.Epic));
            Rules.Transmute(p, Rarity.Epic);
            var r = Assert.Single(p.Stash);
            Assert.Equal(Rarity.Legendary, r.Rarity);
            Assert.NotNull(r.UniqueId);
            Assert.Contains(r.UniqueId, p.Codex);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(p, Rarity.Legendary));
        }

        [Fact]
        public void Transmute_needs_shards()
        {
            var p = WithStash(Rarity.Uncommon, Content.TransmuteInputs(Rarity.Uncommon));
            p.Materials.Clear();
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(p, Rarity.Uncommon));
            Assert.Equal(Content.TransmuteInputs(Rarity.Uncommon), p.Stash.Count);
        }

        [Fact]
        public void Codex_milestones_grant_talent_points_up_to_four()
        {
            var p = Profile.CreateNew(1);
            Assert.Equal(0, p.TalentPoints("Hero_Vesper"));
            for (int i = 0; i < 5; i++) p.Codex.Add("x" + i);
            Assert.Equal(0, p.CodexBonusPoints);
            p.Codex.Add("x5");
            Assert.Equal(1, p.CodexBonusPoints);
            Assert.Equal(1, p.TalentPoints("Hero_Vesper"));
            for (int i = 6; i < 40; i++) p.Codex.Add("x" + i);
            Assert.Equal(Content.MaxCodexBonus, p.CodexBonusPoints);
        }
    }
}
