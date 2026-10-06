using System;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;

namespace SodRpg.Mod
{
    // Explicit readers/writers avoid depending on Mirror's build-time weaver for MOD message types.
    public struct DreamforgeCoopTradeUp : NetworkMessage { public string Json; }
    public struct DreamforgeCoopTradeDown : NetworkMessage { public string Json; }

    internal static class CoopTradeTransport
    {
        internal const string CapabilityKey = "Dreamforge.CoopTrade.v1";
        // Profiles include stash/Continue economic receipts, not just six equipped relics.
        // Bound one on-demand transfer per peer; no buffers exist while trade is idle.
        internal const int MaxProfileChars = 4 * 1024 * 1024;
        private static bool _initialized;
        internal static void Initialize()
        {
            if (_initialized) return;
            Writer<DreamforgeCoopTradeUp>.write = (writer, message) => writer.WriteString(message.Json);
            Reader<DreamforgeCoopTradeUp>.read = reader => new DreamforgeCoopTradeUp { Json = reader.ReadString() };
            Writer<DreamforgeCoopTradeDown>.write = (writer, message) => writer.WriteString(message.Json);
            Reader<DreamforgeCoopTradeDown>.read = reader => new DreamforgeCoopTradeDown { Json = reader.ReadString() };
            _initialized = true;
        }
        internal static string Encode(object message)
        {
            string json = JsonConvert.SerializeObject(message);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > BuildStringLimit)
                throw new InvalidOperationException("Trade message exceeds native string capacity.");
            return json;
        }
        private const int BuildStringLimit = 60000;
        internal static T Decode<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json) || json.Length > BuildStringLimit) return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
