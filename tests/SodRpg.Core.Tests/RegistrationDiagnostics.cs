using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Collects EVERY registration rejection of every compiled hero, not just the first. The real registration
    /// (StarClusters.RegisterAuthored) throws on the first rejection; for diagnosis only, the rejected star and the stars that
    /// require it are removed from a copy of the definitions and the hero is registered again until it registers.
    /// Run with DREAMFORGE_REG_REPORT=(markdown path) to write the report (docs/specs/v1.31-registration-report.md).
    /// The registered hero is always restored to the empty baseline afterwards.
    /// </summary>
    public sealed class RegistrationDiagnostics
    {
        internal sealed class Rejection
        {
            public string Message, Reason, StarId, ClusterId;
            public int Order;
        }

        private static Type MapType(string hero) =>
            typeof(RegistrationDiagnostics).Assembly.GetType("SodRpg.Core.Tests.DiagnosticMaps." + hero.Substring("Hero_".Length));

        /// <summary>The guarded test-only maps (gen_cs.py --diagnostic) when present, otherwise the production generated maps.</summary>
        internal static IEnumerable<string> Heroes()
        {
            var heroes = new HashSet<string>(StarClusters.CompiledHeroes, StringComparer.Ordinal);
            foreach (string hero in new[] { "Hero_Aurena", "Hero_Bismuth", "Hero_Cetus", "Hero_Husk", "Hero_Lacerta", "Hero_Mist", "Hero_Nachia", "Hero_Vesper", "Hero_Yubar" })
                if (MapType(hero) != null) heroes.Add(hero);
            return heroes.OrderBy(x => x, StringComparer.Ordinal);
        }

        internal static string[] Omitted(string hero) =>
            (string[])MapType(hero)?.GetField("Omitted").GetValue(null) ?? Array.Empty<string>();

        internal static AuthoredStarDef[] Definitions(string hero, List<string> constructionFailures)
        {
            var type = MapType(hero);
            if (type == null) return StarClusters.CreateGeneratedAuthored(hero);
            return (AuthoredStarDef[])type.GetMethod("Create").Invoke(null, new object[] { constructionFailures });
        }

        /// <summary>Every rejection of one hero (empty when it registers as generated).</summary>
        internal static List<Rejection> Diagnose(string hero, out int totalStars, out List<AuthoredStarDef> registrable)
        {
            var constructionFailures = new List<string>();
            var current = Definitions(hero, constructionFailures).ToList();
            totalStars = current.Count + constructionFailures.Count;
            var rejections = new List<Rejection>();
            foreach (string failure in constructionFailures)
            {
                int colon = failure.IndexOf(": ", StringComparison.Ordinal);
                string id = failure.Substring(0, colon);
                rejections.Add(new Rejection { Order = rejections.Count + 1, StarId = id, ClusterId = "(not constructed)", Message = failure, Reason = "Definition construction throws: " + failure.Substring(colon + 2).Replace(id, "<star>") });
                // The star was never constructed, so it is absent from the list: close the gap it leaves in the graph.
                current = Bypass(current, new AuthoredStarDef { LocalStarId = id }, false);
            }
            try
            {
                for (int guard = 0; guard < 4000; guard++)
                {
                    try { StarClusters.RegisterAuthored(hero, current); break; }
                    catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
                    {
                        // The first star the message names is the rejected one ("Invalid prerequisite: A -> B" rejects A).
                        var culprit = current.Where(d => ContainsId(error.Message, d.LocalStarId)).OrderBy(d => error.Message.IndexOf(d.LocalStarId, StringComparison.Ordinal)).ThenByDescending(d => d.LocalStarId.Length).FirstOrDefault();
                        if (culprit == null) throw new InvalidOperationException("Rejection names no star of " + hero + ": " + error.Message);
                        rejections.Add(new Rejection { Order = rejections.Count + 1, Message = error.Message, StarId = culprit.LocalStarId, ClusterId = culprit.ClusterId,
                            Reason = error.Message.Replace(culprit.LocalStarId, "<star>") });
                        current = Bypass(current, culprit, true);
                        if (current.Count == 0) throw new InvalidOperationException("Every star of " + hero + " was rejected.");
                    }
                }
            }
            finally { StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>()); }
            registrable = current;
            return rejections;
        }

        /// <summary>
        /// Diagnosis only: drop a rejected star and close the gap it leaves, so every other star is judged on its own merits and the
        /// removal itself cannot cause a "no attainable path" rejection. Prerequisites on the star are dropped, its graph neighbours are
        /// joined to each other, and stars anchored on it take its anchor.
        /// </summary>
        private static List<AuthoredStarDef> Bypass(List<AuthoredStarDef> all, AuthoredStarDef gone, bool remove)
        {
            string id = gone.LocalStarId;
            var neighbours = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in all)
                foreach (var e in d.Edges)
                {
                    if (e.From == id && e.To != id) neighbours.Add(e.To);
                    if (e.To == id && e.From != id) neighbours.Add(e.From);
                }
            // A star that was never constructed has lost the edges it owned: its dependents are its neighbours too.
            if (!remove)
                foreach (var d in all)
                    if (d.RequiredStarIds.Contains(id) || d.RequiredAnyStarIds.Contains(id)) neighbours.Add(d.LocalStarId);
            string anchor = gone.AnchorId ?? neighbours.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
            bool anchorIsNeighbour = anchor != null;
            var result = all.Where(d => d.LocalStarId != id).ToList();
            var existing = new HashSet<string>(result.SelectMany(d => d.Edges).Select(e => e.From + ">" + e.To), StringComparer.Ordinal);
            var ordered = neighbours.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            foreach (var d in result)
            {
                d.RequiredStarIds = d.RequiredStarIds.Where(x => x != id).ToArray();
                d.RequiredAnyStarIds = d.RequiredAnyStarIds.Where(x => x != id).ToArray();
                if (d.AnchorId == id) d.AnchorId = anchor != d.LocalStarId ? anchor : neighbours.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault(x => x != d.LocalStarId) ?? anchor;
                d.Edges = d.Edges.Where(e => e.From != id && e.To != id).ToArray();
            }
            // The cluster entry anchor is carried by its first star alone (the others inherit it): hand it to a neighbour when that star goes.
            if (gone.AnchorId != null && gone.ClusterId != null && !result.Any(d => d.ClusterId == gone.ClusterId && d.AnchorId != null))
            {
                var heir = result.Where(d => d.ClusterId == gone.ClusterId && d.LocalStarId != gone.AnchorId)
                    .OrderByDescending(d => neighbours.Contains(d.LocalStarId)).ThenBy(d => d.LocalStarId, StringComparer.Ordinal).FirstOrDefault();
                if (heir != null) heir.AnchorId = gone.AnchorId;
            }
            // Its own anchor link is implicit (AnchorId, not an Edge): make it explicit for each neighbour so they stay attached to the anchor.
            if (gone.AnchorId != null)
                foreach (var d in result.Where(x => neighbours.Contains(x.LocalStarId) && x.LocalStarId != gone.AnchorId))
                    d.Edges = d.Edges.Concat(new[] { new AuthoredStarEdge(gone.AnchorId, d.LocalStarId) }).ToArray();
            for (int i = 0; i < ordered.Length; i++)
                for (int j = i + 1; j < ordered.Length; j++)
                {
                    if (existing.Contains(ordered[i] + ">" + ordered[j]) || existing.Contains(ordered[j] + ">" + ordered[i])) continue;
                    var owner = result.FirstOrDefault(d => d.LocalStarId == ordered[i]);
                    if (owner == null) continue;
                    owner.Edges = owner.Edges.Concat(new[] { new AuthoredStarEdge(ordered[i], ordered[j]) }).ToArray();
                }
            return result;
        }

        /// <summary>Registers the hero's definitions and buys a maximum-point build greedily (as GeneratedHeroAcceptanceTests does): runtime composition failures that registration cannot see.</summary>
        internal static string Play(string hero, List<AuthoredStarDef> definitions)
        {
            try
            {
                StarClusters.RegisterAuthored(hero, definitions);
                var profile = GeneratedHeroAcceptanceTests.MaxPointProfile(hero, out var keystones, out var refused);
                return "plays (keystones " + keystones.Count + ", " + Rules.SpentPoints(profile.Hero(hero), hero) + " points spent)";
            }
            catch (Exception error) { return "FAILS: " + error.GetType().Name + ": " + error.Message; }
            finally { StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>()); }
        }

        private static bool ContainsId(string message, string id)
        {
            int at = 0;
            while ((at = message.IndexOf(id, at, StringComparison.Ordinal)) >= 0)
            {
                int end = at + id.Length;
                bool leftOk = at == 0 || !(char.IsLetterOrDigit(message[at - 1]) || message[at - 1] == '.' || message[at - 1] == '-' || message[at - 1] == '_');
                bool rightOk = end == message.Length || !(char.IsLetterOrDigit(message[end]) || message[end] == '-' || message[end] == '_'
                    || message[end] == '.' && end + 1 < message.Length && char.IsLetterOrDigit(message[end + 1]));
                if (leftOk && rightOk) return true;
                at = end;
            }
            return false;
        }

        [Fact]
        public void Writes_the_registration_report_when_requested()
        {
            string path = Environment.GetEnvironmentVariable("DREAMFORGE_REG_REPORT");
            if (string.IsNullOrEmpty(path)) return; // diagnostic tool, not an acceptance check; GeneratedHeroAcceptanceTests is the gate
            var sb = new StringBuilder();
            sb.Append("# v1.31 registration diagnostics report\n\n");
            string preamble = Environment.GetEnvironmentVariable("DREAMFORGE_REG_PREAMBLE");
            if (!string.IsNullOrEmpty(preamble)) sb.Append(File.ReadAllText(preamble).Replace("\r\n", "\n").TrimEnd()).Append("\n\n---\n\n");
            sb.Append("Generated by `RegistrationDiagnostics` (tests/SodRpg.Core.Tests): every compiled hero is registered through the real `StarClusters.RegisterAuthored`. ")
              .Append("The real registration throws on the first rejection, so for diagnosis only the rejected star is removed from an in-memory copy ")
              .Append("(its prerequisites are dropped and its graph neighbours joined, so the removal causes no secondary rejection) and the hero is registered again until it registers; every star is thus judged on its own merits. Nothing here changes any generated file. Rows that still fail to compile (the legacy keystone rows `h.<hero>.key` / `key2` being fixed elsewhere) are omitted from the diagnosed map and listed in the last column; the guarded maps come from `python tools/star-manifest/gen_cs.py --all --diagnostic`.\n\n");
            var results = new List<(string hero, int total, List<Rejection> rejections)>();
            var plays = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string hero in Heroes())
            {
                var rejections = Diagnose(hero, out int total, out var registrable);
                results.Add((hero, total, rejections));
                plays[hero] = rejections.Count == 0 ? Play(hero, registrable) : "not played: the hero does not register as generated";
            }
            sb.Append("| Hero | Stars checked | Rejected stars | Distinct reasons | Omitted legacy keys |\n|---|---:|---:|---:|---|\n");
            foreach (var (hero, total, rejections) in results)
                sb.Append("| ").Append(hero).Append(" | ").Append(total).Append(" | ").Append(rejections.Count).Append(" | ")
                  .Append(rejections.Select(r => r.Reason).Distinct().Count()).Append(" | ")
                  .Append(Omitted(hero).Length > 0 ? string.Join(", ", Omitted(hero)) : "-").Append(" |\n");
            foreach (var (hero, total, rejections) in results)
            {
                sb.Append("\n## ").Append(hero).Append("\n\n");
                sb.Append("Greedy maximum-point play: ").Append(plays[hero]).Append("\n\n");
                if (rejections.Count == 0) { sb.Append("No rejection: registers cleanly.\n"); continue; }
                foreach (var group in rejections.GroupBy(r => r.Reason).OrderByDescending(g => g.Count()))
                {
                    sb.Append("### ").Append(group.Key.Replace("|", "\\|")).Append("\n\n");
                    sb.Append(group.Count()).Append(" rejected star(s).\n\n");
                    foreach (var byCluster in group.GroupBy(r => r.ClusterId))
                        sb.Append("- `").Append(byCluster.Key).Append("`: ").Append(string.Join(", ", byCluster.Select(r => "`" + r.StarId + "`#" + r.Order))).Append('\n');
                    sb.Append('\n');
                }
            }
            File.WriteAllText(path, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        }
    }
}
