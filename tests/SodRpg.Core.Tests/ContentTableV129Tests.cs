using System;
using System.IO;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class ContentTableV129Tests
    {
        private static ulong NumericIdentity(System.Collections.Generic.List<string> records)
        {
            ulong hash = 1469598103934665603UL;
            foreach (var record in records.OrderBy(x => x, StringComparer.Ordinal))
            {
                foreach (char value in record)
                    hash = unchecked((hash ^ value) * 1099511628211UL);
                hash = unchecked((hash ^ 10UL) * 1099511628211UL);
            }
            return hash;
        }

        [Fact]
        public void Complete_equipment_numeric_semantics_match_canonical_inputs()
        {
            using var bases = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "EquipmentBases.json")));
            using var uniques = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "EquipmentUniques.json")));
            var expected = new System.Collections.Generic.List<string>();
            var actual = new System.Collections.Generic.List<string>();
            var baseRows = bases.RootElement.GetProperty("bases");
            var uniqueRows = uniques.RootElement.GetProperty("uniques");
            Assert.Equal(Content.Bases.Count, baseRows.EnumerateObject().Count());
            Assert.Equal(Content.Uniques.Count, uniqueRows.EnumerateObject().Count());
            foreach (var row in baseRows.EnumerateObject())
            {
                var item = Content.GetBase(row.Name);
                expected.Add($"base:{row.Name}:{row.Value.GetProperty("stat").GetString()}:int:{row.Value.GetProperty("implicitValue").GetInt32()}");
                actual.Add($"base:{item.Id}:{item.ImplicitStat}:int:{item.ImplicitValue}");
            }
            foreach (var row in uniqueRows.EnumerateObject())
            {
                Assert.True(Content.TryGetUnique(row.Name, out var item), row.Name);
                var powers = row.Value.GetProperty("powers").EnumerateArray().ToArray();
                Assert.Equal(powers.Length, item.Powers.Count);
                for (int index = 0; index < powers.Length; index++)
                {
                    expected.Add($"unique:{row.Name}:power:{index}:{powers[index].GetProperty("power").GetString()}:int:{powers[index].GetProperty("value").GetInt32()}");
                    actual.Add($"unique:{item.Id}:power:{index}:{item.Powers[index].Power}:int:{item.Powers[index].Value}");
                }
                var link = row.Value.GetProperty("link");
                if (link.ValueKind == System.Text.Json.JsonValueKind.Null)
                    Assert.Null(item.Link);
                else
                {
                    Assert.NotNull(item.Link);
                    expected.Add($"unique:{row.Name}:link:{link.GetProperty("kind").GetString()}:milli:{decimal.ToInt32(link.GetProperty("value").GetDecimal() * 1000m)}");
                    actual.Add($"unique:{item.Id}:link:{item.Link.Kind}:milli:{item.Link.ValueMilli}");
                }
            }
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(NumericIdentity(expected), NumericIdentity(actual));
        }

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
                    Assert.True(Content.TryGetUnique(r[0], out var u), r[0]);
                    Assert.Equal(r[1], u.BaseId);
                    Assert.Equal(r[2], u.Name.Ja); Assert.Equal(r[3], u.Name.En);
                    Assert.Equal(r[4], u.Lore.Ja); Assert.Equal(r[5], u.Lore.En);
                    Assert.Equal(2, u.Powers.Count);
                    for (int i = 0; i < 2; i++)
                    {
                        var power = u.Powers[i];
                        Assert.Equal(Effect(r[6 + i * 2]), power.Power);
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
                    }
                }
            }
            finally { Loc.Japanese = old; }
        }

        [Fact]
        public void Available_sets_match_canonical_effects_and_have_exactly_the_reviewed_pieces()
        {
            bool old = Loc.Japanese; Loc.Japanese = true;
            try
            {
                var rows = Rows();
                using var canonical = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "EquipmentSets.json")));
                var definitions = canonical.RootElement.GetProperty("sets").EnumerateArray()
                    .ToDictionary(x => x.GetProperty("id").GetString());
                var sets = rows.Where(r => r.Length == 6 && r[0].StartsWith("set.")).ToArray();
                Assert.Equal(24, sets.Length);
                foreach (var r in sets)
                {
                    var set = Assert.Single(Content.Sets, s => s.Id == r[0]);
                    var definition = definitions[r[0]];
                    foreach (var stage in new[] { "twoPiece", "threePiece", "sixPiece" })
                    {
                        var effects = definition.GetProperty(stage).EnumerateArray().ToArray();
                        if (stage == "twoPiece")
                        {
                            Assert.Equal(effects.Length, set.TwoPiece.Length);
                            for (int i = 0; i < effects.Length; i++)
                            {
                                Assert.Equal(Enum.Parse<Stat>(effects[i].GetProperty("stat").GetString()), set.TwoPiece[i].Stat);
                                Assert.Equal(effects[i].GetProperty("value").GetInt32(), set.TwoPiece[i].Value);
                            }
                        }
                        else
                        {
                            var actual = stage == "threePiece" ? set.ThreePiece : set.SixPiece;
                            Assert.Equal(effects.Length, actual.Length);
                            for (int i = 0; i < effects.Length; i++)
                            {
                                Assert.Equal(Enum.Parse<Power>(effects[i].GetProperty("power").GetString()), actual[i].Power);
                                Assert.Equal(effects[i].GetProperty("value").GetInt32(), actual[i].Value);
                            }
                        }
                    }
                    var pieces = rows.Where(x => x.Length == 5 && x[1] == r[0]).ToArray();
                    Assert.Equal(3, pieces.Length);
                    foreach (var piece in pieces)
                    {
                        Assert.True(Content.TryGetUnique(piece[0], out var u), piece[0]);
                        Assert.Equal(r[0], u.SetId); Assert.Equal(piece[2], u.BaseId);
                    }
                }
            }
            finally { Loc.Japanese = old; }
        }

        [Fact]
        public void Every_distributed_power_is_implemented_and_every_bond_can_be_satisfied()
        {
            Assert.Equal(16, Content.Uniques.Count(u => u.Powers.Any(p => p.Power == Power.UnbowedMind)));
            Assert.Single(Content.Sets, s => s.ThreePiece.Any(p => p.Power == Power.UnbowedMind));
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
