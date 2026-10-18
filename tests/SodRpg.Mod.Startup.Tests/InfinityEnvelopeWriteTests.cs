using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    public sealed partial class InfinityLobbyStartTests
    {
        [Theory]
        [InlineData(false, null)]                  // No stored envelope yet: the first write must land.
        [InlineData(true, "previous-envelope")]    // A different text must rewrite and resend.
        public void EnvelopeWriteDecisionWritesWhenTheKeyIsMissingOrTheTextChanged(bool hasCurrent, string current)
            => Assert.True(InfinityMode.EnvelopeNeedsWrite(hasCurrent, current, "envelope-text"));

        [Fact]
        public void EnvelopeWriteDecisionSkipsOnlyAnIdenticalText()
            => Assert.False(InfinityMode.EnvelopeNeedsWrite(true, "envelope-text", "envelope-text"));

        [Fact]
        public void UnchangedInfinityStateLeavesTheStoredEnvelopeAndAChangedStateRewritesIt()
        {
            var (session, _, _) = StartRevealGraph();
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            var state = session.Profile.Run.Infinity;

            InfinityMode.WriteEnvelope();
            string stored = settings.customData[InfinityMode.RuntimeKey];
            Assert.True(InfinityMode.TryReadEnvelope(out var initial));
            Assert.Equal(state.ClearedCombatTotal, initial.State.ClearedCombatTotal);

            // SaveContinueData repeats this write on every node travel and on each 20 s
            // mid-run lock: an unchanged state must leave the stored text alone. A rewrite
            // would store a fresh, content-equal string, so instance identity proves the skip.
            InfinityMode.WriteEnvelope();
            InfinityMode.WriteEnvelope();
            Assert.True(ReferenceEquals(stored, settings.customData[InfinityMode.RuntimeKey]));
            Assert.True(InfinityMode.TryReadEnvelope(out var unchanged));
            Assert.Equal(initial.State.ClearedCombatTotal, unchanged.State.ClearedCombatTotal);

            // A changed state must still reach every client through a fresh write.
            state.ClearedCombatTotal += 5;
            InfinityMode.WriteEnvelope();
            string rewritten = settings.customData[InfinityMode.RuntimeKey];
            Assert.False(ReferenceEquals(stored, rewritten));
            Assert.NotEqual(stored, rewritten);
            Assert.True(InfinityMode.TryReadEnvelope(out var updated));
            Assert.Equal(state.ClearedCombatTotal, updated.State.ClearedCombatTotal);
        }
    }
}
