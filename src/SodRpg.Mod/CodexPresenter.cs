using System;
using System.Text;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>
    /// 図鑑の分類・文字列づくり。画面（CodexView）から Unity に依存しない部分だけを切り出したもので、試験から直接呼べる。
    /// 見つけていない固有品・セット・固有効果・銘品・組は、名前も効果も一言も出さない（？？？）。土台だけは秘密ではない。
    /// </summary>
    internal static class CodexPresenter
    {
        public const string Gold = "#ffd24a", Orange = "#ff8a3d", Purple = "#e0b0ff", Cyan = "#7fd8ff", Dim = "#8a8aa0";

        /// <summary>画面に出す分類（並び順＝ボタンの並び）。Core の分類 6 つすべてを並べる。</summary>
        public static readonly CodexCategory[] Categories =
        {
            CodexCategory.Uniques, CodexCategory.Bases, CodexCategory.Sets, CodexCategory.Powers, CodexCategory.Named, CodexCategory.MiniSets,
        };

        private static readonly Slot[] Slots = { Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm };

        private static string Colored(string text, string hex) => "<color=" + hex + ">" + text + "</color>";

        // UiStyles.RarityHex と同じ配色（UiStyles は Unity に依存するので、ここには写して持つ）。
        private static string RarityHex(SodRpg.Core.Game.Rarity r)
        {
            switch (r)
            {
                case SodRpg.Core.Game.Rarity.Common: return "#d6d6d6";
                case SodRpg.Core.Game.Rarity.Uncommon: return "#62d962";
                case SodRpg.Core.Game.Rarity.Rare: return "#4fa8ff";
                case SodRpg.Core.Game.Rarity.Epic: return "#c475ff";
                default: return Gold;
            }
        }

        public static string CatName(CodexCategory c)
        {
            switch (c)
            {
                case CodexCategory.Bases: return Loc.T("土台", "Bases");
                case CodexCategory.Uniques: return Loc.T("固有品", "Legendaries");
                case CodexCategory.Sets: return Loc.T("セット", "Sets");
                case CodexCategory.Named: return Loc.T("銘品", "Named");
                case CodexCategory.MiniSets: return Loc.T("組", "Mini sets");
                default: return Loc.T("固有効果", "Powers");
            }
        }

        /// <summary>カテゴリボタンの文字（例「銘品 3/360」）。</summary>
        public static string CategoryLabel(CodexResult res, CodexCategory c) =>
            $"{CatName(c)} {res.CategoryFound[(int)c]}/{res.CategoryTotal[(int)c]}";

        /// <summary>記録タブに出す要約（見つけた数）。2行に分ける。</summary>
        public static string Summary(CodexResult res)
        {
            string Part(CodexCategory c) => CatName(c) + " " + res.CategoryFound[(int)c] + "/" + res.CategoryTotal[(int)c];
            string sep = Loc.T("　", "   ");
            return Part(CodexCategory.Bases) + sep + Part(CodexCategory.Uniques) + sep + Part(CodexCategory.Sets) + sep + Part(CodexCategory.Powers)
                + "\n" + Part(CodexCategory.Named) + sep + Part(CodexCategory.MiniSets);
        }

        public static string Meta(CodexEntry e)
        {
            switch (e.Category)
            {
                case CodexCategory.Sets: return Loc.T("セット", "Set");
                case CodexCategory.MiniSets: return Loc.T("組", "Mini set");
                case CodexCategory.Powers: return SlotsText(e.SlotMask);
                default:
                    if (e.Base == null) return "";
                    return Content.SlotName(e.Base.Slot) + " · " + Content.LineName(e.Base.Line)
                        + (FamilyPrefs.Label(e.Base.Family) is string familyLabel ? " · " + familyLabel : "");
            }
        }

        public static string SlotsText(int mask)
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

        /// <summary>枠の色（#rrggbb）。見つけていなければ灰色。</summary>
        public static string FrameHex(CodexEntry e, bool found)
        {
            return found ? NameColor(e) : "#4a4a5c";
        }

        public static string NameColor(CodexEntry e)
        {
            if (e.Category == CodexCategory.Sets || (e.Unique != null && e.Unique.SetId != null)) return Orange;
            if (e.Category == CodexCategory.MiniSets) return Cyan;
            if (e.Category == CodexCategory.Named) return RarityHex(e.Named.Rarity);
            if (e.Category == CodexCategory.Powers) return Purple;
            if (e.Category == CodexCategory.Bases) return "#d6d6d6";
            return RarityHex(SodRpg.Core.Game.Rarity.Legendary);
        }

        /// <summary>一覧の1行ぶんの文字。見つけていないものは「？？？」（土台だけ名前を淡く出す）。</summary>
        public static string RowText(CodexEntry e, bool found)
        {
            string name;
            if (found) name = Colored(e.Name.ToString(), NameColor(e));
            else if (e.Category == CodexCategory.Bases) name = Colored(e.Name.ToString(), "#6a6a7c");
            else name = Colored("？？？", "#5a5a6c");
            return name + "  " + Colored(Meta(e), Dim);
        }

        /// <summary>詳細の見出しと本文を作る。</summary>
        public static void Detail(CodexEntry e, bool found, CodexState state, out string head, out string body)
        {
            var sb = new StringBuilder();
            bool secret = !found && e.Category != CodexCategory.Bases;
            if (secret)
            {
                head = "<size=19><b>？？？</b></size>\n" + Colored(CatName(e.Category) + " · " + Meta(e), Dim)
                    + "\n" + Colored(Loc.T("まだ見つけていません", "Not found yet"), "#aa8866");
                sb.Append(Loc.T("手に入れると、名前・効果・詳しい説明がここに載ります。", "Once you obtain it, its name, effects and details are recorded here."));
            }
            else
            {
                head = "<size=19><b>" + Colored(e.Name.ToString(), NameColor(e)) + "</b></size>\n"
                    + Colored(Loc.Japanese ? e.Name.En : e.Name.Ja, Dim) + "\n"
                    + Colored(CatName(e.Category) + " · " + Meta(e), Dim)
                    + (found ? "" : "\n" + Colored(Loc.T("未発見（土台の情報は公開されています）", "Not found yet (base info is public)"), "#aa8866"));
                switch (e.Category)
                {
                    case CodexCategory.Bases: AppendBase(sb, e, state); break;
                    case CodexCategory.Uniques: AppendUnique(sb, e, state); break;
                    case CodexCategory.Sets: AppendSet(sb, e, state); break;
                    case CodexCategory.Named: AppendNamed(sb, e, state); break;
                    case CodexCategory.MiniSets: AppendMiniSetBody(sb, e.MiniSet, null, state); break;
                    default: AppendPower(sb, e, state); break;
                }
            }
            body = sb.ToString();
        }

        private static void AppendImplicit(StringBuilder sb, BaseDef b)
        {
            sb.Append(Colored(Content.FormatStat(b.ImplicitStat, b.ImplicitValue), "#c8c8ff"))
              .Append(Colored(Loc.T("  （この土台が必ず持つ性能）", "  (always on this base)"), Dim)).Append('\n');
        }

        private static void AppendBase(StringBuilder sb, CodexEntry e, CodexState state)
        {
            AppendImplicit(sb, e.Base);
            int total = 0, found = 0;
            foreach (var u in Content.Uniques)
            {
                if (u.BaseId != e.Id) continue;
                total++;
                if (state.Codex.Contains(u.Id)) found++;
            }
            sb.Append('\n').Append(Loc.T(
                "特性：入手のたびに抽選されます。",
                "Affixes are rolled each time you obtain one.")).Append('\n');
            if (total > 0)
                sb.Append('\n').Append(Loc.T($"この土台の固有品：{found}/{total} 発見", $"Legendaries on this base: {found}/{total} found"));
        }

        private static void AppendUnique(StringBuilder sb, CodexEntry e, CodexState state)
        {
            var u = e.Unique;
            if (e.Base != null)
            {
                sb.Append(Loc.T("土台：", "Base: ")).Append(e.Base.Name.ToString()).Append('\n');
                AppendImplicit(sb, e.Base);
            }
            foreach (var pw in u.Powers) sb.Append(Colored(Content.FormatPower(pw.Power, pw.Value), Purple)).Append('\n');
            if (u.Link != null) sb.Append(Colored(Links.Describe(u.Link), Cyan)).Append('\n');
            sb.Append(Loc.T("特性：入手のたびに3つ抽選されます。", "Affixes: 3 are rolled each time you obtain one.")).Append('\n');
            sb.Append(Colored(Loc.T(
                $"覚醒：装着して敵を倒すと溜まり、{Content.AwakenThresholdFor(1)}・{Content.AwakenThresholdFor(2)}・{Content.AwakenThresholdFor(3)}で覚醒Ⅰ・Ⅱ・Ⅲ（固有効果 最大{Content.AwakenPowerPctAt(Content.MaxAwakenLevel) / 100f:0.##}倍）。",
                $"Awakening: gather power while worn; at {Content.AwakenThresholdFor(1)}, {Content.AwakenThresholdFor(2)} and {Content.AwakenThresholdFor(3)} it reaches I, II and III (powers up to x{Content.AwakenPowerPctAt(Content.MaxAwakenLevel) / 100f:0.##})."), Dim)).Append('\n');
            if (u.SetId != null && Content.GetSet(u.SetId) is SetDef set)
            {
                sb.Append('\n').Append(Colored($"《{set.Name}》", Orange)).Append('\n');
                AppendSetBody(sb, set, u.Id, state);
            }
            else if (!string.IsNullOrEmpty(u.Lore.ToString()))
                sb.Append('\n').Append("<i>").Append(Colored(u.Lore.ToString(), "#c9a86a")).Append("</i>");
        }

        private static void AppendSet(StringBuilder sb, CodexEntry e, CodexState state)
        {
            AppendSetBody(sb, e.Set, null, state);
        }

        private static void AppendSetBody(StringBuilder sb, SetDef set, string currentPieceId, CodexState state)
        {
            sb.Append(Colored(set.Describe(), "#c8c8e0")).Append('\n').Append('\n');
            CodexEntry setEntry = null;
            foreach (var se in CodexQuery.Entries(CodexCategory.Sets)) if (se.Set == set) { setEntry = se; break; }
            if (setEntry == null) return;
            int have = 0;
            foreach (var pc in setEntry.Pieces) if (state.Codex.Contains(pc.Id)) have++;
            sb.Append(Loc.T($"部位（見つけた数 {have}/{setEntry.Pieces.Count}）", $"Pieces ({have}/{setEntry.Pieces.Count} found)")).Append('\n');
            foreach (var pc in setEntry.Pieces)
            {
                string slotLine = "";
                if (Content.TryGetBase(pc.BaseId, out var pb)) slotLine = Content.SlotName(pb.Slot) + " · " + Content.LineName(pb.Line);
                bool got = state.Codex.Contains(pc.Id);
                sb.Append(got ? "◆ " : "◇ ");
                sb.Append(got ? Colored(pc.Name.ToString(), Orange) : Colored("？？？", "#6a6a7c"));
                sb.Append("  ").Append(Colored(slotLine, Dim));
                if (pc.Id == currentPieceId) sb.Append(Colored(Loc.T("  ←この装備", "  <- this one"), Dim));
                sb.Append('\n');
            }
        }

        /// <summary>銘品（発見済み）：土台・固有効果・特性の抽選・一言・所属する組。</summary>
        private static void AppendNamed(StringBuilder sb, CodexEntry e, CodexState state)
        {
            var n = e.Named;
            sb.Append(Loc.T("レア度：", "Rarity: ")).Append(Colored(Content.RarityName(n.Rarity).ToString(), RarityHex(n.Rarity))).Append('\n');
            if (e.Base != null)
            {
                sb.Append(Loc.T("土台：", "Base: ")).Append(e.Base.Name.ToString()).Append('\n');
                AppendImplicit(sb, e.Base);
            }
            foreach (var pw in n.Powers) sb.Append(Colored(Content.FormatPower(pw.Power, pw.Value), Purple)).Append('\n');
            int affixes = Content.AffixCount(n.Rarity);
            sb.Append(Loc.T($"特性：入手のたびに{affixes}つ抽選されます。", $"Affixes: {affixes} are rolled each time you obtain one.")).Append('\n');
            if (!string.IsNullOrEmpty(n.Lore.ToString()))
                sb.Append('\n').Append("<i>").Append(Colored(n.Lore.ToString(), "#c9a86a")).Append("</i>").Append('\n');
            if (n.MiniSetId != null && NamedItems.TryGetMiniSet(n.MiniSetId, out var ms))
            {
                sb.Append('\n').Append(Colored($"《{ms.Name}》", Cyan)).Append('\n');
                AppendMiniSetBody(sb, ms, n.Id, state);
            }
        }

        /// <summary>組：2/3点のボーナスと、部位ごとの発見状況（未発見の部位は名前を伏せる）。</summary>
        private static void AppendMiniSetBody(StringBuilder sb, MiniSetDef set, string currentNamedId, CodexState state)
        {
            sb.Append(Colored(set.Describe(), "#c8c8e0")).Append('\n').Append('\n');
            int have = 0;
            foreach (var id in set.PieceIds) if (state.Codex.Contains(NamedItems.CodexId(id))) have++;
            sb.Append(Loc.T($"部位（見つけた数 {have}/{set.PieceCount}）", $"Pieces ({have}/{set.PieceCount} found)")).Append('\n');
            foreach (var id in set.PieceIds)
            {
                bool got = state.Codex.Contains(NamedItems.CodexId(id));
                if (!NamedItems.TryGetNamed(id, out var pc))
                {
                    sb.Append("◇ ").Append(Colored("？？？", "#6a6a7c")).Append('\n');
                    continue;
                }
                string slotLine = "";
                if (Content.TryGetBase(pc.BaseId, out var pb)) slotLine = Content.SlotName(pb.Slot) + " · " + Content.LineName(pb.Line);
                sb.Append(got ? "◆ " : "◇ ");
                sb.Append(got ? Colored(pc.Name.ToString(), RarityHex(pc.Rarity)) : Colored("？？？", "#6a6a7c"));
                sb.Append("  ").Append(Colored(slotLine, Dim));
                if (id == currentNamedId) sb.Append(Colored(Loc.T("  ←この装備", "  <- this one"), Dim));
                sb.Append('\n');
            }
        }

        private static void AppendPower(StringBuilder sb, CodexEntry e, CodexState state)
        {
            if (CodexQuery.TryPowerRange(e.Power, out int min, out int max))
            {
                if (min == max) sb.Append(Colored(Content.FormatPower(e.Power, max), Purple)).Append('\n');
                else
                {
                    sb.Append(Colored(Content.FormatPower(e.Power, min), Purple)).Append(Colored(Loc.T("  （最小）", "  (min)"), Dim)).Append('\n');
                    sb.Append(Colored(Content.FormatPower(e.Power, max), Purple)).Append(Colored(Loc.T("  （最大）", "  (max)"), Dim)).Append('\n');
                }
            }
            sb.Append('\n').Append(Loc.T("付く部位：", "Found on: ")).Append(SlotsText(e.SlotMask)).Append('\n');
            var names = new System.Collections.Generic.List<string>();
            foreach (var u in e.Carriers)
                if (state.Codex.Contains(u.Id)) names.Add(u.Name.ToString());
            foreach (var s in e.CarrierSets)
                foreach (var se in CodexQuery.Entries(CodexCategory.Sets))
                    if (se.Set == s && state.IsFound(se)) names.Add("《" + s.Name + "》");
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
