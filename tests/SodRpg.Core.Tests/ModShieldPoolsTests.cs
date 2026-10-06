using System;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class ModShieldPoolsTests
    {
        private sealed class Handle { public bool Alive = true; public float Amount, Seconds; }
        private sealed class Adapter : IModShieldAdapter<Handle>
        {
            public int Processes, Creates, Destroys;
            public float Multiplier = 1f, Published;
            public Handle Last;
            public Action<Handle> AfterProcessRaw;
            public bool IsAlive(Handle handle) => handle.Alive && handle.Amount > 0;
            public float Remaining(Handle handle) => handle.Amount;
            public float ProcessRaw(Handle handle, float rawAmount)
            { Processes++; AfterProcessRaw?.Invoke(handle); return rawAmount * Multiplier; }
            public Handle CreateRaw(float rawAmount, float seconds, float processedCap)
            {
                Creates++;
                Last = new Handle { Amount = Math.Min(processedCap, ProcessRaw(null, rawAmount)), Seconds = seconds };
                Published = Last.Amount;
                return Last;
            }
            public void SetProcessed(Handle handle, float amount) => handle.Amount = amount;
            public void Refresh(Handle handle, float seconds) => handle.Seconds = seconds;
            public void Destroy(Handle handle) { Destroys++; handle.Alive = false; }
        }
        [Fact]
        public void SacrificeLowerAwardCapPreservesStrongerOrdinaryPool()
        {
            var pools = new ModShieldPools<Handle>(); var adapter = new Adapter();
            var key = new ModShieldPoolKey(1, 2, ModShieldPoolKind.Ordinary);
            pools.Apply(key, adapter, 13, 100, 0, 4, 1);
            adapter.Multiplier = 2;
            pools.Apply(key, adapter, 20, 100, 1, 4, 1, .1f);
            Assert.Equal(13, adapter.Last.Amount); Assert.Equal(1, pools.Count);
            adapter.Last.Amount = 2;
            pools.Apply(key, adapter, 20, 100, 2, 4, 1, .1f);
            Assert.Equal(10, adapter.Last.Amount);
        }
        [Fact]
        public void SeparateOwnersKindsAndUnrelatedHandlesCoexist()
        {
            var pools = new ModShieldPools<Handle>(); var adapters = new Adapter[4];
            var unrelated = new Handle { Amount = 90 };
            for (int i = 0; i < 4; i++)
            {
                adapters[i] = new Adapter();
                pools.Apply(new ModShieldPoolKey(i == 3 ? 2 : 1, 5, (ModShieldPoolKind)(i % 3)), adapters[i], 100, 100, 0, 4, 1);
            }
            Assert.Equal(4, pools.Count); pools.RemoveOwner(1); Assert.Equal(1, pools.Count);
            Assert.True(adapters[3].Last.Alive); Assert.True(unrelated.Alive); Assert.Equal(90, unrelated.Amount);
            pools.Clear(); Assert.Equal(0, pools.Count); Assert.True(unrelated.Alive);
        }
        [Theory]
        [InlineData(ModShieldPoolKind.Ordinary, 15f)]
        [InlineData(ModShieldPoolKind.Rampart, 10f)]
        [InlineData(ModShieldPoolKind.Allied, 3f)]
        public void NativeCreationWithoutShieldSkipsAwardAndNextGrantUsesOriginalCap(ModShieldPoolKind kind, float cap)
        {
            var pools = new ModShieldPools<Se_GenericShield_OneShot>();
            var recipient = new Entity();
            var root = new Actor();
            var key = new ModShieldPoolKey(1, 2, kind);
            Mirror.NetworkServer.active = true;
            try
            {
                root.AfterShieldCreated = handle => { handle.DestroyIfActive(); handle.shield = null; };
                Assert.False(pools.Apply(key, new NativeModShieldAdapter(root, recipient), 20, 100, 0, 4, 1));
                Assert.Equal(0, pools.Count);
                Assert.Equal(0f, recipient.Status.currentShield);
                root.AfterShieldCreated = null;
                Assert.True(pools.Apply(key, new NativeModShieldAdapter(root, recipient), 20, 100, 1, 4, 1));
                Assert.Equal(cap, recipient.Status.currentShield, 4);
                Assert.Equal(1, pools.Count);
            }
            finally { Mirror.NetworkServer.active = false; }
        }
        [Theory]
        [InlineData(ModShieldPoolKind.Ordinary, 15f)]
        [InlineData(ModShieldPoolKind.Rampart, 10f)]
        [InlineData(ModShieldPoolKind.Allied, 3f)]
        public void DestroyedNativePoolIsReplacedWithoutResurrectingItsRemainingAmount(ModShieldPoolKind kind, float cap)
        {
            var pools = new ModShieldPools<Se_GenericShield_OneShot>();
            var recipient = new Entity();
            var root = new Actor();
            var adapter = new NativeModShieldAdapter(root, recipient);
            var key = new ModShieldPoolKey(1, 2, kind);
            Se_GenericShield_OneShot previous = null;
            Mirror.NetworkServer.active = true;
            try
            {
                root.AfterShieldCreated = handle => previous = handle;
                Assert.True(pools.Apply(key, adapter, 20, 100, 0, 4, 1));
                previous.DestroyIfActive();
                previous.shield = null;
                root.AfterShieldCreated = null;
                Assert.True(pools.Apply(key, adapter, 1, 100, 1, 4, 1));
                Assert.Equal(1f, recipient.Status.currentShield);
                Assert.True(pools.Apply(key, adapter, 20, 100, 2, 4, 1));
                Assert.Equal(cap, recipient.Status.currentShield, 4);
                Assert.False(previous.isActive);
                Assert.Equal(1, pools.Count);
            }
            finally { Mirror.NetworkServer.active = false; }
        }
        [Fact]
        public void ReceiverProcessorDestroyingCachedHandleCreatesAFreshAward()
        {
            var pools = new ModShieldPools<Handle>(); var adapter = new Adapter();
            var key = new ModShieldPoolKey(1, 2, ModShieldPoolKind.Ordinary);
            pools.Apply(key, adapter, 15, 100, 0, 4, 1);
            var previous = adapter.Last;
            adapter.AfterProcessRaw = handle =>
            {
                adapter.AfterProcessRaw = null;
                handle.Alive = false;
            };
            Assert.True(pools.Apply(key, adapter, 6, 100, 1, 4, 1));
            Assert.NotSame(previous, adapter.Last);
            Assert.Equal(6f, adapter.Last.Amount);
            Assert.Equal(2, adapter.Creates);
            Assert.Equal(0, adapter.Destroys);
            Assert.Equal(1, pools.Count);
        }
        [Fact]
        public void InvalidAmountsAndLowerCapsFailExplicitly()
        {
            var pools = new ModShieldPools<Handle>(); var adapter = new Adapter();
            var key = new ModShieldPoolKey(1, 2, ModShieldPoolKind.Allied);
            Assert.Throws<ArgumentOutOfRangeException>(() => pools.Apply(key, adapter, float.NaN, 100, 0, 4, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pools.Apply(key, adapter, 5, 100, 0, 4, 1, .1f));
            Assert.Equal(0, adapter.Processes);
        }
        [Fact]
        public void PendingAwardFromPreviousEquipmentEpochCannotCreateANewPool()
        {
            var pools = new ModShieldPools<Handle>(); var adapter = new Adapter();
            var key = new ModShieldPoolKey(1, 2, ModShieldPoolKind.Ordinary);
            pools.Apply(key, adapter, 6, 100, 0, 4, 1);
            pools.Maintain(key, 100, 1, 2, true);
            Assert.False(pools.Apply(key, adapter, 8, 100, 1, 4, 1));
            Assert.Equal(0, pools.Count); Assert.Equal(1, adapter.Creates);
            Assert.True(pools.Apply(key, adapter, 8, 100, 1, 4, 2));
            Assert.Equal(2, adapter.Creates);
        }
    }
}
