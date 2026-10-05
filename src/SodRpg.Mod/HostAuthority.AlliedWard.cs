using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        /// <summary>Payload sink for a C02-admitted activation; registry/codec producers supply the explicit typed definition.</summary>
        private int DispatchAdmittedWard(HeroRuntime owner, AlliedWardDefinition definition, string sourceMemory, long shieldEquipmentEpoch,
            AuthoredMechanismSpec authored = null, KeystoneSourceKind sourceKind = KeystoneSourceKind.NativeMemory,
            string quota = null, MemoryActivationEvent notification = default, AttributionBudget budget = AttributionBudget.PerActivation)
        {
            if (!NetworkServer.active) throw new InvalidOperationException("Allied wards require host authority.");
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!Alive(owner.Hero) || FindMemory(owner.Hero, sourceMemory) == null
                || shieldEquipmentEpoch != ModShieldEquipmentEpoch(owner)) return 0;
            var payload = authored != null ? AuthoredKeystoneComposer.MechanismPayload(authored)
                : AuthoredKeystoneComposer.MechanismPayload(new AuthoredMechanismSpec
                    { Kind = AuthoredMechanismKind.AlliedWard, ChannelId = definition.ChannelId, Ward = definition });
            var transformed = TransformAuthoredPayload(owner.Hero, payload, sourceMemory, null, sourceKind,
                definition.RecipientKind == WardRecipientKind.OwnedSummons ? KeystoneRecipientKind.OwnedSummon : KeystoneRecipientKind.AlliedHero);
            if (transformed.Value <= 0 || transformed.TargetCount < 1) return 0;
            definition = new AlliedWardDefinition(definition.ChannelId, definition.RecipientKind, definition.AmountBasis,
                definition.PoolKind, transformed.Value * 100m, definition.IncludeOwner, (float)transformed.RadiusMetres,
                (float)transformed.DurationSeconds, definition.BaseTargets,
                Math.Max(0, transformed.TargetCount - definition.BaseTargets), definition.MaxTargets, definition.Limits, definition.Budget);
            var buffers = RentMechanismDispatchBuffers();
            try
            {
            var entities = buffers.Entities;
            if (definition.RecipientKind == WardRecipientKind.AlliedTravelers)
            {
                foreach (var player in DewPlayer.gamePlayers)
                {
                    var hero = player != null ? player.hero : null;
                    if (hero != null) entities[hero.GetInstanceID()] = hero;
                }
            }
            else
            {
                foreach (var summon in owner.Hero.summons)
                    if (summon != null && summon.hero == owner.Hero) entities[summon.GetInstanceID()] = summon;
            }
            var candidates = buffers.Candidates;
            foreach (var pair in entities)
            {
                var entity = pair.Value;
                var summon = entity as Summon;
                candidates.Add(new WardCandidate(pair.Key, summon != null && summon.hero != null ? summon.hero.GetInstanceID() : 0,
                    entity is Hero, summon != null, entity is Hero traveler ? Alive(traveler) : entity.isActive, entity.GetRelation(owner.Hero) == EntityRelation.Ally,
                    entity.currentHealth, entity.maxHealth, (entity.agentPosition - owner.Hero.agentPosition).sqrMagnitude));
            }
            var awards = buffers.Awards;
            AlliedWard.Select(definition, owner.Hero.GetInstanceID(), true,
                owner.Hero.Status.attackDamage, owner.Hero.Status.abilityPower, candidates, awards, buffers.Eligible, buffers.CandidateIds);
            if (awards.Count == 0 || quota != null && !_memoryAttribution.TrySpend(quota, budget, notification, true)) return 0;
            int count = 0;
            foreach (var award in awards)
            {
                var recipient = entities[award.RecipientId];
                // Validate again at dispatch: the previous recipient's native shield event may alter these facts.
                if (!recipient.isActive || recipient.currentHealth <= 0 || recipient is Hero traveler && !Alive(traveler)
                    || recipient != owner.Hero && recipient.GetRelation(owner.Hero) != EntityRelation.Ally
                    || recipient is Summon summon && summon.hero != owner.Hero
                    || (recipient.agentPosition - owner.Hero.agentPosition).sqrMagnitude > definition.RadiusMetres * definition.RadiusMetres) continue;
                float raw = definition.AmountBasis == WardAmountBasis.RecipientMaxHP
                    ? recipient.maxHealth * (float)(definition.ValueUnits / 10000m) : award.RawAmount;
                if (AwardModShield(owner, recipient, definition.PoolKind,
                    SupportStats.AmplifyShield(raw, owner.Powers.Build.Get(SodRpg.Core.Game.Stat.ShieldPower)),
                    definition.DurationSeconds, sourceMemory, shieldEquipmentEpoch)) count++;
            }
            return count;
            }
            finally { ReturnMechanismDispatchBuffers(buffers); }
        }
    }
}
