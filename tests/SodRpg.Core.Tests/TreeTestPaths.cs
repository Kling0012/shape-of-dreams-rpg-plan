using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Core.Tests
{
    internal static class TreeTestPaths
    {
        internal static void Connect(Profile p, string hero, string id)
        {
            var tree = HeroTreeLayout.ForHero(hero);
            var parents = new int[tree.Nodes.Count];
            for (int i = 0; i < parents.Length; i++) parents[i] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(tree.StartIndex);
            parents[tree.StartIndex] = tree.StartIndex;
            int target = -1;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (tree.Nodes[current].Id == id) { target = current; break; }
                foreach (int next in tree.Nodes[current].Neighbors)
                {
                    if (parents[next] >= 0 || tree.Nodes[next].Talent?.IsKeystone == true) continue;
                    parents[next] = current;
                    queue.Enqueue(next);
                }
            }
            if (target < 0) throw new InvalidOperationException(id);
            var path = new Stack<int>();
            for (int at = parents[target]; at != tree.StartIndex; at = parents[at]) path.Push(at);
            while (path.Count > 0)
            {
                var node = tree.Nodes[path.Pop()];
                if (!p.Hero(hero).Talents.TryGetValue(node.Id, out int rank) || rank <= 0)
                    Rules.AddTalentRank(p, hero, node.Id, node.Talent.IsChoice ? 0 : (int?)null);
            }
        }
    }
}
