using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// Records which allocated stars can influence one another's build output, so C15 can leave alone every star that
    /// a given change cannot touch. Build composition calls <see cref="Touch(string, string)"/> at every place where several
    /// stars are combined into one output (a stat/power/link total, a merged gimmick or mechanism channel, a scoped modifier or
    /// boost reaching its recipients, a bridge reaching its endpoints, a replaced star, a pair, a haste total, a strongest-effect
    /// comparison). All stars touching one token end up in one component; a star outside the component of every changed star is
    /// provably unaffected by the change, because no output it contributes to also depends on a changed star.
    /// Over-approximating (joining stars that do not really interact) is always safe; missing a join is not.
    /// </summary>
    internal sealed class StarDependencies
    {
        private readonly Dictionary<string, string> parent = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> tokenOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> alias = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The keystone the build actually applied (null when none, or when its route/mastery gate is not met).</summary>
        internal KeystoneDefinition AppliedKeystone { get; set; }

        /// <summary>A choice star contributes through its selected option; the allocation (and every change) names the choice star.</summary>
        internal void Alias(string optionId, string starId)
        {
            if (optionId == null || starId == null || optionId == starId) return;
            if (alias.TryGetValue(optionId, out string existing)) { Union(existing, starId); return; }
            alias.Add(optionId, starId);
        }

        internal void Touch(string token, string starId)
        {
            if (starId == null) return;
            starId = alias.TryGetValue(starId, out string star) ? star : starId;
            if (tokenOwner.TryGetValue(token, out string owner)) Union(owner, starId);
            else
            {
                tokenOwner.Add(token, starId);
                Find(starId);
            }
        }

        internal void Touch(string token, IEnumerable<string> starIds)
        {
            if (starIds == null) return;
            foreach (string id in starIds) Touch(token, id);
        }

        internal string Find(string id)
        {
            if (!parent.TryGetValue(id, out string up)) { parent.Add(id, id); return id; }
            string root = id;
            while (up != root) { root = up; up = parent[root]; }
            // Path compression.
            string cursor = id;
            while (cursor != root) { string next = parent[cursor]; parent[cursor] = root; cursor = next; }
            return root;
        }

        private void Union(string a, string b)
        {
            string ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }
    }
}
