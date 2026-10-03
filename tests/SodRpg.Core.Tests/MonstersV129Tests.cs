using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class MonstersV129Tests
    {
        [Fact]
        public void Catalog_has_thirty_distinct_variants_and_twenty_single_bit_affixes()
        {
            Assert.Equal(30, Variants.All.Count);
            Assert.Equal(17, Variants.All.Count(Variants.IsExpanded));
            Assert.Equal(30, Variants.All.Select(v => v.Id).Distinct().Count());
            Assert.Equal(30, Variants.All.Select(v => v.MonsterType).Distinct().Count());
            Assert.Equal(20, Nightmares.AllAffixes.Length);
            Assert.Equal(20, Nightmares.AllAffixes.Distinct().Count());
            foreach (var affix in Nightmares.AllAffixes)
            {
                int bits = (int)affix;
                Assert.True(bits > 0 && (bits & (bits - 1)) == 0);
            }
        }

        [Fact]
        public void Every_variant_and_affix_has_localized_player_information()
        {
            bool original = Loc.Japanese;
            try
            {
                foreach (bool japanese in new[] { true, false })
                {
                    Loc.Japanese = japanese;
                    foreach (var affix in Nightmares.AllAffixes)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(Nightmares.AffixName(affix)));
                        Assert.False(string.IsNullOrWhiteSpace(Nightmares.AffixDescription(affix)));
                    }
                    foreach (var variant in Variants.All)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(japanese ? variant.Name.Ja : variant.Name.En));
                        Assert.False(string.IsNullOrWhiteSpace(japanese ? variant.Description.Ja : variant.Description.En));
                    }
                }
            }
            finally { Loc.Japanese = original; }
        }

        [Fact]
        public void Expanded_affixes_have_only_common_nightmare_health_as_passive_stats()
        {
            foreach (var affix in Nightmares.AllAffixes.Where(Nightmares.HasBehavior))
            {
                var stats = Nightmares.MonsterStats(affix, out float regeneration);
                Assert.Equal(Nightmares.BaseHealthPct, stats.Where(s => s.Stat == Stat.MaxHealthPct).Sum(s => s.Value));
                Assert.DoesNotContain(stats, s => s.Stat != Stat.MaxHealthPct);
                Assert.Equal(0f, regeneration);
            }
            foreach (var variant in Variants.All.Where(Variants.IsExpanded))
            {
                Assert.Equal(100, variant.ShardBonusPct);
                Assert.DoesNotContain(variant.Stats, s => s.Stat == Stat.AttackPct || s.Stat == Stat.PowerPct);
            }
        }

        [Fact]
        public void New_masks_round_trip_the_integer_wire_representation_without_old_bit_loss()
        {
            var expanded = Nightmares.AllAffixes.Where(Nightmares.HasBehavior).ToArray();
            Assert.Equal(10, expanded.Length);
            Assert.False(Nightmares.HasBehavior(NightmareAffix.Ironclad | NightmareAffix.Sundering));
            foreach (var affix in expanded)
            {
                var sent = affix | NightmareAffix.Ironclad | NightmareAffix.Sundering;
                int encoded = (int)sent;
                Assert.Equal(sent, Nightmares.Sanitize(encoded));
                Assert.Equal(sent, Nightmares.Sanitize(encoded | (1 << 20) | (1 << 30)));
                Assert.Equal(3, Nightmares.Count(Nightmares.Sanitize(encoded)));
            }
            var all = Nightmares.AllAffixes.Aggregate(NightmareAffix.None, (a, b) => a | b);
            Assert.Equal((1 << 20) - 1, (int)all);
            Assert.Equal(all, Nightmares.Sanitize(-1));
        }

        [Theory]
        [InlineData(MonsterTier.Normal, MonsterTier.MiniBoss, 2, 2)]
        [InlineData(MonsterTier.MiniBoss, MonsterTier.Boss, 10, 10)]
        [InlineData(MonsterTier.Boss, MonsterTier.Boss, 40, 40)]
        public void Expanded_kills_keep_original_tier_double_rewards_and_persist_accounting(
            MonsterTier tier, MonsterTier rewardTier, int starXp, int awakenPoints)
        {
            Assert.Equal(rewardTier, Nightmares.RewardTier(tier));
            var variant = Variants.All.First(Variants.IsExpanded);
            var boss = Variants.All.Single(v => v.MonsterType == "Mon_Forest_BossDemon");
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var p = Profile.CreateNew(129);
                var relic = Loot.RollUnique(new Rng(20), Content.Uniques.First(u => u.Powers.Count > 0), 1);
                p.Stash.Add(relic);
                Rules.Equip(p, "Hero_Vesper", relic.Uid);
                Rules.BeginRun(p, "Hero_Vesper");
                p.Run.Bounties.Clear();
                p.Run.Bounties.Add(new Bounty { Kind = BountyKind.NightmareHunter, Target = 10 });
                var affixes = mode == 1 ? NightmareAffix.None : NightmareAffix.Veiled | NightmareAffix.LastStand;
                string id = mode == 0 ? null : (tier == MonsterTier.Boss ? boss : variant).Id;
                Rules.OnKill(p, tier, 1, affixes, "Hero_Vesper", variantId: id);
                Assert.Equal(starXp, p.Hero("Hero_Vesper").StarXp);
                Assert.Equal(awakenPoints, relic.AwakenPoints);
                Assert.Equal(1, p.Stats.Kills);
                Assert.Equal(1, p.Run.Bounties[0].Progress);
                var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
                Assert.Equal(mode == 0 ? 1 : 0, q.Stats.NightmaresSlain);
                Assert.Equal(mode == 0 ? 0 : 1, q.Stats.VariantsSlain);
                Assert.Equal(1, q.Stats.Kills);
                Assert.Equal(starXp, q.Hero("Hero_Vesper").StarXp);
                Assert.Equal(awakenPoints, q.FindStash(relic.Uid).AwakenPoints);
            }
        }

        [Theory]
        [InlineData(3, 2)]
        [InlineData(5, 3)]
        public void Regeneration_and_recuperation_never_roll_together_or_reduce_affix_count(int depth, int count)
        {
            var rng = new Rng(129);
            var excludedPair = NightmareAffix.Regenerating | NightmareAffix.Recuperating;
            for (int i = 0; i < 2000; i++)
            {
                var affixes = Nightmares.Roll(rng, MonsterTier.MiniBoss, depth, 100);
                Assert.Equal(count, Nightmares.Count(affixes));
                Assert.NotEqual(excludedPair, affixes & excludedPair);
            }
        }

        [Theory]
        [InlineData(MonsterTier.Normal, MonsterTier.MiniBoss)]
        [InlineData(MonsterTier.MiniBoss, MonsterTier.Boss)]
        [InlineData(MonsterTier.Boss, MonsterTier.Boss)]
        public void Expanded_kills_pay_the_promoted_loot_tier_without_an_extra_shard_multiplier(
            MonsterTier originalTier, MonsterTier promotedTier)
        {
            var variant = Variants.All.First(Variants.IsExpanded);
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var expected = Profile.CreateNew(9129);
                var actual = Profile.CreateNew(9129);
                Rules.BeginRun(expected, "reward");
                Rules.BeginRun(actual, "reward");
                expected.Run.Bounties.Clear();
                actual.Run.Bounties.Clear();
                Rules.OnKill(expected, promotedTier, 10);
                Rules.OnKill(actual, originalTier, 10,
                    mode == 1 ? NightmareAffix.None : NightmareAffix.Hollow,
                    variantId: mode == 0 ? null : variant.Id);
                Assert.Equal(expected.Run.SatchelShards, actual.Run.SatchelShards);
                Assert.Equal(expected.Run.SatchelTuning, actual.Run.SatchelTuning);
                Assert.Equal(expected.DreamXp, actual.DreamXp);
                Assert.Equal(expected.DreamLevel, actual.DreamLevel);
                Assert.Equal(expected.EpicPity, actual.EpicPity);
                Assert.Equal(
                    expected.Run.Satchel.Select(r => (r.BaseId, r.UniqueId, r.Rarity, r.ItemLevel)),
                    actual.Run.Satchel.Select(r => (r.BaseId, r.UniqueId, r.Rarity, r.ItemLevel)));
            }
        }

        [Theory]
        [InlineData(NightmareAffix.Veiled, 6f, 0f, 1f)]
        [InlineData(NightmareAffix.Veiled, 6.001f, 0f, 0.7f)]
        [InlineData(NightmareAffix.Hollow, 3f, 0f, 1f)]
        [InlineData(NightmareAffix.Hollow, 2.999f, 0f, 0.7f)]
        [InlineData(NightmareAffix.Facing, 4f, 0.5f, 0.7f)]
        [InlineData(NightmareAffix.Facing, 4f, 0.499f, 1f)]
        [InlineData(NightmareAffix.Facing, 4f, -1f, 1f)]
        [InlineData(NightmareAffix.Veiled | NightmareAffix.Hollow | NightmareAffix.Facing, -1f, 1f, 1f)]
        public void Distance_and_cone_guards_have_exact_counterplay_boundaries(
            NightmareAffix affixes, float distance, float dot, float expected)
        {
            Assert.Equal(expected, Incoming(affixes, distance: distance, dot: dot), 5);
        }

        [Fact]
        public void Allied_and_channel_guards_require_their_state_and_total_reduction_is_capped()
        {
            Assert.Equal(1f, Incoming(NightmareAffix.Packbound));
            Assert.Equal(0.7f, Incoming(NightmareAffix.Packbound, ally: true), 5);
            Assert.Equal(1f, Incoming(NightmareAffix.Committed));
            Assert.Equal(0.7f, Incoming(NightmareAffix.Committed, channeling: true), 5);
            var guards = NightmareAffix.Veiled | NightmareAffix.Facing | NightmareAffix.Packbound | NightmareAffix.Pulsing | NightmareAffix.Committed;
            Assert.Equal(0.6f, Incoming(guards, 7, 1, true, true, age: 2), 5);
            Assert.Equal(0.8f, Incoming(guards, 7, 1, true, true, age: 2, boss: true), 5);
            Assert.Equal(1.2f, Incoming(guards, 7, 1, true, true, recovering: true, age: 2), 5);
            Assert.Equal(1.2f, Incoming(guards, 7, 1, true, true, age: 2, boss: true, opening: true), 5);
            Assert.Equal(0.7f, Incoming(NightmareAffix.Veiled, 7, recovering: true), 5);
        }

        [Theory]
        [InlineData(0f, false)]
        [InlineData(1.999f, false)]
        [InlineData(2f, true)]
        [InlineData(4.999f, true)]
        [InlineData(5f, false)]
        [InlineData(7.999f, false)]
        [InlineData(8f, true)]
        [InlineData(11f, false)]
        public void Pulse_warmup_and_repeated_open_windows_are_damage_windows(float age, bool guarded)
        {
            Assert.Equal(guarded, MonsterBehavior.PulseGuarded(age));
            Assert.Equal(guarded ? 0.7f : 1f, Incoming(NightmareAffix.Pulsing, age: age), 5);
        }

        [Fact]
        public void Skittish_hit_slow_changes_movement_without_affecting_other_affixes()
        {
            Assert.Equal(20f, MonsterBehavior.MovementPct(NightmareAffix.Skittish, false));
            Assert.Equal(-15f, MonsterBehavior.MovementPct(NightmareAffix.Skittish | NightmareAffix.Veiled, true));
            Assert.Equal(0f, MonsterBehavior.MovementPct(NightmareAffix.Veiled, true));
        }

        [Theory]
        [InlineData(3.999f, 1f, 10f, 0f)]
        [InlineData(4f, 0.5f, 10f, 0.5f)]
        [InlineData(4f, 100f, 10f, 10f)]
        [InlineData(4f, 1f, 0.25f, 0.25f)]
        [InlineData(4f, -1f, 10f, 0f)]
        [InlineData(4f, 1f, -1f, 0f)]
        public void Healing_waits_for_quiet_and_never_exceeds_elapsed_time_or_budget(
            float quiet, float elapsed, float budget, float expected)
        {
            Assert.Equal(expected, MonsterBehavior.HealingPct(quiet, elapsed, budget), 5);
        }

        [Fact]
        public void Repeated_healing_requests_exhaust_one_life_budget()
        {
            float remaining = 10f, healed = 0f;
            for (int i = 0; i < 30; i++)
            {
                float amount = MonsterBehavior.HealingPct(4 + i * 0.75f, 0.75f, remaining);
                remaining -= amount;
                healed += amount;
            }
            Assert.Equal(10f, healed, 5);
            Assert.Equal(0f, remaining);
            Assert.Equal(0f, MonsterBehavior.HealingPct(100, 100, remaining));
        }

        [Fact]
        public void Last_stand_warning_requires_threshold_affix_and_unused_life_trigger()
        {
            Assert.False(MonsterBehavior.ShouldWarnLastStand(NightmareAffix.LastStand, 0.351f, false));
            Assert.True(MonsterBehavior.ShouldWarnLastStand(NightmareAffix.LastStand, 0.35f, false));
            Assert.True(MonsterBehavior.ShouldWarnLastStand(NightmareAffix.LastStand, 0.1f, false));
            Assert.False(MonsterBehavior.ShouldWarnLastStand(NightmareAffix.LastStand, 0.1f, true));
            Assert.False(MonsterBehavior.ShouldWarnLastStand(NightmareAffix.Beacon, 0.1f, false));
        }

        [Theory]
        [InlineData(1f, 0.7f, 1)]
        [InlineData(0.7f, 0.69f, 0)]
        [InlineData(0.71f, 0.701f, 0)]
        [InlineData(0.6f, 0.4f, 2)]
        [InlineData(1f, 0.2f, 3)]
        [InlineData(0.4f, 0.3f, 0)]
        [InlineData(0.2f, 1f, 0)]
        [InlineData(0.4f, 0.4f, 0)]
        public void Phase_crossings_report_each_downward_threshold_including_large_hits(float previous, float current, int expected)
        {
            Assert.Equal(expected, MonsterBehavior.CrossedHealthPhases(previous, current));
        }

        [Fact]
        public void Equal_builds_have_the_same_pressure_for_one_or_four_players()
        {
            var player = new Build { DreamLevel = 25, SpentStarPoints = 40 };
            var solo = DreamPressure.Average(new[] { player });
            var party = DreamPressure.Average(new[] { player, player, player, player });
            Assert.Equal(1.6, solo.HealthMultiplier, 8);
            Assert.Equal(1.29, solo.DamageMultiplier, 8);
            Assert.Equal(solo.HealthMultiplier, party.HealthMultiplier);
            Assert.Equal(solo.DamageMultiplier, party.DamageMultiplier);
            var joining = DreamPressure.Average(new Build[] { player, null });
            Assert.Equal(13, joining.AverageDreamLevel);
            Assert.Equal(20, joining.AverageSpentStarPoints);
            Assert.Equal(1.25, joining.HealthMultiplier, 8);
            Assert.Equal(1.121, joining.DamageMultiplier, 8);
        }

        private static float Incoming(NightmareAffix affixes, float distance = 4, float dot = 0,
            bool ally = false, bool channeling = false, bool recovering = false, float age = 0,
            bool boss = false, bool opening = false) =>
            MonsterBehavior.IncomingMultiplier(affixes, distance, dot, ally, channeling, recovering, age, boss, opening);
    }
}
