using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #161: 命中ごとの Expose/Sap/Weakspot/Crescendo 参照は、効果を1つも持たないビルドでは
    /// 全状態走査なしで 0 を返す。フラグは SetBuild のたびに作り直すので、後から効果を足した
    /// ビルドは必ず値を返し、外したビルドは再び 0 に戻る（効果量は変わらない）。
    /// </summary>
    public class HotPathQueryFlagTests
    {
        private const string Memory = "St_Q_Fleche";

        private static GimmickEntry Entry(string star, GimmickEffect effect, int value = 20) =>
            new GimmickEntry { StarId = star, Memory = Memory, Def = new GimmickDef
                { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = value } };

        private static List<GimmickRequest> Results() => new List<GimmickRequest>();

        [Fact]
        public void Gimmick_effect_queries_return_zero_without_entries_and_values_after_rebuild()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry("s.burst", GimmickEffect.Burst) });
            Assert.Equal(0f, runtime.ExposePercent(1, 10f));
            Assert.Equal(0f, runtime.WeakspotPercent(1, 10f));
            Assert.Equal(0f, runtime.SapPercent(1, 10f, boss: false));
            Assert.Equal(0f, runtime.CrescendoPercent(Memory, 10f));

            // 効果を持つ星に入れ替えると、同じ問い合わせが値を返す（早期リターンが誤って 0 を返さない）。
            runtime.SetBuild(new[] { Entry("s.expose", GimmickEffect.Expose, 15) });
            runtime.Fire(GimmickTrigger.OnHit, Memory, 10f, 1, 100f, false, Results());
            Assert.Equal(15f, runtime.ExposePercent(1, 11f));

            // 外すと再び 0。
            runtime.SetBuild(new[] { Entry("s.burst", GimmickEffect.Burst) });
            Assert.Equal(0f, runtime.ExposePercent(1, 12f));
        }

        [Fact]
        public void Pair_expose_returns_zero_without_mark_pairs_and_marks_after_rebuild()
        {
            var runtime = new PairComboRuntime();
            runtime.SetBuild(new[] { new PairComboEntry { Def = PairCombos.Get("h.husk.pair.3"), Ranks = 1 } });
            Assert.Equal(0, runtime.ExposePercent(1, 10f));

            runtime.SetBuild(new[] { new PairComboEntry { Def = PairCombos.Get("h.husk.pair.1"), Ranks = 2 } });
            runtime.Fire(PairComboTrigger.OnHit, "St_Q_Laceration", 10f, 1, 100f, false,
                new HashSet<string> { "St_Q_Laceration", "St_D_TheKillingFlow" }, false, Results());
            Assert.Equal(3, runtime.ExposePercent(1, 11f));
            Assert.Equal(0, runtime.ExposePercent(2, 11f));
        }
    }
}
