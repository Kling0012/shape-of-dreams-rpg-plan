using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class CodexQueryTests
    {
        private static CodexState State(Profile p) => new CodexState(p.Codex, CodexQuery.KnownPowers(p));

        [Fact]
        public void Categories_cover_all_content()
        {
            Assert.Equal(Content.Bases.Count, CodexQuery.Entries(CodexCategory.Bases).Count);
            Assert.Equal(Content.Uniques.Count, CodexQuery.Entries(CodexCategory.Uniques).Count);
            Assert.Equal(Content.Sets.Count, CodexQuery.Entries(CodexCategory.Sets).Count);
            Assert.NotEmpty(CodexQuery.Entries(CodexCategory.Powers));
            Assert.All(CodexQuery.Entries(CodexCategory.Sets), e => Assert.NotEmpty(e.Pieces));
        }

        [Fact]
        public void Counts_follow_codex()
        {
            var p = Profile.CreateNew(1);
            var u = Content.Uniques.First(x => x.SetId == null);
            p.Codex.Add(u.Id);
            p.Codex.Add(Content.Bases[0].Id);
            var f = new CodexFilter { Category = CodexCategory.Uniques };
            var r = CodexQuery.Filter(State(p), f);
            Assert.Equal(1, r.CategoryFound[(int)CodexCategory.Uniques]);
            Assert.Equal(1, r.CategoryFound[(int)CodexCategory.Bases]);
            Assert.Equal(Content.Uniques.Count, r.CategoryTotal[(int)CodexCategory.Uniques]);
            Assert.Equal(Content.Uniques.Count, r.Items.Count);
            f.Found = CodexFoundFilter.Found;
            r = CodexQuery.Filter(State(p), f, r);
            Assert.Single(r.Items);
            Assert.True(r.ItemFound[0]);
            f.Found = CodexFoundFilter.Unfound;
            r = CodexQuery.Filter(State(p), f, r);
            Assert.Equal(Content.Uniques.Count - 1, r.Items.Count);
        }

        [Fact]
        public void Slot_and_line_filters_apply()
        {
            var p = Profile.CreateNew(1);
            var f = new CodexFilter { Category = CodexCategory.Bases, Slot = Slot.Head, Line = Line.Guard };
            var r = CodexQuery.Filter(State(p), f);
            Assert.NotEmpty(r.Items);
            Assert.All(r.Items, e => { Assert.Equal(Slot.Head, e.Base.Slot); Assert.Equal(Line.Guard, e.Base.Line); });
            Assert.Equal(r.Items.Count, r.ScopeTotal);
        }

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

        [Fact]
        public void Powers_are_found_through_unique_or_owned_relic()
        {
            var p = Profile.CreateNew(1);
            var u = Content.Uniques.First(x => x.Powers.Count > 0);
            var power = u.Powers[0].Power;
            var e = CodexQuery.Entries(CodexCategory.Powers).First(x => x.Power == power);
            Assert.False(State(p).IsFound(e));
            p.Codex.Add(u.Id);
            Assert.True(State(p).IsFound(e));
            Assert.True(CodexQuery.TryPowerRange(power, out int min, out int max));
            Assert.True(max >= min);
        }
    }
}
