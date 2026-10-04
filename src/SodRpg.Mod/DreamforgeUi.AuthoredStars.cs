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
        private static readonly GUILayoutOption[] StarWrappedWidth = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };

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
        private readonly GUIContent _starRelationHelp = new GUIContent();
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
            _starRelationHelp.text = Loc.T(
                "青い輪＝注目中（ホバー／二択を開いた星）　淡青の輪＝接続先・合わせ技の前提　太い線＝注目中の星の接続\n線で取得済みの星につながると解放（金＝取得済み）。合わせ技は橋・両端の星の取得と、両方の記憶の装着が必要です。",
                "Blue ring = focus (hover / open choice)   Pale blue ring = connection or combo prerequisite   Thick line = focus connection\nConnect to an acquired star to unlock (gold = acquired). Combos require the bridge, both endpoint stars and both memories equipped.");
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
            _starChoiceNote = new GUIStyle(_st.Small) { wordWrap = true };
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
            GUILayout.Label(_starRelationHelp, _starChoiceNote, StarWrappedWidth);
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
        private const float StarChoiceMargin = 8f, StarChoiceGap = 8f, StarChoiceScrollBar = 18f;
        private const float StarChoiceCloseWidth = 170f;
        private readonly Vector2[] _starChoiceScrolls = new Vector2[2];
        private readonly GUIContent _starChoiceHeader = new GUIContent(), _starChoiceClose = new GUIContent(), _starChoiceHelp = new GUIContent();
        private readonly GUIContent[] _starChoiceHeads = { new GUIContent(), new GUIContent() };
        private readonly float[] _starChoiceHeadHeight = new float[2], _starChoiceBodyHeight = new float[2], _starChoiceContentHeight = new float[2];
        private readonly int[] _starChoiceActionCount = new int[2];
        private readonly Rect[] _starChoiceViews = new Rect[2];
        private float _starChoiceHeaderHeight, _starChoiceHelpHeight, _starChoiceColumnWidth;
        private GUIStyle _starChoiceNote;
        private string _starChoiceStateText;
        private bool _starChoiceCanEdit;

        private sealed class StarChoiceAction
        {
            public readonly GUIContent Content = new GUIContent();
            public GUIStyle Style;
            public bool Button;
            public float Height;
        }

        private static StarChoiceAction[] StarNewActions()
        {
            var actions = new StarChoiceAction[4];
            for (int i = 0; i < actions.Length; i++) actions[i] = new StarChoiceAction();
            return actions;
        }
        private readonly StarChoiceAction[] _starChoiceActions = StarNewActions();

        /// <summary>選択パネルの星の添え字（#45: パネルを出している間の毎パスの全走査を避ける）。</summary>
        private int StarChoiceResolveIndex()
        {
            int index = _starChoiceIndex;
            if (index < 0 || index >= _starLayout.Nodes.Count || _starLayout.Nodes[index].Talent?.Id != _starChoiceId)
            {
                index = -1;
                for (int i = 0; i < _starLayout.Nodes.Count; i++)
                    if (_starLayout.Nodes[i].Talent?.Id == _starChoiceId) { index = i; break; }
                _starChoiceIndex = index;
            }
            return index;
        }

        // Both headings remain visible; only each card's contents scroll when the viewport is short.
        private Rect StarChoiceOverlayRect(Rect canvas)
        {
            if (!_starChoiceShown || Event.current.type == EventType.Layout) return Rect.zero;
            int index = StarChoiceResolveIndex();
            if (index < 0 || !_starLayout.Nodes[index].Talent.IsChoice)
                throw new InvalidOperationException("Selected choice star is missing from the current layout: " + _starChoiceId);
            EnsureStarTextStyles();
            var state = _starNodes[index];
            var t = _starLayout.Nodes[index].Talent;
            StarBuildChoicePanel(t, state, _s.CanEditTalents && _s.Profile.Run == null);
            float width = Mathf.Min(1000f, canvas.width - 2f * StarChoiceMargin);
            float available = canvas.height - 2f * StarChoiceMargin;
            if (width <= 0f || available <= 0f) return Rect.zero;
            float inner = width - _st.Panel.padding.horizontal;
            float closeWidth = Mathf.Min(StarChoiceCloseWidth, inner * 0.5f);
            _starChoiceHeaderHeight = Mathf.Max(
                _starChoiceHead.CalcHeight(_starChoiceHeader, inner - closeWidth - StarChoiceGap),
                _st.ButtonWrap.CalcHeight(_starChoiceClose, closeWidth));
            _starChoiceColumnWidth = (inner - StarChoiceGap) * 0.5f;
            float bodyWidth = Mathf.Max(1f, _starChoiceColumnWidth - _st.OptionIdle.padding.horizontal - StarChoiceScrollBar);
            _starChoiceHelpHeight = _starChoiceNote.CalcHeight(_starChoiceHelp, bodyWidth);
            float need = 0f;
            for (int option = 0; option < 2; option++)
            {
                _starChoiceHeadHeight[option] = _starChoiceHead.CalcHeight(_starChoiceHeads[option],
                    Mathf.Max(1f, _starChoiceColumnWidth - _st.OptionIdle.padding.horizontal));
                _starChoiceBodyHeight[option] = _starChoiceBody.CalcHeight(state.ChoiceOptions[option], bodyWidth);
                float content = _starChoiceBodyHeight[option] + StarChoiceGap + _starChoiceHelpHeight;
                for (int action = 0; action < _starChoiceActionCount[option]; action++)
                {
                    var item = _starChoiceActions[option * 2 + action];
                    item.Height = Mathf.Max(34f, item.Style.CalcHeight(item.Content, bodyWidth));
                    content += StarChoiceGap + item.Height;
                }
                _starChoiceContentHeight[option] = content;
                need = Mathf.Max(need, content);
            }
            float heads = Mathf.Max(_starChoiceHeadHeight[0], _starChoiceHeadHeight[1]);
            float height = Mathf.Min(available, _st.Panel.padding.vertical + _starChoiceHeaderHeight + StarChoiceGap
                + _st.OptionIdle.padding.vertical + heads + StarChoiceGap + need);
            var area = new Rect(canvas.x + (canvas.width - width) * 0.5f, canvas.y + StarChoiceMargin, width, height);
            float top = area.y + _st.Panel.padding.top + _starChoiceHeaderHeight + StarChoiceGap;
            float viewTop = top + _st.OptionIdle.padding.top + heads + StarChoiceGap;
            for (int option = 0; option < 2; option++)
            {
                _starChoiceViews[option] = new Rect(area.x + _st.Panel.padding.left
                    + option * (_starChoiceColumnWidth + StarChoiceGap) + _st.OptionIdle.padding.left,
                    viewTop, _starChoiceColumnWidth - _st.OptionIdle.padding.horizontal,
                    Mathf.Max(1f, area.yMax - _st.Panel.padding.bottom - _st.OptionIdle.padding.bottom - viewTop));
                _starChoiceScrolls[option].y = Mathf.Clamp(_starChoiceScrolls[option].y, 0f,
                    Mathf.Max(0f, _starChoiceContentHeight[option] - _starChoiceViews[option].height));
            }
            return area;
        }

        // Only actual controls capture input. Card text/background remains a canvas pan surface.
        private bool StarChoicePanelBlocks(Vector2 mouse)
        {
            if (_starOverlay.width <= 0f || !_starOverlay.Contains(mouse)) return false;
            if (StarChoiceCloseRect(_starOverlay).Contains(mouse)) return true;
            for (int option = 0; option < 2; option++)
            {
                Rect view = _starChoiceViews[option];
                if (!view.Contains(mouse)) continue;
                if (_starChoiceContentHeight[option] > view.height && mouse.x >= view.xMax - StarChoiceScrollBar) return true;
                float y = view.y + _starChoiceBodyHeight[option] + StarChoiceGap + _starChoiceHelpHeight - _starChoiceScrolls[option].y;
                for (int action = 0; action < _starChoiceActionCount[option]; action++)
                {
                    var item = _starChoiceActions[option * 2 + action];
                    y += StarChoiceGap;
                    if (item.Button && new Rect(view.x, y, view.width - StarChoiceScrollBar, item.Height).Contains(mouse)) return true;
                    y += item.Height;
                }
            }
            return false;
        }

        private Rect StarChoiceCloseRect(Rect area)
        {
            float inner = area.width - _st.Panel.padding.horizontal;
            float width = Mathf.Min(StarChoiceCloseWidth, inner * 0.5f);
            return new Rect(area.xMax - _st.Panel.padding.right - width, area.y + _st.Panel.padding.top, width, _starChoiceHeaderHeight);
        }

        private void StarBuildChoicePanel(TalentDef t, StarNode state, bool canEdit)
        {
            if (_starChoiceStateText == state.Tooltip.text && _starChoiceCanEdit == canEdit) return;
            _starChoiceStateText = state.Tooltip.text;
            _starChoiceCanEdit = canEdit;
            _starChoiceHeader.text = state.Name.text + "  " + state.RankLabel.text;
            _starChoiceClose.text = Loc.T("選択を閉じる", "Close selection");
            _starChoiceHelp.text = !canEdit
                ? Loc.T("遠征中は変更できません。帰還後は無料で切り替えられます。", "Cannot change during an expedition. Switching is free after returning.")
                : Loc.T("どちらか1つを選択。選んだ効果は全段に適用され、切り替えは無料です。", "Choose one effect. It applies to every rank; switching is free.");
            for (int option = 0; option < 2; option++)
            {
                bool chosen = state.Allocated && state.Choice == option;
                bool switching = state.Allocated && !chosen;
                bool more = chosen && state.Rank < t.MaxRank;
                string heading = option == 0 ? Loc.T("効果 A", "Effect A") : Loc.T("効果 B", "Effect B");
                _starChoiceHeads[option].text = chosen
                    ? "<color=#ffd36e>✓ " + heading + Loc.T("（選択中）", " (chosen)") + "</color>"
                    : "<color=#9fe0ff>" + heading + Loc.T("（未選択）", " (unselected)") + "</color>";
                string blocked = !canEdit ? Loc.T("遠征中は変更できません", "Cannot change during an expedition")
                    : switching ? null
                    : !state.Unlocked ? Loc.T("取得済みの星と線でつながると選べます", "Connect it to an acquired star to choose")
                    : state.Rank >= t.MaxRank ? null
                    : !state.Available ? Loc.T("ポイントが足りません", "Not enough points") : null;
                int count = 0;
                if (chosen && (!more || blocked != null))
                    StarChoiceSetAction(option, count++, !more
                        ? Loc.T($"✓ 選択中（{state.Rank}/{t.MaxRank}段・最大）", $"✓ Chosen ({state.Rank}/{t.MaxRank}, maximum)")
                        : Loc.T($"✓ 選択中（{state.Rank}/{t.MaxRank}段）", $"✓ Chosen ({state.Rank}/{t.MaxRank})"), _st.TagChosen, false);
                if (blocked != null)
                    StarChoiceSetAction(option, count++, Loc.T("取得できません：", "Unavailable: ") + blocked, _st.TagBlocked, false);
                else if (!chosen || more)
                    StarChoiceSetAction(option, count++, switching ? Loc.T("この効果に切り替える（無料）", "Switch to this effect (free)")
                        : more ? Loc.T($"この効果をもう1段取得（{state.Rank}/{t.MaxRank}段）", $"Allocate one more rank ({state.Rank}/{t.MaxRank})")
                        : Loc.T("この効果で1段取得", "Allocate one rank with this effect"), _st.ButtonWrap, true);
                _starChoiceActionCount[option] = count;
            }
        }

        private void StarChoiceSetAction(int option, int action, string text, GUIStyle style, bool button)
        {
            var item = _starChoiceActions[option * 2 + action];
            item.Content.text = text;
            item.Style = style;
            item.Button = button;
        }

        private void DrawStarChoicePicker(Profile p, string hero, Rect area)
        {
            if (!_starChoiceShown || area.width <= 0f) return;
            int index = StarChoiceResolveIndex();
            var t = _starLayout.Nodes[index].Talent;
            var state = _starNodes[index];
            bool close = false;
            int act = -1;
            GUI.Box(area, GUIContent.none, _st.Panel);
            Rect closeRect = StarChoiceCloseRect(area);
            GUI.Label(new Rect(area.x + _st.Panel.padding.left, closeRect.y,
                closeRect.x - area.x - _st.Panel.padding.left - StarChoiceGap, _starChoiceHeaderHeight), _starChoiceHeader, _starChoiceHead);
            close = GUI.Button(closeRect, _starChoiceClose, _st.ButtonWrap);
            float heads = Mathf.Max(_starChoiceHeadHeight[0], _starChoiceHeadHeight[1]);
            for (int option = 0; option < 2; option++)
            {
                Rect view = _starChoiceViews[option];
                var card = new Rect(view.x - _st.OptionIdle.padding.left,
                    view.y - StarChoiceGap - heads - _st.OptionIdle.padding.top,
                    _starChoiceColumnWidth, view.height + StarChoiceGap + heads + _st.OptionIdle.padding.vertical);
                GUI.Box(card, GUIContent.none, state.Allocated && state.Choice == option ? _st.OptionChosen : _st.OptionIdle);
                GUI.Label(new Rect(view.x, card.y + _st.OptionIdle.padding.top, view.width, heads), _starChoiceHeads[option], _starChoiceHead);
                float width = Mathf.Max(1f, view.width - StarChoiceScrollBar);
                _starChoiceScrolls[option] = GUI.BeginScrollView(view, _starChoiceScrolls[option],
                    new Rect(0f, 0f, width, _starChoiceContentHeight[option]));
                try
                {
                    GUI.Label(new Rect(0f, 0f, width, _starChoiceBodyHeight[option]), state.ChoiceOptions[option], _starChoiceBody);
                    float y = _starChoiceBodyHeight[option] + StarChoiceGap;
                    GUI.Label(new Rect(0f, y, width, _starChoiceHelpHeight), _starChoiceHelp, _starChoiceNote);
                    y += _starChoiceHelpHeight;
                    for (int action = 0; action < _starChoiceActionCount[option]; action++)
                    {
                        var item = _starChoiceActions[option * 2 + action];
                        y += StarChoiceGap;
                        var rect = new Rect(0f, y, width, item.Height);
                        if (item.Button) { if (GUI.Button(rect, item.Content, item.Style)) act = option; }
                        else GUI.Box(rect, item.Content, item.Style);
                        y += item.Height;
                    }
                }
                finally { GUI.EndScrollView(); }
            }
            if (close) { _starChoiceId = null; GUIUtility.ExitGUI(); }
            if (act < 0) return;
            try
            {
                if (state.Allocated && state.Choice != act) Rules.SetTalentChoice(p, hero, t.Id, act);
                else Rules.AddTalentRank(p, hero, t.Id, act);
                _starDirty = true;
                _s.MarkDirty(true);
            }
            catch (AllocationValidationException error) { OfferAllocationRefund(error, p, hero); }
            catch (InvalidOperationException error) { SetStatus(error.Message); }
            GUIUtility.ExitGUI();
        }
    }
}
