using System;

namespace SodRpg.Core.Game
{
    /// <summary>A position in star-map layout or screen/UI units, as chosen by the caller.</summary>
    public readonly struct StarMapPoint
    {
        public StarMapPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
    }

    /// <summary>Inclusive viewport limits in the same screen/UI units as the tested geometry.</summary>
    public readonly struct StarMapBounds
    {
        public StarMapBounds(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public float XMin { get; }
        public float YMin { get; }
        public float XMax { get; }
        public float YMax { get; }
    }

    /// <summary>Allocation-free search and geometry operations for the star-map UI.</summary>
    public static class StarMapMath
    {
        /// <summary>Returns the screen/UI pan offset that centers a layout position at the given zoom.</summary>
        public static StarMapPoint PanToNode(float x, float y, float zoom)
        {
            return new StarMapPoint(-x * zoom, -y * zoom);
        }

        /// <summary>Matches a nonempty query against a nullable name or effect description using ordinal case-insensitive text.</summary>
        public static bool Matches(string name, string description, string query)
        {
            return !string.IsNullOrEmpty(query)
                && ((name != null && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (description != null && description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <summary>Tests inclusive overlap of a node's bounding box and viewport, all in screen/UI units.</summary>
        public static bool IsVisible(float x, float y, float radius, StarMapBounds viewport)
        {
            return x + radius >= viewport.XMin && x - radius <= viewport.XMax
                && y + radius >= viewport.YMin && y - radius <= viewport.YMax;
        }

        /// <summary>Clips a screen/UI line segment to the inclusive viewport, preserving endpoint order.</summary>
        public static bool TryClipEdge(StarMapPoint a, StarMapPoint b, StarMapBounds viewport,
            out StarMapPoint clippedA, out StarMapPoint clippedB)
        {
            clippedA = default;
            clippedB = default;

            if (Math.Max(a.X, b.X) < viewport.XMin || Math.Min(a.X, b.X) > viewport.XMax
                || Math.Max(a.Y, b.Y) < viewport.YMin || Math.Min(a.Y, b.Y) > viewport.YMax)
                return false;

            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float start = 0f;
            float end = 1f;
            if (!ClipBoundary(-dx, a.X - viewport.XMin, ref start, ref end)
                || !ClipBoundary(dx, viewport.XMax - a.X, ref start, ref end)
                || !ClipBoundary(-dy, a.Y - viewport.YMin, ref start, ref end)
                || !ClipBoundary(dy, viewport.YMax - a.Y, ref start, ref end))
                return false;

            clippedA = start == 0f ? a : new StarMapPoint(a.X + start * dx, a.Y + start * dy);
            clippedB = end == 1f ? b : new StarMapPoint(a.X + end * dx, a.Y + end * dy);
            return true;
        }

        /// <summary>Packs signed floor-based 90-by-18 screen/UI label cells into one collision-free key.</summary>
        public static long LabelCell(float x, float y)
        {
            int column = (int)Math.Floor(x / 90f);
            int row = (int)Math.Floor(y / 18f);
            return ((long)column << 32) | (uint)row;
        }

        /// <summary>Same packing with an explicit cell size (larger label fonts need larger cells).</summary>
        public static long LabelCell(float x, float y, float cellWidth, float cellHeight)
        {
            if (!(cellWidth > 0f) || !(cellHeight > 0f)) throw new ArgumentOutOfRangeException(nameof(cellWidth));
            int column = (int)Math.Floor(x / cellWidth);
            int row = (int)Math.Floor(y / cellHeight);
            return ((long)column << 32) | (uint)row;
        }

        /// <summary>Creates the internal deterministic stress layout: 800 nodes on a centered 40-by-20 grid, 95 layout units apart.</summary>
        internal static StarMapPoint[] BuildSyntheticLayout()
        {
            var nodes = new StarMapPoint[800];
            for (int row = 0; row < 20; row++)
            {
                for (int column = 0; column < 40; column++)
                    nodes[row * 40 + column] = new StarMapPoint((column - 19.5f) * 95f, (row - 9.5f) * 95f);
            }
            return nodes;
        }

        private static bool ClipBoundary(float direction, float distance, ref float start, ref float end)
        {
            if (direction == 0f)
                return distance >= 0f;

            float ratio = distance / direction;
            if (direction < 0f)
            {
                if (ratio > end)
                    return false;
                if (ratio > start)
                    start = ratio;
            }
            else
            {
                if (ratio < start)
                    return false;
                if (ratio < end)
                    end = ratio;
            }
            return true;
        }
    }
}
