using System;
using System.Collections.Generic;
using System.Globalization;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public sealed partial class TradeAuthority
    {
        /// <summary>Native save companion: decisions and tombstones at the same point as native currency.</summary>
        public string CaptureCheckpoint()
        {
            var players = new List<object>();
            foreach (var pair in _players)
            {
                var ledger = pair.Value;
                var executed = new List<object>();
                foreach (long token in ledger.Order)
                {
                    var decision = ledger.Executed[token];
                    executed.Add(new JsonObject().Add("token", token).Add("fingerprint", ledger.Fingerprints[token])
                        .Add("ok", decision.Ok).Add("replayed", decision.Replayed).Add("reason", decision.Reason)
                        .Add("gold", (long)decision.SpendGold).Add("dust", (long)decision.SpendDust)
                        .Add("earn", (long)decision.EarnDust).Add("ledger", decision.LedgerId));
                }
                var floors = new List<object>();
                foreach (var floor in ledger.Floors)
                    floors.Add(new JsonObject().Add("generation", (long)floor.Key).Add("token", floor.Value));
                var cancelled = new List<object>();
                foreach (long token in ledger.CancelledOrder) cancelled.Add(token);
                var salvage = new List<object>();
                foreach (ulong uid in ledger.SalvageOrder) salvage.Add(uid.ToString(CultureInfo.InvariantCulture));
                players.Add(new JsonObject().Add("key", pair.Key).Add("id", ledger.Id).Add("runId", ledger.RunId)
                    .Add("floors", floors).Add("floorOverflow", ledger.FloorOverflow).Add("executed", executed)
                    .Add("cancelled", cancelled).Add("salvage", salvage).Add("overflowBonusPaid", ledger.OverflowBonusPaid)
                    .Add("overflowBonusBlocked", ledger.OverflowBonusBlocked)
                    .Add("overflowBonusPreviousRunId", ledger.PreviousOverflowBonusRunId)
                    .Add("overflowBonusPreviousPaid", ledger.PreviousOverflowBonusPaid));
            }
            return Json.Write(new JsonObject().Add("version", 1L).Add("generation", (long)Generation)
                .Add("serial", (long)_ledgerSerial).Add("players", players));
        }

        /// <summary>Atomically restores the authority; invalid data never leaves a partially restored ledger.</summary>
        public void RestoreCheckpoint(string snapshot)
        {
            if (!(Json.Parse(snapshot) is JsonObject root) || CheckpointLong(root, "version", 1, 1) != 1)
                throw new LedgerFormatException("Invalid trade authority checkpoint.");
            int generation = (int)CheckpointLong(root, "generation", 1, 0x3FFFFFFF);
            int serial = (int)CheckpointLong(root, "serial", 0, int.MaxValue);
            var players = new Dictionary<string, PlayerLedger>();
            var ledgerIds = new HashSet<long>();
            foreach (var value in CheckpointArray(root, "players", MaxTrackedPlayers))
            {
                var player = CheckpointObject(value);
                string key = CheckpointString(player, "key");
                long id = CheckpointLong(player, "id", 1, long.MaxValue);
                if ((id >> 32) != generation || (uint)id == 0 || (uint)id > (uint)serial
                    || players.ContainsKey(key) || !ledgerIds.Add(id)) throw new LedgerFormatException("Invalid trade ledger identity.");
                var ledger = new PlayerLedger
                {
                    Id = id,
                    RunId = player.TryGet("runId", out object run) ? run as string : null,
                    FloorOverflow = CheckpointBool(player, "floorOverflow"),
                    OverflowBonusPaid = player.TryGet("overflowBonusPaid", out _)
                        ? CheckpointLong(player, "overflowBonusPaid", 0, long.MaxValue) : 0,
                    OverflowBonusBlocked = player.TryGet("overflowBonusBlocked", out _) && CheckpointBool(player, "overflowBonusBlocked"),
                    PreviousOverflowBonusRunId = player.TryGet("overflowBonusPreviousRunId", out object previousRun) ? previousRun as string : null,
                    PreviousOverflowBonusPaid = player.TryGet("overflowBonusPreviousPaid", out _)
                        ? CheckpointLong(player, "overflowBonusPreviousPaid", 0, long.MaxValue) : 0,
                };
                if ((previousRun != null && !(previousRun is string))
                    || (ledger.PreviousOverflowBonusPaid > 0 && ledger.PreviousOverflowBonusRunId == null))
                    throw new LedgerFormatException("Invalid previous overflow bonus receipt.");
                foreach (var entry in CheckpointArray(player, "floors", MaxFloorGenerations))
                {
                    var floor = CheckpointObject(entry);
                    int clientGeneration = (int)CheckpointLong(floor, "generation", 0, int.MaxValue);
                    long token = CheckpointLong(floor, "token", 1, long.MaxValue);
                    if (ClientGeneration(token) != clientGeneration || ledger.Floors.ContainsKey(clientGeneration))
                        throw new LedgerFormatException("Invalid trade receipt floor.");
                    ledger.Floors.Add(clientGeneration, token);
                }
                foreach (var entry in CheckpointArray(player, "executed", MaxTokensPerPlayer))
                {
                    var executed = CheckpointObject(entry);
                    long token = CheckpointLong(executed, "token", 1, long.MaxValue);
                    bool ok = CheckpointBool(executed, "ok");
                    string fingerprint = CheckpointString(executed, "fingerprint");
                    string receiptReason = executed.TryGet("reason", out object reason) ? reason as string : null;
                    bool nativeFailure = fingerprint.StartsWith("o:", StringComparison.Ordinal) && receiptReason == "native";
                    bool manualFailure = (fingerprint.StartsWith("m:", StringComparison.Ordinal)
                        || fingerprint.StartsWith("d:", StringComparison.Ordinal) || fingerprint.StartsWith("s:", StringComparison.Ordinal))
                        && (receiptReason == "error" || receiptReason == TradeWire.LostReason);
                    if (ledger.Executed.ContainsKey(token)
                        || CheckpointLong(executed, "ledger", 1, long.MaxValue) != id
                        || (!ok && ((!nativeFailure && !manualFailure)
                            || CheckpointLong(executed, "gold", 0, int.MaxValue) != 0
                            || CheckpointLong(executed, "dust", 0, int.MaxValue) != 0
                            || CheckpointLong(executed, "earn", 0, int.MaxValue) != 0)))
                        throw new LedgerFormatException("Invalid executed trade receipt.");
                    ledger.Executed.Add(token, new TradeDecision
                    {
                        Ok = ok, Replayed = CheckpointBool(executed, "replayed"),
                        Reason = receiptReason,
                        SpendGold = (int)CheckpointLong(executed, "gold", 0, int.MaxValue),
                        SpendDust = (int)CheckpointLong(executed, "dust", 0, int.MaxValue),
                        EarnDust = (int)CheckpointLong(executed, "earn", 0, int.MaxValue), LedgerId = id,
                    });
                    ledger.Fingerprints.Add(token, fingerprint);
                    ledger.Order.Enqueue(token);
                }
                foreach (var entry in CheckpointArray(player, "cancelled", MaxTokensPerPlayer))
                {
                    if (!(entry is long token) || token <= 0 || ledger.Executed.ContainsKey(token) || !ledger.Cancelled.Add(token))
                        throw new LedgerFormatException("Invalid cancelled trade receipt.");
                    ledger.CancelledOrder.Enqueue(token);
                }
                foreach (var entry in CheckpointArray(player, "salvage", MaxSalvagedUidsPerRun))
                {
                    if (!(entry is string text) || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong uid)
                        || !ledger.SalvagedUids.Add(uid)) throw new LedgerFormatException("Invalid salvaged relic receipt.");
                    ledger.SalvageOrder.Enqueue(uid);
                }
                players.Add(key, ledger);
            }
            _players.Clear();
            foreach (var pair in players) _players.Add(pair.Key, pair.Value);
            Generation = generation;
            _ledgerSerial = serial;
        }

        private static JsonObject CheckpointObject(object value) => value as JsonObject
            ?? throw new LedgerFormatException("Trade checkpoint object is missing.");

        private static List<object> CheckpointArray(JsonObject parent, string key, int maximum)
        {
            if (!parent.TryGet(key, out object value) || !(value is List<object> items) || items.Count > maximum)
                throw new LedgerFormatException("Invalid trade checkpoint list: " + key);
            return items;
        }

        private static string CheckpointString(JsonObject parent, string key)
        {
            if (!parent.TryGet(key, out object value) || !(value is string text) || text.Length == 0)
                throw new LedgerFormatException("Invalid trade checkpoint string: " + key);
            return text;
        }

        private static long CheckpointLong(JsonObject parent, string key, long minimum, long maximum)
        {
            if (!parent.TryGet(key, out object value) || !(value is long number) || number < minimum || number > maximum)
                throw new LedgerFormatException("Invalid trade checkpoint number: " + key);
            return number;
        }

        private static bool CheckpointBool(JsonObject parent, string key)
        {
            if (!parent.TryGet(key, out object value) || !(value is bool flag))
                throw new LedgerFormatException("Invalid trade checkpoint flag: " + key);
            return flag;
        }
    }
}
