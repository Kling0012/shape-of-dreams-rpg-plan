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

        /// <summary>
        /// Two passes: the first finds every cluster star's position (bridges first, keystones last, so short entries win the
        /// nearby space); the second adds the stars in author order at those positions, so node order and edge order stay stable.
        /// </summary>
        private static void PlaceClusters(List<HeroTreeNode> nodes, List<List<int>> neighbors,
            List<HeroTreeEdge> edges, IReadOnlyList<TalentDef> talents)
        {
            var recorded = new Dictionary<string, StarMapPoint>(StringComparer.Ordinal);
            var scratchNodes = new List<HeroTreeNode>(nodes);
            var scratchNeighbors = new List<List<int>>(neighbors.Count);
            foreach (var list in neighbors) scratchNeighbors.Add(new List<int>(list));
            PlaceClusters(scratchNodes, scratchNeighbors, new List<HeroTreeEdge>(edges), talents, null, recorded);
            PlaceClusters(nodes, neighbors, edges, talents, recorded, null);
        }

        private static void PlaceClusters(List<HeroTreeNode> nodes, List<List<int>> neighbors,
            List<HeroTreeEdge> edges, IReadOnlyList<TalentDef> talents,
            Dictionary<string, StarMapPoint> fixedPositions, Dictionary<string, StarMapPoint> recorded)
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
                if (fixedPositions != null && fixedPositions.TryGetValue(talent.Id, out var known)) { x = known.X; y = known.Y; }
                recorded?.Add(talent.Id, new StarMapPoint(x, y));
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
            Dictionary<string, TalentDef> definitions = null;
            bool HasExplicitOuterAttachment(TalentDef talent)
            {
                if (talent.AuthoredStar == null) return false;
                foreach (var edge in talent.AuthoredStar.Edges)
                {
                    string other = edge.From == talent.Id ? edge.To : edge.To == talent.Id ? edge.From : null;
                    if (other == null) continue;
                    if (definitions == null)
                    {
                        definitions = new Dictionary<string, TalentDef>(talents.Count, StringComparer.Ordinal);
                        foreach (var definition in talents) definitions.Add(definition.Id, definition);
                    }
                    if (definitions.TryGetValue(other, out var endpoint)
                        && (endpoint.AuthoredStar?.ClusterId ?? endpoint.Cluster?.Id) != talent.AuthoredStar.ClusterId)
                        return true;
                }
                return false;
            }

            // Only implicit roots attach to the closest trunk; authored roots retain their exact access edge.
            int originalCount = nodes.Count;
            int outerOrder = 0;
            // Baseline and retained-legacy outer anchors keep the ring slots saved profiles grew around; newly authored
            // anchors search for space only afterwards, so registering a generated map never moves a shipped star.
            void PlaceOuterAnchors(bool newlyAuthored)
            {
                foreach (var talent in talents)
                {
                    if (!talent.IsOuterAnchor) continue;
                    if ((talent.AuthoredStar != null && !talent.AuthoredStar.RetainedLegacy) != newlyAuthored) continue;
                    double angle = Angle(outerOrder++, 8);
                    float x, y;
                    float radius = 1350f;
                    do
                    {
                        x = (float)(radius * Math.Cos(angle));
                        y = (float)(radius * Math.Sin(angle));
                        radius += MinimumSpacing;
                    } while (!grid.Free(x, y));
                    if (HasExplicitOuterAttachment(talent))
                    {
                        Add(talent, x, y);
                        continue;
                    }
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
            }
            PlaceOuterAnchors(false);
            List<List<TalentDef>> ClusterGroups(bool retained)
            {
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
            for (int i = clusters.Count - 1; i >= 0; i--)
                if (clusters[i].Count == 0 || (clusters[i][0].AuthoredStar == null || clusters[i][0].AuthoredStar.RetainedLegacy) != retained) clusters.RemoveAt(i);
            // Bridge clusters claim the free space next to their ring star first, so their entry line stays short and the
            // large memory clusters flow around them; keystones are placed last, each beside its own anchor.
            var ordered = new List<List<TalentDef>>(clusters.Count);
            // A cluster whose entry star is wired to three or more stars of a route needs one place close to all of them, so it
            // claims space before the bridges that would otherwise fence that route in.
            for (int pass = 0; pass < 4; pass++)
                foreach (var candidate in clusters)
                {
                    if (candidate.Count == 0) { if (pass == 2) ordered.Add(candidate); continue; }
                    var kind = candidate[0].Cluster.Region.Kind;
                    int rank = kind == ClusterRegionKind.Bridge ? 1 : kind == ClusterRegionKind.Keystone ? 3 : 2;
                    if (rank == 2 && OutsideEdgeStars(candidate, indices).Count >= 3) rank = 0;
                    if (rank == pass) ordered.Add(candidate);
                }
            return fixedPositions == null ? ordered : clusters;
            }

            var keystonePoints = new List<StarMapPoint>();
            void PlaceClusterGroups(List<List<TalentDef>> clusters)
            {
            bool AnchorsReady(List<TalentDef> group)
            {
                if (IsKeystoneGroup(group))
                {
                    foreach (var star in group) if (!indices.ContainsKey(KeystoneAnchor(star))) return false;
                    return true;
                }
                return indices.ContainsKey(group[0].Cluster.Anchor);
            }
            for (int groupIndex = 0; groupIndex < clusters.Count; groupIndex++)
            {
                // Preserve author order among ready groups; an anchor may live in a later cluster.
                int ready = groupIndex;
                while (ready < clusters.Count && clusters[ready].Count != 0 && !AnchorsReady(clusters[ready])) ready++;
                if (ready == clusters.Count)
                    throw new InvalidOperationException("Cluster anchor is missing or cyclic: " + clusters[groupIndex][0].Cluster.Id);
                var group = clusters[ready];
                for (int i = ready; i > groupIndex; i--) clusters[i] = clusters[i - 1];
                clusters[groupIndex] = group;
                group.Sort((a, b) => a.ClusterOrder.CompareTo(b.ClusterOrder));
                if (group.Count == 0) continue;
                var cluster = group[0].Cluster;
                if (IsKeystoneGroup(group))
                {
                    // Authored keystones are independent big stars: each sits beside its own anchor, never in a shared fan.
                    foreach (var star in group)
                    {
                        var home = nodes[indices[KeystoneAnchor(star)]];
                        double heading = Math.Atan2(home.Y, home.X);
                        float kx = 0, ky = 0;
                        bool found = fixedPositions != null;
                        for (int shell = 1; !found; shell++)
                        {
                            int directions = 16 + shell * 8;
                            for (int direction = 0; direction < directions && !found; direction++)
                            {
                                int signed = direction == 0 ? 0 : (direction + 1) / 2 * (direction % 2 == 1 ? 1 : -1);
                                double angle = heading + signed * 2 * Math.PI / directions;
                                kx = home.X + (float)(shell * MinimumSpacing * 1.5f * Math.Cos(angle));
                                ky = home.Y + (float)(shell * MinimumSpacing * 1.5f * Math.Sin(angle));
                                found = grid.Free(kx, ky) && KeystoneFree(keystonePoints, kx, ky);
                            }
                        }
                        keystonePoints.Add(new StarMapPoint(kx, ky));
                        Add(star, kx, ky);
                    }
                    continue;
                }
                if (!indices.TryGetValue(PlacementAnchor(group, indices), out int anchor))
                    throw new InvalidOperationException("Cluster anchor missing from layout: " + cluster.Id);
                var offsets = ClusterOffsets(group, StarShapes(group, cluster));
                // The entry star (the one the anchor's line reaches) is the rigid unit's origin. Rings and chains already start there;
                // a fan's arc lies far from its pivot, which used to push the entry hundreds of units away from the anchor.
                var entryOffset = offsets[0];
                if (Math.Abs(entryOffset.X) > 0.001f || Math.Abs(entryOffset.Y) > 0.001f)
                    for (int i = 0; i < offsets.Length; i++) offsets[i] = new StarMapPoint(offsets[i].X - entryOffset.X, offsets[i].Y - entryOffset.Y);
                var positions = new StarMapPoint[group.Count];
                // Every star the entry star's authored edges reach outside the cluster is a candidate to sit beside; the nearest
                // free shell around any of them wins (the primary anchor first on ties), so crowded hubs still get a short line.
                var homes = new List<int> { anchor };
                foreach (int other in OutsideEdgeStars(group, indices)) if (!homes.Contains(other)) homes.Add(other);
                bool placed = fixedPositions != null;
                // Search nearest free shells around the anchors, retaining the whole shape as one rigid unit.
                for (int shell = 1; !placed; shell++)
                {
                    foreach (int home in homes)
                    {
                    var origin = nodes[home];
                    double outward = Math.Atan2(origin.Y, origin.X);
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
                    if (placed) break;
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
            }
            PlaceClusterGroups(ClusterGroups(true));
            PlaceOuterAnchors(true);
            PlaceClusterGroups(ClusterGroups(false));
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

        /// <summary>Keystones are drawn larger than other stars, so they keep twice the ordinary spacing from each other.</summary>
        public const float KeystoneSpacing = MinimumSpacing * 2f;

        private static bool IsKeystoneGroup(List<TalentDef> group) =>
            group.Count > 0 && group[0].Cluster.Region.Kind == ClusterRegionKind.Keystone && group[0].AuthoredStar != null;

        /// <summary>
        /// The star a cluster is placed beside. Authored clusters record one group-level anchor (a route's first star), but the
        /// entry line really leaves from the already placed star the entry star's authored edge reaches outside the cluster (a route's fourth or
        /// seventh star); placing beside that star keeps the entry line short. Otherwise the cluster's own anchor.
        /// </summary>
        private static string PlacementAnchor(List<TalentDef> group, Dictionary<string, int> placed)
        {
            var entry = group[0];
            if (entry.AuthoredStar == null) return entry.Cluster.Anchor;
            foreach (var edge in entry.AuthoredStar.Edges)
            {
                string other = edge.To == entry.Id ? edge.From : edge.From == entry.Id ? edge.To : null;
                if (other == null || !placed.TryGetValue(other, out _)) continue;
                bool inside = false;
                foreach (var star in group) if (star.Id == other) { inside = true; break; }
                if (!inside) return other;
            }
            return entry.Cluster.Anchor;
        }

        /// <summary>Already placed stars outside the group that the entry star's authored edges reach, in edge order.</summary>
        private static List<int> OutsideEdgeStars(List<TalentDef> group, Dictionary<string, int> placed)
        {
            var result = new List<int>();
            var entry = group[0];
            if (entry.AuthoredStar == null) return result;
            foreach (var edge in entry.AuthoredStar.Edges)
            {
                string other = edge.To == entry.Id ? edge.From : edge.From == entry.Id ? edge.To : null;
                if (other == null || !placed.TryGetValue(other, out int index)) continue;
                bool inside = false;
                foreach (var star in group) if (star.Id == other) { inside = true; break; }
                if (!inside && !result.Contains(index)) result.Add(index);
            }
            return result;
        }

        private static string KeystoneAnchor(TalentDef star) => star.AuthoredStar.AnchorId ?? star.Cluster.Anchor;

        private static bool KeystoneFree(List<StarMapPoint> placed, float x, float y)
        {
            foreach (var point in placed)
            {
                float dx = point.X - x, dy = point.Y - y;
                if (dx * dx + dy * dy < KeystoneSpacing * KeystoneSpacing) return false;
            }
            return true;
        }

        /// <summary>Per-star geometry: authored stars carry their own shape, others use their cluster's shape.</summary>
        private static ClusterShape[] StarShapes(List<TalentDef> stars, StarClusterDef cluster)
        {
            var shapes = new ClusterShape[stars.Count];
            for (int i = 0; i < stars.Count; i++)
                shapes[i] = stars[i].AuthoredStar != null ? stars[i].AuthoredStar.Shape : cluster.Shape;
            return shapes;
        }

        /// <summary>
        /// Compound geometry: maximal consecutive same-shape runs become segments. Every segment keeps its
        /// single-shape offsets; segments are appended along +x with one step of clearance, so stars of
        /// different segments are always at least one step apart and the unit stays rigid and deterministic.
        /// Uniform clusters keep their historic single-shape geometry.
        /// </summary>
        private static StarMapPoint[] ClusterOffsets(List<TalentDef> stars, ClusterShape[] shapes)
        {
            if (shapes.Length == 0) return new StarMapPoint[stars.Count];
            bool uniform = true;
            for (int i = 1; i < shapes.Length; i++) if (shapes[i] != shapes[0]) uniform = false;
            if (uniform) return SegmentOffsets(stars, shapes[0]);
            const float step = MinimumSpacing * 1.25f;
            var offsets = new StarMapPoint[stars.Count];
            var segment = new List<TalentDef>(stars.Count);
            float advance = 0f;
            int start = 0;
            while (start < stars.Count)
            {
                int end = start + 1;
                while (end < stars.Count && shapes[end] == shapes[start]) end++;
                segment.Clear();
                for (int i = start; i < end; i++) segment.Add(stars[i]);
                var local = SegmentOffsets(segment, shapes[start]);
                float width = 0f;
                for (int i = 0; i < local.Length; i++)
                {
                    if (local[i].X > width) width = local[i].X;
                    offsets[start + i] = new StarMapPoint(advance + local[i].X, local[i].Y);
                }
                advance += width + step;
                start = end;
            }
            return offsets;
        }

        private static StarMapPoint[] SegmentOffsets(List<TalentDef> stars, ClusterShape shape)
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
