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

    internal sealed class ExtendedStarPlacement
    {
        internal string[] Requires;
        internal int Ring;
        internal decimal Angle;
        internal float? X, Y;
        internal bool? Center;
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

    /// <summary>§5 の星図の上限。既存9人の星から求める（数値はコードに書かない）。</summary>
    public sealed class ExtendedStarLimits
    {
        internal readonly Dictionary<Stat, int> StatValues = new Dictionary<Stat, int>();
        internal readonly Dictionary<Stat, int> StatRanks = new Dictionary<Stat, int>();
        internal readonly Dictionary<Stat, decimal> StatTotals = new Dictionary<Stat, decimal>();
        internal readonly Dictionary<Stat, int> StatKeystoneCaps = new Dictionary<Stat, int>();
        internal readonly Dictionary<LinkKind, decimal> LinkValues = new Dictionary<LinkKind, decimal>();
        internal readonly Dictionary<LinkKind, int> LinkRanks = new Dictionary<LinkKind, int>();
        internal readonly Dictionary<LinkKind, decimal> LinkTotals = new Dictionary<LinkKind, decimal>();
        internal readonly Dictionary<Power, int> PowerValues = new Dictionary<Power, int>();
        internal readonly Dictionary<Power, int> PowerRanks = new Dictionary<Power, int>();
        internal readonly Dictionary<Power, decimal> PowerTotals = new Dictionary<Power, decimal>();
        internal readonly Dictionary<Power, int> PowerKeystoneCaps = new Dictionary<Power, int>();
        internal readonly Dictionary<GimmickParam, int> GimmickParamValues = new Dictionary<GimmickParam, int>();
        internal readonly Dictionary<GimmickParam, int> GimmickParamRanks = new Dictionary<GimmickParam, int>();
        internal readonly Dictionary<GimmickParam, decimal> GimmickParamTotals = new Dictionary<GimmickParam, decimal>();
        internal readonly Dictionary<string, int> NativeValues = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> NativeRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, decimal> NativeTotals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> ScopedValues = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> ScopedRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, decimal> ScopedTotals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        internal readonly Dictionary<string, GimmickDef> Gimmicks = new Dictionary<string, GimmickDef>(StringComparer.Ordinal);
        public IReadOnlyDictionary<Stat, int> PerRankMax;
        public IReadOnlyDictionary<Stat, int> MaxRankMax;
        public IReadOnlyDictionary<Stat, int> KeystoneValueMax;
        public IReadOnlyDictionary<LinkKind, decimal> LinkPerRankMax;
        public IReadOnlyDictionary<LinkKind, int> LinkMaxRankMax;
        public IReadOnlyDictionary<Power, int> PowerPerRankMax;
        public IReadOnlyDictionary<Power, int> PowerMaxRankMax;
        public IReadOnlyDictionary<Power, int> PowerKeystoneValueMax;

        public ExtendedStarLimits()
        {
            PerRankMax = StatValues;
            MaxRankMax = StatRanks;
            KeystoneValueMax = StatKeystoneCaps;
            LinkPerRankMax = LinkValues;
            LinkMaxRankMax = LinkRanks;
            PowerPerRankMax = PowerValues;
            PowerMaxRankMax = PowerRanks;
            PowerKeystoneValueMax = PowerKeystoneCaps;
        }
        public int NormalStarsMax;
        public int KeystonesMax;
        public int StarsMax;
        public int ChoicesMax;
        public int OptionsPerChoiceMax;
        public int ClustersMax;
        public int RankCostMax;
        public int RanksMax;
        public int GimmickBoostMax;
        public int GimmickBoostRanksMax;
        internal decimal GimmickBoostTotalMax;
        internal static string NativeKey(LinkKind kind, string profile) => kind + "/" + (profile ?? "");
        internal static string ScopedKey(ScopeKind scope, GimmickParam? param, string profile) =>
            scope + "/" + (param?.ToString() ?? "amount") + "/" + (profile ?? "");

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Star limits from the nine shipped travelers (apiVersion ").Append(ExtendedHeroes.ApiVersion).Append("):\n");
            sb.Append("  complete TreeFor maxima: stars=").Append(StarsMax).Append(", normal=").Append(NormalStarsMax)
                .Append(", keystones=").Append(KeystonesMax).Append(", choices=").Append(ChoicesMax)
                .Append(", options per choice=").Append(OptionsPerChoiceMax).Append(", clusters=").Append(ClustersMax)
                .Append(", rankCost=").Append(RankCostMax).Append(", maxRank=").Append(RanksMax).Append('\n');
            sb.Append("  Ranked stars must satisfy perRank, maxRank and single-star total maxima together.\n");
            sb.Append("  per Stat (perRank max / maxRank max / single-star total max / keystone value max):\n");
            foreach (var kv in StatValues)
                sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value).Append(" / ")
                    .Append(StatRanks[kv.Key]).Append(" / ").Append(StatTotals[kv.Key].ToString(CultureInfo.InvariantCulture))
                    .Append(" / ").Append(StatKeystoneCaps[kv.Key]).Append('\n');
            sb.Append("  per LinkKind (perRank max / maxRank max / single-star total max):\n");
            foreach (var kv in LinkValues)
                sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value.ToString(CultureInfo.InvariantCulture))
                    .Append(" / ").Append(LinkRanks[kv.Key]).Append(" / ").Append(LinkTotals[kv.Key].ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  per Power (perRank max / maxRank max / single-star total max / keystone value max; 0 = unavailable):\n");
            var powers = new HashSet<Power>(PowerValues.Keys);
            powers.UnionWith(PowerKeystoneCaps.Keys);
            var ordered = new List<Power>(powers);
            ordered.Sort();
            foreach (var power in ordered)
            {
                PowerValues.TryGetValue(power, out int perRank);
                PowerRanks.TryGetValue(power, out int ranks);
                PowerTotals.TryGetValue(power, out decimal total);
                PowerKeystoneCaps.TryGetValue(power, out int value);
                sb.Append("    ").Append(power).Append(": ").Append(perRank).Append(" / ").Append(ranks)
                    .Append(" / ").Append(total.ToString(CultureInfo.InvariantCulture)).Append(" / ").Append(value).Append('\n');
            }
            sb.Append("  GimmickBoost (perRank max / maxRank max / single-star total max): ").Append(GimmickBoostMax)
                .Append(" / ").Append(GimmickBoostRanksMax).Append(" / ").Append(GimmickBoostTotalMax.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  per GimmickParam (perRank max / maxRank max / single-star total max):\n");
            foreach (var kv in GimmickParamValues)
                sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value).Append(" / ").Append(GimmickParamRanks[kv.Key])
                    .Append(" / ").Append(GimmickParamTotals[kv.Key].ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  NativeModifier kind/capProfile (value hundredths max / maxRank max / single-star total hundredths max; unavailable if absent):\n");
            foreach (var kv in NativeValues)
                sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value).Append(" / ").Append(NativeRanks[kv.Key])
                    .Append(" / ").Append(NativeTotals[kv.Key].ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  ScopedModifier scope/parameter/capProfile (amount hundredths, probability ten-thousandths, or target count max / maxRank max / single-star total max in the same units):\n");
            foreach (var kv in ScopedValues)
                sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value).Append(" / ").Append(ScopedRanks[kv.Key])
                    .Append(" / ").Append(ScopedTotals[kv.Key].ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  named shipped gimmicks (read-only references):\n");
            foreach (var kv in Gimmicks) sb.Append("    ").Append(kv.Key).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 拡張旅人の登録簿（ヒーローコネクタ §6-1）。宣言JSONの読み取り・検証・星図の構築はすべてこの中で行い、
    /// Unity にも本体の型にも依存しない。登録は Mod 側の走査とランタイムAPI（Dreamforge.Api.HeroConnector）から。
    /// 1つの宣言の失敗はほかの宣言・Dreamforge 本体に広げない（fail-soft、§4）。
    /// </summary>
    public static partial class ExtendedHeroes
    {
        public const int ApiVersion = 1;

        private static readonly object RegistryLock = new object();
        private static readonly List<ExtendedHeroDef> HeroesInOrder = new List<ExtendedHeroDef>();
        private static readonly Dictionary<string, ExtendedHeroDef> HeroesByKey = new Dictionary<string, ExtendedHeroDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Txt> MemoryNames = new Dictionary<string, Txt>(StringComparer.Ordinal);
        private static ExtendedStarLimits limits;
        private static string limitsFingerprint;

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

        /// <summary>記憶の表示名と登録の問い合わせ（§6-6）。装備・リンク・報酬の生成には入らない。</summary>
        public static bool TryGetMemoryName(string memoryKey, out Txt name)
        {
            lock (RegistryLock) return MemoryNames.TryGetValue(memoryKey, out name);
        }

        /// <summary>登録簿の旅人の近接判定（§6-3）。未登録は false。</summary>
        public static bool IsMelee(string heroKey) => TryGet(heroKey, out var hero) && hero.Melee;

        /// <summary>星IDが拡張旅人の星（"x." 始まり）か。休眠割り当ての判定に使う（§10）。</summary>
        public static bool IsExtendedStarId(string starId) => starId != null && starId.Length > 2 && starId[0] == 'x' && starId[1] == '.';

        /// <summary>§5 の上限。既存9人の星から求め、登録内容が変われば再計算する。</summary>
        public static ExtendedStarLimits Limits()
        {
            lock (RegistryLock)
            {
                string fingerprint = StarClusters.AuthoredRegistryFingerprint;
                if (limits == null || limitsFingerprint != fingerprint)
                {
                    limits = ComputeLimits();
                    limitsFingerprint = fingerprint;
                }
                return limits;
            }
        }

        private static ExtendedStarLimits ComputeLimits()
        {
            var result = new ExtendedStarLimits();
            void Max<TKey>(Dictionary<TKey, int> values, TKey key, int value)
            {
                values.TryGetValue(key, out int best);
                if (value > best) values[key] = value;
            }
            void MaxTotal<TKey>(Dictionary<TKey, decimal> totals, TKey key, decimal perRank, int maxRank)
            {
                totals.TryGetValue(key, out decimal best);
                decimal total = perRank * maxRank;
                if (total > best) totals[key] = total;
            }
            void Inspect(TalentDef t)
            {
                result.RankCostMax = Math.Max(result.RankCostMax, t.RankCost);
                result.RanksMax = Math.Max(result.RanksMax, t.MaxRank);
                if (t.IsKeystone)
                {
                    if (t.Power != Power.None) Max(result.PowerKeystoneCaps, t.Power, t.PowerValue);
                    else if (t.PerRank > 0) Max(result.StatKeystoneCaps, t.Stat, t.PerRank);
                }
                else if (t.IsPowerNode)
                {
                    Max(result.PowerValues, t.RankPower, t.PerRank);
                    Max(result.PowerRanks, t.RankPower, t.MaxRank);
                    MaxTotal(result.PowerTotals, t.RankPower, t.PerRank, t.MaxRank);
                }
                else if (t.PerRank > 0)
                {
                    Max(result.StatValues, t.Stat, t.PerRank);
                    Max(result.StatRanks, t.Stat, t.MaxRank);
                    MaxTotal(result.StatTotals, t.Stat, t.PerRank, t.MaxRank);
                }
                if (t.LinkPerRank != null)
                {
                    var link = t.LinkPerRank;
                    result.LinkValues.TryGetValue(link.Kind, out decimal best);
                    if (link.Value > best) result.LinkValues[link.Kind] = link.Value;
                    Max(result.LinkRanks, link.Kind, t.MaxRank);
                    MaxTotal(result.LinkTotals, link.Kind, link.Value, t.MaxRank);
                }
                if (t.Gimmick != null && t.Mechanism == null && t.EffectChannel == null)
                {
                    // Only standalone shipped definitions can be referenced. No authored mechanism is unwrapped.
                    if (!result.Gimmicks.ContainsKey(t.Id)) result.Gimmicks.Add(t.Id, t.Gimmick.Copy());
                }
                if (t.GimmickBoost > 0)
                {
                    result.GimmickBoostMax = Math.Max(result.GimmickBoostMax, t.GimmickBoost);
                    result.GimmickBoostRanksMax = Math.Max(result.GimmickBoostRanksMax, t.MaxRank);
                    result.GimmickBoostTotalMax = Math.Max(result.GimmickBoostTotalMax, (decimal)t.GimmickBoost * t.MaxRank);
                }
                if (t.GimmickParameter.HasValue)
                {
                    Max(result.GimmickParamValues, t.GimmickParameter.Value, t.GimmickParamAmount);
                    Max(result.GimmickParamRanks, t.GimmickParameter.Value, t.MaxRank);
                    MaxTotal(result.GimmickParamTotals, t.GimmickParameter.Value, t.GimmickParamAmount, t.MaxRank);
                }
                if (t.NativeModifier != null)
                {
                    var n = t.NativeModifier;
                    string key = ExtendedStarLimits.NativeKey(n.Kind, n.CapProfileId);
                    Max(result.NativeValues, key, n.Value.Units);
                    Max(result.NativeRanks, key, t.MaxRank);
                    MaxTotal(result.NativeTotals, key, n.Value.Units, t.MaxRank);
                }
                if (t.ScopedModifier != null)
                {
                    var m = t.ScopedModifier;
                    string key = ExtendedStarLimits.ScopedKey(m.ScopeKind, m.Param, m.CapProfileId);
                    int amount = m.Param == GimmickParam.ExtraTargets ? m.ExtraTargets
                        : m.Param == GimmickParam.Chance ? m.Probability.Units : m.Amount.Units;
                    Max(result.ScopedValues, key, amount);
                    Max(result.ScopedRanks, key, t.MaxRank);
                    MaxTotal(result.ScopedTotals, key, amount, t.MaxRank);
                }
                result.OptionsPerChoiceMax = Math.Max(result.OptionsPerChoiceMax, t.Choices.Count);
                foreach (var option in t.Choices) Inspect(option);
            }
            var heroKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in HeroSigils.CoreBaseline)
            {
                if (t.HeroKey == null) continue;
                heroKeys.Add(t.HeroKey);
                Inspect(t);
                // Preserve the apiVersion 1 Stat caps, including the default Stat of Power nodes.
                if (!t.IsKeystone)
                {
                    Max(result.StatValues, t.Stat, t.PerRank);
                    Max(result.StatRanks, t.Stat, t.MaxRank);
                    MaxTotal(result.StatTotals, t.Stat, t.PerRank, t.MaxRank);
                }
            }
            foreach (var talent in HeroStarRoutes.All) if (heroKeys.Contains(talent.HeroKey)) Inspect(talent);
            foreach (string heroKey in heroKeys)
            {
                int normal = 0, keys = 0, choices = 0;
                var clusters = new HashSet<string>(StringComparer.Ordinal);
                var tree = HeroSigils.TreeFor(heroKey);
                result.StarsMax = Math.Max(result.StarsMax, tree.Count);
                foreach (var t in tree)
                {
                    Inspect(t);
                    if (t.IsKeystone) keys++; else normal++;
                    if (t.IsChoice) choices++;
                    if (t.Cluster != null) clusters.Add(t.Cluster.Id);
                }
                result.NormalStarsMax = Math.Max(result.NormalStarsMax, normal);
                result.KeystonesMax = Math.Max(result.KeystonesMax, keys);
                result.ChoicesMax = Math.Max(result.ChoicesMax, choices);
                result.ClustersMax = Math.Max(result.ClustersMax, clusters.Count);
            }
            void InspectLegacy(ClusterStarDef star)
            {
                if (star.Kind == ClusterStarKind.GimmickBoost)
                {
                    result.GimmickBoostMax = Math.Max(result.GimmickBoostMax, (int)star.Amount);
                    result.GimmickBoostRanksMax = Math.Max(result.GimmickBoostRanksMax, star.MaxRank);
                    result.GimmickBoostTotalMax = Math.Max(result.GimmickBoostTotalMax, (decimal)(int)star.Amount * star.MaxRank);
                }
                if (star.Kind == ClusterStarKind.GimmickParam && star.ScopedModifier == null)
                {
                    Max(result.GimmickParamValues, star.Param, (int)star.Amount);
                    Max(result.GimmickParamRanks, star.Param, star.MaxRank);
                    MaxTotal(result.GimmickParamTotals, star.Param, (int)star.Amount, star.MaxRank);
                }
                if (star.Options != null) foreach (var option in star.Options) InspectLegacy(option);
            }
            foreach (var cluster in StarClusters.All) foreach (var star in cluster.Stars) InspectLegacy(star);
            foreach (var kind in new[] { LinkKind.MemoryDamage, LinkKind.MemoryHaste })
                if (result.LinkValues.TryGetValue(kind, out decimal value))
                {
                    string key = ExtendedStarLimits.NativeKey(kind, "dreamforge.connector.native." + kind);
                    result.NativeValues[key] = ValueUnits.FromPercent(value).Units;
                    result.NativeRanks[key] = result.LinkRanks[kind];
                    result.NativeTotals[key] = result.LinkTotals[kind] * 100m;
                }
            foreach (var kv in result.StatValues)
                Max(result.StatKeystoneCaps, kv.Key, kv.Value * result.StatRanks[kv.Key]);
            return result;
        }

        internal static bool TryGetNativeCapProfile(string id, out NativeStarCapProfile profile)
        {
            profile = null;
            const string prefix = "dreamforge.connector.native.";
            if (id == null || !id.StartsWith(prefix, StringComparison.Ordinal)
                || !Enum.TryParse(id.Substring(prefix.Length), out LinkKind kind)
                || kind != LinkKind.MemoryDamage && kind != LinkKind.MemoryHaste) return false;
            var caps = Limits();
            if (!caps.LinkPerRankMax.TryGetValue(kind, out decimal value) || !caps.LinkMaxRankMax.TryGetValue(kind, out int ranks)) return false;
            profile = new NativeStarCapProfile { Id = id, Kind = kind, Maximum = ValueUnits.FromPercent(value * ranks) };
            return true;
        }

        /// <summary>
        /// 宣言ファイル1つ分（§3-2）を読み、旅人ごとに判定して登録する。戻り値は報告の材料。
        /// 例外を外へ投げない（fail-soft）。warn には "[Dreamforge/Connector]" 付きの1行を渡す。
        /// 同じ key がすでに同じ内容で登録済みのときは、重ねて登録せず受理として返す（走査2回目）。
        /// </summary>
        public static ExtendedHeroFileResult Register(string json, string sourceModId, Action<string> warn)
            => Register(json, sourceModId, warn, null);

        /// <summary>The host supplies a bounded, MOD-relative file reader for optional star manifests.</summary>
        public static ExtendedHeroFileResult Register(string json, string sourceModId, Action<string> warn,
            Func<string, string> readManifest)
        {
            var result = new ExtendedHeroFileResult { SourceModId = sourceModId };
            try
            {
                if (string.IsNullOrWhiteSpace(json) || json.Length > ManifestTextLimit)
                    throw new FormatException("the declaration is empty or exceeds the connector text limit");
                if (!(Json.ParseConnector(json) is JsonObject root))
                    throw new FormatException("the top level must be a JSON object");
                if (!root.TryGet("dreamforgeApi", out var apiObj) || !(apiObj is long api) || api != ApiVersion)
                    throw new FormatException("unsupported dreamforgeApi " + Describe(apiObj));
                if (!root.TryGet("heroes", out var heroesObj) || !(heroesObj is List<object> heroes))
                    throw new FormatException("heroes must be an array");
                foreach (var entry in heroes)
                    result.Heroes.Add(RegisterHero(entry, result.SourceModId, warn, readManifest));
            }
            catch (Exception ex)
            {
                result.FileError = ex.Message;
                Warn(warn, result.SourceModId, null, "rejected: " + result.FileError);
            }
            return result;
        }

        private static ExtendedHeroResult RegisterHero(object entry, string sourceModId, Action<string> warn,
            Func<string, string> readManifest)
        {
            var outcome = new ExtendedHeroResult();
            var provisionalMemories = new List<string>();
            try
            {
                if (!(entry is JsonObject hero)) return Rejected(outcome, warn, "a hero entry must be a JSON object");
                if (!hero.TryGet("key", out var keyObj) || !(keyObj is string key) || !ValidHeroKey(key))
                    return Rejected(outcome, warn, "invalid hero key");
                outcome.Key = key;
                if (HeroSigils.IsCoreTraveler(key)) return Rejected(outcome, warn, "key is one of the nine shipped travelers");
                ExtendedHeroDef previous;
                lock (RegistryLock) HeroesByKey.TryGetValue(key, out previous);
                if (previous != null && previous.SourceModId != sourceModId)
                    return Rejected(outcome, warn, "key is already declared by " + previous.SourceModId + "; the first declaration wins");
                if (!TryReadText(hero, "name", null, out var name) || name == null)
                {
                    string fallback = key.Substring(5);
                    name = new Txt(fallback, fallback, fallback);
                }
                bool melee = hero.TryGet("melee", out var meleeObj) && meleeObj is bool b && b;
                var memories = new Dictionary<string, Txt>(StringComparer.Ordinal);
                if (hero.TryGet("memories", out var memoriesObj) && memoriesObj is List<object> memoryList)
                    foreach (var memory in memoryList) ReadMemory(memory, memories, warn, key);
                if (previous == null)
                    lock (RegistryLock)
                        foreach (var memory in memories)
                            if (!MemoryNames.ContainsKey(memory.Key))
                            {
                                MemoryNames.Add(memory.Key, memory.Value);
                                provisionalMemories.Add(memory.Key);
                            }
                var tree = ReadTree(hero, key, memories, out var layout, out string treeReason);
                string declarationHash = StarClusters.RegistryHash(Json.Write(hero));
                var provisional = new ExtendedHeroDef(key, sourceModId, name, melee, memories, tree, declarationHash);
                // Authored ownership requires a registered traveler. Publish its identity before validating its tree.
                if (previous == null)
                    lock (RegistryLock) HeroesByKey.Add(key, provisional);
                try
                {
                    if (hero.TryGet("starManifest", out var manifestPath) && manifestPath != null)
                    {
                        try
                        {
                            if (!(manifestPath is string path) || readManifest == null)
                                throw new FormatException("starManifest requires a relative path and a loaded source MOD folder");
                            var baseline = CreateManifestRoutes(key, memories);
                            var manifest = ReadManifest(readManifest(path), key, memories, baseline, sourceModId, warn, out var registry);
                            if (manifest.Count == 0) throw new FormatException("no valid authored stars remain");
                            var generated = registry.TreeFor(key);
                            declarationHash = StarClusters.RegistryHash(Json.Write(hero) + "|starManifest|" + StarClusters.Fingerprint(generated) + "|routes|" + ManifestRouteFingerprint(baseline));
                            if (previous == null)
                            {
                                StarClusters.RegisterAuthored(key, manifest, baseline, registry);
                            }
                            tree = generated;
                            layout = null;
                            treeReason = null;
                        }
                        catch (Exception ex)
                        {
                            Warn(warn, sourceModId, key, "starManifest rejected; using starTree: " + ex.Message);
                            declarationHash = StarClusters.RegistryHash(Json.Write(hero));
                        }
                    }
                    if (previous != null)
                    {
                        if (previous.DeclarationHash != declarationHash)
                            return Rejected(outcome, warn, "key already has a different declaration; the first declaration wins");
                        outcome.Accepted = true;
                        outcome.DeclarationHash = declarationHash;
                        return outcome;
                    }
                    var def = new ExtendedHeroDef(key, sourceModId, name, melee, memories, tree, declarationHash);
                    lock (RegistryLock)
                    {
                        HeroesByKey[key] = def;
                        HeroesInOrder.Add(def);
                    }
                    if (treeReason != null)
                    {
                        outcome.GenericTree = true;
                        outcome.Reason = treeReason;
                        Warn(warn, sourceModId, key, "star tree rejected; using the generic tree: " + treeReason);
                    }
                    if (tree != null && !StarClusters.TryGetRegisteredTree(key, out _))
                    {
                        HeroSigils.RegisterExtendedTree(key, tree);
                        if (layout != null) HeroTreeLayout.RegisterExtendedLayout(key, layout);
                    }
                    outcome.Accepted = true;
                    outcome.DeclarationHash = declarationHash;
                    return outcome;
                }
                catch
                {
                    if (previous == null) lock (RegistryLock) HeroesByKey.Remove(key);
                    throw;
                }
            }
            catch (Exception ex) { return Rejected(outcome, warn, "unexpected failure: " + ex.Message); }
            finally
            {
                if (!outcome.Accepted)
                    lock (RegistryLock)
                        foreach (string memory in provisionalMemories) MemoryNames.Remove(memory);
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
        private static IReadOnlyList<TalentDef> ReadTree(JsonObject hero, string heroKey,
            IReadOnlyDictionary<string, Txt> memories, out HeroTreeLayout layout, out string reason)
        {
            layout = null;
            try { return ReadTreeCore(hero, heroKey, memories, out layout, out reason); }
            catch (Exception ex)
            {
                layout = null;
                reason = "invalid star tree: " + ex.Message;
                return null;
            }
        }

        private static IReadOnlyList<TalentDef> ReadTreeCore(JsonObject hero, string heroKey,
            IReadOnlyDictionary<string, Txt> memories, out HeroTreeLayout layout, out string reason)
        {
            layout = null;
            reason = null;
            if (!hero.TryGet("starTree", out var treeObj) || !(treeObj is JsonObject tree)) return null;
            if (!tree.TryGet("nodes", out var nodesObj) || !(nodesObj is List<object> nodes)) return null;
            if (nodes.Count == 0) return null;
            var limits = Limits();
            if (nodes.Count > limits.StarsMax)
            { reason = "the tree declares " + nodes.Count + " stars; the shipped max is " + limits.StarsMax; return null; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var built = new List<TalentDef>(nodes.Count);
            var placements = new Dictionary<string, ExtendedStarPlacement>(StringComparer.Ordinal);
            var clusters = ReadClusters(tree, heroKey, limits);
            string prefix = "x." + heroKey + ".";
            bool customLayout = false;
            int normalStars = 0, keystones = 0, choices = 0;
            foreach (var nodeObj in nodes)
            {
                if (!(nodeObj is JsonObject node)) throw new FormatException("a node must be a JSON object");
                if (!node.TryGet("id", out var idObj) || !(idObj is string id) || !ValidNodeId(id))
                    throw new FormatException("invalid node id (expected [a-z0-9_], up to 32 chars)");
                if (!ids.Add(id)) throw new FormatException("duplicate node id '" + id + "'");
                var talent = ReadNode(node, prefix + id, heroKey, memories, limits, clusters, false);
                if (talent.IsKeystone) keystones++; else normalStars++;
                if (talent.IsChoice) choices++;
                var placement = new ExtendedStarPlacement();
                if (node.TryGet("center", out var centerObj))
                {
                    if (!(centerObj is bool center)) throw new FormatException("center must be boolean");
                    placement.Center = center;
                    customLayout = true;
                }
                if (node.TryGet("requires", out var requiresObj))
                {
                    customLayout = true;
                    placement.Requires = ReadIds(requiresObj, prefix, "requires");
                }
                if (node.TryGet("position", out var positionObj))
                {
                    customLayout = true;
                    if (!(positionObj is JsonObject position)) throw new FormatException("position must be an object");
                    bool hasX = position.TryGet("x", out var xObj), hasY = position.TryGet("y", out var yObj);
                    if (hasX || hasY)
                    {
                        if (!hasX || !hasY || position.TryGet("ring", out _) || position.TryGet("angle", out _)
                            || !TryNumber(xObj, out decimal x) || !TryNumber(yObj, out decimal y))
                            throw new FormatException("position requires numeric x and y, or ring and angle, not both");
                        placement.X = (float)x;
                        placement.Y = (float)y;
                    }
                    else
                    {
                        if (!position.TryGet("ring", out var ringObj) || !(ringObj is long ring) || ring < 1 || ring > int.MaxValue
                            || !position.TryGet("angle", out var angleObj) || !TryNumber(angleObj, out decimal angle))
                            throw new FormatException("position requires a positive integer ring and a numeric angle");
                        placement.Ring = (int)ring;
                        placement.Angle = angle;
                    }
                }
                placements.Add(talent.Id, placement);
                built.Add(talent);
            }
            if (normalStars > limits.NormalStarsMax)
                throw new FormatException("the tree declares " + normalStars + " normal stars; the shipped max is " + limits.NormalStarsMax);
            if (keystones > limits.KeystonesMax)
                throw new FormatException("the tree declares " + keystones + " keystones; the shipped max is " + limits.KeystonesMax);
            if (choices > limits.ChoicesMax)
                throw new FormatException("the tree declares " + choices + " choices; the shipped max is " + limits.ChoicesMax);
            foreach (var cluster in clusters.Values)
            {
                var stars = new List<ClusterStarDef>();
                foreach (var talent in built) if (talent.Cluster == cluster) stars.Add(talent.ClusterStar);
                if (stars.Count == 0) throw new FormatException("empty cluster '" + cluster.Id + "'");
                cluster.Stars = stars.AsReadOnly();
            }
            ValidateExtendedPrerequisites(built);
            ValidateExtendedEffects(built);
            FractionalScopedModifiers.ValidateTree(built);
            // Cluster and dream-ring metadata are rendered by the existing presentation, with connector edges/positions.
            if (customLayout || clusters.Count > 0 || built.Exists(t => t.IsDreamRing))
            {
                layout = HeroTreeLayout.CreateExtended(built, placements, out reason);
                if (layout == null) return null;
            }
            if (built.Exists(t => t.AuthoredStar != null))
                ValidateExtendedPrerequisites(built, layout ?? HeroTreeLayout.ForTalents(built));
            return built.AsReadOnly();
        }

        private static Dictionary<string, StarClusterDef> ReadClusters(JsonObject tree, string heroKey, ExtendedStarLimits limits)
        {
            var result = new Dictionary<string, StarClusterDef>(StringComparer.Ordinal);
            if (!tree.TryGet("clusters", out var raw)) return result;
            if (!(raw is List<object> list) || list.Count > limits.ClustersMax)
                throw new FormatException("clusters must be an array with at most " + limits.ClustersMax + " entries");
            foreach (var item in list)
            {
                if (!(item is JsonObject cluster) || !cluster.TryGet("id", out var idObj) || !(idObj is string id) || !ValidNodeId(id))
                    throw new FormatException("invalid cluster id");
                TryReadText(cluster, "name", id, out var name);
                var region = cluster.TryGet("region", out var regionObj) ? ReadEnum<ClusterRegionKind>(regionObj, "cluster region") : ClusterRegionKind.Outer;
                var shape = cluster.TryGet("shape", out var shapeObj) ? ReadEnum<ClusterShape>(shapeObj, "cluster shape") : ClusterShape.Chain;
                if (result.ContainsKey(id)) throw new FormatException("duplicate cluster '" + id + "'");
                result.Add(id, new StarClusterDef { Id = "x." + heroKey + ".cluster." + id, HeroKey = heroKey,
                    Name = name ?? new Txt(id, id, id), Region = new ClusterRegion { Kind = region, Id = ReadOptionalString(cluster, "regionId") }, Shape = shape });
            }
            return result;
        }

        private static TalentDef ReadNode(JsonObject node, string id, string heroKey, IReadOnlyDictionary<string, Txt> memories,
            ExtendedStarLimits limits, Dictionary<string, StarClusterDef> clusters, bool option)
        {
            try { return ReadNodeCore(node, id, heroKey, memories, limits, clusters, option); }
            catch (Exception error) { throw new FormatException("node '" + id + "': " + error.Message, error); }
        }

        private static TalentDef ReadNodeCore(JsonObject node, string id, string heroKey, IReadOnlyDictionary<string, Txt> memories,
            ExtendedStarLimits limits, Dictionary<string, StarClusterDef> clusters, bool option)
        {
            TryReadText(node, "name", id.Substring(id.LastIndexOf('.') + 1), out var name);
            name = name ?? new Txt(id, id, id);
            bool keystone = ReadBool(node, "keystone"), dreamRing = ReadBool(node, "isDreamRing");
            int tier = ReadInteger(node, "tier", 1, 1, 2);
            int rankCost = ReadInteger(node, "rankCost", keystone ? Content.KeystoneCost : 1, 1, limits.RankCostMax);
            bool choice = node.TryGet("choices", out var choicesObj);
            bool hasStat = node.TryGet("stat", out var statObj), hasPower = node.TryGet("power", out var powerObj);
            bool hasLink = node.TryGet("link", out var linkObj), hasNative = node.TryGet("nativeModifier", out var nativeObj);
            bool hasScoped = node.TryGet("scopedModifier", out var scopedObj), hasBoost = node.TryGet("gimmickBoost", out var boostObj);
            bool hasParam = node.TryGet("gimmickParam", out var paramObj), hasGimmick = node.TryGet("gimmick", out var gimmickObj);
            int effects = (hasStat ? 1 : 0) + (hasPower ? 1 : 0) + (hasLink ? 1 : 0) + (hasNative ? 1 : 0)
                + (hasScoped ? 1 : 0) + (hasBoost ? 1 : 0) + (hasParam ? 1 : 0) + (choice ? 1 : 0);
            if (effects != 1 && !(effects == 0 && hasGimmick))
                throw new FormatException("specify one effect or choices (a named gimmick may accompany stat, link or power)");
            if (hasGimmick && (choice || hasNative || hasScoped || hasBoost || hasParam || keystone))
                throw new FormatException("a named gimmick cannot accompany this effect");
            if (option && (choice || keystone || node.TryGet("id", out _) || node.TryGet("cluster", out _)
                || node.TryGet("requires", out _) || node.TryGet("position", out _) || node.TryGet("center", out _)
                || node.TryGet("requiredStars", out _) || node.TryGet("requiredAnyStars", out _)))
                throw new FormatException("a choice option cannot declare a choice, keystone, id, cluster, requires or position");
            if (keystone && (!hasStat && !hasPower || option)) throw new FormatException("keystones require stat or power");
            string memory = null;
            if (node.TryGet("memory", out var memoryObj)) memory = ReadMemoryReference(memoryObj, memories);
            if ((hasLink || hasNative || hasScoped || hasBoost || hasParam || hasGimmick) && memory == null)
                throw new FormatException("this effect requires memory");
            int maxRank = keystone ? 1 : ReadInteger(node, "maxRank", null, 1, limits.RanksMax);
            TalentDef talent;
            ClusterStarKind inferred;
            if (choice)
            {
                if (!(choicesObj is List<object> options) || options.Count < 2 || options.Count > limits.OptionsPerChoiceMax)
                    throw new FormatException("choices requires 2.." + limits.OptionsPerChoiceMax + " non-choice options");
                var choices = new List<TalentDef>();
                foreach (var entry in options)
                {
                    if (!(entry is JsonObject definition)) throw new FormatException("a choice option must be an object");
                    var selected = ReadNode(definition, id, heroKey, memories, limits, clusters, true);
                    if (selected.MaxRank != maxRank) throw new FormatException("choice options must have the parent's maxRank");
                    if (definition.TryGet("rankCost", out _) && selected.RankCost != rankCost) throw new FormatException("choice options must use the parent rankCost");
                    selected.RankCost = rankCost;
                    selected.ClusterStar.RankCost = rankCost;
                    choices.Add(selected);
                }
                talent = new TalentDef(id, Line.Offense, name, default(Stat), 0, maxRank) { Choices = choices.AsReadOnly() };
                inferred = ClusterStarKind.Choice;
            }
            else if (hasStat || hasPower)
            {
                Stat stat = hasStat ? ReadEnum<Stat>(statObj, "stat") : default;
                Power power = hasPower ? ReadEnum<Power>(powerObj, "power") : Power.None;
                var valueCaps = hasStat ? limits.StatValues : null;
                int valueMax = hasStat ? Lookup(keystone ? limits.StatKeystoneCaps : valueCaps, stat, "stat")
                    : Lookup(keystone ? limits.PowerKeystoneCaps : limits.PowerValues, power, "power");
                int amount = ReadInteger(node, keystone ? "value" : "perRank", null, 1, valueMax);
                if (keystone)
                {
                    TryReadText(node, "description", null, out var description);
                    if (hasStat && description == null) throw new FormatException("a Stat keystone requires a description");
                    talent = hasStat ? new TalentDef(id, Line.Offense, name, stat, amount, description)
                        : new TalentDef(id, Line.Offense, name, power, amount, description);
                    inferred = ClusterStarKind.Keystone;
                }
                else
                {
                    int ranks = hasStat ? Lookup(limits.StatRanks, stat, "stat") : Lookup(limits.PowerRanks, power, "power");
                    if (maxRank > ranks) throw new FormatException("maxRank exceeds the shipped max " + ranks);
                    decimal totalCap = hasStat ? Lookup(limits.StatTotals, stat, "stat") : Lookup(limits.PowerTotals, power, "power");
                    ValidateTotal(amount, maxRank, totalCap, hasStat ? stat.ToString() : power.ToString());
                    talent = hasStat ? new TalentDef(id, Line.Offense, name, stat, amount, maxRank)
                        : new TalentDef(id, Line.Offense, name, power, amount, maxRank);
                    inferred = hasPower ? ClusterStarKind.Notable : ClusterStarKind.Stat;
                }
            }
            else if (hasLink)
            {
                var kind = ReadEnum<LinkKind>(linkObj, "link");
                decimal cap = Lookup(limits.LinkValues, kind, "link");
                if (!node.TryGet("perRank", out var raw) || !TryNumber(raw, out decimal value) || value <= 0 || value > cap
                    || kind != LinkKind.MemoryDamage && value != decimal.Truncate(value))
                    throw new FormatException("invalid perRank for " + kind + "; shipped max is " + cap.ToString(CultureInfo.InvariantCulture));
                if (maxRank > limits.LinkRanks[kind]) throw new FormatException("maxRank exceeds the shipped max for " + kind);
                ValidateTotal(value, maxRank, limits.LinkTotals[kind], kind.ToString());
                talent = new TalentDef(id, Line.Offense, name, new LinkDef { Kind = kind, Value = value, Requires = new[] { memory } }, maxRank);
                inferred = kind == LinkKind.MemoryHaste ? ClusterStarKind.MemoryHaste : kind == LinkKind.MemoryDamage ? ClusterStarKind.MemoryDamage : ClusterStarKind.Notable;
            }
            else
            {
                talent = new TalentDef(id, Line.Offense, name, default(Stat), 0, maxRank);
                inferred = ClusterStarKind.Notable;
                if (hasNative)
                {
                    if (!(nativeObj is JsonObject native)) throw new FormatException("nativeModifier must be an object");
                    var kind = native.TryGet("kind", out var raw) ? ReadEnum<LinkKind>(raw, "native kind") : LinkKind.MemoryDamage;
                    string profile = ReadOptionalString(native, "capProfile") ?? "dreamforge.connector.native." + kind;
                    string capKey = ExtendedStarLimits.NativeKey(kind, profile);
                    int value = ReadInteger(native, "valueUnits", null, 1, Lookup(limits.NativeValues, capKey, "native kind/capProfile"));
                    if (maxRank > limits.NativeRanks[capKey]) throw new FormatException("nativeModifier maxRank exceeds the shipped max");
                    ValidateTotal(value, maxRank, limits.NativeTotals[capKey], "nativeModifier " + capKey);
                    talent.NativeModifier = new NativeMemoryModifierDef { Memory = memory, Kind = kind, Value = new ValueUnits(value), CapProfileId = profile };
                    inferred = kind == LinkKind.MemoryHaste ? ClusterStarKind.MemoryHaste : ClusterStarKind.MemoryDamage;
                }
                if (hasScoped)
                {
                    if (!(scopedObj is JsonObject scoped)) throw new FormatException("scopedModifier must be an object");
                    var scope = scoped.TryGet("scope", out var raw) ? ReadEnum<ScopeKind>(raw, "scope") : ScopeKind.Memory;
                    GimmickParam? param = scoped.TryGet("param", out var parameter) ? ReadEnum<GimmickParam>(parameter, "scoped param") : (GimmickParam?)null;
                    string profile = ReadOptionalString(scoped, "capProfile");
                    string capKey = ExtendedStarLimits.ScopedKey(scope, param, profile);
                    int cap = Lookup(limits.ScopedValues, capKey, "scope/param/capProfile");
                    int amount = ReadInteger(scoped, "amountUnits", 0, 0, cap);
                    int probability = ReadInteger(scoped, "probabilityUnits", 0, 0, cap);
                    int extra = ReadInteger(scoped, "extraTargets", 0, 0, cap);
                    if (maxRank > limits.ScopedRanks[capKey]) throw new FormatException("scopedModifier maxRank exceeds the shipped max");
                    int perRank = param == GimmickParam.ExtraTargets ? extra : param == GimmickParam.Chance ? probability : amount;
                    ValidateTotal(perRank, maxRank, limits.ScopedTotals[capKey], "scopedModifier " + capKey);
                    var targets = scoped.TryGet("targets", out var targetObj) ? ReadIds(targetObj, "x." + heroKey + ".", "scoped targets") : Array.Empty<string>();
                    var effectsList = new List<GimmickEffect>();
                    if (scoped.TryGet("effects", out var effectObj))
                    {
                        if (!(effectObj is List<object> list)) throw new FormatException("scoped effects must be an array");
                        foreach (var effect in list) effectsList.Add(ReadEnum<GimmickEffect>(effect, "target effect"));
                    }
                    talent.ScopedModifier = new ScopedModifierDef { ScopeKind = scope, ScopeMemory = memory, Param = param,
                        TargetEffectIds = targets, TargetEffects = effectsList.ToArray(), Amount = new ModifierUnits(amount),
                        Probability = new ProbabilityUnits(probability), ExtraTargets = extra, CapProfileId = profile };
                    inferred = param.HasValue ? ClusterStarKind.GimmickParam : ClusterStarKind.GimmickBoost;
                }
                if (hasBoost)
                {
                    if (!(boostObj is long boost) || boost < 1 || boost > limits.GimmickBoostMax || maxRank > limits.GimmickBoostRanksMax)
                        throw new FormatException("gimmickBoost exceeds the shipped value/rank max");
                    ValidateTotal(boost, maxRank, limits.GimmickBoostTotalMax, "gimmickBoost");
                    talent.GimmickBoost = (int)boost;
                    inferred = ClusterStarKind.GimmickBoost;
                }
                if (hasParam)
                {
                    var param = ReadEnum<GimmickParam>(paramObj, "gimmickParam");
                    int amount = ReadInteger(node, "perRank", null, 1, Lookup(limits.GimmickParamValues, param, "gimmickParam"));
                    if (maxRank > limits.GimmickParamRanks[param]) throw new FormatException("gimmickParam maxRank exceeds the shipped max");
                    ValidateTotal(amount, maxRank, limits.GimmickParamTotals[param], "gimmickParam " + param);
                    talent.GimmickParameter = param;
                    talent.GimmickParamAmount = amount;
                    inferred = ClusterStarKind.GimmickParam;
                }
            }
            if (hasGimmick)
            {
                if (!(gimmickObj is string reference) || !limits.Gimmicks.TryGetValue(reference, out var definition))
                    throw new FormatException("gimmick must name a shipped standalone GimmickDef star");
                if (memory.StartsWith("St_M_", StringComparison.Ordinal)) throw new FormatException("movement memories cannot trigger gimmicks");
                talent.Gimmick = definition.Copy();
            }
            if (node.TryGet("requiredStars", out var allObj) || node.TryGet("requiredAnyStars", out _))
            {
                string prefix = "x." + heroKey + ".";
                talent.AuthoredStar = new AuthoredStarDef { HeroKey = heroKey, LocalStarId = id, RetainedLegacy = true,
                    RequiredStarIds = allObj == null ? Array.Empty<string>() : ReadIds(allObj, prefix, "requiredStars"),
                    RequiredAnyStarIds = node.TryGet("requiredAnyStars", out var anyObj) ? ReadIds(anyObj, prefix, "requiredAnyStars") : Array.Empty<string>() };
            }
            talent.HeroKey = heroKey;
            talent.Tier = tier;
            talent.RankCost = rankCost;
            talent.RouteMemory = memory;
            talent.RouteOrder = ReadInteger(node, "routeOrder", 0, 0, limits.StarsMax);
            talent.IsDreamRing = dreamRing;
            StarClusterDef cluster = null;
            if (node.TryGet("cluster", out var clusterObj)
                && (!(clusterObj is string clusterId) || !clusters.TryGetValue(clusterId, out cluster)))
                throw new FormatException("cluster must name an entry in starTree.clusters");
            talent.Cluster = cluster;
            talent.ClusterOrder = ReadInteger(node, "clusterOrder", 0, 0, limits.StarsMax);
            bool explicitKind = node.TryGet("kind", out var kindObj);
            var starKind = explicitKind ? ReadEnum<ClusterStarKind>(kindObj, "kind") : inferred;
            if (starKind != inferred && !(hasLink && starKind == ClusterStarKind.Notable))
                throw new FormatException("kind does not match the effect");
            if (cluster != null || choice || explicitKind || option)
                talent.ClusterStar = new ClusterStarDef { Kind = starKind, Name = name, Memory = memory,
                    Amount = talent.LinkPerRank?.Value ?? (talent.GimmickBoost > 0 ? talent.GimmickBoost : talent.GimmickParameter.HasValue ? talent.GimmickParamAmount : keystone && hasPower ? talent.PowerValue : talent.PerRank),
                    Param = talent.GimmickParameter ?? talent.ScopedModifier?.Param ?? default,
                    Power = keystone ? talent.Power : talent.RankPower, Stat = talent.Stat, MaxRank = maxRank, RankCost = rankCost,
                    NativeModifier = talent.NativeModifier, ScopedModifier = talent.ScopedModifier, Gimmick = talent.Gimmick,
                    Options = choice ? new List<ClusterStarDef>(System.Linq.Enumerable.Select(talent.Choices, t => t.ClusterStar)) : null };
            if (talent.IsChoice)
                foreach (var selected in talent.Choices) { selected.Cluster = cluster; selected.ClusterOrder = talent.ClusterOrder; }
            return talent;
        }

        private static void ValidateTotal(decimal perRank, int maxRank, decimal cap, string kind)
        {
            decimal total = perRank * maxRank;
            if (total > cap)
                throw new FormatException(kind + " total (perRank * maxRank) " + total.ToString(CultureInfo.InvariantCulture)
                    + " exceeds the shipped single-star total max " + cap.ToString(CultureInfo.InvariantCulture));
        }

        private static void ValidateExtendedEffects(IReadOnlyList<TalentDef> tree)
        {
            var all = new List<TalentDef>();
            foreach (var t in tree) { if (t.IsChoice) all.AddRange(t.Choices); else all.Add(t); }
            foreach (var t in all)
            {
                if (t.GimmickBoost == 0 && !t.GimmickParameter.HasValue) continue;
                bool found = false;
                foreach (var target in all)
                    if (target.RouteMemory == t.RouteMemory && target.Gimmick != null
                        && (!t.GimmickParameter.HasValue || Gimmicks.SupportsParameter(target.Gimmick, t.GimmickParameter.Value))) found = true;
                if (!found) throw new FormatException("gimmick modifier has no matching recipient: " + t.Id);
            }
        }

        private static void ValidateExtendedPrerequisites(IReadOnlyList<TalentDef> tree, HeroTreeLayout layout = null)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var talent in tree) ids.Add(talent.Id);
            var attainable = new HashSet<string>(StringComparer.Ordinal) { "tree.start" };
            foreach (var talent in tree)
            {
                var authored = talent.AuthoredStar;
                if (authored == null) continue;
                foreach (string id in System.Linq.Enumerable.Concat(authored.RequiredStarIds, authored.RequiredAnyStarIds))
                    if (id == talent.Id || !ids.Contains(id))
                        throw new FormatException("unknown or self-referencing prerequisite: " + id);
            }
            var nodes = layout == null ? null : System.Linq.Enumerable.ToDictionary(layout.Nodes, n => n.Id);
            bool progress;
            do
            {
                progress = false;
                foreach (var talent in tree)
                {
                    if (attainable.Contains(talent.Id)) continue;
                    var authored = talent.AuthoredStar;
                    if (authored != null && (!System.Linq.Enumerable.All(authored.RequiredStarIds, attainable.Contains)
                        || authored.RequiredAnyStarIds.Count > 0 && !System.Linq.Enumerable.Any(authored.RequiredAnyStarIds, attainable.Contains))) continue;
                    if (nodes != null && !System.Linq.Enumerable.Any(nodes[talent.Id].Neighbors, i => attainable.Contains(layout.Nodes[i].Id))) continue;
                    progress |= attainable.Add(talent.Id);
                }
            } while (progress);
            if (attainable.Count != tree.Count + 1)
                throw new FormatException("allocation prerequisites and connections have no attainable path");
        }

        private static T ReadEnum<T>(object raw, string field) where T : struct
        {
            if (!(raw is string text) || !Enum.TryParse(text, out T value) || value.ToString() != text || !Enum.IsDefined(typeof(T), value))
                throw new FormatException("invalid " + field);
            return value;
        }

        private static TValue Lookup<TKey, TValue>(Dictionary<TKey, TValue> values, TKey key, string field)
        {
            if (!values.TryGetValue(key, out var value)) throw new FormatException(field + " is not used by shipped stars");
            return value;
        }

        private static int ReadInteger(JsonObject node, string field, int? fallback, int minimum, int maximum)
        {
            if (!node.TryGet(field, out var raw))
            {
                if (fallback.HasValue) return fallback.Value;
                throw new FormatException(field + " is required");
            }
            if (!(raw is long value) || value < minimum || value > maximum)
                throw new FormatException(field + " must be " + minimum + ".." + maximum);
            return (int)value;
        }

        private static bool ReadBool(JsonObject node, string field)
        {
            if (!node.TryGet(field, out var raw)) return false;
            if (!(raw is bool value)) throw new FormatException(field + " must be boolean");
            return value;
        }

        private static string ReadOptionalString(JsonObject node, string field)
        {
            if (!node.TryGet(field, out var raw)) return null;
            if (raw == null) return null;
            if (!(raw is string value)) throw new FormatException(field + " must be a string");
            return value;
        }

        private static string ReadMemoryReference(object raw, IReadOnlyDictionary<string, Txt> memories)
        {
            if (!(raw is string memory) || !memories.ContainsKey(memory) && !Links.IsBuiltinMemory(memory))
                throw new FormatException("memory must be declared by this traveler or be a shipped memory");
            return memory;
        }

        private static string[] ReadIds(object raw, string prefix, string field)
        {
            if (!(raw is List<object> list)) throw new FormatException(field + " must be an array of ids");
            var result = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                if (!(entry is string id) || !ValidNodeId(id)) throw new FormatException("invalid " + field + " id");
                if (unique.Add(id)) result.Add(prefix + id);
            }
            return result.ToArray();
        }

        private static bool ValidNodeId(string id)
        {
            if (id.Length == 0 || id.Length > 32) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }

        private static bool TryNumber(object value, out decimal number)
        {
            if (value is long integer) { number = integer; return true; }
            if (value is ConnectorNumber token) return token.TryDecimal(out number);
            number = 0;
            return false;
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
