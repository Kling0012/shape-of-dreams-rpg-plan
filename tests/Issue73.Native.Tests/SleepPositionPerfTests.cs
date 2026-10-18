using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#751）眠り判定の距離ループで対象エンティティの位置読み出しを1回にまとめる
    /// NativeSleepPositionPerf の検証。ValidateBody は実ソースから抽出した本体そのものに、
    /// 本体 ActorManager.CanEntitySleep と同じ命令並びの見本（SleepPositionIlShapes）を
    /// PatchProcessor.GetOriginalInstructions で読んで渡す。EntityXY は同じ参照なら値を
    /// 使い回し、別の参照や Prefix の後は読み直し、設定が OFF なら常に読み直すこと、
    /// Postfix が判定のあとに参照を放すことを確かめる。
    /// </summary>
    public sealed class SleepPositionPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(SleepPositionIlShapes).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        [Fact]
        public void ValidateBodyAcceptsTheNativeDistanceLoopShape()
        {
            Assert.True(NativeSleepPositionPerf.ValidateBody(
                InstructionsOf(nameof(SleepPositionIlShapes.SubjectBeforeSubtraction))));
        }

        [Fact]
        public void ValidateBodyRejectsASubjectLoadedBeforeTheHeroOperand()
        {
            Assert.False(NativeSleepPositionPerf.ValidateBody(
                InstructionsOf(nameof(SleepPositionIlShapes.SubjectLoadedFirst))));
        }

        [Fact]
        public void ValidateBodyRejectsASinglePositionRead()
        {
            Assert.False(NativeSleepPositionPerf.ValidateBody(
                InstructionsOf(nameof(SleepPositionIlShapes.SinglePositionRead))));
        }

        private static Vector2 EntityXY(Entity e)
            => (Vector2)typeof(NativeSleepPositionPerf).GetMethod("EntityXY", Hidden).Invoke(null, new object[] { e });

        private static void Prefix()
            => typeof(NativeSleepPositionPerf).GetMethod("Prefix", Hidden).Invoke(null, null);

        private static void Postfix()
            => typeof(NativeSleepPositionPerf).GetMethod("Postfix", Hidden).Invoke(null, null);

        private static Entity CachedEntity()
            => (Entity)typeof(NativeSleepPositionPerf).GetField("_cachedEntity", Hidden).GetValue(null);

        [Fact]
        public void EntityXYReusesTheValueForTheSameReferenceUntilTheNextCheck()
        {
            var e = new Entity { position = new Vector3(10f, 0f, 20f) };
            Prefix();
            var first = EntityXY(e);
            Assert.Equal(new Vector2(10f, 20f), first);
            e.position = new Vector3(30f, 0f, 40f);
            Assert.Equal(first, EntityXY(e)); // 移動後も最初の読み出し値を使い回す
            Assert.Same(e, CachedEntity());
        }

        [Fact]
        public void EntityXYRereadsAfterPrefixAndForADifferentEntity()
        {
            var e = new Entity { position = new Vector3(10f, 0f, 20f) };
            var other = new Entity { position = new Vector3(5f, 0f, 6f) };
            Prefix();
            EntityXY(e);
            e.position = new Vector3(30f, 0f, 40f);
            Prefix();
            Assert.Equal(new Vector2(30f, 40f), EntityXY(e)); // Prefix の後は読み直す
            Assert.Equal(new Vector2(5f, 6f), EntityXY(other)); // 別の参照は読み直す
            Assert.Same(other, CachedEntity());
        }

        [Fact]
        public void EntityXYAlwaysRereadsWhileTheSettingIsOff()
        {
            var saved = NativePerfOptions.SleepPosition;
            NativePerfOptions.SleepPosition = false;
            try
            {
                var e = new Entity { position = new Vector3(10f, 0f, 20f) };
                Prefix();
                Assert.Equal(new Vector2(10f, 20f), EntityXY(e));
                e.position = new Vector3(30f, 0f, 40f);
                Assert.Equal(new Vector2(30f, 40f), EntityXY(e)); // 同じ参照でも毎回読み直す
                Assert.Null(CachedEntity()); // OFF の間はキャッシュに触れない
            }
            finally { NativePerfOptions.SleepPosition = saved; }
        }

        [Fact]
        public void PostfixReleasesTheCachedEntityReference()
        {
            var e = new Entity { position = new Vector3(10f, 0f, 20f) };
            Prefix();
            EntityXY(e);
            Assert.Same(e, CachedEntity());
            Postfix();
            Assert.Null(CachedEntity()); // 判定が終わった時点で参照を放す
            e.position = new Vector3(30f, 0f, 40f);
            Assert.Equal(new Vector2(30f, 40f), EntityXY(e)); // 放した後は次の判定で読み直す
        }
    }
}
