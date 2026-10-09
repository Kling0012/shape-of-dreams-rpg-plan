using System;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    // Uses the production-linked host adapter and wound runtime; Unity/game APIs are doubles.
    public sealed class WoundRankNativeTests
    {
        private const string Memory = "St_QR_DistortedMind";

        private static (Build Build, GimmickDef Wound) BismuthWound(int spent)
        {
            var key = StarClusters.CreateGeneratedAuthored("Hero_Bismuth")
                .Single(star => star.LocalStarId == "bismuth.key.unfinished-arrows").Effect.KeystoneDefinition;
            var build = new Build { SpentStarPoints = spent, SelectedKeystone = key };
            var grant = Assert.Single(key.Grants);
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                AuthoredKeystoneComposer.MechanismPayload(grant), Memory,
                sourceSlot: MechanismMemorySlot.Q, heroKey: "Hero_Bismuth");
            Assert.Equal(45m, grant.Gimmick.Value);
            Assert.Equal(6m, result.DurationSeconds);
            return (build, AuthoredKeystoneComposer.EffectiveGimmick(grant.Gimmick, result));
        }

        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime, Entity Enemy) Setup(Build build)
        {
            UnityEngine.Time.time = 0;
            var runtime = new HostAuthority.HeroRuntime { Hero = new Hero(), Powers = new PowerRuntime(build, 0) };
            return (new HostAuthority(), runtime, new Entity { Relation = EntityRelation.Enemy });
        }

        private static void Apply(HostAuthority host, HostAuthority.HeroRuntime runtime, Entity enemy, GimmickDef wound)
        {
            runtime.PendingGimmicks.Add(new HostAuthority.PendingGimmick
            {
                Request = new GimmickRequest { Entry = new GimmickEntry
                    { StarId = "test.ranked.wound", Memory = Memory, Def = wound } },
                Victim = enemy
            });
            host.FlushAuthored(runtime);
            Assert.Empty(runtime.PendingGimmicks);
        }

        [Theory]
        [InlineData(0, 90f)]
        [InlineData(250, 157.5f)]
        [InlineData(500, 225f)]
        [InlineData(504, 226.08f)]
        public void Shipped_bismuth_grant_delivers_its_ranked_total_over_all_twelve_ticks(int spent, float expected)
        {
            var (build, wound) = BismuthWound(spent);
            Assert.Equal(expected, wound.ValuePercent, 3);
            var (host, runtime, enemy) = Setup(build);
            Apply(host, runtime, enemy, wound);
            Assert.Equal(1000f, enemy.currentHealth);
            for (int tick = 1; tick <= 12; tick++)
            {
                UnityEngine.Time.time = tick * .5f;
                host.FlushAuthored(runtime);
                Assert.Equal(1000f - expected * tick / 12f, enemy.currentHealth, 3);
            }
            UnityEngine.Time.time = 12;
            host.FlushAuthored(runtime);
            Assert.Equal(1000f - expected, enemy.currentHealth, 3);
        }

        [Theory]
        [InlineData(-1, 1d)]
        [InlineData(0, 1d)]
        [InlineData(500, 2.5d)]
        [InlineData(504, 2.512d)]
        [InlineData(int.MaxValue, 2.512d)]
        public void Host_safety_budget_still_caps_excessive_totals_and_clamps_rank_bounds(int spent, double rankFactor)
        {
            var (host, runtime, enemy) = Setup(new Build { SpentStarPoints = spent });
            float expected = (float)(Gimmicks.Cap(GimmickEffect.Wound) * rankFactor);
            float initialHealth = expected * 4f;
            enemy.currentHealth = initialHealth;
            // Deliberately exceed the final cap at the host boundary: removing the cap must fail this test.
            Apply(host, runtime, enemy, new GimmickDef { Trigger = GimmickTrigger.OnHit,
                Effect = GimmickEffect.Wound, Value = Gimmicks.Cap(GimmickEffect.Wound) * 10m, DurationPercent = 100 });
            Assert.Equal(initialHealth, enemy.currentHealth);
            UnityEngine.Time.time = 6;
            host.FlushAuthored(runtime);
            Assert.Equal(initialHealth - expected, enemy.currentHealth, 3);
            UnityEngine.Time.time = 12;
            host.FlushAuthored(runtime);
            Assert.Equal(initialHealth - expected, enemy.currentHealth, 3);
        }

        [Theory]
        [InlineData(0, 1d)]
        [InlineData(504, 2.512d)]
        public void Ordinary_payload_cap_precedes_rank_without_a_second_damage_multiplier(int spent, double rankFactor)
        {
            var build = new Build { SpentStarPoints = spent };
            float expected = (float)(Gimmicks.Cap(GimmickEffect.Wound) * rankFactor);
            var pristine = new GimmickDef { Trigger = GimmickTrigger.OnHit,
                Effect = GimmickEffect.Wound, Value = Gimmicks.Cap(GimmickEffect.Wound) * 10m, DurationPercent = 100 };
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                AuthoredKeystoneComposer.GimmickPayload(pristine), Memory);
            Assert.Equal(expected, (float)result.Value, 3);
            var (host, runtime, enemy) = Setup(build);
            float initialHealth = expected * 4f;
            enemy.currentHealth = initialHealth;
            Apply(host, runtime, enemy, AuthoredKeystoneComposer.EffectiveGimmick(pristine, result));
            Assert.Equal(initialHealth, enemy.currentHealth);
            UnityEngine.Time.time = 6;
            host.FlushAuthored(runtime);
            Assert.Equal(initialHealth - expected, enemy.currentHealth, 3);
            UnityEngine.Time.time = 12;
            host.FlushAuthored(runtime);
            Assert.Equal(initialHealth - expected, enemy.currentHealth, 3);
        }

        [Fact]
        public void Weaker_refresh_preserves_stronger_ranked_wound_and_half_second_phase()
        {
            var (build, wound) = BismuthWound(500);
            var (host, runtime, enemy) = Setup(build);
            Apply(host, runtime, enemy, wound);
            UnityEngine.Time.time = .25f;
            Apply(host, runtime, enemy, new GimmickDef { Trigger = GimmickTrigger.OnHit,
                Effect = GimmickEffect.Wound, Value = 10m, DurationPercent = 100 });
            Assert.Equal(1000f, enemy.currentHealth);
            UnityEngine.Time.time = .5f;
            host.FlushAuthored(runtime);
            Assert.Equal(981.25f, enemy.currentHealth); // One original tick, not two stacks or a shifted tick.
            UnityEngine.Time.time = 6.25f;
            host.FlushAuthored(runtime);
            Assert.Equal(775f, enemy.currentHealth, 3);
            UnityEngine.Time.time = 12;
            host.FlushAuthored(runtime);
            Assert.Equal(775f, enemy.currentHealth, 3);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(504)]
        public void Equipment_weak_point_wound_stays_unranked(int spent)
        {
            const string heroKey = "Hero_Cetus";
            var filler = new TalentDef("test.wound.filler", Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, 1)
                { HeroKey = heroKey, RankCost = Math.Max(1, spent) };
            var tree = HeroSigils.TreeFor(heroKey).Where(t => t.Cluster == null).Append(filler).ToArray();
            var profile = Profile.CreateNew(121);
            var hero = profile.Hero(heroKey);
            if (spent > 0) hero.Talents.Add(filler.Id, 1);
            var relic = new Relic { Uid = "test.wound.equipment", BaseId = Content.Bases.First(b => b.Slot == Slot.Weapon).Id,
                Rarity = Rarity.Rare, ItemLevel = 1 };
            relic.Powers.Add(new PowerLine(Power.WeakPointWound, 120));
            profile.Stash.Add(relic); hero.Equipped[(int)Slot.Weapon] = relic.Uid;
            var build = Build.Decode(Build.ComputeForTree(profile, heroKey, 0, tree).Encode());
            Assert.Equal(spent, build.SpentStarPoints);
            Assert.Equal(120, build.Get(Power.WeakPointWound));
            Assert.Empty(build.Gimmicks);
            var powers = new PowerRuntime(build, 0);
            Assert.Equal(0f, powers.TakeWeakPointWound(1, 1, 100f, true));
            Assert.Equal(0f, powers.TakeWeakPointWound(1.1f, 1, 100f, true));
            Assert.Equal(120f, powers.TakeWeakPointWound(1.2f, 1, 100f, true));
        }
    }
}
