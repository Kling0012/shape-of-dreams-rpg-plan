using System;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#846）演出プレハブが無い弾の命中・完了・消散 RPC の送信を省く
    /// NativeProjectileIdleRpcPerf の Prefix の検証。送信メソッドの MethodInfo は
    /// 境界（NativeEmbracePositionBoundary.cs）のスタブ Projectile から同名の
    /// 非公開メソッドで取る（Prefix は名前で振り分けるので中身は空でよい）。
    /// (a) 対応する字段が null なら省く（false）、非 null なら送る（true）、
    /// (b) 他の3字段を全部設定していても対応する字段が null なら省く
    /// （送信メソッドと字段の取り違えの検出）、
    /// (c) 設定が無効なら常に送る、(d) switch が知らない名前は常に送る
    /// （Prepare への追加で case を書き忘れても安全側に倒れる）。
    /// </summary>
    public sealed class ProjectileIdleRpcPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Prefix は静的な設定だけを読むため、毎テストで有効に整えて Dispose で
        // 既定（有効）へ戻す。省く・送るの判定は境界のスタブ Projectile の
        // 字段ごとに変わるので、テスト間で持ち越すものはない。
        public ProjectileIdleRpcPerfTests()
        {
            NativePerfOptions.ProjectileIdleRpc = true;
        }

        public void Dispose()
        {
            NativePerfOptions.ProjectileIdleRpc = true;
        }

        private static bool Send(Projectile projectile, MethodBase sender)
            => (bool)typeof(NativeProjectileIdleRpcPerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { projectile, sender });

        // 本体側の送信メソッドと同じ名前の、スタブ Projectile の非公開メソッド。
        private static MethodBase Sender(string name)
            => typeof(Projectile).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

        // (a) 各送信メソッドは、対応する字段が null なら省き、非 null なら送る。
        [Theory]
        [InlineData("RpcPlayOnEntityEffect", nameof(Projectile.effectOnEntity))]
        [InlineData("RpcPlayOnDissipateEffect", nameof(Projectile.effectOnDissipate))]
        [InlineData("RpcStopOnFlyEffect", nameof(Projectile.effectOnFly))]
        [InlineData("RpcPlayOnCompleteEffect", nameof(Projectile.effectOnComplete))]
        public void EachSenderSkipsOnNullFieldAndSendsOnSetField(string senderName, string fieldName)
        {
            var projectile = new Projectile();
            Assert.False(Send(projectile, Sender(senderName)));
            typeof(Projectile).GetField(fieldName).SetValue(projectile, new GameObject());
            Assert.True(Send(projectile, Sender(senderName)));
        }

        // (b) 他の3字段を全部設定していても、対応する字段が null なら省く。
        // 送信メソッドと字段の取り違え（例: RpcPlayOnEntityEffect を effectOnComplete で
        // 判定する）は、対応する字段が null のまま true に変わることでここで検出される。
        // 取り違え先が3字段のどれであっても同じく true に変わるよう、他は全部設定する。
        [Theory]
        [InlineData("RpcPlayOnEntityEffect", nameof(Projectile.effectOnEntity))]
        [InlineData("RpcPlayOnDissipateEffect", nameof(Projectile.effectOnDissipate))]
        [InlineData("RpcStopOnFlyEffect", nameof(Projectile.effectOnFly))]
        [InlineData("RpcPlayOnCompleteEffect", nameof(Projectile.effectOnComplete))]
        public void EachSenderIgnoresForeignFields(string senderName, string ownFieldName)
        {
            var projectile = new Projectile();
            foreach (var field in typeof(Projectile).GetFields())
            {
                if (field.Name != ownFieldName) field.SetValue(projectile, new GameObject());
            }
            Assert.False(Send(projectile, Sender(senderName)));
            typeof(Projectile).GetField(ownFieldName).SetValue(projectile, new GameObject());
            Assert.True(Send(projectile, Sender(senderName)));
        }

        // (c) ProjectileIdleRpc が false なら、字段が null でも常に送る。
        [Fact]
        public void ADisabledOptionAlwaysSends()
        {
            NativePerfOptions.ProjectileIdleRpc = false;
            var projectile = new Projectile();
            Assert.True(Send(projectile, Sender("RpcPlayOnEntityEffect")));
            Assert.True(Send(projectile, Sender("RpcPlayOnDissipateEffect")));
            Assert.True(Send(projectile, Sender("RpcStopOnFlyEffect")));
            Assert.True(Send(projectile, Sender("RpcPlayOnCompleteEffect")));
        }

        // (d) switch が知らない名前（Prepare への追加で case を書き忘れた送信メソッド）
        // は、字段が全部 null でも常に送る。
        [Fact]
        public void AnUnknownSenderNameAlwaysSends()
        {
            var projectile = new Projectile();
            Assert.True(Send(projectile, typeof(object).GetMethod("ToString")));
        }
    }
}
