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
            GUILayout.Label(Loc.T("自動詠唱", "Automatic casting"), _st.Header);
            GUI.enabled = wasEnabled && editable;
            bool enabled = GUILayout.Toggle(AutoCastCoopSettings.DisplayNativeCadence,
                Loc.T("マルチでは本体と同じ速さ（既定 ON）", "Native speed in multiplayer (default ON)"));
            GUI.enabled = wasEnabled;
            if (editable && enabled != cfg.autoCastCoopNativeCadence)
            {
                cfg.autoCastCoopNativeCadence = enabled;
                AutoCastCoopSettings.Configure(enabled);
                _saveLevelEssenceConfig?.Invoke();
            }
            GUILayout.Label(Loc.T("ビスマスの自動詠唱を最大毎秒5回にします。ソロの速さは変わりません。",
                "Limits Bismuth's automatic casting to 5 per second. Solo speed is unchanged."), _st.Small);
            GUILayout.Label(editable ? Loc.T("ホストの設定が全員に適用されます。変更は即反映して保存します。",
                "The host's setting applies to everyone. Changes apply and save immediately.")
                : Loc.T("ホストの設定を表示しています（変更はホストのみ）。",
                "Showing the host's setting (only the host can change it)."), _st.Small);
        }
    }
}
