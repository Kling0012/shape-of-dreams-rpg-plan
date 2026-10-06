using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal static class InfinityReport
{
    private static string F(double value, string format = "0.000") => value.ToString(format, CultureInfo.InvariantCulture);
    private static string Rate(long count, double hours)
        => $"{F(count / hours)} ± {F(1.96 * Math.Sqrt(count) / hours)}";

    internal static string Render(Options options, InfinitySimulation simulation, TimeSpan elapsed)
    {
        var b = new StringBuilder();
        b.AppendLine("# Infinity stage 2: actual Core economics / 実コード経済計測");
        b.AppendLine();
        b.AppendLine("## Reproduction / 再現");
        b.AppendLine("```sh");
        b.AppendLine($"DOTNET_ROLL_FORWARD=Major dotnet run --project tools/BalanceSim -c Release -- --mode infinity --infinity-scope {options.InfinityScope} --players {options.Players} --seed {options.Seed} --item-level {options.ItemLevel} --out tools/BalanceSim/result-infinity.md");
        b.AppendLine("```");
        b.AppendLine($"Independent profiles per row: **{options.Players}**; deterministic Core SplitMix64 seed: **{options.Seed}**; item level: **{options.ItemLevel}**. Runtime: {F(elapsed.TotalSeconds, "0.0")} s. Each row uses a fresh zero-credit profile; the same player seed stream is reused across scenarios. Generated star content is registered with `StarClusters.RegisterAllGenerated()`. Native difficulty fixture: **diffNormal**, boss nightmare flag **false**. Boss source: `{simulation.BossTypeName}` (the registered native Forest boss-set source). Ordinary uses one eligible source plus three unregistered bosses per expedition; Infinity fixes that source for every cycle.");
        b.AppendLine($"Observed .NET runtime: **{Environment.Version}**. Major roll-forward lets the net8.0 simulator run on the installed newer runtime without changing its target framework.");
        b.AppendLine();
        b.AppendLine("The simulator executes `Rules.BeginRun`, `Rules.OnKill` → real `Loot`/`BossSets`/`Waypoints`, `Rules.OnRoomsCleared`, `Rules.ReachInfinityChoice`, `Rules.Delve` and actual Infinity graph/phase transitions. It calls **the same `InfinityRewards.AdvanceCombat` and `EnterRoom` APIs as the runtime**. There is no second limiter, rarity reroll, or rewritten loot distribution here. The clock interval between two kills is aggregated: with no intervening reward mutations, additive refill followed by burst clamp is equivalent to native 0.25-second ticks.");
        b.AppendLine();
        b.AppendLine("## Method and limits / 方法・限界");
        b.AppendLine("- Ordinary reference: 20 Combat rooms + 4 actual bosses per 35 active combat minutes, 10 Lesser + 8 Normal per Combat room, independent 25% MiniBoss chance. Every encounter node takes 35/24 minutes. Ordinary profiles secure at each zone boundary (Heat 0); victory at four zones starts the next ordinary expedition through real Rules. Only the first of its four bosses uses the registered Forest source; the remaining three have no registered boss set. This is an explicit encounter fixture, not a measurement of Forest boss incidence. Boss-set drop depth equals chosen DreamDepth; native bosses are not nightmare-promoted.");
        b.AppendLine(options.InfinityScope == "comparison"
            ? $"- Lightweight comparison scope: six fixed 30-minute rows (normal depth 0/5, and the four Infinity scenarios at Core default period {InfinityRunState.DefaultInterval}). Long EpicMirage and funded-boss sensitivities are not measured; legal return/codec observation remains included."
            : $"- Full scope: Infinity compares current Core short/middle/long periods ({string.Join("/", InfinitySimulation.Intervals.Select(i => i.Value))}) × 30/60/120 minute combinations at 1x and 4x encounter throughput, plus EpicMirage and funded-boss sensitivities.");
        b.AppendLine("- Boss encounters consume time too. Each cycle uses actual boss/soul completion and Delve, so Heat grows to its game cap and pressure grows from cumulative cleared rooms. Graph identity advances at Delve; native zone and item level stay fixed. Partial final rooms earn time credit and completed kills but do not count as cleared rooms.");
        b.AppendLine("- Promotion sensitivity fixes DreamDepth 5 and a model nightmare chance multiplier of 1.5, calling Nightmares.Roll on the current Heat rather than assigning a promoted reward tier directly. Hoard sensitivity uses the real holding/release path. Fixed waypoint fixtures isolate each modifier instead of modeling offer-selection probability; the real before-generation guarantee gate still applies. No paid merchant/events, old-asset crafting, old-item recovery, or discretionary bounty actions are modeled; naturally completed kill/room/secure bounties and feats remain enabled.");
        b.AppendLine("- All modeled session time is active combat: no travel, pause, loading, choice or idle refill. This is an intentionally generous active-time supply exposure, **not measured native clear speed**. Runtime uses synchronized native elapsed game time only in eligible active Combat/ExitBoss while the local hero is in combat and not KO; inactive/rejoin baselines are discarded. Native base-game Gold/Dust and paid goods funded from old assets are outside a global numeric cap; MOD-added native kill currency is disabled (cap 0). Infinity MOD dust-conversion/merchant opportunities are separately pre-authorized before payment.");
        if (options.InfinityScope == "full")
            b.AppendLine($"- The separate funded native boss observation isolates #48 after 135 minutes of combat credit accrued through the real refill API, with {InfinityRunState.DefaultInterval} room-budget entries sharing 45 active combat minutes and no intervening reward admissions, followed by one 90-minute boss encounter. It is a stored-credit sensitivity case, not typical 35-minute encounter throughput. The boss is an actual Boss-tier reward source at Heat 0 with the registered native ID. Zero observed sets in shorter Infinity rows do not mean the path is disabled.");
        b.AppendLine("- Found outputs are counted from actual Drop events, even if capacity overflow converts them into shards. Boss sets are a separated subset of Legendary, not an extra additive count. Hoard final-output reservations include x3 at generation; released outputs are not charged again. Resource totals below include currently held supplies and secured supplies, not merely what fits in the stash; an unfinished session is not forced into an illegal non-boss return. The separate return/codec observation below exercises legal return.");
        b.AppendLine("- Memory is bounded by one live profile/encounter, existing satchel/stash/Hoard capacity, and scalar sums per row. The simulator stores no per-kill history and no all-player sample array.");
        b.AppendLine();
        b.AppendLine("## Analytic random high-rare budget / 抽選高レアの解析予算");
        b.AppendLine("These numbers come from **`InfinityRewards.NormalHighRarePerHour`** and **`NormalLegendaryPerHour`**, not a simulator copy of their formulas. The lower ordinary reference has Heat 0, no nightmares, no waypoint amplification or guarantees, and conservatively **excludes all boss-set output** because native eligible-boss incidence is not measured. Infinity refill always uses the DreamDepth 0 lower reference, even at higher depth: persisted high-depth credit cannot inflate later low-depth runs. Admission reserves the full current-modifier conservative Epic+ AND Legendary expectations **before rolling**, including promotion, luck, duplication/Hoard and every actual registered boss-set chance. The Legendary budget prevents trading Epic expectation into higher Legendary production.");
        b.AppendLine();
        b.AppendLine("| DreamDepth | Ordinary non-set Epic+ EV / hour | Infinity Epic+ authorization / hour | Ordinary non-set Legendary EV / hour | Infinity Legendary authorization / hour | Guaranteed authorization / hour |");
        b.AppendLine("|---:|---:|---:|---:|---:|---:|");
        foreach (int depth in new[] { 0, 5 })
            b.AppendLine($"| {depth} | {F(InfinityRewards.NormalHighRarePerHour(depth), "0.######")} | {F(InfinityRewards.NormalHighRarePerHour(0), "0.######")} | {F(InfinityRewards.NormalLegendaryPerHour(depth), "0.######")} | {F(InfinityRewards.NormalLegendaryPerHour(0), "0.######")} | {F(InfinityRewards.GuaranteesPerHour)} |");
        b.AppendLine();
        b.AppendLine($"Zero initial time credit plus pre-roll EV debit bounds cumulative authorized random high-rare EV by accumulated active combat hours × the depth-0 reference rate. Stored earned credit can burst later; this is not a strict rolling-hour or arbitrary fresh-session window limit. This is an expectation authorization bound, not a promise that each random sample stays below ordinary observed output. Guarantees use separate opportunity/final-output budgets at {F(InfinityRewards.GuaranteesPerHour, "0.######")}/hour; whole opportunities reserve their potential output before rolling.");
        b.AppendLine();
        foreach (string scenario in simulation.Rows.Select(r => r.Scenario).Distinct())
        {
            b.AppendLine($"## Observed outputs: {scenario}");
            b.AppendLine();
            b.AppendLine("| Period | Minutes | Cleared / profile | Bosses / profile | Peak Heat / pressure | Relics total | Epic total | Legendary total | Boss-set subset | Epic/hour ± MC error | Legendary/hour ± MC error | Boss-set/hour |");
            b.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in simulation.Rows.Where(r => r.Scenario == scenario))
                b.AppendLine($"| {(r.Interval == 0 ? "normal" : r.Interval.ToString(CultureInfo.InvariantCulture))} | {r.Minutes} | {F((double)r.Rooms / r.Players, "0.0")} | {F((double)r.Bosses / r.Players, "0.0")} | {r.PeakHeat} / {r.Pressure} | {r.Relics} | {r.Epic} | {r.Legendary} | {r.BossSet} | {Rate(r.Epic, r.Hours)} | {Rate(r.Legendary, r.Hours)} | {F(r.BossSet / r.Hours)} |");
            b.AppendLine();
        }
        b.AppendLine("The ± columns are the approximate 95% Poisson Monte Carlo sampling error `1.96*sqrt(count)/exposure-hours`, not gameplay prediction intervals and not valid rare-event confidence limits near zero. Zero observed events do **not** establish zero probability. Outcomes share profile state (bounties/overflow/waypoints), so this approximation is descriptive, not a proof or an independent identically distributed event assumption. Each interval's real exposure is players × session minutes/60; integer total counts are included for auditability.");
        b.AppendLine();
        b.AppendLine("## Actual authorizations / 実際の認可・抑止");
        b.AppendLine();
        b.AppendLine("Accepted/rejected counts are the persisted Core `AcceptedKills` / `RejectedKills`, not inferred from empty rolls. EV and output reservation columns are actual scalar debit around OnKill after refill. Hoard final-output credits can be reserved before release. Guarantee output credits reserve the whole possible output before rolling; an empty roll or smaller output does not refund them. These credits are not actual guaranteed-drop counts; actual drops appear above.");
        b.AppendLine();
        b.AppendLine("| Scenario | Period | Minutes | Attempted kills | Accepted | Rejected | Nightmare kills | Random Epic+ EV reserved / hour | Epic+ reference / hour | Random Legendary EV reserved / hour | Legendary reference / hour | Final free outputs reserved / hour | Guarantee opportunities reserved | Guaranteed output credits reserved |");
        b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in simulation.Rows.Where(r => r.Interval != 0))
        {
            b.AppendLine($"| {r.Scenario} | {r.Interval} | {r.Minutes} | {r.Attempts} | {r.Accepted} | {r.Rejected} | {r.Nightmares} | {F(r.HighRareSpent / r.Hours, "0.######")} | {F(InfinityRewards.NormalHighRarePerHour(0), "0.######")} | {F(r.LegendarySpent / r.Hours, "0.######")} | {F(InfinityRewards.NormalLegendaryPerHour(0), "0.######")} | {F(r.OutputReserved / r.Hours)} | {F(r.GuaranteeOpportunities, "0")} | {F(r.Guaranteed, "0")} |");
        }
        b.AppendLine();
        b.AppendLine("## Resources and implemented caps / 資源・実装上限");
        b.AppendLine();
        b.AppendLine($"Rates from Core: free relics **{F(InfinityRewards.RelicsPerHour, "0")}/hour**; shards **{F(InfinityRewards.ShardsPerHour, "0")}/hour**; tuning **{F(InfinityRewards.TuningPerHour, "0")}/hour**; Dream XP **{F(InfinityRewards.XpPerHour, "0")}/hour**; Star XP and aggregate equipped-relic awakening **{F(InfinityRewards.StarXpPerHour, "0")}/hour**. Refill stays at the global lower baseline; current depth/waypoint multipliers increase the requested reward, not the budget. Merchant and dust-conversion authorization rates are **{F(InfinityRewards.MerchantsPerHour, "0")}** and **{F(InfinityRewards.DustConversionsPerHour, "0")}** per hour. Native MOD bonus Gold/Dust: **0** (base-game currency remains unchanged). Failed pre-payment attempts conservatively retain reservations; paid countervalue is never confiscated.");
        b.AppendLine();
        b.AppendLine("| Scenario | Period | Minutes | Shards/hour | Tuning/hour | Dream XP/hour | Star XP/hour | Equipped relic awakening/hour |");
        b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in simulation.Rows)
            b.AppendLine($"| {r.Scenario} | {(r.Interval == 0 ? "normal" : r.Interval.ToString(CultureInfo.InvariantCulture))} | {r.Minutes} | {F(r.Shards / r.Hours)} | {F(r.Tuning / r.Hours)} | {F(r.DreamXp / r.Hours)} | {F(r.StarXp / r.Hours)} | {F(r.Awakening / r.Hours)} |");
        b.AppendLine();
        b.AppendLine("Resource caps apply to free positive supply, including capacity overflow, heat secure bonus, naturally earned free rewards, and conversions. Paid-item recovery/crafting is excluded. The equipped existing Legendary fixture exposes actual awakening, but can reach its finite maximum; observed awakening can therefore be lower than available authorization. Dream XP reconstructs level thresholds plus residual XP; reaching maximum level similarly truncates credited XP. No resource-limit claim is inferred solely from these sample values.");
        b.AppendLine();
        b.AppendLine("## Legal secured return and persistence observation / 合法帰還・保存の観測");
        b.AppendLine();
        b.AppendLine(simulation.PersistenceObservation);
        b.AppendLine($"Codec notes: {(simulation.PersistenceNotes.Count == 0 ? "none" : string.Join("; ", simulation.PersistenceNotes))}.");
        b.AppendLine();
        b.AppendLine("This requested simulator is a real Core economics/serialization smoke surface, not a Unity scene, multiplayer, or wall-clock benchmark. The 35-minute ordinary reference is an explicit balancing assumption, not native measurement. If measured ordinary same-condition supply is lower, its conservative authorization reference must be lowered; this report does not replace that measurement.");
        return b.ToString();
    }
}
