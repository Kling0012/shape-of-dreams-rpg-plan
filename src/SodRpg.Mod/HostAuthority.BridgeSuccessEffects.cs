using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class BridgeSuccessHostState
        {
            public PairComboRuntime Runtime;
            public IReadOnlyDictionary<string, int> EndpointRanks;
            public Func<string, string, decimal, decimal> TransformExpose;
            public readonly Dictionary<(string Pair, string Channel), AuthoredMechanismSpec> Wards =
                new Dictionary<(string, string), AuthoredMechanismSpec>();
        }
        private readonly Dictionary<Hero, BridgeSuccessHostState> _bridgeSuccessEffects = new Dictionary<Hero, BridgeSuccessHostState>();
        private void InitializeBridgeSuccessEffects() => MemoryActivationPublished += OnBridgeSuccessEvent;
        private void DisposeBridgeSuccessEffects()
        {
            MemoryActivationPublished -= OnBridgeSuccessEvent;
            _bridgeSuccessEffects.Clear();
        }
        internal void SetBridgeSuccessEffects(Hero hero, IEnumerable<BridgeSuccessDefinition> definitions, IReadOnlyDictionary<string, int> endpointRanks)
        {
            if (hero == null || definitions == null || endpointRanks == null) throw new ArgumentNullException();
            if (!_runtimes.TryGetValue(hero, out var heroRuntime)) throw new InvalidOperationException("Install the authoritative hero runtime before registering its bridge effects.");
            var copy = new List<BridgeSuccessDefinition>();
            foreach (var definition in definitions)
            {
                if (definition == null) throw new ArgumentException("A bridge definition is required.");
                if (_runtimes.TryGetValue(hero, out var runtime))
                    foreach (var legacy in runtime.Powers.Build.PairCombos)
                        if (legacy.Def.Id == definition.PairId) throw new InvalidOperationException("The legacy pair binding must be migrated before a success-effect replacement is registered.");
                ValidateBridgeNativeDefinition(definition);
                copy.Add(definition);
            }
            var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in endpointRanks) ranks.Add(pair.Key, pair.Value);
            if (!_bridgeSuccessEffects.TryGetValue(hero, out var state)) state = new BridgeSuccessHostState { Runtime = heroRuntime.PairCombos };
            if (state.TransformExpose == null) state.TransformExpose = (source, channel, units) =>
            {
                var result = TransformAuthoredPayload(hero, new KeystonePayload(KeystoneLayer.ModEffect, units / 100m,
                    new KeystoneCaps(100), KeystonePayloadKind.Gimmick, GimmickEffect.Expose, channel),
                    source, null, KeystoneSourceKind.NativeMemory);
                return result.Value * 100m;
            };
            state.Wards.Clear();
            foreach (var definition in copy)
                foreach (var payload in definition.Payloads)
                    if (payload.Kind == BridgePayloadKind.AlliedWard)
                        state.Wards[(definition.PairId, payload.ChannelId)] = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.AlliedWard,
                            ChannelId = payload.ChannelId, Ward = payload.Ward, UncappedValueUnits = payload.UncappedValueUnits,
                            UncappedDurationSeconds = payload.UncappedDurationSeconds, UncappedRadiusMetres = payload.UncappedRadiusMetres,
                            UncappedTargetCount = payload.UncappedTargetCount };
            state.Runtime.SetSuccessEffects(copy); state.EndpointRanks = ranks;
            state.Runtime.RefreshSuccessPrerequisites(CollectMechanismEquipment(hero, hero.GetInstanceID()), ranks);
            _bridgeSuccessEffects[hero] = state;
        }
        private static void ValidateBridgeNativeDefinition(BridgeSuccessDefinition definition)
        {
            if (definition.UsesNativeWindowLifetime)
                throw new InvalidOperationException("This bridge requires an exact native Ultimate-lifetime adapter that is not bound.");
            if (definition.SourcePhase != BridgeSourcePhase.Any
                && (definition.PayoffSource.Kind != MemorySelectorKind.Memory
                    || definition.PayoffSource.Memory != nameof(St_R_BaptismOfSun)))
                throw new InvalidOperationException("This bridge source has no verified native explosion-phase adapter.");
            if (definition.PayoffTrigger == MemoryEventKind.Kill && (definition.BasePayoff.Kind == BridgePayloadKind.Damage
                || HasDamageExtra(definition)))
                throw new InvalidOperationException("A kill-triggered damage bridge requires an explicitly bound area or surviving-target selector.");
        }
        private static bool HasDamageExtra(BridgeSuccessDefinition definition)
        {
            foreach (var extra in definition.Extras) if (extra.Kind == BridgePayloadKind.Damage) return true;
            return false;
        }
        private void OnBridgeSuccessEvent(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage)
        {
            if (!NetworkServer.active || !Alive(hero) || !_bridgeSuccessEffects.TryGetValue(hero, out var state)) return;
            var equipment = CollectMechanismEquipment(hero, notification.OwnerId);
            var buffers = RentMechanismDispatchBuffers();
            try
            {
            var transactions = buffers.Bridges;
            bool summons = _runtimes.TryGetValue(hero, out var rt) && HasOwnSummons(rt);
            state.Runtime.FireAttributed(notification, Time.time, nativeDamage, equipment, state.EndpointRanks, transactions,
                phase: NativeBridgeSourcePhase(notification), hasOwnedSummon: summons,
                damageTargetReady: BridgeDamageTargetReady(hero, victim));
            foreach (var transaction in transactions) ApplyBridgeSuccess(hero, victim, transaction);
            }
            finally { ReturnMechanismDispatchBuffers(buffers); }
        }
        private static BridgeSourcePhase NativeBridgeSourcePhase(MemoryActivationEvent notification)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (notification.SourceMemory != nameof(St_R_BaptismOfSun) || notification.DamagePacketId <= 0
                || packet == null || !packet.Admitted || packet.Serial != notification.DamagePacketId
                || packet.Identity.OwnerId != notification.OwnerId || packet.Identity.ActivationId != notification.ActivationId
                || packet.Identity.EquipmentEpoch != notification.EquipmentEpoch || packet.Identity.SourceMemory != notification.SourceMemory
                || packet.Actor == null || packet.Actor.GetType() != typeof(Ai_R_BaptismOfSun)) return BridgeSourcePhase.Any;
            var explosion = (Ai_R_BaptismOfSun)packet.Actor;
            if (packet.Identity.NativePayloadKind == NativePayloadKind.NativeEndingPhase && explosion.skipBuff)
                return BridgeSourcePhase.EndingExplosion;
            return packet.Identity.NativePayloadKind == NativePayloadKind.Skill && !explosion.skipBuff
                ? BridgeSourcePhase.InitialExplosion : BridgeSourcePhase.Any;
        }
        private void ApplyBridgeSuccess(Hero hero, Entity victim, BridgeSuccessTransaction transaction)
        {
            var equipment = CollectMechanismEquipment(hero, transaction.Notification.OwnerId);
            if (!Alive(hero) || !_bridgeSuccessEffects.TryGetValue(hero, out var state)
                || !transaction.IsCurrent(state.Runtime, equipment, state.EndpointRanks)) return;
            if (transaction.Notification.EventKind == MemoryEventKind.OwnedBasicAttackFired
                && (!_runtimes.TryGetValue(hero, out var firedOwner) || !HasOwnSummons(firedOwner))) return;
            for (int p = 0; p < transaction.Payloads.Count; p++)
                if (transaction.Payloads[p].Kind == BridgePayloadKind.Damage && !BridgeDamageTargetReady(hero, victim)) return;
            var buffers = RentMechanismDispatchBuffers();
            try
            {
            var recharge = buffers.Recharges;
            transaction.CreateRechargeRequests(equipment, state.EndpointRanks, recharge);
            foreach (var request in recharge) ApplyDirectedRecharge(hero, request);
            for (int p = 0; p < transaction.Payloads.Count; p++)
            {
                var payload = transaction.Payloads[p];
                if (!transaction.IsCurrent(state.Runtime, CollectMechanismEquipment(hero, transaction.Notification.OwnerId), state.EndpointRanks)) return;
                if (!_runtimes.TryGetValue(hero, out var owner)) return;
                if (payload.Kind == BridgePayloadKind.OrdinaryShield)
                {
                    var shield = TransformAuthoredPayload(hero, AuthoredKeystoneComposer.BridgePayload(payload),
                        transaction.Notification.SourceMemory, null, KeystoneSourceKind.NativeMemory);
                    AwardModShield(owner, hero, ModShieldPoolKind.Ordinary,
                        SupportStats.AmplifyShield(hero.maxHealth * (float)(shield.Value / 100m), owner.Powers.Build.Get(Stat.ShieldPower)),
                        (float)shield.DurationSeconds, transaction.Notification.SourceMemory, ModShieldEquipmentEpoch(owner));
                    continue;
                }
                if (payload.Kind == BridgePayloadKind.AlliedWard)
                {
                    // The success transaction already spent the pair's quota; the ward uses the C12 recipient, cap and pool rules.
                    var authored = state.Wards[(transaction.PairId, payload.ChannelId)];
                    DispatchAdmittedWard(owner, payload.Ward, transaction.Notification.SourceMemory, ModShieldEquipmentEpoch(owner),
                        authored: authored, sourceKind: KeystoneSourceKind.NativeMemory);
                    continue;
                }
                if (payload.Kind == BridgePayloadKind.Gimmick)
                {
                    DispatchBridgeGimmick(owner, victim, transaction, payload);
                    continue;
                }
                if (payload.Kind != BridgePayloadKind.Damage) continue;
                if (!transaction.IsCurrent(state.Runtime, CollectMechanismEquipment(hero, transaction.Notification.OwnerId), state.EndpointRanks)) return;
                if (!BridgeDamageTargetReady(hero, victim)) continue;
                float basis = payload.DamageBasis == BridgeDamageBasis.NativeHit ? transaction.NativeDamage : Math.Max(hero.Status.attackDamage, hero.Status.abilityPower);
                var effective = TransformAuthoredPayload(hero, AuthoredKeystoneComposer.BridgePayload(payload),
                    transaction.Notification.SourceMemory, null, KeystoneSourceKind.NativeMemory);
                float amount = TransformAuthoredGeneratedDamage(hero, basis * (float)(effective.Value / 100m),
                    transaction.Notification.SourceMemory, payload.ChannelId, GimmickEffect.None);
                _pairDamageDepth++;
                try
                {
                    var damage = payload.DamageBasis == BridgeDamageBasis.NativeHit ? hero.PureDamage(amount, 0f)
                        : hero.Status.abilityPower > hero.Status.attackDamage ? hero.MagicDamage(amount, 0f) : hero.PhysicalDamage(amount, 0f);
                    damage.SetElemental(null).SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim);
                }
                finally { _pairDamageDepth--; }
            }
            DispatchAuthoredBridgeChannels(hero, victim, transaction);
            }
            finally { ReturnMechanismDispatchBuffers(buffers); }
        }
        private static bool BridgeDamageTargetReady(Hero hero, Entity victim) => victim != null && victim.isActive
            && victim.currentHealth > 0f && victim.GetRelation(hero) == EntityRelation.Enemy;
        private void ClearBridgeSuccessEffects(Hero hero)
        {
            if (_bridgeSuccessEffects.TryGetValue(hero, out var state)) state.Runtime.ClearSuccessEffectsTransient();
        }
        internal float BridgeSuccessExposePercent(Hero hero, Entity victim)
        {
            if (!_bridgeSuccessEffects.TryGetValue(hero, out var state)) return 0;
            return (float)(state.Runtime.BridgeExposeUnits(AttributedVictimLifetime(victim), Time.time,
                CollectMechanismEquipment(hero, hero.GetInstanceID()), state.EndpointRanks, state.TransformExpose) / 100m);
        }
    }
}
