using System;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal static class TravelerCombatNative
    {
        internal static bool IsMelee(Hero hero)
        {
            if (hero == null) return false;
            try
            {
                var attack = hero.Ability?.attackAbility;
                bool melee = attack != null && attack.configs != null && attack.currentConfigIndex >= 0
                    && attack.currentConfigIndex < attack.configs.Length
                    && attack.currentConfig?.spawnedInstanceRef.asset is MeleeAttackInstance;
                return TravelerCombat.IsMelee(hero.GetType().Name, melee);
            }
            catch (Exception) { return false; }
        }
    }
}
