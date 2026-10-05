using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed class NativeAcceptanceTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public NativeAcceptanceTests()
        {
            NetworkServer.active = true;
            Time.frameCount = 1;
            Time.unscaledTime = 100;
            DewPlayer.gamePlayers.Clear();
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "run" };
            HostAuthority.NativeInstance = null;
            Set(typeof(ClientSession), "_hostSession", null);
        }
        public void Dispose()
        {
            DewPlayer.gamePlayers.Clear();
            HostAuthority.NativeInstance = null;
            Set(typeof(ClientSession), "_hostSession", null);
            NetworkedManagerBase<GameManager>.softInstance = null;
            NetworkedManagerBase<ActorManager>.softInstance = null;
        }

        [Fact]
        public void ReturningPeerResendSharesFrameBudgetWithDifferentialTrafficAndEventuallyDeliversEveryFact()
        {
            var saved = new KillClassificationCheckpoint { RunId = "run", HostSequence = 70 };
            var returning = new KillReplayPeer { Id = "native-owner.client", NativeOwnerId = "native-owner" };
            returning.Participation.Add(new KillParticipationRange { StreamId = "prior", After = 0, Through = 70 });
            saved.HostPeers.Add(returning);
            for (int i = 1; i <= 70; i++)
                saved.HostFacts.Add(new AuthoritativeRunKill("run", "event-" + i, (uint)i, 0, NightmareAffix.None, null, i, "prior"));
            var session = Session();
            session.Profile.KillClassification = saved;
            Set(typeof(ClientSession), "_hostSession", session);
            var actor = new Actor();
            var host = Host(actor);
            var player = new DewPlayer { netId = 7, guid = "native-owner" };
            DewPlayer.gamePlayers.Add(player);
            Call(host, "OnKillReceipt", new DreamforgeKillReceiptMsg
            {
                protocol = Protocol.Version, authorityGeneration = ClientSession.HostAuthorityGeneration,
                runId = "run", clientId = "client", observationSessionId = "reconnected",
                receipts = Array.Empty<DreamforgeKillStreamReceipt>(),
            }, player);
            actor.Sent.Clear(); // First stream control is explicitly outside the replay frame budget.
            for (uint id = 100; id < 150; id++) Call(host, "QueueMonsterCue", id, (int)id);
            var delivered = new List<long>();
            var frameLoads = new List<int>();
            for (int frame = 0; frame < 16 && delivered.Count < 70; frame++)
            {
                Time.frameCount++;
                int before = actor.Sent.Count;
                Call(host, "TickKillReplay");
                var sends = actor.Sent.Skip(before).ToArray();
                Assert.InRange(sends.Length, 1, 32);
                frameLoads.Add(sends.Length);
                delivered.AddRange(sends.Select(s => s.Message).OfType<DreamforgeMonsterKillMsg>().Select(m => m.sequence));
                Call(host, "TickKillReplay"); // Re-entry in the same frame must not obtain a second RPC allowance.
                Assert.InRange(actor.Sent.Count - before, 1, 32);
                delivered.AddRange(actor.Sent.Skip(before + sends.Length).Select(s => s.Message).OfType<DreamforgeMonsterKillMsg>().Select(m => m.sequence));
            }
            Assert.True(frameLoads.Count > 2, "70 facts plus differential traffic must span multiple frames.");
            Assert.Equal(Enumerable.Range(1, 70).Select(i => (long)i), delivered);
            Assert.Contains(actor.Sent, s => s.Target == null && s.Message is DreamforgeMonsterCueMsg);
            Assert.All(actor.Sent.Where(s => s.Message is DreamforgeMonsterKillMsg), s => Assert.Same(player, s.Target));
            var outstanding = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(outstanding);
            Assert.Equal(Enumerable.Range(1, 70).Select(i => (long)i), outstanding.HostFacts.Select(f => f.Sequence));
            Call(host, "OnKillReceipt", new DreamforgeKillReceiptMsg
            {
                protocol = Protocol.Version, authorityGeneration = ClientSession.HostAuthorityGeneration,
                runId = "run", clientId = "client", observationSessionId = "reconnected",
                receipts = new[] { new DreamforgeKillStreamReceipt { streamId = "prior", receivedThrough = 70 } },
            }, player);
            var awaitingHostDurability = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(awaitingHostDurability);
            Assert.Equal(70, awaitingHostDurability.HostFacts.Count); // Peer ACK alone cannot discard host-unsaved facts.
            Set(session, "_killDurableRunId", "run");
            Set(session, "_killDurableReceipts", new[] { new KillReceiptState { StreamId = "prior", AcknowledgedThrough = 70 } });
            var compacted = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(compacted);
            Assert.Empty(compacted.HostFacts);
            Assert.Equal(70, compacted.HostSequence);
            Assert.All(compacted.HostPeers, p => Assert.DoesNotContain(p.Participation, r => r.StreamId == "prior"));
            session.Profile.KillClassification = compacted;
            session.Profile = ProfileCodec.Read(ProfileCodec.Write(session.Profile), new List<string>());
            DewPlayer.gamePlayers.Clear();
            var restoredHost = Host(new Actor());
            var afterReload = new KillClassificationCheckpoint { RunId = "run" };
            restoredHost.CaptureKillReplay(afterReload);
            Assert.Empty(afterReload.HostFacts);
            Assert.Equal(70, afterReload.HostSequence); // The frontier survives without recreating resolved history.
        }

        [Fact]
        public void DifferentialUpdatesCoalesceWithoutMovingIdsBehindTheirNeighborsAndRotateCategories()
        {
            var actor = new Actor();
            var host = Host(actor);
            foreach (uint id in new uint[] { 101, 102 })
                Call(host, "SendMonsterRemoval", new HostAuthority.MonsterRuntime { SyncNetId = id });
            var first = new HostAuthority.MonsterRuntime { Monster = new Monster { netId = 201 }, Variant = new HostAuthority.Variant { Id = "initial" } };
            var second = new HostAuthority.MonsterRuntime { Monster = new Monster { netId = 202 }, Variant = new HostAuthority.Variant { Id = "neighbor" } };
            Call(host, "SendMonsterClassification", first, null);
            Call(host, "SendMonsterClassification", second, null);
            Call(host, "QueueMonsterCue", 301u, 1);
            Call(host, "QueueMonsterCue", 302u, 2);
            for (int i = 0; i < 100; i++)
            {
                first.Variant.Id = "updated-" + i;
                Call(host, "SendMonsterClassification", first, null);
                Call(host, "QueueMonsterCue", 301u, i);
            }
            Call(host, "FlushMonsterSync", 3);
            Assert.Equal(new uint[] { 101, 201, 301 }, actor.Sent.Select(s => NetId(s.Message)));
            Call(host, "FlushMonsterSync", 3);
            Assert.Equal(new uint[] { 101, 201, 301, 102, 202, 302 }, actor.Sent.Select(s => NetId(s.Message)));
            Assert.Equal("updated-99", Assert.IsType<DreamforgeVariantMsg>(actor.Sent[1].Message).variantId);
            Assert.Equal(99, Assert.IsType<DreamforgeMonsterCueMsg>(actor.Sent[2].Message).cue);
            Call(host, "FlushMonsterSync", 32);
            Assert.Equal(6, actor.Sent.Count); // Neither intermediate values nor unchanged classifications are resent.
            Call(host, "SendMonsterClassification", first, null);
            Call(host, "FlushMonsterSync", 32);
            Assert.Equal(6, actor.Sent.Count);
        }

        [Fact]
        public void EmptyProvisionalPeerIsNotRestoredOrPersistedAndDisconnectLeavesNoPeer()
        {
            var session = Session();
            var checkpoint = new KillClassificationCheckpoint { RunId = "run", HostSequence = 42 };
            var empty = new KillReplayPeer { Id = "connection.old.7" };
            empty.Participation.Add(new KillParticipationRange { StreamId = "old", After = 42, Through = 42 });
            checkpoint.HostPeers.Add(empty);
            session.Profile.KillClassification = checkpoint;
            Set(typeof(ClientSession), "_hostSession", session);
            var host = Host(new Actor());
            Call(host, "EnsureKillRun");
            Assert.Empty((IDictionary)Get(host, "_killPeers"));
            var player = new DewPlayer { netId = 7, guid = "unmodded" };
            DewPlayer.gamePlayers.Add(player);
            Call(host, "RegisterKillPeer", player);
            var snapshot = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(snapshot);
            Assert.Empty(snapshot.HostPeers);
            DewPlayer.gamePlayers.Remove(player);
            Call(host, "TickKillReplay");
            Assert.Empty((IDictionary)Get(host, "_killPeers"));
            var afterDisconnect = new KillClassificationCheckpoint { RunId = "run" };
            host.CaptureKillReplay(afterDisconnect);
            Assert.Empty(afterDisconnect.HostPeers);
        }

        [Fact]
        public void DividendsReachDiskOnlyAtCoalescedDeadlineAndOnlyWrittenRevisionAdvancesHostAck()
        {
            string directory = Path.Combine(Path.GetTempPath(), "issue73-native-" + Guid.NewGuid().ToString("N"));
            var session = Session();
            var store = new ProfileStore(new RealFileSystem(), Path.Combine(directory, "profile.json"), 73);
            Set(session, "_store", store);
            Set(session, "_nextSave", 130f);
            Set(typeof(ClientSession), "_hostSession", session);
            try
            {
                ClientSession.PrepareHostKillStream("run", "73", 3);
                var receipt = DreamforgePressureDividendMsg.FromReward(new PressureDividendReward("run", 0, 1, "7", "nonce-one"), 7);
                Call(session, "OnPressureDividend", receipt);
                Assert.Equal(1, session.Profile.Run.SatchelShards);
                Assert.True((bool)Get(session, "_dirty"));
                Assert.Equal(105f, Get(session, "_nextSave"));
                Call(session, "TickPeriodicSave");
                Assert.False(File.Exists(store.Path));
                Assert.Equal(0, Get(session, "_saveCount"));
                Time.unscaledTime = 102;
                Call(session, "OnPressureDividend", DreamforgePressureDividendMsg.FromReward(new PressureDividendReward("run", 0, 2, "7", "nonce-two"), 7));
                Call(session, "OnPressureDividend", receipt);
                Assert.Equal(2, session.Profile.Run.SatchelShards);
                Assert.Equal(105f, Get(session, "_nextSave"));
                Time.unscaledTime = 104.99f;
                Call(session, "TickPeriodicSave");
                Call(session, "TickKillSync");
                Assert.False(File.Exists(store.Path));
                Assert.Equal(0, ClientSession.DurableHostKillReceipt("73"));
                Time.unscaledTime = 105;
                Call(session, "TickPeriodicSave");
                var writer = (AsyncProfileWriter)Get(session, "_writer");
                Assert.True(writer.WaitForRevision(session.Profile.Revision));
                Assert.Equal(1, Get(session, "_saveCount"));
                var persisted = store.Load();
                Assert.Equal(2, persisted.Run.SatchelShards);
                Assert.Equal(3, Assert.Single(persisted.KillClassification.Receipts).AcknowledgedThrough);
                Call(session, "TickKillSync");
                Assert.Equal(3, ClientSession.DurableHostKillReceipt("73"));
                Assert.False((bool)Get(session, "_dirty"));
            }
            finally
            {
                session.FlushSaves();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Theory]
        [InlineData("basic")]
        [InlineData("damage")]
        [InlineData("element")]
        public void NativeScopeBuffersPreserveOuterFactsAcrossNestedAndClientOnlyCalls(string kind)
        {
            Host(new Actor());
            Type type = kind == "basic" ? typeof(BasicAttackContext) : kind == "damage" ? typeof(NativeDamageContext) : typeof(ElementApplicationContext);
            var outerActor = new Actor();
            var outerTarget = new Entity { currentHealth = 83 };
            outerTarget.Status.currentShield = 17;
            outerTarget.Status.fireStack = 9;
            object Enter(Actor actor, Entity target)
            {
                object[] args = kind == "basic" ? new object[] { actor, new Entity(), target, true, true, null }
                    : kind == "damage" ? new object[] { actor, target, null }
                    : new object[] { actor, ElementalType.Fire, target, null };
                type.GetMethod("Prefix", Hidden).Invoke(null, args);
                return args[args.Length - 1];
            }
            var outerState = Enter(outerActor, outerTarget);
            var outer = Get(type, "Current");
            try
            {
                var innerState = Enter(new Actor(), new Entity());
                var inner = Get(type, "Current");
                try { Assert.NotSame(outer, inner); }
                finally { Call(type, "Finalizer", innerState); }
                Assert.Same(outer, Get(type, "Current"));
                Assert.Same(outerActor, Get(outer, "Actor"));
                Assert.Same(outerTarget, Get(outer, "Target"));
                if (kind == "damage") { Assert.Equal(83f, Get(outer, "Health")); Assert.Equal(17f, Get(outer, "Shield")); }
                if (kind == "element") Assert.Equal(9, Get(outer, "Fire"));
                NetworkServer.active = false;
                var clientState = Enter(new Actor(), new Entity());
                try { Assert.Null(Get(type, "Current")); }
                finally { Call(type, "Finalizer", clientState); NetworkServer.active = true; }
                Assert.Same(outer, Get(type, "Current"));
            }
            finally { Call(type, "Finalizer", outerState); }
            Assert.Null(Get(type, "Current"));
            Assert.Null(Get(outer, "Actor"));
            Assert.Null(Get(outer, "Target"));
            long previousSerial = kind == "element" ? 0 : (long)Get(outer, "Serial");
            var reusedState = Enter(new Actor(), new Entity());
            try
            {
                Assert.Same(outer, Get(type, "Current"));
                if (kind != "element") Assert.True((long)Get(outer, "Serial") > previousSerial);
            }
            finally { Call(type, "Finalizer", reusedState); }
        }

        [Fact]
        public void MechanismDispatchBufferReturnClearsInnerReferencesWithoutClearingOuterWork()
        {
            var host = Host(new Actor());
            var outer = Call(host, "RentMechanismDispatchBuffers");
            var victim = new Entity();
            ((IDictionary)Get(outer, "Entities")).Add(17L, victim);
            var inner = Call(host, "RentMechanismDispatchBuffers");
            Assert.NotSame(outer, inner);
            ((IDictionary)Get(inner, "Entities")).Add(18L, new Entity());
            Call(host, "ReturnMechanismDispatchBuffers", inner);
            Assert.Same(victim, ((IDictionary)Get(outer, "Entities"))[17L]);
            var reusedInner = Call(host, "RentMechanismDispatchBuffers");
            Assert.Same(inner, reusedInner);
            Assert.Empty((IDictionary)Get(reusedInner, "Entities"));
            Call(host, "ReturnMechanismDispatchBuffers", reusedInner);
            Call(host, "ReturnMechanismDispatchBuffers", outer);
            Assert.Empty((IDictionary)Get(outer, "Entities"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        public void DirectRemoveAbilityRunsProductionHookInvalidatesEpochAndPreservesBorrowedEquipment(int index)
        {
            var harmony = new Harmony("issue73.remove." + index);
            harmony.CreateClassProcessor(typeof(NativeAttributedAbilityRemoved)).Patch();
            try
            {
                var host = Host(new Actor());
                var hero = new Hero();
                var source = new TestQ { owner = hero };
                var survivor = new TestW { owner = hero };
                hero.Skill.Slots[(HeroSkillLocation)index] = source;
                hero.Skill.Slots[(HeroSkillLocation)((index + 1) % 6)] = survivor;
                long before = host.RefreshMemoryAttributionEquipment(hero);
                var equipment = (IDictionary)Get(host, "_mechanismEquipment");
                var borrowed = (MechanismEquipment)equipment[hero];
                var borrowedIds = (HashSet<string>)((IDictionary)Get(host, "_attributionMemoryIds"))[hero];
                var ledger = (MemoryActivationAttribution)Get(host, "_memoryAttribution");
                var oldActivation = ledger.BeginActivation(hero.GetInstanceID(), nameof(TestQ));
                long notifiedEpoch = 0;
                host.MemoryAttributionEquipmentChanged += (owner, epoch) => { Assert.Same(hero, owner); notifiedEpoch = epoch; };
                hero.Ability.RemoveAbility(index); // Harmony invokes the real Mod postfix, not a hand-written epoch update.
                long after = ledger.EquipmentEpoch(hero.GetInstanceID());
                Assert.True(after > before);
                Assert.Equal(after, notifiedEpoch);
                Assert.False(ledger.IsCurrent(oldActivation));
                var current = (MechanismEquipment)equipment[hero];
                Assert.NotSame(borrowed, current);
                Assert.Null(current.Find(nameof(TestQ)));
                Assert.Equal(survivor.GetInstanceID(), current.Find(nameof(TestW)).InstanceId);
                Assert.Equal(source.GetInstanceID(), borrowed.Find(nameof(TestQ)).InstanceId);
                Assert.Equal(before, borrowed.EquipmentEpoch);
                Assert.Contains(nameof(TestQ), borrowedIds);
                Assert.DoesNotContain(nameof(TestQ), (HashSet<string>)((IDictionary)Get(host, "_attributionMemoryIds"))[hero]);
                hero.Ability.RemoveAbility(index);
                Assert.Equal(after, ledger.EquipmentEpoch(hero.GetInstanceID()));
            }
            finally { harmony.UnpatchAll(harmony.Id); }
        }

        private static uint NetId(object message) => (uint)Get(message, "netId");
        private static ClientSession Session()
        {
            var profile = Profile.CreateNew(73);
            Rules.BeginRun(profile, "run");
            var session = new ClientSession { Profile = profile, LocalHero = new Hero { netId = 7 } };
            session.ActiveRunId = "run"; // RunActive は本体実装と同じく TrackRun による runId の追跡が必要
            return session;
        }
        private static HostAuthority Host(Actor actor)
        {
            var host = new HostAuthority();
            HostAuthority.NativeInstance = host;
            Set(host, "_registeredOn", actor);
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            return host;
        }
        private static Type ObjectType(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;
        private static object Get(object target, string name) => ObjectType(target).GetField(name, Hidden).GetValue(Instance(target));
        private static void Set(object target, string name, object value) => ObjectType(target).GetField(name, Hidden).SetValue(Instance(target), value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return ObjectType(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
}
