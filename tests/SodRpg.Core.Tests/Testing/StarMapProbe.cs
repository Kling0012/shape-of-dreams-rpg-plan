using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;

namespace SodRpg.Core.Tests.Testing
{
    /// <summary>
    /// Walks one hero's whole star map the way a player does and reports every star the player could never buy.
    /// A star is bought on the smallest allocation that reaches it: the stars on a path from the start, plus (only when
    /// the star needs one) the stars that give an effect somewhere else in the map something to act on. Choice stars are
    /// tried option by option and rank by rank. Nothing is refunded: a purchase that would need a refund counts as refused.
    /// </summary>
    internal sealed class StarMapProbe
    {
        private sealed class State
        {
            public readonly Dictionary<string, int> Ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> Choices = new Dictionary<string, int>(StringComparer.Ordinal);

            public State Clone()
            {
                var copy = new State();
                foreach (var rank in Ranks) copy.Ranks[rank.Key] = rank.Value;
                foreach (var choice in Choices) copy.Choices[choice.Key] = choice.Value;
                return copy;
            }

            public void Merge(State other)
            {
                foreach (var rank in other.Ranks) Ranks[rank.Key] = Math.Max(Ranks.TryGetValue(rank.Key, out int own) ? own : 0, rank.Value);
                foreach (var choice in other.Choices) Choices[choice.Key] = choice.Value;
            }
        }

        private readonly string hero;
        private readonly Profile profile;
        private readonly HeroState allocation;
        private readonly HeroTreeLayout layout;
        private readonly Dictionary<string, int> indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, State> reached = new Dictionary<string, State>(StringComparer.Ordinal);

        public StarMapProbe(string hero)
        {
            this.hero = hero;
            profile = Profile.CreateNew(77);
            allocation = profile.Hero(hero);
            allocation.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            allocation.Kills = 1000000;
            for (int i = 0; i < Content.MaxCodexBonus * Content.CodexPerPoint; i++) profile.Codex.Add("probe.codex." + i);
            layout = HeroTreeLayout.ForHero(hero);
            for (int i = 0; i < layout.Nodes.Count; i++) indexOf[layout.Nodes[i].Id] = i;
        }

        /// <summary>Stars the player can never buy: reaching them, or one of their options or ranks, always needs a refund or is refused.</summary>
        public List<string> Problems { get; } = new List<string>();

        /// <summary>The stars owned on the path that reached the star (for diagnosing a failure), or null when it was never reached.</summary>
        public string Path(string id) => reached.TryGetValue(id, out var state)
            ? string.Join(" ", state.Ranks.Keys.Select(k => k + (state.Choices.TryGetValue(k, out int o) ? "/o" + o : ""))) : null;

        /// <summary>Stars that were reachable only after buying an unrelated star first (informational).</summary>
        public Dictionary<string, string> NeedsCompanion { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        private IEnumerable<TalentDef> Stars => layout.Nodes.Where(n => n.Talent != null && !n.Talent.IsKeystone).Select(n => n.Talent);

        public void Run()
        {
            var pending = Stars.Select(t => t.Id).ToList();
            bool progress = true, companions = false;
            while (progress || !companions)
            {
                if (!progress) companions = true;
                progress = false;
                foreach (string id in pending.ToList())
                {
                    var star = layout.Nodes[indexOf[id]].Talent;
                    var found = Reach(star, companions);
                    if (found == null) continue;
                    reached[id] = found.Value.Entry;
                    if (found.Value.Companion != null) NeedsCompanion[id] = found.Value.Companion;
                    pending.Remove(id);
                    progress = true;
                    if (companions) break; // a plain pass may now succeed where the companion search was needed
                }
                if (companions && progress) companions = false;
            }
            foreach (string id in pending) Problems.Add("unreachable: " + Describe(layout.Nodes[indexOf[id]].Talent));
            foreach (var star in Stars)
                if (reached.ContainsKey(star.Id)) CheckRanksAndOptions(star);
        }

        private static string Describe(TalentDef star) => star.Id + " (" + star.Name + ")";

        private IEnumerable<State> Bases(TalentDef star)
        {
            foreach (int neighbour in layout.Nodes[indexOf[star.Id]].Neighbors)
            {
                if (neighbour == layout.StartIndex) { yield return new State(); continue; }
                // Only one essence slot per kind ever counts, so a slot star is a destination, not a way through to the hero's own
                // stars. The outer clusters are the exception: they hang off the end of a route, behind its slot star.
                if (IsEssenceSlot(layout.Nodes[neighbour].Talent) && !IsOuter(star)) continue;
                if (reached.TryGetValue(layout.Nodes[neighbour].Id, out var state)) yield return state.Clone();
            }
        }

        private (State Entry, string Companion)? Reach(TalentDef star, bool companions)
        {
            // A path that wanders through an essence slot star is a poor path: only one slot per kind counts, so it wastes
            // the slot. Prefer a path without one and fall back to it only when nothing else exists.
            (State Entry, string Companion)? fallback = null;
            foreach (var basis in Bases(star).OrderBy(HasSlot))
            {
                var attempt = WithRequiredStars(basis, star);
                if (attempt == null) continue;
                var entry = Buy(attempt, star, 1);
                if (entry != null)
                {
                    if (!HasSlot(attempt) || IsOuter(star)) return (entry, null);
                    if (fallback == null) fallback = (entry, null);
                }
                if (!companions) continue;
                foreach (var candidate in Companions(star))
                {
                    var merged = attempt.Clone();
                    merged.Merge(reached[candidate]);
                    entry = Buy(merged, star, 1);
                    if (entry == null) continue;
                    if (!HasSlot(merged) || IsOuter(star)) return (entry, candidate);
                    if (fallback == null) fallback = (entry, candidate);
                }
            }
            return companions ? fallback : null;
        }

        /// <summary>Adds the allocations that bring in the stars a star lists as prerequisites, when the path to it did not already own them.</summary>
        private State WithRequiredStars(State basis, TalentDef star)
        {
            var authored = star.AuthoredStar;
            if (authored == null) return basis;
            var state = basis.Clone();
            foreach (string required in authored.RequiredStarIds)
            {
                if (state.Ranks.ContainsKey(required)) continue;
                if (!reached.TryGetValue(required, out var owned)) return null;
                state.Merge(owned);
            }
            if (authored.RequiredAnyStarIds.Count > 0 && !authored.RequiredAnyStarIds.Any(state.Ranks.ContainsKey))
            {
                string any = authored.RequiredAnyStarIds.FirstOrDefault(reached.ContainsKey);
                if (any == null) return null;
                state.Merge(reached[any]);
            }
            return state;
        }

        /// <summary>Stars worth owning first: the hero's own routes and base stars, and the star's own cluster.</summary>
        private IEnumerable<string> Companions(TalentDef star) => CompanionPool(star).OrderBy(id => HasSlot(reached[id]));

        private bool HasSlot(State state) => state.Ranks.Keys.Any(id => indexOf.TryGetValue(id, out int i) && IsEssenceSlot(layout.Nodes[i].Talent));

        private IEnumerable<string> CompanionPool(TalentDef star)
        {
            foreach (var pair in reached)
            {
                var other = layout.Nodes[indexOf[pair.Key]].Talent;
                if (other.RouteId != null || other.Cluster == null && !other.IsOuterAnchor) yield return pair.Key;
                else if (star.Cluster != null && other.Cluster != null && other.Cluster.Id == star.Cluster.Id) yield return pair.Key;
            }
        }

        private void Load(State state)
        {
            allocation.Talents.Clear();
            allocation.TalentChoices.Clear();
            foreach (var rank in state.Ranks) allocation.Talents[rank.Key] = rank.Value;
            foreach (var choice in state.Choices) allocation.TalentChoices[choice.Key] = choice.Value;
        }

        private bool Purchasable(State state, TalentDef star, int option)
        {
            Load(state);
            if (!layout.CanReach(allocation, star)) return false;
            try
            {
                var plan = Rules.PreviewAllocationChange(profile, hero, new AllocationChange
                {
                    Kind = AllocationChangeKind.Purchase, CandidateStarId = star.Id, SelectedOption = star.IsChoice ? (int?)option : null,
                });
                return plan.CanApply && plan.AffectedRefundIds.Count == 0;
            }
            catch (InvalidOperationException) { return false; }
        }

        private bool Switchable(State state, TalentDef star, int option)
        {
            Load(state);
            try
            {
                var plan = Rules.PreviewAllocationChange(profile, hero, new AllocationChange
                {
                    Kind = AllocationChangeKind.Choice, CandidateStarId = star.Id, SelectedOption = option,
                });
                return plan.CanApply;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>The allocation after buying rank 1 of the star (with the first option that works), or null when no option can be bought.</summary>
        private State Buy(State state, TalentDef star, int rank)
        {
            int options = star.IsChoice ? star.Choices.Count : 1;
            for (int option = 0; option < options; option++)
            {
                if (!Purchasable(state, star, option)) continue;
                var next = state.Clone();
                next.Ranks[star.Id] = rank;
                if (star.IsChoice) next.Choices[star.Id] = option;
                return next;
            }
            return null;
        }

        private static bool IsOuter(TalentDef star) => star.IsOuterAnchor || star.Cluster?.Region.Kind == ClusterRegionKind.Outer;

        private static bool IsEssenceSlot(TalentDef star) => star.Stat == Stat.EssenceSlotIdentity || star.Stat == Stat.EssenceSlotMovement;

        /// <summary>Every option of a choice star, and every rank after the first, must be buyable on the same path.</summary>
        private void CheckRanksAndOptions(TalentDef star)
        {
            if (IsEssenceSlot(star)) return; // only one slot per kind ever counts; a second one is refused by design
            int options = star.IsChoice ? star.Choices.Count : 1;
            var entry = reached[star.Id];
            var basis = entry.Clone();
            basis.Ranks.Remove(star.Id);
            basis.Choices.Remove(star.Id);
            for (int option = 0; option < options; option++)
            {
                var current = basis.Clone();
                if (star.IsChoice) current.Choices[star.Id] = option;
                for (int rank = 1; rank <= star.MaxRank; rank++)
                {
                    var step = rank == 1 ? OptionEntry(current, star, option) : Rank(current, star, option, rank);
                    if (step == null)
                    {
                        Problems.Add("refused: " + Describe(star) + (star.IsChoice ? " option " + option : "") + " rank " + rank + "/" + star.MaxRank);
                        break;
                    }
                    current = step;
                }
            }
        }

        private State OptionEntry(State basis, TalentDef star, int option)
        {
            if (Purchasable(basis, star, option)) return Take(basis, star, option, 1);
            foreach (var candidate in Companions(star))
            {
                var merged = basis.Clone();
                merged.Merge(reached[candidate]);
                if (merged.Ranks.ContainsKey(star.Id))
                {
                    // The way to that star's recipient runs through the star itself: take the other option first, then switch.
                    if (merged.Choices.TryGetValue(star.Id, out int held) && held != option && Switchable(merged, star, option)) return Take(merged, star, option, 1);
                    continue;
                }
                if (Purchasable(merged, star, option)) return Take(merged, star, option, 1);
            }
            return null;
        }

        private State Rank(State basis, TalentDef star, int option, int rank) =>
            Purchasable(basis, star, option) ? Take(basis, star, option, rank) : null;

        private static State Take(State basis, TalentDef star, int option, int rank)
        {
            var next = basis.Clone();
            next.Ranks[star.Id] = rank;
            if (star.IsChoice) next.Choices[star.Id] = option;
            return next;
        }
    }
}
