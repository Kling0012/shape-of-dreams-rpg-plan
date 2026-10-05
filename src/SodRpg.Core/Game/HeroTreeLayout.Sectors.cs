using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed partial class HeroTreeLayout
    {
        private const float ShapeStep = 100f;
        private const float HaloPadding = 37f; // Renderer: keystone radius 29 + halo margin 8.
        private const float HaloGap = 16f;

        private sealed class LayoutGroup
        {
            public readonly List<int> Stars = new List<int>();
            public readonly List<int> Outside = new List<int>();
            public readonly List<HeroTreeEdge> Flow = new List<HeroTreeEdge>();
            public ClusterRegionKind Region;
            public int Sector;
            public double Heading, SectorHeading;
            public float X, Y, Radius, Floor;
            public StarMapPoint[] Offsets;
            public string Id;
        }

        private static void PlaceSectors(List<HeroTreeNode> nodes, List<List<int>> neighbors, List<HeroTreeEdge> edges)
        {
            var routes = new List<string>();
            var routeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var memories = new Dictionary<string, int>(StringComparer.Ordinal);
            var indices = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                indices.Add(nodes[i].Id, i);
                var talent = nodes[i].Talent;
                if (talent?.RouteId == null || routeIndex.ContainsKey(talent.RouteId)) continue;
                int route = routes.Count;
                routes.Add(talent.RouteId);
                routeIndex.Add(talent.RouteId, route);
                if (talent.RouteMemory != null) memories[talent.RouteMemory] = route;
            }
            if (routes.Count == 0) return;
            var owner = new int[nodes.Count];
            var queue = new int[nodes.Count];
            int count = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var talent = nodes[i].Talent;
                owner[i] = -1;
                if (talent?.RouteId != null && routeIndex.TryGetValue(talent.RouteId, out int route)
                    || talent?.Cluster?.Region.Kind == ClusterRegionKind.Memory && talent.RouteMemory != null
                        && memories.TryGetValue(talent.RouteMemory, out route))
                { owner[i] = route; queue[count++] = i; }
            }
            // Unowned outer stars inherit the route actually providing their access edge, rather
            // than an arbitrary global ring slot. Memory stars always retain their explicit owner.
            for (int cursor = 0; cursor < count; cursor++)
                foreach (int next in neighbors[queue[cursor]])
                {
                    if (next == 0 || owner[next] >= 0) continue;
                    owner[next] = owner[queue[cursor]];
                    queue[count++] = next;
                }
            for (int i = 0; i < owner.Length; i++) if (owner[i] < 0) owner[i] = 0;
            var weights = new int[routes.Count, routes.Count];
            foreach (var edge in edges)
            {
                int a = owner[edge.A], b = owner[edge.B];
                if (a == b || edge.A == 0 || edge.B == 0) continue;
                weights[a, b]++;
                weights[b, a]++;
            }
            // A bridge's two access routes count even when its own stars inherited only one route.
            for (int i = 1; i < nodes.Count; i++)
            {
                if (nodes[i].Talent?.IsDreamRing != true) continue;
                var adjacent = neighbors[i];
                for (int a = 0; a < adjacent.Count; a++)
                    for (int b = a + 1; b < adjacent.Count; b++)
                    {
                        int ra = owner[adjacent[a]], rb = owner[adjacent[b]];
                        if (ra == rb) continue;
                        weights[ra, rb] += 8;
                        weights[rb, ra] += 8;
                    }
            }
            var order = SectorOrder(weights, routes.Count);
            var slot = new int[routes.Count];
            for (int i = 0; i < order.Length; i++) slot[order[i]] = i;
            double half = Math.PI / routes.Count;
            double Heading(int route) => Angle(slot[route], routes.Count);
            void Move(int index, float x, float y)
            {
                var old = nodes[index];
                nodes[index] = new HeroTreeNode(old.Id, x, y, old.Kind, old.Talent, neighbors[index]);
            }
            void Polar(int index, float radius, double angle) => Move(index, (float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));

            var groups = new List<LayoutGroup>();
            var groupById = new Dictionary<string, LayoutGroup>(StringComparer.Ordinal);
            var groupOf = new LayoutGroup[nodes.Count];
            for (int i = 1; i < nodes.Count; i++)
            {
                var definition = nodes[i].Talent.Cluster;
                if (definition == null || nodes[i].Talent.IsKeystone) continue;
                if (!groupById.TryGetValue(definition.Id, out var group))
                {
                    group = new LayoutGroup { Id = definition.Id, Region = definition.Region.Kind, Sector = owner[i] };
                    groups.Add(group);
                    groupById.Add(definition.Id, group);
                }
                group.Stars.Add(i);
                groupOf[i] = group;
            }
            foreach (var group in groups)
            {
                group.Stars.Sort((a, b) => nodes[a].Talent.ClusterOrder.CompareTo(nodes[b].Talent.ClusterOrder));
                foreach (int star in group.Stars)
                    foreach (int other in neighbors[star])
                        if (groupOf[other] != group && !group.Outside.Contains(other)) group.Outside.Add(other);
                group.Offsets = GroupOffsets(group, nodes, neighbors, indices);
                foreach (int star in group.Stars)
                {
                    var definition = nodes[star].Talent.AuthoredStar;
                    if (definition == null) continue;
                    foreach (string required in definition.RequiredStarIds)
                        if (indices.TryGetValue(required, out int parent)) group.Flow.Add(new HeroTreeEdge(parent, star));
                    foreach (string required in definition.RequiredAnyStarIds)
                        if (indices.TryGetValue(required, out int parent)) group.Flow.Add(new HeroTreeEdge(parent, star));
                }
                group.Radius = HaloPadding;
                foreach (var point in group.Offsets)
                    group.Radius = Math.Max(group.Radius, (float)Math.Sqrt(point.X * point.X + point.Y * point.Y) + HaloPadding);
                if (group.Region == ClusterRegionKind.Memory) group.Radius = (group.Radius - HaloPadding) * 1.15f + HaloPadding;
                group.Heading = Heading(group.Sector);
                group.SectorHeading = group.Heading;
            }
            // Reserve a boundary lane before packing; route order increases in 100-unit radial steps.
            for (int i = 1; i < nodes.Count; i++)
            {
                var talent = nodes[i].Talent;
                if (talent.RouteId != null)
                    Polar(i, 480f + (talent.RouteOrder - 1) * 100f, Heading(owner[i]) - half * 0.88);
            }
            var placed = new List<LayoutGroup>(groups.Count);
            groups.Sort((a, b) =>
            {
                int region = a.Region.CompareTo(b.Region);
                if (region != 0) return region;
                int sector = slot[a.Sector].CompareTo(slot[b.Sector]);
                return sector != 0 ? sector : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            // Ring access stars sit on the boundary between their actual route endpoints.
            for (int i = 1; i < nodes.Count; i++)
            {
                if (!nodes[i].Talent.IsDreamRing) continue;
                float x = 0, y = 0, radius = 0; int endpoints = 0;
                foreach (int next in neighbors[i])
                {
                    if (nodes[next].Talent?.RouteId == null) continue;
                    double angle = Heading(owner[next]);
                    x += (float)Math.Cos(angle); y += (float)Math.Sin(angle);
                    radius += Distance(nodes[next].X, nodes[next].Y); endpoints++;
                }
                if (endpoints > 0)
                {
                    double angle = Math.Atan2(y, x);
                    PlaceLoose(i, (float)(radius / endpoints * Math.Cos(angle)), (float)(radius / endpoints * Math.Sin(angle)));
                }
            }
            foreach (var group in groups)
            {
                if (group.Region != ClusterRegionKind.Bridge) continue;
                // Bridge groups may straddle a boundary; they do not occupy a foreign memory sector.
                float x = 0, y = 0; int endpoints = 0;
                foreach (int other in group.Outside)
                    if (groupOf[other] == null) { x += nodes[other].X; y += nodes[other].Y; endpoints++; }
                if (endpoints > 0) group.Heading = Math.Atan2(y / endpoints, x / endpoints);
                group.Floor = 680;
                Pack(group, 680, endpoints == 0 ? 0 : Distance(x / endpoints, y / endpoints));
            }
            foreach (var group in groups)
            {
                if (group.Region != ClusterRegionKind.Memory) continue;
                float floor = 680;
                foreach (int other in group.Outside)
                    if (nodes[other].Talent?.RouteId != null) floor = Math.Max(floor, Distance(nodes[other].X, nodes[other].Y) + HaloGap);
                group.Floor = floor;
                Pack(group, floor, 0);
            }
            var routeEnd = new float[routes.Count];
            for (int i = 0; i < routes.Count; i++) routeEnd[i] = 900;
            foreach (var group in placed)
            {
                if (group.Region != ClusterRegionKind.Memory) continue;
                float radius = Distance(group.X, group.Y);
                routeEnd[group.Sector] = Math.Max(routeEnd[group.Sector], radius + group.Radius);
            }
            for (int i = 1; i < nodes.Count; i++)
                if (nodes[i].Talent.IsOuterAnchor && groupOf[i] == null)
                {
                    double angle = Heading(owner[i]);
                    float radius = routeEnd[owner[i]] + 100;
                    PlaceLoose(i, (float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
                }
            foreach (var group in groups)
            {
                if (group.Region != ClusterRegionKind.Outer) continue;
                group.Floor = routeEnd[group.Sector] + HaloGap;
                Pack(group, group.Floor, 0);
            }
            // Independent keystones stay near their actual destination, outside every group's halo.
            var keys = new List<int>();
            for (int i = 1; i < nodes.Count; i++)
            {
                if (!nodes[i].Talent.IsKeystone || nodes[i].Talent.AuthoredStar == null) continue;
                int anchor = indices[KeystoneAnchor(nodes[i].Talent)];
                float homeX = nodes[anchor].X, homeY = nodes[anchor].Y;
                double heading = Math.Atan2(homeY, homeX);
                bool found = false;
                for (int shell = 1; !found; shell++)
                    for (int direction = 0; direction < 32 && !found; direction++)
                    {
                        double angle = heading + direction * Math.PI / 16;
                        float x = homeX + (float)(shell * 100 * Math.Cos(angle));
                        float y = homeY + (float)(shell * 100 * Math.Sin(angle));
                        if (!ClearPoint(i, x, y, HaloPadding)) continue;
                        bool clear = true;
                        foreach (int key in keys)
                            if (Distance(nodes[key].X - x, nodes[key].Y - y) < KeystoneSpacing) { clear = false; break; }
                        if (!clear) continue;
                        Move(i, x, y); keys.Add(i); found = true;
                    }
            }
            void PlaceLoose(int index, float homeX, float homeY)
            {
                for (int shell = 0; ; shell++)
                    for (int direction = 0; direction < (shell == 0 ? 1 : 32); direction++)
                    {
                        double angle = direction * Math.PI / 16;
                        float x = homeX + (float)(shell * 80 * Math.Cos(angle));
                        float y = homeY + (float)(shell * 80 * Math.Sin(angle));
                        bool clear = true;
                        foreach (var group in placed)
                            if (Distance(group.X - x, group.Y - y) < group.Radius + 45) { clear = false; break; }
                        if (!clear) continue;
                        for (int j = 0; j < nodes.Count && clear; j++)
                        {
                            if (j == index || groupOf[j] != null || nodes[j].Talent?.AuthoredStar != null && nodes[j].Talent.IsKeystone) continue;
                            if (Distance(nodes[j].X - x, nodes[j].Y - y) < MinimumSpacing + 0.1f) clear = false;
                        }
                        if (clear) { Move(index, x, y); return; }
                    }
            }
            // Layouts are already cached by StarClusters' registered layout and the static fallback.
            // Fixed candidate order (no RNG) makes repeated construction bit-for-bit deterministic.
            RefineGroups(groups, placed, nodes, neighbors, edges, half);
            // Bow routes after group refinement, keeping radius, sector and halo clearance fixed.
            // Accept only fewer star occlusions without adding crossings on affected edges.
            for (int i = 1; i < nodes.Count; i++)
            {
                var talent = nodes[i].Talent;
                if (talent.RouteId == null) continue;
                double bend = Math.Sin((talent.RouteOrder - 1) * Math.PI / 6);
                if (bend < 0.01) continue;
                float radius = Distance(nodes[i].X, nodes[i].Y);
                RouteDefects(i, nodes[i].X, nodes[i].Y, out int oldCrossings, out int bestPasses);
                float bestX = nodes[i].X, bestY = nodes[i].Y;
                for (int trial = 0; trial < 5; trial++)
                {
                    double angle = Heading(owner[i]) - half * 0.88 + (0.1 - trial * 0.02) * bend;
                    float x = (float)(radius * Math.Cos(angle)), y = (float)(radius * Math.Sin(angle));
                    if (!ClearPoint(i, x, y, 45)) continue;
                    RouteDefects(i, x, y, out int crossings, out int passes);
                    if (crossings > oldCrossings || passes >= bestPasses) continue;
                    bestX = x; bestY = y; bestPasses = passes;
                }
                if (bestX != nodes[i].X || bestY != nodes[i].Y) Move(i, bestX, bestY);
            }

            void RouteDefects(int index, float x, float y, out int crossings, out int passes)
            {
                crossings = 0; passes = 0;
                var point = new StarMapPoint(x, y);
                foreach (int next in neighbors[index])
                {
                    var endpoint = new StarMapPoint(nodes[next].X, nodes[next].Y);
                    float minX = Math.Min(x, endpoint.X), maxX = Math.Max(x, endpoint.X);
                    float minY = Math.Min(y, endpoint.Y), maxY = Math.Max(y, endpoint.Y);
                    foreach (var edge in edges)
                    {
                        if (edge.A == index || edge.B == index || edge.A == next || edge.B == next) continue;
                        var a = new StarMapPoint(nodes[edge.A].X, nodes[edge.A].Y);
                        var b = new StarMapPoint(nodes[edge.B].X, nodes[edge.B].Y);
                        if (Math.Max(a.X, b.X) < minX || Math.Min(a.X, b.X) > maxX
                            || Math.Max(a.Y, b.Y) < minY || Math.Min(a.Y, b.Y) > maxY) continue;
                        if (Crosses(point, endpoint, a, b)) crossings++;
                    }
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        if (j == index || j == next) continue;
                        if (nodes[j].X < minX - 30 || nodes[j].X > maxX + 30
                            || nodes[j].Y < minY - 30 || nodes[j].Y > maxY + 30) continue;
                        double starRadius = nodes[j].Kind == HeroTreeNodeKind.Keystone ? 29
                            : nodes[j].Kind == HeroTreeNodeKind.Notable ? 23 : 18;
                        if (SegmentDistanceSquared(point, endpoint, new StarMapPoint(nodes[j].X, nodes[j].Y))
                            < starRadius * starRadius) passes++;
                    }
                }
                double radius = nodes[index].Kind == HeroTreeNodeKind.Keystone ? 29
                    : nodes[index].Kind == HeroTreeNodeKind.Notable ? 23 : 18;
                foreach (var edge in edges)
                {
                    if (edge.A == index || edge.B == index) continue;
                    var a = new StarMapPoint(nodes[edge.A].X, nodes[edge.A].Y);
                    var b = new StarMapPoint(nodes[edge.B].X, nodes[edge.B].Y);
                    if (x < Math.Min(a.X, b.X) - radius || x > Math.Max(a.X, b.X) + radius
                        || y < Math.Min(a.Y, b.Y) - radius || y > Math.Max(a.Y, b.Y) + radius) continue;
                    if (SegmentDistanceSquared(a, b, point) < radius * radius) passes++;
                }
            }

            bool ClearPoint(int index, float x, float y, float padding)
            {
                foreach (var group in placed)
                    if (Distance(group.X - x, group.Y - y) < group.Radius + padding + HaloGap) return false;
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (j == index || groupOf[j] != null || nodes[j].Talent?.AuthoredStar != null && nodes[j].Talent.IsKeystone && !keys.Contains(j)) continue;
                    if (Distance(nodes[j].X - x, nodes[j].Y - y) < MinimumSpacing + 0.1f) return false;
                }
                return true;
            }
            void WriteGroup(LayoutGroup group)
            {
                double cos = Math.Cos(group.Heading), sin = Math.Sin(group.Heading);
                for (int j = 0; j < group.Stars.Count; j++)
                {
                    var p = group.Offsets[j];
                    if (group.Region == ClusterRegionKind.Memory)
                    {
                        // Bend layers onto radial arcs: tangent spread must not put a prerequisite
                        // farther out than its successor. Fan/ring/chain contour is retained locally.
                        float radius = Distance(group.X, group.Y);
                        float target = radius + p.X;
                        double angle = Math.Atan2(p.Y, target);
                        p = new StarMapPoint((float)(target * Math.Cos(angle)) - radius, (float)(target * Math.Sin(angle)));
                    }
                    Move(group.Stars[j], group.X + (float)(p.X * cos - p.Y * sin), group.Y + (float)(p.X * sin + p.Y * cos));
                }
            }
            void Pack(LayoutGroup group, float floor, float preferred)
            {
                float start = Math.Max(floor + group.Radius, (float)((group.Radius + HaloGap) / Math.Sin(half * 0.78)));
                double best = double.MaxValue;
                float bestX = 0, bestY = 0;
                int first = -1;
                // Once a free shell is found, examine only four more shells: bounded work instead
                // of the old expanding 24+8*shell searches around every attachment.
                for (int shell = 0; first < 0 || shell <= first + 4; shell++)
                {
                    float radius = start + shell * 80;
                    for (int direction = 0; direction < 15; direction++)
                    {
                        int signed = direction == 0 ? 0 : (direction + 1) / 2 * (direction % 2 == 1 ? 1 : -1);
                        double delta = signed * half / 9;
                        if (group.Region != ClusterRegionKind.Bridge
                            && Math.Abs(delta) + Math.Asin(Math.Min(1, (group.Radius + HaloGap) / radius)) > half * 0.85) continue;
                        double angle = group.Heading + delta;
                        float x = (float)(radius * Math.Cos(angle)), y = (float)(radius * Math.Sin(angle));
                        bool clear = true;
                        foreach (var other in placed)
                            if (Distance(other.X - x, other.Y - y) < other.Radius + group.Radius + HaloGap) { clear = false; break; }
                        if (!clear) continue;
                        // Keep the trunk's lane and central stars outside the halo.
                        for (int j = 0; j < nodes.Count && clear; j++)
                        {
                            if (groupOf[j] != null || nodes[j].Talent?.AuthoredStar != null && nodes[j].Talent.IsKeystone) continue;
                            if (Distance(nodes[j].X - x, nodes[j].Y - y) < group.Radius + 45) clear = false;
                        }
                        if (!clear) continue;
                        if (first < 0) first = shell;
                        double score = radius * radius * 0.08;
                        foreach (int other in group.Outside)
                        {
                            if (groupOf[other] != null && !placed.Contains(groupOf[other])) continue;
                            double dx = x - nodes[other].X, dy = y - nodes[other].Y;
                            score += dx * dx + dy * dy;
                        }
                        if (preferred > 0) score += (radius - preferred) * (radius - preferred);
                        if (score < best) { best = score; bestX = x; bestY = y; }
                    }
                }
                group.X = bestX; group.Y = bestY;
                group.Heading = Math.Atan2(bestY, bestX);
                placed.Add(group);
                WriteGroup(group);
            }
        }

        private static float Distance(float x, float y) => (float)Math.Sqrt(x * x + y * y);

        private static int[] SectorOrder(int[,] weights, int count)
        {
            var current = new int[count];
            var best = new int[count];
            var used = new bool[count];
            var position = new int[count];
            long bestScore = long.MaxValue;
            current[0] = 0; used[0] = true;
            void Search(int depth)
            {
                if (depth == count)
                {
                    for (int i = 0; i < count; i++) position[current[i]] = i;
                    long score = 0;
                    for (int a = 0; a < count; a++)
                        for (int b = a + 1; b < count; b++)
                        {
                            int distance = Math.Abs(position[a] - position[b]);
                            distance = Math.Min(distance, count - distance);
                            score += weights[a, b] * distance * distance;
                        }
                    if (score < bestScore) { bestScore = score; Array.Copy(current, best, count); }
                    return;
                }
                for (int i = 1; i < count; i++)
                    if (!used[i]) { current[depth] = i; used[i] = true; Search(depth + 1); used[i] = false; }
            }
            Search(1);
            return best;
        }

        private static StarMapPoint[] GroupOffsets(LayoutGroup group, List<HeroTreeNode> nodes,
            List<List<int>> neighbors, Dictionary<string, int> indices)
        {
            int count = group.Stars.Count;
            var result = new StarMapPoint[count];
            var local = new Dictionary<int, int>();
            for (int i = 0; i < count; i++) local.Add(group.Stars[i], i);
            var depth = new int[count];
            // Graph distance handles authored satellites with no prerequisite. Explicit prerequisites
            // then take precedence, so an undirected shortcut cannot reverse the progression.
            var queue = new int[count];
            for (int i = 0; i < count; i++) depth[i] = -1;
            int endQueue = 0;
            for (int i = 0; i < count; i++)
            {
                bool external = i == 0, internalRequirement = false;
                foreach (int other in neighbors[group.Stars[i]]) if (!local.ContainsKey(other)) external = true;
                var definition = nodes[group.Stars[i]].Talent.AuthoredStar;
                if (definition != null)
                {
                    foreach (string required in definition.RequiredStarIds)
                        if (indices.TryGetValue(required, out int parent) && local.ContainsKey(parent)) internalRequirement = true;
                    foreach (string required in definition.RequiredAnyStarIds)
                        if (indices.TryGetValue(required, out int parent) && local.ContainsKey(parent)) internalRequirement = true;
                }
                if (external && !internalRequirement) { depth[i] = 0; queue[endQueue++] = i; }
            }
            if (endQueue == 0) { depth[0] = 0; queue[endQueue++] = 0; }
            for (int cursor = 0; cursor < endQueue; cursor++)
                foreach (int other in neighbors[group.Stars[queue[cursor]]])
                    if (local.TryGetValue(other, out int child) && depth[child] < 0)
                    { depth[child] = depth[queue[cursor]] + 1; queue[endQueue++] = child; }
            for (int i = 0; i < count; i++) if (depth[i] < 0) depth[i] = i;
            for (int pass = 0; pass < count; pass++)
            {
                bool changed = false;
                for (int i = 0; i < count; i++)
                {
                    var definition = nodes[group.Stars[i]].Talent.AuthoredStar;
                    if (definition == null) continue;
                    foreach (string required in definition.RequiredStarIds)
                        if (indices.TryGetValue(required, out int star) && local.TryGetValue(star, out int parent) && depth[i] <= depth[parent])
                        { depth[i] = depth[parent] + 1; changed = true; }
                    foreach (string required in definition.RequiredAnyStarIds)
                        if (indices.TryGetValue(required, out int star) && local.TryGetValue(star, out int parent) && depth[i] <= depth[parent])
                        { depth[i] = depth[parent] + 1; changed = true; }
                }
                if (!changed) break;
            }
            int start = 0;
            float advance = 0;
            while (start < count)
            {
                ClusterShape Shape(int i) => nodes[group.Stars[i]].Talent.AuthoredStar?.Shape ?? nodes[group.Stars[i]].Talent.Cluster.Shape;
                int end = start + 1;
                while (end < count && Shape(end) == Shape(start)) end++;
                var sorted = new List<int>(end - start);
                for (int i = start; i < end; i++) sorted.Add(i);
                sorted.Sort((a, b) => { int d = depth[a].CompareTo(depth[b]); return d != 0 ? d : a.CompareTo(b); });
                int n = sorted.Count;
                if (Shape(start) == ClusterShape.Ring && n > 1)
                {
                    float radius = (float)(ShapeStep / (2 * Math.Sin(Math.PI / n)));
                    var slots = new List<StarMapPoint>(n);
                    for (int i = 0; i < n; i++)
                    {
                        double angle = Math.PI + i * 2 * Math.PI / n;
                        slots.Add(new StarMapPoint(radius + (float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle))));
                    }
                    // Sort the ring from its inward edge to its outward edge. Equal-depth swaps
                    // below preserve all prerequisite levels while reducing chord intersections.
                    slots.Sort((a, b) => { int x = a.X.CompareTo(b.X); return x != 0 ? x : a.Y.CompareTo(b.Y); });
                    for (int i = 0; i < n; i++) result[sorted[i]] = new StarMapPoint(advance + slots[i].X + depth[sorted[i]] * 8, slots[i].Y);
                    advance = result[sorted[n - 1]].X + ShapeStep;
                }
                else if (Shape(start) == ClusterShape.Chain)
                {
                    // A shallow curved chain avoids a skipped-link chord passing through every
                    // intervening star, without turning the manifest's chain into a fan or ring.
                    for (int i = 0; i < n; i++) result[sorted[i]] = new StarMapPoint(advance + i * ShapeStep, (float)(220 * Math.Sin(i * Math.PI * 2 / Math.Max(2, n - 1))));
                    advance += (n - 1) * ShapeStep + ShapeStep;
                }
                else
                {
                    int minDepth = depth[sorted[0]], maxDepth = minDepth;
                    int cursor = 0;
                    while (cursor < n)
                    {
                        int limit = cursor + 1, d = depth[sorted[cursor]];
                        while (limit < n && depth[sorted[limit]] == d) limit++;
                        for (int i = cursor; i < limit; i++)
                            result[sorted[i]] = new StarMapPoint(advance + (d - minDepth) * ShapeStep,
                                (i - cursor - (limit - cursor - 1) * 0.5f) * ShapeStep);
                        maxDepth = Math.Max(maxDepth, d);
                        cursor = limit;
                    }
                    advance += (maxDepth - minDepth) * ShapeStep + ShapeStep;
                }
                start = end;
            }
            ImproveShape(result, depth, group, nodes, neighbors);
            double cx = 0, cy = 0;
            foreach (var point in result) { cx += point.X; cy += point.Y; }
            cx /= count; cy /= count;
            for (int i = 0; i < count; i++) result[i] = new StarMapPoint(result[i].X - (float)cx, result[i].Y - (float)cy);
            return result;
        }

        private static void ImproveShape(StarMapPoint[] points, int[] depth, LayoutGroup group,
            List<HeroTreeNode> nodes, List<List<int>> neighbors)
        {
            var local = new Dictionary<int, int>();
            for (int i = 0; i < points.Length; i++) local.Add(group.Stars[i], i);
            var links = new List<HeroTreeEdge>();
            for (int i = 0; i < points.Length; i++)
                foreach (int other in neighbors[group.Stars[i]])
                    if (local.TryGetValue(other, out int j) && j > i) links.Add(new HeroTreeEdge(i, j));
            // Bound geometric work, rather than elapsed time, so large authored groups cannot
            // turn the local solver into an unbounded quartic search or lose determinism.
            long scoreWork = (long)links.Count * (points.Length + (long)links.Count);
            int evaluationsPerPass = (int)Math.Min(int.MaxValue, 1000000L / Math.Max(1L, scoreWork));
            if (evaluationsPerPass == 0) return;
            double Score()
            {
                double score = 0;
                for (int i = 0; i < links.Count; i++)
                {
                    var e = links[i]; var a = points[e.A]; var b = points[e.B];
                    double dx = b.X - a.X, dy = b.Y - a.Y;
                    score += (dx * dx + dy * dy) / 10000;
                    for (int j = 0; j < points.Length; j++)
                        if (j != e.A && j != e.B && SegmentDistanceSquared(a, b, points[j]) < 30 * 30) score += 1000;
                    for (int j = 0; j < i; j++)
                    {
                        var f = links[j];
                        if (e.A != f.A && e.A != f.B && e.B != f.A && e.B != f.B
                            && Crosses(a, b, points[f.A], points[f.B])) score += 500;
                    }
                }
                return score;
            }
            double best = Score();
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                int evaluations = 0;
                for (int i = 0; i < points.Length && evaluations < evaluationsPerPass; i++)
                    for (int j = i + 1; j < points.Length && evaluations < evaluationsPerPass; j++)
                    {
                        if (depth[i] != depth[j]) continue;
                        var a = nodes[group.Stars[i]].Talent; var b = nodes[group.Stars[j]].Talent;
                        if ((a.AuthoredStar?.Shape ?? a.Cluster.Shape) != (b.AuthoredStar?.Shape ?? b.Cluster.Shape)) continue;
                        // Identical neighborhoods make this swap a graph automorphism: its
                        // geometry score cannot improve. Large spoke fans otherwise repeat
                        // the same full score for every interchangeable pair of leaves.
                        int first = group.Stars[i], second = group.Stars[j];
                        if (neighbors[first].Count == neighbors[second].Count)
                        {
                            bool identical = true;
                            foreach (int other in neighbors[first])
                                if (!neighbors[second].Contains(other)) { identical = false; break; }
                            if (identical) continue;
                        }
                        evaluations++;
                        var old = points[i]; points[i] = points[j]; points[j] = old;
                        double score = Score();
                        if (score < best) { best = score; changed = true; }
                        else { old = points[i]; points[i] = points[j]; points[j] = old; }
                    }
                if (!changed) break;
            }
        }

        private static double SegmentDistanceSquared(StarMapPoint a, StarMapPoint b, StarMapPoint p)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = dx * dx + dy * dy;
            double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length));
            double x = p.X - a.X - t * dx, y = p.Y - a.Y - t * dy;
            return x * x + y * y;
        }

        private static bool Crosses(StarMapPoint a, StarMapPoint b, StarMapPoint c, StarMapPoint d)
        {
            double Side(StarMapPoint p, StarMapPoint q, StarMapPoint r) =>
                (double)(q.X - p.X) * (r.Y - p.Y) - (double)(q.Y - p.Y) * (r.X - p.X);
            return Side(a, b, c) * Side(a, b, d) < -0.0001 && Side(c, d, a) * Side(c, d, b) < -0.0001;
        }

        private static void RefineGroups(List<LayoutGroup> groups, List<LayoutGroup> placed,
            List<HeroTreeNode> nodes, List<List<int>> neighbors, List<HeroTreeEdge> edges, double half)
        {
            // Rigid translation/rotation candidates are evaluated against incident edges only.
            // Shape, radial floor and sector containment remain hard constraints, not penalties.
            var incident = new List<HeroTreeEdge>();
            var candidate = new StarMapPoint[nodes.Count];
            var member = new bool[nodes.Count];
            foreach (var group in groups)
            {
                incident.Clear();
                foreach (int star in group.Stars) member[star] = true;
                foreach (var edge in edges) if (member[edge.A] != member[edge.B]) incident.Add(edge);
                double originHeading = Math.Atan2(group.Y, group.X);
                var basePoints = new StarMapPoint[group.Stars.Count];
                for (int i = 0; i < basePoints.Length; i++) basePoints[i] = new StarMapPoint(nodes[group.Stars[i]].X, nodes[group.Stars[i]].Y);
                double Score(float x, float y, double rotation)
                {
                    double cos = Math.Cos(rotation), sin = Math.Sin(rotation);
                    for (int i = 0; i < group.Stars.Count; i++)
                    {
                        float dx = basePoints[i].X - group.X, dy = basePoints[i].Y - group.Y;
                        candidate[group.Stars[i]] = new StarMapPoint(x + (float)(dx * cos - dy * sin), y + (float)(dx * sin + dy * cos));
                    }
                    StarMapPoint Point(int i) => member[i] ? candidate[i] : new StarMapPoint(nodes[i].X, nodes[i].Y);
                    if (group.Region == ClusterRegionKind.Memory)
                        foreach (var flow in group.Flow)
                        {
                            var a = Point(flow.A); var b = Point(flow.B);
                            if ((double)a.X * a.X + (double)a.Y * a.Y >= (double)b.X * b.X + (double)b.Y * b.Y) return double.MaxValue;
                        }
                    double score = 0;
                    foreach (var edge in incident)
                    {
                        var a = Point(edge.A); var b = Point(edge.B);
                        double dx = b.X - a.X, dy = b.Y - a.Y;
                        score += (dx * dx + dy * dy) / 10000;
                        // Bounding-box rejection keeps the full geometry checks local.
                        float minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
                        float minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            if (i == edge.A || i == edge.B) continue;
                            var p = Point(i);
                            if (p.X < minX - 30 || p.X > maxX + 30 || p.Y < minY - 30 || p.Y > maxY + 30) continue;
                            if (SegmentDistanceSquared(a, b, p) < 30 * 30) score += 1000;
                        }
                        foreach (var other in edges)
                        {
                            if (edge.A == other.A || edge.A == other.B || edge.B == other.A || edge.B == other.B) continue;
                            var c = Point(other.A); var d = Point(other.B);
                            if (Math.Max(c.X, d.X) < minX || Math.Min(c.X, d.X) > maxX
                                || Math.Max(c.Y, d.Y) < minY || Math.Min(c.Y, d.Y) > maxY) continue;
                            if (Crosses(a, b, c, d)) score += 500;
                        }
                    }
                    return score;
                }
                double best = Score(group.X, group.Y, 0);
                float bestX = group.X, bestY = group.Y;
                double bestRotation = 0;
                for (int trial = group.Region == ClusterRegionKind.Memory ? 2 : 0; trial < 8; trial++)
                {
                    double rotation = trial < 2 ? (trial == 0 ? -0.1 : 0.1) : 0;
                    if (group.Region == ClusterRegionKind.Memory) rotation = 0;
                    double angle = originHeading + (trial >= 2 ? (trial - 2) * Math.PI / 3 : 0);
                    float x = group.X + (trial >= 2 ? (float)(32 * Math.Cos(angle)) : 0);
                    float y = group.Y + (trial >= 2 ? (float)(32 * Math.Sin(angle)) : 0);
                    float radius = Distance(x, y);
                    if (radius - group.Radius < group.Floor) continue;
                    double delta = Math.Atan2(Math.Sin(Math.Atan2(y, x) - group.SectorHeading), Math.Cos(Math.Atan2(y, x) - group.SectorHeading));
                    if (group.Region != ClusterRegionKind.Bridge && Math.Abs(delta)
                        + Math.Asin(Math.Min(1, (group.Radius + HaloGap) / radius)) > half * 0.85) continue;
                    bool clear = true;
                    foreach (var other in placed)
                        if (other != group && Distance(other.X - x, other.Y - y) < other.Radius + group.Radius + HaloGap) { clear = false; break; }
                    for (int j = 0; j < nodes.Count && clear; j++)
                    {
                        if (member[j] || nodes[j].Talent?.Cluster != null && !nodes[j].Talent.IsKeystone) continue;
                        if (Distance(nodes[j].X - x, nodes[j].Y - y) < group.Radius + 45) clear = false;
                    }
                    if (!clear) continue;
                    double score = Score(x, y, rotation);
                    if (score < best) { best = score; bestX = x; bestY = y; bestRotation = rotation; }
                }
                if (bestX == group.X && bestY == group.Y && bestRotation == 0)
                {
                    foreach (int star in group.Stars) member[star] = false;
                    continue;
                }
                double cosBest = Math.Cos(bestRotation), sinBest = Math.Sin(bestRotation);
                for (int i = 0; i < group.Stars.Count; i++)
                {
                    var old = nodes[group.Stars[i]];
                    float dx = basePoints[i].X - group.X, dy = basePoints[i].Y - group.Y;
                    nodes[group.Stars[i]] = new HeroTreeNode(old.Id, bestX + (float)(dx * cosBest - dy * sinBest),
                        bestY + (float)(dx * sinBest + dy * cosBest), old.Kind, old.Talent, neighbors[group.Stars[i]]);
                }
                group.X = bestX; group.Y = bestY;
                foreach (int star in group.Stars) member[star] = false;
            }
        }
    }
}
