using System.Reflection;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.DeathFx.Tests
{
    // DeathFxThrottle.cs is compiled into this assembly, so every test drives
    // the production light-quality Prefix through its real entry point — the
    // same private static Prefix(EntityVisual __instance) the game's Harmony
    // detour calls before EntityVisual.UserCode_RpcHandleDeath__GibInfo —
    // followed by the doubled death-RPC playback. No detour is installed.
    public sealed class DeathFxLightQualityTests
    {
        private static readonly MethodBase DeathPrefix = typeof(DeathFxLightQualityPatch)
            .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);

        public DeathFxLightQualityTests()
        {
            Log.Warnings.Clear();
            ManagerBase<GraphicsManager>.instance = null;
        }

        // A death-fx light driven by FxPointLight, like the game's fxDeath
        // prefabs: both sit on model.fxDeath of the entity's model.
        private static (EntityVisual visual, Light light, FxPointLight driver) NewEnemy(Quality3Levels quality)
        {
            var graphics = new GraphicsManager { currentEffectQuality = quality };
            ManagerBase<GraphicsManager>.instance = graphics;
            var visual = new EntityVisual();
            var fxDeath = new GameObject();
            var driver = fxDeath.AddComponent<FxPointLight>();
            var light = fxDeath.AddComponent<Light>();
            visual.model.fxDeath = fxDeath;
            return (visual, light, driver);
        }

        // Harmony runs the Prefix before the patched RPC body.
        private static void Die(EntityVisual visual)
        {
            DeathPrefix.Invoke(null, new object[] { visual });
            visual.UserCode_RpcHandleDeath__GibInfo();
        }

        [Fact]
        public void Death_WithoutLowQuality_KeepsDeathFxLightsOn()
        {
            var (visual, light, driver) = NewEnemy(Quality3Levels.Medium);
            Die(visual);
            Assert.True(light.enabled);
            Assert.True(driver.enabled);
        }

        [Theory]
        [InlineData(Quality3Levels.Medium)]
        [InlineData(Quality3Levels.High)]
        public void QualityRaisedAfterLowDeath_BringsDeathFxLightsBack(Quality3Levels raised)
        {
            var (visual, light, driver) = NewEnemy(Quality3Levels.Low);
            Die(visual);
            // Low: suppressed by design, and the disabled driver keeps the
            // game from turning the light back on during playback.
            Assert.False(light.enabled);
            Assert.False(driver.enabled);

            // The player raises the effect quality, then this fxDeath plays
            // again (the same entity, or a pool-reused enemy carrying the same
            // model.fxDeath): the death light must come back (issue #571).
            ManagerBase<GraphicsManager>.instance.currentEffectQuality = raised;
            Die(visual);
            Assert.True(driver.enabled);
            Assert.True(light.enabled);
        }
    }
}
