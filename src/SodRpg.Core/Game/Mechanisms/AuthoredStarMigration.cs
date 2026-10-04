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
        /// <summary>
        /// The original star was itself a Choice, so a stored option index belongs to the old effect and cannot be kept.
        /// Set by StarClusters.RegisterMigrations from the baseline tree; a star that was not a choice but now is keeps an explicit pick.
        /// </summary>
        public bool LegacyWasChoice { get; internal set; }
    }

    public sealed class StarMigrationRefund
    {
        internal StarMigrationRefund(List<string> ids, int points, List<string> changedIds, int changedPoints)
        {
            StarIds = ids.AsReadOnly(); RefundCost = points;
            ChangedStarIds = changedIds.AsReadOnly(); ChangedRefundCost = changedPoints;
        }
        /// <summary>Every refunded star or keystone, whatever the reason.</summary>
        public IReadOnlyList<string> StarIds { get; }
        public int RefundCost { get; }
        /// <summary>Stars and keystones refunded because their effect was redefined (a subset of StarIds).</summary>
        public IReadOnlyList<string> ChangedStarIds { get; }
        /// <summary>Points returned for ChangedStarIds only, at the original per-rank cost.</summary>
        public int ChangedRefundCost { get; }
    }

    public static class AuthoredStarMigration
    {
        /// <summary>The revision stamped on a hero once the registered migration rules have been applied.</summary>
        public const int CurrentVersion = 1;
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
                // A keystone keeps its ID and single rank; its old cost is the rule's RankCost (legacy keys cost Content.KeystoneCost).
                if (rule == null || rule.MaxRank <= 0 || rule.RankCost <= 0 || costs.ContainsKey(rule.LocalStarId)
                    || !nodes.TryGetValue(rule.LocalStarId, out var node) || node.MaxRank != rule.MaxRank
                    || !node.IsKeystone && node.RankCost != rule.RankCost)
                    throw new InvalidOperationException("Migration must retain original ID, rank and cost: " + rule?.LocalStarId);
                costs.Add(rule.LocalStarId, rule);
            }
            bool Redefined(string id) => hero.AuthoredMigrationVersion < migrationVersion
                && costs.TryGetValue(id, out var rule) && rule.ChangedEffect;
            var candidate = new HeroState { Kills = hero.Kills, StarXp = hero.StarXp };
            foreach (var allocation in hero.Talents)
            {
                if (!nodes.TryGetValue(allocation.Key, out var node) || allocation.Value <= 0 || allocation.Value > node.MaxRank)
                    throw new InvalidOperationException("Invalid migration allocation: " + allocation.Key);
                candidate.Talents.Add(allocation.Key, allocation.Value);
            }
            candidate.CopyKeystonesFrom(hero);
            foreach (var choice in hero.TalentChoices) candidate.TalentChoices.Add(choice.Key, choice.Value);
            var refunded = new List<string>();
            var redefinedIds = new List<string>();
            int points = 0, redefinedPoints = 0;
            void Refund(string id)
            {
                int rank = candidate.Talents[id];
                int amount = rank * (costs.TryGetValue(id, out var old) ? old.RankCost : nodes[id].RankCost);
                points = checked(points + amount);
                if (Redefined(id)) { redefinedPoints = checked(redefinedPoints + amount); redefinedIds.Add(id); }
                candidate.Talents.Remove(id); candidate.TalentChoices.Remove(id); refunded.Add(id);
            }
            var invalid = new List<string>();
            foreach (var allocation in candidate.Talents)
            {
                var node = nodes[allocation.Key];
                // A redefined effect never keeps ranks bought under the old meaning. The one exception is a star that became a
                // Choice: it has no old option index, so an explicit pick already recorded for it is the new design's.
                if (Redefined(node.Id) && (!node.IsChoice || costs[node.Id].LegacyWasChoice)) { invalid.Add(node.Id); continue; }
                bool needsChoice = node.IsChoice || node.AuthoredStar?.RequiresExplicitSelection == true || Redefined(node.Id);
                if (needsChoice && (!node.IsChoice || !candidate.TalentChoices.TryGetValue(node.Id, out int option)
                    || option < 0 || option >= node.Choices.Count)) invalid.Add(node.Id);
            }
            foreach (string id in invalid) Refund(id);
            foreach (string selected in candidate.Keystones)
            {
                if (selected == null || !Redefined(selected)) continue;
                refunded.Add(selected); redefinedIds.Add(selected);
                int old = costs[selected].RankCost;
                points = checked(points + old); redefinedPoints = checked(redefinedPoints + old);
                candidate.RemoveKeystone(selected);
            }
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
                foreach (string selected in candidate.Keystones)
                {
                    if (selected == null) continue;
                    TalentDef key;
                    if (!nodes.TryGetValue(selected, out key) || !layout.CanReach(candidate, key) || !EligibleKey(key))
                    {
                        refunded.Add(selected);
                        points = checked(points + (key?.KeystoneDefinition?.Cost ?? Content.KeystoneCost));
                        candidate.RemoveKeystone(selected);
                        changed = true;
                    }
                }
            } while (changed);
            // 枠が減った（星のレベルが下がることは通常ないが、データの都合で減った）場合：
            // 後から選んだ刻印から外し、その費用を戻す。
            while (candidate.KeystoneCount > candidate.KeystoneSlotCount)
            {
                string removed = candidate.Keystones[candidate.KeystoneCount - 1];
                refunded.Add(removed);
                points = checked(points + (nodes.TryGetValue(removed, out var removedKey) ? removedKey.KeystoneDefinition?.Cost ?? Content.KeystoneCost : Content.KeystoneCost));
                candidate.RemoveKeystone(removed);
            }
            // CanReach admits a candidate adjacent to the allocated graph; it never admits an allocated disconnected island.
            if (!layout.AllocationsConnected(candidate, null, candidate.Keystones))
                throw new InvalidOperationException("Migration leaves an invalid keystone or disconnected allocation.");
            hero.Talents.Clear(); foreach (var allocation in candidate.Talents) hero.Talents.Add(allocation.Key, allocation.Value);
            hero.TalentChoices.Clear();
            foreach (var choice in candidate.TalentChoices) if (hero.Talents.ContainsKey(choice.Key)) hero.TalentChoices.Add(choice.Key, choice.Value);
            hero.CopyKeystonesFrom(candidate);
            if (migrations.Count > 0) hero.AuthoredMigrationVersion = Math.Max(hero.AuthoredMigrationVersion, migrationVersion);
            return new StarMigrationRefund(refunded, points, redefinedIds, redefinedPoints);
        }
    }
}
