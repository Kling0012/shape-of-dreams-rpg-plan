using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.32 A (currency stars: KillGoldPct, EliteKillGoldPct, DreamDustPct, DreamDustDelvePct) and B (RunGrowth).
    /// The example registration below is the exact shape tools/star-manifest/gen_cs.py emits for the documented manifest rows.
    /// </summary>
    public sealed class StarAdditionsV132Tests
    {
        private const string Hero = "Hero_Vesper", Anchor = "outer.v132.s1";
        private const string Growth = "outer.v132.growth", CapA = "outer.v132.cap1", CapB = "outer.v132.cap2", Fork = "outer.v132.fork";

        private static AuthoredStarDef Star(string id, ClusterStarDef effect, params string[] linkedTo) => new AuthoredStarDef
        {
            HeroKey = Hero, LocalStarId = id, ClusterId = "outer.v132", Region = ClusterRegion.Outer, AnchorId = Anchor, Shape = ClusterShape.Fan,
            Edges = linkedTo.Select(x => new AuthoredStarEdge(id, x)).ToArray(), Effect = effect,
            RequiresExplicitSelection = effect.Kind == ClusterStarKind.Choice, SourceDocument = "docs/specs/v1.32-star-additions.md",
            MechanismIds = new[] { "C01" }, Notes = "",
        };

        private static ClusterStarDef PowerStar(string name, Power power, int amount) => new ClusterStarDef
        { Kind = ClusterStarKind.Notable, Name = new Txt(name, name), MaxRank = 1, RankCost = 1, Power = power, Amount = amount };

        private static RunGrowthDef VesperGrowth() => new RunGrowthDef(RunGrowthTrigger.DamageTakenMaxHpPct, 10, 60,
            new[] { new RunGrowthEffect(Stat.Armor, 1000), new RunGrowthEffect(Stat.MaxHealthFlat, 4000) });

        private static List<AuthoredStarDef> Definitions(RunGrowthDef growth = null, int capBonus = 20, int goldAmount = 4)
        {
            var defs = new List<AuthoredStarDef>();
            var all = new List<string>();
            for (int i = 1; i <= 4; i++) all.Add("outer.v132.kg" + i);
            all.Add("outer.v132.elite");
            for (int i = 1; i <= 4; i++) all.Add("outer.v132.dd" + i);
            all.AddRange(new[] { "outer.v132.basket", Growth, CapA, CapB, Fork });
            defs.Add(Star(Anchor, new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("入口", "Entrance"), Stat = Stat.Armor, Amount = 1 }, all.ToArray()));
            for (int i = 1; i <= 4; i++) defs.Add(Star("outer.v132.kg" + i, PowerStar("巡る富" + i, Power.KillGoldPct, goldAmount)));
            defs.Add(Star("outer.v132.elite", PowerStar("黄金の嗅覚", Power.EliteKillGoldPct, 25)));
            for (int i = 1; i <= 4; i++) defs.Add(Star("outer.v132.dd" + i, PowerStar("夢屑の集め" + i, Power.DreamDustPct, 4)));
            defs.Add(Star("outer.v132.basket", PowerStar("夢屑の籠", Power.DreamDustDelvePct, 10)));
            defs.Add(Star(Growth, new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試練の傷", "Scars of Trial"), MaxRank = 1, RankCost = 1,
                RunGrowth = growth ?? VesperGrowth() }));
            foreach (string id in new[] { CapA, CapB })
                defs.Add(Star(id, new ClusterStarDef { Kind = ClusterStarKind.RunGrowthModifier, Name = new Txt("鍛錬の余地", "Room to Train"), MaxRank = 1, RankCost = 1,
                    RunGrowthModifier = new RunGrowthModifierDef(Growth, capBonus, 0, false) }));
            defs.Add(Star(Fork, new ClusterStarDef { Kind = ClusterStarKind.Choice, Name = new Txt("鍛錬の分かれ道", "Training Fork"), MaxRank = 1, RankCost = 1, Options = new[]
            {
                new ClusterStarDef { Kind = ClusterStarKind.RunGrowthModifier, Name = new Txt("深まる傷", "Deepening Scars"), MaxRank = 1, RankCost = 1,
                    RunGrowthModifier = new RunGrowthModifierDef(null, 0, 50, false) },
                new ClusterStarDef { Kind = ClusterStarKind.RunGrowthModifier, Name = new Txt("早い慣れ", "Quick Study"), MaxRank = 1, RankCost = 1,
                    RunGrowthModifier = new RunGrowthModifierDef(Growth, 0, 0, true) },
            } }));
            return defs;
        }

        private static (Profile Profile, IReadOnlyList<TalentDef> Tree) Register(List<AuthoredStarDef> defs = null)
        {
            var tree = StarClusters.RegisterAuthored(Hero, defs ?? Definitions()).TreeFor(Hero);
            var profile = new Profile();
            profile.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            return (profile, tree);
        }

        private static void Allocate(Profile profile, IReadOnlyList<TalentDef> tree, string id, int choice = -1)
        {
            var hero = profile.Hero(Hero);
            AuthoredStarContractTests.AllocatePath(hero, tree, id);
            hero.Talents[id] = 1;
            if (choice >= 0) hero.TalentChoices[id] = choice;
        }

        private static void Restore() => StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>());

        private static Build Compute(Profile profile) => Build.Compute(profile, Hero, 0);

        // ───── A. currency ─────

        [Fact]
        public void Currency_stars_aggregate_and_hit_the_design_caps()
        {
            try
            {
                var (profile, tree) = Register();
                foreach (var id in tree.Where(t => t.RankPower is Power p && CurrencyStars.IsPower(p)).Select(t => t.Id)) Allocate(profile, tree, id);
                var build = Compute(profile);
                // 4 x 4% raw, capped at the design total.
                Assert.Equal(12, build.Get(Power.KillGoldPct));
                Assert.Equal(25, build.Get(Power.EliteKillGoldPct));
                Assert.Equal(12, build.Get(Power.DreamDustPct));
                Assert.Equal(10, build.Get(Power.DreamDustDelvePct));
                Assert.Equal(0.12f, CurrencyStars.KillGoldMultiplierDelta(build));
                Assert.Equal(25, CurrencyStars.EliteKillGoldPercent(build));
                Assert.Equal(22, CurrencyStars.DreamDustPercent(build, delving: false));
                Assert.Equal(32, CurrencyStars.DreamDustPercent(build, delving: true));
                // Survives the wire, and a host that reconstructs from the allocation agrees.
                var decoded = Build.Decode(build.Encode());
                Assert.NotNull(decoded);
                Assert.Equal(build.Encode(), decoded.Encode());
                Assert.Equal(12, decoded.Get(Power.KillGoldPct));
            }
            finally { Restore(); }
        }

        [Fact]
        public void A_partial_selection_sums_below_the_caps()
        {
            try
            {
                var (profile, tree) = Register();
                Allocate(profile, tree, "outer.v132.kg1"); Allocate(profile, tree, "outer.v132.kg2");
                Allocate(profile, tree, "outer.v132.dd1"); Allocate(profile, tree, "outer.v132.basket");
                var build = Compute(profile);
                Assert.Equal(8, build.Get(Power.KillGoldPct));
                Assert.Equal(0, build.Get(Power.EliteKillGoldPct));
                Assert.Equal(4, build.Get(Power.DreamDustPct));
                Assert.Equal(4 + 10, CurrencyStars.DreamDustPercent(build, false));
                Assert.Equal(4 + 10 + 10, CurrencyStars.DreamDustPercent(build, true));
            }
            finally { Restore(); }
        }

        [Fact]
        public void Currency_powers_never_drop_from_gear()
        {
            foreach (Power p in new[] { Power.KillGoldPct, Power.EliteKillGoldPct, Power.DreamDustPct, Power.DreamDustDelvePct })
            {
                Assert.True(CurrencyStars.IsPower(p));
                Assert.False(Content.IsPowerDroppable(p));
                Assert.All(Enum.GetValues(typeof(Slot)).Cast<Slot>(), slot => Assert.DoesNotContain(Content.PowerPool(slot), r => r.Power == p));
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    string text = Content.FormatPower(p, Content.PowerCap(p));
                    Assert.Contains(Content.PowerCap(p).ToString(), text);
                }
            }
        }

        [Fact]
        public void Delve_state_follows_the_run_heat()
        {
            Assert.False(CurrencyStars.IsDelving(null));
            Assert.False(CurrencyStars.IsDelving(new RunState { Heat = 0 }));
            Assert.False(CurrencyStars.IsDelving(new RunState { Heat = 2, StartDepth = 2 }));
            Assert.True(CurrencyStars.IsDelving(new RunState { Heat = 1 }));
        }

        [Fact]
        public void Dream_dust_boosts_only_own_pickups_with_probabilistic_rounding()
        {
            var build = new Build();
            build.Powers[Power.DreamDustPct] = 12; build.Powers[Power.DreamDustDelvePct] = 10;
            // A gift from another player is never boosted; an own pickup is.
            Assert.Equal(0, CurrencyStars.DreamDustBonusForPickup(build, true, 100, isGivenByOtherPlayer: true, roll: 0d));
            Assert.Equal(22, CurrencyStars.DreamDustBonusForPickup(build, false, 100, isGivenByOtherPlayer: false, roll: 0.5d));
            Assert.Equal(32, CurrencyStars.DreamDustBonusForPickup(build, true, 100, isGivenByOtherPlayer: false, roll: 0.5d));
            // 7 * 22% = 1.54: rounds up when the roll is below the fraction, down otherwise (the base game's RandomRoundToInt).
            Assert.Equal(2, CurrencyStars.DreamDustBonusForPickup(build, false, 7, false, 0.53d));
            Assert.Equal(1, CurrencyStars.DreamDustBonusForPickup(build, false, 7, false, 0.55d));
            Assert.Equal(0, CurrencyStars.DreamDustBonusForPickup(new Build(), true, 100, false, 0d));
            Assert.Equal(0, CurrencyStars.DreamDustBonusForPickup(build, false, 0, false, 0d));
        }

        [Fact]
        public void Elite_gold_bonus_follows_the_base_games_per_player_share()
        {
            // 100 kill gold shared by 2 players, profile x1.5, this player's multiplier 1.16: 100/2*1.5*1.16 = 87 gold, 25% of it extra.
            Assert.Equal(87d * 0.25d, CurrencyStars.EliteKillGoldBonus(100, 2, 1.5f, 1.16f, 25), 4);
            Assert.Equal(0d, CurrencyStars.EliteKillGoldBonus(100, 2, 1.5f, 1.16f, 0));
            Assert.Equal(0d, CurrencyStars.EliteKillGoldBonus(0, 2, 1.5f, 1.16f, 25));
            Assert.Equal(0d, CurrencyStars.EliteKillGoldBonus(100, 0, 1.5f, 1.16f, 25));
            Assert.Equal(3, CurrencyStars.RandomRound(2.75d, 0.2d));
            Assert.Equal(2, CurrencyStars.RandomRound(2.75d, 0.8d));
        }

        [Fact]
        public void Multiplier_ledger_restores_the_exact_value_after_many_build_changes()
        {
            var ledger = new AdditiveMultiplierLedger<string>();
            float baseline = 1f;
            float value = baseline;
            float[] deltas = { 0.04f, 0.08f, 0.12f, 0.04f, 0.0f, 0.12f, 0.08f };
            for (int i = 0; i < 20000; i++)
            {
                value = ledger.Apply("p", value, deltas[i % deltas.Length]);   // Build change: re-apply over the previous application
                if (i % 7 == 3) value = ledger.Release("p", value);            // hero re-created / build cleared
            }
            value = ledger.Release("p", value);
            Assert.Equal(baseline, value);   // bit-exact: no drift
            Assert.Equal(0, ledger.Count);
            // Applying a delta is exactly baseline + delta every time, however often it was re-applied.
            float v = 1f;
            for (int i = 0; i < 1000; i++) v = ledger.Apply("p", v, 0.04f);
            Assert.Equal(1f + 0.04f, v);
            Assert.Equal(1f, ledger.Release("p", v));
        }

        [Fact]
        public void Multiplier_ledger_coexists_with_other_effects_that_move_the_same_value()
        {
            var ledger = new AdditiveMultiplierLedger<string>();
            float value = 1f;
            value = ledger.Apply("p", value, 0.12f);
            value += 0.04f;                                  // the base game's "Hoarded Wealth" star changes it meanwhile
            value = ledger.Release("p", value);              // only our difference is taken back
            Assert.Equal(1.04f, value, 5);
            value -= 0.04f;
            Assert.Equal(1f, value, 5);
            // Two players are independent.
            float a = 1f, b = 1.5f;
            a = ledger.Apply("a", a, 0.1f); b = ledger.Apply("b", b, 0.2f);
            b = ledger.Release("b", b);
            Assert.Equal(1.1f, a); Assert.Equal(1.5f, b);
            Assert.Equal(0.1f, ledger.AppliedDelta("a"));
            ledger.Forget("a");
            Assert.Equal(0f, ledger.AppliedDelta("a"));
        }

        // ───── B. RunGrowth ─────

        [Fact]
        public void Run_growth_definitions_are_validated()
        {
            Assert.Throws<ArgumentException>(() => new RunGrowthDef(RunGrowthTrigger.DamageTakenMaxHpPct, 0, 60, new[] { new RunGrowthEffect(Stat.Armor, 1000) }));
            Assert.Throws<ArgumentException>(() => new RunGrowthDef(RunGrowthTrigger.DamageTakenMaxHpPct, 101, 60, new[] { new RunGrowthEffect(Stat.Armor, 1000) }));
            Assert.Throws<ArgumentException>(() => new RunGrowthDef(RunGrowthTrigger.ParrySuccess, 1, 0, new[] { new RunGrowthEffect(Stat.Armor, 1000) }));
            Assert.Throws<ArgumentException>(() => new RunGrowthDef(RunGrowthTrigger.ParrySuccess, 1, 600, new[] { new RunGrowthEffect(Stat.Armor, 1000) }));
            Assert.Throws<ArgumentException>(() => new RunGrowthDef(RunGrowthTrigger.ParrySuccess, 1, 5, new RunGrowthEffect[0]));
            Assert.Throws<ArgumentException>(() => new RunGrowthDef(RunGrowthTrigger.ParrySuccess, 1, 5, new[] { new RunGrowthEffect(Stat.EssenceSlotIdentity, 1000) }));
            Assert.Throws<ArgumentException>(() => new RunGrowthDef(RunGrowthTrigger.ParrySuccess, 1, 5, new[] { new RunGrowthEffect(Stat.Armor, 1000), new RunGrowthEffect(Stat.Armor, 1000) }));
            Assert.Throws<ArgumentException>(() => new RunGrowthDef((RunGrowthTrigger)99, 1, 5, new[] { new RunGrowthEffect(Stat.Armor, 1000) }));
            Assert.Throws<ArgumentException>(() => new RunGrowthModifierDef(null, 0, 0, false));
            Assert.Throws<ArgumentException>(() => new RunGrowthModifierDef("bad id!", 1, 0, false));
            // A star carries exactly one payload.
            var broken = Definitions();
            broken.Find(d => d.LocalStarId == Growth).Effect.Power = Power.Barrier;
            Assert.Throws<InvalidOperationException>(() => StarClusters.RegisterAuthored(Hero, broken));
            Restore();
        }

        [Fact]
        public void The_registered_tree_composes_growth_with_modifiers_into_the_build()
        {
            try
            {
                var (profile, tree) = Register();
                Allocate(profile, tree, Growth);
                var plain = Compute(profile);
                var entry = Assert.Single(plain.RunGrowths);
                Assert.Equal(Growth, entry.StarId);
                Assert.Equal(60, entry.Cap); Assert.Equal(0, entry.EffectPercent); Assert.Equal(1, entry.GainMultiplier);
                Allocate(profile, tree, CapA); Allocate(profile, tree, CapB);
                Allocate(profile, tree, Fork, choice: 0);   // per-stack effect +50%
                var boosted = Assert.Single(Compute(profile).RunGrowths);
                Assert.Equal(100, boosted.Cap);                     // 60 + 20 + 20
                Assert.Equal(50, boosted.EffectPercent);
                Assert.Equal(1, boosted.GainMultiplier);
                Assert.Contains(Fork, boosted.ContributorIds);
                profile.Hero(Hero).TalentChoices[Fork] = 1;         // gain speed x2 instead
                var quick = Assert.Single(Compute(profile).RunGrowths);
                Assert.Equal(0, quick.EffectPercent); Assert.Equal(2, quick.GainMultiplier); Assert.Equal(100, quick.Cap);
                // Without the growth star, modifiers have nothing to modify.
                profile.Hero(Hero).Talents.Remove(Growth);
                Assert.Empty(Compute(profile).RunGrowths);
            }
            finally { Restore(); }
        }

        [Fact]
        public void Run_growth_survives_the_wire_and_changes_the_fingerprint()
        {
            string before = ContentFingerprint.Value;
            try
            {
                var (profile, tree) = Register();
                string registered = ContentFingerprint.Value;
                Assert.NotEqual(before, registered);
                Allocate(profile, tree, Growth); Allocate(profile, tree, CapA); Allocate(profile, tree, Fork, choice: 1);
                var build = Compute(profile);
                string wire = build.Encode();
                Assert.Contains(";w:", wire);
                var decoded = Build.Decode(wire);
                Assert.NotNull(decoded);
                var a = Assert.Single(build.RunGrowths); var b = Assert.Single(decoded.RunGrowths);
                Assert.Equal((a.StarId, a.Trigger, a.Threshold, a.Cap, a.EffectPercent, a.GainMultiplier),
                    (b.StarId, b.Trigger, b.Threshold, b.Cap, b.EffectPercent, b.GainMultiplier));
                Assert.Equal(a.Effects.Select(e => (e.Stat, e.AmountMilli)), b.Effects.Select(e => (e.Stat, e.AmountMilli)));
                Assert.Equal(wire, decoded.Encode());
                // The host's reconstruct-and-compare path accepts the same bytes.
                Assert.Equal(wire, Compute(profile).Encode());
                // A build without growth has no section at all (old wire unchanged).
                Assert.DoesNotContain(";w:", new Build().Encode());
                // Different registered data (here: one more cap per stack) changes the fingerprint.
                var changed = Definitions(new RunGrowthDef(RunGrowthTrigger.DamageTakenMaxHpPct, 10, 61,
                    new[] { new RunGrowthEffect(Stat.Armor, 1000), new RunGrowthEffect(Stat.MaxHealthFlat, 4000) }));
                StarClusters.RegisterAuthored(Hero, changed);
                Assert.NotEqual(registered, ContentFingerprint.Value);
                var otherAmount = Definitions(null, capBonus: 21);
                StarClusters.RegisterAuthored(Hero, otherAmount);
                Assert.NotEqual(registered, ContentFingerprint.Value);
            }
            finally { Restore(); }
            Assert.Equal(before, ContentFingerprint.Value);
        }

        [Fact]
        public void Growth_modifiers_and_capped_currency_preserve_their_caps_while_paid_points_rank_damage()
        {
            try
            {
                var (profile, tree) = Register();
                var hero = profile.Hero(Hero);
                AuthoredStarContractTests.AllocatePath(hero, tree, CapA);
                var beforeModifier = Compute(profile);
                Rules.AddTalentRank(profile, Hero, CapA);
                var withoutGrowth = Compute(profile);
                Assert.Empty(withoutGrowth.RunGrowths);
                Assert.Equal(beforeModifier.SpentStarPoints + 1, withoutGrowth.SpentStarPoints);
                Assert.True(withoutGrowth.Links.Where(l => l.Kind == LinkKind.MemoryDamage).Sum(l => l.Value)
                    > beforeModifier.Links.Where(l => l.Kind == LinkKind.MemoryDamage).Sum(l => l.Value));
                AuthoredStarContractTests.AllocatePath(hero, tree, Growth);
                Rules.AddTalentRank(profile, Hero, Growth);
                Rules.AddTalentRank(profile, Hero, CapB);
                Rules.AddTalentRank(profile, Hero, Fork, choice: 1);
                var entry = Assert.Single(Compute(profile).RunGrowths);
                Assert.Equal(100, entry.Cap); Assert.Equal(2, entry.GainMultiplier);
                // Refunding a modifier lowers the cap again.
                Rules.RemoveTalentRank(profile, Hero, CapB);
                Assert.Equal(80, Assert.Single(Compute(profile).RunGrowths).Cap);
                // Currency remains capped at 12%; another paid point can still improve star damage.
                for (int i = 1; i <= 3; i++) Rules.AddTalentRank(profile, Hero, "outer.v132.kg" + i);
                Assert.Equal(12, Compute(profile).Get(Power.KillGoldPct));
                var beforeCurrency = Compute(profile);
                Rules.AddTalentRank(profile, Hero, "outer.v132.kg4");
                var afterCurrency = Compute(profile);
                Assert.Equal(12, afterCurrency.Get(Power.KillGoldPct));
                Assert.Equal(beforeCurrency.SpentStarPoints + 1, afterCurrency.SpentStarPoints);
                Assert.True(afterCurrency.Links.Where(l => l.Kind == LinkKind.MemoryDamage).Sum(l => l.Value)
                    > beforeCurrency.Links.Where(l => l.Kind == LinkKind.MemoryDamage).Sum(l => l.Value));
                Rules.AddTalentRank(profile, Hero, "outer.v132.elite");
                Rules.AddTalentRank(profile, Hero, "outer.v132.basket");
                Assert.Equal(25, Compute(profile).Get(Power.EliteKillGoldPct));
                Assert.Equal(10, Compute(profile).Get(Power.DreamDustDelvePct));
            }
            finally { Restore(); }
        }

        [Fact]
        public void Host_reconstruction_accepts_the_derived_growth_and_rejects_a_forged_one()
        {
            try
            {
                var (profile, tree) = Register();
                Allocate(profile, tree, Growth); Allocate(profile, tree, CapA); Allocate(profile, tree, "outer.v132.kg1");
                var build = Compute(profile);
                string submission = HostBuildValidation.Encode(build, profile, Hero, 0);
                Assert.True(HostBuildValidation.TryAccept(submission, Hero, out var accepted, out string reason), reason);
                Assert.Equal(build.Encode(), accepted.Encode());
                Assert.Equal(80, Assert.Single(accepted.RunGrowths).Cap);
                Assert.Equal(4, accepted.Get(Power.KillGoldPct));
                // A client cannot claim a larger cap, a faster gain or a growth it has not unlocked.
                Assert.Contains(":80:0:1:", submission);
                Assert.False(HostBuildValidation.TryAccept(submission.Replace(":80:0:1:", ":500:0:1:"), Hero, out _, out _));
                Assert.False(HostBuildValidation.TryAccept(submission.Replace(":80:0:1:", ":80:0:2:"), Hero, out _, out _));
                Assert.False(HostBuildValidation.TryAccept(submission.Replace(":80:0:1:", ":80:100:1:"), Hero, out _, out _));
                profile.Hero(Hero).Talents.Remove(Growth); profile.Hero(Hero).Talents.Remove(CapA);
                string without = HostBuildValidation.Encode(Compute(profile), profile, Hero, 0);
                Assert.True(HostBuildValidation.TryAccept(without, Hero, out var bare, out reason), reason);
                Assert.Empty(bare.RunGrowths);
                Assert.False(HostBuildValidation.TryAccept(
                    submission.Substring(0, submission.IndexOf('|')) + "|" + without.Substring(without.IndexOf('|') + 1), Hero, out _, out _));
            }
            finally { Restore(); }
        }

        [Fact]
        public void Malformed_growth_sections_are_rejected_atomically()
        {
            string ok = "s:;p:;h:0;d:1;a:0;l:;g:;c:;w:";
            Assert.NotNull(Build.Decode("s:;p:;h:0;d:1;a:0;l:;g:;c:"));
            foreach (string bad in new[]
            {
                ok + "x:1:10:60:0:1",                          // wrong field count
                ok + "outer.v132.growth:9:10:60:0:1:7=1000",   // unknown trigger
                ok + "outer.v132.growth:1:0:60:0:1:7=1000",    // threshold out of range
                ok + "outer.v132.growth:1:10:900:0:1:7=1000",  // cap out of range
                ok + "outer.v132.growth:1:10:60:0:3:7=1000",   // gain multiplier out of range
                ok + "outer.v132.growth:1:10:60:0:1:99=1000",  // unknown stat
                ok + "outer.v132.growth:1:10:60:0:1:18=1000",  // unsupported stat (essence slot)
                ok + "outer.v132.growth:1:10:60:0:1:7=0",      // zero amount
                ok + "outer.v132.growth:1:10:60:0:1:7=1000,outer.v132.growth:1:10:60:0:1:7=1000", // duplicate
            })
                Assert.Null(Build.Decode(bad));
            Assert.NotNull(Build.Decode(ok + "outer.v132.growth:1:10:60:0:1:7=1000"));
        }

        [Fact]
        public void Acquired_effects_list_the_growth_with_its_modifiers()
        {
            try
            {
                var (profile, tree) = Register();
                Allocate(profile, tree, Growth); Allocate(profile, tree, CapA);
                var summary = StarSummary.Compute(profile, Hero);
                var line = Assert.Single(summary.RunGrowths);
                Assert.NotNull(line.Growth);
                Assert.Contains(Growth, line.StarIds); Assert.Contains(CapA, line.StarIds);
                Assert.False(summary.IsEmpty);
                Loc.Japanese = true;
                string live = RunGrowth.SummaryLine(line.Growth, 12);
                Assert.Contains("12/80", live);
                Assert.Contains("防御 +12", live);
                Assert.Contains("最大HP +48", live);
                Assert.Contains("鍛錬 12/80", RunGrowth.HudLine(line.Growth, 12));
                Assert.DoesNotContain("(", RunGrowth.HudLine(line.Growth, 0));   // nothing gained yet: no breakdown
                Loc.Japanese = false;
                Assert.Contains("Training 12/80", RunGrowth.HudLine(line.Growth, 12));
                Loc.Japanese = true;
            }
            finally { Restore(); Loc.Japanese = true; }
        }

        private static RunGrowthEntry Entry(RunGrowthTrigger trigger, int threshold, int cap, int effectPercent = 0, int gain = 1, params RunGrowthEffect[] effects)
            => new RunGrowthEntry
            {
                StarId = "g." + trigger, Trigger = trigger, Threshold = threshold, Cap = cap, EffectPercent = effectPercent, GainMultiplier = gain,
                Effects = effects.Length > 0 ? effects : new[] { new RunGrowthEffect(Stat.Armor, 1000) },
            };

        [Fact]
        public void Health_triggers_stack_each_time_the_threshold_is_reached()
        {
            var ledger = new RunGrowthLedger();
            Assert.True(ledger.EnsureRun("run-1"));
            var damage = Entry(RunGrowthTrigger.DamageTakenMaxHpPct, 10, 60);
            // 3 hits of 4% of max HP: 12% crosses 10% once and leaves 2% progress.
            Assert.Equal(0, ledger.Gain("p1", damage, RunGrowthTrigger.DamageTakenMaxHpPct, RunGrowth.HealthPercent(40f, 1000f)));
            Assert.Equal(0, ledger.Gain("p1", damage, RunGrowthTrigger.DamageTakenMaxHpPct, RunGrowth.HealthPercent(40f, 1000f)));
            Assert.Equal(1, ledger.Gain("p1", damage, RunGrowthTrigger.DamageTakenMaxHpPct, RunGrowth.HealthPercent(40f, 1000f)));
            Assert.Equal(1, ledger.Stacks("p1", damage.StarId));
            Assert.Equal(2000, ledger.ProgressMilli("p1", damage.StarId));
            // One huge hit (35%) pays out several stacks at once.
            Assert.Equal(3, ledger.Gain("p1", damage, RunGrowthTrigger.DamageTakenMaxHpPct, 35d));
            Assert.Equal(4, ledger.Stacks("p1", damage.StarId));
            // A different trigger or another player does not touch this growth.
            Assert.Equal(0, ledger.Gain("p1", damage, RunGrowthTrigger.ShieldAbsorbedMaxHpPct, 50d));
            Assert.Equal(0, ledger.Stacks("p2", damage.StarId));
            // Shield absorption uses the same arithmetic with its own entry.
            var shield = Entry(RunGrowthTrigger.ShieldAbsorbedMaxHpPct, 10, 60);
            Assert.Equal(2, ledger.Gain("p1", shield, RunGrowthTrigger.ShieldAbsorbedMaxHpPct, 20d));
            Assert.Equal(0, ledger.Gain("p1", shield, RunGrowthTrigger.ShieldAbsorbedMaxHpPct, 0d));
            Assert.Equal(0, ledger.Gain("p1", shield, RunGrowthTrigger.ShieldAbsorbedMaxHpPct, double.NaN));
            Assert.Equal(0d, RunGrowth.HealthPercent(10f, 0f));
            Assert.Equal(0d, RunGrowth.HealthPercent(-5f, 100f));
        }

        [Fact]
        public void Event_triggers_count_events_per_stack_and_double_gain_doubles_the_stacks()
        {
            var ledger = new RunGrowthLedger();
            ledger.EnsureRun("run-1");
            var parry = Entry(RunGrowthTrigger.ParrySuccess, 1, 80, effects: new[] { new RunGrowthEffect(Stat.AttackFlat, 1000), new RunGrowthEffect(Stat.CritDamagePct, 300) });
            for (int i = 1; i <= 5; i++) Assert.Equal(1, ledger.Gain("mist", parry, RunGrowthTrigger.ParrySuccess, 1));
            Assert.Equal(5, ledger.Stacks("mist", parry.StarId));
            var kill = Entry(RunGrowthTrigger.CritBasicAttackKill, 3, 80, effects: new[] { new RunGrowthEffect(Stat.AttackFlat, 500) });
            Assert.Equal(0, ledger.Gain("husk", kill, RunGrowthTrigger.CritBasicAttackKill, 1));
            Assert.Equal(0, ledger.Gain("husk", kill, RunGrowthTrigger.CritBasicAttackKill, 1));
            Assert.Equal(1, ledger.Gain("husk", kill, RunGrowthTrigger.CritBasicAttackKill, 1));
            var fast = Entry(RunGrowthTrigger.CritBasicAttackKill, 1, 80, gain: 2, effects: new RunGrowthEffect(Stat.AttackFlat, 500));
            Assert.Equal(2, ledger.Gain("husk2", fast, RunGrowthTrigger.CritBasicAttackKill, 1));
            Assert.Equal(2, ledger.Stacks("husk2", fast.StarId));
        }

        [Fact]
        public void Stacks_stop_at_the_cap_and_a_raised_cap_continues_from_there()
        {
            var ledger = new RunGrowthLedger();
            ledger.EnsureRun("run-1");
            var small = Entry(RunGrowthTrigger.ParrySuccess, 1, 3, gain: 2);
            Assert.Equal(2, ledger.Gain("p", small, RunGrowthTrigger.ParrySuccess, 1));
            Assert.Equal(1, ledger.Gain("p", small, RunGrowthTrigger.ParrySuccess, 1));   // 2 + 2 clamped to the cap of 3
            Assert.Equal(0, ledger.Gain("p", small, RunGrowthTrigger.ParrySuccess, 5));
            Assert.Equal(3, ledger.Stacks("p", small.StarId));
            Assert.Equal(0, ledger.ProgressMilli("p", small.StarId));                     // no banked progress at the cap
            var raised = small.Copy(); raised.Cap = 20;                                   // a cap +N star was taken later
            Assert.Equal(2, ledger.Gain("p", raised, RunGrowthTrigger.ParrySuccess, 1));
            Assert.Equal(5, ledger.Stacks("p", small.StarId));
            // Stats only ever count stacks up to the (possibly lowered) cap.
            var lowered = small.Copy(); lowered.Cap = 4;
            Assert.Equal(4d, RunGrowth.StatTotal(lowered, lowered.Effects[0], ledger.Stacks("p", small.StarId)));
        }

        [Fact]
        public void Per_stack_effects_support_fractions_and_the_effect_modifier()
        {
            var shield = Entry(RunGrowthTrigger.ShieldAbsorbedMaxHpPct, 10, 60, effects: new RunGrowthEffect(Stat.ShieldPower, 500));
            Assert.Equal(15d, RunGrowth.StatTotal(shield, shield.Effects[0], 30), 6);       // 30 x 0.5%
            shield.EffectPercent = 50;
            Assert.Equal(22.5d, RunGrowth.StatTotal(shield, shield.Effects[0], 30), 6);     // +50% per stack
            Assert.Equal(45d, RunGrowth.StatTotal(shield, shield.Effects[0], 60), 6);
            Assert.Equal(45d, RunGrowth.StatTotal(shield, shield.Effects[0], 600), 6);      // never beyond the cap
            Assert.Equal(0d, RunGrowth.StatTotal(shield, shield.Effects[0], -3), 6);
            Assert.Equal("0.5", (500 / 1000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(0.003f, StatUnits.ToGame(Stat.CritDamagePct, 0.3d), 6);            // 0.3% crit damage in game units
            Assert.Equal(0.5f, StatUnits.ToGame(Stat.AttackFlat, 0.5d), 6);
        }

        [Fact]
        public void Stacks_live_outside_any_hero_runtime_and_survive_its_disposal_and_a_resume()
        {
            var ledger = new RunGrowthLedger();
            ledger.EnsureRun("run-1");
            var entry = Entry(RunGrowthTrigger.DamageTakenMaxHpPct, 10, 60);
            ledger.Gain("player-7", entry, RunGrowthTrigger.DamageTakenMaxHpPct, 45d);
            Assert.Equal(4, ledger.Stacks("player-7", entry.StarId));
            // A hero is disposed and re-created: the Build is composed again, a new PowerRuntime is made; the ledger is not involved.
            var first = new PowerRuntime(new Build(), 0);
            first = null;
            var second = new PowerRuntime(new Build(), 10);
            Assert.NotNull(second);
            Assert.Equal(4, ledger.Stacks("player-7", entry.StarId));
            Assert.False(ledger.EnsureRun("run-1"));                       // same expedition: nothing resets
            Assert.Equal(4, ledger.Stacks("player-7", entry.StarId));
            Assert.Equal(5000, ledger.ProgressMilli("player-7", entry.StarId));
            // Co-op resume: the stacks are part of the saved run state and come back exactly.
            var state = new RunRecoveryState();
            ledger.Capture(state);
            Assert.Equal("run-1", state.GrowthRunId);
            var resumed = new RunGrowthLedger();
            resumed.Restore(state.Clone());
            Assert.Equal("run-1", resumed.RunId);
            Assert.Equal(4, resumed.Stacks("player-7", entry.StarId));
            Assert.Equal(5000, resumed.ProgressMilli("player-7", entry.StarId));
            Assert.False(resumed.EnsureRun("run-1"));
            Assert.Equal(4, resumed.Stacks("player-7", entry.StarId));
        }

        [Fact]
        public void A_new_run_starts_every_player_at_zero()
        {
            var ledger = new RunGrowthLedger();
            ledger.EnsureRun("run-1");
            var entry = Entry(RunGrowthTrigger.ParrySuccess, 1, 80);
            ledger.Gain("a", entry, RunGrowthTrigger.ParrySuccess, 1);
            ledger.Gain("b", entry, RunGrowthTrigger.ParrySuccess, 1);
            int version = ledger.Version;
            Assert.True(ledger.EnsureRun("run-2"));
            Assert.True(ledger.Version > version);
            Assert.Equal(0, ledger.Stacks("a", entry.StarId)); Assert.Equal(0, ledger.Stacks("b", entry.StarId));
            Assert.Empty(ledger.Export());
            Assert.Equal(1, ledger.Gain("a", entry, RunGrowthTrigger.ParrySuccess, 1));
            Assert.False(ledger.EnsureRun(null));                           // an unknown run id never resets
            Assert.False(ledger.EnsureRun(""));
            Assert.Equal(1, ledger.Stacks("a", entry.StarId));
            ledger.Reset();
            Assert.Equal(0, ledger.Stacks("a", entry.StarId));
        }

        [Fact]
        public void Saved_run_state_round_trips_through_the_profile_codec_and_ignores_garbage()
        {
            var profile = Profile.CreateNew(77);
            var ledger = new RunGrowthLedger();
            ledger.EnsureRun("run-9");
            var entry = Entry(RunGrowthTrigger.CritBasicAttackKill, 2, 80);
            ledger.Gain("12", entry, RunGrowthTrigger.CritBasicAttackKill, 1);
            ledger.Gain("12", entry, RunGrowthTrigger.CritBasicAttackKill, 3);
            ledger.Gain("13", entry, RunGrowthTrigger.CritBasicAttackKill, 1);
            var state = new RunRecoveryState { RunId = "run-9" };
            ledger.Capture(state);
            profile.RunRecovery = state;
            string json = ProfileCodec.Write(profile);
            var read = ProfileCodec.Read(json, new List<string>());
            Assert.NotNull(read.RunRecovery);
            Assert.Equal("run-9", read.RunRecovery.GrowthRunId);
            Assert.Equal(2, read.RunRecovery.Growth.Count);
            var restored = new RunGrowthLedger();
            restored.Restore(read.RunRecovery);
            Assert.Equal(2, restored.Stacks("12", entry.StarId)); Assert.Equal(0, restored.Stacks("13", entry.StarId));
            Assert.Equal(1000, restored.ProgressMilli("13", entry.StarId));
            // Out-of-range or malformed saved entries are dropped, not trusted.
            var corrupt = new RunRecoveryState { GrowthRunId = "run-9" };
            corrupt.Growth.Add(new RunGrowthSave { Owner = "1", GrowthId = "bad id!", Stacks = 3 });
            corrupt.Growth.Add(new RunGrowthSave { Owner = "1", GrowthId = "g.ok", Stacks = 100000 });
            corrupt.Growth.Add(new RunGrowthSave { Owner = null, GrowthId = "g.ok", Stacks = 3 });
            corrupt.Growth.Add(new RunGrowthSave { Owner = "1", GrowthId = "g.ok", Stacks = 7 });
            var guarded = new RunGrowthLedger(); guarded.Restore(corrupt);
            Assert.Equal(7, guarded.Stacks("1", "g.ok"));
            Assert.DoesNotContain(guarded.Export(), x => x.GrowthId != "g.ok");
            var empty = new RunGrowthLedger(); empty.Restore(null);
            Assert.Null(empty.RunId);
        }
    }
}
