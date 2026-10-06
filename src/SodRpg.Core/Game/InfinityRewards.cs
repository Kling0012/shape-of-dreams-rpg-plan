using System;

namespace SodRpg.Core.Game
{
    /// <summary>Fixed-size lifetime credits. A new profile has no time credits; runs never reset them.</summary>
    public sealed class InfinityRewardBudget
    {
        public double LesserTime, NormalTime, MiniBossTime, BossTime;
        public double LesserRoom, NormalRoom, MiniBossRoom, BossRoom;
        public double HighRare, Legendary, Relics, GuaranteeOpportunities, GuaranteedRelics;
        public double Shards, Tuning, Xp, StarXp, Awakening, DustConversions, Merchants;
        public string RoomRunId;
        public long RoomGraph = -1, RoomEpoch = -1;
        public long AcceptedKills, RejectedKills;
        public InfinityRewardBudget Clone() => (InfinityRewardBudget)MemberwiseClone();
    }

    public static class InfinityRewards
    {
        public const double ReferenceSeconds = InfinityBalance.ReferenceSeconds;
        public const double RelicsPerHour = InfinityBalance.RelicsPerHour, GuaranteesPerHour = InfinityBalance.GuaranteesPerHour;
        public const double ShardsPerHour = InfinityBalance.ShardsPerHour, TuningPerHour = InfinityBalance.TuningPerHour, XpPerHour = InfinityBalance.XpPerHour;
        public const double StarXpPerHour = InfinityBalance.StarXpPerHour, AwakeningPerHour = InfinityBalance.AwakeningPerHour;
        public const double DustConversionsPerHour = InfinityBalance.DustConversionsPerHour, MerchantsPerHour = InfinityBalance.MerchantsPerHour;
        public const int KillMixLesser = InfinityBalance.KillMixLesser;
        public const int KillMixNormal = InfinityBalance.KillMixNormal;
        public const int KillMixMiniBoss = InfinityBalance.KillMixMiniBoss;
        public const int KillMixBoss = InfinityBalance.KillMixBoss;
        public const double LesserTimeBurst = InfinityBalance.LesserTimeBurst;
        public const double NormalTimeBurst = InfinityBalance.NormalTimeBurst;
        public const double MiniBossTimeBurst = InfinityBalance.MiniBossTimeBurst;
        public const double BossTimeBurst = InfinityBalance.BossTimeBurst;
        public const double HighRareBurst = InfinityBalance.HighRareBurst;
        public const double RelicsBurst = InfinityBalance.RelicsBurst;
        public const double LegendaryBurst = InfinityBalance.LegendaryBurst;
        public const double GuaranteeOpportunitiesBurst = InfinityBalance.GuaranteeOpportunitiesBurst;
        public const double GuaranteedRelicsBurst = InfinityBalance.GuaranteedRelicsBurst;
        public const double ShardsBurst = InfinityBalance.ShardsBurst;
        public const double TuningBurst = InfinityBalance.TuningBurst;
        public const double XpBurst = InfinityBalance.XpBurst;
        public const double StarXpBurst = InfinityBalance.StarXpBurst;
        public const double AwakeningBurst = InfinityBalance.AwakeningBurst;
        public const double DustConversionsBurst = InfinityBalance.DustConversionsBurst;
        public const double MerchantsBurst = InfinityBalance.MerchantsBurst;
        public const double LesserRoomCap = InfinityBalance.LesserRoomCap;
        public const double LesserRoomIncrement = InfinityBalance.LesserRoomIncrement;
        public const double NormalRoomCap = InfinityBalance.NormalRoomCap;
        public const double NormalRoomIncrement = InfinityBalance.NormalRoomIncrement;
        public const double MiniBossRoomCap = InfinityBalance.MiniBossRoomCap;
        public const double MiniBossRoomIncrement = InfinityBalance.MiniBossRoomIncrement;
        public const double BossRoomCap = InfinityBalance.BossRoomCap;
        public const double BossRoomIncrement = InfinityBalance.BossRoomIncrement;
        private static readonly double HighRarePerSecond = NormalHighRarePerHour(0) / 3600;
        private static readonly double LegendaryPerSecond = NormalLegendaryPerHour(0) / 3600;
        public static bool Active(Profile p) => p?.Run?.Infinity != null;
        private static void Refill(ref double credit, double rate, double seconds, double burst)
            => credit = Math.Min(burst, credit + rate * seconds);
        private static int Take(ref double credit, int requested)
        {
            int amount = (int)Math.Min(Math.Max(0, requested), Math.Floor(credit + 1e-9));
            credit = Math.Max(0, credit - amount);
            return amount;
        }
        public static void AdvanceCombat(Profile p, double deltaSeconds)
        {
            if (!Active(p) || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0) return;
            var b = p.InfinityRewardBudget;
            Refill(ref b.LesserTime, KillMixLesser / ReferenceSeconds, deltaSeconds, LesserTimeBurst);
            Refill(ref b.NormalTime, KillMixNormal / ReferenceSeconds, deltaSeconds, NormalTimeBurst);
            Refill(ref b.MiniBossTime, KillMixMiniBoss / ReferenceSeconds, deltaSeconds, MiniBossTimeBurst);
            Refill(ref b.BossTime, KillMixBoss / ReferenceSeconds, deltaSeconds, BossTimeBurst);
            Refill(ref b.HighRare, HighRarePerSecond, deltaSeconds, HighRareBurst);
            Refill(ref b.Relics, RelicsPerHour / 3600, deltaSeconds, RelicsBurst);
            Refill(ref b.Legendary, LegendaryPerSecond, deltaSeconds, LegendaryBurst);
            Refill(ref b.GuaranteeOpportunities, GuaranteesPerHour / 3600, deltaSeconds, GuaranteeOpportunitiesBurst);
            Refill(ref b.GuaranteedRelics, GuaranteesPerHour / 3600, deltaSeconds, GuaranteedRelicsBurst);
            Refill(ref b.Shards, ShardsPerHour / 3600, deltaSeconds, ShardsBurst);
            Refill(ref b.Tuning, TuningPerHour / 3600, deltaSeconds, TuningBurst);
            Refill(ref b.Xp, XpPerHour / 3600, deltaSeconds, XpBurst);
            Refill(ref b.StarXp, StarXpPerHour / 3600, deltaSeconds, StarXpBurst);
            Refill(ref b.Awakening, AwakeningPerHour / 3600, deltaSeconds, AwakeningBurst);
            Refill(ref b.DustConversions, DustConversionsPerHour / 3600, deltaSeconds, DustConversionsBurst);
            Refill(ref b.Merchants, MerchantsPerHour / 3600, deltaSeconds, MerchantsBurst);
        }
        /// <summary>Call once on authoritative entry to a new native Combat, never for a boss/revisit.</summary>
        public static void EnterRoom(Profile p, long graph, long roomEpoch)
        {
            if (!Active(p) || graph < 0 || roomEpoch < 0) return;
            var b = p.InfinityRewardBudget;
            if (b.RoomRunId == p.Run.RunId && (graph < b.RoomGraph || (graph == b.RoomGraph && roomEpoch <= b.RoomEpoch))) return;
            b.RoomRunId = p.Run.RunId; b.RoomGraph = graph; b.RoomEpoch = roomEpoch;
            b.LesserRoom = Math.Min(LesserRoomCap, b.LesserRoom + LesserRoomIncrement);
            b.NormalRoom = Math.Min(NormalRoomCap, b.NormalRoom + NormalRoomIncrement);
            b.MiniBossRoom = Math.Min(MiniBossRoomCap, b.MiniBossRoom + MiniBossRoomIncrement);
            b.BossRoom = Math.Min(BossRoomCap, b.BossRoom + BossRoomIncrement);
        }
        // Share the actual rarity weights with Loot; balance changes cannot silently stale the cap.
        private static double HighProbability(MonsterTier tier, double luck, Rarity floor, out double legendary)
            => Loot.HighRarityProbability(luck, tier >= MonsterTier.MiniBoss, floor, out legendary);
        public static double NormalHighRarePerHour(int dreamDepth) => NormalRareRate(dreamDepth, false);
        public static double NormalLegendaryPerHour(int dreamDepth) => NormalRareRate(dreamDepth, true);
        private static double NormalRareRate(int depth, bool legendaryOnly)
        {
            double luck = DreamDepth.RarityLuck(depth);
            double lesser = HighProbability(MonsterTier.Lesser, luck, Rarity.Common, out double lesserLegend);
            double normal = HighProbability(MonsterTier.Normal, luck, Rarity.Common, out double normalLegend);
            double mini = HighProbability(MonsterTier.MiniBoss, luck + Loot.TierLuck(MonsterTier.MiniBoss), Rarity.Common, out double miniLegend);
            double boss = HighProbability(MonsterTier.Boss, luck + Loot.TierLuck(MonsterTier.Boss), Rarity.Uncommon, out double bossLegend);
            // No boss-set credit: most ordinary bosses have no registered set, unlike a repeated fixed Demon farm.
            double ev = KillMixLesser * Loot.DropChance(MonsterTier.Lesser, 0) * (legendaryOnly ? lesserLegend : lesser)
                + KillMixNormal * Loot.DropChance(MonsterTier.Normal, 0) * (legendaryOnly ? normalLegend : normal)
                + KillMixMiniBoss * Loot.DropChance(MonsterTier.MiniBoss, 0) * (legendaryOnly ? miniLegend : mini)
                + KillMixBoss * Loot.DropChance(MonsterTier.Boss, 0) * (1 + Loot.BossExtraRelicChance) * (legendaryOnly ? bossLegend : boss);
            return ev * 3600 / ReferenceSeconds;
        }
        public static double ExpectedKillHighRareCost(RunState run, MonsterTier rollTier, int heat, Waypoint waypoint,
            bool nightmare, string bossTypeName = null, bool bossDropNightmare = false, int bossDropDepth = 0)
            => ExpectedKillCosts(run, rollTier, heat, waypoint, nightmare, bossTypeName, bossDropNightmare, bossDropDepth, out _);
        public static double ExpectedKillLegendaryCost(RunState run, MonsterTier rollTier, int heat, Waypoint waypoint,
            bool nightmare, string bossTypeName = null, bool bossDropNightmare = false, int bossDropDepth = 0)
        {
            ExpectedKillCosts(run, rollTier, heat, waypoint, nightmare, bossTypeName, bossDropNightmare, bossDropDepth, out double legendary);
            return legendary;
        }
        private static double ExpectedKillCosts(RunState run, MonsterTier rollTier, int heat, Waypoint waypoint,
            bool nightmare, string bossTypeName, bool bossDropNightmare, int bossDropDepth, out double legendary)
        {
            var t = Waypoints.Sum(waypoint);
            double dropBonus = Rules.LimboDropBonus * run.LimboDepth + run.EventDropBonus + (DailyDream.Get(run.DailyId)?.DropBonus ?? 0);
            double luck = Rules.LimboLuck * run.LimboDepth + run.EventLuck + DreamDepth.RarityLuck(run.DreamDepth) + t.Luck;
            foreach (var pact in run.Pacts)
            {
                var def = Pacts.Get(pact);
                if (def != null) { dropBonus += def.DropBonus; luck += def.Luck; }
            }
            double chance = Math.Min(1, Math.Max(0, Loot.DropChance(rollTier, heat) * (1 + dropBonus)));
            double probability = HighProbability(rollTier, Loot.TierLuck(rollTier) + Loot.HeatLuck * Loot.ClampHeat(heat) + luck,
                rollTier == MonsterTier.Boss ? Rarity.Uncommon : Rarity.Common, out double legendaryProbability);
            double factor = chance * (rollTier == MonsterTier.Boss ? 1 + Loot.BossExtraRelicChance : 1);
            double ev = factor * probability;
            legendary = factor * legendaryProbability;
            if ((t.ForcedRarity.HasValue && t.ForcedRarity.Value < Rarity.Epic) || t.RelicSalvageMultiplier > 0 || t.AwakeningPerRelic > 0)
            { ev = 0; legendary = 0; }
            double copies = t.TwinRelics ? 2 : nightmare ? t.NightmareRewardMultiplier : 1;
            if (t.DelayDropsUntilBoss) copies *= 3;
            ev *= copies; legendary *= copies;
            if (BossSets.TryGetSet(bossTypeName, out _))
            {
                double set = BossSets.DropChance(bossDropNightmare, bossDropDepth);
                ev += set; legendary += set;
            }
            return ev;
        }
        public static bool GuaranteedWaypoint(Waypoint waypoint, MonsterTier tier)
            => Waypoints.Sum(waypoint).MinimumRarity >= Rarity.Epic || (Waypoints.Sum(waypoint).NonBossRelicsToTuning && tier == MonsterTier.Boss);
        public static bool CanChooseWaypoint(Profile p, Waypoint waypoint)
            => !Active(p) || (!GuaranteedWaypoint(waypoint, MonsterTier.Boss))
                || (p.InfinityRewardBudget.GuaranteeOpportunities + 1e-9 >= 1
                    && p.InfinityRewardBudget.GuaranteedRelics + 1e-9 >= (waypoint == Waypoint.BossTribute ? 2 : 1));
        public static bool CanChooseWaypoint(Profile p, Waypoint waypoint, out string reason)
        {
            bool allowed = CanChooseWaypoint(p, waypoint);
            int needed = waypoint == Waypoint.BossTribute ? 2 : 1;
            reason = allowed ? null : Loc.T($"Infinityの保証予算が不足しています（必要枠{needed}、戦闘1時間につき{GuaranteesPerHour}枠、初期枠なし）。",
                $"Infinity guarantee budget is unavailable ({needed} credit(s) required, {GuaranteesPerHour} per combat hour, no initial credit).");
            return allowed;
        }
        /// <summary>Admit the entire random opportunity before any roll; never inspect a rolled rarity to limit EV.</summary>
        internal static bool AdmitKill(Profile p, MonsterTier tier, MonsterTier rollTier, int heat, Waypoint waypoint, bool nightmare,
            string bossTypeName, bool bossDropNightmare, int bossDropDepth)
        {
            if (!Active(p)) return true;
            var b = p.InfinityRewardBudget;
            double time = rollTier == MonsterTier.Lesser ? b.LesserTime : rollTier == MonsterTier.Normal ? b.NormalTime : rollTier == MonsterTier.MiniBoss ? b.MiniBossTime : b.BossTime;
            double room = rollTier == MonsterTier.Lesser ? b.LesserRoom : rollTier == MonsterTier.Normal ? b.NormalRoom : rollTier == MonsterTier.MiniBoss ? b.MiniBossRoom : b.BossRoom;
            bool guaranteed = GuaranteedWaypoint(waypoint, tier);
            int guaranteedOutputs = guaranteed ? (rollTier == MonsterTier.Boss ? 2 : 1) : 0;
            if (time + 1e-9 < 1 || room + 1e-9 < 1 || (guaranteed && (b.GuaranteeOpportunities + 1e-9 < 1
                || b.GuaranteedRelics + 1e-9 < guaranteedOutputs || b.Relics + 1e-9 < guaranteedOutputs)))
            { if (b.RejectedKills < long.MaxValue) b.RejectedKills++; return false; }
            double ev = ExpectedKillCosts(p.Run, rollTier, heat, waypoint, nightmare, tier == MonsterTier.Boss ? bossTypeName : null, bossDropNightmare, bossDropDepth, out double legendary);
            // The guarantee covers the Epic floor, not random Legendary rolls; legendary already includes the boss set once.
            if (guaranteed) ev = tier == MonsterTier.Boss && BossSets.TryGetSet(bossTypeName, out _) ? BossSets.DropChance(bossDropNightmare, bossDropDepth) : 0;
            if (b.HighRare + 1e-12 < ev || b.Legendary + 1e-12 < legendary)
            { if (b.RejectedKills < long.MaxValue) b.RejectedKills++; return false; }
            if (rollTier == MonsterTier.Lesser) { b.LesserTime = Math.Max(0, b.LesserTime - 1); b.LesserRoom = Math.Max(0, b.LesserRoom - 1); }
            else if (rollTier == MonsterTier.Normal) { b.NormalTime = Math.Max(0, b.NormalTime - 1); b.NormalRoom = Math.Max(0, b.NormalRoom - 1); }
            else if (rollTier == MonsterTier.MiniBoss) { b.MiniBossTime = Math.Max(0, b.MiniBossTime - 1); b.MiniBossRoom = Math.Max(0, b.MiniBossRoom - 1); }
            else { b.BossTime = Math.Max(0, b.BossTime - 1); b.BossRoom = Math.Max(0, b.BossRoom - 1); }
            b.Legendary = Math.Max(0, b.Legendary - legendary);
            b.HighRare = Math.Max(0, b.HighRare - ev);
            if (guaranteed)
            {
                b.GuaranteeOpportunities = Math.Max(0, b.GuaranteeOpportunities - 1);
                b.GuaranteedRelics = Math.Max(0, b.GuaranteedRelics - guaranteedOutputs);
            }
            if (b.AcceptedKills < long.MaxValue) b.AcceptedKills++;
            return true;
        }
        /// <summary>Reserve final free output, including Hoard amplification, before deferral; no charge on release.</summary>
        internal static void LimitReward(Profile p, KillReward reward, int amplification = 1)
        {
            if (!Active(p)) return;
            var b = p.InfinityRewardBudget;
            int keep = (int)Math.Min(reward.Relics.Count, Math.Floor(b.Relics / amplification + 1e-9));
            if (keep < reward.Relics.Count) reward.Relics.RemoveRange(keep, reward.Relics.Count - keep);
            b.Relics = Math.Max(0, b.Relics - keep * amplification);
            foreach (var relic in reward.Relics) relic.InfinityFreeSupply = true;
            reward.Shards = TakeAmplified(ref b.Shards, reward.Shards, amplification);
            reward.Tuning = TakeAmplified(ref b.Tuning, reward.Tuning, amplification);
        }
        private static int TakeAmplified(ref double credit, int amount, int copies)
        {
            int keep = (int)Math.Min(Math.Max(0, amount), Math.Floor(credit / copies + 1e-9));
            credit = Math.Max(0, credit - (double)keep * copies); return keep;
        }
        public static int LimitShards(Profile p, int amount) => Active(p) ? Take(ref p.InfinityRewardBudget.Shards, amount) : amount;
        public static int LimitTuning(Profile p, int amount) => Active(p) ? Take(ref p.InfinityRewardBudget.Tuning, amount) : amount;
        public static int LimitXp(Profile p, int amount) => Active(p) ? Take(ref p.InfinityRewardBudget.Xp, amount) : amount;
        public static int LimitStarXp(Profile p, int amount) => Active(p) ? Take(ref p.InfinityRewardBudget.StarXp, amount) : amount;
        public static int LimitAwakening(Profile p, int amount) => Active(p) ? Take(ref p.InfinityRewardBudget.Awakening, amount) : amount;
        internal static int LimitHoardOverflow(Profile p, int amount) => Active(p) ? TakeAmplified(ref p.InfinityRewardBudget.Shards, amount, 3) : amount;
        public static bool CanConvertDust(Profile p, int batches) => !Active(p) || (batches > 0 && p.InfinityRewardBudget.DustConversions + 1e-9 >= batches && p.InfinityRewardBudget.Shards + 1e-9 >= (long)batches * Economy.ShardsPerBatch);
        public static bool ReserveDustConversion(Profile p, int batches)
        {
            if (!CanConvertDust(p, batches)) return false;
            if (Active(p)) { p.InfinityRewardBudget.DustConversions = Math.Max(0, p.InfinityRewardBudget.DustConversions - batches); p.InfinityRewardBudget.Shards = Math.Max(0, p.InfinityRewardBudget.Shards - batches * Economy.ShardsPerBatch); }
            return true;
        }
        public static bool CanBuyMerchant(Profile p) => !Active(p) || p.InfinityRewardBudget.Merchants + 1e-9 >= 1;
        public static bool ReserveMerchant(Profile p)
        {
            if (!CanBuyMerchant(p)) return false;
            if (Active(p)) p.InfinityRewardBudget.Merchants = Math.Max(0, p.InfinityRewardBudget.Merchants - 1);
            return true;
        }
    }
}
