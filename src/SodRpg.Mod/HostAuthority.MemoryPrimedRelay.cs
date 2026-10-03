using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class MemoryPrimedRelayState
        {
            internal readonly MemoryPrimedRuntime Primed;
            internal readonly RelayWindowRuntime Relay;
            internal readonly Dictionary<string, SkillTrigger> Equipment = new Dictionary<string, SkillTrigger>(StringComparer.Ordinal);
            internal readonly Dictionary<string, long> Epochs = new Dictionary<string, long>(StringComparer.Ordinal);
            internal Dictionary<string, MemoryPrimedDefinition> Preparations = new Dictionary<string, MemoryPrimedDefinition>(StringComparer.Ordinal);
            internal bool HasPreparations;
            internal long Generation;
            internal MemoryPrimedRelayState(long owner) { Primed = new MemoryPrimedRuntime(owner); Relay = new RelayWindowRuntime(owner); }
        }
        private readonly Dictionary<Hero, MemoryPrimedRelayState> _memoryPrimedRelay = new Dictionary<Hero, MemoryPrimedRelayState>();
        private bool _memoryPrimedRelaySubscribed;

        private void InitializeMemoryPrimedRelay()
        {
            if (_memoryPrimedRelaySubscribed) return;
            MemoryActivationPublished += OnMemoryPrimedRelayNotification;
            MemoryAttributionEquipmentChanged += OnMemoryPrimedRelayEquipmentChanged;
            _memoryPrimedRelaySubscribed = true;
        }

        private MemoryPrimedRelayState GetMemoryPrimedRelay(Hero hero)
        {
            if (hero == null || hero.Skill == null) throw new ArgumentException("A live skill owner is required.");
            InitializeMemoryPrimedRelay();
            if (!_memoryPrimedRelay.TryGetValue(hero, out var state))
                _memoryPrimedRelay.Add(hero, state = new MemoryPrimedRelayState(hero.GetInstanceID()));
            SynchronizeMemoryPrimedRelay(hero, state);
            return state;
        }

        /// <summary>Explicit registry binding; C01/C03/C07 supply validated, scoped final definitions.</summary>
        internal void ConfigureMemoryPreparations(Hero hero, IEnumerable<MemoryPrimedDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (hero == null || hero.Skill == null) throw new ArgumentException("A live skill owner is required.");
            if (!_runtimes.TryGetValue(hero, out var rt)) throw new InvalidOperationException("Configure preparations after creating the owner runtime.");
            var entries = new List<MemoryPrimedDefinition>(definitions);
            var next = new Dictionary<string, MemoryPrimedDefinition>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || !Links.IsMemory(entry.SourceMemory)) throw new ArgumentException("Unknown preparation memory.");
                if (next.ContainsKey(entry.ChannelId)) throw new ArgumentException("Preparation channels must be unique.");
                next.Add(entry.ChannelId, entry);
                var movement = hero.Skill.GetSkill(HeroSkillLocation.Movement);
                if (movement != null && movement.GetType().Name == entry.SourceMemory)
                    throw new InvalidOperationException("Movement cannot grant a memory preparation.");
            }
            var state = GetMemoryPrimedRelay(hero);
            state.Primed.Configure(entries);
            bool changed = state.Preparations.Count != next.Count;
            if (!changed)
                foreach (var pair in next)
                    if (!state.Preparations.TryGetValue(pair.Key, out var prior) || !pair.Value.Same(prior)) { changed = true; break; }
            if (changed) state.Generation++;
            state.Preparations = next;
            state.HasPreparations = entries.Count != 0;
            rt.Powers.UsesMemoryPreparationLedger = state.HasPreparations;
        }

        internal void ConfigureRelayWindows(Hero hero, IEnumerable<RelayWindowDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var entries = new List<RelayWindowDefinition>(definitions);
            foreach (var entry in entries)
                if (entry == null || !Links.IsMemory(entry.TargetMemory)) throw new ArgumentException("Unknown relay target memory.");
            GetMemoryPrimedRelay(hero).Relay.Configure(entries);
        }

        private void OnMemoryPrimedRelayEquipmentChanged(Hero hero, long epoch)
        {
            if (_memoryPrimedRelay.TryGetValue(hero, out var state)) SynchronizeMemoryPrimedRelay(hero, state);
        }

        private void SynchronizeMemoryPrimedRelay(Hero hero, MemoryPrimedRelayState state)
        {
            var equipped = new Dictionary<string, SkillTrigger>(StringComparer.Ordinal);
            var epochs = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var slot in LinkSkills)
            {
                if (slot == HeroSkillLocation.Movement) continue;
                var skill = hero.Skill.GetSkill(slot);
                if (skill == null) continue;
                string memory = skill.GetType().Name;
                if (equipped.ContainsKey(memory)) throw new InvalidOperationException("A memory source has more than one equipped instance.");
                equipped.Add(memory, skill);
                long epoch = state.Equipment.TryGetValue(memory, out var prior) && prior == skill
                    ? state.Epochs[memory] : _memoryAttribution.NewPacketId();
                epochs.Add(memory, epoch);
            }
            state.Primed.SetEquipment(epochs);
            var q = hero.Skill.GetSkill(HeroSkillLocation.Q);
            string qMemory = q != null ? q.GetType().Name : null;
            epochs.TryGetValue(RelayWindowDefinition.SourceMemory, out long sourceEpoch);
            long qEpoch = qMemory != null && epochs.TryGetValue(qMemory, out long targetEpoch) ? targetEpoch : 0;
            state.Relay.SetEquipment(sourceEpoch, qMemory, qEpoch);
            state.Equipment.Clear(); state.Epochs.Clear();
            foreach (var pair in equipped) state.Equipment.Add(pair.Key, pair.Value);
            foreach (var pair in epochs) state.Epochs.Add(pair.Key, pair.Value);
        }

        private static MemoryActivationEvent WithPreparationEpoch(MemoryActivationEvent value, MemoryPrimedRelayState state)
        {
            state.Epochs.TryGetValue(value.SourceMemory, out long epoch);
            return new MemoryActivationEvent(value.OwnerId, value.SourceMemory, value.ActivationId, value.DamagePacketId,
                value.VictimId, value.EventKind, value.NativePayloadKind, value.GeneratedOrigin, epoch);
        }

        private void OnMemoryPrimedRelayNotification(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage)
        {
            if (!_memoryPrimedRelay.TryGetValue(hero, out var state) || !Alive(hero)
                || !_memoryAttribution.IsCurrent(notification)) return;
            SynchronizeMemoryPrimedRelay(hero, state);
            var source = WithPreparationEpoch(notification, state);
            state.Primed.OnSourceEvent(source, Time.time);
            state.Relay.OnSourceEvent(source, Time.time);
            if (!state.HasPreparations || notification.EventKind != MemoryEventKind.OwnedBasicAttackHit || nativeDamage <= 0
                || victim == null || !_runtimes.TryGetValue(hero, out var rt)) return;
            var candidates = new List<NextBasicBonusCandidate>();
            float higher = Math.Max(hero.Status.attackDamage, hero.Status.abilityPower);
            rt.Powers.CollectNextBasicBonuses(Time.time, higher, candidates);
            if (!state.Primed.TryConsume(notification, Time.time, higher, candidates, out var selected)) return;
            if (!selected.IsMemoryPreparation) rt.Powers.ConsumeNextBasicBonus(selected);
            long generation = state.Generation;
            long sourceEpoch = selected.IsMemoryPreparation ? state.Epochs[selected.SourceMemory] : 0;
            rt.NewPowers.Pending.Add(() =>
            {
                if (!Alive(hero) || state.Generation != generation || victim == null || !victim.isActive
                    || victim.currentHealth <= 0 || victim.GetRelation(hero) != EntityRelation.Enemy) return;
                SynchronizeMemoryPrimedRelay(hero, state);
                if (selected.IsMemoryPreparation && (!state.Epochs.TryGetValue(selected.SourceMemory, out long current)
                    || current != sourceEpoch)) return;
                EnterGenerated(hero);
                try { hero.PureDamage(selected.Damage, 0f).SetElemental(null).SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim); }
                finally { ExitGenerated(hero); }
            });
        }

        private void ApplyRelayWindowDamage(HeroRuntime rt, ref DamageData damage, Entity target)
        {
            if (!_memoryPrimedRelay.TryGetValue(rt.Hero, out var state)) return;
            var packet = NativeAttributedDamagePacket.Current;
            if (packet == null || !packet.Admitted || packet.Victim != target || packet.Identity.OwnerId != rt.Hero.GetInstanceID()
                || !_memoryAttribution.IsCurrent(packet.Identity) || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || damage.IsAmountModifiedBy(typeof(GimmickRuntime))) return;
            SynchronizeMemoryPrimedRelay(rt.Hero, state);
            var hit = WithPreparationEpoch(packet.Identity.Event(MemoryEventKind.Hit, packet.Serial), state);
            float amplification = state.Relay.DamageAmplification(hit, Time.time);
            if (amplification > 0) damage.ApplyAmplification(amplification);
        }

        private void ClearMemoryPrimedRelay(Hero hero = null)
        {
            foreach (var pair in _memoryPrimedRelay)
            {
                if (!ReferenceEquals(hero, null) && pair.Key != hero) continue;
                pair.Value.Primed.Clear(); pair.Value.Relay.Clear(); pair.Value.Generation++;
            }
        }

        private void RemoveMemoryPrimedRelay(Hero hero)
        {
            ClearMemoryPrimedRelay(hero);
            if (!ReferenceEquals(hero, null) && _runtimes.TryGetValue(hero, out var rt)) rt.Powers.UsesMemoryPreparationLedger = false;
            if (!ReferenceEquals(hero, null)) _memoryPrimedRelay.Remove(hero);
        }
    }
}
