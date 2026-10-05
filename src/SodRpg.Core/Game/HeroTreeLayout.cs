using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public enum HeroTreeNodeKind { Small, Notable, Keystone }

    public sealed class HeroTreeNode
    {
        internal HeroTreeNode(string id, float x, float y, HeroTreeNodeKind kind, TalentDef talent, List<int> neighbors)
        {
            Id = id;
            X = x;
            Y = y;
            Kind = kind;
            Talent = talent;
            Neighbors = neighbors.AsReadOnly();
        }

        public string Id { get; }
        public float X { get; }
        public float Y { get; }
        public HeroTreeNodeKind Kind { get; }
        public TalentDef Talent { get; }
        public IReadOnlyList<int> Neighbors { get; }
    }

    public sealed class HeroTreeEdge
    {
        internal HeroTreeEdge(int a, int b) { A = a; B = b; }
        public int A { get; }
        public int B { get; }
    }

    /// <summary>Stable, shared star positions and undirected connections, including the unspent starting star.</summary>
    public sealed partial class HeroTreeLayout
    {
        public const float MinimumSpacing = 80f;
        private const float InnerRadius = 180f;
        private const float DeepRadius = 330f;
        // Reference geometry resolves shipped implicit outer access edges before sector placement.
        // Final hero coordinates come exclusively from PlaceSectors; topology keeps registration order.
        private const float BranchRadius = 480f;
        private const float BranchStep = 95f;
        // Loadout A/B order, independent of route registration and persistent node ids.
        private static readonly Dictionary<string, string[]> BranchOrder = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Hero_Vesper"] = new[] { "resolve", "cruel-sun", "sanctuary", "charge", "mercy", "discipline", "baptism" },
            ["Hero_Lacerta"] = new[] { "powder", "hand-cannon", "quick-trigger", "nimble-dodge", "double-tap", "incendiary", "precision" },
            ["Hero_Cetus"] = new[] { "icy-veins", "embrace-chill", "back-off", "frost-charge", "charged", "boreal-chunk", "frozen-fists" },
            ["Hero_Yubar"] = new[] { "exotic-matter", "ethereal", "cataclysm", "flicker", "converging-stars", "supernova", "tranquility" },
            ["Hero_Husk"] = new[] { "killing-flow", "laceration", "annihilation", "flash-step", "wind-scar", "death-mark", "deception" },
            ["Hero_Mist"] = new[] { "en-garde", "lunge", "determination", "fast-feet", "priorite", "fleche", "parry" },
            ["Hero_Nachia"] = new[] { "pack-heart", "sylvan-call", "natures-whisper", "dreamy-waltz", "circle-life", "moonlight-pact", "serpent-blessing" },
            ["Hero_Aurena"] = new[] { "claw", "golden-burst", "dangerous-theory", "feathery-dash", "beautiful-threat", "reduction", "chain-reaction" },
            ["Hero_Bismuth"] = new[] { "prismatic-eyes", "innocence", "distorting-sprint", "infernal-tales", "valiant-heart", "distorted-mind" },
        };
        private static readonly Dictionary<string, HeroTreeLayout> Layouts = CreateLayouts();
        private static readonly HeroTreeLayout Generic = Create(Content.Talents, false);
        private readonly Dictionary<string, int> indices;
        // Reused under the lock: neither UI availability checks nor build evaluation allocate traversal buffers.
        private readonly bool[] reached;
        private readonly int[] queue;
        private readonly object traversalLock = new object();

        private HeroTreeLayout(List<HeroTreeNode> nodes, List<HeroTreeEdge> edges)
        {
            Nodes = nodes.AsReadOnly();
            Edges = edges.AsReadOnly();
            indices = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++) indices.Add(nodes[i].Id, i);
            reached = new bool[nodes.Count];
            queue = new int[nodes.Count];
        }

        public IReadOnlyList<HeroTreeNode> Nodes { get; }
        public IReadOnlyList<HeroTreeEdge> Edges { get; }
        public int StartIndex => 0;

        public static HeroTreeLayout ForHero(string heroKey) =>
            StarClusters.TryGetRegisteredLayout(heroKey, out var registered) ? registered
                : heroKey != null && Layouts.TryGetValue(heroKey, out var layout) ? layout : Generic;

        /// <summary>Builds a validated generated tree for data tooling and layout stress scenarios.</summary>
        public static HeroTreeLayout ForTalents(IReadOnlyList<TalentDef> talents) => Create(talents, true);

        private static Dictionary<string, HeroTreeLayout> CreateLayouts()
        {
            var layouts = new Dictionary<string, HeroTreeLayout>(StringComparer.Ordinal);
            foreach (var talent in HeroSigils.All)
                if (!layouts.ContainsKey(talent.HeroKey))
                    layouts.Add(talent.HeroKey, Create(HeroSigils.TreeFor(talent.HeroKey), true));
            return layouts;
        }

        private static HeroTreeNodeKind Kind(TalentDef talent)
        {
            if (talent.IsKeystone) return HeroTreeNodeKind.Keystone;
            if (talent.ClusterStar != null)
                return talent.IsChoice || talent.ClusterStar.Kind == ClusterStarKind.Notable
                    ? HeroTreeNodeKind.Notable : HeroTreeNodeKind.Small;
            if (talent.IsKeystone || talent.Stat == Stat.FourthAttackShift
                || talent.Stat == Stat.EssenceSlotIdentity || talent.Stat == Stat.EssenceSlotMovement)
                return HeroTreeNodeKind.Keystone;
            return talent.IsPowerNode || talent.LinkPerRank != null || talent.RouteOrder == 7
                ? HeroTreeNodeKind.Notable : HeroTreeNodeKind.Small;
        }

        private static double Angle(double position, int count) => -Math.PI / 2 + 2 * Math.PI * position / count;

        private static HeroTreeLayout Create(IReadOnlyList<TalentDef> talents, bool heroTree)
        {
            var nodes = new List<HeroTreeNode>(talents.Count + 1);
            var neighbors = new List<List<int>>(talents.Count + 1);
            var edges = new List<HeroTreeEdge>();
            var inner = new List<TalentDef>();
            var deep = new List<TalentDef>();
            var ring = new List<TalentDef>();
            var branches = new List<List<TalentDef>>();
            var routeIndices = new Dictionary<string, int>(StringComparer.Ordinal);

            int Add(TalentDef talent, float radius, double angle)
            {
                int index = nodes.Count;
                var adjacent = new List<int>();
                neighbors.Add(adjacent);
                nodes.Add(new HeroTreeNode(talent?.Id ?? "tree.start", (float)(radius * Math.Cos(angle)),
                    (float)(radius * Math.Sin(angle)), talent == null ? HeroTreeNodeKind.Keystone : Kind(talent), talent, adjacent));
                return index;
            }

            void Join(int a, int b)
            {
                neighbors[a].Add(b);
                neighbors[b].Add(a);
                edges.Add(new HeroTreeEdge(a, b));
            }

            Add(null, 0, 0);
            if (!heroTree)
            {
                // The fallback retains its freely selectable generic nodes and route-rank keystone gates.
                float radius = Math.Max(InnerRadius, talents.Count * MinimumSpacing / (float)Math.PI);
                for (int i = 0; i < talents.Count; i++) Join(0, Add(talents[i], radius, Angle(i, talents.Count)));
                return new HeroTreeLayout(nodes, edges);
            }

            foreach (var talent in talents)
            {
                if (talent.Cluster != null || talent.IsOuterAnchor) continue;
                if (talent.IsDreamRing) ring.Add(talent);
                else if (talent.RouteId != null)
                {
                    if (!routeIndices.TryGetValue(talent.RouteId, out int branch))
                    {
                        branch = branches.Count;
                        routeIndices.Add(talent.RouteId, branch);
                        branches.Add(new List<TalentDef>());
                    }
                    branches[branch].Add(talent);
                }
                else if (talent.Tier == 1 || talent.IsKeystone || talent.Stat == Stat.FourthAttackShift) inner.Add(talent);
                else deep.Add(talent);
            }
            if (branches.Count > 0 && BranchOrder.TryGetValue(branches[0][0].HeroKey, out var branchOrder))
            {
                string prefix = "h." + branches[0][0].HeroKey.Substring(5).ToLowerInvariant() + ".route.";
                var ordered = new List<List<TalentDef>>(branches.Count);
                foreach (string slug in branchOrder) ordered.Add(branches[routeIndices[prefix + slug]]);
                branches = ordered;
            }

            var innerIndices = new int[inner.Count];
            for (int i = 0; i < inner.Count; i++)
            {
                innerIndices[i] = Add(inner[i], InnerRadius, Angle(i, inner.Count));
                Join(0, innerIndices[i]);
            }
            var deepIndices = new int[deep.Count];
            for (int i = 0; i < deep.Count; i++)
            {
                deepIndices[i] = Add(deep[i], DeepRadius, Angle(i, deep.Count));
                Join(innerIndices[i * inner.Count / deep.Count], deepIndices[i]);
            }
            var branchNodes = new List<int[]>(branches.Count);
            for (int i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                branch.Sort((a, b) => a.RouteOrder.CompareTo(b.RouteOrder));
                var ordered = new int[branch.Count];
                for (int j = 0; j < branch.Count; j++)
                {
                    ordered[j] = Add(branch[j], BranchRadius + BranchStep * j, Angle(i, branches.Count));
                    Join(j == 0 ? deepIndices[i * deep.Count / branches.Count] : ordered[j - 1], ordered[j]);
                }
                branchNodes.Add(ordered);
            }
            // Preserve the shipped cyclic bridge endpoints (the eighth uses the later route stars).
            for (int i = 0; i < ring.Count; i++)
            {
                int branch = i % branches.Count;
                int order = i < branches.Count ? 3 : 5;
                int bridge = Add(ring[i], BranchRadius + BranchStep * order, Angle(branch + 0.5, branches.Count));
                Join(branchNodes[branch][order], bridge);
                Join(bridge, branchNodes[(branch + 1) % branches.Count][order]);
            }
            // Preserve the two early direct access edges; the sector pass may move their endpoints.
            Join(branchNodes[1][2], branchNodes[2][2]);
            Join(branchNodes[4][2], branchNodes[5][2]);
            PlaceClusters(nodes, neighbors, edges, talents);
            return new HeroTreeLayout(nodes, edges);
        }

        internal bool CanReach(HeroState hero, TalentDef talent)
        {
            if (!AuthoredStarContract.PrerequisitesMet(hero, talent, null, hero.Keystones)) return false;
            if (!indices.TryGetValue(talent.Id, out int target) || Nodes[target].Talent != talent) return false;
            lock (traversalLock)
            {
                Traverse(hero, -1, hero.Keystones);
                if (reached[target]) return true;
                var adjacent = Nodes[target].Neighbors;
                for (int i = 0; i < adjacent.Count; i++)
                    if (reached[adjacent[i]]) return true;
                return false;
            }
        }

        internal bool[] ReachabilitySnapshot(HeroState hero)
        {
            lock (traversalLock)
            {
                Traverse(hero, -1, hero.Keystones);
                var eligible = new bool[Nodes.Count];
                for (int i = 0; i < Nodes.Count; i++)
                {
                    if (!reached[i]) continue;
                    eligible[i] = true;
                    foreach (int adjacent in Nodes[i].Neighbors) eligible[adjacent] = true;
                }
                return eligible;
            }
        }

        internal bool CanReach(HeroState hero, TalentDef talent, bool[] snapshot) =>
            indices.TryGetValue(talent.Id, out int target) && Nodes[target].Talent == talent && snapshot[target]
            && AuthoredStarContract.PrerequisitesMet(hero, talent, null, hero.Keystones);

        internal bool AllocationsConnected(HeroState hero, string removedTalent, string keystone) =>
            AllocationsConnected(hero, removedTalent, keystone == null ? Array.Empty<string>() : new[] { keystone });

        /// <summary>選択中の刻印すべて（Keystones）が取得済みの星とつながっているか。</summary>
        internal bool AllocationsConnected(HeroState hero, string removedTalent, string[] keystones)
        {
            int removed = removedTalent != null && indices.TryGetValue(removedTalent, out int index) ? index : -1;
            lock (traversalLock)
            {
                Traverse(hero, removed, keystones);
                foreach (var allocation in hero.Talents)
                    if (allocation.Value > 0 && allocation.Key != removedTalent
                        && (!indices.TryGetValue(allocation.Key, out int allocated) || !reached[allocated]
                            || !AuthoredStarContract.PrerequisitesMet(hero, Nodes[allocated].Talent, removedTalent, keystones))) return false;
                foreach (string keystone in keystones)
                    if (keystone != null && (!indices.TryGetValue(keystone, out int key) || !reached[key]
                        || !AuthoredStarContract.PrerequisitesMet(hero, Nodes[key].Talent, removedTalent, keystones))) return false;
                return true;
            }
        }

        private void Traverse(HeroState hero, int removed, string[] keystones)
        {
            Array.Clear(reached, 0, reached.Length);
            reached[StartIndex] = true;
            queue[0] = StartIndex;
            int count = 1;
            for (int cursor = 0; cursor < count; cursor++)
            {
                var adjacent = Nodes[queue[cursor]].Neighbors;
                for (int i = 0; i < adjacent.Count; i++)
                {
                    int next = adjacent[i];
                    if (next == removed || reached[next]) continue;
                    var talent = Nodes[next].Talent;
                    bool allocated = talent.IsKeystone ? Array.IndexOf(keystones, talent.Id) >= 0
                        : hero.Talents.TryGetValue(talent.Id, out int rank) && rank > 0;
                    if (!allocated) continue;
                    reached[next] = true;
                    queue[count++] = next;
                }
            }
        }

    }
}
