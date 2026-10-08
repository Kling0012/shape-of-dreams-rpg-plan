using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class OverflowBonusPeer
        {
            internal string RunId;
            internal long LedgerId, Total;
            internal bool Pending, Disabled;
            internal int GrantFrame = -1;
            internal readonly DreamforgeOverflowBonusResultMsg Reply = new DreamforgeOverflowBonusResultMsg();
        }

        private readonly Dictionary<DewPlayer, OverflowBonusPeer> _overflowBonusPeers = new Dictionary<DewPlayer, OverflowBonusPeer>();
        private Action<DreamforgeOverflowBonusMsg, DewPlayer> _onOverflowBonus;
        private bool _overflowBonusDisabled;

        private void RegisterOverflowBonus(Actor actor)
        {
            try
            {
                if (_onOverflowBonus == null) _onOverflowBonus = OnOverflowBonus;
                actor.CustomRpc_RegisterServerMessageHandler<DreamforgeOverflowBonusMsg>(nameof(DreamforgeOverflowBonusMsg), _onOverflowBonus);
            }
            catch (Exception ex) { Log.Warn("Overflow bonus transport unavailable; other features continue: " + ex.Message); }
        }

        private void UnregisterOverflowBonus(Actor actor)
        {
            if (actor != null && _onOverflowBonus != null)
                try { actor.CustomRpc_UnregisterServerMessageHandler<DreamforgeOverflowBonusMsg>(_onOverflowBonus); } catch (Exception) { }
            // Keep queued obligations across scene transports; currency authority/receipts outlive actors.
        }

        internal void OnOverflowBonus(DreamforgeOverflowBonusMsg msg, DewPlayer caller)
        {
            if (!NetworkServer.active || !ClientSession.HostOverflowBonusReady || caller == null || msg == null || !caller.isHumanPlayer
                || !DewPlayer.gamePlayers.Contains(caller) && !DewPlayer.lobbyPlayers.Contains(caller)) return;
            try
            {
                ApplyContinueTrades();
                if (!_overflowBonusPeers.TryGetValue(caller, out var peer))
                {
                    if (_overflowBonusPeers.Count >= SodRpg.Core.Game.TradeAuthority.MaxTrackedPlayers) return;
                    _overflowBonusPeers.Add(caller, peer = new OverflowBonusPeer());
                }
                if (msg.version != 1 || msg.total < 0 || _overflowBonusDisabled)
                {
                    ReplyOverflowBonus(caller, peer, false);
                    return;
                }
                string nativeRunId = TradeRunId();
                // A zero-total handshake may initialize this run, never an already earned obligation.
                if (msg.total == 0 && msg.ledgerId == 0)
                {
                    if (string.IsNullOrEmpty(nativeRunId) || msg.runId != nativeRunId)
                    {
                        ReplyOverflowBonus(caller, peer, true);
                        return;
                    }
                    peer.RunId = nativeRunId;
                    peer.LedgerId = _tradeAuthority.LedgerIdOf(TradePlayerKey(caller), nativeRunId);
                    peer.Total = 0;
                    peer.Pending = false;
                    peer.Disabled = _tradeAuthority.PendingOverflowBonus(TradePlayerKey(caller), nativeRunId, 0, peer.LedgerId) < 0;
                    ReplyOverflowBonus(caller, peer, !peer.Disabled);
                    return;
                }
                int delta = _tradeAuthority.PendingOverflowBonus(TradePlayerKey(caller), msg.runId, msg.total, msg.ledgerId);
                if (delta < 0 || delta > 0 && msg.runId != nativeRunId)
                {
                    // A host restart or rehost replaced the ledger of this same native run. Offer a fresh
                    // handshake with the current identity instead of disabling the bonus for the whole run:
                    // the client must rebase its cumulative total to zero, so nothing already paid under the
                    // old ledger is paid twice. Old-run deltas and blocked ledgers still disable only the dust.
                    long currentLedger = string.IsNullOrEmpty(nativeRunId) || msg.runId != nativeRunId
                        ? 0
                        : _tradeAuthority.LedgerIdOf(TradePlayerKey(caller), nativeRunId);
                    if (delta < 0 && currentLedger != 0 && msg.ledgerId != currentLedger)
                    {
                        peer.RunId = nativeRunId;
                        peer.LedgerId = currentLedger;
                        peer.Total = 0;
                        peer.Pending = false;
                        peer.Disabled = _tradeAuthority.OverflowBonusBlocked(TradePlayerKey(caller), nativeRunId);
                        Log.Warn("Overflow bonus ledger changed for " + caller.playerName + "; offering a fresh handshake, already paid dust stays paid");
                        ReplyOverflowBonus(caller, peer, !peer.Disabled);
                        return;
                    }
                    if (!peer.Disabled)
                        Log.Warn("Overflow bonus ledger/run mismatch; only extra Dream Dust is disabled for " + caller.playerName);
                    peer.Disabled = true;
                    ReplyOverflowBonus(caller, peer, false);
                    return;
                }
                if (peer.RunId != msg.runId || peer.LedgerId != msg.ledgerId)
                {
                    peer.RunId = msg.runId;
                    peer.LedgerId = msg.ledgerId;
                    peer.Total = 0;
                }
                peer.Disabled = false;
                // The preference travels with every cumulative update. OFF creates no new total;
                // obligations earned while ON still settle after switching OFF.
                peer.Total = Math.Max(peer.Total, msg.total);
                peer.Pending = true;
            }
            catch (Exception ex) { Log.Warn("Overflow bonus request failed; other features continue: " + ex.Message); }
        }

        internal void TickOverflowBonus()
        {
            if (!NetworkServer.active || !ClientSession.HostOverflowBonusReady || _overflowBonusDisabled) return;
            ApplyContinueTrades();
            foreach (var entry in _overflowBonusPeers)
            {
                var owner = entry.Key;
                var peer = entry.Value;
                if (owner == null || !peer.Pending || peer.Disabled || peer.GrantFrame == Time.frameCount) continue;
                peer.Pending = false;
                string key = TradePlayerKey(owner);
                int delta = _tradeAuthority.PendingOverflowBonus(key, peer.RunId, peer.Total, peer.LedgerId);
                if (delta < 0 || delta > int.MaxValue - (long)owner.dreamDust
                    || delta > 0 && peer.RunId != TradeRunId())
                {
                    peer.Disabled = true;
                    Log.Warn("Overflow bonus balance/ledger unavailable; only extra Dream Dust is disabled for " + owner.playerName);
                    ReplyOverflowBonus(owner, peer, false);
                    continue;
                }
                if (delta > 0)
                {
                    int before = owner.dreamDust;
                    peer.GrantFrame = Time.frameCount; // Set before native callbacks; never grant twice in one frame.
                    try
                    {
                        owner.EarnDreamDust(delta); // One AddDreamDust + one native earned RPC per owner/frame.
                        if (owner.dreamDust - (long)before != delta)
                            throw new InvalidOperationException("Unexpected native Dream Dust balance.");
                        if (_tradeAuthority.CommitOverflowBonus(key, peer.RunId, peer.Total, peer.LedgerId) != peer.Total)
                            throw new InvalidOperationException("Overflow bonus receipt changed during native grant.");
                    }
                    catch (Exception ex)
                    {
                        // Mutation precedes RPC/callbacks. Exact payment is committed even if a later callback
                        // throws, and an exactly settled balance proves the native outcome: a subscriber/RPC
                        // callback throwing after the mutation is not a currency failure, so the optional bonus
                        // must keep working for the rest of the session.
                        // Partial/ambiguous native changes must never be retried against a possibly paid balance.
                        if (owner.dreamDust - (long)before == delta)
                        {
                            _tradeAuthority.CommitOverflowBonus(key, peer.RunId, peer.Total, peer.LedgerId);
                            Log.Warn("Overflow bonus settled exactly but a native callback threw; the bonus continues: " + ex.Message);
                        }
                        else
                        {
                            peer.Disabled = true;
                            _tradeAuthority.BlockOverflowBonus(key, peer.RunId, peer.LedgerId);
                            _overflowBonusDisabled = true;
                            Log.Warn("Overflow bonus native grant failed; only extra Dream Dust is disabled: " + ex.Message);
                        }
                    }
                }
                ReplyOverflowBonus(owner, peer, !peer.Disabled && !_overflowBonusDisabled);
                if (_overflowBonusDisabled) return;
            }
        }

        private void ReplyOverflowBonus(DewPlayer owner, OverflowBonusPeer peer, bool available)
        {
            var reply = peer.Reply;
            reply.available = available;
            reply.runId = peer.RunId;
            reply.ledgerId = peer.LedgerId;
            reply.paid = _tradeAuthority.OverflowBonusPaid(TradePlayerKey(owner), peer.RunId);
            if (owner == DewPlayer.local) ClientSession.ReceiveLocalOverflowBonus(reply);
            else _registeredOn?.CustomRpc_SendMessageToClient(owner, reply);
        }

        private void ResetOverflowBonusCheckpoint()
        {
            // Pending maxima are from the abandoned timeline, not part of the restored native currency snapshot.
            _overflowBonusPeers.Clear();
        }
    }
}
