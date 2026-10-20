using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// Bounds the on-hero pickup FX of dream dust and gold orbs (#889). Native in-place
    /// playback has no EffectAutoDestroy entry until the pickup's destroy queue finishes.
    /// If canDestroy never succeeds, that timeout never starts and attached FX can survive.
    /// Keep native playback and actor pooling, but independently stop/detach/hide this child
    /// at the native 30-second FX limit or room change. Restore it before its next playback.
    /// Contract failures disable only this patch and leave the native game path in place.
    /// </summary>
    [HarmonyPatch]
    internal static class PickupFxLifetimePatch
    {
        private static MethodInfo _play;
        private static bool _disabled;

        private static bool Prepare()
        {
            if (_disabled) return false;
            try
            {
                _play = AccessTools.DeclaredMethod(typeof(DewEffect), nameof(DewEffect.Play),
                    new[] { typeof(GameObject), typeof(Entity), typeof(Mirror.NetworkIdentity) });
                var parameters = _play?.GetParameters();
                var onHeroField = AccessTools.DeclaredField(typeof(PickupInstance), nameof(PickupInstance.pickupEffectOnHero));
                if (_play == null || !_play.IsStatic || _play.ReturnType != typeof(void)
                    || parameters.Length != 3 || parameters[0].ParameterType != typeof(GameObject)
                    || parameters[1].ParameterType != typeof(Entity)
                    || parameters[2].ParameterType != typeof(Mirror.NetworkIdentity)
                    || onHeroField == null || !onHeroField.IsPublic || onHeroField.FieldType != typeof(GameObject))
                    throw new MissingMethodException("pickup on-hero effect play contract changed");
                return true;
            }
            catch (Exception ex) { Disable(ex); return false; }
        }

        private static MethodBase TargetMethod() => _play;

        // Restore only a child hidden by this guard, before native attachment and playback.
        // This also runs after a guard failure so an already pooled instance stays usable.
        private static void Prefix(GameObject effect, Entity attach, Mirror.NetworkIdentity parent)
        {
            if (effect == null || attach == null || parent == null) return;
            try
            {
                if (FindPickup(effect, parent) == null) return;
                effect.GetComponent<PickupFxLifetime>()?.RestoreForPlayback();
            }
            catch (Exception ex) { Disable(ex); }
        }

        // HandleEffectMessage reaches this overload on both host and remote clients.
        private static void Postfix(GameObject effect, Entity attach, Mirror.NetworkIdentity parent)
        {
            if (_disabled || effect == null || attach == null || parent == null) return;
            try
            {
                var pickup = FindPickup(effect, parent);
                if (pickup == null) return;
                var guard = effect.GetComponent<PickupFxLifetime>();
                if (guard == null) guard = effect.AddComponent<PickupFxLifetime>();
                guard.Begin(pickup);
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static PickupInstance FindPickup(GameObject effect, Mirror.NetworkIdentity parent)
        {
            var pickup = parent.GetComponent<PickupInstance>();
            if (!(pickup is Pickup_DreamDust) && !(pickup is Pickup_BaseGoldOrb)) return null;
            // Never deactivate an Actor root or a pooled clone owned by another lifecycle.
            if (effect != pickup.pickupEffectOnHero || effect == pickup.gameObject
                || !effect.transform.IsChildOf(pickup.transform)) return null;
            return pickup;
        }

        internal static void Disable(Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            Log.Warn("Pickup FX lifetime patch disabled; other MOD features remain active: " + ex.Message);
        }
    }

    // One timer per on-hero pickup playback; no Update and no scene scan. The pickup actor
    // itself (pooled, reuseInRoom) is never disabled or destroyed by this guard.
    internal sealed class PickupFxLifetime : MonoBehaviour
    {
        private const float CheckInterval = 0.5f; // EffectAutoDestroy.Tick recheck rate
        private const float MaxLifetime = 30f;    // EffectAutoDestroy timeout

        private PickupInstance _source;
        private ZoneManager _zone;
        private uint _sourceNetId;
        private float _sourceCreated;
        private float _expires;
        private bool _tracking;
        private bool _hidden;
        private ParticleSystem[] _particles;

        internal void RestoreForPlayback()
        {
            Unsubscribe();
            if (!_hidden) return;
            _hidden = false;
            gameObject.SetActive(true);
        }

        internal void Begin(PickupInstance source)
        {
            Unsubscribe();
            if (_particles == null) _particles = GetComponentsInChildren<ParticleSystem>(true);
            _source = source;
            _sourceNetId = source.netId;
            _sourceCreated = source.creationTime;
            _expires = Time.time + MaxLifetime;
            _tracking = true;
            SubscribeZone();
            InvokeRepeating(nameof(CheckLifetime), CheckInterval, CheckInterval);
        }

        private void OnRoomLoaded(EventInfoLoadRoom _) => Retire();

        private void CheckLifetime()
        {
            if (!_tracking) return;
            try
            {
                SubscribeZone();
                // Native cleanup sets isActive=false, then UnSpawn clears netId before
                // CustomDestroyRoutine waits for FX. Neither means a new pickup life.
                // Only a new nonzero identity or creation time identifies pool reuse.
                if (Time.time >= _expires || _source == null
                    || _source.netId != 0 && _source.netId != _sourceNetId
                    || _source.creationTime != _sourceCreated
                    || _zone != null && _zone.isInRoomTransition)
                {
                    Retire();
                }
            }
            catch (Exception ex)
            {
                PickupFxLifetimePatch.Disable(ex);
                Retire();
            }
        }

        private void SubscribeZone()
        {
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (zone == _zone) return;
            if (_zone != null) _zone.ClientEvent_OnRoomLoaded -= OnRoomLoaded;
            _zone = zone;
            if (zone != null) zone.ClientEvent_OnRoomLoaded += OnRoomLoaded;
        }

        private void Retire()
        {
            if (!_tracking) return;
            Unsubscribe();
            try
            {
                ListReturnHandle<IAttachableToEntity> handle;
                var attachables = gameObject.GetComponentsInChildrenNonAlloc(includeInactive: true, out handle);
                try
                {
                    foreach (IAttachableToEntity item in attachables) item.OnAttachToEntity(null);
                }
                finally { handle.Return(); }
                DewEffect.Stop(gameObject);
            }
            catch (Exception ex) { PickupFxLifetimePatch.Disable(ex); }
            finally
            {
                // Stop() can leave live particles and fading IEffectComponents. Unlike
                // pooled DestroyEntry, it does not deactivate the visual. Clear particles
                // and hide only this child, even if a third-party Stop/detach callback fails.
                try
                {
                    if (_particles != null)
                        for (int i = 0; i < _particles.Length; i++)
                            if (_particles[i] != null)
                                _particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                catch (Exception ex) { PickupFxLifetimePatch.Disable(ex); }
                finally
                {
                    try
                    {
                        _hidden = true;
                        gameObject.SetActive(false);
                    }
                    catch (Exception ex) { PickupFxLifetimePatch.Disable(ex); }
                }
            }
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            _tracking = false;
            CancelInvoke(nameof(CheckLifetime));
            if (_zone != null) _zone.ClientEvent_OnRoomLoaded -= OnRoomLoaded;
            _source = null;
            _zone = null;
        }
    }
}
