using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>戦ったときの深度と道標を保持する、保留中の撃破の記録（#71）。精算はこれを使う。</summary>
    public readonly struct PendingRunKill
    {
        public string RunId { get; }
        public int ZoneIndex { get; }
        public int RoomIndex { get; }
        public MonsterTier Tier { get; }
        public int Level { get; }
        public NightmareAffix Nightmare { get; }
        public string VariantId { get; }
        public string HeroKey { get; }
        public string EventId { get; }
        public uint MonsterNetId { get; }
        public string BossTypeName { get; }
        public bool BossDropNightmare { get; }
        public int BossDropDepth { get; }
        /// <summary>戦ったときの潜行深度。null は記録前の保存データで、精算時の現在深度を使う。</summary>
        public int? Heat { get; }
        /// <summary>戦ったときの道標。null は記録前の保存データで、精算時の道標を使う。</summary>
        public Waypoint? Waypoint { get; }

        public PendingRunKill(string runId, int zoneIndex, int roomIndex, MonsterTier tier, int level,
            NightmareAffix nightmare, string variantId, string heroKey, string eventId = null, uint monsterNetId = 0,
            int? heat = null, Waypoint? waypoint = null, string bossTypeName = null,
            bool bossDropNightmare = false, int bossDropDepth = 0)
        {
            RunId = runId; ZoneIndex = zoneIndex; RoomIndex = roomIndex; Tier = tier; Level = level;
            Nightmare = nightmare; VariantId = variantId; HeroKey = heroKey;
            EventId = eventId; MonsterNetId = monsterNetId;
            Heat = heat; Waypoint = waypoint;
            BossTypeName = bossTypeName; BossDropNightmare = bossDropNightmare;
            BossDropDepth = Math.Max(0, Math.Min(5, bossDropDepth));
        }
    }

    public sealed class PendingRunRewards
    {
        private readonly Queue<PendingRunKill> _kills = new Queue<PendingRunKill>();
        public int Count => _kills.Count;
        public IEnumerable<PendingRunKill> Facts => _kills;
        public bool HasFor(string runId, int zoneIndex) => _kills.Count > 0
            && _kills.Peek().RunId == runId && _kills.Peek().ZoneIndex == zoneIndex;
        public void Add(PendingRunKill kill) { if (!string.IsNullOrEmpty(kill.RunId)) _kills.Enqueue(kill); }
        public void Clear() => _kills.Clear();

        /// <summary>
        /// Preserve event order and never replay a fact into a different expedition.
        /// A kill leaves the queue only after its reward succeeded: a throwing reward keeps the
        /// fact queued (at the head) so settlement can retry it instead of dropping it (#72).
        /// </summary>
        public int Drain(string runId, int zoneIndex, Action<PendingRunKill> reward)
        {
            int awarded = 0;
            while (_kills.Count > 0)
            {
                var kill = _kills.Peek();
                if (kill.RunId != runId) { _kills.Dequeue(); continue; }
                if (kill.ZoneIndex != zoneIndex) break;
                reward(kill);
                _kills.Dequeue();
                awarded++;
            }
            return awarded;
        }
    }
}
