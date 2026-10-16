using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>説明文と Mod の動作が同じ定数を使うこと（吸収の上限・傷・準備・跳弾・味方の範囲）。</summary>
    public sealed class GimmickSharedConstantTests
    {
        [Fact]
        public void Siphon_description_and_heal_use_the_same_cap()
        {
            bool japanese = Loc.Japanese;
            try
            {
                var def = new GimmickDef { Effect = GimmickEffect.Siphon, Value = 1000, Trigger = GimmickTrigger.OnHit, Arg = 1 };
                Loc.Japanese = true;
                Assert.Contains("最大HPの1.5%まで", Gimmicks.Describe(def, "St_Q_Fleche"));
                Loc.Japanese = false;
                string en = Gimmicks.Describe(def, "St_Q_Fleche");
                Assert.Contains("at most 1.5% of your maximum health", en);
                Assert.Contains("capped at 1.5% of their own maximum health", en);
                Assert.Equal(15f, Gimmicks.SiphonHeal(100000f, 1000f, 50f), 3);
            }
            finally { Loc.Japanese = japanese; }
        }
    }
}
