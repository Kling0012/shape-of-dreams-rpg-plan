using System;
using HarmonyLib;

namespace SodRpg.Mod
{
    /// <summary>
    /// Reflection lookups into the game that return null instead of throwing. These live in static
    /// fields, so a single renamed member would otherwise break the whole type initializer (and with it
    /// the host) after a game update. Callers treat null as "this feature is unavailable".
    /// </summary>
    internal static class SafeReflection
    {
        internal static AccessTools.FieldRef<T, F> FieldRef<T, F>(string field) where T : class
        {
            try { return AccessTools.FieldRefAccess<T, F>(field); }
            catch (Exception ex)
            {
                Log.Warn($"Native field {typeof(T).Name}.{field} unavailable; the dependent feature is disabled: {ex.Message}");
                return null;
            }
        }

        internal static Action<T, V> Setter<T, V>(string property)
        {
            try
            {
                var setter = AccessTools.PropertySetter(typeof(T), property);
                if (setter == null) throw new MissingMethodException(typeof(T).FullName, "set_" + property);
                return (Action<T, V>)Delegate.CreateDelegate(typeof(Action<T, V>), setter);
            }
            catch (Exception ex)
            {
                Log.Warn($"Native setter {typeof(T).Name}.{property} unavailable; the dependent feature is disabled: {ex.Message}");
                return null;
            }
        }
    }
}
