using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        // この世界で分解（手動・鞄あふれの追加ダスト）から得たドリームダストの表示用の計数。
        // 上限の判定自体はホストの TradeAuthority が行う。ここは画面の表示と、上限に達したときの案内だけ。
        private bool _salvageDustCapNotified;
        private float _nextSalvageDustCapNoticeAt = -1f;

        /// <summary>
        /// 分解でダストを受け取ったとき、この世界の計数に加える（遺物は失われていないものは数えない）。
        /// ホストが残りに合わせて削った分は計数にも残さず、数えた量を返す（成功の案内にも使う）。
        /// </summary>
        internal long CountSalvageDustPaid(long dust)
        {
            var run = Profile.Run;
            if (run == null || dust <= 0) return 0;
            long room = Economy.DustCap(run.SalvageDustZoneIndex, SalvageScale) - run.SalvageDustEarned;
            long granted = Math.Min(dust, Math.Max(0, room));
            if (granted > 0)
            {
                run.SalvageDustEarned += granted;
                _dirty = true; // 普通の定期保存で足りる（表示の計数なので、書き込みの障壁にはしない）。
            }
            return granted;
        }

        /// <summary>世界の切り替えで計数を戻し、追加ダストの支払い済み累計の増えた分を数える。上限に達したら1回だけ案内する。</summary>
        private void TickSalvageDustCap()
        {
            var run = Profile.Run;
            if (run == null) return;
            int zone = CurrentZoneIndex;
            if (zone >= 0 && run.SalvageDustZoneIndex != zone)
            {
                run.SalvageDustZoneIndex = zone;
                run.SalvageDustEarned = 0;
                _salvageDustCapNotified = false;
            }
            // ホストが追加ダストとして支払った累計（同じランのもの）の増えた分だけこの世界に数える。
            // 基準はラン全体で保つ（世界をまたいでも、支払いの増加分だけを次の世界へ数える）。
            if (_overflowBonusReplyRun == run.RunId && _overflowBonusPaid != run.SalvageDustOverflowPaidSeen)
            {
                if (_overflowBonusPaid > run.SalvageDustOverflowPaidSeen)
                {
                    run.SalvageDustEarned += _overflowBonusPaid - run.SalvageDustOverflowPaidSeen;
                    _dirty = true;
                }
                run.SalvageDustOverflowPaidSeen = _overflowBonusPaid;
            }
            if (run.SalvageDustEarned >= Economy.DustCap(run.SalvageDustZoneIndex, SalvageScale) && !_salvageDustCapNotified)
                NotifySalvageDustCapReached();
        }

        /// <summary>上限に達した案内。まとめて分解で大量に断られても、少しの間は1回にまとめる。</summary>
        private void NotifySalvageDustCapReached()
        {
            if (Time.unscaledTime < _nextSalvageDustCapNoticeAt) return;
            _nextSalvageDustCapNoticeAt = Time.unscaledTime + 10f;
            _salvageDustCapNotified = true;
            Emit(new GameEvent(EventKind.Warning, Loc.T(
                "この世界で分解から得られる夢のダストは上限に達しました（次の世界で回復します）。",
                "Salvage Dream Dust from relics has reached this world's cap (it recovers in the next world).", "本世界中分解获得的梦尘已达上限（下一个世界会恢复）。")));
        }
    }
}
