using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Culinary.ModUnderTest;
using HarmonyLib;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SodRpg.Culinary.Tests
{
    public sealed class CulinaryReloadTests : IDisposable
    {
        private const string StoreKey = "dreamforge.culinary.native-batch.v1";
        private readonly List<Type> _copies = new List<Type>();

        public CulinaryReloadTests()
        {
            AppDomain.CurrentDomain.SetData(StoreKey, null);
            Mod.Log.Warnings.Clear();
        }

        private Type Load()
        {
            var assembly = Assembly.Load(File.ReadAllBytes(typeof(AssemblyMarker).Assembly.Location));
            var type = assembly.GetType("SodRpg.Mod.CulinaryIngredientCap", throwOnError: true);
            _copies.Add(type);
            Call(type, "Install", new Harmony("culinary.test." + Guid.NewGuid().ToString("N")));
            Assert.True(Enabled(type));
            return type;
        }

        private static object Call(Type type, string name, params object[] arguments) =>
            type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments);
        private static bool Enabled(Type type) =>
            (bool)type.GetField("_enabled", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        private static Hero Owner(StackedStatusEffect effect) => new Hero { Status = new Status { Effect = effect } };
        private static int Count(Type type, Hero hero, int nativeCount)
        {
            object[] arguments = { hero, nativeCount };
            Call(type, "AfterIngredientCount", arguments);
            return (int)arguments[1];
        }

        [Fact]
        public void SecondAssemblyRetainsNativeBatchWithoutTruncatingLiveInventory()
        {
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 40, stack = 1000 };
            var hero = Owner(effect);
            var first = Load();
            Call(first, "BeforeMutation", effect);
            Assert.Equal(int.MaxValue, effect.maxStack);
            Assert.Equal(40, Count(first, hero, effect.stack));
            Call(first, "Stop");
            Assert.Equal(1000, effect.stack);
            Assert.Equal(int.MaxValue, effect.maxStack);

            var second = Load();
            Assert.NotSame(first.Assembly, second.Assembly);
            Assert.Equal(first.Module.ModuleVersionId, second.Module.ModuleVersionId);
            Call(second, "BeforeMutation", effect);
            Assert.True(Enabled(second));
            Assert.Equal(40, Count(second, hero, effect.stack));
            Assert.False((bool)Call(second, "BeforeConsumeIngredients", hero));
            Assert.Equal(960, effect.stack);
            Assert.Equal(40, effect.LastRemoved);
            Assert.Equal(1, effect.RemoveCalls);
            Assert.Empty(Mod.Log.Warnings);
        }

        [Fact]
        public void SameCopyRestartAndPooledReuseRetainOriginalBatch()
        {
            var copy = Load();
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 17, stack = 80 };
            Call(copy, "BeforeMutation", effect);
            Call(copy, "Stop");
            Assert.Equal(80, effect.stack);
            Call(copy, "Install", new Harmony("culinary.restart"));
            // A pooled object can still have our raised maximum on its next OnCreate.
            effect.stack = 31;
            Call(copy, "BeforeMutation", effect);
            Assert.Equal(17, Count(copy, Owner(effect), 31));
            Assert.Empty(Mod.Log.Warnings);
        }

        [Fact]
        public void IndependentAndRestoredEffectsCaptureTheirOwnNativeBatch()
        {
            var first = Load();
            var original = new Se_Gem_L_Culinary_Stack { maxStack = 17, stack = 300 };
            Call(first, "BeforeMutation", original);
            var second = Load();
            var restored = new Se_Gem_L_Culinary_Stack { maxStack = 63, stack = original.stack };
            Call(second, "BeforeMutation", restored);
            Assert.Equal(17, Count(second, Owner(original), original.stack));
            Assert.Equal(63, Count(first, Owner(restored), restored.stack));
            Assert.Equal(300, restored.stack);
            Assert.Equal(int.MaxValue, restored.maxStack);
            Assert.Empty(Mod.Log.Warnings);
        }

        [Fact]
        public void ConcurrentCopiesCaptureBeforeEitherRaisesTheMaximum()
        {
            var first = Load();
            var second = Load();
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 29, stack = 100 };
            Parallel.Invoke(() => Call(first, "BeforeMutation", effect),
                () => Call(second, "BeforeMutation", effect));
            Assert.True(Enabled(first));
            Assert.True(Enabled(second));
            Assert.Equal(29, Count(first, Owner(effect), 100));
            Assert.Equal(29, Count(second, Owner(effect), 100));
            Assert.Empty(Mod.Log.Warnings);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [InlineData(715827883)]
        public void UnknownUnsafeNativeLimitsStillDisableOnlyCulinary(int maximum)
        {
            var copy = Load();
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = maximum, stack = 100 };
            Call(copy, "BeforeMutation", effect);
            Assert.False(Enabled(copy));
            Assert.Equal(maximum, effect.maxStack);
            Assert.Equal(100, effect.stack);
            Assert.True((bool)Call(copy, "BeforeConsumeIngredients", Owner(effect)));
            Assert.Single(Mod.Log.Warnings);
            Assert.Contains("Native culinary cooking limit is not safe", Mod.Log.Warnings[0]);
        }

        [Fact]
        public void ForeignStoreIsNotReplacedAndDoesNotMutateTheEffect()
        {
            var copy = Load();
            var foreign = new object();
            AppDomain.CurrentDomain.SetData(StoreKey, foreign);
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 40, stack = 100 };
            Call(copy, "BeforeMutation", effect);
            Assert.False(Enabled(copy));
            Assert.Same(foreign, AppDomain.CurrentDomain.GetData(StoreKey));
            Assert.Equal(40, effect.maxStack);
            Assert.Equal(100, effect.stack);
            Assert.Single(Mod.Log.Warnings);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(12, 12)]
        [InlineData(40, 40)]
        [InlineData(90, 40)]
        [InlineData(int.MaxValue, 40)]
        public void ReloadedCookingConsumesOnlyTheDisplayedNativeBatch(int inventory, int batch)
        {
            var first = Load();
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 40, stack = inventory };
            Call(first, "BeforeMutation", effect);
            Call(first, "Stop");
            var second = Load();
            var hero = Owner(effect);
            Assert.Equal(batch, Count(second, hero, inventory));
            Assert.False((bool)Call(second, "BeforeConsumeIngredients", hero));
            Assert.Equal(inventory - batch, effect.stack);
            Assert.Equal(batch, effect.LastRemoved);
            Assert.Empty(Mod.Log.Warnings);
        }

        [Theory]
        [InlineData(2147483640, 20, 7)]
        [InlineData(int.MaxValue, 1, 0)]
        [InlineData(12, 5, 5)]
        [InlineData(12, -5, -5)]
        public void ReloadRetainsOverflowProtectionAndNegativeNativeOperands(int stack, int value, int expected)
        {
            var first = Load();
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 40, stack = stack };
            Call(first, "BeforeMutation", effect);
            Call(first, "Stop");
            var second = Load();
            object[] arguments = { effect, value };
            Call(second, "BeforeAdd", arguments);
            Assert.Equal(expected, (int)arguments[1]);
            Assert.Equal(stack, effect.stack); // The callback never writes the native stack.
            Assert.True(Enabled(second));
            Assert.Empty(Mod.Log.Warnings);
        }

        [Fact]
        public void OtherStatusTypesAndNullAreUntouched()
        {
            var copy = Load();
            foreach (var effect in new StackedStatusEffect[]
                { new StackedStatusEffect { maxStack = 40 }, new DerivedCulinaryStack { maxStack = 40 }, null })
            {
                Call(copy, "BeforeMutation", effect);
                if (effect != null) Assert.Equal(40, effect.maxStack);
            }
            Assert.True(Enabled(copy));
            Assert.Empty(Mod.Log.Warnings);
        }

        [Fact]
        public void SharedProvenanceDoesNotKeepNativeActorsAlive()
        {
            var weak = CaptureTemporaryEffect(Load());
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(weak.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CaptureTemporaryEffect(Type copy)
        {
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 40, stack = 100 };
            Call(copy, "BeforeMutation", effect);
            return new WeakReference(effect);
        }

        public void Dispose()
        {
            foreach (var copy in _copies) Call(copy, "Stop");
            AppDomain.CurrentDomain.SetData(StoreKey, null);
        }
    }
}
