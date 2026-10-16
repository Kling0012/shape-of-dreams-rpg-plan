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
        }

        private void SaveEnemyOwnershipConfig()
        {
            if (NetworkClient.active && !NetworkServer.active) return;
            ApplyEnemyOwnershipConfig();
            try { SaveConfigsToDisk(); }
            catch (Exception ex)
            {
                Log.Warn("Enemy ownership settings save failed: " + ex.Message);
                _ui?.SetStatus(Loc.T("設定を保存できませんでした。", "Could not save settings."));
            }
        }

        private void SaveEnemyOwnershipDisplayConfig()
        {
            try { SaveConfigsToDisk(); }
            catch (Exception ex)
            {
                Log.Warn("Enemy ownership display settings save failed: " + ex.Message);
                _ui?.SetStatus(Loc.T("設定を保存できませんでした。", "Could not save settings."));
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
