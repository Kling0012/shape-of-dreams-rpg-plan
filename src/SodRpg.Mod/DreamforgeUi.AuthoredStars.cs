using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// v1.31 star-map controls: explicit two-option allocation/free switching outside expeditions,
    /// C15 refund approval through the existing panel, and camera jumps at the current 0.15–3 zoom.
    /// Cluster metadata/text rebuild only on content, language or allocation changes; row drawing is
    /// viewport-bounded. Clusters are listed by their display names (never by internal IDs).
    /// </summary>
    internal sealed partial class DreamforgeUi
    {
        // Rebuild metadata/text only on layout, allocation or language changes. The list is virtualized.
        private StarMapCluster[] _starClusters = Array.Empty<StarMapCluster>();
        private GUIContent[] _starClusterLabels = Array.Empty<GUIContent>();
        private float[] _starClusterTops = Array.Empty<float>(), _starClusterHeights = Array.Empty<float>();
        private float _starClusterWidth = -1f, _starClusterHeight;
        private Vector2 _starClusterScroll;
        private bool _starClustersOpen = true;
        private GUIStyle _starClusterRow, _starHelpStyle, _starLegendStyle, _starChoiceBody, _starChoiceHead;
        private static readonly GUILayoutOption[] StarClusterSize =
            { GUILayout.Width(270), GUILayout.ExpandHeight(true), GUILayout.MinHeight(160) };
        private static readonly GUILayoutOption[] StarOptionColumn = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] StarCloseWidth = { GUILayout.Width(170) };
        private static readonly GUILayoutOption[] StarTagHeight = { GUILayout.MinHeight(34) };

        // Legend: every item has a hover tooltip. Items 0-3 are the region row, 4-6 the colour row.
        private sealed class StarLegendItem
        {
            public readonly GUIContent Label = new GUIContent();
            public readonly GUIContent Tip = new GUIContent();
            public float TipHeight = -1f;
        }
        private const int StarLegendRegionItems = 4, StarLegendItems = 7;
        private const float StarLegendTipWidth = 440f;
        private readonly StarLegendItem[] _starLegend = NewLegend();
        private readonly GUIContent _starHelpText = new GUIContent();
        private int _starLegendHover = -1;
        private float _windowWidth = 1060f;

        private static StarLegendItem[] NewLegend()
        {
            var items = new StarLegendItem[StarLegendItems];
            for (int i = 0; i < items.Length; i++) items[i] = new StarLegendItem();
            return items;
        }

        private void BuildStarLegend()
        {
            string[] labels =
            {
                "<color=#78baff>● " + StarMapClusters.RegionLabel(ClusterRegionKind.Memory) + "</color>",
                "<color=#73e6b3>● " + StarMapClusters.RegionLabel(ClusterRegionKind.Bridge) + "</color>",
                "<color=#ffa875>● " + StarMapClusters.RegionLabel(ClusterRegionKind.Outer) + "</color>",
                "<color=#cc8cff>◆ " + StarMapClusters.RegionLabel(ClusterRegionKind.Keystone) + "</color>",
                Loc.T("<color=#cc8cff>紫の大きな星＝刻印</color>", "<color=#cc8cff>Large purple = keystone</color>"),
                Loc.T("<color=#73e6f2>水色＝合わせ技</color>", "<color=#73e6f2>Cyan = combo</color>"),
                Loc.T("<color=#ffc952>金＝取得済み</color>", "<color=#ffc952>Gold = acquired</color>"),
            };
            string[] tips =
            {
                Loc.T("<b>記憶の星団</b>\n装備する記憶（スキル）ごとの星のまとまりです。星を取ると、その記憶が強くなります。星の右上の青い点が目印です。",
                    "<b>Memory cluster</b>\nStars grouped by an equippable memory (skill). Acquiring them strengthens that memory. Marked by a blue dot on the star."),
                Loc.T("<b>記憶の橋の星団</b>\n二つの記憶をつなぐ星のまとまりです。両方の記憶を装備すると合わせ技が働きます。星の右上の緑の点が目印です。",
                    "<b>Bridge cluster</b>\nStars that link two memories. Equip both memories to activate the combo. Marked by a green dot on the star."),
                Loc.T("<b>外縁の星団</b>\n記憶に依らない、旅人全体の能力や仕掛けを伸ばす星のまとまりです。星の右上の橙の点が目印です。",
                    "<b>Outer cluster</b>\nStars that grow the whole Traveler rather than one memory. Marked by an orange dot on the star."),
                Loc.T("<b>刻印</b>\n大きな紫の星。星のレベルに応じて最大3つまで選べて、利点と代償の両方があります。条件を満たすと選べます。",
                    "<b>Keystone</b>\nLarge purple stars. Up to three can be selected as the star level rises; each has a benefit and a drawback. Selectable once its requirements are met."),
                Loc.T("<b>紫の大きな星＝刻印</b>\n星のレベルで増える枠まで選べる特別な星です。上の「刻印」の行からも選べます。",
                    "<b>Large purple star = keystone</b>\nA special star you can hold as many of as your keystone slots allow. You can also pick it from the keystone row above."),
                Loc.T("<b>水色＝合わせ技</b>\n水色の枠の星は、二つの記憶をつなぐ合わせ技の橋です。両方の記憶を装備していると働きます。",
                    "<b>Cyan = combo</b>\nCyan-framed stars are combo bridges between two memories. They work while both memories are equipped."),
                Loc.T("<b>金＝取得済み</b>\n金色の星と線は、すでに取得した星とそのつながりです。",
                    "<b>Gold = acquired</b>\nGold stars and lines are the stars you have already acquired and their connections."),
            };
            for (int i = 0; i < StarLegendItems; i++)
            {
                _starLegend[i].Label.text = labels[i];
                _starLegend[i].Tip.text = tips[i];
                _starLegend[i].TipHeight = -1f;
            }
            _starHelpText.text = Loc.T("ドラッグ：移動　ホイール：拡大縮小　左クリック：振る　右クリック：外す　",
                "Drag: pan   Wheel: zoom   Left click: allocate   Right click: refund   ");
        }

        private void RebuildStarClusters()
        {
            _starClusters = StarMapClusters.Build(_starLayout);
            _starClusterLabels = new GUIContent[_starClusters.Length];
            for (int i = 0; i < _starClusters.Length; i++) _starClusterLabels[i] = new GUIContent();
            _starClusterTops = new float[_starClusters.Length];
            _starClusterHeights = new float[_starClusters.Length];
            _starClusterWidth = -1f;
            _starClusterScroll = Vector2.zero;
            BuildStarLegend();
        }

        private void RefreshStarClusters(HeroState state)
        {
            for (int i = 0; i < _starClusters.Length; i++)
            {
                var cluster = _starClusters[i];
                _starClusterLabels[i].text = "<b>" + cluster.DisplayName + "</b>"
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

        private void EnsureStarTextStyles()
        {
            if (_starHelpStyle != null) return;
            _starHelpStyle = new GUIStyle(_st.Small) { fontSize = 15, wordWrap = false };
            _starLegendStyle = new GUIStyle(_st.Small) { fontSize = 15, wordWrap = false, padding = new RectOffset(4, 4, 3, 3) };
            _starClusterRow = new GUIStyle(_st.RowWrap) { fontSize = 15 };
            _starChoiceBody = new GUIStyle(_st.Label) { fontSize = 16, wordWrap = true, richText = true };
            _starChoiceHead = new GUIStyle(_st.Label) { fontSize = 17, wordWrap = true, richText = true, fontStyle = FontStyle.Bold };
        }

        /// <summary>
        /// Region legend and colour help. Each item has its own hit rect (layout space, so UI scale does not matter);
        /// the hovered item's tooltip is drawn last by <see cref="DrawStarLegendTooltip"/>.
        /// </summary>
        private void DrawStarLegend()
        {
            EnsureStarTextStyles();
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) _starLegendHover = -1;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < StarLegendRegionItems; i++) DrawStarLegendItem(i, repaint);
            GUILayout.FlexibleSpace();
            _starClustersOpen = GUILayout.Toggle(_starClustersOpen, Loc.T("星群一覧", "Clusters"), _st.Button);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(_starHelpText, _starHelpStyle);
            for (int i = StarLegendRegionItems; i < StarLegendItems; i++) DrawStarLegendItem(i, repaint);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawStarLegendItem(int index, bool repaint)
        {
            var item = _starLegend[index];
            Rect rect = GUILayoutUtility.GetRect(item.Label, _starLegendStyle);
            GUI.Label(rect, item.Label, _starLegendStyle);
            if (repaint && rect.Contains(Event.current.mousePosition)) _starLegendHover = index;
            GUILayout.Space(10);
        }

        private void DrawStarLegendTooltip()
        {
            if (_starLegendHover < 0 || Event.current.type != EventType.Repaint) return;
            EnsureStarTextStyles();
            var item = _starLegend[_starLegendHover];
            if (item.TipHeight < 0f) item.TipHeight = _starTooltipStyle.CalcHeight(item.Tip, StarLegendTipWidth);
            Vector2 mouse = Event.current.mousePosition;
            float x = Mathf.Clamp(mouse.x + 12f, 6f, Mathf.Max(6f, _windowWidth - StarLegendTipWidth - 6f));
            float y = mouse.y + 22f;
            if (y + item.TipHeight > _windowHeight - 6f) y = Mathf.Max(6f, mouse.y - item.TipHeight - 8f);
            var tip = new Rect(x, y, StarLegendTipWidth, item.TipHeight);
            StarFillRect(tip, new Color(0.1f, 0.12f, 0.18f));
            GUI.Label(tip, item.Tip, _starTooltipStyle);
        }

        private void DrawStarClusterList()
        {
            if (!_starClustersOpen) return;
            EnsureStarTextStyles();
            if (_starClusters.Length == 0)
            {
                GUILayout.Label(Loc.T("この旅人にはまだ星団がありません。", "This traveler has no clusters yet."), _starHelpStyle);
                return;
            }
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
                    _starClusterHeights[i] = _starClusterRow.CalcHeight(_starClusterLabels[i], width - 6) + 4;
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
                    if (GUI.Button(new Rect(row.x + 6, row.y, row.width - 6, row.height), _starClusterLabels[i], _starClusterRow))
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

        // 選択パネルは星図の上に重ねて描く（レイアウトの高さを使わないので、星図が縮まない）。
        // Layout イベントで開閉を確定し、同じフレームの残りのイベントでは変えない。
        private bool _starChoiceShown;
        private Rect _starOverlay;
        private const float StarChoiceMargin = 8f, StarChoiceMaxHeight = 236f;

        /// <summary>選択パネルを星図の上端に重ねる範囲。開いていなければ空。</summary>
        private Rect StarChoiceOverlayRect(Rect canvas)
        {
            if (!_starChoiceShown) return Rect.zero;
            float height = Mathf.Min(StarChoiceMaxHeight, canvas.height - 2f * StarChoiceMargin);
            return height < 60f ? Rect.zero : new Rect(canvas.x + StarChoiceMargin, canvas.y + StarChoiceMargin, canvas.width - 2f * StarChoiceMargin, height);
        }

        private void DrawStarChoicePicker(Profile p, string hero, Rect area)
        {
            if (!_starChoiceShown || area.width <= 0f) return;
            if (_starChoiceId == null) { GUIUtility.ExitGUI(); return; }
            EnsureStarTextStyles();
            // 約900星の図を、パネルを出している間じゅう毎パス走査しないように添え字を覚える（#45）。
            int index = _starChoiceIndex;
            if (index < 0 || index >= _starLayout.Nodes.Count || _starLayout.Nodes[index].Talent?.Id != _starChoiceId)
            {
                index = -1;
                for (int i = 0; i < _starLayout.Nodes.Count; i++)
                    if (_starLayout.Nodes[i].Talent?.Id == _starChoiceId) { index = i; break; }
                _starChoiceIndex = index;
            }
            if (index < 0 || !_starLayout.Nodes[index].Talent.IsChoice)
                throw new InvalidOperationException("Selected choice star is missing from the current layout: " + _starChoiceId);
            var t = _starLayout.Nodes[index].Talent;
            var state = _starNodes[index];
            bool canEdit = _s.CanEditTalents && p.Run == null;
            // 二つの列は同じ幅にする（本文の長さで幅が変わらないよう、幅を固定する）。
            float column = Mathf.Floor((area.width - _st.Panel.padding.horizontal - 2f * (_st.OptionIdle.margin.horizontal)) * 0.5f);
            var columnWidth = new[] { GUILayout.Width(Mathf.Max(120f, column)) };
            GUILayout.BeginArea(area, _st.Panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(state.Name.text + "  " + state.RankLabel.text, _starChoiceHead, StarOptionColumn);
            if (GUILayout.Button(Loc.T("選択を閉じる", "Close selection"), _st.Button, StarCloseWidth))
            {
                _starChoiceId = null;
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            if (!canEdit)
                GUILayout.Label(Loc.T("遠征中は選択を変更できません。帰還後は無料で切り替えられます。",
                    "Choices cannot be changed during an expedition. Switching is free after returning."), _st.Warn);
            else
                GUILayout.Label(state.Allocated
                    ? Loc.T("選んだ効果は全段に適用されます。別の効果への切り替えは無料です。",
                        "The chosen effect applies to every rank. Switching is free.")
                    : Loc.T("二つの効果のうち、1つを選んで取得します（あとから無料で切り替えられます）。",
                        "Pick one of the two effects to acquire (you can switch later for free)."), _starHelpStyle);
            GUILayout.BeginHorizontal();
            for (int option = 0; option < 2; option++) DrawStarChoiceOption(p, hero, t, state, option, canEdit, columnWidth);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawStarChoiceOption(Profile p, string hero, TalentDef t, StarNode state, int option, bool canEdit, GUILayoutOption[] columnWidth)
        {
            bool chosen = state.Allocated && state.Choice == option;
            GUILayout.BeginVertical(chosen ? _st.OptionChosen : _st.OptionIdle, columnWidth);
            GUILayout.Label(chosen ? Loc.T("<color=#ffd36e>✓ 選択中の効果</color>", "<color=#ffd36e>✓ Chosen effect</color>")
                : state.Allocated ? Loc.T("<color=#d6d6ea>もう一方の効果</color>", "<color=#d6d6ea>The other effect</color>")
                : option == 0 ? Loc.T("<color=#9fe0ff>効果 A</color>", "<color=#9fe0ff>Effect A</color>")
                : Loc.T("<color=#9fe0ff>効果 B</color>", "<color=#9fe0ff>Effect B</color>"), _starChoiceHead);
            GUILayout.Label(state.ChoiceOptions[option], _starChoiceBody);
            GUILayout.FlexibleSpace();
            bool switching = state.Allocated && !chosen;
            bool more = chosen && state.Rank < t.MaxRank;
            // The disabled state is a dark box with the reason; the chosen state is a green tag with a check mark.
            // They are deliberately different from a greyed-out button, which used to read as "already chosen".
            string blocked = !canEdit ? Loc.T("遠征中は変更できません", "Cannot change during an expedition")
                : switching ? null
                : !state.Unlocked ? Loc.T("取得済みの星と線でつながると選べます", "Connect it to an acquired star to choose")
                : state.Rank >= t.MaxRank ? null
                : !state.Available ? Loc.T("ポイントが足りません", "Not enough points")
                : null;
            bool act = false;
            if (chosen && !more)
                GUILayout.Box(Loc.T($"✓ 選択中（{state.Rank}/{t.MaxRank}段・最大）", $"✓ Chosen ({state.Rank}/{t.MaxRank}, maximum)"), _st.TagChosen, StarTagHeight);
            else if (blocked != null)
            {
                if (chosen) GUILayout.Box(Loc.T($"✓ 選択中（{state.Rank}/{t.MaxRank}段）", $"✓ Chosen ({state.Rank}/{t.MaxRank})"), _st.TagChosen, StarTagHeight);
                GUILayout.Box(Loc.T("取得できません：", "Unavailable: ") + blocked, _st.TagBlocked, StarTagHeight);
            }
            else
            {
                act = GUILayout.Button(switching ? Loc.T("この効果に切り替える（無料）", "Switch to this effect (free)")
                    : more ? Loc.T($"この効果をもう1段取得（{state.Rank}/{t.MaxRank}段）", $"Allocate one more rank (currently {state.Rank}/{t.MaxRank})")
                    : Loc.T("この効果で1段取得", "Allocate one rank with this effect"), _st.ButtonWrap, StarTagHeight);
            }
            GUILayout.EndVertical();
            if (!act) return;
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
    }
}
