using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    // Server-side RPC entry: this also works on a dedicated host and runs before
    // the old room actors are destroyed or new room actors can be prepared.
    [HarmonyPatch(typeof(ZoneManager), "InvokeOnRoomLoadStarted")]
    internal static class DynamicDifficultyRoomLoading
    {
        private static void Prefix()
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnDynamicRoomLoading();
        }
    }

    [HarmonyPatch(typeof(Actor), "InvokeOnCreateIfDidnt")]
    internal static class DynamicDifficultySpawned
    {
        private static void Postfix(Actor __instance)
        {
            if (NetworkServer.active && __instance is Entity entity)
                HostAuthority.NativeInstance?.CompleteDynamicSpawn(entity);
        }
    }
}
