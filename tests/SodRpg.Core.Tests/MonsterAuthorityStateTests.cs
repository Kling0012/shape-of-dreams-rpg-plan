using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MonsterAuthorityStateTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Same_live_netId_special_to_ordinary_clears_tags_and_rejects_old_generation(bool variant)
        {
            var state = new MonsterAuthorityState();
            string variantId = variant ? Variants.All.First().Id : null;
            Assert.True(state.Set(11, 7, variant ? NightmareAffix.None : NightmareAffix.Ironclad, variantId));
            Assert.True(state.TryGet(7, out var original));
            Assert.Equal(variantId, original.VariantId);
            Assert.True(state.Observe(22, out bool changed));
            Assert.True(changed);
            Assert.False(state.TryGet(7, out _));
            Assert.True(state.Set(22, 7, NightmareAffix.None, null));
            Assert.False(state.Set(11, 7, NightmareAffix.Ironclad, variantId));
            Assert.True(state.TryGet(7, out var ordinary));
            Assert.Equal(NightmareAffix.None, ordinary.Nightmare);
            Assert.Null(ordinary.VariantId);
        }

        [Fact]
        public void Same_live_netId_variant_to_nightmare_keeps_only_the_new_classification()
        {
            var state = new MonsterAuthorityState();
            string variantId = Variants.All.First().Id;
            Assert.True(state.Set(11, 7, NightmareAffix.None, variantId));
            Assert.True(state.Observe(22, out _));
            Assert.True(state.Set(22, 7, NightmareAffix.Veiled | NightmareAffix.LastStand, null));
            Assert.False(state.Set(11, 7, NightmareAffix.None, variantId));
            Assert.True(state.TryGet(7, out var nightmare));
            Assert.Equal(NightmareAffix.Veiled | NightmareAffix.LastStand, nightmare.Nightmare);
            Assert.Null(nightmare.VariantId);
        }

        [Fact]
        public void Authority_change_requires_build_resend_even_when_new_zone_snapshot_cannot_apply()
        {
            var profile = Profile.CreateNew(809);
            Rules.BeginRun(profile, "run");
            var progress = new RunChoiceProgress();
            progress.BeginRun("run", 0);
            progress.Arrive("run", 1);
            var state = new MonsterAuthorityState();
            Assert.True(state.Observe(11, out _));
            state.BuildSent();
            Assert.False(state.BuildResendRequired);
            var catchup = RunChoiceSnapshot.Capture(profile.Run, 0, 1, 1, 22);
            Assert.True(state.Observe(catchup.AuthorityGeneration, out bool changed));
            Assert.True(changed);
            Assert.True(progress.Receive(catchup));
            Assert.False(progress.ApplyCurrent(profile, 1));
            Assert.True(state.BuildResendRequired);
            state.BuildSent();
            Assert.True(state.Observe(22, out changed));
            Assert.False(changed);
            Assert.False(state.BuildResendRequired);
            Assert.False(state.Observe(11, out _));
            Assert.False(state.BuildResendRequired);
        }

        [Fact]
        public void Explicit_ordinary_replay_removes_special_classification_within_current_generation()
        {
            var state = new MonsterAuthorityState();
            Assert.True(state.Set(11, 7, NightmareAffix.None, Variants.All.First().Id));
            Assert.True(state.Set(11, 7, NightmareAffix.None, null));
            Assert.True(state.TryGet(7, out var ordinary));
            Assert.Null(ordinary.VariantId);
            Assert.Equal(NightmareAffix.None, ordinary.Nightmare);
        }
    }
}
