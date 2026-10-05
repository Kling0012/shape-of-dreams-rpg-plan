using System;

namespace SodRpg.Core.Game
{
    public static partial class Rules
    {
        internal static GameEvent ApplyPressureDividend(Profile profile, PressureDividendReward reward)
        {
            if (profile?.Run == null || reward == null || profile.Run.RunId != reward.RunId)
                throw new InvalidOperationException("A pressure dividend belongs to its active expedition.");
            profile.Run.SatchelShards = (int)Math.Min(int.MaxValue, (long)profile.Run.SatchelShards + reward.ShardCount);
            return new GameEvent(EventKind.Info, Loc.T(
                "夢の圧の報酬として、未確保の欠片を1個獲得しました（敵1体につき最大1個）。",
                "Pressure dividend: gained 1 unsecured shard (maximum 1 per enemy)."));
        }
    }
}
