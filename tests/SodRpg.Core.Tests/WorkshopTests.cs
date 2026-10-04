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
            Assert.Equal(Content.SatchelCapacity + 50, Workshop.SatchelCapacity(p));
            Assert.Equal(Content.StashCapacity + 340, Workshop.StashCapacity(p));

            Rules.BeginRun(p, "cap");
            var rng = new Rng(1001);
            for (int i = 0; i < Content.SatchelCapacity + 50; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            for (int i = 0; i < 10; i++) Rules.OnKill(p, MonsterTier.Boss, 1);
            Assert.Equal(Workshop.SatchelCapacity(p), p.Run.Satchel.Count);
        }

        [Fact]
        public void Retired_upgrades_are_refunded_on_load()
        {
            var p = Profile.CreateNew(1);
            // v1.4 までの保存：廃止した強化を解放済み
            p.Upgrades[Upgrade.LostLantern] = 1;
            p.Upgrades[Upgrade.EchoAmp] = 2;
            p.Upgrades[Upgrade.PactEye] = 1;
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(q.Upgrades);
            Assert.Equal(250 + 150 + 300 + 300, q.Material(Materials.Shard));
            Assert.Equal(3 + 2 + 4 + 5, q.Material(Materials.Tuning));
            Assert.Equal(3, notes.Count(n => n.Contains("返しました")));
            Assert.Equal(Content.RoomsToRecoverLost, Workshop.RoomsToRecover(q));
            Assert.Equal(25, Workshop.EchoPercent(q));
            Assert.Equal(Pacts.Offered, Workshop.PactsOffered(q));
        }

        [Fact]
        public void Six_convenience_upgrades_are_available()
        {
            Assert.Equal(
                new[] { Upgrade.BigSatchel, Upgrade.WideStash, Upgrade.BountyReroll, Upgrade.EchoLantern, Upgrade.LostMap, Upgrade.PactStars },
                Workshop.All.Select(d => d.Id).ToArray());
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
