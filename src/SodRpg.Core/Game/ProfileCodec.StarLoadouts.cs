using System;
using System.Collections.Generic;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        private static void WriteStarLoadouts(JsonObject heroJson, HeroState hero)
        {
            if (!StarLoadouts.HasSavedState(hero)) return;
            var slots = new List<object>(StarLoadouts.Count);
            for (int i = 0; i < StarLoadouts.Count; i++)
            {
                var slot = hero.StarLoadouts[i];
                // The existing talents/choices/keystones fields are authoritative for the active slot.
                if (i == hero.ActiveStarLoadout)
                {
                    slots.Add(new JsonObject().Add("name", StarLoadouts.NormalizeName(slot?.Name, i)));
                    continue;
                }
                if (slot == null || slot.IsEmpty && StarLoadouts.NormalizeName(slot.Name, i) == null) { slots.Add(null); continue; }
                var talents = new JsonObject();
                foreach (var rank in slot.Talents) talents.Add(rank.Key, (long)rank.Value);
                var choices = new JsonObject();
                var choiceIds = new List<string>(slot.TalentChoices.Keys);
                choiceIds.Sort(StringComparer.Ordinal);
                foreach (string id in choiceIds) choices.Add(id, (long)slot.TalentChoices[id]);
                var keys = new List<object>();
                foreach (string id in slot.Keystones) if (id != null) keys.Add(id);
                slots.Add(new JsonObject().Add("name", StarLoadouts.NormalizeName(slot.Name, i))
                    .Add("talents", talents).Add("talentChoices", choices).Add("keystones", keys)
                    .Add("authoredMigrationVersion", (long)slot.AuthoredMigrationVersion));
            }
            heroJson.Add("starLoadouts", new JsonObject().Add("active", (long)hero.ActiveStarLoadout).Add("slots", slots));
        }

        private static void ReadStarLoadouts(Profile profile, string heroKey, JsonObject heroJson, List<string> notes)
        {
            if (!heroJson.TryGet("starLoadouts", out object value)) return;
            var hero = profile.Hero(heroKey);
            if (!(value is JsonObject loadouts) || !loadouts.TryGet("active", out object activeValue)
                || !(activeValue is long active) || active < 0 || active >= StarLoadouts.Count
                || !loadouts.TryGet("slots", out object slotsValue) || !(slotsValue is List<object> slots))
            {
                notes.Add(Loc.T($"{heroKey}: ロードアウト情報を読み込めないため、現在の星を枠1に保持しました。",
                    $"{heroKey}: Invalid loadout metadata; current stars were retained in slot 1."));
                return;
            }
            hero.ActiveStarLoadout = (int)active;
            for (int i = 0; i < StarLoadouts.Count; i++)
            {
                if (i >= slots.Count || slots[i] == null) continue;
                try
                {
                    if (!(slots[i] is JsonObject entry)) throw new LedgerFormatException("Invalid loadout slot");
                    if (entry.TryGet("name", out object name) && name != null && !(name is string))
                        throw new LedgerFormatException("Invalid loadout name");
                    var slot = new StarLoadout { Name = StarLoadouts.NormalizeName(name as string, i),
                        AuthoredMigrationVersion = Clamp(Long(entry, "authoredMigrationVersion"), 0, int.MaxValue) };
                    if (i == hero.ActiveStarLoadout)
                    {
                        hero.StarLoadouts[i] = StarLoadout.Capture(hero, slot.Name);
                        continue;
                    }
                    if (!entry.TryGet("talents", out object ranksValue) || !(ranksValue is JsonObject ranks)
                        || !entry.TryGet("talentChoices", out object choicesValue) || !(choicesValue is JsonObject choices)
                        || !entry.TryGet("keystones", out object keysValue) || !(keysValue is List<object> keys)
                        || keys.Count > slot.Keystones.Length)
                        throw new LedgerFormatException("Invalid loadout allocation");
                    foreach (var rank in ranks.Properties)
                    {
                        if (!(rank.Value is long n) || n < 0 || n > int.MaxValue) throw new LedgerFormatException("Invalid loadout rank");
                        slot.Talents.Add(rank.Key, (int)n);
                    }
                    foreach (var choice in choices.Properties)
                    {
                        if (!(choice.Value is long n) || n < int.MinValue || n > int.MaxValue) throw new LedgerFormatException("Invalid loadout choice");
                        slot.TalentChoices.Add(choice.Key, (int)n);
                    }
                    for (int k = 0; k < keys.Count; k++)
                    {
                        if (keys[k] != null && !(keys[k] is string)) throw new LedgerFormatException("Invalid loadout keystone");
                        slot.Keystones[k] = keys[k] as string;
                    }
                    // Inactive snapshots keep their migration stamp and are reconciled when switched in.
                    // Frozen trade receipts must round-trip even if a slot was saved under different gear.
                    hero.StarLoadouts[i] = slot;
                }
                catch (Exception ex)
                {
                    hero.StarLoadouts[i] = new StarLoadout { AuthoredMigrationVersion = StarClusters.MigrationVersionFor(heroKey) };
                    if (i == hero.ActiveStarLoadout) hero.StarLoadouts[i].ApplyTo(hero);
                    notes.Add(Loc.T($"{heroKey}: ロードアウト{i + 1}を読み込めないため、この枠だけ空にしました（{ex.GetType().Name}）。",
                        $"{heroKey}: Could not load loadout {i + 1}; only this slot was emptied ({ex.GetType().Name})."));
                }
            }
        }
    }
}
