using System;
using System.Globalization;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        private static string Credit(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static JsonObject WriteInfinityRewardBudget(InfinityRewardBudget b)
            => new JsonObject().Add("version", 1L)
                .Add("lesserTime", Credit(b.LesserTime)).Add("normalTime", Credit(b.NormalTime)).Add("miniBossTime", Credit(b.MiniBossTime)).Add("bossTime", Credit(b.BossTime))
                .Add("lesserRoom", Credit(b.LesserRoom)).Add("normalRoom", Credit(b.NormalRoom)).Add("miniBossRoom", Credit(b.MiniBossRoom)).Add("bossRoom", Credit(b.BossRoom))
                .Add("highRare", Credit(b.HighRare)).Add("legendary", Credit(b.Legendary)).Add("relics", Credit(b.Relics)).Add("guaranteeOpportunities", Credit(b.GuaranteeOpportunities)).Add("guaranteedRelics", Credit(b.GuaranteedRelics))
                .Add("shards", Credit(b.Shards)).Add("tuning", Credit(b.Tuning)).Add("xp", Credit(b.Xp)).Add("starXp", Credit(b.StarXp)).Add("awakening", Credit(b.Awakening))
                .Add("dustConversions", Credit(b.DustConversions)).Add("merchants", Credit(b.Merchants))
                .Add("roomRunId", b.RoomRunId).Add("roomGraph", b.RoomGraph).Add("roomEpoch", b.RoomEpoch)
                .Add("acceptedKills", b.AcceptedKills).Add("rejectedKills", b.RejectedKills);
        private static double ReadCredit(JsonObject obj, string key, double cap)
        {
            if (!obj.TryGet(key, out object raw) || !(raw is string text)
                || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > cap)
                throw new LedgerFormatException("Invalid infinity reward credit " + key);
            return value;
        }
        private static InfinityRewardBudget ReadInfinityRewardBudget(JsonObject body)
        {
            if (!body.TryGet("infinityRewardBudget", out object raw)) return new InfinityRewardBudget();
            if (!(raw is JsonObject obj) || InfinityLong(obj, "version", 1, 1) != 1)
                throw new LedgerFormatException("Unknown infinity reward budget format");
            if (!obj.TryGet("roomRunId", out object identity) || (identity != null && (!(identity is string id) || id.Length == 0 || id.Length > 256)))
                throw new LedgerFormatException("Invalid infinity reward room identity");
            var b = new InfinityRewardBudget
            {
                LesserTime = ReadCredit(obj, "lesserTime", InfinityRewards.LesserTimeBurst), NormalTime = ReadCredit(obj, "normalTime", InfinityRewards.NormalTimeBurst), MiniBossTime = ReadCredit(obj, "miniBossTime", InfinityRewards.MiniBossTimeBurst), BossTime = ReadCredit(obj, "bossTime", InfinityRewards.BossTimeBurst),
                LesserRoom = ReadCredit(obj, "lesserRoom", InfinityRewards.LesserRoomCap), NormalRoom = ReadCredit(obj, "normalRoom", InfinityRewards.NormalRoomCap), MiniBossRoom = ReadCredit(obj, "miniBossRoom", InfinityRewards.MiniBossRoomCap), BossRoom = ReadCredit(obj, "bossRoom", InfinityRewards.BossRoomCap),
                HighRare = ReadCredit(obj, "highRare", InfinityRewards.HighRareBurst), Legendary = ReadCredit(obj, "legendary", InfinityRewards.LegendaryBurst), Relics = ReadCredit(obj, "relics", InfinityRewards.RelicsBurst), GuaranteeOpportunities = ReadCredit(obj, "guaranteeOpportunities", InfinityRewards.GuaranteeOpportunitiesBurst), GuaranteedRelics = ReadCredit(obj, "guaranteedRelics", InfinityRewards.GuaranteedRelicsBurst),
                Shards = ReadCredit(obj, "shards", InfinityRewards.ShardsBurst), Tuning = ReadCredit(obj, "tuning", InfinityRewards.TuningBurst), Xp = ReadCredit(obj, "xp", InfinityRewards.XpBurst), StarXp = ReadCredit(obj, "starXp", InfinityRewards.StarXpBurst), Awakening = ReadCredit(obj, "awakening", InfinityRewards.AwakeningBurst),
                DustConversions = ReadCredit(obj, "dustConversions", InfinityRewards.DustConversionsBurst), Merchants = ReadCredit(obj, "merchants", InfinityRewards.MerchantsBurst),
                RoomRunId = identity as string, RoomGraph = InfinityLong(obj, "roomGraph", -1), RoomEpoch = InfinityLong(obj, "roomEpoch", -1),
                AcceptedKills = InfinityLong(obj, "acceptedKills"), RejectedKills = InfinityLong(obj, "rejectedKills"),
            };
            if ((b.RoomRunId == null && (b.RoomGraph != -1 || b.RoomEpoch != -1))
                || (b.RoomRunId != null && (b.RoomGraph < 0 || b.RoomEpoch < 0)))
                throw new LedgerFormatException("Inconsistent infinity reward room identity");
            return b;
        }
    }
}
