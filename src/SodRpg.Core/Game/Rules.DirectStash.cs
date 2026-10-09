using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public static partial class Rules
    {
        /// <summary>
        /// 鞄の遺物を保管庫へ直接送れるか。1遠征（RunId）につき1回で、対象が0個なら使えない
        /// （回数も消費しない）。除外品は <see cref="IsSatchelRelicReserved"/>（中断遺物と同じ条件）。
        /// </summary>
        public static bool CanStashSatchelNow(Profile p, ISet<string> reservedUids = null)
        {
            var run = p?.Run;
            if (run == null || string.IsNullOrEmpty(run.RunId) || p.DirectStashUsedRunIds.Contains(run.RunId)) return false;
            for (int i = 0; i < run.Satchel.Count; i++)
                if (!IsSatchelRelicReserved(p, run.Satchel[i], reservedUids)) return true;
            return false;
        }

        /// <summary>
        /// 鞄の未確保品を保管庫へ直接送る（救済用。確保画面が出ないときの保険）。確保ボーナス・確保数・
        /// 依頼・実績には数えず、遠征は終わらずそのまま続く。保管庫のあふれは確保と同じ扱い（欠片化）。
        /// </summary>
        public static List<GameEvent> StashSatchelNow(Profile p, ISet<string> reservedUids = null)
        {
            if (!CanStashSatchelNow(p, reservedUids))
                throw new InvalidOperationException(Loc.T("今は鞄の遺物を保管庫へ送れません。", "Satchel relics cannot be sent to the stash now."));
            var run = p.Run;
            var sent = new List<Relic>();
            for (int i = 0; i < run.Satchel.Count; i++)
            {
                var relic = run.Satchel[i];
                if (IsSatchelRelicReserved(p, relic, reservedUids)) continue;
                sent.Add(relic);
                run.Satchel.RemoveAt(i--);
            }
            var overflow = new List<Relic>();
            FillStash(p, sent, overflow);
            int shards = 0;
            foreach (var r in overflow) shards = SaturatingAdd(shards, Content.SalvageShards(r.Rarity));
            if (shards > 0) p.AddMaterial(Materials.Shard, shards);
            p.DirectStashUsedRunIds.Add(run.RunId);
            var ev = new List<GameEvent> { new GameEvent(EventKind.Recovered, Loc.T(
                $"鞄の遺物{sent.Count}個を保管庫へ送りました（この遠征ではもう使えません）。",
                $"Sent {sent.Count} satchel relic(s) to the stash (this cannot be used again this expedition).")) };
            if (overflow.Count > 0)
                ev.Add(new GameEvent(EventKind.Warning, Loc.T(
                    $"保管庫が一杯のため{overflow.Count}個を欠片にしました。",
                    $"Stash full: {overflow.Count} relic(s) were turned into shards.")));
            return ev;
        }
    }
}
