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
            RegisterOverflowBonus(actor);
        }

        private void UnregisterHello(Actor actor)
        {
            if (actor != null && _onHello != null)
                try { actor.CustomRpc_UnregisterServerMessageHandler<DreamforgeHelloMsg>(_onHello); } catch (Exception) { }
            UnregisterOverflowBonus(actor);
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
                GemContinueSources.ObserveReceipt(caller.guid, caller, ClientSession.ContinueRunId,
                    msg.continueRunId, msg.continueCheckpointId, msg.continueResumeSession);
                bool sameProtocol = msg.protocol == Protocol.Version;
                bool sameContent = string.Equals(msg.content, ContentFingerprint.Value, StringComparison.Ordinal);
                bool sameVersion = string.Equals(msg.modVer, ModVersion, StringComparison.Ordinal);
                if (sameProtocol && sameContent && sameVersion && msg.continueCheckpoints && msg.infinityAvailable)
                    _versionMismatches.Remove(caller);
                else if (!_versionMismatches.ContainsKey(caller))
                {
                    string theirs = string.IsNullOrEmpty(msg.modVer) ? "?" : msg.modVer;
                    // Name the item that differs: version and protocol often read identical while the content
                    // fingerprint or the peer's Infinity availability is what the warning is about.
                    _versionMismatches[caller] = Loc.T(
                        $"{caller.playerName} の Dreamforge に差があります（{DescribeDifferencesJa(sameProtocol, sameContent, sameVersion, msg.continueCheckpoints, msg.infinityAvailable)}。版 {ModVersion}/{theirs}、Protocol {Protocol.Version}/{msg.protocol}）。機能は続行します。",
                        $"{caller.playerName}'s Dreamforge differs ({DescribeDifferencesEn(sameProtocol, sameContent, sameVersion, msg.continueCheckpoints, msg.infinityAvailable)}; version {ModVersion}/{theirs}, protocol {Protocol.Version}/{msg.protocol}). Features continue.");
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

        // Which compatibility items differ, in the order a player can act on them. A peer that merely has Infinity
        // switched off is reported as that, not as a version or protocol difference.
        internal static string DescribeDifferencesJa(bool sameProtocol, bool sameContent, bool sameVersion,
            bool continueCheckpoints, bool infinityAvailable)
        {
            var items = new List<string>();
            if (!sameVersion) items.Add("版が違う");
            if (!sameProtocol) items.Add("Protocolが違う");
            if (!sameContent) items.Add("内容（星・遺物などの登録）が一致しない");
            if (!continueCheckpoints) items.Add("中断保存に未対応");
            if (!infinityAvailable) items.Add("相手側でインフィニティが無効");
            return string.Join("、", items);
        }

        internal static string DescribeDifferencesEn(bool sameProtocol, bool sameContent, bool sameVersion,
            bool continueCheckpoints, bool infinityAvailable)
        {
            var items = new List<string>();
            if (!sameVersion) items.Add("version differs");
            if (!sameProtocol) items.Add("protocol differs");
            if (!sameContent) items.Add("content registry differs");
            if (!continueCheckpoints) items.Add("no continue-checkpoint support");
            if (!infinityAvailable) items.Add("Infinity is disabled on their side");
            return string.Join(", ", items);
        }

        private void RebuildMismatchList()
        {
            MismatchScratch.Clear();
            foreach (var kv in _versionMismatches)
                if (kv.Key != null && DewPlayer.gamePlayers.Contains(kv.Key)) MismatchScratch.Add(kv.Value);
        }
    }
}
