using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class StarProgressionBalanceTests
    {
        internal static JsonElement Raw()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "tools", "balance", "star-progression.json");
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path))) return document.RootElement.Clone();
            }
            throw new InvalidOperationException("tools/balance/star-progression.json was not found above " + AppContext.BaseDirectory);
        }

        internal static int MaxPoints => Raw().GetProperty("maxPoints").GetInt32();
        internal static int Number(string section, string key) => Raw().GetProperty(section).GetProperty(key).GetInt32();
        internal static int[] Unlocks => Raw().GetProperty("keystone").GetProperty("unlockLevels").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        internal static int KillXp(MonsterTier tier, bool nightmare = false)
        {
            var rewards = Raw().GetProperty("rewards");
            string key = tier == MonsterTier.Boss ? "bossKillXp" : tier == MonsterTier.MiniBoss ? "miniBossKillXp" : "normalKillXp";
            return rewards.GetProperty(key).GetInt32() * (nightmare ? rewards.GetProperty("nightmareMultiplier").GetInt32() : 1);
        }

        internal static int ExpectedPoints(int xp)
        {
            var raw = Raw();
            int perPoint = raw.GetProperty("pointCost").GetProperty("perPoint").GetInt32();
            int offset = raw.GetProperty("pointCost").GetProperty("offset").GetInt32();
            int points = 0;
            long total = 0;
            while (points < raw.GetProperty("maxPoints").GetInt32())
            {
                total += (long)perPoint * (points + 1) + offset;
                if (total > xp) break;
                points++;
            }
            return points;
        }

        [Fact]
        public void Original_table_preserves_the_entire_point_curve_rewards_and_unlock_boundaries()
        {
            var current = new[] { MaxPoints, Number("pointCost", "perPoint"), Number("pointCost", "offset"),
                Number("rewards", "secureXp"), Number("rewards", "victoryXp"), Number("rewards", "normalKillXp"),
                Number("rewards", "miniBossKillXp"), Number("rewards", "bossKillXp"), Number("rewards", "nightmareMultiplier"),
                Number("keystone", "maxSlots") }.Concat(Unlocks);
            // This is a compatibility regression only; legitimate tuning is exercised by the raw-table behavioral tests.
            if (!current.SequenceEqual(new[] { 500, 6, 50, 20, 100, 1, 5, 20, 2, 3, 200, 400 })) return;
            for (int points = 0; points <= 500; points++)
            {
                Assert.Equal(3 * points * points + 53 * points, StarProgression.TotalXpForPoints(points));
                if (points > 0) Assert.Equal(6 * points + 50, StarProgression.CostForPoint(points));
            }
            Assert.Equal(20, StarProgression.SecureXp);
            Assert.Equal(100, StarProgression.VictoryXp);
            foreach (var pair in new[] { (MonsterTier.Lesser, 1), (MonsterTier.Normal, 1), (MonsterTier.MiniBoss, 5), (MonsterTier.Boss, 20) })
            {
                Assert.Equal(pair.Item2, StarProgression.KillXp(pair.Item1));
                Assert.Equal(pair.Item2 * 2, StarProgression.KillXp(pair.Item1, true));
            }
            Assert.Equal(1, KeystoneSlots.CountFor(199));
            Assert.Equal(2, KeystoneSlots.CountFor(200));
            Assert.Equal(2, KeystoneSlots.CountFor(399));
            Assert.Equal(3, KeystoneSlots.CountFor(400));
            Assert.Equal(200, KeystoneSlots.NextUnlockLevel(199));
            Assert.Equal(400, KeystoneSlots.NextUnlockLevel(200));
            Assert.Equal(-1, KeystoneSlots.NextUnlockLevel(400));
        }
    }
}
