using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    // ヒントの数値は実際の値から作る（初期の遺物は枠の数だけ配られる）。
    public sealed class OnboardingNumberTests
    {
        [Fact]
        public void Welcome_names_the_number_of_starter_relics_actually_granted()
        {
            int granted = Onboarding.GrantStarterKit(Profile.CreateNew(140)).Count;
            var body = Onboarding.Get(Hint.Welcome).Body;
            Assert.Contains($"最初の遺物を{granted}つ", body.Ja);
            Assert.Contains($"You get {granted} starter relics", body.En);
        }

        [Fact]
        public void Star_point_and_keystone_hints_use_the_shared_constants_in_both_languages()
        {
            var talent = Onboarding.Get(Hint.TalentPoints).Body;
            Assert.Contains($"最大{StarProgression.MaxPoints}ポイント", talent.Ja);
            Assert.Contains($"up to {StarProgression.MaxPoints} points", talent.En);
            var keystone = Onboarding.Get(Hint.KeystoneReady).Body;
            Assert.Contains($"{Content.KeystoneRouteRequirement}ポイント以上", keystone.Ja);
            Assert.Contains($"{Content.KeystoneRouteRequirement}+ points", keystone.En);
        }
    }
}
