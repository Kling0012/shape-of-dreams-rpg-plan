using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class GimmickPrecisionV131Tests
    {
        private const string Memory = "St_Q_Fleche";

        private static GimmickEntry Entry(string id, GimmickEffect effect, decimal percent = 1.02m,
            float cooldown = 0f, int duration = 0) => new GimmickEntry
        {
            StarId = id, Memory = Memory,
            Def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = percent,
                Cooldown = cooldown, DurationPercent = duration }
        };


        [Fact]
        public void Cluster_boosts_compute_exact_fractional_values_including_sub_milli_results()
        {
            const string hero = "Hero_Cetus", memory = "St_D_IcyVeins", anchor = "h.cetus.route.icy-veins.4";
            var existing = HeroSigils.TreeFor(hero).Where(t => t.Cluster == null).ToArray();
            var effect = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Shield, Value = 1m };
            var cluster = new StarClusterDef
            {
                Id = "precision.cluster", HeroKey = hero, Region = ClusterRegion.Memory("h.cetus.route.icy-veins"),
                Anchor = anchor, Shape = ClusterShape.Chain,
                Stars = new[]
                {
                    new ClusterStarDef { Kind = ClusterStarKind.GimmickBoost, Memory = memory, Amount = 2,
                        Name = new Txt("増幅", "Boost") },
                    new ClusterStarDef { Kind = ClusterStarKind.Notable, Memory = memory, Gimmick = effect,
                        Name = new Txt("障壁", "Shield") }
                }
            };
            var generated = StarClusters.Generate(new[] { cluster }, existing);
            var tree = existing.Concat(generated).ToArray();
            var profile = Profile.CreateNew(31);
            profile.Hero(hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            TreeTestPaths.Connect(profile, hero, anchor);
            Rules.AddTalentRank(profile, hero, anchor);
            foreach (var star in generated) profile.Hero(hero).Talents[star.Id] = 1;
            var build = Build.ComputeForTree(profile, hero, 0, tree);
            Assert.Equal(1020, build.Gimmicks.Single(g => g.StarId == "precision.cluster.2").Def.ValueMilli);
            generated[1].Gimmick.Value = 0.001m;
            var fine = Build.Decode(Build.ComputeForTree(profile, hero, 0, tree).Encode());
            Assert.Equal(0.00102m, fine.Gimmicks.Single(g => g.StarId == "precision.cluster.2").Def.Value);
        }

        [Fact]
        public void Fixed_point_value_survives_wire_and_reaches_damage_application()
        {
            var build = new Build();
            build.Gimmicks.Add(Entry("precision.echo", GimmickEffect.Echo));
            var runtime = new GimmickRuntime();
            runtime.SetBuild(Build.Decode(build.Encode()).Gimmicks);
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0f, 1, 250f, false, requests);
            var request = Assert.Single(requests);
            Assert.Equal(1020, request.Entry.Def.ValueMilli);
            Assert.Equal(2.55f, request.Damage * request.Entry.Def.ValuePercent / 100f, 5);
            Assert.Contains("1.02%", Gimmicks.Describe(request.Entry.Def, Memory));
        }

        [Fact]
        public void Production_host_adapter_applies_fractional_heal_echo_and_cooldown_values()
        {
            UnityEngine.Time.time = 0f;
            var original = new Build();
            foreach (var effect in new[] { GimmickEffect.Heal, GimmickEffect.Echo, GimmickEffect.Recharge })
            {
                var entry = Entry("precision.native." + effect, effect);
                entry.Memory = nameof(St_Q_CruelSun);
                original.Gimmicks.Add(entry);
            }
            var decoded = Build.Decode(original.Encode());
            var hero = new Hero { currentHealth = 500f };
            var skill = new St_Q_CruelSun();
            hero.Skill.Skills[HeroSkillLocation.Q] = skill;
            var victim = new Entity { Relation = EntityRelation.Enemy };
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(decoded, 0) };
            foreach (var entry in decoded.Gimmicks)
                runtime.PendingGimmicks.Add(new HostAuthority.PendingGimmick
                {
                    Request = new GimmickRequest { Entry = entry, Damage = 250f },
                    Victim = victim
                });
            new HostAuthority().Flush(runtime);
            Assert.Equal(510.2f, hero.currentHealth, 4);
            Assert.Equal(997.45f, victim.currentHealth, 4);
            Assert.Equal(9.898f, skill.currentConfigUnscaledCooldownTime, 5);
        }

        [Theory]
        [InlineData(GimmickEffect.Quicken)]
        [InlineData(GimmickEffect.Empower)]
        [InlineData(GimmickEffect.Expose)]
        [InlineData(GimmickEffect.Sap)]
        [InlineData(GimmickEffect.Weakspot)]
        public void Fractional_windows_select_largest_and_preserve_each_stars_expiration(GimmickEffect effect)
        {
            var runtime = new GimmickRuntime();
            var strong = Entry("precision.strong", effect, 1.02m, 10f);
            var lasting = Entry("precision.lasting", effect, 1.01m, 10f, 100);
            runtime.SetBuild(new[] { strong, lasting });
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0f, 1, 100f, false, requests);
            Assert.Equal(2, requests.Count);
            Assert.Equal(1.02f, ReadWindow(runtime, effect, 0f));
            runtime.SetBuild(new[] { strong, lasting });
            Assert.Equal(1.01f, ReadWindow(runtime, effect, 4f));
            Assert.Equal(0f, ReadWindow(runtime, effect, 8f));
        }

        private static float ReadWindow(GimmickRuntime runtime, GimmickEffect effect, float now)
        {
            switch (effect)
            {
                case GimmickEffect.Quicken: return runtime.QuickenPercent(now);
                case GimmickEffect.Empower: return runtime.EmpowerPercent(now);
                case GimmickEffect.Expose: return runtime.ExposePercent(1, now);
                case GimmickEffect.Sap: return runtime.SapPercent(1, now, false);
                default: return runtime.WeakspotPercent(1, now);
            }
        }


        [Fact]
        public void Fractional_primed_damage_retains_extended_window_and_consumes_largest_only()
        {
            var build = new Build();
            build.Powers[Power.EchoingDodge] = 1;
            var runtime = new PowerRuntime(build, 0);
            runtime.OnSkillUsed(0f, true, false);
            runtime.PrimeNextBasic(0f, 1.02f, 20f);
            var first = runtime.OnAttackHit(1f, 1000f, 100f, 100f, 1);
            Assert.Equal(1.02f, first.PrimedDamage, 5);
            Assert.Equal(0f, first.EchoDamage);
            Assert.Equal(1f, runtime.OnAttackHit(2f, 1000f, 100f, 100f, 1).EchoDamage);
            runtime.PrimeNextBasic(3f, 1.02f, 20f);
            Assert.Equal(1.02f, runtime.OnAttackHit(22f, 1000f, 100f, 100f, 1).PrimedDamage, 5);
        }

        [Fact]
        public void Runtime_rejects_oversize_atomically_and_keeps_previous_star_cooldown()
        {
            var runtime = new GimmickRuntime();
            var retained = Entry("precision.retained", GimmickEffect.Echo, cooldown: 2f);
            runtime.SetBuild(new[] { retained });
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0f, 1, 100f, false, requests);
            var oversized = Enumerable.Range(0, BuildLimits.MaxGimmickEntries + 1)
                .Select(i => Entry("precision.oversize." + i, GimmickEffect.Echo)).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.SetBuild(oversized));
            Assert.Throws<ArgumentException>(() => runtime.SetBuild(new[] { retained, retained }));
            Assert.Throws<ArgumentException>(() => runtime.SetBuild(new[] { retained, null }));
            requests.Clear();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 1f, 1, 100f, false, requests);
            Assert.Empty(requests);
            runtime.Fire(GimmickTrigger.OnHit, Memory, 2f, 1, 100f, false, requests);
            Assert.Equal(retained.StarId, Assert.Single(requests).Entry.StarId);
        }
    }
}
