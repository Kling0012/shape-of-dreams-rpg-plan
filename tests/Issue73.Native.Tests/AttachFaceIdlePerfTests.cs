using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#760）面取り付け装飾の変わっていない焼きメッシュ照会の省略（NativeAttachFaceIdlePerf）の検証。
    /// 本体 Dew.GetBakedMeshOptimized の判定は Time.unscaledTime 基準（2026-10-18 逆コンパイル確認:
    /// 再焼き込みは Time.unscaledTime - bakeUnscaledTime &gt; BakeMeshInterval のときだけ、isNew は
    /// bakeUnscaledTime == Time.unscaledTime のときだけ真）なので、記録と判定も同じ時刻で行う。
    /// ShouldSkipLookup は境界（焼き直しと同じフレーム・記録の方が新しい時刻・間隔ちょうど・間隔内・
    /// 間隔超過）、Prefix は記録なし・設定 OFF で本体に戻ることと、焼き直し後の間隔内の照会だけを
    /// 偽で省略すること、Postfix は isNew のとき、省略（nativeAttachFaceIdle）と焼き頂点の
    /// 共有（nativeAttachFaceSharedVerts）のどちらかが ON のときだけ記録すること、
    /// RecordNewBake はしきい値超過時に
    /// 間隔を過ぎた項目だけを掃除して省略を続けること、PruneStale は前回の掃除から間隔が過ぎるまで
    /// 再走査しないことを確かめる。
    /// </summary>
    public sealed class AttachFaceIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Dictionary<int, float> LastNewBake()
            => (Dictionary<int, float>)typeof(NativeAttachFaceIdlePerf).GetField("LastNewBake", Hidden).GetValue(null);

        // PruneStale は前回の掃除から間隔が過ぎたときだけ走査する。テストは時刻を飛ばすので、
        // 順序に依存しないように掃除の時刻を毎回初期化する。
        private static void ResetPruneClock()
            => typeof(NativeAttachFaceIdlePerf).GetField("_lastPruneTime", Hidden).SetValue(null, 0f);

        private static bool Prefix(AttachToFace face, out bool result)
        {
            var method = typeof(NativeAttachFaceIdlePerf).GetMethod("Prefix", Hidden);
            Assert.NotNull(method);
            object[] args = { face, false };
            bool runsOriginal = (bool)method.Invoke(null, args);
            result = (bool)args[1];
            return runsOriginal;
        }

        private static void Postfix(SkinnedMeshRenderer skin, bool isNew)
        {
            var method = typeof(NativeAttachFaceBakeRecord).GetMethod("Postfix", Hidden);
            Assert.NotNull(method);
            method.Invoke(null, new object[] { skin, isNew });
        }

        private static AttachToFace Face(SkinnedMeshRenderer skin)
            => new AttachToFace { targetRenderer = skin, vertexIndex0 = 3 };

        [Fact]
        public void ShouldSkipLookupSkipsOnlyBetweenFramesWithinTheInterval()
        {
            // 間隔に 0.25f を使い、境界値が浮動小数点でちょうど表せるようにする。
            Assert.False(NativeAttachFaceIdlePerf.ShouldSkipLookup(5f, 5f, 0.25f)); // 焼き直しと同じフレーム
            Assert.False(NativeAttachFaceIdlePerf.ShouldSkipLookup(5.5f, 5f, 0.25f)); // 記録の方が新しい時刻
            Assert.True(NativeAttachFaceIdlePerf.ShouldSkipLookup(5f, 5.25f, 0.25f)); // 間隔ちょうど
            Assert.True(NativeAttachFaceIdlePerf.ShouldSkipLookup(5.1f, 5.25f, 0.25f)); // 間隔内
            Assert.False(NativeAttachFaceIdlePerf.ShouldSkipLookup(5f, 5.5f, 0.25f)); // 間隔超過
        }

        [Fact]
        public void PrefixSkipsTheLookupOnlyBetweenBakesWithinTheInterval()
        {
            var savedTime = Time.unscaledTime;
            try
            {
                Dew.BakeMeshInterval = 0.1f;
                LastNewBake().Clear();
                Time.unscaledTime = 10f;
                var skin = new SkinnedMeshRenderer();
                NativeAttachFaceIdlePerf.RecordNewBake(skin);
                var face = Face(skin);

                Time.unscaledTime = 10.05f; // 焼き直しの次フレーム、間隔内
                Assert.False(Prefix(face, out bool skipped)); // 本体の問い合わせを省く
                Assert.False(skipped); // 本体と同じく「新しくない」を返す

                Time.unscaledTime = 10f; // 焼き直しがあったフレームと同一時刻
                Assert.True(Prefix(face, out _)); // 本体が新しい実体を返す経路に任せる

                Time.unscaledTime = 10.2f; // 間隔（0.1秒）を超過
                Assert.True(Prefix(face, out _)); // 本体が焼き直す経路に任せる
            }
            finally { Time.unscaledTime = savedTime; }
        }

        [Fact]
        public void PrefixRunsTheOriginalWithoutARecordOrWhileTheSettingIsOff()
        {
            var savedOption = NativePerfOptions.AttachFaceIdle;
            var savedTime = Time.unscaledTime;
            try
            {
                Dew.BakeMeshInterval = 0.1f;
                LastNewBake().Clear();
                Time.unscaledTime = 10f;
                var skin = new SkinnedMeshRenderer();
                var face = Face(skin);
                Assert.True(Prefix(face, out _)); // 記録がなければ本体に戻る

                NativeAttachFaceIdlePerf.RecordNewBake(skin);
                Time.unscaledTime = 10.05f; // 間隔内でも設定 OFF なら常に本体に戻る
                NativePerfOptions.AttachFaceIdle = false;
                Assert.True(Prefix(face, out _));
                NativePerfOptions.AttachFaceIdle = true;
                Assert.False(Prefix(face, out _)); // ON に戻すと省略する
            }
            finally
            {
                NativePerfOptions.AttachFaceIdle = savedOption;
                Time.unscaledTime = savedTime;
            }
        }

        [Fact]
        public void PostfixRecordsTheBakeOnlyWhenTheMeshIsNew()
        {
            var savedOption = NativePerfOptions.AttachFaceIdle;
            var savedShared = NativePerfOptions.AttachFaceSharedVerts;
            var savedTime = Time.unscaledTime;
            try
            {
                LastNewBake().Clear();
                Time.unscaledTime = 20f;
                var skin = new SkinnedMeshRenderer();
                Postfix(skin, false);
                Assert.Empty(LastNewBake()); // 新しくない焼き直しは記録しない

                Postfix(skin, true);
                Assert.Single(LastNewBake());
                Assert.Equal(20f, LastNewBake()[skin.GetInstanceID()]); // 記録時刻は unscaledTime

                NativePerfOptions.AttachFaceIdle = false;
                Postfix(new SkinnedMeshRenderer(), true);
                Assert.Equal(2, LastNewBake().Count); // 共有頂点キャッシュが ON なら記録が続く

                NativePerfOptions.AttachFaceSharedVerts = false;
                Postfix(new SkinnedMeshRenderer(), true);
                Assert.Equal(2, LastNewBake().Count); // 両方 OFF の間は記録しない
            }
            finally
            {
                NativePerfOptions.AttachFaceIdle = savedOption;
                NativePerfOptions.AttachFaceSharedVerts = savedShared;
                Time.unscaledTime = savedTime;
            }
        }

        [Fact]
        public void PruneStaleRemovesOnlyEntriesPastTheInterval()
        {
            var savedTime = Time.unscaledTime;
            try
            {
                Dew.BakeMeshInterval = 0.1f;
                LastNewBake().Clear();
                ResetPruneClock();
                Time.unscaledTime = 50f;
                var old = new SkinnedMeshRenderer();
                NativeAttachFaceIdlePerf.RecordNewBake(old);
                Time.unscaledTime = 50.2f;
                var recent = new SkinnedMeshRenderer();
                NativeAttachFaceIdlePerf.RecordNewBake(recent);

                NativeAttachFaceIdlePerf.PruneStale(50.2f);
                Assert.DoesNotContain(old.GetInstanceID(), LastNewBake().Keys); // 間隔を過ぎた項目だけ消える
                Assert.Contains(recent.GetInstanceID(), LastNewBake().Keys); // 間隔内の項目は残る
            }
            finally { Time.unscaledTime = savedTime; }
        }

        [Fact]
        public void RecordNewBakePastTheThresholdPrunesStaleEntriesInsteadOfClearing()
        {
            var savedTime = Time.unscaledTime;
            try
            {
                Dew.BakeMeshInterval = 0.1f;
                LastNewBake().Clear();
                ResetPruneClock();
                Time.unscaledTime = 100f;
                var oldest = new SkinnedMeshRenderer();
                NativeAttachFaceIdlePerf.RecordNewBake(oldest);
                Time.unscaledTime = 100.15f;
                for (int i = 0; i < 64; i++)
                    NativeAttachFaceIdlePerf.RecordNewBake(new SkinnedMeshRenderer());
                Assert.Equal(65, LastNewBake().Count); // しきい値（64）を超えても表を消去しない

                var latest = new SkinnedMeshRenderer();
                Time.unscaledTime = 100.2f;
                NativeAttachFaceIdlePerf.RecordNewBake(latest); // この追加で古い項目だけ掃除される
                var table = LastNewBake();
                Assert.Equal(65, table.Count); // 全消去ではなく間隔を過ぎた項目だけが消える
                Assert.DoesNotContain(oldest.GetInstanceID(), table.Keys);
                Assert.Contains(latest.GetInstanceID(), table.Keys);

                var face = Face(latest); // 掃除の後も間隔内の記録は省略を続ける
                Time.unscaledTime = 100.25f; // latest の焼き直しから間隔内の次フレーム
                Assert.False(Prefix(face, out _));
            }
            finally { Time.unscaledTime = savedTime; }
        }

        [Fact]
        public void PruneStaleScansAtMostOncePerBakeInterval()
        {
            var savedTime = Time.unscaledTime;
            try
            {
                Dew.BakeMeshInterval = 0.25f; // 浮動小数点でちょうど表れる間隔
                LastNewBake().Clear();
                ResetPruneClock();
                Time.unscaledTime = 10f;
                var a = new SkinnedMeshRenderer();
                NativeAttachFaceIdlePerf.RecordNewBake(a);
                NativeAttachFaceIdlePerf.PruneStale(10.1f); // 最初の掃除: まだ間隔内なので何も消えない
                Time.unscaledTime = 10.2f;
                var b = new SkinnedMeshRenderer();
                NativeAttachFaceIdlePerf.RecordNewBake(b);

                Time.unscaledTime = 10.3f; // a は間隔超過（0.3 > 0.25）だが前回の掃除からは間隔内
                NativeAttachFaceIdlePerf.PruneStale(10.3f);
                Assert.Contains(a.GetInstanceID(), LastNewBake().Keys); // 間隔内の2回目は走査しない

                NativeAttachFaceIdlePerf.PruneStale(10.36f); // 前回の掃除から 0.26f > 0.25f
                Assert.DoesNotContain(a.GetInstanceID(), LastNewBake().Keys); // 間隔が過ぎたら改めて掃除する
                Assert.Contains(b.GetInstanceID(), LastNewBake().Keys); // 間隔内の項目は残す
            }
            finally
            {
                Dew.BakeMeshInterval = 0.1f;
                Time.unscaledTime = savedTime;
            }
        }
    }
}
