using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.FxBudget.Tests
{
    // NativeFxBudget.cs is compiled into this assembly, so every test drives
    // the production budget through its real entry points (Apply/Restore and
    // the Harmony-based target resolution) against the FxBudgetBoundary
    // doubles. No detour is installed; the patch classes are inspected.
    public sealed class NativeFxBudgetTests
    {
        private static float _clock = 100000f;
        private static readonly List<string> ResolutionWarnings;

        static NativeFxBudgetTests()
        {
            // Target resolution is one-shot static initialization; capture its
            // per-name warnings before any instance constructor clears the log.
            _ = NativeFxBudget.Targets.Count;
            ResolutionWarnings = Log.Warnings.ToList();
        }

        public NativeFxBudgetTests()
        {
            // Every test runs strictly earlier than the previous one, which is
            // exactly the rewind NativeFxBudget uses to reset its shared light
            // window and slot state.
            _clock -= 100f;
            Time.time = _clock;
            Time.frameCount = 0;
            Log.Warnings.Clear();
            ManagerBase<GraphicsManager>.instance = null;
        }

        private static void Tick(float advance = 0f)
        {
            _clock += advance;
            Time.time = _clock;
            Time.frameCount++;
        }

        private static (GameObject effect, NetworkIdentity parent) NewEffect(Actor owner)
        {
            var effect = new GameObject();
            var parent = new NetworkIdentity { actor = owner };
            return (effect, parent);
        }

        private sealed class ThrowingParticleSystem : ParticleSystem
        {
            public override MainModule main => throw new InvalidOperationException("native module unavailable");
        }

        [Fact]
        public void LightBudget_AllowsAtMostThreePerQuarterSecondWindow()
        {
            var window = new List<(GameObject effect, NetworkIdentity parent, Light light)>();
            for (int i = 0; i < 3; i++)
            {
                var (effect, parent) = NewEffect(new St_R_Fireball());
                window.Add((effect, parent, effect.AddComponent<Light>()));
            }
            foreach (var fx in window) { Tick(); NativeFxBudget.Apply(fx.effect, fx.parent); }
            // All three were admitted, not skipped: they carry the range cap.
            Assert.All(window, fx => Assert.Equal(6f, fx.light.range, 5));

            // Free every slot, then replay within 0.25 s of the charges: the
            // time window alone must still reject the fourth light.
            foreach (var fx in window) fx.effect.activeInHierarchy = false;
            var (fourth, fourthParent) = NewEffect(new St_R_Fireball());
            var fourthLight = fourth.AddComponent<Light>();
            Tick(0.1f); NativeFxBudget.Apply(fourth, fourthParent);
            Assert.False(fourthLight.enabled);

            Tick(0.2f); // 0.3 s since the charges: outside the window
            var (fifth, fifthParent) = NewEffect(new St_R_Fireball());
            var fifthLight = fifth.AddComponent<Light>();
            Tick(); NativeFxBudget.Apply(fifth, fifthParent);
            Assert.True(fifthLight.enabled);
        }

        [Fact]
        public void LightBudget_CountsEachReplayOfAPooledEffect()
        {
            // One pooled clone replayed back to back (short hit effects): every
            // playback must use the 0.25 s window like a distinct clone would.
            var (effect, parent) = NewEffect(new St_R_Fireball());
            var light = effect.AddComponent<Light>();
            int admitted = 0;
            for (int i = 0; i < 8; i++)
            {
                effect.activeInHierarchy = false;
                NativeFxBudget.Restore(effect, parent);
                effect.activeInHierarchy = true;
                light.enabled = true;
                Tick(0.02f); NativeFxBudget.Apply(effect, parent);
                if (light.enabled) admitted++;
            }
            Assert.Equal(3, admitted);
        }

        [Fact]
        public void LightBudget_SuppressesLightsBeyondThreeConcurrent()
        {
            var playing = new List<(GameObject effect, NetworkIdentity parent)>();
            for (int i = 0; i < 3; i++)
            {
                var (effect, parent) = NewEffect(new St_R_Fireball());
                effect.AddComponent<Light>();
                playing.Add((effect, parent));
            }
            foreach (var fx in playing) { Tick(); NativeFxBudget.Apply(fx.effect, fx.parent); }

            // The window charges have expired, but all three lights are still
            // playing: the concurrency cap alone keeps rejecting more.
            Tick(0.3f);
            var (fourth, fourthParent) = NewEffect(new St_R_Fireball());
            var fourthLight = fourth.AddComponent<Light>();
            NativeFxBudget.Apply(fourth, fourthParent);
            Assert.False(fourthLight.enabled);

            // One light ends: the next playback may take its slot.
            playing[0].effect.activeInHierarchy = false;
            var (fifth, fifthParent) = NewEffect(new St_R_Fireball());
            var fifthLight = fifth.AddComponent<Light>();
            Tick(0.01f); NativeFxBudget.Apply(fifth, fifthParent);
            Assert.True(fifthLight.enabled);
        }

        [Fact]
        public void LightRangeAndIntensity_AreClampedToBudgetCaps()
        {
            // Driver-animated light: the cap lands on the multipliers and is
            // applied through the native ValueSetter refresh callback.
            var actor = new St_R_Fireball();
            var driver = actor.gameObject.AddComponent<FxPointLight>();
            driver._originalRange = 10f; driver._originalIntensity = 8f; driver.currentValue = 1f;
            var driverLight = actor.gameObject.AddComponent<Light>();
            driverLight.range = 10f; driverLight.intensity = 8f;
            Tick(); NativeFxBudget.Apply(actor);
            Assert.Equal(0.6f, driver.rangeMultiplier, 5);
            Assert.Equal(0.5f, driver.intensityMultiplier, 5);
            Assert.Equal(6f, driverLight.range, 5);
            Assert.Equal(4f, driverLight.intensity, 5);
            Assert.True(driver.enabled);

            // Plain Light without a driver: clamped directly.
            var (effect, parent) = NewEffect(new St_R_Fireball());
            var light = effect.AddComponent<Light>();
            light.range = 12f; light.intensity = 9f;
            Tick(); NativeFxBudget.Apply(effect, parent);
            Assert.Equal(6f, light.range, 5);
            Assert.Equal(4f, light.intensity, 5);
        }

        [Fact]
        public void PoolReuse_RestoresOriginalVisualValuesBeforeNextPlayback()
        {
            var actor = new St_R_Fireball();
            var effect = actor.gameObject;
            var parent = new NetworkIdentity { actor = actor };
            var driver = effect.AddComponent<FxPointLight>();
            driver._originalRange = 10f; driver._originalIntensity = 8f;
            var light = effect.AddComponent<Light>();
            light.range = 10f; light.intensity = 8f;
            var particles = effect.AddComponent<ParticleSystem>();
            particles.main.maxParticles = 100;
            particles.emission.rateOverTimeMultiplier = 100f;
            var trail = effect.AddComponent<TrailRenderer>();
            trail.time = 0.5f;

            Tick(); NativeFxBudget.Apply(effect, parent);
            Assert.Equal(6f, light.range, 5);
            Assert.Equal(4f, light.intensity, 5);
            Assert.Equal(96, particles.main.maxParticles);
            Assert.Equal(60f, particles.emission.rateOverTimeMultiplier, 5);
            Assert.Equal(0.35f, trail.time, 5);
            Assert.False(particles.lights.enabled);

            // The Play_Imp prefix restores a pooled clone before native playback.
            NativeFxBudget.Restore(effect, parent);
            Assert.Equal(10f, light.range, 5);
            Assert.Equal(8f, light.intensity, 5);
            Assert.Equal(1f, driver.rangeMultiplier, 5);
            Assert.Equal(100, particles.main.maxParticles);
            Assert.Equal(100f, particles.emission.rateOverTimeMultiplier, 5);
            Assert.Equal(0.5f, trail.time, 5);
            Assert.True(particles.lights.enabled);

            // A suppressed light comes back enabled for the next pooled use.
            for (int i = 0; i < 2; i++)
            {
                var (filler, fillerParent) = NewEffect(new St_R_Fireball());
                filler.AddComponent<Light>();
                Tick(); NativeFxBudget.Apply(filler, fillerParent);
            }
            var (second, secondParent) = NewEffect(new St_R_Fireball());
            var secondLight = second.AddComponent<Light>();
            secondLight.range = 7f;
            Tick(); NativeFxBudget.Apply(second, secondParent);
            Assert.False(secondLight.enabled);
            NativeFxBudget.Restore(second, secondParent);
            Assert.True(secondLight.enabled);
        }

        [Fact]
        public void MissingOrBrokenTargets_AreSkippedOrDisabledIndividually()
        {
            // Only four of the 64 native names exist in this assembly; the rest
            // were skipped at resolution with a per-name warning, and the
            // survivors are exactly the resolvable ones.
            Assert.Equal(
                new HashSet<Type> { typeof(Gem_R_Blade), typeof(St_R_Fireball), typeof(St_E_FlameJet), typeof(Se_Elm_Fire) },
                new HashSet<Type>(NativeFxBudget.Targets.Keys));
            Assert.Contains(ResolutionWarnings, w => w.Contains("Ai_R_Fireball"));
            Assert.Contains(ResolutionWarnings, w => w.Contains("skipping only"));

            // A target whose components cannot be snapshotted fails soft: that
            // target alone is disabled and keeps its native effects.
            var broken = new St_E_FlameJet();
            broken.gameObject.AddComponent<ThrowingParticleSystem>();
            Tick(); NativeFxBudget.Apply(broken);
            Assert.True(NativeFxBudget.Targets[typeof(St_E_FlameJet)].Disabled);
            Assert.Contains(Log.Warnings, w => w.Contains("disabled for St_E_FlameJet"));
            Tick(); NativeFxBudget.Apply(broken);
            Assert.Equal(1, Log.Warnings.Count(w => w.Contains("disabled for St_E_FlameJet")));

            // Other targets keep their budget.
            var healthy = new Gem_R_Blade();
            var light = healthy.gameObject.AddComponent<Light>();
            light.range = 12f;
            Tick(); NativeFxBudget.Apply(healthy);
            Assert.Equal(6f, light.range, 5);
        }

        [Fact]
        public void PatchTargets_AreRenderLifecycleOnly()
        {
            // The attribute-declared targets of the file's patch classes.
            var attributed = new List<string>();
            foreach (var type in typeof(NativeFxBudget).Assembly.GetTypes()
                .Where(t => t.IsDefined(typeof(HarmonyPatch), false) && t.Name.Contains("FxBudget")))
            {
                foreach (var attribute in type.GetCustomAttributes(typeof(HarmonyPatch), false)
                    .Cast<HarmonyPatch>())
                {
                    if (attribute.info.methodName != null)
                        attributed.Add(attribute.info.declaringType.Name + "." + attribute.info.methodName);
                }
            }
            Assert.Equal(
                new[] { "Actor.OnDisable", "DewEffect.Play_Imp", "ElementalStatusEffect.UpdateStackScaledEmissions" }.OrderBy(n => n),
                attributed.OrderBy(n => n));

            // The OnCreate postfixes resolve to concrete Actor types only.
            var targetMethods = (IEnumerable<MethodBase>)typeof(NativeActorFxBudgetPatch)
                .GetMethod("TargetMethods", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
            var onCreate = targetMethods.ToList();
            Assert.Equal(4, onCreate.Count); // the doubles provide four resolvable targets
            Assert.All(onCreate, m => Assert.Equal("OnCreate", m.Name));
            Assert.All(onCreate, m => Assert.True(typeof(Actor).IsAssignableFrom(m.DeclaringType)));

            // No damage, hit or validation method is anywhere in the patch surface.
            string[] gameplay = { "damage", "hit", "kill", "validate", "collider" };
            var allTargets = onCreate.Select(m => m.Name).Concat(attributed.Select(n => n.Substring(n.IndexOf('.') + 1)));
            Assert.All(allTargets, name =>
                Assert.False(gameplay.Any(g => name.ToLowerInvariant().Contains(g)), name));
        }
    }
}
