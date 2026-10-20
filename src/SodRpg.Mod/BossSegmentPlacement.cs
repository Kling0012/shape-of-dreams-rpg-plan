using UnityEngine;

namespace SodRpg.Mod
{
    // Screen-space tail of one boss-telegraph segment (fed by DrawBossWorldSegment in
    // ClientSession.BossVisuals.cs): culling plus the rotated-quad placement (pivot, angle, rect)
    // the IMGUI draw needs. Pure struct math on WorldToScreenPoint results — no camera, GUI or
    // other engine state — so the test projects compile this file directly and pin the placement,
    // including the shared-vertex equivalence of the per-vertex projection reuse (#890).
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
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
