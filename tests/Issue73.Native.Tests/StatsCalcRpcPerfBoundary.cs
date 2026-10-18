using Mirror;
using SodRpg.Mod;

// （#824）直接リンクした NativePerfPatches.cs の NativeStatsCalcRpcPerf が検証する本体 IL の
// 見本。Mirror.NetworkBehaviour・NetworkWriterPool・EntityStatus は NativeBoundary.cs の定義を
// 使う。ValidateRpcBody は NetworkWriterPool.Get・NetworkBehaviour.SendRPCInternal・
// NetworkWriterPool.Return の3呼び出しだけを許し、ValidateCalcBody は CalculateStats 内の
// 通知呼び出し（RpcInvokeStatsCalculated）がちょうど1つであることを IL で確かめるため、
// 見本は呼び出しの形だけが意味を持つ（実行はしない）。
namespace Issue73.Native.Tests
{
    internal static class StatsCalcIlShapes
    {
        // 出荷時の送信側（Get → SendRPCInternal → Return の3呼び出しのみ）→ 受理。
        internal static void NativeSenderShape(EntityStatus status)
        {
            NetworkWriter writer = NetworkWriterPool.Get();
            status.SendRPCInternal(writer, "System.Void EntityStatus::RpcInvokeStatsCalculated()");
            NetworkWriterPool.Return(writer);
        }

        // writer を2つ取るようになった（呼び出しの数が増えた将来の本体の例）→ 拒否。
        internal static void SenderDoublesTheWriter(EntityStatus status)
        {
            NetworkWriter writer = NetworkWriterPool.Get();
            NetworkWriter spare = NetworkWriterPool.Get();
            status.SendRPCInternal(writer, "System.Void EntityStatus::RpcInvokeStatsCalculated()");
            NetworkWriterPool.Return(writer);
            NetworkWriterPool.Return(spare);
        }

        // writer を返さなくなった（3呼び出しの一方が消えた将来の本体の例）→ 拒否。
        internal static void SenderKeepsTheWriter(EntityStatus status)
        {
            NetworkWriter writer = NetworkWriterPool.Get();
            status.SendRPCInternal(writer, "System.Void EntityStatus::RpcInvokeStatsCalculated()");
        }

        // Get・SendRPCInternal・Return 以外の呼び出しが増えた（副作用が増えた将来の本体の例）→ 拒否。
        internal static void SenderGainedACall(EntityStatus status)
        {
            NetworkWriter writer = NetworkWriterPool.Get();
            System.Math.Max(0, 1);
            status.SendRPCInternal(writer, "System.Void EntityStatus::RpcInvokeStatsCalculated()");
            NetworkWriterPool.Return(writer);
        }

        // CalculateStats 内の通知呼び出しがちょうど1つ（本体 CalculateStats と同じ形。
        // IL 抽出はインライン化しないため、見本から通知を直接呼ぶ）→ 受理。
        internal static void CalcWithOneNotification(EntityStatus status)
        {
            status.RpcInvokeStatsCalculated();
        }

        // 通知を呼ばなくなった（省くと本体の変化が失われる将来の本体の例）→ 拒否。
        internal static void CalcWithoutNotification(EntityStatus status)
        {
            status.MarkStatsDirty();
        }

        // 通知が2回に増えた（保留中の基線と通知の1対1が崩れる将来の本体の例）→ 拒否。
        internal static void CalcWithTwoNotifications(EntityStatus status)
        {
            status.RpcInvokeStatsCalculated();
            status.RpcInvokeStatsCalculated();
        }
    }
}
