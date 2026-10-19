using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// ドリームダストや富のエッセンスの宝珠など、拾得物の獲得演出（pickupEffectOnHero）が
    /// プレイヤーにまとわりついたまま消えない問題（#889）の対策。
    /// この再生は EffectAutoDestroy への登録がないその場再生で、消える保証を拾得物 Actor の
    /// 破棄鎖（Dew.Destroy → 非同期破棄 → despawn → CustomDestroyRoutine での登録）に丸ごと
    /// 預けている。破棄が完結しない停滞（canDestroy が false のままの Actor。StuckDissolve が
    /// 文書化した本体の停滞と同型）が起きると、拾得物は DontDestroyOnLoad の下でアクティブの
    /// まま残り、FxAttachToEntity は演出をヒーローへ追従させ続ける。
    /// ここではこの再生だけに本体と同じ消失処理を独自の有界スケジュールで保証する。
    /// 打ち切り条件は本体の EffectAutoDestroy と同じ 30 秒上限・部屋遷移・部屋替わりで、
    /// 通常の再生（本体の後始末が動く範囲）は一切短くしない。停止は本体の FxStop
    /// （== DewEffect.Stop）と追従解除（OnAttachToEntity(null)）だけで、拾得物本体の
    /// 生存・破棄・プールには触れない。契約が変わったらパッチは入らず従来どおり（fail-soft）。
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

        // HandleEffectMessage(PlayAttachedEffectMessage) reaches this overload for every
        // in-place (isPooled/isNew == false) attached effect, on clients and on the host's
        // local client alike. Only the pickup's own pickupEffectOnHero playback is guarded.
        private static void Postfix(GameObject effect, Entity attach, Mirror.NetworkIdentity parent)
        {
            if (_disabled || effect == null || attach == null || parent == null) return;
            try
            {
                var pickup = parent.GetComponent<PickupInstance>();
                if (pickup == null || effect != pickup.pickupEffectOnHero) return;
                var guard = effect.GetComponent<PickupFxLifetime>();
                if (guard == null) guard = effect.AddComponent<PickupFxLifetime>();
                guard.Begin(pickup);
            }
            catch (Exception ex) { Disable(ex); }
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

        internal void Begin(PickupInstance source)
        {
            Unsubscribe();
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
                // Liveness is identity + creation time only: a picked-up pickup flips
                // Network_isActive during its normal despawn, and deactivating the pickup
                // deactivates this child too (OnDisable), so neither may retire early.
                // A netId/creationTime change means the pooled instance was reused for a
                // different pickup and this playback belongs to the previous life.
                if (Time.time >= _expires || _source == null
                    || _source.netId != _sourceNetId || _source.creationTime != _sourceCreated
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

        // The native disappearing process for this playback: stop the effect the way
        // OnDestroyActor stops mainEffect, then detach the follower the way
        // EffectAutoDestroy.DestroyEntry does before returning a clone to the pool.
        private void Retire()
        {
            if (!_tracking) return;
            Unsubscribe();
            try
            {
                // The exact inverse of DewEffect.Play's attach loop, then FxStop
                // (== DewEffect.Stop, what OnDestroyActor uses for mainEffect).
                ListReturnHandle<IAttachableToEntity> handle;
                foreach (IAttachableToEntity item in gameObject.GetComponentsInChildrenNonAlloc(includeInactive: true, out handle))
                    item.OnAttachToEntity(null);
                handle.Return();
                DewEffect.Stop(gameObject);
            }
            catch (Exception ex) { PickupFxLifetimePatch.Disable(ex); }
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
