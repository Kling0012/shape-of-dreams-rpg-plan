using System;
using SodRpg.Core.Game;
using UnityEngine;

// Nyx 印表示（perf-loop/20261024-0045）のための境界。NativePersistence.targets の
// CompileNyxMarks が HostAuthority.BossNyx.cs（DispatchNyxBoss / TickNyxBoss /
// NyxReconcileTokens / NyxPublishMarks / NyxPlaceMarker / 星点・種・領域の消去 /
// NyxAvailable / NyxReserve / NyxCommitReserve / NyxFacing / NyxStartChannel /
// NyxStopDash と NyxState・NyxChannelState・持ち主の台帳）から抽出した実ソースが
// 触る本体 API の形だけを用意する。印の表示に影響しない環境依存の部位（敵の引き寄せ・
// 種・領域・ダッシュの毎ティック処理・星界の運命の世界更新・予約の帳簿・障壁と移動の
// 実行）は形だけにする（SkollMarkBoundary.cs と同じ扱い）。PublishBossVisual とその状態は
// CompileSkollBurst の、BossFind は CompileSkollMarks の抽出した実物を使う。
namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // 本体 HostAuthority.BossNyx.cs の NyxMovedTarget（private nested struct）。抽出タスクは
        // nested class しか抜けないため、抽出された NyxChannelState が持つ分だけここに置く。
        // 抽出側の NyxChannelState が internal で Targets を公開するため、ここも internal にする
        // （SkollBurstBoundary.cs の SkollCorePending と同じ扱い）。
        internal struct NyxMovedTarget
        {
            internal Entity Target;
            internal long Life;
            internal float Moved;
        }

        // 本体 HostAuthority.BossMechanisms.cs の移動と障壁の実行の形。印の経路の抽出
        // （DispatchNyxBoss の feet と armor、NyxStartChannel の障壁）が呼び出しの形だけで
        // 触る分。この検証ではどの部位も装備しないため、常に失敗（何もしない）でよい。
        internal sealed class BossMovementExecutor
        {
            public bool Execute(HostAuthority host, HeroRuntime rt, BossAction a, Vector3 target, float now) => false;
        }
        internal sealed class BossDefenseExecutor
        {
            public bool Execute(HostAuthority host, HeroRuntime rt, string source, BossAction a, float amount, float now, bool linearDecay = false) => false;
        }

        // SkollBurstBoundary.cs の BossCombatState への追加（Nyx の経路の抽出が読む分）。
        // Build は TickNyxBoss の継続条件（本体では装備の喪失でティックを止める。ここでは
        // 常に続ける）、Movement / Defense は上の呼び出しの形。
        internal sealed partial class BossCombatState
        {
            public Build Build = new Build();
            public readonly BossMovementExecutor Movement = new BossMovementExecutor();
            public readonly BossDefenseExecutor Defense = new BossDefenseExecutor();
        }

        // 本体 HostAuthority.BossMechanisms.cs の予約の帳簿（BossFieldExecutor）の代わり。
        // 印の表示には予約の識別子と存在判定だけが効くため、番号を払い出して存在すると
        // 扱う（予約の成否は SkollMarkBoundary.cs の BossReserveSequence と同じく対象外）。
        private static long _nyxReservationIds;
        private static long BossReservationId(HeroRuntime rt, string set, string profile) => ++_nyxReservationIds;
        private static bool BossReservationExists(HeroRuntime rt, long id) => id != 0;
        private void BossCancelReservation(HeroRuntime rt, long id) { }

        // 本体 HostAuthority.BossNyx.cs の NyxShortPull（星点の起動時の引き寄せ）。敵を動かす
        // だけで印の表示には影響しないため形だけにする。
        private void NyxShortPull(HeroRuntime rt, Vector3 center, Vector3 destination, int maxTargets, float now) { }
        // 本体 HostAuthority.BossNyx.cs の種・領域・ダッシュの毎ティック処理。種（kind 3）と
        // 領域（kind 4）の表示は印（kind 9）の経路とは独立で、ダッシュは表示を持たない。
        // この検証の対象外。
        private void NyxTickSeed(HeroRuntime rt, NyxState state, float now) { }
        private void NyxTickChannel(HeroRuntime rt, NyxState state, float now) { }
        private void NyxTickDash(HeroRuntime rt, NyxState state, float now) { }
        // 本体 HostAuthority.BossHerWorld.cs の星界の運命の世界更新。報酬側の処理で印の表示に
        // は影響しないため形だけにする。
        private void TickNyxWorlds(HeroRuntime rt, float now) { }

        // ---- テストからの準備フック（本体には存在しない） ----
        internal NyxState NyxOwnerForTest(HeroRuntime rt) => NyxGet(rt);
    }
}
