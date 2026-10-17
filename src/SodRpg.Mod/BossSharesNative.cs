using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Monster), nameof(Monster.OnStartServer))]
    internal static class BossShareNativeHealth
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var add = AccessTools.Method(typeof(EntityStatus), nameof(EntityStatus.AddStatBonus));
            int count = 0;
            foreach (var instruction in result) if (instruction.Calls(add)) count++;
            if (count != 1)
            {
                Log.Warn("Boss solo HP unavailable: native multiplayer bonus call changed; room HP retained.");
                return result;
            }
            var capture = AccessTools.Method(typeof(BossShareNativeHealth), nameof(AddBonus));
            foreach (var instruction in result)
                if (instruction.Calls(add)) { instruction.opcode = OpCodes.Call; instruction.operand = capture; }
            return result;
        }

        private static StatBonus AddBonus(EntityStatus status, StatBonus bonus)
        {
            var result = status.AddStatBonus(bonus);
            if (NetworkServer.active && status.entity is Monster monster)
            {
                bool active = HostAuthority.NativeInstance?.RecordBossNativeHealth(monster, result) == true;
                if (active)
                {
                    // Entity.OnPrepare filled HP before Monster.OnStartServer added this bonus.
                    // Finish the first recalculation before shares or a damage dispatch can see it.
                    try { status.CalculateStatsIfDirty(); }
                    catch (Exception ex) { Log.Warn("Boss initial HP recalculation deferred: " + ex.Message); }
                }
            }
            return result;
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.DealDamage))]
    internal static class BossShareDamageScope
    {
        internal sealed class Scope
        {
            internal Entity Victim;
            internal DewPlayer Player;
            internal readonly double[] Planned = new double[4];
            internal double PlannedDamage, RawBudget;
            internal bool Ready;
            internal float DamageCorrection;
        }

        internal struct Dispatch
        {
            internal Scope Previous, Rented;
        }

        internal static Scope Current;
        internal static bool NotificationSupported { get; private set; }
        private static readonly List<Scope> Scopes = new List<Scope>();
        private static int _depth;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var field = AccessTools.Field(typeof(EventInfoDamage), nameof(EventInfoDamage.damage));
            int count = 0;
            foreach (var instruction in result)
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, field)) count++;
            if (count != 1)
            {
                Log.Warn("Boss shares unavailable: native damage notification changed; room HP retained.");
                return result;
            }
            var setter = AccessTools.Method(typeof(BossShareDamageScope), nameof(SetNotificationDamage));
            foreach (var instruction in result)
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, field))
                { instruction.opcode = OpCodes.Call; instruction.operand = setter; }
            NotificationSupported = true;
            return result;
        }

        private static void SetNotificationDamage(ref EventInfoDamage info, FinalDamageData damage)
        {
            var scope = Current;
            if (scope != null && scope.Victim == info.victim && scope.DamageCorrection > 0)
            {
                float correction = UnityEngine.Mathf.Min(damage.amount, scope.DamageCorrection);
                damage.amount -= correction;
                damage.discardedAmount += correction;
            }
            info.damage = damage;
        }

        private static void Prefix(Entity target, out Dispatch __state)
        {
            __state = new Dispatch { Previous = Current };
            Current = null;
            if (!NetworkServer.active || HostAuthority.NativeInstance?.HasBossShares(target) != true) return;
            if (_depth == Scopes.Count) Scopes.Add(new Scope());
            var scope = Scopes[_depth++];
            scope.Victim = target;
            scope.Ready = false;
            scope.Player = null;
            scope.PlannedDamage = scope.RawBudget = 0;
            scope.DamageCorrection = 0;
            __state.Rented = Current = scope;
        }

        private static void Finalizer(Dispatch __state)
        {
            if (__state.Rented != null)
            {
                __state.Rented.Victim = null;
                __state.Rented.Player = null;
                __state.Rented.Ready = false;
                _depth--;
            }
            Current = __state.Previous;
        }
    }
}
