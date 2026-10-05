using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed partial class HeroTreeLayout
    {
        public const float KeystoneSpacing = MinimumSpacing * 2f;

        // Assemble topology in the shipped registration order. Geometry is a separate pass, so moving
        // a group cannot change an implicit attachment, a saved ID, or the order of nodes and edges.
        private static void PlaceClusters(List<HeroTreeNode> nodes, List<List<int>> neighbors,
            List<HeroTreeEdge> edges, IReadOnlyList<TalentDef> talents)
        {
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, TalentDef>(talents.Count, StringComparer.Ordinal);
            foreach (var node in nodes) indices.Add(node.Id, indices.Count);
            foreach (var talent in talents) definitions.Add(talent.Id, talent);
            int Add(TalentDef talent, float x = 0, float y = 0)
            {
                var adjacent = new List<int>();
                int index = nodes.Count;
                neighbors.Add(adjacent);
                nodes.Add(new HeroTreeNode(talent.Id, x, y, Kind(talent), talent, adjacent));
                indices.Add(talent.Id, index);
                return index;
            }
            void Join(int a, int b)
            {
                if (neighbors[a].Contains(b)) return;
                neighbors[a].Add(b);
                neighbors[b].Add(a);
                edges.Add(new HeroTreeEdge(a, b));
            }
            bool ExplicitOuter(TalentDef talent)
            {
                if (talent.AuthoredStar == null) return false;
                foreach (var edge in talent.AuthoredStar.Edges)
                {
                    string other = edge.From == talent.Id ? edge.To : edge.To == talent.Id ? edge.From : null;
                    if (other != null && definitions.TryGetValue(other, out var endpoint)
                        && (endpoint.AuthoredStar?.ClusterId ?? endpoint.Cluster?.Id) != talent.AuthoredStar.ClusterId) return true;
                }
                return false;
            }
            int originalCount = nodes.Count, outerOrder = 0;
            void OuterAnchors(bool newlyAuthored)
            {
                foreach (var talent in talents)
                {
                    if (!talent.IsOuterAnchor || (talent.AuthoredStar != null && !talent.AuthoredStar.RetainedLegacy) != newlyAuthored) continue;
                    // Legacy implicit access edges were selected by these eight angular slots, not by
                    // authored memory. Resolve them before the coordinate cutover; never rewire by new proximity.
                    double angle = Angle(outerOrder++, 8);
                    float x = (float)(1350 * Math.Cos(angle)), y = (float)(1350 * Math.Sin(angle));
                    int added = Add(talent, x, y);
                    if (ExplicitOuter(talent)) continue;
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
                    Join(closest, added);
                }
            }
            List<List<TalentDef>> Groups(bool retained)
            {
                var result = new List<List<TalentDef>>();
                var byCluster = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var talent in talents)
                {
                    if (talent.Cluster == null) continue;
                    if (!byCluster.TryGetValue(talent.Cluster.Id, out int group))
                    {
                        group = result.Count;
                        byCluster.Add(talent.Cluster.Id, group);
                        result.Add(new List<TalentDef>());
                    }
                    if (!indices.ContainsKey(talent.Id)) result[group].Add(talent);
                }
                for (int i = result.Count - 1; i >= 0; i--)
                    if (result[i].Count == 0 || (result[i][0].AuthoredStar == null || result[i][0].AuthoredStar.RetainedLegacy) != retained) result.RemoveAt(i);
                return result;
            }
            void AddGroups(List<List<TalentDef>> groups)
            {
                bool Ready(List<TalentDef> group)
                {
                    if (IsKeystoneGroup(group))
                    {
                        foreach (var star in group) if (!indices.ContainsKey(KeystoneAnchor(star))) return false;
                        return true;
                    }
                    return indices.ContainsKey(group[0].Cluster.Anchor);
                }
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    int ready = groupIndex;
                    while (ready < groups.Count && !Ready(groups[ready])) ready++;
                    if (ready == groups.Count) throw new InvalidOperationException("Cluster anchor is missing or cyclic: " + groups[groupIndex][0].Cluster.Id);
                    var group = groups[ready];
                    for (int i = ready; i > groupIndex; i--) groups[i] = groups[i - 1];
                    groups[groupIndex] = group;
                    group.Sort((a, b) => a.ClusterOrder.CompareTo(b.ClusterOrder));
                    int anchor = indices[PlacementAnchor(group, indices)];
                    var added = new int[group.Count];
                    for (int i = 0; i < group.Count; i++) added[i] = Add(group[i]);
                    var cluster = group[0].Cluster;
                    if (IsKeystoneGroup(group) || cluster.AuthoredEdges != null) continue;
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
                        int entry = added[0];
                        foreach (int next in entries) { if (next != entry) Join(entry, next); entry = next; }
                        foreach (int notable in notables) if (notable != entry) Join(entry, notable);
                        foreach (int choice in choices)
                            if (notables.Count == 0) { if (choice != entry) Join(entry, choice); }
                            else foreach (int notable in notables) if (notable != choice) Join(notable, choice);
                    }
                    else
                    {
                        for (int i = 1; i < added.Length; i++) Join(added[i - 1], added[i]);
                        if (cluster.Shape == ClusterShape.Ring && added.Length > 2) Join(added[added.Length - 1], added[0]);
                    }
                }
            }
            OuterAnchors(false);
            AddGroups(Groups(true));
            OuterAnchors(true);
            AddGroups(Groups(false));
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
            PlaceSectors(nodes, neighbors, edges);
        }

        private static bool IsKeystoneGroup(List<TalentDef> group) =>
            group.Count > 0 && group[0].Cluster.Region.Kind == ClusterRegionKind.Keystone && group[0].AuthoredStar != null;

        private static string KeystoneAnchor(TalentDef star) => star.AuthoredStar.AnchorId ?? star.Cluster.Anchor;

        private static string PlacementAnchor(List<TalentDef> group, Dictionary<string, int> placed)
        {
            var entry = group[0];
            if (entry.AuthoredStar == null) return entry.Cluster.Anchor;
            foreach (var edge in entry.AuthoredStar.Edges)
            {
                string other = edge.To == entry.Id ? edge.From : edge.From == entry.Id ? edge.To : null;
                if (other == null || !placed.ContainsKey(other)) continue;
                bool inside = false;
                foreach (var star in group) if (star.Id == other) { inside = true; break; }
                if (!inside) return other;
            }
            return entry.Cluster.Anchor;
        }
    }
}
