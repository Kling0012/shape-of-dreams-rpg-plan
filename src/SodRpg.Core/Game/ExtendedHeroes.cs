using System;
using System.Collections.Generic;
using System.Globalization;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    /// <summary>ヒーローコネクタ（apiVersion 1）が受け入れた拡張旅人1人分（docs/specs/hero-connector.md §2）。</summary>
    public sealed class ExtendedHeroDef
    {
        public ExtendedHeroDef(string key, string sourceModId, Txt name, bool melee,
            IReadOnlyDictionary<string, Txt> memoryNames, IReadOnlyList<TalentDef> tree, string declarationHash)
        {
            Key = key;
            SourceModId = sourceModId;
            Name = name;
            Melee = melee;
            MemoryNames = memoryNames;
            Tree = tree;
            DeclarationHash = declarationHash;
        }

        /// <summary>Hero のサブクラスの型名（ClientSession.HeroKeyOf と同じ形）。</summary>
        public string Key { get; }
        /// <summary>宣言を置いたMOD（metadata.json の id）。</summary>
        public string SourceModId { get; }
        public Txt Name { get; }
        public bool Melee { get; }
        /// <summary>その旅人の記憶（St_*）の表示名。装備・リンク・報酬の生成には入らない。</summary>
        public IReadOnlyDictionary<string, Txt> MemoryNames { get; }
        /// <summary>宣言から作った星図。汎用ツリー（Content.Talents）のままのとき null。</summary>
        public IReadOnlyList<TalentDef> Tree { get; }
        /// <summary>宣言の旅人部分の正規化JSONのハッシュ。協力の照合と指紋に使う。</summary>
        public string DeclarationHash { get; }
    }

    /// <summary>宣言ファイル1つ分の処理結果（§7 の報告ファイルの材料）。</summary>
    public sealed class ExtendedHeroFileResult
    {
        /// <summary>宣言を置いたMODの id。</summary>
        public string SourceModId;
        /// <summary>ファイル全体の拒否理由（JSON 不成立・apiVersion 未知など）。読めたら null。</summary>
        public string FileError;
        /// <summary>旅人ごとの結果。ファイルが読めたときのみ入る。</summary>
        public List<ExtendedHeroResult> Heroes = new List<ExtendedHeroResult>();
    }

    /// <summary>宣言1旅人分の結果。</summary>
    public sealed class ExtendedHeroResult
    {
        public string Key;
        /// <summary>登録簿に入った（星図は汎用のこともある）。</summary>
        public bool Accepted;
        /// <summary>星図を上限・形式違反で汎用ツリーにした（旅人の登録は残る）。</summary>
        public bool GenericTree;
        /// <summary>拒否・汎用化の理由。問題なければ null。</summary>
        public string Reason;
        /// <summary>受け入れ時の宣言ハッシュ。</summary>
        public string DeclarationHash;
    }

    /// <summary>§5 の星図の上限。起動時に既存9人の Core から求める（数値はコードに書かない）。</summary>
    public sealed class ExtendedStarLimits
    {
        /// <summary>通常の星で宣言できる Stat と、その Stat の最大 PerRank。</summary>
        public IReadOnlyDictionary<Stat, int> PerRankMax;
        /// <summary>通常の星で宣言できる Stat と、その Stat の最大 MaxRank。</summary>
        public IReadOnlyDictionary<Stat, int> MaxRankMax;
        /// <summary>キーストーンの value の Stat ごとの上限（その Stat の通常の星の最大 PerRank × 最大 MaxRank）。</summary>
        public IReadOnlyDictionary<Stat, int> KeystoneValueMax;
        /// <summary>通常の星の数の上限（既存9人の Core の1旅人の最大）。</summary>
        public int NormalStarsMax;
        /// <summary>キーストーンの数の上限。</summary>
        public int KeystonesMax;

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Star limits from the nine shipped travelers' cores (apiVersion ").Append(ExtendedHeroes.ApiVersion).Append("):\n");
            sb.Append("  normal stars per tree: ").Append(NormalStarsMax.ToString(CultureInfo.InvariantCulture))
                .Append(", keystones per tree: ").Append(KeystonesMax.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  per Stat (perRank max / maxRank max / keystone value max):\n");
            foreach (var kv in PerRankMax)
                sb.Append("    ").Append(kv.Key).Append(": ")
                    .Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append(" / ")
                    .Append(MaxRankMax[kv.Key].ToString(CultureInfo.InvariantCulture)).Append(" / ")
                    .Append(KeystoneValueMax[kv.Key].ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 拡張旅人の登録簿（ヒーローコネクタ §6-1）。宣言JSONの読み取り・検証・星図の構築はすべてこの中で行い、
    /// Unity にも本体の型にも依存しない。登録は Mod 側の走査とランタイムAPI（Dreamforge.Api.HeroConnector）から。
    /// 1つの宣言の失敗はほかの宣言・Dreamforge 本体に広げない（fail-soft、§4）。
    /// </summary>
    public static class ExtendedHeroes
    {
        public const int ApiVersion = 1;

        private static readonly object RegistryLock = new object();
        private static readonly List<ExtendedHeroDef> HeroesInOrder = new List<ExtendedHeroDef>();
        private static readonly Dictionary<string, ExtendedHeroDef> HeroesByKey = new Dictionary<string, ExtendedHeroDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Txt> MemoryNames = new Dictionary<string, Txt>(StringComparer.Ordinal);
        private static ExtendedStarLimits limits;

        /// <summary>登録済みの拡張旅人（受け入れ順）。</summary>
        public static IReadOnlyList<ExtendedHeroDef> Registered
        {
            get { lock (RegistryLock) return HeroesInOrder.ToArray(); }
        }

        public static bool IsRegistered(string heroKey) { lock (RegistryLock) return heroKey != null && HeroesByKey.ContainsKey(heroKey); }

        public static bool TryGet(string heroKey, out ExtendedHeroDef hero)
        {
            lock (RegistryLock)
            {
                if (heroKey != null && HeroesByKey.TryGetValue(heroKey, out hero)) return true;
                hero = null;
                return false;
            }
        }

        /// <summary>記憶の表示名（§6-6）。装備・リンク・報酬の生成には入らない、名前引きだけ。</summary>
        public static bool TryGetMemoryName(string memoryKey, out Txt name)
        {
            lock (RegistryLock) return MemoryNames.TryGetValue(memoryKey, out name);
        }

        /// <summary>登録簿の旅人の近接判定（§6-3）。未登録は false。</summary>
        public static bool IsMelee(string heroKey) => TryGet(heroKey, out var hero) && hero.Melee;

        /// <summary>星IDが拡張旅人の星（"x." 始まり）か。休眠割り当ての判定に使う（§10）。</summary>
        public static bool IsExtendedStarId(string starId) => starId != null && starId.Length > 2 && starId[0] == 'x' && starId[1] == '.';

        /// <summary>§5 の上限。既存9人の Core から一度だけ求める。</summary>
        public static ExtendedStarLimits Limits()
        {
            lock (RegistryLock)
            {
                if (limits == null) limits = ComputeLimits();
                return limits;
            }
        }

        private static ExtendedStarLimits ComputeLimits()
        {
            var perRank = new Dictionary<Stat, int>();
            var maxRank = new Dictionary<Stat, int>();
            var starsPerHero = new Dictionary<string, int>(StringComparer.Ordinal);
            var keysPerHero = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in HeroSigils.CoreBaseline)
            {
                if (t.HeroKey == null) continue;
                if (t.IsKeystone)
                {
                    keysPerHero.TryGetValue(t.HeroKey, out int keys);
                    keysPerHero[t.HeroKey] = keys + 1;
                    continue;
                }
                starsPerHero.TryGetValue(t.HeroKey, out int stars);
                starsPerHero[t.HeroKey] = stars + 1;
                perRank.TryGetValue(t.Stat, out int bestPerRank);
                if (t.PerRank > bestPerRank) perRank[t.Stat] = t.PerRank;
                maxRank.TryGetValue(t.Stat, out int bestMaxRank);
                if (t.MaxRank > bestMaxRank) maxRank[t.Stat] = t.MaxRank;
            }
            int normalStarsMax = 0, keystonesMax = 0;
            foreach (var kv in starsPerHero) if (kv.Value > normalStarsMax) normalStarsMax = kv.Value;
            foreach (var kv in keysPerHero) if (kv.Value > keystonesMax) keystonesMax = kv.Value;
            // キーストーンの value の上限は Stat ごとに、その Stat の通常の星の最大 PerRank × 最大 MaxRank
            // （既存の最も強い通常の星1つを満段にした値）とする。Power 系キーストーンの値とは単位が違うため使わない。
            var keystoneValueMax = new Dictionary<Stat, int>();
            foreach (var kv in perRank) keystoneValueMax[kv.Key] = kv.Value * maxRank[kv.Key];
            return new ExtendedStarLimits
            {
                PerRankMax = perRank,
                MaxRankMax = maxRank,
                KeystoneValueMax = keystoneValueMax,
                NormalStarsMax = normalStarsMax,
                KeystonesMax = keystonesMax,
            };
        }

        /// <summary>
        /// 宣言ファイル1つ分（§3-2）を読み、旅人ごとに判定して登録する。戻り値は報告の材料。
        /// 例外を外へ投げない（fail-soft）。warn には "[Dreamforge/Connector]" 付きの1行を渡す。
        /// 同じ key がすでに同じ内容で登録済みのときは、重ねて登録せず受理として返す（走査2回目）。
        /// </summary>
        public static ExtendedHeroFileResult Register(string json, string sourceModId, Action<string> warn)
        {
            var result = new ExtendedHeroFileResult { SourceModId = sourceModId };
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    result.FileError = "the file is empty";
                    Warn(warn, result.SourceModId, null, "rejected: " + result.FileError);
                    return result;
                }
                object parsed;
                try { parsed = Json.Parse(json); }
                catch (Exception ex)
                {
                    result.FileError = "not readable as JSON: " + ex.Message;
                    Warn(warn, result.SourceModId, null, "rejected: " + result.FileError);
                    return result;
                }
                if (!(parsed is JsonObject root))
                {
                    result.FileError = "the top level must be a JSON object";
                    Warn(warn, result.SourceModId, null, "rejected: " + result.FileError);
                    return result;
                }
                if (!root.TryGet("dreamforgeApi", out var apiObj) || !(apiObj is long api) || api != ApiVersion)
                {
                    result.FileError = "unsupported dreamforgeApi " + Describe(apiObj) + " (this Dreamforge reads version " + ApiVersion + ")";
                    Warn(warn, result.SourceModId, null, "rejected: " + result.FileError);
                    return result;
                }
                if (!root.TryGet("heroes", out var heroesObj) || !(heroesObj is List<object> heroes))
                {
                    result.FileError = "heroes must be an array";
                    Warn(warn, result.SourceModId, null, "rejected: " + result.FileError);
                    return result;
                }
                foreach (var entry in heroes)
                    result.Heroes.Add(RegisterHero(entry, result.SourceModId, warn));
            }
            catch (Exception ex)
            {
                // ここへ来るのはこの実装自身の不具合。宣言1つの失敗で止まらないのが約束なので、記録して返す。
                result.FileError = result.FileError ?? ("unexpected failure: " + ex.Message);
                Warn(warn, result.SourceModId, null, "rejected: " + result.FileError);
            }
            return result;
        }

        private static ExtendedHeroResult RegisterHero(object entry, string sourceModId, Action<string> warn)
        {
            var outcome = new ExtendedHeroResult();
            try
            {
                if (!(entry is JsonObject hero))
                    return Rejected(outcome, warn, "a hero entry must be a JSON object");
                if (!hero.TryGet("key", out var keyObj) || !(keyObj is string key) || !ValidHeroKey(key))
                    return Rejected(outcome, warn, "invalid key: expected a Hero_ subclass type name (alphanumeric and _, up to 64 chars)");
                outcome.Key = key;
                if (HeroSigils.IsCoreTraveler(key))
                    return Rejected(outcome, warn, "key " + key + " is one of the nine shipped travelers");
                string declarationHash = StarClusters.RegistryHash(Json.Write(hero));
                lock (RegistryLock)
                {
                    if (HeroesByKey.TryGetValue(key, out var existing))
                    {
                        if (existing.DeclarationHash == declarationHash)
                        {
                            outcome.Accepted = true; // 同じ内容の再読み（走査2回目）。何もしない。
                            outcome.DeclarationHash = declarationHash;
                            return outcome;
                        }
                        return Rejected(outcome, warn, "key " + key + " is already declared by " + existing.SourceModId
                            + "; the first declaration wins");
                    }
                }
                if (!TryReadText(hero, "name", null, out var name) || name == null)
                {
                    string fallback = key.StartsWith("Hero_", StringComparison.Ordinal) ? key.Substring(5) : key;
                    name = new Txt(fallback, fallback, null);
                }
                bool melee = hero.TryGet("melee", out var meleeObj) && meleeObj is bool b && b;
                var memories = new Dictionary<string, Txt>(StringComparer.Ordinal);
                if (hero.TryGet("memories", out var memoriesObj) && memoriesObj is List<object> memoryList)
                    foreach (var memory in memoryList)
                        ReadMemory(memory, memories, warn, key);
                var tree = ReadTree(hero, key, out string treeReason);
                if (treeReason != null)
                {
                    outcome.GenericTree = true;
                    outcome.Reason = treeReason;
                    Warn(warn, sourceModId, key, "star tree rejected; the traveler falls back to the generic tree: " + treeReason);
                }
                var def = new ExtendedHeroDef(key, sourceModId, name, melee, memories, tree, declarationHash);
                lock (RegistryLock)
                {
                    // 二重登録は上で抜けたが、同じファイル内で同じ key を2つ書いた場合のためにもう一度見る。
                    if (HeroesByKey.ContainsKey(key))
                        return Rejected(outcome, warn, "key " + key + " is declared twice in this file; the first entry wins");
                    HeroesByKey.Add(key, def);
                    HeroesInOrder.Add(def);
                    foreach (var kv in memories) if (!MemoryNames.ContainsKey(kv.Key)) MemoryNames.Add(kv.Key, kv.Value);
                }
                if (tree != null) HeroSigils.RegisterExtendedTree(key, tree);
                outcome.Accepted = true;
                outcome.DeclarationHash = declarationHash;
                return outcome;
            }
            catch (Exception ex)
            {
                return Rejected(outcome, warn, "unexpected failure: " + ex.Message);
            }
        }

        private static ExtendedHeroResult Rejected(ExtendedHeroResult outcome, Action<string> warn, string reason)
        {
            outcome.Accepted = false;
            outcome.Reason = reason;
            Warn(warn, null, outcome.Key, "rejected: " + reason);
            return outcome;
        }

        private static void Warn(Action<string> warn, string sourceModId, string key, string message)
        {
            string who = key != null ? key : "file";
            string from = string.IsNullOrEmpty(sourceModId) ? "?" : sourceModId;
            try { warn?.Invoke("[Dreamforge/Connector] " + who + " (" + from + "): " + message); }
            catch (Exception) { }
        }

        private static bool ValidHeroKey(string key)
        {
            if (key.Length < 6 || key.Length > 64 || !key.StartsWith("Hero_", StringComparison.Ordinal)) return false;
            for (int i = 5; i < key.Length; i++)
            {
                char c = key[i];
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }

        private static void ReadMemory(object memory, Dictionary<string, Txt> memories, Action<string> warn, string heroKey)
        {
            try
            {
                if (!(memory is JsonObject entry)) return;
                if (!entry.TryGet("key", out var keyObj) || !(keyObj is string memoryKey) || memoryKey.Length == 0 || memoryKey.Length > 128) return;
                foreach (char c in memoryKey)
                    if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return;
                if (!TryReadText(entry, "name", null, out var name) || name == null) return;
                if (!memories.ContainsKey(memoryKey)) memories.Add(memoryKey, name);
            }
            catch (Exception ex)
            {
                Warn(warn, null, heroKey, "a memory entry was ignored: " + ex.Message);
            }
        }

        /// <summary>starTree を検証して TalentDef へ組み立てる。失敗したら tree=null と理由（§4: 星図だけ汎用にする）。</summary>
        private static IReadOnlyList<TalentDef> ReadTree(JsonObject hero, string heroKey, out string reason)
        {
            reason = null;
            if (!hero.TryGet("starTree", out var treeObj) || !(treeObj is JsonObject tree)) return null;
            if (!tree.TryGet("nodes", out var nodesObj) || !(nodesObj is List<object> nodes)) return null;
            if (nodes.Count == 0) return null;
            var limits = Limits();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var built = new List<TalentDef>(nodes.Count);
            int normalStars = 0, keystones = 0;
            foreach (var nodeObj in nodes)
            {
                if (!(nodeObj is JsonObject node)) { reason = "a node must be a JSON object"; return null; }
                if (!node.TryGet("id", out var idObj) || !(idObj is string id) || id.Length == 0 || id.Length > 32) { reason = "invalid node id (expected [a-z0-9_], up to 32 chars)"; return null; }
                foreach (char c in id)
                    if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') { reason = "invalid node id '" + id + "' (expected [a-z0-9_])"; return null; }
                if (!ids.Add(id)) { reason = "duplicate node id '" + id + "'"; return null; }
                string statName = null;
                if (!node.TryGet("stat", out var statObj) || !(statObj is string statText) || (statName = statText) == null
                    || !Enum.TryParse(statName, out Stat stat) || !limits.PerRankMax.ContainsKey(stat))
                { reason = "node '" + id + "': stat '" + (statName ?? "?") + "' is not declarable (use a Stat of the shipped travelers' cores)"; return null; }
                if (!TryReadText(node, "name", id, out var name)) name = new Txt(id, id, null);
                bool keystone = node.TryGet("keystone", out var keyObj) && keyObj is bool k && k;
                int tier = 1;
                if (node.TryGet("tier", out var tierObj))
                {
                    if (!(tierObj is long tierValue) || (tierValue != 1 && tierValue != 2)) { reason = "node '" + id + "': tier must be 1 or 2"; return null; }
                    tier = (int)tierValue;
                }
                TalentDef talent;
                if (keystone)
                {
                    int valueMax = limits.KeystoneValueMax[stat];
                    if (!node.TryGet("value", out var valueObj) || !(valueObj is long value) || value <= 0 || value > valueMax)
                    { reason = "node '" + id + "': keystone value for " + stat + " must be 1.." + valueMax.ToString(CultureInfo.InvariantCulture); return null; }
                    if (!TryReadText(node, "description", null, out var description) || description == null)
                    { reason = "node '" + id + "': a keystone requires a description"; return null; }
                    keystones++;
                    // RankCost を Content.KeystoneCost に揃える: Stat キーストーンは value が PerRank に入るため、
                    // 既定の RankCost のままだと刻印の費用が 1 になってしまう（TalentDef.KeystoneCost の規則）。
                    talent = new TalentDef("x." + heroKey + "." + id, Line.Offense, name, stat, (int)value, description)
                        { HeroKey = heroKey, RankCost = Content.KeystoneCost };
                }
                else
                {
                    if (!node.TryGet("perRank", out var perRankObj) || !(perRankObj is long perRank) || perRank <= 0
                        || !node.TryGet("maxRank", out var maxRankObj) || !(maxRankObj is long maxRank) || maxRank <= 0)
                    { reason = "node '" + id + "': normal stars require positive integer perRank and maxRank"; return null; }
                    if (perRank > limits.PerRankMax[stat]) { reason = "node '" + id + "': perRank " + perRank + " exceeds the shipped max " + limits.PerRankMax[stat] + " for " + stat; return null; }
                    if (maxRank > limits.MaxRankMax[stat]) { reason = "node '" + id + "': maxRank " + maxRank + " exceeds the shipped max " + limits.MaxRankMax[stat] + " for " + stat; return null; }
                    normalStars++;
                    talent = new TalentDef("x." + heroKey + "." + id, Line.Offense, name, stat, (int)perRank, (int)maxRank)
                        { HeroKey = heroKey, Tier = tier };
                }
                built.Add(talent);
            }
            if (normalStars > limits.NormalStarsMax)
            { reason = "the tree declares " + normalStars + " normal stars; the shipped max is " + limits.NormalStarsMax; return null; }
            if (keystones > limits.KeystonesMax)
            { reason = "the tree declares " + keystones + " keystones; the shipped max is " + limits.KeystonesMax; return null; }
            return built.AsReadOnly();
        }

        /// <summary>ja/en/zh の文字列欄を読む。ほかの言語コードは apiVersion 1 では読まない（Txt が3言語のため）。</summary>
        private static bool TryReadText(JsonObject obj, string field, string fallback, out Txt text)
        {
            text = null;
            if (!obj.TryGet(field, out var value)) return false;
            if (!(value is JsonObject languages)) return false; // 型違いの欄は無視する（§4）
            string ja = null, en = null, zh = null;
            if (languages.TryGet("ja", out var jaObj) && jaObj is string jaText) ja = jaText;
            if (languages.TryGet("en", out var enObj) && enObj is string enText) en = enText;
            if (languages.TryGet("zh", out var zhObj) && zhObj is string zhText) zh = zhText;
            if (ja == null && en == null && zh == null) return fallback == null ? false : Success(out text, fallback);
            string jaFinal = ja ?? en ?? fallback, enFinal = en ?? ja ?? fallback;
            text = new Txt(jaFinal, enFinal, zh);
            return true;
        }

        private static bool Success(out Txt text, string fallback)
        {
            text = new Txt(fallback, fallback, null);
            return true;
        }

        private static string Describe(object value) =>
            value is long l ? l.ToString(CultureInfo.InvariantCulture) : value == null ? "missing" : "unrecognized";

        /// <summary>
        /// ContentFingerprint への記録（§9）。拡張旅人が1人以上のときだけ "x:" の記録を返し、
        /// 1人もいなければ null（指紋は登録前と同じ値のまま）。
        /// </summary>
        public static string FingerprintRecord()
        {
            lock (RegistryLock)
            {
                if (HeroesInOrder.Count == 0) return null;
                var parts = new string[HeroesInOrder.Count];
                for (int i = 0; i < HeroesInOrder.Count; i++)
                    parts[i] = "x:" + HeroesInOrder[i].Key + ":" + HeroesInOrder[i].DeclarationHash;
                Array.Sort(parts, StringComparer.Ordinal);
                return string.Join("|", parts);
            }
        }
    }
}
