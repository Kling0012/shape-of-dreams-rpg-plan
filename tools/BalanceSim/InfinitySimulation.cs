using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record InfinityScenario(string Name, int Speed, int Depth, double NightmareMultiplier, Waypoint Waypoint);

internal sealed class InfinityRow
{
    public required string Scenario;
    public int Minutes, Interval, Players;
    public long Rooms, Bosses, Nightmares, Attempts, Epic, Legendary, BossSet, Relics;
    public long Shards, Tuning, DreamXp, StarXp, Awakening;
    public long Accepted, Rejected;
    public double OutputReserved, Guaranteed, GuaranteeOpportunities, HighRareSpent, LegendarySpent, CombatSeconds;
    public int PeakHeat, Pressure;
    public double Hours => Players * Minutes / 60.0;
}

internal sealed class InfinitySimulation
{
    internal static readonly InfinityScenario[] Scenarios =
    [
        new("baseline 1x", 1, 0, 1, Waypoint.None),
        new("fast 4x", 4, 0, 1, Waypoint.None),
        new("promotion 4x / depth5 / gear1.5", 4, 5, 1.5, Waypoint.None),
        new("Hoard 4x / depth5 / gear1.5", 4, 5, 1.5, Waypoint.BossHoard),
    ];
    internal readonly List<InfinityRow> Rows = new();
    internal readonly List<string> PersistenceNotes = new();
    internal string PersistenceObservation = "";
    internal string BossTypeName = "";
    private readonly Options options;
    // Twenty combat rooms plus four boss encounters take 35 minutes in the reference model.
    private const double NodeSeconds = 35 * 60.0 / 24;
    private const string Hero = "Hero_Vesper";

    internal InfinitySimulation(Options options) => this.options = options;

    internal void Run()
    {
        StarClusters.RegisterAllGenerated();
        BossTypeName = Content.Sets.First(s => s.BossTypeName != null).BossTypeName;
        foreach (int depth in new[] { 0, 5 })
            foreach (int minutes in new[] { 30, 60, 120 })
                Rows.Add(Simulate(new InfinityScenario($"normal depth{depth}", 1, depth, 1, Waypoint.None), minutes, 0));
        foreach (var scenario in Scenarios)
            foreach (int minutes in new[] { 30, 60, 120 })
                foreach (int interval in new[] { 10, 15, 20 })
                    Rows.Add(Simulate(scenario, minutes, interval));
        Rows.Add(Simulate(new InfinityScenario("EpicMirage authorization / 4x", 4, 0, 1, Waypoint.EpicMirage), 300, 10));
        Rows.Add(ObserveFundedBoss());
        ObservePersistence();
    }

    private InfinityRow Simulate(InfinityScenario scenario, int minutes, int interval)
    {
        var row = new InfinityRow { Scenario = scenario.Name, Minutes = minutes, Interval = interval, Players = options.Players };
        var seeds = new Rng(options.Seed);
        for (int player = 0; player < options.Players; player++)
        {
            var profile = Profile.CreateNew(seeds.NextULong());
            var encounterRng = new Rng(seeds.NextULong());
            var equipmentRng = new Rng(seeds.NextULong());
            // An existing equipped relic lets the real awakening route run; it is not counted as new supply.
            var equipped = Loot.RollUnique(equipmentRng, Content.Uniques[0], options.ItemLevel);
            profile.Stash.Add(equipped);
            profile.Hero(Hero).Equipped[(int)equipped.Slot] = equipped.Uid;
            Begin(profile, scenario, interval, 1);
            double elapsed = 0;
            int combatSinceBoss = 0, normalZone = 0, runNumber = 1, normalNode = 0;
            double nodeSeconds = NodeSeconds / scenario.Speed;
            while (elapsed < minutes * 60)
            {
                var run = profile.Run;
                bool boss = interval == 0 ? combatSinceBoss == 5 : run.Infinity.BossDue;
                if (boss)
                {
                    run.Infinity?.TryEnterBoss();
                    AdvanceAndKill(profile, MonsterTier.Boss, NightmareAffix.None, nodeSeconds, ref elapsed, minutes, row,
                        bossSetEligible: interval != 0 || normalZone == 0);
                    if (elapsed > minutes * 60) break;
                    row.Bosses++;
                    combatSinceBoss = 0;
                    if (interval != 0)
                    {
                        FinishBoss(profile);
                        Observe(Rules.Delve(profile), row, profile);
                        var infinity = run.Infinity!;
                        infinity.CompleteGraphTransition(infinity.GraphEpoch + 1);
                        run.ActiveWaypoint = scenario.Waypoint;
                    }
                    else
                    {
                        normalZone++;
                        if (normalZone == 4)
                        {
                            Observe(Rules.EndRun(profile, victory: true), row, profile);
                            Begin(profile, scenario, interval, ++runNumber);
                            normalZone = 0;
                            normalNode = 0;
                        }
                        else
                        {
                            Observe(Rules.ReachSecurePoint(profile), row, profile);
                            Observe(Rules.Secure(profile), row, profile);
                        }
                    }
                    continue;
                }
                if (run.Infinity != null)
                {
                    run.Infinity.RoomEpoch++;
                    InfinityRewards.EnterRoom(profile, run.Infinity.GraphEpoch, run.Infinity.RoomEpoch);
                }
                bool mini = encounterRng.Chance(.25);
                int kills = 18 + (mini ? 1 : 0);
                for (int kill = 0; kill < kills; kill++)
                {
                    var tier = kill < 10 ? MonsterTier.Lesser : kill < 18 ? MonsterTier.Normal : MonsterTier.MiniBoss;
                    var effects = Waypoints.Sum(run.ActiveWaypoint);
                    var nightmare = effects.AllNightmares ? NightmareAffix.Ironclad
                        : Nightmares.Roll(encounterRng, tier, run.Heat, scenario.NightmareMultiplier * effects.NightmareChanceMultiplier);
                    AdvanceAndKill(profile, tier, nightmare, nodeSeconds / kills, ref elapsed, minutes, row);
                    if (elapsed > minutes * 60) break;
                }
                if (elapsed > minutes * 60) break;
                combatSinceBoss++;
                row.Rooms++;
                if (run.Infinity != null)
                    run.Infinity.TryCountCombatClear(run.Infinity.GraphEpoch, combatSinceBoss - 1, true, false, false);
                Observe(Rules.OnRoomsCleared(profile, ++normalNode), row, profile);
                row.PeakHeat = Math.Max(row.PeakHeat, run.PeakHeat);
                row.Pressure = Math.Max(row.Pressure, run.Infinity?.PressureStage ?? 0);
            }
            row.PeakHeat = Math.Max(row.PeakHeat, profile.Run?.PeakHeat ?? 0);
            row.Shards += profile.Material(Materials.Shard) + (profile.Run?.SatchelShards ?? 0) + (profile.Run?.DeferredWaypointShards ?? 0);
            row.Tuning += profile.Material(Materials.Tuning) + (profile.Run?.SatchelTuning ?? 0) + (profile.Run?.DeferredWaypointTuning ?? 0);
            long dreamXp = profile.DreamXp;
            for (int level = 1; level < profile.DreamLevel; level++) dreamXp += Content.XpToNext(level);
            row.DreamXp += dreamXp;
            row.StarXp += profile.Hero(Hero).StarXp;
            row.Awakening += equipped.AwakenPoints;
            CaptureBudget(profile, row);
        }
        return row;
    }

    private void Begin(Profile profile, InfinityScenario scenario, int interval, int runNumber)
    {
        Rules.BeginRun(profile, $"balance-infinity-{runNumber}", heroKey: Hero, dreamDepth: scenario.Depth);
        profile.Run.ActiveWaypoint = scenario.Waypoint;
        if (interval != 0)
            profile.Run.Infinity = new InfinityRunState
            {
                FixedZoneId = "Zone_Forest", DifficultyId = "diffNormal", Interval = interval,
                GraphEpoch = 1, SegmentEpoch = 1, Phase = InfinityPhase.Exploring,
            };
    }

    private void AdvanceAndKill(Profile profile, MonsterTier tier, NightmareAffix nightmare, double seconds,
        ref double elapsed, int minutes, InfinityRow row, bool bossSetEligible = false)
    {
        double remaining = minutes * 60 - elapsed;
        double actual = Math.Min(seconds, remaining);
        // No rewards occur between these active combat ticks. Refill+clamp is additive,
        // so aggregating the interval gives the same credits as native .25-second ticks.
        InfinityRewards.AdvanceCombat(profile, actual);
        row.CombatSeconds += actual;
        elapsed += seconds;
        if (seconds > remaining + 1e-8) return;
        row.Attempts++;
        if (nightmare != NightmareAffix.None) row.Nightmares++;
        var budget = profile.InfinityRewardBudget;
        double rareBefore = budget.HighRare, relicsBefore = budget.Relics;
        double legendaryBefore = budget.Legendary;
        double guaranteeBefore = budget.GuaranteedRelics, opportunityBefore = budget.GuaranteeOpportunities;
        Observe(Rules.OnKill(profile, tier, options.ItemLevel, nightmare, Hero,
            bossTypeName: tier == MonsterTier.Boss && bossSetEligible ? BossTypeName : null, bossDropDepth: profile.Run.DreamDepth), row, profile);
        row.HighRareSpent += Math.Max(0, rareBefore - budget.HighRare);
        row.LegendarySpent += Math.Max(0, legendaryBefore - budget.Legendary);
        row.OutputReserved += Math.Max(0, relicsBefore - budget.Relics);
        row.Guaranteed += Math.Max(0, guaranteeBefore - budget.GuaranteedRelics);
        row.GuaranteeOpportunities += Math.Max(0, opportunityBefore - budget.GuaranteeOpportunities);
    }

    private static void FinishBoss(Profile profile)
    {
        var infinity = profile.Run.Infinity;
        infinity.ObserveBossClear();
        infinity.ObserveSoul(true, false, false);
        infinity.ObserveSoul(false, true, true);
        Rules.ReachInfinityChoice(profile);
        Rules.PickWaypoint(profile, Waypoint.None);
    }

    private static void Observe(List<GameEvent> events, InfinityRow? row, Profile profile)
    {
        Rules.SettleSatchelOverflow(profile);
        foreach (var e in events)
        {
            if (row == null || e.Kind != EventKind.Drop || !e.Rarity.HasValue) continue;
            row.Relics++;
            if (e.Rarity == Rarity.Epic) row.Epic++;
            if (e.Rarity == Rarity.Legendary) row.Legendary++;
            if (e.Relic != null && Content.TryGetUnique(e.Relic.UniqueId, out var unique)
                && unique.SetId != null && Content.GetSet(unique.SetId)?.BossTypeName != null) row.BossSet++;
        }
    }

    private static void CaptureBudget(Profile profile, InfinityRow row)
    {
        // Filled with the Core budget's persisted authorization ledger, not inferred from empty loot rolls.
        var budget = profile.InfinityRewardBudget;
        if (budget == null) return;
        row.Accepted += budget.AcceptedKills;
        row.Rejected += budget.RejectedKills;
    }

    private InfinityRow ObserveFundedBoss()
    {
        // Isolate the existing roll with genuinely accrued credit, not assigned balances.
        // This is a stored-credit sensitivity case, not the ordinary throughput fixture.
        var row = new InfinityRow
        {
            Scenario = "funded native boss / stored combat credit", Minutes = 135, Interval = 10, Players = options.Players
        };
        var seeds = new Rng(options.Seed);
        for (int player = 0; player < options.Players; player++)
        {
            var profile = Profile.CreateNew(seeds.NextULong());
            Begin(profile, Scenarios[0], 10, 1);
            var infinity = profile.Run.Infinity;
            double elapsed = 0;
            for (int room = 0; room < 10; room++)
            {
                infinity.RoomEpoch++;
                InfinityRewards.EnterRoom(profile, infinity.GraphEpoch, infinity.RoomEpoch);
                InfinityRewards.AdvanceCombat(profile, 270);
                row.CombatSeconds += 270;
                elapsed += 270;
                infinity.TryCountCombatClear(infinity.GraphEpoch, room, true, false, false);
                row.Rooms++;
            }
            infinity.TryEnterBoss();
            AdvanceAndKill(profile, MonsterTier.Boss, NightmareAffix.None, 5400, ref elapsed, row.Minutes, row, bossSetEligible: true);
            row.Bosses++;
            row.Pressure = infinity.PressureStage;
            row.Shards += profile.Material(Materials.Shard) + profile.Run.SatchelShards;
            row.Tuning += profile.Material(Materials.Tuning) + profile.Run.SatchelTuning;
            long dreamXp = profile.DreamXp;
            for (int level = 1; level < profile.DreamLevel; level++) dreamXp += Content.XpToNext(level);
            row.DreamXp += dreamXp;
            row.StarXp += profile.Hero(Hero).StarXp;
            CaptureBudget(profile, row);
        }
        return row;
    }

    private void ObservePersistence()
    {
        var profile = Profile.CreateNew(options.Seed);
        var scenario = Scenarios[1];
        Begin(profile, scenario, 10, 1);
        for (int room = 0; room < 10; room++)
        {
            var infinity = profile.Run.Infinity;
            infinity.RoomEpoch++;
            InfinityRewards.EnterRoom(profile, infinity.GraphEpoch, infinity.RoomEpoch);
            InfinityRewards.AdvanceCombat(profile, 20);
            Observe(Rules.OnKill(profile, MonsterTier.Normal, options.ItemLevel, heroKey: Hero), null, profile);
            infinity.TryCountCombatClear(infinity.GraphEpoch, room, true, false, false);
            Observe(Rules.OnRoomsCleared(profile, room + 1), null, profile);
        }
        profile.Run.Infinity.TryEnterBoss();
        InfinityRewards.AdvanceCombat(profile, 20);
        Observe(Rules.OnKill(profile, MonsterTier.Boss, options.ItemLevel, heroKey: Hero, bossTypeName: BossTypeName), null, profile);
        FinishBoss(profile);
        int pressure = profile.Run.Infinity.PressureStage;
        Rules.SecuredReturn(profile);
        var clone = profile.Clone();
        var restored = ProfileCodec.Read(ProfileCodec.Write(clone), PersistenceNotes);
        string records = string.Join("; ", restored.InfinityRecords.Select(kv =>
            $"{kv.Key}: returns={kv.Value.ReturnCount}, best rooms={kv.Value.BestReturnedRooms}, pressure={kv.Value.PressureAtBestReturn}"));
        PersistenceObservation = $"Actual SecuredReturn → Profile.Clone → ProfileCodec.Write/Read: completed={restored.CompletedRunSecuredReturn}, active run={restored.Run != null}; pressure before secure={pressure}; {records}; persisted high-rare credit={restored.InfinityRewardBudget.HighRare:0.######}; accepted/rejected={restored.InfinityRewardBudget.AcceptedKills}/{restored.InfinityRewardBudget.RejectedKills}.";
    }
}
