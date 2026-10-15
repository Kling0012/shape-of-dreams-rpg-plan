using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private Relic _fusionTarget;
        private Profile _fusionProfile;
        private string _fusionMaterial;
        private bool _fusionOpen, _confirmFusion, _fusionCacheValid;
        private int _fusionStamp;
        private Vector2 _fusionScroll;
        private List<Relic> _fusionParts = new List<Relic>();
        private readonly List<string> _fusionRows = new List<string>();
        private readonly List<string> _fusionSelectedRows = new List<string>();
        private string _fusionHeader, _fusionCost, _fusionPreview, _fusionAction, _fusionConfirmAction, _fusionDetail;
        private bool _fusionAfford, _fusionSafe;
        private Func<GameEvent> _fusionApply;
        private Relic _fusionDetailRelic;
        private int _fusionDetailLevel;
        private bool _fusionDetailJapanese;
        private string _fusionStatsText;
        private static readonly GUILayoutOption[] FusionListSize = { GUILayout.Height(132), GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] FusionRowSize = { GUILayout.MinHeight(30), GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };

        private void ResetFusionConfirmation()
        {
            _confirmFusion = false;
        }

        private void SyncFusion(Relic target)
        {
            var p = _s.Profile;
            if (_fusionProfile != p || _fusionTarget != target)
            {
                _fusionProfile = p;
                _fusionTarget = target;
                _fusionMaterial = null;
                _fusionScroll = Vector2.zero;
                _fusionCacheValid = false;
            }
            int stamp;
            unchecked
            {
                stamp = target.FusionLevel + (Loc.Japanese ? 1024 : 0);
                stamp = stamp * 31 + p.Material(Materials.Shard);
                stamp = stamp * 31 + p.Material(Materials.Tuning);
                stamp = stamp * 31 + (_s.CanEditLoadout ? 1 : 0);
                stamp = stamp * 31 + (_s.Trades.IsReserved(target.Uid) ? 1 : 0);
                stamp = stamp * 31 + (p.RetuneOffer?.Uid == target.Uid ? 1 : 0);
                stamp = stamp * 31 + (_fusionMaterial?.GetHashCode() ?? 0);
                foreach (var relic in p.Stash)
                {
                    stamp = stamp * 31 + relic.GetHashCode();
                    stamp = stamp * 31 + relic.Enhance;
                    stamp = stamp * 31 + relic.ItemLevel;
                    stamp = stamp * 31 + relic.AwakenLevel;
                    stamp = stamp * 31 + relic.FusionLevel;
                    stamp = stamp * 31 + (int)relic.Rarity;
                    stamp = stamp * 31 + (relic.BaseId?.GetHashCode() ?? 0);
                    stamp = stamp * 31 + (relic.UniqueId?.GetHashCode() ?? 0);
                    stamp = stamp * 31 + (relic.NamedId?.GetHashCode() ?? 0);
                    stamp = stamp * 31 + (relic.Powers.Count > 0 ? (int)relic.Powers[0].Power : 0);
                    stamp = stamp * 31 + (relic.Locked ? 1 : 0);
                    stamp = stamp * 31 + (p.IsEquippedAnywhere(relic.Uid) ? 1 : 0);
                    stamp = stamp * 31 + (_s.Trades.IsReserved(relic.Uid) ? 1 : 0);
                    stamp = stamp * 31 + (p.RetuneOffer?.Uid == relic.Uid ? 1 : 0);
                }
            }
            if (_fusionCacheValid && stamp == _fusionStamp) return;
            _fusionStamp = stamp;
            _fusionCacheValid = true;
            _confirmFusion = false;
            _fusionParts = Rules.FusionCandidates(p, target, _s.Trades);
            _fusionRows.Clear();
            _fusionSelectedRows.Clear();
            Relic material = null;
            foreach (var relic in _fusionParts)
            {
                string title = UiStyles.RelicTitle(relic) + "  Lv" + relic.ItemLevel;
                _fusionRows.Add("○ " + title);
                _fusionSelectedRows.Add("● " + title);
                if (relic.Uid == _fusionMaterial) material = relic;
            }
            if (material == null) _fusionMaterial = null;
            int level = target.FusionLevel, next = Math.Min(Fusion.MaxLevel, level + 1);
            int shards = Fusion.ShardCost(target), tuning = Fusion.TuningCost(target);
            _fusionHeader = Loc.T($"融合 {level}/{Fusion.MaxLevel}（能力値 +{Fusion.BonusPct(level)}%）", $"Fusion {level}/{Fusion.MaxLevel} (stats +{Fusion.BonusPct(level)}%)");
            if (level >= Fusion.MaxLevel) _fusionHeader += Loc.T("（上限）", " (maximum)");
            _fusionPreview = Loc.T($"融合 {level} → {next}：能力値 +{Fusion.BonusPct(level)}% → +{Fusion.BonusPct(next)}%（固有効果は変わりません）",
                $"Fusion {level} → {next}: stats +{Fusion.BonusPct(level)}% → +{Fusion.BonusPct(next)}% (powers unchanged)");
            _fusionCost = Loc.T($"費用：欠片{shards}" + (tuning > 0 ? $"・調律石{tuning}" : "") + $"（所持：欠片{p.Material(Materials.Shard)}・調律石{p.Material(Materials.Tuning)}）",
                $"Cost: {shards} shards" + (tuning > 0 ? $", {tuning} tuning" : "") + $" (owned: {p.Material(Materials.Shard)} shards, {p.Material(Materials.Tuning)} tuning)");
            _fusionAfford = p.Material(Materials.Shard) >= shards && p.Material(Materials.Tuning) >= tuning;
            _fusionSafe = _s.CanEditLoadout && !_s.Trades.IsReserved(target.Uid) && p.RetuneOffer?.Uid != target.Uid;
            _fusionAction = !_s.CanEditLoadout ? Loc.T("融合は確保地点か遠征の外で使えます", "Fusion requires a secure point or being outside an expedition")
                : !_fusionSafe ? Loc.T("目標は取引中・再調律中です", "Target is trading or retuning")
                : !_fusionAfford ? Loc.T("融合の素材費用が足りません", "Not enough materials for fusion")
                : material == null ? Loc.T("材料を選んでください", "Pick a material")
                : Loc.T($"融合する（段階{next}・能力値+{Fusion.BonusPct(next)}%）", $"Fuse (level {next}, stats +{Fusion.BonusPct(next)}%)");
            _fusionDetail = material == null ? "" : Loc.T($"材料「{material.DisplayName}」を消費します。材料の融合段階・強化値は引き継がず、目標の強化値は変わりません。",
                $"Consumes \"{material.DisplayName}\". Its fusion and enhancement levels are lost; the target's enhancement stays unchanged.");
            _fusionConfirmAction = material == null ? _fusionAction : Loc.T($"<color=#ff8080>「{material.DisplayName}」を消費して融合します。もう一度押すと確定</color>",
                $"<color=#ff8080>Consume \"{material.DisplayName}\" for fusion? Press again to confirm</color>");
        }

        private GameEvent ApplyFusion()
            => Rules.Fuse(_s.Profile, _fusionTarget.Uid, _fusionMaterial, _s.Trades, !_s.CanEditLoadout);

        private string FusionStatsText(Relic relic)
        {
            if (_fusionDetailRelic != relic || _fusionDetailLevel != relic.FusionLevel || _fusionDetailJapanese != Loc.Japanese)
            {
                _fusionDetailRelic = relic;
                _fusionDetailLevel = relic.FusionLevel;
                _fusionDetailJapanese = Loc.Japanese;
                _fusionStatsText = UiStyles.Colored(Loc.T($"融合 {relic.FusionLevel}/{Fusion.MaxLevel}：能力値 +{Fusion.BonusPct(relic.FusionLevel)}%（固有効果を除く）",
                    $"Fusion {relic.FusionLevel}/{Fusion.MaxLevel}: stats +{Fusion.BonusPct(relic.FusionLevel)}% (excluding powers)"), "#7fd8ff");
            }
            return _fusionStatsText;
        }

        private void DrawFusion(Relic target)
        {
            try { SyncFusion(target); }
            catch (Exception ex)
            {
                _fusionCacheValid = false;
                GUILayout.Label(Loc.T("融合のみ利用できません：", "Only Fusion is unavailable: ") + ex.Message, _st.Warn);
                return;
            }
            if (target.FusionLevel >= Fusion.MaxLevel)
            {
                GUILayout.Label(_fusionHeader, _st.Small);
                return;
            }
            if (GUILayout.Button(_fusionHeader, _fusionOpen ? _st.ButtonWrapSel : _st.ButtonWrap, ShrinkMin30))
            {
                _fusionOpen = !_fusionOpen;
                _confirmFusion = false;
            }
            if (!_fusionOpen) return;
            GUILayout.Label(_fusionPreview, _st.Small);
            GUILayout.Label(_fusionCost, _st.Small);
            GUILayout.Label(Loc.T("同じ名前・同じレア度の材料を1つ選びます（鍵なし・未装着・取引中でも再調律中でもない物）。",
                "Pick one material with the same name and rarity (unlocked, unequipped, unreserved, not retuning)."), _st.Small);
            if (_fusionParts.Count == 0)
            {
                GUILayout.Label(Loc.T("使える材料がありません。", "No usable material."), _st.Warn);
                return;
            }
            _fusionScroll = GUILayout.BeginScrollView(_fusionScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, FusionListSize);
            for (int i = 0; i < _fusionParts.Count; i++)
            {
                bool selected = _fusionMaterial == _fusionParts[i].Uid;
                if (GUILayout.Button(selected ? _fusionSelectedRows[i] : _fusionRows[i], selected ? _st.ButtonWrapSel : _st.RowWrap, FusionRowSize))
                {
                    _fusionMaterial = _fusionParts[i].Uid;
                    _fusionCacheValid = false;
                    _confirmFusion = false;
                }
            }
            GUILayout.EndScrollView();
            _fusionScroll.y = WheelScroll(_fusionScroll.y, float.PositiveInfinity);
            if (_fusionMaterial != null) GUILayout.Label(_fusionDetail, _st.Small);
            GUI.enabled = _fusionCacheValid && _fusionMaterial != null && _fusionAfford && _fusionSafe;
            if (GUILayout.Button(_confirmFusion ? _fusionConfirmAction : _fusionAction, _st.ButtonWrap, ShrinkMin30))
            {
                if (!_confirmFusion) _confirmFusion = true;
                else
                {
                    _confirmFusion = false;
                    if (_fusionApply == null) _fusionApply = ApplyFusion;
                    try { Act(_fusionApply, true); }
                    catch (Exception ex) { SetStatus(Loc.T("融合のみ停止しました：", "Only Fusion stopped: ") + ex.Message); }
                    _fusionCacheValid = false;
                    _fusionMaterial = null;
                }
            }
            GUI.enabled = true;
        }
    }
}
