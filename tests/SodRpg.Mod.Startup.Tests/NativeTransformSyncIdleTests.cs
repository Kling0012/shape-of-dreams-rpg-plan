using System.Collections.Generic;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // リンクした実装 NativeTransformSyncIdle.cs の省略判定を、本体 Dew.Core の
    // SyncMovementData 境界スタブ（NativeTransformSyncIdleApi.cs）越しに検証する。
    // 軽量化で setter を省いてよいのは「timestamp 以外がすべて前回値と同一」のときだけ
    // であることを、憲法 原則 II（結果が変わらないことのテスト）に従って確かめる。
    public sealed class NativeTransformSyncIdleTests
    {
        // 動きが止まったアクターの保存標本。速度はすべてゼロ。
        private static SyncMovementData StationarySample() => new SyncMovementData
        {
            channels = SyncTransformChannel.Position,
            isTeleport = false,
            timestamp = 100.0,
            position = new Vector3(12f, 0f, -3f),
            velocity = new Vector3(0f, 0f, 0f),
            rotation = new Quaternion(0f, 0f, 0f, 1f),
            angularVelocity = new Vector3(0f, 0f, 0f),
            scale = new Vector3(1f, 1f, 1f),
            scaleVelocity = new Vector3(0f, 0f, 0f),
        };

        public static IEnumerable<object[]> SamplesDifferingInOneField()
        {
            SyncMovementData With(string field, SyncMovementData sample)
            {
                switch (field)
                {
                    case "channels": sample.channels = SyncTransformChannel.Position | SyncTransformChannel.Rotation; break;
                    case "isTeleport": sample.isTeleport = true; break;
                    case "position": sample.position = new Vector3(13f, 0f, -3f); break;
                    case "velocity": sample.velocity = new Vector3(2f, 0f, 0f); break;
                    case "rotation": sample.rotation = new Quaternion(0.38268343f, 0f, 0f, 0.92387953f); break;
                    case "angularVelocity": sample.angularVelocity = new Vector3(0f, 3f, 0f); break;
                    case "scale": sample.scale = new Vector3(2f, 1f, 1f); break;
                    case "scaleVelocity": sample.scaleVelocity = new Vector3(0.5f, 0f, 0f); break;
                }
                return sample;
            }
            foreach (var field in new[]
            {
                "channels", "isTeleport", "position", "velocity",
                "rotation", "angularVelocity", "scale", "scaleVelocity",
            })
                yield return new object[] { field, With(field, StationarySample()) };
        }

        [Fact]
        public void TimestampOnlyDifferenceIsAnIdleRefresh()
        {
            var current = StationarySample();
            var next = StationarySample();
            next.timestamp = 105.5; // 同期間隔が経過した次の捕捉。timestamp 以外は同一。
            Assert.True(NativeTransformSyncIdle.IsIdleRefresh(in current, in next));
        }

        [Theory]
        [MemberData(nameof(SamplesDifferingInOneField))]
        public void AnyFieldOtherThanTimestampForcesTheRefreshThrough(string field, SyncMovementData next)
        {
            var current = StationarySample();
            Assert.False(NativeTransformSyncIdle.IsIdleRefresh(in current, in next),
                field + " が変わる標本は省略してはならない");
        }

        [Fact]
        public void VelocityStoppingToZeroForcesTheRefreshThrough()
        {
            // 停止直後の標本。速度が非0から0へ変わるため、位置が同じでも省略しない。
            var current = StationarySample();
            current.velocity = new Vector3(2f, 0f, 0f);
            var next = StationarySample();
            next.timestamp = 100.1;
            Assert.False(NativeTransformSyncIdle.IsIdleRefresh(in current, in next));
        }
    }
}
