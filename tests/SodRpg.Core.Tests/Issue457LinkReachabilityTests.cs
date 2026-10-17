using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    public class Issue457LinkReachabilityTests
    {
        private readonly ITestOutputHelper output;

        public Issue457LinkReachabilityTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void Masterwork_flurry_can_activate_in_a_legal_Mist_loadout()
        {
            var unique = Assert.Single(Content.Uniques, u => u.Id == "unique.link_mist_masterwork");
            Assert.NotNull(unique.Link);
            // Native loadout choices: docs/specs/v1.27-traveler-kits.md, Hero_Mist row.
            // HeroSkill.OnLateStartServer equips one choice in each slot; Identity cannot be replaced.
            var identities = new[] { "St_D_AstridsMasterpieceEnGarde", "St_D_AstridsMasterpiecePriorite" };
            var primaries = new[] { "St_Q_Lunge", "St_Q_Fleche" };
            var ultimates = new[] { "St_R_UnbreakableDetermination", "St_R_Parry" };
            int checkedLoadouts = 0, activeLoadouts = 0;
            foreach (string identity in identities)
            foreach (string primary in primaries)
            foreach (string ultimate in ultimates)
            {
                var equipped = new[] { identity, primary, ultimate, "St_M_FastFeet" };
                bool active = Links.Satisfied(unique.Link, "Hero_Mist", equipped, Array.Empty<string>());
                checkedLoadouts++;
                if (active) activeLoadouts++;
                output.WriteLine("{0}: active={1}", string.Join(" + ", equipped), active);
            }
            output.WriteLine("Legal Mist loadouts: {0}; active: {1}", checkedLoadouts, activeLoadouts);
            output.WriteLine("Diagnostic only, impossible dual-Identity input: active={0}",
                Links.Satisfied(unique.Link, "Hero_Mist", identities, Array.Empty<string>()));
            Assert.True(activeLoadouts > 0,
                $"{unique.Id} ({unique.Name.Ja}) cannot activate in any of {checkedLoadouts} legal Mist loadouts; " +
                "only one Astrid's Masterpiece Identity memory can be equipped. Requires: " +
                string.Join(" + ", unique.Link.Requires));
        }

        [Fact]
        public void Every_relic_link_can_be_satisfied_with_at_most_one_Identity_and_one_Movement()
        {
            var failures = new List<string>();
            int checkedLinks = 0;
            foreach (var unique in Content.Uniques.Where(u => u.Link != null))
                Check(unique.Id, unique.Link);
            foreach (var set in Content.Sets)
            foreach (var stage in set.LinkStages)
                Check(set.Id + "/" + stage.RequiredPieces, stage.Link);

            output.WriteLine("Checked relic/set links: {0}; unreachable or untriggerable: {1}", checkedLinks, failures.Count);
            foreach (string failure in failures) output.WriteLine(failure);
            Assert.True(failures.Count == 0,
                "Relic links with no slot-limited witness or no usable memory trigger:\n" +
                string.Join("\n", failures));

            void Check(string id, LinkDef link)
            {
                checkedLinks++;
                if (!HasSlotLimitedWitness(link))
                    failures.Add(id + ": " + string.Join(" + ", link.Requires));
                // HostAuthority.OnSkillUse only triggers haste/surge for a used memory in Requires.
                // Passive Identity memories never produce that use event.
                if ((link.Kind == LinkKind.MemoryHaste || link.Kind == LinkKind.MemorySurge)
                    && !link.Requires.Any(r => Links.IsMemory(r) && !IsIdentity(r)))
                    failures.Add(id + ": " + link.Kind + " has no non-Identity memory trigger: " +
                        string.Join(" + ", link.Requires));
            }
        }

        // This checks the reported necessary slot constraints, not every acquisition/event condition.
        // Ordinary memories/essences and living nearby travelers are granted to isolate slot conflicts.
        private static bool HasSlotLimitedWitness(LinkDef link)
        {
            string[] memories = link.Requires.Where(Links.IsMemory).ToArray();
            string[] identities = memories.Where(IsIdentity).Prepend(null).ToArray();
            string[] movements = memories.Where(IsMovement).Prepend(null).ToArray();
            string[] ordinary = memories.Where(m => !IsIdentity(m) && !IsMovement(m)).ToArray();
            string[] essences = link.Requires.Where(Links.IsEssence).ToArray();
            string[] travelers = link.Requires.Where(Links.IsTraveler).ToArray();
            string hero = travelers.FirstOrDefault() ?? "Hero_Mist";
            foreach (string identity in identities)
            foreach (string movement in movements)
            {
                var equipped = new List<string>(ordinary);
                if (identity != null) equipped.Add(identity);
                if (movement != null) equipped.Add(movement);
                if (Links.Satisfied(link, hero, equipped, essences, travelers)) return true;
            }
            return false;
        }

        // Verified Identity and Movement groups are listed in Links.MemoryNames and traveler kits.
        private static bool IsIdentity(string memory) => memory.StartsWith("St_D_", StringComparison.Ordinal);
        private static bool IsMovement(string memory) => memory.StartsWith("St_M_", StringComparison.Ordinal);
    }
}
