using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>A saved allocation only; experience, point bonuses and equipment belong to the hero.</summary>
    public sealed class StarLoadout
    {
        public string Name { get; set; }
        public SortedDictionary<string, int> Talents { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> TalentChoices { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public string[] Keystones { get; } = new string[KeystoneSlots.Max];
        public int AuthoredMigrationVersion { get; set; }
        public bool IsEmpty => Talents.Count == 0 && TalentChoices.Count == 0 && Array.TrueForAll(Keystones, id => id == null);

        public static StarLoadout Capture(HeroState hero, string name = null)
        {
            var slot = new StarLoadout { Name = name, AuthoredMigrationVersion = hero.AuthoredMigrationVersion };
            foreach (var rank in hero.Talents) slot.Talents.Add(rank.Key, rank.Value);
            foreach (var choice in hero.TalentChoices) slot.TalentChoices.Add(choice.Key, choice.Value);
            Array.Copy(hero.Keystones, slot.Keystones, slot.Keystones.Length);
            return slot;
        }

        public StarLoadout Clone()
        {
            var copy = new StarLoadout { Name = Name, AuthoredMigrationVersion = AuthoredMigrationVersion };
            foreach (var rank in Talents) copy.Talents.Add(rank.Key, rank.Value);
            foreach (var choice in TalentChoices) copy.TalentChoices.Add(choice.Key, choice.Value);
            Array.Copy(Keystones, copy.Keystones, Keystones.Length);
            return copy;
        }

        public void ApplyTo(HeroState hero)
        {
            hero.Talents.Clear();
            foreach (var rank in Talents) hero.Talents.Add(rank.Key, rank.Value);
            hero.TalentChoices.Clear();
            foreach (var choice in TalentChoices) hero.TalentChoices.Add(choice.Key, choice.Value);
            Array.Copy(Keystones, hero.Keystones, Keystones.Length);
            hero.AuthoredMigrationVersion = AuthoredMigrationVersion;
        }
    }

    public static class StarLoadouts
    {
        public const int Count = 5;
        public const int MaxNameLength = 24;

        public static string DefaultName(int index, bool japanese) => japanese ? "ロードアウト" + (index + 1) : "Loadout " + (index + 1);
        public static string DisplayName(HeroState hero, int index, bool japanese) => hero.StarLoadouts[index]?.Name ?? DefaultName(index, japanese);

        public static string NormalizeName(string name, int index)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name == DefaultName(index, true) || name == DefaultName(index, false)) return null;
            name = name.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (name.Length <= MaxNameLength) return name;
            int length = char.IsHighSurrogate(name[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength;
            return name.Substring(0, length);
        }

        public static bool HasSavedState(HeroState hero)
        {
            if (hero.ActiveStarLoadout != 0) return true;
            for (int i = 0; i < Count; i++)
            {
                var slot = hero.StarLoadouts[i];
                if (slot != null && (NormalizeName(slot.Name, i) != null || i != hero.ActiveStarLoadout && !slot.IsEmpty)) return true;
            }
            return false;
        }

        private static HeroState EditableHero(Profile profile, string heroKey, int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (profile.Run != null) throw new InvalidOperationException(Loc.T("遠征中はロードアウトを変更できません。", "Loadouts cannot be changed during an expedition.", "远征中无法更改配置。"));
            return profile.Hero(heroKey);
        }

        public static void Rename(Profile profile, string heroKey, int index, string name)
        {
            var hero = EditableHero(profile, heroKey, index);
            var slot = hero.StarLoadouts[index] ?? (hero.StarLoadouts[index] = new StarLoadout());
            slot.Name = NormalizeName(name, index);
        }

        public static void CopyCurrent(Profile profile, string heroKey, int index)
        {
            var hero = EditableHero(profile, heroKey, index);
            hero.StarLoadouts[index] = StarLoadout.Capture(hero, hero.StarLoadouts[index]?.Name);
        }

        public static void Switch(Profile profile, string heroKey, int index, List<string> notes = null)
        {
            var hero = EditableHero(profile, heroKey, index);
            if (hero.ActiveStarLoadout == index) return;
            var loaded = Repair(profile, heroKey, hero.StarLoadouts[index] ?? new StarLoadout
            {
                AuthoredMigrationVersion = StarClusters.MigrationVersionFor(heroKey),
            }, index, notes);
            hero.StarLoadouts[hero.ActiveStarLoadout] = StarLoadout.Capture(hero, hero.StarLoadouts[hero.ActiveStarLoadout]?.Name);
            loaded.ApplyTo(hero);
            hero.StarLoadouts[index] = loaded;
            hero.ActiveStarLoadout = index;
        }

        /// <summary>Validate a detached slot with today's shared budget and gear. Failure empties only this slot.</summary>
        internal static StarLoadout Repair(Profile profile, string heroKey, StarLoadout slot, int index, List<string> notes)
        {
            var hero = profile.Hero(heroKey).CloneAllocation();
            slot.ApplyTo(hero);
            var draft = new Profile { DreamLevel = profile.DreamLevel };
            draft.Heroes.Add(heroKey, hero);
            draft.Codex.UnionWith(profile.Codex);
            draft.Stash.AddRange(profile.Stash);
            bool repaired = false;
            try
            {
                var engine = Rules.AllocationValidationForHero(heroKey);
                foreach (string id in new List<string>(hero.Talents.Keys))
                {
                    var def = engine.Talent(id);
                    if (def == null || def.IsKeystone || !Rules.BelongsTo(def, heroKey) || hero.Talents[id] <= 0)
                    { hero.Talents.Remove(id); hero.TalentChoices.Remove(id); repaired = true; }
                    else if (hero.Talents[id] > def.MaxRank) { hero.Talents[id] = def.MaxRank; repaired = true; }
                }
                foreach (string id in new List<string>(hero.TalentChoices.Keys))
                {
                    var def = engine.Talent(id);
                    if (!hero.Talents.ContainsKey(id) || def == null || !def.IsChoice)
                    { hero.TalentChoices.Remove(id); repaired = true; }
                }
                foreach (string id in new List<string>(hero.Talents.Keys))
                {
                    var def = engine.Talent(id);
                    if (def.IsChoice && (!hero.TalentChoices.TryGetValue(id, out int choice) || choice < 0 || choice >= def.Choices.Count))
                    { hero.Talents.Remove(id); hero.TalentChoices.Remove(id); repaired = true; }
                }
                var keys = (string[])hero.Keystones.Clone();
                hero.ClearKeystones();
                foreach (string id in keys)
                {
                    if (id == null) continue;
                    var def = engine.Talent(id);
                    if (def != null && def.IsKeystone && Rules.BelongsTo(def, heroKey) && !hero.HasKeystone(id)) hero.AddKeystone(id);
                    else repaired = true;
                }
                var tree = HeroSigils.TreeFor(heroKey);
                var refund = AuthoredStarMigration.Apply(hero, tree, StarClusters.MigrationsFor(heroKey));
                repaired |= refund.StarIds.Count != 0;
                if (Rules.FreePoints(draft, heroKey) < 0 || !engine.AllocationsConnected(hero))
                { Rules.ResetTalents(draft, heroKey); repaired = true; }
                repaired |= engine.RepairSavedAllocation(draft, heroKey);
                if (repaired) notes?.Add(Loc.T($"{heroKey}: ロードアウト{index + 1}の無効な星を無料で払い戻しました。",
                    $"{heroKey}: Invalid stars in loadout {index + 1} were refunded for free.", $"{heroKey}：配置{index + 1}中的无效星已免费退还点数。"));
                return StarLoadout.Capture(hero, slot.Name);
            }
            catch (Exception ex)
            {
                notes?.Add(Loc.T($"{heroKey}: ロードアウト{index + 1}を読み込めないため、この枠だけ空にしました（{ex.GetType().Name}）。",
                    $"{heroKey}: Could not load loadout {index + 1}; only this slot was emptied ({ex.GetType().Name}).", $"{heroKey}：无法读取配置{index + 1}，仅清空了此槽位（{ex.GetType().Name}）。"));
                return new StarLoadout { Name = slot.Name, AuthoredMigrationVersion = StarClusters.MigrationVersionFor(heroKey) };
            }
        }
    }
}
