using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>1遠征ぶんの通貨の記録（RunCurrency が作る）。ドリームダストの残高は遠征をまたいで持ち越す。</summary>
public sealed class RunCurrencyRecord
{
    public double Gold;
    public double DustPickup;      // 自分で拾った分（星の上乗せを含む）
    public double DustBonus;       // うち星の上乗せ分
    public double DustSalvage;     // MODの取引（未確保品の分解）で得た分（星は掛からない）
    public double DustConverted;   // 確保地点で欠片に換えた分
    public double ShardsFromDust;  // 換金で得た欠片
    public int SalvagedRelics;
    public long StarXp;
}

/// <summary>
/// v1.32 A「巡る富」を遠征シミュレーションに重ねる帳簿。本体の形（GameManager.GetKillGoldAmount →
/// Pickup_BaseGoldOrb.GrantGold、Pickup_DreamDust.onGiveDreamDust）とホストの実装
/// （HostAuthority.Currency / HostAuthority.Trades）と同じ経路・同じ丸めで数える。
/// 乱数は構成（星の有無）によらず同じ本数を引くので、なし／ありの比較は同じ擬似乱数列で行われる。
/// </summary>
public sealed class RunCurrency
{
    private readonly Build build;
    private readonly Rng rng;
    private readonly TradeAuthority authority = new();
    private readonly string playerKey;
    private readonly Action<RunCurrencyRecord> sink;
    private readonly List<Relic> scratch = new();
    private RunCurrencyRecord record = new();
    private long starXpBefore;

    public RunCurrency(Build build, ulong seed, int player, Action<RunCurrencyRecord> sink)
    {
        this.build = build;
        this.sink = sink;
        rng = new Rng(seed ^ (0xD15E_0000_0000_0001UL + (ulong)player));
        playerKey = "p" + player.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>燃料切れしない星図（最大ポイントを持つプロフィール）から通貨の Build を作る。</summary>
    public static Build BuildFortune(ulong profileSeed, string heroKey, bool withStars)
    {
        var profile = Profile.CreateNew(profileSeed);
        profile.Hero(heroKey).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
        return V132Builds.FortuneBuild(profile, heroKey, withStars);
    }

    public void BeginRun(Profile profile, string heroKey)
    {
        record = new RunCurrencyRecord();
        starXpBefore = profile.Hero(heroKey).StarXp;
    }

    public void OnKill(RunState run, MonsterTier tier, int zone)
    {
        double deviation = rng.NextDouble(), rollBase = rng.NextDouble(), rollPlayer = rng.NextDouble();
        int baseGold = V132Model.BaseKillGold(tier, zone - 1, deviation, rollBase);
        float multiplier = 1f + CurrencyStars.KillGoldMultiplierDelta(build);
        record.Gold += V132Model.PlayerKillGold(baseGold, multiplier, rollPlayer);
        if (V132Model.IsElite(tier))
        {
            double roll = rng.NextDouble(); // 構成によらず1本引く（比較のために消費を同じに保つ）。
            record.Gold += V132Model.EliteKillGoldBonus(baseGold, multiplier,
                CurrencyStars.EliteKillGoldPercent(build), roll);
        }
    }

    public void OnRoomCleared(Profile profile) => PickupDust(profile, V132Model.RoomDustPickups, V132Model.RoomDustPerPickup);

    public void OnBossKilled(Profile profile) => PickupDust(profile, V132Model.BossDustPickups, V132Model.BossDustPerPickup);

    private void PickupDust(Profile profile, int pickups, int amount)
    {
        bool delving = CurrencyStars.IsDelving(profile.Run);
        for (int i = 0; i < pickups; i++)
        {
            int got = V132Model.PickupDust(amount, build, delving, rng.NextDouble());
            record.DustPickup += got;
            if (got > amount) record.DustBonus += got - amount;
            DustWallet += got;
        }
    }

    /// <summary>現在のドリームダストの持ち高（遠征をまたいで保持。本体の dreamDust と同じ）。</summary>
    public int DustWallet { get; private set; }

    /// <summary>確保地点：未確保のコモン・アンコモンをドリームダストに換え（MODの取引）、ダストを欠片に換える。</summary>
    public void AtSecurePoint(Profile profile)
    {
        SalvageForDust(profile);
        ConvertDust(profile);
    }

    private void SalvageForDust(Profile profile)
    {
        var run = profile.Run;
        if (run == null) return;
        scratch.Clear();
        foreach (var relic in run.Satchel)
            if (relic.Rarity <= Rarity.Uncommon && !relic.Locked)
                scratch.Add(relic);
        foreach (var relic in scratch)
        {
            var request = new TradeRequest
            {
                Token = ++token,
                Kind = TradeKind.SalvageForDust,
                SalvageUid = TradeWire.PackUid(relic.Uid),
                Rarity = (int)relic.Rarity,
                Enhance = relic.Enhance,
            };
            var decision = authority.Evaluate(playerKey, run.RunId, request, gold: 0, dust: DustWallet);
            if (!decision.Ok || decision.Replayed) continue;
            if (Rules.SalvageUnsecured(profile, relic.Uid) == null) continue;
            DustWallet += decision.EarnDust;
            record.DustSalvage += decision.EarnDust;
            record.SalvagedRelics++;
        }
    }

    private void ConvertDust(Profile profile)
    {
        // Rules.ConvertDust は確保地点（AwaitingChoice / GearWindow）でのみ使える。
        while (DustWallet >= Economy.DustPerBatch && profile.Run is { AwaitingChoice: true } or { GearWindow: true })
        {
            int batches = Math.Min(DustWallet / Economy.DustPerBatch, Economy.MaxBatchesPerTrade);
            int paid = batches * Economy.DustPerBatch;
            var request = new TradeRequest { Token = ++token, Kind = TradeKind.DustToShards, Batches = batches };
            var decision = authority.Evaluate(playerKey, profile.Run.RunId, request, gold: 0, dust: DustWallet);
            if (!decision.Ok || decision.Replayed) return;
            Rules.ConvertDust(profile, paid);
            DustWallet -= paid;
            record.DustConverted += paid;
            record.ShardsFromDust += batches * Economy.ShardsPerBatch;
        }
    }

    private long token;

    public void EndRun(Profile profile, string heroKey)
    {
        record.StarXp = profile.Hero(heroKey).StarXp - starXpBefore;
        sink(record);
    }
}

/// <summary>経済の計測結果（1遠征あたりの全プレイヤー平均）。</summary>
internal sealed class EconomyResult
{
    public string Policy = "";
    public int Depth;
    public bool WithStars;
    public double Gold, DustPickup, DustBonus, DustSalvage, DustConverted, ShardsFromDust;
    public double ShardsNet, StarXp, EnhanceAverage, TuningNet;
    public int Runs, Players;
}

/// <summary>遠征の鍛錬1計測（1旅人・1星の組み合わせ・1深度・1方針）。</summary>
internal sealed class GrowthResult
{
    public string HeroKey = "";
    public string Variant = "";
    public int Depth;
    public string Policy = "";
    public int Cap;
    public int Threshold;
    public int[] StacksAfterZone = Array.Empty<int>();
    public int FinalStacks;
    public int CapZone;
    public double EffectText; // 1つ目の能力値の合計（RunGrowth.StatTotal）。
    public double EffectText2; // 2つ目の能力値の合計（あれば）。
    public string EffectName = "";
    public string EffectName2 = "";
}

/// <summary>v1.32 の計測（--mode v132stars）。経済は実際の遠征ループ（Simulation）に通貨を重ね、
/// 鍛錬は RunGrowthLedger に模型化した出来事を流す。どちらもコアの公開APIだけを使う。</summary>
internal static class V132Simulation
{
    public const string EconomyHero = "Hero_Vesper"; // 巡る富は全旅人共通の外縁星団。代表として Vesper。
    public static readonly string[] GrowthHeroes = { "Hero_Vesper", "Hero_Cetus", "Hero_Mist", "Hero_Husk" };
    public static readonly string[] GrowthHeroNames = { "Vesper", "Cetus", "Mist", "空殻" };
    /// <summary>鍛錬の星の組み合わせ（表示順）。</summary>
    public static readonly (string Key, string Ja, string PurchasesKind)[] GrowthVariants =
    {
        ("entry", "入口のみ", "g1"),
        ("entry-double", "入口のみ＋速さ2倍", "g1+double"),
        ("cap1", "上限星1つ", "g1+m1"),
        ("cap2", "上限星2つ", "g1+m1+m2"),
        ("cap2-effect", "上限2＋効果+50%", "g1+m1+m2+effect"),
        ("cap2-double", "上限2＋速さ2倍", "g1+m1+m2+double"),
    };

    // ───── 経済（巡る富）─────

    public static List<EconomyResult> RunEconomy(Options options, string policy)
    {
        var results = new List<EconomyResult>();
        for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
            foreach (bool withStars in new[] { false, true })
                results.Add(RunEconomyOnce(options, policy, depth, withStars));
        return results;
    }

    private static EconomyResult RunEconomyOnce(Options options, string policy, int depth, bool withStars)
    {
        var simOptions = options.Clone();
        simOptions.Policy = policy;
        var seeds = new Rng(options.Seed ^ (0xFEED_0000_0000_0000UL + (ulong)depth));
        var records = new List<RunCurrencyRecord>();
        var build = RunCurrency.BuildFortune(seeds.NextULong(), EconomyHero, withStars);
        var sim = new Simulation(simOptions)
        {
            RunHeroKey = EconomyHero,
            RunDreamDepth = depth,
            CurrencyFactory = player => new RunCurrency(build, options.Seed, player, records.Add),
        };
        sim.Run();
        var result = new EconomyResult
        {
            Policy = policy,
            Depth = depth,
            WithStars = withStars,
            Runs = options.Runs,
            Players = options.Players,
        };
        if (records.Count == 0) return result;
        double Div(double sum) => sum / records.Count;
        foreach (var r in records)
        {
            result.Gold += r.Gold;
            result.DustPickup += r.DustPickup;
            result.DustBonus += r.DustBonus;
            result.DustSalvage += r.DustSalvage;
            result.DustConverted += r.DustConverted;
            result.ShardsFromDust += r.ShardsFromDust;
            result.StarXp += r.StarXp;
        }
        result.Gold = Div(result.Gold);
        result.DustPickup = Div(result.DustPickup);
        result.DustBonus = Div(result.DustBonus);
        result.DustSalvage = Div(result.DustSalvage);
        result.DustConverted = Div(result.DustConverted);
        result.ShardsFromDust = Div(result.ShardsFromDust);
        result.StarXp = Div(result.StarXp);
        // 欠片の純増減・調律石・装備強化は Simulation 本体の計測（遠征ごとの全プレイヤー平均）を使う。
        double shards = 0, tuning = 0, enhance = 0;
        for (int run = 0; run < simOptions.Runs; run++)
            for (int player = 0; player < simOptions.Players; player++)
            {
                shards += sim.Samples[run, (int)Metric.Shards, player];
                tuning += sim.Samples[run, (int)Metric.Tuning, player];
                enhance += sim.Samples[run, (int)Metric.Enhance, player];
            }
        result.ShardsNet = shards / (simOptions.Runs * simOptions.Players);
        result.TuningNet = tuning / (simOptions.Runs * simOptions.Players);
        result.EnhanceAverage = enhance / (simOptions.Runs * simOptions.Players);
        return result;
    }

    // ───── 遠征の鍛錬 ─────

    public static List<GrowthResult> RunGrowth(Options options)
    {
        StarClusters.RegisterAllGenerated();
        var results = new List<GrowthResult>();
        foreach (string policy in new[] { "secure", "greedy" })
            for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
                for (int hero = 0; hero < GrowthHeroes.Length; hero++)
                    foreach (var variant in GrowthVariants)
                        results.Add(SimulateGrowth(options, GrowthHeroes[hero], variant, depth, policy));
        return results;
    }

    /// <summary>割り当て検証を通れず直接割り当てに落ちた星の合計（方法の正確さのために報告書に記す）。</summary>
    public static long DirectWrites { get; private set; }

    /// <summary>
    /// 感度分析：模型の仮定（1部屋の被ダメージ%・障壁吸収%・パリィ回数・会心どめ割合）を変えて
    /// 入口のみの構成の上限到達ゾーンを測る。label は報告書の行名。
    /// </summary>
    public static List<GrowthResult> RunGrowthSensitivity(Options options,
        (string Label, double Damage, double Shield, double Parries, double CritShare)[] scenarios)
    {
        StarClusters.RegisterAllGenerated();
        var results = new List<GrowthResult>();
        foreach (var s in scenarios)
            foreach (string policy in new[] { "secure", "greedy" })
                for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
                    foreach (string heroKey in GrowthHeroes)
                    {
                        var r = SimulateGrowth(options, heroKey, GrowthVariants[0], depth, policy,
                            s.Damage, s.Shield, s.Parries, s.CritShare);
                        r.Variant = s.Label;
                        results.Add(r);
                    }
        return results;
    }

    private static GrowthResult SimulateGrowth(Options options, string heroKey, (string Key, string Ja, string PurchasesKind) variant, int depth, string policy,
        double damageBasePct = V132Model.DamageTakenPerRoomPct, double shieldBasePct = V132Model.ShieldAbsorbedPerRoomPct,
        double parriesPerRoom = V132Model.ParriesPerRoom, double critBasicShare = -1)
    {
        if (critBasicShare < 0) critBasicShare = V132Model.CritBasicKillShare;
        var profile = Profile.CreateNew(1);
        profile.Hero(heroKey).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
        var tree = HeroSigils.TreeFor(heroKey);
        string growthId = V132Builds.GrowthStarId(tree)
            ?? throw new InvalidOperationException(heroKey + " に RunGrowth の星がない");
        var purchases = new List<StarPurchase> { new(growthId) };
        var caps = V132Builds.GrowthCapStarIds(tree, growthId);
        if (variant.PurchasesKind.Contains("m1") && caps.Count > 0) purchases.Add(new StarPurchase(caps[0]));
        if (variant.PurchasesKind.Contains("m2") && caps.Count > 1) purchases.Add(new StarPurchase(caps[1]));
        if (variant.PurchasesKind.Contains("double"))
            purchases.Add(V132Builds.GrowthChoicePurchase(tree, growthId, "double")!.Value);
        else if (variant.PurchasesKind.Contains("effect"))
            purchases.Add(V132Builds.GrowthChoicePurchase(tree, growthId, "effect")!.Value);
        V132Builds.BuyStars(profile, heroKey, purchases);
        DirectWrites += V132Builds.DirectWrites;
        var build = Build.Compute(profile, heroKey, 0);
        var entry = build.RunGrowths.FirstOrDefault(e => e.StarId == growthId)
            ?? throw new InvalidOperationException(heroKey + " の Build に RunGrowth が組み上がっていない");

        var ledger = new RunGrowthLedger();
        ledger.EnsureRun("v132");
        const string owner = "sim";
        int roomsPerZone = V132Model.RoomsPerZone(options.Rooms, depth);
        double killsPerRoom = options.Lesser + options.Normal + options.MiniBoss;
        var stacks = new int[options.Zones];
        for (int zone = 1; zone <= options.Zones; zone++)
        {
            int heat = V132Model.HeatDuringZone(policy, zone);
            for (int room = 0; room < roomsPerZone; room++)
            {
                ledger.Gain(owner, entry, entry.Trigger, entry.Trigger switch
                {
                    RunGrowthTrigger.DamageTakenMaxHpPct => V132Model.DamageTakenPct(damageBasePct, depth, heat),
                    RunGrowthTrigger.ShieldAbsorbedMaxHpPct => V132Model.ShieldAbsorbedPct(shieldBasePct, depth, heat),
                    RunGrowthTrigger.ParrySuccess => parriesPerRoom,
                    _ => killsPerRoom * critBasicShare,
                });
            }
            ledger.Gain(owner, entry, entry.Trigger, entry.Trigger switch
            {
                RunGrowthTrigger.DamageTakenMaxHpPct => V132Model.DamageTakenPct(damageBasePct * V132Model.BossRoomFactor, depth, heat),
                RunGrowthTrigger.ShieldAbsorbedMaxHpPct => V132Model.ShieldAbsorbedPct(shieldBasePct * V132Model.BossRoomFactor, depth, heat),
                RunGrowthTrigger.ParrySuccess => V132Model.ParriesPerBoss,
                // ボスは基本攻撃だけで倒す仮定を置かない（会心の基本攻撃でのとどめは数えない）。
                _ => 0,
            });
            stacks[zone - 1] = ledger.Stacks(owner, entry.StarId);
        }
        int final = ledger.Stacks(owner, entry.StarId);
        return new GrowthResult
        {
            HeroKey = heroKey,
            Variant = variant.Key,
            Depth = depth,
            Policy = policy,
            Cap = entry.Cap,
            Threshold = entry.Threshold,
            StacksAfterZone = stacks,
            FinalStacks = final,
            CapZone = V132Model.FirstZoneAtCap(stacks, entry.Cap),
            EffectText = global::SodRpg.Core.Game.RunGrowth.StatTotal(entry, entry.Effects[0], final),
            EffectText2 = entry.Effects.Count > 1 ? global::SodRpg.Core.Game.RunGrowth.StatTotal(entry, entry.Effects[1], final) : 0,
            EffectName = StatShort(entry.Effects[0].Stat),
            EffectName2 = entry.Effects.Count > 1 ? StatShort(entry.Effects[1].Stat) : "",
        };
    }

    private static string StatShort(Stat stat) => stat switch
    {
        Stat.AttackFlat => "攻撃力",
        Stat.Armor => "防御",
        Stat.MaxHealthFlat => "最大HP",
        Stat.CritDamagePct => "会心ダメージ%",
        Stat.ShieldPower => "シールド%",
        _ => stat.ToString(),
    };
}
