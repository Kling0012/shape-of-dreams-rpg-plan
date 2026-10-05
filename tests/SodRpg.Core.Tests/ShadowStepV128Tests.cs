using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class ShadowStepV128Tests
    {
        private static Build With(params (Power power, int value)[] powers)
        {
            var build = new Build();
            foreach (var (power, value) in powers) build.Powers[power] = value;
            return build;
        }

        [Theory]
        [InlineData(100f, 200f)]
        [InlineData(200f, 100f)]
        public void Movement_empowers_one_basic_hit_using_the_higher_offensive_stat(float attack, float power)
        {
            var runtime = new PowerRuntime(With((Power.ShadowStep, 60)), 0);
            runtime.OnSelfMovement(10);
            runtime.OnAttackFired(false); // Firing or missing does not consume the next hit.
            Assert.Equal(120f, runtime.OnAttackHit(11, 500, attack, power, 1).ShadowStepDamage);
            Assert.Equal(0f, runtime.OnAttackHit(11, 500, attack, power, 1).ShadowStepDamage);
            runtime.OnSelfMovement(12);
            Assert.Equal(120f, runtime.OnAttackHit(12, 500, attack, power, 1).ShadowStepDamage);
        }

        [Fact]
        public void No_movement_or_nonmovement_skills_cannot_arm_the_bonus()
        {
            var runtime = new PowerRuntime(With((Power.ShadowStep, 60)), 0);
            Assert.Equal(0f, runtime.OnAttackHit(0, 500, 100, 200, 1).ShadowStepDamage);
            runtime.OnSkillUsed(1, isMovement: false, isUltimate: false);
            Assert.Equal(0f, runtime.OnAttackHit(1, 500, 100, 200, 1).ShadowStepDamage);
            runtime.OnSkillUsed(2, isMovement: false, isUltimate: true);
            Assert.Equal(0f, runtime.OnAttackHit(2, 500, 100, 200, 1).ShadowStepDamage);
        }

        [Fact]
        public void Movement_without_the_power_does_not_save_a_charge_for_later()
        {
            var build = new Build();
            var runtime = new PowerRuntime(build, 0);
            runtime.OnSelfMovement(10);
            Assert.Equal(0f, runtime.OnAttackHit(10, 500, 100, 200, 1).ShadowStepDamage);
            build.Powers[Power.ShadowStep] = 60;
            Assert.Equal(0f, runtime.OnAttackHit(11, 500, 100, 200, 1).ShadowStepDamage);
        }

        [Theory]
        [InlineData(12.999f, 120f)]
        [InlineData(13f, 0f)]
        [InlineData(13.001f, 0f)]
        public void The_three_second_expiry_boundary_is_exclusive(float hitTime, float expected)
        {
            var runtime = new PowerRuntime(With((Power.ShadowStep, 60)), 0);
            runtime.OnSelfMovement(10);
            Assert.Equal(expected, runtime.OnAttackHit(hitTime, 500, 100, 200, 1).ShadowStepDamage);
        }

        [Fact]
        public void Repeated_movement_refreshes_without_stacking_and_skills_do_not_consume_it()
        {
            var runtime = new PowerRuntime(With((Power.ShadowStep, 60)), 0);
            runtime.OnSelfMovement(10);
            runtime.OnSelfMovement(12);
            runtime.OnSkillUsed(13, isMovement: false, isUltimate: false);
            Assert.Equal(120f, runtime.OnAttackHit(14.5f, 500, 100, 200, 1).ShadowStepDamage);
            Assert.Equal(0f, runtime.OnAttackHit(14.6f, 500, 100, 200, 1).ShadowStepDamage);
        }

        [Fact]
        public void Self_movement_does_not_arm_other_movement_or_skill_powers()
        {
            var runtime = new PowerRuntime(With((Power.ShadowStep, 60), (Power.EchoingDodge, 80),
                (Power.Sprint, 25), (Power.Whirlwind, 70), (Power.PerfectRead, 20),
                (Power.UltimateSurge, 20), (Power.Overload, 20)), 0);
            runtime.OnSelfMovement(10);
            var bonus = runtime.Current(11);
            Assert.Equal(0, bonus.AttackSpeedPct);
            Assert.Equal(0, bonus.MoveSpeedPct);
            Assert.Equal(0, bonus.AttackPct);
            Assert.Equal(0, bonus.PowerPct);
            var hit = runtime.OnAttackHit(11, 500, 100, 200, 1);
            Assert.Equal(120f, hit.ShadowStepDamage);
            Assert.Equal(0f, hit.EchoDamage);
            // Self movement must not spend Whirlwind's cooldown either.
            Assert.Equal(140f, runtime.OnSkillUsed(11, true, false, 200, 500).WhirlwindDamage);
        }

        [Fact]
        public void Movement_skills_arm_both_powers_and_each_bonus_is_consumed_once()
        {
            var runtime = new PowerRuntime(With((Power.ShadowStep, 60), (Power.EchoingDodge, 80)), 0);
            runtime.OnSkillUsed(10, isMovement: true, isUltimate: false);
            var hit = runtime.OnAttackHit(11, 500, 100, 200, 1);
            Assert.Equal(0f, hit.ShadowStepDamage);
            Assert.Equal(160f, hit.EchoDamage);
            var next = runtime.OnAttackHit(11.1f, 500, 100, 200, 1);
            Assert.Equal(120f, next.ShadowStepDamage);
            Assert.Equal(0f, next.EchoDamage);
        }

        [Fact]
        public void Self_movement_refreshes_only_shadow_step_not_echoing_dodge()
        {
            var runtime = new PowerRuntime(With((Power.ShadowStep, 60), (Power.EchoingDodge, 80)), 0);
            runtime.OnSkillUsed(10, isMovement: true, isUltimate: false);
            runtime.OnSelfMovement(12);
            var hit = runtime.OnAttackHit(13, 500, 100, 200, 1);
            Assert.Equal(120f, hit.ShadowStepDamage);
            Assert.Equal(0f, hit.EchoDamage);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(60, 60)]
        [InlineData(150, 150)]
        [InlineData(200, 200)]
        [InlineData(375, 375)]
        [InlineData(376, 375)]
        public void Wire_id_is_appended_and_received_values_are_capped(int value, int expected)
        {
            var decoded = Build.Decode("s:;p:6=40,21=80,42=18,43=" + value + ";h:0");
            Assert.NotNull(decoded);
            Assert.Equal(40, decoded.Get(Power.Executioner));
            Assert.Equal(80, decoded.Get(Power.EchoingDodge));
            Assert.Equal(18, decoded.Get(Power.LucidBoon));
            Assert.Equal(expected, decoded.Get(Power.ShadowStep));
            Assert.Equal(expected, Build.Decode(decoded.Encode()).Get(Power.ShadowStep));
        }

        [Fact]
        public void Existing_husk_keystone_id_grants_shadow_step_and_switching_removes_it()
        {
            const string hero = "Hero_Husk";
            var profile = Profile.CreateNew(128);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(19);
            state.Kills = 600;
            foreach (var node in HeroSigils.TreeFor(hero).Where(t => t.Tier == 1 && !t.IsKeystone))
                for (int rank = 0; rank < node.MaxRank; rank++) Rules.AddTalentRank(profile, hero, node.Id);
            Rules.SetKeystone(profile, hero, "h.husk.key2");
            var build = Build.Compute(profile, hero, 0);
            Assert.Equal(69, build.Get(Power.ShadowStep));
            Assert.Equal(0, build.Get(Power.Executioner));
            Assert.True(Rules.FreePoints(profile, hero) >= 0);
            Rules.RemoveKeystone(profile, hero, "h.husk.key2");
            Rules.SetKeystone(profile, hero, "h.husk.key");
            var alternate = Build.Compute(profile, hero, 0);
            Assert.Equal(0, alternate.Get(Power.ShadowStep));
            Assert.Equal(115, alternate.Get(Power.Umbra));
        }

        [Fact]
        public void Shadow_step_is_exclusive_to_the_husk_keystone_not_shared_loot_or_talents()
        {
            var source = Assert.Single(HeroSigils.All, t => t.Power == Power.ShadowStep || t.RankPower == Power.ShadowStep);
            Assert.Equal("Hero_Husk", source.HeroKey);
            Assert.Equal("h.husk.key2", source.Id);
            Assert.True(source.IsKeystone);
            Assert.Equal(69, source.PowerValue);
            Assert.Equal(150, Content.PowerCap(Power.ShadowStep));
            Assert.DoesNotContain(Content.Talents, t => t.Power == Power.ShadowStep || t.RankPower == Power.ShadowStep);
            Assert.DoesNotContain(Content.SlotOrder.SelectMany(Content.PowerPool), p => p.Power == Power.ShadowStep);
            Assert.DoesNotContain(Content.Uniques.SelectMany(u => u.Powers), p => p.Power == Power.ShadowStep);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Localized_tooltips_explain_the_trigger_window_scaling_and_refresh(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                var key = HeroSigils.All.Single(t => t.Id == "h.husk.key2");
                var terms = japanese
                    ? new[] { "回避", "ダッシュ", "瞬間移動", "3秒", "次の通常攻撃", "高い方", "69%", "重ならず", "延長" }
                    : new[] { "dodge", "dash", "teleport", "3s", "next basic attack", "higher", "AD", "AP", "69%", "does not stack", "refresh" };
                foreach (string description in new[] { Content.FormatPower(Power.ShadowStep, key.PowerValue), key.Description.ToString() })
                    foreach (string term in terms) Assert.Contains(term, description);
                string synergy = key.Description.ToString();
                Assert.Contains(japanese ? "一歩一殺" : "One Step, One Kill", synergy);
                Assert.Contains(japanese ? "確定会心" : "guaranteed critical hit", synergy);
                Assert.Contains(japanese ? "風の傷" : "Scar of the Wind", synergy);
            }
            finally { Loc.Japanese = previous; }
        }
    }
}
