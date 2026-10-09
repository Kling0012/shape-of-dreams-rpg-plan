using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    public static partial class ProfileCodec
    {
        private static void ReadInterruptedRelics(JsonObject body, Profile p, List<string> notes)
        {
            ReadInterruptedIds(body, "interruptedRelicsExecuted", p.InterruptedRelicsExecuted);
            ReadInterruptedIds(body, "interruptedRelicsClaimedRunIds", p.InterruptedRelicsClaimedRunIds);
            ReadInterruptedIds(body, "interruptedRelicsRetiredSourceRunIds", p.InterruptedRelicsRetiredSourceRunIds);
            // 直送の使用済み遠征も同じ任意項目として読む（古い保存なら無くて空）。
            ReadInterruptedIds(body, "directStashUsedRunIds", p.DirectStashUsedRunIds);
            p.InterruptedRelicsId = Str(body, "interruptedRelicsId");
            p.InterruptedRelicsRunId = Str(body, "interruptedRelicsRunId");
            var relics = new List<Relic>();
            ReadRelics(body, "interruptedRelics", relics, notes);
            if (relics.Count == 0)
            {
                Rules.ClearInterruptedRelics(p);
                return;
            }
            // A malformed recovery feature must not prevent loading the rest of this profile.
            if (string.IsNullOrEmpty(p.InterruptedRelicsId) || string.IsNullOrEmpty(p.InterruptedRelicsRunId)
                || p.InterruptedRelicsExecuted.Contains(p.InterruptedRelicsId)
                || p.InterruptedRelicsRetiredSourceRunIds.Contains(p.InterruptedRelicsRunId))
            {
                notes.Add("interruptedRelics: invalid or consumed batch ignored");
                Rules.ClearInterruptedRelics(p);
                return;
            }
            foreach (var relic in relics)
            {
                if (p.ContainsRelicUid(relic.Uid)
                    || p.PendingTrades.Any(trade => trade.Uid == relic.Uid || trade.Relic?.Uid == relic.Uid))
                {
                    notes.Add("interruptedRelics: duplicate owned relic ignored: " + relic.Uid);
                    continue;
                }
                p.InterruptedRelics.Add(relic);
            }
            if (p.InterruptedRelics.Count == 0) Rules.ClearInterruptedRelics(p);
        }

        private static void ReadInterruptedIds(JsonObject body, string key, ICollection<string> into)
        {
            if (!body.TryGet(key, out object value) || !(value is List<object> ids)) return;
            foreach (var id in ids)
                if (id is string text && !string.IsNullOrEmpty(text)) into.Add(text);
        }
    }
}
