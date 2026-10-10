using System;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// 敵の死亡演出（閃光・カメラ揺れ・コントローラ振動）の同時多発を間引く。
    /// 0.1 秒以内の 2 回目以降は無視し、最初の 1 回は必ず通す。本体の
    /// perfPressureStrength が 0 より大きいときは窓を広げてさらに間引く。
    /// カメラから 30m 以上離れた死亡では揺れ・振動を出さない。
    /// 見た目の節約だけなので、報酬・判定・通信には影響させない（fail-soft）。
    /// </summary>
    internal static class DeathFxThrottle
    {
        internal const float BaseWindowSeconds = 0.1f;
        internal const float PressureWindowSeconds = 0.25f;
        internal const float MaxDistanceFromCamera = 30f;

        // 揺れと振動はそれぞれ独立に間引く（最初の 1 回は必ず通す）。
        private static float _nextShakeAllowedAt = float.NegativeInfinity;
        private static float _nextRumbleAllowedAt = float.NegativeInfinity;
        private static bool _loggedFailure;

        /// <summary>本体の性能圧力が立っているときは窓を広げる。取れないときは基本値。</summary>
        private static float WindowSeconds()
        {
            try
            {
                var graphics = ManagerBase<GraphicsManager>.instance;
                if (graphics != null && graphics.perfPressureStrength > 0f) return PressureWindowSeconds;
            }
            catch (Exception) { }
            return BaseWindowSeconds;
        }

        internal static bool PassesShakeWindow()
        {
            float now = Time.time;
            if (now < _nextShakeAllowedAt) return false;
            _nextShakeAllowedAt = now + WindowSeconds();
            return true;
        }

        internal static bool PassesRumbleWindow()
        {
            float now = Time.time;
            if (now < _nextRumbleAllowedAt) return false;
            _nextRumbleAllowedAt = now + WindowSeconds();
            return true;
        }

        internal static bool IsTooFarFromCamera(Transform transform)
        {
            try
            {
                var camera = Camera.main;
                return camera != null
                    && (transform.position - camera.transform.position).sqrMagnitude
                       > MaxDistanceFromCamera * MaxDistanceFromCamera;
            }
            catch (Exception) { return false; }
        }

        internal static void WarnOnce(Exception ex)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Log.Warn("Death fx throttle unavailable; passing effects through: " + ex.Message);
        }
    }

    /// <summary>本体のカメラ揺れ（CinemachineImpulse とゲームパッド振動の両方の元）の再生を間引く。</summary>
    [HarmonyPatch(typeof(FxCameraShake), nameof(FxCameraShake.Play))]
    internal static class DeathCameraShakeThrottlePatch
    {
        private static bool Prefix(FxCameraShake __instance)
        {
            try
            {
                if (DeathFxThrottle.IsTooFarFromCamera(__instance.transform)) return false;
                // 0.1 秒以内の 2 回目以降は無視。ただし最初の 1 回は必ず通す。
                return DeathFxThrottle.PassesShakeWindow();
            }
            catch (Exception ex)
            {
                DeathFxThrottle.WarnOnce(ex);
                return true;
            }
        }
    }

    /// <summary>本体の振動スコープの重なりを間引く（最初の 1 回は必ず通す）。</summary>
    [HarmonyPatch(typeof(DewEffect), nameof(DewEffect.EnableRumbleForPlay))]
    internal static class DeathRumbleScopeThrottlePatch
    {
        private static bool Prefix()
        {
            try
            {
                return DeathFxThrottle.PassesRumbleWindow();
            }
            catch (Exception ex)
            {
                DeathFxThrottle.WarnOnce(ex);
                return true;
            }
        }
    }

    /// <summary>エフェクト品質が低のとき、死亡エフェクトの光（Light）を省く。</summary>
    [HarmonyPatch(typeof(EntityVisual), "UserCode_RpcHandleDeath__GibInfo")]
    internal static class DeathFxLightQualityPatch
    {
        private static void Prefix(EntityVisual __instance)
        {
            try
            {
                var graphics = ManagerBase<GraphicsManager>.instance;
                if (graphics == null || graphics.currentEffectQuality != Quality3Levels.Low) return;
                var model = __instance.model;
                if (model == null || model.fxDeath == null) return;
                // FxPointLight が再生時に Light を有効化し戻すので、ドライバ側も止める。
                foreach (var light in model.fxDeath.GetComponentsInChildren<Light>(true))
                    light.enabled = false;
                foreach (var driver in model.fxDeath.GetComponentsInChildren<FxPointLight>(true))
                    driver.enabled = false;
            }
            catch (Exception ex)
            {
                DeathFxThrottle.WarnOnce(ex);
            }
        }
    }
}
