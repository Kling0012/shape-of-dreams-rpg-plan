using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>星振り候補1件の評価。順序付けに使う値だけを持ち、状態は保持しない。</summary>
public readonly struct CandidateScore
{
    public CandidateScore(double gain, int cost, int breadth, int ordinal)
    {
        Gain = gain;
        Cost = cost;
        Breadth = breadth;
        Ordinal = ordinal;
    }

    /// <summary>この購入で増える力の代理値。</summary>
    public double Gain { get; }
    /// <summary>この購入のポイント費用（1以上）。</summary>
    public int Cost { get; }
    /// <summary>購入後の Build が持つ効果の数（仕掛け・連携・固有効果などの幅）。</summary>
    public int Breadth { get; }
    /// <summary>ツリー内の並び（同じ条件では先に登録された星を好む）。</summary>
    public int Ordinal { get; }
    public double GainPerPoint => Cost > 0 ? Gain / Cost : 0;
}

/// <summary>記憶特化戦略の対象。 authored 星群があれば最大の星群、なければ最大の記憶ルート。</summary>
public sealed record MemoryFocus(string Key, string? Memory, bool IsAuthoredCluster, int Stars);

/// <summary>v1.31 星振りシミュレーションの純粋な補助関数。コアの定数は一切書き換えない。</summary>
public static class StarBalance
{
    /// <summary>報告するポイント節目。v1.31 の上限は StarProgression.MaxPoints = 300。</summary>
    public static readonly int[] Checkpoints = { 0, 50, 100, 150, 200, 250, 300 };

    /// <summary>
    /// 力の代理値。本体が悪夢化抽選の強さとして使う式
    /// （攻撃力% か 魔力% の大きい方 ＋ 最大HP% の半分、Nightmares.GearChanceMult の内部スコア）を
    /// クランプせずにそのまま返す。戦闘の出力そのものではない。
    /// </summary>
    public static double GearScore(Build b) => b == null ? 0
        : Math.Max(b.Get(Stat.AttackPct), b.Get(Stat.PowerPct)) + b.Get(Stat.MaxHealthPct) / 2.0;

    /// <summary>Build が運ぶ効果の数。能力値が動かない仕掛け・連携・固有効果の広がりを見る。</summary>
    public static int EffectBreadth(Build b) => b == null ? 0
        : b.Gimmicks.Count + b.Links.Count + b.Powers.Count
          + b.NativeModifiers.Count + b.Mechanisms.Count + b.PairCombos.Count;

    /// <summary>費用は正で、支出 spent に足しても節目 target を超えないか。</summary>
    public static bool FitsBudget(int spent, int cost, int target) => cost > 0 && spent + cost <= target;

    /// <summary>a を b より優るか。1ポイントあたりの増分 → 効果の幅 → 費用の安さ → 登録順。</summary>
    public static bool Prefer(CandidateScore a, CandidateScore b)
    {
        if (a.GainPerPoint != b.GainPerPoint) return a.GainPerPoint > b.GainPerPoint;
        if (a.Breadth != b.Breadth) return a.Breadth > b.Breadth;
        if (a.Cost != b.Cost) return a.Cost < b.Cost;
        return a.Ordinal < b.Ordinal;
    }

    /// <summary>
    /// 記憶特化戦略の対象を1つ決める。authored 星群（TalentDef.Cluster）があれば星数最大の星群、
    /// 星群がなければ星数最大の記憶ルート。星数同数なら先に現れた方。
    /// </summary>
    public static MemoryFocus? PickMemoryFocus(IReadOnlyList<TalentDef> tree)
    {
        if (tree == null || tree.Count == 0) return null;
        MemoryFocus? best = null;
        int bestStars = 0, bestFirst = int.MaxValue;
        for (int pass = 0; pass < 2; pass++)
        {
            bool clusterPass = pass == 0;
            for (int i = 0; i < tree.Count; i++)
            {
                var t = tree[i];
                if (t == null) continue;
                string? key = clusterPass ? t.Cluster?.Id : t.Cluster == null ? t.RouteId : null;
                if (string.IsNullOrEmpty(key)) continue;
                int stars = 0, first = int.MaxValue;
                string? memory = null;
                for (int j = 0; j < tree.Count; j++)
                {
                    var other = tree[j];
                    if (other == null) continue;
                    string? otherKey = clusterPass ? other.Cluster?.Id : other.Cluster == null ? other.RouteId : null;
                    if (otherKey != key) continue;
                    if (stars == 0) memory = other.RouteMemory;
                    stars++;
                    first = Math.Min(first, j);
                }
                if (stars > bestStars || stars == bestStars && first < bestFirst)
                {
                    bestStars = stars;
                    bestFirst = first;
                    best = new MemoryFocus(key!, memory, clusterPass, stars);
                }
            }
            // 星群が1つでもあればルートには fallback しない。
            if (clusterPass && best != null) return best;
        }
        return best;
    }
}
