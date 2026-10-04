using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Stat = SodRpg.Core.Game.Stat;

    /// <summary>Native adapter for <see cref="PowerShieldLedger{THandle,TEffect}"/>: handles are pooled, so identity is the inner shield.</summary>
    internal sealed class NativePowerShieldAdapter : IPowerShieldAdapter<Se_GenericShield_OneShot, ShieldEffect>
    {
        public bool IsActive(Se_GenericShield_OneShot handle) => handle != null && handle.isActive;
        public ShieldEffect EffectOf(Se_GenericShield_OneShot handle) => handle != null ? handle.shield : null;
        public float Amount(ShieldEffect effect) => effect != null ? effect.amount : 0f;
        public void Refresh(Se_GenericShield_OneShot handle, float seconds) => handle.SetTimer(seconds);
        public void Destroy(Se_GenericShield_OneShot handle) => handle.DestroyIfActive();
    }

    internal sealed partial class HostAuthority
    {
        private PowerShieldLedger<Se_GenericShield_OneShot, ShieldEffect> _powerShieldLedger;
        private PowerShieldLedger<Se_GenericShield_OneShot, ShieldEffect> PowerShields
            => _powerShieldLedger ?? (_powerShieldLedger = new PowerShieldLedger<Se_GenericShield_OneShot, ShieldEffect>(new NativePowerShieldAdapter()));

        // Generated-effect depth per originating hero (the global counter stays for the legacy guards).
        private readonly ScopedDepth<Hero> _generatedBy = new ScopedDepth<Hero>();

        private void EnterGenerated(Hero owner) { _gimmickDamageDepth++; _generatedBy.Enter(owner); }
        private void ExitGenerated(Hero owner) { _gimmickDamageDepth--; _generatedBy.Exit(owner); }

        /// <summary>Generated depth that applies to this hero: scopes opened by other heroes do not suppress its incoming hits.</summary>
        private int GeneratedDepthFor(Hero hero) => _gimmickDamageDepth - _generatedBy.Foreign(hero);

        /// <summary>Destroys still-owned power shields on detach so a reload does not abandon them or stack replacements.</summary>
        private void ReleasePowerShields()
        {
            try { PowerShields.DestroyAll(ex => Log.Error("Host: release power shield " + ex.Message)); }
            catch (Exception ex) { Log.Error("Host: release power shields " + ex.Message); }
            _generatedBy.Clear();
        }

        // ---- support credit for effects cast through the isolated serverActor ----

        private void CreditHealRestored(HeroRuntime rt, Entity target, float healthBefore)
        {
            var hero = rt.Hero;
            if (target == null || hero == null) return;
            bool otherAlly = target is Hero && target != hero && target.GetRelation(hero) == EntityRelation.Ally;
            if (SupportBountyCredit.AllyHeal(target.currentHealth - healthBefore, otherAlly))
                SendBountyReport(rt, BountyReportKind.AllyHealed, 1);
        }

        private void CreditShieldGranted(HeroRuntime rt, Entity target, float finalAmount)
        {
            var hero = rt.Hero;
            if (target == null || hero == null) return;
            bool selfOrAlly = target == hero || target.GetRelation(hero) == EntityRelation.Ally;
            if (SupportBountyCredit.ShieldGrant(true, finalAmount, selfOrAlly))
                SendBountyReport(rt, BountyReportKind.ShieldGranted, 1);
        }

        // ---- kill / element attribution through summons ----

        private HeroRuntime OwnerRuntimeOf(Actor actor)
        {
            if (actor == null) return null;
            Entity nearest = actor as Entity ?? actor.firstEntity;
            var hero = ActorOwnership.ResolveHero<Entity>(nearest, e => e is Hero, e => e is Summon,
                e => ((Summon)e).FindFirstAncestorOfType<Hero>());
            return hero is Hero h && _runtimes.TryGetValue(h, out var rt) ? rt : null;
        }

        // ---- MirageSkin created by this authority ----

        private sealed class OwnedMirage { public StatBonus Health; }
        private readonly OwnedEffectRegistry<Monster, StatusEffect> _mirageSkins = new OwnedEffectRegistry<Monster, StatusEffect>();
        private readonly Dictionary<Monster, OwnedMirage> _mirageBonuses = new Dictionary<Monster, OwnedMirage>();

        /// <summary>
        /// Attaches a native elite skin and keeps every part of it reversible: the shield amount is supplied through
        /// customAmount (skipping the native, untracked max-health addition) and the equivalent health bonus is a
        /// tracked StatBonus that Unhook removes together with the skin.
        /// </summary>
        private void AttachMirageSkin(Monster m, bool allowTier1)
        {
            try
            {
                if (m.Status.HasStatusEffect<MirageSkinEffect>()) return;
                ReleaseMirageSkin(m);
                if (_mirageTier0 == null)
                {
                    _mirageTier0 = new List<MirageSkinEffect>();
                    _mirageTier1 = new List<MirageSkinEffect>();
                    foreach (var e in DewResources.FindAllByType<MirageSkinEffect>())
                    {
                        if (e == null) continue;
                        if (e.tier <= 0) _mirageTier0.Add(e);
                        else _mirageTier1.Add(e);
                    }
                    Log.Info($"MirageSkin pool: tier0={_mirageTier0.Count} tier1={_mirageTier1.Count}");
                }
                var pool = new List<MirageSkinEffect>(_mirageTier0);
                if (allowTier1) pool.AddRange(_mirageTier1);
                if (pool.Count == 0) return;
                var pick = pool[_rng.Range(0, pool.Count - 1)];
                m.Status.CalculateStats();
                float shieldAmount = m.maxHealth * pick.shieldMaxHealthRatio;
                var health = new StatBonus { maxHealthPercentage = pick.maxHealthRatio * 100f - 100f };
                var skin = m.CreateStatusEffect(pick.GetType(), m, new CastInfo(m), se =>
                {
                    if (se is MirageSkinEffect mirage) mirage.customAmount = shieldAmount;
                });
                if (skin == null) return;
                m.Status.AddStatBonus(health);
                _mirageSkins.Add(m, skin);
                _mirageBonuses[m] = new OwnedMirage { Health = health };
            }
            catch (Exception ex)
            {
                Log.Warn("MirageSkin attach failed: " + ex.Message);
            }
        }

        private void ReleaseMirageSkin(Monster m)
        {
            if (m == null) return;
            try { _mirageSkins.Release(m, se => se != null && !se.isDestroyed && se.isActive, se => se.Destroy(),
                ex => Log.Error("Host: remove MirageSkin " + ex.Message)); }
            catch (Exception ex) { Log.Error("Host: release MirageSkin " + ex.Message); }
            if (!_mirageBonuses.TryGetValue(m, out var owned)) return;
            _mirageBonuses.Remove(m);
            try
            {
                if (m.Status != null)
                {
                    m.Status.RemoveStatBonus(owned.Health);
                    m.Status.CalculateStatsIfDirty();
                }
            }
            catch (Exception ex) { Log.Error("Host: remove MirageSkin health " + ex.Message); }
        }

        // ---- pact curses ----

        private void ReleasePactCurses()
        {
            int cleared = _pactCurses.ReleaseAll(se => se != null && !se.isDestroyed && se.isActive, se => se.Destroy(),
                ex => Log.Error("Host: release pact curse " + ex.Message));
            if (cleared > 0) Log.Info($"pact curses released on detach: {cleared}");
        }
    }
}
