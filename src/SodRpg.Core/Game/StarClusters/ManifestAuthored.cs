using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    public static partial class StarClusters
    {
        // Migration rows intentionally omit layout/rank-cost data. Read the original tree,
        // never the currently installed tree, so regeneration cannot move saved nodes.
        internal static AuthoredStarDef ManifestRetained(string hero, string id, ClusterStarDef effect,
            string[] required, string[] requiredAny, AuthoredStarEdge[] edges, MemoryOwnership ownership,
            string source, string[] mechanisms, string notes)
        {
            var original = HeroSigils.BaselineTreeFor(hero).Single(x => x.Id == id);
            if (effect.MaxRank != original.MaxRank)
                throw new InvalidOperationException("Manifest changed retained rank limit: " + id);
            effect.RankCost = original.RankCost;
            if (effect.Options != null)
                foreach (var option in effect.Options) { option.MaxRank = original.MaxRank; option.RankCost = original.RankCost; }
            var retainedKey = effect.KeystoneDefinition;
            if (retainedKey != null && retainedKey.RetainedPower != Power.None)
            {
                // The retained Power comes from the baseline node alone; the node keeps carrying it so Build adds it once.
                if (!original.IsKeystone || original.Power != retainedKey.RetainedPower || original.PowerValue != retainedKey.RetainedPowerValue)
                    throw new InvalidOperationException("Manifest keystone Power differs from the baseline keystone: " + id);
                effect.Power = original.Power; effect.Amount = original.PowerValue;
            }
            // A retained outer bridge that carries a complete authored pair (ManifestNewPair) is a real bridge region too.
            var pair = PairCombos.ForBridge(id);
            bool authoredPair = effect.Mechanism?.Bridge != null;
            return new AuthoredStarDef
            {
                HeroKey = hero, LocalStarId = id,
                ClusterId = original.Cluster?.Id ?? id + ".migration",
                Region = original.Cluster?.Region ?? (original.RouteId != null ? ClusterRegion.Memory(original.RouteId)
                    : pair != null || authoredPair ? ClusterRegion.Bridge(id)
                    : new ClusterRegion { Kind = ClusterRegionKind.Keystone }),
                AnchorId = original.Cluster?.Anchor ?? (original.RouteId != null
                    ? HeroSigils.BaselineTreeFor(hero).First(x => x.RouteId == original.RouteId).Id : id),
                Shape = original.Cluster?.Shape ?? ClusterShape.Fan,
                RequiredStarIds = required, RequiredAnyStarIds = requiredAny, Edges = edges,
                MemoryOwnership = ownership, Effect = effect, RetainedLegacy = true,
                RequiresExplicitSelection = effect.Kind == ClusterStarKind.Choice,
                KeystoneDefinition = effect.KeystoneDefinition,
                SourceDocument = source, MechanismIds = mechanisms, Notes = notes
            };
        }

        // A legacy keystone's upside is its existing Power. It is read from the baseline node, never re-typed in the manifest.
        internal static KeystoneDefinition ManifestKeystone(string hero, string id, IEnumerable<string> requiredMemories,
            IEnumerable<AuthoredKeystoneSpec> upside, IEnumerable<AuthoredKeystoneSpec> downside,
            IEnumerable<string> prerequisites, int cost)
        {
            var original = HeroSigils.BaselineTreeFor(hero).Single(x => x.Id == id);
            if (!original.IsKeystone || original.Power == Power.None || original.PowerValue <= 0)
                throw new InvalidOperationException("Baseline keystone has no retained Power: " + id);
            return AuthoredKeystoneCompiler.Compile(id, requiredMemories, upside, downside, prerequisites, cost,
                retainedPower: original.Power, retainedPowerValue: original.PowerValue);
        }

        // A manifest migration row keeps the star's ID and ranks and changes its effect. The original per-rank cost
        // (keystones: Content.KeystoneCost) comes from the baseline tree, so a saved profile is refunded at what it paid.
        internal static LegacyStarMigration ManifestMigration(string hero, string id, int maxRank)
        {
            var original = HeroSigils.BaselineTreeFor(hero).Single(x => x.Id == id);
            if (original.MaxRank != maxRank) throw new InvalidOperationException("Manifest changed retained rank limit: " + id);
            return new LegacyStarMigration(id, maxRank, original.IsKeystone ? Content.KeystoneCost : original.RankCost, changedEffect: true);
        }

        private static MemoryEventKind ManifestPairTrigger(PairComboTrigger trigger)
        {
            switch (trigger)
            {
                case PairComboTrigger.OnUse: return MemoryEventKind.ConfirmedUse;
                case PairComboTrigger.OnHit: return MemoryEventKind.Hit;
                case PairComboTrigger.OnKill: return MemoryEventKind.Kill;
                case PairComboTrigger.OnCrit: return MemoryEventKind.CriticalHit;
                case PairComboTrigger.OnBasicAttack: return MemoryEventKind.OwnedBasicAttackFired;
                default: throw new InvalidOperationException("A pair requires a concrete native trigger.");
            }
        }

        // Reuse the real baseline pair, including endpoints, phase, interval and rank table.
        // A migrated direct receiver changes the gate/source explicitly; it is not a mark.
        internal static AuthoredMechanismSpec ManifestPair(string hero, string bridgeId,
            string directSource = null, string directRecipient = null, MemoryEventKind directTrigger = MemoryEventKind.ConfirmedUse,
            int[] directRankValues = null, MemoryEventKind? openingOverride = null)
        {
            var pair = PairCombos.ForBridge(bridgeId);
            if (pair == null || pair.HeroKey != hero) throw new InvalidOperationException("Unknown manifest pair: " + bridgeId);
            bool direct = directSource != null;
            if (direct != (directRecipient != null) || direct != (directRankValues != null))
                throw new InvalidOperationException("Incomplete manifest direct-receiver pair: " + bridgeId);
            string opening = direct ? directSource : pair.TriggerMemory;
            string payoff = direct ? directSource : pair.Step == PairComboStep.None ? pair.TriggerMemory : pair.PayoffMemory;
            // openingOverride is a design-table migration of the mark trigger (Aurena B2: Crit -> Hit).
            var openingTrigger = direct ? directTrigger : openingOverride ?? ManifestPairTrigger(pair.Trigger);
            var payoffTrigger = direct ? directTrigger : ManifestPairTrigger(pair.Step == PairComboStep.None ? pair.Trigger : pair.PayoffTrigger);
            var budget = payoffTrigger == MemoryEventKind.Kill ? AttributionBudget.PerKill
                : payoffTrigger == MemoryEventKind.OwnedBasicAttackFired ? AttributionBudget.PerOwnedBasicAttack
                : pair.OncePerVictim && !pair.OncePerActivation ? AttributionBudget.PerActivationVictim : AttributionBudget.PerActivation;
            var values = direct ? directRankValues : pair.TableRankValues.Select(x => checked(x * 100)).ToArray();
            var effect = direct ? GimmickEffect.Recharge : pair.Effect;
            BridgePayload payload;
            if (effect == GimmickEffect.Recharge || effect == GimmickEffect.RechargeOther)
                payload = new BridgePayload(bridgeId, BridgePayloadKind.Recharge, new[] { values[0] },
                    recipient: MemorySelector.Parse(direct ? directRecipient : effect == GimmickEffect.RechargeOther ? "@OTHER" : pair.RechargeMemory));
            else if (effect == GimmickEffect.Shield)
                payload = new BridgePayload(bridgeId, BridgePayloadKind.OrdinaryShield, new[] { values[0] }, capUnits: 1500, durationSeconds: PairCombos.Duration);
            else
                payload = new BridgePayload(bridgeId, BridgePayloadKind.Gimmick, new[] { values[0] },
                    capUnits: checked(Gimmicks.Cap(effect) * 100),
                    gimmick: new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = values[0] / 100m, Arg = pair.Arg });
            var definition = new BridgeSuccessDefinition(pair.Id,
                new[] { new BridgeEndpointRequirement(pair.StarA, pair.RouteA), new BridgeEndpointRequirement(pair.StarB, pair.RouteB) },
                1, direct || pair.Step == PairComboStep.None ? BridgeGateKind.DirectReceiver
                    : pair.Step == PairComboStep.Mark ? BridgeGateKind.Mark : BridgeGateKind.Window,
                MemorySelector.Parse(opening), openingTrigger, MemorySelector.Parse(payoff), payoffTrigger,
                payload, Array.Empty<BridgePayload>(), budget,
                direct ? BridgeSourcePhase.Any : pair.PayoffHitKind == PairComboHitKind.InitialExplosion ? BridgeSourcePhase.InitialExplosion
                    : pair.PayoffHitKind == PairComboHitKind.TerminalExplosion ? BridgeSourcePhase.EndingExplosion : BridgeSourcePhase.Any,
                usesNativeWindowLifetime: !direct && pair.Step == PairComboStep.Window && pair.WindowDuration != PairCombos.Duration,
                cooldownSeconds: direct ? 0 : pair.Cooldown, windowSeconds: pair.WindowDuration);
            return new AuthoredMechanismSpec
            {
                Kind = AuthoredMechanismKind.BridgeSuccess, ChannelId = bridgeId,
                Source = definition.PayoffSource, Trigger = payoffTrigger, Budget = budget,
                Bridge = definition, ValuesByRank = values, Once = pair.OncePerActivation,
                RequiredMemories = new[] { pair.RouteA, pair.RouteB }
            };
        }

        // A newly authored real pair for a redesigned outer bridge whose original node never was a PairCombos entry
        // (Bismuth renewal). It owns exactly two distinct hero route endpoints with explicit star ids, and the
        // direct-receiver gate has no mark, so the three-rank mark limit does not apply to its retained ranks.
        internal static AuthoredMechanismSpec ManifestNewDirectRechargePair(string hero, string bridgeId, string pairId,
            string starA, string memoryA, string starB, string memoryB,
            string source, MemoryEventKind trigger, string recipient, int[] rankValues, bool oncePerActivation)
        {
            if (rankValues == null || rankValues.Length == 0) throw new InvalidOperationException("A retained bridge receiver requires its exact rank table: " + bridgeId);
            var budget = trigger == MemoryEventKind.Kill ? AttributionBudget.PerKill
                : trigger == MemoryEventKind.OwnedBasicAttackFired ? AttributionBudget.PerOwnedBasicAttack : AttributionBudget.PerActivation;
            var payload = new BridgePayload(bridgeId, BridgePayloadKind.Recharge, new[] { rankValues[0] }, recipient: MemorySelector.Parse(recipient));
            var definition = new BridgeSuccessDefinition(pairId,
                new[] { new BridgeEndpointRequirement(starA, memoryA), new BridgeEndpointRequirement(starB, memoryB) },
                1, BridgeGateKind.DirectReceiver, MemorySelector.Parse(source), trigger, MemorySelector.Parse(source), trigger,
                payload, Array.Empty<BridgePayload>(), budget);
            return new AuthoredMechanismSpec
            {
                Kind = AuthoredMechanismKind.BridgeSuccess, ChannelId = bridgeId,
                Source = definition.PayoffSource, Trigger = trigger, Budget = budget,
                Bridge = definition, ValuesByRank = rankValues, Once = oncePerActivation,
                RequiredMemories = new[] { memoryA, memoryB }
            };
        }

        // A newly authored pair for a redesigned outer ring star that never was a PairCombos entry (Aurena renewal,
        // Bismuth resolve, Nachia renewal). Two distinct hero route endpoints with explicit star ids; the center is a
        // retained legacy ring star, so a five-rank marked/window pair keeps its retained ranks (Expose 2/3/4/5/6%).
        internal static AuthoredMechanismSpec ManifestNewPair(string hero, string bridgeId, string pairId,
            string starA, string memoryA, string starB, string memoryB, BridgeGateKind gate,
            string opening, MemoryEventKind openingTrigger, string payoff, MemoryEventKind payoffTrigger,
            BridgePayloadKind payloadKind, string recipient, GimmickEffect effect, int arg, int[] rankValues, bool oncePerActivation)
        {
            if (rankValues == null || rankValues.Length == 0) throw new InvalidOperationException("A retained bridge requires its exact rank table: " + bridgeId);
            var budget = payoffTrigger == MemoryEventKind.Kill ? AttributionBudget.PerKill
                : payoffTrigger == MemoryEventKind.OwnedBasicAttackFired ? AttributionBudget.PerOwnedBasicAttack : AttributionBudget.PerActivation;
            BridgePayload payload;
            switch (payloadKind)
            {
                case BridgePayloadKind.Recharge:
                    payload = new BridgePayload(bridgeId, payloadKind, new[] { rankValues[0] }, recipient: MemorySelector.Parse(recipient)); break;
                case BridgePayloadKind.AlliedWard:
                    payload = new BridgePayload(bridgeId, payloadKind, new[] { rankValues[0] }, ward: new AlliedWardDefinition(bridgeId,
                        WardRecipientKind.AlliedTravelers, WardAmountBasis.CasterMaxOffense, ModShieldPoolKind.Allied, rankValues[0], true)); break;
                case BridgePayloadKind.Gimmick:
                    payload = new BridgePayload(bridgeId, payloadKind, new[] { rankValues[0] }, capUnits: checked(Gimmicks.Cap(effect) * 100),
                        gimmick: new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = rankValues[0] / 100m, Arg = arg }); break;
                default: throw new InvalidOperationException("Unsupported authored pair payload: " + payloadKind);
            }
            var definition = new BridgeSuccessDefinition(pairId,
                new[] { new BridgeEndpointRequirement(starA, memoryA), new BridgeEndpointRequirement(starB, memoryB) },
                1, gate, MemorySelector.Parse(opening), openingTrigger, MemorySelector.Parse(payoff), payoffTrigger,
                payload, Array.Empty<BridgePayload>(), budget, retainedFiveRanks: gate != BridgeGateKind.DirectReceiver && rankValues.Length == 5);
            return new AuthoredMechanismSpec
            {
                Kind = AuthoredMechanismKind.BridgeSuccess, ChannelId = bridgeId,
                Source = definition.PayoffSource, Trigger = payoffTrigger, Budget = budget,
                Bridge = definition, ValuesByRank = rankValues, Once = oncePerActivation,
                RequiredMemories = new[] { memoryA, memoryB }
            };
        }

        private static AuthoredStarDef ManifestBaselinePair(string hero, string bridgeId)
        {
            var original = HeroSigils.BaselineTreeFor(hero).Single(x => x.Id == bridgeId);
            var effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = original.Name,
                MaxRank = original.MaxRank, RankCost = original.RankCost, Mechanism = ManifestPair(hero, bridgeId) };
            return ManifestRetained(hero, bridgeId, effect, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<AuthoredStarEdge>(), null, "src/SodRpg.Core/Game/PairCombos.cs", new[] { "C05" }, "");
        }

        private static AuthoredStarEdge ManifestRouteEntry(HeroTreeLayout baselineLayout, string targetId)
        {
            var target = baselineLayout.Nodes.Single(x => x.Id == targetId).Talent;
            var entry = baselineLayout.Nodes.Single(x => x.Talent?.RouteId == target.RouteId && x.Talent.RouteOrder == 1);
            var core = entry.Neighbors.Select(x => baselineLayout.Nodes[x])
                .Single(x => x.Talent != null && x.Talent.RouteId != target.RouteId);
            return new AuthoredStarEdge(core.Id, targetId);
        }
    }
}
