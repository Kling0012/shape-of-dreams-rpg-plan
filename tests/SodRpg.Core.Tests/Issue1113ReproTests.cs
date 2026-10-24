using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class Issue1113ReproTests
    {
        private static CodexResult Search(Profile profile, CodexCategory category, string text)
        {
            var state = new CodexState(profile.Codex, CodexQuery.KnownPowers(profile));
            return CodexQuery.Filter(state, new CodexFilter { Category = category, Text = text });
        }

        private static LocExternal Dictionary(string code, string source, string translation)
        {
            string json = JsonSerializer.Serialize(new
            {
                language = code,
                name = code,
                strings = new Dictionary<string, string> { [source] = translation },
            });
            var dictionary = LocExternal.Parse(json, code);
            Assert.NotNull(dictionary);
            return dictionary;
        }

        [Theory]
        [InlineData("initial")]
        [InlineData("switch")]
        [InlineData("reload")]
        public void Displayed_external_base_name_remains_in_search_results(string transition)
        {
            var previousLanguage = Loc.Language;
            var previousExternal = Loc.External;
            try
            {
                var profile = Profile.CreateNew(1);
                var entry = CodexQuery.Entries(CodexCategory.Bases).Single(e => e.Id == "weapon.chain_sword");
                Loc.Language = LocLanguage.English;
                Assert.Contains(Search(profile, CodexCategory.Bases, "Chain Sword").Items, e => e.Id == entry.Id);
                int previousKey = Loc.CacheKey;
                string previousName = null;
                if (transition != "initial")
                {
                    previousName = transition == "switch" ? "Kettenschwert" : "Ancienne épée";
                    Loc.External = Dictionary(transition == "switch" ? "de" : "fr", "Chain Sword", previousName);
                    Loc.Language = LocLanguage.External;
                    Assert.Equal(previousName, entry.Name.ToString());
                    Assert.Contains(Search(profile, CodexCategory.Bases, "Chain Sword").Items, e => e.Id == entry.Id);
                    previousKey = Loc.CacheKey;
                }

                Loc.External = Dictionary("fr", "Chain Sword", "Épée chaînée");
                Loc.Language = LocLanguage.External;
                Assert.NotEqual(previousKey, Loc.CacheKey);
                Assert.False(profile.Codex.Contains(entry.Id));
                Assert.Equal("Épée chaînée", entry.Name.ToString());
                Assert.Contains("Épée chaînée", CodexPresenter.RowText(entry, false));
                foreach (string name in new[] { entry.Name.Ja, entry.Name.En, entry.Name.Zh })
                    Assert.Contains(Search(profile, CodexCategory.Bases, name).Items, e => e.Id == entry.Id);
                if (previousName != null)
                    Assert.DoesNotContain(Search(profile, CodexCategory.Bases, previousName).Items, e => e.Id == entry.Id);

                Assert.Contains(Search(profile, CodexCategory.Bases, entry.Name.ToString()).Items, e => e.Id == entry.Id);
            }
            finally
            {
                Loc.External = previousExternal;
                Loc.Language = previousLanguage;
            }
        }

        [Theory]
        [InlineData(CodexCategory.Uniques)]
        [InlineData(CodexCategory.Sets)]
        [InlineData(CodexCategory.Powers)]
        [InlineData(CodexCategory.Named)]
        [InlineData(CodexCategory.MiniSets)]
        public void Unfound_secrets_do_not_match_external_display_names(CodexCategory category)
        {
            var previousLanguage = Loc.Language;
            var previousExternal = Loc.External;
            try
            {
                var profile = Profile.CreateNew(1);
                var entry = CodexQuery.Entries(category).First();
                Loc.External = Dictionary("fr", entry.Name.En, "Secret traduit 1113");
                Loc.Language = LocLanguage.External;
                var state = new CodexState(profile.Codex, CodexQuery.KnownPowers(profile));
                Assert.False(state.IsFound(entry));
                Assert.Equal("Secret traduit 1113", entry.Name.ToString());
                Assert.DoesNotContain("Secret traduit 1113", CodexPresenter.RowText(entry, false));
                Assert.Empty(Search(profile, category, entry.Name.ToString()).Items);
            }
            finally
            {
                Loc.External = previousExternal;
                Loc.Language = previousLanguage;
            }
        }
    }
}
