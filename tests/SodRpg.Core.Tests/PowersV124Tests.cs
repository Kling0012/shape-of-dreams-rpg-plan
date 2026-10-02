using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    // Loc.Japanese is process-wide; these text checks must not race other test collections.
    [CollectionDefinition("V124 power localization", DisableParallelization = true)]
    public sealed class PowersV124Collection { }

    [Collection("V124 power localization")]
    public class PowersV124Tests
    {
        private static Build With(params (Power Power, int Value)[] powers)
        {
            var build = new Build();
            foreach (var (power, value) in powers) build.Powers[power] = value;
            return build;
        }

        [Fact]
        public void Still_water_requires_own_skill_stun_and_only_success_consumes_the_two_second_cooldown()
        {
            var build = With((Power.StillWater, 8));
            var rt = new PowerRuntime(build, 0);
            Assert.Equal(0f, rt.TakeStillWater(0, false, 1000));
            Assert.Equal(0f, rt.TakeStillWater(0, true, 0));
            Assert.Equal(80f, rt.TakeStillWater(0, true, 1000));
            rt.SetBuild(build);
            Assert.Equal(0f, rt.TakeStillWater(1.999f, true, 1000));
            Assert.Equal(160f, rt.TakeStillWater(2, true, 2000));
            Assert.Equal(0f, rt.TakeStillWater(3.999f, true, 2000));
            Assert.Equal(160f, rt.TakeStillWater(4, true, 2000));
        }

        [Fact]
        public void Spenders_ward_accumulates_partial_spends_and_expires_each_grant_independently()
        {
            var rt = new PowerRuntime(With((Power.SpendersWard, 20)), 0);
            Assert.Equal(0f, rt.TakeSpendersWard(0, 60, 1000));
            Assert.Equal(0f, rt.TakeSpendersWard(1, 39, 1000));
            Assert.Equal(200f, rt.TakeSpendersWard(2, 1, 1000));
            Assert.Equal(200f, rt.TakeSpendersWard(3, 100, 1000));
            Assert.Equal(200f, rt.TakeSpendersWard(4, 250, 1000));
            Assert.Equal(0f, rt.TakeSpendersWard(5, 150, 1000));
            Assert.Equal(3, rt.SpendersWardStacks(11.999f));
            Assert.Equal(2, rt.SpendersWardStacks(12));
            Assert.Equal(200f, rt.TakeSpendersWard(12, 100, 1000));
            Assert.Equal(3, rt.SpendersWardStacks(12));
            Assert.Equal(2, rt.SpendersWardStacks(13));
            Assert.Equal(1, rt.SpendersWardStacks(14));
            Assert.Equal(0, rt.SpendersWardStacks(22));
        }

        [Fact]
        public void Spenders_ward_discards_completed_hundreds_at_cap_but_keeps_the_remainder()
        {
            var rt = new PowerRuntime(With((Power.SpendersWard, 10)), 0);
            Assert.Equal(300f, rt.TakeSpendersWard(0, 300, 1000));
            Assert.Equal(0f, rt.TakeSpendersWard(1, 250, 1000));
            Assert.Equal(3, rt.SpendersWardStacks(9.999f));
            Assert.Equal(0, rt.SpendersWardStacks(10));
            Assert.Equal(0f, rt.TakeSpendersWard(10, 49, 1000));
            Assert.Equal(100f, rt.TakeSpendersWard(10, 1, 1000));
            Assert.Equal(1, rt.SpendersWardStacks(10));
        }

        [Fact]
        public void Spenders_ward_preserves_expiries_and_remainder_across_build_refreshes()
        {
            var rt = new PowerRuntime(With((Power.SpendersWard, 5)), 0);
            Assert.Equal(50f, rt.TakeSpendersWard(0, 150, 1000));
            rt.SetBuild(With((Power.SpendersWard, 20)));
            Assert.Equal(200f, rt.TakeSpendersWard(1, 50, 1000));
            Assert.Equal(2, rt.SpendersWardStacks(1));
            Assert.Equal(1, rt.SpendersWardStacks(10));
            Assert.Equal(0, rt.SpendersWardStacks(11));
        }

        [Fact]
        public void Spenders_ward_handles_large_spends_without_overflow_or_unbounded_stacks()
        {
            var rt = new PowerRuntime(With((Power.SpendersWard, 20)), 0);
            Assert.Equal(0f, rt.TakeSpendersWard(0, 99, 1000));
            Assert.Equal(600f, rt.TakeSpendersWard(0, int.MaxValue, 1000));
            Assert.Equal(3, rt.SpendersWardStacks(0));
            // (99 + int.MaxValue) % 100 = 46: no completed hundreds were banked.
            Assert.Equal(0f, rt.TakeSpendersWard(10, 53, 1000));
            Assert.Equal(200f, rt.TakeSpendersWard(10, 1, 1000));
        }

        [Theory]
        [InlineData(0, 1000)]
        [InlineData(-100, 1000)]
        [InlineData(100, 0)]
        [InlineData(100, -1000)]
        public void Invalid_spends_do_not_advance_the_gold_remainder(int gold, float health)
        {
            var rt = new PowerRuntime(With((Power.SpendersWard, 10)), 0);
            Assert.Equal(0f, rt.TakeSpendersWard(0, 50, 1000));
            Assert.Equal(0f, rt.TakeSpendersWard(0, gold, health));
            Assert.Equal(100f, rt.TakeSpendersWard(0, 50, 1000));
            Assert.Equal(1, rt.SpendersWardStacks(0));
        }

        [Fact]
        public void Perfect_read_requires_actual_invulnerability_and_refreshes_without_stacking()
        {
            var build = With((Power.PerfectRead, 20));
            var rt = new PowerRuntime(build, 0);
            Assert.False(rt.TakePerfectRead(0, false));
            Assert.Equal(0, rt.Current(0).AttackSpeedPct);
            Assert.True(rt.TakePerfectRead(0, true));
            Assert.Equal(20, rt.Current(0).AttackSpeedPct);
            rt.SetBuild(build);
            Assert.False(rt.TakePerfectRead(1.499f, true));
            Assert.False(rt.TakePerfectRead(1.5f, false));
            Assert.True(rt.TakePerfectRead(1.5f, true));
            Assert.Equal(20, rt.Current(3).AttackSpeedPct);
            Assert.Equal(20, rt.Current(5.499f).AttackSpeedPct); // v1.27：3秒→4秒
            Assert.Equal(0, rt.Current(5.5f).AttackSpeedPct);
        }

        [Fact]
        public void Perfect_read_adds_to_existing_speed_buffs_and_tracks_equipped_values()
        {
            var rt = new PowerRuntime(With((Power.PerfectRead, 20), (Power.Momentum, 5), (Power.Frenzy, 2)), 0)
                { NearbyEnemies = 2 };
            rt.OnKill(0);
            Assert.True(rt.TakePerfectRead(0, true));
            Assert.Equal(29, rt.Current(0).AttackSpeedPct);
            rt.SetBuild(With((Power.PerfectRead, 40), (Power.Momentum, 5), (Power.Frenzy, 2)));
            Assert.Equal(49, rt.Current(1).AttackSpeedPct);
            rt.SetBuild(With((Power.Momentum, 5), (Power.Frenzy, 2)));
            Assert.Equal(9, rt.Current(1).AttackSpeedPct);
        }

        [Theory]
        [InlineData(3, -1, 0)]
        [InlineData(3, 0, 0)]
        [InlineData(3, 1, 3)]
        [InlineData(3, 5, 15)]
        [InlineData(3, 6, 18)]
        [InlineData(3, 7, 18)]
        [InlineData(1, int.MaxValue, 6)]
        [InlineData(6, 1, 6)]
        [InlineData(6, 4, 18)]
        [InlineData(int.MaxValue, int.MaxValue, 18)]
        [InlineData(-1, 6, 0)]
        public void Lucid_boon_caps_dream_count_and_total_bonus_not_other_powers(int value, int dreams, int expected)
        {
            var rt = new PowerRuntime(With((Power.LucidBoon, value)), 0) { EvilDreamCount = dreams };
            Assert.Equal(expected, rt.Current(0).AttackPct);
            Assert.Equal(expected, rt.Current(0).PowerPct);
            rt.EvilDreamCount = 0;
            Assert.Equal(0, rt.Current(1).AttackPct);
            Assert.Equal(0, rt.Current(1).PowerPct);
        }

        [Fact]
        public void Lucid_boon_adds_to_existing_buffs_and_recalculates_on_dream_and_build_changes()
        {
            var rt = new PowerRuntime(With((Power.LucidBoon, 3), (Power.CrystalResonance, 2),
                (Power.PreyPride, 3), (Power.UltimateSurge, 5), (Power.Overload, 7)), 0)
                { EvilDreamCount = 6, GemQualityTotal = 200, HuntLevel = 2 };
            rt.OnSkillUsed(0, false, true);
            rt.OnSkillUsed(0, false, false);
            Assert.Equal(40, rt.Current(1).AttackPct); // v1.27：過負荷は攻撃力にも
            Assert.Equal(40, rt.Current(1).PowerPct);
            rt.EvilDreamCount = 1;
            Assert.Equal(25, rt.Current(1).AttackPct);
            Assert.Equal(25, rt.Current(1).PowerPct);
            rt.SetBuild(With((Power.LucidBoon, 2)));
            Assert.Equal(2, rt.Current(1).AttackPct);
            Assert.Equal(2, rt.Current(1).PowerPct);
        }

        [Fact]
        public void Unequipped_powers_do_not_trigger_consume_gold_or_start_cooldowns()
        {
            var rt = new PowerRuntime(new Build(), 0) { EvilDreamCount = 6 };
            Assert.Equal(0f, rt.TakeStillWater(0, true, 1000));
            Assert.Equal(0f, rt.TakeSpendersWard(0, 99, 1000));
            Assert.False(rt.TakePerfectRead(0, true));
            Assert.Equal(default(DynamicBonus), rt.Current(0));
            rt.SetBuild(With((Power.StillWater, 8), (Power.SpendersWard, 10), (Power.PerfectRead, 20)));
            Assert.Equal(80f, rt.TakeStillWater(0, true, 1000));
            Assert.True(rt.TakePerfectRead(0, true));
            Assert.Equal(0f, rt.TakeSpendersWard(0, 1, 1000));
            Assert.Equal(100f, rt.TakeSpendersWard(0, 99, 1000));
        }

        [Fact]
        public void Runtime_caps_hand_constructed_shield_and_speed_values()
        {
            var rt = new PowerRuntime(With((Power.StillWater, 1000), (Power.SpendersWard, 1000),
                (Power.PerfectRead, 1000)), 0);
            Assert.Equal(150f, rt.TakeStillWater(0, true, 1000));
            Assert.Equal(600f, rt.TakeSpendersWard(0, 400, 1000));
            Assert.True(rt.TakePerfectRead(0, true));
            Assert.Equal(40, rt.Current(0).AttackSpeedPct);
        }

        [Theory]
        [InlineData(Power.StillWater, 39, 15)]
        [InlineData(Power.SpendersWard, 40, 20)]
        [InlineData(Power.PerfectRead, 41, 40)]
        [InlineData(Power.LucidBoon, 42, 18)]
        public void New_power_ids_round_trip_and_clamp_received_and_equipped_values(Power power, int id, int cap)
        {
            Assert.Equal(id, (int)power);
            Assert.Equal(cap, Content.PowerCap(power));
            Assert.Equal(cap - 1, Build.Decode(With((power, cap - 1)).Encode()).Get(power));
            Assert.Equal(cap, Build.Decode(With((power, cap + 100)).Encode()).Get(power));
            Assert.Equal(0, Build.Decode(With((power, -1)).Encode()).Get(power));
            var profile = Profile.CreateNew(123);
            foreach (var slot in new[] { Slot.Armor, Slot.Charm })
            {
                var relic = new Relic
                {
                    Uid = slot.ToString(), BaseId = Content.BasesFor(slot).First().Id,
                    Rarity = Rarity.Epic, ItemLevel = 1,
                };
                relic.Powers.Add(new PowerLine(power, cap));
                profile.Stash.Add(relic);
                profile.Hero("H").Equipped[(int)slot] = relic.Uid;
            }
            Assert.Equal(cap, Build.Compute(profile, "H", 0).Get(power));
        }

        [Fact]
        public void All_four_powers_round_trip_together_without_colliding_with_existing_powers()
        {
            var build = With((Power.Wildfire, 30), (Power.StillWater, 8), (Power.SpendersWard, 10),
                (Power.PerfectRead, 20), (Power.LucidBoon, 3));
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            Assert.Equal(build.Powers.ToArray(), decoded.Powers.ToArray());
        }

        [Theory]
        [InlineData(Power.StillWater, "止水", "Still Water", "止水の", "Stilled")]
        [InlineData(Power.SpendersWard, "散財の護り", "Spender's Ward", "散財の", "Lavish")]
        [InlineData(Power.PerfectRead, "見切り", "Perfect Read", "見切りの", "Keen-eyed")]
        [InlineData(Power.LucidBoon, "明晰", "Lucid Boon", "明晰な", "Lucid")]
        public void New_powers_have_requested_names_epithets_and_value_bearing_descriptions(
            Power power, string ja, string en, string jaEpithet, string enEpithet)
        {
            var epithet = Content.Epithet(power);
            Assert.NotNull(epithet);
            Assert.Equal(jaEpithet, epithet.Ja);
            Assert.Equal(enEpithet, epithet.En);
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                Assert.Equal(ja, Content.PowerName(power));
                Assert.Contains(ja, Content.FormatPower(power, 7));
                Assert.Contains("7%", Content.FormatPower(power, 7));
                Loc.Japanese = false;
                Assert.Equal(en, Content.PowerName(power));
                Assert.Contains(en, Content.FormatPower(power, 7));
                Assert.Contains("7%", Content.FormatPower(power, 7));
            }
            finally { Loc.Japanese = previous; }
        }

        [Theory]
        [InlineData(Slot.Hands, Power.StillWater, 4, 8)]
        [InlineData(Slot.Armor, Power.StillWater, 4, 8)]
        [InlineData(Slot.Charm, Power.SpendersWard, 5, 10)]
        [InlineData(Slot.Charm, Power.LucidBoon, 1, 3)]
        [InlineData(Slot.Head, Power.LucidBoon, 1, 3)]
        [InlineData(Slot.Feet, Power.PerfectRead, 10, 20)]
        public void New_powers_have_the_requested_slot_pool_ranges(Slot slot, Power power, int min, int max)
        {
            var range = Assert.Single(Content.PowerPool(slot).Where(p => p.Power == power));
            Assert.Equal(min, range.Min);
            Assert.Equal(max, range.Max);
        }
    }
}
