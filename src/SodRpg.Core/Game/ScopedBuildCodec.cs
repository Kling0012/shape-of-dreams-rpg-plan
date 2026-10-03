using System;
using System.Collections.Generic;
using System.Globalization;
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
        }
        internal static void ValidateGimmicks(IReadOnlyList<GimmickEntry> entries)
        {
            var contributors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (e?.Def == null) throw new InvalidOperationException("Missing gimmick entry.");
                var d = e.Def;
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
                sb.Append(e.StarId).Append(':').Append(e.Channel.ChannelId).Append(':').Append((int)e.Channel.ScopeKind).Append(':')
                    .Append(string.Join("+", e.ContributorIds.OrderBy(id => id, StringComparer.Ordinal)));
            }
            first = true;
            foreach (var e in build.Gimmicks)
            {
                if (e.Def.ValuePrecise % 10000 == 0) continue;
                sb.Append(first ? ";v:" : ","); first = false;
                sb.Append(e.StarId).Append(':').Append(e.Def.ValuePrecise.ToString(CultureInfo.InvariantCulture));
            }
        }
        internal static void Read(string kind, string encoded, Build build)
        {
            var f = encoded.Split(':');
            if (f.Length != (kind == "v" ? 2 : 4)) throw new FormatException("Invalid scoped record.");
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
                if (pair.Key[0] == 'v') continue;
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
                    e.Channel = new EffectChannelDef { ChannelId = f[1], SourceMemory = e.Memory, ReceiverMemory = e.Memory, ScopeKind = (ScopeKind)Parse(f[2]) };
                    e.ContributorIds = f[3].Split('+');
                }
            }
            build.ScopedWireRecords.Clear();
            for (int i = 0; i < build.Gimmicks.Count; i++)
                build.Gimmicks[i] = Gimmicks.Clamp(build.Gimmicks[i]) ?? throw new FormatException("Invalid gimmick.");
            Validate(build);
        }
    }
}
