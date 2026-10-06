using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private readonly Dictionary<DewPlayer, BuildUpdateCoalescer> _buildUpdates =
            new Dictionary<DewPlayer, BuildUpdateCoalescer>();
        private readonly HashSet<DewPlayer> _buildRejectionsLogged = new HashSet<DewPlayer>();
        private readonly List<DewPlayer> _buildUpdatePlayers = new List<DewPlayer>();

        private void ReceiveBuildUpdate(DreamforgeBuildMsg msg, DewPlayer caller)
        {
            try
            {
                if (caller == null || !caller.isHumanPlayer || !DewPlayer.gamePlayers.Contains(caller)) return;
                if (msg == null)
                {
                    RejectBuildOnce(caller, "null");
                    return;
                }
                Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeBuildMsg));
                if (!_incomingBuilds.TryGetValue(caller, out var transfer))
                    _incomingBuilds.Add(caller, transfer = new BuildTransferReceiver());
                if (!transfer.TryAccept(msg.ToPart(), out string encoded))
                {
                    RejectBuildOnce(caller, "transfer");
                    return;
                }
                if (encoded == null) return;
                if (!_buildUpdates.TryGetValue(caller, out var updates))
                    _buildUpdates.Add(caller, updates = new BuildUpdateCoalescer());
                // Parsing, reconstruction, native application and acknowledgments all run at the
                // fixed Core deadline, not at packet-arrival frequency. Last complete update wins.
                updates.Submit(encoded);
            }
            catch (Exception)
            {
                RejectBuildOnce(caller, "receive");
            }
        }

        private void ProcessBuildUpdates(double now)
        {
            _buildUpdatePlayers.Clear();
            foreach (var player in _buildUpdates.Keys) _buildUpdatePlayers.Add(player);
            foreach (var player in _buildUpdatePlayers)
            {
                try
                {
                    if (player == null || !player.isHumanPlayer || !DewPlayer.gamePlayers.Contains(player))
                    {
                        RemoveBuildValidationPeer(player);
                        _incomingBuilds.Remove(player);
                        continue;
                    }
                    var hero = player.hero;
                    // Keep the latest submission until the authenticated hero is available.
                    if (hero == null || hero.IsNullOrInactive()) continue;
                    if (!_buildUpdates.TryGetValue(player, out var updates) || !updates.TryTake(now, out var encoded)) continue;
                    string heroKey = ClientSession.HeroKeyOf(hero);
                    if (_builds.TryGetValue(player, out var previous) && previous.HeroKey == heroKey
                        && previous.Encoded == encoded && !previous.ApplyFailed)
                    {
                        SendApplied(player, hero, previous);
                        continue;
                    }
                    if (!HostBuildValidation.TryAccept(encoded, heroKey, out var build, out string reason))
                    {
                        RejectBuildOnce(player, reason);
                        continue;
                    }
                    string summary = build.Encode();
                    if (previous != null && previous.HeroKey == heroKey && previous.Summary == summary && !previous.ApplyFailed)
                    {
                        previous.Encoded = encoded;
                        SendApplied(player, hero, previous);
                        continue;
                    }
                    var received = new ReceivedBuild { Build = build, Encoded = encoded, Summary = summary, HeroKey = heroKey };
                    if (!ApplyValidatedBuild(player, hero, received)) continue;
                    _builds[player] = received;
                    _pressureDirty = true;
                    SendApplied(player, hero, received);
                }
                catch (Exception)
                {
                    // One sender must never abort processing for the other players.
                    RejectBuildOnce(player, "application");
                }
            }
        }

        private bool ApplyValidatedBuild(DewPlayer player, Hero hero, ReceivedBuild received)
        {
            if (received.ApplyFailed || received.HeroKey != ClientSession.HeroKeyOf(hero)) return false;
            try
            {
                Apply(hero, received);
                return true;
            }
            catch (Exception)
            {
                received.ApplyFailed = true;
                RejectBuildOnce(player, "native-application");
                return false;
            }
        }

        private void RejectBuildOnce(DewPlayer player, string reason)
        {
            if (!ReferenceEquals(player, null) && _buildRejectionsLogged.Add(player))
                Log.Warn("Host: rejected build packet (" + reason + ").");
        }

        private void RemoveBuildValidationPeer(DewPlayer player)
        {
            if (ReferenceEquals(player, null)) return;
            _buildUpdates.Remove(player);
            _buildRejectionsLogged.Remove(player);
        }

        private void ClearBuildValidationPeers()
        {
            _buildUpdates.Clear();
            _buildRejectionsLogged.Clear();
            _buildUpdatePlayers.Clear();
        }
    }
}
