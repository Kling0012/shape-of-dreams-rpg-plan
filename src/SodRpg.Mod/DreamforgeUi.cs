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
                sb.Append("\n<size=13>").Append(compact ? Loc.T("未確保：遺物", "Unsecured: ") : Loc.T("まだ持ち帰っていない物：遺物", "Not yet secured: ")).Append(run.Satchel.Count)
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
                    sb.Append("\n<size=13>").Append(Loc.T("装備の効果がまだ反映されていません（ホストがこのMODを入れていない可能性があります）", "Gear bonuses not applied yet (the host may not have this mod)")).Append("</size>");
            }
            int unclaimedFeats = Feats.Unclaimed(p);
            if (unclaimedFeats > 0)
                sb.Append("\n<size=13><color=#ffe17a>").Append(Loc.T($"★ 受け取れる偉業 {unclaimedFeats}（記録タブ）", $"★ {unclaimedFeats} feat reward(s) to claim (Records)")).Append("</color></size>");
            sb.Append("\n<size=13><color=#aaaacc>[").Append(cfg.menuKey).Append(Loc.T("] メニュー", "] Menu")).Append("</color></size>");
            return sb.ToString();
        }

        private void DrawSecurePrompt(float w, float h, DreamforgeConfig cfg)
        {
            var run = _s.Profile.Run;
            var rect = new Rect(w / 2 - 320, 80, 640, 330 + (run.OfferedPacts.Count > 0 ? 34 + 40 * run.OfferedPacts.Count : 0) + (run.OfferedEvent != DreamEvent.None ? 76 : 0));
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.Label(Loc.T("確保地点 ─ ここで持ち帰るか、さらに潜るかを選びます", "Secure Point ─ take your loot home, or delve deeper"), _st.Title);
            int bonus = run.SatchelShards * run.Heat / 4;
            GUILayout.Label(Loc.T(
                $"まだ持ち帰っていない物：遺物{run.Satchel.Count}個、欠片{run.SatchelShards}、調律石{run.SatchelTuning}",
                $"Not yet secured: {run.Satchel.Count} relics, {run.SatchelShards} shards, {run.SatchelTuning} tuning"), _st.Label);
            int next = Math.Min(Content.MaxHeat, run.Heat + 1);
            GUILayout.Label(UiStyles.Colored(Loc.T("確保する：", "Secure: "), "#7af0c8") + Loc.T(
                $"手に入れた物がすべて保管庫に入り、この先で全滅しても失いません。" + (bonus > 0 ? $"潜った分のボーナスとして欠片が{bonus}増えます。" : "") + "潜行は遠征を始めたときの深さに戻ります。",
                $"Everything you carry goes to your stash and is safe even if you fall later." + (bonus > 0 ? $" Your delve bonus adds {bonus} shards." : "") + " Delve returns to your starting depth."), _st.Small);
            GUILayout.Label(UiStyles.Colored(Loc.T("深く潜る：", "Delve: "), "#ffb070") + Loc.T(
                $"持ち帰らずに次のゾーンへ進み、潜行が{next}になります。遺物の出る量が{(int)(Loot.HeatDropBonus * 100 * next)}%増えてレア度も上がりますが、受けるダメージも{Build.DamageTakenPerDelvePct * next}%増えます。全滅すると、まだ持ち帰っていない物は遺失物になります。",
                $"Move on without securing; delve becomes {next}. Relic drops +{(int)(Loot.HeatDropBonus * 100 * next)}% with better rarity, but you take {Build.DamageTakenPerDelvePct * next}% more damage. If your party falls, unsecured loot becomes Lost & Found."), _st.Small);
            GUILayout.Label(UiStyles.Colored(Loc.T(
                $"潜行{next}では、敵の{(int)(Nightmares.Chance(MonsterTier.Normal, next) * 100)}%とエリートの{(int)(Nightmares.Chance(MonsterTier.MiniBoss, next) * 100)}%が「悪夢化」して強くなります。倒すとエリートやボス並みの戦利品が出ます。",
                $"At delve {next}, {(int)(Nightmares.Chance(MonsterTier.Normal, next) * 100)}% of enemies and {(int)(Nightmares.Chance(MonsterTier.MiniBoss, next) * 100)}% of elites turn into stronger nightmares that drop elite-to-boss loot."), "#ff9ae0"), _st.Small);
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
                        batches > 0
                            ? $"ドリームダスト{batches * Economy.DustPerBatch}を欠片{batches * Economy.ShardsPerBatch}に換える（ダスト{Economy.DustPerBatch}につき欠片{Economy.ShardsPerBatch}。所持{dust}）"
                            : $"ドリームダストを欠片に換える（ダスト{Economy.DustPerBatch}につき欠片{Economy.ShardsPerBatch}。所持{dust}）",
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
                GUILayout.Label(Loc.T("または、悪夢の契約を結んで潜ることもできます。代償を受ける代わりに見返りが増え、次に確保するまで効果が重なります。", "Or delve with a nightmare pact: accept a drawback for a bigger reward. Pacts stack until you secure."), _st.Small);
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
            GUILayout.Label(r.Victory ? Loc.T("遠征の結果：夢を踏破しました", "Expedition: Conquered") : Loc.T("遠征の結果：夢から覚めました", "Expedition: Awakened"), _st.Title);
            GUILayout.Label(Loc.T(
                $"敵を{r.Kills}体倒し、遺物を{r.RelicsFound}個見つけました。\nそのうち{r.RelicsSecured}個を持ち帰りました（確保{r.SecuredCount}回、欠片{r.ShardsSecured}）。\n" +
                (r.RelicsLost > 0 || r.EchoShards > 0 ? $"<color=#ff8080>持ち帰れなかった遺物：{r.RelicsLost}個</color>　残響として残った欠片：{r.EchoShards}\n" : "") +
                $"最も深い潜行 {r.PeakHeat}　依頼 {r.BountiesDone}/{r.BountiesTotal}達成　夢のレベル {r.LevelBefore} → {r.LevelAfter}",
                $"Kills {r.Kills}   Relics found {r.RelicsFound}\nSecured {r.RelicsSecured} ({r.SecuredCount} secures, {r.ShardsSecured} shards)\n" +
                (r.RelicsLost > 0 || r.EchoShards > 0 ? $"<color=#ff8080>Lost {r.RelicsLost}</color>   Echo shards {r.EchoShards}\n" : "") +
                $"Peak delve {r.PeakHeat}   Bounties {r.BountiesDone}/{r.BountiesTotal}   Dream Level {r.LevelBefore} -> {r.LevelAfter}"), _st.Label);
            if (r.RelicsLost > 0)
                GUILayout.Label(Loc.T("持ち帰れなかった遺物は「遺失物」として残ります。次の遠征で戦闘部屋を3つ突破すると、その中で一番良い物を1つ取り戻せます。", "Lost relics wait in Lost & Found. Clear 3 combat rooms next expedition to recover the best one."), _st.Small);
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
            float ww = Mathf.Min(1060, w - 20), wh = Mathf.Min(720, h - 20);
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
                $"欠片 {p.Material(Materials.Shard)}　調律石 {p.Material(Materials.Tuning)}　保管庫 {p.Stash.Count}/{Workshop.StashCapacity(p)}　旅人：{HeroName(HeroKey)}",
                $"Shards {p.Material(Materials.Shard)}   Tuning {p.Material(Materials.Tuning)}   Stash {p.Stash.Count}/{Workshop.StashCapacity(p)}   Traveler: {HeroName(HeroKey)}"), _st.Small);
            GUILayout.Label(UiStyles.Colored(TabIntro(_tab), "#c8d0ff"), _st.Small);

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

        private static readonly Txt HowToPlay = new Txt(
            "<b>このMODの目的</b>\n" +
            "本体の遠征に「持ち帰れる装備（遺物）」が加わります。遠征のたびに少しずつ装備を集めて鍛え、次の遠征をもっと深く、もっと楽に進めるようにしていきます。\n\n" +
            "<b>1回の遠征の流れ</b>\n" +
            "1. 敵を倒すと遺物が落ちます。協力プレイでも各自に別々に落ちるので、取り合いにはなりません。\n" +
            "2. 拾った物は、まだ持ち帰っていない状態（未確保）です。\n" +
            "3. 新しいゾーンに着くと確保地点が開きます。ここで「確保する」か「深く潜る」かを選びます。\n" +
            "4. 確保した物は保管庫に入り、遠征が終わっても残ります。\n\n" +
            "<b>確保と潜行の考え方</b>\n" +
            "確保すれば安全ですが、深く潜ると遺物が多く、良い物が出やすくなります。そのぶん敵は強くなり、受けるダメージも増えます。全滅すると、まだ持ち帰っていない物は遺失物になり、次の遠征で戦闘部屋を3つ突破すると一番良い物を1つだけ取り戻せます。手応えを見ながら、どこで確保するかを決めるのがこのMODの駆け引きです。\n\n" +
            "<b>装備の育て方</b>\n" +
            "・装備：旅人ごとに主装備・防具・装飾品の3つを装着します。\n" +
            "・鍛冶：欠片で強化し、調律石で特性を引き直します。いらない物は分解して欠片に戻せます。\n" +
            "・星図：夢のレベルが上がるともらえるポイントで能力を伸ばします。条件を満たすと、強力な到達刻印を1つ選べます。\n" +
            "・工房：余った素材で、すべての旅人に効く恒久的な強化を解放します。\n" +
            "・依頼：遠征ごとに3つ出ます。達成すると欠片や調律石と経験値がもらえます。",
            "<b>What this mod adds</b>\n" +
            "Expeditions now drop gear you can keep (relics). Collect and improve a little every run so the next expedition goes deeper and smoother.\n\n" +
            "<b>One expedition</b>\n" +
            "1. Enemies drop relics. In co-op every player gets their own drops, so there is no fighting over loot.\n" +
            "2. What you pick up is not yet secured.\n" +
            "3. Each new zone opens a secure point where you choose to Secure or Delve.\n" +
            "4. Secured relics go to your stash and stay after the expedition ends.\n\n" +
            "<b>Securing vs. delving</b>\n" +
            "Securing is safe. Delving gives more and better relics, but enemies get tougher and you take more damage. If your party falls, unsecured loot becomes Lost & Found; clear 3 combat rooms next expedition to recover the best piece. Deciding when to secure is the heart of this mod.\n\n" +
            "<b>Growing your gear</b>\n" +
            "- Gear: each Traveler has a weapon, armor and charm slot.\n" +
            "- Forge: enhance with shards, reroll affixes with tuning stones, salvage the rest into shards.\n" +
            "- Star Map: spend points from Dream Levels to grow stats; meet the conditions to pick one powerful keystone.\n" +
            "- Workshop: unlock permanent upgrades shared by all Travelers.\n" +
            "- Bounties: 3 per expedition, rewarding shards, tuning stones and experience.");

        /// <summary>記録タブの偉業：受け取れる物 → 未達成（進み具合）→ 受け取り済み の順に並べる。</summary>
        private void DrawFeats(Profile p)
        {
            int done = p.Feats.Count, total = Feats.All.Count, unclaimed = Feats.Unclaimed(p);
            GUILayout.Label(Loc.T($"偉業（{done}／{total}）", $"Feats ({done}/{total})"), _st.Header);
            GUILayout.Label(Loc.T("遊ぶうちに達成していく目標です。達成すると、欠片と調律石を受け取れます。",
                "Goals you complete as you play. Each one rewards shards and tuning stones."), _st.Small);
            foreach (var f in Feats.All)
            {
                if (!p.Feats.Contains(f.Id) || p.FeatsClaimed.Contains(f.Id)) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(UiStyles.Colored("★ " + f.Name, "#ffe17a") + "  <color=#aab>" + Feats.Describe(f) + "</color>", _st.Small);
                string reward = f.RewardTuning > 0
                    ? Loc.T($"受け取る（欠片{f.RewardShards}・調律石{f.RewardTuning}）", $"Claim ({f.RewardShards} shards, {f.RewardTuning} tuning)")
                    : Loc.T($"受け取る（欠片{f.RewardShards}）", $"Claim ({f.RewardShards} shards)");
                if (GUILayout.Button(reward, _st.ButtonSel, GUILayout.Width(210)))
                {
                    string id = f.Id;
                    Act(() => Rules.ClaimFeat(p, id), false);
                }
                GUILayout.EndHorizontal();
            }
            int shown = 0;
            foreach (var f in Feats.All)
            {
                if (p.Feats.Contains(f.Id)) continue;
                // 未達成は、種類ごとに次の1段だけを出す（一覧が長くなりすぎないように）。
                bool earlierOpen = false;
                foreach (var g in Feats.All)
                {
                    if (g == f) break;
                    if (g.Kind == f.Kind && !p.Feats.Contains(g.Id)) { earlierOpen = true; break; }
                }
                if (earlierOpen) continue;
                int prog = Math.Min(Feats.Progress(p, f), f.Target);
                GUILayout.Label("☆ " + f.Name + "  <color=#aab>" + Feats.Describe(f) + $"  {prog}/{f.Target}</color>", _st.Small);
                shown++;
            }
            if (unclaimed == 0 && shown == 0)
                GUILayout.Label(Loc.T("すべての偉業を達成しました。", "Every feat is complete."), _st.Small);
            int claimed = p.FeatsClaimed.Count;
            if (claimed > 0)
                GUILayout.Label(Loc.T($"<color=#8a8aa0>受け取り済み：{claimed}個</color>", $"<color=#8a8aa0>Claimed: {claimed}</color>"), _st.Small);
        }

        /// <summary>各タブの先頭に出す「ここでできること」。</summary>
        private static string TabIntro(int tab)
        {
            switch (tab)
            {
                case 0: return Loc.T("持ち帰った遺物を、旅人ごとに3つの枠（主装備・防具・装飾品）へ装着します。遠征中は確保地点でだけ付け替えられます。",
                    "Equip relics you brought home into each Traveler's three slots (weapon, armor, charm). During an expedition you can only swap at secure points.");
                case 1: return Loc.T("欠片で遺物を強くし、調律石で気に入らない特性を引き直します。いらない遺物は分解して欠片に戻せます。",
                    "Use shards to enhance relics and tuning stones to reroll an affix you dislike. Salvage what you don't need back into shards.");
                case 2: return Loc.T("夢のレベルが上がるともらえるポイントで、旅人ごとに能力を伸ばします。振り直しは無料なので、気軽に試してください。",
                    "Spend the points you earn from Dream Levels to grow each Traveler. Respec is free, so feel free to experiment.");
                case 3: return Loc.T("余った欠片と調律石で、すべての旅人に効く恒久的な強化を解放します。",
                    "Spend spare shards and tuning stones on permanent upgrades shared by every Traveler.");
                default: return Loc.T("遊び方の確認、今回の遠征の様子、これまでの記録と図鑑を見られます。",
                    "Read how to play, check this expedition, and browse your records and codex.");
            }
        }

        private void FocusPicker()
        {
            var p = _s.Profile;
            GUILayout.Label(Loc.T("狙い系統：選んだ系統の遺物が2倍出やすくなります（遠征の前に選びます）", "Focus: relics of the chosen line drop twice as often (choose before an expedition)"), _st.Small);
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
            GUILayout.Label(Loc.T("旅人：", "Traveler:"), _st.Small, GUILayout.Width(60));
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
                    $"熟練度 {lv}「{Mastery.Title(lv)}」" + (next > 0 ? $" <color=#888>あと{next}体倒すと上がります</color>" : ""),
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
            _scrollDetail = GUILayout.BeginScrollView(_scrollDetail, GUILayout.Height(250));
            if (build.Stats.Count == 0 && build.Powers.Count == 0) GUILayout.Label(Loc.T("まだ何も装着していません。真ん中の一覧から遺物を選び、「装着する」を押してください。", "Nothing equipped yet. Pick a relic from the middle list and press Equip."), _st.Small);
            foreach (var kv in build.Stats) if (kv.Value != 0) GUILayout.Label(Content.FormatStat(kv.Key, kv.Value), _st.Small);
            foreach (var kv in build.Powers) GUILayout.Label(UiStyles.Colored(Content.FormatPower(kv.Key, kv.Value), "#e0b0ff"), _st.Small);
            foreach (var kv in build.Sets)
            {
                var set = Content.GetSet(kv.Key);
                if (set == null) continue;
                GUILayout.Label(UiStyles.Colored($"《{set.Name}》 {kv.Value}/3", "#ffb52e") + "  <color=#ddd>" + set.Progress(kv.Value) + "</color>\n<color=#aab>" + set.Describe() + "</color>", _st.Small);
            }
            foreach (var kv in build.Lines)
            {
                if (kv.Value < 2) continue;
                string bonus = string.Join("・", Content.SetBonus(kv.Key, kv.Value).Select(x => Content.FormatStat(x.Stat, x.Value)));
                GUILayout.Label(UiStyles.Colored(Loc.T($"〈{Content.LineName(kv.Key)}×{kv.Value}〉{bonus}", $"<{Content.LineName(kv.Key)} x{kv.Value}> {bonus}"), "#9fe0c0"), _st.Small);
            }
            GUILayout.EndScrollView();
            if (!_s.CanEditLoadout)
                GUILayout.Label(Loc.T("遠征中は、確保地点に着いたときだけ装備を変えられます。", "During an expedition, you can only change gear at secure points."), _st.Warn);
            GUILayout.Label(Loc.T("同じ系統（破壊・生命・想像）の遺物を2つ、3つとそろえると、系統のボーナスが付きます。", "Equipping 2 or 3 relics of the same line (Destruction, Life, Imagination) grants a line bonus."), _st.Small);
            FocusPicker();
            GUILayout.EndVertical();

            // 中：保管庫
            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(330));
            GUILayout.BeginHorizontal();
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
                if (GUILayout.Button(Content.SlotName(slot).ToString(), _slot == slot ? _st.ButtonSel : _st.Button)) _slot = slot;
            GUILayout.EndHorizontal();
            RelicList(p.Stash.Where(r => r.Slot == _slot), hero, 440);
            GUILayout.EndVertical();

            // 右：詳細と比較
            GUILayout.BeginVertical(_st.Panel);
            var sel = p.FindStash(_selected);
            if (sel == null)
            {
                GUILayout.Label(Loc.T("一覧から遺物を選ぶと、ここに性能と、いま装着している物との違いが表示されます。", "Select a relic to see its stats and how it compares with what you have equipped."), _st.Small);
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
                    foreach (var e in Rules.Equip(p, hero, sel.Uid)) _s.Emit(e);
                    _s.MarkDirty(true);
                }
                if (equipped && GUILayout.Button(Loc.T("外す", "Unequip"), _st.Button, GUILayout.Height(32)))
                {
                    foreach (var e in Rules.Unequip(p, hero, sel.Slot)) _s.Emit(e);
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
            if (list.Count == 0) GUILayout.Label(Loc.T("まだありません。遠征で敵を倒すと遺物が落ち、確保すると保管庫に入ります。", "Nothing here yet. Enemies drop relics on expeditions; secure them to bring them here."), _st.Small);
            var h = _s.Profile.Hero(hero);
            foreach (var r in list)
            {
                string mark = h.Equipped.Contains(r.Uid) ? "<color=#ffe17a>★</color> " : "";
                string lck = r.Locked ? Loc.T(" <color=#aaa>[鍵]</color>", " <color=#aaa>[locked]</color>") : "";
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
            GUILayout.Label(UiStyles.Colored(Content.FormatStat(imp.Stat, imp.Value), "#c8c8ff") + Loc.T("  <color=#888>（この種類が必ず持つ性能）</color>", "  <color=#888>(always on this type)</color>"), _st.Label);
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
            GUILayout.Label(Loc.T("いま装着している物と比べると", "Compared with what you have equipped"), _st.Header);
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
            RelicList(_forgeAllSlots ? p.Stash : p.Stash.Where(r => r.Slot == _slot), HeroKey, 470);
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
                else GUILayout.Label(Loc.T("これ以上は強化できません", "Fully enhanced"), _st.Small);
                string sv = _confirmSalvage == sel.Uid
                    ? Loc.T("<color=#ff8080>もう一度押すと分解します</color>", "<color=#ff8080>Press again to salvage</color>")
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
                    GUILayout.Label(Loc.T($"再調律：引き直したい特性を1つ選んでください（調律石{Content.RetuneCost(sel.Retunes)}。1つの遺物につき{Content.MaxRetunes}回まで）", $"Retune: reroll one affix ({Content.RetuneCost(sel.Retunes)} tuning)"), _st.Small);
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
                GUILayout.Label(Loc.T("左の一覧から遺物を選ぶと、強化・再調律・分解ができます。強化は欠片、再調律は調律石を使います。", "Pick a relic on the left to enhance (shards), retune (tuning stones) or salvage it."), _st.Small);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("合成：同じレア度の遺物3つを、1つ上のレア度の遺物1つに変えます（鍵をかけた物と装着中の物は使いません）", "Transmute: turn 3 relics of one rarity into 1 of the next (locked and equipped relics are never used)"), _st.Header);
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
            GUILayout.Label(Loc.T("製作：欠片を使って、これまでに手に入れた最高のアイテムレベルで新しい遺物を作ります", "Craft: spend shards to make a new relic at the highest item level you have reached"), _st.Header);
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
                $"使えるポイント：残り {Rules.FreePoints(p, hero)} / {p.TalentPoints}（夢のレベルと図鑑のボーナス{p.CodexBonusPoints}の合計。旅人ごとに別々に振れます）",
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
            if (!_s.CanEditTalents) GUILayout.Label(Loc.T("星図は遠征に出ていないときだけ変更できます。", "The star map can only be changed outside expeditions."), _st.Warn);

            GUILayout.BeginHorizontal();
            if (HeroSigils.HasTree(hero))
            {
                GUILayout.BeginVertical(_st.Panel, GUILayout.Width(520));
                GUILayout.Label(Loc.T($"旅人の刻印：{HeroName(hero)}", $"Traveler sigils: {HeroName(hero)}"), _st.Header);
                foreach (var t in HeroSigils.TreeFor(hero)) DrawTalentNode(p, hero, hs, t);
                GUILayout.EndVertical();
                GUILayout.BeginVertical(_st.Panel);
                int lv = Mastery.Level(hs.Kills);
                GUILayout.Label(Loc.T("この旅人だけが使える刻印です。旅人自身のスキルや通常攻撃を伸ばします。", "Sigils unique to this Traveler that strengthen their own skills and attacks."), _st.Small);
                GUILayout.Label(Loc.T(
                    $"到達刻印を選ぶには、このツリーに{Content.KeystoneRouteRequirement}ポイント以上振り、熟練度を{HeroSigils.KeystoneMastery}以上にする必要があります（いまの熟練度は{lv}「{Mastery.Title(lv)}」）",
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
                    : Loc.T("条件を満たすと選べます", "Locked");
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
                else GUILayout.Label(Loc.T("すべて解放済み", "Maxed"), _st.Header, GUILayout.Width(300));
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.EndVertical();
            if (p.Run != null) GUILayout.Label(Loc.T("遠征中は工房を使えません。遠征から戻ってから利用してください。", "The workshop is closed during expeditions."), _st.Warn);
        }

        private void DrawRecordsTab(DreamforgeConfig cfg)
        {
            var p = _s.Profile;
            _scrollRecords = GUILayout.BeginScrollView(_scrollRecords);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(480));
            GUILayout.Label(Loc.T("遊び方", "How to play"), _st.Header);
            GUILayout.Label(HowToPlay.ToString(), _st.Small);
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
            GUILayout.Label(Loc.T("キーの割り当てや表示の大きさは、ゲームのMOD設定で変えられます。", "Keys and scale can be changed in the game's mod settings."), _st.Small);
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
            DrawFeats(p);
            GUILayout.Label(Loc.T("固有品図鑑", "Legendary codex"), _st.Header);
            int foundUniques = 0;
            foreach (var u in Content.Uniques)
            {
                if (!p.Codex.Contains(u.Id)) continue;
                foundUniques++;
                GUILayout.Label(UiStyles.Colored("◆ " + u.Name, UiStyles.RarityHex(Rarity.Legendary)), _st.Small);
            }
            int missingUniques = Content.Uniques.Count - foundUniques;
            GUILayout.Label(Loc.T(
                $"<color=#8a8aa0>見つけた固有品 {foundUniques}／{Content.Uniques.Count}種。まだ見つけていない物が{missingUniques}種あります。</color>",
                $"<color=#8a8aa0>Legendaries found: {foundUniques}/{Content.Uniques.Count}. {missingUniques} still undiscovered.</color>"), _st.Small);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }
    }
}
