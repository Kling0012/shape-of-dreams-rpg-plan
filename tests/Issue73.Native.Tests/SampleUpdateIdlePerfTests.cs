using System;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#854）狙いが変わっていないキャストプレビュー更新（DispatchSample_Update）の
    /// 再送を省く NativeSampleUpdateIdlePerf の Prefix と、サンプリング文脈の受け渡し
    /// （UserCode_TpcSetSampleContext の配送）で記憶を消す NativeSampleContextResetPerf
    /// の Postfix の検証。境界（NativeBoundary.cs）の CastInfo は本体と同じ5字段の
    /// 等値判定を持つ: (a) 同じ値の再送は2回目を省く、(b) 5字段のどれが変わっても
    /// 送る、(c) 値が戻ったら（A→B→A）毎回送る、(d) 送信者が変われば同じ値でも送る、
    /// (e) 文脈が変わったら同じ値でも最初の1回は送る（前のチャージの最後の送信と
    /// 同じ値でも、ホストの新しい ChargingChannel はまだ更新を1度も受け取って
    /// いない）、(f) 設定が無効なら常に送る。
    /// </summary>
    public sealed class SampleUpdateIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Prefix は直前の送信者と値を静的に覚えるため、毎テストで整えて Dispose でも
        // 記憶を空へ戻す（テスト間で持ち越すものはない）。設定も毎テストで有効にする。
        public SampleUpdateIdlePerfTests()
        {
            NativePerfOptions.SampleUpdateIdle = true;
            NativeSampleUpdateIdlePerf.Reset();
        }

        public void Dispose()
        {
            NativePerfOptions.SampleUpdateIdle = true;
            NativeSampleUpdateIdlePerf.Reset();
        }

        private static bool Send(DewPlayer player, CastInfo info)
            => (bool)typeof(NativeSampleUpdateIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { player, info });

        // 実行時は UserCode_TpcSetSampleContext の配送の直後に Harmony の Postfix が走る。
        private static void ContextDelivered()
            => typeof(NativeSampleContextResetPerf).GetMethod("Postfix", Hidden).Invoke(null, null);

        private static CastInfo Info()
            => new CastInfo { caster = new Entity(), target = new Entity(), point = new Vector3(1f, 2f, 3f), angle = 30f, animSelectValue = 0.5f };

        // (a) 同じ値を2回送ると、2回目の再送を省く。
        [Fact]
        public void ARepeatedInfoSkipsTheSecondSend()
        {
            var player = new DewPlayer();
            var info = Info();
            Assert.True(Send(player, info));
            Assert.False(Send(player, info));
        }

        // (b) 5字段のどれか1つでも変われば送る。等値判定が本体と同じ5字段を比べて
        // いることの確認（比べる字段の取り違えは、変わっていない字段だけを比べて
        // いる場合にここで false のまま検出される）。変わった後の同じ値の再送は
        // 再び省く。
        [Theory]
        [InlineData(nameof(CastInfo.caster))]
        [InlineData(nameof(CastInfo.target))]
        [InlineData(nameof(CastInfo.point))]
        [InlineData(nameof(CastInfo.angle))]
        [InlineData(nameof(CastInfo.animSelectValue))]
        public void AChangeInAnyFieldSends(string field)
        {
            var player = new DewPlayer();
            var first = Info();
            Assert.True(Send(player, first));
            Assert.False(Send(player, first));
            var changed = first;
            switch (field)
            {
                case nameof(CastInfo.caster): changed.caster = new Entity(); break;
                case nameof(CastInfo.target): changed.target = new Entity(); break;
                case nameof(CastInfo.point): changed.point = new Vector3(4f, 5f, 6f); break;
                case nameof(CastInfo.angle): changed.angle = first.angle + 1f; break;
                case nameof(CastInfo.animSelectValue): changed.animSelectValue = first.animSelectValue + 1f; break;
            }
            Assert.True(Send(player, changed));
            Assert.False(Send(player, changed));
        }

        // (c) 値が戻ったら（A→B→A）毎回送る。直前の値との比較だけなので、かつて送った
        // 値への戻りは再送として扱わない。A と B は caster・target の参照が違う別の値。
        [Fact]
        public void ARevertedValueSendsEveryTime()
        {
            var player = new DewPlayer();
            var a = Info();
            var b = Info();
            Assert.True(Send(player, a));
            Assert.True(Send(player, b));
            Assert.True(Send(player, a));
        }

        // (d) 送信者が変われば同じ値でも送る。直前が別の送信者なら、元の送信者への
        // 戻りも送る。
        [Fact]
        public void ADifferentSenderSendsEvenTheSameValue()
        {
            var info = Info();
            var first = new DewPlayer();
            var second = new DewPlayer();
            Assert.True(Send(first, info));
            Assert.False(Send(first, info));
            Assert.True(Send(second, info));
            Assert.True(Send(first, info));
        }

        // (e) 文脈が変わったら（Postfix の後）同じ値でも最初の1回は送る。前のチャージの
        // 最後の送信と同じ値のまま同じ対象へもう一度チャージした場合に相当し、ホストの
        // 新しい ChargingChannel はまだ一度も更新を受け取っていない。新しい文脈の中の
        // 同じ値の再送は再び省く。
        [Fact]
        public void ANewContextSendsItsFirstUpdateEvenWithTheSameValue()
        {
            var player = new DewPlayer();
            var info = Info();
            Assert.True(Send(player, info));
            Assert.False(Send(player, info));
            ContextDelivered();
            Assert.True(Send(player, info));
            Assert.False(Send(player, info));
        }

        // (f) SampleUpdateIdle が false なら、常に送る。
        [Fact]
        public void ADisabledOptionAlwaysSends()
        {
            NativePerfOptions.SampleUpdateIdle = false;
            var player = new DewPlayer();
            var info = Info();
            Assert.True(Send(player, info));
            Assert.True(Send(player, info));
        }
    }
}
