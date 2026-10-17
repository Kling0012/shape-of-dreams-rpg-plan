using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // Issue #528 点1: 純白（終点）ゾーンでボス撃破を取りこぼし、進行が止まる。
    public sealed partial class InfinityLobbyStartTests
    {
        /// <summary>
        /// 準備の門（Shrine_PrimusDoor）がボスのノードを直接読み込む純白ゾーンでは、
        /// TryEnterBoss を呼ぶのは TickNative だけ。保存待ち（HostInfinitySaveHoldSatisfied は
        /// 保存失敗時 最大30秒）の間にボスが倒れても、onRoomClear は BossDue の撃破を観測できない。
        /// 保存待ちが解除された後の Tick で、取りこぼした撃破が回収され魂の処理待ちへ進むこと。
        /// </summary>
        [Fact]
        public void PureWhiteBossKilledWhileSaveHoldBlocksTickStillReachesSoulFinish()
        {
            var (session, zone) = StartZoneTransition("issue528-boss-miss");
            var state = session.Profile.Run.Infinity;
            zone.currentZone = SmallPrimusZone();
            zone.SceneZone = zone.currentZone;
            state.FixedZoneId = zone.currentZone.name;
            state.Phase = InfinityPhase.Exploring;
            state.ClearsInCycle = 1;
            zone.currentZoneIndex = 0;
            zone.GenerateWorldAuto();
            Assert.Equal(InfinityPhase.BossDue, state.Phase);

            var room = new Room { isActive = true };
            SingletonDewNetworkBehaviour<Room>.softInstance = room;
            // 門がボスのノードを直接読み込む（地図/投票の RouteTravel を経由しない）到着。
            zone.SetCurrentNodeIndexAndRevealAdjacent(2);
            Assert.Equal(WorldNodeType.ExitBoss, zone.currentNode.type);

            // 保存待ちが成立しない間、TickNative は ExitBoss 分岐の TryEnterBoss に届かない。
            ClientSession.HostInfinitySaveHoldSatisfied = false;
            InfinityMode.Tick();
            Assert.Equal(InfinityPhase.BossDue, state.Phase);

            // その間にボスが倒れる: onRoomClear の ObserveBossClear は BossFight でしか受理されず、
            // 撃破は捨てられる。
            room.didClearRoom = true;
            InfinityMode.OnRoomClear(room);
            Assert.Equal(InfinityPhase.BossDue, state.Phase);

            // 保存待ちが解除された後の Tick で、死亡済みのボス部屋から WaitingSoulFinish に進むこと。
            ClientSession.HostInfinitySaveHoldSatisfied = true;
            InfinityMode.Tick();
            Assert.Equal(InfinityPhase.WaitingSoulFinish, state.Phase);

            // 魂を出して回収し、区間の選択画面（AwaitingChoice）まで到達すること。
            var soul = new Shrine_BossSoul { isActive = true };
            NetworkedManagerBase<ActorManager>.softInstance.AddActor(soul);
            Rift_RoomExit.instance = new Rift_RoomExit();
            InfinityMode.Tick();
            Assert.True(state.SoulObserved);
            NetworkedManagerBase<ActorManager>.softInstance.RemoveActor(soul);
            InfinityMode.Tick();
            Assert.Equal(InfinityPhase.AwaitingChoice, state.Phase);
            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
            DewResources.Zones.Clear();
        }
    }
}
