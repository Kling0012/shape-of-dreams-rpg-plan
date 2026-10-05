using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

// Native API doubles for the linked HostAuthority.IdentityStrikes.cs / HostAuthority.MemoryTunings.cs.
// SimulateDealDamage mirrors the decompiled Actor.DealDamage ordering that the attribution proof depends on:
//   ProcessDealtDamage: dealtDamageProcessor of the dealing Actor and EVERY ancestor (Actor.cs ProcessDealtDamage),
//   InvokeOnDealDamage: ActorEvent_OnDealDamage of the dealing Actor and EVERY ancestor (Actor.cs InvokeOnDealDamage).
namespace SodRpg.Mod
{
    internal partial struct EventInfoDamage { public Actor actor; public Entity victim; public float amount; }
    internal partial class Actor
    {
        public event Action<EventInfoDamage> ActorEvent_OnDealDamage;
        public readonly List<DataProcessor<DamageData, Actor, Entity>> SimDealtDamage = new List<DataProcessor<DamageData, Actor, Entity>>();
        public readonly List<Actor> SimDealers = new List<Actor>();
        public static readonly List<Actor> SimDealerLog = new List<Actor>();
        public static readonly List<(Actor Dealer, Entity Victim, float Amount, object Element)> SimDamageLog = new List<(Actor, Entity, float, object)>();
        internal void SimulateDealDamage(ref DamageData data, Entity target)
        {
            SimDealerLog.Add(this);
            for (Actor a = this; a != null; a = a.parentActor)
                foreach (var processor in a.SimDealtDamage.ToArray()) processor(ref data, this, target);
            var info = new EventInfoDamage { actor = this, victim = target, amount = data.currentAmount };
            SimDamageLog.Add((this, target, data.currentAmount, data.Elemental));
            for (Actor a = this; a != null; a = a.parentActor) a.ActorEvent_OnDealDamage?.Invoke(info);
        }
    }
    internal partial struct DamageData
    {
        public DamageData SetActor(Actor value) { actor = value; return this; }
        public float Amount => currentAmount;
    }
    internal partial struct HealData { public void ApplyAmplification(float value) => Amount *= 1f + value; }
    internal partial struct FinalStats { public float attackDamage, abilityPower, attackSpeedMultiplier; }
    internal partial class Entity
    {
        public readonly ProcessorList<DataProcessor<HealData, Actor, Entity>> dealtHealProcessor = new ProcessorList<DataProcessor<HealData, Actor, Entity>>();
    }
    internal sealed class St_D_TheKillingFlow : SkillTrigger { public int gainedAd; }
    internal sealed class St_D_ScarOfTheWind : SkillTrigger { }
    internal sealed class NativeMemoryPayloadScope { public static NativeMemoryPayloadScope Current; public Actor Source; }
    /// <summary>Compiled with the same call shape as the verified native OnHit: Damage(..).SetElemental(Dark).Dispatch(entity, default chain), then a heal.</summary>
    internal sealed class Ai_D_ScarOfTheWind_DashAtk : AbilityInstance
    {
        public DamageData Damage(float amount) => new DamageData(amount) { actor = this };
        public void OnHit(Entity entity, bool isMain)
        {
            Damage(75f).SetElemental(ElementalType.Dark).Dispatch(entity, default(ReactionChain));
            Heal(1f).Dispatch(info.caster);
        }
    }
}
