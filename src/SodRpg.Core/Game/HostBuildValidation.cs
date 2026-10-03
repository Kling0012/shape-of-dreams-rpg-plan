using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// Client-owned progression is not proof of ownership. This boundary proves structural legality:
    /// one hero's connected allocations, their shared point budget, and bounded equipped sources.
    /// Derived combat effects are compared with the production computation, never trusted independently.
    /// </summary>
    public static class HostBuildValidation
    {
        public const int MaxInputChars = 65536;
        public static int MaxSubmissionChars => checked(BuildLimits.MaxEncodedChars + 1 + MaxInputChars);
        private static readonly int StatCount = Enum.GetValues(typeof(Stat)).Length;
        private static readonly int PowerCount = Enum.GetValues(typeof(Power)).Length;
        private static readonly int PactCount = Enum.GetValues(typeof(Pact)).Length;

        // The outer envelope is protocol 13. Applied summaries keep the existing Build grammar.
        public static string Encode(Build derived, Profile profile, string heroKey, int heat,
            IEnumerable<Pact> pacts = null, int dailyId = 0)
        {
            if (derived == null || profile == null) throw new ArgumentNullException();
            var hero = profile.Hero(heroKey);
            var sb = new StringBuilder();
            sb.Append("D:").Append(profile.DreamLevel).Append(";H:").Append(heat)
                .Append(";M:").Append(hero.Kills).Append(";K:").Append(hero.Keystone).Append(";T:");
            bool first = true;
            foreach (var rank in hero.Talents)
            {
                if (rank.Value <= 0) continue;
                Token(rank.Key);
                if (!first) sb.Append(','); first = false;
                sb.Append(rank.Key).Append('=').Append(rank.Value).Append('=')
                    .Append(hero.TalentChoices.TryGetValue(rank.Key, out int choice) ? choice : -1);
            }
            if (hero.Keystone != null) Token(hero.Keystone);
            sb.Append(";R:"); first = true;
            for (int slot = 0; slot < hero.Equipped.Length; slot++)
            {
                if (hero.Equipped[slot] == null) continue;
                var relic = profile.FindStash(hero.Equipped[slot]);
                if (relic == null) throw new InvalidOperationException("Missing equipped source.");
                Token(relic.Uid); Token(relic.BaseId);
                if (relic.UniqueId != null) Token(relic.UniqueId);
                if (!first) sb.Append(','); first = false;
                sb.Append(slot).Append(':').Append(relic.Uid).Append(':').Append(relic.BaseId).Append(':')
                    .Append(relic.UniqueId).Append(':').Append((int)relic.Rarity).Append(':').Append(relic.ItemLevel)
                    .Append(':').Append(relic.Enhance).Append(':').Append(relic.LimitBreaks)
                    .Append(':').Append(relic.AwakenLevel).Append(':').Append(relic.AwakenPoints)
                    .Append(':').Append(relic.EnhanceMilestones).Append(':');
                bool lineFirst = true;
                foreach (var line in relic.Affixes)
                {
                    if (!lineFirst) sb.Append('+'); lineFirst = false;
                    sb.Append((int)line.Stat).Append('=').Append(line.Value);
                }
                sb.Append(':'); lineFirst = true;
                foreach (var line in relic.Powers)
                {
                    if (!lineFirst) sb.Append('+'); lineFirst = false;
                    sb.Append((int)line.Power).Append('=').Append(line.Value);
                }
            }
            sb.Append(";P:"); first = true;
            if (pacts != null) foreach (var pact in pacts)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append((int)pact);
            }
            sb.Append(";Y:").Append(dailyId);
            if (sb.Length > MaxInputChars) throw new InvalidOperationException("Build inputs exceed their envelope.");
            return derived.Encode() + "|" + sb;
        }

        /// <summary>Atomically reject invalid input; successful output contains only reconstructed source values.</summary>
        public static bool TryAccept(string submission, string heroKey, out Build validated, out string reason)
        {
            validated = null;
            reason = "malformed";
            if (string.IsNullOrEmpty(submission) || submission.Length > MaxSubmissionChars
                || string.IsNullOrEmpty(heroKey) || !BuildTransfer.Ascii(submission)) return false;
            int divider = submission.IndexOf('|');
            if (divider <= 0 || divider > BuildLimits.MaxEncodedChars || submission.LastIndexOf('|') != divider
                || submission.Length - divider - 1 > MaxInputChars) return false;
            try
            {
                string derivedText = submission.Substring(0, divider);
                var claimed = Build.Decode(derivedText);
                if (claimed == null) return false;
                var input = ReadInputs(submission.Substring(divider + 1), heroKey);
                var hero = input.Profile.Hero(heroKey);
                var engine = Rules.AllocationValidationForHero(heroKey);
                if (!TryValidateAllocation(hero, heroKey, engine, out int spent, out reason)) return false;
                var expected = Build.Compute(input.Profile, heroKey, input.Heat, input.Pacts, input.DailyId);
                if (claimed.SpentStarPoints != spent || claimed.DreamLevel != expected.DreamLevel || claimed.Heat != expected.Heat)
                    return Reject("progression", out reason);

                // Scalar totals are discarded in favor of legal equipped + star source values.
                // Compare the ORIGINAL effect sections: Decode's generic Clamp must not erase
                // an attacker's unsupported parameters, excessive values or fake contributors.
                if (!EffectsMatch(derivedText, expected.Encode())) return Reject("derived-effects", out reason);
                expected.SpentStarPoints = spent;
                validated = expected;
                reason = null;
                return true;
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        /// <summary>Shared host allocation boundary, also usable with explicitly generated hero definitions.</summary>
        public static bool TryValidateAllocation(HeroState hero, string heroKey, EffectiveAllocationValidation engine,
            out int spent, out string reason)
        {
            spent = 0;
            reason = null;
            if (hero == null || engine == null) return Reject("allocation", out reason);
            // A typed keystone declares its own cost (every other consumer reads KeystoneDefinition.Cost); legacy keys cost Content.KeystoneCost.
            long total = hero.Keystone == null ? 0 : engine.Talent(hero.Keystone)?.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
            foreach (var rank in hero.Talents)
            {
                var def = engine.Talent(rank.Key);
                if (def == null || def.IsKeystone || !Rules.BelongsTo(def, heroKey)
                    || rank.Value < 1 || rank.Value > def.MaxRank) return Reject("allocation", out reason);
                if (def.IsChoice)
                {
                    if (!hero.TalentChoices.TryGetValue(def.Id, out int choice)
                        || choice < 0 || choice >= def.Choices.Count) return Reject("choice", out reason);
                }
                else if (hero.TalentChoices.ContainsKey(def.Id)) return Reject("choice", out reason);
                total += (long)rank.Value * def.RankCost;
            }
            foreach (var choice in hero.TalentChoices)
                if (!hero.Talents.ContainsKey(choice.Key)) return Reject("choice", out reason);
            if (total > StarProgression.MaxSpendablePoints) return Reject("point-budget", out reason);
            if (!engine.AllocationsConnected(hero)) return Reject("disconnected", out reason);
            if (hero.Keystone != null && !engine.KeystoneUnlocked(hero, heroKey, engine.Talent(hero.Keystone)))
                return Reject("keystone", out reason);
            spent = (int)total;
            return true;
        }

        private static bool EffectsMatch(string claimed, string expected)
        {
            var actualSections = EffectSections(claimed);
            var expectedSections = EffectSections(expected);
            foreach (var section in actualSections)
                if (!expectedSections.TryGetValue(section.Key, out string value) || value != section.Value) return false;
            foreach (var section in expectedSections)
                if (!actualSections.ContainsKey(section.Key) && section.Value.Length > 0) return false;
            return true;
        }

        private static Dictionary<string, string> EffectSections(string text)
        {
            var effects = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string section in text.Split(';'))
            {
                int colon = section.IndexOf(':');
                string kind = section.Substring(0, colon);
                if (kind == "g" || kind == "l" || kind == "c" || kind == "f" || kind == "n" || kind == "j" || kind == "v")
                    effects.Add(kind, section.Substring(colon + 1));
            }
            return effects;
        }


        private sealed class Inputs
        {
            public readonly Profile Profile = new Profile();
            public readonly List<Pact> Pacts = new List<Pact>();
            public int Heat, DailyId;
        }

        private static Inputs ReadInputs(string text, string heroKey)
        {
            var input = new Inputs();
            var hero = input.Profile.Hero(heroKey);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string section in text.Split(';'))
            {
                int colon = section.IndexOf(':');
                if (colon != 1) throw new FormatException();
                string kind = section.Substring(0, 1), body = section.Substring(2);
                if (!seen.Add(kind)) throw new FormatException();
                switch (kind)
                {
                    case "D": input.Profile.DreamLevel = Range(body, 1, Content.MaxDreamLevel); break;
                    case "H": input.Heat = Range(body, 0, Content.MaxHeat); break;
                    case "M": hero.Kills = Range(body, 0, int.MaxValue); break;
                    case "K": if (body.Length > 0) { Token(body); hero.Keystone = body; } break;
                    case "T":
                        foreach (string entry in Entries(body, StarProgression.MaxSpendablePoints))
                        {
                            var fields = entry.Split('=');
                            if (fields.Length != 3) throw new FormatException();
                            Token(fields[0]);
                            hero.Talents.Add(fields[0], Range(fields[1], 1, StarProgression.MaxSpendablePoints));
                            int choice = Range(fields[2], -1, StarProgression.MaxSpendablePoints);
                            if (choice >= 0) hero.TalentChoices.Add(fields[0], choice);
                        }
                        break;
                    case "R":
                        var uids = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string entry in Entries(body, Content.SlotCount))
                        {
                            var fields = entry.Split(':');
                            if (fields.Length != 13) throw new FormatException();
                            int slot = Range(fields[0], 0, Content.SlotCount - 1);
                            Token(fields[1]); Token(fields[2]);
                            if (fields[3].Length > 0) Token(fields[3]);
                            if (hero.Equipped[slot] != null || !uids.Add(fields[1])) throw new FormatException();
                            var relic = new Relic
                            {
                                Uid = fields[1], BaseId = fields[2], UniqueId = fields[3].Length == 0 ? null : fields[3],
                                Rarity = (Rarity)Int(fields[4]), ItemLevel = Int(fields[5]), Enhance = Int(fields[6]),
                                LimitBreaks = Int(fields[7]), AwakenLevel = Int(fields[8]), AwakenPoints = Int(fields[9]),
                                EnhanceMilestones = Int(fields[10]),
                                // The existing milestone history carries the one-time boost on the wire.
                                MilestonePowerApplied = Int(fields[10]) >= 5,
                            };
                            foreach (string line in Lines(fields[11], StatCount))
                            {
                                var pair = line.Split('='); if (pair.Length != 2) throw new FormatException();
                                relic.Affixes.Add(new StatLine((Stat)Int(pair[0]), Int(pair[1])));
                            }
                            foreach (string line in Lines(fields[12], PowerCount))
                            {
                                var pair = line.Split('='); if (pair.Length != 2) throw new FormatException();
                                relic.Powers.Add(new PowerLine((Power)Int(pair[0]), Int(pair[1])));
                            }
                            if (!HostGearValidation.TryValidate(relic, out var gear) || (int)gear.Slot != slot)
                                throw new FormatException();
                            input.Profile.Stash.Add(gear);
                            hero.Equipped[slot] = gear.Uid;
                        }
                        break;
                    case "P":
                        var pacts = new HashSet<Pact>();
                        foreach (string entry in Entries(body, PactCount))
                        {
                            var pact = (Pact)Int(entry);
                            if (Game.Pacts.Get(pact) == null || !pacts.Add(pact)) throw new FormatException();
                            input.Pacts.Add(pact);
                        }
                        break;
                    case "Y":
                        input.DailyId = Int(body);
                        if (input.DailyId != 0 && DailyDream.Get(input.DailyId) == null) throw new FormatException();
                        break;
                    default: throw new FormatException();
                }
            }
            if (seen.Count != 8) throw new FormatException();
            return input;
        }

        private static string[] Entries(string body, int max) => Split(body, ',', max);
        private static string[] Lines(string body, int max) => Split(body, '+', max);
        private static string[] Split(string body, char separator, int max)
        {
            if (body.Length == 0) return Array.Empty<string>();
            var entries = body.Split(separator);
            if (entries.Length > max) throw new FormatException();
            return entries;
        }
        private static int Int(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static int Range(string text, int min, int max)
        {
            int value = Int(text);
            if (value < min || value > max) throw new FormatException();
            return value;
        }
        private static void Token(string text)
        {
            if (!Gimmicks.ValidStarId(text)) throw new FormatException();
        }
        private static bool Reject(string code, out string reason) { reason = code; return false; }
    }
}
