using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private readonly Dictionary<Hero, DirectedRechargeRuntime> _directedRecharges = new Dictionary<Hero, DirectedRechargeRuntime>();
        private void InitializeDirectedRecharge() => MemoryActivationPublished += OnDirectedRechargeEvent;
        private void DisposeDirectedRecharge()
        {
            MemoryActivationPublished -= OnDirectedRechargeEvent;
            _directedRecharges.Clear();
        }
        internal void SetDirectedRechargeChannels(Hero hero, IEnumerable<DirectedRechargeChannel> channels)
        {
            if (hero == null || channels == null) throw new ArgumentNullException();
            if (!_directedRecharges.TryGetValue(hero, out var runtime)) runtime = new DirectedRechargeRuntime();
            runtime.SetChannels(channels);
            _directedRecharges[hero] = runtime;
        }
        private MechanismEquipment CollectMechanismEquipment(Hero hero, long ownerId)
        {
            long epoch = RefreshMemoryAttributionEquipment(hero);
            var memories = new List<EquippedMechanismMemory>();
            if (hero.Skill != null)
                foreach (var slot in LinkSkills)
                {
                    var skill = hero.Skill.GetSkill(slot);
                    if (skill == null) continue;
                    memories.Add(new EquippedMechanismMemory(skill.GetType().Name, skill.GetInstanceID(), ToMechanismSlot(slot),
                        skill.type == SkillType.Normal, skill.type == SkillType.Ultimate));
                }
            return new MechanismEquipment(ownerId, epoch, memories);
        }
        private static MechanismMemorySlot ToMechanismSlot(HeroSkillLocation slot)
        {
            switch (slot)
            {
                case HeroSkillLocation.Identity: return MechanismMemorySlot.Identity;
                case HeroSkillLocation.Q: return MechanismMemorySlot.Q;
                case HeroSkillLocation.W: return MechanismMemorySlot.W;
                case HeroSkillLocation.E: return MechanismMemorySlot.E;
                case HeroSkillLocation.R: return MechanismMemorySlot.R;
                case HeroSkillLocation.Movement: return MechanismMemorySlot.Movement;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
        private void OnDirectedRechargeEvent(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage)
        {
            if (!NetworkServer.active || !Alive(hero) || !_directedRecharges.TryGetValue(hero, out var runtime)) return;
            var equipment = CollectMechanismEquipment(hero, notification.OwnerId);
            int elements = 0;
            if (victim != null)
            {
                if (victim.Status.HasElemental(ElementalType.Fire)) elements++;
                if (victim.Status.HasElemental(ElementalType.Cold)) elements++;
                if (victim.Status.HasElemental(ElementalType.Light)) elements++;
                if (victim.Status.HasElemental(ElementalType.Dark)) elements++;
            }
            var requests = new List<DirectedRechargeRequest>();
            bool summons = _runtimes.TryGetValue(hero, out var rt) && HasOwnSummons(rt);
            runtime.Notify(notification, equipment, new RechargeConditionContext(hero.Status.currentShield > 0f, elements, summons), _rng.NextDouble, requests);
            foreach (var request in requests) ApplyDirectedRecharge(hero, request);
        }
        private void ApplyDirectedRecharge(Hero hero, DirectedRechargeRequest request)
        {
            if (!NetworkServer.active || !Alive(hero) || !request.IsCurrent(CollectMechanismEquipment(hero, request.OwnerId))) return;
            if (request.RequiresOwnedSummon && (!_runtimes.TryGetValue(hero, out var rt) || !HasOwnSummons(rt))) return;
            var skill = FindMemory(hero, request.RecipientMemory);
            if (skill == null || skill.GetInstanceID() != request.RecipientInstanceId) return;
            // One current-config ratio; native code applies it to all reducible config maxima and owns charges/processors.
            float ratio = request.NativeRatio(skill.currentConfigUnscaledCooldownTime, skill.currentConfigUnscaledMaxCooldownTime);
            if (ratio > 0f) hero.ApplyCooldownReductionByRatio(skill, ratio, false);
        }
        private void ClearDirectedRecharge(Hero hero)
        {
            if (_directedRecharges.TryGetValue(hero, out var runtime)) runtime.ClearTransient();
        }
    }
}
