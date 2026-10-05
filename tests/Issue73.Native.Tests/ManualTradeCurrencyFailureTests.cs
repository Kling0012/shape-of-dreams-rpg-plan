using System;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;
using Xunit.Abstractions;

namespace Issue73.Native.Tests
{
    public sealed class ManualTradeCurrencyFailureTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private readonly bool _previousServerActive = NetworkServer.active;
        private readonly GameManager _previousGame = NetworkedManagerBase<GameManager>.softInstance;
        private readonly Action<string> _previousErrorSink = Log.ErrorSink;
        private readonly object _previousContinueTrades = typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).GetValue(null);

        public ManualTradeCurrencyFailureTests(ITestOutputHelper output)
        {
            NetworkServer.active = true;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run181" };
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, null);
            // Native logging does not throw; retain the harness's fail-fast default for other tests.
            Log.ErrorSink = output.WriteLine;
        }

        public void Dispose()
        {
            NetworkServer.active = _previousServerActive;
            NetworkedManagerBase<GameManager>.softInstance = _previousGame;
            typeof(HostAuthority).GetField("_pendingContinueTrades", Hidden).SetValue(null, _previousContinueTrades);
            Log.ErrorSink = _previousErrorSink;
        }

        [Theory]
        [InlineData(TradeKind.MerchantGold, true)]
        [InlineData(TradeKind.DustToShards, true)]
        [InlineData(TradeKind.SalvageForDust, true)]
        [InlineData(TradeKind.MerchantGold, false)]
        [InlineData(TradeKind.DustToShards, false)]
        [InlineData(TradeKind.SalvageForDust, false)]
        public void Currency_exception_settles_once_and_queries_and_resends_agree(TradeKind kind, bool balanceChanged)
        {
            var profile = Profile.CreateNew(181);
            Rules.BeginRun(profile, "run181");
            var trades = new TradeLedger();
            var relic = Loot.RollRelic(new Rng(181), Rarity.Rare, 3);
            PendingTrade trade;
            if (kind == TradeKind.MerchantGold)
                trade = trades.BeginMerchant(0, Economy.MerchantGoldBase(0), now: 0);
            else if (kind == TradeKind.DustToShards)
                trade = trades.BeginDustToShards(1, now: 0);
            else
            {
                profile.Run.Satchel.Add(relic);
                trade = trades.BeginSalvage(relic, now: 0);
            }
            int shardsBefore = profile.Material(Materials.Shard);
            var player = new DewPlayer { guid = "player181", gold = 1000, dreamDust = 1000 };
            int nativeCalls = 0;
            player.BeforeCurrencyChange = () =>
            {
                nativeCalls++;
                if (!balanceChanged) throw new InvalidOperationException("native currency failure before mutation");
            };
            player.AfterCurrencyChange = () => throw new InvalidOperationException("native RPC failure after mutation");
            var actor = new Actor();
            var host = new HostAuthority();
            typeof(HostAuthority).GetField("_registeredOn", Hidden).SetValue(host, actor);
            TradeWire.Encode(trade, out int gold, out int dust, out int earn);
            var request = new DreamforgeTradeMsg
            {
                protocol = Protocol.Version, token = trade.Token, spendGold = gold, spendDust = dust, earnDust = earn,
            };

            var first = Send(host, actor, player, request);
            Assert.Equal(balanceChanged, first.ok);
            Assert.Equal(balanceChanged ? TradeOutcome.Paid : TradeOutcome.Failed, ApplyResult(profile, trades, first));
            TradeWire.SplitReason(first.reason, out _, out long ledgerId);
            TradeWire.EncodeQuery(ledgerId, out gold, out dust, out earn);
            var query = new DreamforgeTradeMsg
            {
                protocol = Protocol.Version, token = trade.Token, spendGold = gold, spendDust = dust, earnDust = earn,
            };
            foreach (var message in new[] { query, request })
            {
                var replay = Send(host, actor, player, message);
                Assert.Equal(first.ok, replay.ok);
                Assert.Equal(first.reason, replay.reason);
                Assert.Equal(TradeOutcome.NotFound, ApplyResult(profile, trades, replay));
            }

            Assert.Equal(1000 - (balanceChanged ? trade.SpendGold : 0), player.gold);
            Assert.Equal(1000 + (balanceChanged ? trade.EarnDust - trade.SpendDust : 0), player.dreamDust);
            Assert.Equal(1, nativeCalls);
            Assert.Equal(shardsBefore + (balanceChanged && kind == TradeKind.DustToShards ? Economy.ShardsPerBatch : 0),
                profile.Material(Materials.Shard));
            if (kind == TradeKind.MerchantGold && balanceChanged)
                Assert.Single(profile.Run.Satchel);
            else if (kind == TradeKind.SalvageForDust && !balanceChanged)
                Assert.Same(relic, Assert.Single(profile.Run.Satchel));
            else
                Assert.Empty(profile.Run.Satchel);
        }

        private static DreamforgeTradeResultMsg Send(HostAuthority host, Actor actor, DewPlayer player, DreamforgeTradeMsg message)
        {
            actor.Sent.Clear();
            typeof(HostAuthority).GetMethod("OnTrade", Hidden).Invoke(host, new object[] { message, player });
            return Assert.IsType<DreamforgeTradeResultMsg>(Assert.Single(actor.Sent).Message);
        }

        // Same TradeLedger/Rules boundary as ClientSession.OnTradeResult and the existing salvage integration tests.
        private static TradeOutcome ApplyResult(Profile profile, TradeLedger trades, DreamforgeTradeResultMsg message)
        {
            TradeWire.SplitReason(message.reason, out string code, out _);
            var outcome = trades.OnResult(message.token, message.ok, code, out var trade);
            if (outcome == TradeOutcome.Failed && trade.Kind == TradeKind.SalvageForDust)
                Rules.RestorePendingSalvage(profile, trade.Uid);
            else if (outcome == TradeOutcome.Paid)
            {
                switch (trade.Kind)
                {
                    case TradeKind.MerchantGold: Rules.GrantPaidMerchant(profile, trades, trade); break;
                    case TradeKind.DustToShards: Rules.GrantPaidDustShards(profile, trade.SpendDust); break;
                    case TradeKind.SalvageForDust: Rules.SalvageUnsecured(profile, trade.Uid); break;
                }
            }
            return outcome;
        }
    }
}
