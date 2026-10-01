using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Line = SodRpg.Core.Game.Line;
    using Power = SodRpg.Core.Game.Power;
    using Rarity = SodRpg.Core.Game.Rarity;
    using Slot = SodRpg.Core.Game.Slot;
    using Stat = SodRpg.Core.Game.Stat;

    /// <summary>夢鍛メニュー（装備・鍛冶・星図・記録）、HUD、確保地点のパネル、通知の描画。</summary>
    internal sealed class DreamforgeUi
    {
        private sealed class Toast
        {
            public string Text;
            public float Until;
            public GUIContent Content;
            public float W, H;
        }

        private sealed class Label3D
        {
            public string Text;
            public GUIContent Content;
            public Vector2 Size;
        }

        // HUD は文字列を0.25秒ごとに作り直し、1枚のラベルとして描く（GUILayout を使わない）。
        private string _hudText;
        private GUIContent _hudContent;
        private float _hudHeight;
        private float _nextHudRebuild;
        private readonly Dictionary<int, Label3D> _nightmareLabelCache = new Dictionary<int, Label3D>();

        /// <summary>GUILayout を使うパネル（メニュー・確保地点・結果）が出ているか。出ていなければレイアウト処理自体を止める。</summary>
        private readonly List<Hint> _hints = new List<Hint>();

        public bool NeedsLayout
        {
            get
            {
                if (Open || _hints.Count > 0) return true;
                var p = _s.Profile;
                if (p.LastReport != null && (p.LastReport != _shownReport || !_reportDismissed)) return true;
                return _s.InGame && p.Run != null && p.Run.AwaitingChoice && _s.ActiveRunId != null;
            }
        }

        /// <summary>今フレーム、レイアウト処理が有効か（Update で設定）。無効なら GUILayout を呼ばない。</summary>
        public bool LayoutEnabled { get; set; }

        /// <summary>状態が変わったので HUD を作り直す。</summary>
        public void InvalidateHud() => _nextHudRebuild = 0;

        private static readonly string[] KnownHeroes =
        {
            "Hero_Lacerta", "Hero_Mist", "Hero_Aurena", "Hero_Bismuth", "Hero_Vesper", "Hero_Yubar", "Hero_Nachia", "Hero_Husk", "Hero_Cetus",
        };

        private readonly ClientSession _s;
        private readonly Func<DreamforgeConfig> _cfg;
        private readonly UiStyles _st = new UiStyles();
        private readonly List<Toast> _toasts = new List<Toast>();

        private int _tab;
        private Slot _slot = Slot.Weapon;
        private string _selected;
        private string _heroSel;
        private Vector2 _scrollList, _scrollDetail, _scrollRecords;
        private int _retuneIndex = -1;
        private string _confirmSalvage;
        private bool _forgeAllSlots = true;
        private string _status;
        private float _statusUntil;

        public bool Open { get; private set; }

        /// <summary>直近の描画で、マウスが確保地点のパネルの上にあったか（クリックをゲームへ通さないため）。</summary>
        public bool MouseOverPanel { get; private set; }

        public DreamforgeUi(ClientSession session, Func<DreamforgeConfig> cfg)
        {
            _s = session;
            _cfg = cfg;
        }

        public void Toggle()
        {
            Open = !Open;
            _confirmSalvage = null;
        }

        public void Close() => Open = false;

        public void Dispose() => _st.Dispose();

        public void Notify(GameEvent e)
        {
            if (e == null) return;
            InvalidateHud();
            if (e.Kind == EventKind.Hint)
            {
                if (e.HintId.HasValue && !_hints.Contains(e.HintId.Value)) _hints.Add(e.HintId.Value);
                return;
            }
            if (e.Kind != EventKind.Drop && e.Kind != EventKind.Bounty) Log.Info(e.Text);
            if (!_cfg().showToasts && e.Kind == EventKind.Drop && e.Rarity.HasValue && e.Rarity.Value < Rarity.Rare) return;
            string text = e.Rarity.HasValue ? UiStyles.Colored(e.Text, UiStyles.RarityHex(e.Rarity.Value)) : Decorate(e);
            _toasts.Add(new Toast { Text = text, Until = Time.unscaledTime + (e.Kind == EventKind.Drop ? 6f : 8f) });
            while (_toasts.Count > 7) _toasts.RemoveAt(0);
        }

        private static string Decorate(GameEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.LevelUp: return UiStyles.Colored(e.Text, "#ffe17a");
                case EventKind.Secured: return UiStyles.Colored(e.Text, "#7af0c8");
                case EventKind.Delved: return UiStyles.Colored(e.Text, "#ff8a5c");
                case EventKind.Recovered: return UiStyles.Colored(e.Text, "#7af0c8");
                case EventKind.Bounty: return UiStyles.Colored(e.Text, "#ffd36e");
                case EventKind.Lost:
                case EventKind.Warning: return UiStyles.Colored(e.Text, "#ff7070");
                default: return e.Text;
            }
        }

        private void SetStatus(string text)
        {
            _status = text;
            _statusUntil = Time.unscaledTime + 5f;
        }

        private string HeroKey
        {
            get
            {
                var h = _s.LocalHero;
                if (h != null) return ClientSession.HeroKeyOf(h);
                if (_heroSel == null) _heroSel = _s.Profile.Heroes.Keys.FirstOrDefault(k => k.StartsWith("Hero_")) ?? KnownHeroes[0];
                return _heroSel;
            }
        }

        private static string HeroName(string key) => key != null && key.StartsWith("Hero_") ? key.Substring(5) : key;

        // ─────────────────────────── 描画の入口 ───────────────────────────

        public void Draw()
        {
            _st.EnsureBuilt();
            var cfg = _cfg();
            float scale = Mathf.Clamp(Screen.height / 1080f * Mathf.Clamp(cfg.uiScale, 0.5f, 2.5f), 0.5f, 4f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;
            var type = Event.current.type;
            bool repaint = type == EventType.Repaint;
            if (repaint) MouseOverPanel = false;
            bool layout = LayoutEnabled;
            try
            {
                var zm = NetworkedManagerBase<ZoneManager>.softInstance;
                bool transition = zm != null && zm.isInAnyTransition;
                if (_s.InGame && !transition)
                {
                    if (repaint)
                    {
                        DrawNightmareLabels(scale);
                        if (cfg.hudMode != HudMode.Off) DrawHud(w, h, cfg);
                    }
                    if (layout && _s.Profile.Run != null && _s.Profile.Run.AwaitingChoice && _s.ActiveRunId != null) DrawSecurePrompt(w, h, cfg);
                }
                if (repaint) DrawToasts(w, h);
                if (layout && _s.Profile.LastReport != null && (_s.Profile.LastReport != _shownReport || !_reportDismissed)) DrawReport(w, h);
                if (layout && Open) DrawWindow(w, h, cfg);
                if (layout && _hints.Count > 0) DrawHint(w, h, cfg);
            }
            catch (Exception ex)
            {
                Log.Error("UI: " + ex);
                Open = false;
            }
            finally
            {
                GUI.matrix = oldMatrix;
            }
        }

        private void DrawHud(float w, float h, DreamforgeConfig cfg)
        {
            float now = Time.unscaledTime;
            if (_hudText == null || now >= _nextHudRebuild)
            {
                _nextHudRebuild = now + 0.25f;
                string text = BuildHudText(cfg);
                if (text != _hudText)
                {
                    _hudText = text;
                    _hudContent = new GUIContent(text);
                    _hudHeight = _st.Hud.CalcHeight(_hudContent, 290);
                }
            }
            GUI.Label(new Rect(10, h * 0.30f, 290, _hudHeight), _hudContent, _st.Hud);
        }

        private readonly System.Text.StringBuilder _hudSb = new System.Text.StringBuilder(512);

        private string BuildHudText(DreamforgeConfig cfg)
        {
            var p = _s.Profile;
            var run = p.Run;
            var sb = _hudSb;
            sb.Length = 0;
            int need = Content.XpToNext(p.DreamLevel);
            string xp = p.DreamLevel >= Content.MaxDreamLevel ? "MAX" : (p.DreamXp * 100 / Math.Max(1, need)) + "%";
            sb.Append(Loc.T("<b>夢鍛</b>  夢のレベル ", "<b>Dreamforge</b>  Dream Lv ")).Append(p.DreamLevel).Append("  <color=#aaaacc>(").Append(xp).Append(")</color>");
            bool compact = cfg.hudMode == HudMode.Compact;
            if (run != null && _s.ActiveRunId != null)
            {
                var daily = DailyDream.Get(run.DailyId);
                if (!compact && (daily != null || run.LimboDepth > 0))
                {
                    sb.Append("\n<size=13>");
                    if (daily != null) sb.Append("<color=#a8d8ff>").Append(Loc.T("今日の夢：", "Today: ")).Append(daily.Name).Append("</color>");
                    if (run.LimboDepth > 0) sb.Append("<color=#d0a0ff>").Append(Loc.T("　Limbo ", "  Limbo ")).Append(run.LimboDepth).Append("</color>");
                    sb.Append("</size>");
                }
                string heatColor = run.Heat == 0 ? "#9aa0b8" : run.Heat < 3 ? "#ffb070" : "#ff5a4a";
                sb.Append('\n').Append(Loc.T("潜行 ", "Delve ")).Append("<color=").Append(heatColor).Append('>')
                    .Append('●', run.Heat).Append('○', Content.MaxHeat - run.Heat).Append("</color>");
                sb.Append("\n<size=13>").Append(Loc.T("未確保：遺物", "Unsecured: ")).Append(run.Satchel.Count)
                    .Append(Loc.T("  欠片", " relics  ")).Append(run.SatchelShards)
                    .Append(Loc.T("  調律石", " shards  ")).Append(run.SatchelTuning).Append(Loc.T("", " tuning")).Append("</size>");
                if (!compact && run.Pacts.Count > 0)
                {
                    sb.Append("\n<size=13><color=#ff9a7a>").Append(Loc.T("契約：", "Pacts: "));
                    for (int i = 0; i < run.Pacts.Count; i++)
                    {
                        if (i > 0) sb.Append("・");
                        sb.Append(Pacts.Get(run.Pacts[i])?.Name);
                    }
                    sb.Append("</color></size>");
                }
                if (!compact) foreach (var b in run.Bounties)
                {
                    sb.Append("\n<size=13>");
                    if (b.Done) sb.Append("<color=#7af0c8>●</color><color=#888>").Append(b.Describe()).Append("</color>");
                    else sb.Append("○").Append(b.Describe()).Append(" <color=#aaaacc>").Append(b.Progress).Append('/').Append(b.Target).Append("</color>");
                    sb.Append("</size>");
                }
                if (!_s.HostConfirmed && _s.LocalHero != null)
                    sb.Append("\n<size=13>").Append(Loc.T("能力の反映待ち（ホスト未導入？）", "Waiting for host (host has no mod?)")).Append("</size>");
            }
            sb.Append("\n<size=13><color=#aaaacc>[").Append(cfg.menuKey).Append(Loc.T("] メニュー", "] Menu")).Append("</color></size>");
            return sb.ToString();
        }

        private void DrawSecurePrompt(float w, float h, DreamforgeConfig cfg)
        {
            var run = _s.Profile.Run;
            var rect = new Rect(w / 2 - 300, 80, 600, 260 + (run.OfferedPacts.Count > 0 ? 34 + 40 * run.OfferedPacts.Count : 0) + (run.OfferedEvent != DreamEvent.None ? 76 : 0));
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.Label(Loc.T("確保地点", "Secure Point"), _st.Title);
            int bonus = run.SatchelShards * run.Heat / 4;
            GUILayout.Label(Loc.T(
                $"未確保：遺物{run.Satchel.Count}個・欠片{run.SatchelShards}（確保で潜行ボーナス+{bonus}）・調律石{run.SatchelTuning}",
                $"Unsecured: {run.Satchel.Count} relics, {run.SatchelShards} shards (+{bonus} delve bonus), {run.SatchelTuning} tuning"), _st.Label);
            int next = Math.Min(Content.MaxHeat, run.Heat + 1);
            GUILayout.Label(UiStyles.Colored(Loc.T(
                $"潜行{next}：敵の{(int)(Nightmares.Chance(MonsterTier.Normal, next) * 100)}%・エリートの{(int)(Nightmares.Chance(MonsterTier.MiniBoss, next) * 100)}%が悪夢化（本体のエリート化＋悪夢の効果。倒せばエリート〜ボス級の戦利品）",
                $"Depth {next}: {(int)(Nightmares.Chance(MonsterTier.Normal, next) * 100)}% of enemies and {(int)(Nightmares.Chance(MonsterTier.MiniBoss, next) * 100)}% of elites become nightmares (elite-to-boss loot)"), "#ff9ae0"), _st.Small);
            GUILayout.Label(Loc.T(
                $"深く潜る → 潜行{next}：ドロップ率+{(int)(Loot.HeatDropBonus * 100 * next)}%・レア度上昇／被ダメージ+{Build.DamageTakenPerDelvePct * next}%。全滅すると未確保品は遺失物に。",
                $"Delve -> level {next}: +{(int)(Loot.HeatDropBonus * 100 * next)}% drops, better rarity / +{Build.DamageTakenPerDelvePct * next}% damage taken. Unsecured loot is lost on defeat."), _st.Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T($"確保する [{cfg.secureKey}]", $"Secure [{cfg.secureKey}]"), _st.Button, GUILayout.Height(34))) _s.Secure();
            if (GUILayout.Button(Loc.T($"深く潜る [{cfg.delveKey}]", $"Delve [{cfg.delveKey}]"), _st.Button, GUILayout.Height(34))) _s.Delve();
            if (GUILayout.Button(Loc.T($"装備を整える [{cfg.menuKey}]", $"Gear up [{cfg.menuKey}]"), _st.Button, GUILayout.Height(34)))
            {
                Open = true;
                _tab = 0;
            }
            GUILayout.EndHorizontal();
            {
                int dust = _s.LocalDust;
                GUI.enabled = dust >= Economy.DustPerBatch && !_s.TradePending(TradeKind.DustToShards);
                int batches = Math.Min(dust / Economy.DustPerBatch, 10);
                if (GUILayout.Button(Loc.T(
                        $"ドリームダストを欠片に換える（{Economy.DustPerBatch}→{Economy.ShardsPerBatch}、所持{dust}" + (batches > 0 ? $"、{batches * Economy.DustPerBatch}→{batches * Economy.ShardsPerBatch}" : "") + "）",
                        $"Convert Dream Dust to shards ({Economy.DustPerBatch}->{Economy.ShardsPerBatch}, you have {dust})"), _st.Button, GUILayout.Height(30)))
                {
                    string err = _s.ConvertDust();
                    if (err != null) SetStatus(err);
                }
                GUI.enabled = true;
            }
            if (run.OfferedEvent != DreamEvent.None)
            {
                var e = run.OfferedEvent;
                bool merchant = e == DreamEvent.Merchant;
                bool ok = DreamEvents.CanUse(_s.Profile, e, merchant, out string why);
                if (merchant && ok && _s.LocalGold < _s.MerchantPrice()) { ok = false; why = Loc.T($"ゴールドが足りません（{_s.MerchantPrice()}G）", $"Not enough gold ({_s.MerchantPrice()}G)"); }
                GUILayout.Label(UiStyles.Colored(Loc.T("出来事：", "Event: ") + DreamEvents.Name(e), "#9fe0ff") + "  <color=#aab>" + DreamEvents.Describe(e, _s.Profile) + "</color>", _st.Small);
                GUI.enabled = ok;
                string label = !ok ? why : merchant ? Loc.T($"買う（{_s.MerchantPrice()}G）", $"Buy ({_s.MerchantPrice()}G)") : Loc.T("この出来事を選ぶ", "Take this event");
                if (GUILayout.Button(label, _st.Row, GUILayout.Height(32)))
                {
                    if (merchant)
                    {
                        string err = _s.BuyFromMerchant();
                        if (err != null) SetStatus(err);
                    }
                    else
                    {
                        try
                        {
                            foreach (var x in Rules.UseEvent(_s.Profile, e)) _s.Emit(x);
                            _s.MarkDirty(false);
                            _s.SaveNow();
                        }
                        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
                    }
                }
                GUI.enabled = true;
            }
            if (run.OfferedPacts.Count > 0)
            {
                GUILayout.Label(Loc.T("…または悪夢の契約を結んで潜る（次の確保まで重なって効く）", "...or delve with a nightmare pact (stacks until you secure)"), _st.Small);
                foreach (var id in run.OfferedPacts.ToList())
                {
                    var d = Pacts.Get(id);
                    if (d == null) continue;
                    if (GUILayout.Button($"<b>{d.Name}</b>  <color=#ffb0a0>{d.Description}</color>", _st.Row, GUILayout.Height(36))) _s.Delve(id);
                }
            }
            GUILayout.EndArea();
        }

        private readonly List<uint> _labelScratch = new List<uint>();

        /// <summary>悪夢化した敵の頭上に名札を出す。</summary>
        private void DrawNightmareLabels(float scale)
        {
            if (_s.Nightmare.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            _labelScratch.Clear();
            foreach (var kv in _s.Nightmare)
            {
                if (!Mirror.NetworkClient.spawned.TryGetValue(kv.Key, out var id) || id == null)
                {
                    // まだスポーンしていない可能性がある。10秒たっても現れなければ消す。
                    if (!_s.NightmareSeenAt.TryGetValue(kv.Key, out float seen) || Time.unscaledTime - seen > 10f) _labelScratch.Add(kv.Key);
                    continue;
                }
                var m = id.GetComponent<Monster>();
                if (m == null || !m.isActive)
                {
                    _labelScratch.Add(kv.Key);
                    continue;
                }
                var sp = cam.WorldToScreenPoint(m.position + Vector3.up * 3.2f);
                if (sp.z <= 0) continue;
                if (!_nightmareLabelCache.TryGetValue((int)kv.Value, out var lab))
                {
                    string text = UiStyles.Colored(Nightmares.Label(kv.Value), "#ff6ad5");
                    var content = new GUIContent(text);
                    lab = new Label3D { Text = text, Content = content, Size = _st.ToastMeasure.CalcSize(content) };
                    _nightmareLabelCache[(int)kv.Value] = lab;
                }
                float x = sp.x / scale - lab.Size.x / 2, y = (Screen.height - sp.y) / scale - lab.Size.y;
                GUI.Label(new Rect(x, y, lab.Size.x + 4, lab.Size.y), lab.Content, _st.Toast);
            }
            foreach (var k in _labelScratch)
            {
                _s.Nightmare.Remove(k);
                _s.NightmareSeenAt.Remove(k);
            }
        }

        private bool _reportDismissed;
        private RunReport _shownReport;

        private void DrawReport(float w, float h)
        {
            var r = _s.Profile.LastReport;
            if (r != _shownReport)
            {
                _shownReport = r;
                _reportDismissed = false;
            }
            var rect = new Rect(w / 2 - 250, h * 0.16f, 500, 215);
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.Label(r.Victory ? Loc.T("遠征の結果：踏破", "Expedition: Conquered") : Loc.T("遠征の結果：夢から覚めた", "Expedition: Awakened"), _st.Title);
            GUILayout.Label(Loc.T(
                $"撃破 {r.Kills}　遺物 {r.RelicsFound}個を発見\n確保 {r.RelicsSecured}個（確保{r.SecuredCount}回・欠片{r.ShardsSecured}）\n" +
                (r.RelicsLost > 0 || r.EchoShards > 0 ? $"<color=#ff8080>遺失 {r.RelicsLost}個</color>　残響の欠片 {r.EchoShards}\n" : "") +
                $"最高潜行 {r.PeakHeat}　依頼 {r.BountiesDone}/{r.BountiesTotal}　夢のレベル {r.LevelBefore} → {r.LevelAfter}",
                $"Kills {r.Kills}   Relics found {r.RelicsFound}\nSecured {r.RelicsSecured} ({r.SecuredCount} secures, {r.ShardsSecured} shards)\n" +
                (r.RelicsLost > 0 || r.EchoShards > 0 ? $"<color=#ff8080>Lost {r.RelicsLost}</color>   Echo shards {r.EchoShards}\n" : "") +
                $"Peak delve {r.PeakHeat}   Bounties {r.BountiesDone}/{r.BountiesTotal}   Dream Level {r.LevelBefore} -> {r.LevelAfter}"), _st.Label);
            if (r.RelicsLost > 0)
                GUILayout.Label(Loc.T("失った遺物は、次の遠征で戦闘部屋を3つ突破すると1つ取り戻せます。", "Clear 3 combat rooms next expedition to recover one lost relic."), _st.Small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("閉じる", "Close"), _st.Button, GUILayout.Height(30))) _reportDismissed = true;
            GUILayout.EndArea();
        }

        /// <summary>初めて触る人向けのヒント（1つずつ）。ようこそは画面中央に大きく。</summary>
        private void DrawHint(float w, float h, DreamforgeConfig cfg)
        {
            var id = _hints[0];
            var def = Onboarding.Get(id);
            if (def == null)
            {
                _hints.RemoveAt(0);
                return;
            }
            bool welcome = id == Hint.Welcome;
            float pw = welcome ? 640 : 520;
            float ph = welcome ? 330 : 210;
            var rect = welcome ? new Rect((w - pw) / 2, (h - ph) / 2, pw, ph) : new Rect((w - pw) / 2, h - ph - 150, pw, ph);
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.Label(UiStyles.Colored((welcome ? "" : Loc.T("ヒント：", "Tip: ")) + def.Title, "#ffe17a"), welcome ? _st.Title : _st.Header);
            GUILayout.Label(def.Body.ToString().Replace("[F6]", "[" + cfg.menuKey + "]").Replace("[F7]", "[" + cfg.secureKey + "]").Replace("[F8]", "[" + cfg.delveKey + "]"), _st.Label);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            string next = _hints.Count > 1 ? Loc.T($"了解（あと{_hints.Count - 1}）", $"Got it ({_hints.Count - 1} more)") : Loc.T("了解", "Got it");
            if (GUILayout.Button(next, _st.ButtonSel, GUILayout.Height(32))) _hints.RemoveAt(0);
            if (welcome && GUILayout.Button(Loc.T($"メニューを開く [{cfg.menuKey}]", $"Open menu [{cfg.menuKey}]"), _st.Button, GUILayout.Height(32)))
            {
                _hints.RemoveAt(0);
                Open = true;
                _tab = 0;
            }
            if (GUILayout.Button(Loc.T("今後ヒントを出さない", "Turn tips off"), _st.Button, GUILayout.Height(32)))
            {
                _s.Profile.HintsOff = true;
                _hints.Clear();
                _s.MarkDirty(false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawToasts(float w, float h)
        {
            float now = Time.unscaledTime;
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (_toasts[i].Until < now) _toasts.RemoveAt(i);
            float y = h * 0.30f;
            foreach (var t in _toasts)
            {
                if (t.Content == null)
                {
                    t.Content = new GUIContent(t.Text);
                    t.W = Mathf.Min(_st.ToastMeasure.CalcSize(t.Content).x + 6, 560);
                    t.H = _st.Toast.CalcHeight(t.Content, t.W);
                }
                GUI.Label(new Rect(w - t.W - 16, y, t.W, t.H), t.Content, _st.Toast);
                y += t.H + 4;
            }
        }

        // ─────────────────────────── メニュー ───────────────────────────

        private void DrawWindow(float w, float h, DreamforgeConfig cfg)
        {
            float ww = Mathf.Min(1060, w - 20), wh = Mathf.Min(660, h - 20);
            var rect = new Rect((w - ww) / 2, (h - wh) / 2, ww, wh);
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("夢鍛 ─ 夢の遺物", "Dreamforge ─ Relics of the Dream"), _st.Title, GUILayout.Width(330));
            string[] tabs = { Loc.T("装備", "Gear"), Loc.T("鍛冶", "Forge"), Loc.T("星図", "Star Map"), Loc.T("工房", "Workshop"), Loc.T("記録", "Records") };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], i == _tab ? _st.TabSel : _st.Tab)) { _tab = i; _confirmSalvage = null; _retuneIndex = -1; }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T($"閉じる [{cfg.menuKey}]", $"Close [{cfg.menuKey}]"), _st.Button)) Open = false;
            GUILayout.EndHorizontal();

            var p = _s.Profile;
            GUILayout.Label(Loc.T(
                $"欠片 {p.Material(Materials.Shard)}　調律石 {p.Material(Materials.Tuning)}　保管庫 {p.Stash.Count}/{Workshop.StashCapacity(p)}　キャラ：{HeroName(HeroKey)}",
                $"Shards {p.Material(Materials.Shard)}   Tuning {p.Material(Materials.Tuning)}   Stash {p.Stash.Count}/{Workshop.StashCapacity(p)}   Traveler: {HeroName(HeroKey)}"), _st.Small);

            switch (_tab)
            {
                case 0: DrawGearTab(); break;
                case 1: DrawForgeTab(); break;
                case 2: DrawTalentTab(); break;
                case 3: DrawWorkshopTab(); break;
                default: DrawRecordsTab(cfg); break;
            }
            if (_status != null && Time.unscaledTime < _statusUntil) GUILayout.Label(_status, _st.Warn);
            GUILayout.EndArea();
        }

        private void FocusPicker()
        {
            var p = _s.Profile;
            GUILayout.Label(Loc.T("狙い系統（その系統の遺物が2倍出やすい）", "Focus (relics of this line drop twice as often)"), _st.Small);
            GUILayout.BeginHorizontal();
            GUI.enabled = p.Run == null;
            if (GUILayout.Button(Loc.T("なし", "None"), p.Focus == null ? _st.ButtonSel : _st.Button)) SetFocus(null);
            foreach (Line l in Enum.GetValues(typeof(Line)))
                if (GUILayout.Button(Content.LineName(l).ToString(), p.Focus == l ? _st.ButtonSel : _st.Button)) SetFocus(l);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void SetFocus(Line? l)
        {
            try
            {
                Rules.SetFocus(_s.Profile, l);
                _s.MarkDirty(false);
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void HeroPicker()
        {
            if (_s.LocalHero != null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("キャラ：", "Traveler:"), _st.Small, GUILayout.Width(60));
            var keys = KnownHeroes.Union(_s.Profile.Heroes.Keys.Where(k => k != "default")).ToList();
            int idx = Math.Max(0, keys.IndexOf(HeroKey));
            if (GUILayout.Button("<", _st.Button, GUILayout.Width(30))) _heroSel = keys[(idx - 1 + keys.Count) % keys.Count];
            GUILayout.Label("<b>" + HeroName(HeroKey) + "</b>", _st.Label, GUILayout.Width(110));
            if (GUILayout.Button(">", _st.Button, GUILayout.Width(30))) _heroSel = keys[(idx + 1) % keys.Count];
            GUILayout.EndHorizontal();
        }

        private void DrawGearTab()
        {
            var p = _s.Profile;
            string hero = HeroKey;
            GUILayout.BeginHorizontal();

            // 左：装着中とビルド
            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(320));
            HeroPicker();
            {
                int kills = p.Hero(hero).Kills;
                int lv = Mastery.Level(kills);
                int next = Mastery.ToNext(kills);
                GUILayout.Label(Loc.T(
                    $"熟練度 {lv}「{Mastery.Title(lv)}」" + (next > 0 ? $" <color=#888>次まで{next}体</color>" : ""),
                    $"Mastery {lv} \"{Mastery.Title(lv)}\"" + (next > 0 ? $" <color=#888>{next} kills to next</color>" : "")), _st.Small);
            }
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                var r = Rules.EquippedRelic(p, hero, slot);
                string label = Content.SlotName(slot) + "： " + (r != null ? UiStyles.RelicTitle(r) : Loc.T("<color=#777>（なし）</color>", "<color=#777>(empty)</color>"));
                if (GUILayout.Button(label, _slot == slot ? _st.RowSel : _st.Row, GUILayout.Height(30)))
                {
                    _slot = slot;
                    _selected = r?.Uid;
                }
            }
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("現在の強さ", "Current build"), _st.Header);
            var build = _s.CurrentBuild(hero);
            _scrollDetail = GUILayout.BeginScrollView(_scrollDetail, GUILayout.Height(300));
            if (build.Stats.Count == 0 && build.Powers.Count == 0) GUILayout.Label(Loc.T("まだ補正はありません。", "No bonuses yet."), _st.Small);
            foreach (var kv in build.Stats) if (kv.Value != 0) GUILayout.Label(Content.FormatStat(kv.Key, kv.Value), _st.Small);
            foreach (var kv in build.Powers) GUILayout.Label(UiStyles.Colored(Content.FormatPower(kv.Key, kv.Value), "#e0b0ff"), _st.Small);
            foreach (var kv in build.Sets)
            {
                var set = Content.GetSet(kv.Key);
                if (set == null) continue;
                GUILayout.Label(UiStyles.Colored($"《{set.Name}》 {kv.Value}/3  ", "#ffb52e") + "<color=#aab>" + set.Describe() + "</color>", _st.Small);
            }
            foreach (var kv in build.Lines)
            {
                if (kv.Value < 2) continue;
                string bonus = string.Join("・", Content.SetBonus(kv.Key, kv.Value).Select(x => Content.FormatStat(x.Stat, x.Value)));
                GUILayout.Label(UiStyles.Colored(Loc.T($"〈{Content.LineName(kv.Key)}×{kv.Value}〉{bonus}", $"<{Content.LineName(kv.Key)} x{kv.Value}> {bonus}"), "#9fe0c0"), _st.Small);
            }
            GUILayout.EndScrollView();
            if (!_s.CanEditLoadout)
                GUILayout.Label(Loc.T("遠征中は確保地点でのみ装備を変更できます。", "During an expedition, gear can only be changed at secure points."), _st.Warn);
            GUILayout.Label(Loc.T("同じ系統を2つ・3つ揃えるとセット効果。", "2 or 3 relics of one line grant a set bonus."), _st.Small);
            FocusPicker();
            GUILayout.EndVertical();

            // 中：保管庫
            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(330));
            GUILayout.BeginHorizontal();
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
                if (GUILayout.Button(Content.SlotName(slot).ToString(), _slot == slot ? _st.ButtonSel : _st.Button)) _slot = slot;
            GUILayout.EndHorizontal();
            RelicList(p.Stash.Where(r => r.Slot == _slot), hero, 470);
            GUILayout.EndVertical();

            // 右：詳細と比較
            GUILayout.BeginVertical(_st.Panel);
            var sel = p.FindStash(_selected);
            if (sel == null)
            {
                GUILayout.Label(Loc.T("遺物を選ぶと詳細と比較が出ます。", "Select a relic to see details and comparison."), _st.Small);
            }
            else
            {
                RelicDetail(sel);
                var cur = Rules.EquippedRelic(p, hero, sel.Slot);
                if (cur != null && cur.Uid != sel.Uid) Comparison(sel, cur);
                GUILayout.FlexibleSpace();
                GUI.enabled = _s.CanEditLoadout;
                GUILayout.BeginHorizontal();
                bool equipped = cur != null && cur.Uid == sel.Uid;
                if (!equipped && GUILayout.Button(Loc.T("装着する", "Equip"), _st.Button, GUILayout.Height(32)))
                {
                    Rules.Equip(p, hero, sel.Uid);
                    _s.MarkDirty(true);
                }
                if (equipped && GUILayout.Button(Loc.T("外す", "Unequip"), _st.Button, GUILayout.Height(32)))
                {
                    Rules.Unequip(p, hero, sel.Slot);
                    _s.MarkDirty(true);
                }
                GUI.enabled = true;
                if (GUILayout.Button(sel.Locked ? Loc.T("鍵を外す", "Unlock") : Loc.T("鍵をかける", "Lock"), _st.Button, GUILayout.Height(32)))
                {
                    Rules.ToggleLock(p, sel.Uid);
                    _s.MarkDirty(false);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void RelicList(IEnumerable<Relic> relics, string hero, float height)
        {
            var list = relics.OrderByDescending(r => r.Score).ToList();
            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.Height(height));
            if (list.Count == 0) GUILayout.Label(Loc.T("（空）遠征で敵を倒すと遺物が手に入ります。", "(empty) Defeat enemies on expeditions to find relics."), _st.Small);
            var h = _s.Profile.Hero(hero);
            foreach (var r in list)
            {
                string mark = h.Equipped.Contains(r.Uid) ? "<color=#ffe17a>★</color> " : "";
                string lck = r.Locked ? " <color=#aaa>[鍵]</color>" : "";
                string text = $"{mark}{UiStyles.RelicTitle(r)} <color=#9a9ab0>Lv{r.ItemLevel}</color>{lck}";
                if (GUILayout.Button(text, _selected == r.Uid ? _st.RowSel : _st.Row, GUILayout.Height(28)))
                {
                    _selected = r.Uid;
                    _retuneIndex = -1;
                    _confirmSalvage = null;
                }
            }
            GUILayout.EndScrollView();
        }

        private void RelicDetail(Relic r)
        {
            GUILayout.Label("<size=19><b>" + UiStyles.RelicTitle(r) + "</b></size>", _st.Label);
            GUILayout.Label($"{Content.RarityName(r.Rarity)} · {Content.SlotName(r.Slot)} · {Content.LineName(r.Base.Line)} · Lv{r.ItemLevel}"
                + (r.Retunes > 0 ? Loc.T($" · 再調律{r.Retunes}/{Content.MaxRetunes}", $" · retuned {r.Retunes}/{Content.MaxRetunes}") : ""), _st.Small);
            var imp = r.Implicit;
            GUILayout.Label(UiStyles.Colored(Content.FormatStat(imp.Stat, imp.Value), "#c8c8ff") + Loc.T("  <color=#888>（基礎）</color>", "  <color=#888>(base)</color>"), _st.Label);
            foreach (var a in r.EffectiveStats().Skip(1)) GUILayout.Label(Content.FormatStat(a.Stat, a.Value), _st.Label);
            foreach (var pw in r.EffectivePowers()) GUILayout.Label(UiStyles.Colored(Content.FormatPower(pw.Power, pw.Value), "#e0b0ff"), _st.Label);
            if (r.UniqueId != null && Content.TryGetUnique(r.UniqueId, out var u))
            {
                if (u.SetId != null && Content.GetSet(u.SetId) is SetDef set)
                    GUILayout.Label(UiStyles.Colored($"《{set.Name}》", "#ffb52e") + " <color=#aab>" + set.Describe() + "</color>", _st.Small);
                else
                    GUILayout.Label("<i>" + UiStyles.Colored(u.Lore.ToString(), "#c9a86a") + "</i>", _st.Small);
            }
        }

        private void Comparison(Relic sel, Relic cur)
        {
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("装着中との差", "Versus equipped"), _st.Header);
            var a = new Dictionary<Stat, int>();
            foreach (var s in sel.EffectiveStats()) { a.TryGetValue(s.Stat, out int v); a[s.Stat] = v + s.Value; }
            foreach (var s in cur.EffectiveStats()) { a.TryGetValue(s.Stat, out int v); a[s.Stat] = v - s.Value; }
            foreach (var kv in a.OrderBy(k => k.Key))
            {
                if (kv.Value == 0) continue;
                GUILayout.Label(UiStyles.Colored(Content.FormatStat(kv.Key, kv.Value), kv.Value > 0 ? "#7cf07c" : "#ff7a7a"), _st.Small);
            }
            var gained = sel.Powers.Select(x => x.Power).Except(cur.Powers.Select(x => x.Power));
            var lost = cur.Powers.Select(x => x.Power).Except(sel.Powers.Select(x => x.Power));
            foreach (var g in gained) GUILayout.Label(UiStyles.Colored("+ " + Content.PowerName(g), "#7cf07c"), _st.Small);
            foreach (var l in lost) GUILayout.Label(UiStyles.Colored("- " + Content.PowerName(l), "#ff7a7a"), _st.Small);
        }

        private void DrawForgeTab()
        {
            var p = _s.Profile;
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(360));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("すべて", "All"), _forgeAllSlots ? _st.ButtonSel : _st.Button)) _forgeAllSlots = true;
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
                if (GUILayout.Button(Content.SlotName(slot).ToString(), !_forgeAllSlots && _slot == slot ? _st.ButtonSel : _st.Button))
                {
                    _forgeAllSlots = false;
                    _slot = slot;
                }
            GUILayout.EndHorizontal();
            RelicList(_forgeAllSlots ? p.Stash : p.Stash.Where(r => r.Slot == _slot), HeroKey, 500);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_st.Panel);
            var sel = p.FindStash(_selected);
            if (sel != null)
            {
                RelicDetail(sel);
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                if (sel.Enhance < Content.MaxEnhance)
                {
                    if (GUILayout.Button(Loc.T($"強化 +{sel.Enhance + 1}（欠片{Content.EnhanceCost(sel.Enhance)}）", $"Enhance +{sel.Enhance + 1} ({Content.EnhanceCost(sel.Enhance)} shards)"), _st.Button, GUILayout.Height(32)))
                        Act(() => Rules.Enhance(p, sel.Uid), true);
                }
                else GUILayout.Label(Loc.T("強化は最大です", "Fully enhanced"), _st.Small);
                string sv = _confirmSalvage == sel.Uid
                    ? Loc.T("<color=#ff8080>本当に分解？</color>", "<color=#ff8080>Really salvage?</color>")
                    : Loc.T($"分解（欠片{Rules.SalvageValue(sel)}）", $"Salvage ({Rules.SalvageValue(sel)} shards)");
                if (GUILayout.Button(sv, _st.Button, GUILayout.Height(32)))
                {
                    if (_confirmSalvage == sel.Uid)
                    {
                        Act(() => Rules.Salvage(p, sel.Uid), true);
                        _selected = null;
                        _confirmSalvage = null;
                    }
                    else _confirmSalvage = sel.Uid;
                }
                GUILayout.EndHorizontal();

                if (sel.Retunes < Content.MaxRetunes && sel.Affixes.Count > 0)
                {
                    GUILayout.Label(Loc.T($"再調律：特性を1つ選んで引き直す（調律石{Content.RetuneCost(sel.Retunes)}）", $"Retune: reroll one affix ({Content.RetuneCost(sel.Retunes)} tuning)"), _st.Small);
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < sel.Affixes.Count; i++)
                    {
                        var a = sel.Affixes[i];
                        if (GUILayout.Button(Content.FormatStat(a.Stat, a.Value), _retuneIndex == i ? _st.ButtonSel : _st.Button)) _retuneIndex = i;
                    }
                    GUILayout.EndHorizontal();
                    GUI.enabled = _retuneIndex >= 0;
                    if (GUILayout.Button(Loc.T("再調律する", "Retune"), _st.Button, GUILayout.Height(30)))
                    {
                        int idx = _retuneIndex;
                        Act(() => Rules.Retune(p, sel.Uid, idx), true);
                        _retuneIndex = -1;
                    }
                    GUI.enabled = true;
                }
            }
            else
            {
                GUILayout.Label(Loc.T("左の一覧から遺物を選ぶと、強化・再調律・分解ができます。", "Pick a relic on the left to enhance, retune or salvage it."), _st.Small);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("合成（鍵なし・未装着の同じレア度3つ → 1つ上のレア度）", "Transmute (3 unlocked, unequipped of a rarity -> 1 of the next)"), _st.Header);
            GUILayout.BeginHorizontal();
            foreach (Rarity r in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic })
            {
                int n = Rules.TransmuteCandidates(p, r).Count;
                GUI.enabled = n >= 3 && p.Material(Materials.Shard) >= Rules.TransmuteCost(r);
                string label = UiStyles.Colored(Content.RarityName(r).ToString(), UiStyles.RarityHex(r)) + $" {n}/3\n" + Loc.T($"欠片{Rules.TransmuteCost(r)}", $"{Rules.TransmuteCost(r)} shards");
                if (GUILayout.Button(label, _st.Button, GUILayout.Height(44))) Act(() => Rules.Transmute(p, r), false);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("製作（到達した最高アイテムレベルで作る）", "Craft (at your highest reached item level)"), _st.Header);
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Content.SlotName(slot).ToString(), _st.Label, GUILayout.Width(80));
                if (GUILayout.Button(Loc.T($"通常：アンコモン以上（欠片{Rules.CraftShardCost(false)}）", $"Basic: Uncommon+ ({Rules.CraftShardCost(false)} shards)"), _st.Button))
                    Act(() => Rules.Craft(p, slot, false), false);
                if (GUILayout.Button(Loc.T($"上等：レア以上（欠片{Rules.CraftShardCost(true)}・調律石{Rules.CraftTuningCost(true)}）", $"Fine: Rare+ ({Rules.CraftShardCost(true)} shards, {Rules.CraftTuningCost(true)} tuning)"), _st.Button))
                    Act(() => Rules.Craft(p, slot, true), false);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void Act(Func<GameEvent> action, bool affectsBuild)
        {
            try
            {
                var e = action();
                _s.Emit(e);
                _s.MarkDirty(affectsBuild);
                _s.SaveNow();
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void DrawTalentTab()
        {
            var p = _s.Profile;
            string hero = HeroKey;
            var hs = p.Hero(hero);
            GUILayout.BeginHorizontal();
            HeroPicker();
            GUILayout.Label(Loc.T(
                $"専門化ポイント：残り {Rules.FreePoints(p, hero)} / {p.TalentPoints}（夢のレベル＋図鑑ボーナス{p.CodexBonusPoints}・キャラごとに配分）",
                $"Points: {Rules.FreePoints(p, hero)} free / {p.TalentPoints} (Dream Level + codex bonus {p.CodexBonusPoints}, allotted per Traveler)"), _st.Label);
            GUILayout.FlexibleSpace();
            GUI.enabled = _s.CanEditTalents;
            if (GUILayout.Button(Loc.T("振り直し（無料）", "Respec (free)"), _st.Button))
            {
                Rules.ResetTalents(p, hero);
                _s.MarkDirty(true);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!_s.CanEditTalents) GUILayout.Label(Loc.T("星図は遠征の外でのみ変更できます。", "The star map can only be changed outside expeditions."), _st.Warn);

            GUILayout.BeginHorizontal();
            if (HeroSigils.HasTree(hero))
            {
                GUILayout.BeginVertical(_st.Panel, GUILayout.Width(520));
                GUILayout.Label(Loc.T($"旅人の刻印：{HeroName(hero)}", $"Traveler sigils: {HeroName(hero)}"), _st.Header);
                foreach (var t in HeroSigils.TreeFor(hero)) DrawTalentNode(p, hero, hs, t);
                GUILayout.EndVertical();
                GUILayout.BeginVertical(_st.Panel);
                int lv = Mastery.Level(hs.Kills);
                GUILayout.Label(Loc.T("この旅人のキットに効く刻印です。", "These sigils work with this Traveler's own kit."), _st.Small);
                GUILayout.Label(Loc.T(
                    $"到達刻印：ツリーに{Content.KeystoneRouteRequirement}pt以上＋熟練度{HeroSigils.KeystoneMastery}以上（現在 {lv}「{Mastery.Title(lv)}」）",
                    $"Keystone: {Content.KeystoneRouteRequirement}+ points in the tree and mastery {HeroSigils.KeystoneMastery}+ (now {lv} \"{Mastery.Title(lv)}\")"), _st.Small);
                GUILayout.EndVertical();
            }
            else
            {
                foreach (Line route in Enum.GetValues(typeof(Line)))
                {
                    GUILayout.BeginVertical(_st.Panel, GUILayout.Width(330));
                    GUILayout.Label($"{Content.LineName(route)}  <color=#aaa>({Rules.RouteRanks(hs, route)})</color>", _st.Header);
                    foreach (var t in Content.Talents.Where(x => x.Route == route)) DrawTalentNode(p, hero, hs, t);
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawTalentNode(Profile p, string hero, HeroState hs, TalentDef t)
        {
            if (t.IsKeystone)
            {
                GUILayout.Space(6);
                bool active = hs.Keystone == t.Id;
                bool unlocked = Rules.KeystoneUnlocked(p, hero, t);
                GUILayout.Label((active ? "<color=#ffe17a>◆</color> " : "◇ ") + "<b>" + t.Name + "</b>" + Loc.T("（到達刻印）", " (Keystone)"), _st.Label);
                GUILayout.Label(t.Description.ToString(), _st.Small);
                GUI.enabled = _s.CanEditTalents && (active || unlocked);
                string btn = active ? Loc.T("刻印を外す", "Remove") : unlocked
                    ? Loc.T($"刻印する（{Content.KeystoneCost}pt）", $"Engrave ({Content.KeystoneCost}pt)")
                    : Loc.T("条件を満たすと解放", "Locked");
                if (GUILayout.Button(btn, active ? _st.ButtonSel : _st.Button))
                {
                    try
                    {
                        Rules.SetKeystone(p, hero, active ? null : t.Id);
                        _s.MarkDirty(true);
                    }
                    catch (InvalidOperationException ex) { SetStatus(ex.Message); }
                }
                GUI.enabled = true;
                return;
            }
            int rank = hs.Talents.TryGetValue(t.Id, out int rk) ? rk : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{t.Name}</b> {rank}/{t.MaxRank}\n<color=#aab>{Content.FormatStat(t.Stat, t.PerRank)} /{Loc.T("段", "rank")}</color>", _st.Small, GUILayout.Width(230));
            GUI.enabled = _s.CanEditTalents && rank < t.MaxRank && Rules.FreePoints(p, hero) > 0;
            if (GUILayout.Button("+", _st.Button, GUILayout.Width(44), GUILayout.Height(34)))
            {
                try
                {
                    Rules.AddTalentRank(p, hero, t.Id);
                    _s.MarkDirty(true);
                }
                catch (InvalidOperationException ex) { SetStatus(ex.Message); }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawWorkshopTab()
        {
            var p = _s.Profile;
            GUILayout.Label(Loc.T("夢の工房：余った欠片と調律石で、アカウント共通の恒久強化を解放する（遠征の外でのみ）。",
                "Dream Workshop: spend spare shards and tuning stones on permanent account-wide upgrades (outside expeditions)."), _st.Label);
            GUILayout.BeginVertical(_st.Panel);
            foreach (var def in Workshop.All)
            {
                int lv = Workshop.Level(p, def.Id);
                GUILayout.BeginHorizontal();
                string pips = new string('●', lv) + new string('○', def.MaxLevel - lv);
                GUILayout.Label($"<b>{def.Name}</b>  <color=#ffd36e>{pips}</color>\n<color=#aab>{def.Description}</color>", _st.Small, GUILayout.Width(560));
                if (lv < def.MaxLevel)
                {
                    var cost = def.Costs[lv];
                    bool afford = p.Material(Materials.Shard) >= cost.Shards && p.Material(Materials.Tuning) >= cost.Tuning;
                    GUI.enabled = afford && p.Run == null;
                    string label = Loc.T($"解放（欠片{cost.Shards}" + (cost.Tuning > 0 ? $"・調律石{cost.Tuning}" : "") + "）",
                        $"Unlock ({cost.Shards} shards" + (cost.Tuning > 0 ? $", {cost.Tuning} tuning" : "") + ")");
                    if (GUILayout.Button(label, _st.Button, GUILayout.Width(300), GUILayout.Height(40))) Act(() => Rules.BuyUpgrade(p, def.Id), false);
                    GUI.enabled = true;
                }
                else GUILayout.Label(Loc.T("完了", "Maxed"), _st.Header, GUILayout.Width(300));
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.EndVertical();
            if (p.Run != null) GUILayout.Label(Loc.T("遠征中は工房を使えません。", "The workshop is closed during expeditions."), _st.Warn);
        }

        private void DrawRecordsTab(DreamforgeConfig cfg)
        {
            var p = _s.Profile;
            _scrollRecords = GUILayout.BeginScrollView(_scrollRecords);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(480));
            GUILayout.Label(Loc.T("遊び方", "How to play"), _st.Header);
            GUILayout.Label(Loc.T(
                "・敵を倒すと、各プレイヤーに個別の「遺物」（装備）が落ちる。拾った物はまず<b>未確保</b>。\n" +
                "・新しいゾーンに着くたびに<b>確保地点</b>。「確保」で保管庫へ。「深く潜る」と潜行が上がり、ドロップ率とレア度・敵の悪夢化（本体のエリート化）が増える代わりに被ダメージも増える。潜行が深いほど確保時の欠片ボーナスも増える。本体の Limbo 深度も遺物のドロップを増やす。\n" +
                "・全滅すると未確保の遺物は<b>遺失物</b>に。次の遠征で戦闘部屋を3つ突破すると、最良の1つを取り戻せる。\n" +
                "・装備は遠征の外か確保地点で変更できる。主装備・防具・装飾品の3枠。\n" +
                "・倒した数で<b>夢のレベル</b>が上がり、星図（専門化）のポイントが増える。6pt入れたルートでは<b>刻印</b>を1つ選べる。\n" +
                "・鍛冶：欠片で強化（+5まで）、調律石で特性の引き直し（3回まで）、不要な遺物は分解。\n" +
                "・遠征ごとに依頼が3つ。達成すると欠片・調律石（未確保）と経験値。確保の最中に達成した分はそのまま確保。",
                "- Enemies drop personal <b>relics</b> (gear) for every player. New loot starts <b>unsecured</b>.\n" +
                "- Each new zone is a <b>secure point</b>. Secure moves loot to your stash. Delve raises your delve level: more drops, better rarity and more nightmare elites (the game's own elites), but more damage taken — and a bigger shard bonus when you finally secure. The game's Limbo depth also boosts relic drops.\n" +
                "- If your party is wiped, unsecured relics become <b>Lost & Found</b>. Clear 3 combat rooms next run to recover the best one.\n" +
                "- Change gear outside expeditions or at secure points. Three slots: weapon, armor, charm.\n" +
                "- Kills raise your <b>Dream Level</b>, granting star map points. With 6 points in a route you can engrave one <b>keystone</b>.\n" +
                "- Forge: enhance with shards (+5 max), retune affixes with tuning stones (3 times), salvage the rest.\n" +
                "- Each expedition offers 3 bounties. Rewards (shards, tuning) go to your satchel unsecured, plus xp."), _st.Small);
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("設定", "Settings"), _st.Header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("ヒントをもう一度見る", "Show tips again"), _st.Button, GUILayout.Width(180)))
            {
                _s.Profile.SeenHints.Clear();
                _s.Profile.HintsOff = false;
                _s.MarkDirty(false);
                Notify(Rules.HintOnce(_s.Profile, Hint.Welcome).FirstOrDefault());
            }
            if (GUILayout.Button(Loc.T("English", "日本語"), _st.Button, GUILayout.Width(120)))
            {
                cfg.japanese = !cfg.japanese;
                Loc.Japanese = cfg.japanese;
            }
            GUILayout.Label(Loc.T("キー・倍率はゲームのMOD設定から変更できます。", "Keys and scale can be changed in the game's mod settings."), _st.Small);
            GUILayout.EndHorizontal();
            if (_s.SavePath != null) GUILayout.Label(Loc.T("保存先：", "Save file: ") + _s.SavePath, _st.Small);
            if (_s.LoadNotes != null) GUILayout.Label(_s.LoadNotes, _st.Warn);
            if (_s.SaveError != null) GUILayout.Label(Loc.T("保存エラー：", "Save error: ") + _s.SaveError, _st.Warn);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_st.Panel);
            var st = p.Stats;
            int need = Content.XpToNext(p.DreamLevel);
            var today = DailyDream.Today;
            GUILayout.Label(Loc.T("今日の夢", "Today's dream"), _st.Header);
            GUILayout.Label($"<b>{today.Name}</b>  {today.Description}", _st.Small);
            GUILayout.Label(Loc.T("記録", "Records"), _st.Header);
            GUILayout.Label(Loc.T(
                $"夢のレベル {p.DreamLevel}（{p.DreamXp}/{need}）\n遠征 {st.Runs}回　踏破 {st.Victories}　全滅 {st.Defeats}\n撃破 {st.Kills}　遺物 {st.RelicsFound}個（固有品 {st.LegendariesFound}）\n確保した最高潜行 {st.BestHeatSecured}　図鑑 {p.Codex.Count}/{Content.Bases.Count + Content.Uniques.Count}\nエピック救済カウント {p.EpicPity}",
                $"Dream Level {p.DreamLevel} ({p.DreamXp}/{need})\nRuns {st.Runs}  Victories {st.Victories}  Defeats {st.Defeats}\nKills {st.Kills}  Relics {st.RelicsFound} (legendary {st.LegendariesFound})\nBest secured depth {st.BestHeatSecured}  Codex {p.Codex.Count}/{Content.Bases.Count + Content.Uniques.Count}\nEpic pity counter {p.EpicPity}"), _st.Small);
            if (p.Run != null)
            {
                GUILayout.Label(Loc.T($"今回の遠征（未確保 {p.Run.Satchel.Count}）", $"This expedition ({p.Run.Satchel.Count} unsecured)"), _st.Header);
                int rerolls = Rules.RerollsLeft(p);
                for (int i = 0; i < p.Run.Bounties.Count; i++)
                {
                    var b = p.Run.Bounties[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((b.Done ? "● " : "○ ") + b.Describe() + $"  {b.Progress}/{b.Target}  <color=#c9a86a>{b.RewardText()}</color>", _st.Small);
                    if (!b.Done && rerolls > 0)
                    {
                        int idx = i;
                        if (GUILayout.Button(Loc.T($"引き直し（残り{rerolls}）", $"Reroll ({rerolls} left)"), _st.Button, GUILayout.Width(150))) Act(() => Rules.RerollBounty(p, idx), false);
                    }
                    GUILayout.EndHorizontal();
                }
                foreach (var r in p.Run.Satchel.OrderByDescending(r => r.Score).ToList())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("· " + UiStyles.RelicTitle(r) + $" Lv{r.ItemLevel}", _st.Small);
                    if (GUILayout.Button(Loc.T($"分解（ダスト+{Economy.SalvageDust(r)}）", $"Salvage (+{Economy.SalvageDust(r)} dust)"), _st.Button, GUILayout.Width(170)))
                    {
                        string err = _s.SalvageUnsecured(r.Uid);
                        if (err != null) SetStatus(err);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label(Loc.T($"遺失物（{p.LostAndFound.Count}/{Content.LostAndFoundCapacity}）", $"Lost & Found ({p.LostAndFound.Count}/{Content.LostAndFoundCapacity})"), _st.Header);
            if (p.LostAndFound.Count == 0) GUILayout.Label(Loc.T("なし", "None"), _st.Small);
            foreach (var r in p.LostAndFound.OrderByDescending(r => r.Score)) GUILayout.Label("· " + UiStyles.RelicTitle(r) + $" Lv{r.ItemLevel}", _st.Small);
            GUILayout.Label(Loc.T("固有品図鑑", "Legendary codex"), _st.Header);
            foreach (var u in Content.Uniques)
                GUILayout.Label(p.Codex.Contains(u.Id) ? UiStyles.Colored("◆ " + u.Name, UiStyles.RarityHex(Rarity.Legendary)) : "<color=#666>◇ ？？？</color>", _st.Small);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }
    }
}
