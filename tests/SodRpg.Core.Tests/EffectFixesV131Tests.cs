using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class EffectFixesV131Tests
    {
        private static PowerRuntime With(params (Power Power, int Value)[] powers)
        {
            var build = new Build();
            foreach (var (power, value) in powers) build.Powers[power] = value;
            return new PowerRuntime(build, 0);
        }

        [Fact]
        public void Overload_does_not_start_from_identity_slot()
        {
            var rt = With((Power.Overload, 30));
            rt.OnSkillUsed(0, false, false, isIdentity: true);
            Assert.Equal(0, rt.Current(1).PowerPct);
            rt.OnSkillUsed(0, false, false);
            Assert.True(rt.Current(1).PowerPct > 0);
        }

        [Fact]
        public void Retaliation_needs_an_enemy_attacker()
        {
            var rt = With((Power.Retaliation, 30));
            rt.OnDamaged(0, 10, false);
            Assert.Equal(0, rt.Current(1).AttackPct);
            rt.OnDamaged(0, 10, false, attackedByEnemy: true);
            Assert.True(rt.Current(1).AttackPct > 0);
        }

        [Fact]
        public void Finale_returns_a_remaining_cooldown_fraction()
        {
            var rt = With((Power.Finale, 30));
            rt.TakeFinale(0, 0); rt.TakeFinale(0, 1);
            Assert.Equal(0.3f, rt.TakeFinale(0, 2), 3);
        }

        [Fact]
        public void Pact_curse_resync_rules()
        {
            int e = PactCurseSync.Encode(2, 3);
            Assert.Equal(2, PactCurseSync.Strength(e));
            Assert.Equal(3, PactCurseSync.Ordinal(e));
            Assert.Equal(0, PactCurseSync.Ordinal(PactCurseSync.Encode(1, 0)));
            string k = PactCurseSync.Key("run", 5, 9);
            Assert.True(PactCurseSync.ShouldResend(1, "", k));
            Assert.False(PactCurseSync.ShouldResend(1, k, k));
            Assert.True(PactCurseSync.ShouldResend(1, k, PactCurseSync.Key("run", 6, 9)));
            Assert.False(PactCurseSync.ShouldResend(0, "", k));
            Assert.False(PactCurseSync.ShouldResend(2, "", ""));
            Assert.True(PactCurseSync.ShouldApply(0, 5));
            Assert.True(PactCurseSync.ShouldApply(1, 0));
            Assert.False(PactCurseSync.ShouldApply(1, 1));
            Assert.True(PactCurseSync.ShouldApply(2, 1));
        }

        [Fact]
        public void Owned_registry_counts_only_live_effects()
        {
            var r = new OwnedEffectRegistry<string, string>();
            r.Add("a", "x"); r.Add("a", "dead");
            Assert.Equal(1, r.CountLive("a", s => s == "x"));
            Assert.Equal(0, r.CountLive("b", s => true));
        }

        [Fact]
        public void First_zone_uses_chosen_depth_before_the_run_exists()
        {
            Assert.Equal(6, DreamDepth.ZoneNodeOffsetForGeneration(null, 3, 0, true, false));
            Assert.Equal(0, DreamDepth.ZoneNodeOffsetForGeneration(null, 3, 1, true, false));
            Assert.Equal(4, DreamDepth.ZoneNodeOffsetForGeneration(2, 3, 1, true, false));
            Assert.Equal(0, DreamDepth.ZoneNodeOffsetForGeneration(null, 3, 0, false, false));
            Assert.Equal(0, DreamDepth.ZoneNodeOffsetForGeneration(null, 3, 0, true, true));
        }
    }
}
