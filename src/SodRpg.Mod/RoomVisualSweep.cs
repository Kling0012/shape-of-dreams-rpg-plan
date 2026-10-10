using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// 部屋を移動しても敵のモデルが残る問題の対策。
    /// 本体は死亡時の dissolve を Coroutine で回し、その間 EntityVisual の
    /// ICleanup.canDestroy が false になる。プールされたインスタンスが dissolve 中に
    /// 非アクティブ化されると Coroutine は黙って止まり、フラグが true のまま残る
    /// （EntityVisual.cs:1099/1109 が唯一の書き込み箇所で、再利用時に初期化はない）。
    /// canDestroy が false のままの Actor は破棄が完結せず、DontDestroyOnLoad の下で
    /// アクティブなまま残って、以降のどの部屋にもモデルが見える。
    /// 再利用（OnStartClient）で止まったままのフラグを解消し、残ってしまった分は
    /// 部屋遷移完了後の掃除（ClientSession.RoomSweep）で回収する。
    /// </summary>
    internal static class StuckDissolve
    {
        private static FieldInfo _isDissolvingField;
        private static bool _loggedFailure;

        /// <summary>止まったままの dissolve フラグを落とす。取れないときは何もしない。</summary>
        internal static bool Clear(EntityVisual visual)
        {
            if (visual == null) return false;
            try
            {
                if (_isDissolvingField == null)
                    _isDissolvingField = AccessTools.Field(typeof(EntityVisual), "_isDissolving");
                if (_isDissolvingField == null)
                {
                    WarnOnce(null);
                    return false;
                }
                if (!(_isDissolvingField.GetValue(visual) is bool stuck) || !stuck) return false;
                _isDissolvingField.SetValue(visual, false);
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce(ex);
                return false;
            }
        }

        private static void WarnOnce(Exception ex)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Log.Warn("Stuck dissolve reset unavailable; leftover model sweep only hides renderers: "
                + (ex != null ? ex.Message : "field _isDissolving not found"));
        }
    }

    /// <summary>プール再利用で始まった EntityVisual に、前の死亡から残った dissolve フラグを持たせない。</summary>
    [HarmonyPatch(typeof(EntityVisual), nameof(EntityVisual.OnStartClient))]
    internal static class EntityVisualDissolveReuseResetPatch
    {
        private static void Postfix(EntityVisual __instance)
        {
            try { StuckDissolve.Clear(__instance); }
            catch (Exception) { }
        }
    }
}
