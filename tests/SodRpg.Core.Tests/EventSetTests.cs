using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class EventSetTests
    {
        private static Profile AtEvent(DreamEvent e, ulong seed = 3)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "ev");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = e;
            return p;
        }

        [Fact]
        public void Merchant_sells_an_unsecured_relic_for_shards()
        {
            var p = AtEvent(DreamEvent.Merchant);
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Merchant, out _));
            p.AddMaterial(Materials.Shard, 100);
            Rules.UseEvent(p, DreamEvent.Merchant);
            Assert.Equal(100 - DreamEvents.MerchantCost(0), p.Material(Materials.Shard));
            var r = Assert.Single(p.Run.Satchel);
            Assert.True(r.Rarity >= Rarity.Uncommon);
            Assert.Equal(DreamEvent.None, p.Run.OfferedEvent);
            Assert.Throws<InvalidOperationException>(() => Rules.UseEvent(p, DreamEvent.Merchant)); // 1回だけ
        }

        [Fact]
        public void Chalice_doubles_or_loses_unsecured_shards()
        {
            int wins = 0, losses = 0;
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var p = AtEvent(DreamEvent.Chalice, seed);
                p.Run.SatchelShards = 40;
                Rules.UseEvent(p, DreamEvent.Chalice);
                if (p.Run.SatchelShards == 80) wins++;
                else
                {
                    Assert.Equal(0, p.Run.SatchelShards);
                    losses++;
                }
            }
            Assert.InRange(wins, 70, 130);
            Assert.Equal(200, wins + losses);
        }

        [Fact]
        public void Lantern_recovers_the_best_lost_relic()
        {
            var p = AtEvent(DreamEvent.Lantern);
            var rng = new Rng(1004);
            p.LostAndFound.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            var best = Loot.RollRelic(rng, Rarity.Epic, 10);
            p.LostAndFound.Add(best);
            Rules.UseEvent(p, DreamEvent.Lantern);
            Assert.Contains(best, p.Run.Satchel);
            Assert.Single(p.LostAndFound);
        }

        [Fact]
        public void Event_survives_codec()
        {
            var p = AtEvent(DreamEvent.Fountain);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(DreamEvent.Fountain, q.Run.OfferedEvent);
        }

        private static Relic SetPiece(Profile p, string uniqueId)
        {
            Content.TryGetUnique(uniqueId, out var u);
            var r = new Relic { Uid = "s" + p.Stash.Count, BaseId = u.BaseId, UniqueId = uniqueId, Rarity = Rarity.Legendary, ItemLevel = 1 };
            p.Stash.Add(r);
            return r;
        }

        [Fact]
        public void Every_set_has_six_pieces_one_per_slot()
        {
            foreach (var set in Content.Sets)
            {
                var pieces = Content.Uniques.Where(u => u.SetId == set.Id).ToList();
                Assert.Equal(6, pieces.Count);
                Assert.Equal(6, pieces.Select(u => Content.GetBase(u.BaseId).Slot).Distinct().Count());
                if (set.BossTypeName != null)
                {
                    // #48: boss sets carry their stages as fixed boss profiles, not stat/power rows.
                    Assert.Empty(set.TwoPiece);
                    Assert.Empty(set.ThreePiece);
                    Assert.NotEmpty(set.BossStages);
                }
                else
                {
                    Assert.NotEmpty(set.TwoPiece);
                    Assert.NotEmpty(set.ThreePiece);
                }
                Assert.False(string.IsNullOrWhiteSpace(set.Describe()));
            }
        }

        [Theory]
        [InlineData("set.runupcharge", Stat.AttackFlat, Power.RunUp, Power.StrafeShot)]
        [InlineData("set.crystalcircuit", Stat.PowerFlat, Power.Finale, Power.CrystalResonance)]
        public void Ordinary_set_fixed_stats_and_bonuses_activate_only_at_their_thresholds(
            string setId, Stat flat, Power damage, Power unchanged)
        {
            using var canonical = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "EquipmentSets.json")));
            var definition = canonical.RootElement.GetProperty("sets").EnumerateArray()
                .Single(x => x.GetProperty("id").GetString() == setId);
            int flatValue = definition.GetProperty("twoPiece").EnumerateArray()
                .Single(x => x.GetProperty("stat").GetString() == flat.ToString()).GetProperty("value").GetInt32();
            int PowerValue(Power power) => definition.GetProperty("threePiece").EnumerateArray()
                .Single(x => x.GetProperty("power").GetString() == power.ToString()).GetProperty("value").GetInt32();
            int damageValue = Math.Min(Content.PowerCap(damage), PowerValue(damage));
            int unchangedValue = Math.Min(Content.PowerCap(unchanged), PowerValue(unchanged));
            var p = Profile.CreateNew(1);
            var ids = Content.Uniques.Where(u => u.SetId == setId).Select(u => u.Id).ToArray();
            var stages = new List<Build>();
            var implicitFlats = new List<int>();
            int implicitFlat = 0;
            foreach (var id in ids)
            {
                var relic = SetPiece(p, id);
                relic.Affixes.Clear();
                relic.Powers.Clear();
                if (relic.Implicit.Stat == flat) implicitFlat += relic.Implicit.Value;
                implicitFlats.Add(implicitFlat);
                Rules.Equip(p, "H", relic.Uid);
                stages.Add(Build.Compute(p, "H", 0));
            }
            Assert.Equal(implicitFlats[0], stages[0].Get(flat));
            Assert.Equal(Math.Min(Content.StatCap(flat), implicitFlats[1] + flatValue), stages[1].Get(flat));
            Assert.Equal(0, stages[1].Get(damage));
            Assert.Equal(damageValue, stages[2].Get(damage));
            Assert.Equal(unchangedValue, stages[2].Get(unchanged));
            Assert.Equal(damageValue, stages[5].Get(damage));
            Assert.Equal(Math.Min(Content.StatCap(flat), implicitFlats[5] + flatValue), stages[5].Get(flat));
            if (damage == Power.Finale)
            {
                var runtime = new PowerRuntime(stages[2], 0);
                Assert.Equal(0f, runtime.TakeFinale(0f, 0));
                Assert.Equal(0f, runtime.TakeFinale(1f, 1));
                Assert.Equal(damageValue / 100f, runtime.TakeFinale(2f, 2));
                Assert.Equal(0f, runtime.TakeFinale(3f, 2));
            }
        }

        [Fact]
        public void Set_pieces_drop_as_legendaries()
        {
            var rng = new Rng(55);
            var seen = new HashSet<string>();
            for (int i = 0; i < 4000; i++) seen.Add(Loot.RollRelic(rng, Rarity.Legendary, 10).UniqueId);
            // Boss-limited pieces never enter the generic pool (#48); their exclusion is covered separately.
            foreach (var u in Content.Uniques.Where(x => x.SetId != null && !BossSets.IsExclusive(x))) Assert.Contains(u.Id, seen);
        }
    }
}
