using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>Canonical conditions and exact addition; stateful applications retain their source identity.</summary>
    public static class BuildAggregation
    {
        public static string LinkKey(LinkDef link)
        {
            if (!Links.Validate(link)) throw new ArgumentException("Invalid link.", nameof(link));
            var requires = CanonicalRequirements(link);
            return ((int)link.Kind).ToString(CultureInfo.InvariantCulture) + ":" + string.Join("+", requires);
        }

        private static string[] CanonicalRequirements(LinkDef link)
        {
            var requires = new string[link.Requires.Length];
            for (int i = 0; i < requires.Length; i++) requires[i] = Links.Canon(link.Requires[i]);
            Array.Sort(requires, StringComparer.Ordinal);
            return requires;
        }

        public static IReadOnlyList<LinkDef> LinksForBuild(IEnumerable<LinkDef> links)
        {
            var result = new List<LinkDef>();
            var byKey = new Dictionary<string, LinkDef>(StringComparer.Ordinal);
            foreach (var link in links)
            {
                if (!Links.Validate(link)) throw new ArgumentException("Invalid link.", nameof(link));
                if (link.Kind == LinkKind.BossReward) throw new ArgumentException("Boss rewards require their independent set/profile section.", nameof(links));
                if (link.ValueMilli < 0) throw new ArgumentException("Negative link value.", nameof(links));
                // Surge windows retain their source identity and select the largest active value.
                if (link.Kind == LinkKind.MemorySurge)
                {
                    result.Add(link);
                    continue;
                }
                string key = LinkKey(link);
                if (byKey.TryGetValue(key, out var combined))
                    combined.ValueMilli = checked(combined.ValueMilli + link.ValueMilli);
                else
                {
                    combined = new LinkDef { Kind = link.Kind, Requires = CanonicalRequirements(link), ValueMilli = link.ValueMilli };
                    byKey.Add(key, combined);
                    result.Add(combined);
                }
            }
            return result;
        }

        /// <summary>Gimmicks use their memory as both trigger attribution and receiver; pair receivers use PairKey.</summary>
        public static string GimmickKey(GimmickEntry entry)
        {
            if (entry?.Def == null) throw new ArgumentException("Missing gimmick.", nameof(entry));
            var d = entry.Def;
            return string.Join(":", entry.Memory, ((int)d.Trigger).ToString(CultureInfo.InvariantCulture),
                ((int)d.Effect).ToString(CultureInfo.InvariantCulture), d.Arg.ToString(CultureInfo.InvariantCulture),
                d.Cooldown.ToString("R", CultureInfo.InvariantCulture), d.DurationUnits.ToString(CultureInfo.InvariantCulture),
                d.RadiusUnits.ToString(CultureInfo.InvariantCulture), d.ExtraTargets.ToString(CultureInfo.InvariantCulture),
                d.ChanceUnits.ToString(CultureInfo.InvariantCulture), d.UncappedDurationUnits?.ToString(CultureInfo.InvariantCulture) ?? "",
                d.UncappedRadiusUnits?.ToString(CultureInfo.InvariantCulture) ?? "", d.UncappedExtraTargets?.ToString(CultureInfo.InvariantCulture) ?? "",
                d.UncappedChanceUnits?.ToString(CultureInfo.InvariantCulture) ?? "",
                entry.Channel == null ? "" : FractionalScopedModifiers.ChannelKey(entry));
        }

        public static string GimmickStateKey(GimmickEntry entry) => entry.StarId + ":" + GimmickKey(entry)
            + ":" + (entry.Def.UncappedValue?.ToString(CultureInfo.InvariantCulture) ?? "")
            + ":" + entry.Def.EffectiveValueOrAuthored.ToString(CultureInfo.InvariantCulture);

        /// <summary>Bridge, trigger, payoff and explicit cooldown beneficiary are never interchangeable.</summary>
        public static string PairKey(PairComboEntry entry)
        {
            var d = entry?.Def ?? throw new ArgumentException("Missing pair combo.", nameof(entry));
            return string.Join(":", d.Id, d.BridgeId, d.TriggerMemory, ((int)d.Trigger).ToString(CultureInfo.InvariantCulture),
                d.PayoffMemory, ((int)d.PayoffTrigger).ToString(CultureInfo.InvariantCulture), d.RechargeMemory,
                ((int)d.Effect).ToString(CultureInfo.InvariantCulture), d.Arg.ToString(CultureInfo.InvariantCulture),
                d.Cooldown.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
