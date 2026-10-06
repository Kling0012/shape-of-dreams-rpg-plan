using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Issue #149 段階8（前半）：BossProfiles の挙動数値を tools/balance/bosses へ移した無調整切替の回帰。
    /// 表が移行値のままである限り、コンパイル済みボス定義と指紋が移行前と完全一致することを確認する。
    /// </summary>
    public sealed class BossBalanceMigrationTests
    {
        private static readonly (string Boss, string Prefix)[] BossTables =
        {
            ("demon", "boss_demon."), ("skoll", "boss_skoll."), ("infernus", "boss_infernus."),
            ("ink", "boss_white_night."), ("nyx", "boss_nyx."),
            ("erebos", "boss_erebos."), ("seeker", "boss_seeker."), ("azurak", "boss_azurak."),
            ("primus", "boss_primus_aeron."), ("light", "boss_light_elemental."), ("maw", "boss_maw."),
            ("obliviax", "boss_obliviax."), ("polaris", "boss_polaris."),
        };

        private static readonly Dictionary<string, Func<BossAction, int>> ActionFields =
            new Dictionary<string, Func<BossAction, int>>
            {
                ["cooldownMillis"] = a => a.CooldownMillis, ["delayMillis"] = a => a.DelayMillis,
                ["lifetimeMillis"] = a => a.LifetimeMillis, ["intervalMillis"] = a => a.IntervalMillis,
                ["count"] = a => a.Count, ["maxTargets"] = a => a.MaxTargets,
                ["maxInstances"] = a => a.MaxInstances, ["mainHits"] = a => a.MainHits,
                ["counterLifetimeMillis"] = a => a.CounterLifetimeMillis,
                ["generationLimit"] = a => a.GenerationLimit,
                ["radiusMilli"] = a => a.RadiusMilli, ["rangeMilli"] = a => a.RangeMilli,
                ["widthMilli"] = a => a.WidthMilli, ["speedMilli"] = a => a.SpeedMilli,
                ["angleMilli"] = a => a.AngleMilli, ["hitGateMillis"] = a => a.HitGateMillis,
                ["magnitudeMilli"] = a => a.MagnitudeMilli, ["requiredMode"] = a => a.RequiredMode,
                ["requiredMarks"] = a => a.RequiredMarks,
            };

        private static JsonElement Table(string boss)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "tools", "balance", "bosses", boss + ".json");
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path)))
                    return document.RootElement.Clone();
            }
            throw new InvalidOperationException("tools/balance/bosses/" + boss + ".json was not found above " + AppContext.BaseDirectory);
        }

        private static string MoveId(string bossTable, string prefix, string key)
        {
            if (bossTable == "ink")
            {
                bool white = key.StartsWith("white_", StringComparison.Ordinal);
                return (white ? "boss_white_night." : "boss_dark_moon.") + key.Substring(key.IndexOf('_') + 1);
            }
            return prefix + key;
        }

        [Fact]
        public void Compiled_moves_and_channels_match_the_boss_balance_original_tables()
        {
            var moves = BossProfiles.Moves.ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var (boss, prefix) in BossTables)
            {
                var root = Table(boss);
                foreach (var profile in root.GetProperty("moves").EnumerateObject())
                {
                    string id = MoveId(boss, prefix, profile.Name);
                    Assert.True(moves.TryGetValue(id, out var compiled), "missing move profile " + id);
                    foreach (var channel in profile.Value.GetProperty("channels").EnumerateObject())
                    {
                        var cell = channel.Value;
                        var target = Assert.Single(compiled.Channels, x => x.ChannelId == channel.Name);
                        Assert.Equal(cell.GetProperty("value").GetInt32() * 1000, target.ValueMilli);
                        int expectedCap = cell.TryGetProperty("cap", out var cap) ? cap.GetInt32() : cell.GetProperty("value").GetInt32();
                        Assert.Equal(expectedCap * 1000, target.CapMilli);
                    }
                    var actions = profile.Value.GetProperty("actions");
                    Assert.Equal(actions.GetArrayLength(), compiled.Actions.Count);
                    for (int i = 0; i < actions.GetArrayLength(); i++)
                        foreach (var field in actions[i].EnumerateObject())
                        {
                            Assert.True(ActionFields.TryGetValue(field.Name, out var read), "unknown field " + field.Name);
                            Assert.Equal(field.Value.GetInt32(), read(compiled.Actions[i]));
                        }
                }
            }
        }

        [Fact]
        public void Reward_actions_match_representative_cells_of_the_original_tables()
        {
            var rewards = BossProfiles.Rewards.ToDictionary(x => x.Id, StringComparer.Ordinal);
            // 段階を問わず報酬は表の定数から構成される。ここでは構造がボスごとに異なるため代表値を照合する。
            Assert.Equal(Table("demon").GetProperty("rewards").GetProperty("plantValueMilli").GetInt32(),
                rewards["boss_demon.hysteria"].Stages[1].Actions[1].ValueMilli);
            Assert.Equal(Table("skoll").GetProperty("rewards").GetProperty("healCapMilli").GetInt32(),
                rewards["boss_skoll.glacial_core"].Stages[0].Actions[0].CapMilli);
            Assert.Equal(Table("ink").GetProperty("rewards").GetProperty("darkStage3DwellMillis").GetInt32(),
                rewards["boss_dark_moon.beam"].Stages[2].Actions[0].DwellMillis);
            Assert.Equal(Table("azurak").GetProperty("rewards").GetProperty("damageStage3BudgetMilli").GetInt32(),
                rewards["boss_azurak.burrow"].Stages[2].Actions[1].BudgetMilli);
            Assert.Equal(Table("erebos").GetProperty("rewards").GetProperty("delayDurationMillis").GetInt32(),
                rewards["boss_erebos.last_starlight"].Stages[0].Actions[0].DurationMillis);
            Assert.Equal(Table("maw").GetProperty("rewards").GetProperty("cooldownMagnitudeMilli").GetInt32(),
                rewards["boss_maw.big_chomp"].Stages[2].Actions[2].MagnitudeMilli);
        }

        [Fact]
        public void Unadjusted_migration_preserves_the_boss_fingerprint_records()
        {
            // 表が移行値から1セルでも離れたら pinned 値の照合は意味を失うため、生成済みフラグで飛ばす。
            if (!BossBalanceValues.MatchesFrozenReference) return;
            string[] records = BossProfiles.FingerprintRecords().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            ulong hash = 1469598103934665603UL; // FNV-1a 64, ContentFingerprint と同じ刻み
            foreach (string record in records)
            {
                foreach (char c in record)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
                hash ^= '\n';
                hash *= 1099511628211UL;
            }
            string actual = records.Length + "-" + hash.ToString("x16");
            Assert.Equal("591-685158953d5197ef", actual);
        }
    }
}
