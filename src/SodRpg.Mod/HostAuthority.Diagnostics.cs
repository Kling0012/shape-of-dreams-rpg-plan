using System;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private readonly ExpeditionDiagnostics _expeditionDiagnostics = new ExpeditionDiagnostics();

        private void DiagnoseHostBuild(string stage, Hero hero, Build build)
        {
            try
            {
                bool runtime = hero != null && _runtimes.TryGetValue(hero, out var rt)
                    && rt.AppliedBuild != null && ReferenceEquals(rt.AppliedBuild.Build, build);
                _expeditionDiagnostics.Write(stage + ":" + (hero != null ? hero.netId : 0),
                    "hero=" + ClientSession.HeroKeyOf(hero) + " heroInstance=" + (hero != null ? hero.GetInstanceID() : 0)
                    + " actorInstance=" + (_registeredOn != null ? _registeredOn.GetInstanceID() : 0)
                    + " runtimeMatches=" + runtime + ExpeditionDiagnostics.BuildSummary(build));
            }
            catch (Exception) { }
        }

        private void DiagnoseHostRejection(DewPlayer player, string reason)
        {
            try { DiagnoseHostBuild("host-build-rejected-" + reason, player != null ? player.hero : null, null); }
            catch (Exception) { }
        }

        private void DiagnoseHostPressure(DreamPressure pressure, System.Collections.Generic.IReadOnlyList<Build> builds)
        {
            try
            {
                int missing = 0;
                foreach (var build in builds) if (build == null) missing++;
                _expeditionDiagnostics.Write("host-pressure", "players=" + builds.Count + " missingBuilds=" + missing
                    + " dream=" + pressure.AverageDreamLevel + " stars=" + pressure.AverageSpentStarPoints
                    + " depth=" + pressure.Depth + " waypoint=" + pressure.WaypointMultiplier
                    + " infinityStage=" + pressure.InfinityStage + " hp=" + pressure.HealthMultiplier
                    + " damage=" + pressure.DamageMultiplier);
            }
            catch (Exception) { }
        }

        private void DiagnoseHostTransport(Actor next)
        {
            try
            {
                _expeditionDiagnostics.Write("host-transport", "oldInstance=" + (_registeredOn != null ? _registeredOn.GetInstanceID() : 0)
                    + " newInstance=" + (next != null ? next.GetInstanceID() : 0)
                    + " builds=" + _builds.Count + " runtimes=" + _runtimes.Count);
            }
            catch (Exception) { }
        }
    }
}
