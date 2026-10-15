using System;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    // Runs production settlement, run attachment, build sending and input validation.
    // Native ending playback, Mirror delivery and application to Unity stats are outside this harness.
    public sealed class PolarisNextRunTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string HeroKey = "Hero_Mist";

        public PolarisNextRunTests() => ResetStatics();
        public void Dispose() => ResetStatics();

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void StarlessPath_then_new_run_preserves_equipment_and_sends_pressure_inputs(bool guest, bool detachGame, bool polarisGear)
        {
            NetworkServer.active = !guest;
            NetworkClient.active = true;
            Time.unscaledTime = 100;
            Time.frameCount = 1;
            var profile = Profile.CreateNew(449);
            profile.DreamLevel = 20;
            profile.LastDreamDepth = 3;
            var rng = new Rng(449);
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                var relic = polarisGear
                    ? Loot.RollUnique(rng, Content.Uniques.Single(u => u.Id == "unique.boss_polaris." + slot.ToString().ToLowerInvariant()), 60)
                    : Loot.RollRelic(rng, Rarity.Epic, 60, slot);
                profile.Stash.Add(relic);
                Rules.Equip(profile, HeroKey, relic.Uid);
            }
            var equipped = profile.Hero(HeroKey).Equipped.ToArray();
            var actor = new Actor();
            var hero = new Hero_Mist { netId = 7 };
            var session = new ClientSession { Profile = profile, LocalHero = hero, HostConfirmed = true };
            Set(session, "_clientRpcOn", actor);
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Set(session, "_hostBuildInputCapability", true);
            Set(session, "_hostForbiddenInput", true);
            Set(session, "_hostFusionInput", true);
            Attach(session, "polaris", guest);
            Call(session, "SendBuildIfNeeded");
            var before = AcceptedBuild(actor);
            Assert.NotEmpty(before.Stats);
            if (polarisGear) Assert.Equal(6, before.Sets["set.boss_polaris"]);
            var pressureBefore = DreamPressure.Average(new[] { before }).WithRunModifiers(profile.Run.DreamDepth);

            Call(session, "OnConcluded", new DewGameResult { result = DewGameResult.ResultType.StarlessPath });
            Call(session, "TickRunChoices");
            Assert.Null(profile.Run);
            Assert.Equal("polaris", profile.CompletedRunId);
            Assert.Equal(1, profile.Stats.Victories);
            Assert.Equal(equipped, profile.Hero(HeroKey).Equipped);
            // Re-observing the concluded native run must not create another expedition.
            Call(session, "TrackRun");
            Assert.Null(profile.Run);

            if (detachGame)
            {
                NetworkedManagerBase<GameManager>.softInstance = null;
                Call(session, "ObserveContinueGame", (object)null);
                Call(session, "TrackRun");
                session.LocalHero = new Hero_Mist { netId = 8 };
                actor = new Actor();
                Set(session, "_clientRpcOn", actor);
            }
            actor.Sent.Clear();
            Time.unscaledTime += 1;
            Time.frameCount++;
            Attach(session, "next", guest);
            Assert.Equal("next", session.ActiveRunId);
            Assert.Equal("next", profile.Run.RunId);
            Assert.Equal(3, profile.Run.DreamDepth);
            Assert.Equal(equipped, profile.Hero(HeroKey).Equipped);
            Call(session, "SendBuildIfNeeded");
            var after = AcceptedBuild(actor);
            Assert.Equal(before.Stats, after.Stats);
            Assert.Equal(before.Powers, after.Powers);
            Assert.Equal(before.Sets, after.Sets);
            Assert.True(after.DreamLevel >= before.DreamLevel);
            var pressureAfter = DreamPressure.Average(new[] { after }).WithRunModifiers(profile.Run.DreamDepth);
            Assert.True(pressureAfter.HealthMultiplier >= pressureBefore.HealthMultiplier);
            Assert.True(pressureAfter.DamageMultiplier >= pressureBefore.DamageMultiplier);
            Assert.Equal(1, profile.Stats.Victories);
        }

        private static void Attach(ClientSession session, string runId, bool guest)
        {
            var game = new GameManager { runId = runId };
            NetworkedManagerBase<GameManager>.softInstance = game;
            Call(session, "ObserveContinueGame", game);
            if (guest)
                Call(session, "ReceiveRunChoices", new RunChoiceSnapshot
                {
                    RunId = runId, ZoneIndex = 0, Depth = 3, Revision = runId == "polaris" ? 1 : 2,
                }.Encode(), null);
            Call(session, "TrackRun");
            Call(session, "TickRunChoices");
        }

        private static Build AcceptedBuild(Actor actor)
        {
            var messages = actor.Sent.Select(s => s.Message).OfType<DreamforgeBuildMsg>().ToArray();
            Assert.NotEmpty(messages);
            var receiver = new BuildTransferReceiver();
            string encoded = null;
            foreach (var message in messages)
            {
                Assert.True(receiver.TryAccept(message.ToPart(), out var complete));
                if (complete != null) encoded = complete;
            }
            Assert.NotNull(encoded);
            Assert.True(HostBuildValidation.TryAccept(encoded, HeroKey, out var accepted, out var reason), reason);
            return accepted;
        }

        private static void ResetStatics()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            Time.frameCount = 0;
            Time.unscaledTime = 0;
            InfinityMode.NativeSaveAgreement = false;
            InfinityMode.Available = false;
            typeof(InfinityMode).GetField("_restoring", Hidden).SetValue(null, false);
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
            SingletonDewNetworkBehaviour<Room>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
        }

        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    }
}
