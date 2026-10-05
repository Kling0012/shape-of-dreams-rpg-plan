using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum CodexCategory { Bases = 0, Uniques = 1, Sets = 2, Powers = 3, Named = 4, MiniSets = 5 }

    public enum CodexFoundFilter { All = 0, Found = 1, Unfound = 2 }

    /// <summary>図鑑の1項目（読み取り専用）。土台・固有品・セット・固有効果のいずれか。</summary>
    public sealed class CodexEntry
    {
        public CodexCategory Category { get; internal set; }
        /// <summary>土台・固有品・セットはそのID。固有効果は Power の名前。</summary>
        public string Id { get; internal set; }
        public Txt Name { get; internal set; }
        /// <summary>この項目が関わる枠（1 &lt;&lt; (int)Slot のビット和）。</summary>
        public int SlotMask { get; internal set; }
        /// <summary>この項目が関わる系統（1 &lt;&lt; (int)Line のビット和）。</summary>
        public int LineMask { get; internal set; }
        /// <summary>アイコンに使う土台ID。固有効果は null。</summary>
        public string IconBaseId { get; internal set; }
        public BaseDef Base { get; internal set; }
        public UniqueDef Unique { get; internal set; }
        public SetDef Set { get; internal set; }
        public Power Power { get; internal set; }
        /// <summary>銘品の項目だけ（v1.32）。</summary>
        public NamedDef Named { get; internal set; }
        /// <summary>組（小セット）の項目だけ（v1.32）。</summary>
        public MiniSetDef MiniSet { get; internal set; }
        /// <summary>組の部位（銘品）。</summary>
        public IReadOnlyList<NamedDef> NamedPieces { get; internal set; }
        /// <summary>セットの部位（固有品ID）。</summary>
        public IReadOnlyList<UniqueDef> Pieces { get; internal set; }
        /// <summary>固有効果を固定で持つ固有品・セット（固有効果の項目だけ）。</summary>
        public IReadOnlyList<UniqueDef> Carriers { get; internal set; }
        public IReadOnlyList<SetDef> CarrierSets { get; internal set; }

        private string _search;
        private bool _searchJa;

        /// <summary>検索用の小文字化した文字列（名前の日英と、いまの言語の効果文）。言語が変わったら作り直す。</summary>
        public string SearchText
        {
            get
            {
                if (_search != null && _searchJa == Loc.Japanese) return _search;
                _searchJa = Loc.Japanese;
                _search = CodexQuery.BuildSearch(this).ToLowerInvariant();
                return _search;
            }
        }

        public bool HasSlot(Slot s) => (SlotMask & (1 << (int)s)) != 0;
        public bool HasLine(Line l) => (LineMask & (1 << (int)l)) != 0;
    }

    /// <summary>図鑑の「見つけた」状態。Profile.Codex と、持っている遺物に付いた固有効果から作る。</summary>
    public sealed class CodexState
    {
        public CodexState(ISet<string> codex, ISet<Power> knownPowers)
        {
            Codex = codex;
            KnownPowers = knownPowers;
        }

        public ISet<string> Codex { get; }
        public ISet<Power> KnownPowers { get; }

        public bool IsFound(CodexEntry e)
        {
            switch (e.Category)
            {
                case CodexCategory.Sets:
                    foreach (var p in e.Pieces)
                        if (Codex.Contains(p.Id)) return true;
                    return false;
                case CodexCategory.MiniSets:
                    foreach (var n in e.NamedPieces)
                        if (Codex.Contains(NamedItems.CodexId(n.Id))) return true;
                    return false;
                case CodexCategory.Powers:
                    return KnownPowers.Contains(e.Power);
                default:
                    return Codex.Contains(e.Id);
            }
        }
    }

    public sealed class CodexFilter
    {
        public CodexCategory Category;
        public Slot? Slot;
        public Line? Line;
        public CodexFoundFilter Found;
        public string Text = "";
    }

    public sealed class CodexResult
    {
        public List<CodexEntry> Items = new List<CodexEntry>();
        /// <summary>各項目が見つかっているか（Items と同じ並び）。</summary>
        public List<bool> ItemFound = new List<bool>();
        /// <summary>カテゴリごとの 見つけた数 / 全体。</summary>
        public int[] CategoryFound = new int[6];
        public int[] CategoryTotal = new int[6];
        /// <summary>選んだカテゴリの中での 枠・系統ごとの 見つけた数 / 全体。</summary>
        public int[] SlotFound = new int[6];
        public int[] SlotTotal = new int[6];
        public int[] LineFound = new int[3];
        public int[] LineTotal = new int[3];
        /// <summary>枠・系統の絞り込みまで当てはめた範囲（見つけた／検索の前）の 見つけた数 / 全体。</summary>
        public int ScopeFound, ScopeTotal;
    }

    public static class CodexQuery
    {
        private static IReadOnlyList<CodexEntry>[] _all;

        public static IReadOnlyList<CodexEntry> Entries(CodexCategory c)
        {
            var all = _all;
            if (all == null)
            {
                all = BuildAll();
                _all = all;
            }
            return all[(int)c];
        }

        /// <summary>登録簿（銘品・組）が変わったら作り直す（試験用登録から呼ぶ）。</summary>
        internal static void InvalidateCache() => _all = null;

        private static IReadOnlyList<CodexEntry>[] BuildAll()
        {
            var bases = new List<CodexEntry>();
            foreach (var b in Content.Bases)
                bases.Add(new CodexEntry
                {
                    Category = CodexCategory.Bases, Id = b.Id, Name = b.Name, Base = b, IconBaseId = b.Id,
                    SlotMask = 1 << (int)b.Slot, LineMask = 1 << (int)b.Line,
                });

            var uniques = new List<CodexEntry>();
            var setPieces = new Dictionary<string, List<UniqueDef>>(StringComparer.Ordinal);
            var carriers = new Dictionary<Power, List<UniqueDef>>();
            foreach (var u in Content.Uniques)
            {
                int slot = 0, line = 0;
                if (Content.TryGetBase(u.BaseId, out var b))
                {
                    slot = 1 << (int)b.Slot;
                    line = 1 << (int)b.Line;
                }
                uniques.Add(new CodexEntry
                {
                    Category = CodexCategory.Uniques, Id = u.Id, Name = u.Name, Unique = u, Base = b, IconBaseId = u.BaseId,
                    SlotMask = slot, LineMask = line,
                });
                if (u.SetId != null)
                {
                    if (!setPieces.TryGetValue(u.SetId, out var list)) setPieces[u.SetId] = list = new List<UniqueDef>();
                    list.Add(u);
                }
                foreach (var pw in u.Powers)
                {
                    if (!carriers.TryGetValue(pw.Power, out var list)) carriers[pw.Power] = list = new List<UniqueDef>();
                    list.Add(u);
                }
            }

            var sets = new List<CodexEntry>();
            var setCarriers = new Dictionary<Power, List<SetDef>>();
            foreach (var s in Content.Sets)
            {
                setPieces.TryGetValue(s.Id, out var pieces);
                pieces = pieces ?? new List<UniqueDef>();
                int slot = 0, line = 0;
                string icon = null;
                foreach (var pc in pieces)
                    if (Content.TryGetBase(pc.BaseId, out var pb))
                    {
                        slot |= 1 << (int)pb.Slot;
                        line |= 1 << (int)pb.Line;
                        if (icon == null) icon = pb.Id;
                    }
                sets.Add(new CodexEntry
                {
                    Category = CodexCategory.Sets, Id = s.Id, Name = s.Name, Set = s, Pieces = pieces, IconBaseId = icon,
                    SlotMask = slot, LineMask = line,
                });
                foreach (var pw in s.ThreePiece)
                {
                    if (!setCarriers.TryGetValue(pw.Power, out var list)) setCarriers[pw.Power] = list = new List<SetDef>();
                    list.Add(s);
                }
            }

            var powerSlots = new Dictionary<Power, int>();
            foreach (Slot sl in Enum.GetValues(typeof(Slot)))
                foreach (var r in Content.PowerPool(sl))
                    powerSlots[r.Power] = (powerSlots.TryGetValue(r.Power, out int m) ? m : 0) | (1 << (int)sl);
            var powers = new List<CodexEntry>();
            const int AllLines = 7;
            foreach (Power p in Enum.GetValues(typeof(Power)))
            {
                if (p == Power.None || Content.PowerName(p) == "-") continue;
                powerSlots.TryGetValue(p, out int mask);
                carriers.TryGetValue(p, out var cu);
                setCarriers.TryGetValue(p, out var cs);
                if (cu != null)
                    foreach (var u in cu)
                        if (Content.TryGetBase(u.BaseId, out var ub)) mask |= 1 << (int)ub.Slot;
                if (mask == 0 && cu == null && cs == null) continue; // どこにも付かない効果は載せない
                powers.Add(new CodexEntry
                {
                    Category = CodexCategory.Powers, Id = p.ToString(), Power = p, SlotMask = mask, LineMask = AllLines,
                    Name = new Txt(PowerNameIn(p, true), PowerNameIn(p, false)),
                    Carriers = (IReadOnlyList<UniqueDef>)cu ?? Array.Empty<UniqueDef>(),
                    CarrierSets = (IReadOnlyList<SetDef>)cs ?? Array.Empty<SetDef>(),
                });
            }
            // 銘品と組（v1.32）。登録簿が空の間は項目が1つもない。
            var namedEntries = new List<CodexEntry>();
            foreach (var n in NamedItems.All)
            {
                int slot = 0, line = 0;
                BaseDef b = null;
                if (Content.TryGetBase(n.BaseId, out b))
                {
                    slot = 1 << (int)b.Slot;
                    line = 1 << (int)b.Line;
                }
                namedEntries.Add(new CodexEntry
                {
                    Category = CodexCategory.Named, Id = NamedItems.CodexId(n.Id), Name = n.Name, Named = n, Base = b,
                    IconBaseId = n.BaseId, SlotMask = slot, LineMask = line,
                });
            }

            var miniSetEntries = new List<CodexEntry>();
            foreach (var s in NamedItems.MiniSets)
            {
                var pieces = new List<NamedDef>();
                int slot = 0, line = 0;
                string icon = null;
                foreach (var pieceId in s.PieceIds)
                {
                    if (!NamedItems.TryGetNamed(pieceId, out var piece)) continue;
                    pieces.Add(piece);
                    if (Content.TryGetBase(piece.BaseId, out var pb))
                    {
                        slot |= 1 << (int)pb.Slot;
                        line |= 1 << (int)pb.Line;
                        if (icon == null) icon = pb.Id;
                    }
                }
                miniSetEntries.Add(new CodexEntry
                {
                    Category = CodexCategory.MiniSets, Id = s.Id, Name = s.Name, MiniSet = s, NamedPieces = pieces,
                    IconBaseId = icon, SlotMask = slot, LineMask = line,
                });
            }
            return new IReadOnlyList<CodexEntry>[] { bases, uniques, sets, powers, namedEntries, miniSetEntries };
        }

        private static string PowerNameIn(Power p, bool ja)
        {
            bool old = Loc.Japanese;
            Loc.Japanese = ja;
            try { return Content.PowerName(p); }
            finally { Loc.Japanese = old; }
        }

        /// <summary>固有効果の、見本として見せる値の範囲。落ちる効果は装備の値域、固有品だけの効果はその固定値。</summary>
        public static bool TryPowerRange(Power p, out int min, out int max)
        {
            min = int.MaxValue;
            max = int.MinValue;
            foreach (Slot sl in Enum.GetValues(typeof(Slot)))
                foreach (var r in Content.PowerPool(sl))
                    if (r.Power == p)
                    {
                        min = Math.Min(min, r.Min);
                        max = Math.Max(max, r.Max);
                    }
            if (max >= min) return true;
            foreach (var e in Entries(CodexCategory.Powers))
            {
                if (e.Power != p) continue;
                foreach (var u in e.Carriers)
                    foreach (var pl in u.Powers)
                        if (pl.Power == p)
                        {
                            min = Math.Min(min, pl.Value);
                            max = Math.Max(max, pl.Value);
                        }
                foreach (var s in e.CarrierSets)
                    foreach (var pl in s.ThreePiece)
                        if (pl.Power == p)
                        {
                            min = Math.Min(min, pl.Value);
                            max = Math.Max(max, pl.Value);
                        }
            }
            return max >= min;
        }

        /// <summary>持っている遺物と図鑑に載った固有品から、見つけた固有効果の集合を作る。</summary>
        public static HashSet<Power> KnownPowers(Profile p)
        {
            var set = new HashSet<Power>();
            void AddRelic(Relic r)
            {
                if (r == null) return;
                foreach (var pw in r.Powers) set.Add(pw.Power);
            }
            foreach (var r in p.Stash) AddRelic(r);
            foreach (var r in p.LostAndFound) AddRelic(r);
            if (p.Run != null) foreach (var r in p.Run.Satchel) AddRelic(r);
            foreach (var u in Content.Uniques)
            {
                if (!p.Codex.Contains(u.Id)) continue;
                foreach (var pw in u.Powers) set.Add(pw.Power);
            }
            foreach (var e in Entries(CodexCategory.Sets))
            {
                bool any = false;
                foreach (var pc in e.Pieces)
                    if (p.Codex.Contains(pc.Id)) { any = true; break; }
                if (!any) continue;
                foreach (var pw in e.Set.ThreePiece) set.Add(pw.Power);
            }
            foreach (var n in NamedItems.All)
            {
                if (!p.Codex.Contains(NamedItems.CodexId(n.Id))) continue;
                foreach (var pw in n.Powers) set.Add(pw.Power);
            }
            foreach (var e in Entries(CodexCategory.MiniSets))
            {
                if (e.MiniSet.ThreePiece == null) continue;
                bool any = false;
                foreach (var pc in e.NamedPieces)
                    if (p.Codex.Contains(NamedItems.CodexId(pc.Id))) { any = true; break; }
                if (!any) continue;
                set.Add(e.MiniSet.ThreePiece.Power);
            }
            return set;
        }

        /// <summary>
        /// 検索の対象文字列。名前（日英）と効果の文を含める。見つけていない固有品・セット・固有効果は
        /// 名前も効果も隠すので、Filter 側で対象から外す。
        /// </summary>
        internal static string BuildSearch(CodexEntry e)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(e.Name.Ja).Append(' ').Append(e.Name.En).Append(' ');
            switch (e.Category)
            {
                case CodexCategory.Bases:
                    sb.Append(Content.FormatStat(e.Base.ImplicitStat, e.Base.ImplicitValue));
                    break;
                case CodexCategory.Uniques:
                    if (e.Base != null) sb.Append(e.Base.Name.Ja).Append(' ').Append(e.Base.Name.En).Append(' ');
                    foreach (var pw in e.Unique.Powers) sb.Append(Content.FormatPower(pw.Power, pw.Value)).Append(' ');
                    if (BossProfiles.TryGetMove(e.Unique.BossMove, out var bossMove))
                        sb.Append(bossMove.Description.Ja).Append(' ').Append(bossMove.Description.En).Append(' ');
                    if (e.Unique.Link != null) sb.Append(Links.Describe(e.Unique.Link)).Append(' ');
                    sb.Append(e.Unique.Lore.Ja).Append(' ').Append(e.Unique.Lore.En);
                    break;
                case CodexCategory.Sets:
                    sb.Append(e.Set.Describe());
                    break;
                case CodexCategory.Named:
                    if (e.Base != null) sb.Append(e.Base.Name.Ja).Append(' ').Append(e.Base.Name.En).Append(' ');
                    foreach (var pw in e.Named.Powers) sb.Append(Content.FormatPower(pw.Power, pw.Value)).Append(' ');
                    if (e.Named.MiniSetId != null && NamedItems.TryGetMiniSet(e.Named.MiniSetId, out var ms))
                        sb.Append(ms.Name.Ja).Append(' ').Append(ms.Name.En).Append(' ');
                    sb.Append(e.Named.Lore.Ja).Append(' ').Append(e.Named.Lore.En);
                    break;
                case CodexCategory.MiniSets:
                    sb.Append(e.MiniSet.Describe());
                    break;
                default:
                    if (TryPowerRange(e.Power, out int min, out int max))
                        sb.Append(Content.FormatPower(e.Power, max));
                    break;
            }
            return sb.ToString();
        }

        /// <summary>
        /// 絞り込み。見つけていない固有品・セット・固有効果は、名前も効果も隠すので文字検索にはかからない
        /// （土台の名前は隠さないので検索できる）。
        /// </summary>
        public static CodexResult Filter(CodexState state, CodexFilter f, CodexResult reuse = null)
        {
            var res = reuse ?? new CodexResult();
            res.Items.Clear();
            res.ItemFound.Clear();
            Array.Clear(res.CategoryFound, 0, 6);
            Array.Clear(res.CategoryTotal, 0, 6);
            Array.Clear(res.SlotFound, 0, 6);
            Array.Clear(res.SlotTotal, 0, 6);
            Array.Clear(res.LineFound, 0, 3);
            Array.Clear(res.LineTotal, 0, 3);
            res.ScopeFound = res.ScopeTotal = 0;
            string text = (f.Text ?? "").Trim().ToLowerInvariant();

            for (int c = 0; c < 6; c++)
            {
                bool selected = c == (int)f.Category;
                foreach (var e in Entries((CodexCategory)c))
                {
                    bool found = state.IsFound(e);
                    res.CategoryTotal[c]++;
                    if (found) res.CategoryFound[c]++;
                    if (!selected) continue;
                    for (int s = 0; s < 6; s++)
                        if ((e.SlotMask & (1 << s)) != 0) { res.SlotTotal[s]++; if (found) res.SlotFound[s]++; }
                    for (int l = 0; l < 3; l++)
                        if ((e.LineMask & (1 << l)) != 0) { res.LineTotal[l]++; if (found) res.LineFound[l]++; }
                    if (f.Slot.HasValue && !e.HasSlot(f.Slot.Value)) continue;
                    if (f.Line.HasValue && !e.HasLine(f.Line.Value)) continue;
                    res.ScopeTotal++;
                    if (found) res.ScopeFound++;
                    if (f.Found == CodexFoundFilter.Found && !found) continue;
                    if (f.Found == CodexFoundFilter.Unfound && found) continue;
                    if (text.Length > 0)
                    {
                        bool searchable = found || e.Category == CodexCategory.Bases;
                        if (!searchable || e.SearchText.IndexOf(text, StringComparison.Ordinal) < 0) continue;
                    }
                    res.Items.Add(e);
                    res.ItemFound.Add(found);
                }
            }
            return res;
        }
    }
}
