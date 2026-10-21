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
        private int _fusionLevel, _fusionShards, _fusionTuning;
        private int _fusionJapanese;
        private bool _fusionCanEdit;
        private float _fusionCheckAt;
        private string _fusionCachedMaterial;
        private readonly List<FusionRelicState> _fusionStates = new List<FusionRelicState>();
        private readonly HashSet<string> _fusionEquipped = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _fusionReserved = new HashSet<string>(StringComparer.Ordinal);

        private struct FusionRelicState
        {
            public Relic Relic;
            public string Uid, BaseId, UniqueId, NamedId;
            public int Enhance, Level, Awakening, FusionLevel;
            public SodRpg.Core.Game.Rarity Rarity;
            public Power FirstPower;
            public bool Locked, Equipped, Reserved, Retuning;

            public bool Matches(FusionRelicState other) => ReferenceEquals(Relic, other.Relic)
                && Uid == other.Uid && BaseId == other.BaseId && UniqueId == other.UniqueId && NamedId == other.NamedId
                && Enhance == other.Enhance && Level == other.Level && Awakening == other.Awakening
                && FusionLevel == other.FusionLevel && Rarity == other.Rarity && FirstPower == other.FirstPower
                && Locked == other.Locked && Equipped == other.Equipped && Reserved == other.Reserved && Retuning == other.Retuning;
        }

        private FusionRelicState FusionState(Profile p, Relic relic) => new FusionRelicState
        {
            Relic = relic, Uid = relic.Uid, BaseId = relic.BaseId, UniqueId = relic.UniqueId, NamedId = relic.NamedId,
            Enhance = relic.Enhance, Level = relic.ItemLevel, Awakening = relic.AwakenLevel, FusionLevel = relic.FusionLevel,
            Rarity = relic.Rarity, FirstPower = relic.Powers.Count > 0 ? relic.Powers[0].Power : Power.None,
            Locked = relic.Locked, Equipped = _fusionEquipped.Contains(relic.Uid), Reserved = _fusionReserved.Contains(relic.Uid),
            Retuning = p.RetuneOffer?.Uid == relic.Uid,
        };
        private Vector2 _fusionScroll;
        private List<Relic> _fusionParts = new List<Relic>();
        private readonly List<string> _fusionRows = new List<string>();
        private readonly List<string> _fusionSelectedRows = new List<string>();
        private string _fusionHeader, _fusionCost, _fusionPreview, _fusionAction, _fusionConfirmAction, _fusionDetail;
        private bool _fusionAfford, _fusionSafe;
        private Func<GameEvent> _fusionApply;
        private Relic _fusionDetailRelic;
        private int _fusionDetailLevel;
        private int _fusionDetailJapanese;
        private string _fusionStatsText;
        private static readonly GUILayoutOption[] FusionListSize = { GUILayout.Height(132), GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] FusionRowSize = { GUILayout.MinHeight(30), GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };

        private void ResetFusionConfirmation()
        {
            _confirmFusion = false;
            _fusionCacheValid = false;
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
            bool safe = _s.CanEditLoadout && !_s.Trades.IsReserved(target.Uid) && p.RetuneOffer?.Uid != target.Uid;
            bool unchanged = _fusionCacheValid && _fusionLevel == target.FusionLevel && _fusionJapanese == Loc.CacheKey
                && _fusionShards == p.Material(Materials.Shard) && _fusionTuning == p.Material(Materials.Tuning)
                && _fusionSafe == safe && _fusionCanEdit == _s.CanEditLoadout
                && _fusionCachedMaterial == _fusionMaterial && _fusionStates.Count == p.Stash.Count;
            // OnGUI は同じフレームにも複数回来る。全保管庫の比較は最大0.3秒に1回に抑える。
            // 対象・選択・費用・操作可否の変化と明示的な無効化は待たずに反映する。
            float now = Time.unscaledTime;
            if (unchanged && now < _fusionCheckAt) return;
            _fusionCheckAt = now + 0.3f;
            _fusionEquipped.Clear();
            foreach (var hero in p.Heroes.Values)
                foreach (var uid in hero.Equipped)
                    if (uid != null) _fusionEquipped.Add(uid);
            _s.Trades.ReservedSalvageUids(_fusionReserved);
            if (unchanged)
                for (int i = 0; i < p.Stash.Count; i++)
                    if (!_fusionStates[i].Matches(FusionState(p, p.Stash[i]))) { unchanged = false; break; }
            if (unchanged) return;
            _fusionLevel = target.FusionLevel;
            _fusionJapanese = Loc.CacheKey;
            _fusionCanEdit = _s.CanEditLoadout;
            _fusionShards = p.Material(Materials.Shard);
            _fusionTuning = p.Material(Materials.Tuning);
            _fusionCachedMaterial = _fusionMaterial;
            _fusionStates.Clear();
            foreach (var relic in p.Stash) _fusionStates.Add(FusionState(p, relic));
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
            _fusionHeader = Loc.T($"融合 {level}/{Fusion.MaxLevel}（能力値 +{Fusion.BonusPct(level)}%）", $"Fusion {level}/{Fusion.MaxLevel} (stats +{Fusion.BonusPct(level)}%)", $"融合 {level}/{Fusion.MaxLevel}（能力值 +{Fusion.BonusPct(level)}%）");
            if (level >= Fusion.MaxLevel) _fusionHeader += Loc.T("（上限）", " (maximum)", "（上限）");
            _fusionPreview = Loc.T($"融合 {level} → {next}：能力値 +{Fusion.BonusPct(level)}% → +{Fusion.BonusPct(next)}%（固有効果は変わりません）",
                $"Fusion {level} → {next}: stats +{Fusion.BonusPct(level)}% → +{Fusion.BonusPct(next)}% (powers unchanged)",
                $"融合 {level} → {next}：能力值 +{Fusion.BonusPct(level)}% → +{Fusion.BonusPct(next)}%（固有效果不变）");
            _fusionCost = Loc.T($"費用：欠片{shards}" + (tuning > 0 ? $"・調律石{tuning}" : "") + $"（所持：欠片{p.Material(Materials.Shard)}・調律石{p.Material(Materials.Tuning)}）",
                $"Cost: {shards} shards" + (tuning > 0 ? $", {tuning} tuning" : "") + $" (owned: {p.Material(Materials.Shard)} shards, {p.Material(Materials.Tuning)} tuning)",
                $"费用：碎片{shards}" + (tuning > 0 ? $"・调律石{tuning}" : "") + $"（持有：碎片{p.Material(Materials.Shard)}・调律石{p.Material(Materials.Tuning)}）");
            _fusionAfford = p.Material(Materials.Shard) >= shards && p.Material(Materials.Tuning) >= tuning;
            _fusionSafe = _s.CanEditLoadout && !_s.Trades.IsReserved(target.Uid) && p.RetuneOffer?.Uid != target.Uid;
            _fusionAction = !_s.CanEditLoadout ? Loc.T("融合は確保地点か遠征の外で使えます", "Fusion requires a secure point or being outside an expedition", "融合需要在存仓点或远征之外进行")
                : !_fusionSafe ? Loc.T("目標は取引中・再調律中です", "Target is trading or retuning", "目标正在交易或再调律中")
                : !_fusionAfford ? Loc.T("融合の素材費用が足りません", "Not enough materials for fusion", "融合的材料费用不足")
                : material == null ? Loc.T("材料を選んでください", "Pick a material", "请选择材料")
                : Loc.T($"融合する（段階{next}・能力値+{Fusion.BonusPct(next)}%）", $"Fuse (level {next}, stats +{Fusion.BonusPct(next)}%)", $"融合（阶段{next}・能力值+{Fusion.BonusPct(next)}%）");
            _fusionDetail = material == null ? "" : Loc.T($"材料「{material.DisplayName}」を消費します。材料の融合段階・強化値は引き継がず、目標の強化値は変わりません。",
                $"Consumes \"{material.DisplayName}\". Its fusion and enhancement levels are lost; the target's enhancement stays unchanged.",
                $"消耗材料“{material.DisplayName}”。材料的融合阶段・强化值不会继承，目标的强化值保持不变。");
            _fusionConfirmAction = material == null ? _fusionAction : Loc.T($"<color=#ff8080>「{material.DisplayName}」を消費して融合します。もう一度押すと確定</color>",
                $"<color=#ff8080>Consume \"{material.DisplayName}\" for fusion? Press again to confirm</color>",
                $"<color=#ff8080>将消耗“{material.DisplayName}”进行融合。再按一次确定</color>");
        }

        private GameEvent ApplyFusion()
            => Rules.Fuse(_s.Profile, _fusionTarget.Uid, _fusionMaterial, _s.Trades, !_s.CanEditLoadout);

        private string FusionStatsText(Relic relic)
        {
            if (_fusionDetailRelic != relic || _fusionDetailLevel != relic.FusionLevel || _fusionDetailJapanese != Loc.CacheKey)
            {
                _fusionDetailRelic = relic;
                _fusionDetailLevel = relic.FusionLevel;
                _fusionDetailJapanese = Loc.CacheKey;
                _fusionStatsText = UiStyles.Colored(Loc.T($"融合 {relic.FusionLevel}/{Fusion.MaxLevel}：能力値 +{Fusion.BonusPct(relic.FusionLevel)}%（固有効果を除く）",
                    $"Fusion {relic.FusionLevel}/{Fusion.MaxLevel}: stats +{Fusion.BonusPct(relic.FusionLevel)}% (excluding powers)",
                    $"融合 {relic.FusionLevel}/{Fusion.MaxLevel}：能力值 +{Fusion.BonusPct(relic.FusionLevel)}%（不含固有效果）"), "#7fd8ff");
            }
            return _fusionStatsText;
        }

        private void DrawFusion(Relic target)
        {
            try { SyncFusion(target); }
            catch (Exception ex)
            {
                _fusionCacheValid = false;
                GUILayout.Label(Loc.T("融合のみ利用できません：", "Only Fusion is unavailable: ", "仅融合功能不可用：") + ex.Message, _st.Warn);
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
                "Pick one material with the same name and rarity (unlocked, unequipped, unreserved, not retuning).",
                "选择1件同名・同稀有度的材料（未上锁・未装备・不在交易中也不再调律中）。"), _st.Small);
            if (_fusionParts.Count == 0)
            {
                GUILayout.Label(Loc.T("使える材料がありません。", "No usable material.", "没有可用的材料。"), _st.Warn);
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
                try
                {
                    // クリック時は期限を待たずに再比較し、変化があれば確認を取り直す。
                    _fusionCheckAt = 0;
                    SyncFusion(target);
                    if (_fusionMaterial != null && _fusionAfford && _fusionSafe)
                    {
                        if (!_confirmFusion) _confirmFusion = true;
                        else
                        {
                            _confirmFusion = false;
                            if (_fusionApply == null) _fusionApply = ApplyFusion;
                            Act(_fusionApply, true);
                            _fusionCacheValid = false;
                            _fusionMaterial = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _fusionCacheValid = false;
                    _confirmFusion = false;
                    SetStatus(Loc.T("融合のみ停止しました：", "Only Fusion stopped: ", "仅融合已停用：") + ex.Message);
                }
            }
            GUI.enabled = true;
        }
    }
}
