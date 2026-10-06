using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SodRpg.Core.Game;

namespace SodRpg.Core.Tests
{
    // A pre-cutover compatibility observation, not a second tunable balance source.
    internal static class Stage6CompatibilitySnapshot
    {
        internal static SortedDictionary<string, string> Capture()
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            void Number(string key, object value) => values.Add(key, Convert.ToString(value, CultureInfo.InvariantCulture));
            void Definition(string key, object value)
            {
                if (value == null) return;
                var type = value.GetType();
                if (type.IsPrimitive || type.IsEnum || value is decimal || value is string)
                {
                    Number(key, value);
                    return;
                }
                if (value is IEnumerable sequence)
                {
                    int i = 0;
                    foreach (var item in sequence) Definition(key + "/" + i++, item);
                    return;
                }
                if (value is Txt) return;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    Definition(key + "/" + field.Name, field.GetValue(value));
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (property.GetIndexParameters().Length == 0 && property.Name != "Description")
                        Definition(key + "/" + property.Name, property.GetValue(value));
            }
            Definition("ja/pacts", Pacts.All);
            Definition("ja/daily", DailyDream.All);
            Definition("ja/waypoints", Waypoints.All);
            for (int heat = 0; heat <= Content.MaxHeat; heat++)
            {
                Number("economy/merchant/" + heat, Economy.MerchantGoldBase(heat));
                Number("event/merchant/" + heat, DreamEvents.MerchantCost(heat));
                foreach (MonsterTier tier in Enum.GetValues(typeof(MonsterTier)))
                {
                    string key = "loot/" + heat + "/" + tier;
                    Number(key + "/chance", Loot.DropChance(tier, heat));
                    Number(key + "/luck", Loot.TierLuck(tier));
                    var rng = new Rng(149);
                    for (int roll = 0; roll < 24; roll++)
                    {
                        var reward = Loot.RollKill(rng, tier, 1, heat);
                        Number(key + "/" + roll + "/shards", reward.Shards);
                        Number(key + "/" + roll + "/tuning", reward.Tuning);
                        Number(key + "/" + roll + "/xp", reward.Xp);
                        for (int i = 0; i < reward.Relics.Count; i++)
                        {
                            var r = reward.Relics[i];
                            Definition(key + "/" + roll + "/relic/" + i, new { r.BaseId, r.UniqueId, r.NamedId, r.Rarity, r.ItemLevel, r.Affixes, r.Powers });
                        }
                    }
                    Number(key + "/rng", rng.State);
                }
            }
            foreach (Rarity rarity in Enum.GetValues(typeof(Rarity)))
                for (int enhance = 0; enhance <= 20; enhance++)
                    Number("economy/dust/" + rarity + "/" + enhance, Economy.SalvageDust(rarity, enhance));
            for (int depth = -1; depth <= 6; depth++)
                foreach (bool nightmare in new[] { false, true })
                    Number("boss/" + depth + "/" + nightmare, BossSets.DropChance(nightmare, depth));
            Number("economy/dustBatch", Economy.DustPerBatch);
            Number("economy/shardBatch", Economy.ShardsPerBatch);
            Number("loot/luckPercent", Loot.LuckPercent(1.3));
            Number("event/offer", DreamEvents.OfferChance);
            Number("economy/limboDropBonus", Rules.LimboDropBonus);
            Number("economy/limboLuck", Rules.LimboLuck);
            Number("loot/focusWeight", Loot.FocusWeight);
            Number("loot/setPieceWeight", Loot.SetPieceWeight);
            Number("loot/setCompletionWeight", Loot.SetCompletionWeight);
            for (int heat = 0; heat <= Content.MaxHeat; heat++)
                foreach (bool doubled in new[] { false, true })
                {
                    var p = new Profile { Run = new RunState { Heat = heat, SatchelShards = 100 } };
                    if (doubled) p.Run.Pacts.Add(Pact.CursedHoard);
                    Rules.Secure(p);
                    Number("economy/secure/" + heat + "/" + doubled, p.Material(Materials.Shard));
                }
            return values;
        }
    }
}
