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
        // The saved Infinity reward budget is inert: values are round-tripped unchanged.
        // These caps are the frozen format contract from the retired budget system.
        private static InfinityRewardBudget ReadInfinityRewardBudget(JsonObject body)
        {
            if (!body.TryGet("infinityRewardBudget", out object raw)) return new InfinityRewardBudget();
            if (!(raw is JsonObject obj) || InfinityLong(obj, "version", 1, 1) != 1)
                throw new LedgerFormatException("Unknown infinity reward budget format");
            if (!obj.TryGet("roomRunId", out object identity) || (identity != null && (!(identity is string id) || id.Length == 0 || id.Length > 256)))
                throw new LedgerFormatException("Invalid infinity reward room identity");
            var b = new InfinityRewardBudget
            {
                LesserTime = ReadCredit(obj, "lesserTime", 10), NormalTime = ReadCredit(obj, "normalTime", 8), MiniBossTime = ReadCredit(obj, "miniBossTime", 1), BossTime = ReadCredit(obj, "bossTime", 1),
                LesserRoom = ReadCredit(obj, "lesserRoom", 10), NormalRoom = ReadCredit(obj, "normalRoom", 8), MiniBossRoom = ReadCredit(obj, "miniBossRoom", 1), BossRoom = ReadCredit(obj, "bossRoom", 1),
                HighRare = ReadCredit(obj, "highRare", 7), Legendary = ReadCredit(obj, "legendary", 7), Relics = ReadCredit(obj, "relics", 24), GuaranteeOpportunities = ReadCredit(obj, "guaranteeOpportunities", 1), GuaranteedRelics = ReadCredit(obj, "guaranteedRelics", 2),
                Shards = ReadCredit(obj, "shards", 30), Tuning = ReadCredit(obj, "tuning", 3), Xp = ReadCredit(obj, "xp", 50), StarXp = ReadCredit(obj, "starXp", 40), Awakening = ReadCredit(obj, "awakening", 20),
                DustConversions = ReadCredit(obj, "dustConversions", 1), Merchants = ReadCredit(obj, "merchants", 1),
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
