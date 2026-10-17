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
        private string _dynamicStatusText;
        private bool _dynamicStatusJa, _dynamicStatusFailed;
        private float _nextDynamicStatus;

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
                    string factor = player != null ? " <size=11>×" + _s.DynamicDifficultyFor(player.guid)
                        .ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "</size>" : "";
                    legend += UiStyles.Colored("◆" + (i + 1) + " " + name + factor, OwnershipColors[i]);
                }
                _ownershipLegend = legend;
            }
            GUILayout.Label(_ownershipLegend, _st.Label);
            GUILayout.Label(Loc.T("◆ 自分の担当　◇ 他の人の担当　印なし：保護なし（担当外でも削れる）",
                "◆ Your enemy   ◇ Another player's enemy   No mark: unprotected (damageable by anyone)"), _st.Small);
        }

        private static string OwnershipName(DewPlayer player)
        {
            string name = player != null ? player.playerName : null;
            if (string.IsNullOrEmpty(name)) name = Loc.T("旅人", "Traveler");
            name = name.Replace("<", "〈").Replace(">", "〉");
            return name.Length > 12 ? name.Substring(0, 12) + "…" : name;
        }

        private static string Times(double value) =>
            "×" + value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        // #700: what dynamic difficulty and helper decay do right now. The host reads its own rules;
        // a participant shows only values the host synced for this run, and "—" for the rest.
        private string BuildDynamicStatus()
        {
            const string none = "—";
            bool host = NetworkServer.active;
            var authority = host ? HostAuthority.NativeInstance : null;
            var status = host ? authority?.DynamicStatusSnapshot() : _s.DynamicStatus;
            var display = host ? authority?.HostDynamicDisplay() : null;
            var text = new System.Text.StringBuilder(512);
            text.Append(Loc.T("動的難易度：", "Dynamic difficulty: "));
            text.Append(status == null ? none
                : status.state == DynamicDifficultyStatus.On ? Loc.T("有効", "On")
                : status.state == DynamicDifficultyStatus.SoloOff ? Loc.T("ソロでは無効", "Off in solo")
                : Loc.T("無効", "Off"));
            double exponent = status != null ? status.damageExponent / 1000d : double.NaN;
            double reward = status != null ? status.rewardShare / 1000d : double.NaN;
            for (int i = 0; i < OwnershipColors.Length && i < DewPlayer.gamePlayers.Count; i++)
            {
                var player = DewPlayer.gamePlayers[i];
                if (player == null || !player.isHumanPlayer) continue;
                double f = double.NaN;
                if (host)
                {
                    if (display != null)
                        foreach (var entry in display)
                            if (entry != null && entry.participantId == player.guid) { f = entry.factor / 100d; break; }
                }
                else if (_s.TryDynamicDifficultyFor(player.guid, out double synced)) f = synced;
                text.Append('\n').Append(UiStyles.Colored("◆" + (i + 1) + " " + OwnershipName(player), OwnershipColors[i]));
                if (double.IsNaN(f))
                {
                    text.Append("  ×").Append(none).Append('\n').Append(Loc.T("　敵の体力 ", "　Enemy HP ")).Append(none)
                        .Append(Loc.T(" ・ 攻撃 ", " · Attack ")).Append(none).Append(Loc.T(" ・ 報酬 ", " · Rewards ")).Append(none);
                    continue;
                }
                string mood = f < 0.995 ? Loc.T("やさしめ", "easier") : f > 1.005 ? Loc.T("きびしめ", "harder") : Loc.T("標準", "standard");
                text.Append("  ").Append(Times(f)).Append(Loc.T("（", " (")).Append(mood).Append(Loc.T("）", ")"));
                text.Append('\n').Append(Loc.T("　敵の体力 ", "　Enemy HP ")).Append(Times(f));
                text.Append(Loc.T(" ・ 攻撃 ", " · Attack ")).Append(double.IsNaN(exponent) ? none : Times(Math.Pow(f, exponent)));
                text.Append(Loc.T(" ・ 報酬 ", " · Rewards "))
                    .Append(double.IsNaN(reward) ? none : Times(Math.Min(CoopBalance.DynamicDifficulty.RewardMaxMultiplier, 1 + reward * Math.Max(0, f - 1))));
            }
            text.Append('\n').Append(Loc.T("担当外の減衰：", "Damage to others' enemies: "));
            if (status == null) text.Append(none);
            else
            {
                // The wire carries the 0-based run order; players count worlds from 1.
                text.Append(status.world < 0 ? Loc.T("ラン外", "Outside a run") : Loc.T("世界", "World ") + (status.world + 1))
                    .Append(Loc.T("（減衰 ", " (")).Append(status.decayPercent).Append(Loc.T("%）", "% less)"));
            }
            text.Append('\n').Append(Loc.T("　段階：", "　Stages: "));
            if (status == null) text.Append(none);
            else
            {
                var table = status.decayByWorld;
                text.Append(OwnershipHelp.StageTable(world => table[world], table.Length,
                    Loc.T("世界", "World "), Loc.T("以降", "+")));
            }
            return text.ToString();
        }

        private void DrawDynamicStatus()
        {
            if (_dynamicStatusText == null || Time.unscaledTime >= _nextDynamicStatus || _dynamicStatusJa != Loc.Japanese)
            {
                _dynamicStatusJa = Loc.Japanese;
                _nextDynamicStatus = Time.unscaledTime + 0.25f;
                _dynamicStatusText = BuildDynamicStatus();
            }
            GUILayout.Label(Loc.T("現在の状況", "Current status"), _st.Label);
            GUILayout.Label(_dynamicStatusText, _st.Small);
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
            GUILayout.Label(Loc.T("担当制を切っても、人数による敵の強化の緩和は常に働きます。",
                "Even with ownership off, player-count enemy scaling stays softened."), _st.Small);
            GUI.enabled = previous && editable;
            bool dynamic = GUILayout.Toggle(cfg.dynamicDifficulty, Loc.T("殲滅速度に合わせた難易度", "Difficulty follows clear speed"));
            bool solo = GUILayout.Toggle(cfg.dynamicDifficultySolo, Loc.T("ソロでも使う", "Also use in solo"));
            GUI.enabled = previous;
            if (editable && (dynamic != cfg.dynamicDifficulty || solo != cfg.dynamicDifficultySolo))
            {
                cfg.dynamicDifficulty = dynamic;
                cfg.dynamicDifficultySolo = solo;
                SaveEnemyOwnershipConfig?.Invoke();
            }
            int dynamicClose = (int)Math.Round(CoopBalance.DynamicDifficulty.Strength * 100);
            GUILayout.Label(Loc.T($"戦った部屋のダメージから、次の部屋の敵を調整します。強さの差の{dynamicClose}%だけを埋めます。",
                $"Use combat damage to adjust enemies in the next room, closing {dynamicClose}% of the strength gap."), _st.Small);
            string dynamicMin = CoopBalance.DynamicDifficulty.MinFactor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string rewardMax = CoopBalance.DynamicDifficulty.RewardMaxMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            GUILayout.Label(Loc.T($"係数は{dynamicMin}以上で上限はありません。硬くなった分だけ報酬が増え（最大×{rewardMax}）、易しくなっても報酬は減りません。",
                $"Factor starts at {dynamicMin} with no upper limit. Tougher fights add rewards (up to ×{rewardMax}); easier ones never cut them."), _st.Small);
            GUILayout.Label(Loc.T("協力プレイでは担当制と一緒に有効になります。",
                "In co-op it runs together with enemy ownership."), _st.Small);
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
            if (!_dynamicStatusFailed)
            {
                try { DrawDynamicStatus(); }
                catch (Exception ex)
                {
                    _dynamicStatusFailed = true;
                    Log.Warn("Dynamic difficulty status disabled; settings continue: " + ex.Message);
                }
            }
            GUILayout.Label(KillAssistHelp(), _st.Small);
        }
    }
}
