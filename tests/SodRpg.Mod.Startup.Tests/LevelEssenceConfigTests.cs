using System;
using System.IO;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // 旧設定（levelEssenceStages / levelEssenceMaxSlots）が残った設定ファイルを読んでも落ちず、
    // 旧 MaxSlots を新しい levelEssenceCap に引き継ぐこと（DreamforgeMod.LevelEssence.cs の実物を対象）。
    public sealed class LevelEssenceConfigTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "dreamforge-level-essence-config-" + Guid.NewGuid().ToString("N"));
        private readonly DreamforgeMod _mod = new DreamforgeMod();

        public LevelEssenceConfigTests()
        {
            Directory.CreateDirectory(_dir);
            Log.Errors.Clear();
            Log.Warnings.Clear();
            Log.Infos.Clear();
            // 旧段の警告は1度しか出さない。試験の順番に左右されないように毎回戻す。
            typeof(DreamforgeMod).GetField("_legacyLevelStagesWarned",
                BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, false);
        }

        private string _configPath;

        private void WriteConfig(string json)
        {
            _configPath = Path.Combine(_dir, "config.json");
            File.WriteAllText(_configPath, json);
            _mod.configFilePath = _ => _configPath;
        }

        [Fact]
        public void Legacy_stages_are_ignored_and_legacy_max_slots_carry_into_the_cap()
        {
            WriteConfig("{\"levelEssenceStages\":\"10:1, 20:2\",\"levelEssenceMaxSlots\":6}");
            _mod.LoadConfigsToDisk();
            Assert.Equal(6, _mod.config.levelEssenceCap);       // 旧 MaxSlots を cap に引き継ぐ
            Assert.Equal(10, _mod.config.levelEssenceEveryLevels); // 旧設定に N はなく、既定の10
            Assert.Empty(Log.Errors);
            Assert.Contains(Log.Warnings, m => m.Contains("levelEssenceStages"));
        }

        [Fact]
        public void A_carried_legacy_cap_is_clamped_and_a_present_new_cap_wins()
        {
            WriteConfig("{\"levelEssenceMaxSlots\":99}");
            _mod.LoadConfigsToDisk();
            Assert.Equal(8, _mod.config.levelEssenceCap); // 4〜8に収める

            // 旧段が壊れた型でも読み込みは落ちず、新capがあれば新capを優先する。
            WriteConfig("{\"levelEssenceStages\":[1,2,3],\"levelEssenceMaxSlots\":99,\"levelEssenceCap\":5}");
            _mod.LoadConfigsToDisk();
            Assert.Equal(5, _mod.config.levelEssenceCap);
            Assert.Empty(Log.Errors);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
