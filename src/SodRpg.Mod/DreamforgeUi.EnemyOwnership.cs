using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        internal Action SaveEnemyOwnershipConfig;
        internal Action SaveEnemyOwnershipDisplayConfig;
        private static readonly string[] OwnershipColors = { "#56B4E9", "#E69F00", "#009E73", "#CC79A7" };
        private readonly Label3D[] _ownershipMarks = new Label3D[8];
        private string _ownershipLegend;
        private bool _ownershipLegendJa, _ownershipLegendFailed;
        private float _nextOwnershipLegend;

        private Label3D OwnershipMark(uint netId)
        {
            if (!_cfg().showEnemyOwnershipMarks || !_s.EnemyOwners.TryGetValue(netId, out var state)
                || !state.Protected || state.PlayerSlot < 1 || state.PlayerSlot > 4) return null;
            bool own = DewPlayer.local != null && state.ParticipantId == DewPlayer.local.guid;
            int index = (state.PlayerSlot - 1) * 2 + (own ? 0 : 1);
            var label = _ownershipMarks[index];
            if (label != null) return label;
            // Number and filled/outline shape supplement the color for color-vision differences.
            string text = UiStyles.Colored((own ? "◆" : "◇") + state.PlayerSlot, OwnershipColors[state.PlayerSlot - 1]);
            var content = new GUIContent(text);
            return _ownershipMarks[index] = new Label3D { Text = text, Content = content, Size = _st.ToastMeasure.CalcSize(content) };
        }

        private void DrawOwnershipLegend()
        {
            if (_ownershipLegend == null || Time.unscaledTime >= _nextOwnershipLegend || _ownershipLegendJa != Loc.Japanese)
            {
                _ownershipLegendJa = Loc.Japanese;
                _nextOwnershipLegend = Time.unscaledTime + 0.25f;
                string legend = "";
                for (int i = 0; i < OwnershipColors.Length; i++)
                {
                    var player = i < DewPlayer.gamePlayers.Count ? DewPlayer.gamePlayers[i] : null;
                    string name = player != null ? player.playerName : Loc.T("未参加", "Empty");
                    if (string.IsNullOrEmpty(name)) name = Loc.T("旅人", "Traveler");
                    name = name.Replace("<", "〈").Replace(">", "〉");
                    if (name.Length > 12) name = name.Substring(0, 12) + "…";
                    if (i > 0) legend += "    ";
                    legend += UiStyles.Colored("◆" + (i + 1) + " " + name, OwnershipColors[i]);
                }
                _ownershipLegend = legend;
            }
            GUILayout.Label(_ownershipLegend, _st.Label);
            GUILayout.Label(Loc.T("◆ 自分の担当　◇ 他の人の担当　印なし：共有（保護なし）",
                "◆ Your enemy   ◇ Another player's enemy   No mark: shared (unprotected)"), _st.Small);
        }

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
            if (!_ownershipLegendFailed)
            {
                try { DrawOwnershipLegend(); }
                catch (Exception ex)
                {
                    _ownershipLegendFailed = true;
                    Log.Warn("Enemy ownership legend disabled; settings continue: " + ex.Message);
                }
            }
            bool show = GUILayout.Toggle(cfg.showEnemyOwnershipMarks, Loc.T("担当の印を表示", "Show enemy ownership marks"));
            if (show != cfg.showEnemyOwnershipMarks)
            {
                cfg.showEnemyOwnershipMarks = show;
                SaveEnemyOwnershipDisplayConfig?.Invoke();
            }
        }
    }
}
