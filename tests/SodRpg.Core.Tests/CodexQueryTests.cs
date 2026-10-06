using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class CodexQueryTests
    {
        private static CodexState State(Profile p) => new CodexState(p.Codex, CodexQuery.KnownPowers(p));


        [Fact]
        public void Text_search_never_matches_unfound_secrets()
        {
            var p = Profile.CreateNew(1);
            var u = Content.Uniques.First(x => x.SetId == null);
            var f = new CodexFilter { Category = CodexCategory.Uniques, Text = u.Name.Ja };
            Assert.Empty(CodexQuery.Filter(State(p), f).Items);
            p.Codex.Add(u.Id);
            var r = CodexQuery.Filter(State(p), f);
            Assert.Contains(r.Items, e => e.Id == u.Id);
            f.Text = u.Name.En.ToUpperInvariant();
            Assert.Contains(CodexQuery.Filter(State(p), f).Items, e => e.Id == u.Id);
        }

        [Fact]
        public void Base_names_are_searchable_even_when_unfound()
        {
            var p = Profile.CreateNew(1);
            var b = Content.Bases[5];
            var f = new CodexFilter { Category = CodexCategory.Bases, Text = b.Name.Ja };
            Assert.Contains(CodexQuery.Filter(State(p), f).Items, e => e.Id == b.Id);
        }

        [Fact]
        public void Set_is_found_when_any_piece_is_found()
        {
            var p = Profile.CreateNew(1);
            var set = CodexQuery.Entries(CodexCategory.Sets)[0];
            var st = State(p);
            Assert.False(st.IsFound(set));
            p.Codex.Add(set.Pieces[0].Id);
            Assert.True(State(p).IsFound(set));
            Assert.Contains(set.Set.ThreePiece[0].Power, CodexQuery.KnownPowers(p));
        }

    }
}
