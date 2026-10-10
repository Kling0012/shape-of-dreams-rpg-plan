using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class Rules
    {
        public const int MaxDirectStashUsedRunIds = 256;

        /// <summary>
        /// 鞄の遺物を保管庫へ直接送れるか。1遠征（RunId）につき1回で、対象が0個なら使えない
        /// （回数も消費しない）。除外品は <see cref="IsSatchelRelicReserved"/>（中断遺物と同じ条件）。
        /// </summary>
        public static bool CanStashSatchelNow(Profile p, ISet<string> reservedUids = null)
        {
            var run = p?.Run;
            if (run == null || string.IsNullOrEmpty(run.RunId) || p.DirectStashUsedRunIds.Contains(run.RunId)) return false;
            for (int i = 0; i < run.Satchel.Count; i++)
                if (!IsSatchelRelicReserved(p, run.Satchel[i], reservedUids)) return true;
            return false;
        }

        // Retained native Continue points can outlive the bounded live receipt history.
        // Store an evicted run's own consumed right in its immutable snapshot before eviction.
        private static void PreserveAndPruneDirectStashReceipts(Profile p, string activeRunId)
        {
            var receipts = new HashSet<string>(p.DirectStashUsedRunIds, StringComparer.Ordinal);
            var decoded = new Dictionary<RunCheckpoint, Profile>();
            foreach (var checkpoint in p.ContinueCheckpoints.Where(c => c.RunId == activeRunId))
            {
                var snapshot = ReadDirectStashCheckpoint(checkpoint);
                decoded[checkpoint] = snapshot;
                if (snapshot.DirectStashUsedRunIds.Contains(activeRunId)) receipts.Add(activeRunId);
            }
            var expired = new HashSet<string>(receipts.OrderBy(id => id, StringComparer.Ordinal)
                .Where(id => id != activeRunId)
                .Take(Math.Max(0, receipts.Count - MaxDirectStashUsedRunIds)), StringComparer.Ordinal);
            var replacements = new List<RunCheckpoint>(p.ContinueCheckpoints.Count);
            foreach (var checkpoint in p.ContinueCheckpoints)
            {
                if (!expired.Contains(checkpoint.RunId))
                {
                    replacements.Add(checkpoint);
                    continue;
                }
                if (!decoded.TryGetValue(checkpoint, out var snapshot))
                    snapshot = ReadDirectStashCheckpoint(checkpoint);
                snapshot.DirectStashUsedRunIds.Add(checkpoint.RunId);
                // A snapshot only needs to guarantee its own expedition's consumed right.
                foreach (var id in snapshot.DirectStashUsedRunIds.OrderBy(id => id, StringComparer.Ordinal)
                    .Where(id => id != checkpoint.RunId)
                    .Take(Math.Max(0, snapshot.DirectStashUsedRunIds.Count - MaxDirectStashUsedRunIds)).ToList())
                    snapshot.DirectStashUsedRunIds.Remove(id);
                replacements.Add(new RunCheckpoint(checkpoint.Id, checkpoint.RunId,
                    WriteDirectStashCheckpoint(checkpoint, snapshot.DirectStashUsedRunIds)));
            }
            // Decode, validate and encode every candidate before changing the live profile.
            receipts.ExceptWith(expired);
            p.ContinueCheckpoints.Clear();
            p.ContinueCheckpoints.AddRange(replacements);
            p.DirectStashUsedRunIds.Clear();
            p.DirectStashUsedRunIds.UnionWith(receipts);
        }

        private static Profile ReadDirectStashCheckpoint(RunCheckpoint checkpoint)
        {
            // The normal reader validates format/version and accepts supported migration notes.
            // Its migrated values are never written back into this frozen snapshot.
            var snapshot = ProfileCodec.ReadCheckpointProfile(checkpoint.Snapshot);
            if (snapshot.Run?.RunId != checkpoint.RunId)
                throw new LedgerFormatException("Direct-stash continue checkpoint is invalid.");
            return snapshot;
        }

        private static string WriteDirectStashCheckpoint(RunCheckpoint checkpoint, ISet<string> receipts)
        {
            var root = (JsonObject)Json.Parse(checkpoint.Snapshot);
            root.TryGet("body", out var bodyValue);
            var body = new JsonObject();
            foreach (var property in ((JsonObject)bodyValue).Properties)
                if (property.Key != "directStashUsedRunIds") body.Add(property.Key, property.Value);
            body.Add("directStashUsedRunIds", receipts.OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => (object)id).ToList());
            var replacement = new JsonObject();
            foreach (var property in root.Properties)
                replacement.Add(property.Key, property.Key == "body" ? body : property.Value);
            return Json.Write(replacement);
        }

        /// <summary>
        /// 鞄の未確保品を保管庫へ直接送る（救済用。確保画面が出ないときの保険）。確保ボーナス・確保数・
        /// 依頼・実績には数えず、遠征は終わらずそのまま続く。保管庫のあふれは確保と同じ扱い（欠片化）。
        /// </summary>
        public static List<GameEvent> StashSatchelNow(Profile p, ISet<string> reservedUids = null)
        {
            if (!CanStashSatchelNow(p, reservedUids))
                throw new InvalidOperationException(Loc.T("今は鞄の遺物を保管庫へ送れません。", "Satchel relics cannot be sent to the stash now."));
            var run = p.Run;
            var sent = new List<Relic>();
            for (int i = 0; i < run.Satchel.Count; i++)
            {
                var relic = run.Satchel[i];
                if (IsSatchelRelicReserved(p, relic, reservedUids)) continue;
                sent.Add(relic);
                run.Satchel.RemoveAt(i--);
            }
            var overflow = new List<Relic>();
            FillStash(p, sent, overflow);
            int shards = 0;
            foreach (var r in overflow) shards = SaturatingAdd(shards, Content.SalvageShards(r.Rarity));
            if (shards > 0) p.AddMaterial(Materials.Shard, shards);
            p.DirectStashUsedRunIds.Add(run.RunId);
            var ev = new List<GameEvent> { new GameEvent(EventKind.Recovered, Loc.T(
                $"鞄の遺物{sent.Count}個を保管庫へ送りました（この遠征ではもう使えません）。",
                $"Sent {sent.Count} satchel relic(s) to the stash (this cannot be used again this expedition).")) };
            if (overflow.Count > 0)
                ev.Add(new GameEvent(EventKind.Warning, Loc.T(
                    $"保管庫が一杯のため{overflow.Count}個を欠片にしました。",
                    $"Stash full: {overflow.Count} relic(s) were turned into shards.")));
            return ev;
        }
    }
}
