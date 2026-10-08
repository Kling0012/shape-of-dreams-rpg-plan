using System;
using System.Collections.Generic;
using System.Reflection;

// Minimal shared native API boundary. These are not the game implementations;
// tests invoke the real production callbacks, not Harmony detours or Unity code.
public class StackedStatusEffect
{
    public int maxStack, stack, RemoveCalls, LastRemoved;
    public void OnCreate() { }
    public void SetStack(int value) { stack = Math.Min(maxStack, Math.Max(0, value)); }
    public void AddStack(int value) { SetStack(stack + value); }
    public void RemoveStack(int value)
    {
        RemoveCalls++;
        LastRemoved = value;
        SetStack(stack - value);
    }
}
public class Se_Gem_L_Culinary_Stack : StackedStatusEffect { }
public class DerivedCulinaryStack : Se_Gem_L_Culinary_Stack { }
public class Status
{
    public StackedStatusEffect Effect;
    public object GetStatusEffect(Type type) => Effect;
}
public class Hero { public Status Status = new Status(); }
public class Shrine_CookingPot
{
    public int GetIngredientCount(Hero hero) => hero.Status.Effect.stack;
    public void ConsumeIngredients(Hero hero) { hero.Status.Effect.SetStack(0); }
}

namespace HarmonyLib
{
    public enum HarmonyPatchType { All }
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method) { } }
    public sealed class Harmony
    {
        public string Id { get; }
        public Harmony(string id) { Id = id; }
        public void Patch(MethodInfo method, HarmonyMethod prefix = null, HarmonyMethod postfix = null) { }
        public void Unpatch(MethodInfo method, HarmonyPatchType type, string owner) { }
    }
    public static class AccessTools
    {
        public static Type TypeByName(string name) => typeof(StackedStatusEffect).Assembly.GetType(name);
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] arguments = null)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            return arguments == null ? type.GetMethod(name, flags) :
                type.GetMethod(name, flags, null, arguments, null);
        }
    }
}
namespace SodRpg.Mod
{
    public static class Log
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Warn(string warning) { lock (Warnings) Warnings.Add(warning); }
    }
}
