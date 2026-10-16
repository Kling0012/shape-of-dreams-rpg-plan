using System;

// 共有ボスの持ち分（HostAuthority.BossShares.cs / HostAuthority.SummonOwnership.cs）のための
// 本体 API 境界。BossSharesNative.cs の Harmony トランスパイラはこのハーネスでは動かないため、
// ここにはスコープの形だけを用意する。テストは本体の Actor.DealDamage と同じ順で
// Current にスコープを置き（Prefix）、受ダメ補正後に HP setter 相当（ApplyBossShareHealth）で
// 持ち分の消費を確定する（SetNotificationDamage の通知補正は本体側の処理）。
namespace SodRpg.Mod
{
    internal static class BossShareDamageScope
    {
        internal sealed class Scope
        {
            internal Entity Victim;
            internal DewPlayer Player;
            internal readonly double[] Planned = new double[4];
            internal double PlannedDamage, RawBudget;
            internal bool Ready;
            internal float DamageCorrection;
        }

        internal static Scope Current;
        // 本体ではトランスパイラの適用に成功したとき true になる。テストは明示的に切り替える。
        internal static bool NotificationSupported { get; set; }
    }
}
