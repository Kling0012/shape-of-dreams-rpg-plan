using System;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// Fail-soft contract of the optional overflow Dream Dust bonus (#252):
    /// any bonus-side failure (host unavailable, rejected ledger, stale obligation)
    /// may stop only the extra dust. Relic pickup, satchel overflow and shard credit
    /// must continue, and a stale cross-run obligation must release itself so the
    /// bonus re-arms in the next run instead of clogging forever.
    /// </summary>
    public sealed class OverflowBonusFailSoftTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly bool _server = NetworkServer.active;
        private readonly DewPlayer _owner = new DewPlayer { guid = "solo-failsoft", playerName = "solo-failsoft" };
        private readonly HostAuthority _host = new HostAuthority();

        public OverflowBonusFailSoftTests()
        {
            NetworkServer.active = true;
            NetworkClient.active = true;
            Time.unscaledTime = 100;
            Time.frameCount = 1;
            DewPlayer.local = _owner;
            DewPlayer.gamePlayers.Add(_owner);
            HostAuthority.NativeInstance = _host;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run-1" };
        }

        public void Dispose()
        {
            DewPlayer.gamePlayers.Remove(_owner);
            DewPlayer.local = null;
            HostAuthority.NativeInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkServer.active = _server;
            NetworkClient.active = false;
            Time.unscaledTime = 0;
            Time.frameCount = 0;
        }

        private static void Call(object target, string name)
        {
            try { target.GetType().GetMethod(name, Hidden).Invoke(target, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static object Get(object target, string name)
            => target.GetType().GetField(name, Hidden).GetValue(target);

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, Hidden).SetValue(target, value);

        private static ClientSession Session(Profile profile)
        {
            var session = new ClientSession { Profile = profile, ActiveRunId = "run-1" };
            Set(session, "_hostSession", session);
            return session;
        }

        private static void FillSatchel(Profile profile, ulong seed)
        {
            for (int i = 0; i < Workshop.SatchelCapacity(profile); i++)
                profile.Run.Satchel.Add(Loot.RollRelic(new Rng(seed + (ulong)i), Rarity.Common, 1));
        }

        private static void Pickup(Profile profile, ulong seed, Rarity rarity)
        {
            var pickup = typeof(Rules).GetMethod("AddToSatchel", PrivateStatic);
            pickup.Invoke(null, new object[] { profile, Loot.RollRelic(new Rng(seed), rarity, 5), null, false });
        }

        private void TickBoth(ClientSession session, int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                Call(session, "TickSatchelOverflow");
                Call(_host, "TickOverflowBonus");
                Time.unscaledTime += 2f;
                Time.frameCount++;
            }
        }

        private static GameEvent SingleWarning(ClientSession session, string fragment)
        {
            foreach (var e in session.Events)
                if (e.Kind == EventKind.Warning && e.Text != null && e.Text.Contains(fragment)) return e;
            return null;
        }

        [Fact]
        public void Host_permanently_unavailable_stops_only_the_dust()
        {
            var profile = Profile.CreateNew(176);
            Rules.BeginRun(profile, "run-1");
            FillSatchel(profile, 1);
            var session = Session(profile);
            session.ConfigureOverflowBonus(true);
            Set(_host, "_overflowBonusDisabled", true); // e.g. one failed native grant disables it for good

            int shards = profile.Material(Materials.Shard);
            Pickup(profile, 777, Rarity.Rare);
            TickBoth(session, 13); // past the 20 s no-advertisement window
            Assert.NotNull(SingleWarning(session, "鞄あふれの追加ドリームダスト"));
            Assert.False(profile.ReceiveOverflowDreamDust);
            Assert.Equal(0, _owner.dreamDust);
            Assert.Equal(shards + Content.SalvageShards(Rarity.Common), profile.Material(Materials.Shard));
            Assert.Equal(Workshop.SatchelCapacity(profile), profile.Run.Satchel.Count);

            // Relic intake and shard conversion continue after the warning.
            Pickup(profile, 888, Rarity.Epic);
            Call(session, "TickSatchelOverflow");
            Assert.Equal(shards + Content.SalvageShards(Rarity.Common) * 2, profile.Material(Materials.Shard));
            Assert.Equal(Workshop.SatchelCapacity(profile), profile.Run.Satchel.Count);
            Assert.Equal(0, profile.Run.OverflowDreamDustTotal);
        }

        [Fact]
        public void Working_flow_pays_dust_once_and_keeps_crediting_shards()
        {
            var profile = Profile.CreateNew(176);
            Rules.BeginRun(profile, "run-1");
            FillSatchel(profile, 1);
            var session = Session(profile);
            session.ConfigureOverflowBonus(true);
            int shards = profile.Material(Materials.Shard);

            TickBoth(session, 2); // capability handshake
            Assert.True(profile.ReceiveOverflowDreamDust);

            Pickup(profile, 777, Rarity.Rare);
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Common), profile.Run.OverflowDreamDustTotal);
            Assert.Equal("run-1", profile.OverflowBonusPendingRunId); // accrual reserves the slot immediately
            TickBoth(session, 3);
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Common), profile.Run.OverflowDreamDustTotal);
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Common), _owner.dreamDust);
            Assert.Null(profile.OverflowBonusPendingRunId);
            Assert.Equal(shards + Content.SalvageShards(Rarity.Common), profile.Material(Materials.Shard));
            TickBoth(session, 1); // no double payment on later heartbeats
            Assert.Equal(Economy.SatchelOverflowDust(Rarity.Common), _owner.dreamDust);
        }

        [Fact]
        public void Stale_pending_from_a_finished_run_releases_and_re_arms_the_bonus()
        {
            var profile = Profile.CreateNew(176);
            Rules.BeginRun(profile, "run-1");
            FillSatchel(profile, 1);
            var session = Session(profile);
            session.ConfigureOverflowBonus(true);
            TickBoth(session, 2); // handshake
            Pickup(profile, 777, Rarity.Rare);
            int accrued = Economy.SatchelOverflowDust(Rarity.Common);
            Assert.Equal(accrued, profile.Run.OverflowDreamDustTotal);

            // The obligation can never settle now (host-side native failure), and the run ends unpaid.
            Set(_host, "_overflowBonusDisabled", true);
            TickBoth(session, 3);
            Rules.EndRun(profile, true);
            Rules.BeginRun(profile, "run-2");
            NetworkedManagerBase<GameManager>.softInstance.runId = "run-2";
            FillSatchel(profile, 500);
            int shards = profile.Material(Materials.Shard);

            // First tick in the new run: the stale obligation releases itself with a notice.
            Call(session, "TickSatchelOverflow");
            Assert.Null(profile.OverflowBonusPendingRunId);
            Assert.Equal(0, profile.OverflowBonusPendingTotal);
            Assert.NotNull(SingleWarning(session, "諦めました"));

            // The host recovers; the bonus must re-arm and pay in the new run.
            Set(_host, "_overflowBonusDisabled", false);
            TickBoth(session, 5); // the 5 s capability heartbeat may gate the first resend
            Assert.True(profile.ReceiveOverflowDreamDust);
            Pickup(profile, 999, Rarity.Rare);
            TickBoth(session, 3);
            Assert.Equal(accrued, profile.Run.OverflowDreamDustTotal); // fresh run total, dust paid again
            Assert.Equal(accrued, _owner.dreamDust);
            Assert.Equal(shards + Content.SalvageShards(Rarity.Common), profile.Material(Materials.Shard));
            Assert.Equal(Workshop.SatchelCapacity(profile), profile.Run.Satchel.Count);
        }
    }
}
