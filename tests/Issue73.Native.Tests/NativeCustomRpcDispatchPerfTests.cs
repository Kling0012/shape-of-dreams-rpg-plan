using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#857）CustomRpc 受信の型探しを辞書引きに変える NativeCustomRpcDispatchPerf の
    /// Prefix と TryWire の検証。境界（NativeBoundary.cs）の Actor は本体 Dew.Core
    /// Actor.cs（r1.4.1.2 逆コンパイル）の登録・解除・HandleRpc_Imp の写しを持ち、
    /// DewPersistence.FromJson は実際に JSON を復元する。TryWire で読み取りを組み
    /// 直してから Prefix を直接呼び、本体（模倣）の HandleRpc_Imp と配達結果を比べる:
    /// (a) 1つの型を登録した Actor では2回目以降も覚えた対応で同じハンドラを同じ順に呼ぶ、
    /// (b) 同じ単純名の型が2つあると Prefix は true を返して本体に任せる、
    /// (c) 型の登録解除後はキャッシュを捨てて true を返す、
    /// (d) 復元が例外になる電文は false で捨ててハンドラを呼ばない、
    /// (e) ハンドラ1件の例外で後続のハンドラが止まらない、
    /// (f) server と client の対応は別々に覚える（一方の覚えが他方の配達を歪めない）、
    /// (g) ハンドラが列挙中に自分の登録を解除すると、本体と同じ例外で残りを止める
    ///     （添字走査で次のハンドラを飛ばして続けることはない）、
    /// (h) wrappedAction の形が変わっていると、まだ誰も呼んでいない時点でパッチを
    ///     外して本体へ戻す、
    /// (i) 覚えた後に登録された同名の別型は再走査しない（既知の登録は名前一意。
    ///     docs/specs/perf-loop/20261019-1543.md の主張の範囲を固定する）。
    /// </summary>
    public sealed class NativeCustomRpcDispatchPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Patch = typeof(NativeCustomRpcDispatchPerf);

        // Prefix と読み取り欄は静的な欄を読み書きするため、毎テストで整えて Dispose でも
        // 戻す（試験8が wrappedAction の読み替えを行うため、Dispose で本物へ組み直す）。
        public NativeCustomRpcDispatchPerfTests()
        {
            Reset();
            Assert.True(NativeCustomRpcDispatchPerf.TryWire());
        }

        public void Dispose()
        {
            Reset();
            NativeCustomRpcDispatchPerf.TryWire();
        }

        private static void Reset()
        {
            Patch.GetField("_disabled", Hidden).SetValue(null, false);
            Patch.GetField("_warned", Hidden).SetValue(null, false);
            NativePerfOptions.CustomRpcDispatch = true;
        }

        private static bool Prefix(Actor actor, string type, string json, DewPlayer caller)
            => (bool)Patch.GetMethod("Prefix", Hidden).Invoke(null, new object[] { actor, type, json, caller });

        // 基準側: 境界の Actor が持つ本体 HandleRpc_Imp の写しを直接呼ぶ。
        private static void Vanilla(Actor actor, string type, string json, DewPlayer caller)
            => typeof(Actor).GetMethod("HandleRpc_Imp", Hidden).Invoke(actor, new object[] { type, json, caller });

        private static string Json(int value) => Newtonsoft.Json.JsonConvert.SerializeObject(new SampleMessage { Value = value });

        private static string TwinJson(int tag) => Newtonsoft.Json.JsonConvert.SerializeObject(new RpcDispatchNameA.TwinName { Tag = tag });

        private static object SideCache(Actor actor, bool server)
        {
            var caches = Patch.GetField("Caches", Hidden).GetValue(null);
            var tryGet = caches.GetType().GetMethods()
                .Single(m => m.Name == "TryGetValue" && m.GetParameters().Length == 2);
            var args = new object[] { actor, null };
            return (bool)tryGet.Invoke(caches, args)
                ? args[1].GetType().GetField(server ? "Server" : "Client", Hidden).GetValue(args[1])
                : null;
        }

        private static Dictionary<string, Type> ByName(Actor actor, bool server)
        {
            var side = SideCache(actor, server);
            return (Dictionary<string, Type>)side?.GetType().GetField("ByName", Hidden).GetValue(side);
        }

        private static HashSet<string> Ambiguous(Actor actor, bool server)
        {
            var side = SideCache(actor, server);
            return (HashSet<string>)side?.GetType().GetField("Ambiguous", Hidden).GetValue(side);
        }

        // (a) 1つの型に2つのハンドラを登録した Actor では、2回目以降の受信も覚えた対応で
        // 同じハンドラを同じ順に呼ぶ。配達の結果は本体（模倣）と同じ。
        [Fact]
        public void CachedReceivesDispatchTheSameHandlersInOrder()
        {
            var actor = new Actor();
            var calls = new List<string>();
            actor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => calls.Add("first:" + m.Value));
            actor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => calls.Add("second:" + m.Value));
            Assert.False(Prefix(actor, "SampleMessage", Json(7), null));
            Assert.Equal(new[] { "first:7", "second:7" }, calls);
            calls.Clear();
            Assert.False(Prefix(actor, "SampleMessage", Json(7), null));
            Assert.Equal(new[] { "first:7", "second:7" }, calls);
            Assert.Equal(typeof(SampleMessage), ByName(actor, server: false)["SampleMessage"]);
            Assert.Empty(Ambiguous(actor, server: false));

            var vanillaActor = new Actor();
            var vanillaCalls = new List<string>();
            vanillaActor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => vanillaCalls.Add("first:" + m.Value));
            vanillaActor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => vanillaCalls.Add("second:" + m.Value));
            Vanilla(vanillaActor, "SampleMessage", Json(7), null);
            Assert.Equal(new[] { "first:7", "second:7" }, vanillaCalls);
        }

        // (b) 同じ単純名の型が2つ登録されていると、Prefix は true を返して本体に任せる。
        // 任された本体（模倣）は両方の型へ配達する。
        [Fact]
        public void ASharedSimpleNameDefersToVanillaEveryTime()
        {
            var actor = new Actor();
            var calls = new List<string>();
            actor.CustomRpc_RegisterServerMessageHandler("TwinName", (RpcDispatchNameA.TwinName m, DewPlayer p) => calls.Add("A:" + m.Tag + ":" + (p == null)));
            actor.CustomRpc_RegisterServerMessageHandler("TwinName", (RpcDispatchNameB.TwinName m, DewPlayer p) => calls.Add("B:" + m.Tag));
            var caller = new DewPlayer();
            Assert.True(Prefix(actor, "TwinName", TwinJson(5), caller));
            Assert.Empty(calls);
            Assert.Contains("TwinName", Ambiguous(actor, server: true));
            Assert.True(Prefix(actor, "TwinName", TwinJson(5), caller));
            Assert.Empty(calls);
            Vanilla(actor, "TwinName", TwinJson(5), caller);
            Assert.Equal(new[] { "A:5:False", "B:5" }, calls);
        }

        // (c) 型の登録を解除すると、覚えた対応を捨てて true を返す。本体（模倣）も
        // 辞書が空なので誰も呼ばない（配達結果は同じ）。
        [Fact]
        public void UnregisteringTheTypeDropsTheCacheAndDefersToVanilla()
        {
            var actor = new Actor();
            var calls = new List<string>();
            actor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => calls.Add("v" + m.Value));
            Assert.False(Prefix(actor, "SampleMessage", Json(3), null));
            Assert.Equal(new[] { "v3" }, calls);
            actor.CustomRpc_UnregisterClientMessageHandler<SampleMessage>();
            Assert.True(Prefix(actor, "SampleMessage", Json(3), null));
            Assert.False(ByName(actor, server: false).ContainsKey("SampleMessage"));
            Assert.Single(calls);
            Vanilla(actor, "SampleMessage", Json(3), null);
            Assert.Single(calls);
        }

        // (d) 復元が例外になる電文は false を返して捨て、ハンドラを呼ばない。
        // 本体（模倣）も復元に失敗した電文を捨てて誰も呼ばない。
        [Fact]
        public void ADroppedBodyMessageCallsNoHandlerAndReturnsFalse()
        {
            var actor = new Actor();
            var calls = new List<string>();
            actor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => calls.Add("v" + m.Value));
            Assert.False(Prefix(actor, "SampleMessage", "{ broken", null));
            Assert.Empty(calls);
            Vanilla(actor, "SampleMessage", "{ broken", null);
            Assert.Empty(calls);
        }

        // (e) ハンドラ1件の例外は1件ずつ記録され、後続のハンドラを止めない。
        // 配達の結果は本体（模倣）と同じ。
        [Fact]
        public void AHandlerExceptionDoesNotStopTheRest()
        {
            var actor = new Actor();
            var calls = new List<string>();
            actor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => { calls.Add("boom"); throw new InvalidOperationException("handler"); });
            actor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => calls.Add("after:" + m.Value));
            var logged = Debug.Logged.Count;
            Assert.False(Prefix(actor, "SampleMessage", Json(7), null));
            Assert.Equal(new[] { "boom", "after:7" }, calls);
            Assert.Equal(1, Debug.Logged.Count - logged);
            Assert.IsType<InvalidOperationException>(Debug.Logged[logged]);

            var vanillaActor = new Actor();
            var vanillaCalls = new List<string>();
            vanillaActor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => { vanillaCalls.Add("boom"); throw new InvalidOperationException("handler"); });
            vanillaActor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => vanillaCalls.Add("after:" + m.Value));
            Vanilla(vanillaActor, "SampleMessage", Json(7), null);
            Assert.Equal(new[] { "boom", "after:7" }, vanillaCalls);
        }

        // (f) server と client の対応は別々に覚える。client 側で同名が1つ（一意）、
        // server 側で同名が2つ（曖昧）という組み合わせで、client 側で覚えた対応が
        // server 側の配達を歪めない（server 側は初回の全走査で曖昧と判定して本体に任せる）。
        [Fact]
        public void ServerAndClientResolutionsAreCachedSeparately()
        {
            var actor = new Actor();
            var clientCalls = new List<string>();
            var serverCalls = new List<string>();
            actor.CustomRpc_RegisterClientMessageHandler<RpcDispatchNameA.TwinName>(m => clientCalls.Add("clientA:" + m.Tag));
            actor.CustomRpc_RegisterServerMessageHandler("TwinName", (RpcDispatchNameA.TwinName m, DewPlayer p) => serverCalls.Add("serverA"));
            actor.CustomRpc_RegisterServerMessageHandler("TwinName", (RpcDispatchNameB.TwinName m, DewPlayer p) => serverCalls.Add("serverB"));
            Assert.False(Prefix(actor, "TwinName", TwinJson(1), null));
            Assert.Equal(new[] { "clientA:1" }, clientCalls);
            Assert.Equal(typeof(RpcDispatchNameA.TwinName), ByName(actor, server: false)["TwinName"]);
            var caller = new DewPlayer();
            Assert.True(Prefix(actor, "TwinName", TwinJson(1), caller));
            Assert.Empty(serverCalls);
            Assert.Contains("TwinName", Ambiguous(actor, server: true));
            Assert.Equal(typeof(RpcDispatchNameA.TwinName), ByName(actor, server: false)["TwinName"]);
            Vanilla(actor, "TwinName", TwinJson(1), caller);
            Assert.Equal(new[] { "serverA", "serverB" }, serverCalls);
            Assert.Single(clientCalls);
        }

        // (g) ハンドラが列挙中に自分の登録を解除すると、本体と同じ InvalidOperationException
        // で残りの配達を止める（添字走査なら黙って次のハンドラを飛ばして続けてしまう）。
        // どちらの経路でも2つ目のハンドラは呼ばれない。
        [Fact]
        public void ASelfUnregisteringHandlerAbortsLikeVanilla()
        {
            var json = Json(9);
            var caller = new DewPlayer();
            var actor = new Actor();
            var calls = new List<string>();
            Action<SampleMessage, DewPlayer> selfRemoving = null;
            selfRemoving = (m, p) => { calls.Add("self"); actor.CustomRpc_UnregisterServerMessageHandler(selfRemoving); };
            actor.CustomRpc_RegisterServerMessageHandler("SampleMessage", selfRemoving);
            actor.CustomRpc_RegisterServerMessageHandler<SampleMessage>("SampleMessage", (m, p) => calls.Add("after"));
            var prefixFault = Assert.Throws<TargetInvocationException>(() => Prefix(actor, "SampleMessage", json, caller));
            Assert.IsType<InvalidOperationException>(prefixFault.InnerException);
            Assert.Equal(new[] { "self" }, calls);

            var vanillaActor = new Actor();
            var vanillaCalls = new List<string>();
            Action<SampleMessage, DewPlayer> vanillaRemoving = null;
            vanillaRemoving = (m, p) => { vanillaCalls.Add("self"); vanillaActor.CustomRpc_UnregisterServerMessageHandler(vanillaRemoving); };
            vanillaActor.CustomRpc_RegisterServerMessageHandler("SampleMessage", vanillaRemoving);
            vanillaActor.CustomRpc_RegisterServerMessageHandler<SampleMessage>("SampleMessage", (m, p) => vanillaCalls.Add("after"));
            var vanillaFault = Assert.Throws<TargetInvocationException>(() => Vanilla(vanillaActor, "SampleMessage", json, caller));
            Assert.IsType<InvalidOperationException>(vanillaFault.InnerException);
            Assert.Equal(new[] { "self" }, vanillaCalls);
        }

        // (h) wrappedAction の形が変わっている（宣言型の検査をすり抜けた）場合は、
        // まだ誰も呼んでいない時点でパッチを外して本体へ戻す。ハンドラは呼ばれない
        // まま（本体が1回だけ配達する）ので、二重に呼ばれる経路がない。
        // RpcHandler の object 型字段 originalAction（Action&lt;SampleMessage&gt; が入る）を
        // wrappedAction として読ませることで、Delegate への変換が失敗する状況を作る。
        [Fact]
        public void AWronglyShapedWrappedActionDisablesThePatchBeforeAnyHandler()
        {
            var actor = new Actor();
            var calls = new List<string>();
            actor.CustomRpc_RegisterClientMessageHandler<SampleMessage>(m => calls.Add("v" + m.Value));
            var handlerType = typeof(Actor).GetNestedType("RpcHandler", Hidden);
            var originalAction = AccessTools.Field(handlerType, "originalAction");
            Patch.GetField("_wrappedAction", Hidden).SetValue(null,
                new Func<object, object>(handler => originalAction.GetValue(handler)));
            Assert.True(Prefix(actor, "SampleMessage", Json(7), null));
            Assert.Empty(calls);
            Assert.True((bool)Patch.GetField("_disabled", Hidden).GetValue(null));
            Assert.True((bool)Patch.GetField("_warned", Hidden).GetValue(null));
            Assert.True(Prefix(new Actor(), "SampleMessage", Json(7), null));
            Assert.Empty(calls);
        }

        // (i) 覚えた後に同名の別型を登録しても再走査はしない。毎通の再走査はこの軽量化の
        // 目的と両立せず、既知の登録36か所は名前一意のため起こらない場面であり、
        // docs/specs/perf-loop/20261019-1543.md の主張はこの範囲に限定している（固定する）。
        [Fact]
        public void ALaterSameNameRegistrationKeepsTheCachedSingleType()
        {
            var actor = new Actor();
            var calls = new List<string>();
            actor.CustomRpc_RegisterClientMessageHandler<RpcDispatchNameA.TwinName>(m => calls.Add("A:" + m.Tag));
            Assert.False(Prefix(actor, "TwinName", TwinJson(2), null));
            Assert.Equal(new[] { "A:2" }, calls);
            actor.CustomRpc_RegisterClientMessageHandler<RpcDispatchNameB.TwinName>(m => calls.Add("B:" + m.Tag));
            calls.Clear();
            Assert.False(Prefix(actor, "TwinName", TwinJson(2), null));
            Assert.Equal(new[] { "A:2" }, calls);
        }

        private sealed class SampleMessage { public int Value { get; set; } }
    }
}

namespace Issue73.Native.Tests.RpcDispatchNameA
{
    // 同じ単純名 TwinName を持つ別々の型（本体の「同名2型」の場面を作る）。
    internal sealed class TwinName { public int Tag { get; set; } }
}

namespace Issue73.Native.Tests.RpcDispatchNameB
{
    internal sealed class TwinName { public int Tag { get; set; } }
}
