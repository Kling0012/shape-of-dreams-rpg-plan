using System;
using System.Collections.Generic;
using SodRpg.Mod;
using UnityEngine;

namespace UnityEngine
{
    // （#763）Quaternion の境界。実物の Euler はテスト環境で動かせないため、
    // 入力角度をそのまま記録し、等価比較の材料にする。回転の意味（成分への変換）は
    // この検証では不要で、見本・FollowRotation が同じ呼び出し形をとることだけが要る。
    internal struct Quaternion : IEquatable<Quaternion>
    {
        public float x, y, z;
        public Quaternion(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion(x, y, z);
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
        public bool Equals(Quaternion other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is Quaternion other && Equals(other);
        public override int GetHashCode() => (x.GetHashCode() * 397) ^ (y.GetHashCode() * 31) ^ z.GetHashCode();
        public static bool operator ==(Quaternion a, Quaternion b) => a.Equals(b);
        public static bool operator !=(Quaternion a, Quaternion b) => !a.Equals(b);
        public override string ToString() => "Euler(" + x + ", " + y + ", " + z + ")";
    }

    // NativeOcclusionShaderIdlePerf のコンパイル用の境界。実物の Vector4 同士の比較は
    // 成分の完全一致なので、同じ意味の等価だけを用意する。
    internal struct Vector4 : IEquatable<Vector4>
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public bool Equals(Vector4 other) => x == other.x && y == other.y && z == other.z && w == other.w;
        public override bool Equals(object obj) => obj is Vector4 other && Equals(other);
        public override int GetHashCode() => (((x.GetHashCode() * 397) ^ y.GetHashCode()) * 397 ^ z.GetHashCode()) * 397 ^ w.GetHashCode();
        public static bool operator ==(Vector4 a, Vector4 b) => a.Equals(b);
        public static bool operator !=(Vector4 a, Vector4 b) => !a.Equals(b);
    }

    // グローバルシェーダー値の受け口。テストからは呼ばれないため、呼び形だけを用意する。
    internal static class Shader
    {
        public static void SetGlobalInt(int nameID, int value) { }
        public static void SetGlobalFloat(int nameID, float value) { }
        public static void SetGlobalVector(int nameID, Vector4 value) { }
        public static void SetGlobalVectorArray(int nameID, Vector4[] values) { }
    }
}

namespace SodRpg.Mod
{
    // 本体 Dew.Core のカメラ修飾子。GetFollowOffsetTarget の IL 見本のためだけの最小の境界。
    internal class CameraModifierBase { }
    internal sealed class CameraModifierZoom : CameraModifierBase { public float zoomIndex; }
}

namespace Issue73.Native.Tests
{
    // 本体 CameraManager.GetFollowOffsetTarget と同じ命令並びの見本。実物の IL
    // （2026-10-18 確認、IL_009e〜IL_00b3）は ldc.r4 0 / ldarg.0 / call get_entityCamAngle /
    // ldc.r4 0 / call Quaternion.Euler の5命令をちょうど1箇所に持ち、直後に op_Multiply が続く。
    // NativeLikeTarget は本体の逆コンパイル（CameraManager.cs:278-294）と同じ本体を並べる。
    // それ以外は窓の形が壊れた見本で、置き換えが起きないことの確認に使う。
    internal sealed class CameraZoomIlShapes : CameraManager
    {
        internal Vector3 NativeLikeTarget()
        {
            float num = currentZoomIndex;
            if (!isSpectating)
            {
                foreach (CameraModifierBase cameraModifier in _cameraModifiers)
                {
                    if (cameraModifier is CameraModifierZoom cameraModifierZoom)
                    {
                        num = cameraModifierZoom.zoomIndex;
                    }
                }
            }
            float num2 = num / (float)(zoomSteps - 1);
            Vector3 vector = ((num2 < 0.5f) ? Vector3.LerpUnclamped(farZoomBody, midZoombody, num2 * 2f)
                : Vector3.LerpUnclamped(midZoombody, closeZoomBody, num2 * 2f - 1f));
            return Quaternion.Euler(0f, entityCamAngle, 0f) * vector;
        }

        // 最初の引数が定数 0 でない並び。窓の左端が ldc.r4 にならないため対象から外れる。
        internal Vector3 FirstAngleIsVariable(float other)
            => Quaternion.Euler(other, entityCamAngle, 0f) * new Vector3(1f, 2f, 3f);

        // Euler が2箇所ある並び。ちょうど1箇所でなければ対象から外れる。
        internal Vector3 TwoEulerCalls(Vector3 vector)
            => (Quaternion.Euler(0f, entityCamAngle, 0f) * vector)
               + (Quaternion.Euler(0f, 0f, 0f) * new Vector3(0f, 1f, 0f));

        // Euler が無い並び（適用後の本体）。対象から外れる。
        internal Vector3 NoEulerCall(Vector3 vector) => entityCamAngleRotation * vector;
    }
}
