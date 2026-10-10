using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    // 所見04: 手動取引の送信が配信開始後に例外でも、予約は結果不明として保持され、
    // 遅れて届いた成功応答が一度だけ精算される（抽出された本物の SendPreparedTrade/OnTradeResult）。
    public sealed class TradeSendExceptionTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly bool _previousClientActive = NetworkClient.active;
        private readonly Action<object> _previousSendHook = NetworkClient.SendHook;
        private readonly Action<string> _previousErrorSink = Log.ErrorSink;
        private readonly float _previousTime = Time.unscaledTime;

        public TradeSendExceptionTests()
        {
            NetworkClient.active = true;
            NetworkClient.Sent.Clear();
            Log.ErrorSink = _ => { };
            Time.unscaledTime = 50f;
        }

        public void Dispose()
        {
            NetworkClient.active = _previousClientActive;
            NetworkClient.SendHook = _previousSendHook;
            NetworkClient.Sent.Clear();
            Log.ErrorSink = _previousErrorSink;
            Time.unscaledTime = _previousTime;
        }

        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        [Fact]
        public void Delivery_then_exception_keeps_the_reservation_and_the_late_success_settles_once()
        {
            string directory = Path.Combine(Path.GetTempPath(), "issue73-trade-send-" + Guid.NewGuid().ToString("N"));
            try
            {
                var profile = Profile.CreateNew(73);
                var session = new ClientSession { Profile = profile };
                Set(session, "_store", new ProfileStore(new RealFileSystem(), Path.Combine(directory, "profile.json"), 73));
                var actor = new Actor();
                var delivered = new List<object>();
                actor.BeforeSendToServer = message =>
                {
                    delivered.Add(message); // native delivery already happened...
                    throw new InvalidOperationException("native RPC failed after delivery"); // ...then the call threw
                };
                Set(session, "_clientRpcOn", actor);
                var trades = (TradeLedger)Get(session, "_trades");
                var trade = trades.BeginDustToShards(1, 0);

                string error = (string)Call(session, "SendPreparedTrade", trade);
                Assert.NotNull(error);
                Assert.Contains("不明", error); // uncertain outcome, not a plain failure
                var message = Assert.IsType<DreamforgeTradeMsg>(Assert.Single(delivered));
                Assert.Equal(trade.Token, message.token);
                // The token, ledger and reservation all survive for the host receipt query.
                Assert.Contains(trades.Snapshot(), t => t.Token == trade.Token && t.Unresolved);
                Assert.Contains(profile.PendingTrades, t => t.Token == trade.Token);

                // A late success response is applied instead of being reported as unknown.
                int shardsBefore = profile.Material(Materials.Shard);
                Call(session, "OnTradeResult", new DreamforgeTradeResultMsg
                { token = trade.Token, ok = true, reason = "paid@" + 11L.ToString("x16") });
                Assert.Equal(shardsBefore + Economy.ShardsPerBatch, profile.Material(Materials.Shard));
                Assert.DoesNotContain(trades.Snapshot(), t => t.Token == trade.Token);
                Assert.DoesNotContain(profile.PendingTrades, t => t.Token == trade.Token);
                // The settled receipt does not pay twice.
                Call(session, "OnTradeResult", new DreamforgeTradeResultMsg
                { token = trade.Token, ok = true, reason = "paid@" + 11L.ToString("x16") });
                Assert.Equal(shardsBefore + Economy.ShardsPerBatch, profile.Material(Materials.Shard));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
