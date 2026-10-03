using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class ShieldOwnerEquipment
        {
            public long Epoch;
            public readonly List<SkillTrigger> Skills = new List<SkillTrigger>();
        }
        private sealed class PoolRecipient
        {
            public HeroRuntime Owner;
            public Entity Recipient;
        }
        private readonly ModShieldPools<Se_GenericShield_OneShot> _modShieldPools = new ModShieldPools<Se_GenericShield_OneShot>();
        private readonly Dictionary<ModShieldPoolKey, PoolRecipient> _modShieldRecipients = new Dictionary<ModShieldPoolKey, PoolRecipient>();
        private readonly Dictionary<HeroRuntime, ShieldOwnerEquipment> _shieldEquipment = new Dictionary<HeroRuntime, ShieldOwnerEquipment>();
        private long _shieldEpoch;

        private long ModShieldEquipmentEpoch(HeroRuntime rt)
        {
            var skills = new List<SkillTrigger>();
            foreach (var slot in LinkSkills) skills.Add(rt.Hero.Skill != null ? rt.Hero.Skill.GetSkill(slot) : null);
            if (!_shieldEquipment.TryGetValue(rt, out var state))
                _shieldEquipment.Add(rt, state = new ShieldOwnerEquipment());
            bool changed = state.Skills.Count != skills.Count;
            for (int i = 0; !changed && i < skills.Count; i++) changed = !ReferenceEquals(state.Skills[i], skills[i]);
            if (changed)
            {
                _modShieldPools.RemoveOwner(rt.Hero.GetInstanceID());
                state.Skills.Clear(); state.Skills.AddRange(skills); state.Epoch = ++_shieldEpoch;
            }
            rt.ShieldEquipmentEpoch = state.Epoch;
            return state.Epoch;
        }

        internal void RefreshModShieldEquipment(HeroSkill skill)
        {
            if (skill != null && skill.hero != null && _runtimes.TryGetValue(skill.hero, out var rt))
                ModShieldEquipmentEpoch(rt);
        }

        private bool AwardModShield(HeroRuntime owner, Entity recipient, ModShieldPoolKind kind,
            float rawAmount, float duration, string sourceMemory, long equipmentEpoch, float newAwardCapRatio = 0f)
        {
            if (!NetworkServer.active) throw new InvalidOperationException("MOD shield awards require the server.");
            if (!Alive(owner.Hero) || recipient == null || !recipient.isActive || recipient.currentHealth <= 0) return false;
            if (sourceMemory != null && FindMemory(owner.Hero, sourceMemory) == null) return false;
            long currentEpoch = ModShieldEquipmentEpoch(owner);
            if (equipmentEpoch != currentEpoch) return false;
            if (_am == null || _am.serverActor == null) throw new InvalidOperationException("Root server actor is unavailable for MOD shielding.");
            var key = new ModShieldPoolKey(owner.Hero.GetInstanceID(), recipient.GetInstanceID(), kind);
            _gimmickDamageDepth++;
            try
            {
                bool awarded = _modShieldPools.Apply(key, new NativeModShieldAdapter(_am.serverActor, recipient), rawAmount,
                    recipient.maxHealth, Time.time, duration, currentEpoch, newAwardCapRatio);
                if (awarded) _modShieldRecipients[key] = new PoolRecipient { Owner = owner, Recipient = recipient };
                if (awarded) CreditShieldGranted(owner, recipient, rawAmount);
                return awarded;
            }
            finally { _gimmickDamageDepth--; }
        }

        private void UpdateModShieldPools(float now)
        {
            var dead = new List<ModShieldPoolKey>();
            foreach (var pair in _modShieldRecipients)
            {
                var owner = pair.Value.Owner;
                var recipient = pair.Value.Recipient;
                bool alive = Alive(owner.Hero) && recipient != null && recipient.isActive && recipient.currentHealth > 0f;
                _modShieldPools.Maintain(pair.Key, recipient != null ? recipient.maxHealth : 0f, now,
                    alive ? ModShieldEquipmentEpoch(owner) : owner.ShieldEquipmentEpoch, alive);
                if (!alive || !_modShieldPools.Contains(pair.Key)) dead.Add(pair.Key);
            }
            foreach (var key in dead) _modShieldRecipients.Remove(key);
        }

        private void ClearModShieldPools(HeroRuntime owner = null)
        {
            if (owner == null)
            { _modShieldPools.Clear(); _modShieldRecipients.Clear(); _shieldEquipment.Clear(); return; }
            if (owner.Hero != null) _modShieldPools.RemoveOwner(owner.Hero.GetInstanceID());
            var keys = new List<ModShieldPoolKey>();
            foreach (var pair in _modShieldRecipients) if (pair.Value.Owner == owner) keys.Add(pair.Key);
            foreach (var key in keys) _modShieldRecipients.Remove(key);
            _shieldEquipment.Remove(owner);
        }
    }

    // Observe the authoritative mutation immediately, including unequip/re-equip of the same pooled skill before the next Tick.
    [HarmonyPatch(typeof(HeroSkill), nameof(HeroSkill.UnequipSkill))]
    internal static class NativeModShieldUnequip
    {
        private static void Postfix(HeroSkill __instance, SkillTrigger __result)
        {
            if (NetworkServer.active && __result != null) HostAuthority.NativeInstance?.RefreshModShieldEquipment(__instance);
        }
    }
    [HarmonyPatch(typeof(HeroSkill), nameof(HeroSkill.EquipSkill))]
    internal static class NativeModShieldEquip
    {
        private static void Postfix(HeroSkill __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshModShieldEquipment(__instance);
        }
    }

    internal sealed class NativeModShieldAdapter : IModShieldAdapter<Se_GenericShield_OneShot>
    {
        private readonly Actor _root;
        private readonly Entity _recipient;
        private ShieldEffect _effect;
        internal NativeModShieldAdapter(Actor root, Entity recipient) { _root = root; _recipient = recipient; }
        public bool IsAlive(Se_GenericShield_OneShot handle) => handle != null && handle.isActive && _effect != null
            && ReferenceEquals(handle.shield, _effect) && handle.shield.amount > 0f;
        public float Remaining(Se_GenericShield_OneShot handle) => handle.shield.amount;
        public float ProcessRaw(Se_GenericShield_OneShot handle, float rawAmount) => handle.ProcessShieldAmount(rawAmount, _recipient);
        public void SetProcessed(Se_GenericShield_OneShot handle, float amount) => handle.shield.amount = amount;
        public void Refresh(Se_GenericShield_OneShot handle, float seconds) => handle.SetTimer(seconds);
        public void Destroy(Se_GenericShield_OneShot handle) => handle.DestroyIfActive();
        public Se_GenericShield_OneShot CreateRaw(float rawAmount, float seconds, float processedCap)
        {
            Se_GenericShield_OneShot created = null;
            try
            {
                // Same native construction as Actor.GiveShield; register the exact handle before OnCreate/DoShield.
                var result = _root.CreateStatusEffect<Se_GenericShield_OneShot>(_recipient, new CastInfo(_recipient), se =>
                {
                    created = se;
                    se.chain = default(ReactionChain); se.initAmount = rawAmount; se.isDecay = false; se.SetTimer(seconds);
                    NativeModShieldCreationCap.Register(se, processedCap);
                });
                _effect = result != null ? result.shield : null;
                return result;
            }
            finally { if (!ReferenceEquals(created, null)) NativeModShieldCreationCap.Remove(created); }
        }
    }

    // Cap the final processed return before DoShield registers the effect or emits support events.
    [HarmonyPatch(typeof(Actor), nameof(Actor.ProcessShieldAmount))]
    internal static class NativeModShieldCreationCap
    {
        private static readonly Dictionary<Actor, float> Caps = new Dictionary<Actor, float>();
        internal static void Register(Actor actor, float cap) => Caps.Add(actor, cap);
        internal static void Remove(Actor actor) => Caps.Remove(actor);
        private static void Postfix(Actor __instance, ref float __result)
        {
            if (NetworkServer.active && Caps.TryGetValue(__instance, out float cap))
                __result = Math.Min(__result, cap);
        }
    }
}
