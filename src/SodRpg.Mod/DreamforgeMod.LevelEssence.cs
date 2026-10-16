using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        private static bool _legacyLevelStagesWarned;

        public override void LoadConfigsToDisk()
        {
            try
            {
                string path = GetModConfigFilePath(typeof(DreamforgeMod).GetField(nameof(config), BindingFlags.Public | BindingFlags.Instance));
                if (!File.Exists(path)) { base.LoadConfigsToDisk(); return; }
                var json = JObject.Parse(File.ReadAllText(path));
                // 古い段の型や値が壊れていても、デシリアライズには渡さない。
                if (json.Remove("levelEssenceStages") && !_legacyLevelStagesWarned)
                {
                    _legacyLevelStagesWarned = true;
                    Log.Warn("Level essence slots: obsolete levelEssenceStages ignored; use levelEssenceEveryLevels.");
                }
                if (json["levelEssenceCap"] == null && int.TryParse(json["levelEssenceMaxSlots"]?.ToString(), out int oldCap))
                    json["levelEssenceCap"] = LevelEssenceSettings.ClampCeiling(oldCap);
                json.Remove("levelEssenceMaxSlots");
                config = DewPersistence.FromJson<DreamforgeConfig>(json.ToString()) ?? new DreamforgeConfig();
            }
            catch (Exception ex) { Log.Warn("MOD config could not be loaded; using defaults: " + ex.Message); }
        }

        private static void LevelEssencePatchUnavailable(Type type)
        {
            if (type == typeof(LevelEssenceHudGroups)) LevelEssenceRuntime.DisableHud("HUD patch installation failed");
            if (type == typeof(LevelEssenceChaosReplacement))
            {
                LevelEssenceSettings.RuntimeBonusAvailable = false;
                Log.Warn("Level essence bonus disabled: native Chaos ownership hook unavailable. Other MOD features remain active.");
            }
        }

        private void SaveLevelEssenceConfig()
        {
            if (NetworkClient.active && !NetworkServer.active) return;
            config.levelEssenceEveryLevels = LevelEssenceSettings.ClampEveryLevels(config.levelEssenceEveryLevels);
            config.levelEssenceCap = LevelEssenceSettings.ClampCeiling(config.levelEssenceCap);
            ApplyLevelEssenceConfig();
            ApplyAutoCastCoopConfig();
            try { SaveConfigsToDisk(); }
            catch (Exception ex)
            {
                Log.Warn("Level essence settings save failed: " + ex.Message);
                _ui?.SetStatus(Loc.T("設定を保存できませんでした。", "Could not save settings."));
            }
        }
    }
}
