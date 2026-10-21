using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private static long _bountySequence;
        private string _localBountyReturnRun;
        private uint _localBountyReturnHero;
        private readonly Dictionary<DewPlayer, DreamforgeBountyReturnMsg> _bountyReturns =
            new Dictionary<DewPlayer, DreamforgeBountyReturnMsg>();

        internal static bool SameBountyReturn(DreamforgeBountyReturnMsg msg, InfinityMode.InfinityChoice choice) =>
            msg != null && msg.version == 1 && choice != null && choice.Secure && !choice.Boundary
            && msg.runId == choice.RunId && msg.revision == choice.Revision
            && msg.graphEpoch == choice.GraphEpoch && msg.segmentEpoch == choice.SegmentEpoch;

        private void OnBountyReturn(DreamforgeBountyReturnMsg msg, DewPlayer caller)
        {
            if (!NetworkServer.active || caller == null || !caller.isHumanPlayer
                || !DewPlayer.gamePlayers.Contains(caller) || caller.hero == null
                || msg == null || msg.heroNetId != caller.hero.netId
                || msg.runId != NetworkedManagerBase<GameManager>.softInstance?.runId
                || !SameBountyReturn(msg, InfinityMode.CurrentChoice)) return;
            if (msg.received)
            {
                if (_bountyReturns.TryGetValue(caller, out var sent) && sent.heroNetId == msg.heroNetId
                    && SameBountyReturn(sent, InfinityMode.CurrentChoice)) sent.received = true;
                return;
            }
            if (_bountyReturns.TryGetValue(caller, out var previous) && previous.heroNetId == msg.heroNetId
                && SameBountyReturn(previous, InfinityMode.CurrentChoice))
            {
                if (!previous.received) _registeredOn?.CustomRpc_SendMessageToClient(caller, previous);
                return;
            }
            if (!PrepareBountyReturn(caller.hero, msg.runId)) return;
            var response = new DreamforgeBountyReturnMsg
            {
                runId = msg.runId, heroNetId = msg.heroNetId, revision = msg.revision,
                graphEpoch = msg.graphEpoch, segmentEpoch = msg.segmentEpoch,
            };
            // Both reports and this marker use Actor's reliable ordered TargetRpc channel.
            _bountyReturns[caller] = response;
            _registeredOn.CustomRpc_SendMessageToClient(caller, response);
        }

        internal bool PrepareBountyReturn(Hero hero, string runId)
        {
            if (!NetworkServer.active || _registeredOn == null || hero == null
                || hero.owner == null || !hero.owner.isHumanPlayer || hero.owner.hero != hero
                || runId != NetworkedManagerBase<GameManager>.softInstance?.runId) return false;
            if (_runtimes.TryGetValue(hero, out var rt)) FlushBountyReports(rt);
            if (hero.owner == DewPlayer.local)
            {
                _localBountyReturnRun = runId;
                _localBountyReturnHero = hero.netId;
            }
            return true;
        }

        internal static bool InfinityBountiesSettled(InfinityMode.InfinityChoice choice)
        {
            var host = NativeInstance;
            if (host == null) return false;
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (player == null || !player.isHumanPlayer || player == DewPlayer.local) continue;
                if (!host._bountyReturns.TryGetValue(player, out var receipt) || !receipt.received
                    || player.hero == null || receipt.heroNetId != player.hero.netId
                    || !SameBountyReturn(receipt, choice)) return false;
            }
            return true;
        }

        private void QueueBountyReport(HeroRuntime rt, BountyReportKind kind, int value)
        {
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId)) return;
            var hero = rt.Hero;
            var player = hero != null ? hero.owner : null;
            // A terminal receipt survives loadout/runtime replacement until the expedition ends.
            if (hero != null && (player == DewPlayer.local && _localBountyReturnRun == runId
                && _localBountyReturnHero == hero.netId
                || player != null && _bountyReturns.TryGetValue(player, out var terminal)
                    && terminal.runId == runId && terminal.heroNetId == hero.netId)) return;
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

        // The counts are cumulative, so a fixed-rate flush merges a burst into one message per
        // interval instead of one per frame; the final counts still go out through the boundary
        // flush below before any conclusion or save. Credited progress can land up to 1 s late.
        private const float BountyFlushSeconds = 1f;

        /// <summary>Tick stage: throttles FlushBountyReports. Boundary and removal flushes stay immediate.</summary>
        private void TickBountyReports()
        {
            if (_tickNow < _nextBountyFlush) return;
            _nextBountyFlush = _tickNow + BountyFlushSeconds;
            FlushBountyReports();
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
            if (player == null || !player.isHumanPlayer || player.hero != hero
                || rt.PendingBountyRun != NetworkedManagerBase<GameManager>.softInstance?.runId)
            {
                Log.Info("Host: discarded pending bounty reports: " + (player == null ? "missing player"
                    : !player.isHumanPlayer ? "non-human player" : player.hero != hero ? "hero changed" : "run changed"));
                ClearBountyReports(rt);
                return;
            }
            if (_registeredOn == null) return;
            if (_bountyCountPeers.Contains(player)
                || player == DewPlayer.local && ClientSession.LocalBountyCountsReady(_registeredOn))
            {
                var message = new DreamforgeBountyCountsMsg
                {
                    heroNetId = hero.netId, runId = rt.PendingBountyRun, sequence = ++_bountySequence,
                    shields = rt.PendingBountyShields, allyHeals = rt.PendingBountyHeals,
                    // The local transport may retain this message after the counters are cleared.
                    elementalKills = (int[])rt.PendingElementalKills.Clone(),
                };
                _registeredOn.CustomRpc_SendMessageToClient(player, message);
                if (player == DewPlayer.local) ClientSession.ReceiveLocalBountyCounts(_registeredOn, message);
            }
            else
            {
                while (rt.PendingBountyShields > 0)
                {
                    SendBountyReport(rt, BountyReportKind.ShieldGranted, 1);
                    rt.PendingBountyShields--;
                }
                while (rt.PendingBountyHeals > 0)
                {
                    SendBountyReport(rt, BountyReportKind.AllyHealed, 1);
                    rt.PendingBountyHeals--;
                }
                for (int mask = 1; mask < 16; mask++)
                    while (rt.PendingElementalKills[mask] > 0)
                    {
                        SendBountyReport(rt, BountyReportKind.ElementalKill, mask);
                        rt.PendingElementalKills[mask]--;
                    }
            }
            ClearBountyReports(rt);
        }

        private static void ClearBountyReports(HeroRuntime rt)
        {
            rt.PendingBountyShields = rt.PendingBountyHeals = 0;
            Array.Clear(rt.PendingElementalKills, 0, rt.PendingElementalKills.Length);
        }
    }

    internal sealed partial class ClientSession
    {
        private readonly Action<DreamforgeBountyCountsMsg> _onBountyCounts;
        private string _bountySequenceRun;
        private uint _bountySequenceHero;
        private long _lastBountySequence;
        private DreamforgeBountyReturnMsg _bountyReturnReceipt;
        private float _nextBountyReturn;

        internal static void ReceiveLocalBountyCounts(Actor actor, DreamforgeBountyCountsMsg msg)
        {
            if (LocalBountyCountsReady(actor)) _hostSession.OnBountyCounts(msg);
        }

        private bool SettleInfinityBounties(InfinityMode.InfinityChoice choice)
        {
            if (!RunActive || LocalHero == null || _clientRpcOn == null) return false;
            if (NetworkServer.active)
                return LocalBountyCountsReady(_clientRpcOn)
                    && HostAuthority.NativeInstance?.PrepareBountyReturn(LocalHero, choice.RunId) == true
                    && HostAuthority.InfinityBountiesSettled(choice);
            if (HostAuthority.SameBountyReturn(_bountyReturnReceipt, choice)
                && _bountyReturnReceipt.heroNetId == LocalHero.netId) return true;
            if (Time.unscaledTime < _nextBountyReturn) return false;
            _nextBountyReturn = Time.unscaledTime + 1f;
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeBountyReturnMsg
            {
                runId = choice.RunId, heroNetId = LocalHero.netId, revision = choice.Revision,
                graphEpoch = choice.GraphEpoch, segmentEpoch = choice.SegmentEpoch,
            });
            return false;
        }

        private void OnBountyReturn(DreamforgeBountyReturnMsg msg)
        {
            if (NetworkServer.active || !RunActive || msg == null || msg.received
                || msg.runId != ActiveRunId || LocalHero == null || msg.heroNetId != LocalHero.netId
                || !HostAuthority.SameBountyReturn(msg, InfinityMode.CurrentChoice)) return;
            _bountyReturnReceipt = msg;
            _clientRpcOn?.CustomRpc_SendMessageToServer(new DreamforgeBountyReturnMsg
            {
                runId = msg.runId, heroNetId = msg.heroNetId, revision = msg.revision,
                graphEpoch = msg.graphEpoch, segmentEpoch = msg.segmentEpoch, received = true,
            });
        }

        internal static bool LocalBountyCountsReady(Actor actor) => _hostSession != null
            && ReferenceEquals(_hostSession._clientRpcOn, actor) && _hostSession._bountyCountsReady;

        private void RegisterBountyCounts(Actor actor)
        {
            _bountyCountsReady = false;
            _bountySequenceRun = null;
            _lastBountySequence = 0;
            _bountyReturnReceipt = null;
            _nextBountyReturn = 0;
            try
            {
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeBountyReturnMsg>(OnBountyReturn);
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
            try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeBountyReturnMsg>(OnBountyReturn); } catch (Exception) { }
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
            if (_bountySequenceRun != msg.runId || _bountySequenceHero != msg.heroNetId)
            {
                _bountySequenceRun = msg.runId;
                _bountySequenceHero = msg.heroNetId;
                _lastBountySequence = 0;
            }
            if (msg.sequence > 0 && msg.sequence <= _lastBountySequence) return;
            // Local direct delivery and its later loopback are the same report.
            if (msg.sequence > 0) _lastBountySequence = msg.sequence;
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
