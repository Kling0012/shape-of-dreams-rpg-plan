using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        private static JsonObject WriteRunRecovery(RunRecoveryState state)
        {
            if (state == null) return null;
            return new JsonObject().Add("runId", state.RunId).Add("zone", (long)state.ZoneIndex)
                .Add("lastArrival", (long)state.LastArrival)
                .Add("arrivals", state.Arrivals.Select(x => (object)(long)x).ToList())
                .Add("committed", state.CommittedChoices.Select(x => (object)x).ToList())
                .Add("kills", state.PendingKills.Select(x => (object)WritePendingKill(x)).ToList())
                .Add("resultRunId", state.PendingResultRunId).Add("victory", state.PendingVictory.HasValue ? (object)state.PendingVictory.Value : null)
                .Add("publisherHistory", state.PublisherHistory.Select(x => (object)x).ToList())
                .Add("publisherTerminal", state.PublisherTerminalChoices)
                .Add("publisherVictory", state.PublisherVictory.HasValue ? (object)state.PublisherVictory.Value : null)
                .Add("dividendRunId", state.DividendRunId)
                .Add("dividends", state.PendingDividends.Select(x => (object)new JsonObject().Add("runId", x.RunId)
                    .Add("zone", (long)x.ZoneId).Add("spawn", x.SpawnId).Add("owner", x.OwnerId).Add("nonce", x.RewardNonce)).ToList())
                .Add("dividendNonces", state.DividendNonces.Select(x => (object)x).ToList())
                .Add("dividendDeaths", state.DividendDeaths.Select(x => (object)x).ToList())
                .Add("growthRunId", state.GrowthRunId)
                .Add("growth", state.Growth.Select(x => (object)new JsonObject().Add("owner", x.Owner).Add("id", x.GrowthId)
                    .Add("stacks", (long)x.Stacks).Add("progress", x.ProgressMilli)).ToList());
        }

        private static RunRecoveryState ReadRunRecovery(JsonObject parent)
        {
            if (!parent.TryGet("runRecovery", out object value) || !(value is JsonObject j)) return null;
            var state = new RunRecoveryState
            {
                RunId = Str(j, "runId"), ZoneIndex = Clamp(Long(j, "zone"), -1, int.MaxValue),
                LastArrival = Clamp(Long(j, "lastArrival"), -1, int.MaxValue),
                PendingResultRunId = Str(j, "resultRunId"), PendingVictory = NullableBool(j, "victory"),
                PublisherTerminalChoices = Str(j, "publisherTerminal"), PublisherVictory = NullableBool(j, "publisherVictory"),
                DividendRunId = Str(j, "dividendRunId"), GrowthRunId = Str(j, "growthRunId"),
            };
            foreach (object item in Array(j, "arrivals"))
                if (item is long zone && zone >= 0 && zone <= int.MaxValue) state.Arrivals.Add((int)zone);
            foreach (object item in Array(j, "committed"))
                if (item is string encoded) state.CommittedChoices.Add(encoded);
            foreach (object item in Array(j, "publisherHistory"))
                if (item is string encoded) state.PublisherHistory.Add(encoded);
            foreach (object item in Array(j, "kills"))
                if (item is JsonObject kill) state.PendingKills.Add(ReadPendingKill(kill));
            foreach (object item in Array(j, "dividends"))
                if (item is JsonObject dividend)
                    state.PendingDividends.Add(new PressureDividendReward(Str(dividend, "runId"),
                        (int)Long(dividend, "zone"), Long(dividend, "spawn"), Str(dividend, "owner"), Str(dividend, "nonce")));
            foreach (object item in Array(j, "dividendNonces"))
                if (item is string nonce) state.DividendNonces.Add(nonce);
            foreach (object item in Array(j, "dividendDeaths"))
                if (item is string death) state.DividendDeaths.Add(death);
            foreach (object item in Array(j, "growth"))
                if (item is JsonObject growth)
                    state.Growth.Add(new RunGrowthSave { Owner = Str(growth, "owner"), GrowthId = Str(growth, "id"),
                        Stacks = (int)Clamp(Long(growth, "stacks"), 0, RunGrowthDef.MaxCap), ProgressMilli = System.Math.Max(0L, Long(growth, "progress")) });
            return state;
        }

        private static JsonObject WritePendingKill(PendingRunKill kill) => new JsonObject()
            .Add("runId", kill.RunId).Add("zone", (long)kill.ZoneIndex).Add("room", (long)kill.RoomIndex)
            .Add("tier", (long)kill.Tier).Add("level", (long)kill.Level).Add("nightmare", (long)kill.Nightmare)
            .Add("variant", kill.VariantId).Add("hero", kill.HeroKey)
            .Add("eventId", kill.EventId).Add("monster", (long)kill.MonsterNetId)
            .Add("bossTypeName", kill.BossTypeName).Add("bossDropNightmare", kill.BossDropNightmare)
            .Add("bossDropDepth", (long)kill.BossDropDepth);

        private static PendingRunKill ReadPendingKill(JsonObject j) => new PendingRunKill(
            Str(j, "runId"), Clamp(Long(j, "zone"), -1, int.MaxValue), Clamp(Long(j, "room"), 0, int.MaxValue),
            (MonsterTier)Clamp(Long(j, "tier"), 0, (int)MonsterTier.Boss), Clamp(Long(j, "level"), 1, int.MaxValue),
            (NightmareAffix)Long(j, "nightmare"), Str(j, "variant"), Str(j, "hero"), Str(j, "eventId"),
            (uint)System.Math.Max(0, System.Math.Min(uint.MaxValue, Long(j, "monster"))),
            Str(j, "bossTypeName"), NullableBool(j, "bossDropNightmare") ?? false,
            Clamp(Long(j, "bossDropDepth"), 0, 5));

        private static JsonObject WriteKillClassification(KillClassificationCheckpoint state)
        {
            if (state == null) return null;
            return new JsonObject().Add("runId", state.RunId)
                .Add("deaths", state.Deaths.Select(x => (object)new JsonObject().Add("monster", (long)x.MonsterNetId)
                    .Add("kill", WritePendingKill(x.Kill))).ToList())
                .Add("facts", state.Facts.Select(x => (object)new JsonObject().Add("runId", x.RunId)
                    .Add("eventId", x.EventId).Add("monster", (long)x.MonsterNetId).Add("zone", (long)x.ZoneIndex)
                    .Add("nightmare", (long)x.Nightmare).Add("variant", x.VariantId)
                    .Add("bossTypeName", x.BossTypeName).Add("bossDropNightmare", x.BossDropNightmare)
                    .Add("bossDropDepth", (long)x.BossDropDepth)).ToList())
                .Add("resolved", state.ResolvedEventIds.Select(x => (object)x).ToList());
        }

        private static KillClassificationCheckpoint ReadKillClassification(JsonObject parent)
        {
            if (!parent.TryGet("killClassification", out object value) || !(value is JsonObject j)) return null;
            var state = new KillClassificationCheckpoint { RunId = Str(j, "runId") };
            foreach (object item in Array(j, "deaths"))
                if (item is JsonObject death && death.TryGet("kill", out object kill) && kill is JsonObject k)
                    state.Deaths.Add(new PendingMonsterDeath((uint)Long(death, "monster"), ReadPendingKill(k)));
            foreach (object item in Array(j, "facts"))
                if (item is JsonObject fact)
                    state.Facts.Add(new AuthoritativeRunKill(Str(fact, "runId"), Str(fact, "eventId"),
                        (uint)Long(fact, "monster"), Clamp(Long(fact, "zone"), -1, int.MaxValue),
                        (NightmareAffix)Long(fact, "nightmare"), Str(fact, "variant"),
                        Str(fact, "bossTypeName"), NullableBool(fact, "bossDropNightmare") ?? false,
                        Clamp(Long(fact, "bossDropDepth"), 0, 5)));
            foreach (object item in Array(j, "resolved"))
                if (item is string id) state.ResolvedEventIds.Add(id);
            return state;
        }

        private static IEnumerable<object> Array(JsonObject parent, string key) =>
            parent.TryGet(key, out object value) && value is List<object> items ? items : System.Array.Empty<object>();

        private static bool? NullableBool(JsonObject parent, string key) =>
            parent.TryGet(key, out object value) && value is bool result ? result : (bool?)null;
    }
}
