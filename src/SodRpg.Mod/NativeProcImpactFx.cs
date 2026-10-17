using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // Only attached impact visuals at these exact fields may be coalesced.
    // Keep the native packet for the first play; no peer capability is needed.
    internal static class NativeProcImpactFx
    {
        private sealed class Source
        {
            internal AbilityInstance Actor;
            internal int Kind;
        }

        private sealed class Visual
        {
            internal bool Safe;
            internal Visual(GameObject effect)
            {
                var components = effect.GetComponentsInChildren<Component>(true);
                bool particles = false;
                foreach (var component in components)
                {
                    if (component == null) return;
                    var type = component.GetType();
                    if (type == typeof(ParticleSystem))
                    {
                        var ps = (ParticleSystem)component;
                        if (ps.trigger.enabled || ps.collision.enabled || ps.subEmitters.enabled
                            || ps.main.stopAction != ParticleSystemStopAction.None) return;
                        particles = true;
                    }
                    else if (type != typeof(Transform) && type != typeof(ParticleSystemRenderer)
                        && type != typeof(MeshRenderer) && type != typeof(MeshFilter)
                        && type != typeof(TrailRenderer) && type != typeof(Light)
                        && type != typeof(FxParticleSystem) && type != typeof(FxPointLight)) return;
                }
                // Unknown scripts, UnityEvents, entity effects, telegraphs and
                // random-transform scripts all retain native playback.
                Safe = particles;
            }
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly uint _asset;
            private readonly int _kind, _caster, _target;
            internal Key(uint asset, int kind, int caster, int target)
            { _asset = asset; _kind = kind; _caster = caster; _target = target; }
            public bool Equals(Key other) => _asset == other._asset && _kind == other._kind
                && _caster == other._caster && _target == other._target;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => unchecked((((int)_asset * 397 ^ _kind) * 397 ^ _caster) * 397 ^ _target);
        }

        private static readonly ConditionalWeakTable<NetworkIdentity, Source> Sources = new ConditionalWeakTable<NetworkIdentity, Source>();
        private static readonly ConditionalWeakTable<NetworkIdentity, Source>.CreateValueCallback CreateSource = ResolveSource;
        private static readonly ConditionalWeakTable<GameObject, Visual> Visuals = new ConditionalWeakTable<GameObject, Visual>();
        private static readonly ConditionalWeakTable<GameObject, Visual>.CreateValueCallback CreateVisual = effect => new Visual(effect);
        private static readonly HashSet<Key> Seen = new HashSet<Key>();
        private static int _frame = -1;
        private static float _window = -1f;
        private static bool _disabled;

        private static Source ResolveSource(NetworkIdentity identity)
        {
            var actor = identity.GetComponent<AbilityInstance>();
            int kind = actor != null && actor.GetType() == typeof(Ai_Q_IncendiaryRounds_Attack) ? 1
                : actor != null && actor.GetType() == typeof(Ai_Gem_C_Charcoal_Projectile) ? 3
                : actor != null && actor.GetType() == typeof(Ai_Gem_R_Blade) ? 4 : 0;
            return new Source { Actor = actor, Kind = kind };
        }

        internal static bool Allow(NetworkIdentity parent, GameObject effect, Entity entity)
        {
            if (_disabled || !NetworkServer.active || parent == null || effect == null || entity == null || entity.netId == 0) return true;
            try
            {
                var source = Sources.GetValue(parent, CreateSource);
                int kind = source.Kind;
                if (kind == 0 || source.Actor == null) return true;
                if (kind == 1)
                {
                    var actor = (Ai_Q_IncendiaryRounds_Attack)source.Actor;
                    if (effect == actor.aoeEffect) kind = 2;
                    else if (effect != actor.hitEffect) return true;
                }
                else if (kind == 3)
                {
                    if (effect != ((Ai_Gem_C_Charcoal_Projectile)source.Actor).fireHitEffect) return true;
                }
                else
                {
                    var actor = (Ai_Gem_R_Blade)source.Actor;
                    if (effect == actor.hitEffect) kind = 5;
                    else if (effect != actor.slashEffect) return true;
                }
                if (!Visuals.GetValue(effect, CreateVisual).Safe) return true;
                var caster = source.Actor.info.caster;
                if (caster == null || parent.assetId == 0) return true;
                var graphics = ManagerBase<GraphicsManager>.instance;
                float interval = graphics != null && graphics.currentEffectQuality == Quality3Levels.Low ? 0.05f
                    : graphics != null && graphics.perfPressureStrength > 0f ? 0.025f : 0f;
                float now = Time.time;
                if (now < _window || (interval == 0f ? _frame != Time.frameCount : now - _window >= interval))
                {
                    Seen.Clear();
                    _window = now;
                    _frame = Time.frameCount;
                }
                var key = new Key(parent.assetId, kind, caster.GetInstanceID(), entity.GetInstanceID());
                if (Seen.Contains(key)) return false;
                // Bound tracking memory. Overflow keeps native visuals rather
                // than dropping an unclassified target or growing each frame.
                if (Seen.Count < 512) Seen.Add(key);
                return true;
            }
            catch (Exception ex)
            {
                _disabled = true;
                Log.Warn("Native impact FX coalescing disabled; native playback retained: " + ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class NativeProcImpactFxPatch
    {
        private static readonly List<MethodBase> Methods = new List<MethodBase>();
        private static bool _warned;

        private static bool Prepare()
        {
            Methods.Clear();
            foreach (string name in new[] { "PlayNewNetworked", "PlayNetworked" })
            {
                var method = AccessTools.DeclaredMethod(typeof(DewEffect), name,
                    new[] { typeof(NetworkIdentity), typeof(GameObject), typeof(Entity) });
                if (method != null && method.ReturnType == typeof(void) && method.GetMethodBody() != null) Methods.Add(method);
                else if (!_warned)
                {
                    _warned = true;
                    Log.Warn("Native impact FX overload unavailable; that overload retains native playback: " + name);
                }
            }
            return Methods.Count > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods() => Methods;
        private static bool Prefix(NetworkIdentity parent, GameObject effect, Entity entity)
            => NativeProcImpactFx.Allow(parent, effect, entity);
    }
}
