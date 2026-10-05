using System.Diagnostics;
using SodRpg.Core.Game;

namespace BalanceSim;

internal enum StarStrategy
{
    GreedyByStat,
    MemoryFocus,
    KeystoneRush,
}

internal static class StarStrategyNames
{
    public static string Key(StarStrategy s) => s switch
    {
        StarStrategy.GreedyByStat => "greedy",
        StarStrategy.MemoryFocus => "memory",
        _ => "keystone",
    };
    public static string Ja(StarStrategy s) => s switch
    {
        StarStrategy.GreedyByStat => "能力値優先",
        StarStrategy.MemoryFocus => "記憶特化",
        _ => "核先行",
    };
    public static readonly StarStrategy[] All =
        { StarStrategy.GreedyByStat, StarStrategy.MemoryFocus, StarStrategy.KeystoneRush };
}

/// <summary>1つの節目での星の支出と、その時点の Build の実測値。</summary>
internal sealed class StarCheckpoint
{
    public int Target;
    public int Spent;
    public int Unspent;
    public int AttackPct;
    public int PowerPct;
    public int MaxHealthPct;
    public int Armor;
    public int Haste;
    public int AttackSpeedPct;
    public int Gimmicks;
    public int Links;
    public int Powers;
    public double Proxy;
    public int Breadth;
    public string Keystone = "";
    public double GearChanceMult;
}

internal sealed class StarHeroResult
{
    public string HeroKey = "";
    public bool AuthoredTree;
    public int TreeStars;
    public int Clusters;
    public int Keystones;
    public StarStrategy Strategy;
    public MemoryFocus? Focus;
    public List<StarCheckpoint> Checkpoints = [];
    public long ElapsedMilliseconds;
}

/// <summary>
/// v1.31：500ポイントまでの星振りを、本体と同じ Rules.AddTalentRank / Rules.SetKeystone /
/// Build.Compute の経路で購入して眺める。ツリーは実行時に登録されているもの
/// （HeroSigils.TreeFor）をそのまま使うため、authored 星群の追加・差し替えに自動で追従する。
/// </summary>
internal sealed class StarSimulation
{
    /// <summary>核の前提熟練度（HeroSigils.KeystoneMastery=3）を最初から満たしておく撃破数。</summary>
    private const int MasteryKills = 1_000_000;
    private readonly Options options;
    public List<string> Heroes { get; } = [];
    public List<StarHeroResult> Results { get; } = [];

    public StarSimulation(Options options)
    {
        this.options = options;
        foreach (var talent in HeroStarRoutes.All)
            if (Links.IsTraveler(talent.HeroKey) && !Heroes.Contains(talent.HeroKey))
                Heroes.Add(talent.HeroKey);
    }

    public void Run()
    {
        StarClusters.RegisterAllGenerated();
        var watch = Stopwatch.StartNew();
        foreach (string hero in Heroes)
            foreach (var strategy in StarStrategyNames.All)
                Results.Add(RunStrategy(hero, strategy, watch));
    }

    private StarHeroResult RunStrategy(string hero, StarStrategy strategy, Stopwatch watch)
    {
        var result = new StarHeroResult
        {
            HeroKey = hero,
            Strategy = strategy,
            AuthoredTree = StarClusters.TryGetRegisteredTree(hero, out _),
        };
        long start = watch.ElapsedMilliseconds;
        var tree = HeroSigils.TreeFor(hero);
        var layout = HeroTreeLayout.ForHero(hero);
        var engine = Rules.AllocationValidationForHero(hero);
        result.TreeStars = tree.Count;
        var clusterIds = new HashSet<string>(StringComparer.Ordinal);
        int keystones = 0;
        foreach (var talent in tree)
        {
            if (talent.Cluster != null) clusterIds.Add(talent.Cluster.Id);
            if (talent.IsKeystone) keystones++;
        }
        result.Clusters = clusterIds.Count;
        result.Keystones = keystones;
        result.Focus = strategy == StarStrategy.MemoryFocus ? StarBalance.PickMemoryFocus(tree) : null;

        var profile = Profile.CreateNew(1);
        profile.DreamLevel = options.DreamLevel;
        var h = profile.Hero(hero);
        h.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
        h.Kills = MasteryKills;

        foreach (int target in StarBalance.Checkpoints)
        {
            if (target > 0) AllocateTo(profile, hero, tree, layout, engine, strategy, result.Focus, target);
            result.Checkpoints.Add(Snapshot(profile, hero, tree, target));
        }
        result.ElapsedMilliseconds = watch.ElapsedMilliseconds - start;
        return result;
    }

    private StarCheckpoint Snapshot(Profile profile, string hero, IReadOnlyList<TalentDef> tree, int target)
    {
        var build = Build.Compute(profile, hero, 0);
        var h = profile.Hero(hero);
        string keystone = "";
        if (h.Keystone != null)
            foreach (var talent in tree)
                if (talent.Id == h.Keystone)
                    keystone = talent.Name.Ja;
        return new StarCheckpoint
        {
            Target = target,
            Spent = build.SpentStarPoints,
            Unspent = profile.TalentPoints(hero) - build.SpentStarPoints,
            AttackPct = build.Get(Stat.AttackPct),
            PowerPct = build.Get(Stat.PowerPct),
            MaxHealthPct = build.Get(Stat.MaxHealthPct),
            Armor = build.Get(Stat.Armor),
            Haste = build.Get(Stat.Haste),
            AttackSpeedPct = build.Get(Stat.AttackSpeedPct),
            Gimmicks = build.Gimmicks.Count,
            Links = build.Links.Count,
            Powers = build.Powers.Count,
            Proxy = StarBalance.GearScore(build),
            Breadth = StarBalance.EffectBreadth(build),
            Keystone = keystone,
            GearChanceMult = Nightmares.GearChanceMult(build),
        };
    }

    private readonly struct Candidate
    {
        public readonly string Id;
        public readonly int? Option;
        public readonly bool Keystone;
        public readonly int Cost;
        public readonly int Ordinal;
        public readonly double Gain;
        public readonly int Breadth;

        public Candidate(string id, int? option, bool keystone, int cost, int ordinal, double gain, int breadth)
        {
            Id = id; Option = option; Keystone = keystone; Cost = cost;
            Ordinal = ordinal; Gain = gain; Breadth = breadth;
        }
    }

    /// <summary>節目 target に届くまで購入を繰り返す。1周で1つも買えなければ次の節目へ進む。</summary>
    private void AllocateTo(Profile profile, string hero, IReadOnlyList<TalentDef> tree, HeroTreeLayout layout,
        EffectiveAllocationValidation engine, StarStrategy strategy, MemoryFocus? focus, int target)
    {
        while (true)
        {
            var h = profile.Hero(hero);
            int spent = engine.SpentPoints(h);
            if (spent >= target) break;
            var candidates = Collect(profile, hero, tree, layout, engine, h, spent, target);
            if (candidates.Count == 0) break;
            bool focusDone = FocusComplete(focus, tree, h);
            bool applied = false;
            foreach (var pick in Order(candidates, strategy, focus, tree, h, focusDone))
            {
                try
                {
                    if (pick.Keystone) Rules.SetKeystone(profile, hero, pick.Id);
                    else Rules.AddTalentRank(profile, hero, pick.Id, pick.Option);
                    applied = true;
                    break;
                }
                catch (InvalidOperationException) { /* 上限・前提・選択の不整合。次の候補へ。 */ }
            }
            if (!applied) break;
        }
    }

    private static bool FocusComplete(MemoryFocus? focus, IReadOnlyList<TalentDef> tree, HeroState h)
    {
        if (focus == null) return true;
        foreach (var t in tree)
        {
            bool inGroup = focus.IsAuthoredCluster
                ? t.Cluster != null && t.Cluster.Id == focus.Key
                : t.Cluster == null && t.RouteId == focus.Key;
            if (!inGroup) continue;
            int rank = h.Talents.TryGetValue(t.Id, out int value) ? value : 0;
            if (rank < t.MaxRank) return false;
        }
        return true;
    }

    private List<Candidate> Collect(Profile profile, string hero, IReadOnlyList<TalentDef> tree, HeroTreeLayout layout,
        EffectiveAllocationValidation engine, HeroState h, int spent, int target)
    {
        var candidates = new List<Candidate>();
        var current = Build.ComputeForTree(profile, hero, 0, tree, h, null, layout);
        double currentScore = StarBalance.GearScore(current);
        int currentBreadth = StarBalance.EffectBreadth(current);
        for (int i = 0; i < tree.Count; i++)
        {
            var t = tree[i];
            if (t.IsKeystone || !Rules.BelongsTo(t, hero)) continue;
            int rank = h.Talents.TryGetValue(t.Id, out int value) ? value : 0;
            if (rank >= t.MaxRank) continue;
            if (!engine.CanReach(h, t)) continue;
            if (!StarBalance.FitsBudget(spent, t.RankCost, target)) continue;
            if (t.IsChoice)
            {
                int first = rank > 0 && h.TalentChoices.TryGetValue(t.Id, out int chosen) ? chosen : 0;
                int last = rank > 0 ? first : t.Choices.Count - 1;
                for (int option = first; option <= last; option++)
                    candidates.Add(Evaluate(profile, hero, tree, layout, h, t, i, rank + 1, option, currentScore, currentBreadth));
            }
            else
                candidates.Add(Evaluate(profile, hero, tree, layout, h, t, i, rank + 1, null, currentScore, currentBreadth));
        }
        if (h.Keystone == null)
            for (int i = 0; i < tree.Count; i++)
            {
                var key = tree[i];
                if (!key.IsKeystone || !Rules.BelongsTo(key, hero)) continue;
                int cost = key.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
                if (!StarBalance.FitsBudget(spent, cost, target)) continue;
                if (!engine.KeystoneUnlocked(h, hero, key)) continue;
                var draft = h.Clone();
                draft.Keystone = key.Id;
                var build = Build.ComputeForTree(profile, hero, 0, tree, draft, null, layout);
                candidates.Add(new Candidate(key.Id, null, true, cost, int.MaxValue - i,
                    StarBalance.GearScore(build) - currentScore, StarBalance.EffectBreadth(build)));
            }
        return candidates;
    }

    private Candidate Evaluate(Profile profile, string hero, IReadOnlyList<TalentDef> tree, HeroTreeLayout layout,
        HeroState h, TalentDef t, int ordinal, int rank, int? option, double currentScore, int currentBreadth)
    {
        var draft = h.Clone();
        draft.Talents[t.Id] = rank;
        if (option.HasValue) draft.TalentChoices[t.Id] = option.Value;
        var build = Build.ComputeForTree(profile, hero, 0, tree, draft, null, layout);
        return new Candidate(t.Id, option, false, t.RankCost, ordinal * 2 + (option ?? 0),
            StarBalance.GearScore(build) - currentScore, StarBalance.EffectBreadth(build));
    }

    /// <summary>戦略ごとの購入順。全同点のときは手続き的に一意（星→選択肢→核の登録順）。</summary>
    private static IEnumerable<Candidate> Order(List<Candidate> candidates, StarStrategy strategy,
        MemoryFocus? focus, IReadOnlyList<TalentDef> tree, HeroState h, bool focusDone)
    {
        if (strategy == StarStrategy.GreedyByStat || focusDone && strategy == StarStrategy.MemoryFocus
            || strategy == StarStrategy.KeystoneRush && h.Keystone != null)
            return GreedyOrder(candidates);
        if (strategy == StarStrategy.KeystoneRush)
            return candidates.OrderBy(c => c.Keystone ? 0 : 1).ThenBy(c => c.Cost).ThenBy(c => c.Ordinal);
        // MemoryFocus：対象の星群を登録順で完成させ、閉じている間は核以外の最安のつなぎ星を買う。
        var group = candidates.Where(c => !c.Keystone && InFocus(c, focus, tree)).ToList();
        if (group.Count > 0) return group.OrderBy(c => c.Ordinal);
        return candidates.Where(c => !c.Keystone).OrderBy(c => c.Cost).ThenBy(c => c.Ordinal);
    }

    private static bool InFocus(Candidate c, MemoryFocus? focus, IReadOnlyList<TalentDef> tree)
    {
        if (focus == null) return false;
        foreach (var t in tree)
            if (t.Id == c.Id)
                return focus.IsAuthoredCluster
                    ? t.Cluster != null && t.Cluster.Id == focus.Key
                    : t.Cluster == null && t.RouteId == focus.Key;
        return false;
    }

    private static List<Candidate> GreedyOrder(List<Candidate> candidates)
    {
        var list = new List<Candidate>(candidates);
        list.Sort((a, b) =>
        {
            var sa = new CandidateScore(a.Gain, a.Cost, a.Breadth, a.Ordinal);
            var sb = new CandidateScore(b.Gain, b.Cost, b.Breadth, b.Ordinal);
            if (StarBalance.Prefer(sa, sb)) return -1;
            if (StarBalance.Prefer(sb, sa)) return 1;
            return 0;
        });
        return list;
    }
}
