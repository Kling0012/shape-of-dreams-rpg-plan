using System;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private DreamforgePressureMsg _catchupDisplay;
        private string _catchupNotice;
        private float _catchupNoticeOutgoing, _catchupNoticeIncoming;
        private bool _catchupNoticeJapanese;
        public bool CatchupSettingKnown => CanChooseRunRules || _catchupDisplay?.catchupAvailable == true;
        public bool CatchupSettingEnabled => CanChooseRunRules ? HostAuthority.CatchupEnabled
            : _catchupDisplay?.catchupEnabled == true;

        partial void ReceiveCatchup(DreamforgePressureMsg msg)
        {
            // Retain the latest report through loading; room/hero identity is checked when displayed.
            _catchupDisplay = msg.catchupAvailable ? msg : null;
        }

        partial void ResetCatchupDisplay() => _catchupDisplay = null;

        public string CatchupNotice
        {
            get
            {
                var msg = _catchupDisplay;
                var game = NetworkedManagerBase<GameManager>.softInstance;
                var hero = LocalHero;
                if (msg == null || !msg.catchupEnabled || game == null || hero == null || hero.owner == null
                    || msg.catchupRunId != game.runId || _zone == null || _zone.isInAnyTransition
                    || msg.catchupZone != _zone.currentZoneIndex || msg.catchupNode != _zone.currentNodeIndex
                    || msg.catchupRoom != _zone.currentRoomIndex
                    || msg.catchupGraph != (Profile.Run?.Infinity?.GraphEpoch ?? 0)) return null;
                var ids = msg.catchupPlayerIds;
                if (ids == null || msg.catchupOutgoing == null || msg.catchupIncoming == null
                    || ids.Length != msg.catchupOutgoing.Length || ids.Length != msg.catchupIncoming.Length) return null;
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] != hero.owner.netId) continue;
                    float outgoing = msg.catchupOutgoing[i], incoming = msg.catchupIncoming[i];
                    if (float.IsNaN(outgoing) || float.IsInfinity(outgoing) || outgoing < 1
                        || float.IsNaN(incoming) || float.IsInfinity(incoming) || incoming <= 0 || incoming > 1
                        || outgoing == 1 && incoming == 1) return null;
                    if (_catchupNotice == null || _catchupNoticeOutgoing != outgoing || _catchupNoticeIncoming != incoming
                        || _catchupNoticeJapanese != Loc.Japanese)
                    {
                        _catchupNoticeOutgoing = outgoing;
                        _catchupNoticeIncoming = incoming;
                        _catchupNoticeJapanese = Loc.Japanese;
                        _catchupNotice = Loc.T($"同行補正 与ダメ ×{outgoing:0.00} / 被ダメ ×{incoming:0.00}",
                            $"Co-op catch-up Damage ×{outgoing:0.00} / Taken ×{incoming:0.00}");
                    }
                    return _catchupNotice;
                }
                return null;
            }
        }
    }
}
