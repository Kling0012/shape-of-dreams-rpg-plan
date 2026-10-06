using System;
using System.IO;
using System.Text.Json;
using SodRpg.Core.Game;

namespace SodRpg.Core.Tests
{
    internal static class EquipmentBalanceInputs
    {
        internal static JsonElement Raw(string relativePath)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, relativePath);
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path))) return document.RootElement.Clone();
            }
            throw new InvalidOperationException(relativePath + " was not found above " + AppContext.BaseDirectory);
        }

        internal static int Cap(Power power) => Raw("tools/balance/equipment/caps.json").GetProperty("power").GetProperty(power.ToString()).GetInt32();
        internal static int Cap(Stat stat) => Raw("tools/balance/equipment/caps.json").GetProperty("stat").GetProperty(stat.ToString()).GetInt32();
        internal static JsonElement Affix(Slot slot, Stat stat) => Raw("tools/balance/equipment/affixes.json").GetProperty("pools").GetProperty(slot.ToString()).GetProperty(stat.ToString());

        internal static int LevelScale(Stat stat, int level)
        {
            var gear = Raw("tools/balance/gear.json");
            int baseline = gear.GetProperty("BaseLevelScalePct").GetInt32();
            bool damage = stat == Stat.AttackFlat || stat == Stat.PowerFlat;
            bool fixedStat = damage || stat == Stat.Armor || stat == Stat.MaxHealthFlat || stat == Stat.HealthRegen || stat == Stat.Haste || stat == Stat.Tenacity;
            if (!fixedStat) return baseline;
            int cap = gear.GetProperty("ItemLevelScalingCap").GetInt32();
            int growth = gear.GetProperty(damage ? "FlatDamageGrowthPct" : "OtherFixedGrowthPct").GetInt32();
            return baseline + growth * (Math.Max(1, Math.Min(level, cap)) - 1);
        }
        internal static int Scale(int value, int percent)
        {
            decimal rounded = Math.Round(value * percent / 100m, MidpointRounding.AwayFromZero);
            if (value != 0 && rounded == 0) rounded = Math.Sign(value);
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, rounded));
        }
    }
}
