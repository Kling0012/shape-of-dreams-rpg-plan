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

    /// <summary>Dreamforge のメニュー（装備・鍛冶・星図・記録）、HUD、確保地点のパネル、通知の描画。</summary>
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
        private float _hudBottom;
        private int _hudFrame = -10;
        private float _nextHudRebuild;
        private readonly Dictionary<int, Label3D> _nightmareLabelCache = new Dictionary<int, Label3D>();
        private readonly Dictionary<string, Label3D> _variantLabelCache = new Dictionary<string, Label3D>();

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
            // 起動したときに持っていた遺物は「見た」ことにする。それ以降に手に入れた物に NEW を付ける。
            foreach (var r in session.Profile.Stash) _seenUids.Add(r.Uid);
            _seenInit = true;
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

        public void SetStatus(string text)
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
                // ロビーで旅人を選び直したら、メニューもその旅人に合わせる（メニューの < > で選んだ物は、次に選び直すまで保つ）。
                string lobby = LobbyHeroKey();
                if (lobby != null && lobby != _lobbySeen)
                {
                    _lobbySeen = lobby;
                    _heroSel = lobby;
                }
                if (_heroSel == null) _heroSel = _s.Profile.Heroes.Keys.FirstOrDefault(k => k.StartsWith("Hero_")) ?? KnownHeroes[0];
                return _heroSel;
            }
        }

        private string _lobbySeen;

        private static string LobbyHeroKey()
        {
            try
            {
                var lp = DewPlayer.local;
                string t = lp != null ? lp.selectedHeroType : null;
                return string.IsNullOrEmpty(t) ? null : t;
            }
            catch (Exception) { return null; }
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
                    if (layout && !Open && _s.Profile.Run != null && _s.Profile.Run.AwaitingChoice && _s.ActiveRunId != null) DrawSecurePrompt(w, h, cfg);
                }
                if (repaint) DrawToasts(w, h);
                // メニューを開いている間は、確保地点と遠征結果のパネルを隠す（重なった下のボタンを押せないように）。
                if (layout && !Open && _s.Profile.LastReport != null && (_s.Profile.LastReport != _shownReport || !_reportDismissed)) DrawReport(w, h);
                // ヒントの上にマウスがあるときは、下にあるメニューのボタンへマウスの操作を渡さない。
                bool mouseEvent = type == EventType.MouseDown || type == EventType.MouseUp || type == EventType.MouseDrag || type == EventType.ScrollWheel;
                Vector2 realMouse = Event.current.mousePosition;
                bool shield = mouseEvent && _hints.Count > 0 && HintRect(w, h).Contains(realMouse);
                if (shield) Event.current.mousePosition = new Vector2(-99999f, -99999f);
                try
                {
                    if (layout && Open) DrawWindow(w, h, cfg);
                }
                finally
                {
                    if (shield) Event.current.mousePosition = realMouse;
                }
                if (layout && _hints.Count > 0) DrawHint(w, h, cfg);
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Getting control"))
            {
                // ボタンを押した瞬間に表示が変わると、その回の描画だけ配置が合わなくなる。次の描画で直るので、メニューは閉じない。
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
            // v1.25.2：Discord のオーバーレイと重ならないよう、画面の左上に出す。
            GUI.Label(new Rect(10, 10, 290, _hudHeight), _hudContent, _st.Hud);
            _hudBottom = 10 + _hudHeight;
            _hudFrame = Time.frameCount;
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
            sb.Append(Loc.T("<b>Dreamforge</b>  夢のレベル ", "<b>Dreamforge</b>  Dream Lv ")).Append(p.DreamLevel).Append("  <color=#aaaacc>(").Append(xp).Append(")</color>");
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
                sb.Append("\n<size=13>").Append(Loc.T(
                    $"未確保：遺物{run.Satchel.Count}/{Workshop.SatchelCapacity(p)}・欠片{run.SatchelShards}・調律石{run.SatchelTuning}",
                    $"Unsecured: {run.Satchel.Count}/{Workshop.SatchelCapacity(p)} relics, {run.SatchelShards} shards, {run.SatchelTuning} tuning")).Append("</size>");
                if (p.LostAndFound.Count > 0 && !run.LostRecovered)
                {
                    int rooms = Workshop.RoomsToRecover(p);
                    sb.Append("\n<size=13><color=#ffb070>").Append(Loc.T(
                        $"遺失物の回収：戦闘部屋 {Math.Min(run.RoomsCleared, rooms)}/{rooms}",
                        $"Lost & Found: combat rooms {Math.Min(run.RoomsCleared, rooms)}/{rooms}")).Append("</color></size>");
                }
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
                sb.Append("\n<size=13><color=#ffe17a>").Append(Loc.T($"★ 偉業の報酬を{unclaimedFeats}件受け取れます（記録タブ）", $"★ {unclaimedFeats} feat reward(s) to claim (Records tab)")).Append("</color></size>");
            sb.Append("\n<size=13><color=#aaaacc>[").Append(cfg.menuKey).Append(Loc.T("] メニュー", "] Menu")).Append("</color></size>");
            return sb.ToString();
        }

        private void DrawSecurePrompt(float w, float h, DreamforgeConfig cfg)
        {
            var run = _s.Profile.Run;
            var rect = new Rect(w / 2 - 320, 80, 640, 330 + (run.Satchel.Count > 0 ? 42 : 0) + (run.OfferedPacts.Count > 0 ? 34 + 56 * run.OfferedPacts.Count : 0)
                + (run.OfferedEvent != DreamEvent.None ? 84 : 0) + (_s.HasPendingTrades ? 24 : 0)
                + 24);
            // 前のフレームで測った中身の高さがあれば、それに合わせる（余白も、はみ出しも出さない）。
            if (_secureMeasured > 0) rect.height = _secureMeasured + 28;
            if (rect.height > h - 100) rect.height = h - 100;
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            _scrollSecure = GUILayout.BeginScrollView(_scrollSecure);
            GUILayout.Label(Loc.T("確保地点 ─ ここで持ち帰るか、さらに潜るかを選びます", "Secure Point ─ take your loot home, or delve deeper"), _st.Title);
            int bonus = run.SatchelShards * run.Heat / 4;
            if (Pacts.Sum(run.Pacts).DoubleDepthBonus) bonus *= 2;
            GUILayout.Label(Loc.T(
                $"まだ持ち帰っていない物：遺物{run.Satchel.Count}個、欠片{run.SatchelShards}、調律石{run.SatchelTuning}",
                $"Not yet secured: {run.Satchel.Count} relics, {run.SatchelShards} shards, {run.SatchelTuning} tuning"), _st.Label);
            if (run.Satchel.Count > 0)
            {
                // 持ち帰れる遺物を、良い物から順にアイコンで並べる（最大14個）。
                GUILayout.BeginHorizontal();
                foreach (var r in SatchelTop()) IconSlot(r, 36);
                if (run.Satchel.Count > 14) GUILayout.Label($"+{run.Satchel.Count - 14}", _st.Small);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            int next = Math.Min(Content.MaxHeat, run.Heat + 1);
            bool atCap = run.Heat >= Content.MaxHeat;
            GUILayout.Label(UiStyles.Colored(Loc.T("確保する：", "Secure: "), "#7af0c8") + Loc.T(
                $"手に入れた物がすべて保管庫に入り、この先で全滅しても失いません。" + (bonus > 0 ? $"潜った分のボーナスとして欠片が{bonus}増えます。" : "") + "潜行は0に戻ります。",
                $"Everything you carry goes to your stash and is safe even if you fall later." + (bonus > 0 ? $" Your delve bonus adds {bonus} shards." : "") + " Delve resets to 0."), _st.Small);
            int free = Math.Max(0, Workshop.StashCapacity(_s.Profile) - _s.Profile.Stash.Count);
            if (run.Satchel.Count > free)
                GUILayout.Label(Loc.T(
                    $"保管庫の空きは{free}個です。入りきらない{run.Satchel.Count - free}個は、弱い物から欠片になります。",
                    $"Your stash has room for {free}. The weakest {run.Satchel.Count - free} will become shards."), _st.Warn);
            GUILayout.Label(UiStyles.Colored(Loc.T("深く潜る：", "Delve: "), "#ffb070") + Loc.T(
                (atCap ? $"持ち帰らずに次のゾーンへ進みます。潜行は{next}のままです（これ以上は深くなりません）。" : $"持ち帰らずに次のゾーンへ進み、潜行が{next}になります。")
                + $"遺物の出る量が{(int)(Loot.HeatDropBonus * 100 * next)}%増えてレア度も上がりますが、受けるダメージも{Build.DamageTakenPerDelvePct * next}%増えます。全滅すると、まだ持ち帰っていない物は遺失物になります。",
                (atCap ? $"Move on without securing; delve stays at {next} (the maximum)." : $"Move on without securing; delve becomes {next}.")
                + $" Relic drops +{(int)(Loot.HeatDropBonus * 100 * next)}% with better rarity, but you take {Build.DamageTakenPerDelvePct * next}% more damage. If your party falls, unsecured loot becomes Lost & Found."), _st.Small);
            double nmMult = DailyDream.Get(run.DailyId)?.NightmareMult ?? 1.0;
            int nmNormal = (int)(Math.Min(1.0, Nightmares.Chance(MonsterTier.Normal, next) * nmMult) * 100);
            int nmElite = (int)(Math.Min(1.0, Nightmares.Chance(MonsterTier.MiniBoss, next) * nmMult) * 100);
            string nmDay = nmMult > 1.0 ? Loc.T("（今日の夢で増えています）", " (raised by today's dream)") : "";
            GUILayout.Label(UiStyles.Colored(Loc.T(
                $"潜行{next}では、通常の敵の{nmNormal}%、エリートの{nmElite}%が「悪夢化」して強くなります{nmDay}（ボスは対象外）。倒すと、一段上の戦利品が出ます。",
                $"At delve {next}, {nmNormal}% of regular enemies and {nmElite}% of elites become stronger nightmares{nmDay} (bosses excluded). They drop loot a tier higher."), "#ff9ae0"), _st.Small);
            GUILayout.BeginHorizontal();
            GUI.enabled = !_s.HasPendingTrades;
            if (GUILayout.Button(Loc.T($"確保する [{cfg.secureKey}]", $"Secure [{cfg.secureKey}]"), _st.Button, GUILayout.Height(34))) SetStatus(_s.Secure());
            if (GUILayout.Button(Loc.T($"深く潜る [{cfg.delveKey}]", $"Delve [{cfg.delveKey}]"), _st.Button, GUILayout.Height(34))) SetStatus(_s.Delve());
            GUI.enabled = true;
            if (GUILayout.Button(Loc.T($"装備を整える [{cfg.menuKey}]", $"Gear up [{cfg.menuKey}]"), _st.Button, GUILayout.Height(34)))
            {
                Open = true;
                _tab = 0;
            }
            GUILayout.EndHorizontal();
            if (_s.HasPendingTrades)
                GUILayout.Label(Loc.T("取引の応答を待っています。", "Waiting for the trade to complete."), _st.Warn);
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
                bool ok = DreamEvents.CanUse(_s.Profile, e, merchant, out string why, _s.Trades);
                if (merchant && ok && _s.LocalGold < _s.MerchantPrice()) { ok = false; why = Loc.T($"ゴールドが足りません（{_s.MerchantPrice()}G）", $"Not enough gold ({_s.MerchantPrice()}G)"); }
                if (merchant && _s.TradePending(TradeKind.MerchantGold)) { ok = false; why = Loc.T("取引の応答を待っています。", "Waiting for the trade to complete."); }
                GUILayout.BeginHorizontal();
                var art = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                if (Event.current.type == EventType.Repaint) RelicIcons.DrawArt(art, EventArtKey(e));
                GUILayout.BeginVertical();
                GUILayout.Label(UiStyles.Colored(Loc.T("出来事：", "Event: ") + DreamEvents.Name(e), "#9fe0ff") + "  <color=#aab>" + DreamEvents.Describe(e, _s.Profile) + "</color>", _st.Small);
                GUI.enabled = ok;
                bool confirm = ok && _confirmEvent == e && DreamEvents.NeedsConfirm(e);
                string label = !ok ? why
                    : merchant ? Loc.T($"買う（{_s.MerchantPrice()}G）", $"Buy ({_s.MerchantPrice()}G)")
                    : confirm ? Loc.T("<color=#ff8080>取り消せません。もう一度押すと確定します</color>", "<color=#ff8080>This can't be undone. Press again to confirm</color>")
                    : DreamEvents.ActionLabel(e);
                if (GUILayout.Button(label, _st.Row, GUILayout.Height(32)))
                {
                    if (merchant)
                    {
                        string err = _s.BuyFromMerchant();
                        if (err != null) SetStatus(err);
                    }
                    else if (DreamEvents.NeedsConfirm(e) && _confirmEvent != e) _confirmEvent = e;
                    else
                    {
                        _confirmEvent = DreamEvent.None;
                        try
                        {
                            foreach (var x in Rules.UseEvent(_s.Profile, e, trades: _s.Trades)) _s.Emit(x);
                            _s.MarkDirty(false);
                            _s.SaveNow();
                        }
                        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
                    }
                }
                GUI.enabled = true;
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            if (run.OfferedPacts.Count > 0)
            {
                GUILayout.Label(Loc.T("または、悪夢の契約を結んで潜ることもできます。代償を受ける代わりに見返りが増え、次に確保するまで効果が重なります。代償の呪いは本体の呪いと同じもので、契約した人の旅人にだけ付きます。", "Or delve with a nightmare pact: accept a drawback for a bigger reward. Pacts stack until you secure. The curse is one of the game's own curses and only affects the Traveler of whoever swore the pact."), _st.Small);
                GUI.enabled = !_s.HasPendingTrades;
                foreach (var id in run.OfferedPacts.ToList())
                {
                    var d = Pacts.Get(id);
                    if (d == null) continue;
                    if (GUILayout.Button($"<b>{d.Name}</b>\n<color=#ffb0a0>{d.Description}</color>", _st.RowWrap, GUILayout.Height(52))) SetStatus(_s.Delve(id));
                }
                GUI.enabled = true;
            }
            GUILayout.Label(_status != null && Time.unscaledTime < _statusUntil ? _status : " ", _st.Warn);
            if (Event.current.type == EventType.Repaint) _secureMeasured = GUILayoutUtility.GetLastRect().yMax;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private float _secureMeasured;

        private Vector2 _scrollSecure;
        private DreamEvent _confirmEvent;
        private static readonly Dictionary<DreamEvent, string> EventArt = new Dictionary<DreamEvent, string>();

        private static string EventArtKey(DreamEvent e)
        {
            if (!EventArt.TryGetValue(e, out var k)) EventArt[e] = k = "events/" + e;
            return k;
        }

        private readonly List<uint> _labelScratch = new List<uint>();

        /// <summary>悪夢化した敵と夢の変種の頭上に名札を出す。</summary>
        private void DrawNightmareLabels(float scale)
        {
            DrawVariantLabels(scale);
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

        private void DrawVariantLabels(float scale)
        {
            if (_s.Variant.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            _labelScratch.Clear();
            foreach (var kv in _s.Variant)
            {
                bool pending = _s.VariantSeenAt.TryGetValue(kv.Key, out float seen) && Time.unscaledTime - seen <= 10f;
                if (!Mirror.NetworkClient.spawned.TryGetValue(kv.Key, out var id) || id == null)
                {
                    if (!pending) _labelScratch.Add(kv.Key);
                    continue;
                }
                var m = id.GetComponent<Monster>();
                var def = Variants.Get(kv.Value);
                if (m == null || def == null || !m.isActive)
                {
                    if (m == null || def == null || !pending) _labelScratch.Add(kv.Key);
                    continue;
                }
                var sp = cam.WorldToScreenPoint(m.position + Vector3.up * 3.2f);
                if (sp.z <= 0) continue;
                if (!_variantLabelCache.TryGetValue(kv.Value, out var lab))
                {
                    string text = UiStyles.Colored(Variants.Label(def), "#ffb347");
                    var content = new GUIContent(text);
                    lab = new Label3D { Text = text, Content = content, Size = _st.ToastMeasure.CalcSize(content) };
                    _variantLabelCache[kv.Value] = lab;
                }
                float x = sp.x / scale - lab.Size.x / 2, y = (Screen.height - sp.y) / scale - lab.Size.y;
                GUI.Label(new Rect(x, y, lab.Size.x + 4, lab.Size.y), lab.Content, _st.Toast);
            }
            foreach (uint netId in _labelScratch) _s.RemoveVariant(netId);
        }

        private bool _reportDismissed;
        private RunReport _reportHeightFor;
        private bool _reportHeightJa;
        private float _reportHeight;
        private RunReport _shownReport;

        private void DrawReport(float w, float h)
        {
            var r = _s.Profile.LastReport;
            if (r != _shownReport)
            {
                _shownReport = r;
                _reportDismissed = false;
            }
            string text = Loc.T(
                $"敵を{r.Kills}体倒し、遺物を{r.RelicsFound}個見つけました。\nそのうち{r.RelicsSecured}個を持ち帰りました（確保{r.SecuredCount}回、欠片{r.ShardsSecured}）。\n" +
                (r.RelicsLost > 0 || r.EchoShards > 0 ? $"<color=#ff8080>持ち帰れなかった遺物：{r.RelicsLost}個</color>　残響として残った欠片：{r.EchoShards}\n" : "") +
                $"最も深い潜行 {r.PeakHeat}　依頼 {r.BountiesDone}/{r.BountiesTotal}達成　夢のレベル {r.LevelBefore} → {r.LevelAfter}",
                $"Kills {r.Kills}   Relics found {r.RelicsFound}\nSecured {r.RelicsSecured} ({r.SecuredCount} secures, {r.ShardsSecured} shards)\n" +
                (r.RelicsLost > 0 || r.EchoShards > 0 ? $"<color=#ff8080>Lost {r.RelicsLost}</color>   Echo shards {r.EchoShards}\n" : "") +
                $"Peak delve {r.PeakHeat}   Bounties {r.BountiesDone}/{r.BountiesTotal}   Dream Level {r.LevelBefore} -> {r.LevelAfter}");
            int rooms = Workshop.RoomsToRecover(_s.Profile);
            string note = r.RelicsLost > 0
                ? Loc.T($"持ち帰れなかった遺物は「遺失物」として残ります。次の遠征で戦闘部屋を{rooms}つ突破すると、その中で一番良い物を1つ取り戻せます。", $"Lost relics wait in Lost & Found. Clear {rooms} combat rooms next expedition to recover the best one.")
                : null;
            const float width = 520;
            if (_reportHeightFor != r || _reportHeightJa != Loc.Japanese)
            {
                _reportHeightFor = r;
                _reportHeightJa = Loc.Japanese;
                _reportHeight = 40 + _st.Label.CalcHeight(new GUIContent(text), width - 28) + (note != null ? _st.Small.CalcHeight(new GUIContent(note), width - 28) + 6 : 0) + 30 + 34;
            }
            var rect = new Rect(w / 2 - width / 2, h * 0.16f, width, Mathf.Min(_reportHeight, h * 0.8f));
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.Label(r.Victory ? Loc.T("遠征の結果：夢を踏破しました", "Expedition: Conquered") : Loc.T("遠征の結果：夢から覚めました", "Expedition: Awakened"), _st.Title);
            GUILayout.Label(text, _st.Label);
            if (note != null) GUILayout.Label(note, _st.Small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("閉じる", "Close"), _st.Button, GUILayout.Height(30))) _reportDismissed = true;
            GUILayout.EndArea();
        }

        // ヒントの本文の高さ（ヒントと言語ごとに測って使い回す）。
        private Hint _hintMeasured;
        private bool _hintMeasuredJa;
        private float _hintBodyHeight = -1;

        private Rect HintRect(float w, float h)
        {
            bool welcome = _hints.Count > 0 && _hints[0] == Hint.Welcome;
            float pw = welcome ? 640 : 520;
            float ph = welcome ? 330 : 210;
            if (_hints.Count > 0 && _hintBodyHeight >= 0 && _hintMeasured == _hints[0] && _hintMeasuredJa == Loc.Japanese)
                ph = Mathf.Clamp(_hintBodyHeight + (welcome ? 100 : 95), 150, h - 60);
            return welcome ? new Rect((w - pw) / 2, (h - ph) / 2, pw, ph) : new Rect((w - pw) / 2, h - ph - 150, pw, ph);
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
            var rect = HintRect(w, h);
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.Label(UiStyles.Colored((welcome ? "" : Loc.T("ヒント：", "Tip: ")) + def.Title, "#ffe17a"), welcome ? _st.Title : _st.Header);
            string body = def.Body.ToString().Replace("[F6]", "[" + cfg.menuKey + "]").Replace("[F7]", "[" + cfg.secureKey + "]").Replace("[F8]", "[" + cfg.delveKey + "]");
            if (_hintBodyHeight < 0 || _hintMeasured != id || _hintMeasuredJa != Loc.Japanese)
            {
                _hintMeasured = id;
                _hintMeasuredJa = Loc.Japanese;
                _hintBodyHeight = _st.Label.CalcHeight(new GUIContent(body), rect.width - 28);
            }
            GUILayout.Label(body, _st.Label);
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
            // v1.25.2：本体の呪いの表示（画面の右側）と重ならないよう、左側の上部の表示の下に並べる。新しい物を上に、最大6件。
            float y = (Time.frameCount - _hudFrame <= 2 ? _hudBottom : 0) + 10;
            int shown = 0;
            for (int i = _toasts.Count - 1; i >= 0 && shown < 6; i--, shown++)
            {
                var t = _toasts[i];
                if (t.Content == null)
                {
                    t.Content = new GUIContent(t.Text);
                    t.W = Mathf.Min(_st.ToastMeasure.CalcSize(t.Content).x + 6, Mathf.Min(620, w * 0.45f));
                    t.H = _st.Toast.CalcHeight(t.Content, t.W);
                }
                GUI.Label(new Rect(10, y, t.W, t.H), t.Content, _st.Toast);
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
            GUILayout.Label(Loc.T("Dreamforge ─ 夢の遺物", "Dreamforge ─ Relics of the Dream"), _st.Title, GUILayout.Width(330));
            string[] tabs = { Loc.T("装備", "Gear"), Loc.T("鍛冶", "Forge"), Loc.T("星図", "Star Map"), Loc.T("工房", "Workshop"), Loc.T("記録", "Records") };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], i == _tab ? _st.TabSel : _st.Tab)) { _tab = i; _confirmSalvage = null; _confirmBulk = false; _retuneIndex = -1; }
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
            GUILayout.Label(_status != null && Time.unscaledTime < _statusUntil ? _status : " ", _st.Warn);
            GUILayout.EndArea();
        }

        private static readonly Txt HowToPlay = new Txt(
            "<b>このMODの目的</b>\n" +
            "本体の遠征に「持ち帰れる装備（遺物）」が加わります。遠征のたびに少しずつ装備を集めて鍛え、次の遠征をもっと深く、もっと楽に進めるようにしていきます。\n\n" +
            "<b>1回の遠征の流れ</b>\n" +
            "1. 敵を倒すと遺物が落ちます。協力プレイでも各自に別々に落ちるので、取り合いにはなりません。\n" +
            "2. 拾った物は、まだ持ち帰っていない状態（未確保）で鞄に入ります。鞄に入る数には上限があり、あふれると一番弱い物が欠片に変わります。\n" +
            "3. 新しいゾーンに着くと確保地点が開きます。ここで「確保する」か「深く潜る」かを選びます。\n" +
            "4. 確保した物は保管庫に入り、遠征が終わっても残ります。\n\n" +
            "<b>確保と潜行の考え方</b>\n" +
            "確保すれば安全ですが、深く潜ると遺物が多く、良い物が出やすくなります。そのぶん敵は強くなり、受けるダメージも増えます。全滅すると、まだ持ち帰っていない物は遺失物になり、次の遠征で戦闘部屋を" + Content.RoomsToRecoverLost + "つ突破すると一番良い物を1つだけ取り戻せます。手応えを見ながら、どこで確保するかを決めるのがこのMODの駆け引きです。\n\n" +
            "<b>装備の育て方</b>\n" +
            "・装備：旅人ごとに6つの枠（主装備・頭・防具・手・足・装飾品）に装着します。\n" +
            "・鍛冶：欠片で強化し（+3と+5で特性や固有効果が増えます）、調律石で特性を3つの候補から選び直します。いらない物は分解して欠片に戻せます。\n" +
            "・覚醒：固有品は、装着した旅人で敵を倒すと覚醒の力が溜まり、" + Content.AwakenThreshold + "で覚醒して固有効果が1.5倍になります。気に入った1本を使い込みましょう。\n" +
            "・星図：夢のレベルが上がるともらえるポイントで能力を伸ばします。条件を満たすと、強力な到達刻印を1つ選べます。\n" +
            "・工房：余った素材で、鞄や保管庫の拡張など、ずっと続く便利な強化を解放します。\n" +
            "・依頼：遠征ごとに3つ出ます。達成すると、欠片と経験値（依頼によっては調律石も）がもらえます。",
            "<b>What this mod adds</b>\n" +
            "Expeditions now drop gear you can keep (relics). Collect and improve a little every run so the next expedition goes deeper and smoother.\n\n" +
            "<b>One expedition</b>\n" +
            "1. Enemies drop relics. In co-op every player gets their own drops, so there is no fighting over loot.\n" +
            "2. What you pick up goes into your satchel, not yet secured. The satchel has a limit; when it overflows, the weakest relic turns into shards.\n" +
            "3. Each new zone opens a secure point where you choose to Secure or Delve.\n" +
            "4. Secured relics go to your stash and stay after the expedition ends.\n\n" +
            "<b>Securing vs. delving</b>\n" +
            "Securing is safe. Delving gives more and better relics, but enemies get tougher and you take more damage. If your party falls, unsecured loot becomes Lost & Found; clear " + Content.RoomsToRecoverLost + " combat rooms next expedition to recover the best piece. Deciding when to secure is the heart of this mod.\n\n" +
            "<b>Growing your gear</b>\n" +
            "- Gear: each Traveler has six slots: weapon, head, armor, hands, feet and charm.\n" +
            "- Forge: enhance with shards (+3 and +5 add an affix or a power), reroll an affix with tuning stones and pick from 3 options, salvage the rest into shards.\n" +
            "- Awakening: legendaries gather power as the Traveler wearing them defeats enemies; at " + Content.AwakenThreshold + " they awaken and their powers become 1.5x. Pick a favourite and keep using it.\n" +
            "- Star Map: spend points from Dream Levels to grow stats; meet the conditions to pick one powerful keystone.\n" +
            "- Workshop: unlock permanent upgrades shared by all Travelers.\n" +
            "- Bounties: 3 per expedition, rewarding shards, tuning stones and experience.");

        /// <summary>記録タブの偉業：受け取れる物 → 未達成（進み具合）→ 受け取り済み の順に並べる。</summary>
        private readonly List<string> _openFeats = new List<string>();
        private float _openFeatsUntil;

        /// <summary>未達成の偉業（種類ごとに次の1段だけ）の表示行。0.5秒ごとに作り直す。条件を満たしていれば、ここで達成にする。</summary>
        private List<string> OpenFeatLines(Profile p)
        {
            float now = Time.unscaledTime;
            if (now < _openFeatsUntil) return _openFeats;
            _openFeatsUntil = now + 0.5f;
            foreach (var e in Feats.Check(p)) _s.Emit(e);
            _openFeats.Clear();
            var seen = new HashSet<FeatKind>();
            foreach (var f in Feats.All)
            {
                if (p.Feats.Contains(f.Id) || !seen.Add(f.Kind)) continue;
                int prog = Math.Min(Feats.Progress(p, f), f.Target);
                string reward = f.RewardTuning > 0
                    ? Loc.T($"欠片{f.RewardShards}・調律石{f.RewardTuning}", $"{f.RewardShards} shards, {f.RewardTuning} tuning")
                    : Loc.T($"欠片{f.RewardShards}", $"{f.RewardShards} shards");
                _openFeats.Add("☆ " + f.Name + "  <color=#aab>" + Feats.Describe(f) + $"  {prog}/{f.Target}</color>  <color=#c9a86a>{reward}</color>");
            }
            return _openFeats;
        }

        private readonly List<string> _foundUniques = new List<string>();
        private int _foundUniquesCodex = -1;
        private bool _foundUniquesJa;

        private List<string> FoundUniques(Profile p)
        {
            if (p.Codex.Count == _foundUniquesCodex && _foundUniquesJa == Loc.Japanese) return _foundUniques;
            _foundUniquesCodex = p.Codex.Count;
            _foundUniquesJa = Loc.Japanese;
            _foundUniques.Clear();
            foreach (var u in Content.Uniques)
                if (p.Codex.Contains(u.Id)) _foundUniques.Add(UiStyles.Colored("◆ " + u.Name, UiStyles.RarityHex(Rarity.Legendary)));
            return _foundUniques;
        }

        private void DrawFeats(Profile p)
        {
            int done = p.Feats.Count, total = Feats.All.Count, unclaimed = Feats.Unclaimed(p);
            GUILayout.Label(Loc.T($"偉業（{done}／{total}）", $"Feats ({done}/{total})"), _st.Header);
            GUILayout.Label(Loc.T("遊ぶうちに達成していく目標です。達成すると欠片を受け取れます（段階が上がると調律石も）。",
                "Goals you complete as you play. Each one rewards shards (and tuning stones at higher tiers)."), _st.Small);
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
            var open = OpenFeatLines(p);
            foreach (var line in open) GUILayout.Label(line, _st.Small);
            int shown = open.Count;
            if (unclaimed == 0 && shown == 0)
                GUILayout.Label(Loc.T("すべての偉業を達成しました。", "Every feat is complete."), _st.Small);
            GUILayout.Label(Loc.T($"<color=#8a8aa0>受け取り済み：{p.FeatsClaimed.Count}個</color>", $"<color=#8a8aa0>Claimed: {p.FeatsClaimed.Count}</color>"), _st.Small);
        }

        /// <summary>記録タブ：夢の変種の一覧（どんな敵で、どう戦うか）。</summary>
        private void DrawVariantBook(Profile p)
        {
            GUILayout.Label(Loc.T($"夢の変種（倒した数 {p.Stats.VariantsSlain}）", $"Dream variants (slain {p.Stats.VariantsSlain})"), _st.Header);
            GUILayout.Label(Loc.T($"深度{Variants.MinDepth}以降、本体の敵がまれに強い「夢の変種」になって現れます（部屋に1体まで）。倒すと一段上の戦利品が出ます。",
                $"From delve {Variants.MinDepth}, base-game enemies sometimes appear as stronger dream variants (one per room). They drop loot a tier higher."), _st.Small);
            foreach (var v in Variants.All)
                GUILayout.Label(UiStyles.Colored(v.Name.ToString(), "#ffb347") + "  <color=#aab>" + v.Description + "</color>", _st.Small);
        }

        /// <summary>各タブの先頭に出す「ここでできること」。</summary>
        private static string TabIntro(int tab)
        {
            switch (tab)
            {
                case 0: return Loc.T("持ち帰った遺物を、旅人ごとに6つの枠（主装備・頭・防具・手・足・装飾品）へ装着します。遠征中は確保地点でだけ付け替えられます。",
                    "Equip relics you brought home into each Traveler's six slots (weapon, head, armor, hands, feet, charm). During an expedition you can only swap at secure points.");
                case 1: return Loc.T("欠片で遺物を強くし、調律石で気に入らない特性を引き直します。いらない遺物は分解して欠片に戻せます。",
                    "Use shards to enhance relics and tuning stones to reroll an affix you dislike. Salvage what you don't need back into shards.");
                case 2: return Loc.T("夢のレベルが上がるともらえるポイントで、旅人ごとに能力を伸ばします。振り直しは無料なので、気軽に試してください。",
                    "Spend the points you earn from Dream Levels to grow each Traveler. Respec is free, so feel free to experiment.");
                case 3: return Loc.T("余った欠片と調律石で、鞄や保管庫の拡張など、ずっと続く便利な強化を解放します。強さは上がりませんが、遠征がぐっと楽になります。",
                    "Spend spare shards and tuning stones on permanent conveniences such as a bigger satchel and stash. They don't make you stronger, but they make expeditions much easier.");
                default: return Loc.T("遊び方の確認、今回の遠征の様子、これまでの記録と図鑑を見られます。",
                    "Read how to play, check this expedition, and browse your records and codex.");
            }
        }

        private void FocusPicker()
        {
            var p = _s.Profile;
            GUILayout.Label(Loc.T("狙い系統：選んだ系統の遺物が2倍出やすくなります（遠征の前に選びます。「なし」なら今日の夢の系統が出やすくなります）", "Focus: relics of the chosen line drop twice as often (choose before an expedition; with None, today's dream decides)"), _st.Small);
            GUILayout.BeginHorizontal();
            GUI.enabled = p.Run == null || !_s.InGame;
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
                Rules.SetFocus(_s.Profile, l, _s.InGame && _s.Profile.Run != null);
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
                bool keyLocked = lv < HeroSigils.KeystoneMastery && HeroSigils.HasTree(hero);
                GUILayout.Label(Loc.T(
                    $"熟練度 {lv}「{Mastery.Title(lv)}」" + (next > 0 ? $" <color=#888>あと{next}体倒すと上がります</color>" : "")
                        + (keyLocked ? $"\n<color=#888>熟練度{HeroSigils.KeystoneMastery}で到達刻印を選べます。</color>" : ""),
                    $"Mastery {lv} \"{Mastery.Title(lv)}\"" + (next > 0 ? $" <color=#888>{next} kills to next</color>" : "")
                        + (keyLocked ? $"\n<color=#888>Keystones unlock at mastery {HeroSigils.KeystoneMastery}.</color>" : "")), _st.Small);
            }
            foreach (Slot slot in Content.SlotOrder)
            {
                var r = Rules.EquippedRelic(p, hero, slot);
                string label = Content.SlotName(slot) + "： " + (r != null ? UiStyles.RelicTitle(r) : Loc.T("<color=#777>（なし）</color>", "<color=#777>(empty)</color>"));
                GUILayout.BeginHorizontal();
                IconSlot(r, 30);
                if (GUILayout.Button(label, _slot == slot ? _st.RowSel : _st.Row, GUILayout.Height(30)))
                {
                    _slot = slot;
                    _selected = r?.Uid;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("現在の強さ", "Current build"), _st.Header);
            var build = _s.CurrentBuild(hero);
            _scrollDetail = GUILayout.BeginScrollView(_scrollDetail, GUILayout.Height(160));
            if (build.Stats.Count == 0 && build.Powers.Count == 0) GUILayout.Label(Loc.T("まだ何も装着していません。真ん中の一覧から遺物を選び、「装着する」を押してください。", "Nothing equipped yet. Pick a relic from the middle list and press Equip."), _st.Small);
            foreach (var kv in build.Stats) if (kv.Value != 0) GUILayout.Label(Content.FormatStat(kv.Key, kv.Value), _st.Small);
            foreach (var kv in build.Powers) GUILayout.Label(UiStyles.Colored(Content.FormatPower(kv.Key, kv.Value), "#e0b0ff"), _st.Small);
            foreach (var kv in build.Sets)
            {
                var set = Content.GetSet(kv.Key);
                if (set == null) continue;
                GUILayout.Label(UiStyles.Colored($"《{set.Name}》 {kv.Value}/3", "#ff8a3d") + "  <color=#ddd>" + set.Progress(kv.Value) + "</color>\n<color=#aab>" + set.Describe() + "</color>", _st.Small);
            }
            foreach (var kv in build.Lines)
            {
                if (kv.Value < 2) continue;
                string bonus = string.Join("・", Content.SetBonus(kv.Key, kv.Value).Select(x => Content.FormatStat(x.Stat, x.Value)));
                GUILayout.Label(UiStyles.Colored(Loc.T($"〈{Content.LineName(kv.Key)}×{kv.Value}〉{bonus}", $"<{Content.LineName(kv.Key)} x{kv.Value}> {bonus}"), "#9fe0c0"), _st.Small);
            }
            GUILayout.EndScrollView();
            if (!_s.CanEditLoadout)
                GUILayout.Label(Loc.T("遠征中は、確保地点に着いてから次の戦闘で敵を倒すまで、装備を変えられます。", "During an expedition, you can change gear from the moment you reach a secure point until you slay an enemy in the next fight."), _st.Warn);
            GUILayout.Label(Loc.T("同じ系統（破壊・生命・想像）の遺物を2・3・4・6個とそろえるほど、系統のボーナスが重なります。", "Equip 2, 3, 4 or 6 relics of the same line (Destruction, Life, Imagination) for stacking line bonuses."), _st.Small);
            FocusPicker();
            GUILayout.EndVertical();

            // 中：保管庫
            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(330));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Content.SlotOrder.Count; i++)
            {
                if (i == 3)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                var slot = Content.SlotOrder[i];
                if (GUILayout.Button(SlotTabLabel(p, slot), _slot == slot ? _st.ButtonSel : _st.Button)) _slot = slot;
            }
            GUILayout.EndHorizontal();
            RelicList(p.Stash.Where(r => r.Slot == _slot), hero, 410);
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
                var cur = Rules.EquippedRelic(p, hero, sel.Slot);
                _scrollRight = GUILayout.BeginScrollView(_scrollRight);
                RelicDetail(sel);
                if (cur != null && cur.Uid != sel.Uid) Comparison(sel, cur);
                GUILayout.EndScrollView();
                GUI.enabled = _s.CanEditLoadout && !_s.Trades.IsReserved(sel.Uid);
                GUILayout.BeginHorizontal();
                bool equipped = cur != null && cur.Uid == sel.Uid;
                if (!equipped && GUILayout.Button(Loc.T("装着する", "Equip"), _st.Button, GUILayout.Height(32)))
                {
                    foreach (var e in Rules.Equip(p, hero, sel.Uid, _s.Trades)) _s.Emit(e);
                    _s.MarkDirty(true);
                }
                if (equipped && GUILayout.Button(Loc.T("外す", "Unequip"), _st.Button, GUILayout.Height(32)))
                {
                    foreach (var e in Rules.Unequip(p, hero, sel.Slot)) _s.Emit(e);
                    _s.MarkDirty(true);
                }
                GUI.enabled = !_s.Trades.IsReserved(sel.Uid);
                if (GUILayout.Button(sel.Locked ? Loc.T("鍵を外す", "Unlock") : Loc.T("鍵をかける", "Lock"), _st.Button, GUILayout.Height(32)))
                {
                    Rules.ToggleLock(p, sel.Uid, _s.Trades);
                    _s.MarkDirty(false);
                }
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static string SlotTabLabel(Profile p, Slot slot)
        {
            int n = 0;
            foreach (var r in p.Stash) if (r.Slot == slot) n++;
            return n > 0 ? $"{Content.SlotName(slot)} <color=#9a9ab0>{n}</color>" : Content.SlotName(slot).ToString();
        }

        private void RelicList(IEnumerable<Relic> relics, string hero, float height)
        {
            var list = SortedCached(relics, height);
            var rows = RowTexts(list, hero, height);
            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.Height(height));
            if (list.Count == 0) GUILayout.Label(Loc.T("まだありません。遠征で敵を倒すと遺物が落ち、確保すると保管庫に入ります。", "Nothing here yet. Enemies drop relics on expeditions; secure them to bring them here."), _st.Small);
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                GUILayout.BeginHorizontal();
                IconSlot(r, 36);
                if (GUILayout.Button(rows[i], _selected == r.Uid ? _st.RowSel : _st.Row, GUILayout.Height(36)))
                {
                    _selected = r.Uid;
                    _seenUids.Add(r.Uid);
                    _retuneIndex = -1;
                    _confirmSalvage = null;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ───── 描画の使い回し（OnGUI は1フレームに何度も呼ばれるので、並べ替えは0.3秒ごとに1回だけ行う） ─────
        private Vector2 _scrollRight;
        private readonly Dictionary<float, List<Relic>> _sortedCache = new Dictionary<float, List<Relic>>();
        private readonly Dictionary<float, string> _sortedKey = new Dictionary<float, string>();
        private float _sortedUntil;
        private readonly List<Relic> _satchelTop = new List<Relic>();
        private float _satchelTopUntil;
        private int _satchelTopCount = -1;
        private readonly int[] _transmuteCounts = new int[5];
        private float _transmuteUntil;

        private string CacheStamp()
        {
            var p = _s.Profile;
            // 強化は並べ替えの基準（Score）も変えるので、鍵・覚醒・強化の状態も刻印に入れる。
            int state = 0;
            var stash = p.Stash;
            for (int i = 0; i < stash.Count; i++)
            {
                var r = stash[i];
                state = state * 31 + (r.Locked ? 1 : 0) + (r.Awakened ? 2 : 0) + r.Enhance * 4 + (int)r.Rarity * 64 + r.Powers.Count * 1024;
            }
            return p.Stash.Count + ":" + p.Material(Materials.Shard) + ":" + p.Material(Materials.Tuning) + ":" + state + ":" + _slot + ":" + _forgeAllSlots + ":" + HeroKey + ":" + Loc.Japanese;
        }

        /// <summary>一覧（高さで区別）の並べ替えを使い回す。中身・素材・遺物の状態・枠が変わるか、0.3秒たったら作り直す。</summary>
        private List<Relic> SortedCached(IEnumerable<Relic> relics, float id)
        {
            float now = Time.unscaledTime;
            string key = CacheStamp();
            if (now >= _sortedUntil)
            {
                _sortedUntil = now + 0.3f;
                _sortedKey.Clear();
            }
            if (_sortedKey.TryGetValue(id, out var k) && k == key && _sortedCache.TryGetValue(id, out var cached)) return cached;
            if (!_sortedCache.TryGetValue(id, out var list)) _sortedCache[id] = list = new List<Relic>();
            list.Clear();
            list.AddRange(relics);
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            _sortedKey[id] = key;
            return list;
        }

        // ───── 一覧の行の文字（★装着中・▲いまより強い・NEW 新しく手に入れた物・鍵） ─────
        private readonly Dictionary<float, List<string>> _rowCache = new Dictionary<float, List<string>>();
        private readonly Dictionary<float, List<Relic>> _rowCacheFor = new Dictionary<float, List<Relic>>();
        private readonly Dictionary<float, string> _rowCacheKey = new Dictionary<float, string>();
        private readonly HashSet<string> _seenUids = new HashSet<string>();
        private bool _seenInit;

        private List<string> RowTexts(List<Relic> list, string hero, float id)
        {
            var p = _s.Profile;
            if (!_seenInit)
            {
                // 起動したときに持っていた遺物は「見た」ことにする。それ以降に手に入れた物に NEW を付ける。
                _seenInit = true;
                foreach (var r in p.Stash) _seenUids.Add(r.Uid);
            }
            // 装着の変化（▲→★）と言語の切り替えでも作り直す。
            var h = p.Hero(hero);
            int equipHash = hero == null ? 0 : hero.GetHashCode();
            foreach (var u in h.Equipped) equipHash = equipHash * 31 + (u == null ? 0 : u.GetHashCode());
            // 行の文字は鍵・覚醒・強化も出るので、その状態が変わっても作り直す。
            int stateHash = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                stateHash = stateHash * 31 + (r.Locked ? 1 : 0) + (r.Awakened ? 2 : 0) + r.Enhance * 4 + (int)r.Rarity * 64 + r.Powers.Count * 1024;
            }
            string key = _sortedKey.TryGetValue(id, out var k) ? k + ":" + _selected + ":" + _seenUids.Count + ":" + equipHash + ":" + stateHash + (Loc.Japanese ? ":j" : ":e") : null;
            if (key != null && _rowCacheKey.TryGetValue(id, out var ck) && ck == key && _rowCacheFor.TryGetValue(id, out var forList) && forList == list && _rowCache.TryGetValue(id, out var cached))
                return cached;
            if (!_rowCache.TryGetValue(id, out var rows)) _rowCache[id] = rows = new List<string>();
            rows.Clear();
            foreach (var r in list)
            {
                bool equipped = h.Equipped.Contains(r.Uid);
                var cur = equipped ? null : Rules.EquippedRelic(p, hero, r.Slot);
                bool better = !equipped && (cur == null || r.Score > cur.Score);
                string mark = equipped ? "<color=#ffe17a>★</color> " : better ? "<color=#7cf07c>▲</color> " : "";
                string fresh = _seenUids.Contains(r.Uid) ? "" : " <color=#ffd24a><b>NEW</b></color>";
                string lck = r.Locked ? Loc.T(" <color=#aaa>[鍵]</color>", " <color=#aaa>[locked]</color>") : "";
                rows.Add($"{mark}{UiStyles.RelicTitle(r)} <color=#9a9ab0>Lv{r.ItemLevel}</color>{lck}{fresh}");
            }
            _rowCacheFor[id] = list;
            if (key != null) _rowCacheKey[id] = key;
            return rows;
        }

        // ───── まとめて分解（コモン・アンコモン。鍵・装着中・取引中・再調律中の物は使わない） ─────
        private bool _confirmBulk;

        private void BulkSalvageRow()
        {
            var p = _s.Profile;
            var parts = Rules.BulkSalvageCandidates(p, _s.Trades);
            int shards = 0;
            foreach (var r in parts) shards += Rules.SalvageValue(r);
            GUI.enabled = parts.Count > 0 && (p.Run == null || !_s.InGame);
            string label = _confirmBulk
                ? Loc.T($"<color=#ff8080>もう一度押すと、{parts.Count}個をまとめて分解します</color>", $"<color=#ff8080>Press again to salvage {parts.Count} relics</color>")
                : Loc.T($"コモンとアンコモンをまとめて分解（{parts.Count}個・欠片{shards}）", $"Salvage all Common and Uncommon ({parts.Count} relics, {shards} shards)");
            if (GUILayout.Button(label, _st.Button, GUILayout.Height(30)))
            {
                if (!_confirmBulk) _confirmBulk = true;
                else
                {
                    _confirmBulk = false;
                    var ev = Rules.BulkSalvage(p, _s.Trades, !_s.CanEditLoadout);
                    if (_selected != null && p.FindStash(_selected) == null) _selected = null;
                    _s.Emit(ev);
                    _s.MarkDirty(true);
                    _s.SaveNow();
                }
            }
            GUI.enabled = true;
            if (p.Run != null && _s.InGame) GUILayout.Label(Loc.T("まとめて分解は、遠征に出ていないときに使えます。", "Bulk salvage is available outside expeditions."), _st.Small);
        }

        private List<Relic> SatchelTop()
        {
            var run = _s.Profile.Run;
            float now = Time.unscaledTime;
            if (run == null) return _satchelTop;
            if (now < _satchelTopUntil && run.Satchel.Count == _satchelTopCount) return _satchelTop;
            _satchelTopUntil = now + 0.3f;
            _satchelTopCount = run.Satchel.Count;
            _satchelTop.Clear();
            _satchelTop.AddRange(run.Satchel);
            _satchelTop.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (_satchelTop.Count > 14) _satchelTop.RemoveRange(14, _satchelTop.Count - 14);
            return _satchelTop;
        }

        private int TransmuteCount(Rarity r)
        {
            float now = Time.unscaledTime;
            if (now >= _transmuteUntil)
            {
                _transmuteUntil = now + 0.3f;
                for (int i = 0; i < _transmuteCounts.Length; i++)
                    _transmuteCounts[i] = i < (int)Rarity.Legendary ? Rules.TransmuteCandidates(_s.Profile, (Rarity)i, _s.Trades).Count : 0;
            }
            return _transmuteCounts[(int)r];
        }

        /// <summary>アイコンの場所を確保して描く。アイコンが無い遺物・空の枠では場所だけ空ける（行の高さをそろえる）。</summary>
        private static void IconSlot(Relic r, float size)
        {
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (Event.current.type == EventType.Repaint) RelicIcons.Draw(rect, r);
        }

        private void RelicDetail(Relic r)
        {
            GUILayout.BeginHorizontal();
            IconSlot(r, 80);
            GUILayout.BeginVertical();
            GUILayout.Label("<size=19><b>" + UiStyles.RelicTitle(r) + "</b></size>", _st.Label);
            GUILayout.Label($"{Content.RarityName(r.Rarity)} · {Content.SlotName(r.Slot)} · {Content.LineName(r.Base.Line)} · Lv{r.ItemLevel}"
                + (r.Retunes > 0 ? Loc.T($" · 再調律{r.Retunes}/{Content.MaxRetunes}", $" · retuned {r.Retunes}/{Content.MaxRetunes}") : ""), _st.Small);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            var imp = r.Implicit;
            GUILayout.Label(UiStyles.Colored(Content.FormatStat(imp.Stat, imp.Value), "#c8c8ff") + Loc.T("  <color=#888>（この種類が必ず持つ性能）</color>", "  <color=#888>(always on this type)</color>"), _st.Label);
            // 固有効果は遺物の個性なので、特性より先に見せる。
            foreach (var pw in r.EffectivePowers()) GUILayout.Label(UiStyles.Colored(Content.FormatPower(pw.Power, pw.Value), "#e0b0ff"), _st.Label);
            foreach (var a in r.EffectiveStats().Skip(1)) GUILayout.Label(Content.FormatStat(a.Stat, a.Value), _st.Label);
            if (r.Rarity == Rarity.Legendary) GUILayout.Label(AwakenLine(r), _st.Small);
            if (r.UniqueId != null && Content.TryGetUnique(r.UniqueId, out var u))
            {
                if (u.SetId != null && Content.GetSet(u.SetId) is SetDef set)
                    GUILayout.Label(UiStyles.Colored($"《{set.Name}》", "#ff8a3d") + " <color=#aab>" + set.Describe() + "</color>", _st.Small);
                else
                    GUILayout.Label("<i>" + UiStyles.Colored(u.Lore.ToString(), "#c9a86a") + "</i>", _st.Small);
            }
        }

        /// <summary>固有品の覚醒の進み具合、または覚醒済みの印。</summary>
        private static string AwakenLine(Relic r)
        {
            int powerX = Content.AwakenPowerPct, affixX = Content.AwakenAffixPct;
            if (r.Awakened)
                return UiStyles.Colored(Loc.T($"✦ 覚醒済み：固有効果{powerX / 100f:0.#}倍・特性{affixX / 100f:0.#}倍", $"✦ Awakened: powers x{powerX / 100f:0.#}, affixes x{affixX / 100f:0.#}"), "#ffe17a");
            int now = r.AwakenPoints, need = Content.AwakenThreshold;
            int filled = Math.Max(0, Math.Min(10, now * 10 / need));
            string bar = "<color=#ffe17a>" + new string('■', filled) + "</color><color=#8a8aa0>" + new string('□', 10 - filled) + "</color>";
            return Loc.T(
                $"覚醒まで {bar} {now}/{need}\n<color=#8a8aa0>装着した旅人で敵を倒すと溜まります（エリート{Content.AwakenPoints(MonsterTier.MiniBoss, false)}・ボス{Content.AwakenPoints(MonsterTier.Boss, false)}・悪夢化は2倍）。覚醒すると固有効果が{powerX / 100f:0.#}倍、特性が{affixX / 100f:0.#}倍になります。</color>",
                $"Awakening {bar} {now}/{need}\n<color=#8a8aa0>Fills as the Traveler wearing it defeats enemies (elite {Content.AwakenPoints(MonsterTier.MiniBoss, false)}, boss {Content.AwakenPoints(MonsterTier.Boss, false)}, nightmares x2). Awakened: powers x{powerX / 100f:0.#}, affixes x{affixX / 100f:0.#}.</color>");
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
            for (int i = 0; i < Content.SlotOrder.Count; i++)
            {
                if (i == 3)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                var slot = Content.SlotOrder[i];
                if (GUILayout.Button(SlotTabLabel(p, slot), !_forgeAllSlots && _slot == slot ? _st.ButtonSel : _st.Button))
                {
                    _forgeAllSlots = false;
                    _slot = slot;
                }
            }
            GUILayout.EndHorizontal();
            RelicList(_forgeAllSlots ? p.Stash : p.Stash.Where(r => r.Slot == _slot), HeroKey, 430);
            BulkSalvageRow();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_st.Panel);
            // 6枠になって製作の行が増えたので、右の欄全体をスクロールできるようにする。
            _scrollForge = GUILayout.BeginScrollView(_scrollForge);
            var sel = p.FindStash(_selected);
            if (sel != null)
            {
                _scrollRight = GUILayout.BeginScrollView(_scrollRight, GUILayout.Height(260)); // 固有効果まで見えるように
                RelicDetail(sel);
                GUILayout.EndScrollView();
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUI.enabled = !_s.Trades.IsReserved(sel.Uid);
                if (sel.Enhance < Content.MaxEnhance)
                {
                    GUI.enabled = !_s.Trades.IsReserved(sel.Uid) && p.Material(Materials.Shard) >= Content.EnhanceCost(sel.Enhance);
                    if (GUILayout.Button(Loc.T($"強化 +{sel.Enhance + 1}（欠片{Content.EnhanceCost(sel.Enhance)}）", $"Enhance +{sel.Enhance + 1} ({Content.EnhanceCost(sel.Enhance)} shards)"), _st.Button, GUILayout.Height(32)))
                        Act(() => Rules.Enhance(p, sel.Uid, _s.Trades), true);
                }
                else GUILayout.Label(Loc.T($"強化は+{Content.MaxEnhance}が上限です", $"Enhancement is capped at +{Content.MaxEnhance}"), _st.Small);
                bool equippedSel = p.IsEquippedAnywhere(sel.Uid);
                bool salvageBlocked = sel.Locked || (equippedSel && !_s.CanEditLoadout);
                GUI.enabled = !_s.Trades.IsReserved(sel.Uid) && !salvageBlocked;
                int svShards = Rules.SalvageValue(sel), svTuning = Content.SalvageTuning(sel.Rarity);
                string svGain = Loc.T($"欠片{svShards}" + (svTuning > 0 ? $"・調律石{svTuning}" : ""), $"{svShards} shards" + (svTuning > 0 ? $", {svTuning} tuning" : ""));
                bool weighty = equippedSel || sel.Rarity >= Rarity.Epic || sel.AwakenPoints > 0;
                string sv = sel.Locked ? Loc.T("鍵を外すと分解できます", "Unlock to salvage")
                    : salvageBlocked ? Loc.T("装着中の物は確保地点で分解できます", "Equipped: salvage at a secure point")
                    : _confirmSalvage != sel.Uid ? Loc.T($"分解（{svGain}）", $"Salvage ({svGain})")
                    : weighty ? Loc.T($"<color=#ff8080>{(equippedSel ? "装着中の" : "")}「{sel.PlainName}」を分解します{(sel.Awakened ? "（覚醒も失われます）" : sel.AwakenPoints > 0 ? "（覚醒の力も失われます）" : "")}。もう一度押すと確定</color>", $"<color=#ff8080>Salvage {(equippedSel ? "equipped " : "")}\"{sel.PlainName}\"{(sel.AwakenPoints > 0 ? " (awakening is lost)" : "")}? Press again</color>")
                    : Loc.T("<color=#ff8080>もう一度押すと分解します</color>", "<color=#ff8080>Press again to salvage</color>");
                if (GUILayout.Button(sv, _st.Button, GUILayout.Height(32)))
                {
                    if (_confirmSalvage == sel.Uid)
                    {
                        Act(() => Rules.Salvage(p, sel.Uid, _s.Trades, !_s.CanEditLoadout), true);
                        _selected = null;
                        _confirmSalvage = null;
                    }
                    else _confirmSalvage = sel.Uid;
                }
                GUILayout.EndHorizontal();
                GUI.enabled = true;

                string nextMilestone = NextMilestone(sel);
                if (nextMilestone != null) GUILayout.Label(UiStyles.Colored(Loc.T("次の節目：", "Next milestone: ") + nextMilestone, "#ffd36e"), _st.Small);

                var offer = p.RetuneOffer;
                if (offer != null && offer.Uid == sel.Uid && offer.Index < sel.Affixes.Count)
                {
                    // 再調律の候補：3つから選ぶか、元のまま
                    var cur = sel.Affixes[offer.Index];
                    GUILayout.Label(Loc.T($"再調律の候補から1つ選んでください（いま：{Content.FormatStat(cur.Stat, cur.Value)}）", $"Pick a retune option (now: {Content.FormatStat(cur.Stat, cur.Value)})"), _st.Small);
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < offer.Options.Count; i++)
                    {
                        var o = offer.Options[i];
                        string mark = o.Stat == cur.Stat ? (o.Value > cur.Value ? " <color=#7cf07c>▲</color>" : o.Value < cur.Value ? " <color=#ff7a7a>▼</color>" : "") : "";
                        int choice = i;
                        if (GUILayout.Button(Content.FormatStat(o.Stat, o.Value) + mark, _st.ButtonSel, GUILayout.Height(32))) Act(() => Rules.ChooseRetune(p, choice), true);
                    }
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button(Loc.T("元のままにする（使った調律石は戻りません）", "Keep the original (tuning stones are not refunded)"), _st.Button, GUILayout.Height(28))) Act(() => Rules.ChooseRetune(p, -1), false);
                }
                else if (offer != null)
                {
                    GUILayout.Label(Loc.T("別の遺物で、再調律の候補を選んでいる途中です。", "Another relic has retune options waiting."), _st.Warn);
                    if (GUILayout.Button(Loc.T("その遺物を開く", "Open that relic"), _st.Button, GUILayout.Height(28)))
                    {
                        _forgeAllSlots = true;
                        _selected = offer.Uid;
                    }
                }
                else if (sel.Retunes >= Content.MaxRetunes)
                    GUILayout.Label(Loc.T($"この遺物の再調律は使い切りました（{Content.MaxRetunes}/{Content.MaxRetunes}）。", $"No retunes left on this relic ({Content.MaxRetunes}/{Content.MaxRetunes})."), _st.Small);
                else if (sel.Affixes.Count > 0)
                {
                    GUILayout.Label(Loc.T($"再調律：引き直したい特性を1つ選びます。調律石を払うと候補が{Content.RetuneChoices}つ出て、そこから選べます（この遺物はあと{Content.MaxRetunes - sel.Retunes}回）", $"Retune: pick an affix, pay tuning stones, then choose from {Content.RetuneChoices} options ({Content.MaxRetunes - sel.Retunes} left on this relic)"), _st.Small);
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < sel.Affixes.Count; i++)
                    {
                        var a = sel.Affixes[i];
                        if (GUILayout.Button(Content.FormatStat(a.Stat, a.Value), _retuneIndex == i ? _st.ButtonSel : _st.Button)) _retuneIndex = i;
                    }
                    GUILayout.EndHorizontal();
                    int rtCost = Content.RetuneCost(sel.Retunes);
                    bool rtAfford = p.Material(Materials.Tuning) >= rtCost;
                    GUI.enabled = _retuneIndex >= 0 && rtAfford && !_s.Trades.IsReserved(sel.Uid);
                    string rtLabel = !rtAfford ? Loc.T($"調律石が足りません（{rtCost}必要・所持{p.Material(Materials.Tuning)}）", $"Not enough tuning stones ({rtCost} needed, have {p.Material(Materials.Tuning)})")
                        : _retuneIndex < 0 ? Loc.T($"上の特性を1つ選んでください（調律石{rtCost}）", $"Pick an affix above ({rtCost} tuning)")
                        : Loc.T($"候補を出す（調律石{rtCost}）", $"Roll options ({rtCost} tuning)");
                    if (GUILayout.Button(rtLabel, _st.Button, GUILayout.Height(30)))
                    {
                        int idx = _retuneIndex;
                        Act(() => Rules.Retune(p, sel.Uid, idx, _s.Trades), true);
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
            GUILayout.Label(Loc.T("合成：同じレア度の遺物3つを、1つ上のレア度の遺物1つに変えます", "Transmute: turn 3 relics of one rarity into 1 of the next"), _st.Header);
            GUILayout.Label(Loc.T("鍵をかけた物・装着中の物は使わず、残りの中から弱い順に3つを使います。エピック3つからは固有品が生まれます。上の絞り込みで枠を選ぶと、結果をその枠にできます（欠片1.5倍）。",
                "Locked and equipped relics are never used; the 3 weakest of the rest go in. Three Epics become a legendary. Pick a slot filter above to choose the result's slot (1.5x shards)."), _st.Small);
            GUILayout.BeginHorizontal();
            foreach (Rarity r in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic })
            {
                int n = TransmuteCount(r);
                Slot? target = _forgeAllSlots ? (Slot?)null : _slot;
                int tcost = Rules.TransmuteCost(r, target != null);
                GUI.enabled = n >= 3 && p.Material(Materials.Shard) >= tcost;
                string label = UiStyles.Colored(Content.RarityName(r).ToString(), UiStyles.RarityHex(r)) + $" {n}/3\n"
                    + (target != null ? Loc.T($"→{Content.SlotName(target.Value)}・欠片{tcost}", $"→{Content.SlotName(target.Value)}, {tcost} shards") : Loc.T($"欠片{tcost}", $"{tcost} shards"));
                if (GUILayout.Button(label, _st.Button, GUILayout.Height(44))) Act(() => Rules.Transmute(p, r, _s.Trades, target), false);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T($"製作：欠片で新しい遺物を作ります（Lv{Math.Max(1, p.BestItemLevel)}＝これまでの最高）", $"Craft: make a new relic with shards (Lv{Math.Max(1, p.BestItemLevel)}, your best so far)"), _st.Header);
            foreach (Slot slot in Content.SlotOrder)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Content.SlotName(slot).ToString(), _st.Label, GUILayout.Width(70));
                GUI.enabled = p.Material(Materials.Shard) >= Rules.CraftShardCost(false);
                if (GUILayout.Button(Loc.T($"通常：アンコモン以上（欠片{Rules.CraftShardCost(false)}）", $"Basic: Uncommon+ ({Rules.CraftShardCost(false)} shards)"), _st.Button))
                    Act(() => Rules.Craft(p, slot, false), false);
                GUI.enabled = p.Material(Materials.Shard) >= Rules.CraftShardCost(true) && p.Material(Materials.Tuning) >= Rules.CraftTuningCost(true);
                if (GUILayout.Button(Loc.T($"上等：レア以上（欠片{Rules.CraftShardCost(true)}・調律石{Rules.CraftTuningCost(true)}）", $"Fine: Rare+ ({Rules.CraftShardCost(true)} shards, {Rules.CraftTuningCost(true)} tuning)"), _st.Button))
                    Act(() => Rules.Craft(p, slot, true), false);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>強化の次の節目（+3・+5）で何が起きるか。もう節目がなければ null。</summary>
        private static string NextMilestone(Relic r)
        {
            if (r.EnhanceMilestones < 1)
                return Loc.T($"+{Content.EnhanceMilestoneFirst}で特性が1行増えます。", $"+{Content.EnhanceMilestoneFirst}: one more affix.");
            if (r.EnhanceMilestones < 2)
                return r.Powers.Count == 0
                    ? Loc.T($"+{Content.EnhanceMilestoneSecond}でこの枠の固有効果が1つ宿ります。", $"+{Content.EnhanceMilestoneSecond}: gains a power for this slot.")
                    : Loc.T($"+{Content.EnhanceMilestoneSecond}で特性がもう1行増えます。", $"+{Content.EnhanceMilestoneSecond}: one more affix.");
            return null;
        }

        private Vector2 _scrollForge;

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
                $"使えるポイント：残り{Rules.FreePoints(p, hero)}／{p.TalentPoints}（夢のレベルで{p.TalentPoints - p.CodexBonusPoints}、図鑑で{p.CodexBonusPoints}。旅人ごとに別々に振れます）",
                $"Points: {Rules.FreePoints(p, hero)} free / {p.TalentPoints} ({p.TalentPoints - p.CodexBonusPoints} from Dream Level, {p.CodexBonusPoints} from the codex; allotted per Traveler)"), _st.Label);
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

            _scrollTalent = GUILayout.BeginScrollView(_scrollTalent);
            GUILayout.BeginHorizontal();
            if (HeroSigils.HasTree(hero))
            {
                // 手前の星 ｜ 奥の星 ｜ 到達刻印 の3列。
                var tree = HeroSigils.TreeFor(hero);
                int tier1 = Rules.Tier1Ranks(hs, hero);
                bool deepOpen = Rules.DeepStarsOpen(p, hero);

                GUILayout.BeginVertical(_st.Panel, GUILayout.Width(290));
                GUILayout.Label(Loc.T($"手前の星 <color=#aaa>({tier1})</color>", $"First stars <color=#aaa>({tier1})</color>"), _st.Header);
                GUILayout.Label(Loc.T("この旅人だけの刻印です。まずここに振ります。", "This Traveler's own sigils. Start here."), _st.Small);
                foreach (var t in tree) if (!t.IsKeystone && t.Tier == 1) DrawTalentNode(p, hero, hs, t);
                GUILayout.EndVertical();

                GUILayout.BeginVertical(_st.Panel, GUILayout.Width(350));
                GUILayout.Label(Loc.T("奥の星", "Deep stars"), _st.Header);
                GUILayout.Label(deepOpen
                    ? Loc.T("開いています。固有効果を少しずつ伸ばす星もあります。", "Open. Some deep stars grow a power, rank by rank.")
                    : UiStyles.Colored(Loc.T($"手前の星にあと{Content.DeepStarRequirement - tier1}ポイント振ると開きます（{tier1}/{Content.DeepStarRequirement}）。", $"Opens after {Content.DeepStarRequirement - tier1} more points in the first stars ({tier1}/{Content.DeepStarRequirement})."), "#ffb070"), _st.Small);
                foreach (var t in tree) if (!t.IsKeystone && t.Tier == 2) DrawTalentNode(p, hero, hs, t, deepOpen);
                GUILayout.EndVertical();

                GUILayout.BeginVertical(_st.Panel);
                int lv = Mastery.Level(hs.Kills);
                GUILayout.Label(Loc.T("到達刻印（どちらか1つ）", "Keystones (pick one)"), _st.Header);
                GUILayout.Label(Loc.T(
                    $"このツリーに{Content.KeystoneRouteRequirement}ポイント以上振り、熟練度を{HeroSigils.KeystoneMastery}以上にすると選べます（いまの熟練度は{lv}「{Mastery.Title(lv)}」）。遠征に出ていないときなら、いつでも付け替えられます。",
                    $"Needs {Content.KeystoneRouteRequirement}+ points in the tree and mastery {HeroSigils.KeystoneMastery}+ (now {lv} \"{Mastery.Title(lv)}\"). Switch freely outside expeditions."), _st.Small);
                foreach (var t in tree) if (t.IsKeystone) DrawTalentNode(p, hero, hs, t);
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
            GUILayout.EndScrollView();
        }

        private Vector2 _scrollTalent;

        /// <summary>到達刻印を選ぶのに、あと何が足りないか。</summary>
        private static string KeystoneMissing(HeroState hs, string hero, TalentDef t)
        {
            int need = Content.KeystoneRouteRequirement;
            if (t.HeroKey != null)
            {
                int ranks = 0;
                foreach (var kv in hs.Talents)
                    if (Content.TryGetTalent(kv.Key, out var x) && x.HeroKey == hero && !x.IsKeystone) ranks += kv.Value;
                int lv = Mastery.Level(hs.Kills);
                var ja = new List<string>();
                var en = new List<string>();
                if (ranks < need) { ja.Add($"{need - ranks}ポイント"); en.Add($"{need - ranks} more points in this tree"); }
                if (lv < HeroSigils.KeystoneMastery) { ja.Add($"熟練度{HeroSigils.KeystoneMastery}（いま{lv}）"); en.Add($"mastery {HeroSigils.KeystoneMastery} (now {lv})"); }
                return Loc.T("あと：" + string.Join("・", ja), "Needs " + string.Join(", ", en));
            }
            int route = Rules.RouteRanks(hs, t.Route);
            return Loc.T($"選ぶには：{Content.LineName(t.Route)}にあと{Math.Max(0, need - route)}ポイント", $"Needs {Math.Max(0, need - route)} more points in {Content.LineName(t.Route)}");
        }

        private void DrawTalentNode(Profile p, string hero, HeroState hs, TalentDef t, bool open = true)
        {
            if (t.IsKeystone)
            {
                GUILayout.Space(6);
                bool active = hs.Keystone == t.Id;
                bool unlocked = Rules.KeystoneUnlocked(p, hero, t);
                GUILayout.Label((active ? "<color=#ffe17a>◆</color> " : "◇ ") + "<b>" + t.Name + "</b>" + Loc.T("（到達刻印）", " (Keystone)"), _st.Label);
                GUILayout.Label(UiStyles.Colored(Content.FormatPower(t.Power, t.PowerValue), "#e0b0ff") + "\n<color=#aab>" + t.Description + "</color>", _st.Small);
                GUI.enabled = _s.CanEditTalents && (active || unlocked);
                string btn = active ? Loc.T("刻印を外す", "Remove")
                    : !unlocked ? KeystoneMissing(hs, hero, t)
                    : hs.Keystone != null ? Loc.T("こちらに付け替える（無料）", "Switch to this (free)")
                    : Loc.T($"刻印する（{Content.KeystoneCost}ポイント）", $"Engrave ({Content.KeystoneCost} points)");
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
            string pips = "<color=#ffd36e>" + new string('●', rank) + "</color><color=#8a8aa0>" + new string('○', Math.Max(0, t.MaxRank - rank)) + "</color>";
            string effect = t.IsPowerNode ? UiStyles.Colored(t.Describe(), "#e0b0ff") : "<color=#aab>" + t.Describe() + "</color>";
            GUILayout.Label($"<b>{t.Name}</b> {pips}\n{effect}", _st.Small, GUILayout.Width(t.Tier == 2 ? 270 : 210));
            GUI.enabled = open && _s.CanEditTalents && rank < t.MaxRank && Rules.FreePoints(p, hero) > 0;
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
                GUILayout.Label($"<b>{def.Name}</b>  <color=#ffd36e>{pips}</color>  {WorkshopValue(p, def.Id, lv < def.MaxLevel)}\n<color=#aab>{def.Description}</color>", _st.Small, GUILayout.Width(560));
                if (lv < def.MaxLevel)
                {
                    var cost = def.Costs[lv];
                    bool afford = p.Material(Materials.Shard) >= cost.Shards && p.Material(Materials.Tuning) >= cost.Tuning;
                    GUI.enabled = afford && (p.Run == null || !_s.InGame);
                    string label = Loc.T($"解放（欠片{cost.Shards}" + (cost.Tuning > 0 ? $"・調律石{cost.Tuning}" : "") + "）",
                        $"Unlock ({cost.Shards} shards" + (cost.Tuning > 0 ? $", {cost.Tuning} tuning" : "") + ")");
                    if (GUILayout.Button(label, _st.Button, GUILayout.Width(300), GUILayout.Height(40))) Act(() => Rules.BuyUpgrade(p, def.Id, _s.InGame && p.Run != null), false);
                    GUI.enabled = true;
                }
                else GUILayout.Label(Loc.T("すべて解放済み", "Maxed"), _st.Header, GUILayout.Width(300));
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.EndVertical();
            if (p.Run != null && _s.InGame) GUILayout.Label(Loc.T("遠征中は工房を使えません。遠征から戻ってから利用してください。", "The workshop is closed during expeditions."), _st.Warn);
        }

        /// <summary>工房の強化の「いまの値 → 解放後の値」。</summary>
        private static string WorkshopValue(Profile p, Upgrade u, bool canRaise)
        {
            int now, next;
            string ja, en;
            switch (u)
            {
                case Upgrade.BigSatchel: now = Workshop.SatchelCapacity(p); next = now + 5; ja = "鞄の上限"; en = "Satchel"; break;
                case Upgrade.WideStash: now = Workshop.StashCapacity(p); next = now + 20; ja = "保管庫の上限"; en = "Stash"; break;
                case Upgrade.BountyReroll: now = Workshop.RerollsPerRun(p); next = now + 1; ja = "引き直し"; en = "Rerolls"; break;
                default: return "";
            }
            string arrow = canRaise ? $" → <color=#7cf07c>{next}</color>" : "";
            return $"<color=#cfd3ee>{Loc.T(ja, en)} {now}{arrow}</color>";
        }

        private string _confirmDust;

        /// <summary>今日の夢が切り替わる時刻（世界時の0時）を、遊んでいる人の時計で。</summary>
        private static string DailyRollover() => DateTime.UtcNow.Date.AddDays(1).ToLocalTime().ToString("H:mm");

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
            GUILayout.Label(Loc.T($"<color=#8a8aa0>次は {DailyRollover()} に切り替わります。</color>", $"<color=#8a8aa0>Changes at {DailyRollover()}.</color>"), _st.Small);
            GUILayout.Label(Loc.T("記録", "Records"), _st.Header);
            GUILayout.Label(Loc.T(
                $"夢のレベル {p.DreamLevel}（{p.DreamXp}/{need}）\n遠征 {st.Runs}回　踏破 {st.Victories}　全滅 {st.Defeats}\n撃破 {st.Kills}　遺物 {st.RelicsFound}個（固有品 {st.LegendariesFound}）\n確保した最高潜行 {st.BestHeatSecured}　図鑑 {p.Codex.Count}/{Content.Bases.Count + Content.Uniques.Count}\n救済：次のボスでエピック以上が確定する確率 {(int)(Loot.EpicPityChance(p.EpicPity) * 100)}%（エピックが出ないボスを倒すたびに上がり、出ると元に戻ります。普段の抽選とは別です）",
                $"Dream Level {p.DreamLevel} ({p.DreamXp}/{need})\nRuns {st.Runs}  Victories {st.Victories}  Defeats {st.Defeats}\nKills {st.Kills}  Relics {st.RelicsFound} (legendary {st.LegendariesFound})\nBest secured depth {st.BestHeatSecured}  Codex {p.Codex.Count}/{Content.Bases.Count + Content.Uniques.Count}\nPity: next boss guarantees Epic+ at {(int)(Loot.EpicPityChance(p.EpicPity) * 100)}% (rises with each boss that drops no Epic, resets when one does; on top of the normal roll)"), _st.Small);
            if (p.Run != null)
            {
                GUILayout.Label(Loc.T($"今回の遠征（まだ持ち帰っていない遺物{p.Run.Satchel.Count}個）", $"This expedition ({p.Run.Satchel.Count} relics not yet secured)"), _st.Header);
                int rerolls = Rules.RerollsLeft(p);
                for (int i = 0; i < p.Run.Bounties.Count; i++)
                {
                    var b = p.Run.Bounties[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((b.Done ? "● " : "○ ") + b.Describe() + $"  {b.Progress}/{b.Target}  <color=#c9a86a>{b.RewardText(DailyDream.Get(p.Run.DailyId)?.BountyMult ?? 1.0)}</color>", _st.Small);
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
                    IconSlot(r, 28);
                    GUILayout.Label(UiStyles.RelicTitle(r) + $" <color=#9a9ab0>{Content.RarityName(r.Rarity)} Lv{r.ItemLevel}</color>", _st.Small);
                    GUI.enabled = !_s.Trades.IsReserved(r.Uid);
                    bool sure = _confirmDust == r.Uid;
                    string dustLabel = sure
                        ? Loc.T("<color=#ff8080>もう一度押すと分解</color>", "<color=#ff8080>Press again</color>")
                        : Loc.T($"ドリームダストに分解（+{Economy.SalvageDust(r)}）", $"Salvage for Dream Dust (+{Economy.SalvageDust(r)})");
                    if (GUILayout.Button(dustLabel, _st.Button, GUILayout.Width(190)))
                    {
                        if (!sure) _confirmDust = r.Uid;
                        else
                        {
                            _confirmDust = null;
                            string err = _s.SalvageUnsecured(r.Uid);
                            if (err != null) SetStatus(err);
                        }
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label(Loc.T($"遺失物（{p.LostAndFound.Count}/{Content.LostAndFoundCapacity}）", $"Lost & Found ({p.LostAndFound.Count}/{Content.LostAndFoundCapacity})"), _st.Header);
            if (p.LostAndFound.Count == 0) GUILayout.Label(Loc.T("なし", "None"), _st.Small);
            foreach (var r in p.LostAndFound.OrderByDescending(r => r.Score)) GUILayout.Label("· " + UiStyles.RelicTitle(r) + $" Lv{r.ItemLevel}", _st.Small);
            DrawFeats(p);
            DrawVariantBook(p);
            GUILayout.Label(Loc.T("固有品図鑑", "Legendary codex"), _st.Header);
            var found = FoundUniques(p);
            foreach (var line in found) GUILayout.Label(line, _st.Small);
            int foundUniques = found.Count;
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
