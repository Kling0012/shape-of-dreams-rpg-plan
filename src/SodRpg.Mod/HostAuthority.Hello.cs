using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>
    /// 版のあいさつ（ホスト側）。参加者の MOD の版と内容の指紋を受け取り、自分の版を返す。
    /// 違っていれば警告するが、装備・報酬・インフィニティの利用条件にはしない。
    /// </summary>
    internal sealed partial class HostAuthority
    {
        /// <summary>この MOD の版（about/metadata.json の modVer）。起動時に設定する。</summary>
        internal static string ModVersion = "?";

        private readonly Dictionary<DewPlayer, string> _versionMismatches = new Dictionary<DewPlayer, string>();
        private readonly HashSet<DewPlayer> _helloPeers = new HashSet<DewPlayer>();
        private static readonly List<string> MismatchScratch = new List<string>();

        /// <summary>版が違う参加者の説明（ホストの画面に出す）。無ければ空。</summary>
        internal static IReadOnlyList<string> VersionWarnings => MismatchScratch;

        private Action<DreamforgeHelloMsg, DewPlayer> _onHello;
        private Actor _helloActor;

        private void RegisterHello(Actor actor)
        {
            if (ReferenceEquals(actor, _helloActor)) return;
            // Hello decisions belong to one scene's RPC transport, not to the persistent
            // authority. A previous rejection must not downgrade a newly selected run.
            UnregisterHello(_helloActor);
            _helloActor = actor;
            if (actor == null) return;
            if (_onHello == null) _onHello = OnHello;
            actor.CustomRpc_RegisterServerMessageHandler<DreamforgeHelloMsg>(nameof(DreamforgeHelloMsg), _onHello);
        }

        private void UnregisterHello(Actor actor)
        {
            if (actor != null && _onHello != null)
                try { actor.CustomRpc_UnregisterServerMessageHandler<DreamforgeHelloMsg>(_onHello); } catch (Exception) { }
            _helloActor = null;
            _versionMismatches.Clear();
            _helloPeers.Clear();
            RebuildMismatchList();
        }

        private void OnHello(DreamforgeHelloMsg msg, DewPlayer caller)
        {
            try
            {
                if (msg == null || caller == null || !caller.isHumanPlayer || caller == DewPlayer.local
                    || !DewPlayer.gamePlayers.Contains(caller) && !DewPlayer.lobbyPlayers.Contains(caller)) return;
                _helloPeers.Add(caller);
                // Hello reports diagnostics, not authorization. Bind the observation session
                // even when a readable peer advertises another version or content registry.
                BindKillObservationSession(caller, msg.killObservationSessionId);
                bool sameProtocol = msg.protocol == Protocol.Version;
                bool sameContent = string.Equals(msg.content, ContentFingerprint.Value, StringComparison.Ordinal);
                bool sameVersion = string.Equals(msg.modVer, ModVersion, StringComparison.Ordinal);
                if (sameProtocol && sameContent && sameVersion && msg.continueCheckpoints && msg.infinityAvailable)
                    _versionMismatches.Remove(caller);
                else if (!_versionMismatches.ContainsKey(caller))
                {
                    string theirs = string.IsNullOrEmpty(msg.modVer) ? "?" : msg.modVer;
                    _versionMismatches[caller] = Loc.T(
                        $"{caller.playerName} の Dreamforge 互換性情報に差があります（版 {ModVersion}/{theirs}、Protocol {Protocol.Version}/{msg.protocol}）。機能は続行します。",
                        $"{caller.playerName}'s Dreamforge compatibility information differs (version {ModVersion}/{theirs}, protocol {Protocol.Version}/{msg.protocol}). Features continue.");
                    // One warning per mismatch episode/transport, not every five-second retry.
                    // Do not call a content/capability difference a protocol mismatch.
                    Log.Warn($"Host: compatibility warning for {caller.playerName}: protocol {msg.protocol}/{Protocol.Version}, mod {theirs}/{ModVersion}, contentEqual={sameContent}, continueCheckpoints={msg.continueCheckpoints}, infinityAvailable={msg.infinityAvailable}; features continue.");
                }
                RebuildMismatchList();
                CheckInfinityRunCompatibility();
                _registeredOn?.CustomRpc_SendMessageToClient(caller, new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version, modVer = ModVersion, content = ContentFingerprint.Value,
                    authorityGeneration = ClientSession.HostAuthorityGeneration,
                    continueRunId = ClientSession.ContinueRunId,
                    continueCheckpointId = ClientSession.ContinueCheckpointId,
                    continueResumeSession = ClientSession.ContinueResumeSession,
                    continueCheckpoints = true,
                    infinityAvailable = InfinityMode.Available,
                });
            }
            catch (Exception ex) { Log.Error("Host: hello " + ex); }
        }

        private void RebuildMismatchList()
        {
            MismatchScratch.Clear();
            foreach (var kv in _versionMismatches)
                if (kv.Key != null && DewPlayer.gamePlayers.Contains(kv.Key)) MismatchScratch.Add(kv.Value);
        }
    }
}
