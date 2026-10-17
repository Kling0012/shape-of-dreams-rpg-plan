using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnDoHeal))]
    internal static class NativeSupportHeal
    {
        internal static EventInfoHeal Current;
        internal static bool Active;
        private static void Prefix(EventInfoHeal info)
        {
            var host = HostAuthority.NativeInstance;
            if (!NetworkServer.active || host == null || NativeSupportHealScope.Depth == 0) return;
            Current = info;
            Active = true;
            host.OnNativeSupportHeal(info);
        }
    }

    // Keep the handled marker through both sender and receiver callbacks, including nested heals.
    [HarmonyPatch(typeof(Actor), nameof(Actor.DoHeal))]
    internal static class NativeSupportHealScope
    {
        internal static int Depth;
        internal struct State { internal EventInfoHeal Heal; internal bool Active; internal int Depth; }
        private static void Prefix(out State __state)
        {
            __state = new State { Heal = NativeSupportHeal.Current, Active = NativeSupportHeal.Active, Depth = Depth };
            Depth++;
            NativeSupportHeal.Active = false;
        }
        private static Exception Finalizer(Exception __exception, State __state)
        {
            NativeSupportHeal.Current = __state.Heal;
            NativeSupportHeal.Active = __state.Active;
            Depth = __state.Depth;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnGiveShield))]
    internal static class NativeSupportShield
    {
        private static void Prefix(EventInfoShield info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnNativeSupportShield(info);
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class SupportOverhealWard
        {
            internal Se_GenericShield_OneShot Handle;
            internal ShieldEffect Effect;
            internal float Until;
            internal bool Live => Handle != null && Handle.isActive && Effect != null
                && ReferenceEquals(Handle.shield, Effect) && Effect.amount > 0f;
        }

        // A single pool per recipient, shared by all healers and separate from other wards.
        private readonly Dictionary<Hero, SupportOverhealWard> _supportOverhealWards = new Dictionary<Hero, SupportOverhealWard>();
        private readonly List<Hero> _supportOverhealScratch = new List<Hero>();
        private int _supportOverhealDepth;

        private void DispatchSupportHealOwned(HeroRuntime owner, Entity target, HealData heal)
        {
            var support = _am != null ? _am.serverActor : null;
            if (support == null) return;
            var previous = _isolatedSupportHealer;
            _isolatedSupportHealer = owner.Hero;
            try { heal.Dispatch(target); }
            finally { _isolatedSupportHealer = previous; }
        }

        // Called exactly once before Actor walks its ancestors and before receiver callbacks.
        internal void OnNativeSupportHeal(EventInfoHeal info)
        {
            if (!NetworkServer.active) return;
            Hero healer = null;
            try
            {
                bool isolated = _isolatedSupportHealer != null && _am != null && info.actor == _am.serverActor;
                healer = isolated ? _isolatedSupportHealer : KillAssistOwner(info.actor);
                if (isolated)
                {
                    // Nested native heals cannot inherit this dispatch's explicit source.
                    _isolatedSupportHealer = null;
                    RecordKillAssistSupport(healer, info.target, info.amount);
                    if (_runtimes.TryGetValue(healer, out var source)
                        && SupportBountyCredit.AllyHeal(info.amount, info.target is Hero && info.target != healer
                            && info.target.GetRelation(healer) == EntityRelation.Ally))
                        QueueBountyReport(source, BountyReportKind.AllyHealed, 1);
                }
                if (healer != null)
                {
                    if (_runtimes.TryGetValue(healer, out var owner)) OnRunGrowthSupport(owner, info.target, info.amount, false);
                }
            }
            catch (Exception ex) { Log.Warn("Host: support heal credit skipped: " + ex.Message); }
            try { ApplyOverhealProtection(info, healer); }
            catch (Exception ex) { Log.Warn("Host: support overheal skipped: " + ex.Message); }
        }

        private void ApplyOverhealProtection(EventInfoHeal info, Hero healer)
        {
            if (!(info.target is Hero recipient) || !Alive(recipient)
                || !(info.discardedAmount > 0f) || float.IsInfinity(info.discardedAmount)) return;
            float legacy = 0f;
            try
            {
                if (_runtimes.TryGetValue(recipient, out var receiver))
                    legacy = receiver.Powers.TakeOverflowingLife(info.discardedAmount, recipient.maxHealth);
                bool allyHeal = healer != null && healer != recipient && recipient.GetRelation(healer) == EntityRelation.Ally;
                if (allyHeal && TrySupportOverheal(healer, recipient, info.discardedAmount, legacy)) return;
            }
            catch (Exception ex) { Log.Warn("Host: support overheal skipped: " + ex.Message); }
            // Preserve the receiver's original Power if conversion is ineligible or fails.
            if (!(legacy > 0f)) return;
            try
            {
                _supportOverhealDepth++;
                // Granting from the recipient ran its shield power on top of the 10% max-HP cap.
                // Grant from the server actor like the support conversion, so the cap holds.
                var giver = _am != null ? _am.serverActor : null;
                if (giver == null) return;
                var ward = giver.GiveShield(recipient, legacy, PowerRuntime.OverflowingLifeDuration);
                // The isolated grant also skips the recipient's own give-shield event, so the
                // shield-granted record is credited here like every other server-actor ward.
                if (ward != null && _runtimes.TryGetValue(recipient, out var receiver))
                    CreditShieldGranted(receiver, recipient, ward.shield != null ? ward.shield.amount : 0f);
                LogPowerTrigger(Power.OverflowingLife);
            }
            catch (Exception ex) { Log.Warn("Host: OverflowingLife skipped: " + ex.Message); }
            finally { _supportOverhealDepth--; }
        }

        private bool TrySupportOverheal(Hero healer, Hero recipient, float discarded, float legacy)
        {
            var root = _am != null ? _am.serverActor : null;
            if (root == null || !(recipient.maxHealth > 0f) || float.IsInfinity(recipient.maxHealth)) return false;
            float now = Time.time;
            _supportOverhealWards.TryGetValue(recipient, out var ward);
            if (ward != null && (!ward.Live || ward.Until <= now))
            {
                ReleaseSupportOverheal(recipient);
                ward = null;
            }
            float cap = recipient.maxHealth * StarRankBalance.SupportOverhealShieldMaxHealthRatio;
            float before = ward != null ? Math.Min(cap, ward.Effect.amount) : 0f;
            if (ward != null) ward.Effect.amount = before;
            float converted = Math.Min(discarded * StarRankBalance.SupportOverhealShieldRatio, cap);
            if (legacy > converted) return false;
            float amount = Math.Min(converted, Math.Max(0f, cap - before));
            float duration = StarRankBalance.SupportOverhealShieldSeconds;
            _supportOverhealDepth++;
            _powerSupportDepth++;
            try
            {
                float granted;
                if (ward != null)
                {
                    // Respect receiver blockers, but never amplify past the conversion ratio or pool cap.
                    granted = Math.Min(amount, ward.Handle.ProcessShieldAmount(amount, recipient));
                    if (!(granted >= 0f) || float.IsInfinity(granted)) return false;
                    if (!ward.Live) return false;
                    ward.Effect.amount = before + granted;
                    ward.Handle.SetTimer(duration);
                    ward.Until = now + duration;
                }
                else
                {
                    if (!(amount > 0f)) return false;
                    var adapter = new NativeModShieldAdapter(root, recipient);
                    var handle = adapter.CreateRaw(amount, duration, amount);
                    if (handle == null || !adapter.IsAlive(handle)) return false;
                    ward = new SupportOverhealWard { Handle = handle, Effect = handle.shield, Until = now + duration };
                    _supportOverhealWards[recipient] = ward;
                    granted = ward.Effect.amount;
                }
                if (granted > 0f)
                {
                    try
                    {
                        if (_runtimes.TryGetValue(healer, out var owner)) CreditShieldGranted(owner, recipient, granted);
                        else RecordKillAssistSupport(healer, recipient, granted);
                    }
                    catch (Exception ex) { Log.Warn("Host: overheal support credit skipped: " + ex.Message); }
                }
                return true;
            }
            finally { _powerSupportDepth--; _supportOverhealDepth--; }
        }

        private void ReleaseSupportOverheal(Hero recipient)
        {
            if (!_supportOverhealWards.TryGetValue(recipient, out var ward)) return;
            _supportOverhealWards.Remove(recipient);
            try { if (ward.Live) ward.Handle.DestroyIfActive(); }
            catch (Exception ex) { Log.Warn("Host: release support overheal skipped: " + ex.Message); }
        }

        private void UpdateSupportOverheal(float now)
        {
            if (_supportOverhealWards.Count == 0) return;
            _supportOverhealScratch.Clear();
            foreach (var pair in _supportOverhealWards)
            {
                try
                {
                    if (!Alive(pair.Key) || !pair.Value.Live || pair.Value.Until <= now)
                    { _supportOverhealScratch.Add(pair.Key); continue; }
                    pair.Value.Effect.amount = Math.Min(pair.Value.Effect.amount,
                        pair.Key.maxHealth * StarRankBalance.SupportOverhealShieldMaxHealthRatio);
                }
                catch (Exception ex)
                {
                    _supportOverhealScratch.Add(pair.Key);
                    Log.Warn("Host: support overheal maintenance skipped: " + ex.Message);
                }
            }
            foreach (var hero in _supportOverhealScratch) ReleaseSupportOverheal(hero);
        }

        private void ClearSupportOverheal()
        {
            foreach (var pair in _supportOverhealWards)
            {
                try { if (pair.Value.Live) pair.Value.Handle.DestroyIfActive(); }
                catch (Exception ex) { Log.Warn("Host: clear support overheal skipped: " + ex.Message); }
            }
            _supportOverhealWards.Clear();
            _supportOverhealScratch.Clear();
        }
    }
}
