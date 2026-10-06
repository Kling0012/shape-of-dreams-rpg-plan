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
        // Direct (non-transitive) record: which stars every token saw, which stars are mixed into pair/replacement semantics, and
        // which output groups (the sets of channels one marginal check compares) each star's output belongs to.
        private readonly Dictionary<string, HashSet<string>> members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> pairTokens = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> complex = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> groupMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> groupsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>The keystones the build actually applied, in slot order (empty when none, or when a gate is not met).</summary>
        internal IReadOnlyList<KeystoneDefinition> AppliedKeystones { get; set; } = Array.Empty<KeystoneDefinition>();

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
            if (!members.TryGetValue(token, out var seen)) members.Add(token, seen = new HashSet<string>(StringComparer.Ordinal));
            seen.Add(starId);
            // Pair combos and replacements make a star's presence switch other stars' outputs on and off; they are never analysed locally.
            if (token.Length > 1 && token[1] == ':' && (token[0] == 'Q' || token[0] == 'R'))
            {
                complex.Add(starId);
                if (!pairTokens.TryGetValue(starId, out var tokens)) pairTokens.Add(starId, tokens = new List<string>());
                if (!tokens.Contains(token)) tokens.Add(token);
            }
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

        /// <summary>
        /// Registers one output group (the channels a marginal check compares with each other) and every star the channels in it
        /// depend on, given as the tokens that gathered those stars while the build composed them.
        /// </summary>
        internal void Group(string group, IEnumerable<string> tokens)
        {
            if (!groupMembers.TryGetValue(group, out var inGroup)) groupMembers.Add(group, inGroup = new HashSet<string>(StringComparer.Ordinal));
            foreach (string token in tokens)
            {
                if (!members.TryGetValue(token, out var stars)) continue;
                foreach (string star in stars)
                {
                    if (!inGroup.Add(star)) continue;
                    if (!groupsOf.TryGetValue(star, out var list)) groupsOf.Add(star, list = new List<string>());
                    list.Add(group);
                }
            }
        }

        /// <summary>
        /// Every star whose outputs a change of <paramref name="starId"/> can reach: the stars it shares an output group with, plus,
        /// when it takes part in a pair or replacement, the other participants (whose outputs it switches on and off) and their groups.
        /// </summary>
        internal void AddAffected(string starId, HashSet<string> into)
        {
            starId = alias.TryGetValue(starId, out string star) ? star : starId;
            into.Add(starId);
            AddGroupNeighbours(starId, into);
            if (!pairTokens.TryGetValue(starId, out var tokens)) return;
            foreach (string token in tokens)
                foreach (string other in members[token]) { into.Add(other); AddGroupNeighbours(other, into); }
        }

        /// <summary>The output groups the star belongs to (empty when it contributes to none).</summary>
        internal IReadOnlyList<string> GroupsOf(string starId) =>
            groupsOf.TryGetValue(alias.TryGetValue(starId, out string star) ? star : starId, out var groups) ? (IReadOnlyList<string>)groups : Array.Empty<string>();

        internal bool IsComplex(string starId) => complex.Contains(alias.TryGetValue(starId, out string star) ? star : starId);

        /// <summary>Every star that shares an output group with <paramref name="starId"/>: the only stars whose marginal checks it can change.</summary>
        internal void AddGroupNeighbours(string starId, HashSet<string> into)
        {
            if (!groupsOf.TryGetValue(alias.TryGetValue(starId, out string star) ? star : starId, out var groups)) return;
            foreach (string group in groups) into.UnionWith(groupMembers[group]);
        }

        internal void AddOutputMembers(string group, HashSet<string> into)
        {
            if (groupMembers.TryGetValue(group, out var stars)) into.UnionWith(stars);
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
