using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    [CollectionDefinition("SixPieceSets", DisableParallelization = true)]
    public class SixPieceSetsCollection { }

    /// <summary>
    /// v1.31：6つ装着の効果（エンジン）。実データの set.gale（6部位）の SixPiece を
    /// 既知の値に一時的に差し替えて検証する（静的な Content を書き換えるため、他のテストと並列に動かさない）。
    /// </summary>
    [Collection("SixPieceSets")]
    public class SixPieceSetTests
    {
        private const string Hero = "Hero_A";
        private const string SetId = "set.gale";
        private static readonly PowerLine[] Six = { new PowerLine(Power.Breakout, 3) };

        private sealed class Patch : IDisposable
        {
            private readonly SetDef _set = Content.GetSet(SetId);
            private readonly PowerLine[] _old;

            public Patch()
            {
                _old = _set.SixPiece;
                _set.SixPiece = Six;
            }

            public void Dispose()
            {
                _set.SixPiece = _old;
            }
        }

        private static readonly string[] PieceIds =
        {
            "set.gale.head", "set.gale.hands", "set.gale.feet", "set.gale.weapon", "set.gale.armor", "set.gale.charm",
        };

        private static Relic Roll(string uniqueId, ulong seed)
        {
            Content.TryGetUnique(uniqueId, out var u);
            return Loot.RollUnique(new Rng(seed), u, 5);
        }

        private static Profile Equipped(int pieces, int duplicates = 0)
        {
            var p = Profile.CreateNew(22);
            ulong seed = 100;
            for (int i = 0; i < pieces; i++)
            {
                var r = Roll(PieceIds[i], seed++);
                p.Stash.Add(r);
                Rules.Equip(p, Hero, r.Uid);
            }
            for (int i = 0; i < duplicates; i++) // 同じ部位の2個目。枠が埋まっているので入れ替わるだけ
            {
                var r = Roll(PieceIds[0], seed++);
                p.Stash.Add(r);
                Rules.Equip(p, Hero, r.Uid);
            }
            return p;
        }

        [Fact]
        public void Five_pieces_give_only_two_and_three_piece_bonuses_and_six_add_the_new_power()
        {
            using (new Patch())
            {
                var set = Content.GetSet(SetId);
                var five = Build.Compute(Equipped(5), Hero, 0);
                Assert.Equal(5, five.Sets[SetId]);
                foreach (var pw in set.ThreePiece) Assert.True(five.Get(pw.Power) >= pw.Value);
                int breakoutAt5 = five.Get(Power.Breakout);

                var six = Build.Compute(Equipped(6), Hero, 0);
                Assert.Equal(6, six.Sets[SetId]);
                Assert.Equal(breakoutAt5 + Six[0].Value, six.Get(Power.Breakout));
                Assert.Equal(five.Get(Power.UnbowedMind), six.Get(Power.UnbowedMind));
            }
        }

        [Fact]
        public void Six_piece_power_goes_through_the_same_aggregate_cap_as_other_sources()
        {
            using (new Patch())
            {
                Content.GetSet(SetId).SixPiece = new[] { new PowerLine(Power.Breakout, 1000) };
                var b = Build.Compute(Equipped(6), Hero, 0);
                Assert.True(b.Get(Power.Breakout) < 1000, "capped by PowerCaps aggregation");
            }
        }

        [Fact]
        public void Without_six_piece_data_six_pieces_add_nothing_extra()
        {
            using (new Patch())
            {
                var with = Build.Compute(Equipped(6), Hero, 0);
                Content.GetSet(SetId).SixPiece = null;
                var without = Build.Compute(Equipped(6), Hero, 0);
                Assert.Equal(with.Get(Power.Breakout) - Six[0].Value, without.Get(Power.Breakout));
            }
        }

        [Fact]
        public void Duplicate_copies_of_one_piece_do_not_count_twice()
        {
            using (new Patch())
            {
                var b = Build.Compute(Equipped(5, duplicates: 1), Hero, 0);
                Assert.Equal(5, b.Sets[SetId]);
                Assert.Equal(Build.Compute(Equipped(5), Hero, 0).Get(Power.Breakout), b.Get(Power.Breakout));
            }
        }

        [Fact]
        public void Host_reconstruction_matches_the_client_with_six_pieces()
        {
            using (new Patch())
            {
                var p = Equipped(6);
                var client = Build.Compute(p, Hero, 0);
                string packet = HostBuildValidation.Encode(client, p, Hero, 0);
                Assert.True(HostBuildValidation.TryAccept(packet, Hero, out var accepted, out var reason), reason);
                Assert.Equal(client.Encode(), accepted.Encode());
                Assert.Equal(client.Get(Power.Breakout), accepted.Get(Power.Breakout));
            }
        }

        [Fact]
        public void Missing_piece_weighting_does_not_depend_on_the_set_size()
        {
            using (new Patch())
            {
                var owned = new List<Relic> { Roll("set.gale.head", 7) };
                int Hits(IReadOnlyList<Relic> list)
                {
                    int hits = 0;
                    for (ulong s = 0; s < 200; s++)
                        if (Loot.RollRelic(new Rng(1000 + s), Rarity.Legendary, 5, Slot.Hands, null, list, null).UniqueId == "set.gale.hands") hits++;
                    return hits;
                }
                int withSet = Hits(owned), without = Hits(null);
                Assert.True(withSet > without * 5 && withSet > 60, $"missing piece boost: {withSet} vs {without}");
            }
        }

    }
}
