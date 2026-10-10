using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public static partial class HostBuildValidation
    {
        public static Profile CompatibleProfile(Profile profile, string heroKey)
        {
            var source = profile.Hero(heroKey);
            if (source.ExtraPoints == 0) return profile;
            var snapshot = profile.Clone();
            var hero = snapshot.Hero(heroKey);
            hero.ExtraPoints = 0;
            Rules.ResetTalents(snapshot, heroKey);
            var engine = Rules.AllocationValidationForHero(heroKey);
            int remaining = snapshot.TalentPoints(heroKey);
            var pending = new List<string>(source.Talents.Keys);
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < pending.Count;)
                {
                    string id = pending[i];
                    var talent = engine.Talent(id);
                    if (talent == null || !engine.CanReach(hero, talent)) { i++; continue; }
                    int ranks = Math.Min(source.Talents[id], remaining / talent.RankCost);
                    if (ranks > 0)
                    {
                        hero.Talents.Add(id, ranks);
                        if (source.TalentChoices.TryGetValue(id, out int choice)) hero.TalentChoices.Add(id, choice);
                        remaining -= ranks * talent.RankCost;
                        changed = true;
                    }
                    pending.RemoveAt(i);
                }
                foreach (string id in source.Keystones)
                {
                    if (id == null || hero.HasKeystone(id)) continue;
                    var talent = engine.Talent(id);
                    int cost = talent?.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
                    if (talent == null || remaining < cost || hero.KeystoneCount >= hero.KeystoneSlotCount
                        || !engine.KeystoneUnlocked(hero, heroKey, talent)) continue;
                    hero.AddKeystone(id);
                    remaining -= cost;
                    changed = true;
                }
            } while (changed);
            return snapshot;
        }
    }
}
