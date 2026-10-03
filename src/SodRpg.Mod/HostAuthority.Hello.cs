using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>
    /// 版のあいさつ（ホスト側）。参加者の MOD の版と内容の指紋を受け取り、自分の版を返す。
    /// 違っていれば、ホストの画面に「○○の版が違うため、装備の効果を反映できません」を出す（mp-ui-save #4）。
    /// </summary>
    internal sealed partial class HostAuthority
    {
        /// <summary>この MOD の版（about/metadata.json の modVer）。起動時に設定する。</summary>
        internal static string ModVersion = "?";

        private readonly Dictionary<DewPlayer, string> _versionMismatches = new Dictionary<DewPlayer, string>();
        private static readonly List<string> MismatchScratch = new List<string>();

        /// <summary>版が違う参加者の説明（ホストの画面に出す）。無ければ空。</summary>
        internal static IReadOnlyList<string> VersionWarnings => MismatchScratch;

        private Action<DreamforgeHelloMsg, DewPlayer> _onHello;

        private void RegisterHello(Actor actor)
        {
            if (_onHello == null) _onHello = OnHello;
            actor.CustomRpc_RegisterServerMessageHandler<DreamforgeHelloMsg>(nameof(DreamforgeHelloMsg), _onHello);
        }

        private void UnregisterHello(Actor actor)
        {
            if (_onHello != null)
                try { actor.CustomRpc_UnregisterServerMessageHandler<DreamforgeHelloMsg>(_onHello); } catch (Exception) { }
            _versionMismatches.Clear();
            RebuildMismatchList();
        }

        private void OnHello(DreamforgeHelloMsg msg, DewPlayer caller)
        {
            try
            {
                if (msg == null || caller == null || !caller.isHumanPlayer) return;
                bool same = ContentFingerprint.Matches(msg.protocol, msg.content, Protocol.Version);
                if (same) _versionMismatches.Remove(caller);
                else
                {
                    string theirs = string.IsNullOrEmpty(msg.modVer) ? "?" : msg.modVer;
                    _versionMismatches[caller] = Loc.T(
                        $"{caller.playerName} の Dreamforge の版が違います（ホスト {ModVersion} / 相手 {theirs}）。この人の装備の効果は反映されません。",
                        $"{caller.playerName} has a different Dreamforge version (host {ModVersion} / theirs {theirs}). Their gear bonuses are not applied.");
                    Log.Warn($"Host: version mismatch with {caller.playerName}: protocol {msg.protocol} vs {Protocol.Version}, mod {theirs} vs {ModVersion}, content {msg.content} vs {ContentFingerprint.Value}");
                }
                RebuildMismatchList();
                _registeredOn?.CustomRpc_SendMessageToClient(caller, new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version, modVer = ModVersion, content = ContentFingerprint.Value,
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
