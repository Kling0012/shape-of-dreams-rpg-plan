using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class RunContentV129Tests
    {
        [Fact]
        public void Run_content_ids_and_bilingual_texts_are_complete_and_distinct()
        {
            var events = ((DreamEvent[])Enum.GetValues(typeof(DreamEvent))).Where(e => e != DreamEvent.None).ToArray();
            var kinds = (BountyKind[])Enum.GetValues(typeof(BountyKind));
            Assert.Equal(26, events.Length + 1);
            Assert.Equal(44, kinds.Length);
            Assert.Equal(Enumerable.Range(1, 25), events.Select(e => (int)e));
            Assert.Equal(Enumerable.Range(0, 44), kinds.Select(k => (int)k));
            var p = EventProfile(DreamEvent.Merchant, 11);
            bool language = Loc.Japanese;
            try
            {
                foreach (bool japanese in new[] { true, false })
                {
                    Loc.Japanese = japanese;
                    var names = events.Select(DreamEvents.Name).ToArray();
                    var descriptions = events.Select(e => DreamEvents.Describe(e, p)).ToArray();
                    var objectives = kinds.Select(k => new Bounty { Kind = k, Target = 3 }.Describe()).ToArray();
                    Assert.All(names.Concat(descriptions).Concat(objectives), text => Assert.False(string.IsNullOrWhiteSpace(text)));
                    Assert.Equal(names.Length, names.Distinct().Count());
                    Assert.Equal(descriptions.Length, descriptions.Distinct().Count());
                    Assert.Equal(objectives.Length, objectives.Distinct().Count());
                    Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(DreamEvents.ActionLabel(e))));
                }
            }
            finally { Loc.Japanese = language; }
        }

        [Fact]
        public void Weighted_event_offers_cover_every_usable_trade_and_exclude_unaffordable_trades()
        {
            var p = EventProfile(DreamEvent.Merchant, 5);
            var rng = new Rng(773);
            var seen = new HashSet<DreamEvent>();
            for (int i = 0; i < 5000; i++)
            {
                var e = DreamEvents.Roll(rng, p);
                if (e == DreamEvent.None) continue;
                p.Run.OfferedEvent = e;
                Assert.True(DreamEvents.CanUse(p, e, true, out string reason), reason);
                seen.Add(e);
            }
            Assert.Equal(25, seen.Count);
            p.Run.SatchelShards = 0;
            for (int i = 0; i < 500; i++)
            {
                var e = DreamEvents.Roll(rng, p);
                Assert.DoesNotContain(e, new[] { DreamEvent.ShadowExchange, DreamEvent.LostMausoleum, DreamEvent.StoneBroker, DreamEvent.AbyssalChest, DreamEvent.SealedVault });
            }
        }

        public static IEnumerable<object[]> EventCases()
        {
            foreach (DreamEvent e in Enum.GetValues(typeof(DreamEvent)))
                if (e != DreamEvent.None)
                    foreach (ulong seed in new ulong[] { 7, 29, 103 })
                        yield return new object[] { e, seed };
        }

        [Theory]
        [MemberData(nameof(EventCases))]
        public void Event_resolution_is_once_only_and_identical_after_pending_save(DreamEvent e, ulong seed)
        {
            var p = EventProfile(e, seed);
            Assert.True(DreamEvents.CanUse(p, e, out string reason), reason);
            string pending = ProfileCodec.Write(p);
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(pending, notes);
            Assert.Empty(notes);
            Assert.Equal(e, loaded.Run.OfferedEvent);
            var first = Rules.UseEvent(p, e);
            var resumed = Rules.UseEvent(loaded, e);
            Assert.Equal(first.Select(x => (x.Kind, x.Text, x.Rarity)), resumed.Select(x => (x.Kind, x.Text, x.Rarity)));
            Assert.Equal(ProfileCodec.Write(p), ProfileCodec.Write(loaded));
            Assert.Equal(DreamEvent.None, loaded.Run.OfferedEvent);
            Assert.Equal(1, loaded.Stats.EventsUsed);
            string resolved = ProfileCodec.Write(loaded);
            Assert.Equal(resolved, ProfileCodec.Write(ProfileCodec.Read(resolved, new List<string>())));
            Assert.Throws<InvalidOperationException>(() => Rules.UseEvent(loaded, e));
            Assert.Equal(resolved, ProfileCodec.Write(loaded));
        }

        [Fact]
        public void Every_bounty_is_offerable_and_partial_progress_survives_save()
        {
            var p = Profile.CreateNew(17);
            Rules.BeginRun(p, "bounty-save");
            p.Run.Bounties.Clear();
            p.Run.Bounties.AddRange(Bounties.Roll(new Rng(43), 44));
            Assert.Equal(44, p.Run.Bounties.Count);
            Assert.Equal(44, p.Run.Bounties.Select(b => b.Kind).Distinct().Count());
            foreach (var b in p.Run.Bounties)
            {
                Assert.InRange(b.Target, 1, 100000);
                Assert.True(b.RewardShards > 0);
                Assert.True(b.RewardXp > 0);
                b.Progress = Math.Max(0, b.Target - 1);
            }
            string saved = ProfileCodec.Write(p);
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(saved, notes);
            Assert.Empty(notes);
            Assert.Equal(saved, ProfileCodec.Write(loaded));
        }

        private static Profile WithBounties(params BountyKind[] kinds)
        {
            var p = Profile.CreateNew(71);
            Rules.BeginRun(p, "counter-path", heroKey: "run.hero");
            p.Run.Bounties.Clear();
            foreach (var kind in kinds)
                p.Run.Bounties.Add(new Bounty { Kind = kind, Target = 3, RewardShards = 10, RewardTuning = 1, RewardXp = 5 });
            return p;
        }

        [Fact]
        public void Elemental_kill_objectives_count_each_present_element_not_unmarked_kills()
        {
            var p = WithBounties(BountyKind.FireHunter, BountyKind.ColdHunter, BountyKind.LightHunter, BountyKind.DarkHunter);
            Rules.OnElementalKill(p, false, false, false, false);
            Assert.All(p.Run.Bounties, b => Assert.Equal(0, b.Progress));
            Rules.OnElementalKill(p, true, false, true, false);
            Assert.Equal(new[] { 1, 0, 1, 0 }, p.Run.Bounties.Select(b => b.Progress));
            Rules.OnElementalKill(p, false, true, false, true);
            Assert.All(p.Run.Bounties, b => Assert.Equal(1, b.Progress));
            Rules.OnElementalKill(p, true, true, true, true);
            Rules.OnElementalKill(p, true, true, true, true);
            Assert.All(p.Run.Bounties, b => Assert.True(b.Done));
            int tuning = p.Run.SatchelTuning;
            Rules.OnElementalKill(p, true, true, true, true);
            Assert.Equal(tuning, p.Run.SatchelTuning);
        }

        [Fact]
        public void Support_counts_successful_grants_and_only_ally_heals()
        {
            var p = WithBounties(BountyKind.ShieldGiver, BountyKind.AllyHealer);
            foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Rules.OnSupportApplied(p, true, true, invalid);
                Rules.OnSupportApplied(p, false, true, invalid);
            }
            Assert.All(p.Run.Bounties, b => Assert.Equal(0, b.Progress));
            Rules.OnSupportApplied(p, false, false, 20f);
            Assert.All(p.Run.Bounties, b => Assert.Equal(0, b.Progress));
            Rules.OnSupportApplied(p, true, false, 20f);
            Rules.OnSupportApplied(p, false, true, 10f);
            Assert.All(p.Run.Bounties, b => Assert.Equal(1, b.Progress));
        }

        [Fact]
        public void Memory_objectives_are_slot_specific_and_ignore_other_skill_slots()
        {
            var p = WithBounties(BountyKind.MemoryQ, BountyKind.MemoryW, BountyKind.MemoryE, BountyKind.MemoryR);
            Rules.OnMemoryUsed(p, -1);
            Rules.OnMemoryUsed(p, 4);
            Assert.All(p.Run.Bounties, b => Assert.Equal(0, b.Progress));
            for (int i = 0; i < 4; i++)
            {
                Rules.OnMemoryUsed(p, i);
                Assert.Equal(Enumerable.Range(0, 4).Select(j => j <= i ? 1 : 0), p.Run.Bounties.Select(b => b.Progress));
            }
        }

        [Fact]
        public void Travel_and_gimmicks_increment_but_links_and_pressure_reach_a_maximum()
        {
            var p = WithBounties(BountyKind.ZoneTraveler, BountyKind.GimmickUser, BountyKind.LinkWeaver, BountyKind.PressureDiver);
            p.Run.Bounties[3].Target = 140;
            Rules.OnZoneTravelled(p);
            Rules.OnGimmicksTriggered(p, 0);
            Rules.OnGimmicksTriggered(p, -1);
            Rules.OnGimmicksTriggered(p, 2);
            Rules.OnLinksSatisfied(p, 2);
            Rules.OnLinksSatisfied(p, 2);
            Rules.OnLinksSatisfied(p, 1);
            Rules.OnPressureReported(p, p.Run.RunId, 1.3f);
            Rules.OnPressureReported(p, p.Run.RunId, 1.3f);
            Rules.OnPressureReported(p, p.Run.RunId, 1.2f);
            Assert.Equal(new[] { 1, 2, 2, 130 }, p.Run.Bounties.Select(b => b.Progress));
            string saved = ProfileCodec.Write(p);
            p = ProfileCodec.Read(saved, new List<string>());
            Rules.OnLinksSatisfied(p, 3);
            Rules.OnPressureReported(p, p.Run.RunId, 1.4f);
            Assert.True(p.Run.Bounties[2].Done);
            Assert.True(p.Run.Bounties[3].Done);
            Assert.Equal(2, p.Run.SatchelTuning);
        }

        [Fact]
        public void Repeated_current_pressure_completes_a_rerolled_bounty_only_once()
        {
            var p = WithBounties(BountyKind.Slayer);
            string runId = p.Run.RunId;
            Assert.Empty(Rules.OnPressureReported(p, runId, 1.75f));
            Assert.Equal(0, p.Run.Bounties[0].Progress);

            p.Upgrades[Upgrade.BountyReroll] = 1;
            p.RngState = 29;
            Rules.RerollBounty(p, 0);
            var bounty = p.Run.Bounties[0];
            Assert.Equal(BountyKind.PressureDiver, bounty.Kind);
            Assert.False(bounty.Done);

            Assert.Single(Rules.OnPressureReported(p, runId, 1.75f), e => e.Kind == EventKind.Bounty);
            Assert.True(bounty.Done);
            Assert.Equal(bounty.Target, bounty.Progress);
            int xp = p.DreamXp;
            Assert.Empty(Rules.OnPressureReported(p, runId, 1.75f));
            Assert.Empty(Rules.OnPressureReported(p, runId, 1.5f));
            Assert.Empty(Rules.OnPressureReported(p, runId, 1.75f));
            Assert.Equal(bounty.RewardShards, p.Run.SatchelShards);
            Assert.Equal(bounty.RewardTuning, p.Run.SatchelTuning);
            Assert.Equal(xp, p.DreamXp);
            Assert.Equal(1, p.Stats.BountiesDone);
        }

        [Fact]
        public void A_lower_current_pressure_after_bounty_replacement_does_not_block_the_old_value()
        {
            var p = WithBounties(BountyKind.Slayer);
            string runId = p.Run.RunId;
            Assert.Empty(Rules.OnPressureReported(p, runId, 1.75f));
            p.Run.Bounties.Clear();
            var bounty = new Bounty { Kind = BountyKind.PressureDiver, Target = 175, RewardShards = 10, RewardTuning = 1 };
            p.Run.Bounties.Add(bounty);

            Assert.Empty(Rules.OnPressureReported(p, runId, 1.5f));
            Assert.Equal(150, bounty.Progress);
            Assert.False(bounty.Done);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Single(Rules.OnPressureReported(p, runId, 1.75f), e => e.Kind == EventKind.Bounty);
            Assert.True(bounty.Done);
            Assert.Empty(Rules.OnPressureReported(p, runId, 1.75f));
            Assert.Equal(10, p.Run.SatchelShards);
            Assert.Equal(1, p.Run.SatchelTuning);
            Assert.Equal(1, p.Stats.BountiesDone);
        }

        [Fact]
        public void A_new_run_does_not_keep_pressure_progress_or_completion_from_the_previous_run()
        {
            var p = WithBounties(BountyKind.PressureDiver);
            string oldRunId = p.Run.RunId;
            p.Run.Bounties[0].Target = 175;
            Rules.OnPressureReported(p, oldRunId, 1.75f);
            Assert.True(p.Run.Bounties[0].Done);

            Rules.BeginRun(p, "pressure-next");
            p.Run.Bounties.Clear();
            var bounty = new Bounty { Kind = BountyKind.PressureDiver, Target = 175, RewardShards = 10, RewardTuning = 1 };
            p.Run.Bounties.Add(bounty);
            Assert.Empty(Rules.OnPressureReported(p, oldRunId, 1.75f));
            Assert.Empty(Rules.OnPressureReported(p, p.Run.RunId, 1f));
            Assert.Equal(0, bounty.Progress);
            Assert.False(bounty.Done);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Equal(0, p.Run.SatchelTuning);
            Assert.Single(Rules.OnPressureReported(p, p.Run.RunId, 1.75f), e => e.Kind == EventKind.Bounty);
            Assert.True(bounty.Done);
            Assert.Empty(Rules.OnPressureReported(p, p.Run.RunId, 1.75f));
            Assert.Equal(10, p.Run.SatchelShards);
            Assert.Equal(1, p.Run.SatchelTuning);
            Assert.Equal(2, p.Stats.BountiesDone);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(float.MaxValue)]
        [InlineData(-1f)]
        [InlineData(0f)]
        [InlineData(1f)]
        public void Invalid_or_base_pressure_does_not_advance_a_bounty(float multiplier)
        {
            var p = WithBounties(BountyKind.PressureDiver);
            p.Run.Bounties[0].Target = 175;
            Assert.Empty(Rules.OnPressureReported(p, p.Run.RunId, multiplier));
            Assert.Equal(0, p.Run.Bounties[0].Progress);
            Assert.False(p.Run.Bounties[0].Done);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Equal(0, p.Run.SatchelTuning);
        }

        [Fact]
        public void Pressure_reports_require_the_active_profile_run()
        {
            var p = WithBounties(BountyKind.PressureDiver);
            p.Run.Bounties[0].Target = 175;
            Assert.Empty(Rules.OnPressureReported(p, null, 1.75f));
            Assert.Empty(Rules.OnPressureReported(p, "another-run", 1.75f));
            Assert.Equal(0, p.Run.Bounties[0].Progress);
            Assert.False(p.Run.Bounties[0].Done);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Equal(0, p.Run.SatchelTuning);
            p.Run = null;
            Assert.Empty(Rules.OnPressureReported(p, "counter-path", 1.75f));
        }

        [Fact]
        public void Forge_progress_follows_successful_enhance_and_limit_break_operations()
        {
            var p = WithBounties(BountyKind.RelicEnhancer, BountyKind.LimitBreaker);
            var relic = Loot.RollRelic(new Rng(80), Rarity.Rare, 10, Slot.Weapon);
            var material = Loot.RollRelic(new Rng(81), Rarity.Rare, 10, Slot.Weapon);
            p.Stash.Add(relic);
            p.Stash.Add(material);
            Assert.Throws<InvalidOperationException>(() => Rules.Enhance(p, relic.Uid));
            Assert.Equal(0, p.Run.Bounties[0].Progress);
            p.AddMaterial(Materials.Shard, 10000);
            p.AddMaterial(Materials.Tuning, 100);
            Rules.Enhance(p, relic.Uid);
            Assert.Equal(1, p.Run.Bounties[0].Progress);
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, relic.Uid, material.Uid));
            Assert.Equal(0, p.Run.Bounties[1].Progress);
            relic.Enhance = Content.MaxEnhanceFor(relic);
            Rules.LimitBreak(p, relic.Uid, material.Uid);
            Assert.Equal(1, p.Run.Bounties[1].Progress);
            Assert.DoesNotContain(material, p.Stash);
        }

        [Fact]
        public void Forge_completion_returns_the_bounty_notice_and_pays_only_once()
        {
            var p = WithBounties(BountyKind.RelicEnhancer);
            p.Run.Bounties[0].Target = 1;
            var relic = Loot.RollRelic(new Rng(87), Rarity.Rare, 10);
            p.Stash.Add(relic);
            p.AddMaterial(Materials.Shard, 10000);
            var result = Rules.Enhance(p, relic.Uid);
            Assert.Single(result.AdditionalEvents, e => e.Kind == EventKind.Bounty);
            Assert.Equal(10, p.Run.SatchelShards);
            Assert.Equal(1, p.Run.SatchelTuning);
            Assert.True(p.Run.Bounties[0].Done);
            Rules.Enhance(p, relic.Uid);
            Assert.Equal(10, p.Run.SatchelShards);
            Assert.Equal(1, p.Run.SatchelTuning);
        }

        [Fact]
        public void Awakening_objective_tracks_level_reached_not_the_number_of_kills()
        {
            var p = WithBounties(BountyKind.Awakener);
            var relic = Loot.RollUnique(new Rng(90), Content.Uniques.First(), 10);
            p.Stash.Add(relic);
            p.Hero("run.hero").Equipped[(int)relic.Slot] = relic.Uid;
            relic.AwakenPoints = Content.AwakenThresholdFor(1) - 1;
            Rules.OnKill(p, MonsterTier.Normal, 10);
            Assert.Equal(1, p.Run.Bounties[0].Progress);
            Rules.OnKill(p, MonsterTier.Normal, 10);
            Assert.Equal(1, p.Run.Bounties[0].Progress);
            relic.AwakenPoints = Content.AwakenThresholdFor(3) - 1;
            Rules.OnKill(p, MonsterTier.Normal, 10);
            Assert.True(p.Run.Bounties[0].Done);
        }

        [Fact]
        public void Nightmare_flags_and_registered_variant_affixes_drive_hunting_objectives()
        {
            var p = WithBounties(BountyKind.VariantHunter, BountyKind.VeilHunter, BountyKind.PackHunter, BountyKind.PulseHunter, BountyKind.LastStandHunter);
            Rules.OnKill(p, MonsterTier.Normal, 10, variantId: "unknown.variant");
            Assert.All(p.Run.Bounties, b => Assert.Equal(0, b.Progress));
            Rules.OnKill(p, MonsterTier.Normal, 10, NightmareAffix.Veiled | NightmareAffix.Packbound | NightmareAffix.Pulsing | NightmareAffix.LastStand);
            Assert.Equal(new[] { 0, 1, 1, 1, 1 }, p.Run.Bounties.Select(b => b.Progress));
            var variant = Variants.All.First(v => (v.Affixes & NightmareAffix.Packbound) != 0);
            Rules.OnKill(p, MonsterTier.Normal, 10, variantId: variant.Id);
            Assert.Equal(1, p.Run.Bounties[0].Progress);
            Assert.Equal(2, p.Run.Bounties[2].Progress);
        }

        [Fact]
        public void Mausoleum_recovers_every_lost_relic_unenhanced_without_regranting_milestones()
        {
            var p = EventProfile(DreamEvent.LostMausoleum, 110);
            var lost = p.LostAndFound.ToArray();
            foreach (var r in lost) { r.Enhance = 5; r.EnhanceMilestones = 2; }
            int shards = p.Run.SatchelShards;
            Rules.UseEvent(p, DreamEvent.LostMausoleum);
            Assert.Empty(p.LostAndFound);
            Assert.Equal(shards - 60, p.Run.SatchelShards);
            foreach (var r in lost)
            {
                Assert.Contains(r, p.Run.Satchel);
                Assert.Equal(0, r.Enhance);
                Assert.Equal(2, r.EnhanceMilestones);
            }
        }

        [Fact]
        public void Vault_secures_only_the_selected_relic_not_other_unsecured_resources()
        {
            var p = EventProfile(DreamEvent.SealedVault, 120);
            var best = p.Run.Satchel.OrderByDescending(r => r.Score).First();
            int count = p.Run.Satchel.Count, shards = p.Run.SatchelShards, tuning = p.Run.SatchelTuning;
            int bank = p.Material(Materials.Shard);
            Rules.UseEvent(p, DreamEvent.SealedVault);
            Assert.Contains(best, p.Stash);
            Assert.DoesNotContain(best, p.Run.Satchel);
            Assert.Equal(count - 1, p.Run.Satchel.Count);
            Assert.Equal(shards - 40, p.Run.SatchelShards);
            Assert.Equal(tuning, p.Run.SatchelTuning);
            Assert.Equal(bank, p.Material(Materials.Shard));
        }

        [Theory]
        [InlineData(DreamEvent.ShadowExchange)]
        [InlineData(DreamEvent.RelicWager)]
        [InlineData(DreamEvent.TemperingAltar)]
        [InlineData(DreamEvent.StarOffering)]
        [InlineData(DreamEvent.DreamOffering)]
        [InlineData(DreamEvent.RelicExchange)]
        [InlineData(DreamEvent.SealedVault)]
        [InlineData(DreamEvent.PowerCrucible)]
        public void Destructive_trades_cannot_consume_locked_or_reserved_relics(DreamEvent e)
        {
            foreach (bool reserve in new[] { false, true })
            {
                var p = EventProfile(e, 130);
                var ledger = new TradeLedger();
                foreach (var r in p.Run.Satchel)
                {
                    if (reserve) ledger.Begin(TradeKind.SalvageForDust, 0, 0, 0, r.Uid);
                    else r.Locked = true;
                }
                string before = ProfileCodec.Write(p);
                Assert.False(DreamEvents.CanUse(p, e, false, out _, ledger));
                Assert.Throws<InvalidOperationException>(() => Rules.UseEvent(p, e, trades: ledger));
                Assert.Equal(before, ProfileCodec.Write(p));
            }
        }

        [Fact]
        public void Relic_wager_either_upgrades_the_same_base_or_salvages_without_duplicating_the_stake()
        {
            bool won = false, lost = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var p = EventProfile(DreamEvent.RelicWager, seed);
                var stake = p.Run.Satchel.Where(r => r.Rarity < Rarity.Epic).OrderByDescending(r => r.Score).First();
                var others = p.Run.Satchel.Where(r => r != stake).Select(r => r.Uid).ToHashSet();
                int shards = p.Run.SatchelShards;
                Rules.UseEvent(p, DreamEvent.RelicWager);
                Assert.DoesNotContain(stake, p.Run.Satchel);
                Assert.All(others, uid => Assert.Contains(p.Run.Satchel, r => r.Uid == uid));
                var replacement = p.Run.Satchel.Where(r => !others.Contains(r.Uid)).ToArray();
                if (replacement.Length == 1)
                {
                    won = true;
                    Assert.Equal(stake.BaseId, replacement[0].BaseId);
                    Assert.Equal(stake.Rarity + 1, replacement[0].Rarity);
                    Assert.Equal(shards, p.Run.SatchelShards);
                }
                else
                {
                    lost = true;
                    Assert.Empty(replacement);
                    Assert.Equal(shards + Content.SalvageShards(stake.Rarity), p.Run.SatchelShards);
                }
            }
            Assert.True(won && lost);
        }

        [Fact]
        public void Memory_well_replaces_one_first_power_only_with_an_unowned_power_from_its_slot()
        {
            var p = EventProfile(DreamEvent.MemoryWell, 145);
            var before = p.Stash.ToDictionary(r => r.Uid, r => r.Powers.Select(x => (x.Power, x.Value)).ToArray());
            int tuning = p.Material(Materials.Tuning);
            Rules.UseEvent(p, DreamEvent.MemoryWell);
            var changed = Assert.Single(p.Stash, r => !before[r.Uid].SequenceEqual(r.Powers.Select(x => (x.Power, x.Value))));
            Assert.Equal(tuning - 1, p.Material(Materials.Tuning));
            Assert.Equal(before[changed.Uid].Skip(1), changed.Powers.Skip(1).Select(x => (x.Power, x.Value)));
            Assert.DoesNotContain(before[changed.Uid], line => line.Power == changed.Powers[0].Power);
            Assert.Contains(Content.PowerPool(changed.Slot), range => range.Power == changed.Powers[0].Power);
        }

        private static Profile EventProfile(DreamEvent e, ulong seed)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "event-roundtrip", heroKey: "run.hero");
            p.Run.Bounties.Clear();
            p.Run.AwaitingChoice = true;
            p.Run.OfferedEvent = e;
            p.Run.Heat = 2;
            p.Run.SatchelShards = 500;
            p.Run.SatchelTuning = 20;
            p.AddMaterial(Materials.Shard, 10000);
            p.AddMaterial(Materials.Tuning, 100);
            var rng = new Rng(seed + 400);
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                var equipped = Loot.RollRelic(rng, Rarity.Epic, 10, slot);
                p.Stash.Add(equipped);
                p.Hero("run.hero").Equipped[(int)slot] = equipped.Uid;
            }
            foreach (var rarity in new[] { Rarity.Common, Rarity.Common, Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic })
                p.Run.Satchel.Add(Loot.RollRelic(rng, rarity, 10));
            var lost = Loot.RollRelic(rng, Rarity.Rare, 10);
            lost.Enhance = 1;
            p.LostAndFound.Add(lost);
            p.LostAndFound.Add(Loot.RollRelic(rng, Rarity.Epic, 10));
            return p;
        }
    }
}
