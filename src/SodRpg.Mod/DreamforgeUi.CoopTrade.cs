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
        private static readonly GUILayoutOption[] TradeRowSize = { GUILayout.MinHeight(58), GUILayout.MinWidth(0) };

        // Cached only while this tab is drawn. Stat/power entries are immutable, so reference
        // comparisons also detect rerolls without formatting descriptions on every repaint.
        private sealed class TradeRelicText
        {
            public string Uid, BaseId, UniqueId, NamedId, Title, SelectedTitle, IdleTitle, Details;
            public int Enhance, Level, Awakening, LimitBreaks;
            public SodRpg.Core.Game.Rarity Rarity;
            public bool Japanese;
            public StatLine[] Affixes;
            public PowerLine[] Powers;

            public bool Matches(Relic r)
            {
                if (BaseId != r.BaseId || UniqueId != r.UniqueId || NamedId != r.NamedId
                    || Enhance != r.Enhance || Level != r.ItemLevel || Awakening != r.AwakenLevel
                    || LimitBreaks != r.LimitBreaks || Rarity != r.Rarity || Japanese != Loc.Japanese
                    || Affixes.Length != r.Affixes.Count || Powers.Length != r.Powers.Count) return false;
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
                LimitBreaks = r.LimitBreaks, Rarity = r.Rarity, Japanese = Loc.Japanese,
                Affixes = r.Affixes.ToArray(), Powers = r.Powers.ToArray(),
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
                GUI.enabled = enabled && _s.CanCoopTrade && !trade.Preparing && !trade.OwnConfirmed && !_s.CoopTradeOfferPending && invalid == null;
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
            _tradeOwnScroll = GUILayout.BeginScrollView(_tradeOwnScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, TradePaneSize);
            DrawCoopTradeOwnRelics(trade, enabled && trade.Accepted && _s.CanCoopTrade && !trade.Preparing && invalid == null);
            GUILayout.EndScrollView();
            _tradeOwnScroll.y = WheelScroll(_tradeOwnScroll.y, float.PositiveInfinity);
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
                GUI.enabled = enabled && peer.Available && _s.CanCoopTrade;
                if (GUILayout.Button(Loc.T("交換を申し込む", "Request trade"), _st.ButtonWrap, GUILayout.Width(170))) SetStatus(_s.RequestCoopTrade(peer.Key));
                GUI.enabled = enabled;
                GUILayout.EndHorizontal();
                if (!peer.Available) GUILayout.Label(Loc.T("交換に未対応、または応答待ちです（交換のみ利用不可）。", "Trade unsupported or awaiting a response (trade only is unavailable)."), _st.Small);
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

        private static CoopTradeOffer CopyCoopTradeOffer(CoopTradeOffer original, int shards, int tuning)
        {
            var offer = new CoopTradeOffer { Shards = shards, Tuning = tuning };
            if (original != null) offer.RelicUids.AddRange(original.RelicUids);
            return offer;
        }

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

        private void DrawCoopTradeRelicChoices(Profile p, List<Relic> relics, CoopTradeView trade, bool editable)
        {
            bool enabled = GUI.enabled;
            bool found = false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (p.IsEquippedAnywhere(relic.Uid)) continue;
                found = true;
                var text = CoopTradeRelicText(relic);
                bool selected = trade.OwnOffer != null && trade.OwnOffer.RelicUids.Contains(relic.Uid);
                GUILayout.BeginVertical(_st.Panel);
                GUI.enabled = enabled && editable;
                if (GUILayout.Button(selected ? text.SelectedTitle : text.IdleTitle, selected ? _st.ButtonWrapSel : _st.RowWrap, TradeRowSize)
                    && TryCoopTradeAmounts(out int shards, out int tuning))
                {
                    var offer = CopyCoopTradeOffer(trade.OwnOffer, shards, tuning);
                    if (selected) offer.RelicUids.Remove(relic.Uid); else offer.RelicUids.Add(relic.Uid);
                    SetStatus(_s.SetCoopTradeOffer(offer));
                }
                GUI.enabled = enabled;
                GUILayout.Label(text.Details, _st.Small);
                GUILayout.EndVertical();
            }
            if (!found) GUILayout.Label(Loc.T("交換できる未装着の遺物がありません。", "No unequipped relics to offer."), _st.Small);
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
                        if (cached.Uid == offer.RelicUids[i] && cached.Japanese == Loc.Japanese) { text = cached; break; }
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
