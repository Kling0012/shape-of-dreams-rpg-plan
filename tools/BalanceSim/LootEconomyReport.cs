using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>Exact baseline distributions and rewards from adopted Core definitions, not encounter simulation.</summary>
internal static class LootEconomyReport
{
    public const string Policy = "Core baseline; heat/depth 0..5; no pact/waypoint/depth luck; boss Uncommon floor; no focus/owned/codex modifiers; material expectations before modifiers; merchant before native gold scaling";
    internal const int SecureSampleShards = 100;

    public static IReadOnlyList<QuantityMetricValue> Measure()
    {
        var entries = new List<QuantityMetricValue>();
        void Add(string id, string label, double value, string unit) =>
            entries.Add(new("loot-economy/" + id, label, value, unit));

        // Keep each coefficient observable even when a cap or a zero probability masks its current effect.
        Add("loot/heatDropBonus", "Drop multiplier increment per heat", Loot.HeatDropBonus, "multiplier/heat");
        Add("loot/heatLuck", "Rarity luck per heat", Loot.HeatLuck, "luck/heat");
        Add("loot/rarityLuckCoefficient", "Rarity weight multiplier increment per luck", LootBalance.RarityLuckCoefficient, "multiplier/luck");
        Add("loot/bossExtraRelicChance", "Conditional boss extra relic chance", Loot.BossExtraRelicChance * 100, "percent");
        Add("loot/focusWeight", "Focused line selection weight multiplier", Loot.FocusWeight, "multiplier");
        Add("loot/setPieceWeight", "Legendary set piece selection multiplier", Loot.SetPieceWeight, "multiplier");
        Add("loot/setCompletionWeight", "Started set missing piece selection multiplier", Loot.SetCompletionWeight, "multiplier");
        Add("loot/variantAdditiveShards", "Variant additive shards after integer percentage reward", LootBalance.VariantAdditiveShards, "shards");
        foreach (var rarity in Enum.GetValues<Rarity>())
            Add($"loot/rarityWeight/{rarity}", $"{rarity} base rarity weight", LootBalance.RarityWeights[(int)rarity], "weight");

        foreach (var tier in Enum.GetValues<MonsterTier>())
        {
            Add($"loot/tier/{tier}/luck", $"{tier} base rarity luck", Loot.TierLuck(tier), "luck");
            for (int heat = 0; heat <= 5; heat++)
            {
                string key = $"loot/tier/{tier}/heat/{heat}";
                double chance = Loot.DropChance(tier, heat);
                double luck = Loot.TierLuck(tier) + Loot.HeatLuck * Loot.ClampHeat(heat);
                bool legendary = tier >= MonsterTier.MiniBoss;
                Rarity floor = tier == MonsterTier.Boss ? Rarity.Uncommon : Rarity.Common;
                Add(key + "/dropChance", $"{tier} heat {heat}: drop chance", chance * 100, "percent");
                Add(key + "/luckPercent", $"{tier} heat {heat}: rarity weight increase per rank", Loot.LuckPercent(luck), "percent");
                double relics = chance * (tier == MonsterTier.Boss ? 1 + Loot.BossExtraRelicChance : 1);
                Add(key + "/expectedRelics", $"{tier} heat {heat}: expected ordinary relics", relics, "relics/kill");
                double high = Loot.HighRarityProbability(luck, legendary, floor, out double legend);
                Add(key + "/expectedEpicPlus", $"{tier} heat {heat}: expected Epic+ relics", relics * high, "relics/kill");
                Add(key + "/expectedLegendary", $"{tier} heat {heat}: expected Legendary relics", relics * legend, "relics/kill");
                AddRarityDistribution(entries, key, $"{tier} heat {heat}", luck, legendary, floor);
            }
        }
        // Separately expose the floor/allowLegendary contracts, including a Legendary-only floor.
        foreach (var floor in Enum.GetValues<Rarity>())
            foreach (bool legendary in new[] { false, true })
            {
                // Legendary-only without permission has no legal outcome, not a zero-valued distribution.
                if (!legendary && floor == Rarity.Legendary) continue;
                AddRarityDistribution(entries, $"loot/distribution/floor/{floor}/legendary/{legendary}",
                    $"Luck 0; floor {floor}; Legendary allowed {legendary}", 0, legendary, floor);
            }

        Add("materials/lesser/shardChance", "Lesser shard payout chance", LootBalance.LesserShardChance * 100, "percent");
        Add("materials/lesser/shards", "Lesser successful shard payout", LootBalance.LesserShards, "shards");
        Add("materials/lesser/expectedShards", "Lesser expected shards", LootBalance.LesserShardChance * LootBalance.LesserShards, "shards/kill");
        Add("materials/normal/shardChance", "Normal shard payout chance", LootBalance.NormalShardChance * 100, "percent");
        Add("materials/normal/shardMin", "Normal minimum successful shard payout", LootBalance.NormalShardMin, "shards");
        Add("materials/normal/shardMax", "Normal maximum successful shard payout", LootBalance.NormalShardMax, "shards");
        Add("materials/normal/expectedShards", "Normal expected shards", LootBalance.NormalShardChance * ((double)LootBalance.NormalShardMin + LootBalance.NormalShardMax) / 2, "shards/kill");
        Add("materials/miniBoss/shardMin", "MiniBoss minimum shard payout", LootBalance.MiniBossShardMin, "shards");
        Add("materials/miniBoss/shardMax", "MiniBoss maximum shard payout", LootBalance.MiniBossShardMax, "shards");
        Add("materials/miniBoss/expectedShards", "MiniBoss expected shards", ((double)LootBalance.MiniBossShardMin + LootBalance.MiniBossShardMax) / 2, "shards/kill");
        Add("materials/miniBoss/tuningChance", "MiniBoss tuning payout chance", LootBalance.MiniBossTuningChance * 100, "percent");
        Add("materials/miniBoss/tuning", "MiniBoss successful tuning payout", LootBalance.MiniBossTuning, "tuning");
        Add("materials/miniBoss/expectedTuning", "MiniBoss expected tuning", LootBalance.MiniBossTuningChance * LootBalance.MiniBossTuning, "tuning/kill");
        Add("materials/boss/shardMin", "Boss minimum shard payout", LootBalance.BossShardMin, "shards");
        Add("materials/boss/shardMax", "Boss maximum shard payout", LootBalance.BossShardMax, "shards");
        Add("materials/boss/expectedShards", "Boss expected shards", ((double)LootBalance.BossShardMin + LootBalance.BossShardMax) / 2, "shards/kill");
        Add("materials/boss/tuning", "Boss fixed tuning payout", LootBalance.BossTuning, "tuning/kill");

        Add("bossSets/normalDropPercent", "Boss set base drop chance", BossSets.NormalDropPercent, "percent");
        Add("bossSets/nightmareBonusPercent", "Boss set nightmare bonus", BossSets.NightmareBonusPercent, "percentage-points");
        Add("bossSets/depthBonusPercent", "Boss set bonus per depth", BossSets.DepthBonusPercent, "percentage-points/depth");
        Add("bossSets/maxDropDepth", "Boss set drop depth cap", BossSets.MaxDropDepth, "depth");
        Add("bossSets/maxDropPercent", "Boss set drop chance cap", BossSets.MaxDropPercent, "percent");
        foreach (bool nightmare in new[] { false, true })
            for (int depth = 0; depth <= 5; depth++)
                Add($"bossSets/nightmare/{nightmare}/depth/{depth}/dropChance", $"Boss set nightmare {nightmare}, depth {depth}", BossSets.DropChance(nightmare, depth) * 100, "percent");

        Add("economy/dustPerBatch", "Dust spent per exchange batch", Economy.DustPerBatch, "dust/batch");
        Add("economy/shardsPerBatch", "Shards earned per exchange batch", Economy.ShardsPerBatch, "shards/batch");
        Add("economy/shardsPerDust", "Dust exchange rate", (double)Economy.ShardsPerBatch / Economy.DustPerBatch, "shards/dust");
        Add("economy/merchantGoldPerHeat", "Merchant gold price increment per heat", EconomyBalance.MerchantGoldPerHeat, "gold/heat");
        for (int heat = 0; heat <= 5; heat++)
            Add($"economy/merchant/heat/{heat}/gold", $"Merchant heat {heat}: base gold price", Economy.MerchantGoldBase(heat), "gold");
        Add("economy/salvageDustPerShard", "Unenhanced salvage dust per base shard", EconomyBalance.SalvageDustPerShard, "dust/shard");
        Add("economy/salvageDustPerEnhance", "Salvage dust per enhancement level", EconomyBalance.SalvageDustPerEnhance, "dust/enhance");
        foreach (var rarity in Enum.GetValues<Rarity>())
            foreach (int enhance in new[] { 0, 5, 20 })
                Add($"economy/salvage/{rarity}/enhance/{enhance}", $"{rarity} +{enhance}: formula salvage dust", Economy.SalvageDust(rarity, enhance), "dust");
        Add("economy/expedition/limboDropBonus", "Limbo drop-rate increment per depth", Rules.LimboDropBonus, "drop-rate/limbo-depth");
        Add("economy/expedition/limboLuck", "Limbo luck increment per depth", Rules.LimboLuck, "luck/limbo-depth");
        Add("economy/expedition/secureBonusDivisor", "Secure delve bonus divisor", EconomyBalance.SecureBonusDivisor, "divisor");
        for (int heat = 0; heat <= Content.MaxHeat; heat++)
            foreach (bool doubled in new[] { false, true })
            {
                var profile = new Profile { Run = new RunState { Heat = heat, SatchelShards = SecureSampleShards } };
                if (doubled) profile.Run.Pacts.Add(Pact.CursedHoard);
                Rules.Secure(profile);
                Add($"economy/secure/heat/{heat}/depthPact/{doubled}/shards",
                    $"Secure {SecureSampleShards} shards, heat {heat}, depth pact {doubled}", profile.Material(Materials.Shard), "shards");
            }
        return entries;
    }

    private static void AddRarityDistribution(List<QuantityMetricValue> entries, string key, string label,
        double luck, bool allowLegendary, Rarity floor)
    {
        // Same adopted definitions and operation order as RollRarity, without consuming RNG.
        double f = 1 + LootBalance.RarityLuckCoefficient * Math.Max(0, luck), total = 0;
        var weights = new double[LootBalance.RarityWeights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            if (i < (int)floor || (!allowLegendary && i == (int)Rarity.Legendary)) continue;
            weights[i] = LootBalance.RarityWeights[i] * Math.Pow(f, i);
            total += weights[i];
        }
        for (int i = 0; i < weights.Length; i++)
        {
            // RollRarity returns its floor if all permitted weights are zero.
            double probability = total == 0 ? (i == (int)floor ? 1 : 0) : weights[i] / total;
            entries.Add(new("loot-economy/" + key + "/rarity/" + (Rarity)i,
                label + ": " + (Rarity)i + " rarity probability", probability * 100, "percent"));
        }
    }
}
