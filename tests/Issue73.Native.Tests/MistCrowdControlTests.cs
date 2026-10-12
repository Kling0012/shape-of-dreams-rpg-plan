using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// リンクした本物の NativeMistLifetime.cs（フレッシュ追尾の対象消失）と
    /// HostAuthority.CrowdControlRecovery.cs（ホストの行動不能保険）を動かす。
    /// 境界の EntityControl/StatusEffect は NativeBoundary.cs の模倣で、時間は _tickNow で進める。
    /// </summary>
    public sealed class MistCrowdControlTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly bool _server = NetworkServer.active;
        private readonly HostAuthority _host = new HostAuthority();

        public MistCrowdControlTests()
        {
            NetworkServer.active = true;
        }

        public void Dispose()
        {
            NetworkServer.active = _server;
        }

        private static object InvokePatch(Type type, string method, params object[] args)
            => type.GetMethod(method, Hidden).Invoke(null, args);

        private static List<BasicEffect> EffectsOf(Hero hero)
            => (List<BasicEffect>)typeof(EntityStatus).GetField("_basicEffects", Hidden).GetValue(hero.Status);

        /// <summary>走査1回分。本体のホストtickと同じ意味で _tickNow を進める。</summary>
        private void Scan(float now)
        {
            typeof(HostAuthority).GetField("_tickNow", Hidden).SetValue(_host, now);
            typeof(HostAuthority).GetMethod("StageCrowdControlRecovery", Hidden).Invoke(_host, null);
        }

        private Hero MistHeroWithRuntime()
        {
            var hero = new Hero_Mist { owner = new DewPlayer { guid = "mist-cc", playerName = "mist-cc" } };
            var runtimes = (IDictionary)typeof(HostAuthority).GetField("_runtimes", Hidden).GetValue(_host);
            runtimes.Add(hero, new HostAuthority.HeroRuntime { Hero = hero });
            // _zone が null だと遷移保護で走査自体がスキップされる。
            typeof(HostAuthority).GetField("_zone", Hidden).SetValue(_host, new ZoneManager());
            return hero;
        }

        [Fact]
        public void DestroyedFlecheTargetCancelsMistDisplacementInsteadOfNativeUpdate()
        {
            var hero = new Hero_Mist();
            var control = hero.Control;
            control.entity = hero;
            control.ongoingDisplacement = new DispByTarget
            { isFriendly = true, target = new Monster { isActive = false, currentHealth = 0 } };
            // 原因: 本体 DoDisplacement は対象参照の後に elapsedTime/cancelTime を進める。
            // 修正は Prefix が false を返して対象参照の前に中断処理だけを呼ぶ。
            bool skip = (bool)InvokePatch(typeof(NativeMistDisplacementTarget), "Prefix", control);
            Assert.False(skip);
            Assert.Equal(1, control.Cancels);
            Assert.Null(control.ongoingDisplacement);
            // 対象が有効な突進は本体の更新に戻し、中断しない。
            var valid = new EntityControl { entity = hero };
            valid.ongoingDisplacement = new DispByTarget { isFriendly = true, target = new Monster() };
            Assert.True((bool)InvokePatch(typeof(NativeMistDisplacementTarget), "Prefix", valid));
            Assert.Equal(0, valid.Cancels);
        }

        [Fact]
        public void PeerCancelsInvalidTargetDisplacementLocally()
        {
            NetworkServer.active = false;
            var hero = new Hero_Mist();
            var control = hero.Control;
            control.entity = hero;
            control.ongoingDisplacement = new DispByTarget
            { isFriendly = true, target = new Monster { currentHealth = 0 } };
            bool skip = (bool)InvokePatch(typeof(NativeMistDisplacementTarget), "Prefix", control);
            Assert.False(skip);
            Assert.Equal(1, control.Cancels); // CancelOngoingDisplacementLocal 経由
            Assert.Null(control.ongoingDisplacement);
        }

        [Fact]
        public void OrphanedStunIsRecoveredOnlyAfterSixSecondsOfObservation()
        {
            var hero = MistHeroWithRuntime();
            var effects = EffectsOf(hero);
            var container = new Se_GenericEffectContainer
            { victim = hero, isActive = false, id = "Stagger", creationTime = 90f, maxDuration = 2f, remainingDuration = 0f };
            var orphan = new StunEffect { parent = container };
            effects.Add(orphan);
            Scan(101);
            Assert.True(orphan.isAlive); // 観測を始めたばかりなら解除しない
            for (float now = 102; now <= 106; now++) Scan(now);
            Assert.True(orphan.isAlive); // 6秒未満の観測では解除しない
            Scan(107);
            Assert.False(orphan.isAlive); // 6秒観測した孤立Stunだけを解除した
        }

        [Fact]
        public void NormalStunWithLivingParentAndProgressingTimerIsKept()
        {
            var hero = MistHeroWithRuntime();
            var effects = EffectsOf(hero);
            var longContainer = new Se_GenericEffectContainer
            { victim = hero, id = "DreamforgeDaze", creationTime = 100f, maxDuration = 30f, remainingDuration = 28f };
            var longStun = new StunEffect { parent = longContainer };
            var shortContainer = new Se_GenericEffectContainer
            { victim = hero, id = "Stagger", creationTime = 100f, maxDuration = 2f, remainingDuration = 1.5f };
            var shortStun = new RootEffect { parent = shortContainer };
            effects.Add(longStun);
            effects.Add(shortStun);
            float remaining = 1.5f;
            for (float now = 101; now <= 108; now++)
            {
                Scan(now);
                Assert.True(longStun.isAlive);  // 通常の長い有限スタン
                Assert.True(shortStun.isAlive); // 残り時間が進む通常の短いスタン
                longContainer.remainingDuration -= 1f;
                remaining = Math.Max(0.05f, remaining - 0.5f);
                shortContainer.remainingDuration = remaining;
            }
        }

        [Fact]
        public void HostCancelsFlecheDisplacementStuckPastInvalidTimeout()
        {
            var hero = MistHeroWithRuntime();
            var control = hero.Control;
            control.entity = hero;
            var dash = new Ai_Q_Fleche_Dash();
            control.ongoingDisplacement = new DispByTarget
            {
                isFriendly = true,
                target = new Monster(), // 対象は有効
                elapsedTime = 10f,
                cancelTime = float.NaN, // タイムアウトだけが不正
                onCancel = dash.OnCancel,
            };
            Scan(100);
            Assert.Equal(1, control.Cancels);
            Assert.Null(control.ongoingDisplacement);
        }
    }
}
