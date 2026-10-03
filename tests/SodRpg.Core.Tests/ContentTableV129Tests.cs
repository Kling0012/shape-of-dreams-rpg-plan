using System;
using System.IO;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class ContentTableV129Tests
    {
        private static string[][] Rows() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ReviewedV129.md"))
            .Where(s => s.StartsWith("|"))
            .Select(s => s.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray()).ToArray();

        private static Power Effect(string text)
        {
            if (text.StartsWith("P") && int.TryParse(text.Substring(1), out int number))
                return (Power)((int)Power.ShieldbreakBurst + number - 1);
            return Enum.GetValues(typeof(Power)).Cast<Power>().Single(p => Content.PowerName(p) == text);
        }

        [Fact]
        public void All_available_reviewed_uniques_match_names_lore_powers_and_links()
        {
            bool old = Loc.Japanese;
            Loc.Japanese = true;
            try
            {
                var rows = Rows().Where(r => r.Length == 13 && r[0].StartsWith("unique.")).ToArray();
                Assert.Equal(559, rows.Length);
                Assert.Equal(16, rows.Count(r => r[6] == "P37" || r[8] == "P37"));
                foreach (var r in rows)
                {
                    if (r[6] == "P37" || r[8] == "P37")
                    { Assert.False(Content.TryGetUnique(r[0], out _), r[0]); continue; }
                    Assert.True(Content.TryGetUnique(r[0], out var u), r[0]);
                    Assert.Equal(r[1], u.BaseId);
                    Assert.Equal(r[2], u.Name.Ja); Assert.Equal(r[3], u.Name.En);
                    Assert.Equal(r[4], u.Lore.Ja); Assert.Equal(r[5], u.Lore.En);
                    Assert.Equal(2, u.Powers.Count);
                    for (int i = 0; i < 2; i++)
                    {
                        var power = u.Powers[i];
                        Assert.Equal(Effect(r[6 + i * 2]), power.Power);
                        Assert.Equal(int.Parse(r[7 + i * 2]), power.Value);
                        Assert.InRange(power.Value, 1, Content.PowerCap(power.Power));
                        if (NewPowersV129.IsPower(power.Power))
                            Assert.Contains(Content.PowerPool(Content.GetBase(u.BaseId).Slot), x => x.Power == power.Power);
                    }
                    if (r[10] == "-") Assert.Null(u.Link);
                    else
                    {
                        Assert.True(Links.Validate(u.Link), u.Id);
                        Assert.Equal(r[10].Split('+'), u.Link.Requires);
                        Assert.Equal(r[11], u.Link.Kind.ToString());
                        Assert.Equal(int.Parse(r[12]), u.Link.Value);
                    }
                }
            }
            finally { Loc.Japanese = old; }
        }

        [Fact]
        public void Available_sets_match_reviewed_effects_and_have_exactly_the_reviewed_pieces()
        {
            bool old = Loc.Japanese; Loc.Japanese = true;
            try
            {
                var rows = Rows();
                var sets = rows.Where(r => r.Length == 6 && r[0].StartsWith("set.")).ToArray();
                Assert.Equal(24, sets.Length);
                foreach (var r in sets)
                {
                    if (r[5].Contains("P37 "))
                    { Assert.DoesNotContain(Content.Sets, s => s.Id == r[0]); continue; }
                    var set = Assert.Single(Content.Sets, s => s.Id == r[0]);
                    Assert.Equal(r[1], set.Name.Ja); Assert.Equal(r[2], set.Name.En);
                    var effects = r[5].Split(';').Select(x => x.Trim().Split(' ')).ToArray();
                    Assert.Equal(effects.Length, set.ThreePiece.Length);
                    for (int i = 0; i < effects.Length; i++)
                    {
                        Assert.Equal(Effect(effects[i][0]), set.ThreePiece[i].Power);
                        Assert.Equal(int.Parse(effects[i][1]), set.ThreePiece[i].Value);
                    }
                    var pieces = rows.Where(x => x.Length == 5 && x[1] == r[0]).ToArray();
                    Assert.Equal(3, pieces.Length);
                    foreach (var piece in pieces)
                    {
                        Assert.True(Content.TryGetUnique(piece[0], out var u), piece[0]);
                        Assert.Equal(r[0], u.SetId); Assert.Equal(piece[2], u.BaseId);
                        Assert.Equal(piece[3], u.Name.Ja); Assert.Equal(piece[4], u.Name.En);
                    }
                }
            }
            finally { Loc.Japanese = old; }
        }

        [Fact]
        public void Every_distributed_power_is_implemented_and_every_bond_can_be_satisfied()
        {
            Assert.All(Content.Uniques, u => Assert.DoesNotContain(u.Powers, p => p.Power == Power.UnbowedMind));
            Assert.All(Content.Sets, s => Assert.DoesNotContain(s.ThreePiece, p => p.Power == Power.UnbowedMind));
            var bonds = Content.Uniques.Where(u => u.Link != null && u.Link.Requires.Count(x => x.StartsWith("Hero_")) >= 2).ToArray();
            Assert.Equal(18, bonds.Length);
            foreach (var u in bonds)
            {
                Assert.True(Links.Validate(u.Link), u.Id);
                var travelers = u.Link.Requires.Where(x => x.StartsWith("Hero_")).ToArray();
                Assert.True(Links.Satisfied(u.Link, travelers[0], null, null, travelers.Skip(1).ToArray()), u.Id);
                Assert.False(Links.Satisfied(u.Link, travelers[0], null, null), u.Id);
                Assert.False(Links.Satisfied(u.Link, "Hero_Unrelated", null, null, travelers), u.Id);
            }
        }
    }
}
