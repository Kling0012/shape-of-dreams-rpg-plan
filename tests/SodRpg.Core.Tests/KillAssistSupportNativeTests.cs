using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod
{
    // Boundary stubs for HostAuthority.KillAssists/SupportOverheal/RunGrowth (see UnbowedNativeTests
    // and AuthoredMechanismNativeApi for the convention: only the API surface the mod files call).
    internal sealed class GameSettingsManager { public int maxPlayers = 4; }
    internal sealed class NativeDamageContext { internal static NativeDamageContext Current; internal Entity Target; internal float Health, Shield; }
    internal sealed class Se_R_Parry_End : AbilityInstance { public Entity victim; public float blockedDamage; }
    internal partial struct EventInfoShield { public StatusEffect statusEffect; public Entity target; public float finalAmount; }
    internal partial struct EventInfoStatusEffect { public StatusEffect effect; public Entity victim; }
    internal partial struct EventInfoDamage { public float negatedAmountByShield; }
    internal partial struct EventInfoAttackHit { public Actor actor; }
    internal partial class BasicAttackContext { public Hero From; public Entity Target; }

    internal partial class DewPlayer { public uint netId; }
    internal partial class StatusEffect
    {
        public bool isBeneficialBuff;
        internal static readonly BasicEffectMask HarmfulEffects = BasicEffectMask.Slow;
    }
    internal partial class Entity
    {
        public event Action<EventInfoStatusEffect> ClientEntityEvent_OnStatusEffectAdded;
        internal void RaiseStatusEffectAdded(EventInfoStatusEffect info) => ClientEntityEvent_OnStatusEffectAdded?.Invoke(info);
    }
    internal partial class Actor
    {
        // Harmony runs these mod patches before the native listeners (Actor.cs InvokeOnDealDamage etc.).
        private static void RunPatch(Type patch, string name, object info)
            => patch.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { info });
        public void InvokeOnDealDamage(EventInfoDamage info) => RunPatch(typeof(NativeKillAssistDamage), "Prefix", info);
        public void InvokeOnDoHeal(EventInfoHeal info)
        {
            RunPatch(typeof(NativeKillAssistHeal), "Prefix", info);
            RunPatch(typeof(NativeSupportHeal), "Prefix", info);
        }
        public void InvokeOnGiveShield(EventInfoShield info)
        {
            RunPatch(typeof(NativeKillAssistShield), "Prefix", info);
            RunPatch(typeof(NativeSupportShield), "Prefix", info);
        }
    }
    internal sealed partial class HostAuthority
    {
        private int _powerSupportDepth;
        private bool _reflectingDamage, _shattering;
        private bool IsAttributedNativePacket(Actor actor, Entity victim) => true;
        private void QueueBountyReport(HeroRuntime rt, BountyReportKind kind, int value) { }
        internal readonly List<(HeroRuntime Rt, Entity Victim)> KillPowers = new List<(HeroRuntime, Entity)>();
        internal readonly List<(HeroRuntime Rt, Entity Victim, string Memory, bool Summon, bool Assist)> NewPowerKills
            = new List<(HeroRuntime, Entity, string, bool, bool)>();
        private void ApplyKillPowers(HeroRuntime rt, Entity victim) => KillPowers.Add((rt, victim));
        private void OnNewPowerKill(HeroRuntime rt, EventInfoKill info, string assistMemory = null, bool assistSummon = false, bool assist = false)
            => NewPowerKills.Add((rt, info.victim, assistMemory, assistSummon, assist));
        internal sealed partial class HeroRuntime
        {
            public readonly HashSet<int> CritBasicVictims = new HashSet<int>();
            public StatBonus GrowthBonus;
            public readonly Dictionary<Stat, double> GrowthTotals = new Dictionary<Stat, double>();
            public int GrowthVersion = -1;
            public Build GrowthBuild;
            public float GrowthSentAt;
            public int GrowthSentVersion = -1;
            public string GrowthOwnerKey;
            public Build GrowthSentBuild;
            public string GrowthSentRun;
            public uint GrowthSentHero;
            public DewPlayer GrowthSentPlayer;
            public Actor GrowthSentActor;
            public readonly int[] GrowthSentStacks = new int[256];
            public int GrowthSentCount;
            public float GrowthNextCheck;
        }
    }

    // HostAuthority.RunGrowth drive helpers: the ledger is process-wide, so each test pins its own run id.
    internal static class KillAssistSupportTestRuntime
    {
        internal static HostAuthority NewHost(string runId)
        {
            NetworkedManagerBase<GameManager>.softInstance.runId = runId;
            // The growth ledger is process-wide; negative tests assert before any Gain would re-key it.
            HostAuthority.RunGrowthLedger.Reset();
            UnityEngine.Time.time = 100;
            Mirror.NetworkServer.active = true;
            BasicAttackContext.Current = null;
            var host = new HostAuthority();
            HostAuthority.NativeInstance = host;
            // InitializeKillAssists is private and normally called from HostAuthority startup (not compiled here).
            typeof(HostAuthority).GetMethod("InitializeKillAssists", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
            return host;
        }
        internal static Hero HeroWithOwner(uint netId) => new Hero { owner = new DewPlayer { netId = netId, guid = "growth-player-" + netId }, maxHealth = 1000, currentHealth = 1000 };
        internal static HostAuthority.HeroRuntime TrackRuntime(HostAuthority host, Hero hero, params (string StarId, RunGrowthTrigger Trigger)[] growths)
        {
            var build = new Build();
            foreach (var (starId, trigger) in growths)
                build.RunGrowths.Add(new RunGrowthEntry { StarId = starId, Trigger = trigger, Threshold = 1, Cap = 100, GainMultiplier = 1 });
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0) };
            host.Track(rt);
            return rt;
        }
        internal static Monster Enemy() => new Monster { Relation = EntityRelation.Enemy, owner = new DewPlayer { isHumanPlayer = false }, currentHealth = 500 };
        // TrackKillAssistEntity is private in production; the spawn hook that calls it is not compiled here.
        internal static void TrackVictim(HostAuthority host, Entity entity)
            => typeof(HostAuthority).GetMethod("TrackKillAssistEntity", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { entity });
        internal static int Stacks(Hero hero, string starId) => HostAuthority.RunGrowthLedger.Stacks(hero.owner.guid, starId);
    }
}

namespace SodRpg.Core.Tests
{
    using SodRpg.Mod;

    // マルチの5秒アシスト（HostAuthority.KillAssists.cs）と支援の溢れ回復→障壁・遠征成長
    // （HostAuthority.SupportOverheal.cs / RunGrowth.cs）。ネイティブ境界はスタブで駆動する。
    public sealed class KillAssistSupportNativeTests
    {
        private static int Growth(Hero hero, string starId) => KillAssistSupportTestRuntime.Stacks(hero, starId);

        private static void Kill(HostAuthority host, Entity enemy, Hero killer, float at)
        {
            UnityEngine.Time.time = at;
            var kill = new EventInfoKill { actor = killer, victim = enemy };
            enemy.RaiseDeath(kill);
            host.DispatchKillAssists(kill);
        }

        [Fact]
        public void DamageOrControlWithinFiveSecondsAssistsAndGrantsGrowthOnce()
        {
            var host = KillAssistSupportTestRuntime.NewHost("ka-contact");
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);
            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var striker = KillAssistSupportTestRuntime.HeroWithOwner(2);
            var controller = KillAssistSupportTestRuntime.HeroWithOwner(3);
            var killerRt = KillAssistSupportTestRuntime.TrackRuntime(host, killer);
            var strikerRt = KillAssistSupportTestRuntime.TrackRuntime(host, striker,
                ("growth.kill", RunGrowthTrigger.EnemyKill), ("growth.crit", RunGrowthTrigger.CritBasicAttackKill));
            KillAssistSupportTestRuntime.TrackRuntime(host, controller);
            UnityEngine.Time.time = 100;
            striker.InvokeOnDealDamage(new EventInfoDamage { actor = striker, victim = enemy, damage = new DamageData(40) });
            enemy.RaiseStatusEffectAdded(new EventInfoStatusEffect
            { effect = new Se_GenericEffectContainer { victim = enemy, parentActor = controller }, victim = enemy });
            Kill(host, enemy, killer, 104);

            Assert.Equal(2, host.NewPowerKills.Count);
            Assert.All(host.NewPowerKills, entry => { Assert.True(entry.Assist); Assert.Equal(enemy, entry.Victim); Assert.False(entry.Summon); });
            Assert.Contains(host.NewPowerKills, entry => entry.Rt == strikerRt);
            Assert.Contains(host.NewPowerKills, entry => entry.Rt.Hero == controller);
            Assert.DoesNotContain(host.NewPowerKills, entry => entry.Rt == killerRt);
            Assert.Equal(2, host.KillPowers.Count);
            Assert.Equal(0, Growth(striker, "growth.kill")); // 遠征成長は撃破者だけ
            Assert.Equal(0, Growth(striker, "growth.crit"));
            Assert.Equal(0, Growth(killer, "growth.kill"));

            host.DispatchKillAssists(new EventInfoKill { actor = killer, victim = enemy }); // 同じ撃破では二重発動しない
            Assert.Equal(2, host.NewPowerKills.Count);
        }

        [Fact]
        public void ContactsOlderThanFiveSecondsDoNotAssist()
        {
            var host = KillAssistSupportTestRuntime.NewHost("ka-stale");
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);
            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var striker = KillAssistSupportTestRuntime.HeroWithOwner(2);
            KillAssistSupportTestRuntime.TrackRuntime(host, striker, ("growth.kill", RunGrowthTrigger.EnemyKill));
            UnityEngine.Time.time = 100;
            striker.InvokeOnDealDamage(new EventInfoDamage { actor = striker, victim = enemy, damage = new DamageData(40) });
            enemy.RaiseStatusEffectAdded(new EventInfoStatusEffect
            { effect = new Se_GenericEffectContainer { victim = enemy, parentActor = striker }, victim = enemy });
            Kill(host, enemy, killer, 105.2f);

            Assert.Empty(host.NewPowerKills);
            Assert.Empty(host.KillPowers);
            Assert.Equal(0, Growth(striker, "growth.kill"));
        }

        [Fact]
        public void SoloRunStaysUnchangedWithoutOtherHeroes()
        {
            var host = KillAssistSupportTestRuntime.NewHost("ka-solo");
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);
            var solo = KillAssistSupportTestRuntime.HeroWithOwner(1);
            KillAssistSupportTestRuntime.TrackRuntime(host, solo, ("growth.kill", RunGrowthTrigger.EnemyKill));
            UnityEngine.Time.time = 100;
            solo.InvokeOnDealDamage(new EventInfoDamage { actor = solo, victim = enemy, damage = new DamageData(40) });
            Kill(host, enemy, solo, 101);
            Assert.Empty(host.NewPowerKills);
            Assert.Empty(host.KillPowers);
            Assert.Equal(0, Growth(solo, "growth.kill"));

            // サーバ権限のない記録は無視される（シングルのクライアント側で何も起きない）。
            var enemy2 = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy2);
            var helper = KillAssistSupportTestRuntime.HeroWithOwner(2);
            KillAssistSupportTestRuntime.TrackRuntime(host, helper, ("growth.kill", RunGrowthTrigger.EnemyKill));
            Mirror.NetworkServer.active = false;
            try
            {
                helper.InvokeOnDealDamage(new EventInfoDamage { actor = helper, victim = enemy2, damage = new DamageData(40) });
            }
            finally { Mirror.NetworkServer.active = true; }
            Kill(host, enemy2, solo, 102);
            Assert.Empty(host.NewPowerKills);
            Assert.Equal(0, Growth(helper, "growth.kill"));
        }

        [Fact]
        public void KillerContactIsExcludedWhileHelperAssists()
        {
            var host = KillAssistSupportTestRuntime.NewHost("ka-killer");
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);
            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var striker = KillAssistSupportTestRuntime.HeroWithOwner(2);
            var killerRt = KillAssistSupportTestRuntime.TrackRuntime(host, killer, ("growth.kill", RunGrowthTrigger.EnemyKill));
            var strikerRt = KillAssistSupportTestRuntime.TrackRuntime(host, striker, ("growth.kill", RunGrowthTrigger.EnemyKill));
            UnityEngine.Time.time = 100;
            killer.InvokeOnDealDamage(new EventInfoDamage { actor = killer, victim = enemy, damage = new DamageData(60) });
            striker.InvokeOnDealDamage(new EventInfoDamage { actor = striker, victim = enemy, damage = new DamageData(40) });
            Kill(host, enemy, killer, 103);

            var assisted = Assert.Single(host.NewPowerKills);
            Assert.Equal(strikerRt, assisted.Rt);
            Assert.True(assisted.Assist);
            Assert.DoesNotContain(host.KillPowers, entry => entry.Rt == killerRt);
            Assert.Equal(0, Growth(killer, "growth.kill"));
            Assert.Equal(0, Growth(striker, "growth.kill")); // 遠征成長は撃破者だけ
        }

        [Fact]
        public void SummonHitsAttributeToTheOwner()
        {
            var host = KillAssistSupportTestRuntime.NewHost("ka-summon");
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);
            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var owner = KillAssistSupportTestRuntime.HeroWithOwner(2);
            var ownerRt = KillAssistSupportTestRuntime.TrackRuntime(host, owner, ("growth.kill", RunGrowthTrigger.EnemyKill));
            var summon = new Summon { parentActor = owner };
            UnityEngine.Time.time = 100;
            summon.InvokeOnDealDamage(new EventInfoDamage { actor = summon, victim = enemy, damage = new DamageData(25) });
            Kill(host, enemy, killer, 104);

            var assisted = Assert.Single(host.NewPowerKills);
            Assert.Equal(ownerRt, assisted.Rt);
            Assert.True(assisted.Summon); // 召喚体の命中は所有者のアシストとして扱う
            Assert.True(assisted.Assist);
            Assert.Null(assisted.Memory);
            Assert.Equal(0, Growth(owner, "growth.kill")); // 召喚によるアシストにも遠征成長を配らない
        }

        [Fact]
        public void HealOrShieldOnTheKillerWithinFiveSecondsAssists()
        {
            var host = KillAssistSupportTestRuntime.NewHost("ka-support");
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);
            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var healer = KillAssistSupportTestRuntime.HeroWithOwner(2);
            var warder = KillAssistSupportTestRuntime.HeroWithOwner(3);
            KillAssistSupportTestRuntime.TrackVictim(host, killer); // 撃破者自身も被支援の記録先になる
            KillAssistSupportTestRuntime.TrackRuntime(host, healer);
            KillAssistSupportTestRuntime.TrackRuntime(host, warder);
            UnityEngine.Time.time = 100;
            healer.InvokeOnDoHeal(new EventInfoHeal { actor = healer, target = killer, amount = 30 });
            warder.InvokeOnGiveShield(new EventInfoShield { statusEffect = new StatusEffect { parentActor = warder }, target = killer, finalAmount = 20 });
            Kill(host, enemy, killer, 103);

            Assert.Equal(2, host.NewPowerKills.Count);
            Assert.All(host.NewPowerKills, entry => Assert.True(entry.Assist));
            Assert.Contains(host.NewPowerKills, entry => entry.Rt.Hero == healer);
            Assert.Contains(host.NewPowerKills, entry => entry.Rt.Hero == warder);
        }

        [Fact]
        public void CritKillGrowthOnlyWhenTheAssistersOwnLastHitWasCritical()
        {
            var host = KillAssistSupportTestRuntime.NewHost("ka-crit");
            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);

            // 会心の基本攻撃が最後の命中でも、アシストには会心撃破の遠征成長を配らない。
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);
            var striker = KillAssistSupportTestRuntime.HeroWithOwner(2);
            KillAssistSupportTestRuntime.TrackRuntime(host, striker, ("growth.crit", RunGrowthTrigger.CritBasicAttackKill));
            UnityEngine.Time.time = 100;
            striker.InvokeOnDealDamage(new EventInfoDamage { actor = striker, victim = enemy, damage = new DamageData(10) });
            BasicAttackContext.Current = new BasicAttackContext { From = striker, Target = enemy, Actor = striker, Primary = true };
            striker.InvokeOnDealDamage(new EventInfoDamage
            { actor = striker, victim = enemy, damage = new DamageData(20).SetAttr(DamageAttribute.IsCrit) });
            BasicAttackContext.Current = null;
            Kill(host, enemy, killer, 104);
            Assert.Equal(0, Growth(striker, "growth.crit"));

            // 最初だけ会心で、最後の命中が非会心 → 付かない。
            var enemy2 = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy2);
            var striker2 = KillAssistSupportTestRuntime.HeroWithOwner(3);
            KillAssistSupportTestRuntime.TrackRuntime(host, striker2, ("growth.crit", RunGrowthTrigger.CritBasicAttackKill));
            UnityEngine.Time.time = 100;
            BasicAttackContext.Current = new BasicAttackContext { From = striker2, Target = enemy2, Actor = striker2, Primary = true };
            striker2.InvokeOnDealDamage(new EventInfoDamage
            { actor = striker2, victim = enemy2, damage = new DamageData(20).SetAttr(DamageAttribute.IsCrit) });
            BasicAttackContext.Current = null;
            striker2.InvokeOnDealDamage(new EventInfoDamage { actor = striker2, victim = enemy2, damage = new DamageData(10) });
            Kill(host, enemy2, killer, 104);
            Assert.Equal(0, Growth(striker2, "growth.crit"));

            // 撃破者の会心は支援者の成長にならない。
            var enemy3 = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy3);
            var striker3 = KillAssistSupportTestRuntime.HeroWithOwner(4);
            var killerRt = KillAssistSupportTestRuntime.TrackRuntime(host, killer, ("growth.crit", RunGrowthTrigger.CritBasicAttackKill));
            KillAssistSupportTestRuntime.TrackRuntime(host, striker3, ("growth.crit", RunGrowthTrigger.CritBasicAttackKill));
            UnityEngine.Time.time = 100;
            striker3.InvokeOnDealDamage(new EventInfoDamage { actor = striker3, victim = enemy3, damage = new DamageData(10) });
            BasicAttackContext.Current = new BasicAttackContext { From = killer, Target = enemy3, Actor = killer, Primary = true };
            killer.InvokeOnDealDamage(new EventInfoDamage
            { actor = killer, victim = enemy3, damage = new DamageData(60).SetAttr(DamageAttribute.IsCrit) });
            BasicAttackContext.Current = null;
            Kill(host, enemy3, killer, 104);
            Assert.Equal(0, Growth(striker3, "growth.crit"));
            Assert.Equal(0, Growth(killer, "growth.crit")); // 撃破者自身はアシスト経路では処理されない
        }

        [Fact]
        public void OverhealConvertsHalfIntoAllyBarrierUpToHealthCap()
        {
            var host = KillAssistSupportTestRuntime.NewHost("so-ward");
            var healer = KillAssistSupportTestRuntime.HeroWithOwner(11);
            KillAssistSupportTestRuntime.TrackRuntime(host, healer);
            var ally = KillAssistSupportTestRuntime.HeroWithOwner(12);
            host.OnNativeSupportHeal(new EventInfoHeal { actor = healer, target = ally, amount = 100, discardedAmount = 100 });
            Assert.Equal(50f, ally.Status.currentShield); // 溢れの50%が障壁
            Assert.Equal(1, host.Shields);

            var ally2 = KillAssistSupportTestRuntime.HeroWithOwner(13);
            host.OnNativeSupportHeal(new EventInfoHeal { actor = healer, target = ally2, amount = 2000, discardedAmount = 2000 });
            Assert.Equal(150f, ally2.Status.currentShield); // 受け手の最大HPの15%が上限
            Assert.Equal(2, host.Shields);
        }

        [Fact]
        public void SelfOverhealIsNotConvertedToABarrier()
        {
            var host = KillAssistSupportTestRuntime.NewHost("so-self");
            var healer = KillAssistSupportTestRuntime.HeroWithOwner(11);
            KillAssistSupportTestRuntime.TrackRuntime(host, healer);
            host.OnNativeSupportHeal(new EventInfoHeal { actor = healer, target = healer, amount = 50, discardedAmount = 50 });
            Assert.Equal(0f, healer.Status.currentShield);
            Assert.Equal(0, host.Shields);
        }

        [Fact]
        public void OverflowingLifeAndSupportOverhealKeepOnlyTheLargerConversion()
        {
            var host = KillAssistSupportTestRuntime.NewHost("so-compete");
            var healer = KillAssistSupportTestRuntime.HeroWithOwner(11);
            KillAssistSupportTestRuntime.TrackRuntime(host, healer);

            // 溢れる命（超過100%・上限は最大HPの10%）の方が変換量が大きい → 従来の処理だけ。
            var keeper = KillAssistSupportTestRuntime.HeroWithOwner(12);
            var keeperBuild = new Build(); keeperBuild.Powers[Power.OverflowingLife] = 100;
            host.Track(new HostAuthority.HeroRuntime { Hero = keeper, Powers = new PowerRuntime(keeperBuild, 0) });
            host.OnNativeSupportHeal(new EventInfoHeal { actor = healer, target = keeper, amount = 100, discardedAmount = 100 });
            Assert.Equal(100f, keeper.Status.currentShield); // OverflowingLife の 100
            Assert.Equal(0, host.Shields); // 支援側の障壁化は起きない

            // 支援の変換（50%）の方が大きい → 支援の障壁だけ。
            var taker = KillAssistSupportTestRuntime.HeroWithOwner(13);
            var takerBuild = new Build(); takerBuild.Powers[Power.OverflowingLife] = 40;
            host.Track(new HostAuthority.HeroRuntime { Hero = taker, Powers = new PowerRuntime(takerBuild, 0) });
            host.OnNativeSupportHeal(new EventInfoHeal { actor = healer, target = taker, amount = 100, discardedAmount = 100 });
            Assert.Equal(50f, taker.Status.currentShield);
            Assert.Equal(1, host.Shields);
        }

        [Fact]
        public void AllyHealingAndShieldsCountDoubleForSupportGrowth()
        {
            var host = KillAssistSupportTestRuntime.NewHost("so-growth");
            var supporter = KillAssistSupportTestRuntime.HeroWithOwner(21);
            KillAssistSupportTestRuntime.TrackRuntime(host, supporter, ("growth.heal", RunGrowthTrigger.HealSucceeded));
            var ally = KillAssistSupportTestRuntime.HeroWithOwner(22);

            host.OnNativeSupportHeal(new EventInfoHeal { actor = supporter, target = ally, amount = 10 });
            Assert.Equal(2, Growth(supporter, "growth.heal")); // 味方への回復は2回分
            host.OnNativeSupportHeal(new EventInfoHeal { actor = supporter, target = supporter, amount = 10 });
            Assert.Equal(3, Growth(supporter, "growth.heal")); // 自己回復は1回分

            host.OnNativeSupportShield(new EventInfoShield { statusEffect = new StatusEffect { parentActor = supporter }, target = ally, finalAmount = 5 });
            Assert.Equal(5, Growth(supporter, "growth.heal")); // 味方への障壁も2回分
            host.OnNativeSupportShield(new EventInfoShield { statusEffect = new StatusEffect { parentActor = supporter }, target = supporter, finalAmount = 5 });
            Assert.Equal(5, Growth(supporter, "growth.heal")); // 自己障壁は対象外
        }
    }
}
