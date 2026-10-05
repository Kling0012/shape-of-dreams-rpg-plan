using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Ai_U_BigChomp),"OnHit")]
    internal static class MawBigChompNativeHit
    {
        private static void Prefix(Ai_U_BigChomp __instance, Entity entity)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RecordBossBigChompHit(__instance,entity);
        }
    }

    // Scope only the three original calls, not effects dispatched by base.OnAfterDelay.
    // Managed native IL: HealData.Dispatch IL_006e, GiveShield IL_009d,
    // ApplyCooldownReduction(firstTrigger,amount,true,false) IL_00b4.
    [HarmonyPatch(typeof(Ai_U_BigChomp),"OnAfterDelay")]
    internal static class MawBigChompNativeDelay
    {
        internal struct Scope
        {
            internal Ai_U_BigChomp Instance;
            internal int Kind;
        }
        internal static Scope Current;
        private static void Prefix(Ai_U_BigChomp __instance, out Scope __state)
        {
            __state = Current;
            Current = new Scope { Instance = NetworkServer.active ? __instance : null };
        }
        private static void Finalizer(Ai_U_BigChomp __instance, Scope __state)
        {
            try { if (NetworkServer.active) HostAuthority.NativeInstance?.CompleteBossBigChompDelay(__instance); }
            finally { Current = __state; }
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var heal = AccessTools.Method(typeof(HealData),nameof(HealData.Dispatch),new[] { typeof(Entity),typeof(ReactionChain) });
            var shield = AccessTools.Method(typeof(Actor),nameof(Actor.GiveShield),new[] { typeof(Entity),typeof(float),typeof(float),typeof(bool),typeof(ReactionChain) });
            var cooldown = AccessTools.Method(typeof(Actor),nameof(Actor.ApplyCooldownReduction),new[] { typeof(AbilityTrigger),typeof(float),typeof(bool),typeof(bool) });
            int heals = 0, shields = 0, cooldowns = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(heal))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MawBigChompNativeDelay),nameof(Heal)); heals++; }
                else if (instruction.Calls(shield))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MawBigChompNativeDelay),nameof(Shield)); shields++; }
                else if (instruction.Calls(cooldown))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MawBigChompNativeDelay),nameof(Cooldown)); cooldowns++; }
                yield return instruction;
            }
            if (heals != 1 || shields != 1 || cooldowns != 1)
                throw new InvalidOperationException("Big Chomp adapter requires exactly one original owner heal, shield and first-trigger cooldown call.");
        }
        private static void Heal(ref HealData data, Entity target, ReactionChain chain)
        {
            var previous = Current;
            Current.Kind = 1;
            try { data.Dispatch(target,chain); }
            finally { Current = previous; }
        }
        private static Se_GenericShield_OneShot Shield(Actor actor, Entity target, float amount, float duration, bool isDecay, ReactionChain chain)
        {
            var previous = Current;
            Current.Kind = 2;
            try { return actor.GiveShield(target,amount,duration,isDecay,chain); }
            finally { Current = previous; }
        }
        private static void Cooldown(Actor actor, AbilityTrigger target, float amount, bool scaled, bool ignoreCanReceiveCooldown)
        {
            var previous = Current;
            Current.Kind = 3;
            try { actor.ApplyCooldownReduction(target,amount,scaled,ignoreCanReceiveCooldown); }
            finally { Current = previous; }
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class MawChompBinding
        {
            internal HostAuthority Host;
            internal HeroRuntime Runtime;
            internal Ai_U_BigChomp Instance;
            internal St_U_BigChomp Skill;
            internal Actor Parent;
            internal long Life, ParentLife, HeroLife, SkillLife, Epoch, Activation;
            internal float Weight, HealExtra, ShieldExtra, CooldownExtra;
            internal int Stage;
            internal bool Claimed, Rejected, Completed, Healed, Shielded, Reduced;
            internal readonly DataProcessor<HealData,Actor,Entity> HealProcessor, ShieldProcessor;
            internal readonly DataProcessor<CooldownReductionSettings,Actor,AbilityTrigger> CooldownProcessor;
            internal MawChompBinding()
            {
                HealProcessor = ProcessHeal; ShieldProcessor = ProcessShield; CooldownProcessor = ProcessCooldown;
            }
            private void ProcessHeal(ref HealData data, Actor actor, Entity target)
            {
                if (Host == null || Runtime == null || Healed || MawBigChompNativeDelay.Current.Kind != 1 || actor != Instance || data.actor != Instance
                    || target != Runtime.Hero || data.originalAmount <= 0 || !Host.MawChompClaim(this)) return;
                Healed = true;
                if (HealExtra > 0) data.AddAmount(HealExtra);
            }
            private void ProcessShield(ref HealData data, Actor actor, Entity target)
            {
                if (Host == null || Runtime == null || Shielded || MawBigChompNativeDelay.Current.Kind != 2 || !(actor is Se_GenericShield_OneShot)
                    || actor.parentActor != Instance || target != Runtime.Hero || data.originalAmount <= 0
                    || !Host.MawChompClaim(this)) return;
                Shielded = true;
                if (Stage >= 2 && ShieldExtra > 0) data.AddAmount(ShieldExtra);
            }
            private void ProcessCooldown(ref CooldownReductionSettings data, Actor actor, AbilityTrigger target)
            {
                if (Host == null || Runtime == null || Reduced || MawBigChompNativeDelay.Current.Kind != 3 || actor != Instance || target != Skill
                    || data.amount <= 0 || !Host.MawChompClaim(this)) return;
                Reduced = true;
                if (Stage >= 3 && CooldownExtra > 0) data.amount += CooldownExtra;
            }
            internal void Reset()
            {
                Host = null; Runtime = null; Instance = null; Skill = null; Parent = null;
                Life = ParentLife = HeroLife = SkillLife = Epoch = Activation = 0;
                Weight = HealExtra = ShieldExtra = CooldownExtra = 0; Stage = 0;
                Claimed = Rejected = Completed = Healed = Shielded = Reduced = false;
            }
        }
        private const string MawChompSlot = "boss_maw.big_chomp.slot";
        private readonly BossObjectPool<MawChompBinding> _mawChompPool = new BossObjectPool<MawChompBinding>(128, () => new MawChompBinding());
        private readonly Dictionary<Ai_U_BigChomp,MawChompBinding> _mawChomps = new Dictionary<Ai_U_BigChomp,MawChompBinding>(128);
        private readonly List<Ai_U_BigChomp> _mawChompScratch = new List<Ai_U_BigChomp>(128);

        internal void BindBossBigChomp(EventInfoAbilityInstance info)
        {
            if (!NetworkServer.active || !(info.instance is Ai_U_BigChomp instance) || _mawChomps.ContainsKey(instance)
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || instance.gem != null
                || !(instance.info.caster is Hero hero) || !BossNativeRuntime(hero,out var rt) || !BossEnsure(rt)
                || BossRewardStage(rt,BossProfiles.MawRewardId) <= 0 || !BossAlive(hero)
                || !_bossNativeSources.TryGetValue(instance,out var source) || !BossNativeSourceCurrent(source,false)
                || !(instance.firstTrigger is St_U_BigChomp skill) || source.Cast.Trigger != skill
                || source.Cast.Owner != hero || FindMemory(hero,nameof(St_U_BigChomp)) != skill
                || source.Cast.EquipmentEpoch != rt.ShieldEquipmentEpoch || _mawChomps.Count >= 128) return;
            var binding = _mawChompPool.Rent();
            if (binding == null) return;
            binding.Reset(); binding.Host = this; binding.Runtime = rt; binding.Instance = instance;
            binding.Skill = skill; binding.Parent = instance.parentActor; binding.Life = source.Life;
            binding.ParentLife = source.ParentLife; binding.HeroLife = BossNativeActorLife(hero);
            binding.SkillLife = source.Cast.TriggerLife; binding.Epoch = rt.ShieldEquipmentEpoch; binding.Activation = source.Cast.Activation;
            _mawChomps.Add(instance,binding);
            instance.dealtHealProcessor.Add(binding.HealProcessor);
            instance.dealtShieldProcessor.Add(binding.ShieldProcessor);
            instance.dealtCooldownReductionProcessor.Add(binding.CooldownProcessor);
        }
        private bool MawChompCurrent(MawChompBinding binding)
        {
            var instance = binding.Instance;
            var rt = binding.Runtime;
            return NetworkServer.active && instance != null && rt != null && rt.Boss.Build != null && BossAlive(rt.Hero)
                && binding.Epoch == rt.ShieldEquipmentEpoch && instance.info.caster == rt.Hero && instance.gem == null
                && instance.firstTrigger == binding.Skill && instance.parentActor == binding.Parent
                && BossNativeSameLife(instance,binding.Life) && BossNativeSameLife(binding.Parent,binding.ParentLife)
                && BossNativeSameLife(rt.Hero,binding.HeroLife) && BossNativeSameLife(binding.Skill,binding.SkillLife)
                && FindMemory(rt.Hero,nameof(St_U_BigChomp)) == binding.Skill && binding.Skill.owner == rt.Hero
                && _bossNativeSources.TryGetValue(instance,out var source) && source.Life == binding.Life
                && source.Cast.Activation == binding.Activation && BossNativeSourceCurrent(source,true)
                && BossRewardStage(rt,BossProfiles.MawRewardId) > 0;
        }
        internal void RecordBossBigChompHit(Ai_U_BigChomp instance, Entity target)
        {
            if (!_mawChomps.TryGetValue(instance,out var binding) || binding.Completed || !MawChompCurrent(binding)
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || target == null
                || target.IsNullInactiveDeadOrKnockedOut() || target.Status.hasDamageImmunity
                || target.GetRelation(binding.Runtime.Hero) != EntityRelation.Enemy) return;
            float weight = target.IsAnyBoss() ? 1f + instance.anyBossMultiplier : 1f;
            if (float.IsNaN(weight) || weight <= 0) return;
            binding.Weight = Math.Min(3f,binding.Weight + weight);
        }
        private bool MawChompClaim(MawChompBinding binding)
        {
            if (binding.Rejected || binding.Completed || MawBigChompNativeDelay.Current.Instance != binding.Instance
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !MawChompCurrent(binding)) return false;
            if (binding.Claimed) return true;
            if (binding.Weight <= 0) return false;
            var instance = binding.Instance;
            var rt = binding.Runtime;
            int stage = BossRewardStage(rt,BossProfiles.MawRewardId);
            float h = Math.Max(0f,Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower));
            // These are loaded native values, never guessed serialized initializer defaults.
            float heal = instance.GetValue(instance.healPerHitAmount);
            float shield = instance.GetValue(instance.shieldPerHitAmount);
            binding.HealExtra = heal > 0 ? Math.Min(.08f * h,heal * binding.Weight * .20f) : 0;
            binding.ShieldExtra = stage >= 2 && shield > 0 ? Math.Min(.10f * h,shield * binding.Weight * .20f) : 0;
            binding.CooldownExtra = stage >= 3 && instance.reduceCooldown > 0 ? Math.Min(.50f,binding.Weight * .25f) : 0;
            if (binding.HealExtra <= 0 && binding.ShieldExtra <= 0 && binding.CooldownExtra <= 0) return false;
            if (!BossReady(rt,MawChompSlot,Time.time,8000)) { binding.Rejected = true; return false; }
            binding.Stage = stage; binding.Claimed = true; return true;
        }
        internal void CompleteBossBigChompDelay(Ai_U_BigChomp instance)
        {
            if (_mawChomps.TryGetValue(instance,out var binding)) binding.Completed = true;
        }
        internal void ClearBossBigChompActor(Actor actor)
        {
            if (!(actor is Ai_U_BigChomp instance) || !_mawChomps.TryGetValue(instance,out var binding)) return;
            _mawChomps.Remove(instance);
            if (instance != null)
            {
                instance.dealtHealProcessor.Remove(binding.HealProcessor);
                instance.dealtShieldProcessor.Remove(binding.ShieldProcessor);
                instance.dealtCooldownReductionProcessor.Remove(binding.CooldownProcessor);
            }
            binding.Reset(); _mawChompPool.Return(binding);
        }
        private void TickBossBigChomp(HeroRuntime rt)
        {
            float now = Time.time;
            if (now < rt.BossNative.ChompPoll) return;
            rt.BossNative.ChompPoll = now + .1f;
            _mawChompScratch.Clear();
            foreach (var pair in _mawChomps)
                if (pair.Value.Runtime == rt && (!MawChompCurrent(pair.Value) || pair.Value.Completed)) _mawChompScratch.Add(pair.Key);
            for (int i = 0; i < _mawChompScratch.Count; i++) ClearBossBigChompActor(_mawChompScratch[i]);
            _mawChompScratch.Clear();
        }
        private void ClearBossBigChomp(HeroRuntime rt)
        {
            _mawChompScratch.Clear();
            foreach (var pair in _mawChomps) if (pair.Value.Runtime == rt) _mawChompScratch.Add(pair.Key);
            for (int i = 0; i < _mawChompScratch.Count; i++) ClearBossBigChompActor(_mawChompScratch[i]);
            _mawChompScratch.Clear();
        }
    }
}
