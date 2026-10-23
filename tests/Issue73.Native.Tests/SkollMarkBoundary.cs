using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

// Skoll 印表示（perf-loop/20261023-2325）のための境界。NativePersistence.targets の
// CompileSkollMarks が HostAuthority.BossSkoll.cs（SkollOwnerState / SkollCooldownAvailable /
// SkollCommitAction / SkollReserve / SkollFacing / SkollSlash / SkollArrowField / SkollRain /
// SkollPublishMarks / DispatchSkollBoss / TickSkollBoss と SkollState・持ち主の台帳）と
// HostAuthority.BossDemon.cs（BossFind）から抽出した実ソースが触る本体 API の形だけを用意する。
// 純粋な計算（BossAlive / BossDirection）は本体の式をそのまま写す。環境に依存する部位
// （カーソル位置・ナビメッシュの着地判定・量と魔法の算出・予約の帳簿）は印の表示には
// 影響しないため形だけにする（SkollBurstBoundary.cs の BossReady と同じ扱い）。
namespace UnityEngine
{
    // 本体 Transform.forward（+Z 方向、Unity と同じく読み取り専用プロパティ）。
    // SkollFacing が持ち主の向きの基準に読む分。既存の部分宣言
    // （CharacterModelDisplayBoundsBoundary.cs など）と同じ形で足す。
    internal partial class Transform
    {
        public Vector3 forward => new Vector3(0f, 0f, 1f);
    }
}
namespace SodRpg.Mod
{
    // 本体 HostAuthority.BossPools.cs の BossPulseBuffer の写し（32 段の固定長と
    // Reset / Add だけ。印の経路の抽出が使う分）。
    internal sealed class BossPulseBuffer
    {
        private readonly HostAuthority.BossScheduledPulse[] _values = new HostAuthority.BossScheduledPulse[32];
        public int Count { get; private set; }
        public BossPulseBuffer Reset() { Array.Clear(_values, 0, Count); Count = 0; return this; }
        public bool Add(HostAuthority.BossScheduledPulse pulse)
        {
            if (Count == _values.Length) return false;
            _values[Count++] = pulse; return true;
        }
    }

    internal sealed partial class HostAuthority
    {
        // 本体 HostAuthority.BossMechanisms.cs の BossScheduledPulse の形。印の経路の抽出が
        // 作るのは（action, center, end, amount, magic, delayMillis）の6引数だけで、標的や
        // 行動妨害の成分はこの検証では読まれないため省く。
        internal readonly struct BossScheduledPulse
        {
            public BossScheduledPulse(BossAction action, Vector3 center, Vector3 end, float amount, bool magic, int delayMillis)
            {
                Action = action; Center = center; End = end; Amount = amount; Magic = magic; DelayMillis = delayMillis;
            }
            public BossAction Action { get; }
            public Vector3 Center { get; }
            public Vector3 End { get; }
            public float Amount { get; }
            public bool Magic { get; }
            public int DelayMillis { get; }
        }

        // 本体 HostAuthority.BossRuntime.cs の BossProgressLedger の形。印の経路では
        // stage2 の印アクション（MainHits=0）しか通らず、帳簿の蓄積は印表示の対象外の
        // ため、常に進めた扱いにする。
        internal sealed class BossProgressLedger
        {
            public bool Advance(string key, long activation, int required, float lifetime, float now) => true;
            public void Consume(string key) { }
        }

        // 本体 HostAuthority.BossMechanisms.cs の BossAlive と同じ式。
        private static bool BossAlive(Entity e) => e != null && e.isActive && e.currentHealth > 0 && (!(e is Hero h) || !h.isKnockedOut);

        // 本体 HostAuthority.BossRuntime.cs の BossDirection と同じ式（XZ 平面の向き）。
        private static Vector3 BossDirection(Vector3 from, Vector3 to) { var d = to - from; d.y = 0; return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero; }

        // 本体はカーソル位置（owner.cursorWorldPos）を返す。印の経路で読むのは満ちた印の解除と
        // charm の目標選択だけで、この検証の対象外のため、ここでは持ち主の位置で代える。
        private static Vector3 BossCursor(HeroRuntime rt) => rt.Hero.agentPosition;

        // 本体はナビメッシュの掃引と視線判定で着地点を拘束する。印の経路では呼ばれないため、
        // XZ の射程内に切り詰める形だけを置く。
        private static bool BossGround(Vector3 origin, Vector3 desired, float range, out Vector3 point)
        {
            var delta = desired - origin; delta.y = 0;
            point = origin + (delta.magnitude <= range ? delta : delta.normalized * range);
            return true;
        }

        // 本体は装備のチャンネル値と本体ステータスから量を決める。印の経路では呼ばれない。
        private static float BossAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, string channel) => 0f;
        // 本体は魔力が攻撃力より高ければ魔法扱いする。印の経路では呼ばれない。
        private static bool BossMagic(HeroRuntime rt) => false;

        // 本体は BossFieldExecutor への予約を経て真偽を返し、予約の成否は印の表示とは無関係。
        // パルスの解放（finally の Reset）だけを写す。
        private bool BossReserveSequence(HeroRuntime rt, string set, string profile, BossPulseBuffer pulses, float now,
            int maxInstances = 1, int lifetimeMillis = 0, long nativeLife = 0, int maxSetInstances = 4, bool replaceOldest = false)
        {
            try { return true; }
            finally { pulses?.Reset(); }
        }

        // 本体 HostAuthority.cs の HeroRuntime の Powers。BossFind と DispatchSkollBoss の
        // moves ループが Build.BossMoves を読む分。
        internal sealed partial class HeroRuntime
        {
            public readonly PowerRuntime Powers = new PowerRuntime(new Build(), 0f);
        }
    }
}
