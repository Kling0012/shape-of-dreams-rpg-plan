using System;
using UnityEngine;

namespace SodRpg.Mod
{
    // ClientSession の部分クラス。部屋遷移の完了後と遠征の終了後に 1 回だけ、
    // 残ってしまった敵の見た目を掃除する（毎フレームは走らない）。
    internal sealed partial class ClientSession
    {
        // 遷移直後は本体の破棄が数フレームに分かれて進むため、少し待ってから掃除する。
        private const float SweepDelaySeconds = 1.5f;

        private float _roomSweepPendingAt;

        private void OnRoomLoadedSweep(EventInfoLoadRoom info)
        {
            try { _roomSweepPendingAt = Time.unscaledTime + SweepDelaySeconds; }
            catch (Exception ex) { Log.Error("Client OnRoomLoaded sweep: " + ex.Message); }
        }

        private void TickRoomVisualSweep()
        {
            if (_roomSweepPendingAt == 0f || Time.unscaledTime < _roomSweepPendingAt) return;
            _roomSweepPendingAt = 0f;
            SweepLeftoverVisuals();
        }

        /// <summary>
        /// Park されずに残った非アクティブ Monster の EntityVisual（Renderer が有効なもの）を回収する。
        /// プールに Park された（GameObject が非アクティブな）インスタンスは再利用の対象なので触らない。
        /// 見た目の掃除だけなので、本体の破棄判定・報酬・通信には影響させない（fail-soft）。
        /// </summary>
        internal void SweepLeftoverVisuals()
        {
            try
            {
                var visuals = UnityEngine.Object.FindObjectsByType<EntityVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                int hidden = 0;
                int unstuck = 0;
                foreach (var visual in visuals)
                {
                    if (visual == null) continue;
                    Entity entity = null;
                    try { entity = visual.entity; } catch (Exception) { }
                    if (!(entity is Monster monster)) continue;
                    // アクティブな敵は対象外。部屋の読み込み後に残っているのは破棄漏れ。
                    bool destroyed = false;
                    try { destroyed = monster.isDestroyed; } catch (Exception) { }
                    if (!destroyed) continue;
                    // 止まったままの dissolve フラグを解消し、本体の破棄が完結できるようにする。
                    if (StuckDissolve.Clear(visual)) unstuck++;
                    // GameObject が非アクティブなら Park 済み（プールの再利用対象）。描画は出ていない。
                    bool visibleLeftover = false;
                    try { visibleLeftover = visual.gameObject.activeInHierarchy; } catch (Exception) { }
                    if (!visibleLeftover) continue;
                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
                    bool anyEnabled = false;
                    foreach (var renderer in renderers)
                        if (renderer != null && renderer.enabled) { anyEnabled = true; break; }
                    if (!anyEnabled) continue;
                    foreach (var renderer in renderers)
                        if (renderer != null) renderer.enabled = false;
                    hidden++;
                }
                if (hidden > 0 || unstuck > 0)
                    Log.Info($"Room visual sweep: hid {hidden} leftover monster model(s), unstuck {unstuck} dissolve(s).");
            }
            catch (Exception ex)
            {
                Log.Error("Client room visual sweep: " + ex.Message);
            }
        }
    }
}
