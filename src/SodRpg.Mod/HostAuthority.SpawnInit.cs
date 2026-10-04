using System;

namespace SodRpg.Mod
{
    /// <summary>
    /// 選択待ち中の敵初期化の規則（#60）。
    /// 通常ルートは最初の戦闑で確保／潜行を確定するため、敵の初期化は確定後でよい。
    /// 純白の入口など戦闑では選択を解決できない経路は、未確定の道標を選ばず、
    /// 現在の確定済みの状態（道標なし・現在の潜行深度）で必須初期化を進める。
    /// </summary>
    internal static class SpawnInitRules
    {
        /// <summary>選択待ち中でも出現処理を進めるか。戦闑では選択を解決できない間は、初期化を確定待ちで止めない。</summary>
        public static bool ProcessesWhileAwaitingChoice(bool awaitingChoice, bool combatChoiceSuspended)
            => !awaitingChoice || combatChoiceSuspended;

        /// <summary>既存敵の深度ボーナスを置き換えるか。深くなったときだけ置き換え、浅く確保しても下げない（二重に掛けない）。</summary>
        public static bool RealignsDepthBonus(int appliedDepth, int partyDepth) => partyDepth > appliedDepth;

        /// <summary>
        /// 深度ボーナスの置き換え後の現在HP（#68）。本体の再計算はHPの割合を保つため、削った敵は
        /// 置き換え前の絶対値へ戻す（新しい最大HPまで）。傷ついていない敵は新しい最大HPのまま満タンにする。
        /// </summary>
        public static float HealthAfterDepthRealign(float healthBefore, float maxHealthBefore, float maxHealthAfter)
        {
            if (maxHealthAfter <= 0f) return 0f;
            if (healthBefore >= maxHealthBefore) return maxHealthAfter;
            return Math.Min(healthBefore, maxHealthAfter);
        }
    }
}
