using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    // HostAuthority.GimmicksV129.cs の TryWeakspotBasic 軽量化（付与率が0のときの早期リターン）の等価性。
    // 旧ロジック「bonus > 0 && NextDouble() < 追加会心率」と、乱数を引く回数と会心の値が一致することを固定する。
    public sealed class WeakspotBasicRngEquivalenceTests
    {
        private sealed class CountingRandom : Random
        {
            public int Draws;
            public double Last;
            public CountingRandom(int seed) : base(seed) { }
            public override double NextDouble() { Draws++; Last = base.NextDouble(); return Last; }
        }

        private static GimmickEntry Entry(GimmickEffect effect) => new GimmickEntry
        {
            StarId = "h.mist.v129." + effect,
            Memory = "St_Q_Fleche",
            Def = new GimmickDef { Effect = effect, Value = 1000, Trigger = GimmickTrigger.OnHit, Arg = 0 }
        };

        private static CountingRandom ArmRng(HostAuthority host)
        {
            var rng = new CountingRandom(1);
            typeof(HostAuthority).GetField("_rng", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(host, rng);
            return rng;
        }

        [Fact]
        public void ZeroBonusSkipsTheRngDrawAndKeepsCriticalFalse()
        {
            var host = new HostAuthority();
            var hero = new Hero();
            host.Track(new HostAuthority.HeroRuntime { Hero = hero });
            var rng = ArmRng(host);
            UnityEngine.Time.time = 1f;
            var victim = new Entity { Relation = EntityRelation.Enemy }; // Weakspot の窓がない相手（付与率0）
            bool critical = false;
            host.TryWeakspotBasic(hero, victim, ref critical);
            Assert.Equal(0, rng.Draws); // 旧ロジックの短絡と同じく乱数を引かない
            Assert.False(critical);
        }

        [Fact]
        public void ArmedBonusDrawsTheRngOnceAndSetsCriticalByTheAddedProbability()
        {
            var host = new HostAuthority();
            var hero = new Hero();
            var runtime = new HostAuthority.HeroRuntime { Hero = hero };
            host.Track(runtime);
            var rng = ArmRng(host);
            UnityEngine.Time.time = 1f;
            var victim = new Entity { Relation = EntityRelation.Enemy };
            runtime.Gimmicks.SetBuild(new[] { Entry(GimmickEffect.Weakspot) });
            runtime.Gimmicks.Fire(GimmickTrigger.OnHit, "St_Q_Fleche", UnityEngine.Time.time,
                victim.GetInstanceID(), 100, false, new List<GimmickRequest>());
            Assert.Equal(25f, runtime.Gimmicks.WeakspotPercent(victim.GetInstanceID(), UnityEngine.Time.time));
            bool critical = false;
            host.TryWeakspotBasic(hero, victim, ref critical);
            Assert.Equal(1, rng.Draws); // 付与率があるときは旧ロジックと同じくちょうど1回引く
            float added = Gimmicks.AddedCritProbability(0f, 25f); // BasicCritChanceV129 の境界値は0
            Assert.Equal(rng.Last < added, critical);
        }
    }
}
