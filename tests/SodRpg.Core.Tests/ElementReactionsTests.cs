using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class ElementReactionsTests
    {
        private static Build All(int steam = 50, int eclipse = 10, int cinder = 1, int crystal = 5)
        {
            var b = new Build();
            b.Powers[Power.Steam] = steam;
            b.Powers[Power.Eclipse] = eclipse;
            b.Powers[Power.Cinder] = cinder;
            b.Powers[Power.FrostCrystal] = crystal;
            return b;
        }

        private static readonly ElementSnapshot Four = new ElementSnapshot(3, true, 5, 5);

        [Fact]
        public void Every_element_combination_detects_only_the_four_declared_pairs()
        {
            for (int bits = 0; bits < 16; bits++)
            {
                bool fire = (bits & 1) != 0, cold = (bits & 2) != 0;
                bool light = (bits & 4) != 0, dark = (bits & 8) != 0;
                var elements = new ElementSnapshot(fire ? 7 : 0, cold, light ? 5 : 0, dark ? 5 : 0);
                Assert.Equal(fire && cold, elements.HasPair(Power.Steam));
                Assert.Equal(light && dark, elements.HasPair(Power.Eclipse));
                Assert.Equal(fire && dark, elements.HasPair(Power.Cinder));
                Assert.Equal(light && cold, elements.HasPair(Power.FrostCrystal));
                Assert.False(elements.HasPair(Power.Convergence));
                var r = new ElementReactionRuntime().Apply(All(), -123, elements, 0, 100, 200, 400);
                Assert.Equal(fire && cold, r.Steam);
                Assert.Equal(light && dark ? 10 : 0, r.ExposePercent);
                Assert.Equal(fire && dark ? 1 : 0, r.CinderStacks);
                Assert.Equal(light && cold ? 20f : 0f, r.Shield);
            }
        }

        [Fact]
        public void Snapshot_is_unchanged_and_all_four_reactions_can_coexist_with_convergence()
        {
            var runtime = new ElementReactionRuntime();
            var b = All();
            b.Powers[Power.Convergence] = 100;
            var r = runtime.Apply(b, 1, Four, 0, 300, 200, 400);
            Assert.Equal(150f, r.SteamDamage);
            Assert.Equal(20f, r.Shield);
            Assert.Equal(3, Four.Fire);
            Assert.True(Four.Cold);
            Assert.Equal(5, Four.Light);
            Assert.Equal(5, Four.Dark);
            Assert.Equal(300f, new PowerRuntime(b, 0).TakeConvergence(0, 1, true, 300));
        }

        [Fact]
        public void Each_reaction_has_its_own_exact_six_second_gate_per_enemy()
        {
            var rt = new ElementReactionRuntime();
            var b = All();
            Assert.True(rt.Apply(b, 1, new ElementSnapshot(1, true, 0, 0), 10, 100, 200, 400).Steam);
            var otherPairs = rt.Apply(b, 1, Four, 11, 100, 200, 400);
            Assert.False(otherPairs.Steam);
            Assert.Equal(10, otherPairs.ExposePercent);
            Assert.Equal(1, otherPairs.CinderStacks);
            Assert.Equal(20f, otherPairs.Shield);
            Assert.True(rt.Apply(b, 2, Four, 11, 100, 200, 400).Steam);
            Assert.False(rt.Apply(b, 1, Four, 15.999f, 100, 200, 400).Steam);
            var boundary = rt.Apply(b, 1, Four, 16, 100, 200, 400);
            Assert.True(boundary.Steam);
            Assert.Equal(0, boundary.ExposePercent);
            Assert.Equal(10, rt.Apply(b, 1, Four, 17, 100, 200, 400).ExposePercent);
        }

        [Fact]
        public void Build_resends_or_unequipping_and_reequipping_do_not_reset_target_gates()
        {
            var rt = new ElementReactionRuntime();
            Assert.True(rt.Apply(All(), 1, Four, 10, 100, 200, 400).Steam);
            Assert.False(rt.Apply(new Build(), 1, Four, 11, 100, 200, 400).Steam);
            Assert.False(rt.Apply(Build.Decode(All().Encode()), 1, Four, 12, 100, 200, 400).Steam);
            Assert.True(rt.Apply(All(), 1, Four, 16, 100, 200, 400).Steam);
        }

        [Fact]
        public void Unequipped_and_isolated_effects_cannot_react_or_spend_a_gate()
        {
            var rt = new ElementReactionRuntime();
            Assert.Equal(default, rt.Apply(new Build(), 1, Four, 0, 100, 200, 400));
            Assert.Equal(default, rt.Apply(All(), 1, Four, 0, 100, 200, 400, isolatedEffect: true));
            Assert.Equal(0, rt.ExposePercent(1, 0));
            Assert.Equal(0, rt.OnDeath(1));
            Assert.True(rt.Apply(All(), 1, Four, 0, 100, 200, 400).Steam);
        }

        [Fact]
        public void Expose_is_owned_by_one_hero_and_expires_at_four_seconds()
        {
            var first = new ElementReactionRuntime();
            var second = new ElementReactionRuntime();
            first.Apply(All(), -7, Four, 10, 100, 200, 400);
            Assert.Equal(10, first.ExposePercent(-7, 13.999f));
            Assert.Equal(0, second.ExposePercent(-7, 13));
            Assert.Equal(0, first.ExposePercent(-8, 13));
            Assert.Equal(0, first.ExposePercent(-7, 14));
        }

        [Fact]
        public void Cinder_marks_do_not_stack_and_death_consumes_them_once()
        {
            var rt = new ElementReactionRuntime();
            rt.Apply(All(), 1, Four, 0, 100, 200, 400);
            rt.Apply(All(), 1, Four, 6, 100, 200, 400);
            Assert.Equal(1, rt.OnDeath(1));
            Assert.Equal(0, rt.OnDeath(1));
            Assert.Equal(0, rt.ExposePercent(1, 6));
            // A new target lifetime with the same host ID starts fresh.
            Assert.True(rt.Apply(All(), 1, Four, 6, 100, 200, 400).Steam);
            Assert.Equal(0, rt.OnDeath(1, isolatedEffect: true));
            Assert.Equal(0, rt.OnDeath(1));
        }

        [Fact]
        public void Waypoint_doubles_amounts_after_caps_but_leaves_radius_duration_and_gates_fixed()
        {
            var rt = new ElementReactionRuntime();
            var b = All(999, 999, 999, 999);
            var r = rt.Apply(b, 1, Four, 0, 100, 200, 400, 2f);
            Assert.Equal(480f, r.SteamDamage);
            Assert.Equal(50, r.ExposePercent);
            Assert.Equal(2, r.CinderStacks);
            Assert.Equal(96f, r.Shield);
            Assert.Equal(2, rt.OnDeath(1));
            Assert.Equal(3f, ElementReactionRuntime.SteamRadius);
            Assert.Equal(30f, ElementReactionRuntime.SteamSlowPercent);
            Assert.Equal(2f, ElementReactionRuntime.SteamSlowDuration);
            Assert.Equal(4f, ElementReactionRuntime.CinderRadius);
            Assert.Equal(4f, ElementReactionRuntime.ShieldDuration);
            Assert.Equal(4f, ElementReactionRuntime.ExposeDuration);
            Assert.Equal(6f, ElementReactionRuntime.Interval);
        }

        [Fact]
        public void Invalid_values_cannot_emit_invalid_damage_or_shields_and_clear_removes_marks()
        {
            var rt = new ElementReactionRuntime();
            Assert.Equal(default, rt.Apply(All(), 1, Four, float.NaN, 100, 100, 100));
            Assert.Equal(default, rt.Apply(All(), 0, Four, 0, 100, 100, 100));
            var r = rt.Apply(All(), 1, Four, 0, float.NaN, float.PositiveInfinity, -100, float.NaN);
            Assert.Equal(0f, r.SteamDamage);
            Assert.Equal(0f, r.Shield);
            rt.Clear();
            Assert.Equal(0, rt.OnDeath(1));
            Assert.Equal(0, rt.ExposePercent(1, 0));
        }

        [Fact]
        public void Powers_round_trip_through_existing_build_encoding_with_caps()
        {
            var decoded = Build.Decode(All(999, 999, 999, 999).Encode());
            Assert.NotNull(decoded);
            Assert.Equal(300, decoded.Get(Power.Steam));
            Assert.Equal(62, decoded.Get(Power.Eclipse));
            Assert.Equal(2, decoded.Get(Power.Cinder));
            Assert.Equal(30, decoded.Get(Power.FrostCrystal));
            Assert.Equal(43, (int)Power.ShadowStep);
            Assert.Equal(new[] { 44, 45, 46, 47 }, decoded.Powers.Keys.Select(p => (int)p).ToArray());
        }

        [Theory]
        [InlineData(Power.Steam, Slot.Hands, "蒸気", "Steam", 120)]
        [InlineData(Power.Eclipse, Slot.Head, "蝕", "Eclipse", 25)]
        [InlineData(Power.Cinder, Slot.Hands, "燃え殻", "Cinder", 1)]
        [InlineData(Power.FrostCrystal, Slot.Head, "氷晶", "Frost Crystal", 12)]
        public void Reactions_have_appropriate_epic_pools_caps_and_names(Power power, Slot slot, string ja, string en, int cap)
        {
            Assert.Equal(cap, Content.PowerCap(power));
            Assert.Single(Content.PowerPool(Slot.Charm), p => p.Power == power);
            Assert.Single(Content.PowerPool(slot), p => p.Power == power);
            Assert.DoesNotContain(Content.PowerPool(Slot.Weapon), p => p.Power == power);
            Assert.NotNull(Content.Epithet(power));
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = true;
                Assert.Equal(ja, Content.PowerName(power));
                Loc.Japanese = false;
                Assert.Equal(en, Content.PowerName(power));
            }
            finally { Loc.Japanese = previous; }
        }
    }
}
