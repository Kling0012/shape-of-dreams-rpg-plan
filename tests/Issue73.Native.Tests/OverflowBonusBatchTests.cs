using System;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed class OverflowBonusBatchTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private readonly bool _server = NetworkServer.active;
        private readonly GameManager _game = NetworkedManagerBase<GameManager>.softInstance;
        private readonly int _frame = Time.frameCount;
        private readonly object _restore = typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).GetValue(null);
        private readonly object _session = typeof(ClientSession).GetField("_hostSession", Hidden).GetValue(null);
        private readonly DewPlayer _owner = new DewPlayer { guid = "bonus252", playerName = "bonus252" };
        private readonly HostAuthority _host = new HostAuthority();
        private readonly Actor _actor = new Actor();
        private readonly TradeAuthority _ledger;
        private readonly long _id;

        public OverflowBonusBatchTests()
        {
            NetworkServer.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "bonus252-run" };
            Time.frameCount = 252;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, null);
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
            typeof(HostAuthority).GetField("_registeredOn", Hidden).SetValue(_host, _actor);
            _ledger = (TradeAuthority)typeof(HostAuthority).GetField("_tradeAuthority", Hidden).GetValue(_host);
            _id = _ledger.LedgerIdOf(_owner.guid, "bonus252-run");
            DewPlayer.gamePlayers.Add(_owner);
        }

        public void Dispose()
        {
            DewPlayer.gamePlayers.Remove(_owner);
            NetworkServer.active = _server;
            NetworkedManagerBase<GameManager>.softInstance = _game;
            Time.frameCount = _frame;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, _restore);
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, _session);
        }

        private void Send(long total) => _host.OnOverflowBonus(new DreamforgeOverflowBonusMsg
        {
            enabled = true, runId = "bonus252-run", ledgerId = _id, total = total,
        }, _owner);

        [Fact]
        public void Burst_and_reordered_totals_grant_once_per_frame_and_checkpoint_discards_future_queue()
        {
            int calls = 0;
            _owner.BeforeCurrencyChange = () => calls++;
            for (int i = 1; i <= 500; i++) Send(i * 15);
            Send(15);
            Assert.Equal(0, _owner.dreamDust);
            _host.TickOverflowBonus();
            Assert.Equal(7500, _owner.dreamDust);
            Assert.Equal(1, calls);
            string snapshot = _host.CaptureContinueTrades();
            Send(7515);
            _host.TickOverflowBonus();
            Assert.Equal(7500, _owner.dreamDust);
            Assert.Equal(1, calls);
            typeof(HostAuthority).GetMethod("ResetOverflowBonusCheckpoint", Hidden).Invoke(_host, null);
            _ledger.RestoreCheckpoint(snapshot);
            Time.frameCount++;
            _host.TickOverflowBonus();
            Assert.Equal(7500, _owner.dreamDust);
            Send(7500);
            _host.TickOverflowBonus();
            Assert.Equal(1, calls);
            Send(7530);
            _host.TickOverflowBonus();
            Assert.Equal(7530, _owner.dreamDust);
            Assert.Equal(2, calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Native_exception_never_retries_possible_payment_and_manual_ledger_remains_usable(bool mutated)
        {
            int calls = 0;
            _owner.BeforeCurrencyChange = () =>
            {
                calls++;
                if (!mutated) throw new InvalidOperationException("before mutation");
            };
            _owner.AfterCurrencyChange = () => throw new InvalidOperationException("after mutation");
            Send(150);
            _host.TickOverflowBonus();
            Send(150);
            Time.frameCount++;
            _host.TickOverflowBonus();
            Assert.Equal(1, calls);
            Assert.Equal(mutated ? 150 : 0, _owner.dreamDust);
            var restored = new TradeAuthority();
            restored.RestoreCheckpoint(_host.CaptureContinueTrades());
            Assert.Equal(mutated ? 0 : -1, restored.PendingOverflowBonus(_owner.guid, "bonus252-run", 150, _id));
            Assert.True(restored.Evaluate(_owner.guid, "bonus252-run", new TradeRequest
                { Token = 1, Kind = TradeKind.MerchantGold, Heat = 0 }, 1000, 0).Ok);
        }
    }
}
