using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        internal sealed class LinkDiagnosticState
        {
            internal bool Seen, Alive, Sampled;
            internal string RunId;
            internal readonly List<LinkDef> Defined = new List<LinkDef>();
            internal readonly HashSet<string> Memories = new HashSet<string>();
            internal readonly HashSet<string> Essences = new HashSet<string>();
            internal readonly List<LinkDef> Active = new List<LinkDef>();
        }

        private const int MaxLinkDiagnosticLines = 32;
        private string _linkDiagnosticRunId;
        private int _linkDiagnosticLines;

        // #457: observe the existing decision only; never let diagnostic failures affect it.
        private void DiagnoseLinks(HeroRuntime rt, bool alive, bool sampled)
        {
            try
            {
                string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
                if (_linkDiagnosticRunId != runId)
                {
                    _linkDiagnosticRunId = runId;
                    _linkDiagnosticLines = 0;
                }
                if (_linkDiagnosticLines >= MaxLinkDiagnosticLines) return;
                var state = rt.LinkDiagnostic;
                var build = rt.Powers.Build;
                bool same = state.Seen && state.RunId == runId && state.Defined.Count == build.Links.Count
                    && state.Alive == alive && state.Sampled == sampled
                    && (!sampled || (state.Memories.SetEquals(rt.LinkMemories) && state.Essences.SetEquals(rt.LinkEssences)))
                    && state.Active.Count == rt.SatisfiedLinks.Count;
                for (int i = 0; same && i < state.Defined.Count; i++)
                    same = ReferenceEquals(state.Defined[i], build.Links[i]);
                for (int i = 0; same && i < state.Active.Count; i++)
                    same = ReferenceEquals(state.Active[i], rt.SatisfiedLinks[i]);
                if (same) return;
                state.Seen = true;
                state.RunId = runId;
                state.Defined.Clear();
                state.Defined.AddRange(build.Links);
                state.Alive = alive;
                state.Sampled = sampled;
                state.Memories.Clear();
                state.Essences.Clear();
                if (sampled)
                {
                    state.Memories.UnionWith(rt.LinkMemories);
                    state.Essences.UnionWith(rt.LinkEssences);
                }
                state.Active.Clear();
                state.Active.AddRange(rt.SatisfiedLinks);

                WriteLinkDiagnostic("heroInstance=" + rt.Hero.GetInstanceID() + " hero=" + rt.HeroKey
                    + " alive=" + alive + " sampled=" + sampled + " links=" + build.Links.Count
                    + " active=" + rt.SatisfiedLinks.Count
                    + " memories=" + (sampled ? string.Join("+", rt.LinkMemories) : "not-sampled")
                    + " essences=" + (sampled ? string.Join("+", rt.LinkEssences) : "not-sampled"));
                int reported = 0, identityLinks = 0;
                foreach (var link in build.Links)
                {
                    bool identity = false;
                    foreach (string requirement in link.Requires)
                        if (requirement != null && requirement.StartsWith("St_D_", StringComparison.Ordinal))
                        { identity = true; break; }
                    if (!identity) continue;
                    identityLinks++;
                    if (reported >= 6 || _linkDiagnosticLines >= MaxLinkDiagnosticLines) continue;
                    reported++;
                    WriteLinkDiagnostic("heroInstance=" + rt.Hero.GetInstanceID() + " kind=" + link.Kind
                        + " valueMilli=" + link.ValueMilli + " requires=" + string.Join("+", link.Requires)
                        + " active=" + rt.SatisfiedLinks.Contains(link));
                }
                if (identityLinks > reported && _linkDiagnosticLines < MaxLinkDiagnosticLines)
                    WriteLinkDiagnostic("identityLinks=" + identityLinks + " omitted=" + (identityLinks - reported));
            }
            catch (Exception) { }
        }

        private void WriteLinkDiagnostic(string details)
        {
            _linkDiagnosticLines++;
            Log.Info("diag457 " + details
                + (_linkDiagnosticLines == MaxLinkDiagnosticLines ? " (run log limit reached)" : ""));
        }
    }
}
