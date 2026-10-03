using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed partial class HeroTreeLayout
    {
        private sealed class PlacementGrid
        {
            private readonly Dictionary<long, List<StarMapPoint>> cells = new Dictionary<long, List<StarMapPoint>>();
            private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
            public void Add(float x, float y)
            {
                long key = Key((int)Math.Floor(x / MinimumSpacing), (int)Math.Floor(y / MinimumSpacing));
                if (!cells.TryGetValue(key, out var points)) cells.Add(key, points = new List<StarMapPoint>());
                points.Add(new StarMapPoint(x, y));
            }
            public bool Free(float x, float y)
            {
                int column = (int)Math.Floor(x / MinimumSpacing), row = (int)Math.Floor(y / MinimumSpacing);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (cells.TryGetValue(Key(column + dx, row + dy), out var points))
                            foreach (var point in points)
                            {
                                float px = point.X - x, py = point.Y - y;
                                if (px * px + py * py < MinimumSpacing * MinimumSpacing) return false;
                            }
                return true;
            }
        }

        private static void PlaceClusters(List<HeroTreeNode> nodes, List<List<int>> neighbors,
            List<HeroTreeEdge> edges, IReadOnlyList<TalentDef> talents)
        {
            var grid = new PlacementGrid();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                indices.Add(node.Id, indices.Count);
                grid.Add(node.X, node.Y);
            }
            int Add(TalentDef talent, float x, float y)
            {
                var adjacent = new List<int>();
                int index = nodes.Count;
                neighbors.Add(adjacent);
                nodes.Add(new HeroTreeNode(talent.Id, x, y, Kind(talent), talent, adjacent));
                indices.Add(talent.Id, index);
                grid.Add(x, y);
                return index;
            }
            void Join(int a, int b)
            {
                if (neighbors[a].Contains(b)) return;
                neighbors[a].Add(b);
                neighbors[b].Add(a);
                edges.Add(new HeroTreeEdge(a, b));
            }

            // Outer anchors are real, refundable stat stars. Attach each to the closest trunk tip.
            int originalCount = nodes.Count;
            int outerOrder = 0;
            foreach (var talent in talents)
            {
                if (!talent.IsOuterAnchor) continue;
                double angle = Angle(outerOrder++, 8);
                float x, y;
                float radius = 1350f;
                do
                {
                    x = (float)(radius * Math.Cos(angle));
                    y = (float)(radius * Math.Sin(angle));
                    radius += MinimumSpacing;
                } while (!grid.Free(x, y));
                int closest = -1;
                float best = float.MaxValue;
                for (int i = 1; i < originalCount; i++)
                {
                    var candidate = nodes[i];
                    if (candidate.Talent.RouteId == null || candidate.Talent.RouteOrder < 7) continue;
                    float dx = candidate.X - x, dy = candidate.Y - y;
                    float distance = dx * dx + dy * dy;
                    if (distance < best) { best = distance; closest = i; }
                }
                if (closest < 0) throw new InvalidOperationException("Outer anchor has no trunk tip: " + talent.Id);
                Join(closest, Add(talent, x, y));
            }

            var clusters = new List<List<TalentDef>>();
            var byCluster = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var talent in talents)
            {
                if (talent.Cluster == null) continue;
                if (!byCluster.TryGetValue(talent.Cluster.Id, out int group))
                {
                    group = clusters.Count;
                    byCluster.Add(talent.Cluster.Id, group);
                    clusters.Add(new List<TalentDef>());
                }
                if (!indices.ContainsKey(talent.Id)) clusters[group].Add(talent);
            }
            foreach (var group in clusters)
            {
                group.Sort((a, b) => a.ClusterOrder.CompareTo(b.ClusterOrder));
                if (group.Count == 0) continue;
                var cluster = group[0].Cluster;
                if (!indices.TryGetValue(cluster.Anchor, out int anchor))
                    throw new InvalidOperationException("Cluster anchor missing from layout: " + cluster.Id);
                var offsets = ClusterOffsets(group, cluster.Shape);
                var positions = new StarMapPoint[group.Count];
                var origin = nodes[anchor];
                double outward = Math.Atan2(origin.Y, origin.X);
                bool placed = false;
                // Search nearest free shells around the anchor, retaining the whole shape as one rigid unit.
                for (int shell = 1; !placed; shell++)
                {
                    int directions = 24 + shell * 8;
                    for (int direction = 0; direction < directions && !placed; direction++)
                    {
                        int signed = direction == 0 ? 0 : (direction + 1) / 2 * (direction % 2 == 1 ? 1 : -1);
                        double angle = outward + signed * 2 * Math.PI / directions;
                        double cos = Math.Cos(angle), sin = Math.Sin(angle);
                        float cx = origin.X + (float)(shell * MinimumSpacing * 1.2f * cos);
                        float cy = origin.Y + (float)(shell * MinimumSpacing * 1.2f * sin);
                        bool clear = true;
                        for (int i = 0; i < offsets.Length; i++)
                        {
                            float x = cx + (float)(offsets[i].X * cos - offsets[i].Y * sin);
                            float y = cy + (float)(offsets[i].X * sin + offsets[i].Y * cos);
                            if (!grid.Free(x, y)) { clear = false; break; }
                            positions[i] = new StarMapPoint(x, y);
                        }
                        placed = clear;
                    }
                }
                var added = new int[group.Count];
                for (int i = 0; i < group.Count; i++) added[i] = Add(group[i], positions[i].X, positions[i].Y);
                if (cluster.AuthoredEdges != null) continue;
                Join(anchor, added[0]);
                if (cluster.Shape == ClusterShape.Fan)
                {
                    var entries = new List<int>();
                    var notables = new List<int>();
                    var choices = new List<int>();
                    for (int i = 0; i < group.Count; i++)
                        if (group[i].IsChoice) choices.Add(added[i]);
                        else if (group[i].ClusterStar.Kind == ClusterStarKind.Notable) notables.Add(added[i]);
                        else entries.Add(added[i]);
                    // Follow the entry arc, then branch into each notable and converge on each choice.
                    int entry = added[0];
                    foreach (int next in entries)
                    {
                        if (next != entry) Join(entry, next);
                        entry = next;
                    }
                    foreach (int notable in notables) if (notable != entry) Join(entry, notable);
                    foreach (int choice in choices)
                    {
                        if (notables.Count == 0) { if (choice != entry) Join(entry, choice); }
                        else foreach (int notable in notables) if (notable != choice) Join(notable, choice);
                    }
                }
                else
                {
                    for (int i = 1; i < added.Length; i++) Join(added[i - 1], added[i]);
                    if (cluster.Shape == ClusterShape.Ring && added.Length > 2) Join(added[added.Length - 1], added[0]);
                }
            }
            // Explicit topology replaces inferred shape edges; cross-cluster endpoints resolve after placement.
            foreach (var talent in talents)
            {
                if (talent.AuthoredStar == null) continue;
                foreach (var edge in talent.AuthoredStar.Edges)
                {
                    if (!indices.TryGetValue(edge.From, out int from) || !indices.TryGetValue(edge.To, out int to))
                        throw new InvalidOperationException("Authored edge endpoint missing: " + talent.Id);
                    Join(from, to);
                }
            }
        }

        private static StarMapPoint[] ClusterOffsets(List<TalentDef> stars, ClusterShape shape)
        {
            const float step = MinimumSpacing * 1.25f;
            var offsets = new StarMapPoint[stars.Count];
            if (shape == ClusterShape.Chain)
            {
                for (int i = 0; i < offsets.Length; i++) offsets[i] = new StarMapPoint(i * step, 0);
                return offsets;
            }
            if (shape == ClusterShape.Ring)
            {
                if (stars.Count == 1) return offsets;
                float radius = (float)(step / (2 * Math.Sin(Math.PI / stars.Count)));
                for (int i = 0; i < offsets.Length; i++)
                {
                    double angle = Math.PI + 2 * Math.PI * i / stars.Count;
                    offsets[i] = new StarMapPoint(radius + (float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
                }
                return offsets;
            }
            var layers = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (int i = 0; i < stars.Count; i++)
                layers[stars[i].IsChoice ? 2 : stars[i].ClusterStar.Kind == ClusterStarKind.Notable ? 1 : 0].Add(i);
            float radiusBase = 160f;
            foreach (var layer in layers) radiusBase = Math.Max(radiusBase, layer.Count * step);
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var layer = layers[layerIndex];
                float radius = radiusBase + layerIndex * step;
                for (int i = 0; i < layer.Count; i++)
                {
                    double angle = layer.Count == 1 ? 0 : -0.6 + 1.2 * i / (layer.Count - 1);
                    offsets[layer[i]] = new StarMapPoint((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
                }
            }
            return offsets;
        }
    }
}
