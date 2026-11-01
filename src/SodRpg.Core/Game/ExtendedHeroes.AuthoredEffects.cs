using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ExtendedHeroes
    {
        private static readonly string[] ShippedHeroKeys = { "Hero_Aurena", "Hero_Bismuth", "Hero_Cetus", "Hero_Husk", "Hero_Lacerta", "Hero_Mist", "Hero_Nachia", "Hero_Vesper", "Hero_Yubar" };

        internal static void DescribeAuthoredEffectReferences(System.Text.StringBuilder sb)
        {
            sb.Append("  authoredEffect references (fixed shipped values/conditions; maxRank <= reference, rankCost >= reference):\n");
            sb.Append("    Native selected-key adapters and unbound native bridge adapters are rejected; native-specific sources cannot be rebound. References remain subject to full binding validation.\n");
            foreach (string hero in ShippedHeroKeys)
                foreach (var node in HeroSigils.TreeFor(hero))
                    foreach (var source in node.IsChoice ? node.Choices : new[] { node })
                    {
                        if (source.RunGrowth != null || source.RunGrowthModifier != null
                            || source.Mechanism == null && source.KeystoneDefinition == null && source.EffectChannel == null && source.PairCombo == null) continue;
                        sb.Append("    ").Append(hero).Append('/').Append(node.Id);
                        if (node.IsChoice) sb.Append(" option=").Append(node.Choices.ToList().IndexOf(source));
                        sb.Append(": ").Append(source.Mechanism?.Kind.ToString() ?? (source.IsKeystone ? "Keystone" : source.PairCombo != null ? "Pair" : "EffectChannel"))
                            .Append(", maxRank=").Append(source.MaxRank).Append(", minimum rankCost=").Append(source.RankCost)
                            .Append(", keystone=").Append(source.IsKeystone).Append('\n');
                    }
        }

        private sealed class EffectBindings
        {
            internal string Prefix;
            internal readonly Dictionary<string, string> Stars = new Dictionary<string, string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, string> Memories = new Dictionary<string, string>(StringComparer.Ordinal);
            internal string Memory(string value) => value != null && Memories.TryGetValue(value, out var mapped) ? mapped : value;
            internal string Star(string value)
            {
                if (!Stars.TryGetValue(value, out var mapped)) throw new FormatException("authored effect requires a star binding: " + value);
                return mapped;
            }
            internal string EffectId(string value) => Stars.ContainsKey(value) ? Star(value)
                : ShippedHeroKeys.SelectMany(HeroSigils.TreeFor).Any(t => t.Id == value)
                    ? throw new FormatException("authored effect targets an unbound star: " + value) : value;
            internal string Channel(string value)
            {
                if (value == null) return null;
                if (Stars.TryGetValue(value, out var mapped)) return mapped;
                return Prefix + "channel." + StarClusters.RegistryHash(value);
            }
            internal MemorySelector Selector(MemorySelector value)
            {
                if (value == null) return null;
                string expression = value.Expression;
                // Replace complete selector tokens, never substrings of another memory name.
                return MemorySelector.Parse(System.Text.RegularExpressions.Regex.Replace(expression, "[A-Za-z0-9_]+", match => Memory(match.Value)));
            }
        }

        private static EffectBindings ReadEffectBindings(JsonObject tree, string heroKey, IReadOnlyDictionary<string, Txt> memories)
        {
            var result = new EffectBindings { Prefix = "x." + heroKey + "." };
            if (!tree.TryGet("effectBindings", out var raw)) return result;
            if (!(raw is JsonObject bindings)) throw new FormatException("effectBindings must be an object");
            foreach (var entry in bindings.Properties)
            {
                if (entry.Key != "stars" && entry.Key != "memories") throw new FormatException("unknown effectBindings field: " + entry.Key);
                if (!(entry.Value is JsonObject map) || map.Properties.Count > Limits().StarsMax)
                    throw new FormatException("effectBindings maps exceed the shipped star count or are not objects");
                foreach (var pair in map.Properties)
                {
                    if (entry.Key == "stars")
                    {
                        if (!Gimmicks.ValidStarId(pair.Key) || !(pair.Value is string local) || !ValidNodeId(local))
                            throw new FormatException("invalid authored star binding");
                        result.Stars.Add(pair.Key, result.Prefix + local);
                    }
                    else
                    {
                        if (!Links.IsBuiltinMemory(pair.Key)) throw new FormatException("memory binding source must be a shipped memory");
                        result.Memories.Add(pair.Key, ReadMemoryReference(pair.Value, memories));
                    }
                }
            }
            if (result.Stars.Values.Distinct(StringComparer.Ordinal).Count() != result.Stars.Count)
                throw new FormatException("authored star bindings must be one-to-one");
            if (result.Memories.Values.Distinct(StringComparer.Ordinal).Count() != result.Memories.Count
                || result.Memories.Any(p => p.Key != p.Value && Links.IsBuiltinMemory(p.Value) && !result.Memories.ContainsKey(p.Value)))
                throw new FormatException("authored memory bindings must be one-to-one, including unchanged shipped memories");
            return result;
        }

        private static TalentDef ReadAuthoredEffect(object raw, string id, Txt name, int maxRank, int rankCost, bool keystone, EffectBindings bindings)
        {
            if (!(raw is JsonObject reference) || !reference.TryGet("star", out var starRaw) || !(starRaw is string starId))
                throw new FormatException("authoredEffect requires a shipped star reference");
            if (!reference.TryGet("hero", out var heroRaw) || !(heroRaw is string sourceHero) || !ShippedHeroKeys.Contains(sourceHero))
                throw new FormatException("authoredEffect.hero must name one of the nine shipped travelers");
            foreach (var field in reference.Properties)
                if (field.Key != "hero" && field.Key != "star" && field.Key != "option") throw new FormatException("unknown authoredEffect field: " + field.Key);
            var source = HeroSigils.TreeFor(sourceHero).FirstOrDefault(t => t.Id == starId);
            if (source == null) throw new FormatException("authoredEffect must name a star from the nine shipped travelers");
            if (reference.TryGet("option", out var optionRaw))
            {
                if (!(optionRaw is long option) || option < 0 || option >= source.Choices.Count) throw new FormatException("invalid authoredEffect option index");
                source = source.Choices[(int)option];
            }
            if (source.IsChoice || source.RunGrowth != null || source.RunGrowthModifier != null
                || source.Mechanism == null && source.KeystoneDefinition == null && source.EffectChannel == null && source.PairCombo == null)
                throw new FormatException("authoredEffect requires an existing typed mechanism, keystone, channel or pair; RunGrowth is unsupported");
            if (source.IsKeystone != keystone || maxRank > source.MaxRank || rankCost < source.RankCost)
                throw new FormatException("authoredEffect exceeds the reference rank limit, reduces its rank cost or changes keystone kind");
            if (bindings.Star(starId) != id) throw new FormatException("authoredEffect source must bind to its owning node");
            var talent = source.IsKeystone
                ? source.Power != Power.None ? new TalentDef(id, source.Route, name, source.Power, source.PowerValue, source.Description)
                    : new TalentDef(id, source.Route, name, source.Stat, source.PerRank, source.Description)
                : source.LinkPerRank != null ? new TalentDef(id, source.Route, name, new LinkDef { Kind = source.LinkPerRank.Kind,
                    ValueMilli = source.LinkPerRank.ValueMilli, Requires = source.LinkPerRank.Requires.Select(bindings.Memory).ToArray() }, maxRank)
                : source.RankPower != Power.None ? new TalentDef(id, source.Route, name, source.RankPower, source.PerRank, maxRank)
                : new TalentDef(id, source.Route, name, source.Stat, source.PerRank, maxRank);
            talent.ConnectorEffectSource = source.Id;
            if (source.AuthoredStar != null)
                talent.AuthoredStar = new AuthoredStarDef { LocalStarId = id, RetainedLegacy = true,
                    RequiredStarIds = source.AuthoredStar.RequiredStarIds.Select(bindings.Star).ToArray(),
                    RequiredAnyStarIds = source.AuthoredStar.RequiredAnyStarIds.Select(bindings.Star).ToArray() };
            if (source.ClusterStar != null)
                talent.ClusterStar = new ClusterStarDef { Kind = source.ClusterStar.Kind, Amount = source.ClusterStar.Amount,
                    Param = source.ClusterStar.Param, Power = source.ClusterStar.Power, Stat = source.ClusterStar.Stat };
            talent.RouteMemory = bindings.Memory(source.RouteMemory);
            talent.RouteId = source.RouteId == null ? null : bindings.Channel(source.RouteId);
            talent.Gimmick = source.Gimmick?.Copy();
            talent.GimmickBoost = source.GimmickBoost;
            talent.GimmickParameter = source.GimmickParameter;
            talent.GimmickParamAmount = source.GimmickParamAmount;
            talent.Mechanism = BindMechanism(source.Mechanism ?? (source.PairCombo == null ? null : StarClusters.ManifestPair(source.HeroKey, source.Id)), bindings);
            talent.KeystoneDefinition = BindKeystone(source.KeystoneDefinition, id, rankCost, bindings);
            if (source.EffectChannel != null)
            {
                var c = source.EffectChannel;
                if (c.PairSuccessId != null) throw new FormatException("pair-conditioned legacy channels require a typed bridge reference");
                talent.EffectChannel = new EffectChannelDef { OwnerId = c.OwnerId, SourceMemory = bindings.Memory(c.SourceMemory),
                    ReceiverMemory = bindings.Memory(c.ReceiverMemory), ChannelId = bindings.Channel(c.ChannelId),
                    EquipmentRequirements = c.EquipmentRequirements.Select(bindings.Memory).ToArray(), ActivationBudget = c.ActivationBudget,
                    ClockPolicy = c.ClockPolicy, ScopeKind = c.ScopeKind };
            }
            return talent;
        }

        private static AuthoredMechanismSpec BindMechanism(AuthoredMechanismSpec original, EffectBindings b)
        {
            if (original == null) return null;
            var s = original.Copy();
            s.ValuesByRank = original.ValuesByRank.ToArray();
            if (s.Kind == AuthoredMechanismKind.StunSourceFilter || s.Kind == AuthoredMechanismKind.SacrificeShield)
                throw new FormatException("native selected-key adapter cannot be transferred: " + s.Kind);
            if (s.Trigger == MemoryEventKind.OwnedBasicAttackFired && b.Memory("St_D_CircleOfLife") != "St_D_CircleOfLife")
                throw new FormatException("owned basic-attack fired adapter cannot rebind St_D_CircleOfLife");
            if (s.Tuning != null && b.Memory(s.Tuning.Memory) != s.Tuning.Memory
                || s.Relay != null && b.Memory(RelayWindowDefinition.SourceMemory) != RelayWindowDefinition.SourceMemory
                || s.IdentityStrike != null && (s.IdentityStrike.Trigger == IdentityStrikeTrigger.DashAttackBonusAsMemory
                    || s.IdentityStrike.IsCriticalMechanism || s.IdentityStrike.BonusSpeedUnitsPerPercent > 0)
                    && b.Memory(s.IdentityStrike.Identity) != s.IdentityStrike.Identity)
                throw new FormatException("native-specific mechanism cannot rebind its source memory: " + s.Kind);
            s.ChannelId = b.Channel(s.ChannelId); s.Source = b.Selector(s.Source); s.PairId = b.Channel(s.PairId);
            s.Replaces = s.Replaces.Select(b.Star).ToArray(); s.RequiredMemories = s.RequiredMemories.Select(b.Memory).ToArray();
            s.TriggerByIdentity = s.TriggerByIdentity.ToDictionary(p => b.Memory(p.Key), p => p.Value, StringComparer.Ordinal);
            if (s.Recharge != null)
            {
                var c = s.Recharge;
                s.Recharge = new DirectedRechargeChannel(b.Channel(c.ChannelId), b.Selector(c.Source), c.SourceTrigger, b.Selector(c.Recipient),
                    new[] { c.ValueUnits }, c.Budget, c.ProbabilityUnits, c.EveryN, c.Condition, c.RequiredElementTypes, c.ModifierUnits, c.ModifierScope, c.CapUnits);
            }
            if (s.Primed != null)
            {
                var p = s.Primed; s.Primed = new MemoryPrimedDefinition(b.Channel(p.ChannelId), b.Memory(p.SourceMemory), p.Trigger, p.ValueUnits, p.DurationSeconds, p.Budget);
            }
            if (s.Relay != null)
            {
                var r = s.Relay;
                s.Relay = r.EffectiveDurationSeconds.HasValue ? RelayWindowDefinition.FromEffective(b.Channel(r.ChannelId), b.Memory(r.TargetMemory), r.ValueUnits, r.EffectiveDurationSeconds.Value)
                    : new RelayWindowDefinition(b.Channel(r.ChannelId), b.Memory(r.TargetMemory), r.ValueUnits, r.DurationModifierUnits, r.QuietRelay);
            }
            if (s.Ward != null) s.Ward = BindWard(s.Ward, b);
            if (s.Dividend != null)
            {
                var d = s.Dividend; s.Dividend = PressureDividendChannel.FromEffective(b.Memory(d.SourceMemory), d.RequiredMemories.Select(b.Memory), d.ContributorIds.Select(b.Star), d.ProbabilityUnits);
            }
            if (s.Tuning != null) s.Tuning = new MemoryTuningDefinition(b.Channel(s.Tuning.ChannelId), s.Tuning.Kind, s.Tuning.ValueUnits);
            if (s.IdentityStrike != null)
            {
                var i = s.IdentityStrike;
                s.IdentityStrike = new IdentityStrikeDefinition(b.Channel(i.ChannelId), b.Memory(i.Identity), i.Trigger, i.EveryN, i.WindowSeconds,
                    i.AdUnits, i.BonusSpeedUnitsPerPercent, i.Element, i.Shape, i.RangeMetres, i.WidthOrArc, i.MaxTargets, i.Basis);
            }
            if (s.Bridge != null)
            {
                var c = s.Bridge;
                if (c.UsesNativeWindowLifetime
                    || c.SourcePhase != BridgeSourcePhase.Any && (c.PayoffSource.Kind != MemorySelectorKind.Memory || c.PayoffSource.Memory != "St_R_BaptismOfSun")
                    || c.PayoffTrigger == MemoryEventKind.Kill && (c.BasePayoff.Kind == BridgePayloadKind.Damage || c.Extras.Any(p => p.Kind == BridgePayloadKind.Damage)))
                    throw new FormatException("bridge requires a native adapter that is not bound");
                if ((c.SourcePhase != BridgeSourcePhase.Any || c.UsesNativeWindowLifetime)
                    && (b.Selector(c.OpeningSource).Expression != c.OpeningSource.Expression || b.Selector(c.PayoffSource).Expression != c.PayoffSource.Expression))
                    throw new FormatException("native bridge phase/lifetime cannot rebind its source memory");
                s.Bridge = new BridgeSuccessDefinition(b.Channel(c.PairId), c.Endpoints.Select(e => new BridgeEndpointRequirement(b.Star(e.StarId), b.Memory(e.Memory), e.MinimumRank)),
                    c.Rank, c.GateKind, b.Selector(c.OpeningSource), c.OpeningTrigger, b.Selector(c.PayoffSource), c.PayoffTrigger,
                    BindPayload(c.BasePayoff, b), c.Extras.Select(p => BindPayload(p, b)), c.Budget, c.SourcePhase, c.UsesNativeWindowLifetime,
                    c.CooldownSeconds, c.WindowSeconds, c.RetainedFiveRanks, c.MarkSeconds, c.WindowLifetimeScale);
            }
            AuthoredMechanisms.Validate(s);
            return s;
        }

        private static AlliedWardDefinition BindWard(AlliedWardDefinition w, EffectBindings b) => new AlliedWardDefinition(b.Channel(w.ChannelId),
            w.RecipientKind, w.AmountBasis, w.PoolKind, w.ValueUnits, w.IncludeOwner, w.RadiusMetres, w.DurationSeconds,
            w.BaseTargets, w.Targets - w.BaseTargets, w.MaxTargets, w.Limits, w.Budget);

        private static BridgePayload BindPayload(BridgePayload p, EffectBindings b) => BridgePayload.FromEffective(b.Channel(p.ChannelId), p.Kind, p.ValueUnits,
            b.Selector(p.Recipient), p.DamageBasis, p.DurationSeconds, p.Gimmick?.Copy(), p.UncappedValueUnits, p.UncappedProbabilityUnits,
            p.CapUnits, p.UncappedDurationSeconds, p.UncappedRadiusMetres, p.UncappedTargetCount, p.DurationCapSeconds, p.Ward == null ? null : BindWard(p.Ward, b));

        private static KeystoneDefinition BindKeystone(KeystoneDefinition k, string id, int rankCost, EffectBindings b)
        {
            if (k == null) return null;
            if (k.Payloads.Contains(KeystonePayloadKind.SacrificeShield)) throw new FormatException("native selected-key SacrificeShield cannot be transferred");
            string fixedMemory = k.KeystoneId == CoopKeystones.Id(CoopKeystoneKind.SharedCrystalField) ? "St_QR_InfernalTales"
                : k.KeystoneId == CoopKeystones.Id(CoopKeystoneKind.ShieldWall) ? "St_R_BaptismOfSun"
                : k.KeystoneId == CoopKeystones.Id(CoopKeystoneKind.ShadowCue) ? "St_D_TheKillingFlow" : null;
            if (fixedMemory != null && b.Memory(fixedMemory) != fixedMemory)
                throw new FormatException("native cooperative keystone cannot rebind its source memory: " + fixedMemory);
            KeystoneTransform Bind(KeystoneTransform t)
            {
                var s = t.Scope;
                var scope = new KeystoneScope(s.TargetMemorySet.Select(b.Memory), s.TargetEffectSet,
                    s.TargetEffectIds.Select(b.EffectId), s.ReceiverMemorySet.Select(b.Memory), s.Recipient, s.PayloadKind, s.Argument,
                    s.SourceKind, s.SourceSelectors.Select(b.Selector), s.ReceiverSelectors.Select(b.Selector));
                switch (t.Operation)
                {
                    case KeystoneOperation.Scale: return KeystoneTransform.Scale(t.TargetLayer, t.Field, t.MagnitudeUnits, scope);
                    case KeystoneOperation.SetSeconds: return KeystoneTransform.SetSeconds(t.Field, t.Seconds, scope);
                    case KeystoneOperation.AddTargets: return KeystoneTransform.AddTargets(t.Count, scope);
                    case KeystoneOperation.SetEveryN: return KeystoneTransform.SetEveryN(t.Count, scope);
                    case KeystoneOperation.Set: return KeystoneTransform.Set(t.TargetLayer, t.Field, t.Seconds, scope, t.ExpectedFrom);
                    case KeystoneOperation.Add: return KeystoneTransform.Add(t.TargetLayer, t.Field, t.Seconds, scope, t.Maximum);
                    default: throw new FormatException("unknown shipped keystone operation");
                }
            }
            return new KeystoneDefinition(id, k.RequiredMemories.Select(b.Memory), k.Upside.Select(Bind), k.Prerequisites.Select(b.Star),
                k.Payloads, Math.Max(k.Cost, rankCost), k.Grants.Select(g => BindMechanism(g, b)), k.RetainedPower, k.RetainedPowerValue);
        }

        private static readonly Dictionary<string, PairComboDef> PairsByBridge = new Dictionary<string, PairComboDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PairComboDef> PairsById = new Dictionary<string, PairComboDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> EffectSources = new Dictionary<string, string>(StringComparer.Ordinal);

        private static void IndexAuthoredEffects(ExtendedHeroDef hero)
        {
            if (hero.Tree == null) return;
            foreach (var parent in hero.Tree)
            foreach (var node in parent.IsChoice ? parent.Choices : new[] { parent })
            {
                if (node.ConnectorEffectSource != null) EffectSources[node.Id] = node.ConnectorEffectSource;
                var b = node.Mechanism?.Bridge;
                if (b == null) continue;
                var pair = new PairComboDef { Id = b.PairId, BridgeId = node.Id, HeroKey = hero.Key, AuthoredDefinition = b,
                    RouteA = b.Endpoints[0].Memory, RouteB = b.Endpoints[1].Memory,
                    StarA = b.Endpoints[0].StarId, StarB = b.Endpoints[1].StarId, Name = node.Name,
                    TriggerMemory = b.OpeningSource.Memory, PayoffMemory = b.PayoffSource.Memory };
                // Options are selected typed effects, never unconditional legacy bridge nodes.
                if (!parent.IsChoice) PairsByBridge[node.Id] = pair;
                PairsById[b.PairId] = pair;
            }
        }

        internal static PairComboDef ConnectorPair(string id, bool bridge)
        {
            if (id == null) return null;
            lock (RegistryLock) return (bridge ? PairsByBridge : PairsById).TryGetValue(id, out var pair) ? pair : null;
        }

        internal static string ConnectorEffectSource(string id)
        {
            if (!IsExtendedStarId(id)) return null;
            lock (RegistryLock) return EffectSources.TryGetValue(id, out var source) ? source : null;
        }

        private static void ValidateEffectBindings(IReadOnlyList<TalentDef> tree, EffectBindings bindings)
        {
            foreach (var talent in tree.SelectMany(t => t.IsChoice ? t.Choices : new[] { t }))
                if (talent.RouteMemory != null && talent.RouteId == null && !talent.IsDreamRing && !talent.IsKeystone)
                    talent.RouteId = bindings.Channel(talent.RouteMemory);
            var ids = new HashSet<string>(tree.Select(t => t.Id), StringComparer.Ordinal);
            foreach (string target in bindings.Stars.Values)
                if (!ids.Contains(target)) throw new FormatException("authored star binding targets a missing node: " + target);
            AuthoredMechanisms.ValidateBindings(tree);
        }
    }
}
