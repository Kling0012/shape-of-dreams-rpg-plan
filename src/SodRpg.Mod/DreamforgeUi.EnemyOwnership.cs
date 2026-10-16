using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        internal Action SaveEnemyOwnershipConfig;

        private void DrawEnemyOwnershipSettings(DreamforgeConfig cfg)
        {
            bool editable = !NetworkClient.active || NetworkServer.active;
            bool previous = GUI.enabled;
            GUILayout.Space(4);
            GUILayout.Label(Loc.T("ホスト設定", "Host settings"), _st.Header);
            GUI.enabled = previous && editable;
            bool enabled = GUILayout.Toggle(cfg.coopEnemyOwnership, Loc.T("担当制（マルチ）", "Enemy ownership (coop)"));
            GUI.enabled = previous;
            if (editable && enabled != cfg.coopEnemyOwnership)
            {
                cfg.coopEnemyOwnership = enabled;
                SaveEnemyOwnershipConfig?.Invoke();
            }
            GUILayout.Label(editable
                ? Loc.T("敵を各旅人に割り当て、担当者の育ち具合に合わせます。変更は即反映して保存します。",
                    "Assign enemies to each traveler and scale them to their progress. Changes apply and save immediately.")
                : Loc.T("変更はホストのみ。ホストの設定が使われます。",
                    "Only the host can change this. The host's setting applies."), _st.Small);
        }
    }
}
