using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 衰弱・弱点の問い合わせは、ホストで敵の攻撃・味方の攻撃のたびに呼ばれる。
    /// 状態の一覧を IReadOnlyList で回すと列挙子の箱詰めで毎回確保していたので、確保が出ないことを確かめる。
    /// </summary>
    public sealed class GimmickQueryAllocationTests
    {
        [Fact]
        public void Sap_and_weakspot_queries_do_not_allocate()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[]
            {
                new GimmickEntry { StarId = "h.mist.v129.Sap", Memory = "St_Q_Fleche", Def = new GimmickDef { Effect = GimmickEffect.Sap, Value = 1000, Trigger = GimmickTrigger.OnHit } },
                new GimmickEntry { StarId = "h.mist.v129.Weakspot", Memory = "St_Q_Fleche", Def = new GimmickDef { Effect = GimmickEffect.Weakspot, Value = 1000, Trigger = GimmickTrigger.OnHit } },
            });
            var requests = new List<GimmickRequest>(16);
            runtime.Fire(GimmickTrigger.OnHit, "St_Q_Fleche", 1f, 7, 100, false, requests, activationId: 1);
            float sap = 0, weak = 0;
            for (int i = 0; i < 100; i++) { sap += runtime.SapPercent(7, 1.5f, false); weak += runtime.WeakspotPercent(7, 1.5f); }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { sap += runtime.SapPercent(7, 1.5f, false); weak += runtime.WeakspotPercent(7, 1.5f); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(sap > 0 && weak > 0, "the windows must be active so the loops actually run");
            Assert.Equal(0, allocated);
        }
    }
}
