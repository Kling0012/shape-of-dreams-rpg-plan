using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>View-space geometry, rebuilt only after layout, canvas, pan or zoom changes.</summary>
    internal sealed class StarMapView
    {
        internal readonly struct EdgeQuad
        {
            internal EdgeQuad(int a, int b, Vector2 start, float length, float cosine, float sine)
            { A = a; B = b; Start = start; Length = length; Cosine = cosine; Sine = sine; }
            internal readonly int A, B;
            internal readonly Vector2 Start;
            internal readonly float Length, Cosine, Sine;
        }

        // The game's GUIUtility.RotateAroundPivot pre-multiplies its rotation. Under GUI.matrix scaling
        // that rotates around the wrong group origin. Bind the same native unclip operation once;
        // unlike screen conversion, it excludes OS/editor window offsets. No reflection in OnGUI.
        private static readonly Func<Vector2, Vector2> Unclip = (Func<Vector2, Vector2>)Delegate.CreateDelegate(
            typeof(Func<Vector2, Vector2>), typeof(GUI).Assembly.GetType("UnityEngine.GUIClip", true)
                .GetMethod("Unclip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Vector2) }, null));

        private HeroTreeLayout _layout;
        private Rect _viewport;
        private Vector2 _pan;
        private float _zoom, _guiScale;
        private readonly HashSet<long> _labelCells = new HashSet<long>();
        internal Rect[] NodeRects { get; private set; }
        internal long[] LabelCells { get; private set; }
        internal bool[] Named { get; private set; }
        internal int[] VisibleNodes { get; private set; }
        internal EdgeQuad[] VisibleEdges { get; private set; }
        internal int NodeCount { get; private set; }
        internal int EdgeCount { get; private set; }

        // Label thresholds were tuned at 1080p (GUI scale 1). On a smaller GUI scale the same text is physically
        // smaller, so names appear later (hidden rather than unreadably tiny); larger scales keep the 1080p thresholds.
        private const float LabelCellWidth = 112f, LabelCellHeight = 22f;

        internal void Update(HeroTreeLayout layout, Rect viewport, Vector2 pan, float zoom, float guiScale)
        {
            guiScale = Mathf.Clamp(guiScale, 0.4f, 1f);
            if (_layout == layout && _viewport == viewport && _pan == pan && _zoom == zoom && _guiScale == guiScale) return;
            if (_layout != layout)
            {
                NodeRects = new Rect[layout.Nodes.Count];
                LabelCells = new long[layout.Nodes.Count];
                Named = new bool[layout.Nodes.Count];
                VisibleNodes = new int[layout.Nodes.Count];
                VisibleEdges = new EdgeQuad[layout.Edges.Count];
            }
            _layout = layout;
            _viewport = viewport;
            _pan = pan;
            _zoom = zoom;
            _guiScale = guiScale;
            float labelZoom = zoom * guiScale;
            NodeCount = EdgeCount = 0;
            _labelCells.Clear();
            var bounds = new StarMapBounds(viewport.xMin, viewport.yMin, viewport.xMax, viewport.yMax);
            var nodeBounds = new StarMapBounds(bounds.XMin - 80f, bounds.YMin - 26f, bounds.XMax + 80f, bounds.YMax + 11f);
            Vector2 center = viewport.center + pan;
            float sizeScale = Mathf.Clamp(zoom, 0.45f, 1.6f);
            for (int i = 0; i < layout.Nodes.Count; i++)
            {
                var n = layout.Nodes[i];
                float x = center.x + n.X * zoom, y = center.y + n.Y * zoom;
                float size = (n.Kind == HeroTreeNodeKind.Small ? 36f : n.Kind == HeroTreeNodeKind.Notable ? 46f : 58f) * sizeScale;
                // Includes the halo, rank badge and the label below the disc.
                float half = size * 0.5f;
                if (!StarMapMath.IsVisible(x, y, half, nodeBounds)) continue;
                NodeRects[i] = new Rect(x - half, y - half, size, size);
                VisibleNodes[NodeCount++] = i;
                long cell = StarMapMath.LabelCell(x, y + half + 7f, LabelCellWidth, LabelCellHeight);
                LabelCells[i] = cell;
                bool named = n.Kind == HeroTreeNodeKind.Keystone || (n.Talent != null && PairCombos.ForBridge(n.Talent.Id) != null)
                    ? labelZoom >= 0.2f : n.Kind != HeroTreeNodeKind.Small ? labelZoom >= 0.55f : labelZoom >= 1f;
                Named[i] = named && _labelCells.Add(cell);
            }
            for (int i = 0; i < layout.Edges.Count; i++)
            {
                var edge = layout.Edges[i];
                var a = layout.Nodes[edge.A];
                var b = layout.Nodes[edge.B];
                if (!StarMapMath.TryClipEdge(new StarMapPoint(center.x + a.X * zoom, center.y + a.Y * zoom),
                    new StarMapPoint(center.x + b.X * zoom, center.y + b.Y * zoom), bounds, out var from, out var to)) continue;
                float dx = to.X - from.X, dy = to.Y - from.Y;
                float length = Mathf.Sqrt(dx * dx + dy * dy);
                if (length < 0.5f) continue;
                VisibleEdges[EdgeCount++] = new EdgeQuad(edge.A, edge.B, new Vector2(from.X, from.Y), length, dx / length, dy / length);
            }
        }

        internal int Hit(Vector2 mouse)
        {
            for (int v = NodeCount - 1; v >= 0; v--)
            {
                int i = VisibleNodes[v];
                if (NodeRects[i].Contains(mouse)) return i;
            }
            return -1;
        }

        internal static Vector2 UnclippedOrigin(Matrix4x4 baseMatrix)
        {
            GUI.matrix = Matrix4x4.identity;
            try { return Unclip(Vector2.zero); }
            finally { GUI.matrix = baseMatrix; }
        }

        internal static void DrawEdge(EdgeQuad edge, Vector2 unclippedOrigin, Matrix4x4 baseMatrix, Color color)
        {
            Vector2 pivot = unclippedOrigin + edge.Start;
            // T(pivot) * R * T(-pivot), composed on the RIGHT of the base transform.
            // GUI adds the group/window origin before this matrix; the pivot must use those same units.
            var rotation = Matrix4x4.identity;
            rotation.m00 = edge.Cosine; rotation.m01 = -edge.Sine;
            rotation.m10 = edge.Sine; rotation.m11 = edge.Cosine;
            rotation.m03 = pivot.x - edge.Cosine * pivot.x + edge.Sine * pivot.y;
            rotation.m13 = pivot.y - edge.Sine * pivot.x - edge.Cosine * pivot.y;
            var oldColor = GUI.color;
            try
            {
                GUI.matrix = baseMatrix * rotation;
                GUI.color = color;
                GUI.DrawTexture(new Rect(edge.Start.x, edge.Start.y - 1f, edge.Length, 2f), Texture2D.whiteTexture);
            }
            finally { GUI.matrix = baseMatrix; GUI.color = oldColor; }
        }
    }
}
