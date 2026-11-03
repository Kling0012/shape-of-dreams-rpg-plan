using UnityEngine;

namespace SodRpg.Mod
{
    // Screen-space tail of one boss-telegraph segment (fed by DrawBossWorldSegment in
    // ClientSession.BossVisuals.cs): culling plus the rotated-quad placement (pivot, angle, rect)
    // and the pivot rotation matrix the IMGUI draw needs. Pure struct math on WorldToScreenPoint
    // results — no camera, GUI or other engine state — so the test projects compile this file
    // directly and pin the placement and the rotation, including the shared-vertex equivalence
    // of the per-vertex projection reuse (#890) and the direct composition of the rotation
    // around the pivot (#1153).
    internal static class BossSegmentPlacement
    {
        internal static bool TryPlace(Vector3 a, Vector3 b, float width, int screenWidth, int screenHeight,
            out Vector2 pivot, out float angle, out Rect rect)
        {
            pivot = default; angle = 0f; rect = default;
            if (!Finite(a) || !Finite(b) || a.z <= 0f || b.z <= 0f
                || a.x < 0 && b.x < 0 || a.x > screenWidth && b.x > screenWidth
                || a.y < 0 && b.y < 0 || a.y > screenHeight && b.y > screenHeight) return false;
            pivot = new Vector2(a.x, screenHeight - a.y);
            var difference = new Vector2(b.x - a.x, a.y - b.y);
            float length = difference.magnitude;
            if (length < .1f) return false;
            angle = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;
            rect = new Rect(pivot.x, pivot.y - width * .5f, length, width);
            return true;
        }

        // The six Matrix4x4 members the IMGUI tail writes for a rotation about the pivot
        // (m00, m01, m03, m10, m11, m13); every other member is the identity's. This is
        // T(pivot)·R(angle)·T(-pivot) — exactly what GUIUtility.RotateAroundPivot produces from
        // the identity base DrawBossEffects installs (see DrawBossWorldSegment), composed here
        // from sincos so the caller spends one native GUI.matrix write instead of the helper's
        // TRS passes, matrix multiplies and read-backs.
        internal readonly struct PivotRotation
        {
            internal readonly float M00, M01, M03, M10, M11, M13;
            internal PivotRotation(float m00, float m01, float m03, float m10, float m11, float m13)
            { M00 = m00; M01 = m01; M03 = m03; M10 = m10; M11 = m11; M13 = m13; }
        }
        internal static PivotRotation RotationAroundPivot(float angle, Vector2 pivot)
        {
            float radians = angle * Mathf.Deg2Rad;
            float sine = Mathf.Sin(radians), cosine = Mathf.Cos(radians);
            return new PivotRotation(cosine, -sine, pivot.x - (cosine * pivot.x - sine * pivot.y),
                sine, cosine, pivot.y - (sine * pivot.x + cosine * pivot.y));
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
