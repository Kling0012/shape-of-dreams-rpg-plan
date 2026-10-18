using HarmonyLib;

namespace SodRpg.Mod
{
    // Native shortcuts use InputManager; IMGUI reads the keyboard independently.
    [HarmonyPatch(typeof(InputManager), "GetKeyPressedCached")]
    internal static class BlockNativeKeysWhileTextEditing
    {
        private static bool Prepare()
        {
            bool available = AccessTools.DeclaredMethod(typeof(InputManager), "GetKeyPressedCached") != null;
            if (!available) Log.Warn("MOD text-input key blocking disabled: InputManager.GetKeyPressedCached unavailable.");
            return available;
        }

        private static bool Prefix(ref bool __result)
        {
            if (!ImeInput.Editing) return true;
            __result = false;
            return false;
        }
    }
}
