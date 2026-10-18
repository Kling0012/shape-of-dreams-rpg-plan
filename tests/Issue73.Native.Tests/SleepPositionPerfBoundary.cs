using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;

namespace SodRpg.Mod
{
    // （#751・#760・#763・#766・#770）本体 NativePerfPatches.cs の NativePerfOptions / NativePerfPatch のうち、
    // NativeSleepPositionPerf・NativeAttachFaceIdlePerf・NativeAttachFaceBakeRecord・NativeCameraZoomRotationPerf・
    // NativeCastIndicatorCirclePerf・NativeCharacterModelDisplayBounds の抽出部分から参照される範囲の境界。
    // Warn のログ出力は本体の Log 依存のためここには置かない。
    internal static class NativePerfOptions
    {
        internal static bool SleepPosition = true;
        internal static bool AttachFaceIdle = true;
        internal static bool CameraZoomRotation = true;
        internal static bool CastIndicatorCircle = true;
        internal static bool CharacterModelDisplayBounds = true;
    }

    internal static class NativePerfPatch
    {
        internal static void Warn(ref bool warned, string feature, string reason)
        {
            if (warned) return;
            warned = true;
        }

        // 実ソースの NativeCharacterModelDisplayBounds 系パッチの Prepare が呼ぶ検索。
        internal static MethodBase Find(Type type, string method, ref bool warned)
            => HarmonyLib.AccessTools.DeclaredMethod(type, method, Type.EmptyTypes);
    }
}

// 本体の VectorPlaneTransformExtension（グローバル名前空間、ToXY(this Vector3)）と同じ意味の境界。
// 拡張メソッドの形にすると既存の Vector3Extensions.ToXY と呼び出し構文が衝突するため、
// 通常の static メソッドにして、見本からは明示的に呼ぶ。XZ 平面を返す点は Vector3Extensions と同じ。
internal static class VectorPlaneTransformExtension
{
    internal static Vector2 ToXY(Vector3 value) => new Vector2(value.x, value.z);
}

namespace Issue73.Native.Tests
{
    // 本体 ActorManager.CanEntitySleep のヒーロー距離ループと同じ命令並びの見本。
    // 実物の IL（2026-10-18 確認）は英雄側の position→ToXY を先に積み、対象（ldarg.1）の
    // position→ToXY→op_Subtraction が続く。ValidateBody はこの並びだけを手がかりに対象を見つけ、
    // 読み出しがちょうど2組であることも確かめる。
    internal sealed class SleepPositionIlShapes
    {
        internal bool SubjectBeforeSubtraction(Entity e, List<Hero> heroes)
        {
            try
            {
                if (e is Hero) return false;
                if (e.IsAnyBoss()) return false;
                foreach (var hero in heroes)
                {
                    var delta = VectorPlaneTransformExtension.ToXY(hero.position)
                              - VectorPlaneTransformExtension.ToXY(e.position);
                    if (delta.sqrMagnitude < 1600f) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        // 対象を引き算の左側へ積む並び。対象の ToXY の次に来るのは op_Subtraction ではなく
        // 英雄側の読み出しになるため、ValidateBody は対象を見つけられない。
        internal bool SubjectLoadedFirst(Entity e, List<Hero> heroes)
        {
            try
            {
                foreach (var hero in heroes)
                {
                    var delta = VectorPlaneTransformExtension.ToXY(e.position)
                              - VectorPlaneTransformExtension.ToXY(hero.position);
                    if (delta.sqrMagnitude < 1600f) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        // 読み出しが1組だけの形状（適用後の本体や、本体の書き換えで壊れた場合）。
        internal bool SinglePositionRead(Entity e, Hero hero)
            => VectorPlaneTransformExtension.ToXY(e.position).sqrMagnitude < 1600f
               && hero != null;
    }
}
