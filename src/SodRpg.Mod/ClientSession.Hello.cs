using System;
using System.Collections.Generic;
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
        private int _hostMeleeCoop;
        /// <summary>ホストの近接・協力設定（hello の返事）。0 は旧版のホスト・未受信・自分がホスト。表示は自分の設定で代わりに見積もる。</summary>
        internal int HostMeleeCoop => _hostMeleeCoop;
        private string _continueReceiptRunId, _continueReceiptCheckpointId, _continueReceiptResumeSession;
        private string _sentContinueReceiptRunId, _sentContinueReceiptCheckpointId, _sentContinueReceiptResumeSession;
        private Actor _sentContinueReceiptActor;
        internal static bool RemoteHostInfinityAvailable => _hostSession?._hostInfinityAvailable == true;
        // #144: a participant with no host answer yet is "waiting", not "disabled".
        internal static bool RemoteHostHelloAnswered => _hostSession?._helloAnswered == true;

        /// <summary>ホストと版が違う／ホストから返事がないときの説明。問題なければ null。</summary>
        private Txt _hostVersionWarning;
        public string HostVersionWarning => _hostVersionWarning?.ToString();
        private Action<DreamforgeHeroConnectorMsg> _onHeroConnector;
        /// <summary>ホストの拡張旅人の宣言が自分と一致しない旅人の名前一覧（§9）。差し引きが変わったら文を更新する。</summary>
        private Txt _heroConnectorWarning;
        /// <summary>ホストの Dreamforge が自分の拡張旅人を読めないため、その旅人の星が反映されない説明。該当しなければ null。</summary>
        public string HeroConnectorHostNotice => _heroConnectorWarning?.ToString();

        private void RegisterHello(Actor actor)
        {
            if (_onHello == null) _onHello = OnHello;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeHelloMsg>(_onHello);
            try
            {
                if (_onHeroConnector == null) _onHeroConnector = OnHeroConnector;
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeHeroConnectorMsg>(_onHeroConnector);
            }
            catch (Exception) { }
            RegisterOverflowBonus(actor);
            RegisterBuildInputCapability(actor);
            RegisterBountyCounts(actor);
            RegisterRoomOmens(actor);
            RegisterAutocastPressureCapability(actor);
            RegisterSkillState(actor);
        }

        private void UnregisterHello(Actor actor)
        {
            if (_onHello != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeHelloMsg>(_onHello); } catch (Exception) { }
            if (_onHeroConnector != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeHeroConnectorMsg>(_onHeroConnector); } catch (Exception) { }
            UnregisterOverflowBonus(actor);
            UnregisterBuildInputCapability(actor);
            UnregisterBountyCounts(actor);
            UnregisterRoomOmens(actor);
            UnregisterAutocastPressureCapability(actor);
            UnregisterSkillState(actor);
        }

        private void ResetHello()
        {
            _helloFirstSent = -1f;
            _nextHello = 0f;
            _helloAnswered = false;
            _hostCompatibilityWarned = false;
            _hostInfinityAvailable = false;
            _hostMeleeCoop = 0;
            _hostBuildInputCapability = false;
            _hostForbiddenInput = false;
            _hostFusionInput = false;
            _forbiddenGearStripNotice = false;
            ForbiddenGearHostNotice = null;
            _hostNetLite = false;
            _netLiteReceiveReady = false;
            _hostAutocastPressureCapability = false;
            _nextAutocastPressureReport = 0f;
            _heroConnectorWarning = null;
            ResetHelloSchedule();
            _hostVersionWarning = null;
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
            // 復元状態を書き終えてから受領を通知する。保存先が無効な場合も通知しない。
            // 失敗時は未通知のままにし、次のHelloで確認付き保存を再試行する。
            if (!SaveNow(true)) return;
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
            RecordHelloSchedule(now);
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
            TickHelloSchedule();
            // 未設定のときだけ作る。言語変更時も含め、ほかの経路が設定した警告文を上書きしない。
            // 日英を保持し、表示時に解決する（毎フレームの文字列確保を避ける）。
            if (!_helloAnswered && _helloFirstSent >= 0 && now - _helloFirstSent > 20f
                && HostVersionWarning == null)
            {
                _hostVersionWarning = new Txt(
                    "ホストから Dreamforge の返事がありません。ホストがこの MOD の同じ版（" + HostAuthority.ModVersion + "）を入れているか確認してください。",
                    "No Dreamforge reply from the host. Make sure the host runs the same version of this mod (" + HostAuthority.ModVersion + ").", "主机没有回应 Dreamforge。请确认主机安装了相同版本（" + HostAuthority.ModVersion + "）的本MOD。");
            }
        }

        private void OnHello(DreamforgeHelloMsg msg)
        {
            if (msg == null) return;
            _helloAnswered = true;
            bool sameProtocol = msg.protocol == Protocol.Version;
            bool sameContent = string.Equals(msg.content, ContentFingerprint.Value, StringComparison.Ordinal);
            bool sameVersion = string.Equals(msg.modVer, HostAuthority.ModVersion, StringComparison.Ordinal);
            _hostInfinityAvailable = msg.infinityAvailable;
            _hostMeleeCoop = msg.meleeCoop;
            ReceiveContinueHandshake(msg);
            if (msg.authorityGeneration != 0) ObserveMonsterAuthority(msg.authorityGeneration);
            if (sameProtocol && sameContent && sameVersion && msg.continueCheckpoints)
            {
                _hostCompatibilityWarned = false;
                _hostVersionWarning = null;
            }
            else if (!_hostCompatibilityWarned)
            {
                _hostCompatibilityWarned = true;
                string theirs = string.IsNullOrEmpty(msg.modVer) ? "?" : msg.modVer;
                // The host's own Infinity availability is shown by the lobby line, so only the shared items are listed here.
                _hostVersionWarning = new Txt(
                    $"ホストの Dreamforge に差があります（{HostAuthority.DescribeDifferencesJa(sameProtocol, sameContent, sameVersion, msg.continueCheckpoints, true)}。版 {theirs}/{HostAuthority.ModVersion}、Protocol {msg.protocol}/{Protocol.Version}）。機能は続行します。",
                    $"The host's Dreamforge differs ({HostAuthority.DescribeDifferencesEn(sameProtocol, sameContent, sameVersion, msg.continueCheckpoints, true)}; version {theirs}/{HostAuthority.ModVersion}, protocol {msg.protocol}/{Protocol.Version}). Features continue.", $"主机的 Dreamforge 存在差异（{HostAuthority.DescribeDifferencesZh(sameProtocol, sameContent, sameVersion, msg.continueCheckpoints, true)}。版 {theirs}/{HostAuthority.ModVersion}、Protocol {msg.protocol}/{Protocol.Version}）。功能将继续运行。");
                Log.Warn($"Client: compatibility warning: protocol {msg.protocol}/{Protocol.Version}, mod {theirs}/{HostAuthority.ModVersion}, contentEqual={sameContent}, continueCheckpoints={msg.continueCheckpoints}; features continue.");
            }
        }

        /// <summary>
        /// ホスト → 参加者: ホストの拡張旅人の登録簿の一覧（§9）。自分の宣言のうちホストに無いもの・
        /// 宣言の異なるものは、その旅人の星がこの部屋で反映されない（ホストの星図検証で拒否される）ので、
        /// 1行の知らせで伝える。プレイを止めたりはしない。
        /// </summary>
        private void OnHeroConnector(DreamforgeHeroConnectorMsg msg)
        {
            try
            {
                if (!_heroConnectorEnabled || msg == null || msg.version != 1 || !_hostHeroConnector) return;
                var hosts = msg.heroes;
                var hashes = msg.hashes;
                if (hosts == null || hashes == null || hosts.Length != hashes.Length || hosts.Length > 64) return;
                var hostSet = new Dictionary<string, string>(hosts.Length, StringComparer.Ordinal);
                foreach (string key in hosts) if (string.IsNullOrEmpty(key)) return;
                for (int i = 0; i < hosts.Length; i++) hostSet[hosts[i]] = hashes[i];
                var mismatched = new List<string>();
                foreach (var hero in ExtendedHeroes.Registered)
                    if (!hostSet.TryGetValue(hero.Key, out string hash) || hash != hero.DeclarationHash)
                        mismatched.Add(HeroNames.Display(hero.Key));
                if (mismatched.Count == 0)
                {
                    _heroConnectorWarning = null;
                    return;
                }
                var warning = new Txt(
                    $"ホストの Dreamforge が次の拡張旅人を読み込んでいないため、この部屋ではその旅人の星が反映されません: {string.Join("、", mismatched)}",
                    $"The host's Dreamforge does not have these extended travelers, so their stars will not apply in this room: {string.Join(", ", mismatched)}",
                    $"主机的 Dreamforge 未加载以下扩展旅人，其星章不会在这个房间生效: {string.Join("、", mismatched)}");
                if (_heroConnectorWarning?.Ja != warning.Ja)
                {
                    _heroConnectorWarning = warning;
                    Log.Warn("Client: hero connector mismatch with the host: " + string.Join(", ", mismatched) + "; those stars will not apply.");
                }
            }
            catch (Exception ex) { Log.Error("Client: hero connector exchange " + ex); }
        }
    }
}
