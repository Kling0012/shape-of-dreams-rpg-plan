using SodRpg.Core.Game;

namespace BalanceSim;

internal static class DreamEventValuesReport
{
    public static IReadOnlyList<QuantityMetricValue> Measure()
    {
        var values = new List<QuantityMetricValue>();
        void Add(string key, double value, string unit) =>
            values.Add(new("events/" + key, key, value, unit));
        // Adopted Core definitions, before external reward/XP modifiers.
        Add("offerChance", DreamEvents.OfferChance, "probability");
        Add("stargazerDropBonus", DreamEvents.StargazerDropBonus, "drop-rate");
        Add("luckyStarLuck", DreamEvents.LuckyStarLuck, "luck");
        Add("merchant/baseShards", EventsBalance.MerchantBaseShards, "shards");
        Add("merchant/shardsPerHeat", EventsBalance.MerchantShardsPerHeat, "shards/heat");
        Add("twinMirror/shards", EventsBalance.TwinMirrorShards, "shards");
        Add("twinMirror/epicShards", EventsBalance.TwinMirrorEpicShards, "shards");
        Add("chalice/winChance", EventsBalance.ChaliceWinChance, "probability");
        Add("chalice/bonusMultiplier", EventsBalance.ChaliceBonusMultiplier, "multiplier");
        Add("cauldron/relicCount", EventsBalance.CauldronRelicCount, "relics");
        Add("tapir/relicsPerTuning", EventsBalance.TapirRelicsPerTuning, "relics");
        Add("courageGate/heatIncrement", EventsBalance.CourageGateHeatIncrement, "heat");
        Add("courageGate/shards", EventsBalance.CourageGateShards, "shards");
        Add("archive/baseXp", EventsBalance.ArchiveBaseXp, "dream-xp");
        Add("archive/xpPerHeat", EventsBalance.ArchiveXpPerHeat, "dream-xp/heat");
        Add("memoryWell/tuning", EventsBalance.MemoryWellTuning, "tuning");
        Add("memoryWell/epicTuning", EventsBalance.MemoryWellEpicTuning, "tuning");
        Add("shadowExchange/shards", EventsBalance.ShadowExchangeShards, "shards");
        Add("shadowExchange/epicShards", EventsBalance.ShadowExchangeEpicShards, "shards");
        Add("lostMausoleum/shards", EventsBalance.LostMausoleumShards, "shards");
        Add("relicWager/rareWinChance", EventsBalance.RelicWagerRareWinChance, "probability");
        Add("relicWager/lowerWinChance", EventsBalance.RelicWagerLowerWinChance, "probability");
        Add("stoneBroker/shards", EventsBalance.StoneBrokerShards, "shards");
        Add("stoneBroker/tuning", EventsBalance.StoneBrokerTuning, "tuning");
        Add("shardKiln/tuning", EventsBalance.ShardKilnTuning, "tuning");
        Add("shardKiln/shards", EventsBalance.ShardKilnShards, "shards");
        Add("starOffering/xp", EventsBalance.StarOfferingXp, "star-xp");
        Add("dreamOffering/baseXp", EventsBalance.DreamOfferingBaseXp, "dream-xp");
        Add("dreamOffering/xpPerHeat", EventsBalance.DreamOfferingXpPerHeat, "dream-xp/heat");
        Add("abyssalChest/shards", EventsBalance.AbyssalChestShards, "shards");
        Add("abyssalChest/heatIncrement", EventsBalance.AbyssalChestHeatIncrement, "heat");
        Add("sealedVault/shards", EventsBalance.SealedVaultShards, "shards");
        Add("offerWeights/Merchant", DreamEvents.OfferWeight(DreamEvent.Merchant), "weight");
        Add("offerWeights/Fountain", DreamEvents.OfferWeight(DreamEvent.Fountain), "weight");
        Add("offerWeights/Chalice", DreamEvents.OfferWeight(DreamEvent.Chalice), "weight");
        Add("offerWeights/Lantern", DreamEvents.OfferWeight(DreamEvent.Lantern), "weight");
        Add("offerWeights/ForgeShrine", DreamEvents.OfferWeight(DreamEvent.ForgeShrine), "weight");
        Add("offerWeights/TwinMirror", DreamEvents.OfferWeight(DreamEvent.TwinMirror), "weight");
        Add("offerWeights/Stargazer", DreamEvents.OfferWeight(DreamEvent.Stargazer), "weight");
        Add("offerWeights/Cauldron", DreamEvents.OfferWeight(DreamEvent.Cauldron), "weight");
        Add("offerWeights/Tapir", DreamEvents.OfferWeight(DreamEvent.Tapir), "weight");
        Add("offerWeights/CourageGate", DreamEvents.OfferWeight(DreamEvent.CourageGate), "weight");
        Add("offerWeights/Archive", DreamEvents.OfferWeight(DreamEvent.Archive), "weight");
        Add("offerWeights/LuckyStar", DreamEvents.OfferWeight(DreamEvent.LuckyStar), "weight");
        Add("offerWeights/MemoryWell", DreamEvents.OfferWeight(DreamEvent.MemoryWell), "weight");
        Add("offerWeights/ShadowExchange", DreamEvents.OfferWeight(DreamEvent.ShadowExchange), "weight");
        Add("offerWeights/LostMausoleum", DreamEvents.OfferWeight(DreamEvent.LostMausoleum), "weight");
        Add("offerWeights/RelicWager", DreamEvents.OfferWeight(DreamEvent.RelicWager), "weight");
        Add("offerWeights/TemperingAltar", DreamEvents.OfferWeight(DreamEvent.TemperingAltar), "weight");
        Add("offerWeights/StoneBroker", DreamEvents.OfferWeight(DreamEvent.StoneBroker), "weight");
        Add("offerWeights/ShardKiln", DreamEvents.OfferWeight(DreamEvent.ShardKiln), "weight");
        Add("offerWeights/StarOffering", DreamEvents.OfferWeight(DreamEvent.StarOffering), "weight");
        Add("offerWeights/DreamOffering", DreamEvents.OfferWeight(DreamEvent.DreamOffering), "weight");
        Add("offerWeights/AbyssalChest", DreamEvents.OfferWeight(DreamEvent.AbyssalChest), "weight");
        Add("offerWeights/RelicExchange", DreamEvents.OfferWeight(DreamEvent.RelicExchange), "weight");
        Add("offerWeights/SealedVault", DreamEvents.OfferWeight(DreamEvent.SealedVault), "weight");
        Add("offerWeights/PowerCrucible", DreamEvents.OfferWeight(DreamEvent.PowerCrucible), "weight");
        for (int heat = 0; heat <= Content.MaxHeat; heat++)
        {
            Add($"merchant/heat/{heat}/cost", DreamEvents.MerchantCost(heat), "shards");
            Add($"archive/heat/{heat}/reward", DreamEvents.ArchiveXp(heat), "dream-xp");
            Add($"dreamOffering/heat/{heat}/reward", DreamEvents.DreamOfferingXp(heat), "dream-xp");
        }
        foreach (var rarity in Enum.GetValues<Rarity>())
        {
            Add($"twinMirror/rarity/{rarity}/cost", DreamEvents.TwinMirrorCost(rarity), "shards");
            Add($"memoryWell/rarity/{rarity}/cost", DreamEvents.MemoryWellCost(rarity), "tuning");
            Add($"shadowExchange/rarity/{rarity}/cost", DreamEvents.ShadowExchangeCost(rarity), "shards");
            if (rarity < Rarity.Epic)
                Add($"relicWager/rarity/{rarity}/chance", DreamEvents.RelicWagerChance(rarity), "probability");
        }
        // Fixed count axis: tuning the conversion rate does not add/remove rows.
        for (int count = 0; count <= Content.SatchelCapacity; count++)
            Add($"tapir/relicCount/{count}/tuning", count / EventsBalance.TapirRelicsPerTuning, "tuning");
        Add("chalice/winTotalMultiplier", 1 + EventsBalance.ChaliceBonusMultiplier, "multiplier");
        return values;
    }
}
