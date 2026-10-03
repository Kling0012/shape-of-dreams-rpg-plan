using System;
using System.Collections.Generic;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class PressureDividendTests
    {
        private static ISet<string> Equipped() => new HashSet<string> { "St_D_IcyVeins" };
        private static PressureDividendChannel Channel(int probability = 4000, string source = "St_D_IcyVeins",
            string[] required = null) => new PressureDividendChannel(new[]
            { new PressureDividendContribution("synthetic.dividend", source, probability, required) });

        private static PressureDividendDeath Death(double multiplier = 1.25, bool eligible = true,
            int zone = 0, long spawn = 1, string run = "run")
        {
            var enemy = new PressureDividendEnemy(run, zone, spawn);
            enemy.RecordAppliedHpMultiplier(multiplier);
            return enemy.CaptureDeath(eligible);
        }

        private static PressureDividendAttribution Native(string owner = "7", string source = "St_D_IcyVeins") =>
            new PressureDividendAttribution(owner, source, PressureDividendKillOrigin.NativeMemory, PressureDividendVictimKind.NativeLootEnemy);

        [Theory]
        [InlineData(PressureDividendVictimKind.Unknown)]
        [InlineData(PressureDividendVictimKind.EnemySummon)]
        public void Unknown_or_summoned_victim_provenance_never_rolls(PressureDividendVictimKind victimKind)
        {
            Assert.Null(new PressureDividendRuntime().TryAward(Death(),
                new PressureDividendAttribution("7", "St_D_IcyVeins", PressureDividendKillOrigin.NativeMemory, victimKind),
                new[] { Channel() }, Equipped(), () => throw new InvalidOperationException("No roll expected"), () => "nonce"));
        }

        [Fact]
        public void Source_must_be_known_non_movement_and_still_equipped()
        {
            Assert.Throws<ArgumentException>(() => new PressureDividendContribution("synthetic.a", "unknown.memory", 100));
            Assert.Throws<ArgumentException>(() => new PressureDividendContribution("synthetic.a", "St_M_FrostyCharge", 100));
            Assert.Null(new PressureDividendRuntime().TryAward(Death(), Native(), new[] { Channel() }, new HashSet<string>(),
                () => throw new InvalidOperationException("Unequipped source cannot roll"), () => "nonce"));
        }

        [Fact]
        public void Equivalent_contributions_add_then_one_boost_chance_and_final_cap()
        {
            var channel = new PressureDividendChannel(new[]
            {
                new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 1000),
                new PressureDividendContribution("synthetic.b", "St_D_IcyVeins", 500),
            }, boostModifierUnits: 2000, chanceProbabilityUnits: 125);
            Assert.Equal(1925, channel.ProbabilityUnits);
            Assert.Equal(2, channel.ContributorIds.Count);
            Assert.Equal(4000, new PressureDividendChannel(new[]
            { new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 3500) }, chanceProbabilityUnits: 1000).ProbabilityUnits);
            Assert.Equal(4000, new PressureDividendChannel(new[]
            {
                new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 3500),
                new PressureDividendContribution("synthetic.b", "St_D_IcyVeins", 1000),
            }).ProbabilityUnits);
        }

        [Fact]
        public void Fractional_probability_and_requirements_survive_composition_without_rounding()
        {
            var channel = new PressureDividendChannel(new[]
            {
                new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 100, new[] { "St_R_BaptismOfSun", "St_Q_CruelSun", "St_R_BaptismOfSun" }),
                new PressureDividendContribution("synthetic.b", "St_D_IcyVeins", 25, new[] { "St_Q_CruelSun", "St_R_BaptismOfSun" }),
            }, boostModifierUnits: 2000);
            Assert.Equal(150, channel.ProbabilityUnits);
            Assert.Equal(new[] { "St_Q_CruelSun", "St_R_BaptismOfSun" }, channel.RequiredMemories);
            Assert.False(channel.Matches("St_D_IcyVeins", new HashSet<string> { "St_Q_CruelSun" }));
            Assert.True(channel.Matches("St_D_IcyVeins", new HashSet<string> { "St_D_IcyVeins", "St_R_BaptismOfSun", "St_Q_CruelSun" }));
            Assert.False(channel.Matches("St_D_Resolve", new HashSet<string> { "St_D_IcyVeins", "St_R_BaptismOfSun", "St_Q_CruelSun" }));
            Assert.Throws<ArgumentException>(() => new PressureDividendChannel(new[]
            { new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 1) }, boostModifierUnits: 1));
        }

        [Fact]
        public void Duplicate_contributors_and_non_equivalent_contributions_fail_explicitly()
        {
            Assert.Throws<ArgumentException>(() => new PressureDividendChannel(new[]
            {
                new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 100),
                new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 100),
            }));
            Assert.Throws<ArgumentException>(() => new PressureDividendChannel(new[]
            {
                new PressureDividendContribution("synthetic.a", "St_D_IcyVeins", 100),
                new PressureDividendContribution("synthetic.b", "St_D_Resolve", 100),
            }));
        }

        [Theory]
        [InlineData(1.249, false, 0)]
        [InlineData(1.25, true, 1)]
        [InlineData(1.251, true, 1)]
        public void Uses_actual_applied_hp_threshold(double multiplier, bool awarded, int rolls)
        {
            int calls = 0;
            var reward = new PressureDividendRuntime().TryAward(Death(multiplier), Native(), new[] { Channel() }, Equipped(),
                () => { calls++; return 0; }, () => "nonce");
            Assert.Equal(awarded, reward != null);
            Assert.Equal(rolls, calls);
        }

        [Fact]
        public void Updates_only_the_applied_spawn_and_freezes_its_death_snapshot()
        {
            var first = new PressureDividendEnemy("run", 0, 1);
            var second = new PressureDividendEnemy("run", 0, 2);
            first.RecordAppliedHpMultiplier(1.249);
            second.RecordAppliedHpMultiplier(1.25);
            var death = first.CaptureDeath(true);
            second.RecordAppliedHpMultiplier(1.75);
            Assert.Equal(1.249, death.AppliedHpMultiplier);
            Assert.Equal(1, death.AppliedVersion);
            Assert.Equal(2, second.AppliedVersion);
            Assert.Equal(1.75, second.CaptureDeath(true).AppliedHpMultiplier);
            Assert.Same(death, first.CaptureDeath(false));
            Assert.Throws<InvalidOperationException>(() => first.RecordAppliedHpMultiplier(2));
        }

        [Fact]
        public void Unapplied_pressure_is_ineligible_even_if_a_global_pressure_would_qualify()
        {
            var death = new PressureDividendEnemy("run", 0, 1).CaptureDeath(true);
            Assert.False(death.RewardEligible);
            Assert.Null(new PressureDividendRuntime().TryAward(death, Native(), new[] { Channel() }, Equipped(),
                () => throw new InvalidOperationException("No roll expected"), () => "nonce"));
        }

        [Theory]
        [InlineData(PressureDividendKillOrigin.Generated)]
        [InlineData(PressureDividendKillOrigin.Unknown)]
        public void Generated_or_unknown_attribution_never_rolls(PressureDividendKillOrigin origin)
        {
            Assert.Null(new PressureDividendRuntime().TryAward(Death(),
                new PressureDividendAttribution("7", "St_D_IcyVeins", origin, PressureDividendVictimKind.NativeLootEnemy), new[] { Channel() }, Equipped(),
                () => throw new InvalidOperationException("No roll expected"), () => "nonce"));
        }

        [Fact]
        public void Disabled_native_rewards_never_roll_even_when_pressure_qualifies()
        {
            Assert.Null(new PressureDividendRuntime().TryAward(Death(eligible: false), Native(), new[] { Channel() }, Equipped(),
                () => throw new InvalidOperationException("No roll expected"), () => "nonce"));
        }

        [Theory]
        [InlineData(3999, true)]
        [InlineData(4000, false)]
        [InlineData(9999, false)]
        public void Success_and_failure_both_consume_one_owner_death_roll_across_retransmission(int roll, bool success)
        {
            var runtime = new PressureDividendRuntime();
            int rolls = 0;
            Func<int> random = () => { rolls++; return roll; };
            Assert.Equal(success, runtime.TryAward(Death(), Native(), new[] { Channel() }, Equipped(), random, () => "nonce") != null);
            // Equivalent rebuilt channels cannot reset a successful or failed roll.
            Assert.Null(runtime.TryAward(Death(), Native(), new[] { Channel() }, Equipped(), random, () => "new-nonce"));
            Assert.Equal(1, rolls);
        }

        [Fact]
        public void Spawn_zone_run_and_owner_are_independent_reward_identity_fields()
        {
            var runtime = new PressureDividendRuntime();
            var deaths = new[] { Death(), Death(spawn: 2), Death(zone: 1), Death(run: "next-run") };
            foreach (var death in deaths)
            {
                Assert.NotNull(runtime.TryAward(death, Native(), new[] { Channel() }, Equipped(), () => 0, () => Guid.NewGuid().ToString("N")));
                Assert.NotNull(runtime.TryAward(death, Native("8"), new[] { Channel() }, Equipped(), () => 0, () => Guid.NewGuid().ToString("N")));
            }
        }

        [Fact]
        public void Ambiguous_non_equivalent_conditions_stop_without_inventing_probability_policy()
        {
            int rolls = 0;
            Assert.Throws<InvalidOperationException>(() => new PressureDividendRuntime().TryAward(Death(), Native(),
                new[] { Channel(), Channel(required: new[] { "St_Q_CruelSun" }) }, new HashSet<string> { "St_D_IcyVeins", "St_Q_CruelSun" },
                () => { rolls++; return 0; }, () => "nonce"));
            Assert.Equal(0, rolls);
        }

        [Fact]
        public void Different_source_conditions_still_admit_only_the_attributed_source()
        {
            int rolls = 0;
            var reward = new PressureDividendRuntime().TryAward(Death(), Native(),
                new[] { Channel(), Channel(source: "St_D_Resolve") }, Equipped(),
                () => { rolls++; return 0; }, () => "nonce");
            Assert.NotNull(reward);
            Assert.Equal(1, rolls);
        }

        [Fact]
        public void Authenticated_pending_receipt_waits_for_run_and_rejects_owner_run_and_nonce_replay()
        {
            var pending = new PendingPressureDividends();
            var reward = new PressureDividendReward("run", 0, 1, "7", "nonce");
            Assert.False(pending.AddAuthenticated(reward, "run", "8"));
            Assert.False(pending.AddAuthenticated(reward, "different-run", "7"));
            Assert.True(pending.AddAuthenticated(reward, "run", "7"));
            var profile = Profile.CreateNew(9);
            Assert.Equal(0, pending.Drain(profile));
            Assert.Equal(1, pending.Count);
            Rules.BeginRun(profile, "run");
            Assert.Equal(1, pending.Drain(profile));
            Assert.Equal(1, profile.Run.SatchelShards);
            Assert.False(pending.AddAuthenticated(reward, "run", "7"));
            Assert.False(pending.AddAuthenticated(new PressureDividendReward("run", 0, 1, "7", "other-nonce"), "run", "7"));
            Assert.False(pending.AddAuthenticated(new PressureDividendReward("run", 0, 2, "7", "nonce"), "run", "7"));
            Assert.Equal(0, pending.Drain(profile));
        }

        [Fact]
        public void Dividend_does_not_depend_on_normal_loot_and_uses_ordinary_secure_and_defeat_rules()
        {
            var secured = Profile.CreateNew(11);
            Rules.BeginRun(secured, "secure-run");
            var pending = new PendingPressureDividends();
            pending.AddAuthenticated(new PressureDividendReward("secure-run", 0, 1, "7", "nonce"), "secure-run", "7");
            Assert.Equal(1, pending.Drain(secured));
            Assert.Equal(0, secured.Run.Kills); // This separate reward never invokes normal loot or doubles it.
            Assert.Equal(1, secured.Run.SatchelShards);
            Rules.EndRun(secured, true);
            Assert.Null(secured.Run);
            Assert.Equal(1, secured.Material(Materials.Shard));

            var lost = Profile.CreateNew(12);
            Rules.BeginRun(lost, "lost-run");
            for (int spawn = 1; spawn <= 4; spawn++)
                pending.AddAuthenticated(new PressureDividendReward("lost-run", 0, spawn, "7", "nonce" + spawn), "lost-run", "7");
            pending.Drain(lost);
            Assert.Equal(4, lost.Run.SatchelShards);
            Rules.EndRun(lost, false);
            Assert.Null(lost.Run);
            Assert.Equal(1, lost.Material(Materials.Shard)); // Existing 25% echo; the other three remain lost.
        }

        [Fact]
        public void Optional_native_message_preserves_receipt_and_rejects_variable_shard_amount()
        {
            var reward = new PressureDividendReward("run", 2, 4294967296, "7", "nonce");
            var message = DreamforgePressureDividendMsg.FromReward(reward, 7);
            var settings = new JsonSerializerOptions { IncludeFields = true };
            string encoded = JsonSerializer.Serialize(message, settings);
            message = JsonSerializer.Deserialize<DreamforgePressureDividendMsg>(encoded, settings);
            var decoded = message.ToReward();
            Assert.Equal(reward.SpawnId, decoded.SpawnId);
            Assert.Equal(reward.ZoneId, decoded.ZoneId);
            Assert.Equal(reward.OwnerId, decoded.OwnerId);
            Assert.Equal(1, decoded.ShardCount);
            message.shardCount = 2;
            Assert.Throws<InvalidOperationException>(() => message.ToReward());
        }
    }
}
