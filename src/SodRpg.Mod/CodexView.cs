using System;
using System.Collections.Generic;
using System.Text;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Line = SodRpg.Core.Game.Line;
    using Power = SodRpg.Core.Game.Power;
    using Rarity = SodRpg.Core.Game.Rarity;
    using Slot = SodRpg.Core.Game.Slot;

    /// <summary>
    /// 図鑑（記録タブから開く）。土台・固有品・セット・固有効果を、絞り込みと文字検索つきで一覧し、右に詳細を出す。
    /// 性能：一覧と詳細の文字列は、絞り込み・検索・選択・図鑑の件数・言語が変わったときだけ作り直す。
    /// 描画は見えている行だけを、固定の行高で手動に描く（GUILayout は場所の確保にしか使わない）。
    /// 見つけていない固有品・セット・固有効果は名前も効果も出さない（？？？）。土台は秘密ではないので名前とアイコンを淡く出す。
    /// </summary>
    internal sealed class CodexView
    {
        private const float RowH = 40f, IconSize = 32f, ListW = 400f, BarH = 28f;
        private const string Gold = "#ffd24a", Orange = "#ff8a3d", Purple = "#e0b0ff", Cyan = "#7fd8ff", Dim = "#8a8aa0";

        private readonly CodexFilter _filter = new CodexFilter { Category = CodexCategory.Uniques };
        private CodexResult _res = new CodexResult();
        private CodexState _state;
        private string _search = "";
        private bool _dirty = true;
        private int _sigCodex = -1, _sigStash = -1, _sigLost = -1, _sigSatchel = -1;
        private bool _sigJa;

        private readonly List<string> _rowText = new List<string>();
        private readonly List<Color> _rowFrame = new List<Color>();
        private readonly string[] _catLabels = new string[4];
        private readonly string[] _foundLabels = new string[3];
        private readonly string[] _slotLabels = new string[7];
        private readonly string[] _lineLabels = new string[4];
        private string _status = "", _summary = "", _hint = "", _backLabel = "", _searchHint = "", _emptyText = "";

        private CodexEntry _sel;
        private bool _selFound;
        private string _detailHead = "", _detailBody = "";
        private float _detailBodyH, _detailBodyW = -1;

        private Vector2 _listScroll, _detailScroll;
        private GUIStyle _btn, _btnSel, _rowName, _searchStyle, _hintStyle, _dimLabel, _qMark;
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly GUIContent _tmp = new GUIContent();

        private static readonly CodexCategory[] Cats = { CodexCategory.Uniques, CodexCategory.Bases, CodexCategory.Sets, CodexCategory.Powers };
        private static readonly Slot[] Slots = { Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm };
        private static readonly Line[] Lines = { Line.Offense, Line.Guard, Line.Resonance };

        private static string CatName(CodexCategory c)
        {
            switch (c)
            {
                case CodexCategory.Bases: return Loc.T("土台", "Bases");
                case CodexCategory.Uniques: return Loc.T("固有品", "Legendaries");
                case CodexCategory.Sets: return Loc.T("セット", "Sets");
                default: return Loc.T("固有効果", "Powers");
            }
        }

        /// <summary>記録タブに出す要約（見つけた数）。図鑑が変わったときだけ作り直す。</summary>
        public string Summary(Profile p)
        {
            Sync(p);
            return _summary;
        }

        /// <summary>図鑑を描く。戻る操作が押されたら true。</summary>
        public bool Draw(Profile p, UiStyles st)
        {
            Sync(p);
            EnsureStyles(st);
            var area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Layout) return false;
            bool back = false;
            float x = area.x, w = area.width, y = area.y;

            // 1段目：戻る・カテゴリ
            if (GUI.Button(new Rect(x, y, 120f, BarH), _backLabel, _btn)) back = true;
            float cx = x + 130f, cw = Mathf.Max(80f, (w - 130f) / 4f - 4f);
            for (int i = 0; i < 4; i++)
            {
                bool on = _filter.Category == Cats[i];
                if (GUI.Button(new Rect(cx + i * (cw + 4f), y, cw, BarH), _catLabels[i], on ? _btnSel : _btn) && !on)
                {
                    _filter.Category = Cats[i];
                    _filter.Slot = null;
                    _filter.Line = null;
                    _sel = null;
                    _listScroll = Vector2.zero;
                    _dirty = true;
                }
            }
            y += BarH + 4f;

            // 2段目：見つけた状態・検索
            float fw = 118f;
            for (int i = 0; i < 3; i++)
            {
                bool on = (int)_filter.Found == i;
                if (GUI.Button(new Rect(x + i * (fw + 4f), y, fw, BarH), _foundLabels[i], on ? _btnSel : _btn) && !on)
                {
                    _filter.Found = (CodexFoundFilter)i;
                    _listScroll = Vector2.zero;
                    _dirty = true;
                }
            }
            float sx = x + 3 * (fw + 4f) + 6f, sw = Mathf.Max(120f, w - (sx - x) - 220f);
            var sr = new Rect(sx, y + 1f, sw, 26f);
            string q = GUI.TextField(sr, _search, 40, _searchStyle);
            if (q != _search)
            {
                _search = q;
                _filter.Text = q;
                _listScroll = Vector2.zero;
                _dirty = true;
            }
            if (_search.Length == 0) GUI.Label(new Rect(sr.x + 6f, sr.y, sr.width - 12f, sr.height), _searchHint, _dimLabel);
            GUI.Label(new Rect(sr.xMax + 6f, y, w - (sr.xMax - x) - 6f, BarH), _status, _hintStyle);
            y += BarH + 4f;

            // 3段目：枠・系統
            float sbw = 82f;
            DrawChip(new Rect(x, y, sbw, BarH), 0, _slotLabels, _filter.Slot == null, () => _filter.Slot = null);
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                DrawChip(new Rect(x + (i + 1) * (sbw + 3f), y, sbw, BarH), i + 1, _slotLabels, _filter.Slot == Slots[i], () => _filter.Slot = Slots[k]);
            }
            float lx = x + 7 * (sbw + 3f) + 14f, lw = Mathf.Min(130f, Mathf.Max(70f, (w - (lx - x)) / 4f - 3f));
            DrawChip(new Rect(lx, y, lw, BarH), 0, _lineLabels, _filter.Line == null, () => _filter.Line = null);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                DrawChip(new Rect(lx + (i + 1) * (lw + 3f), y, lw, BarH), i + 1, _lineLabels, _filter.Line == Lines[i], () => _filter.Line = Lines[k]);
            }
            y += BarH + 6f;

            // 下：ヒント（常に同じ高さ）
            float hintH = 36f;
            float bodyH = Mathf.Max(60f, area.yMax - y - hintH - 4f);
            var listRect = new Rect(x, y, ListW, bodyH);
            var detailRect = new Rect(x + ListW + 8f, y, w - ListW - 8f, bodyH);
            DrawList(listRect);
            DrawDetail(detailRect);
            GUI.Label(new Rect(x, area.yMax - hintH, w, hintH), _hint, _hintStyle);
            return back;
        }

        private void DrawChip(Rect r, int index, string[] labels, bool on, Action apply)
        {
            if (!GUI.Button(r, labels[index], on ? _btnSel : _btn) || on) return;
            apply();
            _listScroll = Vector2.zero;
            _dirty = true;
        }

        private void DrawList(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            int n = _res.Items.Count;
            if (n == 0)
            {
                GUI.Label(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, r.height - 16f), _emptyText, _dimLabel);
                return;
            }
            float contentH = n * RowH;
            var view = new Rect(0f, 0f, r.width - 18f, contentH);
            _listScroll = GUI.BeginScrollView(r, _listScroll, view);
            int first = Mathf.Max(0, (int)(_listScroll.y / RowH) - 1);
            int last = Mathf.Min(n - 1, (int)((_listScroll.y + r.height) / RowH) + 1);
            for (int i = first; i <= last; i++)
            {
                var e = _res.Items[i];
                bool found = _res.ItemFound[i];
                var row = new Rect(0f, i * RowH, view.width, RowH - 2f);
                bool selected = ReferenceEquals(e, _sel);
                if (GUI.Button(row, GUIContent.none, selected ? _btnSel : _btn)) Select(e, found);
                DrawIcon(new Rect(row.x + 5f, row.y + (row.height - IconSize) / 2f, IconSize, IconSize), e, found, _rowFrame[i]);
                GUI.Label(new Rect(row.x + IconSize + 12f, row.y, row.width - IconSize - 16f, row.height), _rowText[i], _rowName);
            }
            GUI.EndScrollView();
        }

        private void DrawIcon(Rect r, CodexEntry e, bool found, Color frame)
        {
            var old = GUI.color;
            GUI.color = new Color(0.06f, 0.06f, 0.1f, 0.95f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            bool secret = !found && e.Category != CodexCategory.Bases;
            Texture2D tex = !secret && e.IconBaseId != null ? RelicIcons.For(e.IconBaseId) : null;
            if (tex != null)
            {
                GUI.color = found ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), tex, ScaleMode.ScaleToFit, true);
            }
            GUI.color = frame;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.color = old;
            if (tex == null) GUI.Label(r, secret ? "？" : (e.Category == CodexCategory.Powers ? "✦" : ""), _qMark);
        }

        private void DrawDetail(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            if (_sel == null)
            {
                GUI.Label(new Rect(r.x + 12f, r.y + 10f, r.width - 24f, 60f), _detailBody, _dimLabel);
                return;
            }
            float inner = r.width - 18f;
            if (!Mathf.Approximately(_detailBodyW, inner)) RecalcBody(inner);
            float headH = 104f;
            var view = new Rect(0f, 0f, inner, headH + _detailBodyH + 8f);
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, view);
            var iconRect = new Rect(10f, 10f, 88f, 88f);
            DrawIcon(iconRect, _sel, _selFound, _selFound ? FrameFor(_sel, true) : new Color(0.32f, 0.32f, 0.42f));
            GUI.Label(new Rect(108f, 6f, inner - 116f, 96f), _detailHead, st_Label);
            GUI.Label(new Rect(4f, headH, inner - 8f, _detailBodyH), _detailBody, st_Label);
            GUI.EndScrollView();
        }

        private GUIStyle st_Label;

        private void RecalcBody(float width)
        {
            _detailBodyW = width;
            _tmp.text = _detailBody;
            _detailBodyH = st_Label.CalcHeight(_tmp, width - 8f);
        }

        private void EnsureStyles(UiStyles st)
        {
            if (_btn != null) return;
            _btn = new GUIStyle(st.Button)
            {
                fontSize = 13, padding = new RectOffset(4, 4, 2, 2), margin = new RectOffset(0, 0, 0, 0), wordWrap = false, clipping = TextClipping.Clip,
            };
            _btnSel = new GUIStyle(st.ButtonSel)
            {
                fontSize = 13, padding = new RectOffset(4, 4, 2, 2), margin = new RectOffset(0, 0, 0, 0), wordWrap = false, clipping = TextClipping.Clip,
            };
            _rowName = new GUIStyle(st.Small)
            {
                fontSize = 14, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, richText = true,
            };
            _hintStyle = new GUIStyle(st.Small) { fontSize = 12, wordWrap = true, clipping = TextClipping.Clip };
            _dimLabel = new GUIStyle(st.Small) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.55f, 0.55f, 0.65f) } };
            _qMark = new GUIStyle(st.Header) { alignment = TextAnchor.MiddleCenter, fontSize = 18, normal = { textColor = new Color(0.6f, 0.6f, 0.7f) }, padding = new RectOffset(0, 0, 0, 0) };
            _searchStyle = new GUIStyle(GUI.skin.textField) { font = st.Label.font, fontSize = 15, fixedHeight = 26f, wordWrap = false };
            st_Label = st.Label;
        }

        private void Select(CodexEntry e, bool found)
        {
            _sel = e;
            _selFound = found;
            _detailScroll = Vector2.zero;
            BuildDetail();
        }

        // ───────────────────────── 作り直し ─────────────────────────

        private void Sync(Profile p)
        {
            int stash = p.Stash.Count, lost = p.LostAndFound.Count, sat = p.Run != null ? p.Run.Satchel.Count : 0;
            if (!_dirty && p.Codex.Count == _sigCodex && stash == _sigStash && lost == _sigLost && sat == _sigSatchel && _sigJa == Loc.Japanese) return;
            _dirty = false;
            _sigCodex = p.Codex.Count;
            _sigStash = stash;
            _sigLost = lost;
            _sigSatchel = sat;
            _sigJa = Loc.Japanese;
            _state = new CodexState(p.Codex, CodexQuery.KnownPowers(p));
            _res = CodexQuery.Filter(_state, _filter, _res);
            if (_sel != null) _selFound = _state.IsFound(_sel);
            BuildTexts();
            BuildDetail();
        }

        private static string Meta(CodexEntry e)
        {
            switch (e.Category)
            {
                case CodexCategory.Sets: return Loc.T("セット", "Set");
                case CodexCategory.Powers: return SlotsText(e.SlotMask);
                default:
                    return Content.SlotName(e.Base.Slot) + " · " + Content.LineName(e.Base.Line);
            }
        }

        private static string SlotsText(int mask)
        {
            if (mask == 0x3F) return Loc.T("全部位", "Any slot");
            var sb = new StringBuilder();
            foreach (var s in Slots)
                if ((mask & (1 << (int)s)) != 0)
                {
                    if (sb.Length > 0) sb.Append(Loc.T("・", "/"));
                    sb.Append(Content.SlotName(s));
                }
            return sb.ToString();
        }

        private Color FrameFor(CodexEntry e, bool found)
        {
            string hex;
            if (!found) hex = "#4a4a5c";
            else if (e.Category == CodexCategory.Sets || (e.Unique != null && e.Unique.SetId != null)) hex = Orange;
            else if (e.Category == CodexCategory.Powers) hex = Purple;
            else if (e.Category == CodexCategory.Bases) hex = "#d6d6d6";
            else hex = UiStyles.RarityHex(Rarity.Legendary);
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        private static string NameColor(CodexEntry e)
        {
            if (e.Category == CodexCategory.Sets || (e.Unique != null && e.Unique.SetId != null)) return Orange;
            if (e.Category == CodexCategory.Powers) return Purple;
            if (e.Category == CodexCategory.Bases) return "#d6d6d6";
            return UiStyles.RarityHex(Rarity.Legendary);
        }

        private void BuildTexts()
        {
            _rowText.Clear();
            _rowFrame.Clear();
            for (int i = 0; i < _res.Items.Count; i++)
            {
                var e = _res.Items[i];
                bool found = _res.ItemFound[i];
                string name;
                if (found) name = UiStyles.Colored(e.Name.ToString(), NameColor(e));
                else if (e.Category == CodexCategory.Bases) name = UiStyles.Colored(e.Name.ToString(), "#6a6a7c");
                else name = UiStyles.Colored("？？？", "#5a5a6c");
                _rowText.Add(name + "  " + UiStyles.Colored(Meta(e), Dim));
                _rowFrame.Add(FrameFor(e, found));
            }

            for (int i = 0; i < 4; i++)
                _catLabels[i] = $"{CatName(Cats[i])} {_res.CategoryFound[(int)Cats[i]]}/{_res.CategoryTotal[(int)Cats[i]]}";
            int all = _res.ScopeTotal, got = _res.ScopeFound;
            _foundLabels[0] = Loc.T($"すべて {all}", $"All {all}");
            _foundLabels[1] = Loc.T($"発見済み {got}", $"Found {got}");
            _foundLabels[2] = Loc.T($"未発見 {all - got}", $"Unfound {all - got}");
            int cat = (int)_filter.Category;
            _slotLabels[0] = Loc.T($"全枠 {_res.CategoryFound[cat]}/{_res.CategoryTotal[cat]}", $"All {_res.CategoryFound[cat]}/{_res.CategoryTotal[cat]}");
            for (int i = 0; i < 6; i++)
                _slotLabels[i + 1] = $"{Content.SlotName(Slots[i])} {_res.SlotFound[(int)Slots[i]]}/{_res.SlotTotal[(int)Slots[i]]}";
            _lineLabels[0] = Loc.T("全系統", "All lines");
            for (int i = 0; i < 3; i++)
                _lineLabels[i + 1] = $"{Content.LineName(Lines[i])} {_res.LineFound[(int)Lines[i]]}/{_res.LineTotal[(int)Lines[i]]}";
            _status = Loc.T($"{_res.Items.Count}件を表示（この条件で {got}/{all} 発見）", $"Showing {_res.Items.Count} ({got}/{all} found in this view)");
            _backLabel = Loc.T("← 記録へ", "← Records");
            _searchHint = Loc.T("名前・効果で検索", "Search name / effect");
            _emptyText = Loc.T("条件に合う項目がありません。絞り込みや検索を変えてみてください。", "Nothing matches. Try changing the filters or search.");
            _hint = Loc.T(
                "固有品・セット・固有効果は、見つけるまで名前も効果も伏せられます（？？？。枠と系統だけ見えます）。土台は秘密ではないので、未発見でも名前とアイコンが淡く見えます。文字検索の対象は、見つけた物と土台の名前・効果です。",
                "Legendaries, sets and powers keep their name and effects hidden until you find them (???; only slot and line show). Bases are not secret, so unfound ones still show a dim name and icon. Search covers found entries and base names.");

            int bf = _res.CategoryFound[(int)CodexCategory.Bases], bt = _res.CategoryTotal[(int)CodexCategory.Bases];
            int uf = _res.CategoryFound[(int)CodexCategory.Uniques], ut = _res.CategoryTotal[(int)CodexCategory.Uniques];
            int sf = _res.CategoryFound[(int)CodexCategory.Sets], stt = _res.CategoryTotal[(int)CodexCategory.Sets];
            int pf = _res.CategoryFound[(int)CodexCategory.Powers], pt = _res.CategoryTotal[(int)CodexCategory.Powers];
            _summary = Loc.T(
                $"土台 {bf}/{bt}　固有品 {uf}/{ut}　セット {sf}/{stt}　固有効果 {pf}/{pt}",
                $"Bases {bf}/{bt}   Legendaries {uf}/{ut}   Sets {sf}/{stt}   Powers {pf}/{pt}");
        }

        private void BuildDetail()
        {
            if (_sel == null)
            {
                _detailHead = "";
                _detailBody = Loc.T("左の一覧から項目を選ぶと、ここに詳しく出ます。", "Pick an entry on the left to see its details here.");
                _detailBodyW = -1;
                return;
            }
            var e = _sel;
            var sb = _sb;
            sb.Length = 0;
            bool secret = !_selFound && e.Category != CodexCategory.Bases;
            string head;
            if (secret)
            {
                head = "<size=19><b>？？？</b></size>\n" + UiStyles.Colored(CatName(e.Category) + " · " + (e.Category == CodexCategory.Powers ? SlotsText(e.SlotMask) : Meta(e)), Dim)
                    + "\n" + UiStyles.Colored(Loc.T("まだ見つけていません", "Not found yet"), "#aa8866");
                sb.Append(Loc.T("手に入れると、名前・効果・詳しい説明がここに載ります。", "Once you obtain it, its name, effects and details are recorded here."));
            }
            else
            {
                head = "<size=19><b>" + UiStyles.Colored(e.Name.ToString(), NameColor(e)) + "</b></size>\n"
                    + UiStyles.Colored(Loc.Japanese ? e.Name.En : e.Name.Ja, Dim) + "\n"
                    + UiStyles.Colored(CatName(e.Category) + " · " + Meta(e), Dim)
                    + (_selFound ? "" : "\n" + UiStyles.Colored(Loc.T("未発見（土台の情報は公開されています）", "Not found yet (base info is public)"), "#aa8866"));
                switch (e.Category)
                {
                    case CodexCategory.Bases: AppendBase(sb, e); break;
                    case CodexCategory.Uniques: AppendUnique(sb, e); break;
                    case CodexCategory.Sets: AppendSet(sb, e); break;
                    default: AppendPower(sb, e); break;
                }
            }
            _detailHead = head;
            _detailBody = sb.ToString();
            _detailBodyW = -1;
        }

        private void AppendImplicit(StringBuilder sb, BaseDef b)
        {
            sb.Append(UiStyles.Colored(Content.FormatStat(b.ImplicitStat, b.ImplicitValue), "#c8c8ff"))
              .Append(UiStyles.Colored(Loc.T("  （この土台が必ず持つ性能）", "  (always on this base)"), Dim)).Append('\n');
        }

        private void AppendBase(StringBuilder sb, CodexEntry e)
        {
            AppendImplicit(sb, e.Base);
            int total = 0, found = 0;
            foreach (var u in Content.Uniques)
            {
                if (u.BaseId != e.Id) continue;
                total++;
                if (_state.Codex.Contains(u.Id)) found++;
            }
            sb.Append('\n').Append(Loc.T(
                "特性：入手のたびに抽選されます。",
                "Affixes are rolled each time you obtain one.")).Append('\n');
            if (total > 0)
                sb.Append('\n').Append(Loc.T($"この土台の固有品：{found}/{total} 発見", $"Legendaries on this base: {found}/{total} found"));
        }

        private void AppendUnique(StringBuilder sb, CodexEntry e)
        {
            var u = e.Unique;
            if (e.Base != null)
            {
                sb.Append(Loc.T("土台：", "Base: ")).Append(e.Base.Name.ToString()).Append('\n');
                AppendImplicit(sb, e.Base);
            }
            foreach (var pw in u.Powers) sb.Append(UiStyles.Colored(Content.FormatPower(pw.Power, pw.Value), Purple)).Append('\n');
            if (u.Link != null) sb.Append(UiStyles.Colored(Links.Describe(u.Link), Cyan)).Append('\n');
            sb.Append(Loc.T("特性：入手のたびに3つ抽選されます。", "Affixes: 3 are rolled each time you obtain one.")).Append('\n');
            sb.Append(UiStyles.Colored(Loc.T(
                $"覚醒：装着して敵を倒すと溜まり、{Content.AwakenThresholdFor(1)}・{Content.AwakenThresholdFor(2)}・{Content.AwakenThresholdFor(3)}で覚醒Ⅰ・Ⅱ・Ⅲ（固有効果 最大{Content.AwakenPowerPctAt(Content.MaxAwakenLevel) / 100f:0.##}倍）。",
                $"Awakening: gather power while worn; at {Content.AwakenThresholdFor(1)}, {Content.AwakenThresholdFor(2)} and {Content.AwakenThresholdFor(3)} it reaches I, II and III (powers up to x{Content.AwakenPowerPctAt(Content.MaxAwakenLevel) / 100f:0.##})."), Dim)).Append('\n');
            if (u.SetId != null && Content.GetSet(u.SetId) is SetDef set)
            {
                sb.Append('\n').Append(UiStyles.Colored($"《{set.Name}》", Orange)).Append('\n');
                AppendSetBody(sb, set, u.Id);
            }
            else if (!string.IsNullOrEmpty(u.Lore.ToString()))
                sb.Append('\n').Append("<i>").Append(UiStyles.Colored(u.Lore.ToString(), "#c9a86a")).Append("</i>");
        }

        private void AppendSet(StringBuilder sb, CodexEntry e)
        {
            AppendSetBody(sb, e.Set, null);
        }

        private void AppendSetBody(StringBuilder sb, SetDef set, string currentPieceId)
        {
            sb.Append(UiStyles.Colored(set.Describe(), "#c8c8e0")).Append('\n').Append('\n');
            CodexEntry setEntry = null;
            foreach (var se in CodexQuery.Entries(CodexCategory.Sets)) if (se.Set == set) { setEntry = se; break; }
            if (setEntry == null) return;
            int have = 0;
            foreach (var pc in setEntry.Pieces) if (_state.Codex.Contains(pc.Id)) have++;
            sb.Append(Loc.T($"部位（見つけた数 {have}/{setEntry.Pieces.Count}）", $"Pieces ({have}/{setEntry.Pieces.Count} found)")).Append('\n');
            foreach (var pc in setEntry.Pieces)
            {
                string slotLine = "";
                if (Content.TryGetBase(pc.BaseId, out var pb)) slotLine = Content.SlotName(pb.Slot) + " · " + Content.LineName(pb.Line);
                bool got = _state.Codex.Contains(pc.Id);
                sb.Append(got ? "◆ " : "◇ ");
                sb.Append(got ? UiStyles.Colored(pc.Name.ToString(), Orange) : UiStyles.Colored("？？？", "#6a6a7c"));
                sb.Append("  ").Append(UiStyles.Colored(slotLine, Dim));
                if (pc.Id == currentPieceId) sb.Append(UiStyles.Colored(Loc.T("  ←この装備", "  <- this one"), Dim));
                sb.Append('\n');
            }
        }

        private void AppendPower(StringBuilder sb, CodexEntry e)
        {
            if (CodexQuery.TryPowerRange(e.Power, out int min, out int max))
            {
                if (min == max) sb.Append(UiStyles.Colored(Content.FormatPower(e.Power, max), Purple)).Append('\n');
                else
                {
                    sb.Append(UiStyles.Colored(Content.FormatPower(e.Power, min), Purple)).Append(UiStyles.Colored(Loc.T("  （最小）", "  (min)"), Dim)).Append('\n');
                    sb.Append(UiStyles.Colored(Content.FormatPower(e.Power, max), Purple)).Append(UiStyles.Colored(Loc.T("  （最大）", "  (max)"), Dim)).Append('\n');
                }
            }
            sb.Append('\n').Append(Loc.T("付く部位：", "Found on: ")).Append(SlotsText(e.SlotMask)).Append('\n');
            var names = new List<string>();
            foreach (var u in e.Carriers)
                if (_state.Codex.Contains(u.Id)) names.Add(u.Name.ToString());
            foreach (var s in e.CarrierSets)
                foreach (var se in CodexQuery.Entries(CodexCategory.Sets))
                    if (se.Set == s && _state.IsFound(se)) names.Add("《" + s.Name + "》");
            if (names.Count > 0)
            {
                sb.Append('\n').Append(Loc.T("持っている発見済みの装備：", "Found gear carrying it: ")).Append('\n');
                int shown = Math.Min(names.Count, 12);
                for (int i = 0; i < shown; i++) sb.Append("・").Append(names[i]).Append('\n');
                if (names.Count > shown) sb.Append(Loc.T($"…ほか{names.Count - shown}件", $"...and {names.Count - shown} more")).Append('\n');
            }
        }
    }
}
