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
        private LocLanguage _ownershipLegendJa;
        private bool _ownershipLegendFailed;
        private readonly string[] _legendNames = new string[4], _legendIds = new string[4];
        private readonly double[] _legendFactors = new double[4];
        private readonly string[] _statusNames = new string[4], _statusIds = new string[4];
        private readonly bool[] _statusHumans = new bool[4];
        private readonly double[] _statusFactors = new double[4];
        private DynamicDifficultyStatus _statusInputs;

        private bool RememberDynamicStatus(DynamicDifficultyStatus status)
        {
            if (status == null)
            {
                bool changed = _statusInputs != null;
                _statusInputs = null;
                return changed;
            }
            var previous = _statusInputs;
            bool same = previous != null && previous.state == status.state && previous.world == status.world
                && previous.decayPercent == status.decayPercent && previous.damageExponent == status.damageExponent
                && previous.rewardShare == status.rewardShare && previous.decayByWorld.Length == status.decayByWorld.Length;
            if (same)
                for (int i = 0; i < status.decayByWorld.Length; i++)
                    if (previous.decayByWorld[i] != status.decayByWorld[i]) { same = false; break; }
            if (same) return false;
            if (previous == null) _statusInputs = previous = new DynamicDifficultyStatus();
            previous.state = status.state; previous.world = status.world; previous.decayPercent = status.decayPercent;
            previous.damageExponent = status.damageExponent; previous.rewardShare = status.rewardShare;
            if (previous.decayByWorld == null || previous.decayByWorld.Length != status.decayByWorld.Length)
                previous.decayByWorld = new int[status.decayByWorld.Length];
            Array.Copy(status.decayByWorld, previous.decayByWorld, status.decayByWorld.Length);
            return true;
        }
        private bool _statusHost;
        private float _nextDynamicStatus;
        private DynamicDifficultyStatus _observedDynamicStatus;
        private DynamicDifficultyDisplay[] _observedDynamicDisplay;
        private string _dynamicLimits;
        private LocLanguage _dynamicLimitsJa;
        private string _dynamicGap;
        private string _dynamicStatusText;
        private LocLanguage _dynamicStatusJa;
        private bool _dynamicStatusFailed;

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
            bool changed = _ownershipLegend == null || _ownershipLegendJa != Loc.Language;
            for (int i = 0; i < OwnershipColors.Length; i++)
            {
                var player = i < DewPlayer.gamePlayers.Count ? DewPlayer.gamePlayers[i] : null;
                string name = player != null ? player.playerName : null;
                string id = player != null ? player.guid : null;
                double factor = player != null ? _s.DynamicDifficultyFor(id) : 0;
                if (_legendNames[i] != name || _legendIds[i] != id || !_legendFactors[i].Equals(factor)) changed = true;
                _legendNames[i] = name; _legendIds[i] = id; _legendFactors[i] = factor;
            }
            if (changed)
            {
                _ownershipLegendJa = Loc.Language;
                string legend = "";
                for (int i = 0; i < OwnershipColors.Length; i++)
                {
                    var player = i < DewPlayer.gamePlayers.Count ? DewPlayer.gamePlayers[i] : null;
                    string name = player != null ? player.playerName : Loc.T("未参加", "Empty", "未加入");
                    if (string.IsNullOrEmpty(name)) name = Loc.T("旅人", "Traveler", "旅人");
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
                "◆ Your enemy   ◇ Another player's enemy   No mark: unprotected (damageable by anyone)", "◆ 自己的敌人　◇ 他人的敌人　无标记：无归属保护（非归属者也可造成伤害）"), _st.Small);
        }

        private static string OwnershipName(DewPlayer player)
        {
            string name = player != null ? player.playerName : null;
            if (string.IsNullOrEmpty(name)) name = Loc.T("旅人", "Traveler", "旅人");
            name = name.Replace("<", "〈").Replace(">", "〉");
            return name.Length > 12 ? name.Substring(0, 12) + "…" : name;
        }

        private static string Times(double value) =>
            "×" + value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        // ×倍率の差を「+42%」「−24%」の形で返す。標準（四捨五入で ±0）のときは空（#700）。
        private static string SignedPercent(double multiplier)
        {
            int percent = (int)Math.Round(multiplier * 100) - 100;
            return percent == 0 ? "" : (percent > 0 ? "+" : "−") + Math.Abs(percent) + "%";
        }

        // #700: what dynamic difficulty and helper decay do right now. The host reads its own rules;
        // a participant shows only values the host synced for this run, and "—" for the rest.
        private string BuildDynamicStatus(bool host, DynamicDifficultyStatus status, DynamicDifficultyDisplay[] display)
        {
            const string none = "—";
            var text = new System.Text.StringBuilder(512);
            text.Append(Loc.T("動的難易度：", "Dynamic difficulty: ", "动态难度："));
            text.Append(status == null ? none
                : status.state == DynamicDifficultyStatus.On ? Loc.T("有効", "On", "开启")
                : status.state == DynamicDifficultyStatus.SoloOff ? Loc.T("ソロでは無効", "Off in solo", "单人时关闭")
                : Loc.T("無効", "Off", "关闭"));
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
                    text.Append("  ×").Append(none).Append('\n').Append(Loc.T("　敵の体力 ", "　Enemy HP ", "　敌人生命 ")).Append(none)
                        .Append(Loc.T(" ・ 攻撃 ", " · Attack ", " · 攻击 ")).Append(none).Append(Loc.T(" ・ 報酬 ", " · Rewards ", " · 奖励 ")).Append(none);
                    continue;
                }
                string mood = f < 0.995 ? Loc.T("やさしめ", "easier", "偏易") : f > 1.005 ? Loc.T("きびしめ", "harder", "偏难") : Loc.T("標準", "standard", "标准");
                text.Append("  ").Append(Times(f)).Append(Loc.T("（", " (", "（")).Append(mood).Append(Loc.T("）", ")", "）"));
                // 2026-10-19：動的難易度は敵の攻撃を変えない。体力と報酬は差の割合も併記する。
                // 2026-10-20：世界ごとの上限に達しているときは（上限）を添える（表示は結果の f をそのまま使う）。
                bool capped = status != null && f >= CoopBalance.DynamicDifficulty.MaxFactor(status.world) - 0.005;
                string cappedText = capped ? Loc.T("（上限）", " (capped)", "（上限）") : "";
                text.Append('\n').Append(Loc.T("　敵の体力 ", "　Enemy HP ", "　敌人生命 ")).Append(Times(f)).Append(SignedPercent(f)).Append(cappedText);
                text.Append(Loc.T(" ・ 攻撃 ", " · Attack ", " · 攻击 ")).Append(status == null ? none : Times(1));
                double rewardMultiplier = double.IsNaN(reward) ? double.NaN
                    : Math.Min(CoopBalance.DynamicDifficulty.RewardMaxMultiplier, 1 + reward * Math.Max(0, f - 1));
                text.Append(Loc.T(" ・ 報酬 ", " · Rewards ", " · 奖励 ")).Append(double.IsNaN(rewardMultiplier) ? none : Times(rewardMultiplier) + SignedPercent(rewardMultiplier));
            }
            text.Append('\n').Append(Loc.T("担当外の減衰：", "Damage to others' enemies: ", "对他人敌人的伤害衰减："));
            if (status == null) text.Append(none);
            else
            {
                // The wire carries the 0-based run order; players count worlds from 1.
                text.Append(status.world < 0 ? Loc.T("ラン外", "Outside a run", "远征之外") : Loc.T("世界", "World ", "世界") + (status.world + 1))
                    .Append(Loc.T("（減衰 ", " (", "（衰减 ")).Append(status.decayPercent).Append(Loc.T("%）", "% less)", "%）"));
            }
            text.Append('\n').Append(Loc.T("　段階：", "　Stages: ", "　阶段："));
            if (status == null) text.Append(none);
            else
            {
                var table = status.decayByWorld;
                text.Append(OwnershipHelp.StageTable(world => table[world], table.Length,
                    Loc.T("世界", "World ", "世界"), Loc.T("以降", "+", "及以后")));
            }
            return text.ToString();
        }

        private void DrawDynamicStatus()
        {
            bool host = NetworkServer.active;
            var authority = host ? HostAuthority.NativeInstance : null;
            if (_dynamicStatusText == null || _statusHost != host || Time.unscaledTime >= _nextDynamicStatus)
            {
                _nextDynamicStatus = Time.unscaledTime + 0.25f;
                _observedDynamicStatus = host ? authority?.DynamicStatusSnapshot() : _s.DynamicStatus;
                _observedDynamicDisplay = host ? authority?.HostDynamicDisplay() : null;
            }
            var status = host ? _observedDynamicStatus : _s.DynamicStatus;
            var display = _observedDynamicDisplay;
            bool changed = RememberDynamicStatus(status);
            changed |= _dynamicStatusText == null || _dynamicStatusJa != Loc.Language || _statusHost != host;
            for (int i = 0; i < OwnershipColors.Length; i++)
            {
                var player = i < DewPlayer.gamePlayers.Count ? DewPlayer.gamePlayers[i] : null;
                string name = player != null ? player.playerName : null;
                string id = player != null ? player.guid : null;
                bool human = player != null && player.isHumanPlayer;
                double factor = double.NaN;
                if (host)
                {
                    if (display != null)
                        foreach (var entry in display)
                            if (entry != null && entry.participantId == id) { factor = entry.factor / 100d; break; }
                }
                else if (id != null && _s.TryDynamicDifficultyFor(id, out double synced)) factor = synced;
                if (_statusNames[i] != name || _statusIds[i] != id || _statusHumans[i] != human
                    || !_statusFactors[i].Equals(factor)) changed = true;
                _statusNames[i] = name; _statusIds[i] = id; _statusHumans[i] = human; _statusFactors[i] = factor;
            }
            if (changed)
            {
                _dynamicStatusJa = Loc.Language; _statusHost = host;
                _dynamicStatusText = BuildDynamicStatus(host, status, display);
            }
            GUILayout.Label(Loc.T("現在の状況", "Current status", "当前状态"), _st.Label);
            GUILayout.Label(_dynamicStatusText, _st.Small);
        }

        private bool _dynamicHudFailed;

        /// <summary>左上のまとめ表示（HUD）に、自分の動的難易度を1行で足す（#700）。
        /// 値の元は状況表示と同じ（ホストは自分の規則、参加者は受け取った値）。敵の攻撃は変わらないので出さない。</summary>
        private void AppendDynamicHud(System.Text.StringBuilder sb)
        {
            var local = DewPlayer.local;
            if (local == null) return;
            bool host = NetworkServer.active;
            var authority = host ? HostAuthority.NativeInstance : null;
            var status = host ? authority?.DynamicStatusSnapshot() : _s.DynamicStatus;
            if (status == null || status.state != DynamicDifficultyStatus.On) return;
            double f = double.NaN;
            if (host)
            {
                var display = authority?.HostDynamicDisplay();
                if (display != null)
                    foreach (var entry in display)
                        if (entry != null && entry.participantId == local.guid) { f = entry.factor / 100d; break; }
            }
            else _s.TryDynamicDifficultyFor(local.guid, out f);
            if (double.IsNaN(f)) return;
            sb.Append("\n<size=13>").Append(Loc.T("動的難易度 ", "Dynamic difficulty: ", "动态难度 "));
            if (f >= 0.995 && f <= 1.005) sb.Append(Loc.T("標準", "standard", "标准"));
            else
            {
                sb.Append(Loc.T("敵の体力 ", "Enemy HP ", "敌人生命 ")).Append(SignedPercent(f));
                // 2026-10-20：世界ごとの上限に達しているときは（上限）を添える（参加者も同期された世界番号から分かる）。
                if (status != null && f >= CoopBalance.DynamicDifficulty.MaxFactor(status.world) - 0.005)
                    sb.Append(Loc.T("（上限）", " (capped)", "（上限）"));
                double rewardMultiplier = Math.Min(CoopBalance.DynamicDifficulty.RewardMaxMultiplier,
                    1 + status.rewardShare / 1000d * Math.Max(0, f - 1));
                string rewardPercent = SignedPercent(rewardMultiplier);
                if (rewardPercent.Length > 0) sb.Append(Loc.T("・報酬 ", " · Rewards ", "· 奖励 ")).Append(rewardPercent);
                if (f < 0.995) sb.Append(Loc.T("（やさしめ）", " (easier)", "（偏易）"));
            }
            sb.Append("</size>");
        }

        private void DrawEnemyOwnershipSettings(DreamforgeConfig cfg)
        {
            bool editable = !NetworkClient.active || NetworkServer.active;
            bool previous = GUI.enabled;
            GUILayout.Space(4);
            GUILayout.Label(Loc.T("ホスト設定", "Host settings", "主机设置"), _st.Header);
            GUI.enabled = previous && editable;
            bool enabled = GUILayout.Toggle(cfg.coopEnemyOwnership, Loc.T("担当制（マルチ）", "Enemy ownership (coop)", "敌人归属制（多人）"));
            GUI.enabled = previous;
            if (editable && enabled != cfg.coopEnemyOwnership)
            {
                cfg.coopEnemyOwnership = enabled;
                SaveEnemyOwnershipConfig?.Invoke();
            }
            GUILayout.Label(editable
                ? Loc.T("敵を各旅人に割り当て、担当者の育ち具合に合わせます。変更は即反映して保存します。",
                    "Assign enemies to each traveler and scale them to their progress. Changes apply and save immediately.", "将敌人分配给各旅人，并按其成长程度调整强度。更改会立即生效并保存。")
                : Loc.T("変更はホストのみ。ホストの設定が使われます。",
                    "Only the host can change this. The host's setting applies.", "仅主机可更改。将应用主机的设置。"), _st.Small);
            GUILayout.Label(Loc.T("担当制を切っても、人数による敵の強化の緩和は常に働きます。",
                "Even with ownership off, player-count enemy scaling stays softened.", "即使关闭归属制，随人数增强敌人的缓和效果也始终生效。"), _st.Small);
            GUI.enabled = previous && editable;
            bool dynamic = GUILayout.Toggle(cfg.dynamicDifficulty, Loc.T("殲滅速度に合わせた難易度", "Difficulty follows clear speed", "难度随通关速度变化"));
            bool solo = GUILayout.Toggle(cfg.dynamicDifficultySolo, Loc.T("ソロでも使う", "Also use in solo", "单人时也启用"));
            GUI.enabled = previous;
            if (editable && (dynamic != cfg.dynamicDifficulty || solo != cfg.dynamicDifficultySolo))
            {
                cfg.dynamicDifficulty = dynamic;
                cfg.dynamicDifficultySolo = solo;
                SaveEnemyOwnershipConfig?.Invoke();
            }
            if (_dynamicLimits == null || _dynamicLimitsJa != Loc.Language)
            {
                _dynamicLimitsJa = Loc.Language;
                // 2026-10-20：上がるときと下がるときで埋め方の強さが違う。上限は世界ごとの段階。
                int dynamicClose = (int)Math.Round(CoopBalance.DynamicDifficulty.StrengthUp * 100);
                int dynamicRelief = (int)Math.Round(CoopBalance.DynamicDifficulty.StrengthDown * 100);
                _dynamicGap = Loc.T($"戦った部屋のダメージから、次の部屋の敵を調整します。強くなるときは差の{dynamicClose}%、楽になるときは{dynamicRelief}%を埋めます。",
                    $"Use combat damage to adjust enemies in the next room, closing {dynamicClose}% of the strength gap upward and {dynamicRelief}% downward.",
                    $"根据战斗房间的伤害调整下一个房间的敌人。增强时弥补强度差距的{dynamicClose}%，变简单时弥补{dynamicRelief}%。");
                string dynamicMin = CoopBalance.DynamicDifficulty.MinFactor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                string rewardMax = CoopBalance.DynamicDifficulty.RewardMaxMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                var capStages = new System.Text.StringBuilder();
                for (int i = 0; i < CoopBalance.DynamicDifficulty.MaxFactorWorlds; i++)
                {
                    if (i > 0) capStages.Append(Loc.T(" ・ ", " · ", " ・ "));
                    if (i == 0) capStages.Append(Loc.T("世界", "World ", "世界"));
                    capStages.Append(i + 1);
                    if (i == CoopBalance.DynamicDifficulty.MaxFactorWorlds - 1) capStages.Append(Loc.T("以降", "+", "及以后"));
                    capStages.Append(" ×").Append(CoopBalance.DynamicDifficulty.MaxFactor(i)
                        .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
                _dynamicLimits = Loc.T($"係数は{dynamicMin}以上で、上限は世界ごとに変わります（{capStages}）。硬くなった分だけ報酬が増え（最大×{rewardMax}）、易しくなっても報酬は減りません。",
                    $"Factor starts at {dynamicMin} and is capped per world ({capStages}). Tougher fights add rewards (up to ×{rewardMax}); easier ones never cut them.",
                    $"系数下限为{dynamicMin}，上限随世界变化（{capStages}）。变强会增加奖励（最高×{rewardMax}），变简单也不会减少奖励。");
            }
            GUILayout.Label(_dynamicGap, _st.Small);
            GUILayout.Label(_dynamicLimits, _st.Small);
            GUILayout.Label(Loc.T("協力プレイでは担当制と一緒に有効になります。",
                "In co-op it runs together with enemy ownership.",
                "协作游玩时与敌人负责制一同生效。"), _st.Small);
            if (!_ownershipLegendFailed)
            {
                try { DrawOwnershipLegend(); }
                catch (Exception ex)
                {
                    _ownershipLegendFailed = true;
                    Log.Warn("Enemy ownership legend disabled; settings continue: " + ex.Message);
                }
            }
            bool show = GUILayout.Toggle(cfg.showEnemyOwnershipMarks, Loc.T("担当の印を表示", "Show enemy ownership marks", "显示敌人归属标记"));
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
