using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class NewPowersV129Tests
    {
        private static PowerRuntime With(params (Power Power, int Value)[] powers)
        {
            var build = new Build();
            foreach (var (power, value) in powers) build.Powers[power] = value;
            return new PowerRuntime(build, 0);
        }

        [Fact]
        public void Forty_four_ids_are_appended_and_have_text_caps_epithets_and_wire_roundtrips()
        {
            var powers = Enum.GetValues(typeof(Power)).Cast<Power>().Where(NewPowersV129.IsPower).ToArray();
            Assert.Equal(44, powers.Length);
            Assert.Equal(48, (int)Power.ShieldbreakBurst);
            Assert.Equal(91, (int)Power.SpilloverStrike);
            Assert.Equal(92, Enum.GetValues(typeof(Power)).Length);
            bool previous = Loc.Japanese;
            try
            {
                foreach (bool japanese in new[] { true, false })
                {
                    Loc.Japanese = japanese;
                    foreach (Power power in powers)
                    {
                        int cap = Content.PowerCap(power);
                        Assert.True(cap > 0);
                        string description = Content.FormatPower(power, cap);
                        Assert.Contains(Content.PowerName(power), description);
                        Assert.DoesNotContain("{v", description);
                        Assert.NotNull(Content.Epithet(power));
                        var build = Build.Decode($"s:;p:{(int)power}={int.MaxValue};h:0");
                        Assert.Equal(cap, build.Get(power));
                        Assert.Equal(cap, Build.Decode(build.Encode()).Get(power));
                    }
                }
            }
            finally { Loc.Japanese = previous; }
        }

        [Fact]
        public void Pools_obey_design_slots_and_new_ranges_and_unsupported_hooks_are_excluded()
        {
            foreach (Slot slot in Content.SlotOrder)
            {
                var pool = Content.PowerPool(slot);
                Assert.Equal(pool.Count, pool.Select(p => p.Power).Distinct().Count());
                foreach (var range in pool.Where(p => NewPowersV129.IsPower(p.Power)))
                {
                    Assert.InRange(range.Min, 1, range.Max);
                    Assert.InRange(range.Max, range.Min, Content.PowerCap(range.Power));
                    Assert.True(Content.IsPowerDroppable(range.Power));
                }
            }
            Assert.Contains(Content.PowerPool(Slot.Head), p => p.Power == Power.GleamingWard && p.Min == 6 && p.Max == 12);
            Assert.DoesNotContain(Content.PowerPool(Slot.Weapon), p => p.Power == Power.GleamingWard);
            Assert.Contains(Content.PowerPool(Slot.Weapon), p => p.Power == Power.Medley && p.Min == 3 && p.Max == 6);
            Assert.DoesNotContain(Content.PowerPool(Slot.Armor), p => p.Power == Power.Medley);
            Assert.DoesNotContain(Content.SlotOrder.SelectMany(Content.PowerPool), p => p.Power == Power.UnbowedMind);
            Assert.Contains(Content.SlotOrder.SelectMany(Content.PowerPool), p => p.Power == Power.PilingLuck);
        }

        [Fact]
        public void Rare_rolls_never_include_any_conditional_attributes_while_epics_can()
        {
            var rng = new Rng(129);
            foreach (Slot slot in Content.SlotOrder)
                for (int i = 0; i < 250; i++)
                {
                    var rare = Loot.RollRelic(rng, Rarity.Rare, 40, slot: slot);
                    Assert.Single(rare.Powers);
                    Assert.All(rare.Powers, p => Assert.False(NewPowersV129.IsConditionalAttribute(p.Power)));
                }
            Assert.True(Content.PowerAllowedForRarity(Power.GleamingWard, Rarity.Epic));
            Assert.True(Content.PowerAllowedForRarity(Power.Medley, Rarity.Legendary));
            Assert.False(Content.PowerAllowedForRarity(Power.UnbowedMind, Rarity.Epic));
        }

        [Fact]
        public void Shieldbreak_requires_enemy_break_and_keeps_six_second_gate()
        {
            var rt = With((Power.ShieldbreakBurst, 12));
            Assert.Equal(0, rt.TakeShieldbreak(0, 20, 1000, false));
            Assert.Equal(0, rt.TakeShieldbreak(0, 0, 1000, true));
            Assert.Equal(120, rt.TakeShieldbreak(0, 20, 1000, true));
            Assert.Equal(0, rt.TakeShieldbreak(5.999f, 2000, 1000, true));
            Assert.Equal(240, rt.TakeShieldbreak(6, 2000, 1000, true));
        }

        [Fact]
        public void Shared_ward_never_repeats_shared_shields_and_shield_bash_does_not_consume_shield()
        {
            var rt = With((Power.SharedWard, 40), (Power.ShieldBash, 60), (Power.GleamingWard, 12));
            Assert.Equal(80, rt.SharedWardShield(200));
            Assert.Equal(0, rt.SharedWardShield(200, true));
            Assert.Equal(0, rt.Current(0).AttackPct);
            rt.ShieldAmount = 200;
            Assert.Equal(12, rt.Current(0).AttackPct);
            Assert.Equal(120, rt.OnNewBasicHit(0, 1, 100, 100, false).DirectDamage);
            Assert.Equal(200, rt.ShieldAmount);
            rt.ShieldAmount = 0;
            Assert.Equal(0, rt.OnNewBasicHit(1, 1, 100, 100, false).DirectDamage);
        }

        [Fact]
        public void Support_effects_use_actual_healing_and_per_ally_gates()
        {
            var rt = With((Power.CoStar, 20), (Power.TriumphSong, 8), (Power.WatchfulHand, 25), (Power.KindnessReturns, 30), (Power.RelayHand, 10));
            Assert.Equal(.2f, rt.CoStarCooldownFraction(false));
            Assert.Equal(.1f, rt.CoStarCooldownFraction(true));
            Assert.Equal(80, rt.TriumphSongHeal(1000));
            Assert.Equal(0, rt.TakeWatchfulHand(0, 1, .3f, 1000));
            Assert.Equal(250, rt.TakeWatchfulHand(0, 1, .29f, 1000));
            Assert.Equal(250, rt.TakeWatchfulHand(0, 2, .29f, 1000));
            Assert.Equal(0, rt.TakeWatchfulHand(44.999f, 1, .29f, 1000));
            Assert.Equal(250, rt.TakeWatchfulHand(45, 1, .29f, 1000));
            Assert.Equal(0, rt.TakeWatchfulHand(45, 3, 0, 1000));
            Assert.Equal(30, rt.KindnessReturnsHeal(100));
            Assert.Equal(0, rt.KindnessReturnsHeal(-10));
            Assert.Equal(.1f, rt.TakeRelayHand(0, 1));
            Assert.Equal(0, rt.TakeRelayHand(1.99f, 1));
            Assert.Equal(.1f, rt.TakeRelayHand(0, 2));
            Assert.Equal(.1f, rt.TakeRelayHand(2, 1));
        }

        [Fact]
        public void Wanderer_requires_target_switch_and_primary_hit_with_per_target_gate()
        {
            var rt = With((Power.WanderersEdge, 30));
            Assert.Equal(0, rt.OnNewBasicHit(0, 1, 200, 1, false).DirectDamage);
            Assert.Equal(60, rt.OnNewBasicHit(.1f, 2, 200, 1, false).DirectDamage);
            Assert.Equal(0, rt.OnNewBasicHit(.2f, 3, 200, 1, false, primary: false).DirectDamage);
            Assert.Equal(0, rt.OnNewBasicHit(.3f, 2, 200, 1, false).DirectDamage);
            Assert.Equal(60, rt.OnNewBasicHit(.4f, 1, 200, 1, false).DirectDamage);
            Assert.Equal(0, rt.OnNewBasicHit(.5f, 2, 200, 1, false).DirectDamage);
            Assert.Equal(0, rt.OnNewBasicHit(1f, 1, 200, 1, false).DirectDamage);
            Assert.Equal(60, rt.OnNewBasicHit(1.1f, 2, 200, 1, false).DirectDamage);
        }

        [Fact]
        public void Focus_requires_five_contiguous_hits_and_resets_on_four_second_gap()
        {
            var rt = With((Power.FocusFire, 80));
            for (int i = 0; i < 4; i++) Assert.Equal(0, rt.OnNewBasicHit(i, 1, 200, 1, false).DirectDamage);
            Assert.Equal(160, rt.OnNewBasicHit(4, 1, 200, 1, false).DirectDamage);
            for (int i = 5; i < 9; i++) Assert.Equal(0, rt.OnNewBasicHit(i, 1, 200, 1, false).DirectDamage);
            Assert.Equal(0, rt.OnNewBasicHit(12, 1, 200, 1, false).DirectDamage);
            Assert.Equal(0, rt.OnNewBasicHit(13, 2, 200, 1, false).DirectDamage);
        }

        [Fact]
        public void Breakout_needs_a_fresh_boundary_crossing_after_cooldown()
        {
            var rt = With((Power.Breakout, 12));
            Assert.Equal(0, rt.TakeBreakout(0, 3, 1000));
            Assert.Equal(120, rt.TakeBreakout(0, 4, 1000));
            Assert.Equal(0, rt.TakeBreakout(11, 5, 1000));
            Assert.Equal(0, rt.TakeBreakout(12, 3, 1000));
            Assert.Equal(120, rt.TakeBreakout(12, 4, 1000));
        }

        [Fact]
        public void Position_effects_cover_single_enemy_stillness_moving_and_party_rank()
        {
            var rt = With((Power.DuelistsWay, 40), (Power.ImmovableStance, 11), (Power.StrafeShot, 30), (Power.VanguardsOath, 30), (Power.RearguardsWay, 12));
            rt.NearbyEnemies8 = 1;
            Assert.Equal(80, rt.OnNewBasicHit(0, 1, 200, 100, false).DirectDamage);
            Assert.Equal(0, rt.OnNewBasicHit(0, 2, 200, 100, false, targetWithin8: false).DirectDamage);
            rt.NearbyEnemies8 = 2;
            Assert.Equal(0, rt.OnNewBasicHit(0, 1, 200, 100, false).DirectDamage);
            rt.ObserveMovement(0, false, 0);
            Assert.Equal(0, rt.OutgoingDamageAmplification(.999f, false, false));
            Assert.Equal(.11f, rt.OutgoingDamageAmplification(1, false, false));
            Assert.Equal(.05f, rt.IncomingDamageReduction(1));
            rt.ObserveMovement(2, true, 1);
            Assert.Equal(0, rt.IncomingDamageReduction(2));
            Assert.Equal(30, rt.OnNewBasicHit(2, 1, 200, 100, false).SlowPercent);
            rt.IsVanguard = true;
            Assert.Equal(.15f, rt.ShieldGainAmplification());
            rt.LivingPartySize = 2;
            Assert.Equal(.3f, rt.ShieldGainAmplification());
            rt.IsRearguard = true;
            Assert.Equal(.12f, rt.OutgoingDamageAmplification(2, true, false));
            rt.LivingPartySize = 1;
            Assert.Equal(0, rt.OutgoingDamageAmplification(2, true, false));
        }

        [Fact]
        public void All_next_basic_bonuses_share_largest_only_and_only_winner_is_consumed()
        {
            var rt = With((Power.EchoingDodge, 70), (Power.ShadowStep, 60), (Power.RunUp, 80));
            rt.ObserveMovement(0, true, 200, true);
            Assert.False(rt.RunUpReady);
            rt.ObserveMovement(0, true, 11.9f);
            Assert.False(rt.RunUpReady);
            rt.ObserveMovement(0, true, .1f);
            Assert.True(rt.RunUpReady);
            rt.OnSkillUsed(0, true, false);
            rt.PrimeNextBasic(0, 100);
            var first = rt.OnAttackHit(1, 1000, 200, 100, 1);
            Assert.Equal(200, first.PrimedDamage);
            Assert.Equal(0, first.EchoDamage + first.ShadowStepDamage + first.RunUpDamage);
            Assert.True(rt.RunUpReady);
            Assert.Equal(160, rt.OnAttackHit(1.1f, 1000, 200, 100, 1).RunUpDamage);
            Assert.Equal(140, rt.OnAttackHit(1.2f, 1000, 200, 100, 1).EchoDamage);
            Assert.Equal(120, rt.OnAttackHit(1.3f, 1000, 200, 100, 1).ShadowStepDamage);
            Assert.False(rt.RunUpReady);
            Assert.Equal(0, rt.WalkedDistance);
            rt.PrimeNextBasic(10, 80);
            Assert.Equal(0, rt.OnAttackHit(15, 1000, 200, 100, 1).PrimedDamage);
        }

        [Fact]
        public void Element_threshold_gates_and_prism_sequence_never_consume_native_stacks()
        {
            var rt = With((Power.StardustCycle, 20), (Power.PrismShift, 3));
            Assert.Equal(0, rt.OnElementApplied(0, 1, 2, 3, 4, 1000).LongestCooldownFraction);
            Assert.Equal(.2f, rt.OnElementApplied(0, 1, 2, 4, 5, 1000).LongestCooldownFraction);
            Assert.Equal(0, rt.OnElementApplied(.9f, 2, 2, 4, 5, 1000).LongestCooldownFraction);
            Assert.Equal(.2f, rt.OnElementApplied(1, 2, 2, 4, 5, 1000).LongestCooldownFraction);
            Assert.Equal(0, rt.OnElementApplied(2, 1, 2, 4, 5, 1000).LongestCooldownFraction);
            Assert.Equal(.2f, rt.OnElementApplied(5, 1, 2, 4, 5, 1000).LongestCooldownFraction);
            Assert.Equal(30, rt.OnElementApplied(5, 1, 0, 0, 1, 1000).Shield);
            Assert.Equal(0, rt.OnElementApplied(5.5f, 1, 3, 0, 1, 1000).Shield);
            Assert.Equal(0, rt.OnElementApplied(6, 1, 3, 1, 2, 1000).Shield);
            Assert.Equal(30, rt.OnElementApplied(6, 1, 1, 0, 1, 1000).Shield);
        }

        [Fact]
        public void Element_kill_and_critical_bonuses_use_types_and_dark_threshold()
        {
            var rt = With((Power.UmbralHeritage, 80), (Power.BrittleIce, 60), (Power.ElementalHarvest, 35));
            Assert.False(rt.RollUmbralHeritage(1, 0));
            Assert.True(rt.RollUmbralHeritage(2, .79));
            Assert.False(rt.RollUmbralHeritage(2, .8));
            Assert.Equal(120, rt.BrittleIceDamage(200, true, true));
            Assert.Equal(0, rt.BrittleIceDamage(200, false, true));
            Assert.Equal(0, rt.ElementalHarvestDamage(200, 1));
            Assert.Equal(140, rt.ElementalHarvestDamage(200, 2));
            Assert.Equal(280, rt.ElementalHarvestDamage(200, 4));
        }

        [Fact]
        public void Summon_effects_require_owned_kills_actual_death_and_overheal_window()
        {
            var rt = With((Power.PackFeast, 5), (Power.DeathBloom, 120), (Power.Lifeline, 6));
            Assert.Equal(50, rt.PackFeastShield(1000, true));
            Assert.Equal(0, rt.PackFeastShield(1000, false));
            Assert.Equal(240, rt.DeathBloomDamage(200, true));
            Assert.Equal(0, rt.DeathBloomDamage(200, false));
            rt.OnOverheal(0, 0);
            Assert.Equal(0, rt.OutgoingDamageAmplification(1, false, true));
            rt.OnOverheal(1, 10);
            Assert.Equal(.06f, rt.OutgoingDamageAmplification(4.999f, false, true));
            rt.OnOverheal(4, 20);
            Assert.Equal(.06f, rt.OutgoingDamageAmplification(7.999f, false, true));
            Assert.Equal(0, rt.OutgoingDamageAmplification(8, false, true));
        }

        [Fact]
        public void Medley_counts_distinct_normal_memories_refreshes_on_new_types_and_expires()
        {
            var rt = With((Power.Medley, 6));
            rt.OnNewMemoryUsed(0, "movement", true, false);
            rt.OnNewMemoryUsed(0, "ultimate", false, true);
            Assert.Equal(0, rt.MedleyStacks(0));
            rt.OnNewMemoryUsed(0, "Q", false, false);
            rt.OnNewMemoryUsed(5, "Q", false, false);
            Assert.Equal(1, rt.MedleyStacks(5));
            rt.OnNewMemoryUsed(8, "W", false, false);
            rt.OnNewMemoryUsed(10, "E", false, false);
            rt.OnNewMemoryUsed(12, "R", false, false);
            Assert.Equal(3, rt.MedleyStacks(23.999f));
            Assert.Equal(18, rt.Current(23.999f).AttackPct);
            Assert.Equal(0, rt.MedleyStacks(24));
        }

        [Fact]
        public void Memory_windows_readiness_repeat_cooldowns_and_splashes_are_independent()
        {
            var rt = With((Power.Spellsweep, 60), (Power.BareHandedPride, 2), (Power.OpeningSalvo, 30), (Power.PileOn, 40), (Power.AceInHand, 16), (Power.CritSplash, 40));
            rt.ObserveMemoryReadiness(0, true, true, false);
            Assert.Equal(0, rt.OnNewMemoryUsed(0, "Q", false, false));
            Assert.False(rt.AllNormalMemoriesCooling);
            rt.ObserveMemoryReadiness(2, true, false, true);
            Assert.Equal(.3f, rt.OnNewMemoryUsed(0, "Q", false, false));
            Assert.Equal(.4f, rt.OnNewMemoryUsed(1, "Q", false, false));
            rt.OnNewMemoryUsed(2, "W", false, false);
            Assert.Equal(0, rt.OnNewMemoryUsed(2, "Q", false, false));
            Assert.Equal(.16f, rt.OutgoingDamageAmplification(2, true, false));
            Assert.Equal(0, rt.OutgoingDamageAmplification(2, false, false));
            rt.ObserveMemoryReadiness(2, false, true, false);
            var hit = rt.OnNewBasicHit(3, 1, 100, 200, true);
            Assert.Equal(200, hit.SplashDamage);
            Assert.Equal(.2f, hit.NormalCooldownSeconds);
            Assert.Equal(0, rt.OnNewBasicHit(3.1f, 1, 100, 200, false).NormalCooldownSeconds);
            Assert.Equal(.2f, rt.OnNewBasicHit(3.3f, 1, 100, 200, false).NormalCooldownSeconds);
            Assert.Equal(0, rt.OnNewBasicHit(4, 1, 100, 200, false).SplashDamage);
            rt.OnNewMemoryUsed(5, "Q", false, false);
            Assert.Equal(0, rt.OnNewBasicHit(10, 1, 100, 200, false).SplashDamage);
        }

        [Fact]
        public void Utility_effects_return_exact_percentages_and_shard_gate()
        {
            var rt = With((Power.CrystalCircuit, 15), (Power.ShardBoon, 10), (Power.DreamOmen, 25), (Power.ReturningBlade, 20), (Power.Apothecary, 20), (Power.SpilloverStrike, 80));
            Assert.Equal(.15f, rt.CrystalCircuitCooldownFraction);
            Assert.Equal(.25f, rt.DreamOmenCooldownFraction);
            Assert.Equal(125, rt.DreamOmenShield(1000));
            Assert.Equal(10, rt.TakeShardBoon(0, 1000));
            Assert.Equal(0, rt.TakeShardBoon(.299f, 1000));
            Assert.Equal(10, rt.TakeShardBoon(.3f, 1000));
            Assert.Equal(.2f, rt.ReturningBladeCooldownFraction(true));
            Assert.Equal(0, rt.ReturningBladeCooldownFraction(false));
            Assert.Equal(.2f, rt.ApothecaryCooldownFraction);
            Assert.Equal(50, rt.ApothecaryAllyHeal(100));
            Assert.Equal(80, rt.SpilloverDamage(200, 100));
            Assert.Equal(0, rt.SpilloverDamage(80, 100));
        }

        [Fact]
        public void Health_loss_accumulates_only_actual_loss_and_zone_resets_progress()
        {
            var rt = With((Power.TollOfGrudge, 30), (Power.UnbowedMind, 8), (Power.ReadyGuard, 12), (Power.RunUp, 80));
            Assert.Equal(0, rt.TakeTollOfGrudge(300, 1000));
            Assert.Equal(300, rt.TakeTollOfGrudge(400, 1000));
            Assert.Equal(600, rt.TakeTollOfGrudge(1200, 1000));
            Assert.Equal(0, rt.TakeTollOfGrudge(0, 1000));
            Assert.Equal(0, rt.TakeUnbowedMind(0, 1000, false));
            Assert.Equal(80, rt.TakeUnbowedMind(0, 1000, true));
            Assert.Equal(0, rt.TakeUnbowedMind(7.999f, 1000, true));
            Assert.Equal(80, rt.TakeUnbowedMind(8, 1000, true));
            Assert.Equal(0, rt.TakeReadyGuard(0, 1000));
            Assert.Equal(120, rt.TakeReadyGuard(5, 1000));
            Assert.Equal(0, rt.TakeReadyGuard(9.999f, 1000));
            Assert.Equal(120, rt.TakeReadyGuard(15, 1000));
            rt.ObserveMovement(0, true, 12);
            rt.OnZoneLoaded();
            Assert.False(rt.RunUpReady);
            Assert.Equal(0, rt.TakeTollOfGrudge(500, 1000));
        }

        [Fact]
        public void Critical_sequences_handle_guarantees_target_counts_timeout_and_forgetting()
        {
            var rt = With((Power.PilingLuck, 2), (Power.WeakPointWound, 120));
            for (int i = 0; i < 12; i++) rt.OnNewBasicHit(i, 1, 100, 100, false);
            Assert.Equal(10, rt.PilingLuckStacks);
            Assert.Equal(.2f, rt.PilingLuckCriticalChance);
            rt.OnNewBasicHit(13, 1, 100, 100, true, true);
            Assert.Equal(10, rt.PilingLuckStacks);
            rt.OnNewBasicHit(14, 1, 100, 100, true);
            Assert.Equal(0, rt.PilingLuckStacks);
            Assert.Equal(0, rt.TakeWeakPointWound(0, 1, 200, true));
            Assert.Equal(0, rt.TakeWeakPointWound(1, 1, 200, false));
            Assert.Equal(0, rt.TakeWeakPointWound(1, 1, 200, true));
            Assert.Equal(0, rt.TakeWeakPointWound(1, 2, 200, true));
            Assert.Equal(240, rt.TakeWeakPointWound(2, 1, 200, true));
            Assert.Equal(0, rt.TakeWeakPointWound(7, 2, 200, true));
            rt.ForgetNewPowerTarget(2);
            Assert.Equal(0, rt.TakeWeakPointWound(8, 2, 200, true));
            Assert.Equal(0, rt.TakeWeakPointWound(9, 2, 200, true));
            Assert.Equal(240, rt.TakeWeakPointWound(10, 2, 200, true));
        }

        [Fact]
        public void Conditional_total_caps_all_eleven_sources_but_excludes_links()
        {
            var rt = With((Power.Vigor, 40), (Power.Overload, 50), (Power.Resonance, 20), (Power.UltimateSurge, 50),
                (Power.Devotion, 10), (Power.CrystalResonance, 6), (Power.LucidBoon, 18), (Power.PreyPride, 12),
                (Power.Retaliation, 60), (Power.GleamingWard, 18), (Power.Medley, 10));
            rt.ResonanceSelf = 20; rt.ResonanceShared = 10; rt.GemQualityTotal = 800; rt.EvilDreamCount = 6; rt.HuntLevel = 3;
            rt.ShieldAmount = 100; rt.LinkAttunePct = 17;
            for (int i = 0; i < 5; i++) rt.OnShrineUsed();
            rt.OnSkillUsed(0, false, false); rt.OnSkillUsed(0, false, true); rt.OnDamaged(0, 1, true);
            rt.OnNewMemoryUsed(0, "Q", false, false);
            rt.OnNewMemoryUsed(0, "W", false, false);
            rt.OnNewMemoryUsed(0, "E", false, false);
            Assert.Equal(137, rt.Current(1).AttackPct);
            Assert.Equal(137, rt.Current(1).PowerPct);
        }

        [Fact]
        public void Conditional_budget_preserves_awakening_and_roundtrips_safely()
        {
            var rt = With((Power.Vigor, 40), (Power.Overload, 50), (Power.Retaliation, 60));
            rt.Build.ConditionalBasePowers[Power.Vigor] = 30;
            rt.Build.ConditionalBasePowers[Power.Overload] = 40;
            rt.Build.ConditionalBasePowers[Power.Retaliation] = 50;
            rt.OnSkillUsed(0, false, false); rt.OnDamaged(0, 1, true);
            Assert.Equal(150, rt.Current(1).AttackPct);
            var decoded = Build.Decode(rt.Build.Encode());
            Assert.Equal(30, decoded.ConditionalBase(Power.Vigor));
            rt.SetBuild(decoded);
            Assert.Equal(150, rt.Current(1).AttackPct);
            var malicious = Build.Decode("s:;p:29=40;u:29=0;h:0");
            Assert.Equal(23, malicious.ConditionalBase(Power.Vigor));
            Assert.Equal(40, Build.Decode("s:;p:29=40;h:0").ConditionalBase(Power.Vigor));
        }

        [Fact]
        public void Movement_never_triggers_pile_on_and_breaks_the_previous_sequence()
        {
            var rt = With((Power.PileOn, 40));
            rt.OnNewMemoryUsed(0, "Q", false, false);
            rt.OnNewMemoryUsed(1, "Q", false, false);
            for (int i = 2; i < 10; i++) Assert.Equal(0, rt.OnNewMemoryUsed(i, "M", true, false));
            Assert.Equal(0, rt.OnNewMemoryUsed(10, "Q", false, false));
            Assert.Equal(0, rt.OnNewMemoryUsed(11, "Q", false, false));
            Assert.Equal(.4f, rt.OnNewMemoryUsed(12, "Q", false, false));
        }

        [Fact]
        public void Unequipping_clears_new_charges_and_zone_guard_requires_a_new_five_second_rest()
        {
            var rt = With((Power.Medley, 6), (Power.RunUp, 80), (Power.ReadyGuard, 12));
            var equipped = rt.Build;
            rt.OnNewMemoryUsed(0, "Q", false, false);
            rt.ObserveMovement(0, true, 12);
            rt.SetBuild(new Build()); rt.SetBuild(equipped);
            Assert.False(rt.RunUpReady);
            Assert.Equal(0, rt.MedleyStacks(1));
            rt.OnZoneLoaded(100);
            Assert.Equal(0, rt.TakeReadyGuard(104.999f, 1000));
            Assert.Equal(120, rt.TakeReadyGuard(110, 1000));
        }

        [Fact]
        public void Actual_awakened_gear_build_preserves_unawakened_budget_values()
        {
            var p = Profile.CreateNew(129);
            var r = Loot.RollRelic(new Rng(129), Rarity.Epic, 40, slot: Slot.Weapon);
            r.Powers.Clear(); r.Powers.Add(new PowerLine(Power.Overload, 20));
            r.AwakenLevel = 3;
            p.Stash.Add(r);
            Rules.Equip(p, "Hero_Vesper", r.Uid);
            var build = Build.Compute(p, "Hero_Vesper", 0);
            Assert.Equal(36, build.Get(Power.Overload));
            Assert.Equal(20, build.ConditionalBase(Power.Overload));
            var roundtrip = Build.Decode(build.Encode());
            Assert.Equal(20, roundtrip.ConditionalBase(Power.Overload));
        }

        [Fact]
        public void Legacy_powerless_rare_milestone_cannot_grant_conditional_attributes()
        {
            for (ulong seed = 1; seed <= 80; seed++)
            {
                var p = Profile.CreateNew(seed);
                var r = Loot.RollRelic(new Rng(seed), Rarity.Rare, 1, slot: Slot.Head);
                r.Powers.Clear();
                r.Enhance = Content.EnhanceMilestoneSecond - 1;
                r.EnhanceMilestones = 1;
                p.Stash.Add(r); p.AddMaterial(Materials.Shard, 100000);
                Rules.Enhance(p, r.Uid);
                Assert.False(NewPowersV129.IsConditionalAttribute(Assert.Single(r.Powers).Power));
            }
        }

        [Fact]
        public void Per_ally_support_cooldowns_survive_world_travel_and_build_refresh()
        {
            var rt = With((Power.WatchfulHand, 25), (Power.RelayHand, 10));
            Assert.Equal(250, rt.TakeWatchfulHand(100, 1, .2f, 1000));
            Assert.Equal(.1f, rt.TakeRelayHand(100, 1));
            rt.OnZoneLoaded(101);
            rt.SetBuild(rt.Build);
            Assert.Equal(0, rt.TakeWatchfulHand(101, 1, .2f, 1000));
            Assert.Equal(0, rt.TakeRelayHand(101, 1));
            Assert.Equal(250, rt.TakeWatchfulHand(101, 2, .2f, 1000));
            Assert.Equal(.1f, rt.TakeRelayHand(101, 2));
            Assert.Equal(.1f, rt.TakeRelayHand(102, 1));
            Assert.Equal(0, rt.TakeWatchfulHand(144.999f, 1, .2f, 1000));
            Assert.Equal(250, rt.TakeWatchfulHand(145, 1, .2f, 1000));
        }

        [Fact]
        public void Memory_well_cannot_reroll_a_rare_into_conditional_attributes()
        {
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var p = Profile.CreateNew(seed);
                var r = Loot.RollRelic(new Rng(seed), Rarity.Rare, 10, slot: Slot.Head);
                Power old = Assert.Single(r.Powers).Power;
                p.Stash.Add(r);
                Rules.Equip(p, "Hero_Vesper", r.Uid);
                Rules.BeginRun(p, "rare-well", heroKey: "Hero_Vesper");
                p.Run.AwaitingChoice = true;
                p.Run.OfferedEvent = DreamEvent.MemoryWell;
                p.AddMaterial(Materials.Tuning, 1);
                Rules.UseEvent(p, DreamEvent.MemoryWell);
                Power replacement = Assert.Single(r.Powers).Power;
                Assert.NotEqual(old, replacement);
                Assert.False(NewPowersV129.IsConditionalAttribute(replacement));
            }
        }
    }
}
