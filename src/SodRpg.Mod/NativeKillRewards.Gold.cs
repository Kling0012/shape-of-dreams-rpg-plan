using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SodRpg.Mod
{
    internal static partial class NativeKillRewards
    {
        private sealed class GoldRecipients
        {
            internal DewPlayer Killer;
            internal uint KillerLifetime;
            internal DewPlayer[] Assists;
            internal uint[] AssistLifetimes;
        }

        private sealed class GoldGrant
        {
            internal GoldRecipients Recipients;
            internal bool HasKillerAmount;
            internal int KillerAmount;
            internal int[] Pending;
            internal bool[] HasPending;
        }

        private static ConditionalWeakTable<object, GoldRecipients> _goldSources = new ConditionalWeakTable<object, GoldRecipients>();
        [ThreadStatic] private static GoldRecipients _goldDrop;
        [ThreadStatic] private static GoldGrant _goldGrant;

        internal static void ClearGold()
        {
            _goldSources = new ConditionalWeakTable<object, GoldRecipients>();
            _goldDrop = null;
            _goldGrant = null;
        }

        private static void InstallGold(Harmony harmony, List<KeyValuePair<MethodInfo, MethodInfo>> patches)
        {
            ClearGold();
            Patch(AccessTools.DeclaredMethod(typeof(PickupManager), "HandleDeathDrop", new[] { typeof(EventInfoKill) }),
                nameof(BeforeGoldDeath), 0);
            Patch(AccessTools.DeclaredMethod(typeof(PickupManager), "HandleDeathDrop", new[] { typeof(EventInfoKill) }),
                nameof(AfterGoldDeath), 2);
            // DropGoldRoutine yields across frames. Tag the created iterator and restore its scope on every MoveNext.
            var factory = AccessTools.DeclaredMethod(typeof(PickupManager), "DropGoldRoutine");
            Patch(factory, nameof(AfterGoldRoutineCreated), 1);
            var stateMachine = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            if (stateMachine == null) throw new MissingMemberException("PickupManager.DropGoldRoutine iterator");
            var move = AccessTools.DeclaredMethod(stateMachine, "MoveNext");
            Patch(move, nameof(BeforeGoldMoveNext), 0);
            Patch(move, nameof(AfterGoldDeath), 2);
            Patch(AccessTools.DeclaredMethod(typeof(Actor), "PrepareAndSpawn"), nameof(BeforeGoldOrbSpawn), 0);
            var pickup = AccessTools.DeclaredMethod(typeof(Pickup_BaseGoldOrb), "OnPickup", new[] { typeof(Hero) });
            Patch(pickup, nameof(BeforeGoldPickup), 0);
            Patch(pickup, nameof(AfterGoldDeath), 2);
            var grant = AccessTools.DeclaredMethod(typeof(Pickup_BaseGoldOrb), nameof(Pickup_BaseGoldOrb.GrantGold),
                new[] { typeof(int), typeof(Hero), typeof(bool), typeof(bool) });
            Patch(grant, nameof(BeforeGoldGrant), 0);
            Patch(grant, nameof(AfterGoldGrant), 2);
            var earn = AccessTools.DeclaredMethod(typeof(DewPlayer), nameof(DewPlayer.EarnGold), new[] { typeof(int) });
            Patch(earn, nameof(BeforeEarnKillGold), 0);
            Patch(earn, nameof(AfterEarnKillGold), 2);

            void Patch(MethodInfo target, string name, int kind)
            {
                if (target == null) throw new MissingMethodException("Native kill gold: " + name);
                var method = AccessTools.DeclaredMethod(typeof(NativeKillRewards), name);
                patches.Add(new KeyValuePair<MethodInfo, MethodInfo>(target, method));
                var patch = new HarmonyMethod(method) { priority = Priority.Last };
                if (kind == 0) harmony.Patch(target, prefix: patch);
                else if (kind == 1) harmony.Patch(target, postfix: patch);
                else harmony.Patch(target, finalizer: patch);
            }
        }

        private static void BeforeGoldDeath(EventInfoKill obj, out GoldRecipients __state)
        {
            __state = _goldDrop;
            _goldDrop = null;
            if (!Active) return;
            try
            {
                var host = HostAuthority.NativeInstance;
                if (host == null) return;
                var heroes = host.NativeKillGoldRecipients(obj, out var killer);
                if (heroes == null || killer?.owner == null) return;
                var recipients = new GoldRecipients
                {
                    Killer = killer.owner, KillerLifetime = killer.owner.netId,
                    Assists = new DewPlayer[heroes.Length], AssistLifetimes = new uint[heroes.Length]
                };
                for (int i = 0; i < heroes.Length; i++)
                {
                    recipients.Assists[i] = heroes[i]?.owner;
                    recipients.AssistLifetimes[i] = heroes[i]?.owner?.netId ?? 0;
                }
                _goldDrop = recipients;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static Exception AfterGoldDeath(Exception __exception, GoldRecipients __state)
        {
            _goldDrop = __state;
            return __exception;
        }

        private static void AfterGoldRoutineCreated(object __result, bool isKillGold, bool isGivenByOtherPlayer, Hero target)
        {
            if (!Active || _goldDrop == null || __result == null || !isKillGold || isGivenByOtherPlayer || target != null) return;
            try { _goldSources.Add(__result, _goldDrop); }
            catch (Exception ex) { Fail(ex); }
        }

        private static void BeforeGoldMoveNext(object __instance, out GoldRecipients __state)
        {
            __state = _goldDrop;
            _goldDrop = null;
            if (!Active) return;
            try
            {
                if (_goldSources.TryGetValue(__instance, out var recipients))
                    _goldDrop = recipients;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void BeforeGoldOrbSpawn(Actor __instance)
        {
            if (!Active || !(__instance is Pickup_BaseGoldOrb orb)) return;
            try
            {
                // Pooled orbs must never retain the previous enemy's recipients.
                _goldSources.Remove(orb);
                if (_goldDrop != null && orb.isKillGold && !orb.isGivenByOtherPlayer && orb.target == null)
                    _goldSources.Add(orb, _goldDrop);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void BeforeGoldPickup(Pickup_BaseGoldOrb __instance, out GoldRecipients __state)
        {
            __state = _goldDrop;
            _goldDrop = null;
            if (!Active) return;
            try
            {
                if (_goldSources.TryGetValue(__instance, out var recipients))
                    _goldDrop = recipients;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void BeforeGoldGrant(bool isKillGold, bool isGivenByOtherPlayer, Hero target, out GoldGrant __state)
        {
            __state = _goldGrant;
            _goldGrant = null;
            if (!Active || _goldDrop == null || !isKillGold || isGivenByOtherPlayer || target != null) return;
            try
            {
                var recipients = _goldDrop;
                if (recipients.Killer == null || recipients.Killer.netId != recipients.KillerLifetime
                    || !DewPlayer.gamePlayers.Contains(recipients.Killer)) return;
                _goldGrant = new GoldGrant
                {
                    Recipients = recipients,
                    Pending = new int[recipients.Assists.Length], HasPending = new bool[recipients.Assists.Length]
                };
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static bool BeforeEarnKillGold(DewPlayer __instance, ref int amount, out GoldGrant __state)
        {
            var grant = __state = _goldGrant;
            // Nested earnings caused by native reward listeners are independent of this orb.
            _goldGrant = null;
            if (!Active || grant == null) return true;
            try
            {
                var recipients = grant.Recipients;
                if (__instance == recipients.Killer && __instance.netId == recipients.KillerLifetime)
                {
                    grant.HasKillerAmount = true;
                    grant.KillerAmount = amount;
                    return true;
                }
                for (int i = 0; i < recipients.Assists.Length; i++)
                {
                    if (__instance != recipients.Assists[i] || __instance.netId != recipients.AssistLifetimes[i]) continue;
                    if (grant.HasKillerAmount) { amount = grant.KillerAmount; return true; }
                    // Native iterates lobby order; defer earlier assists until the killer's rounded amount is known.
                    grant.Pending[i] = amount;
                    grant.HasPending[i] = true;
                    return false;
                }
                return true;
            }
            catch (Exception ex) { Fail(ex); return true; }
        }

        private static Exception AfterEarnKillGold(Exception __exception, GoldGrant __state)
        {
            _goldGrant = __state;
            return __exception;
        }

        private static Exception AfterGoldGrant(Exception __exception, GoldGrant __state)
        {
            var grant = _goldGrant;
            _goldGrant = __state;
            if (grant == null) return __exception;
            // Leave the outer native grant scope while flushing so callbacks cannot be mistaken for this payout.
            var outer = _goldGrant;
            _goldGrant = null;
            try
            {
                for (int i = 0; i < grant.HasPending.Length; i++)
                {
                    if (!grant.HasPending[i]) continue;
                    var player = grant.Recipients.Assists[i];
                    if (player == null || player.netId != grant.Recipients.AssistLifetimes[i]) continue;
                    try { player.EarnGold(Active && grant.HasKillerAmount ? grant.KillerAmount : grant.Pending[i]); }
                    catch (Exception ex) { Fail(ex); }
                }
            }
            catch (Exception ex) { Fail(ex); }
            finally { _goldGrant = outer; }
            return __exception;
        }
    }
}
