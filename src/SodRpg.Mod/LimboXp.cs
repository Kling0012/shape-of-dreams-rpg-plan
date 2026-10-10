using HarmonyLib;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>
    /// Limbo の深さの分だけ、遠征中に得る経験値（旅人のレベル用）を増やす。
    /// 本体の経験値はすべて Hero.ReceiveExperience（[Server]）に集約される。経験値オーブの
    /// Pickup_BaseExpOrb.GrantExp も Ai_RandomGemUpgrade の昇級付与もここへ来るので、
    /// この入口で一度だけ掛ければ本体側の倍率（オーブの量に既に掛かる ges.expGlobalMultiplier 等）
    /// にさらに乗る形になる。ReceiveExperience はサーバーでしか呼ばれず、HostRun も
    /// ホストかつ遠征中だけ非 null なので、クライアントでは何も変わらない（協力プレイで一貫）。
    /// HostRun が null（遠征外・通常ラン）なら Limbo 以外なので何もしない。
    /// </summary>
    [HarmonyPatch(typeof(Hero), nameof(Hero.ReceiveExperience))]
    internal static class LimboXp
    {
        private static void Prefix(ref int amount)
        {
            if (amount <= 0) return;
            int depth = ClientSession.HostRun?.LimboDepth ?? 0;
            if (depth <= 0) return;
            // 全ての倍率が確定してから丸める（DreamDepth.ScaleReward と同じ規則）。
            amount = DreamDepth.ScaleReward(amount, Rules.LimboXpMultiplier(depth));
        }
    }
}
