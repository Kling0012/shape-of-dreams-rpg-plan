using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#866）ダッシュ攻撃の掃引で捨てられる最後の 1 歩の範囲移動の書き込みを省く
    /// NativeDashSweepStepPerf の検証。境界（DashSweepStepPerfBoundary.cs）の模倣
    /// DashAttackInstance.ActiveLogicUpdate は本体 Dew.Core の逆コンパイルの転写で、
    /// 実際に Harmony で当てて: (a) DoCollisionChecks の呼び出し回数と各回の range 位置・
    /// ループ後の range.localPosition・_lastPosition がパッチ前後で一致し、書き込みは
    /// 捨てられる最後の 1 歩の 1 回ぶんだけ減る、(b) 設定を切ると書き込みまで完全に一致する、
    /// (c) dist が NaN の掃引では本体どおり歩き続け書き続ける（番兵で止めて観測）、
    /// (d) 窓の形が違う本体では Transpiler は何も足さず警告は 1 回だけ、
    /// (e) 挿入分岐の判定（SkipTailStep）は num が dist を超えたときのみ真で、
    /// NaN と設定オフでは偽。
    /// </summary>
    public sealed class DashSweepStepPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // パッチ側の静的な欄を読み書きするため、毎テストで警告の抑制を解除し、設定を既定
        // （有効）へ戻す。ログの警告欄も毎テスト空に戻す（BossSharesTests と同じ扱い）。
        public DashSweepStepPerfTests()
        {
            ResetPatchState();
            NativePerfOptions.DashSweepStep = true;
            Log.Warnings.Clear();
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.DashSweepStep = true;
            Log.Warnings.Clear();
        }

        private static void ResetPatchState()
            => typeof(NativeDashSweepStepPerf).GetField("_warned", Hidden).SetValue(null, false);

        private static bool PrepareRan()
            => (bool)typeof(NativeDashSweepStepPerf).GetMethod("Prepare", Hidden).Invoke(null, null);

        private static bool SkipTailStep(float num, float dist)
            => (bool)typeof(NativeDashSweepStepPerf).GetMethod("SkipTailStep", Hidden)
                .Invoke(null, new object[] { num, dist });

        private sealed class SweepObservation
        {
            internal readonly List<Vector3> CheckPositions = new List<Vector3>();
            internal readonly List<Vector3> PositionWrites = new List<Vector3>();
            internal Vector3 RestoredLocalPosition;
            internal Vector3 LastPosition;
            internal string Thrown;
        }

        // 1 回の掃引の観測。last と local は掃引の始点（_lastPosition と range のローカル位置）、
        // current は今の range のワールド位置（vector2）、self は実体自身の position。
        // checkLimit を正にすると、その回数ぶんの判定を記録した次の判定で例外を出す
        // （dist が NaN の掃引は本体でも終わらないため、その観測のためだけの番兵）。
        private static SweepObservation RunSweep(float last, float local, float current, float self, int checkLimit = 0)
        {
            var instance = new DashAttackInstance
            {
                damageGracePeriod = 0f,
                _isDashing = true,
                _lastPosition = new Vector3(last, 0f, 0f),
                position = new Vector3(self, 0f, 0f),
            };
            instance.isServer = true;
            instance.range.shape = DewCollider.ColliderShape.Circle;
            instance.range.radius = 2.5f;
            instance.range.transform.localPosition = new Vector3(local, 0f, 0f);
            instance.range.transform.position = new Vector3(current, 0f, 0f);
            instance.range.transform.positionWriteLog.Clear(); // 用意の書き込みは観測から除く
            instance.collisionCheckLimit = checkLimit;
            var observation = new SweepObservation();
            try
            {
                instance.ActiveLogicUpdate(0f);
            }
            catch (InvalidOperationException ex)
            {
                observation.Thrown = ex.Message;
            }
            observation.CheckPositions.AddRange(instance.checkPositions);
            observation.PositionWrites.AddRange(instance.range.transform.positionWriteLog);
            observation.RestoredLocalPosition = instance.range.transform.localPosition;
            observation.LastPosition = instance._lastPosition;
            return observation;
        }

        // NaN を含む比較（同じ NaN 同士は等しいとみなす）。
        private static bool Same(float a, float b) => a == b || (float.IsNaN(a) && float.IsNaN(b));

        private static bool SameValues(Vector3 a, Vector3 b)
            => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z);

        private static void AssertSameValues(IReadOnlyList<Vector3> expected, IReadOnlyList<Vector3> actual, string what)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
                Assert.True(SameValues(expected[i], actual[i]),
                    what + "[" + i + "] 期待 " + expected[i].x + "," + expected[i].y + "," + expected[i].z
                    + " 実際 " + actual[i].x + "," + actual[i].y + "," + actual[i].z);
        }

        // 転写そのものが本体どおりであることの担保（パッチ前後が両方同じように誤るのを防ぐ）。
        // 始点 2・終点 7・歩幅 2.5 で、判定は 2 → 4.5 → 7 の 3 回、書き込みは初回の位置合わせの
        // 2 と各歩の 4.5・7、それから捨てられる 9.5 の計 4 回、ローカル位置は 1 に戻り、
        // _lastPosition は実体の position（3）へ進む。
        private static void AssertVanillaShape(SweepObservation run)
        {
            Assert.Null(run.Thrown);
            AssertSameValues(new[]
            {
                new Vector3(2f, 0f, 0f), new Vector3(4.5f, 0f, 0f), new Vector3(7f, 0f, 0f),
            }, run.CheckPositions, "判定位置");
            AssertSameValues(new[]
            {
                new Vector3(2f, 0f, 0f), new Vector3(4.5f, 0f, 0f), new Vector3(7f, 0f, 0f), new Vector3(9.5f, 0f, 0f),
            }, run.PositionWrites, "書き込み");
            Assert.True(SameValues(new Vector3(1f, 0f, 0f), run.RestoredLocalPosition), "戻したローカル位置");
            Assert.True(SameValues(new Vector3(3f, 0f, 0f), run.LastPosition), "_lastPosition");
        }

        // (a) パッチを当てた掃引は、判定・復元・実体の位置までも本体と同じで、
        // 書き込みだけが捨てられる最後の 1 歩の 1 回ぶん減る。
        [Fact]
        public void ThePatchedSweepJudgesTheSameStepsAndSkipsOnlyTheDiscardedWrite()
        {
            var vanilla = RunSweep(1f, 1f, 7f, 3f);
            AssertVanillaShape(vanilla);
            Assert.True(PrepareRan());
            var harmony = new Harmony("issue73.dashsweep." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.CreateClassProcessor(typeof(NativeDashSweepStepPerf)).Patch();
                Assert.Empty(Log.Warnings); // 形の照合を通って当たっている
                var patched = RunSweep(1f, 1f, 7f, 3f);
                AssertSameValues(vanilla.CheckPositions, patched.CheckPositions, "判定位置");
                Assert.True(SameValues(vanilla.RestoredLocalPosition, patched.RestoredLocalPosition), "戻したローカル位置");
                Assert.True(SameValues(vanilla.LastPosition, patched.LastPosition), "_lastPosition");
                Assert.Null(patched.Thrown);
                // 書き込みは 1 回ぶんだけ減り、残る列は本体と同じ先頭から並ぶ。
                Assert.Equal(vanilla.PositionWrites.Count - 1, patched.PositionWrites.Count);
                AssertSameValues(vanilla.PositionWrites.Take(patched.PositionWrites.Count).ToList(), patched.PositionWrites, "書き込み");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (b) 設定を切ったときは挿入分岐が常に偽になり、判定も書き込みも完全に本体どおり。
        [Fact]
        public void ADisabledOptionKeepsEveryWriteLikeTheVanillaBody()
        {
            var vanilla = RunSweep(1f, 1f, 7f, 3f);
            AssertVanillaShape(vanilla);
            NativePerfOptions.DashSweepStep = false;
            Assert.True(PrepareRan());
            var harmony = new Harmony("issue73.dashsweep." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.CreateClassProcessor(typeof(NativeDashSweepStepPerf)).Patch();
                Assert.Empty(Log.Warnings);
                var off = RunSweep(1f, 1f, 7f, 3f);
                AssertSameValues(vanilla.CheckPositions, off.CheckPositions, "判定位置");
                AssertSameValues(vanilla.PositionWrites, off.PositionWrites, "書き込み");
                Assert.True(SameValues(vanilla.RestoredLocalPosition, off.RestoredLocalPosition), "戻したローカル位置");
                Assert.True(SameValues(vanilla.LastPosition, off.LastPosition), "_lastPosition");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (c) dist が NaN の掃引。本体の ble.un は NaN でも歩き続けるため、番兵（6 回記録した
        // 次の判定で例外）で止めて観測する。パッチを当てても同じ歩数・同じ位置で同じ書き込み
        // をしてから同じ番兵で止まる（C# の > が NaN で偽になるため、書き込みは省かれない）。
        [Fact]
        public void ANanDistanceKeepsWalkingAndWritingLikeTheVanillaBody()
        {
            var vanilla = RunSweep(1f, 1f, float.NaN, 3f, checkLimit: 6);
            Assert.NotNull(vanilla.Thrown); // 本体は NaN では終わらない
            Assert.True(PrepareRan());
            var harmony = new Harmony("issue73.dashsweep." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.CreateClassProcessor(typeof(NativeDashSweepStepPerf)).Patch();
                Assert.Empty(Log.Warnings);
                var patched = RunSweep(1f, 1f, float.NaN, 3f, checkLimit: 6);
                Assert.Equal(vanilla.Thrown, patched.Thrown); // 同じ番兵で止まる＝途中で抜けていない
                AssertSameValues(vanilla.CheckPositions, patched.CheckPositions, "判定位置");
                AssertSameValues(vanilla.PositionWrites, patched.PositionWrites, "書き込み"); // 書き込みも同じ
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (d) 窓の形が違う本体では、Transpiler は命令を 1 つも足さず（＝パッチが外れ）、
        // 警告は最初の 1 回だけ出る。2 回目の照合でも警告は増えない。
        [Fact]
        public void AWrongShapedBodyIsLeftAloneWithASingleWarning()
        {
            Assert.True(PrepareRan()); // Transpiler が基本クラス（DoCollisionChecks・range）を探す準備
            var bad = typeof(DashAttackInstance).GetMethod("SweepWithAnExtraPositionRead", Hidden);
            Assert.NotNull(bad);
            var transpiler = typeof(NativeDashSweepStepPerf).GetMethod("Transpiler", Hidden);
            var generator = new DynamicMethod("dashSweepWrongShape", typeof(void), Type.EmptyTypes).GetILGenerator();
            var before = PatchProcessor.GetOriginalInstructions(bad);
            string original = string.Join(",", before.Select(i => i.opcode.Name));
            var first = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { generator, before });
            Assert.Equal(original, string.Join(",", first.Select(i => i.opcode.Name))); // 足しても減らしてもいない
            Assert.Single(Log.Warnings); // 警告は 1 回
            Assert.Contains("dash sweep step", Log.Warnings[0]);
            Assert.True((bool)typeof(NativeDashSweepStepPerf).GetField("_warned", Hidden).GetValue(null));
            var second = (IEnumerable<CodeInstruction>)transpiler.Invoke(
                null, new object[] { generator, PatchProcessor.GetOriginalInstructions(bad) });
            Assert.Equal(original, string.Join(",", second.Select(i => i.opcode.Name)));
            Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない
        }

        // (e) 挿入分岐の判定そのもの。num が dist を厳密に超えたときのみ真（ble.un の補集合）で、
        // ちょうど・未満・NaN では偽、設定オフでは常に偽。
        [Fact]
        public void TheInsertedBranchSkipsOnlyWhenTheNumberPassedTheDistance()
        {
            NativePerfOptions.DashSweepStep = true;
            Assert.False(SkipTailStep(2.5f, 5f)); // まだ届いていない
            Assert.False(SkipTailStep(5f, 5f)); // ちょうど（ble.un は歩を続ける側）
            Assert.True(SkipTailStep(7.5f, 5f)); // 超えた → 復元へ飛ぶ
            Assert.False(SkipTailStep(float.NaN, 5f)); // NaN では飛ばない（書き込む）
            Assert.False(SkipTailStep(7.5f, float.NaN));
            NativePerfOptions.DashSweepStep = false;
            Assert.False(SkipTailStep(7.5f, 5f)); // 設定オフでは常に書き込む
        }
    }
}
