using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class SynergyTests
    {
        private static Build With(params (Power p, int v)[] powers)
        {
            var b = new Build();
            foreach (var (p, v) in powers) b.Powers[p] = v;
            return b;
        }

        [Fact]
        public void Convergence_needs_all_four_and_has_a_per_enemy_cooldown()
        {
            var rt = new PowerRuntime(With((Power.Convergence, 150)), 0);
            Assert.Equal(0, rt.TakeConvergence(1, 7, false, 100));
            Assert.Equal(150f, rt.TakeConvergence(1, 7, true, 100), 3);
            Assert.Equal(0, rt.TakeConvergence(3, 7, true, 100));           // 同じ敵は6秒
            Assert.Equal(150f, rt.TakeConvergence(3, 8, true, 100), 3);     // 別の敵は即
            Assert.Equal(150f, rt.TakeConvergence(7.1f, 7, true, 100), 3);
            Assert.Equal(0, new PowerRuntime(new Build(), 0).TakeConvergence(1, 1, true, 100));
        }

        [Fact]
        public void Ultimate_surge_raises_attack_and_power_for_five_seconds()
        {
            var rt = new PowerRuntime(With((Power.UltimateSurge, 20)), 0);
            rt.OnSkillUsed(10, isMovement: false, isUltimate: false);
            Assert.Equal(0, rt.Current(11).AttackPct);
            rt.OnSkillUsed(10, isMovement: false, isUltimate: true);
            var d = rt.Current(14.9f);
            Assert.Equal(20, d.AttackPct);
            Assert.Equal(20, d.PowerPct);
            Assert.Equal(0, rt.Current(15.1f).AttackPct);
        }

        [Theory]
        [InlineData(BountyKind.ChaosSeeker)]
        [InlineData(BountyKind.Recycler)]
        public void Game_actions_progress_their_bounties_only(BountyKind kind)
        {
            var p = Profile.CreateNew(3);
            Rules.BeginRun(p, "g");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = kind, Target = 2, RewardShards = 10, RewardXp = 5 });
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.Slayer, Target = 2, RewardShards = 10, RewardXp = 5 });
            Assert.Empty(Rules.OnGameAction(p, kind));
            Assert.Equal(1, p.Run.Bounties[0].Progress);
            Assert.Contains(Rules.OnGameAction(p, kind), e => e.Kind == EventKind.Bounty);
            Assert.True(p.Run.Bounties[0].Done);
            Assert.Equal(0, p.Run.Bounties[1].Progress);
            Assert.False(string.IsNullOrWhiteSpace(p.Run.Bounties[0].Describe()));
        }

        [Fact]
        public void Game_actions_outside_a_run_do_nothing()
        {
            var p = Profile.CreateNew(3);
            Assert.Empty(Rules.OnGameAction(p, BountyKind.Patron));
        }

    }
}
