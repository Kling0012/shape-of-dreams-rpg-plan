using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 銘品（v1.32 設計 3.2・4）。固定の土台・名前・一言（Lore）・1〜2個の固有効果を持つ低レアの定義。
    /// 特性は通常どおり個体ごとに抽選するので、ここには持たない。MiniSetId は所属する組（null 可）。
    /// </summary>
    public sealed class NamedDef
    {
        public NamedDef(string id, string baseId, Rarity rarity, Txt name, Txt lore, string miniSetId, Power p1, int v1)
            : this(id, baseId, rarity, name, lore, miniSetId, p1, v1, Power.None, 0)
        {
        }

        public NamedDef(string id, string baseId, Rarity rarity, Txt name, Txt lore, string miniSetId, Power p1, int v1, Power p2, int v2)
        {
            if (rarity != Rarity.Uncommon && rarity != Rarity.Rare && rarity != Rarity.Epic)
                throw new ArgumentOutOfRangeException(nameof(rarity), "銘品のレア度はアンコモン・レア・エピックのどれかです: " + rarity);
            Id = id;
            BaseId = baseId;
            Rarity = rarity;
            Name = name;
            Lore = lore;
            MiniSetId = miniSetId;
            Powers = p2 == Power.None
                ? new[] { new PowerLine(p1, v1) }
                : new[] { new PowerLine(p1, v1), new PowerLine(p2, v2) };
        }

        public string Id { get; }
        public string BaseId { get; }
        public Rarity Rarity { get; }
        public Txt Name { get; }
        public Txt Lore { get; }
        /// <summary>1〜2個の固定の固有効果（値も固定）。</summary>
        public IReadOnlyList<PowerLine> Powers { get; }
        /// <summary>所属する組（小セット）のID。組に属さない銘品は null。</summary>
        public string MiniSetId { get; }
    }

    /// <summary>組（小セット・v1.32 設計 3.2）。2〜3部位。2点は能力値1行、3点は弱い固有効果1行（2点の組は null）。</summary>
    public sealed class MiniSetDef
    {
        public MiniSetDef(string id, Txt name, string[] pieceIds, StatLine twoPiece)
            : this(id, name, pieceIds, twoPiece, null)
        {
        }

        public MiniSetDef(string id, Txt name, string[] pieceIds, StatLine twoPiece, PowerLine threePiece)
        {
            if (pieceIds == null) throw new ArgumentNullException(nameof(pieceIds));
            if (pieceIds.Length < 2 || pieceIds.Length > 3)
                throw new ArgumentOutOfRangeException(nameof(pieceIds), "組の部位は2〜3です: " + pieceIds.Length);
            Id = id;
            Name = name;
            PieceIds = pieceIds.ToArray();
            TwoPiece = twoPiece;
            ThreePiece = threePiece;
        }

        public string Id { get; }
        public Txt Name { get; }
        /// <summary>組の部位（銘品ID）。</summary>
        public IReadOnlyList<string> PieceIds { get; }
        /// <summary>2つ装着のボーナス（能力値1行）。</summary>
        public StatLine TwoPiece { get; }
        /// <summary>3つ装着のボーナス（弱い固有効果1行）。2点の組は null。</summary>
        public PowerLine ThreePiece { get; }
        public int PieceCount => PieceIds.Count;

        /// <summary>図鑑・装着画面の説明文（SetDef.Describe と同じ書式）。</summary>
        public string Describe()
        {
            string two = Content.FormatStat(TwoPiece.Stat, TwoPiece.Value);
            if (ThreePiece == null)
                return Loc.T($"2つ装着：{two}", $"2 pieces: {two}");
            string three = Content.FormatPowerBullets(ThreePiece.Power, ThreePiece.Value, "　");
            return Loc.T($"2つ装着：{two}\n3つ装着：\n{three}", $"2 pieces: {two}\n3 pieces:\n{three}");
        }
    }

    /// <summary>
    /// 銘品と組の登録簿（v1.32）。本体のデータ（NamedItems.Data.cs）は初回アクセスで登録される。
    /// </summary>
    public static class NamedItems
    {
        private static List<NamedDef> _named;
        private static List<MiniSetDef> _miniSets;
        private static Dictionary<string, NamedDef> _namedById;
        private static Dictionary<string, MiniSetDef> _miniSetsById;

        static NamedItems()
        {
            RegisterForTests(NamedItemsData.Named, NamedItemsData.MiniSets);
        }

        public static IReadOnlyList<NamedDef> All => _named;
        public static IReadOnlyList<MiniSetDef> MiniSets => _miniSets;

        /// <summary>銘品1種の、土台を1とした抽選の重み（設計 3.4）。</summary>
        public const int UncommonWeight = 2;
        public const int RareWeight = 3;
        public const int EpicWeight = 6;

        /// <summary>図鑑にまだない銘品の重みの倍率（設計 3.4）。</summary>
        public const int NotInCodexMultiplier = 3;

        /// <summary>始めた組の未所持部位の重みの倍率（設計 3.4）。</summary>
        public const int MissingMiniSetPieceMultiplier = 4;

        public static int RarityWeight(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return UncommonWeight;
                case Rarity.Rare: return RareWeight;
                case Rarity.Epic: return EpicWeight;
                default: return 0;
            }
        }

        /// <summary>図鑑に載る文字列。固有品・土台のIDと衝突しない接頭辞を付ける（設計 4）。</summary>
        public static string CodexId(string namedId) => "n:" + namedId;

        public static bool TryGetNamed(string id, out NamedDef def) => _namedById.TryGetValue(id ?? string.Empty, out def);
        public static bool TryGetMiniSet(string id, out MiniSetDef def) => _miniSetsById.TryGetValue(id ?? string.Empty, out def);

        /// <summary>ツールチップの一言の行。銘品でない遺物・定義の無い銘品IDは null。</summary>
        public static string LoreLine(Relic r)
        {
            if (r == null || r.NamedId == null || !TryGetNamed(r.NamedId, out var def)) return null;
            string lore = def.Lore.ToString();
            return string.IsNullOrEmpty(lore) ? null : lore;
        }

        /// <summary>ツールチップの組の行（例「《霜誓の記章》 装着中 2/3」）。装着中の部位数 pieces は呼び出し側（Build.MiniSets）から渡す。</summary>
        public static string MiniSetLine(Relic r, int pieces)
        {
            if (r == null || r.NamedId == null || !TryGetNamed(r.NamedId, out var def)
                || def.MiniSetId == null || !TryGetMiniSet(def.MiniSetId, out var set)) return null;
            int n = Math.Max(0, Math.Min(set.PieceCount, pieces));
            return Loc.T($"《{set.Name}》 装着中 {n}/{set.PieceCount}", $"\"{set.Name}\" {n}/{set.PieceCount} worn");
        }

        /// <summary>図鑑の組の進み（例「《霜誓の記章》 発見 1/3」）。codex は Profile.Codex。</summary>
        public static string MiniSetProgress(string miniSetId, ISet<string> codex)
        {
            if (!TryGetMiniSet(miniSetId, out var set)) return null;
            int found = 0;
            foreach (var piece in set.PieceIds)
                if (codex != null && codex.Contains(CodexId(piece))) found++;
            return Loc.T($"《{set.Name}》 発見 {found}/{set.PieceCount}", $"\"{set.Name}\" {found}/{set.PieceCount} found");
        }

        /// <summary>試験用の登録。登録簿を丸ごと差し替え、図鑑のキャッシュを作り直させる。空を渡せば空の登録簿になる
        /// （本体のデータへ戻すには NamedItemsData を渡す。起動時の初期化も同じ物を登録する）。</summary>
        internal static void RegisterForTests(IEnumerable<NamedDef> named, IEnumerable<MiniSetDef> miniSets)
        {
            var namedList = (named ?? Enumerable.Empty<NamedDef>()).ToList();
            var miniList = (miniSets ?? Enumerable.Empty<MiniSetDef>()).ToList();
            var byId = new Dictionary<string, NamedDef>(StringComparer.Ordinal);
            foreach (var n in namedList)
            {
                if (byId.ContainsKey(n.Id)) throw new ArgumentException("銘品IDの重複: " + n.Id);
                byId.Add(n.Id, n);
            }
            var miniById = new Dictionary<string, MiniSetDef>(StringComparer.Ordinal);
            foreach (var m in miniList)
            {
                if (miniById.ContainsKey(m.Id)) throw new ArgumentException("組IDの重複: " + m.Id);
                miniById.Add(m.Id, m);
            }
            _named = namedList;
            _miniSets = miniList;
            _namedById = byId;
            _miniSetsById = miniById;
            CodexQuery.InvalidateCache();
        }
    }
}
