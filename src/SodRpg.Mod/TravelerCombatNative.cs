using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class TravelerCombatNative
    {
        // One line per (traveler, asset type) so the first in-game run shows how each traveler was classified.
        private static readonly HashSet<(string, Type)> Reported = new HashSet<(string, Type)>();

        internal static bool IsMelee(Hero hero)
        {
            if (hero == null) return false;
            string name = null;
            try
            {
                name = hero.GetType().Name;
                // Other travelers never qualify; skip reading their attack asset.
                if (!TravelerCombat.IsMelee(name, true)) return false;
                var attack = hero.Ability?.attackAbility;
                object asset = attack != null && attack.configs != null && attack.currentConfigIndex >= 0
                    && attack.currentConfigIndex < attack.configs.Length
                    ? attack.currentConfig?.spawnedInstanceRef.asset : null;
                bool melee = IsMeleeAsset(asset);
                Report(name, asset?.GetType(), melee);
                return melee;
            }
            catch (Exception ex)
            {
                Report(name ?? "?", ex.GetType(), false, ex.Message);
                return false;
            }
        }

        // The asset is expected to be the instance component, but also accept a prefab GameObject or a sibling component.
        private static bool IsMeleeAsset(object asset)
        {
            if (asset is MeleeAttackInstance) return true;
            if (asset is GameObject go) return go != null && go.GetComponent<MeleeAttackInstance>() != null;
            if (asset is Component component) return component != null && component.GetComponent<MeleeAttackInstance>() != null;
            return false;
        }

        private static void Report(string hero, Type asset, bool melee, string error = null)
        {
            if (!Reported.Add((hero, asset))) return;
            if (error != null) Log.Warn($"Melee check failed for {hero}: {error}");
            else Log.Info($"Melee check: {hero} basic attack asset {(asset == null ? "none" : asset.Name)} -> {(melee ? "melee" : "not melee")}");
        }
    }
}
