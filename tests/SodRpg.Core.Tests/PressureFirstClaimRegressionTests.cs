using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class PressureFirstClaimRegressionTests
    {
        [Fact]
        public void Pressure_extra_cannot_consume_FirstClaims_only_room_reward_without_delivering_it()
        {
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var p = Profile.CreateNew(seed);
                Rules.BeginRun(p, "first-claim-pressure");
                p.Run.ActiveWaypoint = Waypoint.FirstClaim;
                p.Run.Bounties.Clear();
                Rules.OnKill(p, MonsterTier.Normal, 1, rewardScale: 1.0 / 3);
                Assert.True(p.Run.Satchel.Count == 1,
                    $"Seed {seed}: the first extra's thinned relic consumed First Claim without delivering it.");
                Assert.True(p.Run.Satchel[0].Rarity >= Rarity.Rare);
                // The guarantee remains one per room, even with many later original kills.
                for (int i = 0; i < 20; i++) Rules.OnKill(p, MonsterTier.Normal, 1);
                Assert.Single(p.Run.Satchel);
            }
        }

        [Fact]
        public void Restored_guarantee_does_not_unthin_currencies_or_random_Legendary()
        {
            var p = Profile.CreateNew(8);
            Rules.BeginRun(p, "first-claim-floor");
            var reward = new KillReward { Shards = 30, Tuning = 12, Xp = 9 };
            reward.Relics.Add(Loot.RollRelic(new Rng(9), Rarity.Legendary, 1));
            Waypoints.ApplyKill(p, MonsterTier.Normal, false, new Rng(10), reward, 1, null, 0,
                Waypoint.FirstClaim, out _, out _, rewardScale: 0);
            Assert.Equal(Rarity.Rare, Assert.Single(reward.Relics).Rarity);
            Assert.Equal(0, reward.Shards);
            Assert.Equal(0, reward.Tuning);
            Assert.Equal(0, reward.Xp);
        }

        /// <summary>インフィニティでも一番槍の固定保証は枠なしで出る（旧台帳が0でも同じ）。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void Restored_guarantee_in_infinity_ignores_the_legacy_budget_ledger(double credit)
        {
            var p = Profile.CreateNew(8);
            Rules.BeginRun(p, "first-claim-infinity");
            p.Run.Infinity = new InfinityRunState { Interval = InfinityRunState.LongInterval };
            p.InfinityRewardBudget.Relics = credit;
            var reward = new KillReward();
            Waypoints.ApplyKill(p, MonsterTier.Normal, false, new Rng(10), reward, 1, null, 0,
                Waypoint.FirstClaim, out _, out _, rewardScale: 0);
            Assert.Single(reward.Relics);
            Assert.Equal(Rarity.Rare, reward.Relics[0].Rarity);
            Assert.Equal(credit, p.InfinityRewardBudget.Relics); // 台帳は変わらない
        }
    }
}
