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
    internal sealed partial class DreamforgeUi
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

        // 連携（v1.26）の印に見る枠。Identity と Movement も記憶の対象になる。
        private static readonly HeroSkillLocation[] LinkSkillSlots =
            { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R, HeroSkillLocation.Identity, HeroSkillLocation.Movement };

        private readonly ClientSession _s;
        private readonly Func<DreamforgeConfig> _cfg;
        private readonly UiStyles _st = new UiStyles();
        private readonly List<Toast> _toasts = new List<Toast>();

        private int _tab;
        private Slot _slot = Slot.Weapon;
        private string _selected;
        private string _heroSel;
        private Vector2 _scrollList, _scrollRecords;
        private long _infinityRecordsRevision;
        private Profile _infinityRecordsProfile;
        private bool _infinityRecordsJapanese;
        private string _infinityRecordsText;
        private int _retuneIndex = -1;
        private string _confirmSalvage;
        private string _confirmAffixReroll;
        private Relic _confirmEnhance;
        private int _confirmEnhanceTarget;
        private bool _forgeAllSlots = true;
        private string _status;
        private float _statusUntil;
        // 連携（v1.26）：ローカルの旅人の装着は0.5秒に1回だけ見る（OnGUI は1フレームに何度も走るため）。
        private readonly HashSet<string> _linkMemories = new HashSet<string>();
        private readonly HashSet<string> _linkEssences = new HashSet<string>();
        private readonly HashSet<string> _linkAllies = new HashSet<string>();
        private string _linkHeroKey;
        private Func<string, bool> _linkMarks;
        private float _nextLinkCheck;

        public bool Open { get; private set; }

        /// <summary>直近の描画で、マウスが確保地点のパネルの上にあったか（クリックをゲームへ通さないため）。</summary>
        public bool MouseOverPanel { get; private set; }

        public DreamforgeUi(ClientSession session, Func<DreamforgeConfig> cfg)
        {
            _s = session;
            _cfg = cfg;
            _s.ProfileChanged += ResetProfileView;
            // 起動したときに持っていた遺物は「見た」ことにする。それ以降に手に入れた物に NEW を付ける。
            foreach (var r in session.Profile.Stash) _seenUids.Add(r.Uid);
            _seenInit = true;
        }

        private bool _secureHidden;
        private RunState _securePanelRun;
        private long _securePanelSegment;

        // #227: soul completion creates floor loot; it does not mean players collected it.
        // Visibility is local only. Keep the shared choice/reward receipts untouched.
        private void UpdateSecurePanel()
        {
            var run = _s.Profile.Run;
            bool waiting = _s.InGame && run != null && run.AwaitingChoice && _s.ActiveRunId != null;
            if (!waiting)
            {
                _securePanelRun = null;
                _secureHidden = false;
                return;
            }
            long segment = run.Infinity?.SegmentEpoch ?? 0;
            if (ReferenceEquals(run, _securePanelRun) && segment == _securePanelSegment) return;
            _securePanelRun = run;
            _securePanelSegment = segment;
            _secureHidden = run.Infinity != null;
        }

        /// <summary>確保地点の画面を隠す／出す。通常は自動表示、インフィニティのボス後は回収のため折りたたんで待つ。</summary>
        public void ToggleSecurePanel()
        {
            UpdateSecurePanel();
            _secureHidden = !_secureHidden;
        }

        public void Toggle()
        {
            Open = !Open;
            if (!Open) CancelStarDrag();
            _confirmSalvage = null;
            _confirmAffixReroll = null;
        }

        public void Close()
        {
            CancelStarDrag();
            _confirmEnhance = null;
            Open = false;
        }

        public void Dispose()
        {
            _s.ProfileChanged -= ResetProfileView;
            _st.Dispose();
        }

        public void Notify(GameEvent e)
        {
            if (e == null) return;
            if (e.SatchelOverflowCount > 0)
            {
                _satchelTopUntil = 0;
                _satchelTopCount = -1;
                _codex.Invalidate();
            }
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
                if (_heroSel == null)
                {
                    foreach (string key in _s.Profile.Heroes.Keys)
                        if (key.StartsWith("Hero_", StringComparison.Ordinal)) { _heroSel = key; break; }
                    if (_heroSel == null) _heroSel = KnownHeroes[0];
                }
                return _heroSel;
            }
        }

        /// <summary>
        /// 連携（v1.26）の条件の ✓／・ の判定。ゲームの中ではローカルの旅人の装着と比べ、
        /// 0.5秒に1回までしか集め直さない。ゲームの外では null（印を付けない）。
        /// </summary>
        private Func<string, bool> LinkMarks()
        {
            var hero = _s.LocalHero;
            if (hero == null)
            {
                _linkMarks = null;
                _linkHeroKey = null;
                return null;
            }
            string key = ClientSession.HeroKeyOf(hero);
            float now = Time.unscaledTime;
            if (_linkMarks == null || key != _linkHeroKey || now >= _nextLinkCheck)
            {
                _nextLinkCheck = now + 0.5f;
                _linkHeroKey = key;
                _linkMemories.Clear();
                _linkEssences.Clear();
                _linkAllies.Clear();
                if (hero.Skill != null)
                {
                    foreach (var loc in LinkSkillSlots)
                    {
                        var skill = hero.Skill.GetSkill(loc);
                        if (skill != null) _linkMemories.Add(skill.GetType().Name);
                    }
                    foreach (var kv in hero.Skill.gems)
                        if (kv.Value != null) _linkEssences.Add(kv.Value.GetType().Name);
                }
                var memories = _linkMemories;
                var essences = _linkEssences;
                // Replicated positions are a display estimate; the host applies the authoritative 10 m check.
                var actors = NetworkedManagerBase<ActorManager>.softInstance;
                if (actors != null)
                    foreach (var ally in actors.allHeroes)
                    {
                        if (ally == null || ally == hero) continue;
                        if (Links.IsBondAlly(ally.isActive && ally.isAlive && !ally.isKnockedOut,
                            ally.GetRelation(hero) == EntityRelation.Ally,
                            Vector3.Distance(hero.agentPosition, ally.agentPosition)))
                            _linkAllies.Add(ally.GetType().Name);
                    }
                _linkMarks = t => Links.RequirementSatisfied(t, key, memories, essences, _linkAllies);
            }
            return _linkMarks;
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

        private static string HeroName(string key) => HeroNames.Display(key);

        // ─────────────────────────── 描画の入口 ───────────────────────────

        public void Draw()
        {
            ValidateEnhanceConfirmation();
            _st.EnsureBuilt();
            var cfg = _cfg();
            float scale = Mathf.Clamp(Screen.height / 1080f * Mathf.Clamp(cfg.uiScale, 0.5f, 2.5f), 0.5f, 4f);
            // Keep the star map's fixed header rows and two choice cards within the visible window,
            // including both optional sidebars. Honor the configured scale until it would clip that viewport.
            if (Open && _tab == 2) scale = Mathf.Min(scale, Mathf.Min(Screen.width / 1600f, Screen.height / 960f));
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
                UpdateSecurePanel();
                var zm = NetworkedManagerBase<ZoneManager>.softInstance;
                bool transition = zm != null && zm.isInAnyTransition;
                if (_s.InGame && !transition)
                {
                    if (repaint)
                    {
                        DrawNightmareLabels(scale);
                        if (cfg.hudMode != HudMode.Off) DrawHud(w, h, cfg);
                    }
                    bool secureWait = _s.Profile.Run != null && _s.Profile.Run.AwaitingChoice && _s.ActiveRunId != null;
                    if (layout && !Open && secureWait)
                    {
                        if (_secureHidden) DrawSecureCollapsed(w, cfg);
                        else DrawSecurePrompt(w, h, cfg);
                    }
                }
                if (repaint) DrawToasts(w, h);
                // メニューを開いている間は、確保地点と遠征結果のパネルを隠す（重なった下のボタンを押せないように）。
                if (layout && !Open && _s.Profile.LastReport != null && (_s.Profile.LastReport != _shownReport || !_reportDismissed)) DrawReport(w, h);
                // ヒントの上にマウスがあるときは、下にあるメニューのボタンへマウスの操作を渡さない。
                // ただし星図のドラッグ中の移動・解放は座標を入れ替えない。ドラッグはホットコントロールを持つので
                // 下のボタンは押せず、入れ替えた座標でパンを計算すると図が画面の外へ飛んでしまう（#45）。
                bool mouseEvent = type == EventType.MouseDown || type == EventType.MouseUp || type == EventType.MouseDrag || type == EventType.ScrollWheel;
                Vector2 realMouse = Event.current.mousePosition;
                bool starDragGesture = _starDragging && (type == EventType.MouseDrag || type == EventType.MouseUp);
                bool shield = mouseEvent && !starDragGesture && _hints.Count > 0 && HintRect(w, h).Contains(realMouse);
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
            sb.Append(Loc.T("<b>Dreamforge</b>  夢のレベル ", "<b>Dreamforge</b>  Dream Lv ")).Append(p.DreamLevel).Append("  <color=#c4c4dc>(").Append(xp).Append(")</color>");
            sb.Append("\n<size=13>").Append(Loc.T("夢の深さ ", "Dream depth ")).Append(_s.ChosenDreamDepth)
                .Append(Loc.T("・夢の圧 ", " · Pressure "));
            if (_s.HostConfirmed)
                sb.Append("HP×").Append(_s.PressureHealthMultiplier.ToString("0.00"))
                    .Append(Loc.T("・攻×", " · ATK×")).Append(_s.PressureDamageMultiplier.ToString("0.00"));
            else sb.Append(Loc.T("ホストの確認待ち", "awaiting host"));
            sb.Append("</size>");
            if (run?.Infinity != null)
            {
                var infinity = run.Infinity;
                sb.Append("\n<size=13>").Append(Loc.T("インフィニティ · クリア ", "Infinity · cleared "))
                    .Append(infinity.ClearedCombatTotal).Append(Loc.T("部屋 · 周期 ", " rooms · cycle "))
                    .Append(infinity.ClearsInCycle).Append('/').Append(infinity.Interval)
                    .Append(Loc.T(" · 圧段階 ", " · pressure stage ")).Append(infinity.PressureStage);
                if (infinity.PressureStage == InfinityRunState.MaximumPressureStage) sb.Append(Loc.T("（上限）", " (cap)"));
                sb.Append("</size>");
                // #144: host/solo judge only their own Infinity availability; participants wait
                // for the host's answer instead of being told "disabled" without a reason.
                string supportNotice = ClientSession.InfinitySupportNotice(_s.CanChooseRunRules);
                if (supportNotice != null)
                    sb.Append("\n<color=#ffb070>").Append(supportNotice).Append("</color>");
                else if (!InfinityMode.NativeSaveAgreement)
                    sb.Append("\n<color=#ffb070>").Append(Loc.T("保存不一致：進行停止・回復待ち", "Save mismatch: progression paused; recovery required")).Append("</color>");
            }
            string sectionTags = SectionTagNotice();
            if (sectionTags != null)
                sb.Append("\n<size=13><color=#ffd27f>").Append(sectionTags).Append("</color></size>");
            // 版違いは見落とすと「効果も報酬も入らない」ので、目立つ色で常に出す（mp-ui-save #4）。
            if (_s.HostVersionWarning != null)
                sb.Append("\n<size=13><color=#ff7070>").Append(_s.HostVersionWarning).Append("</color></size>");
            var hostWarnings = HostAuthority.VersionWarnings;
            for (int i = 0; i < hostWarnings.Count; i++)
                sb.Append("\n<size=13><color=#ff7070>").Append(hostWarnings[i]).Append("</color></size>");
            bool compact = cfg.hudMode == HudMode.Compact;
            if (run != null && _s.ActiveRunId != null)
            {
                var waypoint = Waypoints.Get(run.ActiveWaypoint);
                if (waypoint != null)
                    sb.Append("\n<size=13><color=#a8e9cd>").Append(Loc.T("道標：", "Waypoint: ")).Append(waypoint.Name).Append("</color></size>");
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
                if (run.ActiveWaypoint == Waypoint.BossHoard)
                {
                    sb.Append("\n<size=13><color=#a8e9cd>");
                    if (run.WaypointHoardReleased)
                        sb.Append(Loc.T("宝庫：開封済み・撃破戦利品 ×3", "Hoard: opened · kill loot ×3"));
                    else
                        sb.Append(Loc.T(
                            $"宝庫保留（倍率前）：遺物{run.DeferredWaypointRelics.Count}・欠片{run.DeferredWaypointShards}・調律石{run.DeferredWaypointTuning}\nボス撃破で払い出し ×3・未開封で離れると失う",
                            $"Hoard held (before ×3): {run.DeferredWaypointRelics.Count} relics, {run.DeferredWaypointShards} shards, {run.DeferredWaypointTuning} tuning\nBoss defeat pays ×3; leaving unopened forfeits it"));
                    sb.Append("</color></size>");
                }
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
                    if (b.Done) sb.Append("<color=#7af0c8>●</color><color=#aaa>").Append(b.Describe()).Append("</color>");
                    else sb.Append("○").Append(b.Describe()).Append(" <color=#c4c4dc>").Append(b.Progress).Append('/').Append(b.Target).Append("</color>");
                    sb.Append("</size>");
                }
                AppendRunGrowthHud(sb);
                if (!_s.HostConfirmed && _s.LocalHero != null)
                    sb.Append("\n<size=13>").Append(Loc.T("装備の効果がまだ反映されていません（ホストがこのMODを入れていない可能性があります）", "Gear bonuses not applied yet (the host may not have this mod)")).Append("</size>");
            }
            int unclaimedFeats = Feats.Unclaimed(p);
            if (unclaimedFeats > 0)
                sb.Append("\n<size=13><color=#ffe17a>").Append(Loc.T($"★ 偉業の報酬を{unclaimedFeats}件受け取れます（記録タブ）", $"★ {unclaimedFeats} feat reward(s) to claim (Records tab)")).Append("</color></size>");
            sb.Append("\n<size=13><color=#c4c4dc>[").Append(cfg.menuKey).Append(Loc.T("] メニュー", "] Menu")).Append("</color></size>");
            return sb.ToString();
        }

        /// <summary>v1.32 B：遠征の鍛錬のスタックを小さな1行ずつ（旅人が持つ RunGrowth ごと）。</summary>
        private void AppendRunGrowthHud(System.Text.StringBuilder sb)
        {
            var hero = _s.LocalHero;
            if (hero == null) return;
            var build = _s.CurrentBuild(ClientSession.HeroKeyOf(hero));
            if (build == null) return;
            foreach (var entry in build.RunGrowths)
                sb.Append("\n<size=13><color=#9fe3a0>").Append(RunGrowth.HudLine(entry, _s.GrowthStacks(entry.StarId))).Append("</color></size>");
        }

        /// <summary>
        /// いまの区画に弱点・耐性を持つ変種がいるときの一行知らせ（例：「この区画：火に弱い敵がいる」）。
        /// 既存の変種通知（DreamforgeVariantMsg → ClientSession.Variant）の結果から作るので、新しい通信は要らない。
        /// </summary>
        private string SectionTagNotice()
        {
            if (_s.Variant.Count == 0) return null;
            var hero = _s.LocalHero;
            var section = hero != null ? hero.section : null;
            if (section == null) return null;
            VariantTag tags = VariantTag.None;
            foreach (var kv in _s.Variant)
            {
                var def = Variants.Get(kv.Value);
                if (def == null || def.Tags == VariantTag.None) continue;
                if (!Mirror.NetworkClient.spawned.TryGetValue(kv.Key, out var id) || id == null) continue;
                var m = LabelMonster(kv.Key, id);
                if (m == null || !m.isActive || m.section != section) continue;
                tags |= def.Tags;
            }
            return tags == VariantTag.None ? null : Variants.ZoneNotice(tags);
        }

        private (int Menu, int Secure, int Delve, int Panel, bool Japanese) _secureButtonKey;
        private string _secureOpenButton, _secureButton, _delveButton, _gearButton, _secureHideButton;
        private string _infinityOpenButton, _infinityBackButton;

        private void CacheSecureButtons(DreamforgeConfig cfg)
        {
            var key = ((int)cfg.menuKey, (int)cfg.secureKey, (int)cfg.delveKey, (int)cfg.securePanelKey, Loc.Japanese);
            if (_secureOpenButton != null && _secureButtonKey.Equals(key)) return;
            _secureButtonKey = key;
            _secureOpenButton = Loc.T($"確保地点を開く [{cfg.securePanelKey}]　（確保 [{cfg.secureKey}]・潜行 [{cfg.delveKey}]）",
                $"Open secure point [{cfg.securePanelKey}]  (Secure [{cfg.secureKey}] · Delve [{cfg.delveKey}])");
            _secureButton = Loc.T($"確保する [{cfg.secureKey}]", $"Secure [{cfg.secureKey}]");
            _delveButton = Loc.T($"深く潜る [{cfg.delveKey}]", $"Delve [{cfg.delveKey}]");
            _gearButton = Loc.T($"装備を整える [{cfg.menuKey}]", $"Gear up [{cfg.menuKey}]");
            _secureHideButton = Loc.T($"画面を隠す [{cfg.securePanelKey}]", $"Hide [{cfg.securePanelKey}]");
            _infinityOpenButton = Loc.T($"回収完了：継続／帰還を選ぶ [{cfg.securePanelKey}]", $"Loot collected: choose Delve / Return [{cfg.securePanelKey}]");
            _infinityBackButton = Loc.T($"戻る（拾いに行く） [{cfg.securePanelKey}]", $"Back to collect loot [{cfg.securePanelKey}]");
        }

        /// <summary>確保地点の画面を隠している間の、小さな呼び出しボタン。</summary>
        private void DrawSecureCollapsed(float w, DreamforgeConfig cfg)
        {
            CacheSecureButtons(cfg);
            bool infinity = _s.Profile.Run?.Infinity != null;
            var rect = infinity ? new Rect(w / 2 - 280, 12, 560, 76) : new Rect(w / 2 - 190, 12, 380, 44);
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            if (infinity)
                GUILayout.Label(Loc.T("記憶・エッセンスを拾ってから開いてください。進行はホストが決定します。",
                    "Collect memories and essences first. The host decides when to proceed."), _st.Small);
            if (GUILayout.Button(infinity ? _infinityOpenButton : _secureOpenButton, _st.Button, GUILayout.Height(28)))
                _secureHidden = false;
            GUILayout.EndArea();
        }

        private (int Count, int Shards, int Tuning, int Heat, int Bonus, int Free, double NightmareMult, bool Japanese) _secureTextKey;
        private string _secureLootText, _secureBonusText, _secureOverflowText, _secureDelveText, _secureNightmareText, _secureExtraText;

        private void CacheSecureText(RunState run, int bonus, int free, double nmMult)
        {
            var key = (run.Satchel.Count, run.SatchelShards, run.SatchelTuning, run.Heat, bonus, free, nmMult, Loc.Japanese);
            if (_secureLootText != null && _secureTextKey.Equals(key)) return;
            _secureTextKey = key;
            int next = Math.Min(Content.MaxHeat, run.Heat + 1);
            bool atCap = run.Heat >= Content.MaxHeat;
            _secureLootText = Loc.T(
                $"まだ持ち帰っていない物：遺物{run.Satchel.Count}個、欠片{run.SatchelShards}、調律石{run.SatchelTuning}",
                $"Not yet secured: {run.Satchel.Count} relics, {run.SatchelShards} shards, {run.SatchelTuning} tuning");
            _secureExtraText = run.Satchel.Count > 14 ? $"+{run.Satchel.Count - 14}" : null;
            _secureBonusText = UiStyles.Colored(Loc.T("確保する：", "Secure: "), "#7af0c8") + Loc.T(
                $"手に入れた物がすべて保管庫に入り、この先で全滅しても失いません。" + (bonus > 0 ? $"潜った分のボーナスとして欠片が{bonus}増えます。" : "") + "潜行は0に戻ります。",
                $"Everything you carry goes to your stash and is safe even if you fall later." + (bonus > 0 ? $" Your delve bonus adds {bonus} shards." : "") + " Delve resets to 0.");
            _secureOverflowText = run.Satchel.Count > free ? Loc.T(
                $"保管庫の空きは{free}個です。入りきらない{run.Satchel.Count - free}個は、弱い物から欠片になります。",
                $"Your stash has room for {free}. The weakest {run.Satchel.Count - free} will become shards.") : null;
            _secureDelveText = UiStyles.Colored(Loc.T("深く潜る：", "Delve: "), "#ffb070") + Loc.T(
                (atCap ? $"持ち帰らずに次のゾーンへ進みます。潜行は{next}のままです（これ以上は深くなりません）。" : $"持ち帰らずに次のゾーンへ進み、潜行が{next}になります。")
                + $"遺物の出る量が{(int)(Loot.HeatDropBonus * 100 * next)}%増えてレア度も上がりますが、受けるダメージも{Build.DamageTakenPerDelvePct * next}%増えます。全滅すると、まだ持ち帰っていない物は遺失物になります。",
                (atCap ? $"Move on without securing; delve stays at {next} (the maximum)." : $"Move on without securing; delve becomes {next}.")
                + $" Relic drops +{(int)(Loot.HeatDropBonus * 100 * next)}% with better rarity, but you take {Build.DamageTakenPerDelvePct * next}% more damage. If your party falls, unsecured loot becomes Lost & Found.");
            int nmNormal = (int)(Math.Min(1.0, Nightmares.Chance(MonsterTier.Normal, next) * nmMult) * 100);
            int nmElite = (int)(Math.Min(1.0, Nightmares.Chance(MonsterTier.MiniBoss, next) * nmMult) * 100);
            string nmDay = nmMult > 1.0 ? Loc.T("（今日の夢で増えています）", " (raised by today's dream)") : "";
            _secureNightmareText = UiStyles.Colored(Loc.T(
                $"潜行{next}では、通常の敵の{nmNormal}%、エリートの{nmElite}%が「悪夢化」して強くなります{nmDay}（ボスは対象外）。倒すと、一段上の戦利品が出ます。",
                $"At delve {next}, {nmNormal}% of regular enemies and {nmElite}% of elites become stronger nightmares{nmDay} (bosses excluded). They drop loot a tier higher."), "#ff9ae0");
        }

        private void DrawSecurePrompt(float w, float h, DreamforgeConfig cfg)
        {
            CacheSecureButtons(cfg);
            var run = _s.Profile.Run;
            var rect = new Rect(w / 2 - 320, 80, 640, 330 + (run.Satchel.Count > 0 ? 42 : 0) + (run.OfferedPacts.Count > 0 ? 34 + 56 * run.OfferedPacts.Count : 0)
                + (run.OfferedEvent != DreamEvent.None ? 84 : 0) + (_s.HasPendingTrades || _s.HasHeldTrades ? 24 : 0)
                + 24 + (run.OfferedWaypoints.Count > 0 ? 250 : 44));
            // 前のフレームで測った中身の高さがあれば、それに合わせる（余白も、はみ出しも出さない）。
            if (_secureMeasured > 0) rect.height = _secureMeasured + 28;
            if (rect.height > h - 100) rect.height = h - 100;
            if (rect.Contains(Event.current.mousePosition)) MouseOverPanel = true;
            GUILayout.BeginArea(rect, _st.Window);
            if (run.Infinity != null && GUILayout.Button(_infinityBackButton, _st.Button, GUILayout.Height(34)))
                _secureHidden = true;
            _scrollSecure = GUILayout.BeginScrollView(_scrollSecure);
            GUILayout.Label(Loc.T("確保地点 ─ ここで持ち帰るか、さらに潜るかを選びます", "Secure Point ─ take your loot home, or delve deeper"), _st.Title);
            DrawWaypointPicker(run);
            if (run.Infinity != null)
            {
                GUILayout.Label(Loc.T("インフィニティ：確保は全員帰還して終了、潜行は全員続行。ホストが決定します。",
                    "Infinity: Secure returns everyone and ends the run; Delve continues for everyone. The host decides."), _st.Warn);
                DrawInfinityCaps();
            }
            int bonus = run.SatchelShards * run.Heat / EconomyBalance.SecureBonusDivisor;
            if (Pacts.Sum(run.Pacts).DoubleDepthBonus) bonus *= PactBalance.DoubleDepthBonusMultiplier;
            int free = Math.Max(0, Workshop.StashCapacity(_s.Profile) - _s.Profile.Stash.Count);
            CacheSecureText(run, bonus, free, DailyDream.Get(run.DailyId)?.NightmareMult ?? 1.0);
            GUILayout.Label(_secureLootText, _st.Label);
            if (run.Satchel.Count > 0)
            {
                // 持ち帰れる遺物を、良い物から順にアイコンで並べる（最大14個）。
                GUILayout.BeginHorizontal();
                var satchel = SortedSatchel();
                for (int i = 0; i < Math.Min(14, satchel.Count); i++) IconSlot(satchel[i], 36);
                if (_secureExtraText != null) GUILayout.Label(_secureExtraText, _st.Small);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (run.Infinity != null)
                GUILayout.Label(Loc.T("以下の潜行ボーナス・満杯時の欠片化は上限前の見積もりです。無料出力予算で減少し、抑止分の代替報酬はありません。",
                    "Delve bonuses and overflow-to-shard amounts below are pre-cap estimates. Free-output budgets may reduce them; withheld rewards have no substitute."), _st.Warn);
            GUILayout.Label(_secureBonusText, _st.Small);
            if (_secureOverflowText != null) GUILayout.Label(_secureOverflowText, _st.Warn);
            GUILayout.Label(_secureDelveText, _st.Small);
            GUILayout.Label(_secureNightmareText, _st.Small);
            GUILayout.BeginHorizontal();
            GUI.enabled = _s.CanResolveSecureChoice;
            if (GUILayout.Button(_secureButton, _st.Button, GUILayout.Height(34))) SetStatus(_s.Secure());
            if (GUILayout.Button(_delveButton, _st.Button, GUILayout.Height(34))) SetStatus(_s.Delve());
            GUI.enabled = true;
            if (GUILayout.Button(_gearButton, _st.Button, GUILayout.Height(34)))
            {
                Open = true;
                _tab = 0;
            }
            if (GUILayout.Button(_secureHideButton, _st.Button, GUILayout.Height(34)))
                _secureHidden = true;
            GUILayout.EndHorizontal();
            if (_s.HasPendingTrades)
                GUILayout.Label(Loc.T("取引の応答を待っています。", "Waiting for the trade to complete."), _st.Warn);
            else if (_s.LostTradeCount > 0)
                GUILayout.Label(Loc.T("ホストが確認できない取引があります。遅れて届く応答を待っています（対価も返却も保留中。確保・潜行は続けられます）。手放すにはコンソールで dreamforge_trades_giveup。", "Some trades cannot be confirmed by the host and are on hold (you can still secure or delve). To give them up, run dreamforge_trades_giveup in the console."), _st.Small);
            else if (_s.HasHeldTrades)
                GUILayout.Label(Loc.T("取引の結果をホストに確認中です（確保・潜行は続けられます）。", "Checking a trade result with the host (you can still secure or delve)."), _st.Small);
            else if (!_s.CanResolveSecureChoice)
                GUILayout.Label(Loc.T("ホストが道標を決めて確保または潜行を選ぶまでお待ちください。", "Waiting for the host to confirm a waypoint and choose Secure or Delve."), _st.Small);
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
                GUILayout.BeginHorizontal();
                var art = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                if (Event.current.type == EventType.Repaint) RelicIcons.DrawArt(art, EventArtKey(e));
                GUILayout.BeginVertical();
                GUILayout.Label(UiStyles.Colored(Loc.T("出来事：", "Event: ") + DreamEvents.Name(e), "#9fe0ff") + "  <color=#ccd>" + DreamEvents.Describe(e, _s.Profile) + "</color>", _st.Small);
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
                GUI.enabled = _s.CanResolveSecureChoice;
                for (int i = 0; i < run.OfferedPacts.Count; i++)
                {
                    var id = run.OfferedPacts[i];
                    var d = Pacts.Get(id);
                    if (d == null) continue;
                    if (GUILayout.Button($"<b>{d.Name}</b>\n<color=#ffb0a0>{d.Description}</color>", _st.RowWrap, GUILayout.Height(52)))
                    {
                        SetStatus(_s.Delve(id));
                        break; // Delve can replace the offered collection during this GUI event.
                    }
                }
                GUI.enabled = true;
            }
            GUILayout.Label(_status != null && Time.unscaledTime < _statusUntil ? _status : " ", _st.Warn);
            if (Event.current.type == EventType.Repaint) _secureMeasured = GUILayoutUtility.GetLastRect().yMax;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private float _secureMeasured;

        private readonly Dictionary<Waypoint, string> _waypointCards = new Dictionary<Waypoint, string>();
        private bool _waypointCardsJapanese;
        private static readonly string[] DepthLabels = { "0", "1", "2", "3", "4", "5" };

        private void DrawWaypointPicker(RunState run)
        {
            GUILayout.Label(Loc.T("道標 ─ 次のゾーンの決まり", "Waypoint ─ a rule for the next zone"), _st.Label);
            if (!_s.WaypointChoicesReady)
            {
                GUILayout.Label(Loc.T("ホストから道標の候補を受け取っています。", "Waiting for the host's waypoint cards."), _st.Small);
                return;
            }
            GUILayout.Label(Loc.T("ホストが1枚選びます。「選ばない」こともできます。次の確保地点に着くと効果が終わります。", "The host may choose one card or skip. Its effect ends at the next secure point."), _st.Small);
            GUILayout.Label(run.Infinity != null
                ? Loc.T("ボス後の選択はホストが全員分を確定します。戦闘で自動潜行しません。", "The host confirms the post-boss choice for everyone. Combat does not auto-delve.")
                : Loc.T("選択を終えずに戦闘を続けると、選択中の道標で潜行します。契約は結びません。", "Continuing combat commits the selected waypoint and delves without a pact."), _st.Small);
            if (_waypointCardsJapanese != Loc.Japanese)
            {
                _waypointCardsJapanese = Loc.Japanese;
                _waypointCards.Clear();
            }
            for (int i = 0; i < run.OfferedWaypoints.Count; i++)
            {
                var id = run.OfferedWaypoints[i];
                var def = Waypoints.Get(id);
                if (def == null) continue;
                if (!_waypointCards.TryGetValue(id, out var label))
                    _waypointCards[id] = label = $"<b>{def.Name}</b>\n<color=#a8e9cd>{def.Description}</color>";
                bool rewardAvailable = InfinityRewards.CanChooseWaypoint(_s.Profile, id, out string unavailableReason);
                GUI.enabled = _s.CanChooseRunRules && rewardAvailable;
                if (GUILayout.Button(label, _st.RowWrap, GUILayout.MinHeight(56)))
                    SetStatus(_s.ChooseWaypoint(id));
                GUI.enabled = true;
                if (!rewardAvailable) GUILayout.Label(unavailableReason, _st.Warn);
            }
            if (run.OfferedWaypoints.Count > 0)
            {
                GUI.enabled = _s.CanChooseRunRules;
                if (GUILayout.Button(Loc.T("道標を選ばない", "Skip the waypoint"), _st.Button, GUILayout.Height(28)))
                    SetStatus(_s.ChooseWaypoint(Waypoint.None));
                GUI.enabled = true;
            }
            var chosen = Waypoints.Get(run.PendingWaypoint);
            GUILayout.Label(run.WaypointChosen
                ? Loc.T("選択：", "Selected: ") + (chosen != null ? chosen.Name.ToString() : Loc.T("道標なし", "No waypoint"))
                : Loc.T("未選択（このまま進むと道標なし）", "No selection (continuing skips the waypoint)"), _st.Small);
        }

        private void DrawDreamDepthChoice()
        {
            if (_s.InGame) return;
            int depth = _s.ChosenDreamDepth;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("夢の深さ", "Dream depth"), _st.Label, GUILayout.Width(110));
            GUI.enabled = _s.CanChooseDepth;
            for (int i = 0; i <= DreamDepth.Maximum; i++)
                if (GUILayout.Button(DepthLabels[i], depth == i ? _st.TabSel : _st.Tab, GUILayout.Width(36)))
                    SetStatus(_s.ChooseDreamDepth(i));
            GUI.enabled = true;
            GUILayout.Label(Loc.T("ホストが選択・遠征中は固定", "Chosen by the host; fixed during the expedition"), _st.Small);
            GUILayout.EndHorizontal();
            DrawInfinityChoice();
            if (!_s.HasHostRunChoices)
                GUILayout.Label(Loc.T("ホストの選んだ深さは、遠征の開始時に届きます。", "The host's chosen depth will arrive when the expedition starts."), _st.Small);
            else GUILayout.Label(Loc.T(
                $"敵HP ×{DreamDepth.HealthMultiplier(depth):0.00}・敵ダメージ ×{DreamDepth.DamageMultiplier(depth):0.00}・良い遺物の出やすさ +{Loot.LuckPercent(DreamDepth.RarityLuck(depth)):0}%・覚醒の力 ×{DreamDepth.AwakeningMultiplier(depth):0.00}・星の経験 ×{DreamDepth.StarXpMultiplier(depth):0.00}・部屋 +{DreamDepth.ExtraZoneNodes(depth)}",
                $"Enemy HP ×{DreamDepth.HealthMultiplier(depth):0.00} · enemy damage ×{DreamDepth.DamageMultiplier(depth):0.00} · better relics +{Loot.LuckPercent(DreamDepth.RarityLuck(depth)):0}% · awakening ×{DreamDepth.AwakeningMultiplier(depth):0.00} · star XP ×{DreamDepth.StarXpMultiplier(depth):0.00} · Rooms +{DreamDepth.ExtraZoneNodes(depth)}"), _st.Small);
        }

        private static readonly int[] InfinityIntervals = { InfinityRunState.ShortInterval, InfinityRunState.MiddleInterval, InfinityRunState.LongInterval };
        private static readonly string[] InfinityIntervalLabels = Array.ConvertAll(InfinityIntervals, interval => interval.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private void DrawInfinityChoice()
        {
            bool enabled = _s.ChosenInfinityEnabled;
            int interval = _s.ChosenInfinityInterval;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("インフィニティ", "Infinity mode"), _st.Label, GUILayout.Width(110));
            GUI.enabled = _s.CanChooseDepth;
            if (GUILayout.Button(Loc.T("オフ（通常）", "Off (normal)"), !enabled ? _st.TabSel : _st.Tab, GUILayout.Width(112)))
                SetStatus(_s.ChooseInfinity(false, interval));
            GUI.enabled = _s.CanChooseDepth && InfinityMode.Available;
            if (GUILayout.Button(Loc.T("オン", "On"), enabled ? _st.TabSel : _st.Tab, GUILayout.Width(64)))
                SetStatus(_s.ChooseInfinity(true, interval));
            GUILayout.Label(Loc.T("ボス周期", "Boss interval"), _st.Small, GUILayout.Width(85));
            GUI.enabled = _s.CanChooseDepth && (!enabled || InfinityMode.Available);
            for (int i = 0; i < InfinityIntervals.Length; i++)
            {
                int value = InfinityIntervals[i];
                if (GUILayout.Button(InfinityIntervalLabels[i], value == interval ? _st.TabSel : _st.Tab, GUILayout.Width(36)))
                    SetStatus(_s.ChooseInfinity(enabled, value));
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            // #144: host/solo judge only their own availability; a participant without the host's
            // answer yet sees "waiting for the host's setting", not an unexplained "disabled".
            string lobbySupportNotice = ClientSession.InfinitySupportNotice(_s.CanChooseRunRules);
            if (lobbySupportNotice != null)
                GUILayout.Label(lobbySupportNotice, _st.Warn);
            if (enabled)
                GUILayout.Label(Loc.T("同じゾーンを再生成。周期ボスの魂報酬後に、ホストが全員の帰還／続行を選びます。再生成時はKO復活・狩りの局所リセット。報酬速度の上限は段階2です。",
                    "Regenerates the same zone. After each boss's soul reward, the host chooses return or continue for everyone. Regeneration revives KO players and resets local hunts. Reward rate limits are deferred to stage 2."), _st.Small);
        }

        private Vector2 _scrollSecure;
        private DreamEvent _confirmEvent;
        private static readonly Dictionary<DreamEvent, string> EventArt = new Dictionary<DreamEvent, string>();

        private static string EventArtKey(DreamEvent e)
        {
            if (!EventArt.TryGetValue(e, out var k)) EventArt[e] = k = "events/" + e;
            return k;
        }

        private readonly List<uint> _labelScratch = new List<uint>();

        private readonly Dictionary<uint, (Mirror.NetworkIdentity Identity, Monster Monster)> _labelMonsters =
            new Dictionary<uint, (Mirror.NetworkIdentity, Monster)>();

        private Monster LabelMonster(uint netId, Mirror.NetworkIdentity identity)
        {
            if (_labelMonsters.TryGetValue(netId, out var cached) && cached.Identity == identity && cached.Monster != null)
                return cached.Monster;
            var monster = identity.GetComponent<Monster>();
            if (monster != null) _labelMonsters[netId] = (identity, monster);
            return monster;
        }

        /// <summary>悪夢化した敵と夢の変種の頭上に名札を出す。</summary>
        private void DrawNightmareLabels(float scale)
        {
            _labelScratch.Clear();
            foreach (var entry in _labelMonsters)
                if ((!_s.Nightmare.ContainsKey(entry.Key) && !_s.Variant.ContainsKey(entry.Key))
                    || entry.Value.Identity == null || entry.Value.Monster == null)
                    _labelScratch.Add(entry.Key);
            foreach (uint netId in _labelScratch) _labelMonsters.Remove(netId);
            DrawVariantLabels(scale);
            if (_s.Nightmare.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var kv in _s.Nightmare)
            {
                // Delta sync retains tags outside the local spawn/interest window until host removal.
                if (!Mirror.NetworkClient.spawned.TryGetValue(kv.Key, out var id) || id == null) continue;
                var m = LabelMonster(kv.Key, id);
                if (m == null || !m.isActive) continue;
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
        }

        private void DrawVariantLabels(float scale)
        {
            if (_s.Variant.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var kv in _s.Variant)
            {
                if (!Mirror.NetworkClient.spawned.TryGetValue(kv.Key, out var id) || id == null) continue;
                var m = LabelMonster(kv.Key, id);
                var def = Variants.Get(kv.Value);
                if (m == null || def == null || !m.isActive) continue;
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
            GUILayout.Label(r.SecuredReturn
                ? Loc.T("インフィニティの結果：確保して帰還しました", "Infinity: Secured and returned")
                : r.Victory ? Loc.T("遠征の結果：夢を踏破しました", "Expedition: Conquered")
                : Loc.T("遠征の結果：夢から覚めました", "Expedition: Awakened"), _st.Title);
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

        private float _windowHeight = 720f;
        // 遺物の一覧は最低でも7行ぶん（1行37）見えるようにする。
        private static readonly GUILayoutOption[] ForgeListSize = { GUILayout.MinHeight(264), GUILayout.ExpandHeight(true) };
        private static readonly GUILayoutOption[] ForgeColumnSize = { GUILayout.Width(360), GUILayout.ExpandHeight(true) };
        private static readonly GUILayoutOption[] ForgePaneSize = { GUILayout.ExpandHeight(true) };
        // 横に並ぶボタンは、文が長くても行の幅を押し広げない（押し広げると右端が切れる）。
        private static readonly GUILayoutOption[] ShrinkWidth = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] ShrinkMin28 = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true), GUILayout.MinHeight(28) };
        private static readonly GUILayoutOption[] ShrinkMin30 = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true), GUILayout.MinHeight(30) };
        private static readonly GUILayoutOption[] ShrinkMin32 = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true), GUILayout.MinHeight(32) };
        private static readonly GUILayoutOption[] ShrinkMin44 = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true), GUILayout.MinHeight(44) };

        // DrawWindow は DreamforgeUi.Window.cs へ移した（#147 の順序の試験と一緒に管理する）。

        private static readonly Txt HowToPlay = new Txt(
            "<b>このMODの目的</b>\n" +
            "本体の遠征に「持ち帰れる装備（遺物）」が加わります。遠征のたびに少しずつ装備を集めて鍛え、次の遠征をもっと深く、もっと楽に進めるようにしていきます。\n\n" +
            "<b>1回の遠征の流れ</b>\n" +
            "1. 敵を倒すと遺物が落ちます。協力プレイでも各自に別々に落ちるので、取り合いにはなりません。\n" +
            "2. 拾った物は、まだ持ち帰っていない状態（未確保）で鞄に入ります。あふれるとレア度の低い物（同じレア度ならスコアの低い物）から欠片に換え、そのティック内（先に保存・確保する場合はその前）に持ち主のプロフィールへまとめて付与・通知します。協力プレイの参加者も各自のローカルで付与し、通常のまとめて／定期保存に任せます。MOD設定で追加ドリームダストをONにすると、欠片を減らさずダストもホストがまとめて付与します（既定OFF）。道標による報酬停止中は何も得られません。\n" +
            "3. 新しいゾーンに着くと確保地点が開きます。ここで「確保する」か「深く潜る」かを選びます。\n" +
            "4. 確保した物は保管庫に入り、遠征が終わっても残ります。\n\n" +
            "<b>確保と潜行の考え方</b>\n" +
            "確保すれば安全ですが、深く潜ると遺物が多く、良い物が出やすくなります。そのぶん敵は強くなり、受けるダメージも増えます。全滅すると、まだ持ち帰っていない物は遺失物になり、次の遠征で戦闘部屋を" + Content.RoomsToRecoverLost + "つ突破すると一番良い物を1つだけ取り戻せます。手応えを見ながら、どこで確保するかを決めるのがこのMODの駆け引きです。\n\n" +
            "<b>装備の育て方</b>\n" +
            "・装備：旅人ごとに6つの枠（主装備・頭・防具・手・足・装飾品）に装着します。\n" +
            $"・鍛冶：欠片で強化し（+{Content.EnhanceMilestoneFirst}と+{Content.EnhanceMilestoneSecond}で特性や固有効果が増えます）、調律石で特性を{Content.RetuneChoices}つの候補から選び直します。エピック以上は強化・再調律・限界突破・特性の洗い直しの素材費用が基本の{Content.ForgeMaterialCostMultiplier(Rarity.Epic)}倍です。いらない物は分解して欠片に戻せます。\n" +
            $"・覚醒：固有品は、装着した旅人で敵を倒すと覚醒の力が溜まり、{Content.AwakenThresholdFor(1)}・{Content.AwakenThresholdFor(2)}・{Content.AwakenThresholdFor(3)}で覚醒Ⅰ・Ⅱ・Ⅲになります（固有効果は{Content.AwakenPowerPctAt(1) / 100m:0.##}・{Content.AwakenPowerPctAt(2) / 100m:0.##}・{Content.AwakenPowerPctAt(3) / 100m:0.##}倍）。気に入った1本を使い込みましょう。\n" +
            "・星図：旅人ごとの星の経験で最大" + StarProgression.MaxPoints + "ポイントを得ます。図鑑・テスト用の追加分は別枠です。始まりの星から線でつながる星へ伸ばし、到達刻印は星のレベルに応じて最大3つまで選べます。夢のレベルは星のポイントではなく、工房や夢の圧（敵の強さ）に関わります。\n" +
            "・工房：余った素材で、鞄や保管庫の拡張など、ずっと続く便利な強化を解放します。\n" +
            "・依頼：遠征ごとに3つ出ます。達成すると、欠片と経験値（依頼によっては調律石も）がもらえます。",
            "<b>What this mod adds</b>\n" +
            "Expeditions now drop gear you can keep (relics). Collect and improve a little every run so the next expedition goes deeper and smoother.\n\n" +
            "<b>One expedition</b>\n" +
            "1. Enemies drop relics. In co-op every player gets their own drops, so there is no fighting over loot.\n" +
            "2. What you pick up goes into your unsecured satchel. Overflow converts the lowest-rarity relic (lowest score within that rarity) into shards, credited to its owner's profile with one summary within the tick (or before an earlier save/secure boundary). Co-op participants receive them locally too, using normal batched/periodic saves. Enable extra overflow Dream Dust in mod settings to also receive host-batched Dust without reducing shards (OFF by default). A reward-suppressing waypoint grants nothing.\n" +
            "3. Each new zone opens a secure point where you choose to Secure or Delve.\n" +
            "4. Secured relics go to your stash and stay after the expedition ends.\n\n" +
            "<b>Securing vs. delving</b>\n" +
            "Securing is safe. Delving gives more and better relics, but enemies get tougher and you take more damage. If your party falls, unsecured loot becomes Lost & Found; clear " + Content.RoomsToRecoverLost + " combat rooms next expedition to recover the best piece. Deciding when to secure is the heart of this mod.\n\n" +
            "<b>Growing your gear</b>\n" +
            "- Gear: each Traveler has six slots: weapon, head, armor, hands, feet and charm.\n" +
            $"- Forge: enhance with shards (+{Content.EnhanceMilestoneFirst} and +{Content.EnhanceMilestoneSecond} add an affix or a power), reroll an affix with tuning stones and pick from {Content.RetuneChoices} options. Epics and legendaries pay x{Content.ForgeMaterialCostMultiplier(Rarity.Epic)} the base materials for enhancement, retuning, limit breaks and affix rerolls. Salvage the rest into shards.\n" +
            $"- Awakening: legendaries gather power as the Traveler wearing them defeats enemies; at {Content.AwakenThresholdFor(1)}, {Content.AwakenThresholdFor(2)} and {Content.AwakenThresholdFor(3)} they reach Awakening I, II and III (powers x{Content.AwakenPowerPctAt(1) / 100m:0.##}, x{Content.AwakenPowerPctAt(2) / 100m:0.##}, x{Content.AwakenPowerPctAt(3) / 100m:0.##}). Pick a favourite and keep using it.\n" +
            "- Star Map: each Traveler earns up to " + StarProgression.MaxPoints + " points from their own star XP, plus separate codex/test bonuses. Grow along connections from the starting star; choose up to three keystones as your star level rises. Dream Level affects workshop access and dream pressure (enemy strength), not star points.\n" +
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
                _openFeats.Add("☆ " + f.Name + "  <color=#ccd>" + Feats.Describe(f) + $"  {prog}/{f.Target}</color>  <color=#c9a86a>{reward}</color>");
            }
            return _openFeats;
        }

        private readonly CodexView _codex = new CodexView();
        private bool _codexOpen;

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
                GUILayout.Label(UiStyles.Colored("★ " + f.Name, "#ffe17a") + "  <color=#ccd>" + Feats.Describe(f) + "</color>", _st.Small);
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
                GUILayout.Label(UiStyles.Colored(v.Name.ToString(), "#ffb347") + "  <color=#ccd>" + v.Description + "</color>", _st.Small);
            GUILayout.Label(Loc.T("悪夢の性質", "Nightmare affixes"), _st.Header);
            GUILayout.Label(Loc.T("金色は障壁の予告、琥珀色は条件付きの守り、青色は反撃の好機または敵の減速、緑色は傷繕いを示します。距離や向きによる守りは、性質の説明を確かめてください。",
                "Gold warns of a shield; amber marks a conditional guard; blue marks a punish window or a slowed enemy; green marks recuperation. Read each affix for distance and facing conditions."), _st.Small);
            foreach (var affix in Nightmares.AllAffixes)
                GUILayout.Label(UiStyles.Colored(Nightmares.AffixName(affix), "#ff6ad5") + "  <color=#ccd>"
                    + Nightmares.AffixDescription(affix) + "</color>", _st.Small);
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
                case 2: return Loc.T("旅人ごとの星の経験で、始まりの星からつながる星へ伸ばします。振り直しは無料です。夢のレベルと振った星は、敵を強くする夢の圧にも関わります。",
                    "Earn star XP per Traveler and grow along connections from the starting star. Respec is free. Dream Level and spent stars also raise dream pressure, strengthening enemies.");
                case 3: return Loc.T("余った欠片と調律石で、鞄や保管庫の拡張など、ずっと続く便利な強化を解放します。強さは上がりませんが、遠征がぐっと楽になります。",
                    "Spend spare shards and tuning stones on permanent conveniences such as a bigger satchel and stash. They don't make you stronger, but they make expeditions much easier.");
                default: return Loc.T("遊び方の確認、遠征の状態、これまでの記録と図鑑を見られます。",
                    "Read how to play, check expedition status, and browse your records and codex.");
            }
        }

        private void DrawGearTab()
        {
            var p = _s.Profile;
            string hero = HeroKey;
            // 鍛冶と同じく欄をウィンドウの残り高さまで広げる（#147）。左欄は欄ごとスクロールに収める
            // （#147 の残り）：払い戻しパネルで高さが足りないとき、それ以外の固定部分の下端
            // （狙い系統など）が枠の外へ出ていた。詳細は DreamforgeUi.GearColumn.cs。
            GUILayout.BeginHorizontal(ForgePaneSize);

            // 左：装着中とビルド
            DrawGearColumn(p, hero);

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
            GUILayout.BeginVertical(_st.Panel, ForgePaneSize);
            var sel = p.FindStash(_selected);
            if (sel == null)
            {
                GUILayout.Label(Loc.T("一覧から遺物を選ぶと、ここに性能と、いま装着している物との違いが表示されます。", "Select a relic to see its stats and how it compares with what you have equipped."), _st.Small);
            }
            else DrawGearDetail(p, hero, sel);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static string SlotTabLabel(Profile p, Slot slot)
        {
            int n = 0;
            foreach (var r in p.Stash) if (r.Slot == slot) n++;
            return n > 0 ? $"{Content.SlotName(slot)} <color=#bcbcd2>{n}</color>" : Content.SlotName(slot).ToString();
        }

        private void RelicList(IEnumerable<Relic> relics, string hero, float height)
        {
            // height <= 0 は「残りの高さいっぱい」（鍛冶）。並べ替えの使い回しの鍵は固定値を使う。
            bool flex = height <= 0f;
            float cacheId = flex ? 431f : height;
            float visibleHeight = flex ? Mathf.Max(300f, _windowHeight) : height;
            var list = SortedCached(relics, cacheId);
            var rows = RowTexts(list, hero, cacheId);
            _scrollList = flex
                ? GUILayout.BeginScrollView(_scrollList, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, ForgeListSize)
                : GUILayout.BeginScrollView(_scrollList, GUILayout.Height(height));
            if (list.Count == 0) GUILayout.Label(Loc.T("まだありません。遠征で敵を倒すと遺物が落ち、確保すると保管庫に入ります。", "Nothing here yet. Enemies drop relics on expeditions; secure them to bring them here."), _st.Small);
            // 保管庫は最大420個まで広がるので、見えている行だけを描く（行の高さは一定）。
            const float rowStep = 37f; // 行36＋余白1（GUILayout は隣り合う余白を重ねる）
            int first = Math.Max(0, (int)(_scrollList.y / rowStep) - 2);
            int last = Math.Min(list.Count, first + (int)(visibleHeight / rowStep) + 5);
            if (first > 0) GUILayout.Space(first * rowStep);
            for (int i = first; i < last; i++)
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
                    _confirmEnhance = null;
                    _confirmAffixReroll = null;
                }
                GUILayout.EndHorizontal();
            }
            if (last < list.Count) GUILayout.Space((list.Count - last) * rowStep);
            GUILayout.EndScrollView();
            _scrollList.y = WheelScroll(_scrollList.y, list.Count * rowStep);
        }

        /// <summary>
        /// 直前に閉じたスクロール欄の上でホイールを回したとき、IMGUI が処理しなかった分をここで動かす
        /// （ゲーム側の入力で標準のホイール処理が効かないことがあるため）。内容の高さが不明なら無限大を渡す。
        /// </summary>
        private static float WheelScroll(float y, float contentHeight)
        {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel) return y;
            Rect view = GUILayoutUtility.GetLastRect();
            if (!view.Contains(e.mousePosition)) return y;
            float next = ScrollMath.Wheel(y, e.delta.y, 20f, contentHeight, view.height);
            e.Use();
            return next;
        }

        // ───── 描画の使い回し（OnGUI は1フレームに何度も呼ばれるので、並べ替えは0.3秒ごとに1回だけ行う） ─────
        private Vector2 _scrollRight;
        private readonly Dictionary<float, List<Relic>> _sortedCache = new Dictionary<float, List<Relic>>();
        private readonly Dictionary<float, string> _sortedKey = new Dictionary<float, string>();
        private float _sortedUntil;
        private readonly List<Relic> _satchelTop = new List<Relic>();
        private float _satchelTopUntil;
        private int _satchelTopCount = -1;
        private RunState _satchelTopRun;
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
                state = state * 31 + (r.Locked ? 1 : 0) + r.AwakenLevel * 2 + r.Enhance * 4 + (int)r.Rarity * 64 + r.Powers.Count * 1024;
            }
            return p.Stash.Count + ":" + state + ":" + _slot + ":" + _forgeAllSlots + ":" + HeroKey + ":" + Loc.Japanese;
        }

        /// <summary>一覧（高さで区別）の並べ替えを使い回す。中身・遺物の状態・枠が変わるか、0.3秒たったら作り直す。素材だけの増減は順序に影響しない。</summary>
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
                stateHash = stateHash * 31 + (r.Locked ? 1 : 0) + r.AwakenLevel * 2 + r.Enhance * 4 + (int)r.Rarity * 64 + r.Powers.Count * 1024;
                // 記憶の井戸で先頭の固有効果が変わると、数・Scoreが同じでもエピックの銘が変わる。
                stateHash = stateHash * 31 + (r.Powers.Count > 0 ? (int)r.Powers[0].Power : 0);
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
                rows.Add($"{mark}{UiStyles.RelicTitle(r)} <color=#bcbcd2>Lv{r.ItemLevel}</color>{lck}{fresh}");
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
            int high = 0;
            foreach (var r in parts) if (r.Rarity >= Rarity.Rare) high++;
            // 左の欄は幅が狭いので、見出しと選択肢を2段に分ける（1行に並べると右が切れる）。
            GUILayout.Label(Loc.T("分解するレア度：", "Salvage up to:"), _st.Small);
            var names = new[] { Loc.T("コモンまで", "Common"), Loc.T("アンコモンまで", "Uncommon"), Loc.T("レアまで", "Rare"), Loc.T("エピックまで", "Epic") };
            int cur = (int)p.BulkSalvageMaxRarity;
            int pick = GUILayout.SelectionGrid(Math.Max(0, Math.Min(3, cur)), names, 2, _st.ButtonWrap);
            if (pick != cur)
            {
                Rules.SetBulkSalvageMaxRarity(p, (Rarity)pick);
                _confirmBulk = false;
                _s.MarkDirty(true);
                _s.SaveNow();
                parts = Rules.BulkSalvageCandidates(p, _s.Trades);
                shards = 0; high = 0;
                foreach (var r in parts) { shards += Rules.SalvageValue(r); if (r.Rarity >= Rarity.Rare) high++; }
            }
            if (high > 0)
                GUILayout.Label(Loc.T($"<color=#ff8080>注意：レア以上が{high}個含まれます。2回押して確定します。</color>", $"<color=#ff8080>Warning: {high} Rare or better relics are included. Press twice to confirm.</color>"), _st.Small);
            GUI.enabled = parts.Count > 0 && (p.Run == null || !_s.InGame);
            string label = _confirmBulk
                ? Loc.T($"<color=#ff8080>もう一度押すと、{parts.Count}個をまとめて分解します" + (high > 0 ? $"（レア以上{high}個）" : "") + "</color>", $"<color=#ff8080>Press again to salvage {parts.Count} relics" + (high > 0 ? $" ({high} Rare+)" : "") + "</color>")
                : Loc.T($"まとめて分解（{parts.Count}個・欠片{shards}）", $"Salvage in bulk ({parts.Count} relics, {shards} shards)");
            if (GUILayout.Button(label, _st.ButtonWrap, ShrinkMin30))
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

        private List<Relic> SortedSatchel()
        {
            var run = _s.Profile.Run;
            float now = Time.unscaledTime;
            if (run == null)
            {
                _satchelTop.Clear();
                _satchelTopRun = null;
                return _satchelTop;
            }
            if (now < _satchelTopUntil && run == _satchelTopRun && run.Satchel.Count == _satchelTopCount) return _satchelTop;
            _satchelTopRun = run;
            _satchelTopUntil = now + 0.3f;
            _satchelTopCount = run.Satchel.Count;
            _satchelTop.Clear();
            _satchelTop.AddRange(run.Satchel);
            _satchelTop.Sort((a, b) => b.Score.CompareTo(a.Score));
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
                + (FamilyPrefs.Label(r.Base.Family) is string familyLabel ? " · " + familyLabel : "")
                + (r.Retunes > 0 ? Loc.T($" · 再調律{r.Retunes}/{Content.MaxRetunes}", $" · retuned {r.Retunes}/{Content.MaxRetunes}") : ""), _st.Small);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T(
                $"Lv1は100%。固定攻魔は1Lvごと+{GearBalance.FlatDamageGrowthPct}%、他の固定能力は+{GearBalance.OtherFixedGrowthPct}%（Lv{Content.ItemLevelScalingCap}で停止）。%能力は成長しません。保存済み特性は再抽選せず、基礎能力は再計算します。",
                $"Lv1: 100%. Flat attack/power gain +{GearBalance.FlatDamageGrowthPct}% per level; other fixed stats +{GearBalance.OtherFixedGrowthPct}% (stops at Lv{Content.ItemLevelScalingCap}). Percent stats do not scale. Saved affixes are not rerolled; implicit stats recalculate."), _st.Small);
            if (r.DeveloperGranted)
                GUILayout.Label(Loc.T("出所：開発付与", "Source: developer grant"), _st.Small);
            if (Content.MaxLimitBreaks(r.Rarity) > 0 && (r.LimitBreaks > 0 || r.Enhance >= Content.MaxEnhance))
                GUILayout.Label(UiStyles.Colored(Loc.T($"限界突破 {r.LimitBreaks}/{Content.MaxLimitBreaks(r.Rarity)}（上限 +{Content.MaxEnhanceFor(r)}）",
                    $"Limit breaks {r.LimitBreaks}/{Content.MaxLimitBreaks(r.Rarity)} (cap +{Content.MaxEnhanceFor(r)})"), "#ffd36e"), _st.Small);
            var imp = r.Implicit;
            GUILayout.Label(UiStyles.Colored(Content.FormatStat(imp.Stat, imp.Value), "#c8c8ff") + Loc.T("  <color=#aaa>（この種類が必ず持つ性能）</color>", "  <color=#aaa>(always on this type)</color>"), _st.Label);
            // 固有効果は遺物の個性なので、特性より先に見せる。
            foreach (var pw in r.EffectivePowers()) GUILayout.Label(UiStyles.Colored(Content.FormatPowerBullets(pw.Power, pw.Value), "#e0b0ff"), _st.Label);
            if (r.BossMove != null)
                GUILayout.Label(UiStyles.Colored(r.DescribeBossMove(), "#e0b0ff"), _st.Label);
            // 連携（v1.26）：条件と効果を1行で。ゲームの中なら各条件に ✓／・ が付く。
            var link = r.Link;
            if (link != null) GUILayout.Label(UiStyles.Colored(Links.Describe(link, LinkMarks()), "#7fd8ff"), _st.Small);
            foreach (var a in r.EffectiveStats().Skip(1)) GUILayout.Label(Content.FormatStat(a.Stat, a.Value), _st.Label);
            if (r.Rarity == Rarity.Legendary) GUILayout.Label(AwakenLine(r), _st.Small);
            if (r.UniqueId != null && Content.TryGetUnique(r.UniqueId, out var u))
            {
                if (u.SetId != null && Content.GetSet(u.SetId) is SetDef set)
                {
                    GUILayout.Label(UiStyles.Colored($"《{set.Name}》", "#ff8a3d") + " <color=#ccd>" + set.Describe() + "</color>", _st.Small);
                    var build = _s.CurrentBuild(HeroKey);
                    build.Sets.TryGetValue(set.Id, out int count);
                    DrawSetLinkProgress(set, count);
                }
                else
                    GUILayout.Label("<i>" + UiStyles.Colored(u.Lore.ToString(), "#c9a86a") + "</i>", _st.Small);
            }
        }

        private void DrawSetLinkProgress(SetDef set, int count)
        {
            if (set.LinkStages.Length == 0) return;
            var marks = LinkMarks();
            var stage = set.SelectLinkStage(count) ?? set.LinkStages[0];
            bool satisfied = marks != null && Links.Satisfied(stage.Link, _linkHeroKey, _linkMemories, _linkEssences);
            GUILayout.Label(UiStyles.Colored(set.DescribeLinkProgress(count, satisfied), "#7fd8ff"), _st.Small);
        }

        /// <summary>固有品の覚醒の進み具合、または覚醒済みの印。</summary>
        private static string AwakenLine(Relic r)
        {
            int level = r.AwakenLevel;
            string done = "";
            if (level > 0)
            {
                int powerX = Content.AwakenPowerPctAt(level), affixX = Content.AwakenAffixPctAt(level);
                done = UiStyles.Colored(Loc.T($"✦ 覚醒{Content.AwakenNumeral(level)}：固有効果{powerX / 100f:0.##}倍・特性{affixX / 100f:0.##}倍",
                    $"✦ Awakening {Content.AwakenNumeral(level)}: powers x{powerX / 100f:0.##}, affixes x{affixX / 100f:0.##}"), "#ffe17a");
                if (level >= Content.MaxAwakenLevel) return done;
                done += "\n";
            }
            int from = Content.AwakenThresholdFor(level), to = Content.AwakenThresholdFor(level + 1);
            int now = r.AwakenPoints;
            int filled = Math.Max(0, Math.Min(10, (now - from) * 10 / Math.Max(1, to - from)));
            string bar = "<color=#ffe17a>" + new string('■', filled) + "</color><color=#8a8aa0>" + new string('□', 10 - filled) + "</color>";
            string next = Content.AwakenNumeral(level + 1);
            int nextPower = Content.AwakenPowerPctAt(level + 1), nextAffix = Content.AwakenAffixPctAt(level + 1);
            return done + Loc.T(
                $"覚醒{next}まで {bar} {now}/{to}\n<color=#8a8aa0>装着した旅人で敵を倒すと溜まります（エリート{Content.AwakenPoints(MonsterTier.MiniBoss, false)}・ボス{Content.AwakenPoints(MonsterTier.Boss, false)}・悪夢化は{Content.AwakenNightmareMultiplier}倍）。覚醒{next}で固有効果が{nextPower / 100f:0.##}倍、特性が{nextAffix / 100f:0.##}倍になります（全{Content.MaxAwakenLevel}段）。</color>",
                $"Awakening {next} {bar} {now}/{to}\n<color=#8a8aa0>Fills as the Traveler wearing it defeats enemies (elite {Content.AwakenPoints(MonsterTier.MiniBoss, false)}, boss {Content.AwakenPoints(MonsterTier.Boss, false)}, nightmares x{Content.AwakenNightmareMultiplier}). Awakening {next}: powers x{nextPower / 100f:0.##}, affixes x{nextAffix / 100f:0.##} ({Content.MaxAwakenLevel} levels).</color>");
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
            if (sel.BossMove != cur.BossMove)
            {
                if (sel.BossMove != null) GUILayout.Label(UiStyles.Colored("+ " + sel.DescribeBossMove(), "#7cf07c"), _st.Small);
                if (cur.BossMove != null) GUILayout.Label(UiStyles.Colored("- " + cur.DescribeBossMove(), "#ff7a7a"), _st.Small);
            }
        }

        private void DrawForgeTab()
        {
            var p = _s.Profile;
            GUILayout.BeginHorizontal(ForgePaneSize);

            GUILayout.BeginVertical(_st.Panel, ForgeColumnSize);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("すべて", "All"), _forgeAllSlots ? _st.ButtonSel : _st.Button)) { _forgeAllSlots = true; _confirmEnhance = null; }
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
                    _confirmEnhance = null;
                }
            }
            GUILayout.EndHorizontal();
            // まとめて分解は一覧の上（#147）：一覧は下限まで縮んで余りを吸収するが、
            // 下に置くと高さが足りないときに選択肢と確定ボタンが下端から切れる。
            BulkSalvageRow();
            RelicList(_forgeAllSlots ? p.Stash : p.Stash.Where(r => r.Slot == _slot), HeroKey, 0f);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_st.Panel, ForgePaneSize);
            // 6枠になって製作の行が増えたので、右の欄全体をスクロールできるようにする。
            // 横スクロールは使わない（長い文は折り返す）。横に広げると右端が切れて読めなくなる。
            _scrollForge = GUILayout.BeginScrollView(_scrollForge, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            var sel = p.FindStash(_selected);
            if (sel != null)
            {
                _scrollRight = GUILayout.BeginScrollView(_scrollRight, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(260)); // 固有効果まで見えるように
                RelicDetail(sel);
                GUILayout.EndScrollView();
                _scrollRight.y = WheelScroll(_scrollRight.y, float.PositiveInfinity);
                GUILayout.Space(4);
                ValidateEnhanceConfirmation();
                int failureChance = Rules.EnhanceFailureChance(sel);
                if (failureChance > 0)
                    GUILayout.Label(UiStyles.Colored(Loc.T($"失敗の確率 {failureChance}%（失敗すると、{Content.EnhanceDemotionChance * 100:0.######}%の確率で{Content.EnhanceDemotionSteps}段下がります）",
                        $"Failure chance: {failureChance}% (a failure has a {Content.EnhanceDemotionChance * 100:0.######}% chance to lower enhancement by {Content.EnhanceDemotionSteps} level(s))"), "#ff8080"), _st.Small);
                GUILayout.BeginHorizontal();
                GUI.enabled = !_s.Trades.IsReserved(sel.Uid);
                int maxEnhance = Content.MaxEnhanceFor(sel);
                if (sel.Enhance < maxEnhance)
                {
                    int enhanceCost = Content.EnhanceCost(sel);
                    GUI.enabled = !_s.Trades.IsReserved(sel.Uid) && p.RetuneOffer == null && p.Material(Materials.Shard) >= enhanceCost;
                    bool confirm = _confirmEnhance == sel && _confirmEnhanceTarget == sel.Enhance + 1;
                    string enhanceLabel = confirm
                        ? Loc.T($"<color=#ff8080>もう一度押すと強化 +{sel.Enhance + 1}を確定します（欠片{enhanceCost}）</color>",
                            $"<color=#ff8080>Press again to confirm Enhance +{sel.Enhance + 1} ({enhanceCost} shards)</color>")
                        : Loc.T($"強化 +{sel.Enhance + 1}（欠片{enhanceCost}）", $"Enhance +{sel.Enhance + 1} ({enhanceCost} shards)");
                    if (GUILayout.Button(enhanceLabel, _st.ButtonWrap, ShrinkMin32))
                    {
                        if (failureChance > 0 && !confirm)
                        {
                            _confirmEnhance = sel;
                            _confirmEnhanceTarget = sel.Enhance + 1;
                        }
                        else Act(() => Rules.Enhance(p, sel.Uid, _s.Trades), true);
                    }
                }
                else GUILayout.Label(Loc.T($"強化は+{maxEnhance}が上限です", $"Enhancement is capped at +{maxEnhance}"), _st.Small);
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
                if (GUILayout.Button(sv, _st.ButtonWrap, ShrinkMin32))
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

                // 限界突破（v1.27）：上限に達したレア以上の遺物だけ。
                if (sel.Enhance >= maxEnhance && sel.LimitBreaks < Content.MaxLimitBreaks(sel.Rarity))
                    DrawLimitBreak(sel);

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
                        if (GUILayout.Button(Content.FormatStat(o.Stat, o.Value) + mark, _st.ButtonWrapSel, ShrinkMin32)) Act(() => Rules.ChooseRetune(p, choice), true);
                    }
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button(Loc.T("元のままにする（使った調律石は戻りません）", "Keep the original (tuning stones are not refunded)"), _st.ButtonWrap, ShrinkMin28)) Act(() => Rules.ChooseRetune(p, -1), false);
                }
                else if (offer != null)
                {
                    GUILayout.Label(Loc.T("別の遺物で、再調律の候補を選んでいる途中です。", "Another relic has retune options waiting."), _st.Warn);
                    if (GUILayout.Button(Loc.T("その遺物を開く", "Open that relic"), _st.ButtonWrap, ShrinkMin28))
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
                        if (GUILayout.Button(Content.FormatStat(a.Stat, a.Value), _retuneIndex == i ? _st.ButtonWrapSel : _st.ButtonWrap, ShrinkWidth)) _retuneIndex = i;
                    }
                    GUILayout.EndHorizontal();
                    int rtCost = Content.RetuneCost(sel);
                    bool rtAfford = p.Material(Materials.Tuning) >= rtCost;
                    GUI.enabled = _retuneIndex >= 0 && rtAfford && !_s.Trades.IsReserved(sel.Uid);
                    string rtLabel = !rtAfford ? Loc.T($"調律石が足りません（{rtCost}必要・所持{p.Material(Materials.Tuning)}）", $"Not enough tuning stones ({rtCost} needed, have {p.Material(Materials.Tuning)})")
                        : _retuneIndex < 0 ? Loc.T($"上の特性を1つ選んでください（調律石{rtCost}）", $"Pick an affix above ({rtCost} tuning)")
                        : Loc.T($"候補を出す（調律石{rtCost}）", $"Roll options ({rtCost} tuning)");
                    if (GUILayout.Button(rtLabel, _st.ButtonWrap, ShrinkMin30))
                    {
                        int idx = _retuneIndex;
                        Act(() => Rules.Retune(p, sel.Uid, idx, _s.Trades), true);
                        _retuneIndex = -1;
                    }
                    GUI.enabled = true;
                }

                // 特性の洗い直し（v1.31）：再調律の下に置く。候補が出ている遺物と、特性のない遺物には出さない。
                if (sel.Affixes.Count > 0 && !(p.RetuneOffer != null && p.RetuneOffer.Uid == sel.Uid))
                {
                    var (rrShards, rrTuning) = Rules.AffixRerollCost(sel);
                    bool rrAfford = p.Material(Materials.Shard) >= rrShards && p.Material(Materials.Tuning) >= rrTuning;
                    bool rrConfirm = _confirmAffixReroll == sel.Uid;
                    GUI.enabled = rrAfford && !_s.Trades.IsReserved(sel.Uid); // 鍵つき・装着中は押したときに理由が表示される
                    string rrLabel = !rrAfford
                        ? Loc.T($"素材が足りません（欠片{rrShards}・調律石{rrTuning}必要）", $"Not enough materials ({rrShards} shards and {rrTuning} tuning stones needed)")
                        : rrConfirm ? Loc.T($"<color=#ff8080>もう一度押すと「{sel.PlainName}」の特性を全部引き直します（欠片{rrShards}・調律石{rrTuning}・取り消せません）</color>",
                            $"<color=#ff8080>Press again to reroll every affix on \"{sel.PlainName}\" ({rrShards} shards, {rrTuning} tuning; cannot be undone)</color>")
                        : Loc.T($"特性を洗い直す（欠片{rrShards}・調律石{rrTuning}）", $"Reroll all affixes ({rrShards} shards, {rrTuning} tuning)");
                    if (GUILayout.Button(rrLabel, _st.ButtonWrap, ShrinkMin30))
                    {
                        if (rrConfirm)
                        {
                            _confirmAffixReroll = null;
                            Act(() => Rules.AffixReroll(p, sel.Uid, _s.Trades), true);
                        }
                        else _confirmAffixReroll = sel.Uid;
                    }
                    GUI.enabled = true;
                }
            }
            else
            {
                GUILayout.Label(Loc.T("左の一覧から遺物を選ぶと、強化・再調律・分解ができます。強化は欠片、再調律は調律石を使います。", "Pick a relic on the left to enhance (shards), retune (tuning stones) or salvage it."), _st.Small);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("合成：同じレア度の遺物を決まった個数集めて、1つ上のレア度の遺物1つに変えます", "Transmute: turn a set number of relics of one rarity into 1 of the next"), _st.Header);
            GUILayout.Label(Loc.T($"鍵をかけた物・装着中の物は使わず、残りの中から弱い順に必要な個数を使います。エピックからは固有品が生まれます（欠片と調律石が要ります）。上の絞り込みで枠を選ぶと、結果をその枠にできます（欠片{Content.TransmuteTargetCostPct / 100m:0.##}倍）。",
                $"Locked and equipped relics are never used; the weakest of the rest go in. Epics become a legendary (costs shards and tuning stones). Pick a slot filter above to choose the result's slot ({Content.TransmuteTargetCostPct / 100m:0.##}x shards)."), _st.Small);
            GUILayout.BeginHorizontal();
            foreach (Rarity r in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic })
            {
                int n = TransmuteCount(r);
                Slot? target = _forgeAllSlots ? (Slot?)null : _slot;
                int tcost = Rules.TransmuteCost(r, target != null);
                int need = Content.TransmuteInputs(r), ttune = Rules.TransmuteTuning(r);
                GUI.enabled = n >= need && p.Material(Materials.Shard) >= tcost && p.Material(Materials.Tuning) >= ttune;
                string tuneJa = ttune > 0 ? $"・調律石{ttune}" : "", tuneEn = ttune > 0 ? $", {ttune} tuning" : "";
                string label = UiStyles.Colored(Content.RarityName(r).ToString(), UiStyles.RarityHex(r)) + $" {n}/{need}\n"
                    + (target != null ? Loc.T($"→{Content.SlotName(target.Value)}・欠片{tcost}{tuneJa}", $"→{Content.SlotName(target.Value)}, {tcost} shards{tuneEn}") : Loc.T($"欠片{tcost}{tuneJa}", $"{tcost} shards{tuneEn}"));
                if (GUILayout.Button(label, _st.ButtonWrap, ShrinkMin44)) Act(() => Rules.Transmute(p, r, _s.Trades, target), false);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T($"製作：欠片で新しい遺物を作ります（Lv{Math.Max(1, p.BestItemLevel)}＝これまでの最高）", $"Craft: make a new relic with shards (Lv{Math.Max(1, p.BestItemLevel)}, your best so far)"), _st.Header);
            foreach (Slot slot in Content.SlotOrder)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Content.SlotName(slot).ToString(), _st.Label, GUILayout.Width(70));
                GUI.enabled = p.Material(Materials.Shard) >= Rules.CraftShardCost(false);
                if (GUILayout.Button(Loc.T($"通常：アンコモン以上（欠片{Rules.CraftShardCost(false)}）", $"Basic: Uncommon+ ({Rules.CraftShardCost(false)} shards)"), _st.ButtonWrap, ShrinkWidth))
                    Act(() => Rules.Craft(p, slot, false), false);
                GUI.enabled = p.Material(Materials.Shard) >= Rules.CraftShardCost(true) && p.Material(Materials.Tuning) >= Rules.CraftTuningCost(true);
                if (GUILayout.Button(Loc.T($"上等：レア以上（欠片{Rules.CraftShardCost(true)}・調律石{Rules.CraftTuningCost(true)}）", $"Fine: Rare+ ({Rules.CraftShardCost(true)} shards, {Rules.CraftTuningCost(true)} tuning)"), _st.ButtonWrap, ShrinkWidth))
                    Act(() => Rules.Craft(p, slot, true), false);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            _scrollForge.y = WheelScroll(_scrollForge.y, float.PositiveInfinity);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>強化の次の節目（+3・+5・+10・+15・+20）で何が起きるか。もう節目がなければ null。</summary>
        private static string NextMilestone(Relic r)
        {
            // そのレア度で届く一番高い強化値（限界突破の回数上限まで）。
            int ceiling = Content.MaxEnhanceFor(r.Rarity, Content.MaxLimitBreaks(r.Rarity));
            if (r.EnhanceMilestones < 1)
                return Loc.T($"+{Content.EnhanceMilestoneFirst}で特性が1行増えます。", $"+{Content.EnhanceMilestoneFirst}: one more affix.");
            if (r.EnhanceMilestones < 2)
                return r.AuthoredEffectCount == 0
                    ? Loc.T($"+{Content.EnhanceMilestoneSecond}でこの枠の固有効果が1つ宿ります。", $"+{Content.EnhanceMilestoneSecond}: gains a power for this slot.")
                    : Loc.T($"+{Content.EnhanceMilestoneSecond}で特性がもう1行増えます。", $"+{Content.EnhanceMilestoneSecond}: one more affix.");
            if (r.EnhanceMilestones < 3 && Content.EnhanceMilestoneThird <= ceiling)
                return Loc.T($"+{Content.EnhanceMilestoneThird}で特性がもう1行増えます。", $"+{Content.EnhanceMilestoneThird}: one more affix.");
            if (r.EnhanceMilestones < 4 && Content.EnhanceMilestoneFourth <= ceiling)
                return Loc.T($"+{Content.EnhanceMilestoneFourth}で特性がもう1行増えます。", $"+{Content.EnhanceMilestoneFourth}: one more affix.");
            if (!r.MilestonePowerApplied && r.EnhanceMilestones < 5 && Content.EnhanceMilestoneFifth <= ceiling)
                return Loc.T($"+{Content.EnhanceMilestoneFifth}で固有効果1つの値が{Content.LimitBreakPowerPct / 100m:0.##}倍になります。", $"+{Content.EnhanceMilestoneFifth}: one power's value grows {Content.LimitBreakPowerPct / 100m:0.##}x.");
            return null;
        }

        // ───── 限界突破（v1.27）：素材選びの状態 ─────
        private string _limitBreakTarget, _limitBreakMaterial;
        private bool _limitBreakOpen, _confirmLimitBreak;

        /// <summary>限界突破の確認。同じ枠・同じレア度以上の使える遺物から素材を選び、2回押しで確定する。</summary>
        private void DrawLimitBreak(Relic sel)
        {
            var p = _s.Profile;
            int maxBreaks = Content.MaxLimitBreaks(sel.Rarity);
            int n = sel.LimitBreaks + 1;
            int tuningCost = Content.LimitBreakTuningCost(n, sel.Rarity), shardCost = Content.LimitBreakShardCost(n, sel.Rarity);
            int capNow = Content.MaxEnhanceFor(sel), capNext = Content.MaxEnhanceFor(sel.Rarity, n);
            if (_limitBreakTarget != sel.Uid)
            {
                _limitBreakTarget = sel.Uid;
                _limitBreakMaterial = null;
                _confirmLimitBreak = false;
            }
            if (GUILayout.Button(_limitBreakOpen
                    ? Loc.T($"限界突破（{sel.LimitBreaks}/{maxBreaks}）　上限 +{capNow} → +{capNext}　△ 閉じる", $"Limit break ({sel.LimitBreaks}/{maxBreaks})   cap +{capNow} -> +{capNext}   close")
                    : Loc.T($"限界突破（{sel.LimitBreaks}/{maxBreaks}）　上限 +{capNow} → +{capNext}", $"Limit break ({sel.LimitBreaks}/{maxBreaks})   cap +{capNow} -> +{capNext}"),
                    _st.ButtonWrap, ShrinkMin30))
            {
                _limitBreakOpen = !_limitBreakOpen;
                _confirmLimitBreak = false;
            }
            if (!_limitBreakOpen) return;
            GUILayout.Label(Loc.T("同じ枠で同じレア度以上の遺物を1つ素材として消費します（装着中・取引の待ち・鍵のかかった物・再調律の候補中の物は使えません）。",
                "Consumes one relic of the same slot and equal or higher rarity (equipped, pending-trade, locked or retuning relics can't be used)."), _st.Small);
            GUILayout.Label(Loc.T($"{n}回目の費用：調律石{tuningCost}・欠片{shardCost}（所持：調律石{p.Material(Materials.Tuning)}・欠片{p.Material(Materials.Shard)}）",
                $"Break {n} costs {tuningCost} tuning and {shardCost} shards (you have {p.Material(Materials.Tuning)} tuning, {p.Material(Materials.Shard)} shards)."), _st.Small);
            var parts = Rules.LimitBreakCandidates(p, sel, _s.Trades);
            if (_limitBreakMaterial != null && p.FindStash(_limitBreakMaterial) == null) _limitBreakMaterial = null;
            if (parts.Count == 0)
            {
                GUILayout.Label(Loc.T("使える素材がありません（同じ枠で同じレア度以上の、鍵なし・未装着の遺物が必要です）。", "No usable material (need an unlocked, unequipped relic of the same slot and equal or higher rarity)."), _st.Warn);
                return;
            }
            GUILayout.Label(Loc.T("素材を選んでください：", "Pick a material:"), _st.Small);
            foreach (var m in parts)
            {
                if (GUILayout.Button((_limitBreakMaterial == m.Uid ? "● " : "○ ") + UiStyles.RelicTitle(m) + $" <color=#bcbcd2>+{m.Enhance} · Lv{m.ItemLevel}</color>",
                    _limitBreakMaterial == m.Uid ? _st.ButtonSel : _st.Row, GUILayout.Height(26)))
                {
                    _limitBreakMaterial = m.Uid;
                    _confirmLimitBreak = false;
                }
            }
            bool afford = p.Material(Materials.Tuning) >= tuningCost && p.Material(Materials.Shard) >= shardCost;
            GUI.enabled = _limitBreakMaterial != null && afford && !_s.Trades.IsReserved(sel.Uid);
            string materialName = _limitBreakMaterial != null ? p.FindStash(_limitBreakMaterial)?.PlainName : null;
            string label = _limitBreakMaterial == null
                ? Loc.T("素材を選んでください", "Pick a material")
                : !afford
                    ? Loc.T($"調律石{tuningCost}と欠片{shardCost}が必要です", $"Need {tuningCost} tuning and {shardCost} shards")
                    : _confirmLimitBreak
                        ? Loc.T($"<color=#ff8080>「{materialName}」を素材に限界突破します。もう一度押すと確定</color>", $"<color=#ff8080>Limit break using \"{materialName}\"? Press again</color>")
                        : Loc.T($"限界突破する（上限 +{capNext}）", $"Limit break (cap becomes +{capNext})");
            if (GUILayout.Button(label, _st.ButtonWrap, ShrinkMin30))
            {
                if (!_confirmLimitBreak) _confirmLimitBreak = true;
                else
                {
                    string material = _limitBreakMaterial;
                    Act(() => Rules.LimitBreak(p, sel.Uid, material, _s.Trades), true);
                    _limitBreakOpen = false;
                    _confirmLimitBreak = false;
                    _limitBreakMaterial = null;
                }
            }
            GUI.enabled = true;
        }

        private Vector2 _scrollForge;

        private void ValidateEnhanceConfirmation()
        {
            var r = _confirmEnhance;
            if (r == null) return;
            var p = _s.Profile;
            if (!Open || _tab != 1 || _selected != r.Uid || p.FindStash(r.Uid) != r
                || r.Enhance + 1 != _confirmEnhanceTarget || r.Enhance >= Content.MaxEnhanceFor(r)
                || p.Material(Materials.Shard) < Content.EnhanceCost(r)
                || _s.Trades.IsReserved(r.Uid) || p.RetuneOffer != null)
                _confirmEnhance = null;
        }

        private void Act(Func<GameEvent> action, bool affectsBuild)
        {
            _confirmEnhance = null;
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

        private sealed class StarNode
        {
            public int Rank, Choice = -1;
            public bool Allocated, Available, Unlocked, PairEquippedA, PairEquippedB, SearchMatch, SummaryHover;
            public string Description, SearchDescription, EffectSummary, MissingRequirements;
            public PairComboDef PairDefinition;
            public ClusterRegionKind? Region;
            public GUIContent[] ChoiceOptions;
            public Texture2D Icon;
            public bool Keystone, Pair;
            public float TooltipWidth, TooltipHeight;
            public readonly GUIContent Name = new GUIContent();
            public readonly GUIContent RankLabel = new GUIContent();
            public readonly GUIContent Tooltip = new GUIContent();
            public readonly GUIContent KeystoneLabel = new GUIContent();
        }

        // Layout and localized text survive IMGUI events; only state changes rebuild descriptions.
        private HeroTreeLayout _starLayout;
        private StarNode[] _starNodes;
        private string _starHero, _starKeystoneSignature;
        private readonly GUIContent _starPoints = new GUIContent();
        private readonly GUIContent _starProgress = new GUIContent();
        private HeroState _starState;
        private bool _starJapanese, _starDirty = true, _starDragging, _starMoved;
        private int _starXp, _starKills, _starCodex, _starTestBonus, _starFree;
        private int _starPressed = -1, _starMouseButton, _starDragControl;
        private Vector2 _starPan, _starDragOrigin, _starPanOrigin;
        private float _starZoom = 1f, _starMinX, _starMaxX, _starMinY, _starMaxY;
        private bool _starNeedsFit = true;
        private int[] _starKeystones = new int[0];
        private string _starChoiceId;
        // 選択パネルの星の添え字（#45: パネルを出している間の毎パスの全走査を避ける）。
        private int _starChoiceIndex = -1;
        private readonly StarMapView _starView = new StarMapView();
        private string _starSearch = "";
        private bool _starSearchDirty = true;
        private int[] _starMatches = new int[0];
        private int _starMatchCount, _starMatchCursor = -1;
        private readonly GUIContent _starSearchStatus = new GUIContent();
        private static readonly GUILayoutOption[] StarRowHeight = { GUILayout.Height(30) };
        private static readonly GUILayoutOption[] StarKeystoneWidth = { GUILayout.Width(110) };
        // 「ミストの刻印：」のように旅人の名前が入るので、少し広く取る。星を探す欄の見出しも同じ幅にそろえる。
        private const float StarKeystoneHeaderPx = 190f;
        private static readonly GUILayoutOption[] StarKeystoneHeaderWidth = { GUILayout.Width(StarKeystoneHeaderPx) };
        private const int StarKeystonesPerRow = 5;
        // 刻印の帯の「刻印 n/枠数」と次の枠のヒント。Rebuild ではなく状態変更のたびに作り直す。
        private readonly GUIContent _starKeystoneSlots = new GUIContent();
        // Keystone hovered in the bar (repaint of the bar happens before the canvas), shown with the canvas tooltip.
        private int _starKeystoneHover = -1;
        private GUIStyle _starRankStyle, _starTooltipStyle, _starNameStyle, _starSearchStyle;
        private static Texture2D _starDisc, _starSearchRing;
        private const float StarMinZoom = 0.15f, StarMaxZoom = 3f;
        // 全体表示で端の星の光輪・名前（下に出る）が切れないよう、各辺に残す余白（画面の単位）。
        private const float StarFitMargin = 64f;
        private static readonly Color StarKeystone = new Color(0.8f, 0.55f, 1f);
        private static readonly Color StarPair = new Color(0.45f, 0.9f, 0.95f);
        private static readonly GUILayoutOption[] StarFill = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] StarCanvasSize =
            { GUILayout.MinHeight(160), GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] StarHorizontalSize = { GUILayout.ExpandHeight(true) };
        private static readonly Color StarGold = new Color(1f, 0.79f, 0.32f);
        private static readonly Color StarBright = new Color(0.87f, 0.94f, 1f);
        private static readonly Color StarGrey = new Color(0.36f, 0.39f, 0.46f);
        private static readonly Color StarFocus = new Color(134f / 255f, 199f / 255f, 1f);
        private static readonly Color StarRelation = new Color(168f / 255f, 223f / 255f, 1f);

        private bool _starSumOpen, _starSumDirty = true;
        private int _starSumSig, _starSumHover = -1;
        private Vector2 _starSumScroll;
        private readonly List<StarSumEntry> _starSumEntries = new List<StarSumEntry>();
        private readonly GUIContent _starSumHeader = new GUIContent();
        private GUIStyle _starSumLine, _starSumTitle;
        private float _starSumWidth = -1f, _starSumHeight;
        private static readonly GUILayoutOption[] StarSumWidth = { GUILayout.Width(420), GUILayout.ExpandHeight(true) };

        private sealed class StarSumEntry
        {
            public readonly GUIContent Content = new GUIContent();
            public int[] Nodes;
            public bool Title;
            public float Y, Height;
        }

        private void CancelStarDrag()
        {
            if (_starDragControl != 0 && GUIUtility.hotControl == _starDragControl) GUIUtility.hotControl = 0;
            _starDragControl = 0;
            _starDragging = false;
            _starPressed = -1;
        }

        private void RebuildStarTree(string hero)
        {
            bool newHero = _starHero != hero;
            _starHero = hero;
            if (newHero) { _starChoiceId = null; _starChoiceIndex = -1; }
            _starJapanese = Loc.Japanese;
            _starLayout = HeroTreeLayout.ForHero(hero);
            _starNodes = new StarNode[_starLayout.Nodes.Count];
            _starMatches = new int[_starNodes.Length];
            _starMatchCursor = -1;
            _starSearchDirty = true;
            _starMinX = _starMinY = float.MaxValue;
            _starMaxX = _starMaxY = float.MinValue;
            var keystones = new List<int>();
            for (int i = 0; i < _starNodes.Length; i++)
            {
                var node = _starLayout.Nodes[i];
                var t = node.Talent;
                var pair = t == null ? null : PairCombos.ForBridge(t.Id);
                string iconKey = StarIconKey(t);
                _starNodes[i] = new StarNode
                {
                    Description = t == null ? null : StarMapPresentation.DisplayDescription(t),
                    ChoiceOptions = t != null && t.IsChoice ? new[] { new GUIContent(), new GUIContent() } : null,
                    Icon = RelicIcons.For("stars/" + (iconKey == "choice" ? "link" : iconKey)),
                    Keystone = t != null && t.IsKeystone,
                    Pair = pair != null,
                    PairDefinition = pair,
                    Region = t?.Cluster?.Region.Kind,
                };
                _starNodes[i].Name.text = t == null ? Loc.T("始まり", "Start") : pair != null ? pair.Name.ToString() : t.Name.ToString();
                _starNodes[i].EffectSummary = t == null ? "" : t.IsChoice ? StarMapPresentation.EffectSummary(t)
                    : StarMapPresentation.EffectSummary(_starNodes[i].Description);
                if (t != null && t.IsKeystone) keystones.Add(i);
                _starMinX = Mathf.Min(_starMinX, node.X); _starMaxX = Mathf.Max(_starMaxX, node.X);
                _starMinY = Mathf.Min(_starMinY, node.Y); _starMaxY = Mathf.Max(_starMaxY, node.Y);
            }
            _starKeystones = keystones.ToArray();
            RebuildStarClusters();
            if (newHero) _starNeedsFit = true;
            CancelStarDrag();
            _starDirty = true;
            if (_starRankStyle == null)
            {
                _starRankStyle = new GUIStyle(_st.Label)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 15, wordWrap = false,
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _starTooltipStyle = new GUIStyle(_st.Label)
                {
                    fontSize = 17, wordWrap = true, padding = new RectOffset(12, 12, 10, 10)
                };
                _starNameStyle = new GUIStyle(_st.Label)
                {
                    alignment = TextAnchor.UpperCenter, fontSize = 14, wordWrap = false, clipping = TextClipping.Overflow,
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _starSearchStyle = new GUIStyle(GUI.skin.textField)
                {
                    font = _st.Label.font, fontSize = 16, fixedHeight = 26f, wordWrap = false
                };
                _starRankStyle.fontSize = 12;
            }
        }

        private void RefreshStarState(Profile p, string hero, HeroState hs)
        {
            if (_starHero != hero || _starJapanese != Loc.Japanese || _starLayout != HeroTreeLayout.ForHero(hero))
            { RebuildStarTree(hero); _starSumDirty = true; }
            // 再構築は Layout パスでのみ行う（#45）。約900星ぶんの文字列をマウスイベントのパスで作り直すと、
            // 星を選んだ直後のドラッグ開始が引っかかる。選択の可否は Rules が毎回検証するので、
            // 表示が1フレーム遅れて変わっても操作には影響しない。
            if (Event.current.type != EventType.Layout) return;
            string keystones = string.Join("\u0001", hs.Keystones);
            bool changed = _starDirty || _starState != hs || _starXp != hs.StarXp || _starKills != hs.Kills
                || _starCodex != p.CodexBonusPoints || _starTestBonus != Profile.TestBonusPoints
                || _starKeystoneSignature != keystones;
            var marks = LinkMarks();
            bool matchingHero = _s.LocalHero != null && ClientSession.HeroKeyOf(_s.LocalHero) == hero;
            if (_starSumOpen)
            {
                // Equipped memories/allies only change the check marks; a cheap order-free signature avoids per-frame text building.
                int sig = matchingHero ? 1 : 0;
                foreach (string m in _linkMemories) sig ^= m.GetHashCode();
                foreach (string m in _linkEssences) sig += m.GetHashCode();
                foreach (string m in _linkAllies) sig -= m.GetHashCode();
                // v1.32 B: the live RunGrowth stacks are part of the acquired-effects text.
                if (matchingHero) sig += _s.RunGrowthVersion * 7919;
                if (sig != _starSumSig) { _starSumSig = sig; _starSumDirty = true; }
            }
            for (int i = 0; i < _starNodes.Length; i++)
            {
                var n = _starNodes[i];
                var t = _starLayout.Nodes[i].Talent;
                int rank = t == null ? 1 : t.IsKeystone ? (hs.HasKeystone(t.Id) ? 1 : 0)
                    : hs.Talents.TryGetValue(t.Id, out var r) ? r : 0;
                if (n.Rank != rank) changed = true;
                n.Rank = rank;
                n.Allocated = rank > 0;
                int choice = t != null && t.IsChoice && hs.TalentChoices.TryGetValue(t.Id, out int option) ? option : -1;
                if (n.Choice != choice) changed = true;
                n.Choice = choice;
                var pair = n.PairDefinition;
                if (pair != null)
                {
                    bool a = matchingHero && marks != null && marks(pair.RouteA);
                    bool b = matchingHero && marks != null && marks(pair.RouteB);
                    if (n.PairEquippedA != a || n.PairEquippedB != b) changed = true;
                    n.PairEquippedA = a;
                    n.PairEquippedB = b;
                }
            }
            if (!changed) return;
            int spent = Rules.SpentPoints(hs, hero);
            _starState = hs;
            _starXp = hs.StarXp;
            _starKills = hs.Kills;
            _starCodex = p.CodexBonusPoints;
            _starTestBonus = Profile.TestBonusPoints;
            _starKeystoneSignature = keystones;
            _starFree = p.TalentPoints(hero) - spent;
            _starDirty = false;
            _starSumDirty = true;
            int earned = StarProgression.Points(hs.StarXp);
            string damageRank = StarDamageScaling.Multiplier(spent).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            _starPoints.text = Loc.T($"使えるポイント：残り {_starFree} / 合計 {p.TalentPoints(hero)}　星ダメージ ×{damageRank}",
                $"Available points: {_starFree} remaining / {p.TalentPoints(hero)} total   Star damage ×{damageRank}");
            _starPoints.tooltip = Loc.T("使用済み1点ごとに星由来のダメージ量+0.3%。500点で2.5倍、504点で2.512倍。装備・加速・CD・軽減・発動条件は対象外。星の記憶ダメージはCrescendoの合計上限120%の外へ加える。",
                "Star damage quantities gain 0.3% per spent point: ×2.5 at 500, ×2.512 at 504. Equipment, haste, cooldowns, reduction and activation conditions are unchanged. Star memory damage is added outside Crescendo's combined 120% cap.");
            _starProgress.text = Loc.T($"星のレベル {earned}/{StarProgression.MaxPoints}　", $"Star level {earned}/{StarProgression.MaxPoints}   ")
                + (earned >= StarProgression.MaxPoints ? Loc.T("ポイント上限", "Point cap reached")
                : Loc.T($"次まで {hs.StarXp - StarProgression.TotalXpForPoints(earned)}/{StarProgression.CostForPoint(earned + 1)} XP",
                    $"Next: {hs.StarXp - StarProgression.TotalXpForPoints(earned)}/{StarProgression.CostForPoint(earned + 1)} XP"));
            var reachable = _starLayout.ReachabilitySnapshot(hs);
            int slots = hs.KeystoneSlotCount;
            bool freeSlot = hs.KeystoneCount < slots;
            _starKeystoneSlots.text = StarMapPresentation.KeystoneSlotStatus(hs.KeystoneCount, slots)
                + "　" + StarMapPresentation.NextSlotText(earned);
            for (int i = 0; i < _starNodes.Length; i++)
            {
                var n = _starNodes[i];
                n.TooltipWidth = 0;
                var t = _starLayout.Nodes[i].Talent;
                if (t == null)
                {
                    n.RankLabel.text = "+";
                    n.Tooltip.text = Loc.T("<b>始まりの星</b>\n取得済み。ここからつながる星へ伸ばせます。",
                        "<b>Starting star</b>\nAlready acquired. Grow along its connections.");
                    continue;
                }
                bool unlocked = t.IsKeystone ? Rules.KeystoneUnlocked(p, hero, t) : _starLayout.CanReach(hs, t, reachable);
                n.Unlocked = unlocked;
                int keyCost = t.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
                int cost = t.IsKeystone ? keyCost : t.RankCost;
                bool slotsFull = t.IsKeystone && !n.Allocated && !freeSlot;
                if (t.IsKeystone)
                {
                    string keystoneState = n.Allocated ? Loc.T("<color=#ffc952>選択中</color>", "<color=#ffc952>active</color>")
                        : !unlocked ? Loc.T("<color=#ffb090>条件未達</color>", "<color=#ffb090>locked</color>")
                        : slotsFull ? Loc.T("<color=#ffb090>枠がいっぱい</color>", "<color=#ffb090>no free slot</color>")
                        : Loc.T("<color=#9fe0ff>選べる</color>", "<color=#9fe0ff>available</color>");
                    n.KeystoneLabel.text = "<color=#cc8cff>◆</color> " + n.Name.text + "  " + keystoneState;
                }
                n.Available = unlocked && n.Rank < t.MaxRank && !slotsFull && _starFree >= cost;
                n.RankLabel.text = n.Rank + "/" + t.MaxRank;
                n.MissingRequirements = StarMapPresentation.MissingRequirements(_starLayout, i, hs, _starFree, reachable[i]);
                string condition = n.MissingRequirements;
                string state = n.Rank >= t.MaxRank ? Loc.T("取得済み（最大段）", "Acquired (maximum rank)")
                    : n.Available ? Loc.T("取得可能", "Available") : Loc.T("条件不足", "Requirements missing");
                var pair = n.PairDefinition;
                string title = pair == null ? t.Name.ToString() : pair.Name.ToString();
                string description = pair != null && t.Mechanism == null
                    ? StarMapPresentation.DisplayDescription(t, Math.Max(1, n.Rank)) : n.Description;
                if (t.IsChoice)
                {
                    description = StarMapPresentation.ChoiceDescription(t, n.Choice, n.Rank, true);
                    for (int optionIndex = 0; optionIndex < 2; optionIndex++)
                        n.ChoiceOptions[optionIndex].text = StarMapPresentation.ChoiceOptionBody(t, optionIndex, true)
                            + "\n" + StarMapPresentation.DamageRankNote(t, optionIndex, spent);
                    n.EffectSummary = StarMapPresentation.EffectSummary(t, n.Choice);
                }
                n.SearchDescription = StarMapPresentation.PresentationLabel(t) + "\n" + description;
                if (pair != null)
                {
                    description += "\n" + (n.PairEquippedA ? "✓ " : "・ ") + Links.ItemName(pair.RouteA)
                        + Loc.T("を装着", " equipped")
                        + "\n" + (n.PairEquippedB ? "✓ " : "・ ") + Links.ItemName(pair.RouteB)
                        + Loc.T("を装着", " equipped");
                }
                // 必要ポイントは、本文に既に書かれていれば繰り返さない（刻印は本文の末尾に入っている）。
                bool costInBody = description.Contains(Loc.T("ポイント", "point"));
                string readyColor = !unlocked || slotsFull ? "#ffb090" : n.Rank >= t.MaxRank ? "#ffc952" : _starFree < cost ? "#ffb090" : "#9fe0ff";
                n.Tooltip.text = "<b>" + title + "</b>  " + n.RankLabel.text + "\n<color=#d2d2e6>" + StarMapPresentation.PresentationLabel(t) + "</color>"
                    + "\n" + description
                    + "\n" + StarMapPresentation.DamageRankNote(t, n.Choice, spent)
                    + (costInBody ? "" : Loc.T($"\n必要ポイント：{(t.IsKeystone ? keyCost : t.RankCost)}", $"\nPoint cost: {(t.IsKeystone ? keyCost : t.RankCost)}"))
                    + (condition.Length == 0 ? "" : "\n" + condition)
                    + "\n<color=" + readyColor + ">" + state + "</color>"
                    + (t.IsKeystone ? Loc.T("\n左クリック：選ぶ　右クリック：外す（費用は戻ります）",
                        "\nLeft click: select. Right click: remove (its cost is refunded).")
                        : t.IsChoice ? Loc.T("\n左クリック：選択パネルを開く（切り替えは無料・遠征外のみ）　右クリック：1段外す",
                        "\nLeft click: open the option panel (switching is free outside expeditions). Right click: refund one rank.")
                        : Loc.T("\n左クリック：1段振る　右クリック：1段外す",
                            "\nLeft click: allocate one rank. Right click: refund one rank."))
                    + Loc.T("\n残りの星が始まりにつながる場合だけ外せます。",
                        "\nRefunds require all remaining stars to stay connected to the start.");
            }
            RefreshStarClusters();
            _starSearchDirty = true;
        }

        private void DrawTalentTab()
        {
            var p = _s.Profile;
            string beforePicker = HeroKey;
            HeroPicker();
            string hero = HeroKey;
            if (hero != beforePicker) { CancelStarDrag(); GUIUtility.ExitGUI(); }
            RefreshStarState(p, hero, p.Hero(hero));
            GUILayout.BeginHorizontal();
            GUILayout.Label(_starPoints, _st.Label, StarFill);
            if (GUILayout.Button(Loc.T("全体を表示", "Show all"), _st.Button))
            {
                _starNeedsFit = true;
                CancelStarDrag();
            }
            if (GUILayout.Button(Loc.T("始まりに戻る", "Back to start"), _st.Button))
                StarJumpTo(_starLayout.StartIndex);
            bool wasOpen = _starSumOpen;
            _starSumOpen = GUILayout.Toggle(_starSumOpen, Loc.T("取得した効果", "Acquired effects"), _st.Button);
            if (_starSumOpen != wasOpen) { _starSumDirty = true; if (!_starSumOpen) StarSumSetHover(-1); CancelStarDrag(); }
            GUI.enabled = _s.CanEditTalents && p.Run == null;
            if (GUILayout.Button(Loc.T("振り直し（無料）", "Respec (free)"), _st.Button))
            {
                Rules.ResetTalents(p, hero);
                _starDirty = true;
                _s.MarkDirty(true);
                GUIUtility.ExitGUI();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            EnsureStarTextStyles();
            GUILayout.Label(_starProgress, _starHelpStyle);
            if (_starFree < 0) GUILayout.Label(Loc.T("振った星が現在のポイントを超えています。無料で振り直せます。", "Your spent stars exceed your current points. Respec is free."), _st.Warn);
            if (!_s.CanEditTalents || p.Run != null)
                GUILayout.Label(!_s.InGame && p.Run != null
                    ? Loc.T("中断中の遠征が終わるまで、星図は変更できません。", "Star Map edits are locked until the suspended expedition ends.")
                    : Loc.T("星図は遠征に出ていないときだけ変更できます。", "The star map can only be changed outside expeditions."), _st.Warn);
            DrawKeystoneBar(p, hero);
            if (Event.current.type == EventType.Layout) _starChoiceShown = _starChoiceId != null;
            StarDrawSearch();
            DrawStarLegend();
            GUILayout.BeginHorizontal(StarHorizontalSize);
            DrawStarClusterList();
            Rect starCanvas = GUILayoutUtility.GetRect(0, 10000, 0, 10000, StarCanvasSize);
            _starOverlay = StarChoiceOverlayRect(starCanvas);
            DrawStarCanvas(starCanvas, p, hero);
            DrawStarChoicePicker(p, hero, _starOverlay);
            if (_starSumOpen) DrawStarSummaryPanel(p, hero);
            GUILayout.EndHorizontal();
            DrawStarLegendTooltip();
        }

        private void StarSumSetHover(int index)
        {
            if (_starSumHover == index) return;
            if (_starSumHover >= 0 && _starSumHover < _starSumEntries.Count && _starSumEntries[_starSumHover].Nodes != null)
                foreach (int i in _starSumEntries[_starSumHover].Nodes) if (i < _starNodes.Length) _starNodes[i].SummaryHover = false;
            _starSumHover = index;
            if (index >= 0 && index < _starSumEntries.Count && _starSumEntries[index].Nodes != null)
                foreach (int i in _starSumEntries[index].Nodes) if (i < _starNodes.Length) _starNodes[i].SummaryHover = true;
        }

        private void StarSumAdd(List<StarSummaryLine> lines, Dictionary<string, int> indexOf, string title)
        {
            if (lines.Count == 0) return;
            StarSumAddTitle(title);
            foreach (var line in lines) StarSumAddLine(line, indexOf);
        }

        private void StarSumAddTitle(string title)
        {
            var e = new StarSumEntry { Title = true };
            e.Content.text = title;
            _starSumEntries.Add(e);
        }

        private void StarSumAddLine(StarSummaryLine line, Dictionary<string, int> indexOf)
        {
            if (line.StarIds.Count == 1 && indexOf.TryGetValue(line.StarIds[0], out int typedIndex)
                && _starLayout.Nodes[typedIndex].Talent.Mechanism != null)
            {
                var node = _starNodes[typedIndex];
                string text = node.Name.text + "  " + node.RankLabel.text + "\n" + node.Description;
                var pair = node.PairDefinition;
                if (pair != null)
                    text += "\n" + (node.PairEquippedA ? "✓ " : "・ ") + Links.Name(pair.RouteA) + Loc.T("を装着", " equipped")
                        + "\n" + (node.PairEquippedB ? "✓ " : "・ ") + Links.Name(pair.RouteB) + Loc.T("を装着", " equipped");
                StarSumAddNode(typedIndex, text);
                return;
            }
            var nodes = new List<int>(line.StarIds.Count);
            foreach (string id in line.StarIds)
            {
                if (!indexOf.TryGetValue(id, out int i))
                    throw new InvalidOperationException("Acquired effect references a missing star: " + id);
                nodes.Add(i);
            }
            var e = new StarSumEntry { Nodes = nodes.ToArray() };
            e.Content.text = line.Text;
            _starSumEntries.Add(e);
        }

        // Rebuilt only when allocations, keystone, language or equipped memories change.
        private void StarSumRebuild(Profile p, string hero)
        {
            foreach (var n in _starNodes) n.SummaryHover = false;
            _starSumHover = -1;
            _starSumDirty = false;
            _starSumEntries.Clear();
            _starSumWidth = -1f;
            bool matching = _s.LocalHero != null && ClientSession.HeroKeyOf(_s.LocalHero) == hero;
            var marks = matching ? LinkMarks() : null;
            var sum = StarSummary.Compute(p, hero, marks);
            var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _starLayout.Nodes.Count; i++)
                if (_starLayout.Nodes[i].Talent != null) indexOf[_starLayout.Nodes[i].Talent.Id] = i;
            _starSumHeader.text = Loc.T($"使ったポイント {sum.Spent} / 合計 {sum.Total}", $"Points used {sum.Spent} / {sum.Total} total");
            if (sum.Spent == 0) { StarSumAddTitle(Loc.T("まだ星を取得していません。", "No stars acquired yet.")); return; }
            var note = new StarSumEntry();
            note.Content.text = "<color=#9aa>" + Loc.T("発動間隔や上限は、各星の説明で確認できます。", "Cooldowns and caps are listed in each star's description.") + "</color>";
            _starSumEntries.Add(note);
            for (int i = 0; i < _starNodes.Length; i++)
                if (_starNodes[i].Allocated && _starNodes[i].Keystone)
                {
                    StarSumAddTitle(Loc.T("刻印", "Keystone"));
                    StarSumAddNode(i, _starNodes[i].Name.text + "\n" + _starNodes[i].Description);
                }
            StarSumAdd(sum.Stats, indexOf, Loc.T("能力値", "Stats"));
            StarSumAdd(sum.Powers, indexOf, Loc.T("固有効果", "Powers"));
            if (sum.RunGrowths.Count > 0)
            {
                StarSumAddTitle(Loc.T("遠征の鍛錬", "Expedition training"));
                foreach (var growth in sum.RunGrowths)
                {
                    // Only the traveler in play has stacks to show; another traveler's page shows the definition.
                    var live = new StarSummaryLine { Growth = growth.Growth,
                        Text = matching ? RunGrowth.SummaryLine(growth.Growth, _s.GrowthStacks(growth.Growth.StarId)) : growth.Text };
                    live.StarIds.AddRange(growth.StarIds);
                    StarSumAddLine(live, indexOf);
                }
            }
            if (sum.Memories.Count > 0)
            {
                StarSumAddTitle(Loc.T("記憶ごとの効果", "Effects by memory"));
                foreach (var g in sum.Memories)
                {
                    StarSumAddTitle("  " + g.Title);
                    foreach (var line in g.Lines) StarSumAddLine(line, indexOf);
                }
            }
            bool choiceTitle = false, mechanismTitle = false;
            for (int i = 0; i < _starNodes.Length; i++)
            {
                var t = _starLayout.Nodes[i].Talent;
                var n = _starNodes[i];
                if (t == null || !n.Allocated || n.Keystone) continue;
                if (t.IsChoice)
                {
                    if (!choiceTitle) { StarSumAddTitle(Loc.T("選択の星", "Choice stars")); choiceTitle = true; }
                    StarSumAddNode(i, n.Name.text + "  " + n.RankLabel.text + "\n"
                        + StarMapPresentation.ChoiceDescription(t, n.Choice, n.Rank));
                }
                else if (t.Mechanism != null && n.PairDefinition == null)
                {
                    if (!mechanismTitle) { StarSumAddTitle(Loc.T("仕掛けの効果", "Mechanism effects")); mechanismTitle = true; }
                    StarSumAddNode(i, n.Name.text + "  " + n.RankLabel.text + "\n" + n.Description);
                }
            }
        }

        private void DrawStarSummaryPanel(Profile p, string hero)
        {
            if (_starSumLine == null)
            {
                _starSumLine = new GUIStyle(_st.Small) { fontSize = 15, wordWrap = true, richText = true, padding = new RectOffset(6, 4, 3, 3) };
                _starSumTitle = new GUIStyle(_st.Label) { fontSize = 17, wordWrap = true, richText = true, fontStyle = FontStyle.Bold };
            }
            if (_starSumDirty && Event.current.type == EventType.Layout) StarSumRebuild(p, hero);
            GUILayout.BeginVertical(_st.Panel, StarSumWidth);
            GUILayout.Label(_starSumHeader, _st.Header);
            Rect area = GUILayoutUtility.GetRect(0, 10000, 0, 10000, StarCanvasSize);
            int hover = -1;
            bool repaint = Event.current.type == EventType.Repaint;
            if (Event.current.type != EventType.Layout)
            {
                float width = Mathf.Max(1, area.width - 18);
                if (_starSumWidth != width)
                {
                    _starSumWidth = width;
                    _starSumHeight = 0;
                    for (int i = 0; i < _starSumEntries.Count; i++)
                    {
                        var entry = _starSumEntries[i];
                        entry.Y = _starSumHeight;
                        entry.Height = (entry.Title ? _starSumTitle : _starSumLine).CalcHeight(entry.Content, width) + 4;
                        _starSumHeight += entry.Height;
                    }
                }
                _starSumScroll = GUI.BeginScrollView(area, _starSumScroll, new Rect(0, 0, width, _starSumHeight));
                try
                {
                    // Binary-search the first visible row; draw only the viewport's rows.
                    int lo = 0, hi = _starSumEntries.Count;
                    while (lo < hi)
                    {
                        int mid = lo + (hi - lo) / 2;
                        var entry = _starSumEntries[mid];
                        if (entry.Y + entry.Height < _starSumScroll.y) lo = mid + 1;
                        else hi = mid;
                    }
                    for (int i = lo; i < _starSumEntries.Count; i++)
                    {
                        var entry = _starSumEntries[i];
                        if (entry.Y > _starSumScroll.y + area.height) break;
                        var row = new Rect(0, entry.Y, width, entry.Height);
                        GUI.Label(row, entry.Content, entry.Title ? _starSumTitle : _starSumLine);
                        if (repaint && !entry.Title && row.Contains(Event.current.mousePosition)) hover = i;
                    }
                }
                finally { GUI.EndScrollView(); }
            }
            GUILayout.EndVertical();
            if (repaint) StarSumSetHover(hover);
        }

        private void StarRefreshSearch()
        {
            if (!_starSearchDirty) return;
            _starSearchDirty = false;
            _starMatchCount = 0;
            for (int i = 0; i < _starNodes.Length; i++)
            {
                var node = _starNodes[i];
                node.SearchMatch = StarMapMath.Matches(node.Name.text, node.SearchDescription, _starSearch);
                if (node.SearchMatch) _starMatches[_starMatchCount++] = i;
            }
            if (_starMatchCursor >= _starMatchCount) _starMatchCursor = -1;
            _starSearchStatus.text = _starSearch.Length == 0 ? ""
                : Loc.T($"{_starMatchCount} 個の星", $"{_starMatchCount} stars");
        }

        private void StarDrawSearch()
        {
            StarRefreshSearch();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("星を探す", "Find star"), _starHelpStyle, StarKeystoneHeaderWidth);
            GUI.SetNextControlName("DreamforgeStarSearch");
            // Handle Enter before TextField can consume it; leave IME composition to the text field.
            var e = Event.current;
            bool next = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "DreamforgeStarSearch" && Input.compositionString.Length == 0;
            if (next) e.Use();
            string query = GUILayout.TextField(_starSearch, _starSearchStyle, StarFill);
            if (query != _starSearch)
            {
                _starSearch = query;
                _starMatchCursor = -1;
                _starSearchDirty = true;
                StarRefreshSearch();
            }
            GUILayout.Label(_starSearchStatus, _starHelpStyle, StarKeystoneWidth);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _starMatchCount > 0;
            if (GUILayout.Button(Loc.T("次の星へ", "Next star"), _st.Button, StarRowHeight)) next = true;
            GUI.enabled = enabled;
            GUILayout.EndHorizontal();
            if (next && _starMatchCount > 0)
            {
                _starMatchCursor = (_starMatchCursor + 1) % _starMatchCount;
                StarJumpTo(_starMatches[_starMatchCursor]);
            }
        }

        private void StarJumpTo(int index)
        {
            var node = _starLayout.Nodes[index];
            var pan = StarMapMath.PanToNode(node.X, node.Y, _starZoom);
            _starPan = new Vector2(pan.X, pan.Y);
            _starNeedsFit = false;
            CancelStarDrag();
        }


        private void DrawStarCanvas(Rect canvas, Profile p, string hero)
        {
            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) return;
            int hover = -1;
            GUI.BeginGroup(canvas);
            try
            {
                var viewport = new Rect(0, 0, canvas.width, canvas.height);
                float minZoom = StarMinZoom;
                if (_starNeedsFit || e.type == EventType.ScrollWheel)
                {
                    // Large layouts must fit below the usual zoom floor; scrolling uses the same lower bound.
                    StarMapMath.FitView(viewport.width, viewport.height, _starMinX, _starMaxX, _starMinY, _starMaxY,
                        StarFitMargin, 0f, StarMaxZoom, out float fitZoom, out var fitPan);
                    minZoom = Mathf.Min(StarMinZoom, fitZoom);
                    if (_starNeedsFit && viewport.width > 50f && viewport.height > 50f)
                    {
                        _starNeedsFit = false;
                        _starZoom = fitZoom;
                        _starPan = new Vector2(fitPan.X, fitPan.Y);
                    }
                }
                Vector2 mouse = e.mousePosition;
                Vector2 windowMouse = mouse + canvas.position;
                // The panel background can pan, but obscured stars cannot be hovered, clicked or zoomed.
                bool insideCanvas = viewport.Contains(mouse);
                bool inside = insideCanvas && !(_starOverlay.width > 0f && _starOverlay.Contains(windowMouse));
                bool canPan = insideCanvas && !StarChoicePanelBlocks(windowMouse);
                float guiScale = Mathf.Abs(GUI.matrix.m00);
                _starView.Update(_starLayout, viewport, _starPan, _starZoom, guiScale);
                if (inside) hover = _starView.Hit(mouse);
                if (inside && e.type == EventType.ScrollWheel)
                {
                    float zoom = Mathf.Clamp(_starZoom * Mathf.Pow(1.12f, -e.delta.y), minZoom, StarMaxZoom);
                    _starPan = mouse - viewport.center - (mouse - viewport.center - _starPan) * (zoom / _starZoom);
                    _starZoom = zoom;
                    e.Use();
                }
                if (canPan && e.type == EventType.MouseDown && e.button <= 2)
                {
                    // ドラッグ中に追加で押したボタンでは、押した場所の星をクリックとして確定させない（#45）。
                    // 移動距離はその場でやり直すので、離した瞬間の誤選択・誤解除が出なくなる。
                    bool joining = _starDragging && _starDragControl != 0 && GUIUtility.hotControl == _starDragControl;
                    _starMoved = false;
                    if (joining) _starPressed = -1;
                    else { _starPressed = hover; _starMouseButton = e.button; }
                    _starDragging = true;
                    GUIUtility.hotControl = control;
                    _starDragControl = control;
                    _starDragOrigin = mouse;
                    _starPanOrigin = _starPan;
                    e.Use();
                }
                if (_starDragging && e.type == EventType.MouseDrag)
                {
                    Vector2 delta = mouse - _starDragOrigin;
                    // クリックとドラッグの境は画面の物理ピクセルで測る（#45）。GUI 座標のままだと表示の倍率が
                    // 小さい環境で2ピクセル程度のふるえがクリック扱いになり、星を意図せず振ってしまう。
                    Vector2 physical = delta * guiScale;
                    if (physical.sqrMagnitude > 25f) _starMoved = true;
                    if (_starMoved) _starPan = _starPanOrigin + delta;
                    e.Use();
                }
                if (_starDragging && e.type == EventType.MouseUp && e.button == _starMouseButton)
                {
                    int pressed = _starPressed;
                    CancelStarDrag();
                    e.Use();
                    if (!_starMoved && inside && hover == pressed && pressed >= 0 && _starMouseButton < 2)
                    {
                        var t = _starLayout.Nodes[pressed].Talent;
                        if (t != null)
                        {
                            if (_starMouseButton == 0 && t.IsChoice)
                            {
                                if (_starChoiceId != t.Id)
                                    _starChoiceScrolls[0] = _starChoiceScrolls[1] = Vector2.zero;
                                _starChoiceId = t.Id;
                                _starChoiceIndex = pressed;
                                GUIUtility.ExitGUI();
                            }
                            if (!_s.CanEditTalents || p.Run != null) return;
                            try
                            {
                                if (_starMouseButton == 1) Rules.RemoveTalentRank(p, hero, t.Id);
                                else if (t.IsKeystone)
                                {
                                    if (!_starNodes[pressed].Allocated) Rules.SetKeystone(p, hero, t.Id);
                                }
                                else Rules.AddTalentRank(p, hero, t.Id);
                                _starDirty = true;
                                _s.MarkDirty(true);
                            }
                            catch (AllocationValidationException ex) { OfferAllocationRefund(ex, p, hero); }
                            catch (InvalidOperationException ex) { SetStatus(ex.Message); }
                        }
                    }
                }
                if (e.type != EventType.Repaint) return;
                _starView.Update(_starLayout, viewport, _starPan, _starZoom, guiScale);
                // Pan/zoom events may have changed the geometry since the initial hit check.
                hover = inside ? _starView.Hit(mouse) : -1;
                int choiceFocus = _starChoiceId != null ? _starChoiceIndex : -1;
                _starView.UpdateRelationships(hover, choiceFocus);
                StarFillRect(viewport, new Color(0.035f, 0.045f, 0.075f));
                Matrix4x4 baseMatrix = GUI.matrix;
                Vector2 unclippedOrigin = StarMapView.UnclippedOrigin(baseMatrix);
                for (int i = 0; i < _starView.EdgeCount; i++)
                {
                    var edge = _starView.VisibleEdges[i];
                    var a = _starNodes[edge.A];
                    var b = _starNodes[edge.B];
                    Color color = a.Allocated && b.Allocated ? StarGold
                        : (a.Allocated && b.Available || b.Allocated && a.Available) ? StarBright : StarGrey * 0.55f;
                    if (_starView.Focused[edge.A] || _starView.Focused[edge.B])
                        StarMapView.DrawEdge(edge, unclippedOrigin, baseMatrix, StarRelation, 6f);
                    StarMapView.DrawEdge(edge, unclippedOrigin, baseMatrix, color);
                }
                var disc = StarDisc();
                for (int v = 0; v < _starView.NodeCount; v++)
                {
                    int i = _starView.VisibleNodes[v];
                    Rect rect = _starView.NodeRects[i];
                    var n = _starNodes[i];
                    Color frame = n.Allocated ? StarGold : n.Keystone ? StarKeystone : n.Pair ? StarPair : n.Available ? StarBright : StarGrey;
                    if (n.SearchMatch || n.SummaryHover)
                        StarDrawDisc(StarSearchRing(), new Rect(rect.x - 11f, rect.y - 11f, rect.width + 22f, rect.height + 22f), StarGold);
                    if (_starView.Focused[i])
                        StarDrawDisc(StarSearchRing(), new Rect(rect.x - 8f, rect.y - 8f, rect.width + 16f, rect.height + 16f), StarFocus);
                    else if (_starView.Related[i])
                        StarDrawDisc(StarSearchRing(), new Rect(rect.x - 5f, rect.y - 5f, rect.width + 10f, rect.height + 10f), StarRelation);
                    if (n.Available && !n.Allocated)
                        StarDrawDisc(disc, new Rect(rect.x - 5f, rect.y - 5f, rect.width + 10f, rect.height + 10f),
                            new Color(StarBright.r, StarBright.g, StarBright.b, 0.22f));
                    if (n.Keystone)
                        StarDrawDisc(disc, new Rect(rect.x - 7f, rect.y - 7f, rect.width + 14f, rect.height + 14f),
                            new Color(frame.r, frame.g, frame.b, 0.3f));
                    StarDrawDisc(disc, rect, frame);
                    float border = (_starView.Focused[i] ? 4f : 2.5f) * Mathf.Clamp(rect.width / 40f, 0.6f, 1.6f);
                    var inner = new Rect(rect.x + border, rect.y + border, rect.width - border * 2, rect.height - border * 2);
                    StarDrawDisc(disc, inner, n.Allocated ? new Color(0.26f, 0.18f, 0.06f) : new Color(0.07f, 0.09f, 0.14f));
                    if (n.Icon != null)
                    {
                        float pad = inner.width * 0.17f;
                        var old = GUI.color;
                        GUI.color = n.Allocated || n.Available ? Color.white : new Color(0.62f, 0.64f, 0.7f, 0.8f);
                        GUI.DrawTexture(new Rect(inner.x + pad, inner.y + pad, inner.width - pad * 2, inner.height - pad * 2), n.Icon, ScaleMode.ScaleToFit, true);
                        GUI.color = old;
                        if (_starLayout.Nodes[i].Talent != null && rect.width >= 24f)
                        {
                            var badge = new Rect(rect.center.x - 17f, rect.yMax - 9f, 34f, 16f);
                            StarFillRect(badge, new Color(0.04f, 0.05f, 0.08f, 0.9f));
                            GUI.Label(badge, n.RankLabel, _starRankStyle);
                        }
                    }
                    else if (rect.width >= 24f) GUI.Label(rect, n.RankLabel, _starRankStyle);
                    if (n.Region.HasValue && rect.width >= 20f)
                        StarDrawDisc(disc, new Rect(rect.xMax - 7f, rect.y - 3f, 8f, 8f), StarRegionColor(n.Region.Value));
                    // Both focus labels stay visible; ordinary labels yield cells to them.
                    if (_starView.Focused[i] || (_starView.Named[i]
                        && (hover < 0 || _starView.LabelCells[i] != _starView.LabelCells[hover])
                        && (choiceFocus < 0 || !_starView.Visible[choiceFocus] || _starView.LabelCells[i] != _starView.LabelCells[choiceFocus])))
                        GUI.Label(new Rect(rect.center.x - 100f, rect.yMax + 7f, 200f, 22f), n.Name, _starNameStyle);
                }
            }
            finally { GUI.EndGroup(); }
            // Tooltips may extend above the viewport, so long effects remain readable.
            if (hover < 0 && _starKeystoneHover >= 0 && e.type == EventType.Repaint) hover = _starKeystoneHover;
            if (hover >= 0 && !_starDragging)
            {
                float width = Mathf.Min(480f, canvas.width - 12f);
                var node = _starNodes[hover];
                if (node.TooltipWidth != width)
                {
                    node.TooltipWidth = width;
                    node.TooltipHeight = _starTooltipStyle.CalcHeight(node.Tooltip, width);
                }
                Vector2 mouse = e.mousePosition;
                float x = mouse.x + 22f;
                if (x + width > canvas.xMax - 6f) x = mouse.x - width - 22f;
                var tip = new Rect(Mathf.Clamp(x, canvas.x + 6f, Mathf.Max(canvas.x + 6f, canvas.xMax - width - 6f)),
                    Mathf.Clamp(mouse.y + 18f, 6f, Mathf.Max(6f, canvas.yMax - node.TooltipHeight - 6f)), width, node.TooltipHeight);
                StarFillRect(tip, new Color(0.1f, 0.12f, 0.18f));
                GUI.Label(tip, node.Tooltip, _starTooltipStyle);
            }
        }

        /// <summary>星の記号（icons/stars/*.png）の名前。画像が無ければ段の数字だけを描く。</summary>
        private static string StarIconKey(TalentDef t)
        {
            if (t == null) return "start";
            if (t.IsKeystone) return "keystone";
            if (t.IsChoice) return "choice";
            if (PairCombos.ForBridge(t.Id) != null) return "pair";
            if (t.Gimmick != null || t.GimmickBoost > 0 || t.GimmickParameter.HasValue) return "gimmick";
            if (t.LinkPerRank != null) return t.LinkPerRank.Kind == LinkKind.MemoryDamage
                || t.LinkPerRank.Kind == LinkKind.MemoryHaste ? "memory" : "link";
            if (t.IsPowerNode || t.RunGrowth != null || t.RunGrowthModifier != null) return "unique";
            switch (t.Stat)
            {
                case Stat.AttackPct: case Stat.AttackFlat: return "attack";
                case Stat.PowerPct: case Stat.PowerFlat: return "power";
                case Stat.AttackSpeedPct: return "aspd";
                case Stat.Haste: return "haste";
                case Stat.CritChancePct: return "critc";
                case Stat.CritDamagePct: return "critd";
                case Stat.MaxHealthPct: case Stat.MaxHealthFlat: return "health";
                case Stat.Armor: return "armor";
                case Stat.HealthRegen: return "regen";
                case Stat.MoveSpeedPct: return "move";
                case Stat.Tenacity: return "tenacity";
                case Stat.FireAmp: return "fire";
                case Stat.ColdAmp: return "cold";
                case Stat.LightAmp: return "light";
                case Stat.DarkAmp: return "dark";
                case Stat.AttackRangePct: return "range";
                case Stat.FourthAttackShift: return "fourth";
                case Stat.EssenceSlotIdentity: case Stat.EssenceSlotMovement: return "essence";
                case Stat.HealPower: return "heal";
                case Stat.ShieldPower: return "shield";
                case Stat.SummonPower: return "summon";
                case Stat.SacrificeReduction: return "sacrifice";
                default: return "ring";
            }
        }

        /// <summary>
        /// 刻印の一覧。図のどこにあるか探さなくても、選べる刻印とその状態が分かるようにする。
        /// 押すと図をその刻印へ寄せ、条件を満たしていれば選ぶ。選択済みは右クリックで外す。
        /// 見出しの行に「刻印 n/枠数」と、次の枠が開く星のレベルを出す。
        /// </summary>
        private void DrawKeystoneBar(Profile p, string hero)
        {
            if (_starKeystones.Length == 0) return;
            // v1.31: travelers have 8–10 keystones, so the bar wraps into rows instead of overflowing one line.
            if (Event.current.type == EventType.Repaint) _starKeystoneHover = -1;
            for (int k = 0; k < _starKeystones.Length; k++)
            {
                if (k % StarKeystonesPerRow == 0)
                {
                    if (k > 0) GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (k == 0)
                    {
                        GUILayout.Label(HeroNames.KeystoneHeader(hero), _starHelpStyle, StarKeystoneHeaderWidth);
                        GUILayout.Label(_starKeystoneSlots, _starHelpStyle);
                    }
                    else GUILayout.Space(StarKeystoneHeaderPx + 4f);
                }
                int index = _starKeystones[k];
                var t = _starLayout.Nodes[index].Talent;
                var state = _starNodes[index];
                bool chosen = state.Allocated;
                bool unlocked = state.Unlocked;
                bool pressed = GUILayout.Button(state.KeystoneLabel, chosen ? _st.RowSel : _st.Row, StarRowHeight);
                Rect buttonRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.Repaint && buttonRect.Contains(Event.current.mousePosition))
                    _starKeystoneHover = index;
                if (chosen && _s.CanEditTalents && p.Run == null
                    && Event.current.type == EventType.MouseDown && Event.current.button == 1 && buttonRect.Contains(Event.current.mousePosition))
                {
                    Event.current.Use();
                    try
                    {
                        Rules.RemoveKeystone(p, hero, t.Id);
                        _starDirty = true;
                        _s.MarkDirty(true);
                    }
                    catch (AllocationValidationException ex) { OfferAllocationRefund(ex, p, hero); }
                    catch (InvalidOperationException ex) { SetStatus(ex.Message); }
                    continue;
                }
                if (pressed)
                {
                    StarJumpTo(index);
                    if (!chosen && unlocked && _s.CanEditTalents && p.Run == null)
                    {
                        try
                        {
                            Rules.SetKeystone(p, hero, t.Id);
                            _starDirty = true;
                            _s.MarkDirty(true);
                        }
                        catch (AllocationValidationException ex) { OfferAllocationRefund(ex, p, hero); }
                        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
                    }
                }
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>縁をなめらかにした白い円（一度だけ作って使い回す）。</summary>
        private static Texture2D StarDisc()
        {
            if (_starDisc != null) return _starDisc;
            const int size = 64;
            _starDisc = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float c = (size - 1) / 2f, r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
                }
            _starDisc.SetPixels32(pixels);
            _starDisc.Apply();
            return _starDisc;
        }
        /// <summary>Search halo with a transparent center, generated once and reused for every match.</summary>
        private static Texture2D StarSearchRing()
        {
            if (_starSearchRing != null) return _starSearchRing;
            const int size = 96;
            _starSearchRing = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float center = (size - 1) / 2f, radius = size / 2f - 3f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center, dy = y - center;
                    float distance = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(2.5f - distance) * 255f));
                }
            _starSearchRing.SetPixels32(pixels);
            _starSearchRing.Apply();
            return _starSearchRing;
        }

        private static void StarDrawDisc(Texture2D disc, Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, disc, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        private static void StarFillRect(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }


        // DrawWorkshopTab・WorkshopValue は DreamforgeUi.Workshop.cs へ移した（#147 でスクロールに入れた）。

        private string _confirmDust;

        /// <summary>今日の夢が切り替わる時刻（世界時の0時）を、遊んでいる人の時計で。</summary>
        private static string DailyRollover() => DateTime.UtcNow.Date.AddDays(1).ToLocalTime().ToString("H:mm");

        private void DrawRecordsTab(DreamforgeConfig cfg)
        {
            var p = _s.Profile;
            if (_codexOpen)
            {
                if (_codex.Draw(p, _st)) _codexOpen = false;
                return;
            }
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
                $"夢のレベル {p.DreamLevel}（{p.DreamXp}/{need}）\n遠征 {st.Runs}回　踏破 {st.Victories}　全滅 {st.Defeats}\n撃破 {st.Kills}　遺物 {st.RelicsFound}個（固有品 {st.LegendariesFound}）\n確保した最高潜行 {st.BestHeatSecured}　図鑑 {p.Codex.Count}/{Content.Bases.Count + Content.Uniques.Count}",
                $"Dream Level {p.DreamLevel} ({p.DreamXp}/{need})\nRuns {st.Runs}  Victories {st.Victories}  Defeats {st.Defeats}\nKills {st.Kills}  Relics {st.RelicsFound} (legendary {st.LegendariesFound})\nBest secured depth {st.BestHeatSecured}  Codex {p.Codex.Count}/{Content.Bases.Count + Content.Uniques.Count}"), _st.Small);
            DrawInfinityRecords(p);
            if (p.Run != null)
            {
                GUILayout.Label(_s.InGame
                    ? Loc.T($"今回の遠征（まだ持ち帰っていない遺物{p.Run.Satchel.Count}個）", $"This expedition ({p.Run.Satchel.Count} relics not yet secured)")
                    : Loc.T($"中断中の遠征（まだ持ち帰っていない遺物{p.Run.Satchel.Count}個）", $"Suspended expedition ({p.Run.Satchel.Count} relics not yet secured)"), _st.Header);
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
                foreach (var r in SortedSatchel())
                {
                    GUILayout.BeginHorizontal();
                    IconSlot(r, 28);
                    GUILayout.Label(UiStyles.RelicTitle(r) + $" <color=#bcbcd2>{Content.RarityName(r.Rarity)} Lv{r.ItemLevel}</color>", _st.Small);
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
            GUILayout.Label(Loc.T("図鑑", "Codex"), _st.Header);
            GUILayout.Label(UiStyles.Colored(_codex.Summary(p), "#c8c8e0"), _st.Small);
            if (GUILayout.Button(Loc.T("図鑑を開く（土台・固有品・セット・固有効果・銘品・組）", "Open the codex (bases, legendaries, sets, powers, named items, mini sets)"), _st.Button)) _codexOpen = true;
            GUILayout.Label(Loc.T(
                "<color=#8a8aa0>見つけていない固有品・セット・固有効果・銘品・組は、見つけるまで名前も効果も伏せられています。</color>",
                "<color=#8a8aa0>Unfound legendaries, sets, powers, named items and mini sets stay hidden until you find them.</color>"), _st.Small);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        private void DrawInfinityCaps()
        {
            GUILayout.Label(Loc.T($"報酬上限：一般供給は実戦闘時間予算、Epic以上の抽選は部屋ごとの撃破機会予算も必要です。戦闘1時間あたり無料遺物{InfinityRewards.RelicsPerHour}個、Epic以上の保証は別枠{InfinityRewards.GuaranteesPerHour}個。待機・休止・ロード・再接続では補充しません。",
                $"Reward caps: ordinary supply uses combat-time budgets; Epic+ rolls also require per-room kill opportunity budgets. Free relics: {InfinityRewards.RelicsPerHour}/combat hour; Epic+ guarantees: a separate {InfinityRewards.GuaranteesPerHour}/combat hour. Idle, pause, loading and reconnecting do not refill budgets."), _st.Small);
            GUILayout.Label(Loc.T("欠片・調律石・夢XP・星XP・覚醒・換金機会にも上限があります。Heatボーナスと満杯時の欠片化も対象です。支払済みの対価・旧所持品の回収・有償製作は無料供給と別扱いです。",
                "Shards, tuning, Dream XP, Star XP, awakening and exchange opportunities are capped too, including Heat bonuses and overflow conversion. Paid rewards, recovered existing items and paid crafting are separate from free supply."), _st.Small);
            GUILayout.Label(Loc.T("本体の基本収入と星のゴールド／ダストボーナスは通常モードと同じです。旧資産を使う有償取得も含めた総取得量の上限ではありません。",
                "Native base income and star Gold/Dust bonuses follow normal-mode rules. These are not total-acquisition caps including spending existing assets."), _st.Small);
        }

        private void DrawInfinityRecords(Profile profile)
        {
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("インフィニティ ─ 設定別の確保帰還", "Infinity ─ secured returns by settings"), _st.Header);
            GUILayout.Label(Loc.T("最深確保は帰還時の累計Combatクリア部屋数で比較し、その帰還の圧段階を併記します。敗北・切断・未帰還の到達は更新しません。",
                "Best secured return is ranked by cumulative cleared Combat rooms, with pressure at that return. Defeats, disconnects and unreturned progress do not update it."), _st.Small);
            CacheInfinityRecords(profile);
            GUILayout.Label(_infinityRecordsText, _st.Small);
            if (profile.InfinityRecords.Count >= InfinityRecords.MaximumConfigurations)
                GUILayout.Label(Loc.T("設定グループの保存上限です。既存グループだけ更新できます。",
                    "Configuration storage is full. Only existing groups can be updated."), _st.Warn);
            DrawInfinityCaps();
        }

        private void CacheInfinityRecords(Profile profile)
        {
            if (_infinityRecordsText != null && ReferenceEquals(_infinityRecordsProfile, profile)
                && _infinityRecordsJapanese == Loc.Japanese && _infinityRecordsRevision == profile.InfinityRecordsRevision) return;
            _infinityRecordsProfile = profile;
            _infinityRecordsJapanese = Loc.Japanese;
            _infinityRecordsRevision = profile.InfinityRecordsRevision;
            if (profile.InfinityRecords.Count == 0)
            {
                _infinityRecordsText = Loc.T("確保して帰還した記録はまだありません。", "No secured-return records yet.");
                return;
            }
            var text = new System.Text.StringBuilder();
            foreach (var pair in profile.InfinityRecords)
            {
                var record = pair.Value;
                if (text.Length > 0) text.Append("\n\n");
                string zone = InfinitySettingName(record.FixedZoneId, false);
                string difficulty = InfinitySettingName(record.DifficultyId, true);
                text.Append(Loc.T(
                    $"<b>{zone} · {difficulty}</b>　周期{record.Interval}部屋 · 夢の深さ{record.DreamDepth}\n最高帰還：累計{record.BestReturnedRooms}部屋 · 圧段階{record.PressureAtBestReturn}/{InfinityRunState.MaximumPressureStage}　帰還{record.ReturnCount}回",
                    $"<b>{zone} · {difficulty}</b>  Interval {record.Interval} rooms · Dream Depth {record.DreamDepth}\nBest return: {record.BestReturnedRooms} cumulative rooms · pressure {record.PressureAtBestReturn}/{InfinityRunState.MaximumPressureStage}  Returns {record.ReturnCount}"));
                if (record.LastReturnedRooms.HasValue)
                    text.Append(Loc.T(
                        $"\n直近の帰還：累計{record.LastReturnedRooms.Value}部屋 · 圧段階{record.LastPressure.Value}/{InfinityRunState.MaximumPressureStage}",
                        $"\nLast return: {record.LastReturnedRooms.Value} cumulative rooms · pressure {record.LastPressure.Value}/{InfinityRunState.MaximumPressureStage}"));
            }
            _infinityRecordsText = text.ToString();
        }

        private static string InfinitySettingName(string id, bool difficulty)
        {
            if (string.IsNullOrEmpty(id)) return Loc.T("難易度未記録", "Unrecorded difficulty");
            string key = difficulty ? "Difficulty_" + id + "_Name" : id + "_Name";
            var languages = DewLocalization.buildData.dataByLanguage;
            if (languages.TryGetValue(Loc.Japanese ? "ja-JP" : "en-US", out var language)
                && language.ui.TryGetValue(key, out string label)) return label;
            if (languages.TryGetValue("en-US", out language) && language.ui.TryGetValue(key, out label)) return label;
            string readable = id.StartsWith(difficulty ? "diff" : "Zone_", StringComparison.Ordinal)
                ? id.Substring(difficulty ? 4 : 5) : id;
            return readable.Replace('_', ' ').Replace("<", "").Replace(">", "");
        }
    }
}
