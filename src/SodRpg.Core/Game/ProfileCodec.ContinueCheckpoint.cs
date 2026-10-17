using System.Collections.Generic;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        private static void WriteContinueState(JsonObject body, Profile profile, bool legacy = false)
        {
            var checkpoints = new List<object>();
            // Confirmed points are protected independently of the bounded pending attempts.
            for (int i = 0; i < profile.ContinueCheckpoints.Count; i++)
            {
                var checkpoint = profile.ContinueCheckpoints[i];
                var entry = new JsonObject().Add("id", checkpoint.Id).Add("runId", checkpoint.RunId)
                    .Add("snapshot", legacy ? checkpoint.Snapshot : checkpoint.SnapshotForStore(profile.CoopTradeEconomy));
                if (!legacy) entry.Add("committed", checkpoint.Committed);
                checkpoints.Add(entry);
            }
            body.Add("continueCheckpoints", checkpoints).Add("continueLobbyBaseline",
                legacy ? profile.ContinueLobbyBaseline : profile.SharedLobbyBaseline())
                .Add("continueResumeSession", profile.ContinueResumeSession);
        }

        private static void ReadContinueState(JsonObject body, Profile profile, List<string> notes)
        {
            string economy = Str(body, "coopTradeEconomy");
            profile.ContinueLobbyBaseline = ExpandCheckpointEconomy(Str(body, "continueLobbyBaseline"), economy);
            profile.ContinueResumeSession = Str(body, "continueResumeSession");
            foreach (var item in Array(body, "continueCheckpoints"))
            {
                if (!(item is JsonObject entry) || string.IsNullOrEmpty(Str(entry, "id"))
                    || string.IsNullOrEmpty(Str(entry, "runId")) || string.IsNullOrEmpty(Str(entry, "snapshot")))
                {
                    notes.Add("continueCheckpoints: invalid checkpoint ignored");
                    continue;
                }
                string id = Str(entry, "id");
                profile.ContinueCheckpoints.RemoveAll(checkpoint => checkpoint.Id == id);
                // Legacy entries have no confirmation flag. Protect them until an explicit native confirmation.
                if (!entry.TryGet("committed", out _)) profile.LegacyContinueEncoding = true;
                profile.ContinueCheckpoints.Add(new RunCheckpoint(id, Str(entry, "runId"),
                    ExpandCheckpointEconomy(Str(entry, "snapshot"), economy), Bool(entry, "committed", true)));
            }
        }

        internal static string ShareCheckpointEconomy(string snapshot, string economy)
        {
            if (string.IsNullOrEmpty(snapshot) || string.IsNullOrEmpty(economy)) return snapshot;
            var root = Json.Parse(snapshot) as JsonObject;
            if (root == null || !root.TryGet("body", out var value) || !(value is JsonObject body)
                || !body.TryGet("coopTradeEconomy", out var existing) || !Equals(existing, economy)) return snapshot;
            return RewriteCheckpointEconomy(root, body, economy, true);
        }

        private static string ExpandCheckpointEconomy(string snapshot, string economy)
        {
            if (string.IsNullOrEmpty(snapshot) || snapshot.IndexOf("\"coopTradeEconomyShared\"", System.StringComparison.Ordinal) < 0)
                return snapshot;
            var root = Json.Parse(snapshot) as JsonObject;
            if (root == null || !root.TryGet("body", out var value) || !(value is JsonObject body)
                || !Bool(body, "coopTradeEconomyShared", false)) return snapshot;
            if (string.IsNullOrEmpty(economy)) throw new LedgerFormatException("Shared checkpoint economy is missing.");
            return RewriteCheckpointEconomy(root, body, economy, false);
        }

        private static string RewriteCheckpointEconomy(JsonObject root, JsonObject body, string economy, bool share)
        {
            var replacementBody = new JsonObject();
            // Replace in place so canonical field order survives expansion.
            foreach (var property in body.Properties)
            {
                if (share && property.Key == "coopTradeEconomy")
                    replacementBody.Add("coopTradeEconomyShared", true);
                else if (!share && property.Key == "coopTradeEconomyShared")
                    replacementBody.Add("coopTradeEconomy", economy);
                else replacementBody.Add(property.Key, property.Value);
            }
            var replacement = new JsonObject();
            foreach (var property in root.Properties)
                replacement.Add(property.Key, property.Key == "body" ? replacementBody : property.Value);
            return Json.Write(replacement);
        }

        internal static string CheckpointRelicKey(Relic relic) => relic == null ? null : Json.Write(WriteRelic(relic));
        internal static string CheckpointRetuneKey(RetuneOffer offer) => Json.Write(WriteRetuneOffer(offer));

        internal static string CheckpointTradeKey(IEnumerable<PendingTrade> trades)
        {
            var values = new List<object>();
            foreach (var trade in trades) values.Add(WriteTrade(trade));
            return Json.Write(values);
        }

        internal static string CheckpointSalvageKey(IEnumerable<PendingSalvage> salvage)
        {
            var values = new List<object>();
            foreach (var pending in salvage)
                values.Add(WriteRelic(pending.Relic).Add("returnTarget", (long)pending.ReturnTarget));
            return Json.Write(values);
        }
    }
}
