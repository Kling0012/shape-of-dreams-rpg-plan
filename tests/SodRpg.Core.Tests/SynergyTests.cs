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

        [Theory]
        [InlineData(Power.Ember)]
        [InlineData(Power.Frost)]
        [InlineData(Power.Radiance)]
        [InlineData(Power.Umbra)]
        public void Element_powers_stack_their_element_at_the_listed_amount(Power power)
        {
            var rt = new PowerRuntime(With((power, 30)), 0, 77);
            int n = 20000, total = 0;
            for (int i = 0; i < n; i++)
            {
                var r = rt.OnAttackHit(i, 500, 100, 0, 1);
                int applied = power == Power.Ember ? r.FireStacks : power == Power.Frost ? r.ColdStacks
                    : power == Power.Radiance ? r.LightStacks : r.DarkStacks;
                Assert.Equal(0, r.FireStacks + r.ColdStacks + r.LightStacks + r.DarkStacks - applied);
                total += applied;
            }
            Assert.InRange(total / (double)n, 0.27, 0.33); // 30は平均0.3つ
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

        [Fact]
        public void Elemental_amp_affixes_roll_and_convert_to_game_units()
        {
            var rng = new Rng(5);
            var seen = new HashSet<Stat>();
            for (int i = 0; i < 6000; i++)
                foreach (var a in Loot.RollRelic(rng, Rarity.Rare, 10).Affixes) seen.Add(a.Stat);
            foreach (var s in new[] { Stat.FireAmp, Stat.ColdAmp, Stat.LightAmp, Stat.DarkAmp }) Assert.Contains(s, seen);
            Assert.Equal(0.1f, StatUnits.ToGame(Stat.LightAmp, 10), 4);
        }

        [Fact]
        public void New_synergy_powers_roll_on_epics_and_have_text()
        {
            var rng = new Rng(6);
            var seen = new HashSet<Power>();
            for (int i = 0; i < 12000; i++) seen.Add(Loot.RollRelic(rng, Rarity.Epic, 10).Powers[0].Power);
            var synergy = new[] { Power.Ember, Power.Frost, Power.Radiance, Power.Umbra, Power.Convergence, Power.EchoingDodge, Power.UltimateSurge };
            foreach (var p in synergy)
            {
                Assert.Contains(p, seen);
                Assert.True(Content.PowerCap(p) > 0);
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    Assert.NotEqual("-", Content.FormatPower(p, 10));
                }
            }
            Loc.Japanese = true;
        }

        [Fact]
        public void Synergy_uniques_exist()
        {
            foreach (var id in new[] { "unique.prism_clock", "unique.afterimage_cloak", "unique.dawnbreaker" })
            {
                Assert.True(Content.TryGetUnique(id, out var u), id);
                Assert.Equal(2, u.Powers.Count);
            }
        }

        [Theory]
        [InlineData(BountyKind.ChaosSeeker)]
        [InlineData(BountyKind.Patron)]
        [InlineData(BountyKind.Refiner)]
        [InlineData(BountyKind.Alchemist)]
        [InlineData(BountyKind.Recycler)]
        [InlineData(BountyKind.HunterBait)]
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

        [Fact]
        public void Daily_boosts_include_elemental_powers()
        {
            var p = Profile.CreateNew(1);
            var r = new Relic { Uid = "u", BaseId = "weapon.blaze_greatsword", Rarity = Rarity.Epic, ItemLevel = 1 };
            r.Powers.Add(new PowerLine(Power.Ember, 30));
            p.Stash.Add(r);
            Rules.Equip(p, "H", r.Uid);
            Assert.Equal(45, Build.Compute(p, "H", 0, null, 1).Get(Power.Ember)); // 烈火の日
        }
    }
}
