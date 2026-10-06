using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;

namespace SodRpg.Core.Tests
{
    // Independent expectations read the authoring tables, not generated/product methods.
    internal static class LootEconomyInputs
    {
        internal static JsonElement Raw(string domain)
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, "tools", "balance", domain + ".json");
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path))) return document.RootElement.Clone();
            }
            throw new InvalidOperationException("tools/balance/" + domain + ".json was not found");
        }

        internal static double BossChance(bool nightmare, int depth)
        {
            var table = Raw("boss-sets");
            int boundedDepth = Math.Max(0, Math.Min(table.GetProperty("maxDropDepth").GetInt32(), depth));
            long percent = table.GetProperty("normalDropPercent").GetInt32()
                + (nightmare ? table.GetProperty("nightmareBonusPercent").GetInt32() : 0)
                + (long)table.GetProperty("depthBonusPercent").GetInt32() * boundedDepth;
            return Math.Min(table.GetProperty("maxDropPercent").GetInt32(), percent) / 100.0;
        }

        internal static string TierKey(MonsterTier tier) => tier == MonsterTier.Lesser ? "lesser"
            : tier == MonsterTier.Normal ? "normal" : tier == MonsterTier.MiniBoss ? "miniBoss" : "boss";

        internal static double DropChance(MonsterTier tier, int heat)
        {
            var table = Raw("loot");
            return Math.Min(1, table.GetProperty("dropChance").GetProperty(TierKey(tier)).GetDouble()
                * (1 + table.GetProperty("heat").GetProperty("dropBonus").GetDouble() * Math.Max(0, Math.Min(5, heat))));
        }

        internal static double RarityProbability(MonsterTier tier, int heat, Rarity rarity)
        {
            var table = Raw("loot");
            var weights = table.GetProperty("rarityWeights").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            double luck = (tier >= MonsterTier.MiniBoss ? table.GetProperty("tierLuck").GetProperty(TierKey(tier)).GetDouble() : 0)
                + table.GetProperty("heat").GetProperty("luck").GetDouble() * Math.Max(0, Math.Min(5, heat));
            double multiplier = 1 + table.GetProperty("rarityLuckCoefficient").GetDouble() * Math.Max(0, luck);
            for (int i = 0; i < weights.Length; i++)
                weights[i] = (tier == MonsterTier.Boss && i == 0) || (tier < MonsterTier.MiniBoss && i == 4)
                    ? 0 : weights[i] * Math.Pow(multiplier, i);
            return weights[(int)rarity] / weights.Sum();
        }

        internal static int Exchange(string key) => Raw("economy").GetProperty("exchange").GetProperty(key).GetInt32();

        internal static int MerchantPrice(int heat)
        {
            var merchant = Raw("economy").GetProperty("merchant");
            return merchant.GetProperty("baseGold").GetInt32()
                + merchant.GetProperty("goldPerHeat").GetInt32() * Math.Max(0, Math.Min(5, heat));
        }

        internal static int SalvageDust(Rarity rarity, int enhance)
        {
            var table = Raw("economy").GetProperty("salvage");
            return ForgeBalanceTests.At("salvage", "shards", (int)rarity) * table.GetProperty("dustPerShard").GetInt32()
                + enhance * table.GetProperty("dustPerEnhance").GetInt32();
        }

        internal static double SamplingTolerance(double variance, int samples) => 6 * Math.Sqrt(Math.Max(0, variance) / samples) + 1.0 / samples;
    }
}
