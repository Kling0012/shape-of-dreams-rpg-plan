using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>
    /// 夢の深さの分だけ、各ゾーンの部屋数（ノード数）を増やす。
    /// 部屋数は ZoneManager.GenerateWorld_Imp が
    /// Range(currentZone.numOfNodes.x, y + 1) + DewBuildProfile.current.worldNodeCountOffset
    /// で決めており、生成はサーバーだけ（LoadNode の [Server] ルーチン）で行われ、
    /// 出来上がった nodes は SyncList で参加者へ届く。だからホストの生成時だけオフセットを足せばよい。
    /// 生成の間だけ足し、Postfix と Finalizer のどちらか一度だけ 正確に足した分を戻す（生成が例外でも元に戻す）。
    /// </summary>
    [HarmonyPatch(typeof(ZoneManager), "GenerateWorld_Imp")]
    internal static class DepthRooms
    {
        // GenerateWorld_Imp は再入しない：呼び出し元は GenerateWorldAuto / GenerateWorldWithSeed だけで、
        // 中で世界を生成し直すことはない。だから静的な「足した分」で足切りと復元を揃えられる。
        private static int _added;

        private static void Prefix(ZoneManager __instance)
        {
            var zone = __instance.currentZone;
            if (zone == null) return;
            // HostRun は NetworkServer.active かつ遠征中のときだけ非 null（夢の圧と同じ深度の源）。
            var run = ClientSession.HostRun;
            int add = DreamDepth.ZoneNodeOffset(run?.DreamDepth ?? 0, NetworkServer.active, zone.useSpecialGeneration, run != null);
            if (add == 0) return;
            DewBuildProfile.current.worldNodeCountOffset += add;
            _added += add;
        }

        private static void Postfix() => Restore();

        // 元の処理が例外を投げてもここで戻す（例外は握りつぶさない）。
        private static void Finalizer() => Restore();

        private static void Restore()
        {
            if (_added == 0) return;
            DewBuildProfile.current.worldNodeCountOffset -= _added;
            _added = 0;
        }
    }
}
