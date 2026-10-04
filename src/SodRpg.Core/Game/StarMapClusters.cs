using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed class StarMapCluster
    {
        private readonly int[] nodeIndices;
        internal readonly HeroTreeLayout Layout;

        internal StarMapCluster(HeroTreeLayout layout, string id, Txt name, Txt displayName, ClusterRegionKind region, int[] indices, float x, float y)
        {
            Layout = layout;
            Id = id;
            Name = name;
            DisplayName = displayName;
            Region = region;
            nodeIndices = indices;
            X = x;
            Y = y;
        }

        public string Id { get; }
        /// <summary>Localized representative-star name; the authored cluster's identity/name is Id.</summary>
        public Txt Name { get; }
        /// <summary>画面に出す星団の名前（例：「氷の血脈の星団」）。内部IDは含まない。</summary>
        public Txt DisplayName { get; internal set; }
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
                result[next++] = new StarMapCluster(layout, pair.Key, name, DisplayNameFor(layout, definitions[pair.Key], name),
                    definitions[pair.Key].Region.Kind,
                    indices.ToArray(), (float)(x / indices.Count), (float)(y / indices.Count));
            }
            Array.Sort(result, (a, b) =>
            {
                int region = a.Region.CompareTo(b.Region);
                return region != 0 ? region : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            MakeNamesDistinct(layout, result);
            return result;
        }

        /// <summary>
        /// 同じ旅人の星団どうしで表示名が重ならないようにする。重なる名前だけを、その星団で最初の見せ場の星
        /// （「太陽の洗礼：洗礼の脈動」）で言い分け、それでも重なれば番号（一・二…）を付ける。
        /// </summary>
        private static void MakeNamesDistinct(HeroTreeLayout layout, StarMapCluster[] clusters)
        {
            var seen = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < clusters.Length; i++)
            {
                if (!seen.TryGetValue(clusters[i].DisplayName.Ja, out var list)) seen.Add(clusters[i].DisplayName.Ja, list = new List<int>());
                list.Add(i);
            }
            foreach (var pair in seen)
            {
                if (pair.Value.Count < 2) continue;
                foreach (int i in pair.Value)
                {
                    var cluster = clusters[i];
                    var star = NotableStarName(layout, cluster);
                    var name = cluster.DisplayName;
                    string ja = name.Ja.EndsWith("の星団", StringComparison.Ordinal) ? name.Ja.Substring(0, name.Ja.Length - 3) : name.Ja;
                    string en = name.En.EndsWith(" Cluster", StringComparison.Ordinal) ? name.En.Substring(0, name.En.Length - 8) : name.En;
                    cluster.DisplayName = new Txt(ja + "：" + star.Ja, en + ": " + star.En);
                }
                var again = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                foreach (int i in pair.Value)
                {
                    if (!again.TryGetValue(clusters[i].DisplayName.Ja, out var list)) again.Add(clusters[i].DisplayName.Ja, list = new List<int>());
                    list.Add(i);
                }
                foreach (var group in again.Values)
                {
                    if (group.Count < 2) continue;
                    for (int n = 0; n < group.Count; n++)
                    {
                        var name = clusters[group[n]].DisplayName;
                        clusters[group[n]].DisplayName = new Txt(name.Ja + "（" + Ordinal(n + 1) + "）", name.En + " (" + (n + 1) + ")");
                    }
                }
            }
        }

        private static Txt NotableStarName(HeroTreeLayout layout, StarMapCluster cluster)
        {
            for (int i = 0; i < cluster.NodeCount; i++)
            {
                var node = layout.Nodes[cluster.NodeIndex(i)];
                if (node.Kind == HeroTreeNodeKind.Notable) return node.Talent.Name;
            }
            return cluster.Name;
        }

        private static string Ordinal(int number)
        {
            string[] digits = { "〇", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (number < 10) return digits[number];
            if (number < 20) return "十" + (number == 10 ? "" : digits[number - 10]);
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 星団の表示名。明示の名前があればそれ、無ければ地域と記憶から作る（記憶なら「氷の血脈の星団」、
        /// 橋なら「橋の星団「コンボ名」」、外縁・刻印なら代表の星の名前から）。内部のIDは使わない。
        /// </summary>
        public static Txt DisplayNameFor(HeroTreeLayout layout, StarClusterDef def, Txt representative)
        {
            if (def.Name != null && !string.IsNullOrWhiteSpace(def.Name.Ja) && !string.IsNullOrWhiteSpace(def.Name.En)) return def.Name;
            switch (def.Region.Kind)
            {
                case ClusterRegionKind.Memory:
                    string memory = MemoryOf(layout, def);
                    if (memory != null)
                    {
                        var m = Links.Name(memory);
                        return new Txt(m.Ja + "の星団", m.En + " Cluster");
                    }
                    return new Txt(representative.Ja + "の星団", representative.En + " Cluster");
                case ClusterRegionKind.Bridge:
                    var pair = def.Region.Id == null ? null : PairCombos.ForBridge(def.Region.Id);
                    if (pair != null && pair.Name != null && !string.IsNullOrWhiteSpace(pair.Name.Ja) && !string.IsNullOrWhiteSpace(pair.Name.En))
                        return new Txt("橋の星団「" + pair.Name.Ja + "」", "Bridge Cluster: " + pair.Name.En);
                    if (pair != null && Links.IsMemory(pair.RouteA) && Links.IsMemory(pair.RouteB))
                    {
                        var a = Links.Name(pair.RouteA);
                        var b = Links.Name(pair.RouteB);
                        return new Txt("橋の星団「" + a.Ja + "×" + b.Ja + "」", "Bridge Cluster: " + a.En + " x " + b.En);
                    }
                    return new Txt("橋の星団「" + representative.Ja + "」", "Bridge Cluster: " + representative.En);
                case ClusterRegionKind.Keystone:
                    return new Txt(representative.Ja + "の刻印星団", representative.En + " Keystone Cluster");
                default:
                    return new Txt(representative.Ja + "の外縁星団", representative.En + " Outer Cluster");
            }
        }

        private static string MemoryOf(HeroTreeLayout layout, StarClusterDef def)
        {
            string fallback = null;
            for (int i = 0; i < layout.Nodes.Count; i++)
            {
                var talent = layout.Nodes[i].Talent;
                if (talent == null || !Links.IsMemory(talent.RouteMemory)) continue;
                if (talent.Id == def.Anchor) return talent.RouteMemory;
                if (fallback == null && talent.Cluster != null && talent.Cluster.Id == def.Id) fallback = talent.RouteMemory;
            }
            return fallback;
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
                if (talent.IsKeystone ? state.HasKeystone(talent.Id)
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
                case ClusterRegionKind.Keystone: return Loc.T("刻印", "Keystone");
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cluster region.");
            }
        }
    }
}
