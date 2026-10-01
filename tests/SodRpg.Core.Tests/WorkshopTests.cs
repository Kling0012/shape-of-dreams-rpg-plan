using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class WorkshopTests
    {
        private static Profile Rich(ulong seed = 1)
        {
            var p = Profile.CreateNew(seed);
            p.AddMaterial(Materials.Shard, 100000);
            p.AddMaterial(Materials.Tuning, 1000);
            return p;
        }

        private static void Max(Profile p, Upgrade u)
        {
            for (int i = 0; i < Workshop.Get(u).MaxLevel; i++) Rules.BuyUpgrade(p, u);
        }

        [Fact]
        public void Buying_costs_materials_and_stops_at_max()
        {
            var p = Profile.CreateNew(1);
            p.AddMaterial(Materials.Shard, 100);
            Rules.BuyUpgrade(p, Upgrade.BigSatchel);
            Assert.Equal(1, Workshop.Level(p, Upgrade.BigSatchel));
            Assert.Equal(0, p.Material(Materials.Shard));
            Assert.Throws<InvalidOperationException>(() => Rules.BuyUpgrade(p, Upgrade.BigSatchel)); // 素材不足
            var r = Rich();
            Max(r, Upgrade.BigSatchel);
            Assert.Throws<InvalidOperationException>(() => Rules.BuyUpgrade(r, Upgrade.BigSatchel)); // 最大
        }

        [Fact]
        public void Workshop_is_closed_during_runs()
        {
            var p = Rich();
            Rules.BeginRun(p, "w");
            Assert.Throws<InvalidOperationException>(() => Rules.BuyUpgrade(p, Upgrade.WideStash));
        }

        [Fact]
        public void Costs_increase_with_level()
        {
            foreach (var d in Workshop.All)
            {
                Assert.True(d.MaxLevel >= 1);
                for (int i = 1; i < d.MaxLevel; i++)
                {
                    Assert.True(d.Costs[i].Shards > d.Costs[i - 1].Shards, d.Key);
                    Assert.True(d.Costs[i].Tuning >= d.Costs[i - 1].Tuning, d.Key);
                }
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    Assert.False(string.IsNullOrWhiteSpace(d.Name.ToString()));
                    Assert.False(string.IsNullOrWhiteSpace(d.Description.ToString()));
                }
            }
            Loc.Japanese = true;
            Assert.Equal(Workshop.All.Count, Workshop.All.Select(d => d.Key).Distinct().Count());
        }

        [Fact]
        public void Capacities_grow_with_upgrades()
        {
            var p = Rich();
            Max(p, Upgrade.BigSatchel);
            Max(p, Upgrade.WideStash);
            Assert.Equal(Content.SatchelCapacity + 15, Workshop.SatchelCapacity(p));
            Assert.Equal(Content.StashCapacity + 60, Workshop.StashCapacity(p));

            Rules.BeginRun(p, "cap");
            var rng = new Rng(1001);
            for (int i = 0; i < Content.SatchelCapacity + 15; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            for (int i = 0; i < 10; i++) Rules.OnKill(p, MonsterTier.Boss, 1);
            Assert.Equal(Workshop.SatchelCapacity(p), p.Run.Satchel.Count);
        }

        [Fact]
        public void Lantern_recovers_lost_relics_after_two_rooms()
        {
            var p = Rich();
            Rules.BuyUpgrade(p, Upgrade.LostLantern);
            p.LostAndFound.Add(Loot.RollRelic(new Rng(1002), Rarity.Rare, 1));
            Rules.BeginRun(p, "l");
            p.Run.Bounties.Clear();
            Assert.Empty(Rules.OnRoomsCleared(p, 1));
            Assert.Contains(Rules.OnRoomsCleared(p, 2), e => e.Kind == EventKind.Recovered);
        }

        [Fact]
        public void Echo_amplifier_keeps_more_shards()
        {
            var p = Rich();
            Assert.Equal(25, Workshop.Echo(p, 100));
            Max(p, Upgrade.EchoAmp);
            Assert.Equal(45, Workshop.Echo(p, 100));
            Assert.Equal(1, Workshop.Echo(p, 1));
            Assert.Equal(0, Workshop.Echo(p, 0));
            long before = p.Material(Materials.Shard);
            Rules.BeginRun(p, "e");
            p.Run.Bounties.Clear();
            p.Run.SatchelShards = 100;
            Rules.EndRun(p, victory: false);
            Assert.Equal(before + 45, p.Material(Materials.Shard));
        }

        [Fact]
        public void Pact_appraiser_offers_four_pacts()
        {
            var p = Rich();
            Rules.BuyUpgrade(p, Upgrade.PactEye);
            Rules.BeginRun(p, "p");
            Rules.ReachSecurePoint(p);
            Assert.Equal(4, p.Run.OfferedPacts.Count);
        }

        [Fact]
        public void Rerolls_replace_an_unfinished_bounty_with_a_new_kind()
        {
            var p = Rich();
            Rules.BeginRun(p, "r");
            Assert.Equal(0, Rules.RerollsLeft(p));
            Assert.Throws<InvalidOperationException>(() => Rules.RerollBounty(p, 0));
            Rules.EndRun(p, true);

            Max(p, Upgrade.BountyReroll);
            Rules.BeginRun(p, "r2");
            Assert.Equal(2, Rules.RerollsLeft(p));
            var before = p.Run.Bounties.Select(b => b.Kind).ToList();
            Rules.RerollBounty(p, 1);
            Assert.Equal(1, Rules.RerollsLeft(p));
            Assert.Equal(3, p.Run.Bounties.Select(b => b.Kind).Distinct().Count());
            Assert.DoesNotContain(p.Run.Bounties[1].Kind, before);
            Assert.Equal(before[0], p.Run.Bounties[0].Kind);

            p.Run.Bounties[2].Done = true;
            Assert.Throws<InvalidOperationException>(() => Rules.RerollBounty(p, 2));
            Rules.RerollBounty(p, 0);
            Assert.Equal(0, Rules.RerollsLeft(p));
            Assert.Throws<InvalidOperationException>(() => Rules.RerollBounty(p, 0));
        }

        [Fact]
        public void Upgrades_and_rerolls_roundtrip_and_reject_unknown_keys()
        {
            var p = Rich();
            Rules.BuyUpgrade(p, Upgrade.BountyReroll);
            Rules.BuyUpgrade(p, Upgrade.WideStash);
            Rules.BeginRun(p, "u");
            Rules.RerollBounty(p, 0);
            string text = ProfileCodec.Write(p);
            var q = ProfileCodec.Read(text, new List<string>());
            Assert.Equal(1, Workshop.Level(q, Upgrade.BountyReroll));
            Assert.Equal(1, Workshop.Level(q, Upgrade.WideStash));
            Assert.Equal(1, q.Run.RerollsUsed);
            Assert.Equal(text, ProfileCodec.Write(q));
        }
    }
}
