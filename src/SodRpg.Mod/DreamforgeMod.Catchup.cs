using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        partial void ApplyCatchupConfig() => HostAuthority.CatchupEnabled = config.coopCatchup;

        partial void ObserveCatchupPatch(System.Type type, bool installed)
        {
            if (type != typeof(NativeCatchupDamage) && type != typeof(NativeDamageContext)) return;
            if (!installed || !HasInstalledClass(type))
            {
                HostAuthority.CatchupHooksAvailable = false;
                Log.Warn("Co-op catch-up unavailable: native damage hook was not installed: " + type.FullName);
            }
        }
    }

    internal sealed partial class DreamforgeUi
    {
        private void DrawCatchupSettings(DreamforgeConfig cfg)
        {
            bool editable = !NetworkClient.active || NetworkServer.active;
            bool wasEnabled = GUI.enabled;
            UnityEngine.GUILayout.Space(4);
            UnityEngine.GUILayout.Label(Loc.T("同行補正", "Co-op catch-up"), _st.Header);
            GUI.enabled = wasEnabled && editable;
            bool enabled = UnityEngine.GUILayout.Toggle(editable ? cfg.coopCatchup : _s.CatchupSettingEnabled,
                Loc.T("育ちの低い旅人を引き上げる", "Help less-developed travelers"));
            GUI.enabled = wasEnabled;
            if (editable && enabled != cfg.coopCatchup)
            {
                cfg.coopCatchup = enabled;
                HostAuthority.CatchupEnabled = enabled;
                // Reuse the existing host-only MOD config save callback (no profile schema change).
                _saveLevelEssenceConfig?.Invoke();
                InvalidateHud();
            }
            UnityEngine.GUILayout.Label(editable
                ? Loc.T("ホストの設定が全員に適用されます。補正の強さは次の部屋で更新します。",
                    "The host's setting applies to everyone. Strength updates in the next room.")
                : _s.CatchupSettingKnown
                    ? Loc.T("ホストの設定です（変更はホストのみ）。", "Host setting (only the host can change it).")
                    : Loc.T("ホストの同行補正設定は未受信です。", "Waiting for the host's catch-up setting."), _st.Small);
            string notice = _s.CatchupNotice;
            if (notice != null) UnityEngine.GUILayout.Label(notice, _st.Small);
        }
    }
}
