using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 1キャラ分の「今回の強さ」。装着中の遺物・専門化・夢の深度から毎回組み立てる。
    /// ゲームへ渡すのはこの結果だけで、能力補正そのものは保存しない（計画書 付録C）。
    /// </summary>
    public sealed class Build
    {
        public SortedDictionary<Stat, int> Stats { get; } = new SortedDictionary<Stat, int>();
        public SortedDictionary<Power, int> Powers { get; } = new SortedDictionary<Power, int>();
        /// <summary>Conditional power values before awakening, used for the shared 120% budget. Missing means unawakened.</summary>
        public SortedDictionary<Power, int> ConditionalBasePowers { get; } = new SortedDictionary<Power, int>();
        /// <summary>系統ごとの装着数（2以上でセット効果）。表示用で、通信には含めない。</summary>
        public SortedDictionary<Line, int> Lines { get; } = new SortedDictionary<Line, int>();
        /// <summary>セット遺物の装着数（表示用）。</summary>
        public SortedDictionary<string, int> Sets { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>組（小セット）の装着数（表示用・v1.32）。SetDef の集計とは別。</summary>
        public SortedDictionary<string, int> MiniSets { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>遺物と星の連携。装着条件はホストで判定し、常時の能力値には加えない。</summary>
        public List<LinkDef> Links { get; } = new List<LinkDef>();
        /// <summary>振ったルートの星が持つ、記憶に反応する仕掛け。</summary>
        public List<GimmickEntry> Gimmicks { get; } = new List<GimmickEntry>();
        /// <summary>橋と両隣の星を取得した合わせ技。記憶の装備条件は各イベントで判定する。</summary>
        public List<PairComboEntry> PairCombos { get; } = new List<PairComboEntry>();
        public List<NativeMemoryModifierEntry> NativeModifiers { get; } = new List<NativeMemoryModifierEntry>();
        public List<AuthoredMechanismEntry> Mechanisms { get; } = new List<AuthoredMechanismEntry>();
        /// <summary>v1.32 B：遠征を通して成長する仕組み（修飾を反映済み）。通信では空なら節ごと省く。</summary>
        public List<RunGrowthEntry> RunGrowths { get; } = new List<RunGrowthEntry>();
        public SortedDictionary<string, int> MechanismEndpointRanks { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public KeystoneDefinition SelectedKeystone { get; set; }
        /// <summary>Set only by C15's dependency-aware evaluation; records which stars combine into which outputs.</summary>
        internal StarDependencies Dependencies { get; set; }
        internal Dictionary<string, string[]> ScopedWireRecords { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public int Heat { get; set; }
        /// <summary>夢の圧へ送る進行度。欠けている旧データは夢1・星0。</summary>
        public int DreamLevel { get; set; } = 1;
        public int SpentStarPoints { get; set; }

        public int Get(Stat s) => Stats.TryGetValue(s, out int v) ? v : 0;
        public int Get(Power p) => Powers.TryGetValue(p, out int v) ? v : 0;
        public int ConditionalBase(Power p) => ConditionalBasePowers.TryGetValue(p, out int v) ? v : Get(p);

        /// <summary>潜行1段ごとの被ダメージ増加（%）。本体 Limbo の「被ダメージ増加」と同じ表現。</summary>
        public const int DamageTakenPerDelvePct = 6;

        /// <summary>被ダメージの倍率（1.0 = 増減なし）。</summary>
        public float DamageTakenMultiplier => 1f + DamageTakenPerDelvePct * Heat / 100f;

        public static Build Compute(Profile p, string heroKey, int heat, IEnumerable<Pact> pacts = null, int dailyId = 0)
            => ComputeTree(p, heroKey, heat, HeroSigils.TreeFor(heroKey), HeroTreeLayout.ForHero(heroKey), pacts, dailyId);

        /// <summary>Runs the production build pipeline against an explicitly generated, connected tree.</summary>
        public static Build ComputeForTree(Profile p, string heroKey, int heat, IReadOnlyList<TalentDef> tree)
            => ComputeTree(p, heroKey, heat, tree, HeroTreeLayout.ForTalents(tree), null, 0);

        public static Build ComputeForTree(Profile p, string heroKey, int heat, IReadOnlyList<TalentDef> tree,
            HeroState allocation, HeroState reachability = null, HeroTreeLayout layout = null)
            => ComputeTree(p, heroKey, heat, tree, layout ?? HeroTreeLayout.ForTalents(tree), null, 0, allocation, reachability);

        /// <summary>
        /// Identical to the public overload for a tree the caller has already validated, whose id table it owns and whose
        /// reachability snapshot (for exactly <paramref name="reachability"/>) it has already taken. Used by C15, which evaluates
        /// the same tree and the same reachability state hundreds of times per preview.
        /// </summary>
        internal static Build ComputeForValidatedTree(Profile p, string heroKey, IReadOnlyList<TalentDef> tree,
            HeroState allocation, HeroState reachability, HeroTreeLayout layout,
            Dictionary<string, TalentDef> definitions, bool[] reachabilitySnapshot, StarDependencies dependencies = null)
            => ComputeTree(p, heroKey, 0, tree, layout, null, 0, allocation, reachability, definitions, reachabilitySnapshot, dependencies);

        private static Build ComputeTree(Profile p, string heroKey, int heat, IReadOnlyList<TalentDef> tree,
            HeroTreeLayout layout, IEnumerable<Pact> pacts, int dailyId, HeroState allocation = null, HeroState reachability = null,
            Dictionary<string, TalentDef> validatedDefinitions = null, bool[] reachabilitySnapshot = null,
            StarDependencies dependencies = null)
        {
            var h = allocation ?? p.Hero(heroKey);
            Dictionary<string, TalentDef> definitions = validatedDefinitions;
            if (definitions == null)
            {
                FractionalScopedModifiers.ValidateTree(tree);
                definitions = new Dictionary<string, TalentDef>(StringComparer.Ordinal);
                foreach (var talent in tree) definitions.Add(talent.Id, talent);
            }
            var reachabilityState = reachability ?? h;
            var reachable = reachabilitySnapshot ?? layout.ReachabilitySnapshot(reachabilityState);
            bool Unlocked(TalentDef talent) => Rules.BelongsTo(talent, heroKey) && layout.CanReach(reachabilityState, talent, reachable);
            long spent = h.Keystone != null && definitions.TryGetValue(h.Keystone, out var selectedKey)
                ? selectedKey.KeystoneDefinition?.Cost ?? Content.KeystoneCost : 0;
            foreach (var allocated in h.Talents)
                if (definitions.TryGetValue(allocated.Key, out var talent))
                    spent += (long)Math.Max(0, allocated.Value) * talent.RankCost;
            if (spent > StarProgression.MaxSpendablePoints)
                throw new InvalidOperationException("The build exceeds the star point budget.");
            var b = new Build
            {
                Heat = Loot.ClampHeat(heat),
                DreamLevel = Math.Max(1, Math.Min(Content.MaxDreamLevel, p.DreamLevel)),
                SpentStarPoints = (int)spent,
                Dependencies = dependencies,
            };
            var rawStats = new Dictionary<Stat, int>();
            var rawPowers = new Dictionary<Power, int>();
            var awakenGains = new Dictionary<Power, int>();
            var basePowers = new Dictionary<Power, decimal>();
            var equippedLinks = new Dictionary<string, (LinkDef Link, decimal Base, decimal Scaled)>(StringComparer.Ordinal);
            void AddPower(Power power, int value)
            {
                Add(rawPowers, power, value);
                basePowers.TryGetValue(power, out decimal current);
                basePowers[power] = current + value;
            }
            var selectedTalents = new List<KeyValuePair<TalentDef, int>>();
            foreach (var kv in h.Talents)
            {
                if (kv.Value <= 0 || !definitions.TryGetValue(kv.Key, out var talent) || talent.IsKeystone
                    || !Unlocked(talent)) continue;
                int rank = Math.Min(kv.Value, talent.MaxRank);
                if (talent.IsChoice)
                {
                    if (!h.TalentChoices.TryGetValue(talent.Id, out int choice) || choice < 0 || choice >= talent.Choices.Count)
                        throw new InvalidOperationException("An allocated choice star requires a valid selection: " + talent.Id);
                    talent = talent.Choices[choice];
                    dependencies?.Alias(talent.Id, kv.Key);
                }
                selectedTalents.Add(new KeyValuePair<TalentDef, int>(talent, rank));
            }

            foreach (string uid in h.Equipped)
            {
                var r = p.FindStash(uid);
                if (r == null) continue;
                foreach (var s in r.EffectiveStats()) Add(rawStats, s.Stat, s.Value);
                for (int i = 0; i < r.Powers.Count; i++)
                {
                    var pw = r.Powers[i];
                    int value = Relic.Scale(pw.Value, Content.EnhancePowerScalePct(r.Enhance));
                    if (r.Awakened) value = (int)((long)value * Content.AwakenPowerPctAt(r.AwakenLevel) / 100);
                    Add(rawPowers, pw.Power, value);
                    // The saved first power already includes the one-time +20 bonus.
                    decimal basis = i == 0 && (r.MilestonePowerApplied || r.EnhanceMilestones >= 5)
                        ? pw.Value * 100m / Content.LimitBreakPowerPct : pw.Value;
                    basePowers.TryGetValue(pw.Power, out decimal current);
                    basePowers[pw.Power] = current + basis;
                }
                if (r.Awakened)
                    foreach (var pw in r.Powers)
                    {
                        if (!NewPowersV129.IsConditionalAttribute(pw.Power)) continue;
                        int before = Relic.Scale(pw.Value, Content.EnhancePowerScalePct(r.Enhance));
                        int after = (int)((long)before * Content.AwakenPowerPctAt(r.AwakenLevel) / 100);
                        Add(awakenGains, pw.Power, after - before);
                    }
                // Equipment link bases share a cap only with matching equipment conditions.
                var link = r.Link;
                if (link != null && global::SodRpg.Core.Game.Links.Validate(link))
                {
                    decimal scaled = link.Value * Content.AwakenPowerPctAt(r.AwakenLevel) / 100m;
                    if (link.Kind == LinkKind.MemorySurge)
                    {
                        // Surge sources own separate windows; they never add together.
                        b.Links.Add(new LinkDef
                        {
                            Requires = link.Requires, Kind = link.Kind,
                            ValueMilli = CappedLinkMilli(link.Value, scaled,
                                global::SodRpg.Core.Game.Links.EquippedCap(link.Kind, link.Requires.Length)),
                        });
                    }
                    else
                    {
                        string linkKey = BuildAggregation.LinkKey(link);
                        equippedLinks.TryGetValue(linkKey, out var total);
                        equippedLinks[linkKey] = (link, total.Base + link.Value, total.Scaled + scaled);
                    }
                }
                b.Lines.TryGetValue(r.Base.Line, out int n);
                b.Lines[r.Base.Line] = n + 1;
            }
            foreach (var total in equippedLinks.Values)
                b.Links.Add(new LinkDef
                {
                    Requires = total.Link.Requires, Kind = total.Link.Kind,
                    ValueMilli = CappedLinkMilli(total.Base, total.Scaled,
                        global::SodRpg.Core.Game.Links.EquippedCap(total.Link.Kind, total.Link.Requires.Length)),
                });
            foreach (var kv in b.Lines)
                foreach (var s in Content.SetBonus(kv.Key, kv.Value)) Add(rawStats, s.Stat, s.Value);
            // 同じ部位（固有品ID）を重複して数えない。6つ装着は6種類の部位がそろった時だけ有効。
            var setPieces = new Dictionary<string, HashSet<string>>();
            foreach (string uid in h.Equipped)
            {
                var r = p.FindStash(uid);
                if (r?.UniqueId == null || !Content.TryGetUnique(r.UniqueId, out var u) || u.SetId == null) continue;
                if (!setPieces.TryGetValue(u.SetId, out var ids)) setPieces[u.SetId] = ids = new HashSet<string>(StringComparer.Ordinal);
                ids.Add(u.Id);
            }
            foreach (var sp in setPieces)
            {
                var kv = new KeyValuePair<string, int>(sp.Key, sp.Value.Count);
                b.Sets[kv.Key] = kv.Value;
                var set = Content.GetSet(kv.Key);
                if (set == null) continue;
                if (kv.Value >= 2) foreach (var s in set.TwoPiece) Add(rawStats, s.Stat, s.Value);
                if (kv.Value >= 3) foreach (var pw in set.ThreePiece) AddPower(pw.Power, pw.Value);
                if (kv.Value >= 6 && set.HasSixPiece) foreach (var pw in set.SixPiece) AddPower(pw.Power, pw.Value);
            }
            // 組（小セット・v1.32 設計 3.2/4）。同じ組の別々の銘品だけを数え、SetDef の集計とは別に足す（重複して数えない）。
            var miniPieces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string uid in h.Equipped)
            {
                var r = p.FindStash(uid);
                if (r?.NamedId == null || !NamedItems.TryGetNamed(r.NamedId, out var named) || named.MiniSetId == null) continue;
                if (!miniPieces.TryGetValue(named.MiniSetId, out var ids))
                    miniPieces[named.MiniSetId] = ids = new HashSet<string>(StringComparer.Ordinal);
                ids.Add(named.Id);
            }
            foreach (var mp in miniPieces)
            {
                b.MiniSets[mp.Key] = mp.Value.Count;
                if (!NamedItems.TryGetMiniSet(mp.Key, out var mini)) continue;
                if (mp.Value.Count >= 2) Add(rawStats, mini.TwoPiece.Stat, mini.TwoPiece.Value);
                if (mp.Value.Count >= 3 && mini.ThreePiece != null) AddPower(mini.ThreePiece.Power, mini.ThreePiece.Value);
            }
            foreach (var selected in selectedTalents)
            {
                TalentDef t = selected.Key;
                int rank = selected.Value;
                var pair = global::SodRpg.Core.Game.PairCombos.ForBridge(t.Id);
                if (pair != null && t.Mechanism == null)
                {
                    var entry = global::SodRpg.Core.Game.PairCombos.Activate(pair, h, rank);
                    if (dependencies != null)
                    {
                        // The combo exists only while both neighbours are allocated, and it removes/gates authored bridge mechanisms of the same pair.
                        dependencies.Touch("Q:" + pair.Id, t.Id);
                        dependencies.Touch("Q:" + pair.Id, pair.StarA);
                        dependencies.Touch("Q:" + pair.Id, pair.StarB);
                    }
                    if (entry != null && definitions.TryGetValue(pair.StarA, out var starA)
                        && definitions.TryGetValue(pair.StarB, out var starB)
                        && Unlocked(starA) && Unlocked(starB))
                        b.PairCombos.Add(entry);
                    continue; // Inner bridges are combos, never their old unconditional ring stats.
                }
                if (t.Gimmick != null && t.Gimmick.Value > 0 && !FractionalScopedModifiers.RequiresMechanismRoute(t.EffectChannel))
                {
                    var entry = new GimmickEntry
                    {
                        StarId = t.Id, Memory = t.RouteMemory, Channel = t.EffectChannel,
                        ContributorIds = new[] { t.Id },
                        Def = new GimmickDef
                        {
                            Trigger = t.Gimmick.Trigger, Effect = t.Gimmick.Effect,
                            ValuePrecise = checked(t.Gimmick.ValuePrecise * rank), Arg = t.Gimmick.Arg,
                            Cooldown = t.Gimmick.Cooldown, DurationUnits = t.Gimmick.DurationUnits,
                            RadiusUnits = t.Gimmick.RadiusUnits, ExtraTargets = t.Gimmick.ExtraTargets,
                            ChanceUnits = t.Gimmick.ChanceUnits,
                        },
                    };
                    b.Gimmicks.Add(entry);
                }
                if (t.LinkPerRank != null)
                {
                    var link = new LinkDef
                    {
                        Requires = t.LinkPerRank.Requires,
                        Kind = t.LinkPerRank.Kind,
                        ValueMilli = checked(t.LinkPerRank.ValueMilli * rank),
                    };
                    if (global::SodRpg.Core.Game.Links.Validate(link))
                    {
                        b.Links.Add(link);
                        if (dependencies != null)
                        {
                            // Links of one key add up; haste totals combine every link naming the same memory.
                            dependencies.Touch("L:" + BuildAggregation.LinkKey(link), t.Id);
                            if (link.Kind == LinkKind.MemoryHaste)
                                foreach (string memory in link.Requires) dependencies.Touch("H:" + global::SodRpg.Core.Game.Links.Canon(memory), t.Id);
                        }
                    }
                }
                else if (t.IsPowerNode)
                {
                    AddPower(t.RankPower, t.PerRank * rank);
                    dependencies?.Touch("P:" + ((int)t.RankPower).ToString(CultureInfo.InvariantCulture), t.Id);
                }
                else if (t.PerRank != 0)
                {
                    Add(rawStats, t.Stat, t.PerRank * rank);
                    dependencies?.Touch("S:" + ((int)t.Stat).ToString(CultureInfo.InvariantCulture), t.Id);
                }
            }
            FractionalScopedModifiers.Compose(b, selectedTalents);
            AuthoredMechanisms.Compose(b, selectedTalents);
            global::SodRpg.Core.Game.RunGrowth.Compose(b, selectedTalents);
            bool KeyUnlocked(TalentDef candidateKey)
            {
                if (!Unlocked(candidateKey)) return false;
                var gate = reachability ?? h;
                long ranks = 0;
                foreach (var kv in gate.Talents)
                    if (definitions.TryGetValue(kv.Key, out var t) && !t.IsKeystone
                        && (candidateKey.HeroKey != null ? t.HeroKey == heroKey : t.HeroKey == null && t.Route == candidateKey.Route))
                        ranks += Math.Max(0, Math.Min(t.MaxRank, kv.Value));
                return ranks >= Content.KeystoneRouteRequirement
                    && (candidateKey.HeroKey == null || Mastery.Level(gate.Kills) >= HeroSigils.KeystoneMastery);
            }
            if (h.Keystone != null && definitions.TryGetValue(h.Keystone, out var key) && key.IsKeystone
                && Rules.BelongsTo(key, heroKey) && KeyUnlocked(key))
            {
                b.SelectedKeystone = key.KeystoneDefinition;
                if (dependencies != null) dependencies.AppliedKeystone = b.SelectedKeystone;
                bool migratedStillWater = false;
                if (key.Power == Power.StillWater && key.KeystoneDefinition != null)
                    foreach (var grant in key.KeystoneDefinition.Grants)
                        if (grant.Kind == AuthoredMechanismKind.StunSourceFilter) { migratedStillWater = true; break; }
                if (key.Power != Power.None && !migratedStillWater) AddPower(key.Power, key.PowerValue);
            }
            AuthoredKeystoneComposer.Apply(b);

            var daily = DailyDream.Get(dailyId);
            if (daily != null)
            {
                foreach (var pw in daily.BoostedPowers)
                    if (rawPowers.TryGetValue(pw, out int v))
                    {
                        rawPowers[pw] = v + v * DailyDream.PowerBoostPct / 100;
                        basePowers[pw] += basePowers[pw] * DailyDream.PowerBoostPct / 100m;
                        if (awakenGains.TryGetValue(pw, out int gain))
                        {
                            int before = v - gain;
                            awakenGains[pw] = rawPowers[pw] - (before + before * DailyDream.PowerBoostPct / 100);
                        }
                    }
            }
            var pactList = pacts != null ? new List<Pact>(pacts) : new List<Pact>();
            foreach (var id in pactList)
            {
                var d = Game.Pacts.Get(id);
                if (d == null) continue;
                foreach (var s in d.Boons) Add(rawStats, s.Stat, s.Value);
            }
            foreach (var kv in rawStats)
            {
                int cap = Content.StatCap(kv.Key);
                b.Stats[kv.Key] = cap > 0 ? Math.Min(kv.Value, cap) : kv.Value;
            }
            foreach (var kv in rawPowers)
            {
                int cap = Content.PowerCap(kv.Key);
                b.Powers[kv.Key] = cap > 0 ? (int)CappedContribution(basePowers[kv.Key], kv.Value, cap) : kv.Value;
                if (NewPowersV129.IsConditionalAttribute(kv.Key) && awakenGains.TryGetValue(kv.Key, out int gain))
                    b.ConditionalBasePowers[kv.Key] = Math.Min(b.Powers[kv.Key], Math.Max(0, kv.Value - gain));
            }
            foreach (var id in pactList)
            {
                var d = Game.Pacts.Get(id);
                if (d == null) continue;
                foreach (var s in d.Penalties) b.Stats[s.Stat] = b.Get(s.Stat) + s.Value;
            }
            var links = AggregateLinks(b.Links);
            b.Links.Clear();
            b.Links.AddRange(links);
            return b;
        }

        private static IReadOnlyList<LinkDef> AggregateLinks(IEnumerable<LinkDef> links)
            => BuildAggregation.LinksForBuild(links);

        // Every source receives the same capped-base share, then keeps its own multiplier.
        // Summing scaled contributions before applying that share is algebraically identical.
        private static decimal CappedContribution(decimal basis, decimal scaled, decimal cap)
            => Math.Min(cap * 2.5m, basis > cap ? scaled * cap / basis : scaled);

        private static int CappedLinkMilli(decimal basis, decimal scaled, decimal cap)
            => BuildPrecision.FromDecimal(decimal.Round(CappedContribution(basis, scaled, cap), 3, MidpointRounding.AwayFromZero));

        private static void Add<T>(Dictionary<T, int> d, T key, int v)
        {
            d.TryGetValue(key, out int cur);
            d[key] = cur + v;
        }

        /// <summary>
        /// 通信用の短い文字列表現。"s:0=12,3=4;p:1=4;h:2;d:30;a:300;l:3:22000:St_X+Gem_Y" の形。
        /// d は夢のレベル、a は使用済み星ポイント。l は「種類:値:条件+条件+条件」。
        /// c は合わせ技の「ID:段数」。効果は正規の定義から復元し、クライアントからの効果量は受け取らない。
        /// ホストはこれを検証してから能力補正へ変換する。
        /// </summary>
        public string Encode()
        {
            ValidateCounts();
            var sb = new StringBuilder();
            sb.Append("s:");
            bool first = true;
            foreach (var kv in Stats)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append((int)kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(";p:");
            first = true;
            foreach (var kv in Powers)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append((int)kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(";h:").Append(Heat.ToString(CultureInfo.InvariantCulture));
            if (ConditionalBasePowers.Count > 0)
            {
                sb.Append(";u:");
                first = true;
                foreach (var kv in ConditionalBasePowers)
                {
                    if (!NewPowersV129.IsConditionalAttribute(kv.Key)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append((int)kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            sb.Append(";d:").Append(DreamLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(";a:").Append(SpentStarPoints.ToString(CultureInfo.InvariantCulture));
            sb.Append(";l:");
            first = true;
            foreach (var link in AggregateLinks(Links))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(((int)link.Kind).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(link.ValueMilli.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(string.Join("+", link.Requires));
            }
            sb.Append(";g:");
            first = true;
            var stars = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (var raw in Gimmicks)
            {
                var entry = global::SodRpg.Core.Game.Gimmicks.Clamp(raw);
                if (entry == null || !stars.Add(entry.StarId))
                    throw new InvalidOperationException("Invalid or duplicate gimmick entry.");
                if (count >= global::SodRpg.Core.Game.Gimmicks.MaxEntries)
                    throw new InvalidOperationException("The build exceeds the gimmick entry security limit.");
                if (!first) sb.Append(',');
                first = false;
                count++;
                sb.Append(entry.StarId).Append(':').Append(entry.Memory).Append(':')
                    .Append(((int)entry.Def.Trigger).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(((int)entry.Def.Effect).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append((entry.Def.ValuePrecise % 10000 == 0 ? entry.Def.ValuePrecise / 10000 : 0).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Def.Arg.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Def.Cooldown.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append((entry.Def.DurationUnits / 100).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append((entry.Def.RadiusUnits / 100).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Def.ExtraTargets.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append((entry.Def.ChanceUnits / 100).ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(";c:");
            first = true;
            stars.Clear();
            count = 0;
            foreach (var raw in PairCombos)
            {
                if (count >= global::SodRpg.Core.Game.PairCombos.MaxEntries)
                    throw new InvalidOperationException("The build exceeds the pair-combo entry limit.");
                var entry = global::SodRpg.Core.Game.PairCombos.Clamp(raw);
                if (entry == null || !stars.Add(entry.Def.Id))
                    throw new InvalidOperationException("Invalid or duplicate pair-combo entry.");
                if (!first) sb.Append(',');
                first = false;
                count++;
                sb.Append(entry.Def.Id).Append(':').Append(entry.Ranks.ToString(CultureInfo.InvariantCulture));
            }
            ScopedBuildCodec.Append(sb, this);
            global::SodRpg.Core.Game.RunGrowth.Append(sb, this);
            string encoded = sb.ToString();
            if (encoded.Length > BuildLimits.MaxEncodedChars || Encoding.UTF8.GetByteCount(encoded) > BuildLimits.MaxEncodedBytes)
                throw new InvalidOperationException("The build exceeds the encoded message limit.");
            return encoded;
        }

        private void ValidateCounts()
        {
            if (Gimmicks.Count + Mechanisms.Count > BuildLimits.MaxGimmickEntries || Links.Count > BuildLimits.MaxLinkEntries
                || PairCombos.Count > BuildLimits.MaxPairComboEntries || Stats.Count > BuildLimits.MaxStatEntries
                || Powers.Count > BuildLimits.MaxPowerEntries || ConditionalBasePowers.Count > BuildLimits.MaxConditionalPowerEntries
                || MechanismEndpointRanks.Count > StarProgression.MaxSpendablePoints)
                throw new InvalidOperationException("The build exceeds its legal entry envelope.");
            ScopedBuildCodec.Validate(this);
            global::SodRpg.Core.Game.RunGrowth.Validate(this);
            foreach (var link in AggregateLinks(Links))
                if (link.ValueMilli > BuildLimits.MaxLinkValueMilli(link.Kind, link.Requires.Length))
                    throw new InvalidOperationException("The build exceeds its legal link value envelope.");
        }

        /// <summary>Decode protocol-12 thousandths. Invalid or oversized packets are rejected atomically.</summary>
        public static Build Decode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > BuildLimits.MaxEncodedChars) return null;
            // Identifiers and the wire grammar are ASCII; check before allocating token arrays.
            foreach (char ch in text) if (ch > 127) return null;
            var b = new Build();
            var sections = new HashSet<string>(StringComparer.Ordinal);
            var stars = new HashSet<string>(StringComparer.Ordinal);
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (string part in text.Split(';'))
                {
                    int colon = part.IndexOf(':');
                    if (colon < 0) return null;
                    string kind = part.Substring(0, colon);
                    if (!sections.Add(kind)) return null;
                    string body = part.Substring(colon + 1);
                    if (kind == "h") { b.Heat = Loot.ClampHeat(ParseInt(body)); continue; }
                    if (kind == "d") { b.DreamLevel = Math.Max(1, Math.Min(Content.MaxDreamLevel, ParseInt(body))); continue; }
                    if (kind == "a")
                    {
                        b.SpentStarPoints = Math.Max(0, Math.Min(StarProgression.MaxSpendablePoints, ParseInt(body)));
                        continue;
                    }
                    int limit;
                    switch (kind)
                    {
                        case "l": limit = BuildLimits.MaxLinkEntries; break;
                        case "g": limit = BuildLimits.MaxGimmickEntries; break;
                        case "c": limit = BuildLimits.MaxPairComboEntries; break;
                        case "s": limit = BuildLimits.MaxStatEntries; break;
                        case "p": limit = BuildLimits.MaxPowerEntries; break;
                        case "u": limit = BuildLimits.MaxConditionalPowerEntries; break;
                        default: return null;
                        case "f":
                        case "n":
                        case "j": limit = BuildLimits.MaxGimmickEntries; break;
                        case "v": limit = BuildLimits.MaxGimmickEntries; break;
                        case "r": limit = BuildLimits.MaxGimmickEntries; break;
                        case "m": limit = BuildLimits.MaxGimmickEntries; break;
                        case "e": limit = StarProgression.MaxSpendablePoints; break;
                        case "k": limit = 1; break;
                        case "w": limit = global::SodRpg.Core.Game.RunGrowth.MaxEntries; break;
                    }
                    if (body.Length == 0) continue;
                    string[] entries = body.Split(',');
                    if (entries.Length > limit) return null;
                    foreach (string encoded in entries)
                    {
                        if (kind == "w")
                        {
                            if (!global::SodRpg.Core.Game.RunGrowth.Read(encoded, b)) return null;
                            continue;
                        }
                        if (kind == "f" || kind == "n" || kind == "j" || kind == "v" || kind == "r" || kind == "m" || kind == "e" || kind == "k")
                        {
                            ScopedBuildCodec.Read(kind, encoded, b);
                            continue;
                        }
                        if (kind == "l")
                        {
                            string[] f = encoded.Split(':');
                            if (f.Length != 3) return null;
                            var linkKind = (LinkKind)ParseInt(f[0]);
                            int value = ParseInt(f[1]);
                            var link = new LinkDef { Kind = linkKind, ValueMilli = value, Requires = f[2].Split('+') };
                            if (!global::SodRpg.Core.Game.Links.Validate(link) || value < 0
                                || value > BuildLimits.MaxLinkValueMilli(linkKind, link.Requires.Length)) return null;
                            b.Links.Add(link);
                        }
                        else if (kind == "g")
                        {
                            string[] f = encoded.Split(':');
                            if (f.Length != 11 || !float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float cooldown)) return null;
                            var entry = new GimmickEntry
                            {
                                StarId = f[0], Memory = f[1], Def = new GimmickDef
                                {
                                    Trigger = (GimmickTrigger)ParseInt(f[2]), Effect = (GimmickEffect)ParseInt(f[3]),
                                    ValueMilli = ParseInt(f[4]), Arg = ParseInt(f[5]), Cooldown = cooldown,
                                    DurationPercent = ParseInt(f[7]), RadiusPercent = ParseInt(f[8]),
                                    ExtraTargets = ParseInt(f[9]), ChancePercent = ParseInt(f[10]),
                                }
                            };
                            if (!stars.Add(entry.StarId)) return null;
                            b.Gimmicks.Add(entry);
                        }
                        else if (kind == "c")
                        {
                            string[] f = encoded.Split(':');
                            if (f.Length != 2) return null;
                            var entry = global::SodRpg.Core.Game.PairCombos.Clamp(new PairComboEntry
                            { Def = global::SodRpg.Core.Game.PairCombos.Get(f[0]), Ranks = ParseInt(f[1]) });
                            if (entry == null || !pairs.Add(entry.Def.Id)) return null;
                            b.PairCombos.Add(entry);
                        }
                        else
                        {
                            string[] f = encoded.Split('=');
                            if (f.Length != 2) return null;
                            int id = ParseInt(f[0]), value = ParseInt(f[1]);
                            if (kind == "s")
                            {
                                if (!Enum.IsDefined(typeof(Stat), id) || b.Stats.ContainsKey((Stat)id)) return null;
                                int cap = Content.StatCap((Stat)id);
                                b.Stats.Add((Stat)id, Math.Max(-cap, Math.Min(cap, value)));
                            }
                            else
                            {
                                if (!Enum.IsDefined(typeof(Power), id) || id == 0) return null;
                                var power = (Power)id;
                                var target = kind == "u" ? b.ConditionalBasePowers : b.Powers;
                                if (target.ContainsKey(power) || kind == "u" && !NewPowersV129.IsConditionalAttribute(power)) return null;
                                int cap = Content.PowerCap(power);
                                target.Add(power, Math.Max(0, Math.Min((int)(cap * 2.5m), value)));
                            }
                        }
                    }
                }
                foreach (var pw in new List<Power>(b.ConditionalBasePowers.Keys))
                {
                    int effective = b.Get(pw);
                    int minimum = (int)Math.Ceiling(effective * 100d / Content.AwakenPowerPctAt(Content.MaxAwakenLevel));
                    b.ConditionalBasePowers[pw] = Math.Max(minimum, Math.Min(effective, b.ConditionalBasePowers[pw]));
                }
                var links = AggregateLinks(b.Links);
                b.Links.Clear();
                b.Links.AddRange(links);
                ScopedBuildCodec.Apply(b);
                b.ValidateCounts();
                return b;
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.IO.IOException) { return null; }
        }

        private static int ParseInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
