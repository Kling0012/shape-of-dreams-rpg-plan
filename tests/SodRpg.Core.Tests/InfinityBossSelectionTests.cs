using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class InfinityBossSelectionTests
    {
        private static InfinityBossEntry Demon(int weight) => new InfinityBossEntry("Mon_Forest_BossDemon", "Zone_Forest", weight);
        private static InfinityBossEntry Maw(int weight) => new InfinityBossEntry("Mon_Special_BossMaw", null, weight);
        private static InfinityBossEntry Polaris(int weight) => new InfinityBossEntry("Mon_Special_BossPolaris", null, weight);

        [Fact]
        public void Weighted_intervals_include_each_boundary_and_skip_zero_weights()
        {
            var entries = new[] { Demon(2), Maw(0), Polaris(3) };
            var visited = new bool[5];
            for (long epoch = 0; epoch < 1000; epoch++)
            {
                int roll = new Rng(unchecked(Rng.SeedFrom("weighted:infinity-boss:") + (ulong)epoch)).Range(0, 4);
                visited[roll] = true;
                Assert.True(InfinityBossSelection.TryChoose(entries, "weighted", epoch, null, out var selected));
                Assert.Same(roll < 2 ? entries[0] : entries[2], selected);
            }
            Assert.All(visited, value => Assert.True(value));
        }

        [Fact]
        public void Previous_positive_boss_is_excluded_but_sole_positive_can_repeat()
        {
            var entries = new[] { Demon(100), Maw(1), Polaris(0) };
            for (long epoch = 0; epoch < 32; epoch++)
            {
                Assert.True(InfinityBossSelection.TryChoose(entries, "repeat", epoch, entries[0].BossTypeName, out var selected));
                Assert.Same(entries[1], selected);
            }
            var sole = new[] { Demon(0), Maw(1), Polaris(0) };
            Assert.True(InfinityBossSelection.TryChoose(sole, "repeat", 3, sole[1].BossTypeName, out var repeated));
            Assert.Same(sole[1], repeated);
        }

        [Fact]
        public void Replaying_run_segment_and_previous_boss_reproduces_the_sequence()
        {
            var first = new string[64];
            string previous = null;
            for (long epoch = 0; epoch < first.Length; epoch++)
            {
                Assert.True(InfinityBossSelection.TryChoose("resume", epoch, previous, out var selected));
                Assert.NotEqual(previous, selected.BossTypeName);
                first[epoch] = previous = selected.BossTypeName;
            }
            previous = null;
            for (long epoch = 0; epoch < first.Length; epoch++)
            {
                Assert.True(InfinityBossSelection.TryChoose("resume", epoch, previous, out var selected));
                Assert.Equal(first[epoch], selected.BossTypeName);
                previous = selected.BossTypeName;
            }
        }

        public static IEnumerable<object[]> InvalidTables()
        {
            yield return new object[] { null };
            yield return new object[] { Array.Empty<InfinityBossEntry>() };
            yield return new object[] { new[] { Demon(0), Maw(0) } };
            yield return new object[] { new[] { Demon(1), Maw(-1) } };
            yield return new object[] { new[] { Demon(1), Demon(2) } };
            yield return new object[] { new[] { Demon(1), (InfinityBossEntry)null } };
            yield return new object[] { new[] { Demon(int.MaxValue), Maw(1) } };
            yield return new object[] { new[] { Demon(1), new InfinityBossEntry("unknown", null, 0) } };
            yield return new object[] { new[] { new InfinityBossEntry("Mon_Ink_BossDarkMoon", "Zone_Ink", 1) } };
            yield return new object[] { new[] { new InfinityBossEntry("Mon_Forest_BossDemon", null, 1) } };
            yield return new object[] { new[] { new InfinityBossEntry("Mon_Special_BossMaw", "Zone_Forest", 1) } };
        }

        [Theory]
        [MemberData(nameof(InvalidTables))]
        public void Invalid_whole_table_returns_false_and_no_selection(InfinityBossEntry[] entries)
        {
            Assert.False(InfinityBossSelection.TryChoose(entries, "fallback", 0, null, out var selected));
            Assert.Null(selected);
        }
    }
}
