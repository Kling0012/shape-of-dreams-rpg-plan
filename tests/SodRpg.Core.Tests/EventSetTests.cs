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
        public void Events_appear_about_half_the_time_and_lantern_needs_lost_relics()
        {
            var rng = new Rng(9);
            var p = Profile.CreateNew(1);
            Rules.BeginRun(p, "events"); // 確保地点の出来事は遠征中にだけ出る（v1.11）
            int offered = 0;
            for (int i = 0; i < 4000; i++)
            {
                var e = DreamEvents.Roll(rng, p);
                if (e != DreamEvent.None) offered++;
                Assert.NotEqual(DreamEvent.Lantern, e);
            }
            Assert.InRange(offered / 4000.0, 0.45, 0.55);
            p.LostAndFound.Add(Loot.RollRelic(new Rng(1), Rarity.Common, 1));
            var seen = new HashSet<DreamEvent>();
            for (int i = 0; i < 400; i++) seen.Add(DreamEvents.Roll(rng, p));
            Assert.Contains(DreamEvent.Lantern, seen);
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
        public void Fountain_sacrifices_weakest_and_enhances_best()
        {
            var p = AtEvent(DreamEvent.Fountain);
            var rng = new Rng(1003);
            var weak = Loot.RollRelic(rng, Rarity.Common, 1);
            var strong = Loot.RollRelic(rng, Rarity.Epic, 20);
            p.Run.Satchel.Add(weak);
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Fountain, out _));
            p.Run.Satchel.Add(strong);
            Rules.UseEvent(p, DreamEvent.Fountain);
            Assert.DoesNotContain(weak, p.Run.Satchel);
            Assert.Equal(1, strong.Enhance);
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
        public void Securing_or_delving_dismisses_the_event()
        {
            var p = AtEvent(DreamEvent.Chalice);
            Rules.Delve(p);
            Assert.Equal(DreamEvent.None, p.Run.OfferedEvent);
            p = AtEvent(DreamEvent.Chalice);
            Rules.Secure(p);
            Assert.Equal(DreamEvent.None, p.Run.OfferedEvent);
        }

        [Fact]
        public void Event_survives_codec()
        {
            var p = AtEvent(DreamEvent.Fountain);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(DreamEvent.Fountain, q.Run.OfferedEvent);
        }

        [Fact]
        public void Event_texts_exist_in_both_languages()
        {
            var p = AtEvent(DreamEvent.Merchant);
            foreach (bool ja in new[] { true, false })
            {
                Loc.Japanese = ja;
                foreach (DreamEvent e in Enum.GetValues(typeof(DreamEvent)))
                {
                    if (e == DreamEvent.None) continue;
                    Assert.False(string.IsNullOrWhiteSpace(DreamEvents.Name(e)));
                    Assert.False(string.IsNullOrWhiteSpace(DreamEvents.Describe(e, p)));
                }
            }
            Loc.Japanese = true;
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
                Assert.NotEmpty(set.TwoPiece);
                Assert.NotEmpty(set.ThreePiece);
                Assert.False(string.IsNullOrWhiteSpace(set.Describe()));
            }
        }

        [Fact]
        public void Set_bonuses_apply_at_two_and_three_pieces()
        {
            var p = Profile.CreateNew(1);
            Rules.Equip(p, "H", SetPiece(p, "set.tide.weapon").Uid);
            var b1 = Build.Compute(p, "H", 0);
            Assert.Equal(1, b1.Sets["set.tide"]);
            Assert.Equal(0, b1.Get(Power.Frost));

            Rules.Equip(p, "H", SetPiece(p, "set.tide.armor").Uid);
            var b2 = Build.Compute(p, "H", 0);
            Assert.True(b2.Get(Stat.ColdAmp) >= b1.Get(Stat.ColdAmp) + 10);
            Assert.Equal(0, b2.Get(Power.Frost));

            Rules.Equip(p, "H", SetPiece(p, "set.tide.charm").Uid);
            var b3 = Build.Compute(p, "H", 0);
            Assert.Equal(45, b3.Get(Power.Frost)); // v1.27：霜は 1.5倍
            Assert.Equal(65, b3.Get(Power.EchoingDodge)); // v1.27：回避の残響は上乗せ%の尺度（6.5倍）
        }

        [Fact]
        public void Set_pieces_drop_as_legendaries()
        {
            var rng = new Rng(55);
            var seen = new HashSet<string>();
            for (int i = 0; i < 4000; i++) seen.Add(Loot.RollRelic(rng, Rarity.Legendary, 10).UniqueId);
            foreach (var u in Content.Uniques.Where(x => x.SetId != null)) Assert.Contains(u.Id, seen);
        }
    }
}
