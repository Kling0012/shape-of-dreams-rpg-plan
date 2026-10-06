using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>1組ぶんの計測結果（v1.32 セットバランス）。すべての値は決定的な代理値（PowerScore 単位）。</summary>
public sealed class SetBalanceResult
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>計測に使った6つ装着の効果（sixOverride 指定時はその値）。</summary>
    public IReadOnlyList<PowerLine> SixPiece { get; init; } = Array.Empty<PowerLine>();
    /// <summary>代替装備6枠（基準）の代理値。</summary>
    public double Baseline { get; init; }
    /// <summary>2点 + 代替4枠の代理値。</summary>
    public double TwoPieces { get; init; }
    /// <summary>3点 + 代替3枠の代理値。</summary>
    public double ThreePieces { get; init; }
    /// <summary>6点（6つ装着の効果あり）の代理値。</summary>
    public double SixPieces { get; init; }
    /// <summary>6点だが6つ装着の効果を無効にした代理値。</summary>
    public double SixPiecesWithoutBonus { get; init; }

    public double Gain2 => TwoPieces - Baseline;
    public double Gain3 => ThreePieces - Baseline;
    public double Gain6 => SixPieces - Baseline;
    /// <summary>6つ装着の効果そのものの利得（6点の状態からの差分）。</summary>
    public double SixBonusGain => SixPieces - SixPiecesWithoutBonus;
    /// <summary>6点ビルドの代理値に占める、6つ装着の効果の割合（0〜1）。</summary>
    public double SixShare => SixPieces > 0 ? SixBonusGain / SixPieces : 0;
}

/// <summary>
/// v1.32：48組のセットについて、2/3/6点装着の強さを「最良の非セット代替品（同枠）」と比べて測る
/// 純粋な補助。コアの定数は書き換えない（6点差分の計測中だけ SetDef.SixPiece を一時的に差し替えて戻す）。
/// 乱数はコアの Rng（SplitMix64）を固定シードで使うので、同じ Content なら必ず同じ結果になる。
/// </summary>
public static class SetBalance
{
    /// <summary>計測に使う旅人（装備以外の要素を持たない空のプロフィールで計算する）。</summary>
    public const string HeroKey = "Hero_A";
    /// <summary>セット品・代替品ともに仮定するアイテムレベル（強化・覚醒なし）。</summary>
    public const int GearItemLevel = 10;
    /// <summary>代替品の候補を各枠・各レア度（伝説・エピック）について抽選する回数。</summary>
    public const int AlternativeRolls = 64;
    /// <summary>6つ装着の効果が「弱すぎ」とする比率（中央値との比）。</summary>
    public const double WeakRatio = 0.5;
    /// <summary>6つ装着の効果が「強すぎ」とする比率（中央値との比）。</summary>
    public const double StrongRatio = 1.5;

    private const ulong AlternativeSeed = 0x5E7A_0000_0000_0001;
    private const ulong PieceSeedBase = 0x5E7B_0000_0000_0002;
    internal static string SeedIdentity => $"{AlternativeSeed:x16}/{PieceSeedBase:x16}";

    /// <summary>
    /// 力の代理値。Build が届けた能力値・固有効果を、それぞれの上限（Content.StatCap / PowerCap）で
    /// 割って足した「予算の消化率」の合計。戦闘の出力そのものではなく、コア自身の上限設計を
    /// 重さの基準に使う。上限が0の行は無視する。
    /// </summary>
    public static double PowerScore(Build b)
    {
        if (b == null) return 0;
        double total = 0;
        foreach (var kv in b.Stats)
        {
            int cap = Content.StatCap(kv.Key);
            if (cap > 0) total += (double)kv.Value / cap;
        }
        foreach (var kv in b.Powers)
        {
            int cap = Content.PowerCap(kv.Key);
            if (cap > 0) total += (double)kv.Value / cap;
        }
        return total;
    }

    /// <summary>48組すべてを Content.Sets の並びのまま測る。</summary>
    public static IReadOnlyList<SetBalanceResult> MeasureAll()
        => MeasureSets(Content.Sets.Where(s => s.BossTypeName == null).ToArray(), null);

    /// <summary>
    /// 1組だけ測る。sixOverride を渡すと Content を書き換えずにその値での結果を得る
    /// （調整値の検討用）。MeasureAll と同じ装備・同じシードを使う。
    /// </summary>
    public static SetBalanceResult Measure(string setId, IReadOnlyList<PowerLine>? sixOverride = null)
    {
        var set = Content.GetSet(setId);
        if (set == null) throw new ArgumentException("未知のセットID: " + setId);
        if (set.BossTypeName != null) throw new ArgumentException("Boss profiles are outside the original 48-set comparison.", nameof(setId));
        Dictionary<string, IReadOnlyList<PowerLine>>? overrides = null;
        if (sixOverride != null) overrides = new Dictionary<string, IReadOnlyList<PowerLine>> { [set.Id] = sixOverride };
        return MeasureSets(new[] { set }, overrides)[0];
    }

    private static SetBalanceResult MeasureSet(SetDef def, GearContext context, IReadOnlyList<PowerLine>? sixOverride)
    {
        // Build.Compute は Content.GetSet（ライブ定義）を読むので、常にライブ側を書き換えて測る。
        var set = Content.GetSet(def.Id) ?? def;
        var rolled = new List<Relic>();
        var slots = new HashSet<Slot>();
        int ordinal = 0;
        foreach (var u in Content.Uniques)
        {
            if (u.SetId != set.Id) { ordinal++; continue; }
            // Content.Uniques 内の登録位置から作る固定シード（プロセスをまたいで安定）。
            rolled.Add(Loot.RollUnique(new Rng(PieceSeedBase ^ (ulong)ordinal), u, GearItemLevel));
            slots.Add(rolled[^1].Slot);
            ordinal++;
        }
        if (rolled.Count != 6) throw new InvalidOperationException("6部位でないセット: " + set.Id);
        if (slots.Count != 6) throw new InvalidOperationException("6枠がそろっていないセット: " + set.Id);
        var pieces = rolled.ToArray();

        double two = PowerScore(Build.Compute(EquipMixed(context, pieces, 2), HeroKey, 0));
        double three = PowerScore(Build.Compute(EquipMixed(context, pieces, 3), HeroKey, 0));
        double sixWith;
        double sixWithout;
        using (new SixPiecePatch(set, sixOverride))
        {
            sixWith = PowerScore(Build.Compute(EquipAll(pieces), HeroKey, 0));
        }
        using (new SixPiecePatch(set, Array.Empty<PowerLine>()))
        {
            sixWithout = PowerScore(Build.Compute(EquipAll(pieces), HeroKey, 0));
        }
        return new SetBalanceResult
        {
            Id = set.Id,
            Name = set.Name.Ja,
            SixPiece = sixOverride ?? set.SixPiece ?? Array.Empty<PowerLine>(),
            Baseline = context.BaselineScore,
            TwoPieces = two,
            ThreePieces = three,
            SixPieces = sixWith,
            SixPiecesWithoutBonus = sixWithout,
        };
    }

    /// <summary>指定した定義で測る（sixOverrides はセットID → 候補値。無いセットは Live の値）。</summary>
    public static IReadOnlyList<SetBalanceResult> MeasureSets(
        IReadOnlyList<SetDef> sets, IReadOnlyDictionary<string, IReadOnlyList<PowerLine>>? sixOverrides)
    {
        var context = BuildContext();
        var results = new List<SetBalanceResult>(sets.Count);
        foreach (var set in sets)
        {
            if (set.BossTypeName != null) continue;
            IReadOnlyList<PowerLine>? overrideLines = null;
            if (sixOverrides != null && set.Id != null) sixOverrides.TryGetValue(set.Id, out overrideLines);
            results.Add(MeasureSet(set, context, overrideLines));
        }
        return results;
    }

    /// <summary>6つ装着の利得の中央値（偶数個は中央2件の平均）。</summary>
    public static double MedianSixBonusGain(IReadOnlyList<SetBalanceResult> results)
    {
        if (results == null || results.Count == 0) return 0;
        var sorted = results.Select(r => r.SixBonusGain).OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>中央値との比から判定。"ok"（基準内）/ "strong"（強すぎ）/ "weak"（弱すぎ）。</summary>
    public static string Verdict(double sixBonusGain, double median)
    {
        if (median <= 0) return "strong";
        double ratio = sixBonusGain / median;
        if (ratio > StrongRatio) return "strong";
        if (ratio < WeakRatio) return "weak";
        return "ok";
    }

    private sealed class GearContext
    {
        public Relic[] Alternatives = new Relic[6];
        public double BaselineScore;
    }

    /// <summary>枠ごとの最良代替品（非セットの伝説・エピック）と、それを6枠装着した基準を組み立てる。</summary>
    private static GearContext BuildContext()
    {
        var context = new GearContext();
        for (int slot = 0; slot < context.Alternatives.Length; slot++)
        {
            Relic? best = null;
            double bestScore = double.NegativeInfinity;
            for (int rarity = (int)Rarity.Epic; rarity <= (int)Rarity.Legendary; rarity++)
            {
                for (int i = 0; i < AlternativeRolls; i++)
                {
                    var relic = RollAlternative((Slot)slot, (Rarity)rarity, i);
                    if (relic == null) continue;
                    double score = ItemScore(relic);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = relic;
                    }
                }
            }
            if (best == null) throw new InvalidOperationException("代替品の候補がありません: " + (Slot)slot);
            context.Alternatives[slot] = best;
        }
        context.BaselineScore = PowerScore(Build.Compute(EquipAll(context.Alternatives), HeroKey, 0));
        return context;
    }

    /// <summary>固定シードで1個引く。伝説がセット品だった場合は null（候補から外す）。</summary>
    private static Relic? RollAlternative(Slot slot, Rarity rarity, int index)
    {
        ulong seed = AlternativeSeed ^ ((ulong)slot << 44) ^ ((ulong)rarity << 36) ^ (uint)index;
        var relic = Loot.RollRelic(new Rng(seed), rarity, GearItemLevel, slot);
        if (relic.UniqueId != null && Content.TryGetUnique(relic.UniqueId, out var u) && u.SetId != null) return null;
        return relic;
    }


    /// <summary>最初の count 部位（登録順）+ 残りの枠は代替品。</summary>
    private static Profile EquipMixed(GearContext context, Relic[] pieces, int count)
    {
        var used = new HashSet<Slot>();
        var gear = new List<Relic>();
        for (int i = 0; i < count; i++)
        {
            gear.Add(pieces[i]);
            used.Add(pieces[i].Slot);
        }
        for (int slot = 0; slot < context.Alternatives.Length; slot++)
            if (!used.Contains((Slot)slot)) gear.Add(context.Alternatives[slot]);
        return EquipAll(gear);
    }

    private static Profile EquipAll(IEnumerable<Relic> gear)
    {
        var profile = Profile.CreateNew(99);
        foreach (var relic in gear)
        {
            profile.Stash.Add(relic);
            Rules.Equip(profile, HeroKey, relic.Uid);
        }
        return profile;
    }

    /// <summary>Build.Compute は Content.GetSet を読むので、計測の間だけライブ定義を差し替えて必ず戻す。</summary>
    private sealed class SixPiecePatch : IDisposable
    {
        private readonly SetDef set;
        private readonly PowerLine[] previous;

        public SixPiecePatch(SetDef set, IReadOnlyList<PowerLine>? replacement)
        {
            this.set = set;
            previous = set.SixPiece;
            set.SixPiece = replacement?.ToArray() ?? previous;
        }

        public void Dispose() => set.SixPiece = previous;
    }

    /// <summary>単体の遺物の代理値（代替品の選別用。Build 全体の上限とは独立に数える）。</summary>
    private static double ItemScore(Relic r)
    {
        double total = 0;
        foreach (var s in r.EffectiveStats())
        {
            int cap = Content.StatCap(s.Stat);
            if (cap > 0) total += (double)s.Value / cap;
        }
        foreach (var p in r.Powers)
        {
            int cap = Content.PowerCap(p.Power);
            if (cap > 0) total += (double)p.Value / cap;
        }
        return total;
    }
}
