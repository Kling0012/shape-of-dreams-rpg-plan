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
            => DispatchDirectedRechargeEvent(notification, hero, victim, nativeDamage, null);
        private void DispatchDirectedRechargeEvent(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage, string channelId)
        {
            if (!NetworkServer.active || !Alive(hero) || !_directedRecharges.TryGetValue(hero, out var runtime)) return;
            if (channelId == null && _authoredMechanisms.ContainsKey(hero)) return;
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
            AuthoredMechanismSpec spec = null;
            if (channelId != null && _authoredMechanisms.TryGetValue(hero, out var authored) && authored.Channels.TryGetValue(channelId, out var channel))
                spec = channel.Entry.Spec;
            var source = equipment.ResolveEventSource(notification, summons);
            var kind = notification.NativePayloadKind == NativePayloadKind.MainBasicAttack ? KeystoneSourceKind.OwnedBasicAttack
                : notification.NativePayloadKind == NativePayloadKind.SummonAttack ? KeystoneSourceKind.OwnedSummon : KeystoneSourceKind.NativeMemory;
            var effective = spec != null && source != null ? TransformAuthoredPayload(hero, AuthoredKeystoneComposer.MechanismPayload(spec),
                source.Memory, null, kind) : null;
            if (effective != null && effective.Disabled) return;
            runtime.Notify(notification, equipment, new RechargeConditionContext(hero.Status.currentShield > 0f, elements, summons), _rng.NextDouble, requests,
                candidate => channelId == null || candidate.ChannelId == channelId, channelId != null,
                candidate => effective?.EveryN ?? candidate.EveryN,
                candidate => effective != null ? effective.ProbabilityPercent * 100m : candidate.ProbabilityUnits);
            foreach (var request in requests) ApplyDirectedRecharge(hero, request);
        }
        private void ApplyDirectedRecharge(Hero hero, DirectedRechargeRequest request)
        {
            if (!NetworkServer.active || !Alive(hero) || !request.IsCurrent(CollectMechanismEquipment(hero, request.OwnerId))) return;
            if (request.RequiresOwnedSummon && (!_runtimes.TryGetValue(hero, out var rt) || !HasOwnSummons(rt))) return;
            var skill = FindMemory(hero, request.RecipientMemory);
            if (skill == null || skill.GetInstanceID() != request.RecipientInstanceId) return;
            // One current-config ratio; native code applies it to all reducible config maxima and owns charges/processors.
            KeystonePayload payload = new KeystonePayload(KeystoneLayer.ModEffect, request.ValueUnits / 100m,
                new KeystoneCaps(100), KeystonePayloadKind.DirectedRecharge, GimmickEffect.Recharge, request.ChannelId);
            if (_authoredMechanisms.TryGetValue(hero, out var owner))
                foreach (var channel in owner.Channels.Values)
                {
                    var spec = channel.Entry.Spec;
                    if (spec.Recharge != null && spec.ChannelId == request.ChannelId)
                    { payload = AuthoredKeystoneComposer.MechanismPayload(spec); break; }
                    if (spec.Bridge == null) continue;
                    if (spec.Bridge.BasePayoff.ChannelId == request.ChannelId)
                    { payload = AuthoredKeystoneComposer.BridgePayload(spec.Bridge.BasePayoff); break; }
                    foreach (var extra in spec.Bridge.Extras)
                        if (extra.ChannelId == request.ChannelId) { payload = AuthoredKeystoneComposer.BridgePayload(extra); break; }
                }
            var transformed = TransformAuthoredPayload(hero, payload, request.SourceMemory, request.RecipientMemory,
                request.RequiresOwnedSummon ? KeystoneSourceKind.OwnedBasicAttack : KeystoneSourceKind.NativeMemory);
            if (transformed.Disabled) return;
            float ratio = Gimmicks.RemainingCooldownReductionRatio(skill.currentConfigUnscaledCooldownTime,
                skill.currentConfigUnscaledMaxCooldownTime, (float)transformed.Value);
            if (ratio > 0f) hero.ApplyCooldownReductionByRatio(skill, ratio, false);
        }
        private void ClearDirectedRecharge(Hero hero)
        {
            if (_directedRecharges.TryGetValue(hero, out var runtime)) runtime.ClearTransient();
        }
    }
}
