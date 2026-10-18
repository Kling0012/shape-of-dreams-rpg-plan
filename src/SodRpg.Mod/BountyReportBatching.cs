using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private void QueueBountyReport(HeroRuntime rt, BountyReportKind kind, int value)
        {
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId)) return;
            if (rt.PendingBountyRun != runId)
            {
                rt.PendingBountyShields = rt.PendingBountyHeals = 0;
                Array.Clear(rt.PendingElementalKills, 0, rt.PendingElementalKills.Length);
                rt.PendingBountyRun = runId;
            }
            if (kind == BountyReportKind.ShieldGranted) rt.PendingBountyShields++;
            else if (kind == BountyReportKind.AllyHealed) rt.PendingBountyHeals++;
            else if (kind == BountyReportKind.ElementalKill && value > 0 && value < 16) rt.PendingElementalKills[value]++;
        }

        internal void FlushBountyReports()
        {
            foreach (var rt in _runtimes.Values)
            {
                try { FlushBountyReports(rt); }
                catch (Exception ex) { Log.Error("Host: bounty report flush " + ex.Message); }
            }
        }

        private void FlushBountyReports(HeroRuntime rt)
        {
            bool pending = rt.PendingBountyShields != 0 || rt.PendingBountyHeals != 0;
            for (int mask = 1; mask < 16 && !pending; mask++) pending = rt.PendingElementalKills[mask] != 0;
            if (!pending) return;
            var hero = rt.Hero;
            var player = hero != null ? hero.owner : null;
            try
            {
                if (player == null || !player.isHumanPlayer || player.hero != hero || _registeredOn == null
                    || rt.PendingBountyRun != NetworkedManagerBase<GameManager>.softInstance?.runId)
                {
                    Log.Info("Host: discarded pending bounty reports: " + (player == null ? "missing player"
                        : !player.isHumanPlayer ? "non-human player" : player.hero != hero ? "hero changed"
                        : _registeredOn == null ? "missing transport" : "run changed"));
                    return;
                }
                if (_bountyCountPeers.Contains(player)
                    || player == DewPlayer.local && ClientSession.LocalBountyCountsReady(_registeredOn))
                {
                    _registeredOn.CustomRpc_SendMessageToClient(player, new DreamforgeBountyCountsMsg
                    {
                        heroNetId = hero.netId, runId = rt.PendingBountyRun,
                        shields = rt.PendingBountyShields, allyHeals = rt.PendingBountyHeals,
                        // The local transport may retain this message after the counters are cleared.
                        elementalKills = (int[])rt.PendingElementalKills.Clone(),
                    });
                }
                else
                {
                    for (int n = 0; n < rt.PendingBountyShields; n++) SendBountyReport(rt, BountyReportKind.ShieldGranted, 1);
                    for (int n = 0; n < rt.PendingBountyHeals; n++) SendBountyReport(rt, BountyReportKind.AllyHealed, 1);
                    for (int mask = 1; mask < 16; mask++)
                        for (int n = 0; n < rt.PendingElementalKills[mask]; n++) SendBountyReport(rt, BountyReportKind.ElementalKill, mask);
                }
            }
            finally
            {
                rt.PendingBountyShields = rt.PendingBountyHeals = 0;
                Array.Clear(rt.PendingElementalKills, 0, rt.PendingElementalKills.Length);
            }
        }
    }

    internal sealed partial class ClientSession
    {
        private readonly Action<DreamforgeBountyCountsMsg> _onBountyCounts;
        internal static bool LocalBountyCountsReady(Actor actor) => _hostSession != null
            && ReferenceEquals(_hostSession._clientRpcOn, actor) && _hostSession._bountyCountsReady;

        private void RegisterBountyCounts(Actor actor)
        {
            _bountyCountsReady = false;
            try
            {
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeBountyCountsMsg>(_onBountyCounts);
                _bountyCountsReady = true;
            }
            catch (Exception ex) { Log.Warn("Counted bounty reports unavailable; using single reports: " + ex.Message); }
        }

        private void UnregisterBountyCounts(Actor actor)
        {
            bool wasReady = _bountyCountsReady;
            _bountyCountsReady = false;
            if (actor == null) return;
            if (wasReady && NetworkClient.active)
            {
                try
                {
                    actor.CustomRpc_SendMessageToServer(new DreamforgeBuildInputCapabilityMsg
                    {
                        bountyCounts = 0, netLite = _netLiteReceiveReady ? 1 : 0, roomOmens = _omenReceiveReady ? 1 : 0,
                        forbiddenSmith = Forbidden.HasSmith(Profile) ? 1 : 0, nemesisGrudge = Profile.NemesisGrudge,
                    });
                }
                catch (Exception ex) { Log.Warn("Counted bounty report withdrawal unavailable: " + ex.Message); }
            }
            try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeBountyCountsMsg>(_onBountyCounts); } catch (Exception) { }
        }

        private void OnBountyCounts(DreamforgeBountyCountsMsg msg)
        {
            if (msg == null || msg.version != 1 || !RunActive || msg.runId != ActiveRunId) return;
            var hero = LocalHero;
            if (hero == null || msg.heroNetId != hero.netId || msg.shields < 0 || msg.allyHeals < 0
                || msg.elementalKills == null || msg.elementalKills.Length != 16) return;
            for (int mask = 0; mask < 16; mask++) if (msg.elementalKills[mask] < 0) return;
            // A zero count would only allocate an empty event list inside Rules, so skip it here.
            if (msg.shields > 0) Emit(Rules.OnSupportApplied(Profile, true, false, 1f, msg.shields));
            if (msg.allyHeals > 0) Emit(Rules.OnSupportApplied(Profile, false, true, 1f, msg.allyHeals));
            for (int mask = 1; mask < 16; mask++)
                if (msg.elementalKills[mask] > 0)
                    Emit(Rules.OnElementalKill(Profile, (mask & 1) != 0, (mask & 2) != 0,
                        (mask & 4) != 0, (mask & 8) != 0, msg.elementalKills[mask]));
        }
    }

    // Flush before ordered conclusion/save notifications and before lobby teardown.
    [HarmonyPatch]
    internal static class BountyReportsBeforeBoundary
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameResultManager), nameof(GameResultManager.WrapUp), new[] { typeof(DewGameResult.ResultType) });
            yield return AccessTools.Method(typeof(DewPersistence), nameof(DewPersistence.SerializeGameData), new[] { typeof(LoadNodeSettings) });
            yield return AccessTools.Method(typeof(DewNetworkManager), nameof(DewNetworkManager.RestartSession), Type.EmptyTypes);
            yield return AccessTools.Method(typeof(Primus_Ending), nameof(Primus_Ending.StartPrimusDeath), Type.EmptyTypes);
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.FlushBountyReports();
        }
    }
}
