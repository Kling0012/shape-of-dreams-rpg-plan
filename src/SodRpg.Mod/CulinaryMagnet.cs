using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Use the native non-allocating pickup motor and its synchronized target, not host-only
    // transform writes. Culinary pickups remain usable only by their living caster.
    internal static class CulinaryMagnet
    {
        private static Harmony _harmony;
        private static MethodInfo _create, _hook;
        private static FieldInfo _range, _delay, _speed, _accelerationMin, _accelerationMax;
        private static bool _enabled, _warned;
        // Host only: live ingredient pickups. The native motor homes toward the owner, but a blinking owner can
        // outrun it, so each frame the host also moves any pickup left far behind onto its living owner.
        private static readonly List<Actor> Tracked = new List<Actor>();

        internal static void Install(Harmony owner)
        {
            Stop();
            _warned = false;
            try
            {
                var pickup = AccessTools.TypeByName("Ai_Culinary_Pickup");
                var motor = AccessTools.TypeByName("PickupInstance");
                if (pickup == null || motor == null || !motor.IsAssignableFrom(pickup))
                    throw new MissingMemberException("Ai_Culinary_Pickup : PickupInstance");
                _create = AccessTools.DeclaredMethod(pickup, "OnCreate", Type.EmptyTypes);
                if (_create == null || _create.ReturnType != typeof(void))
                    throw new MissingMethodException(pickup.FullName, "OnCreate");
                _range = RequireField(motor, "attractionRange");
                _delay = RequireField(motor, "pickupDelay");
                _speed = RequireField(motor, "maxVelocity");
                _accelerationMin = RequireField(motor, "velocityAccelerationMin");
                _accelerationMax = RequireField(motor, "velocityAccelerationMax");
                _hook = AccessTools.DeclaredMethod(typeof(CulinaryMagnet), nameof(AfterCreate));
                _harmony = new Harmony(owner.Id + ".CulinaryMagnet");
                _harmony.Patch(_create, postfix: new HarmonyMethod(_hook));
                _enabled = true;
            }
            catch (Exception ex)
            {
                Disable(ex);
            }
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            var field = AccessTools.Field(type, name);
            if (field == null || field.FieldType != typeof(float) || field.IsStatic || field.IsInitOnly)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static void AfterCreate(object __instance)
        {
            if (!_enabled) return;
            try
            {
                // Applied after native variation on both host and clients. All movement,
                // eligibility, pickup effects and Network_target synchronization stay native.
                _range.SetValue(__instance, 500f);
                _delay.SetValue(__instance, 0f);
                _speed.SetValue(__instance, 120f);
                _accelerationMin.SetValue(__instance, 150f);
                _accelerationMax.SetValue(__instance, 300f);
                if (NetworkServer.active && __instance is Actor actor) Tracked.Add(actor);
            }
            catch (Exception ex)
            {
                Disable(ex);
            }
        }

        /// <summary>毎フレーム（ホスト）：持ち主から離れすぎた食材を持ち主の位置へ移し、本体の拾得に任せる。</summary>
        internal static void SnapToOwners()
        {
            if (!_enabled || Tracked.Count == 0) return;
            if (!NetworkServer.active) { Tracked.Clear(); return; }
            for (int i = Tracked.Count - 1; i >= 0; i--)
            {
                var actor = Tracked[i];
                // Picked up, expired or destroyed (Unity null) pickups leave the list.
                if (actor == null || !actor.gameObject.activeInHierarchy) { Tracked.RemoveAt(i); continue; }
                try
                {
                    var owner = (actor as AbilityInstance)?.info.caster as Hero;
                    if (owner == null || !owner.isActive || owner.isKnockedOut) continue;
                    var target = owner.agentPosition;
                    var here = actor.transform.position;
                    if (CulinaryPickupSnap.ShouldSnap(true, Vector3.Distance(here, target)))
                        actor.transform.position = target;
                }
                catch (Exception ex)
                {
                    Tracked.RemoveAt(i);
                    if (!_warned)
                    {
                        _warned = true;
                        Log.Warn("Culinary ingredient snap failed for one pickup; others continue: " + ex.Message);
                    }
                }
            }
        }

        private static void Disable(Exception ex)
        {
            Stop();
            if (_warned) return;
            _warned = true;
            Log.Warn("Culinary ingredient attraction disabled; other MOD features remain active: " + ex.Message);
        }

        internal static void Stop()
        {
            _enabled = false;
            Tracked.Clear();
            try
            {
                if (_harmony != null && _create != null && _hook != null)
                    _harmony.Unpatch(_create, _hook);
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("Culinary ingredient attraction cleanup failed: " + ex.Message);
                }
            }
            _harmony = null;
        }
    }
}
