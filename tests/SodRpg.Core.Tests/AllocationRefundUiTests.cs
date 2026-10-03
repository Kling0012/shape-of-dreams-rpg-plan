using System;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class AllocationRefundUiTests
    {
        private sealed class Scenario : IDisposable
        {
            internal const string Hero = "Hero_Cetus", EffectId = "test.refund-ui.echo", DependentId = "test.refund-ui.leaf";
            internal readonly Profile Profile = Profile.CreateNew(15131);
            internal readonly Relic Relic;
            internal readonly RefundUiSession Session;
            internal readonly DreamforgeUi Ui;
            internal readonly AllocationValidationException Proposal;
            internal Scenario()
            {
                Profile.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                var effect = new TalentDef(EffectId, Line.Offense, new Txt("試験", "Test"), Stat.Armor, 0, 2) {
                    HeroKey = Hero, RankCost = 3, RouteMemory = "St_D_IcyVeins",
                    Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 5m }
                };
                var dependent = new TalentDef(DependentId, Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, 1) {
                    HeroKey = Hero, RankCost = 2,
                    AuthoredStar = new AuthoredStarDef { RequiredStarIds = new[] { EffectId } }
                };
                Relic = Loot.RollRelic(new Rng(15131), Rarity.Common, 1, slot: Slot.Weapon);
                Profile.Stash.Add(Relic);
                var tree = HeroSigils.TreeFor(Hero).Where(t => t.Cluster == null).Concat(new[] { effect, dependent }).ToArray();
                Rules.RegisterAllocationValidation(Hero, new EffectiveAllocationValidation(tree, new EffectiveAllocationPolicy {
                    PermanentDisables = new[] { new AllocationDisableRule { EquippedUid = Relic.Uid, StarIds = new[] { EffectId } } }
                }));
                Rules.AddTalentRank(Profile, Hero, EffectId);
                Rules.AddTalentRank(Profile, Hero, EffectId);
                Rules.AddTalentRank(Profile, Hero, DependentId);
                Proposal = Assert.Throws<AllocationValidationException>(() => Rules.Equip(Profile, Hero, Relic.Uid));
                Session = new RefundUiSession { Profile = Profile };
                Ui = new DreamforgeUi(Session, Hero);
                Ui.OfferRefund(Proposal, true, Relic.Uid);
            }
            public void Dispose() => Rules.RegisterAllocationValidation(Hero, null);
        }

        [Fact]
        public void Preview_and_cancel_preserve_loadout_and_all_paid_ranks()
        {
            using var s = new Scenario();
            string before = ProfileCodec.Write(s.Profile);
            Assert.Equal(8, s.Proposal.Plan.RefundCost);
            s.Ui.DrawRefund(-1);
            Assert.Equal(before, ProfileCodec.Write(s.Profile));
            s.Ui.DrawRefund(1);
            Assert.False(s.Ui.HasRefund);
            Assert.Equal(before, ProfileCodec.Write(s.Profile));
        }

        [Fact]
        public void Approval_applies_equipment_and_exact_cost_dependent_refunds_together()
        {
            using var s = new Scenario();
            s.Ui.DrawRefund(0);
            Assert.Equal(s.Relic.Uid, s.Profile.Hero(Scenario.Hero).Equipped[(int)Slot.Weapon]);
            Assert.False(s.Profile.Hero(Scenario.Hero).Talents.ContainsKey(Scenario.EffectId));
            Assert.False(s.Profile.Hero(Scenario.Hero).Talents.ContainsKey(Scenario.DependentId));
            Assert.Equal(0, Rules.SpentPoints(s.Profile.Hero(Scenario.Hero), Scenario.Hero));
            Assert.False(s.Ui.HasRefund);
        }

        [Fact]
        public void Stale_plan_cannot_approve_a_different_refund_cost()
        {
            using var s = new Scenario();
            s.Profile.Hero(Scenario.Hero).Talents[Scenario.EffectId] = 1;
            string changed = ProfileCodec.Write(s.Profile);
            s.Ui.DrawRefund(0);
            Assert.Equal(changed, ProfileCodec.Write(s.Profile));
            Assert.False(s.Ui.HasRefund);
        }

        [Fact]
        public void Reserved_relic_blocks_approval_without_mutating_or_dropping_the_plan()
        {
            using var s = new Scenario();
            var pending = s.Session.Trades.Begin(TradeKind.SalvageForDust, 0, 0, 10, s.Relic.Uid);
            string before = ProfileCodec.Write(s.Profile);
            s.Ui.DrawRefund(0);
            Assert.True(s.Ui.HasRefund);
            Assert.Equal(before, ProfileCodec.Write(s.Profile));
            s.Session.Trades.Complete(pending.Token, false);
            s.Ui.DrawRefund(0);
            Assert.Equal(s.Relic.Uid, s.Profile.Hero(Scenario.Hero).Equipped[(int)Slot.Weapon]);
            Assert.False(s.Ui.HasRefund);
        }
    }
}
