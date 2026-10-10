using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Mod;
using SodRpg.Core.Game;
using SodRpg.Core;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// 分解のドリームダストは Economy の共通スケールを通る。単体の分解と一括分解の両方で、
    /// 取引前に表示する予定ダストとホストが実際に支払う額が一致することを既定値と別の値で確認する。
    /// スケールはプロセス全体の静的なので、変更は必ず try/finally で既定の 1 へ戻す。
    /// </summary>
    public sealed class SalvageScaleTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const string RunId = "scale-run";
        private const string Owner = "scale";
        private const int NonDefaultScale = 4;
        private readonly bool _server = NetworkServer.active;
        private readonly bool _client = NetworkClient.active;
        private readonly GameManager _game = NetworkedManagerBase<GameManager>.softInstance;
        private readonly object _restoreTrades = typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).GetValue(null);
        private readonly object _restoreSession = typeof(ClientSession).GetField("_hostSession", Hidden | BindingFlags.Static).GetValue(null);
        private readonly HostAuthority _host = new HostAuthority();
        private readonly Actor _hostActor = new Actor();
        private readonly DewPlayer _owner = new DewPlayer { guid = Owner, playerName = Owner };
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "salvage-scale-" + Guid.NewGuid().ToString("N"));
        private ClientSession _session;
        private Profile _profile;

        public SalvageScaleTests()
        {
            NetworkServer.active = true;
            NetworkClient.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = RunId };
            Time.unscaledTime = 100;
            Time.frameCount = 1;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, null);
            typeof(HostAuthority).GetField("_registeredOn", Hidden).SetValue(_host, _hostActor);
            HostAuthority.NativeInstance = _host;
            DewPlayer.gamePlayers.Add(_owner);
            DewPlayer.local = _owner;
        }

        public void Dispose()
        {
            _session?.FlushSaves();
            DewPlayer.local = null;
            DewPlayer.gamePlayers.Remove(_owner);
            HostAuthority.NativeInstance = null;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, _restoreTrades);
            typeof(ClientSession).GetField("_hostSession", Hidden | BindingFlags.Static).SetValue(null, _restoreSession);
            NetworkServer.active = _server;
            NetworkClient.active = _client;
            NetworkedManagerBase<GameManager>.softInstance = _game;
            Time.unscaledTime = 0;
            Time.frameCount = 0;
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            Log.InfoSink = null;
        }

        private ClientSession Session()
        {
            _profile = Profile.CreateNew(252);
            Rules.BeginRun(_profile, RunId);
            var session = new ClientSession(null) { Profile = _profile, ActiveRunId = RunId };
            _session = session; // Dispose must drain this session's writer before deleting its files.
            typeof(ClientSession).GetField("_hostSession", Hidden | BindingFlags.Static).SetValue(null, session);
            Set(session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "scale.json"), 252));
            // Mirror host mode: the local client's messages to the server loop straight back.
            var transport = (Actor)Get(session, "_clientRpcOn") ?? new Actor();
            Set(session, "_clientRpcOn", transport);
            transport.BeforeSendToServer += message => CallHost((DreamforgeTradeMsg)message);
            return session;
        }

        private static object Get(ClientSession session, string name) => typeof(ClientSession).GetField(name, Hidden).GetValue(session);
        private static void Set(ClientSession session, string name, object value) => typeof(ClientSession).GetField(name, Hidden).SetValue(session, value);
        private static void Call(ClientSession session, string name, params object[] args)
        {
            try { typeof(ClientSession).GetMethod(name, Hidden).Invoke(session, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private void DeliverHostReplies(ClientSession session)
        {
            var replies = _hostActor.Sent.Select(sent => sent.Message).OfType<DreamforgeTradeResultMsg>().ToArray();
            _hostActor.Sent.Clear();
            foreach (var reply in replies) Call(session, "OnTradeResult", reply);
        }

        private void CallHost(DreamforgeTradeMsg message)
        {
            try { typeof(HostAuthority).GetMethod("OnTrade", Hidden).Invoke(_host, new object[] { message, _owner }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        /// <summary>一括分解の確認と同じ合計（対象になる各行の予定ダストの和）。</summary>
        private static long BulkPreview(ClientSession session)
        {
            long total = 0;
            var satchel = session.Profile.Run.Satchel;
            for (int i = 0; i < satchel.Count; i++)
                if (session.CanBulkSalvageUnsecured(satchel[i]))
                    total += session.SalvageDust(satchel[i]);
            return total;
        }

        [Fact]
        public void Single_salvage_pays_the_previewed_dust_at_the_default_scale_and_another_scale()
        {
            var session = Session();
            var first = Loot.RollRelic(new Rng(7352), Rarity.Epic, 4);
            var second = Loot.RollRelic(new Rng(7353), Rarity.Rare, 3);
            _profile.Run.Satchel.Add(first);
            _profile.Run.Satchel.Add(second);
            Call(session, "SendLedgerProbe"); // learn the host ledger like Wire does
            DeliverHostReplies(session);

            try
            {
                Assert.Equal(1, Economy.SalvageScale);
                int preview = session.SalvageDust(first);
                Assert.Equal(Economy.SalvageDust(first.Rarity, first.Enhance, 1), preview);
                Assert.Null(session.SalvageUnsecured(first.Uid));
                DeliverHostReplies(session);
                Assert.Equal(preview, _owner.dreamDust);

                Economy.SalvageScale = NonDefaultScale;
                int scaledPreview = session.SalvageDust(second);
                Assert.Equal(Economy.SalvageDust(second.Rarity, second.Enhance, 1) * NonDefaultScale, scaledPreview);
                Assert.Null(session.SalvageUnsecured(second.Uid));
                DeliverHostReplies(session);
                Assert.Equal(preview + scaledPreview, _owner.dreamDust);
            }
            finally
            {
                Economy.SalvageScale = 1;
            }
            Assert.Equal(1, Economy.SalvageScale); // 他のテストへ漏らさない
        }

        [Fact]
        public void Bulk_salvage_pays_the_previewed_total_at_the_default_scale_and_another_scale()
        {
            var session = Session();
            var firstWave = new List<Relic>
            {
                Loot.RollRelic(new Rng(7361), Rarity.Rare, 5),
                Loot.RollRelic(new Rng(7362), Rarity.Uncommon, 5),
                Loot.RollRelic(new Rng(7363), Rarity.Common, 5),
            };
            firstWave.ForEach(r => _profile.Run.Satchel.Add(r));
            Call(session, "SendLedgerProbe");
            DeliverHostReplies(session);

            try
            {
                Assert.Equal(1, Economy.SalvageScale);
                Assert.All(firstWave, r => Assert.True(session.CanBulkSalvageUnsecured(r)));
                long preview = BulkPreview(session);
                Assert.Null(session.SalvageUnsecuredBulk(out int sent, out int remaining));
                Assert.Equal(firstWave.Count, sent);
                Assert.Equal(0, remaining);
                DeliverHostReplies(session);
                Assert.Equal(preview, (long)_owner.dreamDust);
                Assert.Empty(_profile.Run.Satchel);

                var secondWave = new List<Relic>
                {
                    Loot.RollRelic(new Rng(7364), Rarity.Rare, 5),
                    Loot.RollRelic(new Rng(7365), Rarity.Uncommon, 5),
                };
                secondWave.ForEach(r => _profile.Run.Satchel.Add(r));
                Economy.SalvageScale = NonDefaultScale;
                long scaledPreview = BulkPreview(session);
                Assert.Equal(secondWave.Sum(r => (long)Economy.SalvageDust(r.Rarity, r.Enhance, 1)) * NonDefaultScale, scaledPreview);
                Assert.Null(session.SalvageUnsecuredBulk(out sent, out remaining));
                Assert.Equal(secondWave.Count, sent);
                Assert.Equal(0, remaining);
                DeliverHostReplies(session);
                Assert.Equal(preview + scaledPreview, (long)_owner.dreamDust);
                Assert.Empty(_profile.Run.Satchel);
            }
            finally
            {
                Economy.SalvageScale = 1;
            }
            Assert.Equal(1, Economy.SalvageScale); // 他のテストへ漏らさない
        }
    }
}
