using System;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        partial void ApplyEnemyOwnershipConfig()
        {
            try { _host?.ConfigureEnemyOwnership(config.coopEnemyOwnership); }
            catch (Exception ex) { Log.Warn("Enemy ownership settings ignored: " + ex.Message); }
            try { _host?.ConfigureDynamicDifficulty(config.dynamicDifficulty, config.dynamicDifficultySolo); }
            catch (Exception ex) { Log.Warn("Dynamic difficulty settings ignored: " + ex.Message); }
        }

        private void SaveEnemyOwnershipConfig()
        {
            if (NetworkClient.active && !NetworkServer.active) return;
            ApplyEnemyOwnershipConfig();
            try { SaveConfigsToDisk(); }
            catch (Exception ex)
            {
                Log.Warn("Enemy ownership settings save failed: " + ex.Message);
                _ui?.SetStatus(Loc.T("設定を保存できませんでした。", "Could not save settings.", "无法保存设置。"));
            }
        }

        private void SaveEnemyOwnershipDisplayConfig()
        {
            try { SaveConfigsToDisk(); }
            catch (Exception ex)
            {
                Log.Warn("Enemy ownership display settings save failed: " + ex.Message);
                _ui?.SetStatus(Loc.T("設定を保存できませんでした。", "Could not save settings.", "无法保存设置。"));
            }
        }

        partial void BindEnemyOwnershipSettings()
        {
            if (_ui == null) return;
            _ui.SaveEnemyOwnershipConfig = SaveEnemyOwnershipConfig;
            _ui.SaveEnemyOwnershipDisplayConfig = SaveEnemyOwnershipDisplayConfig;
        }
    }
}
