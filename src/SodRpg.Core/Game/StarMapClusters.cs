using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed class StarMapCluster
    {
        private readonly int[] nodeIndices;
        internal readonly HeroTreeLayout Layout;

        internal StarMapCluster(HeroTreeLayout layout, string id, Txt name, ClusterRegionKind region, int[] indices, float x, float y)
        {
            Layout = layout;
            Id = id;
            Name = name;
            Region = region;
            nodeIndices = indices;
            X = x;
            Y = y;
        }

        public string Id { get; }
        /// <summary>Localized representative-star name; the authored cluster's identity/name is Id.</summary>
        public Txt Name { get; }
        public ClusterRegionKind Region { get; }
        public int[] NodeIndices => (int[])nodeIndices.Clone();
        public int NodeCount => nodeIndices.Length;
        public int NodeIndex(int index) => nodeIndices[index];
        public float X { get; }
        public float Y { get; }
    }

    /// <summary>Build once per layout; allocation counts and indexed access are allocation-free.</summary>
    public static class StarMapClusters
    {
        public static StarMapCluster[] Build(HeroTreeLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, StarClusterDef>(StringComparer.Ordinal);
            for (int i = 0; i < layout.Nodes.Count; i++)
            {
                var definition = layout.Nodes[i].Talent?.Cluster;
                if (definition == null) continue;
                if (string.IsNullOrWhiteSpace(definition.Id) || definition.Region == null)
                    throw new InvalidOperationException("Cluster ID and region are required.");
                RegionLabel(definition.Region.Kind);
                if (!groups.TryGetValue(definition.Id, out var indices))
                {
                    indices = new List<int>();
                    groups.Add(definition.Id, indices);
                    definitions.Add(definition.Id, definition);
                }
                else
                {
                    var original = definitions[definition.Id];
                    if (original.Region.Kind != definition.Region.Kind
                        || !string.Equals(original.Region.Id, definition.Region.Id, StringComparison.Ordinal)
                        || !string.Equals(original.HeroKey, definition.HeroKey, StringComparison.Ordinal))
                        throw new InvalidOperationException("Inconsistent cluster region mapping: " + definition.Id);
                }
                indices.Add(i);
            }
            var result = new StarMapCluster[groups.Count];
            int next = 0;
            foreach (var pair in groups)
            {
                var indices = pair.Value;
                indices.Sort((a, b) =>
                {
                    int order = layout.Nodes[a].Talent.ClusterOrder.CompareTo(layout.Nodes[b].Talent.ClusterOrder);
                    return order != 0 ? order : StringComparer.Ordinal.Compare(layout.Nodes[a].Id, layout.Nodes[b].Id);
                });
                var name = layout.Nodes[indices[0]].Talent.Name;
                if (name == null || string.IsNullOrWhiteSpace(name.Ja) || string.IsNullOrWhiteSpace(name.En))
                    throw new InvalidOperationException("Cluster representative requires both display names: " + pair.Key);
                double x = 0, y = 0;
                foreach (int index in indices)
                {
                    x += layout.Nodes[index].X;
                    y += layout.Nodes[index].Y;
                }
                result[next++] = new StarMapCluster(layout, pair.Key, name, definitions[pair.Key].Region.Kind,
                    indices.ToArray(), (float)(x / indices.Count), (float)(y / indices.Count));
            }
            Array.Sort(result, (a, b) =>
            {
                int region = a.Region.CompareTo(b.Region);
                return region != 0 ? region : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            return result;
        }

        public static int AllocatedCount(HeroState state, HeroTreeLayout layout, StarMapCluster cluster)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (cluster == null) throw new ArgumentNullException(nameof(cluster));
            if (!ReferenceEquals(layout, cluster.Layout))
                throw new ArgumentException("Cluster belongs to a different layout.", nameof(cluster));
            int allocated = 0;
            for (int i = 0; i < cluster.NodeCount; i++)
            {
                var talent = layout.Nodes[cluster.NodeIndex(i)].Talent;
                if (talent.IsKeystone ? string.Equals(state.Keystone, talent.Id, StringComparison.Ordinal)
                    : state.Talents.TryGetValue(talent.Id, out int rank) && rank > 0)
                    allocated++;
            }
            return allocated;
        }

        public static string RegionLabel(ClusterRegionKind kind)
        {
            switch (kind)
            {
                case ClusterRegionKind.Memory: return Loc.T("記憶", "Memory");
                case ClusterRegionKind.Bridge: return Loc.T("記憶の橋", "Bridge");
                case ClusterRegionKind.Outer: return Loc.T("外縁", "Outer");
                case ClusterRegionKind.Keystone: return Loc.T("要石", "Keystone");
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cluster region.");
            }
        }
    }
}
