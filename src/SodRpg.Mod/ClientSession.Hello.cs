using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// 版のあいさつ（参加者側）。ホストへ自分の版を送り、返事で版違いを知る。
    /// 返事が来ない（ホストが古い版・MODなし）ときも、しばらく待ってから画面で知らせる（mp-ui-save #4）。
    /// </summary>
    internal sealed partial class ClientSession
    {
        private Action<DreamforgeHelloMsg> _onHello;
        private float _helloFirstSent = -1f, _nextHello;
        private bool _helloAnswered;
        private string _acceptedHostContent;
        private bool _hostInfinityAvailable;
        internal static bool RemoteHostInfinityAvailable => _hostSession?._hostInfinityAvailable == true;
        // #144: a participant with no host answer yet is "waiting", not "disabled".
        internal static bool RemoteHostHelloAnswered => _hostSession?._helloAnswered == true;
        private bool MechanismHandshakeAccepted => NetworkServer.active
            || _helloAnswered && string.Equals(_acceptedHostContent, ContentFingerprint.Value, StringComparison.Ordinal);

        /// <summary>ホストと版が違う／ホストから返事がないときの説明。問題なければ null。</summary>
        public string HostVersionWarning { get; private set; }

        private void RegisterHello(Actor actor)
        {
            if (_onHello == null) _onHello = OnHello;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeHelloMsg>(_onHello);
        }

        private void UnregisterHello(Actor actor)
        {
            if (_onHello != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeHelloMsg>(_onHello); } catch (Exception) { }
        }

        private void ResetHello()
        {
            _helloFirstSent = -1f;
            _nextHello = 0f;
            _helloAnswered = false;
            _continueHandshakeReady = false;
            _acceptedHostContent = null;
            _hostInfinityAvailable = false;
            HostVersionWarning = null;
        }

        private void TickHello()
        {
            // ホスト自身はあいさつ不要（自分の版なので必ず一致する）。
            if (_clientRpcOn == null || !NetworkClient.active || NetworkServer.active) return;
            float now = Time.unscaledTime;
            if (now >= _nextHello)
            {
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version, modVer = HostAuthority.ModVersion, content = ContentFingerprint.Value,
                    killObservationSessionId = KillObservationSessionId(_clientRpcOn),
                    continueCheckpoints = true,
                    infinityAvailable = InfinityMode.Available,
                });
                if (_helloFirstSent < 0) _helloFirstSent = now;
                _nextHello = now + 5f;
            }
            if (!_helloAnswered && _helloFirstSent >= 0 && now - _helloFirstSent > 20f)
                HostVersionWarning = Loc.T(
                    "ホストから Dreamforge の返事がありません。ホストがこの MOD の同じ版（" + HostAuthority.ModVersion + "）を入れているか確認してください。",
                    "No Dreamforge reply from the host. Make sure the host runs the same version of this mod (" + HostAuthority.ModVersion + ").");
        }

        private void OnHello(DreamforgeHelloMsg msg)
        {
            if (msg == null) return;
            _helloAnswered = true;
            bool same = msg.continueCheckpoints && ContentFingerprint.Matches(msg.protocol, msg.content, Protocol.Version);
            _acceptedHostContent = same ? msg.content : null;
            _hostInfinityAvailable = same && msg.infinityAvailable;
            if (same) ReceiveContinueHandshake(msg);
            if (same && msg.authorityGeneration != 0) ObserveMonsterAuthority(msg.authorityGeneration);
            string theirs = string.IsNullOrEmpty(msg.modVer) ? "?" : msg.modVer;
            HostVersionWarning = same ? null : Loc.T(
                $"ホストと Dreamforge の版が違います（ホスト {theirs} / 自分 {HostAuthority.ModVersion}）。装備の効果が反映されず、報酬も入りません。全員同じ版にしてください。",
                $"Your Dreamforge version differs from the host (host {theirs} / yours {HostAuthority.ModVersion}). Gear bonuses and rewards will not work. Everyone must use the same version.");
        }
    }
}
