using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>Versioned positional typed records. Base64 is transport escaping, not an opaque authoring payload.</summary>
    public static class AuthoredMechanismCodec
    {
        private const int Version = 1;
        private const int MaxItems = StarProgression.MaxAllocationPoints;
        public static string EncodeSpec(AuthoredMechanismSpec spec)
        {
            AuthoredMechanisms.Validate(spec);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            { writer.Write(Version); WriteSpec(writer, spec); writer.Flush(); return Convert.ToBase64String(stream.ToArray()); }
        }
        public static AuthoredMechanismSpec DecodeSpec(string encoded)
        {
            using (var stream = Open(encoded))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadInt32() != Version) throw new FormatException("Unknown mechanism record version.");
                var spec = ReadSpec(reader); End(stream); AuthoredMechanisms.Validate(spec); return spec;
            }
        }
        public static string Encode(AuthoredMechanismEntry entry)
        {
            ValidateEntry(entry);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Version); writer.Write(entry.StarId); Strings(writer, entry.ContributorIds);
                WriteSpec(writer, entry.Spec); writer.Flush(); return Convert.ToBase64String(stream.ToArray());
            }
        }
        public static AuthoredMechanismEntry Decode(string encoded)
        {
            using (var stream = Open(encoded))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadInt32() != Version) throw new FormatException("Unknown mechanism record version.");
                var entry = new AuthoredMechanismEntry { StarId = Text(reader), ContributorIds = Strings(reader), Spec = ReadSpec(reader) };
                End(stream); ValidateEntry(entry); return entry;
            }
        }
        private static MemoryStream Open(string encoded)
        {
            if (encoded == null || encoded.Length > BuildLimits.MaxEncodedChars) throw new FormatException("Oversized mechanism record.");
            return new MemoryStream(Convert.FromBase64String(encoded), false);
        }
        private static void End(MemoryStream stream) { if (stream.Position != stream.Length) throw new FormatException("Trailing mechanism data."); }
        public static void ValidateEntry(AuthoredMechanismEntry entry)
        {
            if (entry == null || !Gimmicks.ValidStarId(entry.StarId) || entry.ContributorIds == null || entry.ContributorIds.Length == 0
                || entry.ContributorIds.Length > MaxItems || !entry.ContributorIds.Contains(entry.StarId)
                || entry.ContributorIds.Any(id => !Gimmicks.ValidStarId(id)) || entry.ContributorIds.Distinct(StringComparer.Ordinal).Count() != entry.ContributorIds.Length)
                throw new InvalidOperationException("Invalid mechanism contributor identity.");
            AuthoredMechanisms.Validate(entry.Spec);
        }
        private static string Text(BinaryReader r)
        {
            string text = r.ReadString();
            if (text.Length > 4096 || text.Any(c => c > 127)) throw new FormatException("Invalid mechanism text.");
            return text;
        }
        private static int Count(BinaryReader r) { int count = r.ReadInt32(); if (count < 0 || count > MaxItems) throw new FormatException("Invalid mechanism count."); return count; }
        private static void Strings(BinaryWriter w, IEnumerable<string> values)
        {
            var all = values.OrderBy(x => x, StringComparer.Ordinal).ToArray(); w.Write(all.Length); foreach (string value in all) w.Write(value);
        }
        private static string[] Strings(BinaryReader r) { var all = new string[Count(r)]; for (int i = 0; i < all.Length; i++) all[i] = Text(r); return all; }
        private static void OptionalDecimal(BinaryWriter writer, decimal? value)
        { writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }
        private static decimal? OptionalDecimal(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadDecimal() : (decimal?)null;
        private static void OptionalInt(BinaryWriter writer, int? value)
        { writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }
        private static int? OptionalInt(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadInt32() : (int?)null;
        private static void Selector(BinaryWriter w, MemorySelector selector) => w.Write(selector?.Expression ?? "");
        private static MemorySelector Selector(BinaryReader r) { string value = Text(r); return value.Length == 0 ? null : MemorySelector.Parse(value); }
        private static void WriteSpec(BinaryWriter w, AuthoredMechanismSpec s)
        {
            w.Write((int)s.Kind); w.Write(s.ChannelId); Selector(w, s.Source); w.Write((int)s.Trigger); w.Write((int)s.Budget);
            w.Write(s.EveryN); w.Write(s.Once); w.Write((int)s.Condition); w.Write(s.PairId ?? ""); w.Write((int)s.ShieldPool);
            OptionalDecimal(w, s.UncappedValueUnits); OptionalDecimal(w, s.UncappedProbabilityUnits);
            OptionalDecimal(w, s.UncappedDurationSeconds); OptionalDecimal(w, s.UncappedRadiusMetres); OptionalInt(w, s.UncappedTargetCount);
            w.Write(s.ValuesByRank.Length); foreach (int value in s.ValuesByRank) w.Write(value);
            w.Write(s.TriggerByIdentity.Count);
            foreach (var pair in s.TriggerByIdentity.OrderBy(x => x.Key, StringComparer.Ordinal)) { w.Write(pair.Key); w.Write((int)pair.Value); }
            Strings(w, s.Replaces);
            Strings(w, s.RequiredMemories);
            switch (s.Kind)
            {
                case AuthoredMechanismKind.Gimmick: Gimmick(w, s.Gimmick); break;
                case AuthoredMechanismKind.DirectedRecharge:
                    var c = s.Recharge; w.Write(c.ChannelId); Selector(w, c.Source); w.Write((int)c.SourceTrigger); Selector(w, c.Recipient);
                    w.Write(c.ValueUnits); w.Write((int)c.Budget); w.Write(c.ProbabilityUnits); w.Write(c.EveryN); w.Write((int)c.Condition);
                    w.Write(c.RequiredElementTypes); w.Write(c.ModifierUnits); w.Write((int)c.ModifierScope); w.Write(c.CapUnits); break;
                case AuthoredMechanismKind.MemoryPrimed:
                    var p = s.Primed; w.Write(p.ChannelId); w.Write(p.SourceMemory); w.Write((int)p.Trigger); w.Write(p.ValueUnits);
                    w.Write(p.DurationSeconds); w.Write((int)p.Budget); break;
                case AuthoredMechanismKind.RelayWindow:
                    var relay = s.Relay; w.Write(relay.ChannelId); w.Write(relay.TargetMemory); w.Write(relay.ValueUnits);
                    w.Write(relay.DurationModifierUnits); w.Write(relay.QuietRelay); w.Write(relay.EffectiveDurationSeconds.HasValue);
                    if (relay.EffectiveDurationSeconds.HasValue) w.Write(relay.EffectiveDurationSeconds.Value); break;
                case AuthoredMechanismKind.AlliedWard:
                    var ward = s.Ward; w.Write(ward.ChannelId); w.Write((int)ward.RecipientKind); w.Write((int)ward.AmountBasis); w.Write((int)ward.PoolKind);
                    w.Write(ward.ValueUnits); w.Write(ward.IncludeOwner); w.Write(ward.RadiusMetres); w.Write(ward.DurationSeconds);
                    w.Write(ward.BaseTargets); w.Write(ward.Targets - ward.BaseTargets); w.Write(ward.MaxTargets); w.Write((int)ward.Limits); w.Write((int)ward.Budget); break;
                case AuthoredMechanismKind.PressureDividend:
                    var d = s.Dividend; w.Write(d.SourceMemory); w.Write(d.ProbabilityUnits); Strings(w, d.RequiredMemories); Strings(w, d.ContributorIds); break;
                case AuthoredMechanismKind.MemoryTuning:
                    var tuning = s.Tuning; w.Write(tuning.ChannelId); w.Write((int)tuning.Kind); w.Write(tuning.ValueUnits); break;
                case AuthoredMechanismKind.IdentityStrike:
                    var strike = s.IdentityStrike; w.Write(strike.ChannelId); w.Write(strike.Identity); w.Write((int)strike.Trigger); w.Write(strike.EveryN);
                    w.Write(strike.WindowSeconds); w.Write(strike.AdUnits); w.Write(strike.BonusSpeedUnitsPerPercent); w.Write((int)strike.Element);
                    w.Write((int)strike.Shape); w.Write(strike.RangeMetres); w.Write(strike.WidthOrArc); w.Write(strike.MaxTargets); w.Write((int)strike.Basis); break;
                case AuthoredMechanismKind.BridgeSuccess:
                    var b = s.Bridge; w.Write(b.PairId); w.Write(b.Rank); w.Write((int)b.GateKind); Selector(w, b.OpeningSource); w.Write((int)b.OpeningTrigger);
                    Selector(w, b.PayoffSource); w.Write((int)b.PayoffTrigger); w.Write((int)b.Budget); w.Write((int)b.SourcePhase); w.Write(b.UsesNativeWindowLifetime);
                    w.Write(b.CooldownSeconds); w.Write(b.WindowSeconds); w.Write(b.RetainedFiveRanks); w.Write(b.MarkSeconds); w.Write(b.WindowLifetimeScale);
                    w.Write(b.Endpoints.Count); foreach (var endpoint in b.Endpoints.OrderBy(x => x.StarId, StringComparer.Ordinal))
                    { w.Write(endpoint.StarId); w.Write(endpoint.Memory); w.Write(endpoint.MinimumRank); }
                    Payload(w, b.BasePayoff); w.Write(b.Extras.Count); foreach (var extra in b.Extras.OrderBy(x => x.ChannelId, StringComparer.Ordinal)) Payload(w, extra); break;
            }
        }
        private static AuthoredMechanismSpec ReadSpec(BinaryReader r)
        {
            var s = new AuthoredMechanismSpec { Kind = (AuthoredMechanismKind)r.ReadInt32(), ChannelId = Text(r), Source = Selector(r),
                Trigger = (MemoryEventKind)r.ReadInt32(), Budget = (AttributionBudget)r.ReadInt32(), EveryN = r.ReadInt32(), Once = r.ReadBoolean(),
                Condition = (AuthoredMechanismCondition)r.ReadInt32(), PairId = Text(r), ShieldPool = (ModShieldPoolKind)r.ReadInt32() };
            if (s.PairId.Length == 0) s.PairId = null;
            s.UncappedValueUnits = OptionalDecimal(r); s.UncappedProbabilityUnits = OptionalDecimal(r);
            s.UncappedDurationSeconds = OptionalDecimal(r); s.UncappedRadiusMetres = OptionalDecimal(r); s.UncappedTargetCount = OptionalInt(r);
            s.ValuesByRank = new int[Count(r)]; for (int i = 0; i < s.ValuesByRank.Length; i++) s.ValuesByRank[i] = r.ReadInt32();
            int triggers = Count(r); for (int i = 0; i < triggers; i++) s.TriggerByIdentity.Add(Text(r), (MemoryEventKind)r.ReadInt32());
            s.Replaces = Strings(r);
            s.RequiredMemories = Strings(r);
            switch (s.Kind)
            {
                case AuthoredMechanismKind.Gimmick: s.Gimmick = Gimmick(r); break;
                case AuthoredMechanismKind.DirectedRecharge:
                    s.Recharge = new DirectedRechargeChannel(Text(r), Selector(r), (MemoryEventKind)r.ReadInt32(), Selector(r), new[] { r.ReadDecimal() },
                        (AttributionBudget)r.ReadInt32(), r.ReadDecimal(), r.ReadInt32(), (RechargeConditionKind)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                        (RechargeModifierScope)r.ReadInt32(), r.ReadInt32()); break;
                case AuthoredMechanismKind.MemoryPrimed:
                    s.Primed = new MemoryPrimedDefinition(Text(r), Text(r), (MemoryEventKind)r.ReadInt32(), r.ReadDecimal(), r.ReadSingle(), (AttributionBudget)r.ReadInt32()); break;
                case AuthoredMechanismKind.RelayWindow:
                    s.Relay = new RelayWindowDefinition(Text(r), Text(r), r.ReadDecimal(), r.ReadInt32(), r.ReadBoolean());
                    if (r.ReadBoolean()) s.Relay = RelayWindowDefinition.FromEffective(s.Relay.ChannelId, s.Relay.TargetMemory, s.Relay.ValueUnits, r.ReadSingle()); break;
                case AuthoredMechanismKind.AlliedWard:
                    s.Ward = new AlliedWardDefinition(Text(r), (WardRecipientKind)r.ReadInt32(), (WardAmountBasis)r.ReadInt32(), (ModShieldPoolKind)r.ReadInt32(),
                        r.ReadDecimal(), r.ReadBoolean(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                        (WardLimitProfile)r.ReadInt32(), (WardActivationBudget)r.ReadInt32()); break;
                case AuthoredMechanismKind.PressureDividend:
                    string source = Text(r); decimal probability = r.ReadDecimal(); var requirements = Strings(r); var ids = Strings(r);
                    if (ids.Length == 0) throw new FormatException("Missing dividend contributors.");
                    // Effective probability is carried once; the contributor set is attribution metadata, not extra chance.
                    s.Dividend = PressureDividendChannel.FromEffective(source, requirements, ids, probability); break;
                case AuthoredMechanismKind.MemoryTuning:
                    s.Tuning = new MemoryTuningDefinition(Text(r), (MemoryTuningKind)r.ReadInt32(), r.ReadInt32()); break;
                case AuthoredMechanismKind.IdentityStrike:
                    s.IdentityStrike = new IdentityStrikeDefinition(Text(r), Text(r), (IdentityStrikeTrigger)r.ReadInt32(), r.ReadInt32(), r.ReadSingle(),
                        r.ReadInt32(), r.ReadInt32(), (IdentityStrikeElement)r.ReadInt32(), (IdentityStrikeShape)r.ReadInt32(), r.ReadSingle(), r.ReadSingle(),
                        r.ReadInt32(), (IdentityStrikeBasis)r.ReadInt32()); break;
                case AuthoredMechanismKind.BridgeSuccess:
                    string pair = Text(r); int rank = r.ReadInt32(); var gate = (BridgeGateKind)r.ReadInt32(); var opening = Selector(r);
                    var openingTrigger = (MemoryEventKind)r.ReadInt32(); var payoff = Selector(r); var payoffTrigger = (MemoryEventKind)r.ReadInt32();
                    var budget = (AttributionBudget)r.ReadInt32(); var phase = (BridgeSourcePhase)r.ReadInt32(); bool nativeLifetime = r.ReadBoolean();
                    float cooldown = r.ReadSingle(), window = r.ReadSingle(); bool fiveRanks = r.ReadBoolean(); float markSeconds = r.ReadSingle(), lifetimeScale = r.ReadSingle();
                    var endpoints = new List<BridgeEndpointRequirement>(); int endpointCount = Count(r);
                    for (int i = 0; i < endpointCount; i++) endpoints.Add(new BridgeEndpointRequirement(Text(r), Text(r), r.ReadInt32()));
                    var basePayoff = Payload(r); var extras = new List<BridgePayload>(); int extraCount = Count(r);
                    for (int i = 0; i < extraCount; i++) extras.Add(Payload(r));
                    s.Bridge = new BridgeSuccessDefinition(pair, endpoints, rank, gate, opening, openingTrigger, payoff, payoffTrigger, basePayoff, extras, budget, phase, nativeLifetime, cooldown, window, fiveRanks, markSeconds, lifetimeScale); break;
                case AuthoredMechanismKind.SacrificeShield: case AuthoredMechanismKind.StunSourceFilter: break;
                default: throw new FormatException("Unknown mechanism kind.");
            }
            return s;
        }
        private static void Gimmick(BinaryWriter w, GimmickDef g)
        {
            w.Write((int)g.Trigger); w.Write((int)g.Effect); w.Write(g.ValuePrecise); w.Write(g.Arg); w.Write(g.Cooldown);
            w.Write(g.DurationUnits); w.Write(g.RadiusUnits); w.Write(g.ExtraTargets); w.Write(g.ChanceUnits);
            GimmickRawCodec.Write(w, g);
        }
        private static GimmickDef Gimmick(BinaryReader r)
        {
            var gimmick = new GimmickDef { Trigger = (GimmickTrigger)r.ReadInt32(), Effect = (GimmickEffect)r.ReadInt32(),
                ValuePrecise = r.ReadInt64(), Arg = r.ReadInt32(), Cooldown = r.ReadSingle(), DurationUnits = r.ReadInt32(), RadiusUnits = r.ReadInt32(),
                ExtraTargets = r.ReadInt32(), ChanceUnits = r.ReadInt32() };
            GimmickRawCodec.Read(r, gimmick);
            GimmickRawCodec.Validate(gimmick);
            return gimmick;
        }
        private static void Payload(BinaryWriter w, BridgePayload p)
        {
            w.Write(p.ChannelId); w.Write((int)p.Kind); w.Write(p.ValueUnits); w.Write(p.CapUnits);
            Selector(w, p.Recipient); w.Write((int)p.DamageBasis); w.Write(p.DurationSeconds); w.Write(p.DurationCapSeconds);
            OptionalDecimal(w, p.UncappedValueUnits); OptionalDecimal(w, p.UncappedProbabilityUnits);
            OptionalDecimal(w, p.UncappedDurationSeconds); OptionalDecimal(w, p.UncappedRadiusMetres); OptionalInt(w, p.UncappedTargetCount);
            w.Write(p.Gimmick != null); if (p.Gimmick != null) Gimmick(w, p.Gimmick);
            w.Write(p.Ward != null);
            if (p.Ward != null)
            {
                var ward = p.Ward; w.Write((int)ward.RecipientKind); w.Write((int)ward.AmountBasis); w.Write((int)ward.PoolKind);
                w.Write(ward.ValueUnits); w.Write(ward.IncludeOwner); w.Write(ward.RadiusMetres); w.Write(ward.DurationSeconds);
                w.Write(ward.BaseTargets); w.Write(ward.Targets - ward.BaseTargets); w.Write(ward.MaxTargets); w.Write((int)ward.Limits); w.Write((int)ward.Budget);
            }
        }
        private static BridgePayload Payload(BinaryReader r)
        {
            string id = Text(r); var kind = (BridgePayloadKind)r.ReadInt32(); decimal units = r.ReadDecimal(); int cap = r.ReadInt32();
            var recipient = Selector(r); var basis = (BridgeDamageBasis)r.ReadInt32(); float duration = r.ReadSingle();
            decimal durationCap = r.ReadDecimal();
            decimal? raw = OptionalDecimal(r), probability = OptionalDecimal(r), durationRaw = OptionalDecimal(r), radiusRaw = OptionalDecimal(r);
            int? targets = OptionalInt(r);
            var gimmick = r.ReadBoolean() ? Gimmick(r) : null;
            AlliedWardDefinition ward = null;
            if (r.ReadBoolean())
                ward = new AlliedWardDefinition(id, (WardRecipientKind)r.ReadInt32(), (WardAmountBasis)r.ReadInt32(), (ModShieldPoolKind)r.ReadInt32(),
                    r.ReadDecimal(), r.ReadBoolean(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                    (WardLimitProfile)r.ReadInt32(), (WardActivationBudget)r.ReadInt32());
            return BridgePayload.FromEffective(id, kind, units, recipient, basis, duration, gimmick, raw, probability, cap,
                durationRaw, radiusRaw, targets, durationCap, ward);
        }
    }
}
