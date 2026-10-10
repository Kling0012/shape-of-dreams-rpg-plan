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
        private bool _hostCompatibilityWarned;
        private bool _hostInfinityAvailable;
        private string _continueReceiptRunId, _continueReceiptCheckpointId, _continueReceiptResumeSession;
        private string _sentContinueReceiptRunId, _sentContinueReceiptCheckpointId, _sentContinueReceiptResumeSession;
        private Actor _sentContinueReceiptActor;
        internal static bool RemoteHostInfinityAvailable => _hostSession?._hostInfinityAvailable == true;
        // #144: a participant with no host answer yet is "waiting", not "disabled".
        internal static bool RemoteHostHelloAnswered => _hostSession?._helloAnswered == true;

        /// <summary>ホストと版が違う／ホストから返事がないときの説明。問題なければ null。</summary>
        public string HostVersionWarning { get; private set; }

        private void RegisterHello(Actor actor)
        {
            if (_onHello == null) _onHello = OnHello;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeHelloMsg>(_onHello);
            RegisterOverflowBonus(actor);
            RegisterBuildInputCapability(actor);
            RegisterAutocastPressureCapability(actor);
        }

        private void UnregisterHello(Actor actor)
        {
            if (_onHello != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeHelloMsg>(_onHello); } catch (Exception) { }
            UnregisterOverflowBonus(actor);
            UnregisterBuildInputCapability(actor);
            UnregisterAutocastPressureCapability(actor);
        }

        private void ResetHello()
        {
            _helloFirstSent = -1f;
            _nextHello = 0f;
            _helloAnswered = false;
            _hostCompatibilityWarned = false;
            _hostInfinityAvailable = false;
            _hostBuildInputCapability = false;
            _hostNetLite = false;
            _netLiteReceiveReady = false;
            _hostAutocastPressureCapability = false;
            _nextAutocastPressureReport = 0f;
            _nextBuildInputProbe = 0f;
            HostVersionWarning = null;
            ResetOverflowBonusConnection();
            _sentContinueReceiptActor = null;
            _sentContinueReceiptRunId = _sentContinueReceiptCheckpointId = _sentContinueReceiptResumeSession = null;
        }

        private void RememberContinueReceipt(DreamforgeHelloMsg msg, string runId)
        {
            if (_continueCheckpointBlocked || string.IsNullOrEmpty(msg.continueCheckpointId)
                || string.IsNullOrEmpty(msg.continueResumeSession)
                || msg.continueResumeSession == Protocol.LobbyReturnedResumeSession
                || NetworkedManagerBase<GameManager>.softInstance?.runId != runId
                || Profile.Run?.RunId != runId || Profile.ContinueResumeSession != msg.continueResumeSession) return;
            if (_continueReceiptRunId == runId && _continueReceiptCheckpointId == msg.continueCheckpointId
                && _continueReceiptResumeSession == msg.continueResumeSession) return;
            _continueReceiptRunId = runId;
            _continueReceiptCheckpointId = msg.continueCheckpointId;
            _continueReceiptResumeSession = msg.continueResumeSession;
            _buildDirty = true;
            _buildCacheFrame = -1;
        }

        private bool HasCurrentContinueReceipt()
        {
            return !_nativeContinueRestoring && _nativeContinueCheckpoint == null && !_continueCheckpointBlocked
                && !string.IsNullOrEmpty(_continueReceiptCheckpointId)
                && NetworkedManagerBase<GameManager>.softInstance?.runId == _continueReceiptRunId
                && Profile.Run?.RunId == _continueReceiptRunId
                && Profile.ContinueResumeSession == _continueReceiptResumeSession;
        }

        private DreamforgeHelloMsg CreateHelloMessage()
        {
            bool receipt = HasCurrentContinueReceipt();
            return new DreamforgeHelloMsg
            {
                protocol = Protocol.Version, modVer = HostAuthority.ModVersion, content = ContentFingerprint.Value,
                killObservationSessionId = KillObservationSessionId(_clientRpcOn),
                continueCheckpoints = true,
                infinityAvailable = InfinityMode.Available,
                continueRunId = receipt ? _continueReceiptRunId : null,
                continueCheckpointId = receipt ? _continueReceiptCheckpointId : null,
                continueResumeSession = receipt ? _continueReceiptResumeSession : null,
            };
        }

        private void SendContinueReceiptBeforeBuild()
        {
            if (NetworkServer.active || !HasCurrentContinueReceipt()) return;
            if (ReferenceEquals(_sentContinueReceiptActor, _clientRpcOn)
                && _sentContinueReceiptRunId == _continueReceiptRunId
                && _sentContinueReceiptCheckpointId == _continueReceiptCheckpointId
                && _sentContinueReceiptResumeSession == _continueReceiptResumeSession) return;
            // Record transport success before sending Build; failed receipts remain retryable.
            _clientRpcOn.CustomRpc_SendMessageToServer(CreateHelloMessage());
            float now = Time.unscaledTime;
            if (_helloFirstSent < 0) _helloFirstSent = now;
            _nextHello = now + 5f;
            _sentContinueReceiptActor = _clientRpcOn;
            _sentContinueReceiptRunId = _continueReceiptRunId;
            _sentContinueReceiptCheckpointId = _continueReceiptCheckpointId;
            _sentContinueReceiptResumeSession = _continueReceiptResumeSession;
            _buildDirty = true;
            _buildCacheFrame = -1;
        }

        private void TickHello()
        {
            TickAutocastPressureReport();
            // ホスト自身はあいさつ不要（自分の版なので必ず一致する）。
            if (_clientRpcOn == null || !NetworkClient.active || NetworkServer.active) return;
            float now = Time.unscaledTime;
            if (now >= _nextHello)
            {
                _clientRpcOn.CustomRpc_SendMessageToServer(CreateHelloMessage());
                // CustomRpc ignores unknown optional types; only the binary pressure packet needs a reply first.
                if (!_hostAutocastPressureCapability)
                    _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeAutocastPressureCapabilityMsg());
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
            bool sameProtocol = msg.protocol == Protocol.Version;
            bool sameContent = string.Equals(msg.content, ContentFingerprint.Value, StringComparison.Ordinal);
            bool sameVersion = string.Equals(msg.modVer, HostAuthority.ModVersion, StringComparison.Ordinal);
            _hostInfinityAvailable = msg.infinityAvailable;
            ReceiveContinueHandshake(msg);
            if (msg.authorityGeneration != 0) ObserveMonsterAuthority(msg.authorityGeneration);
            if (sameProtocol && sameContent && sameVersion && msg.continueCheckpoints)
            {
                _hostCompatibilityWarned = false;
                HostVersionWarning = null;
            }
            else if (!_hostCompatibilityWarned)
            {
                _hostCompatibilityWarned = true;
                string theirs = string.IsNullOrEmpty(msg.modVer) ? "?" : msg.modVer;
                // The host's own Infinity availability is shown by the lobby line, so only the shared items are listed here.
                HostVersionWarning = Loc.T(
                    $"ホストの Dreamforge に差があります（{HostAuthority.DescribeDifferencesJa(sameProtocol, sameContent, sameVersion, msg.continueCheckpoints, true)}。版 {theirs}/{HostAuthority.ModVersion}、Protocol {msg.protocol}/{Protocol.Version}）。機能は続行します。",
                    $"The host's Dreamforge differs ({HostAuthority.DescribeDifferencesEn(sameProtocol, sameContent, sameVersion, msg.continueCheckpoints, true)}; version {theirs}/{HostAuthority.ModVersion}, protocol {msg.protocol}/{Protocol.Version}). Features continue.");
                Log.Warn($"Client: compatibility warning: protocol {msg.protocol}/{Protocol.Version}, mod {theirs}/{HostAuthority.ModVersion}, contentEqual={sameContent}, continueCheckpoints={msg.continueCheckpoints}; features continue.");
            }
        }
    }
}
