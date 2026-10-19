using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private Vector2 _tradePlayersScroll, _tradeOwnScroll, _tradeOtherScroll;
        private string _tradeDraftId;
        private Profile _tradeDraftProfile;
        private string _tradeShards = "0", _tradeTuning = "0";
        private int _tradeSeenShards, _tradeSeenTuning;
        private readonly Dictionary<Relic, TradeRelicText> _tradeRelicTexts = new Dictionary<Relic, TradeRelicText>();
        private static readonly GUILayoutOption[] TradePaneSize = { GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MinHeight(0) };
        private static readonly GUILayoutOption[] TradeFieldSize = { GUILayout.Width(105) };
        private static readonly GUILayoutOption[] TradeActionSize = { GUILayout.Height(34), GUILayout.MinWidth(0) };
        // A 96px card and 4px gap. Reserve the complete list height, but only draw visible cards.
        private const float TradeRelicRowStep = 100f;
        private readonly List<Relic> _tradeStashChoices = new List<Relic>(), _tradeSatchelChoices = new List<Relic>();
        private float _tradeOwnViewportHeight, _tradeOwnContentHeight;
        private GUIStyle _tradeRelicTitleStyle, _tradeRelicSelectedStyle, _tradeRelicDetailsStyle, _tradeTooltipStyle;
        private UiStyles _tradeRelicStylesFor;
        private readonly GUIContent _tradeTooltipContent = new GUIContent();
        private static readonly GUILayoutOption[] TradeChoicesWidth = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly GUIContent TradePreviewOverflow = new GUIContent("…");
        private static readonly GUILayoutOption[] TradeFilterSize = { GUILayout.Height(30), GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] TradeSearchSize = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly SodRpg.Core.Game.Rarity[] TradeRarities =
        {
            SodRpg.Core.Game.Rarity.Common, SodRpg.Core.Game.Rarity.Uncommon, SodRpg.Core.Game.Rarity.Rare,
            SodRpg.Core.Game.Rarity.Epic, SodRpg.Core.Game.Rarity.Legendary,
        };
        private static readonly Slot[] TradeSlots = { Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm };

        // Display-only filters for the own-offer choices; the offer itself is never filtered.
        // -1 means "all". Button labels are rebuilt only when a filter or the language changes.
        private string _tradeSearch = "";
        private int _tradeRarityFilter = -1, _tradeSlotFilter = -1;
        private string _tradeRarityLabel, _tradeSlotLabel;
        private LocLanguage _tradeFilterLabelsJapanese;

        // Cached only while this tab is drawn. Stat/power entries are immutable, so reference
        // comparisons also detect rerolls without formatting descriptions on every repaint.
        private sealed class TradeRelicText
        {
            public string Uid, BaseId, UniqueId, NamedId, Name, Title, SelectedTitle, IdleTitle, Details;
            public GUIContent SelectedContent, IdleContent, DetailContent;
            public int Enhance, Level, Awakening, LimitBreaks, FusionLevel;
            public SodRpg.Core.Game.Rarity Rarity;
            public Slot Slot;
            public LocLanguage Japanese;
            public StatLine[] Affixes;
            public PowerLine[] Powers;
            // Snapshot the mutable mark so in-place edits also invalidate the displayed effects.
            public ForbiddenMark Forbidden;

            public bool Matches(Relic r)
            {
                if (BaseId != r.BaseId || UniqueId != r.UniqueId || NamedId != r.NamedId
                    || Enhance != r.Enhance || Level != r.ItemLevel || Awakening != r.AwakenLevel
                    || LimitBreaks != r.LimitBreaks || FusionLevel != r.FusionLevel || Rarity != r.Rarity || Japanese != Loc.Language
                    || Affixes.Length != r.Affixes.Count || Powers.Length != r.Powers.Count) return false;
                var mark = r.Forbidden;
                if ((Forbidden == null) != (mark == null)) return false;
                if (mark != null && !Forbidden.SameAs(mark)) return false;
                for (int i = 0; i < Affixes.Length; i++) if (Affixes[i] != r.Affixes[i]) return false;
                for (int i = 0; i < Powers.Length; i++) if (Powers[i] != r.Powers[i]) return false;
                return true;
            }
        }

        private TradeRelicText CoopTradeRelicText(Relic r)
        {
            if (_tradeRelicTexts.TryGetValue(r, out var text) && text.Matches(r)) return text;
            text = new TradeRelicText
            {
                Uid = r.Uid, BaseId = r.BaseId, UniqueId = r.UniqueId, NamedId = r.NamedId,
                Enhance = r.Enhance, Level = r.ItemLevel, Awakening = r.AwakenLevel,
                LimitBreaks = r.LimitBreaks, FusionLevel = r.FusionLevel, Rarity = r.Rarity, Slot = r.Slot, Japanese = Loc.Language,
                Name = r.DisplayName ?? "",
                Affixes = r.Affixes.ToArray(), Powers = r.Powers.ToArray(), Forbidden = r.Forbidden?.Clone(),
                Title = UiStyles.RelicTitle(r) + "  <color=#bcbcd2>" + Content.RarityName(r.Rarity)
                    + " · " + Content.SlotName(r.Slot) + " · Lv" + r.ItemLevel.ToString(CultureInfo.InvariantCulture) + "</color>"
            };
            text.SelectedTitle = "● " + text.Title;
            text.IdleTitle = "○ " + text.Title;
            var details = new StringBuilder();
            foreach (var stat in r.EffectiveStats())
            {
                if (details.Length > 0) details.Append('\n');
                details.Append(Content.FormatStat(stat.Stat, stat.Value));
            }
            foreach (var power in r.EffectivePowers())
                details.Append('\n').Append(UiStyles.Colored(Content.FormatPowerBullets(power.Power, power.Value), "#e0b0ff"));
            if (r.BossMove != null) details.Append('\n').Append(UiStyles.Colored(r.DescribeBossMove(), "#e0b0ff"));
            if (r.Link != null) details.Append('\n').Append(UiStyles.Colored(Links.Describe(r.Link), "#7fd8ff"));
            text.Details = details.ToString();
            string tooltip = text.Title + "\n" + text.Details;
            text.SelectedContent = new GUIContent(text.SelectedTitle, tooltip);
            text.IdleContent = new GUIContent(text.IdleTitle, tooltip);
            text.DetailContent = new GUIContent(text.Details, tooltip);
            _tradeRelicTexts[r] = text;
            return text;
        }

        private string CoopTradeTabLabel()
        {
            var trade = _s.CoopTrade;
            return trade != null && trade.Incoming && !trade.Accepted
                ? Loc.T("交換 ●", "Trade ●") : Loc.T("交換", "Trade");
        }

        private void DrawCoopTradeTab()
        {
            var trade = _s.CoopTrade;
            if (!string.IsNullOrEmpty(_s.CoopTradeWarning)) GUILayout.Label(_s.CoopTradeWarning, _st.Warn);
            string unavailable = _s.CoopTradeUnavailableReason;
            if (!string.IsNullOrEmpty(unavailable) && unavailable != _s.CoopTradeWarning)
                GUILayout.Label(unavailable, _st.Warn);
            if (trade == null)
            {
                _tradeDraftId = null;
                _tradeDraftProfile = null;
                _tradeRelicTexts.Clear();
                DrawCoopTradePlayers();
                return;
            }

            SyncCoopTradeDraft(trade);
            GUILayout.Label(Loc.T("交換相手：", "Trading with: ") + trade.PartnerName, _st.Header);
            if (!string.IsNullOrEmpty(trade.Status)) GUILayout.Label(trade.Status, _st.Small);
            GUILayout.Label(Loc.T(
                "内容が変わると双方の確認が解除されます。受け取る遺物は保管庫へ。欠片・調律石は所持分（鞄を含む）から渡します。",
                "Any offer change clears both confirmations. Received relics go to the stash; shards and tuning include those in your satchel."), _st.Small);

            bool valid = TryCoopTradeAmounts(out int shards, out int tuning);
            string invalid = valid ? null : Loc.T("素材は0以上の整数で入力してください。", "Enter a nonnegative whole number for each material.");
            if (valid && (shards > CoopTradeRules.Shards(_s.Profile) || tuning > CoopTradeRules.Tuning(_s.Profile)))
                invalid = Loc.T("所持している素材の数を超えています。", "The offer exceeds your owned materials.");
            if (!trade.Preparing && invalid != null) GUILayout.Label(invalid, _st.Warn);
            // 提示した遺物を後から装着・使用すると、ホストが確認時に取引を取り消す。確認の前に止める。
            string staleOffer = null;
            if (trade.Accepted && !trade.Preparing && !trade.OwnConfirmed && trade.OwnOffer != null)
                foreach (string uid in trade.OwnOffer.RelicUids)
                    if (!CoopTradeRelicOfferable(_s.Profile, uid))
                    {
                        staleOffer = Loc.T("提示できない遺物が含まれています。提示から外して続けてください。",
                            "Some relics can no longer be offered. Remove them from your offer to continue.");
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(staleOffer, _st.Warn, TradeChoicesWidth);
                        bool staleEnabled = GUI.enabled;
                        GUI.enabled = staleEnabled && _s.CanCoopTrade && !_s.CoopTradeOfferPending;
                        if (GUILayout.Button(Loc.T("無効な遺物を提示から外す", "Remove invalid relics"), _st.ButtonWrap, TradeActionSize))
                            SetStatus(_s.SetCoopTradeOffer(CopyCoopTradeOffer(trade.OwnOffer, trade.OwnOffer.Shards, trade.OwnOffer.Tuning)));
                        GUI.enabled = staleEnabled;
                        GUILayout.EndHorizontal();
                        break;
                    }

            // Actions precede the expanding scroll panes, so no long inventory can hide them.
            bool enabled = GUI.enabled;
            GUILayout.BeginHorizontal();
            if (trade.Incoming && !trade.Accepted)
            {
                GUI.enabled = enabled && _s.CanCoopTrade && !trade.Preparing;
                if (GUILayout.Button(Loc.T("交換を承諾", "Accept trade"), _st.ButtonWrap, TradeActionSize)) SetStatus(_s.AcceptCoopTrade());
            }
            else if (trade.Accepted)
            {
                GUI.enabled = enabled && _s.CanCoopTrade && !trade.Preparing && !trade.OwnConfirmed && !_s.CoopTradeOfferPending && invalid == null && staleOffer == null;
                if (GUILayout.Button(trade.OwnConfirmed ? Loc.T("確認済み", "Confirmed") : Loc.T("この内容で交換を確認", "Confirm this trade"), _st.ButtonWrap, TradeActionSize))
                    SetStatus(_s.ConfirmCoopTrade());
            }
            GUI.enabled = enabled && !trade.Preparing;
            if (GUILayout.Button(trade.Incoming && !trade.Accepted ? Loc.T("拒否", "Reject") : Loc.T("交換を取消", "Cancel trade"), _st.ButtonWrap, TradeActionSize))
                SetStatus(_s.CancelCoopTrade());
            GUI.enabled = enabled;
            GUILayout.EndHorizontal();
            GUILayout.Label(trade.Preparing
                ? Loc.T("交換を確定中：操作はロックされています。ホストの確定・取消の応答を待ってください。", "Finalizing: controls are locked. Wait for the host's commit or abort decision.")
                : Loc.T("自分：", "You: ") + TradeConfirmation(trade.OwnConfirmed) + "    " + Loc.T("相手：", "Partner: ") + TradeConfirmation(trade.OtherConfirmed), _st.Small);

            float columnWidth = Mathf.Max(150f, (_windowWidth - 60f) / 2f);
            GUILayout.BeginHorizontal(TradePaneSize);
            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label(Loc.T("自分が渡すもの", "Your offer"), _st.Header);
            GUI.enabled = enabled && trade.Accepted && _s.CanCoopTrade && !trade.Preparing;
            DrawCoopTradeMaterialFields(trade);
            GUI.enabled = enabled;
            DrawCoopTradeFilters();
            _tradeOwnScroll.y = Mathf.Min(_tradeOwnScroll.y, Mathf.Max(0f, _tradeOwnContentHeight - _tradeOwnViewportHeight));
            _tradeOwnScroll = GUILayout.BeginScrollView(_tradeOwnScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, TradePaneSize);
            DrawCoopTradeOwnRelics(trade, enabled && trade.Accepted && _s.CanCoopTrade && !trade.Preparing && invalid == null);
            Rect contentEnd = GUILayoutUtility.GetRect(0f, 0f);
            if (Event.current.type == EventType.Repaint) _tradeOwnContentHeight = contentEnd.yMax;
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _tradeOwnViewportHeight = GUILayoutUtility.GetLastRect().height;
            _tradeOwnScroll.y = WheelScroll(_tradeOwnScroll.y, _tradeOwnContentHeight);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(columnWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label(Loc.T("相手が渡すもの", "Partner's offer"), _st.Header);
            DrawCoopTradeMaterialSummary(trade.OtherOffer);
            _tradeOtherScroll = GUILayout.BeginScrollView(_tradeOtherScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, TradePaneSize);
            DrawCoopTradeOfferedRelics(trade.OtherProfile, trade.OtherOffer);
            GUILayout.EndScrollView();
            _tradeOtherScroll.y = WheelScroll(_tradeOtherScroll.y, float.PositiveInfinity);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUI.enabled = enabled;
            DrawCoopTradeRelicTooltip();
        }

        private static string TradeConfirmation(bool confirmed) => confirmed
            ? Loc.T("確認済み", "confirmed") : Loc.T("未確認", "not confirmed");

        private void DrawCoopTradePlayers()
        {
            GUILayout.Label(Loc.T("同じセッションのプレイヤー", "Players in this session"), _st.Header);
            GUILayout.Label(Loc.T(
                "交換はロビーまたは遠征の確保地点で行えます。交換に未対応・応答のない相手とは交換できませんが、他の協力プレイ機能は利用できます。",
                "Trade in the lobby or at a safe expedition point. Unsupported or nonresponding peers cannot trade; other co-op features remain available."), _st.Small);
            var peers = _s.CoopTradePeers;
            _tradePlayersScroll = GUILayout.BeginScrollView(_tradePlayersScroll, TradePaneSize);
            if (peers.Count == 0) GUILayout.Label(Loc.T("交換相手がいません。同じセッションに参加してください。", "No trade partners. Join the same session to trade."), _st.Small);
            bool enabled = GUI.enabled;
            for (int i = 0; i < peers.Count; i++)
            {
                var peer = peers[i];
                GUILayout.BeginHorizontal(_st.Panel);
                GUILayout.Label(peer.Name, _st.Label, GUILayout.ExpandWidth(true), GUILayout.MinWidth(0));
                GUI.enabled = enabled && peer.Available && peer.Ready && _s.CanCoopTrade;
                if (GUILayout.Button(Loc.T("交換を申し込む", "Request trade"), _st.ButtonWrap, GUILayout.Width(170))) SetStatus(_s.RequestCoopTrade(peer.Key));
                GUI.enabled = enabled;
                GUILayout.EndHorizontal();
                if (!peer.Available) GUILayout.Label(Loc.T("交換に未対応、または応答待ちです（交換のみ利用不可）。", "Trade unsupported or awaiting a response (trade only is unavailable)."), _st.Small);
                else if (!peer.Ready) GUILayout.Label(Loc.T("相手は今取引できる状態ではありません（戦闘中・保存中など）",
                    "The other player can't trade right now (in combat, saving, etc.)"), _st.Small);
            }
            GUILayout.EndScrollView();
            _tradePlayersScroll.y = WheelScroll(_tradePlayersScroll.y, float.PositiveInfinity);
        }

        private void SyncCoopTradeDraft(CoopTradeView trade)
        {
            var offer = trade.OwnOffer;
            int shards = offer?.Shards ?? 0, tuning = offer?.Tuning ?? 0;
            bool reset = _tradeDraftId != trade.Id || _tradeDraftProfile != _s.Profile;
            if (reset)
            {
                _tradeDraftId = trade.Id;
                _tradeDraftProfile = _s.Profile;
                _tradeRelicTexts.Clear();
                _tradeOwnScroll = _tradeOtherScroll = Vector2.zero;
                _tradeOwnContentHeight = 0f;
            }
            if (reset || _tradeSeenShards != shards) _tradeShards = shards.ToString(CultureInfo.InvariantCulture);
            if (reset || _tradeSeenTuning != tuning) _tradeTuning = tuning.ToString(CultureInfo.InvariantCulture);
            _tradeSeenShards = shards;
            _tradeSeenTuning = tuning;
        }

        private bool TryCoopTradeAmounts(out int shards, out int tuning)
        {
            bool shardsValid = int.TryParse(_tradeShards, NumberStyles.None, CultureInfo.InvariantCulture, out shards);
            bool tuningValid = int.TryParse(_tradeTuning, NumberStyles.None, CultureInfo.InvariantCulture, out tuning);
            return shardsValid && tuningValid;
        }

        private void DrawCoopTradeMaterialFields(CoopTradeView trade)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("欠片", "Shards"), _st.Small, GUILayout.Width(85));
            string shards = GUILayout.TextField(_tradeShards, 10, TradeFieldSize);
            GUILayout.Label(Loc.T("調律石", "Tuning"), _st.Small, GUILayout.Width(85));
            string tuning = GUILayout.TextField(_tradeTuning, 10, TradeFieldSize);
            GUILayout.EndHorizontal();
            if (shards == _tradeShards && tuning == _tradeTuning) return;
            _tradeShards = shards;
            _tradeTuning = tuning;
            if (!TryCoopTradeAmounts(out int shardCount, out int tuningCount)
                || shardCount > CoopTradeRules.Shards(_s.Profile) || tuningCount > CoopTradeRules.Tuning(_s.Profile))
            {
                // An unfinished/invalid draft is not an offer, but editing it must still
                // revoke approval of the previous offer before the partner can commit.
                if (trade.OwnConfirmed || trade.OtherConfirmed)
                    SetStatus(_s.SetCoopTradeOffer(CopyCoopTradeOffer(trade.OwnOffer, trade.OwnOffer?.Shards ?? 0, trade.OwnOffer?.Tuning ?? 0)));
                return;
            }
            var offer = CopyCoopTradeOffer(trade.OwnOffer, shardCount, tuningCount);
            SetStatus(_s.SetCoopTradeOffer(offer));
        }

        private CoopTradeOffer CopyCoopTradeOffer(CoopTradeOffer original, int shards, int tuning)
        {
            var offer = new CoopTradeOffer { Shards = shards, Tuning = tuning };
            if (original != null)
                foreach (string uid in original.RelicUids)
                    if (CoopTradeRelicOfferable(_s.Profile, uid)) offer.RelicUids.Add(uid);
            return offer;
        }

        // 提示後に装着・合成・再調律などで使えなくなった遺物は、次の提示更新で外す（ホストの検証と同じ条件）。
        private static bool CoopTradeRelicOfferable(Profile profile, string uid)
            => CoopTradeRules.RelicOfferable(profile, uid);

        private void DrawCoopTradeOwnRelics(CoopTradeView trade, bool editable)
        {
            var p = _s.Profile;
            GUILayout.Label(Loc.T("選択済みの遺物", "Selected relics"), _st.Small);
            DrawCoopTradeOfferedRelics(p, trade.OwnOffer);
            GUILayout.Label(Loc.T("保管庫（未装着のみ）", "Stash (unequipped only)"), _st.Header);
            DrawCoopTradeRelicChoices(p, p.Stash, trade, editable);
            if (p.Run != null)
            {
                GUILayout.Label(Loc.T("鞄（未装着のみ）", "Satchel (unequipped only)"), _st.Header);
                DrawCoopTradeRelicChoices(p, p.Run.Satchel, trade, editable);
            }
        }

        private void DrawCoopTradeFilters()
        {
            if (_tradeRarityLabel == null || _tradeFilterLabelsJapanese != Loc.Language) RefreshCoopTradeFilterLabels();
            string previousSearch = _tradeSearch;
            int previousRarity = _tradeRarityFilter, previousSlot = _tradeSlotFilter;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("名前で検索", "Search name"), _st.Small, GUILayout.Width(110));
            _ime.BeginField("DreamforgeTradeSearch");
            _tradeSearch = GUILayout.TextField(_tradeSearch ?? "", 40, TradeSearchSize);
            _ime.Track("DreamforgeTradeSearch");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            // Left click advances, right click goes back; "all" sits between the ends of each cycle.
            bool back = Event.current.button == 1;
            if (GUILayout.Button(_tradeRarityLabel, _st.ButtonWrap, TradeFilterSize))
            {
                _tradeRarityFilter = CycleCoopTradeFilter(_tradeRarityFilter, TradeRarities.Length, back);
                RefreshCoopTradeFilterLabels();
            }
            if (GUILayout.Button(_tradeSlotLabel, _st.ButtonWrap, TradeFilterSize))
            {
                _tradeSlotFilter = CycleCoopTradeFilter(_tradeSlotFilter, TradeSlots.Length, back);
                RefreshCoopTradeFilterLabels();
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && CoopTradeFiltering;
            if (GUILayout.Button(Loc.T("解除", "Clear"), _st.ButtonWrap, GUILayout.Height(30), GUILayout.Width(70)))
            {
                _tradeSearch = "";
                _tradeRarityFilter = _tradeSlotFilter = -1;
                RefreshCoopTradeFilterLabels();
            }
            GUI.enabled = enabled;
            GUILayout.EndHorizontal();
            if (previousSearch != _tradeSearch || previousRarity != _tradeRarityFilter || previousSlot != _tradeSlotFilter)
                _tradeOwnScroll.y = 0f;
        }

        private bool CoopTradeFiltering => _tradeRarityFilter >= 0 || _tradeSlotFilter >= 0 || !string.IsNullOrEmpty(_tradeSearch);

        private static int CycleCoopTradeFilter(int current, int count, bool back)
        {
            int next = current + (back ? -1 : 1);
            if (next >= count) return -1;
            if (next < -1) return count - 1;
            return next;
        }

        private void RefreshCoopTradeFilterLabels()
        {
            _tradeFilterLabelsJapanese = Loc.Language;
            _tradeRarityLabel = Loc.T("レア度：", "Rarity: ")
                + (_tradeRarityFilter < 0 ? Loc.T("すべて", "All") : Content.RarityName(TradeRarities[_tradeRarityFilter]));
            _tradeSlotLabel = Loc.T("部位：", "Slot: ")
                + (_tradeSlotFilter < 0 ? Loc.T("すべて", "All") : Content.SlotName(TradeSlots[_tradeSlotFilter]));
        }

        private bool MatchesCoopTradeFilters(TradeRelicText text)
        {
            if (_tradeRarityFilter >= 0 && text.Rarity != TradeRarities[_tradeRarityFilter]) return false;
            if (_tradeSlotFilter >= 0 && text.Slot != TradeSlots[_tradeSlotFilter]) return false;
            string query = _tradeSearch?.Trim();
            return string.IsNullOrEmpty(query) || text.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawCoopTradeRelicChoices(Profile p, List<Relic> relics, CoopTradeView trade, bool editable)
        {
            var list = relics == p.Stash ? _tradeStashChoices : _tradeSatchelChoices;
            list.Clear();
            bool hidden = false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (p.IsEquippedAnywhere(relic.Uid)) continue;
                if (CoopTradeFiltering && !MatchesCoopTradeFilters(CoopTradeRelicText(relic))) { hidden = true; continue; }
                list.Add(relic);
            }
            if (list.Count == 0)
            {
                GUILayout.Label(hidden
                    ? Loc.T("条件に合う遺物がありません。", "No relics match the filters.")
                    : Loc.T("交換できる未装着の遺物がありません。", "No unequipped relics to offer."), _st.Small);
                return;
            }

            // One layout entry reserves both blank regions and all rows. Its content-space y
            // includes the selected-offer summary and (for the satchel) the stash above it.
            float contentHeight = list.Count * TradeRelicRowStep;
            Rect area = GUILayoutUtility.GetRect(0f, float.MaxValue, contentHeight, contentHeight, TradeChoicesWidth);
            if (Event.current.type == EventType.Layout) return;
            float visibleHeight = _tradeOwnViewportHeight > 0f ? _tradeOwnViewportHeight : Mathf.Max(300f, _windowHeight);
            if (_tradeOwnContentHeight > 0f
                && (area.yMin > _tradeOwnScroll.y + visibleHeight || area.yMax < _tradeOwnScroll.y)) return;
            float localY = Mathf.Min(Mathf.Max(0f, _tradeOwnScroll.y - area.y), Mathf.Max(0f, contentHeight - visibleHeight));
            int first = Math.Max(0, (int)(localY / TradeRelicRowStep) - 2);
            int last = Math.Min(list.Count, first + (int)(visibleHeight / TradeRelicRowStep) + 5);
            EnsureCoopTradeRelicStyles();
            bool enabled = GUI.enabled;
            for (int i = first; i < last; i++)
            {
                var relic = list[i];
                var text = CoopTradeRelicText(relic);
                bool selected = trade.OwnOffer != null && trade.OwnOffer.RelicUids.Contains(relic.Uid);
                Rect card = new Rect(area.x, area.y + i * TradeRelicRowStep, area.width, TradeRelicRowStep - 4f);
                GUI.Box(card, GUIContent.none, _st.Panel);
                Rect title = new Rect(card.x + 10f, card.y + 6f, Mathf.Max(0f, card.width - 20f), 36f);
                GUI.enabled = enabled && editable;
                if (GUI.Button(title, selected ? text.SelectedContent : text.IdleContent, selected ? _tradeRelicSelectedStyle : _tradeRelicTitleStyle)
                    && TryCoopTradeAmounts(out int shards, out int tuning))
                {
                    var offer = CopyCoopTradeOffer(trade.OwnOffer, shards, tuning);
                    if (selected) offer.RelicUids.Remove(relic.Uid); else offer.RelicUids.Add(relic.Uid);
                    SetStatus(_s.SetCoopTradeOffer(offer));
                }
                GUI.enabled = enabled;
                Rect preview = new Rect(title.x, card.y + 44f, title.width, 46f);
                bool clipped = _tradeRelicDetailsStyle.CalcHeight(text.DetailContent, preview.width) > preview.height;
                if (clipped) preview.width = Mathf.Max(0f, preview.width - 24f);
                GUI.Label(preview, text.DetailContent, _tradeRelicDetailsStyle);
                if (clipped)
                {
                    TradePreviewOverflow.tooltip = text.DetailContent.tooltip;
                    GUI.Label(new Rect(preview.x + preview.width, preview.y + preview.height - 20f, 24f, 20f),
                        TradePreviewOverflow, _tradeRelicDetailsStyle);
                }
            }
        }

        private void EnsureCoopTradeRelicStyles()
        {
            if (_tradeRelicStylesFor == _st) return;
            _tradeRelicStylesFor = _st;
            // Match RelicList's single-line title. Clip the preview inside the card;
            // the full rich-text description remains available on hover and in the offer summary.
            _tradeRelicTitleStyle = new GUIStyle(_st.Row) { wordWrap = false, clipping = TextClipping.Clip };
            _tradeRelicSelectedStyle = new GUIStyle(_st.RowSel) { wordWrap = false, clipping = TextClipping.Clip };
            _tradeRelicDetailsStyle = new GUIStyle(_st.Small) { wordWrap = true, clipping = TextClipping.Clip };
            _tradeTooltipStyle = new GUIStyle(_st.Small) { clipping = TextClipping.Clip };
            _tradeTooltipStyle.normal.background = _st.Panel.normal.background;
            _tradeTooltipStyle.padding = _st.Panel.padding;
        }

        private void DrawCoopTradeRelicTooltip()
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(GUI.tooltip)) return;
            EnsureCoopTradeRelicStyles();
            _tradeTooltipContent.text = GUI.tooltip;
            float width = Mathf.Min(520f, _windowWidth - 24f);
            float height = Mathf.Min(_windowHeight - 24f, _tradeTooltipStyle.CalcHeight(_tradeTooltipContent, width));
            Vector2 mouse = Event.current.mousePosition;
            float x = Mathf.Max(12f, Mathf.Min(mouse.x + 12f, _windowWidth - width - 12f));
            float y = Mathf.Max(12f, Mathf.Min(mouse.y + 20f, _windowHeight - height - 12f));
            GUI.Box(new Rect(x, y, width, height), _tradeTooltipContent, _tradeTooltipStyle);
        }

        private void DrawCoopTradeMaterialSummary(CoopTradeOffer offer)
        {
            GUILayout.Label(Loc.T($"欠片 {offer?.Shards ?? 0}　調律石 {offer?.Tuning ?? 0}",
                $"Shards {offer?.Shards ?? 0}   Tuning {offer?.Tuning ?? 0}"), _st.Small);
        }

        private void DrawCoopTradeOfferedRelics(Profile profile, CoopTradeOffer offer)
        {
            if (offer == null || offer.RelicUids.Count == 0)
            {
                GUILayout.Label(Loc.T("遺物は選択されていません。", "No relics selected."), _st.Small);
                return;
            }
            for (int i = 0; i < offer.RelicUids.Count; i++)
            {
                var relic = FindCoopTradeRelic(profile, offer.RelicUids[i]);
                TradeRelicText text = relic != null ? CoopTradeRelicText(relic) : null;
                if (text == null && _s.CoopTrade != null && _s.CoopTrade.Preparing)
                    foreach (var cached in _tradeRelicTexts.Values)
                        if (cached.Uid == offer.RelicUids[i] && cached.Japanese == Loc.Language) { text = cached; break; }
                if (text == null)
                {
                    GUILayout.Label(_s.CoopTrade != null && _s.CoopTrade.Preparing
                        ? Loc.T("遺物は交換のために確保されています。", "Relic reserved for this trade.")
                        : Loc.T("遺物の情報を受信待ちです。", "Waiting for relic details."), _st.Small);
                    continue;
                }
                GUILayout.Label(text.Title, _st.Label);
                GUILayout.Label(text.Details, _st.Small);
            }
        }

        private static Relic FindCoopTradeRelic(Profile profile, string uid)
        {
            if (profile == null) return null;
            var relic = profile.FindStash(uid);
            if (relic != null) return relic;
            if (profile.Run != null)
                for (int i = 0; i < profile.Run.Satchel.Count; i++)
                    if (profile.Run.Satchel[i].Uid == uid) return profile.Run.Satchel[i];
            return null;
        }
    }
}
