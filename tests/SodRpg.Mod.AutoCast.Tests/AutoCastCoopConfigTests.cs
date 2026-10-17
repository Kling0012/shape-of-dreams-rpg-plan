using System;
using System.IO;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.AutoCast.Tests
{
    // 設定の既定と保存・読込、tools/balance/coop.json と生成定数の一致。
    public sealed class AutoCastCoopConfigTests
    {
        [Fact]
        public void Native_cadence_setting_defaults_on()
            => Assert.True(new DreamforgeConfig().autoCastCoopNativeCadence);

        [Fact]
        public void Native_cadence_setting_survives_a_save_load_round_trip()
        {
            // 本体の設定保存は公開フィールドの JSON 化（Startup ハーネスの
            // DewPersistence と同じ形）。ここでは System.Text.Json のフィールド
            // round-trip で値が保たれることを確認する。
            var options = new JsonSerializerOptions { IncludeFields = true };
            var off = new DreamforgeConfig { autoCastCoopNativeCadence = false };
            var loadedOff = JsonSerializer.Deserialize<DreamforgeConfig>(JsonSerializer.Serialize(off, options), options);
            Assert.False(loadedOff.autoCastCoopNativeCadence);
            var on = new DreamforgeConfig { autoCastCoopNativeCadence = true };
            var loadedOn = JsonSerializer.Deserialize<DreamforgeConfig>(JsonSerializer.Serialize(on, options), options);
            Assert.True(loadedOn.autoCastCoopNativeCadence);
        }

        [Fact]
        public void Generated_auto_cast_constants_match_tools_balance_coop_json()
        {
            string root = RepoRoot();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "balance", "coop.json")));
            var values = document.RootElement;
            Assert.Equal(CoopBalance.AutoCastIntervalSeconds, values.GetProperty("autoCastIntervalSeconds").GetDouble(), 5);
            Assert.Equal(CoopBalance.AutoCastOwnHitSeconds, values.GetProperty("autoCastOwnHitSeconds").GetDouble(), 5);
            Assert.Equal(CoopBalance.AutoCastAllyHitSeconds, values.GetProperty("autoCastAllyHitSeconds").GetDouble(), 5);
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "tools", "balance", "coop.json"))) return dir.FullName;
            throw new InvalidOperationException("tools/balance/coop.json was not found above " + AppContext.BaseDirectory);
        }
    }
}
