using SodRpg.Core.Game;

namespace BalanceSim;

internal enum Metric
{
    Common, Uncommon, Rare, Epic, Legendary, Secured, Shards, Tuning,
    Level, EquippedRarity, Enhance, EquippedSlots, Bounties, Wiped,
    Count
}

internal sealed class Simulation
{
    internal static readonly string[] MilestoneNames =
    ["初めてのレア以上（発見）", "初めてのエピック以上（発見）", "初めての伝説・固有品（発見）",
     "初めてのセット3部位所持", "夢のレベル5", "夢のレベル10", "夢のレベル20", "装着6枠がすべて+3以上"];
    private const string Hero = "default";
    private readonly Options options;
    public double[,,] Samples { get; }
    public int[,] Milestones { get; }
    // Full transitions and automatically salvaged relics are separate quantities.
    public long[,] Capacity { get; }

    public Simulation(Options options)
    {
        this.options = options;
        Samples = new double[options.Runs, (int)Metric.Count, options.Players];
        Milestones = new int[MilestoneNames.Length, options.Players];
        Capacity = new long[4, options.Players];
    }

    public void Run()
    {
        var seeds = new Rng(options.Seed);
        for (int player = 0; player < options.Players; player++)
        {
            var profile = Profile.CreateNew(seeds.NextULong());
            var scenario = new Rng(seeds.NextULong());
            var row = new double[(int)Metric.Count];
            for (int run = 1; run <= options.Runs; run++)
            {
                Array.Clear(row);
                int shardsBefore = profile.Material(Materials.Shard);
                int tuningBefore = profile.Material(Materials.Tuning);
                Rules.BeginRun(profile, run.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var state = profile.Run;
                int wipeZone = scenario.Chance(options.Wipe) ? scenario.Range(1, options.Zones) : 0;
                int wipeRoom = wipeZone == 0 ? 0 : scenario.Range(1, options.Rooms);
                var actions = PlanExternalActions(state, scenario);
                bool wiped = false;
                int cleared = 0;
                for (int zone = 1; zone <= options.Zones; zone++)
                {
                    if (zone > 1 && Rules.ShouldOfferSecurePoint(profile))
                    {
                        Rules.ReachSecurePoint(profile);
                        if (state.Heat >= options.SecureHeat)
                            Secure(profile, player, run);
                        else
                            Rules.Delve(profile);
                    }
                    int itemLevel = (int)Math.Min(Content.MaxItemLevel,
                        (long)options.ItemLevel + (long)(zone - 1) * options.ItemLevelPerZone);
                    for (int room = 1; room <= options.Rooms; room++)
                    {
                        KillMany(profile, MonsterTier.Lesser, options.Lesser, itemLevel, row, player, run);
                        KillMany(profile, MonsterTier.Normal, options.Normal, itemLevel, row, player, run);
                        if (scenario.Chance(options.MiniBoss))
                            KillMany(profile, MonsterTier.MiniBoss, 1, itemLevel, row, player, run);
                        int before = state.Satchel.Count;
                        Observe(Rules.OnRoomsCleared(profile, ++cleared), before, profile, row, player, run);
                        foreach (var action in actions)
                        {
                            if (action.Room != cleared) continue;
                            for (int i = 0; i < action.Target; i++) Rules.OnGameAction(profile, action.Kind);
                        }
                        CheckLevels(profile, player, run);
                        if (zone == wipeZone && room == wipeRoom) { wiped = true; break; }
                    }
                    if (wiped) break;
                    KillMany(profile, MonsterTier.Boss, options.Bosses, itemLevel, row, player, run);
                }
                if (!wiped) CountStashTransfer(profile, player);
                Rules.EndRun(profile, victory: !wiped);
                CheckPossession(profile, player, run);
                ManageEquipment(profile);
                CheckLevels(profile, player, run);
                CheckEquipment(profile, row, player, run);
                row[(int)Metric.Secured] = profile.LastReport.RelicsSecured;
                row[(int)Metric.Shards] = profile.Material(Materials.Shard) - shardsBefore;
                row[(int)Metric.Tuning] = profile.Material(Materials.Tuning) - tuningBefore;
                row[(int)Metric.Level] = profile.DreamLevel;
                row[(int)Metric.Bounties] = profile.LastReport.BountiesDone;
                row[(int)Metric.Wiped] = wiped ? 1 : 0;
                for (int metric = 0; metric < (int)Metric.Count; metric++)
                    Samples[run - 1, metric, player] = row[metric];
            }
        }
    }

    private List<(BountyKind Kind, int Target, int Room)> PlanExternalActions(RunState state, Rng rng)
    {
        var actions = new List<(BountyKind, int, int)>();
        foreach (var b in state.Bounties)
        {
            // These six kinds concern the base game, not relic forging.
            if (b.Kind is BountyKind.ChaosSeeker or BountyKind.Patron or BountyKind.Refiner
                or BountyKind.Alchemist or BountyKind.Recycler or BountyKind.HunterBait)
            {
                if (rng.Chance(options.Bounty))
                    actions.Add((b.Kind, b.Target, rng.Range(1, options.Zones * options.Rooms)));
            }
        }
        return actions;
    }

    private void KillMany(Profile p, MonsterTier tier, int count, int level, double[] row, int player, int run)
    {
        for (int i = 0; i < count; i++)
        {
            int before = p.Run.Satchel.Count;
            Observe(Rules.OnKill(p, tier, level), before, p, row, player, run);
        }
        CheckLevels(p, player, run);
    }

    private void Observe(List<GameEvent> events, int before, Profile p, double[] row, int player, int run)
    {
        int added = 0;
        foreach (var e in events)
        {
            if (e.Kind == EventKind.Recovered) added++;
            if (e.Kind != EventKind.Drop || !e.Rarity.HasValue) continue;
            var rarity = e.Rarity.Value;
            added++;
            row[(int)rarity]++;
            if (rarity >= Rarity.Rare) Reach(0, player, run);
            if (rarity >= Rarity.Epic) Reach(1, player, run);
            if (rarity == Rarity.Legendary) Reach(2, player, run);
        }
        int capacity = Workshop.SatchelCapacity(p);
        if (before < capacity && before + added >= capacity) Capacity[0, player]++;
        Capacity[1, player] += Math.Max(0, before + added - capacity);
    }

    private void CountStashTransfer(Profile p, int player)
    {
        int capacity = Workshop.StashCapacity(p);
        int before = p.Stash.Count;
        int incoming = p.Run.Satchel.Count;
        if (before < capacity && before + incoming >= capacity) Capacity[2, player]++;
        Capacity[3, player] += Math.Max(0, before + incoming - capacity);
    }

    private void Secure(Profile p, int player, int run)
    {
        CountStashTransfer(p, player);
        Rules.Secure(p);
        CheckPossession(p, player, run);
        CheckLevels(p, player, run);
    }

    private void Reach(int milestone, int player, int run)
    {
        if (Milestones[milestone, player] == 0) Milestones[milestone, player] = run;
    }

    private void CheckLevels(Profile p, int player, int run)
    {
        if (p.DreamLevel >= 5) Reach(4, player, run);
        if (p.DreamLevel >= 10) Reach(5, player, run);
        if (p.DreamLevel >= 20) Reach(6, player, run);
    }

    private void CheckPossession(Profile p, int player, int run)
    {
        if (Milestones[3, player] != 0) return;
        foreach (var set in Content.Sets)
        {
            bool hasPieces = false;
            bool complete = true;
            foreach (var piece in Content.Uniques)
            {
                if (piece.SetId != set.Id) continue;
                hasPieces = true;
                bool owned = false;
                foreach (var relic in p.Stash)
                {
                    if (relic.UniqueId != piece.Id || relic.BaseId != piece.BaseId) continue;
                    owned = true;
                    break;
                }
                if (owned) continue;
                complete = false;
                break;
            }
            // Equipped relics are references into Stash; never count them twice.
            if (hasPieces && complete) { Reach(3, player, run); return; }
        }
    }

    private static int Compare(Relic a, Relic b)
    {
        int rarity = a.Rarity.CompareTo(b.Rarity);
        return rarity != 0 ? rarity : a.ItemLevel.CompareTo(b.ItemLevel);
    }

    private static void ManageEquipment(Profile p)
    {
        for (int slot = 0; slot < Content.SlotCount; slot++)
        {
            Relic? best = Rules.EquippedRelic(p, Hero, (Slot)slot);
            foreach (var relic in p.Stash)
                if ((int)relic.Slot == slot && (best == null || Compare(relic, best) > 0)) best = relic;
            if (best != null) Rules.Equip(p, Hero, best.Uid);
        }
        // Leave room for a full satchel at the next return. Mid-run overflow
        // remains the core's decision; do not manipulate Stash/Satchel directly.
        int target = Math.Max(Content.SlotCount, Workshop.StashCapacity(p) - Workshop.SatchelCapacity(p));
        while (p.Stash.Count > target)
        {
            Relic? worst = null;
            foreach (var relic in p.Stash)
            {
                if (relic.Locked || p.IsEquippedAnywhere(relic.Uid)) continue;
                if (worst == null || Compare(relic, worst) < 0
                    || (Compare(relic, worst) == 0 && relic.Enhance < worst.Enhance)) worst = relic;
            }
            if (worst == null) break;
            Rules.Salvage(p, worst.Uid);
        }
        // Raise the lowest enhancement first; slot order breaks ties. Replacing
        // a relic can lower enhancement, and the old investment is not copied.
        while (true)
        {
            Relic? next = null;
            for (int slot = 0; slot < Content.SlotCount; slot++)
            {
                var relic = Rules.EquippedRelic(p, Hero, (Slot)slot);
                if (relic == null || relic.Enhance >= Content.MaxEnhanceFor(relic)
                    || Content.EnhanceCost(relic.Enhance) > p.Material(Materials.Shard)) continue;
                if (next == null || relic.Enhance < next.Enhance) next = relic;
            }
            if (next == null) break;
            Rules.Enhance(p, next.Uid);
        }
    }

    private void CheckEquipment(Profile p, double[] row, int player, int run)
    {
        int slots = 0, rarity = 0, enhance = 0;
        bool allPlusThree = true;
        for (int slot = 0; slot < Content.SlotCount; slot++)
        {
            var relic = Rules.EquippedRelic(p, Hero, (Slot)slot);
            if (relic == null) { allPlusThree = false; continue; }
            slots++;
            rarity += (int)relic.Rarity;
            enhance += relic.Enhance;
            if (relic.Enhance < 3) allPlusThree = false;
        }
        row[(int)Metric.EquippedSlots] = slots;
        row[(int)Metric.EquippedRarity] = slots == 0 ? 0 : (double)rarity / slots;
        row[(int)Metric.Enhance] = slots == 0 ? 0 : (double)enhance / slots;
        if (allPlusThree) Reach(7, player, run);
    }
}
