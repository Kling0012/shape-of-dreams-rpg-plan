using HarmonyLib;

namespace SodRpg.Mod
{
    /// <summary>Dreamforge のメニューを開いている間は、キャラ操作（クリック移動・攻撃・スキル）を止める。</summary>
    [HarmonyPatch(typeof(ControlManager), "GetShouldProcessCharacterInputAllowKnockedOut")]
    internal static class BlockInputWhileMenuOpen
    {
        public static bool MenuOpen;

        private static void Postfix(ref bool __result)
        {
            if (MenuOpen) __result = false;
        }
    }
}
