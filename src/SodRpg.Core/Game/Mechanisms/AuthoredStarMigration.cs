using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed class LegacyStarMigration
    {
        public LegacyStarMigration(string localStarId, int maxRank, int rankCost, bool changedEffect = false)
        { LocalStarId = localStarId; MaxRank = maxRank; RankCost = rankCost; ChangedEffect = changedEffect; }
        public string LocalStarId { get; }
        public int MaxRank { get; }
        public int RankCost { get; }
        public bool ChangedEffect { get; }
    }

    public sealed class StarMigrationRefund
    {
        internal StarMigrationRefund(List<string> ids, int points) { StarIds = ids.AsReadOnly(); RefundCost = points; }
        public IReadOnlyList<string> StarIds { get; }
        public int RefundCost { get; }
    }

    public static class AuthoredStarMigration
    {
        /// <summary>The installed 6+3+4+1 IDs, in original order. No designed replacements are registered here.</summary>
        public static readonly IReadOnlyList<LegacyStarMigration> CetusRetained = Array.AsReadOnly(new[]
        {
            new LegacyStarMigration("h.cetus.cluster.icy-veins.1", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.icy-veins.2", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.icy-veins.3", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.icy-veins.4", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.icy-veins.5", 1, 1, true),
            new LegacyStarMigration("h.cetus.cluster.icy-veins.6", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.frozen-recall.1", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.frozen-recall.2", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.frozen-recall.3", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.abyssal-shell.1", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.abyssal-shell.2", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.abyssal-shell.3", 1, 1),
            new LegacyStarMigration("h.cetus.cluster.abyssal-shell.4", 1, 1),
            new LegacyStarMigration("h.cetus.outer.abyssal-shell", 1, 1)
        });

        /// <summary>One-way migration, atomic and idempotent. Removal frees original spent points.</summary>
        public static StarMigrationRefund Apply(HeroState hero, IReadOnlyList<TalentDef> tree,
            IReadOnlyList<LegacyStarMigration> migrations, int migrationVersion = 1)
        {
            if (hero == null || tree == null || migrations == null) throw new ArgumentNullException();
            if (migrationVersion <= 0) throw new ArgumentOutOfRangeException(nameof(migrationVersion));
            var nodes = new Dictionary<string, TalentDef>(StringComparer.Ordinal);
            foreach (var node in tree) nodes.Add(node.Id, node);
            var costs = new Dictionary<string, LegacyStarMigration>(StringComparer.Ordinal);
            foreach (var rule in migrations)
            {
                if (rule == null || rule.MaxRank <= 0 || rule.RankCost <= 0 || costs.ContainsKey(rule.LocalStarId)
                    || !nodes.TryGetValue(rule.LocalStarId, out var node) || node.MaxRank != rule.MaxRank || node.RankCost != rule.RankCost)
                    throw new InvalidOperationException("Migration must retain original ID, rank and cost: " + rule?.LocalStarId);
                costs.Add(rule.LocalStarId, rule);
            }
            var candidate = new HeroState { Kills = hero.Kills };
            foreach (var allocation in hero.Talents)
            {
                if (!nodes.TryGetValue(allocation.Key, out var node) || allocation.Value <= 0 || allocation.Value > node.MaxRank)
                    throw new InvalidOperationException("Invalid migration allocation: " + allocation.Key);
                candidate.Talents.Add(allocation.Key, allocation.Value);
            }
            candidate.Keystone = hero.Keystone;
            foreach (var choice in hero.TalentChoices) candidate.TalentChoices.Add(choice.Key, choice.Value);
            var refunded = new List<string>();
            int points = 0;
            void Refund(string id)
            {
                int rank = candidate.Talents[id];
                points = checked(points + rank * (costs.TryGetValue(id, out var old) ? old.RankCost : nodes[id].RankCost));
                candidate.Talents.Remove(id); candidate.TalentChoices.Remove(id); refunded.Add(id);
            }
            var invalid = new List<string>();
            foreach (var allocation in candidate.Talents)
            {
                var node = nodes[allocation.Key];
                bool needsChoice = node.IsChoice || node.AuthoredStar?.RequiresExplicitSelection == true
                    || hero.AuthoredMigrationVersion < migrationVersion && costs.TryGetValue(node.Id, out var old) && old.ChangedEffect;
                if (needsChoice && (!node.IsChoice || !candidate.TalentChoices.TryGetValue(node.Id, out int option)
                    || option < 0 || option >= node.Choices.Count)) invalid.Add(node.Id);
            }
            foreach (string id in invalid) Refund(id);
            var layout = HeroTreeLayout.ForTalents(tree);
            bool EligibleKey(TalentDef key)
            {
                int ranks = 0;
                foreach (var allocation in candidate.Talents)
                    if (nodes.TryGetValue(allocation.Key, out var talent) && !talent.IsKeystone
                        && (key.HeroKey != null ? talent.HeroKey == key.HeroKey : talent.Route == key.Route))
                        ranks += allocation.Value;
                return key.IsKeystone && ranks >= Content.KeystoneRouteRequirement
                    && (key.HeroKey == null || Mastery.Level(candidate.Kills) >= HeroSigils.KeystoneMastery);
            }
            bool changed;
            do
            {
                changed = false; invalid.Clear();
                foreach (var allocation in candidate.Talents)
                    if (!layout.CanReach(candidate, nodes[allocation.Key])) invalid.Add(allocation.Key);
                foreach (string id in invalid) { Refund(id); changed = true; }
                if (candidate.Keystone != null && (!nodes.TryGetValue(candidate.Keystone, out var key)
                    || !layout.CanReach(candidate, key) || !EligibleKey(key)))
                {
                    refunded.Add(candidate.Keystone);
                    points = checked(points + (key?.KeystoneDefinition?.Cost ?? Content.KeystoneCost));
                    candidate.Keystone = null;
                    changed = true;
                }
            } while (changed);
            // CanReach admits a candidate adjacent to the allocated graph; it never admits an allocated disconnected island.
            if (!layout.AllocationsConnected(candidate, null, candidate.Keystone))
                throw new InvalidOperationException("Migration leaves an invalid keystone or disconnected allocation.");
            hero.Talents.Clear(); foreach (var allocation in candidate.Talents) hero.Talents.Add(allocation.Key, allocation.Value);
            hero.TalentChoices.Clear();
            foreach (var choice in candidate.TalentChoices) if (hero.Talents.ContainsKey(choice.Key)) hero.TalentChoices.Add(choice.Key, choice.Value);
            hero.Keystone = candidate.Keystone;
            if (migrations.Count > 0) hero.AuthoredMigrationVersion = Math.Max(hero.AuthoredMigrationVersion, migrationVersion);
            return new StarMigrationRefund(refunded, points);
        }
    }
}
