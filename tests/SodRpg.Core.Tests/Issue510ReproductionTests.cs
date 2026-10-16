using System;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    // Issue #510 の再現（PackMend が発動のたびに一時リストを作る）。HostAuthority.GimmicksV129.cs は
    // リンクした製品コードをそのまま使い、通常攻撃で発動するのと同じPendingGimmick のディスパッチを
    // Flush で走らせる。修正後はスクラッチリストの再利用により、温まった後の1回の発動は
    // 管理ヒープを一切確保しないはずである（Issue 本文: 召喚ビルドでは通常攻撃のたびに確保が起きる）。
    public class Issue510ReproductionTests
    {
        private static GimmickEntry PackMendEntry()
        {
            var build = new Build();
            build.Gimmicks.Add(new GimmickEntry
            {
                StarId = "issue510.packmend",
                Memory = nameof(St_Q_CruelSun),
                Def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.PackMend, Value = 10m }
            });
            return Build.Decode(build.Encode()).Gimmicks[0];
        }

        [Fact]
        public void PackMend_trigger_does_not_allocate_a_fresh_summon_list_each_dispatch()
        {
            UnityEngine.Time.time = 0f;
            var entry = PackMendEntry();
            var host = new HostAuthority();
            var hero = new Hero();
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(PowerBuild(entry), 0) };
            var summon = new Summon { Owner = hero, currentHealth = 500f };
            host.RegisterEntity(summon);

            // 温存（JIT・GimmickHostState・初回確保を済ませる）: 1発目で実際に召喚獣が回復することも確認する。
            summon.currentHealth = 500f;
            FirePackMend(host, runtime, entry);
            Assert.True(summon.currentHealth > 500f, "PackMend が召喚獣を回復したこと（ディスパッチ経路が動いた証拠）: " + summon.currentHealth);

            // 計測する2発目。発動のたびに new List<Summon>() とその配列を確保している現状では
            // 確保量が 0 にならず、この検証は失敗する。
            summon.currentHealth = 500f;
            runtime.PendingGimmicks.Add(new HostAuthority.PendingGimmick
            { Request = new GimmickRequest { Entry = entry, Damage = 100f } });
            long before = GC.GetAllocatedBytesForCurrentThread();
            host.Flush(runtime);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(summon.currentHealth > 500f, "計測した発動でも PackMend が動いたこと: " + summon.currentHealth);
            Assert.Equal(0, allocated);
        }

        private static Build PowerBuild(GimmickEntry entry)
        {
            var build = new Build();
            build.Gimmicks.Add(entry);
            return Build.Decode(build.Encode());
        }

        private static void FirePackMend(HostAuthority host, HostAuthority.HeroRuntime runtime, GimmickEntry entry)
        {
            runtime.PendingGimmicks.Add(new HostAuthority.PendingGimmick
            { Request = new GimmickRequest { Entry = entry, Damage = 100f } });
            host.Flush(runtime);
        }
    }
}
