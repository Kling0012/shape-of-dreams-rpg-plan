using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // Direct tests for the Unity-independent Accessory_AttachPoint.FindOffset cache key and
    // entry (linked from src/SodRpg.Mod/AccessoryOffsetCacheKey.cs). The real FindOffset is a
    // game MonoBehaviour method, so the equivalence checks run the production cache against
    // SimulatedFindOffset, a faithful copy of the native search algorithm over a plain list.
    public sealed class AccessoryOffsetCacheTests
    {
        private sealed class TestOffset
        {
            public string entityName;
            public TestOffset(string entityName) { this.entityName = entityName; }
        }

        // Mirrors the decompiled Accessory_AttachPoint.FindOffset: exact entityName match
        // first, then the Skin_ -> Hero_ base-name fallback, null when nothing matches.
        private static TestOffset SimulatedFindOffset(List<TestOffset> list, string entityOrSkinName)
        {
            if (string.IsNullOrEmpty(entityOrSkinName)) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].entityName == entityOrSkinName)
                    return list[i];
            if (!entityOrSkinName.StartsWith("Skin_", StringComparison.Ordinal))
                return null;
            int skinEnd = entityOrSkinName.IndexOf('_', 5);
            if (skinEnd < 0) skinEnd = entityOrSkinName.Length;
            int baseLength = skinEnd - 5;
            if (baseLength <= 0) return null;
            for (int j = 0; j < list.Count; j++)
            {
                string entityName = list[j].entityName;
                if (entityName == null || entityName.Length != 5 + baseLength
                    || !entityName.StartsWith("Hero_", StringComparison.Ordinal))
                    continue;
                bool matches = true;
                for (int k = 0; k < baseLength; k++)
                    if (entityName[5 + k] != entityOrSkinName[5 + k]) { matches = false; break; }
                if (matches) return list[j];
            }
            return null;
        }

        // Reproduces the NativeAccessoryOffsetPerf Prefix/Postfix flow on the production
        // cache: TryGet answers from the cache, otherwise the search runs and Store records
        // its result (including null). Searches counts native search executions.
        private sealed class CacheDriver
        {
            private readonly AccessoryOffsetCache<TestOffset> _cache = new AccessoryOffsetCache<TestOffset>();
            public int Searches;

            public TestOffset Find(List<TestOffset> offsets, string entityOrSkinName)
            {
                if (_cache.TryGet(entityOrSkinName, offsets, out TestOffset cached)) return cached;
                Searches++;
                TestOffset result = SimulatedFindOffset(offsets, entityOrSkinName);
                _cache.Store(entityOrSkinName, offsets, result);
                return result;
            }
        }

        private static List<TestOffset> SampleList()
        {
            return new List<TestOffset>
            {
                new TestOffset("Hero_A"),
                new TestOffset("Hero_B"),
                new TestOffset("Hero_ABC"),
            };
        }

        [Fact]
        public void Key_Matches_WhenNameListAndElementsAreSameReferences()
        {
            string name = "Hero_A";
            List<TestOffset> offsets = SampleList();
            var key = new AccessoryOffsetCacheKey<TestOffset>(name, offsets);
            Assert.True(key.Matches(name, offsets));
        }

        [Fact]
        public void Key_DoesNotMatch_DifferentStringInstanceWithSameContent()
        {
            string name = string.Concat("Hero_", "A");
            string otherName = string.Concat("Hero", "_A");
            Assert.Equal(name, otherName);
            Assert.False(ReferenceEquals(name, otherName));
            List<TestOffset> offsets = SampleList();
            var key = new AccessoryOffsetCacheKey<TestOffset>(name, offsets);
            Assert.False(key.Matches(otherName, offsets));
        }

        [Fact]
        public void Key_DoesNotMatch_DifferentListInstanceWithSameElements()
        {
            string name = "Hero_A";
            List<TestOffset> offsets = SampleList();
            List<TestOffset> otherList = new List<TestOffset>(offsets);
            var key = new AccessoryOffsetCacheKey<TestOffset>(name, offsets);
            Assert.False(key.Matches(name, otherList));
        }

        [Fact]
        public void Key_DoesNotMatch_WhenSameListCountChanges()
        {
            string name = "Hero_A";
            List<TestOffset> offsets = SampleList();
            var key = new AccessoryOffsetCacheKey<TestOffset>(name, offsets);
            offsets.Add(new TestOffset("Hero_D"));
            Assert.False(key.Matches(name, offsets));
            offsets.RemoveAt(offsets.Count - 1);
            Assert.True(key.Matches(name, offsets));
        }

        [Fact]
        public void Key_DoesNotMatch_WhenSameCountElementIsReplacedInPlace()
        {
            string name = "Hero_A";
            List<TestOffset> offsets = SampleList();
            var key = new AccessoryOffsetCacheKey<TestOffset>(name, offsets);
            TestOffset original = offsets[1];
            offsets[1] = new TestOffset("Hero_B");
            Assert.False(key.Matches(name, offsets));
            offsets[1] = original;
            Assert.True(key.Matches(name, offsets));
        }

        [Fact]
        public void Cache_ReusesStoredResultWithoutSearchingAgain()
        {
            var driver = new CacheDriver();
            List<TestOffset> offsets = SampleList();
            TestOffset first = driver.Find(offsets, "Hero_B");
            Assert.Same(offsets[1], first);
            Assert.Equal(1, driver.Searches);
            for (int frame = 0; frame < 5; frame++)
                Assert.Same(first, driver.Find(offsets, "Hero_B"));
            Assert.Equal(1, driver.Searches);
        }

        [Fact]
        public void Cache_ReusesNullResultWithoutSearchingAgain()
        {
            var driver = new CacheDriver();
            List<TestOffset> offsets = SampleList();
            Assert.Null(driver.Find(offsets, "Hero_Z"));
            Assert.Equal(1, driver.Searches);
            for (int frame = 0; frame < 5; frame++)
                Assert.Null(driver.Find(offsets, "Hero_Z"));
            Assert.Equal(1, driver.Searches);
        }

        public static IEnumerable<object[]> EquivalenceNames()
        {
            yield return new object[] { null };
            yield return new object[] { "" };
            yield return new object[] { "Hero_A" };
            yield return new object[] { "Hero_B" };
            yield return new object[] { "Hero_ABC" };
            yield return new object[] { "Hero_Z" };
            yield return new object[] { "Skin_A" };
            yield return new object[] { "Skin_A_Red" };
            yield return new object[] { "Skin_ABC" };
            yield return new object[] { "Skin_ABC_Alternate" };
            yield return new object[] { "Skin_" };
            yield return new object[] { "Skin_Hero" };
            yield return new object[] { "SomeOther_Name" };
        }

        [Theory]
        [MemberData(nameof(EquivalenceNames))]
        public void Cache_ReturnsSameResultAsDirectSearch_IncludingNull(string name)
        {
            List<TestOffset> offsets = SampleList();
            Assert.Null(SimulatedFindOffset(new List<TestOffset>(), name));
            // Repeated cached calls keep returning exactly what a fresh native search returns.
            var driver = new CacheDriver();
            for (int frame = 0; frame < 3; frame++)
            {
                TestOffset expected = SimulatedFindOffset(offsets, name);
                TestOffset actual = driver.Find(offsets, name);
                Assert.True(ReferenceEquals(expected, actual),
                    "frame " + frame + ": expected " + Describe(expected) + " got " + Describe(actual));
            }
        }

        [Fact]
        public void Cache_ReturnsSameResultAsDirectSearch_AcrossListEdits()
        {
            var driver = new CacheDriver();
            List<TestOffset> offsets = SampleList();
            string name = "Hero_A";

            Assert.Same(SimulatedFindOffset(offsets, name), driver.Find(offsets, name));
            Assert.Equal(1, driver.Searches);

            // Same-count in-place replacement: the key must miss and re-run the search.
            TestOffset originalFirst = offsets[0];
            offsets[0] = new TestOffset("Hero_X");
            Assert.Null(driver.Find(offsets, name));
            Assert.Equal(2, driver.Searches);
            offsets[0] = originalFirst;
            Assert.Same(originalFirst, driver.Find(offsets, name));
            Assert.Equal(3, driver.Searches);

            // Count changes via add and remove.
            offsets.Add(new TestOffset("Hero_A2"));
            Assert.Same(SimulatedFindOffset(offsets, name), driver.Find(offsets, name));
            offsets.RemoveAt(0);
            Assert.Same(SimulatedFindOffset(offsets, name), driver.Find(offsets, name));

            // Name switch and switch back.
            Assert.Same(SimulatedFindOffset(offsets, "Skin_A_Red"), driver.Find(offsets, "Skin_A_Red"));
            Assert.Same(SimulatedFindOffset(offsets, name), driver.Find(offsets, name));

            // List instance swap with identical contents still re-runs the search.
            List<TestOffset> swapped = new List<TestOffset>(offsets);
            Assert.Same(SimulatedFindOffset(swapped, name), driver.Find(swapped, name));
        }

        [Fact]
        public void Cache_FirstMatchWins_LikeNativeSearch()
        {
            var offsets = new List<TestOffset>
            {
                new TestOffset("Hero_A"),
                new TestOffset("Hero_A"),
            };
            var driver = new CacheDriver();
            Assert.Same(offsets[0], driver.Find(offsets, "Hero_A"));
            Assert.Same(offsets[0], driver.Find(offsets, "Hero_A"));
        }

        [Fact]
        public void Cache_SkinFallbackMatchesHeroBaseName()
        {
            var offsets = new List<TestOffset>
            {
                new TestOffset("Hero_A"),
                new TestOffset("Hero_ABC"),
            };
            var driver = new CacheDriver();
            // "Skin_ABC_Alternate": first pass misses, fallback strips to base "ABC".
            Assert.Same(offsets[1], driver.Find(offsets, "Skin_ABC_Alternate"));
            Assert.Same(offsets[1], driver.Find(offsets, "Skin_ABC_Alternate"));
            // "Skin_A" has no separator after the prefix: base name is the whole rest.
            Assert.Same(offsets[0], driver.Find(offsets, "Skin_A"));
            // "Skin_" alone yields an empty base name and no match.
            Assert.Null(driver.Find(offsets, "Skin_"));
        }

        [Fact]
        public void Cache_EntityNameValueEditOnKeptElement_IsNotDetected_DocumentedLimitation()
        {
            // Documented limitation (docs/specs/perf-loop/20261018-1503.md): the key compares
            // references only, so rewriting entityName on a kept element changes nothing the
            // key checks. The stale result keeps serving and the search does not re-run, even
            // though a fresh search (SimulatedFindOffset) would now return null. The mod
            // guides users to disable nativeAccessoryOffsetCache for such edits instead; the
            // snapshot-entityName-references fix stays recorded as an unimplemented idea.
            var driver = new CacheDriver();
            List<TestOffset> offsets = SampleList();
            Assert.Same(offsets[0], driver.Find(offsets, "Hero_A"));
            Assert.Equal(1, driver.Searches);

            offsets[0].entityName = "Hero_X";

            Assert.Same(offsets[0], driver.Find(offsets, "Hero_A"));
            Assert.Equal(1, driver.Searches);
            Assert.Null(SimulatedFindOffset(offsets, "Hero_A"));
        }

        [Fact]
        public void StoreGate_StoresOnlyWhenEnabledAndNativeSearchRan()
        {
            // Postfix gate: with the option off, the Postfix must not store even though the
            // native search ran (__runOriginal true) — the Prefix then re-runs the search
            // every call, and storing would allocate the key snapshot every frame.
            Assert.False(AccessoryOffsetStoreGate.ShouldStore(disabled: false, optionEnabled: false, ranOriginal: true));
            Assert.False(AccessoryOffsetStoreGate.ShouldStore(disabled: false, optionEnabled: false, ranOriginal: false));
            // Another mod's prefix substituted the result: never store it.
            Assert.False(AccessoryOffsetStoreGate.ShouldStore(disabled: false, optionEnabled: true, ranOriginal: false));
            // Runtime failure latched the patch off: never store again.
            Assert.False(AccessoryOffsetStoreGate.ShouldStore(disabled: true, optionEnabled: true, ranOriginal: true));
            // The one storing combination.
            Assert.True(AccessoryOffsetStoreGate.ShouldStore(disabled: false, optionEnabled: true, ranOriginal: true));
        }

        private static string Describe(TestOffset offset)
            => offset == null ? "null" : offset.entityName + "@" + RuntimeHelpers.GetHashCode(offset).ToString();
    }
}
