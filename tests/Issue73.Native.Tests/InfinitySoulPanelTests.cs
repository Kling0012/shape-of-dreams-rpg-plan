using System;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private bool _secureHidden;
        private RunState _securePanelRun;
        private long _securePanelSegment;
        internal bool SecurePanelHidden { get { UpdateSecurePanel(); return _secureHidden; } }
    }
}

namespace Issue73.Native.Tests
{
    public sealed class InfinitySoulPanelTests : IDisposable
    {
        public void Dispose()
        {
            NetworkServer.active = false;
            NetworkedManagerBase<GameManager>.softInstance = null;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Infinity_choice_leaves_floor_access_and_local_visibility_does_not_commit_party_choice(bool host)
        {
            NetworkServer.active = host;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "soul-run" };
            var profile = Profile.CreateNew(227);
            Rules.BeginRun(profile, "soul-run");
            var run = profile.Run;
            run.Infinity = new InfinityRunState { Phase = InfinityPhase.AwaitingChoice, SoulObserved = true };
            run.AwaitingChoice = true;
            var session = new ClientSession { Profile = profile, ActiveRunId = run.RunId };
            var ui = new DreamforgeUi(session);
            var otherView = new DreamforgeUi(session);

            Assert.True(ui.SecurePanelHidden); // Also covers loading directly into the post-soul choice.
            ui.ToggleSecurePanel();
            Assert.False(ui.SecurePanelHidden);
            Assert.True(otherView.SecurePanelHidden);
            ui.ToggleSecurePanel();
            Assert.True(ui.SecurePanelHidden);
            ui.ToggleSecurePanel();
            Assert.False(ui.SecurePanelHidden);
            Assert.True(run.AwaitingChoice);
            Assert.Equal(InfinityPhase.AwaitingChoice, run.Infinity.Phase);
            Assert.Equal(0, run.Infinity.ChoiceRevision);

            run.Infinity.SegmentEpoch++;
            Assert.True(ui.SecurePanelHidden); // A new boss cycle must not inherit the open panel.
            ui.ToggleSecurePanel();
            Assert.False(ui.SecurePanelHidden);
            run.AwaitingChoice = false;
            Assert.False(ui.SecurePanelHidden);
            run.AwaitingChoice = true;
            Assert.True(ui.SecurePanelHidden);
        }

        [Fact]
        public void Normal_secure_point_still_opens_and_can_be_hidden_and_reopened()
        {
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "normal-run" };
            var profile = Profile.CreateNew(228);
            Rules.BeginRun(profile, "normal-run");
            profile.Run.AwaitingChoice = true;
            var ui = new DreamforgeUi(new ClientSession { Profile = profile, ActiveRunId = profile.Run.RunId });
            Assert.False(ui.SecurePanelHidden);
            ui.ToggleSecurePanel();
            Assert.True(ui.SecurePanelHidden);
            ui.ToggleSecurePanel();
            Assert.False(ui.SecurePanelHidden);
            profile.Run.AwaitingChoice = false;
            Assert.False(ui.SecurePanelHidden);
            profile.Run.AwaitingChoice = true;
            Assert.False(ui.SecurePanelHidden);
        }
    }
}
