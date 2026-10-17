using SodRpg.Mod;

namespace Issue73.Native.Tests
{
    /// <summary>ホストの走査と同じく、ランの世界を順に入ったことにする（#700: 担当外の減衰は世界の累計で決まる）。</summary>
    internal static class RunWorldSteps
    {
        internal static void Enter(HostAuthority host, int world)
        {
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            for (int i = 0; i <= world; i++)
            {
                zone.currentZoneIndex = i;
                host.ObserveRunWorld();
            }
        }
    }
}
