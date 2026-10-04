using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.25.1：まとめて分解は、再調律の候補が出ている遺物を対象から外す。</summary>
    public class BulkSalvageV1251Tests
    {
        private static Relic AddRelic(Profile p, int seed)
        {
            var r = Loot.RollRelic(new Rng((ulong)seed), Rarity.Uncommon, 2);
            p.Stash.Add(r);
            return r;
        }

        [Fact]
        public void Offered_relic_alone_is_not_salvaged_and_stays_with_its_offer()
        {
            Loc.Japanese = true;
            var p = Profile.CreateNew(3);
            var offered = AddRelic(p, 41);
            p.AddMaterial(Materials.Tuning, 10);
            Rules.Retune(p, offered.Uid, 0);

            Assert.Empty(Rules.BulkSalvageCandidates(p));
            var ev = Rules.BulkSalvage(p);
            Assert.Equal(0, p.Material(Materials.Shard));
            Assert.Single(p.Stash);
            Assert.NotNull(p.FindStash(offered.Uid));
            Assert.NotNull(p.RetuneOffer);
            Assert.Contains("分解できる遺物はありません", ev.Text);
        }

        [Fact]
        public void Bulk_salvage_keeps_the_offered_relic_and_reports_the_actual_delta()
        {
            Loc.Japanese = true;
            var p = Profile.CreateNew(3);
            var offered = AddRelic(p, 41);
            var others = new[] { AddRelic(p, 42), AddRelic(p, 43) };
            p.AddMaterial(Materials.Tuning, 10);
            Rules.Retune(p, offered.Uid, 0);

            var candidates = Rules.BulkSalvageCandidates(p);
            Assert.Equal(2, candidates.Count);
            Assert.DoesNotContain(candidates, x => x.Uid == offered.Uid);

            int before = p.Material(Materials.Shard);
            var ev = Rules.BulkSalvage(p);
            int delta = p.Material(Materials.Shard) - before;
            Assert.Equal(others.Sum(x => Rules.SalvageValue(x)), delta);
            Assert.Contains($"欠片{delta}", ev.Text);
            Assert.Null(p.FindStash(others[0].Uid));
            Assert.Null(p.FindStash(others[1].Uid));
            Assert.NotNull(p.FindStash(offered.Uid));
            Assert.NotNull(p.RetuneOffer);
        }

        [Fact]
        public void After_choosing_a_retune_the_relic_is_a_bulk_salvage_candidate_again()
        {
            var p = Profile.CreateNew(3);
            var offered = AddRelic(p, 41);
            p.AddMaterial(Materials.Tuning, 10);
            Rules.Retune(p, offered.Uid, 0);
            Rules.ChooseRetune(p, -1);
            Assert.Null(p.RetuneOffer);

            Assert.Single(Rules.BulkSalvageCandidates(p));
            Rules.BulkSalvage(p);
            Assert.Empty(p.Stash);
            Assert.Equal(Rules.SalvageValue(offered), p.Material(Materials.Shard));
        }
    }
}
