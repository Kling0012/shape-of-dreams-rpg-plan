using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.20：鍛冶を数値だけにしない（強化の節目・再調律の3択・合成の枠の指定）。</summary>
    public class ForgeV120Tests
    {
        private static (Profile P, Relic R) WithRelic(Rarity rarity, int seed = 7)
        {
            var p = Profile.CreateNew((ulong)seed);
            var r = Loot.RollRelic(new Rng((ulong)seed), rarity, 5, Slot.Weapon);
            p.Stash.Add(r);
            p.AddMaterial(Materials.Shard, 5000);
            p.AddMaterial(Materials.Tuning, 50);
            return (p, r);
        }

        [Fact]
        public void Plus_three_adds_one_affix_and_plus_five_gives_a_common_a_power()
        {
            var (p, r) = WithRelic(Rarity.Common);
            int affixes = r.Affixes.Count;
            Assert.Empty(r.Powers);
            for (int i = 0; i < 2; i++) Rules.Enhance(p, r.Uid);
            Assert.Equal(affixes, r.Affixes.Count);
            var e3 = Rules.Enhance(p, r.Uid);
            Assert.Equal(affixes + 1, r.Affixes.Count);
            Assert.Equal(1, r.EnhanceMilestones);
            Assert.Equal(EventKind.LevelUp, e3.Kind);
            Rules.Enhance(p, r.Uid);
            Rules.Enhance(p, r.Uid);
            Assert.Single(r.Powers);
            Assert.Equal(2, r.EnhanceMilestones);
            // 固有効果はその枠の候補の下限値
            var pr = Content.PowerPool(r.Slot).First(x => x.Power == r.Powers[0].Power);
            Assert.Equal(pr.Min, r.Powers[0].Value);
            // 能力値は重複しない
            Assert.Equal(r.Affixes.Count, r.Affixes.Select(a => a.Stat).Distinct().Count());
            Assert.DoesNotContain(r.Affixes, a => a.Stat == r.Base.ImplicitStat);
        }

        [Fact]
        public void Plus_five_on_an_epic_adds_an_affix_instead_of_a_second_power()
        {
            var (p, r) = WithRelic(Rarity.Epic);
            int affixes = r.Affixes.Count, powers = r.Powers.Count;
            for (int i = 0; i < 5; i++) Rules.Enhance(p, r.Uid);
            Assert.Equal(powers, r.Powers.Count);
            Assert.Equal(affixes + 2, r.Affixes.Count);
        }

        [Fact]
        public void Old_enhanced_relics_get_their_milestones_once()
        {
            var (p, r) = WithRelic(Rarity.Uncommon);
            r.Enhance = 5; // v1.20 より前に強化した物
            int affixes = r.Affixes.Count;
            Assert.Equal(1, Rules.ApplyEnhanceMilestones(p));
            Assert.Equal(affixes + 1, r.Affixes.Count);
            Assert.Single(r.Powers);
            Assert.Equal(0, Rules.ApplyEnhanceMilestones(p));
            Assert.Equal(affixes + 1, r.Affixes.Count);
        }

        [Fact]
        public void Retune_offers_three_options_and_charges_once()
        {
            var (p, r) = WithRelic(Rarity.Rare);
            int tuning = p.Material(Materials.Tuning);
            var original = r.Affixes[0];
            Rules.Retune(p, r.Uid, 0);
            Assert.NotNull(p.RetuneOffer);
            Assert.Equal(Content.RetuneChoices, p.RetuneOffer.Options.Count);
            Assert.Equal(Content.RetuneChoices, p.RetuneOffer.Options.Select(o => o.Stat).Distinct().Count());
            Assert.Equal(tuning - Content.RetuneCost(0), p.Material(Materials.Tuning));
            Assert.Equal(1, r.Retunes);
            Assert.Same(original, r.Affixes[0]); // 選ぶまでは変わらない
            // 候補が出ている間は、もう一度の再調律・強化・分解はできない
            Assert.Throws<InvalidOperationException>(() => Rules.Retune(p, r.Uid, 0));
            Assert.Throws<InvalidOperationException>(() => Rules.Enhance(p, r.Uid));
            Assert.Throws<InvalidOperationException>(() => Rules.Salvage(p, r.Uid));
            var pick = p.RetuneOffer.Options[1];
            Rules.ChooseRetune(p, 1);
            Assert.Null(p.RetuneOffer);
            Assert.Equal(pick.Stat, r.Affixes[0].Stat);
            Assert.Equal(pick.Value, r.Affixes[0].Value);
            // 他の特性と能力値が重ならない
            Assert.Equal(r.Affixes.Count, r.Affixes.Select(a => a.Stat).Distinct().Count());
        }

        [Fact]
        public void Keeping_the_original_still_spends_the_retune()
        {
            var (p, r) = WithRelic(Rarity.Rare);
            var original = r.Affixes[1];
            Rules.Retune(p, r.Uid, 1);
            Rules.ChooseRetune(p, -1);
            Assert.Same(original, r.Affixes[1]);
            Assert.Equal(1, r.Retunes);
            Assert.Null(p.RetuneOffer);
        }

        [Fact]
        public void Retune_offer_survives_save_and_load()
        {
            var (p, r) = WithRelic(Rarity.Rare);
            for (int i = 0; i < 3; i++) Rules.Enhance(p, r.Uid);
            Rules.Retune(p, r.Uid, 0);
            var json = ProfileCodec.Write(p);
            var back = ProfileCodec.Read(json, new System.Collections.Generic.List<string>());
            Assert.NotNull(back.RetuneOffer);
            Assert.Equal(p.RetuneOffer.Uid, back.RetuneOffer.Uid);
            Assert.Equal(p.RetuneOffer.Index, back.RetuneOffer.Index);
            Assert.Equal(p.RetuneOffer.Options.Select(o => (o.Stat, o.Value)), back.RetuneOffer.Options.Select(o => (o.Stat, o.Value)));
            Assert.Equal(1, back.FindStash(r.Uid).EnhanceMilestones);
            var copy = p.Clone();
            Assert.NotNull(copy.RetuneOffer);
            Assert.NotSame(p.RetuneOffer, copy.RetuneOffer);
        }

        [Fact]
        public void Offered_relic_is_not_used_as_transmute_material()
        {
            var p = Profile.CreateNew(3);
            for (int i = 0; i < 4; i++) p.Stash.Add(Loot.RollRelic(new Rng((ulong)(40 + i)), Rarity.Common, 2));
            p.AddMaterial(Materials.Tuning, 10);
            var weakest = Rules.TransmuteCandidates(p, Rarity.Common).First();
            Rules.Retune(p, weakest.Uid, 0);
            Assert.DoesNotContain(Rules.TransmuteCandidates(p, Rarity.Common), x => x.Uid == weakest.Uid);
        }

        [Fact]
        public void Targeted_transmute_lands_in_the_chosen_slot_and_costs_more()
        {
            foreach (var slot in new[] { Slot.Weapon, Slot.Armor, Slot.Charm })
            {
                var p = Profile.CreateNew(11);
                for (int i = 0; i < 3; i++) p.Stash.Add(Loot.RollRelic(new Rng((ulong)(60 + i)), Rarity.Uncommon, 2));
                p.AddMaterial(Materials.Shard, 1000);
                int before = p.Material(Materials.Shard);
                Rules.Transmute(p, Rarity.Uncommon, target: slot);
                Assert.Equal(before - Rules.TransmuteCost(Rarity.Uncommon, true), p.Material(Materials.Shard));
                Assert.Equal(Rules.TransmuteCost(Rarity.Uncommon) * Content.TransmuteTargetCostPct / 100, Rules.TransmuteCost(Rarity.Uncommon, true));
                var made = Assert.Single(p.Stash);
                Assert.Equal(slot, made.Slot);
                Assert.Equal(Rarity.Rare, made.Rarity);
            }
        }
    }
}
