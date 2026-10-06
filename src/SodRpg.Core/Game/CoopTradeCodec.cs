using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static class CoopTradeCodec
    {
        public static string WriteOffer(CoopTradeOffer offer) => Json.Write(OfferObject(offer));
        public static CoopTradeOffer ReadOffer(string text) => ParseOffer(Object(Json.Parse(text)));
        public static string WriteReceipt(CoopTradeReceipt receipt) => Json.Write(ReceiptObject(receipt));
        public static CoopTradeReceipt ReadReceipt(string text) => ParseReceipt(Object(Json.Parse(text)));

        internal static JsonObject OfferObject(CoopTradeOffer offer)
        {
            if (offer == null || offer.RelicUids == null) throw new InvalidOperationException(Loc.T("取引の提示がありません。", "Trade offer is missing."));
            return new JsonObject().Add("relicUids", offer.RelicUids.Select(uid => (object)uid).ToList())
                .Add("shards", (long)offer.Shards).Add("tuning", (long)offer.Tuning);
        }

        internal static CoopTradeOffer ParseOffer(JsonObject obj)
        {
            var offer = new CoopTradeOffer { Shards = Amount(obj, "shards"), Tuning = Amount(obj, "tuning") };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object value in Array(obj, "relicUids"))
            {
                if (!(value is string uid) || string.IsNullOrEmpty(uid) || !seen.Add(uid))
                    throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Invalid cooperative trade relic IDs."));
                offer.RelicUids.Add(uid);
            }
            return offer;
        }

        internal static JsonObject ReceiptObject(CoopTradeReceipt receipt)
        {
            if (receipt == null) throw new InvalidOperationException(Loc.T("取引の受領書がありません。", "Trade receipt is missing."));
            return new JsonObject().Add("id", receipt.Id).Add("hostKey", receipt.HostKey)
                .Add("firstKey", receipt.FirstKey).Add("secondKey", receipt.SecondKey)
                .Add("firstOffer", OfferObject(receipt.FirstOffer)).Add("secondOffer", OfferObject(receipt.SecondOffer))
                .Add("firstProfile", receipt.FirstProfile).Add("secondProfile", receipt.SecondProfile);
        }

        internal static CoopTradeReceipt ParseReceipt(JsonObject obj)
        {
            var receipt = new CoopTradeReceipt
            {
                Id = String(obj, "id"), HostKey = String(obj, "hostKey"),
                FirstKey = String(obj, "firstKey"), SecondKey = String(obj, "secondKey"),
                FirstProfile = String(obj, "firstProfile"), SecondProfile = String(obj, "secondProfile"),
                FirstOffer = ParseOffer(Object(Value(obj, "firstOffer"))),
                SecondOffer = ParseOffer(Object(Value(obj, "secondOffer"))),
            };
            if (receipt.FirstKey == receipt.SecondKey) throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Cooperative trade participants must differ."));
            return receipt;
        }

        internal static object Value(JsonObject obj, string key)
        {
            if (!obj.TryGet(key, out var value)) throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Missing cooperative trade field: " + key));
            return value;
        }
        internal static JsonObject Object(object value) => value as JsonObject
            ?? throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Cooperative trade object is invalid."));
        internal static List<object> Array(JsonObject obj, string key) => Value(obj, key) as List<object>
            ?? throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Cooperative trade array is invalid: " + key));
        internal static string String(JsonObject obj, string key) => Value(obj, key) is string value && !string.IsNullOrEmpty(value)
            ? value : throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Cooperative trade string is invalid: " + key));
        internal static int Amount(JsonObject obj, string key) => Value(obj, key) is long value && value >= 0 && value <= int.MaxValue
            ? (int)value : throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Cooperative trade amount is invalid: " + key));
    }

    public static partial class ProfileCodec
    {
        private static object WriteCoopTradeReservation(CoopTradeReservation pending)
        {
            if (pending == null) return null;
            var relics = new List<object>();
            foreach (var reserved in pending.Relics)
                relics.Add(new JsonObject().Add("relic", WriteRelic(reserved.Relic))
                    .Add("target", (long)reserved.Target).Add("runId", reserved.RunId));
            return new JsonObject().Add("id", pending.Id).Add("hostKey", pending.HostKey)
                .Add("offer", CoopTradeCodec.OfferObject(pending.Offer)).Add("relics", relics)
                .Add("materialShards", (long)pending.MaterialShards).Add("materialTuning", (long)pending.MaterialTuning)
                .Add("currencyRunId", pending.CurrencyRunId);
        }

        private static void ReadCoopTradeState(JsonObject body, Profile profile)
        {
            if (body.TryGet("coopTradeEconomy", out object economy) && economy != null)
            {
                if (!(economy is string snapshot) || string.IsNullOrEmpty(snapshot))
                    throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Invalid cooperative trade economic checkpoint."));
                profile.CoopTradeEconomy = snapshot;
            }
            if (body.TryGet("coopTradeExecuted", out object executed))
            {
                if (!(executed is List<object> ids)) throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Invalid cooperative trade executed IDs."));
                foreach (object value in ids)
                    if (!(value is string id) || string.IsNullOrEmpty(id) || !profile.CoopTradeExecuted.Add(id))
                        throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Duplicate or invalid cooperative trade executed ID."));
            }
            if (!body.TryGet("coopTradePending", out object valuePending) || valuePending == null) return;
            var obj = CoopTradeCodec.Object(valuePending);
            var pending = new CoopTradeReservation
            {
                Id = CoopTradeCodec.String(obj, "id"), HostKey = CoopTradeCodec.String(obj, "hostKey"),
                Offer = CoopTradeCodec.ParseOffer(CoopTradeCodec.Object(CoopTradeCodec.Value(obj, "offer"))),
                MaterialShards = CoopTradeCodec.Amount(obj, "materialShards"),
                MaterialTuning = CoopTradeCodec.Amount(obj, "materialTuning"),
                CurrencyRunId = CoopTradeCodec.Value(obj, "currencyRunId") as string,
            };
            object currencyRun = CoopTradeCodec.Value(obj, "currencyRunId");
            if (pending.MaterialShards > pending.Offer.Shards || pending.MaterialTuning > pending.Offer.Tuning
                || currencyRun != null && (!(currencyRun is string currencyRunId) || string.IsNullOrEmpty(currencyRunId))
                || (pending.MaterialShards < pending.Offer.Shards || pending.MaterialTuning < pending.Offer.Tuning)
                    && string.IsNullOrEmpty(pending.CurrencyRunId))
                throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Invalid cooperative trade currency escrow."));
            if (profile.CoopTradeExecuted.Contains(pending.Id)) throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Executed cooperative trade still has escrow."));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object value in CoopTradeCodec.Array(obj, "relics"))
            {
                var entry = CoopTradeCodec.Object(value);
                var relicObject = CoopTradeCodec.Object(CoopTradeCodec.Value(entry, "relic"));
                var relic = ReadRelic(relicObject, seen);
                // Escrow may never silently normalize/drop unknown assets, unlike ordinary migration inventory.
                if (Json.Write(WriteRelic(relic)) != Json.Write(relicObject))
                    throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Reserved cooperative trade relic is not canonical."));
                int target = CoopTradeCodec.Amount(entry, "target");
                if (target != (int)CoopTradeReturnTarget.Stash && target != (int)CoopTradeReturnTarget.Satchel)
                    throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Invalid cooperative trade relic return target."));
                object runId = CoopTradeCodec.Value(entry, "runId");
                if (target == (int)CoopTradeReturnTarget.Satchel && (!(runId is string run) || string.IsNullOrEmpty(run))
                    || target == (int)CoopTradeReturnTarget.Stash && runId != null)
                    throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Invalid reserved cooperative trade expedition."));
                pending.Relics.Add(new CoopTradeReservedRelic { Relic = relic, Target = (CoopTradeReturnTarget)target, RunId = runId as string });
            }
            if (!seen.SetEquals(pending.Offer.RelicUids)) throw new LedgerFormatException(Loc.T("協力取引の保存情報が不正です。", "Cooperative trade escrow does not match its offer."));
            profile.CoopTradePending = pending;
        }
    }
}
