using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Line = SodRpg.Core.Game.Line;
    using Power = SodRpg.Core.Game.Power;
    using Rarity = SodRpg.Core.Game.Rarity;
    using Slot = SodRpg.Core.Game.Slot;

    /// <summary>
    /// 図鑑（記録タブから開く）。土台・固有品・セット・固有効果・銘品・組を、絞り込みと文字検索つきで一覧し、右に詳細を出す。
    /// 性能：一覧と詳細の文字列は、絞り込み・検索・選択・図鑑の件数・言語が変わったときだけ作り直す。
    /// 描画は見えている行だけを、固定の行高で手動に描く（GUILayout は場所の確保にしか使わない）。
    /// 見つけていない固有品・セット・固有効果・銘品・組は名前も効果も出さない（？？？）。土台は秘密ではないので名前とアイコンを淡く出す。
    /// 文字列づくりは Unity に依存しない CodexPresenter に置く（試験から呼べる）。
    /// </summary>
    internal sealed class CodexView
    {
        private const float RowH = 40f, IconSize = 32f, ListW = 400f, BarH = 28f;

        private readonly CodexFilter _filter = new CodexFilter { Category = CodexCategory.Uniques };
        private CodexResult _res = new CodexResult();
        private CodexState _state;
        private string _search = "";
        private bool _dirty = true;

        /// <summary>プロフィールの切り替えなどで、一覧と詳細を作り直させる。</summary>
        public void Invalidate() => _dirty = true;
        private int _sigCodex = -1, _sigStash = -1, _sigLost = -1, _sigSatchel = -1;
        private bool _sigJa;

        private readonly List<string> _rowText = new List<string>();
        private readonly List<Color> _rowFrame = new List<Color>();
        private readonly string[] _catLabels = new string[CodexPresenter.Categories.Length];
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
        private readonly GUIContent _tmp = new GUIContent();

        private static readonly CodexCategory[] Cats = CodexPresenter.Categories;
        private static readonly Slot[] Slots = { Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm };
        private static readonly Line[] Lines = { Line.Offense, Line.Guard, Line.Resonance };

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
            float cx = x + 130f, cw = Mathf.Max(80f, (w - 130f) / Cats.Length - 4f);
            for (int i = 0; i < Cats.Length; i++)
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
            Texture2D tex = !secret && e.IconBaseId != null ? RelicIcons.For(e.Unique != null ? e.Unique.Id : null, e.IconBaseId) : null;
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

        private static Color FrameFor(CodexEntry e, bool found)
        {
            ColorUtility.TryParseHtmlString(CodexPresenter.FrameHex(e, found), out var c);
            return c;
        }

        private void BuildTexts()
        {
            _rowText.Clear();
            _rowFrame.Clear();
            for (int i = 0; i < _res.Items.Count; i++)
            {
                var e = _res.Items[i];
                bool found = _res.ItemFound[i];
                _rowText.Add(CodexPresenter.RowText(e, found));
                _rowFrame.Add(FrameFor(e, found));
            }

            for (int i = 0; i < Cats.Length; i++)
                _catLabels[i] = CodexPresenter.CategoryLabel(_res, Cats[i]);
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
                "固有品・セット・固有効果・銘品・組は、見つけるまで名前も効果も伏せられます（？？？。枠と系統だけ見えます）。土台は秘密ではないので、未発見でも名前とアイコンが淡く見えます。文字検索の対象は、見つけた物と土台の名前・効果です。",
                "Legendaries, sets, powers, named items and mini sets keep their name and effects hidden until you find them (???; only slot and line show). Bases are not secret, so unfound ones still show a dim name and icon. Search covers found entries and base names and effects.");

            _summary = CodexPresenter.Summary(_res);
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
            CodexPresenter.Detail(_sel, _selFound, _state, out _detailHead, out _detailBody);
            _detailBodyW = -1;
        }
    }
}
