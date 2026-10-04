using HarmonyLib;

namespace SodRpg.Mod
{
    /// <summary>Enforce Siphon's per-heal limit after the native receiver's healing modifiers.</summary>
    [HarmonyPatch(typeof(Entity), nameof(Entity.ProcessReceivedHeal))]
    internal static class GimmickSiphonLimit
    {
        private static void Postfix(Entity __instance, ref HealData data)
        {
            if (!data.IsAmountModifiedBy(typeof(GimmickSiphonLimit)) || __instance == null) return;
            float amount = data.currentAmount;
            float cap = __instance.maxHealth * 0.015f;
            if (amount > cap && amount > 0) data.ApplyReduction(1f - cap / amount);
        }
    }
}
