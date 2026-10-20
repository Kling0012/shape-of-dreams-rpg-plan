using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private void DrawAutoCastSettings(DreamforgeConfig cfg)
        {
            bool editable = !Mirror.NetworkClient.active || Mirror.NetworkServer.active;
            bool wasEnabled = GUI.enabled;
            GUILayout.Space(4);
            GUILayout.Label(Loc.T("自動詠唱", "Automatic casting", "自动施放"), _st.Header);
            GUI.enabled = wasEnabled && editable;
            bool enabled = GUILayout.Toggle(AutoCastCoopSettings.DisplayNativeCadence,
                Loc.T("マルチでは本体と同じ速さ（既定 ON）", "Native speed in multiplayer (default ON)", "多人时与本体速度相同（默认开启）"));
            GUI.enabled = wasEnabled;
            if (editable && enabled != cfg.autoCastCoopNativeCadence)
            {
                cfg.autoCastCoopNativeCadence = enabled;
                AutoCastCoopSettings.Configure(enabled);
                _saveLevelEssenceConfig?.Invoke();
            }
            GUILayout.Label(Loc.T("ビスマスの自動詠唱を最大毎秒5回にします。ソロの速さは変わりません。",
                "Limits Bismuth's automatic casting to 5 per second. Solo speed is unchanged.", "将Bismuth的自动施放限制为每秒最多5次。单人速度不变。"), _st.Small);
            GUILayout.Label(editable ? Loc.T("ホストの設定が全員に適用されます。変更は即反映して保存します。",
                "The host's setting applies to everyone. Changes apply and save immediately.", "主机的设置适用于所有人。更改会立即生效并保存。")
                : Loc.T("ホストの設定を表示しています（変更はホストのみ）。",
                "Showing the host's setting (only the host can change it).", "正在显示主机的设置（仅主机可更改）。"), _st.Small);
        }
    }
}
