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
    /// 自動分解のまとめ送り（#1010 レビュー F1）。ドロップがフレームに分かれて届く戦闘中に
    /// 確認付き保存（SaveNow(true)）と送信が毎フレーム走らないよう、最初の候補から0.5秒の猶予の間は
    /// 何も送らず、期限が来た1回の Tick で候補全部を1つのまとめ送りとして出すことを確かめる。
    /// 猶予の基準は「空の列に最初の候補が入った時点」なので、続きのドロップは同じまとめ送りに加わり、
    /// 送り終えた後の新しいドロップは改めて猶予からやり直す。
    /// </summary>
    public sealed class AutoSalvageGraceTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const string RunId = "auto-salvage-grace";
        private const string Owner = "grace";
        private readonly bool _server = NetworkServer.active;
        private readonly bool _client = NetworkClient.active;
        private readonly GameManager _game = NetworkedManagerBase<GameManager>.softInstance;
        private readonly object _restoreTrades = typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).GetValue(null);
        private readonly object _restoreSession = typeof(ClientSession).GetField("_hostSession", Hidden | BindingFlags.Static).GetValue(null);
        private readonly HostAuthority _host = new HostAuthority();
        private readonly HostAuthority _restoreNative = HostAuthority.NativeInstance;
        private readonly Actor _hostActor = new Actor();
        private readonly DewPlayer _owner = new DewPlayer { guid = Owner, playerName = Owner };
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "auto-salvage-grace-" + Guid.NewGuid().ToString("N"));
        private ClientSession _session;
        private Profile _profile;
        private Actor _transport;

        public AutoSalvageGraceTests()
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
            HostAuthority.NativeInstance = _restoreNative;
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
            Set(session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "grace.json"), 252));
            // Mirror host mode: the local client's messages to the server loop straight back.
            var transport = (Actor)Get(session, "_clientRpcOn") ?? new Actor();
            _transport = transport;
            Set(session, "_clientRpcOn", transport);
            transport.BeforeSendToServer += message => CallHost((DreamforgeTradeMsg)message);
            Set(session, "_autoSalvageBelow", (Rarity?)Rarity.Rare); // 「レア未満」を選んだ状態
            return session;
        }

        private static object Get(ClientSession session, string name) => typeof(ClientSession).GetField(name, Hidden).GetValue(session);
        private static void Set(ClientSession session, string name, object value) => typeof(ClientSession).GetField(name, Hidden).SetValue(session, value);
        private static void Call(ClientSession session, string name)
        {
            try { typeof(ClientSession).GetMethod(name, Hidden).Invoke(session, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private void DeliverHostReplies(ClientSession session)
        {
            var replies = _hostActor.Sent.Select(sent => sent.Message).OfType<DreamforgeTradeResultMsg>().ToArray();
            _hostActor.Sent.Clear();
            foreach (var reply in replies) typeof(ClientSession).GetMethod("OnTradeResult", Hidden).Invoke(session, new object[] { reply });
        }

        private void CallHost(DreamforgeTradeMsg message)
        {
            try { typeof(HostAuthority).GetMethod("OnTrade", Hidden).Invoke(_host, new object[] { message, _owner }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        /// <summary>ホストへ送った分解の取引（台帳の照会は除く）の件数。</summary>
        private int SentSalvageTrades() => _transport.Sent.Count(s => s.Message is DreamforgeTradeMsg m && m.token != TradeWire.ProbeToken);

        [Fact]
        public void Grace_window_holds_sending_and_one_tick_after_the_deadline_drains_the_queue_together()
        {
            var session = Session();
            var first = Loot.RollRelic(new Rng(7401), Rarity.Common, 3);
            var laterA = Loot.RollRelic(new Rng(7402), Rarity.Uncommon, 3);
            var laterB = Loot.RollRelic(new Rng(7403), Rarity.Common, 3);
            _profile.Run.Satchel.Add(first);
            Call(session, "SendLedgerProbe"); // learn the host ledger like Wire does
            DeliverHostReplies(session);
            Assert.Equal(0, SentSalvageTrades());

            // 最初の候補が入った Tick と、その直後の Tick ではまだ何も送らない。
            session.QueueAutoSalvage(first);
            Assert.Equal(1, ((List<string>)Get(session, "_autoSalvageQueue")).Count);
            Call(session, "TickAutoSalvage");
            Assert.Equal(0, SentSalvageTrades());
            Assert.Single(_profile.Run.Satchel);

            // 猶予の間に届いた追加のドロップも、同じまとめ送りに加わる。
            Time.unscaledTime = 100.25f;
            _profile.Run.Satchel.Add(laterA);
            _profile.Run.Satchel.Add(laterB);
            session.QueueAutoSalvage(laterA);
            session.QueueAutoSalvage(laterB);
            Call(session, "TickAutoSalvage");
            Assert.Equal(0, SentSalvageTrades());
            Assert.Equal(3, _profile.Run.Satchel.Count);

            // 最初の候補から0.5秒経過した1回の Tick で、3件全部をまとめて送る。
            Time.unscaledTime = 100.6f;
            Call(session, "TickAutoSalvage");
            Assert.Equal(3, SentSalvageTrades());
            DeliverHostReplies(session);
            Assert.Empty(_profile.Run.Satchel);
            Assert.Empty((List<string>)Get(session, "_autoSalvageQueue"));

            // 送り終えた後の新しいドロップは、改めて自分の猶予からやり直す（即送りに戻さない）。
            var next = Loot.RollRelic(new Rng(7404), Rarity.Common, 3);
            _profile.Run.Satchel.Add(next);
            session.QueueAutoSalvage(next);
            Call(session, "TickAutoSalvage");
            Assert.Equal(3, SentSalvageTrades());
            Time.unscaledTime = 101.2f;
            Call(session, "TickAutoSalvage");
            Assert.Equal(4, SentSalvageTrades());
            DeliverHostReplies(session);
            Assert.Empty(_profile.Run.Satchel);
        }
    }
}
