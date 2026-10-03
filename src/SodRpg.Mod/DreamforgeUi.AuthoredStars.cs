using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// v1.31 star-map controls: explicit two-option allocation/free switching outside expeditions,
    /// C15 refund approval through the existing panel, and camera jumps at the current 0.15–3 zoom.
    /// Cluster metadata/text rebuild only on content, language or allocation changes; row drawing is
    /// viewport-bounded. Authored IDs are the cluster titles because the Core contract has no title Txt.
    /// </summary>
    internal sealed partial class DreamforgeUi
    {
        // Rebuild metadata/text only on layout, allocation or language changes. The list is virtualized.
        private StarMapCluster[] _starClusters = Array.Empty<StarMapCluster>();
        private GUIContent[] _starClusterLabels = Array.Empty<GUIContent>();
        private float[] _starClusterTops = Array.Empty<float>(), _starClusterHeights = Array.Empty<float>();
        private float _starClusterWidth = -1f, _starClusterHeight;
        private readonly GUIContent _starRegionLegend = new GUIContent();
        private Vector2 _starClusterScroll, _starChoiceScroll;
        private bool _starClustersOpen = true;
        private static readonly GUILayoutOption[] StarClusterSize =
            { GUILayout.Width(226), GUILayout.ExpandHeight(true), GUILayout.MinHeight(160) };
        private static readonly GUILayoutOption[] StarChoiceSize = { GUILayout.MaxHeight(170) };

        private void RebuildStarClusters()
        {
            _starClusters = StarMapClusters.Build(_starLayout);
            _starClusterLabels = new GUIContent[_starClusters.Length];
            for (int i = 0; i < _starClusters.Length; i++) _starClusterLabels[i] = new GUIContent();
            _starClusterTops = new float[_starClusters.Length];
            _starClusterHeights = new float[_starClusters.Length];
            _starClusterWidth = -1f;
            _starClusterScroll = Vector2.zero;
            _starRegionLegend.text = "<color=#78baff>● " + StarMapClusters.RegionLabel(ClusterRegionKind.Memory)
                + "</color>   <color=#73e6b3>● " + StarMapClusters.RegionLabel(ClusterRegionKind.Bridge)
                + "</color>   <color=#ffa875>● " + StarMapClusters.RegionLabel(ClusterRegionKind.Outer)
                + "</color>   <color=#cc8cff>◆ " + StarMapClusters.RegionLabel(ClusterRegionKind.Keystone) + "</color>";
        }

        private void RefreshStarClusters(HeroState state)
        {
            for (int i = 0; i < _starClusters.Length; i++)
            {
                var cluster = _starClusters[i];
                // The authored contract identifies a cluster by ID; it has no localized cluster-title field.
                _starClusterLabels[i].text = StarMapClusters.RegionLabel(cluster.Region) + " / " + cluster.Id
                    + "\n" + Loc.T("代表の星：", "Representative star: ") + cluster.Name
                    + "\n" + StarMapClusters.AllocatedCount(state, _starLayout, cluster) + "/" + cluster.NodeCount
                    + Loc.T(" 星取得", " stars acquired");
            }
            _starClusterWidth = -1f;
        }

        private static Color StarRegionColor(ClusterRegionKind region)
        {
            switch (region)
            {
                case ClusterRegionKind.Memory: return new Color(0.47f, 0.73f, 1f);
                case ClusterRegionKind.Bridge: return new Color(0.45f, 0.90f, 0.70f);
                case ClusterRegionKind.Outer: return new Color(1f, 0.66f, 0.46f);
                case ClusterRegionKind.Keystone: return StarKeystone;
                default: throw new InvalidOperationException("Unmapped star-map region: " + region);
            }
        }

        private void DrawStarRegionLegend()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(_starRegionLegend, _st.Small, StarFill);
            _starClustersOpen = GUILayout.Toggle(_starClustersOpen, Loc.T("星群一覧", "Clusters"), _st.Button);
            GUILayout.EndHorizontal();
        }

        private void DrawStarClusterList()
        {
            if (!_starClustersOpen || _starClusters.Length == 0) return;
            Rect area = GUILayoutUtility.GetRect(0, 226, 0, 10000, StarClusterSize);
            if (Event.current.type == EventType.Layout) return;
            float width = Mathf.Max(1, area.width - 18);
            if (_starClusterWidth != width)
            {
                _starClusterWidth = width;
                _starClusterHeight = 0;
                for (int i = 0; i < _starClusters.Length; i++)
                {
                    _starClusterTops[i] = _starClusterHeight;
                    _starClusterHeights[i] = _st.RowWrap.CalcHeight(_starClusterLabels[i], width - 6) + 4;
                    _starClusterHeight += _starClusterHeights[i];
                }
            }
            Rect content = new Rect(0, 0, width, _starClusterHeight);
            _starClusterScroll = GUI.BeginScrollView(area, _starClusterScroll, content);
            try
            {
                int first = 0, end = _starClusters.Length;
                while (first < end)
                {
                    int mid = first + (end - first) / 2;
                    if (_starClusterTops[mid] + _starClusterHeights[mid] < _starClusterScroll.y) first = mid + 1;
                    else end = mid;
                }
                for (int i = first; i < _starClusters.Length; i++)
                {
                    var cluster = _starClusters[i];
                    if (_starClusterTops[i] > _starClusterScroll.y + area.height) break;
                    Rect row = new Rect(0, _starClusterTops[i], content.width, _starClusterHeights[i] - 2);
                    StarFillRect(new Rect(row.x, row.y, 4, row.height), StarRegionColor(cluster.Region));
                    if (GUI.Button(new Rect(row.x + 6, row.y, row.width - 6, row.height), _starClusterLabels[i], _st.RowWrap))
                    {
                        var pan = StarMapMath.PanToNode(cluster.X, cluster.Y, _starZoom);
                        _starPan = new Vector2(pan.X, pan.Y);
                        _starNeedsFit = false;
                        CancelStarDrag();
                    }
                }
            }
            finally { GUI.EndScrollView(); }
        }

        private void StarSumAddNode(int index, string text)
        {
            var entry = new StarSumEntry { Nodes = new[] { index } };
            entry.Content.text = text;
            _starSumEntries.Add(entry);
        }

        private void DrawStarChoicePicker(Profile p, string hero)
        {
            if (_starChoiceId == null) return;
            int index = -1;
            for (int i = 0; i < _starLayout.Nodes.Count; i++)
                if (_starLayout.Nodes[i].Talent?.Id == _starChoiceId) { index = i; break; }
            if (index < 0 || !_starLayout.Nodes[index].Talent.IsChoice)
                throw new InvalidOperationException("Selected choice star is missing from the current layout: " + _starChoiceId);
            var t = _starLayout.Nodes[index].Talent;
            var state = _starNodes[index];
            GUILayout.BeginVertical(_st.Panel);
            GUILayout.Label(state.Name, _st.Label);
            if (!_s.CanEditTalents || p.Run != null)
                GUILayout.Label(Loc.T("遠征中は選択を変更できません。帰還後は無料で切り替えられます。",
                    "Choices cannot be changed during an expedition. Switching is free after returning."), _st.Warn);
            else if (state.Allocated)
                GUILayout.Label(Loc.T("選択中の効果は全段に適用されます。切り替えは無料です。",
                    "The chosen effect applies to every rank. Switching is free."), _st.Small);
            _starChoiceScroll = GUILayout.BeginScrollView(_starChoiceScroll, StarChoiceSize);
            for (int option = 0; option < 2; option++)
            {
                GUILayout.Label(state.ChoiceOptions[option], _st.Small);
                bool enabled = GUI.enabled;
                bool switching = state.Allocated && state.Choice != option;
                GUI.enabled = enabled && _s.CanEditTalents && p.Run == null
                    && (switching || state.Available && (!state.Allocated || state.Choice == option));
                if (GUILayout.Button(switching ? Loc.T("この効果に切り替える（無料）", "Switch to this effect (free)")
                    : Loc.T("この効果で1段取得", "Allocate one rank with this effect"), _st.Button))
                {
                    try
                    {
                        if (switching) Rules.SetTalentChoice(p, hero, t.Id, option);
                        else Rules.AddTalentRank(p, hero, t.Id, option);
                        _starDirty = true;
                        _s.MarkDirty(true);
                    }
                    catch (AllocationValidationException error) { OfferAllocationRefund(error, p, hero); }
                    catch (InvalidOperationException error) { SetStatus(error.Message); }
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = enabled;
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(Loc.T("選択を閉じる", "Close selection"), _st.Button)) _starChoiceId = null;
            GUILayout.EndVertical();
        }
    }
}
