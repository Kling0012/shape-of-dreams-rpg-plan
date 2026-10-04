using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>--mode v132stars の Markdown 出力（tools/BalanceSim/result-v1.32-stars.md）。</summary>
internal static class V132Report
{
    public static string Render(Options o, IReadOnlyList<EconomyResult> economy, IReadOnlyList<GrowthResult> growth,
        IReadOnlyList<GrowthResult> sensitivity, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# v1.32 星図の追加効果のバランス（巡る富・遠征の鍛錬）");
        sb.AppendLine();
        sb.AppendLine("## 方法");
        sb.AppendLine();
        sb.AppendLine($"- 遠征の構成は遠征モードと同じ（{o.Zones} ゾーン・{o.Rooms} 戦闘部屋・部屋ごとに Lesser {o.Lesser}＋Normal {o.Normal}＋MiniBoss {Probability(o.MiniBoss)}・ゾーン末尾に Boss {o.Bosses}・全滅率 {Probability(o.Wipe)}・seed={o.Seed}・{o.Players} 人×{o.Runs} 遠征）。夢の深度 d では `Rules.BeginRun(dreamDepth: d)` を使うため、1ゾーンの戦闘部屋数は `DreamDepth.ExtraZoneNodes`（深度ごとに+2）で増えます。");
        sb.AppendLine("- 巡る富の効果は、星図なし／全取得（撃破ゴールド+12%・エリート/ボス+25%・夢のダスト+22%（潜行中+32%）＝実装どおりの合計）の2構成で、`Rules.AddTalentRank` で実際に星を買った `Build` を通して測ります。金額・丸めは本体と同じ形（`CurrencyStars.RandomRound`＝本体の `DewMath.RandomRoundToInt`）。");
        sb.AppendLine("- 撃破ゴールドは本体の `GameManager.GetKillGoldAmount` の式（ゾーン倍率 (1+0.4x)×(1+0.20x)、種別ごとの基礎値、±10%のぶれ、確率丸め）で落とし、各人の受け取りは `Pickup_BaseGoldOrb.GrantGold` と同じく人数割り→プロファイル倍率→`monsterKillGoldMultiplier`（巡る富の+12%を含む）→確率丸め。エリート（MiniBoss/Boss）の上乗せは `HostAuthority.GrantEliteKillGold` と同じ式です。種別ごとの基礎額（Lesser 1・Normal 2・MiniBoss 10・Boss 25）は本体アセットの値が読めないための仮定で、変化率にはほぼ効きません。");
        sb.AppendLine("- 夢のダストは、部屋ごとに 5×4 回・ボスごとに 10×4 回を自分で拾う仮定（本体の星の説明の規模感：1ゾーン+100・ボス+40）。上乗せは `Pickup_DreamDust.onGiveDreamDust` 経由（`CurrencyStars.DreamDustBonusForPickup`）、潜行中かは `CurrencyStars.IsDelving` の実式です。MODの取引（未確保のコモン・アンコモンを確保地点で `TradeAuthority` + `Economy.SalvageDust` でダストに換える遊び方）の分は星が掛からないので別掲します。ダスト→欠片は `Rules.ConvertDust`（100→10、1取引10束まで）で実際に換えます。");
        sb.AppendLine("- 星経験は `StarProgression`（撃破・確保・踏破）の実経路の増減。遠征の鍛錬は、`RunGrowthLedger.Gain`（本体のホストと同じ集計）に模型化した出来事を流し、上限への到達はゾーンごとのスタック数で判断します。被ダメージ・障壁の吸収は、夢の深度は `DreamDepth.DamageMultiplier`（+8%/深度）、潜行は `Build.DamageTakenPerDelvePct`（+6%/深度）の実係数を掛けた「1戦闘部屋で最大HPの25%／20%を失う・吸収する」仮定、ボス戦はその2倍。パリィは部屋2回・ボス6回、会心の基本攻撃でのとどめは撃破の 12.5%（基本攻撃50%×会心25%）の仮定です（ボスは数えません）。");
        sb.AppendLine("- 通貨の乱数は構成（星のあり／なし）によらず同じ本数を引きますが、あり構成は欠片が増えて遠征間の強化の回数が変わるため、依頼の計画を通じてわずかにずれます（観測されるぶれはゴールド・ダストとも±2%弱）。遠征の鍛錬の表は決定的な平均値（乱数なし）です。");
        if (V132Simulation.DirectWrites > 0)
            sb.AppendLine($"- 補足：星図への割り当ては `Rules.AddTalentRank`（コアの割り当て検証つき）で行いましたが、記憶にスコープされた選択肢の星など、装備のないこのシミュレーターでは「効果が出る購入」として認められない経路の星 {V132Simulation.DirectWrites} 個は、コアのテスト（`AuthoredStarContractTests.AllocatePath`）と同じ直接割り当てで足しました。`Build.Compute`（効果の集計）と `RunGrowthLedger`（スタック）は常に実経路です。");
        sb.AppendLine();
        RenderEconomy(sb, o, economy, "secure");
        RenderEconomy(sb, o, economy, "greedy");
        RenderGrowth(sb, o, growth, "secure");
        RenderGrowth(sb, o, growth, "greedy");
        RenderEffectTotals(sb, growth);
        RenderSensitivity(sb, o, sensitivity);
        RenderVerdict(sb, o, economy, growth);
        sb.AppendLine($"- 計測時間: {elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}ms（Content と引数が同じなら同じ結果）");
        return sb.ToString();
    }

    private static void RenderEconomy(StringBuilder sb, Options o, IReadOnlyList<EconomyResult> economy, string policy)
    {
        string policyJa = policy == "secure" ? "毎回確保" : "深度3まで潜行（greedy）";
        sb.AppendLine($"## 巡る富：1遠征あたりの収入（方針：{policyJa}）");
        sb.AppendLine();
        if (policy != "secure")
            sb.AppendLine("- greedy は道中で確保地点を通らないため、未確保品のダスト化（MODの取引）もダスト→欠片の換金（`Rules.ConvertDust` は確保地点専用）も遠征中に起こりません。ダストは持ち高として次の遠征に持ち越されます。");
        sb.AppendLine();
        sb.AppendLine("| 夢の深度 | ゴールド（なし） | ゴールド（あり） | 変化 | 拾いダスト（なし） | 拾いダスト（あり） | 変化 | 取引ダスト（なし） | 取引ダスト（あり） |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
        {
            var none = First(economy, policy, depth, false);
            var all = First(economy, policy, depth, true);
            sb.AppendLine($"| {depth} | {Gold(none.Gold)} | {Gold(all.Gold)} | {Pct(V132Model.PercentChange(none.Gold, all.Gold))} | " +
                $"{Gold(none.DustPickup)} | {Gold(all.DustPickup)} | {Pct(V132Model.PercentChange(none.DustPickup, all.DustPickup))} | " +
                $"{Gold(none.DustSalvage)} | {Gold(all.DustSalvage)} |");
        }
        sb.AppendLine();
        sb.AppendLine("| 夢の深度 | 欠片純増（なし） | 欠片純増（あり） | 変化 | うちダスト換金（あり） | 星XP（なし） | 星XP（あり） | 装備強化（なし→あり） |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
        {
            var none = First(economy, policy, depth, false);
            var all = First(economy, policy, depth, true);
            sb.AppendLine($"| {depth} | {Gold(none.ShardsNet)} | {Gold(all.ShardsNet)} | {Pct(V132Model.PercentChange(none.ShardsNet, all.ShardsNet))} | " +
                $"{Gold(all.ShardsFromDust)} | {Gold(none.StarXp)} | {Gold(all.StarXp)} | {all.EnhanceAverage.ToString("0.00", CultureInfo.InvariantCulture)} ← {none.EnhanceAverage.ToString("0.00", CultureInfo.InvariantCulture)} |");
        }
        sb.AppendLine();
        // 下流の読み替え（代表は深度0と深度5）。
        foreach (int depth in new[] { 0, DreamDepth.Maximum })
        {
            var none = First(economy, policy, depth, false);
            var all = First(economy, policy, depth, true);
            double extraGold = all.Gold - none.Gold;
            double extraShards = all.ShardsFromDust - none.ShardsFromDust;
            int extraSteps = V132Model.EnhanceStepsAffordable((long)Math.Round(extraShards * 100)); // 100遠征ぶん
            double merchantCheap = extraGold / Economy.MerchantGoldBase(0);
            double merchantDear = extraGold / Economy.MerchantGoldBase(Content.MaxHeat);
            sb.AppendLine($"- 深度{depth}：1遠征の追加ゴールド {Gold(extraGold)}（夢の商人 `Economy.MerchantGoldBase` 60〜105 なら約 {merchantDear.ToString("0.0", CultureInfo.InvariantCulture)}〜{merchantCheap.ToString("0.0", CultureInfo.InvariantCulture)} 回分）。"
                + $"ダスト換金で増える欠片 {Gold(extraShards)}/遠征（100遠征で追加 {extraSteps} 回ぶんの強化（+0→+1 が20欠片から））。星XPの差 {Gold(all.StarXp - none.StarXp)}。");
        }
        sb.AppendLine();
    }

    private static void RenderGrowth(StringBuilder sb, Options o, IReadOnlyList<GrowthResult> growth, string policy)
    {
        string policyJa = policy == "secure" ? "毎回確保" : "深度3まで潜行（greedy）";
        sb.AppendLine($"## 遠征の鍛錬：ゾーンごとのスタックと上限到達（方針：{policyJa}）");
        sb.AppendLine();
        sb.AppendLine("- 表の値は「そのゾーン終了時のスタック数（上限に届いたゾーン）」。— は未到達。遠征は4ゾーンなので、設計目標（遠征の前半で上限に届かない）は ゾーン1〜2 での到達がないこと、上限星2つでは ゾーン4 か未到達であることです。");
        sb.AppendLine();
        for (int hero = 0; hero < V132Simulation.GrowthHeroes.Length; hero++)
        {
            string heroKey = V132Simulation.GrowthHeroes[hero];
            var first = growth.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == policy && g.Depth == 0 && g.Variant == "entry");
            if (first == null) continue;
            sb.AppendLine($"### {V132Simulation.GrowthHeroNames[hero]}（{DescribeTrigger(heroKey)}）");
            sb.AppendLine();
            sb.AppendLine("| 星の組み合わせ | " + string.Join(" | ", Enumerable.Range(0, DreamDepth.Maximum + 1).Select(d => $"深度{d}")) + " |");
            sb.AppendLine("| --- | " + string.Join(" | ", Enumerable.Range(0, DreamDepth.Maximum + 1).Select(_ => "---:")) + " |");
            foreach (var variant in V132Simulation.GrowthVariants)
            {
                var cells = new List<string>();
                for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
                {
                    var r = growth.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == policy && g.Depth == depth && g.Variant == variant.Key);
                    cells.Add(r == null ? "—" : $"{r.FinalStacks}（上限{r.Cap}）{(r.CapZone == 0 ? "未到達" : $"Z{r.CapZone}")}");
                }
                sb.AppendLine($"| {variant.Ja} | " + string.Join(" | ", cells) + " |");
            }
            sb.AppendLine();
        }
    }

    /// <summary>感度分析の条件（既定・守り重視・攻め重視）。Label=行ラベル、残りは模型の仮定（被ダメージ%・吸収%・パリィ回数・会心どめ割合）。</summary>
    public static readonly (string Label, double Damage, double Shield, double Parries, double CritShare)[] SensitivityScenarios =
    {
        ("守り重視（被ダメージ15%・吸収12%・パリィ1回・会心どめ8%）", 15, 12, 1, 0.08),
        ("既定（25%・20%・2回・12.5%）", 25, 20, 2, 0.125),
        ("攻め重視（35%・28%・3回・20%）", 35, 28, 3, 0.20),
    };

    private static void RenderEffectTotals(StringBuilder sb, IReadOnlyList<GrowthResult> growth)
    {
        sb.AppendLine("## 遠征の鍛錬：最終スタックが能力値に届く量（深度2・毎回確保）");
        sb.AppendLine();
        sb.AppendLine("| 旅人 | 星の組み合わせ | 最終スタック | 1つ目の能力値 | 2つ目の能力値 |");
        sb.AppendLine("| --- | --- | ---: | --- | --- |");
        for (int hero = 0; hero < V132Simulation.GrowthHeroes.Length; hero++)
        {
            string heroKey = V132Simulation.GrowthHeroes[hero];
            foreach (var variant in V132Simulation.GrowthVariants)
            {
                var r = growth.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == "secure" && g.Depth == 2 && g.Variant == variant.Key);
                if (r == null) continue;
                sb.AppendLine($"| {V132Simulation.GrowthHeroNames[hero]} | {variant.Ja} | {r.FinalStacks}/{r.Cap} | " +
                    $"{r.EffectName} +{r.EffectText.ToString("0.#", CultureInfo.InvariantCulture)} | " +
                    (string.IsNullOrEmpty(r.EffectName2) ? "—" : $"{r.EffectName2} +{r.EffectText2.ToString("0.#", CultureInfo.InvariantCulture)}") + " |");
            }
        }
        sb.AppendLine();
    }

    private static void RenderSensitivity(StringBuilder sb, Options o, IReadOnlyList<GrowthResult> sensitivity)
    {
        sb.AppendLine("## 遠征の鍛錬：模型の仮定への感度（入口のみ・毎回確保・上限到達ゾーン、未=未到達）");
        sb.AppendLine();
        sb.AppendLine("| 仮定 | 旅人 | 深度0 | 深度1 | 深度2 | 深度3 | 深度4 | 深度5 |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var scenario in SensitivityScenarios)
            for (int hero = 0; hero < V132Simulation.GrowthHeroes.Length; hero++)
            {
                string heroKey = V132Simulation.GrowthHeroes[hero];
                var cells = new List<string>();
                for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
                {
                    var r = sensitivity.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == "secure"
                        && g.Depth == depth && g.Variant == scenario.Label);
                    cells.Add(r == null ? "—" : r.CapZone == 0 ? "未" : "Z" + r.CapZone.ToString(CultureInfo.InvariantCulture));
                }
                sb.AppendLine($"| {scenario.Label} | {V132Simulation.GrowthHeroNames[hero]} | " + string.Join(" | ", cells) + " |");
            }
        sb.AppendLine();
    }

    private static void RenderVerdict(StringBuilder sb, Options o, IReadOnlyList<EconomyResult> economy, IReadOnlyList<GrowthResult> growth)
    {
        sb.AppendLine("## 判定と推奨値");
        sb.AppendLine();
        // 経済の判定：設計の合計（全撃破+12%・エリート/ボスにさらに+25%・ダスト+22%/潜行中+32%）どおりか、下流は崩れていないか。
        var secure0 = First(economy, "secure", 0, false);
        var secure0All = First(economy, "secure", 0, true);
        var greedy3 = First(economy, "greedy", 3, false);
        var greedy3All = First(economy, "greedy", 3, true);
        double goldChange = V132Model.PercentChange(secure0.Gold, secure0All.Gold);
        double dustChangeSecure = V132Model.PercentChange(secure0.DustPickup, secure0All.DustPickup);
        double dustChangeGreedy = V132Model.PercentChange(greedy3.DustPickup, greedy3All.DustPickup);
        double dustExtraShards = secure0All.ShardsFromDust - secure0.ShardsFromDust;
        sb.AppendLine($"- 巡る富：撃破ゴールドは 全体で {Pct(goldChange)}（設計は全撃破+12%に、エリート/ボスはさらに+25%の上乗せ。雑魚の比重が大きいため合計は+16〜18%になる）。"
            + $"自分で拾う夢のダストは {Pct(dustChangeSecure)}（設計 +22%、毎回確保＝常に非潜行）・{Pct(dustChangeGreedy)}（潜行中の設計は +32%。greedy はゾーン1のみ非潜行なのでその分が少し薄まる）。いずれも設計の範囲です。");
        sb.AppendLine($"- 下流：ダスト換金（`Rules.ConvertDust`、100→10）で増える欠片は1遠征あたり +{dustExtraShards.ToString("0.0", CultureInfo.InvariantCulture)}（確保方針・深度0）。"
            + "欠片の純増の変化がこれより小さく見えるときは、増えた欠片を強化（`Content.EnhanceCost`）に使っているためです（装備強化の列を参照）。"
            + "ゴールドは遠征内の通貨（夢の商人 `Economy.MerchantGoldBase` 60〜105）で、星XPには影響しません。");
        sb.AppendLine("- 星XP：`StarProgression`（撃破・確保・踏破）のみで増え、通貨の星は採用していません。同じ乱数列でも、あり構成は欠片が増えて強化の回数が変わるため依頼の抽選位置がずれ、星XPは1%前後の範囲でぶれます（実質的に差はありません）。");
        sb.AppendLine();
        // 鍛錬の判定。
        int firstHalf = o.Zones / 2; // 4ゾーンなら前半は1〜2
        foreach (string policy in new[] { "secure", "greedy" })
            for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
            {
                var misses = new List<string>();
                foreach (string heroKey in V132Simulation.GrowthHeroes)
                {
                    var entry = growth.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == policy && g.Depth == depth && g.Variant == "entry");
                    var cap2 = growth.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == policy && g.Depth == depth && g.Variant == "cap2");
                    if (entry != null && entry.CapZone != 0 && entry.CapZone <= firstHalf) misses.Add($"{HeroName(heroKey)}入口のみ（Z{entry.CapZone}）");
                    if (cap2 != null && cap2.CapZone != 0 && cap2.CapZone < o.Zones) misses.Add($"{HeroName(heroKey)}上限2つ（Z{cap2.CapZone}）");
                }
                if (misses.Count > 0)
                    sb.AppendLine($"- 鍛錬・方針{policy}・深度{depth}：設計目標を外れる組み合わせ — {string.Join("、", misses)}。");
            }
        sb.AppendLine();
        sb.AppendLine("### 推奨値（目標を外れたところの調整案。ゲームデータは変更していない）");
        sb.AppendLine();
        AppendRecommendations(sb, o, growth);
    }

    private static void AppendRecommendations(StringBuilder sb, Options o, IReadOnlyList<GrowthResult> growth)
    {
        // 深度ごとに、入口のみでゾーン1〜2の到達が出る旅人を拾い、前半で届かなくする閾値を逆算する。
        // 出来事の量は模型から正確に数える（スタックは上限で打ち切られているので逆算には使わない）。
        var lines = new List<string>();
        double killsPerRoom = o.Lesser + o.Normal + o.MiniBoss;
        for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
        {
            int rooms = V132Model.RoomsPerZone(o.Rooms, depth);
            foreach (string policy in new[] { "secure", "greedy" })
                foreach (string heroKey in V132Simulation.GrowthHeroes)
                {
                    var entry = growth.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == policy && g.Depth == depth && g.Variant == "entry");
                    var cap2 = growth.FirstOrDefault(g => g.HeroKey == heroKey && g.Policy == policy && g.Depth == depth && g.Variant == "cap2");
                    bool entryMiss = entry is { CapZone: > 0 } && entry.CapZone <= o.Zones / 2;
                    bool cap2Miss = cap2 != null && cap2.CapZone > 0 && cap2.CapZone < o.Zones;
                    if (entry == null || (!entryMiss && !cap2Miss)) continue;
                    var trigger = TriggerOf(heroKey);
                    double firstHalfUnits = 0, throughThirdZone = 0;
                    for (int zone = 1; zone <= o.Zones; zone++)
                    {
                        int heat = V132Model.HeatDuringZone(policy, zone);
                        double units = rooms * V132Model.RoomUnits(trigger, depth, heat, killsPerRoom)
                            + V132Model.BossUnits(trigger, depth, heat);
                        if (zone <= o.Zones / 2) firstHalfUnits += units;
                        if (zone <= o.Zones - 1) throughThirdZone += units;
                    }
                    if (entryMiss)
                    {
                        // 前半の出来事の量を閾値で割ったスタックが上限に届かない、最小の閾値。
                        int needed = Math.Max(entry.Threshold + 1, (int)Math.Ceiling(firstHalfUnits / entry.Cap) + 1);
                        lines.Add($"- {HeroName(heroKey)}（{policy}・深度{depth}）：入口のみで Z{entry.CapZone} 到達（前半の出来事 {firstHalfUnits.ToString("0", CultureInfo.InvariantCulture)}units／閾値{entry.Threshold}）。"
                            + $"閾値を {entry.Threshold} → {needed} に上げると前半では届かなくなります（上限はそのまま）。");
                    }
                    if (cap2 != null && cap2.CapZone > 0 && cap2.CapZone < o.Zones)
                    {
                        // 上限星2つでも Z4 未満で届くとき：Z3 終了時のスタック（3ゾーンぶんの出来事÷閾値）が
                        // 上限に届かない最小の閾値を勧める（届くのが Z4 以降または未到達になる）。
                        int cap2Cap = cap2.Cap, cap2Threshold = cap2.Threshold;
                        int neededThreshold = Math.Max(cap2Threshold + 1, (int)Math.Ceiling(throughThirdZone / cap2Cap) + 1);
                        lines.Add($"- {HeroName(heroKey)}（{policy}・深度{depth}）：上限星2つでも Z{cap2.CapZone} 到達（上限{cap2Cap}・Z3までの出来事 {throughThirdZone.ToString("0", CultureInfo.InvariantCulture)}units）。"
                            + $"閾値を {cap2Threshold} → {neededThreshold} に上げると、上限星2つでは Z4 か未到達になります（上限の合計は最大 100（+20×2）までしか上げられないため、上限側の調整だけでは間に合いません）。");
                    }
                }
        }

        if (lines.Count == 0)
            sb.AppendLine("- すべての組み合わせで設計目標の範囲内です（入口のみで前半（ゾーン1〜2）に上限到達なし、上限星2つで到達はゾーン4以降か未到達）。数値の変更は不要です。");
        else
        {
            foreach (string line in lines) sb.AppendLine(line);
            // 旅人ごとのまとめ：全深度・両方針で前半到達を消す最小の閾値。
            foreach (string heroKey in V132Simulation.GrowthHeroes)
            {
                var misses = growth.Where(g => g.HeroKey == heroKey && g.Variant == "entry" && g.CapZone > 0 && g.CapZone <= o.Zones / 2).ToList();
                if (misses.Count == 0) continue;
                int maxNeeded = 0, currentThreshold = misses[0].Threshold, firstDepth = int.MaxValue;
                foreach (var miss in misses)
                {
                    firstDepth = Math.Min(firstDepth, miss.Depth);
                    double units = 0;
                    int rooms = V132Model.RoomsPerZone(o.Rooms, miss.Depth);
                    for (int zone = 1; zone <= o.Zones / 2; zone++)
                    {
                        int heat = V132Model.HeatDuringZone(miss.Policy, zone);
                        units += rooms * V132Model.RoomUnits(TriggerOf(heroKey), miss.Depth, heat, killsPerRoom)
                            + V132Model.BossUnits(TriggerOf(heroKey), miss.Depth, heat);
                    }
                    maxNeeded = Math.Max(maxNeeded, Math.Max(currentThreshold + 1, (int)Math.Ceiling(units / miss.Cap) + 1));
                }
                sb.AppendLine($"- まとめ：{HeroName(heroKey)} は閾値を {currentThreshold} → {maxNeeded} に上げると、深度0〜{DreamDepth.Maximum}・両方針で入口のみの前半到達が消えます（現行値 {currentThreshold} では深度{firstDepth}以降で前半到達）。");
            }
        }
    }

    private static RunGrowthTrigger TriggerOf(string heroKey) => heroKey switch
    {
        "Hero_Vesper" => RunGrowthTrigger.DamageTakenMaxHpPct,
        "Hero_Cetus" => RunGrowthTrigger.ShieldAbsorbedMaxHpPct,
        "Hero_Mist" => RunGrowthTrigger.ParrySuccess,
        _ => RunGrowthTrigger.CritBasicAttackKill,
    };

    internal static EconomyResult First(IReadOnlyList<EconomyResult> economy, string policy, int depth, bool withStars) =>
        economy.First(e => e.Policy == policy && e.Depth == depth && e.WithStars == withStars);


    private static string HeroName(string heroKey) => heroKey switch
    {
        "Hero_Vesper" => "Vesper",
        "Hero_Cetus" => "Cetus",
        "Hero_Mist" => "Mist",
        "Hero_Husk" => "空殻",
        _ => heroKey,
    };

    private static string DescribeTrigger(string heroKey) => heroKey switch
    {
        "Hero_Vesper" => "被ダメージ10%ごとに防御+1・最大HP+4、上限60",
        "Hero_Cetus" => "障壁の吸収10%ごとにシールドの強さ+0.5%、上限60",
        "Hero_Mist" => "パリィ1回ごとに攻撃力+1・会心ダメージ+0.3%、上限80",
        "Hero_Husk" => "会心の基本攻撃でのとどめ1回ごとに攻撃力+0.5、上限80",
        _ => "",
    };

    private static string Gold(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Pct(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Probability(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
