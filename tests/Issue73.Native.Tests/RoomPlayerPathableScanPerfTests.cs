using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1163）部屋のロード時に参加者側で走るシーン全体の歩行可能地点の走査を省く
    /// NativeRoomPlayerPathableScanPerf の検証。境界（RoomPlayerPathableScanPerfBoundary.cs）の
    /// 模倣 Room に対して: (a) TryPrepare は成功し、Transpiler は FindInterfacesOfType の呼び出し
    /// を 1 つだけ PathableScan へ差し替え、命令数は増減しない、(b) 実際に Harmony で当てた
    /// Room は、ホスト（NetworkServer.active が真）でも設定オフでも、当てる前の本体と同じ点を
    /// 同じ順で同じだけ本体の走査から受け取る、(c) 純粋な参加者（NetworkServer.active が偽・設定オン）
    /// のときは空で、本体の走査は呼ばれない、(d) 走査の呼び出しが 0 か所・2 か所の本体では
    /// 差し替えを行わず元の IL を返して警告が 1 回だけ出る。
    /// </summary>
    public sealed class RoomPlayerPathableScanPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // パッチ側の静的な欄を読み書きするため、毎テストで欄と結んだ走査デリゲートを空に戻し、
        // 模倣型に向け直して、Dispose で設定と NetworkServer.active を既定へ戻す。テスト間で
        // 持ち越すものはない（点の一覧と呼び出し回数は毎テスト作り直す）。
        public RoomPlayerPathableScanPerfTests()
        {
            ResetPatchState();
            NativePerfOptions.RoomPlayerPathableScan = true;
            Log.Warnings.Clear();
            NetworkServer.active = false;
            PathableScanDew.Calls = 0;
            PathableScanDew.Points.Clear();
            Assert.True(NativeRoomPlayerPathableScanPerf.TryPrepare(
                nameof(PathableScanDew), "FindInterfacesOfType", nameof(PathableScanRoom), nameof(IPathableScanPoint)));
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.RoomPlayerPathableScan = true;
            Log.Warnings.Clear();
            NetworkServer.active = false;
            PathableScanDew.Calls = 0;
            PathableScanDew.Points.Clear();
        }

        private static void ResetPatchState()
        {
            var type = typeof(NativeRoomPlayerPathableScanPerf);
            type.GetField("_target", Hidden).SetValue(null, null);
            type.GetField("_pathablePointType", Hidden).SetValue(null, null);
            type.GetField("_closedScan", Hidden).SetValue(null, null);
            type.GetField("_warned", Hidden).SetValue(null, false);
            // 結んだ走査デリゲートも空に戻す（次の呼び出しで今の _closedScan に結び直す）。
            type.GetNestedTypes(BindingFlags.NonPublic)
                .First(t => t.Name == "ClosedScan`1")
                .MakeGenericType(typeof(IPathableScanPoint))
                .GetField("Scan", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, null);
        }

        private static MethodBase OnStartClientOf(Type room)
            => room.GetMethod("OnStartClient", Hidden);

        private static IEnumerable<CodeInstruction> Transpile(MethodBase target)
            => (IEnumerable<CodeInstruction>)typeof(NativeRoomPlayerPathableScanPerf).GetMethod("Transpiler", Hidden)
                .Invoke(null, new object[] { PatchProcessor.GetOriginalInstructions(target) });

        private static int CountCall(IEnumerable<CodeInstruction> code, string method)
        {
            int count = 0;
            foreach (var instruction in code)
                if (instruction.operand is MethodInfo info && info.Name == method) count++;
            return count;
        }

        // (a) 差し替えは FindInterfacesOfType の呼び出し 1 箇所だけ。命令数は変わらず、警告も出ない。
        [Fact]
        public void TheTranspilerSwapsOnlyTheScanCall()
        {
            var target = OnStartClientOf(typeof(PathableScanRoom));
            var before = PatchProcessor.GetOriginalInstructions(target).ToList();
            var after = Transpile(target).ToList();
            Assert.Empty(Log.Warnings);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(1, CountCall(before, "FindInterfacesOfType"));
            Assert.Equal(0, CountCall(after, "FindInterfacesOfType"));
            Assert.Equal(1, CountCall(after, "PathableScan"));
        }

        // (b) パッチを当てた Room の受け取り。ホスト（NetworkServer.active 真）でも設定オフでも、
        // 当てる前の本体と同じ点を同じ順で受け取り、本体の走査（Dew 側）も同じだけ呼ばれる。
        [Fact]
        public void ThePatchedRoomReceivesTheVanillaScanOnHostAndWhenDisabled()
        {
            var a = new PathableScanPointA();
            var b = new PathableScanPointB();
            PathableScanDew.Points.AddRange(new IPathableScanPoint[] { a, b });
            var vanilla = new PathableScanRoom();
            vanilla.OnStartClient();
            var vanillaScanned = vanilla.scanned.ToArray();

            var harmony = new Harmony("issue73.pathable." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(OnStartClientOf(typeof(PathableScanRoom)),
                    transpiler: new HarmonyMethod(typeof(NativeRoomPlayerPathableScanPerf).GetMethod("Transpiler", Hidden)));
                Assert.Empty(Log.Warnings); // 差し替えが通っている

                // ホスト: 本体の走査を通り、同じ点が同じ順で並ぶ。
                int callsBeforeHost = PathableScanDew.Calls;
                NetworkServer.active = true;
                var hostRoom = new PathableScanRoom();
                hostRoom.OnStartClient();
                Assert.Equal(vanillaScanned, hostRoom.scanned);
                Assert.Equal(callsBeforeHost + 1, PathableScanDew.Calls); // 元の走査が呼ばれた

                // 設定オフ（純粋な参加者でも）: 同じく本体の走査を通る。
                NetworkServer.active = false;
                NativePerfOptions.RoomPlayerPathableScan = false;
                var disabledRoom = new PathableScanRoom();
                disabledRoom.OnStartClient();
                Assert.Equal(vanillaScanned, disabledRoom.scanned);
                Assert.Equal(callsBeforeHost + 2, PathableScanDew.Calls);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (c) 純粋な参加者（NetworkServer.active 偽・設定オン）のとき。Room は空の配列を受け取り、
        // 本体の走査（Dew 側）は呼ばれない。
        [Fact]
        public void ThePatchedRoomSkipsTheScanForGuests()
        {
            PathableScanDew.Points.AddRange(new IPathableScanPoint[] { new PathableScanPointA(), new PathableScanPointB() });

            var harmony = new Harmony("issue73.pathable." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(OnStartClientOf(typeof(PathableScanRoom)),
                    transpiler: new HarmonyMethod(typeof(NativeRoomPlayerPathableScanPerf).GetMethod("Transpiler", Hidden)));
                Assert.Empty(Log.Warnings);
                var guestRoom = new PathableScanRoom();
                guestRoom.OnStartClient();
                Assert.Empty(guestRoom.scanned);
                Assert.Equal(0, PathableScanDew.Calls); // 元の走査は呼ばれていない
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (d) 走査の呼び出しが 0 か所の本体。差し替えは行われず（IL 照合で FindInterfacesOfType が
        // 残り PathableScan は入らない）警告は 1 回だけ。
        [Fact]
        public void AZeroCallBodyIsLeftAlone()
        {
            Assert.True(NativeRoomPlayerPathableScanPerf.TryPrepare(
                nameof(PathableScanDew), "FindInterfacesOfType", nameof(PathableScanRoomNoCall), nameof(IPathableScanPoint)));
            var target = OnStartClientOf(typeof(PathableScanRoomNoCall));
            var before = PatchProcessor.GetOriginalInstructions(target).ToList();
            var transpiled = Transpile(target).ToList();
            Assert.Equal(before.Count, transpiled.Count);
            Assert.Equal(CountCall(before, "FindInterfacesOfType"), CountCall(transpiled, "FindInterfacesOfType"));
            Assert.Equal(0, CountCall(transpiled, "PathableScan"));
            Assert.Single(Log.Warnings);
            Assert.Contains("room player pathable scan", Log.Warnings[0]);
            Transpile(target).ToList();
            Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない
        }

        // (d) 走査の呼び出しが 2 か所並んだ本体。同じく差し替えは行われず警告は 1 回だけ。
        // 実際に当てても挙動は本体のままで、2 か所の走査がどちらも動く。
        [Fact]
        public void ATwoCallBodyIsLeftAlone()
        {
            Assert.True(NativeRoomPlayerPathableScanPerf.TryPrepare(
                nameof(PathableScanDew), "FindInterfacesOfType", nameof(PathableScanRoomTwoCalls), nameof(IPathableScanPoint)));
            var target = OnStartClientOf(typeof(PathableScanRoomTwoCalls));
            var before = PatchProcessor.GetOriginalInstructions(target).ToList();
            var transpiled = Transpile(target).ToList();
            Assert.Equal(before.Count, transpiled.Count);
            Assert.Equal(CountCall(before, "FindInterfacesOfType"), CountCall(transpiled, "FindInterfacesOfType"));
            Assert.Equal(0, CountCall(transpiled, "PathableScan"));
            Assert.Single(Log.Warnings);
            Assert.Contains("room player pathable scan", Log.Warnings[0]);

            var a = new PathableScanPointA();
            PathableScanDew.Points.Add(a);
            var harmony = new Harmony("issue73.pathable." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(target,
                    transpiler: new HarmonyMethod(typeof(NativeRoomPlayerPathableScanPerf).GetMethod("Transpiler", Hidden)));
                var room = new PathableScanRoomTwoCalls();
                room.OnStartClient();
                Assert.Equal(new IPathableScanPoint[] { a, a }, room.scanned); // 本体の 2 か所の走査どおり
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }
    }
}
