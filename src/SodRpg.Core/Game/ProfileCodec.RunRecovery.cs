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

        private static JsonObject WritePendingKill(PendingRunKill kill)
        {
            var j = new JsonObject()
                .Add("runId", kill.RunId).Add("zone", (long)kill.ZoneIndex).Add("room", (long)kill.RoomIndex)
                .Add("tier", (long)kill.Tier).Add("level", (long)kill.Level).Add("nightmare", (long)kill.Nightmare)
                .Add("variant", kill.VariantId).Add("hero", kill.HeroKey)
                .Add("eventId", kill.EventId).Add("monster", (long)kill.MonsterNetId);
            // #71: 戦ったときの深度と道標。記録のない旧保存データは読み込み時に null へ戻る。
            if (kill.Heat.HasValue) j.Add("heat", (long)kill.Heat.Value);
            if (kill.Waypoint.HasValue) j.Add("waypoint", (long)kill.Waypoint.Value);
            return j;
        }

        private static PendingRunKill ReadPendingKill(JsonObject j)
        {
            int? heat = j.TryGet("heat", out object heatValue) && heatValue is long heatLong
                ? Loot.ClampHeat((int)Clamp(heatLong, 0, int.MaxValue)) : (int?)null;
            Waypoint? waypoint = j.TryGet("waypoint", out object waypointValue) && waypointValue is long waypointLong
                && ((Waypoint)waypointLong == Waypoint.None || Waypoints.Get((Waypoint)waypointLong) != null)
                ? (Waypoint)waypointLong : (Waypoint?)null;
            return new PendingRunKill(
                Str(j, "runId"), Clamp(Long(j, "zone"), -1, int.MaxValue), Clamp(Long(j, "room"), 0, int.MaxValue),
                (MonsterTier)Clamp(Long(j, "tier"), 0, (int)MonsterTier.Boss), Clamp(Long(j, "level"), 1, int.MaxValue),
                (NightmareAffix)Long(j, "nightmare"), Str(j, "variant"), Str(j, "hero"), Str(j, "eventId"),
                (uint)System.Math.Max(0, System.Math.Min(uint.MaxValue, Long(j, "monster"))), heat, waypoint);
        }

        private static JsonObject WriteKillClassification(KillClassificationCheckpoint state)
        {
            if (state == null) return null;
            return new JsonObject().Add("runId", state.RunId).Add("clientId", state.ClientId)
                .Add("receipts", state.Receipts.Select(x => (object)new JsonObject().Add("stream", x.StreamId)
                    .Add("receivedThrough", x.ReceivedThrough).Add("acknowledgedThrough", x.AcknowledgedThrough)
                    .Add("skippedThrough", x.SkippedThrough).Add("skippedFrom", x.SkippedFrom)
                    .Add("resolvedBelowBaseline", x.ResolvedBelowBaseline.Select(s => (object)s).ToList())
                    .Add("receivedAhead", x.ReceivedAhead.Select(s => (object)s).ToList())).ToList())
                .Add("deaths", state.Deaths.Select(x => (object)new JsonObject().Add("monster", (long)x.MonsterNetId)
                    .Add("stream", x.StreamId).Add("observer", x.ObservationSessionId).Add("kill", WritePendingKill(x.Kill))).ToList())
                .Add("facts", state.Facts.Select(x => (object)WriteKillFact(x)).ToList())
                .Add("resolved", state.ResolvedEventIds.Select(x => (object)x).ToList())
                .Add("expiredMonsters", state.ExpiredMonsterNetIds.Select(x => (object)(long)x).ToList())
                .Add("expiredVictims", state.ExpiredVictims.Select(x => (object)new JsonObject()
                    .Add("stream", x.StreamId).Add("monster", (long)x.MonsterNetId).Add("observer", x.ObservationSessionId)).ToList())
                .Add("legacyVictims", state.LegacyResolvedVictims.Select(x => (object)(long)x).ToList())
                .Add("hostSequence", state.HostSequence)
                .Add("hostFacts", state.HostFacts.Select(x => (object)WriteKillFact(x)).ToList())
                .Add("hostPeers", state.HostPeers.Select(x => (object)new JsonObject()
                    .Add("id", x.Id).Add("owner", x.NativeOwnerId).Add("receipts", x.Receipts.Select(r => (object)new JsonObject()
                        .Add("stream", r.StreamId).Add("receivedThrough", r.ReceivedThrough)).ToList())
                    .Add("participation", x.Participation.Select(r => (object)new JsonObject()
                        .Add("stream", r.StreamId).Add("observer", r.ObservationSessionId)
                        .Add("after", r.After).Add("through", r.Through)).ToList())).ToList());
        }

        private static JsonObject WriteKillFact(AuthoritativeRunKill fact) => new JsonObject()
            .Add("runId", fact.RunId).Add("eventId", fact.EventId).Add("monster", (long)fact.MonsterNetId)
            .Add("zone", (long)fact.ZoneIndex).Add("nightmare", (long)fact.Nightmare)
            .Add("variant", fact.VariantId).Add("sequence", fact.Sequence).Add("stream", fact.StreamId);

        private static AuthoritativeRunKill ReadKillFact(JsonObject fact) => new AuthoritativeRunKill(
            Str(fact, "runId"), Str(fact, "eventId"), (uint)Long(fact, "monster"),
            Clamp(Long(fact, "zone"), -1, int.MaxValue), (NightmareAffix)Long(fact, "nightmare"),
            Str(fact, "variant"), Long(fact, "sequence"), Str(fact, "stream"));

        private static KillClassificationCheckpoint ReadKillClassification(JsonObject parent)
        {
            if (!parent.TryGet("killClassification", out object value) || !(value is JsonObject j)) return null;
            var state = new KillClassificationCheckpoint
            {
                RunId = Str(j, "runId"), ClientId = Str(j, "clientId"),
                HostSequence = System.Math.Max(0, Long(j, "hostSequence")),
            };
            foreach (object item in Array(j, "receipts"))
                if (item is JsonObject r)
                {
                    var receipt = new KillReceiptState
                    {
                        StreamId = Str(r, "stream") ?? "", ReceivedThrough = System.Math.Max(0, Long(r, "receivedThrough")),
                        AcknowledgedThrough = System.Math.Max(0, Long(r, "acknowledgedThrough")),
                        SkippedThrough = System.Math.Max(0, Long(r, "skippedThrough")),
                        SkippedFrom = System.Math.Max(0, Long(r, "skippedFrom")),
                    };
                    foreach (object s in Array(r, "resolvedBelowBaseline"))
                        if (s is long sequence && sequence > 0) receipt.ResolvedBelowBaseline.Add(sequence);
                    foreach (object s in Array(r, "receivedAhead"))
                        if (s is long sequence && sequence > receipt.ReceivedThrough) receipt.ReceivedAhead.Add(sequence);
                    state.Receipts.Add(receipt);
                }
            foreach (object item in Array(j, "legacyVictims"))
                if (item is long victim && victim > 0 && victim <= uint.MaxValue) state.LegacyResolvedVictims.Add((uint)victim);
            foreach (object item in Array(j, "hostFacts"))
                if (item is JsonObject fact) state.HostFacts.Add(ReadKillFact(fact));
            foreach (object item in Array(j, "hostPeers"))
                if (item is JsonObject peer)
                {
                    var savedPeer = new KillReplayPeer
                    {
                        Id = Str(peer, "id"),
                        NativeOwnerId = Str(peer, "owner"),
                    };
                    foreach (object receipt in Array(peer, "receipts"))
                        if (receipt is JsonObject r) savedPeer.Receipts.Add(new KillReceiptFrontier
                        {
                            StreamId = Str(r, "stream") ?? "", ReceivedThrough = System.Math.Max(0, Long(r, "receivedThrough")),
                        });
                    foreach (object range in Array(peer, "participation"))
                        if (range is JsonObject r)
                        {
                            long after = System.Math.Max(0, Long(r, "after"));
                            long through = System.Math.Max(after, Long(r, "through"));
                            savedPeer.Participation.Add(new KillParticipationRange
                                { StreamId = Str(r, "stream") ?? "", ObservationSessionId = Str(r, "observer"), After = after, Through = through });
                        }
                    state.HostPeers.Add(savedPeer);
                }
            foreach (object item in Array(j, "deaths"))
                if (item is JsonObject death && death.TryGet("kill", out object kill) && kill is JsonObject k)
                    state.Deaths.Add(new PendingMonsterDeath((uint)Long(death, "monster"), ReadPendingKill(k),
                        Str(death, "stream") ?? PendingMonsterDeath.LegacyStreamId, Str(death, "observer")));
            foreach (object item in Array(j, "facts"))
                if (item is JsonObject fact) state.Facts.Add(ReadKillFact(fact));
            foreach (object item in Array(j, "resolved"))
                if (item is string id) state.ResolvedEventIds.Add(id);
            foreach (object item in Array(j, "expiredMonsters"))
                if (item is long monster && monster > 0 && monster <= uint.MaxValue)
                    state.ExpiredMonsterNetIds.Add((uint)monster);
            foreach (object item in Array(j, "expiredVictims"))
                if (item is JsonObject victim)
                {
                    long monster = Long(victim, "monster");
                    if (monster > 0 && monster <= uint.MaxValue)
                        state.ExpiredVictims.Add(new KillVictimKey(Str(victim, "stream"), (uint)monster, Str(victim, "observer")));
                }
            return state;
        }

        private static IEnumerable<object> Array(JsonObject parent, string key) =>
            parent.TryGet(key, out object value) && value is List<object> items ? items : System.Array.Empty<object>();

        private static bool? NullableBool(JsonObject parent, string key) =>
            parent.TryGet(key, out object value) && value is bool result ? result : (bool?)null;
    }
}
