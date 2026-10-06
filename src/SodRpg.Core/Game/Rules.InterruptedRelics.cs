using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    public static partial class Rules
    {
        private static ISet<string> InterruptedSalvageReservations(Profile p, ISet<string> supplied)
        {
            HashSet<string> combined = null;
            foreach (var trade in p.PendingTrades)
            {
                if (trade.Kind != TradeKind.SalvageForDust || string.IsNullOrEmpty(trade.Uid)
                    || (supplied != null && supplied.Contains(trade.Uid))) continue;
                if (combined == null) combined = supplied == null
                    ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(supplied, StringComparer.Ordinal);
                combined.Add(trade.Uid);
            }
            return combined ?? supplied;
        }

        private static bool? InterruptedRunTerminalOutcome(Profile p)
        {
            string id = p.Run.RunId;
            var recovery = p.RunRecovery;
            if (recovery?.PendingResultRunId == id && recovery.PendingVictory.HasValue)
                return recovery.PendingVictory.Value;
            if (recovery?.RunId == id && (recovery.PublisherVictory.HasValue || recovery.PublisherTerminalChoices != null))
                return recovery.PublisherVictory ?? false;
            if (p.LobbyReturnedRunIds.Contains(id)) return false;
            return null;
        }

        private static void CaptureInterruptedRelics(Profile p, ISet<string> reservedUids, List<GameEvent> events)
        {
            if (p.InterruptedRelics.Count > 0)
            {
                p.LostAndFound.AddRange(p.InterruptedRelics);
                p.InterruptedRelicsRetiredSourceRunIds.Add(p.InterruptedRelicsRunId);
                int salvaged = TrimLostAndFound(p);
                foreach (var relic in p.LostAndFound) relic.InfinityFreeSupply = false;
                if (salvaged > 0)
                    events.Add(new GameEvent(EventKind.Warning, Loc.T(
                        $"前の中断遺物を遺失物へ移しました。上限を超えた{salvaged}個を欠片にしました。",
                        $"Previous interrupted relics moved to Lost & Found; {salvaged} overflow relic(s) became shards.")));
            }
            ClearInterruptedRelics(p);
            if (p.InterruptedRelicsRetiredSourceRunIds.Contains(p.Run.RunId)) return;
            for (int i = 0; i < p.Run.Satchel.Count; i++)
            {
                var relic = p.Run.Satchel[i];
                if ((reservedUids != null && reservedUids.Contains(relic.Uid))
                    || p.PendingTrades.Any(trade => trade.Uid == relic.Uid || trade.Relic?.Uid == relic.Uid)
                    || p.PendingSalvage.Any(pending => pending.Relic.Uid == relic.Uid)
                    || (p.CoopTradePending != null && p.CoopTradePending.Relics.Any(escrow => escrow.Relic.Uid == relic.Uid))) continue;
                p.InterruptedRelics.Add(relic);
                p.Run.Satchel.RemoveAt(i--);
            }
            if (p.InterruptedRelics.Count == 0) return;
            p.InterruptedRelicsId = Guid.NewGuid().ToString("N");
            p.InterruptedRelicsRunId = p.Run.RunId;
            events.Add(new GameEvent(EventKind.Info, Loc.T(
                $"中断した遠征の遺物{p.InterruptedRelics.Count}個を受け取れます（現在の遠征で1回まで）。",
                $"You can claim {p.InterruptedRelics.Count} relic(s) from the interrupted expedition, once this expedition.")));
        }

        internal static void ClearInterruptedRelics(Profile p)
        {
            p.InterruptedRelics.Clear();
            p.InterruptedRelicsId = null;
            p.InterruptedRelicsRunId = null;
        }

        internal static void ResumeInterruptedSource(Profile p, bool tradeEconomyApplied)
        {
            if (!tradeEconomyApplied)
            {
                ClearInterruptedRelics(p);
                return;
            }
            // A trade in the receiving expedition may have replaced the source's economic snapshot.
            // Its pending originals remain owned; cancellation must return them, not erase them.
            var relics = p.InterruptedRelics.ToArray();
            ClearInterruptedRelics(p);
            var reserved = new HashSet<string>(p.PendingTrades.Select(trade => trade.Uid), StringComparer.Ordinal);
            foreach (var relic in relics)
                if (!p.ContainsRelicUid(relic.Uid)) AddToSatchelReserved(p, relic, null, false, reserved);
            // Persist this ownership change in the existing trade overlay, including repeated Continue.
            p.CoopTradeEconomy = null;
            p.CoopTradeEconomy = ProfileCodec.WriteCheckpointProfile(p);
        }

        public static bool CanClaimInterruptedRelics(Profile p) => p?.Run != null
            && !string.IsNullOrEmpty(p.Run.RunId)
            && p.InterruptedRelics.Count > 0
            && !string.IsNullOrEmpty(p.InterruptedRelicsId)
            && !string.IsNullOrEmpty(p.InterruptedRelicsRunId)
            && p.Run.RunId != p.InterruptedRelicsRunId
            && !p.InterruptedRelicsExecuted.Contains(p.InterruptedRelicsId)
            && !p.InterruptedRelicsClaimedRunIds.Contains(p.Run.RunId)
            && !p.InterruptedRelicsRetiredSourceRunIds.Contains(p.InterruptedRelicsRunId);

        /// <summary>Transfers the entire latest batch atomically. The caller must confirm an immediate durable save.</summary>
        public static List<GameEvent> ClaimInterruptedRelics(Profile p, ISet<string> reservedUids = null)
        {
            if (!CanClaimInterruptedRelics(p))
                throw new InvalidOperationException(Loc.T("この遠征では中断遺物を受け取れません。", "Interrupted relics cannot be claimed in this expedition."));
            var next = p.Clone();
            var reservations = InterruptedSalvageReservations(next, reservedUids);
            var relics = next.InterruptedRelics.ToArray();
            string receipt = next.InterruptedRelicsId;
            string sourceRun = next.InterruptedRelicsRunId;
            ClearInterruptedRelics(next);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relic in relics)
                if (relic == null || string.IsNullOrEmpty(relic.Uid) || !seen.Add(relic.Uid)
                    || next.ContainsRelicUid(relic.Uid) || next.PendingTrades.Any(trade => trade.Uid == relic.Uid || trade.Relic?.Uid == relic.Uid)
                    || (reservedUids != null && reservedUids.Contains(relic.Uid)))
                    throw new InvalidOperationException(Loc.T("中断遺物の個体IDが予約品または所持品と重複しています。", "Interrupted relic IDs conflict with reserved or owned assets."));
            foreach (var relic in relics) AddToSatchelReserved(next, relic, null, false, reservations);
            next.InterruptedRelicsExecuted.Add(receipt);
            next.InterruptedRelicsClaimedRunIds.Add(next.Run.RunId);
            next.InterruptedRelicsRetiredSourceRunIds.Add(sourceRun);
            // The same economic transfer used by trades publishes the fully validated clone.
            // Continue uses live economics only for the source and receiving expedition IDs.
            CoopTradeRules.CopyTradeState(p, next);
            return new List<GameEvent> { new GameEvent(EventKind.Recovered, Loc.T(
                $"中断した遠征の遺物{relics.Length}個を受け取りました（まだ持ち帰っていません）。",
                $"Claimed {relics.Length} relic(s) from the interrupted expedition (unsecured).")) };
        }
    }
}
