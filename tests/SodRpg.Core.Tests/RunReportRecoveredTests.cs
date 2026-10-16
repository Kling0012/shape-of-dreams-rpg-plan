using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>遺失物から取り戻した遺物も、遠征の報告の「見つけた遺物」に入る（持ち帰り・喪失だけ数えていた）。</summary>
    public sealed class RunReportRecoveredTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Recovered_lost_relic_counts_as_found(bool victory)
        {
            var p = Profile.CreateNew(11);
            Rules.BeginRun(p, "report-recovered");
            p.Run.Bounties.Clear();
            p.LostAndFound.Add(Loot.RollRelic(new Rng(3), Rarity.Rare, 10));
            Rules.OnRoomsCleared(p, Workshop.RoomsToRecover(p));
            Assert.Single(p.Run.Satchel);
            if (victory) Rules.Secure(p);
            Rules.EndRun(p, victory);
            var r = p.LastReport;
            Assert.Equal(1, r.RelicsFound);
            Assert.True(r.RelicsSecured + r.RelicsLost <= r.RelicsFound, $"found {r.RelicsFound}, secured {r.RelicsSecured}, lost {r.RelicsLost}");
        }
    }
}
