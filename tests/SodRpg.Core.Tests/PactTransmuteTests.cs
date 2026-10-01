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
            var notOffered = Pacts.All.Select(x => x.Id).First(x => !p.Run.OfferedPacts.Contains(x));
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
        public void Pact_penalties_and_boons_shape_the_build()
        {
            var p = Profile.CreateNew(1);
            var b = Build.Compute(p, "H", 0, new[] { Pact.GlassHeart, Pact.Frenzy });
            Assert.Equal(-15, b.Get(Stat.MaxHealthPct));
            Assert.Equal(-25, b.Get(Stat.Armor));
            Assert.Equal(15, b.Get(Stat.AttackPct));
            Assert.Equal(15, b.Get(Stat.PowerPct));
            var d = Build.Decode(b.Encode());
            Assert.Equal(b.Stats, d.Stats);
        }

        [Fact]
        public void Every_pact_has_a_downside_or_a_risk_and_an_upside()
        {
            foreach (var d in Pacts.All)
            {
                bool downside = d.Penalties.Length > 0 || d.NoEcho;
                bool upside = d.DropBonus > 0 || d.Luck > 0 || d.ShardMult > 1 || d.XpMult > 1 || d.TuningOnElite > 0 || d.DoubleDepthBonus || d.Boons.Length > 0;
                Assert.True(downside && upside, d.Id.ToString());
                Assert.All(d.Penalties, s => Assert.True(s.Value < 0));
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    Assert.False(string.IsNullOrWhiteSpace(d.Name.ToString()));
                    Assert.False(string.IsNullOrWhiteSpace(d.Description.ToString()));
                }
            }
            Loc.Japanese = true;
        }

        [Fact]
        public void Glass_heart_raises_drop_rate()
        {
            int Drops(Pacts.Totals mods)
            {
                var rng = new Rng(5);
                int pity = 0, n = 0;
                for (int i = 0; i < 40000; i++) n += Loot.RollKill(rng, MonsterTier.Normal, 10, 0, ref pity, null, mods).Relics.Count;
                return n;
            }
            int plain = Drops(null);
            int glass = Drops(Pacts.Sum(new[] { Pact.GlassHeart }));
            Assert.InRange((double)glass / plain, 1.25, 1.55);
        }

        [Fact]
        public void Shard_xp_and_tuning_modifiers_apply_to_kill_rewards()
        {
            var mods = Pacts.Sum(new[] { Pact.Unguarded, Pact.LeadenFeet, Pact.DryDream });
            var rng = new Rng(9);
            int pity = 0;
            var rw = Loot.RollKill(rng, MonsterTier.Boss, 10, 0, ref pity, null, mods);
            Assert.InRange(rw.Shards, 30, 45);   // 20〜30 ×1.5
            Assert.Equal(2, rw.Tuning);          // 1 + 乾いた夢
            Assert.Equal(75, rw.Xp);             // 50 ×1.5
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
            Assert.Equal(40 + 2 * (40 * 1 / 4), p.Material(Materials.Shard));

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
            var chosen = p.Run.OfferedPacts[1];
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
            var p = WithStash(Rarity.Rare, 5);
            var weakest = Rules.TransmuteCandidates(p, Rarity.Rare).Take(3).Select(x => x.Uid).ToList();
            Rules.Transmute(p, Rarity.Rare);
            Assert.Equal(3, p.Stash.Count);
            Assert.DoesNotContain(p.Stash, x => weakest.Contains(x.Uid));
            Assert.Single(p.Stash, x => x.Rarity == Rarity.Epic);
            Assert.Equal(1000 - Rules.TransmuteCost(Rarity.Rare), p.Material(Materials.Shard));
        }

        [Fact]
        public void Transmute_skips_locked_and_equipped_relics()
        {
            var p = WithStash(Rarity.Common, 4);
            Rules.ToggleLock(p, p.Stash[0].Uid);
            Rules.Equip(p, "H", p.Stash[1].Uid);
            Assert.Equal(2, Rules.TransmuteCandidates(p, Rarity.Common).Count);
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(p, Rarity.Common));
            Assert.Equal(4, p.Stash.Count);
        }

        [Fact]
        public void Three_epics_become_a_legendary_and_legendaries_cannot_transmute()
        {
            var p = WithStash(Rarity.Epic, 3);
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
            var p = WithStash(Rarity.Uncommon, 3);
            p.Materials.Clear();
            Assert.Throws<InvalidOperationException>(() => Rules.Transmute(p, Rarity.Uncommon));
            Assert.Equal(3, p.Stash.Count);
        }

        [Fact]
        public void Codex_milestones_grant_talent_points_up_to_four()
        {
            var p = Profile.CreateNew(1);
            Assert.Equal(0, p.TalentPoints);
            for (int i = 0; i < 5; i++) p.Codex.Add("x" + i);
            Assert.Equal(0, p.CodexBonusPoints);
            p.Codex.Add("x5");
            Assert.Equal(1, p.CodexBonusPoints);
            Assert.Equal(1, p.TalentPoints);
            for (int i = 6; i < 40; i++) p.Codex.Add("x" + i);
            Assert.Equal(Content.MaxCodexBonus, p.CodexBonusPoints);
        }
    }
}
