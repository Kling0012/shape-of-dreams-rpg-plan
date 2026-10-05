using System.Collections.Generic;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        private static void WriteContinueState(JsonObject body, Profile profile)
        {
            var checkpoints = new List<object>();
            int first = System.Math.Max(0, profile.ContinueCheckpoints.Count - RunCheckpoint.MaximumHistory);
            for (int i = first; i < profile.ContinueCheckpoints.Count; i++)
            {
                var checkpoint = profile.ContinueCheckpoints[i];
                checkpoints.Add(new JsonObject().Add("id", checkpoint.Id).Add("runId", checkpoint.RunId)
                    .Add("snapshot", checkpoint.Snapshot));
            }
            body.Add("continueCheckpoints", checkpoints).Add("continueLobbyBaseline", profile.ContinueLobbyBaseline)
                .Add("continueResumeSession", profile.ContinueResumeSession);
        }

        private static void ReadContinueState(JsonObject body, Profile profile, List<string> notes)
        {
            profile.ContinueLobbyBaseline = Str(body, "continueLobbyBaseline");
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
                profile.ContinueCheckpoints.Add(new RunCheckpoint(id, Str(entry, "runId"), Str(entry, "snapshot")));
                if (profile.ContinueCheckpoints.Count > RunCheckpoint.MaximumHistory)
                    profile.ContinueCheckpoints.RemoveAt(0);
            }
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
