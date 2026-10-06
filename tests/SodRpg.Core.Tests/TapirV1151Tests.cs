using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.15.1（issue #10・#11）：夢喰いの獏と星読みの塔の説明と効果が一致すること。</summary>
    public class TapirV1151Tests
    {
        private static Profile AtTapir(int lows, bool withEpic)
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "tapir");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.Tapir;
            for (int i = 0; i < lows; i++) p.Run.Satchel.Add(Loot.RollRelic(new Rng((ulong)(20 + i)), Rarity.Uncommon, 3));
            if (withEpic) p.Run.Satchel.Add(Loot.RollRelic(new Rng(99), Rarity.Epic, 3));
            return p;
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void Tapir_targets_only_relics_below_epic(int lows, bool usable)
        {
            var p = AtTapir(lows, withEpic: true);
            Assert.Equal(usable, DreamEvents.CanUse(p, DreamEvent.Tapir, out _));
            if (!usable) return;
            int before = p.Material(Materials.Shard);
            Rules.UseEvent(p, DreamEvent.Tapir);
            Assert.Single(p.Run.Satchel); // エピックだけが残る
            Assert.Equal(Rarity.Epic, p.Run.Satchel[0].Rarity);
            Assert.Equal(before + lows * Content.SalvageShards(Rarity.Uncommon), p.Material(Materials.Shard)); // 保管庫側へ直接
            Assert.Equal(lows / EventBalanceTestData.Number("tapir", "relicsPerTuning"), p.Run.SatchelTuning);
        }

    }
}
