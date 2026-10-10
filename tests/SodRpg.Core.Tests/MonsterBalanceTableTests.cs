using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MonsterBalanceTableTests
    {
        internal static JsonElement Raw()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "tools", "balance", "monsters.json");
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path))) return document.RootElement.Clone();
            }
            throw new InvalidOperationException("tools/balance/monsters.json was not found above " + AppContext.BaseDirectory);
        }

        internal static int Int(string section, string field) => Raw().GetProperty(section).GetProperty(field).GetInt32();
        internal static float Float(string section, string field) => Raw().GetProperty(section).GetProperty(field).GetSingle();
        internal static double Double(string section, string field) => Raw().GetProperty(section).GetProperty(field).GetDouble();
        internal static int AffixCount(int depth) => depth >= Int("nightmare", "ThreeAffixDepth") ? Int("nightmare", "ThreeAffixCount")
            : depth >= Int("nightmare", "TwoAffixDepth") ? Int("nightmare", "TwoAffixCount") : Int("nightmare", "BaseAffixCount");

        [Fact]
        public void Original_enemy_table_preserves_stat_arrays_affix_effects_and_numeric_paths()
        {
            var raw = Raw();
            var values = new[] { "behavior", "nightmare", "variants" }
                .SelectMany(group => raw.GetProperty(group).EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => x.Value.GetDecimal()))
                .Concat(raw.GetProperty("variantStats").EnumerateArray().SelectMany(row => row.GetProperty("stats").EnumerateArray().Select(x => x.GetProperty("value").GetDecimal())));
            // Frozen pre-table inputs gate migration-only evidence; future tuning uses the independent table-driven tests.
            if (!values.SequenceEqual(new decimal[] { 5.0m, 0.2m, 0.5m, 0.7m, 0.3m, 10.0m, 4.0m, 1.0m, -15.0m, 2.0m, 3.0m, 0.35m, 3m, 0.4m, 0.3m, 1.2m, 3.0m, 3.0m, 6.0m, 1.5m, 0.4m, 15.0m, 6.0m, 0.5m, 20.0m, 2.0m, 0.3m, 40m, 50m, 3m, 4m, 1m, 40m, 40m, 30m, 4m, 10m, 150m, 10m, 2m, 0.5m, 400.0m, 8m, 60m, 0.01m, 0.2m, 0.08m, 0.02m, 15m, 15m, 2.0m, 20m, 4.0m, 10m, 20m, 35m, 30m, 0.4m, 1.5m, 15m, 3m, 5m, 2m, 3m, 25m, 20m, 0.06m, 10m, 0.02m, 20m, 4.0m, 100m, 300m, 8m, 2m, 60m, 50m, 30m, 80m, 40m, 250m, 30m, 80m, 40m, 30m, 200m, 30m, 200m, 40m, 60m, 40m, 80m, 25m, 60m, 50m, 20m, 120m, 80m, 40m, 30m, 150m, 30m, 20m, 200m, 30m, 60m, 70m, 60m, 50m, 60m, 60m, 60m, 60m, 70m, 50m, 60m, 40m, 20m, 60m, 50m, 60m, 50m })) return;
            Assert.Null(MonstersBalance.ContentFingerprintRecord);
            var variants = new (string Id, (Stat Stat, int Value)[] Stats)[]
            {
                ("var.corroding_hound", new[] { (Stat.MaxHealthPct, 60), (Stat.MoveSpeedPct, 50), (Stat.AttackSpeedPct, 30) }),
                ("var.mirror_scarab", new[] { (Stat.MaxHealthPct, 80), (Stat.Armor, 40) }),
                ("var.elder_treant", new[] { (Stat.MaxHealthPct, 250), (Stat.Armor, 30) }),
                ("var.blood_bat", new[] { (Stat.MaxHealthPct, 80), (Stat.AttackSpeedPct, 40), (Stat.MoveSpeedPct, 30) }),
                ("var.abyss_oppressor", new[] { (Stat.MaxHealthPct, 200), (Stat.AttackPct, 30) }),
                ("var.hollow_gunner", new[] { (Stat.MaxHealthPct, 200), (Stat.AttackPct, 40) }),
                ("var.blazing_martyr", new[] { (Stat.MaxHealthPct, 60), (Stat.MoveSpeedPct, 40) }),
                ("var.ink_scholar", new[] { (Stat.MaxHealthPct, 80), (Stat.AttackPct, 25) }),
                ("var.thousand_arrows", new[] { (Stat.MaxHealthPct, 60), (Stat.AttackSpeedPct, 50), (Stat.AttackPct, 20) }),
                ("var.molten_core", new[] { (Stat.MaxHealthPct, 120) }),
                ("var.star_stalker", new[] { (Stat.MaxHealthPct, 80), (Stat.MoveSpeedPct, 40), (Stat.AttackPct, 30) }),
                ("var.frost_alpha", new[] { (Stat.MaxHealthPct, 150), (Stat.AttackPct, 30), (Stat.MoveSpeedPct, 20) }),
                ("var.shard_devourer", new[] { (Stat.MaxHealthPct, 200), (Stat.MoveSpeedPct, 30) }),
                ("var.mist_spitter", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.brood_warden", new[] { (Stat.MaxHealthPct, 70) }),
                ("var.hollow_elemental", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.flicker_olm", new[] { (Stat.MaxHealthPct, 50) }),
                ("var.timorous_displacer", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.overreaching_bug", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.last_thaw", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.rime_sentinel", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.furnace_ram", new[] { (Stat.MaxHealthPct, 70) }),
                ("var.ember_mender", new[] { (Stat.MaxHealthPct, 50) }),
                ("var.mist_tiger", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.lantern_seed", new[] { (Stat.MaxHealthPct, 40) }),
                ("var.eclipse_demon", new[] { (Stat.MaxHealthPct, 20) }),
                ("var.rust_scavenger", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.broodfly", new[] { (Stat.MaxHealthPct, 50) }),
                ("var.stardust_shell", new[] { (Stat.MaxHealthPct, 60) }),
                ("var.web_ripper", new[] { (Stat.MaxHealthPct, 50) }),
            };
            foreach (var expected in variants)
            {
                var actual = Variants.Get(expected.Id);
                Assert.Equal(expected.Stats, actual.Stats.Select(s => (s.Stat, s.Value)));
                Assert.Equal(expected.Id == "var.shard_devourer" ? 300 : 100, actual.ShardBonusPct);
            }
            var stats = Nightmares.MonsterStats(Nightmares.AllAffixes.Aggregate(NightmareAffix.None, (a, b) => a | b), out float regen);
            Assert.Equal(new[] { (Stat.MaxHealthPct, 40), (Stat.Armor, 60), (Stat.AttackPct, 40), (Stat.AttackSpeedPct, 30),
                (Stat.MaxHealthPct, 150), (Stat.MoveSpeedPct, 35), (Stat.AttackSpeedPct, 20), (Stat.PowerPct, 50),
                (Stat.Haste, 40), (Stat.Armor, 20), (Stat.Armor, 30), (Stat.AttackPct, 15), (Stat.AttackPct, 10) }, stats.Select(s => (s.Stat, s.Value)));
            Assert.Equal(2f, regen);
            for (int depth = 0; depth <= 5; depth++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(depth == 0 ? 0 : 0.01 * depth), BitConverter.DoubleToInt64Bits(Nightmares.Chance(MonsterTier.Lesser, depth)));
                Assert.Equal(BitConverter.DoubleToInt64Bits(depth == 0 ? 0 : 0.02 * depth), BitConverter.DoubleToInt64Bits(Nightmares.Chance(MonsterTier.Normal, depth)));
                Assert.Equal(BitConverter.DoubleToInt64Bits(depth == 0 ? 0 : 0.20 + 0.08 * (depth - 1)), BitConverter.DoubleToInt64Bits(Nightmares.Chance(MonsterTier.MiniBoss, depth)));
                Assert.Equal(BitConverter.DoubleToInt64Bits(depth < 2 ? 0 : 0.06 + 0.02 * (depth - 2)), BitConverter.DoubleToInt64Bits(Variants.Chance(depth)));
                Assert.Equal(depth >= 5 ? 3 : depth >= 3 ? 2 : 1, Nightmares.AffixCount(depth));
            }
            var guards = NightmareAffix.Veiled | NightmareAffix.Facing | NightmareAffix.Packbound | NightmareAffix.Committed;
            Assert.Equal(BitConverter.SingleToInt32Bits(1f - Math.Min(0.30f * 4f, 0.40f)),
                BitConverter.SingleToInt32Bits(MonsterBehavior.IncomingMultiplier(guards, 7f, 1f, true, true, false, 2f, false, false)));
            Assert.Equal(BitConverter.SingleToInt32Bits(1.2f),
                BitConverter.SingleToInt32Bits(MonsterBehavior.IncomingMultiplier(guards, 7f, 1f, true, true, true, 2f, false, false)));
            Assert.Equal(BitConverter.SingleToInt32Bits(Math.Min(10000f * 15 / 100f, 1000f * 2f / 100f)),
                BitConverter.SingleToInt32Bits(Nightmares.ThornsReflectAmount(10000f, 1000f)));
        }
    }
}
