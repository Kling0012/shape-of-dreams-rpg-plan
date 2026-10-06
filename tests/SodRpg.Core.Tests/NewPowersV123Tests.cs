using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class NewPowersV123Tests
    {
        private static Build With(params (Power Power, int Value)[] powers)
        {
            var build = new Build();
            foreach (var (power, value) in powers) build.Powers[power] = value;
            return build;
        }

        [Fact]
        public void Finale_requires_three_distinct_slots_and_includes_the_eight_second_boundary()
        {
            var rt = new PowerRuntime(With((Power.Finale, 20)), 0);
            Assert.Equal(0f, rt.TakeFinale(0, 0));
            Assert.Equal(0f, rt.TakeFinale(1, 0));
            Assert.Equal(0f, rt.TakeFinale(4, 1));
            Assert.Equal(0f, rt.TakeFinale(8, -1));
            Assert.Equal(0.2f, rt.TakeFinale(9, 2), 4);
        }

        [Fact]
        public void Finale_rejects_expired_uses_and_refreshes_each_slot_independently()
        {
            var rt = new PowerRuntime(With((Power.Finale, 50)), 0);
            rt.TakeFinale(0, 0);
            rt.TakeFinale(1, 1);
            Assert.Equal(0f, rt.TakeFinale(8.01f, 2));
            Assert.Equal(0.5f, rt.TakeFinale(8.02f, 0), 4);
        }

        [Fact]
        public void Finale_consumes_the_combo_and_preserves_cooldown_across_build_changes()
        {
            var build = With((Power.Finale, 20));
            var rt = new PowerRuntime(build, 0);
            rt.TakeFinale(0, 0);
            rt.TakeFinale(0, 1);
            Assert.Equal(0.2f, rt.TakeFinale(0, 2), 4);
            Assert.Equal(0f, rt.TakeFinale(10, 0));
            Assert.Equal(0f, rt.TakeFinale(10, 1));
            Assert.Equal(0.2f, rt.TakeFinale(10, 2), 4);
            rt.SetBuild(build);
            rt.TakeFinale(12, 0);
            rt.TakeFinale(12, 1);
            Assert.Equal(0f, rt.TakeFinale(12, 2));
            Assert.Equal(0f, rt.TakeFinale(19.99f, 0));
            Assert.Equal(0.2f, rt.TakeFinale(20, 0), 4);
        }

        [Fact]
        public void Critical_echo_only_consumes_cooldown_on_crits_and_is_ready_at_half_a_second()
        {
            var build = With((Power.CriticalEcho, 12));
            var rt = new PowerRuntime(build, 0);
            Assert.Equal(0f, rt.TakeCriticalEcho(0, false));
            Assert.Equal(1.2f, rt.TakeCriticalEcho(0, true), 4);
            rt.SetBuild(build);
            Assert.Equal(0f, rt.TakeCriticalEcho(0.49f, true));
            Assert.Equal(0f, rt.TakeCriticalEcho(0.5f, false));
            Assert.Equal(1.2f, rt.TakeCriticalEcho(0.5f, true), 4);
        }

        [Fact]
        public void Fetters_amplifies_hindered_targets_only()
        {
            var rt = new PowerRuntime(With((Power.Fetters, 40)), 0);
            Assert.Equal(0f, rt.FettersAmplification(false));
            Assert.Equal(0.4f, rt.FettersAmplification(true), 4);
            rt.SetBuild(new Build());
            Assert.Equal(0f, rt.FettersAmplification(true));
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(100, 6)]
        [InlineData(int.MaxValue, 48)]
        public void Crystal_resonance_counts_complete_quality_hundreds_and_caps_at_eight(int quality, int expected)
        {
            var rt = new PowerRuntime(With((Power.CrystalResonance, 6)), 0) { GemQualityTotal = quality };
            Assert.Equal(expected, rt.Current(0).AttackPct);
            Assert.Equal(expected, rt.Current(0).PowerPct);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(1, 12)]
        [InlineData(9, 36)]
        public void Prey_pride_uses_current_hunt_level_and_caps_at_three(int hunt, int expected)
        {
            var rt = new PowerRuntime(With((Power.PreyPride, 12)), 0) { HuntLevel = hunt };
            Assert.Equal(expected, rt.Current(0).AttackPct);
            Assert.Equal(expected, rt.Current(0).PowerPct);
            rt.HuntLevel = 0;
            Assert.Equal(0, rt.Current(1).AttackPct);
            Assert.Equal(0, rt.Current(1).PowerPct);
        }

        [Fact]
        public void Devotion_caps_at_five_and_resets_when_the_zone_loads()
        {
            var build = With((Power.Devotion, 10));
            var rt = new PowerRuntime(build, 0);
            for (int i = 1; i <= 5; i++)
            {
                Assert.True(rt.OnShrineUsed());
                Assert.Equal(i, rt.DevotionStacks);
                Assert.Equal(i * 10, rt.Current(0).AttackPct);
                Assert.Equal(i * 10, rt.Current(0).PowerPct);
            }
            Assert.False(rt.OnShrineUsed());
            rt.SetBuild(build);
            Assert.Equal(50, rt.Current(1).AttackPct);
            rt.SetBuild(new Build());
            Assert.False(rt.OnShrineUsed());
            Assert.Equal(0, rt.Current(1).PowerPct);
            rt.SetBuild(build);
            Assert.Equal(50, rt.Current(1).PowerPct);
            rt.OnZoneLoaded();
            Assert.Equal(0, rt.DevotionStacks);
            Assert.Equal(0, rt.Current(2).AttackPct);
            Assert.Equal(0, rt.Current(2).PowerPct);
            Assert.True(rt.OnShrineUsed());
            Assert.Equal(10, rt.Current(2).AttackPct);
        }

        [Fact]
        public void Dynamic_bonuses_add_to_existing_buffs_and_follow_build_and_quality_changes()
        {
            var rt = new PowerRuntime(With((Power.CrystalResonance, 2), (Power.PreyPride, 3),
                (Power.Devotion, 4), (Power.UltimateSurge, 5), (Power.Overload, 7)), 0)
            {
                GemQualityTotal = 250,
                HuntLevel = 2,
            };
            rt.OnShrineUsed();
            rt.OnSkillUsed(1, false, true);
            rt.OnSkillUsed(1, false, false);
            Assert.Equal(26, rt.Current(2).AttackPct); // v1.27：過負荷は攻撃力にも
            Assert.Equal(26, rt.Current(2).PowerPct);
            rt.GemQualityTotal = 99;
            rt.HuntLevel = 0;
            Assert.Equal(16, rt.Current(2).AttackPct);
            Assert.Equal(16, rt.Current(2).PowerPct);
            rt.SetBuild(With((Power.Devotion, 6)));
            Assert.Equal(6, rt.Current(2).AttackPct);
            Assert.Equal(6, rt.Current(2).PowerPct);
        }

        [Theory]
        [InlineData(50, 20, 500, 10)]
        [InlineData(50, 200, 500, 50)]
        [InlineData(100, 1000, 500, 50)]
        [InlineData(100, 0, 500, 0)]
        [InlineData(100, -10, 500, 0)]
        [InlineData(100, 100, 0, 0)]
        public void Overflowing_life_converts_discarded_heal_and_caps_each_shield(int value, float discarded, float health, float expected)
        {
            var rt = new PowerRuntime(With((Power.OverflowingLife, value)), 0);
            Assert.Equal(expected, rt.TakeOverflowingLife(discarded, health), 4);
            Assert.Equal(expected, rt.TakeOverflowingLife(discarded, health), 4);
        }

        [Fact]
        public void Wildfire_requires_three_stacks_and_a_successful_chance_roll()
        {
            var rt = new PowerRuntime(With((Power.Wildfire, 60)), 0);
            Assert.False(rt.TakeWildfire(0, 1, 2, 0));
            Assert.False(rt.TakeWildfire(0, 1, 3, 0.6));
            Assert.True(rt.TakeWildfire(0, 1, 3, 0.599));
        }

        [Fact]
        public void Wildfire_cooldown_is_per_victim_and_survives_build_changes()
        {
            var build = With((Power.Wildfire, 15));
            var rt = new PowerRuntime(build, 0);
            Assert.True(rt.TakeWildfire(0, 1, 3, 0));
            Assert.True(rt.TakeWildfire(0, 2, 4, 0));
            rt.SetBuild(build);
            Assert.False(rt.TakeWildfire(1.99f, 1, 3, 0));
            Assert.True(rt.TakeWildfire(2, 1, 3, 0));
            Assert.True(rt.TakeWildfire(2, 2, 3, 0));
        }

        [Fact]
        public void Wildfire_does_not_forget_active_cooldowns_with_many_victims()
        {
            var rt = new PowerRuntime(With((Power.Wildfire, 60)), 0);
            for (int i = 0; i < 250; i++) Assert.True(rt.TakeWildfire(0, i, 3, 0));
            Assert.False(rt.TakeWildfire(0.1f, 0, 3, 0));
            Assert.True(rt.TakeWildfire(2, 300, 3, 0));
            Assert.True(rt.TakeWildfire(2, 0, 3, 0));
            Assert.False(rt.TakeWildfire(2.1f, 300, 3, 0));
        }

        [Fact]
        public void Unequipped_powers_do_not_trigger_or_start_a_combo_or_shrine_stack()
        {
            var rt = new PowerRuntime(new Build(), 0) { GemQualityTotal = 800, HuntLevel = 3 };
            for (int i = 0; i < 3; i++) Assert.Equal(0f, rt.TakeFinale(0, i));
            Assert.Equal(0f, rt.TakeCriticalEcho(0, true));
            Assert.Equal(0f, rt.FettersAmplification(true));
            Assert.Equal(0f, rt.TakeOverflowingLife(100, 500));
            Assert.False(rt.OnShrineUsed());
            Assert.False(rt.TakeWildfire(0, 1, 3, 0));
            Assert.Equal(default(DynamicBonus), rt.Current(0));
            rt.SetBuild(With((Power.Finale, 20), (Power.Devotion, 4)));
            Assert.Equal(0f, rt.TakeFinale(1, 0));
            Assert.Equal(0, rt.DevotionStacks);
        }

        [Theory]
        [InlineData(Power.Finale, 31, 50)]
        [InlineData(Power.CriticalEcho, 32, 12)]
        [InlineData(Power.Fetters, 33, 40)]
        [InlineData(Power.CrystalResonance, 34, 6)]
        [InlineData(Power.PreyPride, 35, 12)]
        [InlineData(Power.OverflowingLife, 36, 100)]
        [InlineData(Power.Devotion, 37, 10)]
        [InlineData(Power.Wildfire, 38, 60)]
        public void New_power_ids_round_trip_and_clamp_received_and_equipped_values(Power power, int id, int cap)
        {
            Assert.Equal(id, (int)power);
            Assert.Equal(cap, Content.PowerCap(power));
            var valid = Build.Decode(With((power, cap - 1)).Encode());
            Assert.NotNull(valid);
            Assert.Equal(cap - 1, valid.Get(power));
            int wireCap = StarDamageScaling.IsDamage(power)
                ? (int)decimal.Ceiling(cap * (2.5m + 1m + 1.5m * 504 / 500))
                : (int)(cap * 2.5m);
            Assert.Equal(wireCap, Build.Decode(With((power, int.MaxValue)).Encode()).Get(power));
            Assert.Equal(0, Build.Decode(With((power, -1)).Encode()).Get(power));

            var profile = Profile.CreateNew(123);
            foreach (var slot in new[] { Slot.Weapon, Slot.Armor })
            {
                var relic = new Relic
                {
                    Uid = slot.ToString(),
                    BaseId = Content.BasesFor(slot).First().Id,
                    Rarity = Rarity.Epic,
                    ItemLevel = 1,
                };
                relic.Powers.Add(new PowerLine(power, cap));
                profile.Stash.Add(relic);
                profile.Hero("H").Equipped[(int)slot] = relic.Uid;
            }
            Assert.Equal(cap, Build.Compute(profile, "H", 0).Get(power));
        }

        [Fact]
        public void Build_round_trips_all_new_powers_together()
        {
            var build = new Build();
            for (int id = 31; id <= 38; id++) build.Powers[(Power)id] = Content.PowerCap((Power)id);
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            Assert.Equal(build.Powers.ToArray(), decoded.Powers.ToArray());
        }

    }
}
