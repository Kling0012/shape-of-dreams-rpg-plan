using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>買う星1つ。Option は選択肢の星（IsChoice）で選ぶ番号（0始まり）。null なら選択肢でない。</summary>
public readonly struct StarPurchase
{
    public StarPurchase(string id, int? option = null) { Id = id; Option = option; }
    public string Id { get; }
    public int? Option { get; }
}

/// <summary>
/// v1.32 の計測用に、目的の星までの経路を実際の `Rules.AddTalentRank` で最小限に買う割り振り器。
/// 経路は HeroTreeLayout の公開グラフ（中心から隣接へ）の幅優先探索で決め、authored 星の前提
/// （AuthoredStar.RequiredStarIds）も閉包に含める。買えない星（効果が上限に達して無効になるなど）は
/// 経路から外して再探索する。星図の妥当性の判定はすべてコアに任せる。
/// </summary>
public static class V132Builds
{
    private static readonly Dictionary<string, string?> errors = new();

    /// <summary>目的の星（と選択肢）を買う。経路と前提の星も1ランクずつ買う。戻り値は新しく買った星の数。</summary>
    public static int BuyStars(Profile profile, string heroKey, IReadOnlyList<StarPurchase> purchases)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (purchases == null || purchases.Count == 0) return 0;
        var tree = HeroSigils.TreeFor(heroKey);
        var layout = HeroTreeLayout.ForHero(heroKey);
        DirectWrites = 0;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < tree.Count; i++) index[tree[i].Id] = FindNode(layout, tree[i].Id);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in purchases)
            if (p.Id != null && index.TryGetValue(p.Id, out _)) targets.Add(p.Id);
        errors.Clear();
        var hero = profile.Hero(heroKey);
        int before = hero.Talents.Count;
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            // 中心（StartIndex）からの幅優先。核は目的でない限り通らず、閉じた星も通らない。
            var parent = new Dictionary<int, int>();
            var depth = new Dictionary<int, int> { [layout.StartIndex] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(layout.StartIndex);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int next in layout.Nodes[current].Neighbors)
                {
                    if (depth.ContainsKey(next)) continue;
                    var talent = layout.Nodes[next].Talent;
                    if (talent == null || blocked.Contains(talent.Id)) continue;
                    if (talent.IsKeystone && !targets.Contains(talent.Id)) continue;
                    depth[next] = depth[current] + 1;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
            bool Missing(string id) => !index.TryGetValue(id, out int node) || !depth.TryGetValue(node, out _);
            var needed = new HashSet<string>(StringComparer.Ordinal);
            void Include(string id)
            {
                if (Missing(id)) throw new InvalidOperationException("星図で到達できない星: " + id);
                for (int current = index[id]; current != layout.StartIndex; current = parent[current])
                {
                    var talent = layout.Nodes[current].Talent;
                    if (talent != null) needed.Add(talent.Id);
                }
            }
            // 前提（RequiredStarIds）と経路を交互に閉包する（経路の星自身が前提を持つことがある）。
            foreach (string id in targets) Include(id);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (string id in needed.ToList())
                {
                    var authored = TalentById(tree, id)?.AuthoredStar;
                    if (authored == null) continue;
                    foreach (string req in authored.RequiredStarIds)
                        if (needed.Add(req)) { grew = true; Include(req); }
                }
            }
            var order = needed.Where(id => !blocked.Contains(id) && !Missing(id))
                .OrderBy(id => depth[index[id]]).ThenBy(id => id, StringComparer.Ordinal).ToList();
            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (string id in order)
                    if (TryBuy(profile, heroKey, id, purchases, tree) > 0) progressed = true;
            }
            bool Owned(string id) => hero.Talents.TryGetValue(id, out int rank) && rank > 0;
            var unbought = order.Where(id => !Owned(id)).ToList();
            if (unbought.Count == 0) return hero.Talents.Count - before;
            var fresh = unbought.Where(blocked.Add).ToList();
            if (fresh.Count == 0)
            {
                var missing = targets.Where(t => !Owned(t))
                    .Select(t => t + "（" + (errors.TryGetValue(t, out string? err) ? err : "不明") + "）").ToList();
                throw new InvalidOperationException("購入できなかった星: " + string.Join(", ", missing));
            }
            // 買えなかった星を閉じて、別の経路を探す。
        }
    }

    private static int TryBuy(Profile profile, string heroKey, string id, IReadOnlyList<StarPurchase> purchases,
        IReadOnlyList<TalentDef> tree)
    {
        var hero = profile.Hero(heroKey);
        if (hero.Talents.TryGetValue(id, out int rank) && rank > 0) return 0;
        int? option = null;
        foreach (var p in purchases)
            if (p.Id == id) option = p.Option;
        var talent = TalentById(tree, id);
        if (talent == null) return 0;
        if (option == null && !talent.IsChoice)
        {
            try { Rules.AddTalentRank(profile, heroKey, id, null); return 1; }
            catch (InvalidOperationException e) { errors[id] = e.Message; }
        }
        else
        {
            // 選択肢の星：目的の星は指定された選択肢で、経路の星は買える選択肢を前から試す。
            int first = option ?? 0;
            int last = talent.IsChoice ? talent.Choices.Count - 1 : 0;
            for (int choice = first; choice <= last; choice++)
            {
                try { Rules.AddTalentRank(profile, heroKey, id, choice); return 1; }
                catch (InvalidOperationException e) { errors[id] = e.Message; }
            }
        }
        // コアの割り当て検証は「効果が出る購入」しか許さない。記憶にスコープされた経路の星などは
        // 装備のないこのシミュレーターでは買えないため、コアのテスト（AuthoredStarContractTests.AllocatePath）
        // と同じ直接割り当てで足す。星図の形（接続・前提）は幅優先の経路が守り、
        // Build の組み立て（Build.Compute）は常に実経路を使う。
        profile.Hero(heroKey).Talents[id] = 1;
        if (talent.IsChoice) profile.Hero(heroKey).TalentChoices[id] = option ?? 0;
        DirectWrites++;
        return 1;
    }

    /// <summary>直接割り当てに落ちた星の数（報告書の方法に記す。星図の購入検証を通れなかった経路の星）。</summary>
    public static int DirectWrites { get; private set; }

    private static TalentDef? TalentById(IReadOnlyList<TalentDef> tree, string id)
    {
        foreach (var t in tree) if (t.Id == id) return t;
        return null;
    }

    private static int FindNode(HeroTreeLayout layout, string id)
    {
        for (int i = 0; i < layout.Nodes.Count; i++)
            if (layout.Nodes[i].Id == id) return i;
        return -1;
    }

    // ───── 巡る富（共有の外縁星団）─────

    /// <summary>ツリー内の「巡る富」の星（能力値が通貨の Power になっている星）をすべて買う。既定の選択肢はない。</summary>
    public static List<StarPurchase> FortunePurchases(IReadOnlyList<TalentDef> tree) =>
        tree.Where(t => CurrencyStars.IsPower(t.RankPower)).Select(t => new StarPurchase(t.Id)).ToList();

    /// <summary>巡る富を全取得した Build。withStars が false なら星を買わない同じプロフィールの Build。
    /// profile には星経験を与えておく（呼び出し側の責任）。</summary>
    public static Build FortuneBuild(Profile profile, string heroKey, bool withStars)
    {
        if (withStars) BuyStars(profile, heroKey, FortunePurchases(HeroSigils.TreeFor(heroKey)));
        return Build.Compute(profile, heroKey, 0);
    }

    // ───── 遠征の鍛錬（RunGrowth）─────

    /// <summary>旅人 heroKey の RunGrowth の入口の星（RunGrowthDef を持つ星）のID。なければ null。</summary>
    public static string? GrowthStarId(IReadOnlyList<TalentDef> tree) =>
        tree.FirstOrDefault(t => t.RunGrowth != null)?.Id;

    /// <summary>成長の星 g1 に掛かる修飾の星（RunGrowthModifier を持つ小さな星、選択肢を除く）のID。</summary>
    public static List<string> GrowthCapStarIds(IReadOnlyList<TalentDef> tree, string growthId) =>
        tree.Where(t => t.RunGrowthModifier != null && !t.IsChoice && t.RunGrowthModifier.TargetStarId == growthId
                && t.RunGrowthModifier.CapBonus > 0)
            .Select(t => t.Id).ToList();

    /// <summary>成長の星 g1 用の選択肢の星のIDと、選ぶ番号。kind が "effect" なら効果+50%、"double" なら溜まる速さ2倍。</summary>
    public static StarPurchase? GrowthChoicePurchase(IReadOnlyList<TalentDef> tree, string growthId, string kind)
    {
        foreach (var t in tree)
        {
            if (!t.IsChoice) continue;
            for (int i = 0; i < t.Choices.Count; i++)
            {
                var mod = t.Choices[i].RunGrowthModifier;
                if (mod == null || mod.TargetStarId != growthId && mod.TargetStarId != null) continue;
                bool match = kind == "double" ? mod.DoubleGain : mod.EffectPercent > 0;
                if (match) return new StarPurchase(t.Id, i);
            }
        }
        return null;
    }
}
