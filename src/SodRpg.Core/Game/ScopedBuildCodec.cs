using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SodRpg.Core.Game
{
    internal static class ScopedBuildCodec
    {
        private static int Parse(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        internal static void Validate(Build build)
        {
            if (build.NativeModifiers.Count > StarProgression.MaxSpendablePoints) throw new InvalidOperationException("Too many native star modifiers.");
            var nativeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in build.NativeModifiers)
                if (!FractionalScopedModifiers.ValidNativeEntry(n) || !nativeKeys.Add(((int)n.Kind) + ":" + n.Memory))
                    throw new InvalidOperationException("Invalid native star modifier.");
            ValidateGimmicks(build.Gimmicks);
            if (AuthoredMechanisms.EffectiveChannelCount(build) > BuildLimits.MaxGimmickEntries) throw new InvalidOperationException("Too many effective channels.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var channels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in build.Mechanisms)
            {
                AuthoredMechanismCodec.ValidateEntry(entry);
                void ValidateEffective(GimmickDef def)
                {
                    if (def != null && (def.Value > Gimmicks.Cap(def.Effect) || def.Cooldown > Gimmicks.MaxCooldown
                        || def.DurationUnits > Gimmicks.MaxParameterPercent * 100 || def.RadiusUnits > Gimmicks.MaxParameterPercent * 100
                        || def.ExtraTargets > Gimmicks.MaxExtraTargets || def.ChanceUnits > 10000))
                        throw new InvalidOperationException("Effective mechanism payload exceeds its runtime caps.");
                }
                ValidateEffective(entry.Spec.Gimmick);
                if (entry.Spec.Bridge != null)
                {
                    ValidateEffective(entry.Spec.Bridge.BasePayoff.Gimmick);
                    foreach (var extra in entry.Spec.Bridge.Extras) ValidateEffective(extra.Gimmick);
                }
                if (entry.Spec.Kind == AuthoredMechanismKind.StunSourceFilter || entry.Spec.Kind == AuthoredMechanismKind.SacrificeShield)
                {
                    string expected = entry.Spec.Kind == AuthoredMechanismKind.StunSourceFilter ? "h.cetus.key2" : "h.aurena.key2";
                    var key = build.FindSelectedKeystone(expected);
                    if (key == null || entry.StarId != expected
                        || !key.Grants.Any(x => AuthoredMechanisms.Key(x) == AuthoredMechanisms.Key(entry.Spec)))
                        throw new InvalidOperationException("Native keystone adapter does not belong to the selected named key.");
                }
                if (!channels.Add(entry.Spec.ChannelId)) throw new InvalidOperationException("Duplicate mechanism channel.");
                foreach (string id in entry.ContributorIds) ids.Add(id);
                if (entry.Spec.Bridge != null)
                {
                    var pair = PairCombos.Get(entry.Spec.Bridge.PairId) ?? throw new InvalidOperationException("Unregistered real pair.");
                    if (!entry.Spec.Bridge.Endpoints.Any(x => x.StarId == pair.StarA && x.Memory == pair.RouteA)
                        || !entry.Spec.Bridge.Endpoints.Any(x => x.StarId == pair.StarB && x.Memory == pair.RouteB))
                        throw new InvalidOperationException("Pair endpoints differ from the registered definition.");
                    foreach (var endpoint in entry.Spec.Bridge.Endpoints)
                        if (!build.MechanismEndpointRanks.TryGetValue(endpoint.StarId, out int rank) || rank < 0 || rank > StarProgression.MaxSpendablePoints)
                            throw new InvalidOperationException("Missing or invalid actual bridge endpoint rank.");
                }
            }
            if (ids.Count > StarProgression.MaxSpendablePoints) throw new InvalidOperationException("Too many mechanism contributors.");
            foreach (var endpoint in build.MechanismEndpointRanks)
                if (!Gimmicks.ValidStarId(endpoint.Key) || endpoint.Value < 0 || endpoint.Value > StarProgression.MaxSpendablePoints)
                    throw new InvalidOperationException("Invalid endpoint rank.");
            foreach (var entry in build.Mechanisms)
                if (entry.Spec.Condition != AuthoredMechanismCondition.Always && !build.Mechanisms.Any(x => x.Spec.Bridge?.PairId == entry.Spec.PairId))
                    throw new InvalidOperationException("A bridge condition requires its allocated typed base binding.");
        }
        internal static void ValidateGimmicks(IReadOnlyList<GimmickEntry> entries)
        {
            var contributors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (e?.Def == null) throw new InvalidOperationException("Missing gimmick entry.");
                var d = e.Def;
                GimmickRawCodec.Validate(d);
                if (d.ValuePrecise % 10000 != 0 && d.ValuePrecise > Gimmicks.Cap(d.Effect) * Gimmicks.PreciseValueScale
                    || d.DurationUnits % 100 != 0 && (d.DurationUnits > 30000 || !Gimmicks.SupportsParameter(d, GimmickParam.Duration))
                    || d.RadiusUnits % 100 != 0 && (d.RadiusUnits > 30000 || !Gimmicks.SupportsParameter(d, GimmickParam.Radius))
                    || d.ChanceUnits % 100 != 0 && (d.ChanceUnits > 10000 || !Gimmicks.SupportsParameter(d, GimmickParam.Chance)))
                    throw new InvalidOperationException("Invalid exact effect fields.");
                if (e.Channel != null)
                {
                    FractionalScopedModifiers.ChannelKey(e);
                    if (e.ContributorIds == null || e.ContributorIds.Length == 0 || e.ContributorIds.Length > StarProgression.MaxSpendablePoints
                        || e.ContributorIds.Distinct(StringComparer.Ordinal).Count() != e.ContributorIds.Length
                        || e.ContributorIds.Any(id => !Gimmicks.ValidStarId(id)) || !e.ContributorIds.Contains(e.StarId))
                        throw new InvalidOperationException("Invalid channel contributors.");
                    foreach (string id in e.ContributorIds)
                        if (!contributors.Add(id)) throw new InvalidOperationException("A contributor belongs to more than one channel.");
                }
                else if (!contributors.Add(e.StarId)) throw new InvalidOperationException("Duplicate effect contributor.");
            }
            if (contributors.Count > StarProgression.MaxSpendablePoints) throw new InvalidOperationException("Too many authored effect contributors.");
        }
        internal static void Append(StringBuilder sb, Build build)
        {
            bool first = true;
            foreach (var e in build.Gimmicks)
            {
                var d = e.Def;
                if (d.DurationUnits % 100 == 0 && d.RadiusUnits % 100 == 0 && d.ChanceUnits % 100 == 0) continue;
                sb.Append(first ? ";f:" : ","); first = false;
                sb.Append(e.StarId).Append(':').Append(d.DurationUnits).Append(':').Append(d.RadiusUnits).Append(':').Append(d.ChanceUnits);
            }
            first = true;
            foreach (var e in build.NativeModifiers.OrderBy(e => e.Memory, StringComparer.Ordinal).ThenBy(e => e.Kind))
            {
                sb.Append(first ? ";n:" : ","); first = false;
                sb.Append(e.Memory).Append(':').Append((int)e.Kind).Append(':').Append(e.ValueMilli).Append(':').Append(e.CapProfileId);
            }
            first = true;
            foreach (var e in build.Gimmicks)
            {
                if (e.Channel == null) continue;
                sb.Append(first ? ";j:" : ","); first = false;
                var c = e.Channel;
                sb.Append(e.StarId).Append(':').Append(c.ChannelId).Append(':').Append((int)c.ScopeKind).Append(':')
                    .Append(string.Join("+", e.ContributorIds.OrderBy(id => id, StringComparer.Ordinal))).Append(':')
                    .Append(c.OwnerId).Append(':').Append(c.SourceMemory).Append(':').Append(c.ReceiverMemory).Append(':')
                    .Append(string.Join("+", c.EquipmentRequirements.OrderBy(id => id, StringComparer.Ordinal))).Append(':')
                    .Append(c.PairSuccessId ?? "").Append(':').Append(c.ActivationBudget).Append(':').Append(c.ClockPolicy);
            }
            first = true;
            foreach (var e in build.Gimmicks)
            {
                if (e.Def.ValuePrecise % 10000 == 0) continue;
                sb.Append(first ? ";v:" : ","); first = false;
                sb.Append(e.StarId).Append(':').Append(e.Def.ValuePrecise.ToString(CultureInfo.InvariantCulture));
            }
            first = true;
            foreach (var e in build.Gimmicks)
            {
                if (!GimmickRawCodec.HasRaw(e.Def) || build.SelectedKeystones.Count == 0 && !GimmickRawCodec.NeedsExactValue(e.Def)) continue;
                using (var stream = new MemoryStream(GimmickRawCodec.MaxBytes))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    GimmickRawCodec.Write(writer, e.Def, valueOnly: build.SelectedKeystones.Count == 0); writer.Flush();
                    sb.Append(first ? ";r:" : ","); first = false;
                    sb.Append(e.StarId).Append(':').Append(Convert.ToBase64String(stream.ToArray()));
                }
            }
            first = true;
            foreach (var entry in build.Mechanisms.OrderBy(x => x.Spec.ChannelId, StringComparer.Ordinal))
            { sb.Append(first ? ";m:" : ","); first = false; sb.Append(AuthoredMechanismCodec.Encode(entry)); }
            first = true;
            foreach (var endpoint in build.MechanismEndpointRanks)
            { sb.Append(first ? ";e:" : ","); first = false; sb.Append(endpoint.Key).Append(':').Append(endpoint.Value); }
            if (build.SelectedKeystones.Count > 0)
            {
                sb.Append(";k:");
                bool keystoneFirst = true;
                foreach (var key in build.SelectedKeystones)
                {
                    if (!keystoneFirst) sb.Append(','); keystoneFirst = false;
                    sb.Append(AuthoredKeystoneCodec.Encode(key));
                }
            }
        }
        internal static void Read(string kind, string encoded, Build build)
        {
            if (kind == "m") { build.Mechanisms.Add(AuthoredMechanismCodec.Decode(encoded)); return; }
            if (kind == "k")
            {
                var keystone = AuthoredKeystoneCodec.Decode(encoded);
                if (build.HasSelectedKeystone(keystone.KeystoneId)) throw new FormatException("Duplicate keystone record.");
                build.AddSelectedKeystone(keystone); return;
            }
            if (kind == "e")
            {
                var endpoint = encoded.Split(':');
                if (endpoint.Length != 2) throw new FormatException("Invalid endpoint rank record.");
                build.MechanismEndpointRanks.Add(endpoint[0], Parse(endpoint[1])); return;
            }
            var f = encoded.Split(':');
            if (f.Length != (kind == "v" || kind == "r" ? 2 : kind == "j" ? 11 : 4)) throw new FormatException("Invalid scoped record.");
            if (kind == "n")
            {
                build.NativeModifiers.Add(new NativeMemoryModifierEntry { Memory = f[0], Kind = (LinkKind)Parse(f[1]), ValueMilli = Parse(f[2]), CapProfileId = f[3] });
                return;
            }
            if (!Gimmicks.ValidStarId(f[0]) || build.ScopedWireRecords.ContainsKey(kind + ":" + f[0])) throw new FormatException("Duplicate scoped record.");
            build.ScopedWireRecords.Add(kind + ":" + f[0], f);
        }
        internal static void Apply(Build build)
        {
            foreach (var pair in build.ScopedWireRecords)
            {
                if (pair.Key[0] != 'v') continue;
                var f = pair.Value;
                var e = build.Gimmicks.FirstOrDefault(g => g.StarId == f[0]) ?? throw new FormatException("Unknown exact value recipient.");
                long precise = long.Parse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (e.Def.ValuePrecise != 0 || precise <= 0 || precise % 10000 == 0
                    || precise > Gimmicks.Cap(e.Def.Effect) * Gimmicks.PreciseValueScale)
                    throw new FormatException("Invalid exact value override.");
                e.Def.ValuePrecise = precise;
            }
            foreach (var pair in build.ScopedWireRecords)
            {
                if (pair.Key[0] == 'v' || pair.Key[0] == 'r') continue;
                var f = pair.Value;
                var e = build.Gimmicks.FirstOrDefault(g => g.StarId == f[0]) ?? throw new FormatException("Unknown scoped record recipient.");
                if (pair.Key[0] == 'f')
                {
                    int duration = Parse(f[1]), radius = Parse(f[2]), chance = Parse(f[3]);
                    if (duration < 0 || duration > 30000 || radius < 0 || radius > 30000 || chance < 0 || chance > 10000
                        || duration > 0 && !Gimmicks.SupportsParameter(e.Def, GimmickParam.Duration)
                        || radius > 0 && !Gimmicks.SupportsParameter(e.Def, GimmickParam.Radius)
                        || chance > 0 && !Gimmicks.SupportsParameter(e.Def, GimmickParam.Chance)
                        || e.Def.DurationUnits != duration / 100 * 100 || e.Def.RadiusUnits != radius / 100 * 100
                        || e.Def.ChanceUnits != chance / 100 * 100) throw new FormatException("Invalid fractional parameter.");
                    e.Def.DurationUnits = duration; e.Def.RadiusUnits = radius; e.Def.ChanceUnits = chance;
                }
                else
                {
                    e.Channel = new EffectChannelDef { ChannelId = f[1], ScopeKind = (ScopeKind)Parse(f[2]),
                        OwnerId = f[4], SourceMemory = f[5], ReceiverMemory = f[6], EquipmentRequirements = f[7].Length == 0 ? Array.Empty<string>() : f[7].Split('+'),
                        PairSuccessId = f[8].Length == 0 ? null : f[8], ActivationBudget = f[9], ClockPolicy = f[10] };
                    e.ContributorIds = f[3].Split('+');
                }
            }
            foreach (var pair in build.ScopedWireRecords)
            {
                if (pair.Key[0] != 'r') continue;
                var f = pair.Value;
                var e = build.Gimmicks.FirstOrDefault(g => g.StarId == f[0]) ?? throw new FormatException("Unknown postordinary recipient.");
                if (f[1].Length > (GimmickRawCodec.MaxBytes + 2) / 3 * 4)
                    throw new FormatException("Oversized postordinary record.");
                using (var stream = new MemoryStream(Convert.FromBase64String(f[1]), false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (stream.Length > GimmickRawCodec.MaxBytes) throw new FormatException("Oversized postordinary fields.");
                    GimmickRawCodec.Read(reader, e.Def);
                    if (stream.Position != stream.Length || !GimmickRawCodec.HasRaw(e.Def))
                        throw new FormatException("Invalid postordinary record.");
                }
                ValidateRawRecipient(e.Def);
                if (build.SelectedKeystones.Count == 0 && (!GimmickRawCodec.NeedsExactValue(e.Def)
                    || e.Def.UncappedDurationUnits.HasValue || e.Def.UncappedRadiusUnits.HasValue
                    || e.Def.UncappedExtraTargets.HasValue || e.Def.UncappedChanceUnits.HasValue))
                    throw new FormatException("A keyless postordinary record must carry only a finer exact effect value.");
            }
            build.ScopedWireRecords.Clear();
            for (int i = 0; i < build.Gimmicks.Count; i++)
                build.Gimmicks[i] = Gimmicks.Clamp(build.Gimmicks[i]) ?? throw new FormatException("Invalid gimmick.");
            Validate(build);
        }

        private static void ValidateRawRecipient(GimmickDef def)
        {
            bool Parameter(long? raw, int current, int cap, GimmickParam kind) =>
                !raw.HasValue || raw > cap && current == cap && Gimmicks.SupportsParameter(def, kind);
            if (!Parameter(def.UncappedDurationUnits, def.DurationUnits, Gimmicks.MaxParameterPercent * 100, GimmickParam.Duration)
                || !Parameter(def.UncappedRadiusUnits, def.RadiusUnits, Gimmicks.MaxParameterPercent * 100, GimmickParam.Radius)
                || !Parameter(def.UncappedExtraTargets, def.ExtraTargets, Gimmicks.MaxExtraTargets, GimmickParam.ExtraTargets)
                || !Parameter(def.UncappedChanceUnits, def.ChanceUnits, 10000, GimmickParam.Chance))
                throw new FormatException("Postordinary parameters contradict the capped legacy fields.");
            if (def.UncappedValue.HasValue)
            {
                decimal bounded = Math.Min(def.UncappedValue.Value, Gimmicks.Cap(def.Effect));
                decimal precise = bounded * Gimmicks.PreciseValueScale;
                if (precise == decimal.Truncate(precise) && precise != def.ValuePrecise)
                    throw new FormatException("Postordinary value contradicts the capped legacy field.");
            }
        }
    }
}
