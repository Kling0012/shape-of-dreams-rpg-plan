using System;
using HarmonyLib;
using Newtonsoft.Json;

namespace SodRpg.Mod
{
    // Actor's typed RPC decoder silently discards failed JSON. Observe that failure
    // without retrying, changing the exception, or affecting other MODs' messages.
    [HarmonyPatch(typeof(DewPersistence), nameof(DewPersistence.FromJson),
        new[] { typeof(string), typeof(Type), typeof(JsonSerializerSettings) })]
    internal static class NetMessageDecodeWarning
    {
        private static void Finalizer(Type type, Exception __exception)
        {
            if (__exception != null && type != null && type.Assembly == typeof(Protocol).Assembly
                && type.Namespace == typeof(Protocol).Namespace && type.Name.StartsWith("Dreamforge", StringComparison.Ordinal))
                Log.Warn("RPC: discarded unreadable " + type.Name + ": " + __exception.Message);
        }
    }
}
