using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal static class PressureReport
{
    internal static readonly int[] DreamLevels = [1, 5, 10, 20, 30];
    internal static readonly int[] SpentPoints = [0, 50, 250, 500];
    internal static readonly int[] Depths = [0, 1, 2, 3, 4, 5];
    internal static readonly int[] InfinityStages = [0, 1, 10, 100];
    internal static readonly Waypoint[] WaypointAxis = [Waypoint.None, Waypoint.FleetingMemories];
    internal const string Policy = "Single-player Core projections at fixed input axes; Core clamps remain active. Delve heat is independent of DreamDepth. No combat simulation, win rates or clear-speed estimates.";

    internal static List<QuantityMetricValue> Measure()
    {
        StarClusters.RegisterAllGenerated();
        var entries = new List<QuantityMetricValue>();
        void Add(string id, string label, double value, string unit) => entries.Add(new(id, label, value, unit));
        void Parameter(string id, double value, string unit) => Add("pressure/parameters/" + id, id, value, unit);
        void Stats(string id, string label, IEnumerable<StatLine> stats)
        {
            // Affixes can add the same stat twice; report the actual total, not duplicate IDs.
            foreach (var group in stats.GroupBy(s => s.Stat).OrderBy(g => g.Key))
                Add(id + "/stat/" + group.Key, label + " " + group.Key, group.Sum(s => s.Value), StatUnit(group.Key));
        }
        foreach (int level in DreamLevels)
            foreach (int spent in SpentPoints)
                foreach (int depth in Depths)
                    foreach (var waypoint in WaypointAxis)
                        foreach (int stage in InfinityStages)
                        {
                            var pressure = DreamPressure.ForPlayer(level, spent)
                                .WithRunModifiers(depth, Waypoints.Sum(waypoint).PressureMultiplier).WithInfinityPressure(stage);
                            string id = $"pressure/player/{level}/{spent}/{depth}/{waypoint}/{stage}";
                            string label = $"Lv{level} spent={spent} depth={depth} waypoint={waypoint} stage={stage}";
                            Add(id + "/health", label + " HP", pressure.HealthMultiplier, "multiplier");
                            Add(id + "/damage", label + " damage", pressure.DamageMultiplier, "multiplier");
                        }
        foreach (int depth in Depths)
        {
            Add($"pressure/nightmares/affixCount/{depth}", $"Affix count heat={depth}", Nightmares.AffixCount(depth), "affixes");
            string id = $"pressure/depth/{depth}";
            Add(id + "/health", id + " HP", DreamDepth.HealthMultiplier(depth), "multiplier");
            Add(id + "/damage", id + " damage", DreamDepth.DamageMultiplier(depth), "multiplier");
            Add(id + "/rarityLuck", id + " rarity luck", DreamDepth.RarityLuck(depth), "luck");
            Add(id + "/awakening", id + " awakening", DreamDepth.AwakeningMultiplier(depth), "multiplier");
            Add(id + "/starXp", id + " star XP", DreamDepth.StarXpMultiplier(depth), "multiplier");
            Add(id + "/extraNodes", id + " extra nodes", DreamDepth.ExtraZoneNodes(depth), "nodes/zone");
            Add($"pressure/variants/chance/{depth}", $"Variant chance heat={depth}", Variants.Chance(depth), "probability");
            foreach (var tier in Enum.GetValues<MonsterTier>())
            {
                string enemy = $"pressure/nightmares/{tier}/heat/{depth}";
                Add(enemy + "/chance", enemy + " chance", Nightmares.Chance(tier, depth), "probability");
                Stats(enemy + "/depthBonus", enemy, Nightmares.DepthBonus(tier, depth));
            }
        }
        foreach (var affix in Nightmares.AllAffixes.OrderBy(a => a))
        {
            string id = "pressure/nightmares/affix/" + affix;
            Stats(id, affix.ToString(), Nightmares.MonsterStats(affix, out float regen));
            Add(id + "/regen", affix + " regeneration", regen, "percent-max-hp/second");
        }
        foreach (var variant in Variants.All.OrderBy(v => v.Id, StringComparer.Ordinal))
        {
            string id = "pressure/variants/" + variant.Id;
            Stats(id, variant.Id, variant.Stats);
            Add(id + "/shardBonus", variant.Id + " shard reward", variant.ShardBonusPct, "percent");
        }
        Parameter("dream/freeLevels", DreamPressure.FreeDreamLevels, "levels");
        Parameter("dream/healthPerLevel", DreamPressure.HealthPerLevel, "multiplier/level");
        Parameter("dream/healthPerStarPoint", DreamPressure.HealthPerStarPoint, "multiplier/point");
        Parameter("dream/healthPerInfinityStage", DreamPressure.HealthPerInfinityStage, "multiplier/stage");
        Parameter("dream/damagePerLevel", DreamPressure.DamagePerLevel, "multiplier/level");
        Parameter("dream/damagePerStarPoint", DreamPressure.DamagePerStarPoint, "multiplier/point");
        Parameter("dream/damagePerInfinityStage", DreamPressure.DamagePerInfinityStage, "multiplier/stage");
        Parameter("depth/maximum", DreamDepth.Maximum, "depth");
        Parameter("nightmare/baseHealth", Nightmares.BaseHealthPct, "percent-max-hp");
        Parameter("nightmare/ward", Nightmares.WardShieldPct, "percent-max-hp");
        Parameter("nightmare/thorns", Nightmares.ThornsReflectPct, "percent-damage");
        Parameter("nightmare/thornsCap", Nightmares.ThornsReflectMaxHealthPct, "percent-max-hp");
        Parameter("nightmare/thornsInterval", Nightmares.ThornsReflectInterval, "seconds");
        Parameter("nightmare/leech", Nightmares.RavenousLeechPct, "percent-damage");
        Parameter("nightmare/sunderArmor", Nightmares.SunderArmor, "armor");
        Parameter("nightmare/sunderSeconds", Nightmares.SunderSeconds, "seconds");
        Parameter("variant/minDepth", Variants.MinDepth, "heat");
        Parameter("variant/hitCap", Variants.HitCapPct, "percent-max-hp");
        Parameter("variant/deathBurst", Variants.DeathBurstPct, "percent-max-hp");
        Parameter("variant/deathBurstRadius", Variants.DeathBurstRadius, "meters");
        Parameter("behavior/range", MonsterBehavior.Range, "meters");
        Parameter("depth/healthPerDepth", DreamDepth.HealthPerDepth, "multiplier/depth");
        Parameter("depth/damagePerDepth", DreamDepth.DamagePerDepth, "multiplier/depth");
        Parameter("depth/rarityLuckPerDepth", DreamDepth.RarityLuckPerDepth, "luck/depth");
        Parameter("depth/awakeningPerDepth", DreamDepth.AwakeningPerDepth, "multiplier/depth");
        Parameter("depth/starXpPerDepth", DreamDepth.StarXpPerDepth, "multiplier/depth");
        Parameter("depth/extraNodesPerDepth", DreamDepth.ExtraNodesPerDepth, "nodes/zone/depth");
        Parameter("nightmare/bossDepthMinimum", Nightmares.BossDepthMinimum, "heat");
        Parameter("nightmare/bossHealthPerDepth", Nightmares.BossHealthPctPerDepth, "percent-max-hp/heat");
        Parameter("nightmare/healthPerDepth", Nightmares.HealthPctPerDepth, "percent-max-hp/heat");
        Parameter("nightmare/attackPerDepth", Nightmares.AttackPctPerDepth, "percent-attack/heat");
        Parameter("nightmare/armorDepthMinimum", Nightmares.ArmorDepthMinimum, "heat");
        Parameter("nightmare/depthArmor", Nightmares.DepthArmor, "armor");
        Parameter("nightmare/gearHealthDivisor", Nightmares.GearHealthDivisor, "divisor");
        Parameter("nightmare/gearMaximumChanceBonus", Nightmares.GearMaximumChanceBonus, "fraction-chance");
        Parameter("nightmare/gearScoreDivisor", Nightmares.GearScoreDivisor, "score/fraction-chance");
        Parameter("nightmare/lesserChancePerDepth", Nightmares.LesserChancePerDepth, "probability/heat");
        Parameter("nightmare/normalChancePerDepth", Nightmares.NormalChancePerDepth, "probability/heat");
        Parameter("nightmare/miniBossBaseChance", Nightmares.MiniBossBaseChance, "probability");
        Parameter("nightmare/miniBossChancePerDepth", Nightmares.MiniBossChancePerDepth, "probability/heat");
        Parameter("nightmare/twoAffixDepth", Nightmares.TwoAffixDepth, "heat");
        Parameter("nightmare/threeAffixDepth", Nightmares.ThreeAffixDepth, "heat");
        Parameter("nightmare/baseAffixCount", Nightmares.BaseAffixCount, "affixes");
        Parameter("nightmare/twoAffixCount", Nightmares.TwoAffixCount, "affixes");
        Parameter("nightmare/threeAffixCount", Nightmares.ThreeAffixCount, "affixes");
        Parameter("nightmare/ironcladArmor", Nightmares.IroncladArmor, "armor");
        Parameter("nightmare/berserkAttack", Nightmares.BerserkAttackPct, "percent");
        Parameter("nightmare/berserkAttackSpeed", Nightmares.BerserkAttackSpeedPct, "percent");
        Parameter("nightmare/colossalHealth", Nightmares.ColossalHealthPct, "percent");
        Parameter("nightmare/swiftMovement", Nightmares.SwiftMoveSpeedPct, "percent");
        Parameter("nightmare/swiftAttackSpeed", Nightmares.SwiftAttackSpeedPct, "percent");
        Parameter("nightmare/regeneration", Nightmares.RegenerationPctPerSecond, "percent-max-hp/second");
        Parameter("nightmare/arcanePower", Nightmares.ArcanePowerPct, "percent");
        Parameter("nightmare/arcaneHaste", Nightmares.ArcaneHaste, "haste");
        Parameter("nightmare/wardedArmor", Nightmares.WardedArmor, "armor");
        Parameter("nightmare/thornedArmor", Nightmares.ThornedArmor, "armor");
        Parameter("nightmare/ravenousAttack", Nightmares.RavenousAttackPct, "percent");
        Parameter("nightmare/sunderingAttack", Nightmares.SunderingAttackPct, "percent");
        Parameter("variant/baseChance", Variants.BaseChance, "probability");
        Parameter("variant/chancePerDepth", Variants.ChancePerDepth, "probability/heat");
        Parameter("variant/defaultShardBonus", Variants.DefaultShardBonusPct, "percent");
        Parameter("variant/devourerShardBonus", Variants.DevourerShardBonusPct, "percent");
        Parameter("variant/bonusShards", Variants.BonusShards, "shards");
        Parameter("behavior/openingIncoming", MonsterBehavior.OpeningIncomingMultiplier, "multiplier");
        Parameter("behavior/facingDot", MonsterBehavior.FacingDot, "cosine");
        Parameter("behavior/hitMovement", MonsterBehavior.HitMovementPct, "percent");
        Parameter("behavior/unhitMovement", MonsterBehavior.UnhitMovementPct, "percent");
        Parameter("behavior/lastStandHealth", MonsterBehavior.LastStandHealthRatio, "fraction-max-hp");
        Parameter("behavior/firstPhaseHealth", MonsterBehavior.FirstPhaseHealthRatio, "fraction-max-hp");
        Parameter("behavior/secondPhaseHealth", MonsterBehavior.SecondPhaseHealthRatio, "fraction-max-hp");
        Parameter("behavior/innerRange", MonsterBehavior.InnerRange, "meters");
        Parameter("behavior/allyRadius", MonsterBehavior.AllyRadius, "meters");
        Parameter("behavior/guard", MonsterBehavior.GuardReduction, "fraction-damage");
        Parameter("behavior/maxGuard", MonsterBehavior.MaxGuardReduction, "fraction-damage");
        Parameter("behavior/bossGuard", MonsterBehavior.BossGuardReduction, "fraction-damage");
        Parameter("behavior/warmup", MonsterBehavior.WarmupSeconds, "seconds");
        Parameter("behavior/pulseHalfPeriod", MonsterBehavior.PulseHalfPeriod, "seconds");
        Parameter("behavior/recovery", MonsterBehavior.RecoverySeconds, "seconds");
        Parameter("behavior/hitSlow", MonsterBehavior.HitSlowSeconds, "seconds");
        Parameter("behavior/healDelay", MonsterBehavior.HealDelaySeconds, "seconds");
        Parameter("behavior/healRate", MonsterBehavior.HealPctPerSecond, "percent-max-hp/second");
        Parameter("behavior/healBudget", MonsterBehavior.HealBudgetPct, "percent-max-hp");
        Parameter("behavior/shield", MonsterBehavior.ShieldPct, "percent-max-hp");
        Parameter("behavior/shieldSeconds", MonsterBehavior.ShieldSeconds, "seconds");
        Parameter("behavior/phaseOpening", MonsterBehavior.PhaseOpeningSeconds, "seconds");
        Parameter("behavior/weakness", MonsterBehavior.WeaknessTakenBonus, "fraction-damage");
        Parameter("behavior/resistance", MonsterBehavior.MaxTagResistance, "fraction-damage");
        Parameter("behavior/hunter", MonsterBehavior.TagHunterDealtBonus, "fraction-damage");
        Parameter("behavior/lightStacks", MonsterBehavior.LightEaterMinStacks, "stacks");
        return entries;
    }

    private static string StatUnit(Stat stat) => stat switch
    {
        Stat.Armor => "armor", Stat.Haste => "haste", Stat.AttackFlat => "attack", Stat.MaxHealthFlat => "health",
        _ => "percent",
    };

    internal static string Render(IReadOnlyList<QuantityMetricValue> entries)
    {
        var text = new StringBuilder();
        text.AppendLine("# Pressure and enemy profiles / 決定的な圧・敵パラメータ比較");
        text.AppendLine();
        text.AppendLine(Policy);
        text.AppendLine("Waypoint axis: None and FleetingMemories (the pressure modifier). Nightmare/variant chance assumes an eligible enemy with no variant already in the room; single affixes are independent profiles, not combined random outcomes. Stats use display units; appearance is excluded. No seed or player count is consumed.");
        text.AppendLine();
        text.AppendLine("| Stable ID | Projection | Value | Unit | Status |");
        text.AppendLine("|---|---|---:|---|---|");
        foreach (var entry in entries)
            text.AppendLine($"| {entry.Id} | {entry.Label} | {entry.Value?.ToString("G17", CultureInfo.InvariantCulture) ?? "—"} | {entry.Unit} | {entry.Status} |");
        return text.ToString();
    }
}
