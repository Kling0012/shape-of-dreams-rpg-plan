using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.15：仕上げで直したルール。</summary>
    public class PolishV115Tests
    {
        [Fact]
        public void Rarity_always_outranks_item_level_and_enhancement()
        {
            var rare = Loot.RollRelic(new Rng(1), Rarity.Rare, 1);
            var common = Loot.RollRelic(new Rng(2), Rarity.Common, Content.MaxItemLevel);
            common.Enhance = Content.MaxEnhance;
            Assert.True(rare.Score > common.Score);
        }

        [Fact]
        public void Cauldron_uses_the_item_level_of_the_melted_relics()
        {
            var p = Profile.CreateNew(3);
            p.BestItemLevel = 50;
            Rules.BeginRun(p, "c");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.Cauldron;
            for (int i = 0; i < EventBalanceTestData.Number("cauldron", "relicCount"); i++) p.Run.Satchel.Add(Loot.RollRelic(new Rng((ulong)(10 + i)), Rarity.Common, 4));
            Rules.UseEvent(p, DreamEvent.Cauldron);
            Assert.Single(p.Run.Satchel);
            Assert.Equal(4, p.Run.Satchel[0].ItemLevel);
        }

        [Fact]
        public void Treasure_bounty_counts_relics_from_events_too()
        {
            var p = Profile.CreateNew(4);
            Rules.BeginRun(p, "t");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.Treasure, Target = 1, RewardShards = 5, RewardXp = 5 });
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.TwinMirror;
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(5), Rarity.Rare, 3));
            p.Run.SatchelShards = EventBalanceTestData.Number("twinMirror", "shards");
            Rules.UseEvent(p, DreamEvent.TwinMirror);
            Assert.True(p.Run.Bounties[0].Done);
        }

        [Fact]
        public void Legend_and_set_bounties_are_rarer()
        {
            var rng = new Rng(9);
            int rare = 0, total = 0;
            for (int i = 0; i < 3000; i++)
                foreach (var b in Bounties.Roll(rng))
                {
                    total++;
                    if (b.Kind == BountyKind.LegendFinder || b.Kind == BountyKind.SetHunter) rare++;
                }
            // 22種のうち2種で、重みは他の1/3。等確率なら約9%、重み付きなら約3%。
            Assert.InRange(rare / (double)total, 0.01, 0.06);
        }

    }
}
