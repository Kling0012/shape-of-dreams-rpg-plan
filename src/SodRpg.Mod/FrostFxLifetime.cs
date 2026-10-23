using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch]
    internal static class FrostFxLifetimePatch
    {
        private static MethodInfo _playIntoPool;
        private static Func<IList> _entries;
        private static Func<IList, int, GameObject> _entryObject;
        private static Action<int> _destroyEntry;
        private static bool _disabled;

        private static bool Prepare()
        {
            if (_disabled) return false;
            try
            {
                _playIntoPool = AccessTools.DeclaredMethod(typeof(DewEffect), "PlayIntoPool");
                var parameters = _playIntoPool?.GetParameters();
                if (_playIntoPool == null || !_playIntoPool.IsStatic || _playIntoPool.ReturnType != typeof(GameObject)
                    || parameters.Length != 7 || parameters[1].ParameterType != typeof(GameObject)
                    || parameters[2].ParameterType != typeof(Entity)
                    || parameters[3].ParameterType != typeof(bool) || parameters[4].ParameterType != typeof(Vector3)
                    || parameters[5].ParameterType != typeof(Quaternion)
                    || parameters[6].ParameterType != typeof(Mirror.NetworkIdentity))
                    throw new MissingMethodException("DewEffect.PlayIntoPool contract changed");
                var entriesField = AccessTools.DeclaredField(typeof(EffectAutoDestroy), "_entries");
                var entryType = entriesField?.FieldType.IsGenericType == true
                    ? entriesField.FieldType.GetGenericArguments()[0] : null;
                var entryObject = entryType == null ? null : AccessTools.DeclaredField(entryType, "gameObject");
                var indexer = entriesField?.FieldType.GetProperty("Item", new[] { typeof(int) });
                var destroy = AccessTools.DeclaredMethod(typeof(EffectAutoDestroy), "DestroyEntry", new[] { typeof(int) });
                if (entriesField == null || !entriesField.IsStatic || !typeof(IList).IsAssignableFrom(entriesField.FieldType)
                    || entryObject == null || entryObject.IsStatic || entryObject.FieldType != typeof(GameObject)
                    || indexer == null || indexer.PropertyType != entryType || indexer.GetGetMethod() == null
                    || destroy == null || !destroy.IsStatic || destroy.ReturnType != typeof(void))
                    throw new MissingMethodException("EffectAutoDestroy pool release contract changed");
                // Read the live list through its typed indexer: native Entry is a struct,
                // so IList[index] would box every visited entry during each release.
                var entries = Expression.Parameter(typeof(IList));
                var index = Expression.Parameter(typeof(int));
                _entries = Expression.Lambda<Func<IList>>(
                    Expression.Convert(Expression.Field(null, entriesField), typeof(IList))).Compile();
                _entryObject = Expression.Lambda<Func<IList, int, GameObject>>(
                    Expression.Field(Expression.MakeIndex(Expression.Convert(entries, entriesField.FieldType),
                        indexer, new[] { index }), entryObject), entries, index).Compile();
                _destroyEntry = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), destroy);
                return true;
            }
            catch (Exception ex) { Disable(ex); return false; }
        }

        private static MethodBase TargetMethod() => _playIntoPool;

        private static void Postfix(GameObject __result, GameObject template, Entity attach, Mirror.NetworkIdentity parent)
        {
            if (_disabled || __result == null || parent == null) return;
            try
            {
                var frost = parent.GetComponent<Gem_R_Frost>();
                if (frost == null || template != frost.activateEffect) return;
                var lifetime = __result.GetComponent<FrostFxLifetime>();
                if (lifetime == null) lifetime = __result.AddComponent<FrostFxLifetime>();
                lifetime.Begin(frost, attach);
            }
            catch (Exception ex) { Disable(ex); }
        }

        // Release through the native registry before the pool can reuse the clone. Merely
        // disabling a clone leaves an old Entry that can later retire its next playback.
        internal static void Release(GameObject effect)
        {
            var entries = _entries();
            for (int i = entries.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_entryObject(entries, i), effect)) _destroyEntry(i);
            // Also covers an already removed native registration. Never destroy a pooled clone.
            if (effect == null) return;
            DewEffect.Stop(effect);
            effect.SetActive(false);
        }

        internal static void Disable(Exception ex)
        {
            if (_disabled) return;
            _disabled = true;
            Log.Warn("Frost FX lifetime patch disabled; other MOD features remain active: " + ex.Message);
        }
    }

    // One timer per actual frost clone; no Update or global scene/particle scan.
    internal sealed class FrostFxLifetime : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        private const float MaxLifetime = 30f;
        private const int MaxStatusChecks = 128;
        private Gem_R_Frost _source;
        private Hero _owner;
        private Entity _victim;
        private Se_GenericEffectContainer _stun;
        private ZoneManager _zone;
        private ParticleSystem[] _particles;
        private uint _sourceNetId, _ownerNetId, _victimNetId, _stunNetId;
        private float _sourceCreated, _ownerCreated, _victimCreated, _stunCreated;
        private float _expires;
        private bool _tracking;

        internal void Begin(Gem_R_Frost source, Entity victim)
        {
            Unsubscribe();
            if (_particles == null) _particles = GetComponentsInChildren<ParticleSystem>(true);
            _source = source;
            _owner = source.owner;
            _victim = victim;
            _sourceNetId = source.netId;
            _sourceCreated = source.creationTime;
            if (_owner != null)
            {
                _ownerNetId = _owner.netId;
                _ownerCreated = _owner.creationTime;
            }
            if (victim != null)
            {
                _victimNetId = victim.netId;
                _victimCreated = victim.creationTime;
            }
            float duration = source.stunDuration;
            if (float.IsNaN(duration) || float.IsInfinity(duration)) duration = MaxLifetime;
            _expires = Time.time + Mathf.Clamp(duration, CheckInterval, MaxLifetime);
            _tracking = true;
            source.ClientActorEvent_OnDestroyed += OnActorDestroyed;
            source.ClientEvent_OnOwnerChanged += OnOwnerChanged;
            if (_owner != null) _owner.ClientActorEvent_OnDestroyed += OnActorDestroyed;
            if (victim != null) victim.ClientActorEvent_OnDestroyed += OnActorDestroyed;
            SubscribeZone();
            InvokeRepeating(nameof(CheckLifetime), CheckInterval, CheckInterval);
        }

        private void OnActorDestroyed(Actor _) => Retire();
        private void OnOwnerChanged(Hero oldOwner, Hero newOwner) => Retire();
        private void OnRoomLoaded(EventInfoLoadRoom _) => Retire();

        private void CheckLifetime()
        {
            if (!_tracking) return;
            try
            {
                SubscribeZone();
                if (Time.time >= _expires || !SameLife(_source, _sourceNetId, _sourceCreated)
                    || !SameLife(_owner, _ownerNetId, _ownerCreated)
                    || !SameLife(_victim, _victimNetId, _victimCreated) || _source.owner != _owner
                    || _owner.Status == null || _owner.isDead || _victim.Status == null || _victim.isDead
                    || _zone != null && _zone.isInRoomTransition)
                {
                    Retire();
                    return;
                }
                if (!ReferenceEquals(_stun, null))
                {
                    if (!SameLife(_stun, _stunNetId, _stunCreated) || _stun.victim != _victim
                        || _stun.parentActor != _source || _stun.id != "frost_stun") Retire();
                    return;
                }
                // The FX message can precede the status spawn on remote clients. Do not
                // interpret a missing status as an end; the finite deadline still applies.
                var effects = _victim.Status.statusEffects;
                int count = Math.Min(effects.Count, MaxStatusChecks);
                for (int i = 0; i < count; i++)
                {
                    if (!(effects[i] is Se_GenericEffectContainer stun) || !stun.isActive
                        || stun.parentActor != _source || stun.victim != _victim || stun.id != "frost_stun") continue;
                    _stun = stun;
                    _stunNetId = stun.netId;
                    _stunCreated = stun.creationTime;
                    stun.ClientActorEvent_OnDestroyed += OnActorDestroyed;
                    break;
                }
            }
            catch (Exception ex)
            {
                FrostFxLifetimePatch.Disable(ex);
                Retire();
            }
        }

        private static bool SameLife(Actor actor, uint netId, float created)
            => actor != null && actor.isActive && actor.gameObject.activeInHierarchy
                && actor.netId == netId && actor.creationTime == created;

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
            try { FrostFxLifetimePatch.Release(gameObject); }
            catch (Exception ex)
            {
                FrostFxLifetimePatch.Disable(ex);
                // Local visual failure must never reach gameplay or Mirror's spawn state.
                if (gameObject != null) gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            try
            {
                // Native pool release only clears its reset list (playOnAwake == false).
                // Clear every frost particle even when native timeout/room cleanup won the race.
                if (_particles == null) return;
                for (int i = 0; i < _particles.Length; i++)
                    if (_particles[i] != null)
                        _particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            catch (Exception ex) { FrostFxLifetimePatch.Disable(ex); }
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            _tracking = false;
            CancelInvoke(nameof(CheckLifetime));
            if (_source != null)
            {
                _source.ClientActorEvent_OnDestroyed -= OnActorDestroyed;
                _source.ClientEvent_OnOwnerChanged -= OnOwnerChanged;
            }
            if (_owner != null) _owner.ClientActorEvent_OnDestroyed -= OnActorDestroyed;
            if (_victim != null) _victim.ClientActorEvent_OnDestroyed -= OnActorDestroyed;
            if (_stun != null) _stun.ClientActorEvent_OnDestroyed -= OnActorDestroyed;
            if (_zone != null) _zone.ClientEvent_OnRoomLoaded -= OnRoomLoaded;
            _source = null;
            _owner = null;
            _victim = null;
            _stun = null;
            _zone = null;
        }
    }
}
