using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod
{
    // Notification doubles exercise the production adapter's reflection/subscription boundary.
    // They do not assert that these events exist or mark the charging interval in the game DLL.
    // The native side exposes cast start as a SafeAction field (not a C# event) and has no
    // cast-cancel notification: an interruption is only visible as Network_isCasting turning false.
    internal class SafeAction<T>
    {
        private readonly List<Action<T>> _handlers = new List<Action<T>>();
        internal bool RejectAdd, RejectRemove;
        public int Count => _handlers.Count;
        public SafeAction<T> Add(Action<T> handler)
        { if (RejectAdd) throw new NotSupportedException("start unavailable"); if (handler != null) _handlers.Add(handler); return this; }
        public SafeAction<T> Remove(Action<T> handler)
        { if (RejectRemove) throw new NotSupportedException("start removal unavailable"); if (handler != null) _handlers.Remove(handler); return this; }
        public void Invoke(T arg) { foreach (var handler in _handlers.ToArray()) handler?.Invoke(arg); }
    }

    internal partial class Entity
    {
        public SafeAction<EventInfoCast> EntityEvent_OnCastStart = new SafeAction<EventInfoCast>();
        internal int BackOffStartSubscribers => EntityEvent_OnCastStart.Count;
        internal void StartBackOff(Actor trigger)
        {
            if (trigger is SkillTrigger skill) skill.Network_isCasting = true;
            EntityEvent_OnCastStart.Invoke(new EventInfoCast { trigger = trigger });
        }
    }

    internal partial class SkillTrigger { public bool Network_isCasting; }

    internal partial class Hero
    {
        internal int CastCompleteSubscribers => _castCompleteBeforePrepare?.GetInvocationList().Length ?? 0;
        internal void CompleteBackOff(Actor trigger) => _castCompleteBeforePrepare?.Invoke(new EventInfoCast { trigger = trigger });
    }

    internal sealed class St_R_BackOff : SkillTrigger { }

    internal sealed partial class HostAuthority
    {
        internal void InitializeBackOffForTest(HeroRuntime runtime) => InitializeBackOffCharge(runtime);
        internal void TickBackOffForTest(float now) { UnityEngine.Time.time = now; StageBackOffCharge(); }
        internal void ClearBackOffForTest() => ClearBackOffCharge();
        internal void UnhookBackOffForTest(HeroRuntime runtime) => UnhookBackOffCharge(runtime);
        internal int BackOffOwnersForTest => _backOffCharge.Count;
        internal BackOffChargeRuntime BackOffRuntimeForTest(HeroRuntime runtime) => _backOffCharge[runtime].Runtime;
    }
}

namespace SodRpg.Core.Tests
{
    public sealed class BackOffChargeNativeTests
    {
        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime, Hero Hero, SkillTrigger Skill, Entity Enemy)
            Setup(bool rejectStart = false, bool rejectComplete = false, int spent = 0)
        {
            NetworkServer.active = true;
            UnityEngine.Time.time = 0;
            DewPhysics.Entities.Clear();
            Actor.SimDamageLog.Clear();
            Actor.SimDealerLog.Clear();
            var host = new HostAuthority();
            HostAuthority.NativeInstance = host;
            var hero = new Hero_Cetus();
            if (rejectStart) hero.EntityEvent_OnCastStart.RejectAdd = true;
            hero.RejectCastCompleteSubscription = rejectComplete;
            var skill = new St_R_BackOff { owner = hero, parentActor = hero };
            hero.Skill.Skills[HeroSkillLocation.R] = skill;
            var runtime = new HostAuthority.HeroRuntime
                { Hero = hero, HeroKey = "Hero_Cetus", Powers = new PowerRuntime(new Build { SpentStarPoints = spent }, 0) };
            host.Track(runtime);
            host.InitializeBackOffForTest(runtime);
            var enemy = new Entity { Relation = EntityRelation.Enemy };
            DewPhysics.Entities.Add(enemy);
            return (host, runtime, hero, skill, enemy);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void An_unavailable_notification_disables_charge_damage_and_rolls_back_subscriptions(bool rejectStart, bool rejectComplete)
        {
            var (host, runtime, hero, skill, enemy) = Setup(rejectStart, rejectComplete);
            hero.StartBackOff(skill);
            host.TickBackOffForTest(.5f);
            Assert.Equal(1000f, enemy.currentHealth);
            Assert.Empty(Actor.SimDamageLog);
            Assert.Equal(0, host.BackOffOwnersForTest);
            Assert.Equal(0, hero.BackOffStartSubscribers);
            Assert.Equal(0, hero.CastCompleteSubscribers);
        }

        [Fact]
        public void Failed_start_removal_does_not_skip_complete_cleanup_or_reenable_an_unhooked_charge()
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            var clock = host.BackOffRuntimeForTest(runtime);
            hero.StartBackOff(skill);
            hero.EntityEvent_OnCastStart.RejectRemove = true;
            host.UnhookBackOffForTest(runtime);
            Assert.Equal(0, hero.CastCompleteSubscribers);
            hero.StartBackOff(skill); // A stale native delegate must remain inert if removal fails.
            Assert.False(clock.Charging);
            host.TickBackOffForTest(.5f);
            Assert.Equal(1000f, enemy.currentHealth);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Completion_and_casting_interruption_stop_subsequent_damage(bool interrupt)
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            hero.StartBackOff(skill);
            host.TickBackOffForTest(.5f);
            Assert.Equal(960f, enemy.currentHealth);
            if (interrupt)
            {
                skill.Network_isCasting = false;
                Assert.True(host.BackOffRuntimeForTest(runtime).Charging); // Stops on the next tick, not synchronously.
            }
            else hero.CompleteBackOff(skill);
            host.TickBackOffForTest(1f);
            Assert.Equal(960f, enemy.currentHealth);
            Assert.False(host.BackOffRuntimeForTest(runtime).Charging);
        }

        [Fact]
        public void Initialization_is_idempotent_and_unhook_removes_every_subscription()
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            host.InitializeBackOffForTest(runtime);
            Assert.Equal(1, hero.BackOffStartSubscribers);
            Assert.Equal(1, hero.CastCompleteSubscribers);
            hero.StartBackOff(skill);
            host.UnhookBackOffForTest(runtime);
            host.UnhookBackOffForTest(runtime);
            Assert.Equal(0, host.BackOffOwnersForTest);
            Assert.Equal(0, hero.BackOffStartSubscribers);
            Assert.Equal(0, hero.CastCompleteSubscribers);
            hero.StartBackOff(skill);
            host.TickBackOffForTest(.5f);
            Assert.Equal(1000f, enemy.currentHealth);
        }

        [Fact]
        public void Cast_start_subscription_actually_connects_and_damage_lands_during_the_charge()
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            // The handler must sit inside the reflected SafeAction field, not a C# event that never fires.
            Assert.Equal(1, hero.BackOffStartSubscribers);
            Assert.Equal(1, host.BackOffOwnersForTest);
            hero.StartBackOff(skill);
            Assert.True(host.BackOffRuntimeForTest(runtime).Charging);
            host.TickBackOffForTest(.5f);
            Assert.Equal(960f, enemy.currentHealth);
            Assert.NotEmpty(Actor.SimDamageLog);
        }

        [Fact]
        public void Client_and_foreign_trigger_notifications_cannot_start_damage()
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            NetworkServer.active = false;
            hero.StartBackOff(skill);
            host.TickBackOffForTest(.5f);
            NetworkServer.active = true;
            hero.StartBackOff(new St_R_BackOff { owner = hero, parentActor = hero });
            host.TickBackOffForTest(1f);
            Assert.Equal(1000f, enemy.currentHealth);
            Assert.False(host.BackOffRuntimeForTest(runtime).Charging);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Replacing_the_memory_instance_cannot_continue_its_previous_charge(bool finishOld)
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            hero.StartBackOff(skill);
            hero.Skill.Skills[HeroSkillLocation.R] = new St_R_BackOff { owner = hero, parentActor = hero };
            // A completion can arrive after the equipment callback, or not at all.
            if (finishOld) hero.CompleteBackOff(skill);
            host.TickBackOffForTest(.5f);
            Assert.Equal(1000f, enemy.currentHealth);
            Assert.False(host.BackOffRuntimeForTest(runtime).Charging);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_replaced_skills_completion_or_interrupt_does_not_stop_a_new_instances_charge(bool interruptOld)
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            hero.StartBackOff(skill);
            var replacement = new St_R_BackOff { owner = hero, parentActor = hero };
            hero.Skill.Skills[HeroSkillLocation.R] = replacement;
            hero.StartBackOff(replacement);
            if (interruptOld) skill.Network_isCasting = false; else hero.CompleteBackOff(skill);
            host.TickBackOffForTest(.5f);
            Assert.Equal(960f, enemy.currentHealth);
            Assert.True(host.BackOffRuntimeForTest(runtime).Charging);
        }

        [Theory]
        [InlineData("stun")]
        [InlineData("inactive")]
        [InlineData("unequip")]
        [InlineData("zone")]
        public void Invalidated_charge_stops_before_the_next_pulse(string reason)
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            hero.StartBackOff(skill);
            if (reason == "stun") hero.Status.hasStun = true;
            if (reason == "inactive") hero.isActive = false;
            if (reason == "unequip") hero.Skill.Skills.Remove(HeroSkillLocation.R);
            if (reason == "zone") host.ClearBackOffForTest();
            host.TickBackOffForTest(.5f);
            Assert.Equal(1000f, enemy.currentHealth);
            Assert.False(host.BackOffRuntimeForTest(runtime).Charging);
        }

        [Fact]
        public void Damage_keeps_rank_scaling_generated_scope_dot_marker_and_target_limits()
        {
            var (host, runtime, hero, skill, enemy) = Setup(spent: 500);
            hero.Status.abilityPower = 200f;
            for (int i = 0; i < 12; i++) DewPhysics.Entities.Add(new Entity { Relation = EntityRelation.Enemy });
            var ally = new Entity { Relation = EntityRelation.Ally };
            var far = new Entity { Relation = EntityRelation.Enemy, position = new UnityEngine.Vector3(5f, 0, 0) };
            DewPhysics.Entities.Add(ally); DewPhysics.Entities.Add(far);
            int observed = 0;
            hero.SimDealtDamage.Add((ref DamageData damage, Actor actor, Entity target) =>
            {
                observed++;
                Assert.Equal(1, host.GeneratedDamageDepth);
                Assert.True(damage.HasAttr(DamageAttribute.DamageOverTime));
                Assert.True(damage.IsAmountModifiedBy(typeof(GimmickRuntime)));
                Assert.Null(damage.Elemental);
            });
            hero.StartBackOff(skill);
            host.TickBackOffForTest(.5f);
            Assert.Equal(BackOffChargeRuntime.MaxTargets, observed);
            Assert.All(Actor.SimDamageLog, packet => Assert.Equal(200f, packet.Amount, 3));
            Assert.Equal(1000f, ally.currentHealth);
            Assert.Equal(1000f, far.currentHealth);
            Assert.Equal(0, host.GeneratedDamageDepth);
        }

        [Fact]
        public void Generated_scope_is_released_when_native_damage_dispatch_throws()
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            hero.SimDealtDamage.Add((ref DamageData damage, Actor actor, Entity target) => throw new InvalidOperationException("native dispatch"));
            hero.StartBackOff(skill);
            Assert.Throws<InvalidOperationException>(() => host.TickBackOffForTest(.5f));
            Assert.Equal(0, host.GeneratedDamageDepth);
        }

        [Fact]
        public void Interrupting_and_unhooking_one_hero_preserves_another_heroes_charge()
        {
            var (host, runtime, hero, skill, enemy) = Setup();
            var other = new Hero_Cetus { position = new UnityEngine.Vector3(10f, 0, 0) };
            var otherSkill = new St_R_BackOff { owner = other, parentActor = other };
            other.Skill.Skills[HeroSkillLocation.R] = otherSkill;
            var otherRuntime = new HostAuthority.HeroRuntime { Hero = other, Powers = new PowerRuntime(new Build(), 0) };
            host.Track(otherRuntime);
            host.InitializeBackOffForTest(otherRuntime);
            var otherEnemy = new Entity { Relation = EntityRelation.Enemy, position = other.position };
            DewPhysics.Entities.Add(otherEnemy);
            hero.StartBackOff(skill);
            other.StartBackOff(otherSkill);
            skill.Network_isCasting = false;
            host.TickBackOffForTest(.5f);
            Assert.Equal(1000f, enemy.currentHealth);
            Assert.Equal(960f, otherEnemy.currentHealth);
            host.UnhookBackOffForTest(runtime);
            host.TickBackOffForTest(1f);
            Assert.Equal(920f, otherEnemy.currentHealth);
            Assert.Equal(1, host.BackOffOwnersForTest);
        }
    }
}
