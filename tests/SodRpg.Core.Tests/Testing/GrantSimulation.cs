using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core;
using Xunit;

namespace SodRpg.Core.Tests.Testing
{
    public sealed class SimClient
    {
        private readonly Catalog _catalog;

        public SimClient(string key, Catalog catalog)
        {
            Key = key;
            _catalog = catalog;
            Disk = new InMemoryFileSystem();
            Fs = new FaultyFileSystem(Disk);
            Restart();
        }

        public string Key { get; }
        public InMemoryFileSystem Disk { get; }
        public FaultyFileSystem Fs { get; }
        public LedgerStore Store { get; private set; }
        public ClientGrantReceiver Receiver { get; private set; }
        public bool Online { get; set; } = true;
        public int Crashes { get; private set; }

        public string LedgerPath => "profiles/" + Key + "/ledger.json";

        /// <summary>プロセスが死んで再起動した状態を作る（ディスクの内容だけが残る）。</summary>
        public void Restart()
        {
            Fs.Disarm();
            Store = new LedgerStore(Fs, LedgerPath, Key, _catalog);
            Store.Load();
            Receiver = new ClientGrantReceiver(Store, _catalog);
        }

        public void Crash()
        {
            Crashes++;
            Restart();
        }

        /// <summary>障害注入なしで、ディスクに今あるものを読み直した結果。</summary>
        public LedgerState PersistedView()
        {
            var s = new LedgerStore(Disk, LedgerPath, Key, _catalog);
            s.Load();
            return s.State;
        }
    }

    /// <summary>
    /// ホスト1つとクライアント数人を、欠落・重複・遅延・並べ替え・切断・クラッシュのある通信で結ぶ模擬。
    /// 乱数は固定シードで再現できる。
    /// </summary>
    public sealed class GrantSimulation
    {
        private readonly Random _rng;
        private readonly Catalog _catalog;
        private readonly List<(SimClient to, GrantMessage msg)> _toClient = new List<(SimClient, GrantMessage)>();
        private readonly List<AckMessage> _toHost = new List<AckMessage>();

        public GrantSimulation(int seed, Catalog catalog, params string[] clientKeys)
        {
            _rng = new Random(seed);
            _catalog = catalog;
            Clients = clientKeys.Select(k => new SimClient(k, catalog)).ToList();
        }

        public HostGrantBook Host { get; set; } = new HostGrantBook();
        public List<SimClient> Clients { get; }

        public double DropRate { get; set; } = 0.25;
        public double DuplicateRate { get; set; } = 0.25;
        public double DeliverRate { get; set; } = 0.6;
        public double CrashRate { get; set; } = 0.2;
        public double OfflineToggleRate { get; set; } = 0.1;
        /// <summary>ホストが停止している間は再送も受信もしない。</summary>
        public bool HostAlive { get; set; } = true;

        public int InFlight => _toClient.Count + _toHost.Count;

        public Random Rng => _rng;

        public void Tick(bool chaos)
        {
            if (chaos)
            {
                foreach (SimClient c in Clients)
                    if (_rng.NextDouble() < OfflineToggleRate) c.Online = !c.Online;
            }

            if (HostAlive)
            {
                foreach (SimClient c in Clients)
                {
                    if (!c.Online) continue;
                    foreach (GrantMessage m in Host.PendingFor(c.Key))
                    {
                        if (chaos && _rng.NextDouble() < DropRate) continue;
                        _toClient.Add((c, m));
                        if (chaos && _rng.NextDouble() < DuplicateRate) _toClient.Add((c, m));
                    }
                }
            }

            var batch = _toClient.OrderBy(_ => _rng.Next()).ToList();
            _toClient.Clear();
            foreach (var (client, msg) in batch)
            {
                if (chaos && _rng.NextDouble() > DeliverRate) { _toClient.Add((client, msg)); continue; } // 遅延
                if (!client.Online) continue; // 受け手が切断中なら届かない
                Deliver(client, msg, chaos);
            }

            var acks = _toHost.OrderBy(_ => _rng.Next()).ToList();
            _toHost.Clear();
            foreach (AckMessage ack in acks)
            {
                if (!HostAlive) continue;
                if (chaos && _rng.NextDouble() > DeliverRate) { _toHost.Add(ack); continue; }
                Host.OnAck(ack);
            }
        }

        private static readonly FaultMode[] FaultModes =
        {
            FaultMode.CrashBefore, FaultMode.CrashAfter, FaultMode.CrashTorn, FaultMode.IoError,
        };

        private void Deliver(SimClient client, GrantMessage msg, bool chaos)
        {
            if (chaos && _rng.NextDouble() < CrashRate)
                client.Fs.Arm(_rng.Next(0, 3), FaultModes[_rng.Next(FaultModes.Length)]);

            ReceiveResult result;
            try
            {
                result = client.Receiver.Receive(msg);
            }
            catch (CrashException)
            {
                client.Crash(); // ackは返らない
                return;
            }
            finally
            {
                client.Fs.Disarm();
            }

            if (result.Ack == null) return;
            if (chaos && _rng.NextDouble() < DropRate) return; // ack喪失
            _toHost.Add(result.Ack);
        }

        /// <summary>全クライアントを復帰させ、通信を正常にして、配布待ちが無くなるまで進める。</summary>
        public int Heal(int maxTicks = 100)
        {
            foreach (SimClient c in Clients) c.Online = true;
            for (int t = 1; t <= maxTicks; t++)
            {
                Tick(chaos: false);
                if (Host.Count(HostGrantState.Committed) == 0 && InFlight == 0) return t;
            }
            return maxTicks;
        }

        /// <summary>
        /// どの時点でも成り立つべき不変条件。
        ///  1. 各クライアントのディスク上の台帳に隔離や重複がなく、素材数が付与履歴の合計と一致する。
        ///  2. ホストがClosedにした報酬は、受取人のディスク上の台帳に必ず存在する。
        /// </summary>
        public void AssertInvariants()
        {
            foreach (SimClient c in Clients)
            {
                LedgerState view = c.PersistedView();
                Assert.Empty(view.Quarantine);
                Assert.Equal(view.AppliedGrants.Select(g => g.GrantId).Distinct().Count(), view.AppliedGrants.Count);

                int expected = view.AppliedGrants.Sum(g => RewardQuantity(g.DefId));
                Assert.Equal(expected, view.MaterialCount(PrototypeCatalog.ShardMaterialId));
            }

            foreach (string id in Host.AllGrantIds)
            {
                if (Host.StateOf(id) != HostGrantState.Closed) continue;
                GrantMessage m = Host.MessageOf(id);
                SimClient owner = Clients.Single(c => c.Key == m.RecipientKey);
                Assert.True(owner.PersistedView().HasApplied(id), "Closedの報酬が受取人の台帳に無い: " + id);
            }
        }

        private int RewardQuantity(string defId)
        {
            Assert.True(_catalog.TryGetReward(defId, out RewardDef def));
            return def.Quantity;
        }
    }
}
