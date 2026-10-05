using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>Independent boss sections; no profile instructions are accepted from the wire.</summary>
    public static class BossBuildCodec
    {
        public static string Key(string setId, string profileId) => setId + ":" + profileId;
        public static BossMoveEntry FixedMove(string profileId)
        {
            if (!BossProfiles.TryGetMove(profileId, out var p)) throw new ArgumentException("Unknown boss profile.", nameof(profileId));
            return new BossMoveEntry(p.SetId, p.Id, p.Channels.Select(c => new BossChannelValue(c.ChannelId, c.ValueMilli)));
        }
        internal static void Validate(Build build)
        {
            if (build.BossMoves.Count > BossProfiles.MaxEntries || build.BossRewards.Count > BossProfiles.MaxEntries)
                throw new InvalidOperationException("Boss sections exceed 64 entries.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in build.BossMoves)
            {
                if (e == null || !seen.Add(Key(e.SetId, e.ProfileId)) || !BossProfiles.TryGetMove(e.ProfileId, out var p)
                    || p.SetId != e.SetId || e.Channels.Count != p.Channels.Count) throw new FormatException("Invalid boss move identity.");
                bool fixedStage = false;
                var set = Content.GetSet(e.SetId);
                if (set == null || set.BossTypeName == null) throw new FormatException("Invalid boss set.");
                foreach (var stage in set.BossStages) if (stage.ProfileId == p.Id) fixedStage = true;
                for (int i = 0; i < e.Channels.Count; i++)
                {
                    var c = e.Channels[i]; var d = p.Channels[i];
                    bool scaled = d.Kind == BossCoefficientKind.Damage || d.Kind == BossCoefficientKind.Heal || d.Kind == BossCoefficientKind.Shield;
                    int cap = p.SetId == BossProfiles.DemonSetId ? d.CapMilli : checked(d.CapMilli * 3);
                    if (c == null || c.ChannelId != d.ChannelId || c.ValueMilli <= 0 || c.ValueMilli > cap
                        || (fixedStage || !scaled) && c.ValueMilli != d.ValueMilli) throw new FormatException("Invalid boss channel.");
                }
            }
            seen.Clear();
            foreach (var e in build.BossRewards)
            {
                if (e == null || !seen.Add(Key(e.SetId, e.ProfileId)) || !BossProfiles.TryGetReward(e.ProfileId, out var p)
                    || p.SetId != e.SetId || e.Stage < 1 || e.Stage > 3 || Content.GetSet(e.SetId)?.BossReward != p.Id)
                    throw new FormatException("Invalid boss reward stage.");
            }
        }
        internal static void Append(StringBuilder sb, Build build)
        {
            Validate(build);
            sb.Append(";b:"); bool first = true;
            foreach (var e in build.BossMoves.OrderBy(x => x.SetId, StringComparer.Ordinal).ThenBy(x => x.ProfileId, StringComparer.Ordinal))
            {
                if (!first) sb.Append(','); first = false;
                sb.Append(e.SetId).Append(':').Append(e.ProfileId).Append(':');
                for (int i = 0; i < e.Channels.Count; i++)
                {
                    if (i > 0) sb.Append('+');
                    sb.Append(e.Channels[i].ChannelId).Append('=').Append(e.Channels[i].ValueMilli.ToString(CultureInfo.InvariantCulture));
                }
            }
            sb.Append(";z:"); first = true;
            foreach (var e in build.BossRewards.OrderBy(x => x.SetId, StringComparer.Ordinal).ThenBy(x => x.ProfileId, StringComparer.Ordinal))
            {
                if (!first) sb.Append(','); first = false;
                sb.Append(e.SetId).Append(':').Append(e.ProfileId).Append(':').Append(e.Stage.ToString(CultureInfo.InvariantCulture));
            }
        }
        internal static void Read(string kind, string encoded, Build build)
        {
            var f = encoded.Split(':');
            if (f.Length != 3) throw new FormatException("Invalid boss entry.");
            if (kind == "z")
                build.BossRewards.Add(new BossRewardEntry(f[0], f[1], int.Parse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture)));
            else
            {
                if (!BossProfiles.TryGetMove(f[1], out var p)) throw new FormatException("Unknown boss move.");
                var values = f[2].Split('+');
                if (values.Length != p.Channels.Count) throw new FormatException("Invalid channel count.");
                var channels = new BossChannelValue[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    var pair = values[i].Split('=');
                    if (pair.Length != 2) throw new FormatException("Invalid boss channel.");
                    channels[i] = new BossChannelValue(pair[0], int.Parse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture));
                }
                build.BossMoves.Add(new BossMoveEntry(f[0], f[1], channels));
            }
        }
        public static string Describe(BossMoveEntry entry)
        {
            if (entry == null || !BossProfiles.TryGetMove(entry.ProfileId, out var p)) return "";
            string values = string.Join(Loc.T("、", ", "), entry.Channels.Select(c => c.ChannelId + " "
                + (c.ValueMilli / 1000m).ToString("0.###", CultureInfo.InvariantCulture)));
            return p.Description + Loc.T("\n現在のchannel係数：", "\nCurrent channel coefficients: ") + values;
        }
    }
}
