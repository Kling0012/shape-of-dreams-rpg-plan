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
    // 接続直後、ホストの対応の返事を待つ間は禁忌装備を外して送るが、「旧ホスト」の通知は出さない。
    public sealed class ForbiddenGearNoticeTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string HeroKey = "Hero_Mist";

        public ForbiddenGearNoticeTests() => Reset();
        public void Dispose() => Reset();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Notice_waits_for_the_host_answer(bool hostSupportsForbidden)
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            Time.unscaledTime = 100;
            Time.frameCount = 1;
            var profile = Profile.CreateNew(415);
            var relic = Loot.RollRelic(new Rng(415), Rarity.Epic, 20, Slot.Weapon);
            relic.Retunes = Content.MaxRetunes + 1;
            profile.Stash.Add(relic);
            Rules.Equip(profile, HeroKey, relic.Uid);
            var actor = new Actor();
            var session = new ClientSession { Profile = profile, LocalHero = new Hero_Mist { netId = 7 }, HostConfirmed = true };
            Set(session, "_clientRpcOn", actor);
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Set(session, "_helloAnswered", true);
            Set(session, "_helloFirstSent", 99f);

            Call(session, "SendBuildIfNeeded");
            Assert.NotEmpty(actor.Sent.Select(s => s.Message).OfType<DreamforgeBuildMsg>());
            Assert.Null(session.ForbiddenGearHostNotice);

            Call(session, "OnBuildInputCapability", new DreamforgeBuildInputCapabilityMsg
                { version = 1, forbiddenInput = hostSupportsForbidden ? 1 : 0, fusionInput = 1 });
            Time.unscaledTime = 101;
            Time.frameCount = 2;
            Call(session, "SendBuildIfNeeded");
            if (hostSupportsForbidden) Assert.Null(session.ForbiddenGearHostNotice);
            else Assert.NotNull(session.ForbiddenGearHostNotice);
        }

        [Fact]
        public void Notice_appears_when_the_host_never_answers()
        {
            NetworkServer.active = false;
            NetworkClient.active = true;
            Time.unscaledTime = 100;
            Time.frameCount = 1;
            var profile = Profile.CreateNew(415);
            var relic = Loot.RollRelic(new Rng(416), Rarity.Epic, 20, Slot.Weapon);
            relic.Retunes = Content.MaxRetunes + 1;
            profile.Stash.Add(relic);
            Rules.Equip(profile, HeroKey, relic.Uid);
            var session = new ClientSession { Profile = profile, LocalHero = new Hero_Mist { netId = 7 }, HostConfirmed = true };
            Set(session, "_clientRpcOn", new Actor());
            Set(session, "_zone", new ZoneManager { currentZoneIndex = 0 });
            Set(session, "_helloAnswered", true);
            Set(session, "_helloFirstSent", 79f);
            Call(session, "SendBuildIfNeeded");
            Assert.NotNull(session.ForbiddenGearHostNotice);
        }

        private static void Reset()
        {
            NetworkServer.active = false;
            NetworkClient.active = false;
            Time.frameCount = 0;
            Time.unscaledTime = 0;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
        }

        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    }
}
