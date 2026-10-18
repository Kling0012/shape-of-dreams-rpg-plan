using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#801）カーソル無効中の同じ既定カーソル再設定を省く NativeCursorIdlePerf の検証。
    /// ValidateBody は実ソースから抽出した本体そのものに、本体 CursorManager.ApplyCursor の
    /// 無効分岐と同じ読み出し・呼び出し一式の見本（CursorIlShapes）を PatchProcessor.
    /// GetOriginalInstructions で読んで渡し、ガードが引数を読む形・無効分岐がほかの
    /// メンバーを書く形・ガードが第2読み出しを欠く形・読み出しの順を入れ替えた形を拒否する
    /// ことを確かめる。Prefix/Postfix は、無効状態の繰り返しで本体が1回だけ動くこと、
    /// 有効状態を1回はさむと次の無効呼び出しで本体が再び動くこと、本体が完走しなかった
    /// フレーム（__runOriginal=false・Postfix が走らない例外）では印が立たないこと、
    /// 設定を無効にしている間に本体がカーソル画像を設定したあと再有効化しても、最初の
    /// 無効呼び出しが印に見過ごされず本体へ戻ることを確かめる（起動不要の遷移再現）。
    /// </summary>
    public sealed class CursorIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(CursorIlShapes).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        [Fact]
        public void ValidateBodyAcceptsTheNativeApplyCursorGuardShape()
        {
            Assert.True(NativeCursorIdlePerf.ValidateBody(
                InstructionsOf(nameof(CursorIlShapes.SameShape))));
        }

        [Fact]
        public void ValidateBodyRejectsAGuardThatAlsoReadsTheIndexArgument()
        {
            Assert.False(NativeCursorIdlePerf.ValidateBody(
                InstructionsOf(nameof(CursorIlShapes.GuardAlsoReadsTheIndexArgument))));
        }

        [Fact]
        public void ValidateBodyRejectsADisabledBranchThatWritesAnotherMember()
        {
            Assert.False(NativeCursorIdlePerf.ValidateBody(
                InstructionsOf(nameof(CursorIlShapes.DisabledBranchAlsoWritesAnotherMember))));
        }

        [Fact]
        public void ValidateBodyRejectsAGuardThatMissesThePlatformSetting()
        {
            Assert.False(NativeCursorIdlePerf.ValidateBody(
                InstructionsOf(nameof(CursorIlShapes.GuardMissesThePlatformSetting))));
        }

        [Fact]
        public void ValidateBodyRejectsASwappedGuardOperandOrder()
        {
            Assert.False(NativeCursorIdlePerf.ValidateBody(
                InstructionsOf(nameof(CursorIlShapes.SwappedGuardOperandOrder))));
        }

        // 抽出部分の静的欄を整える。プラットフォーム設定は毎テストで初期化し、
        // 前のテストのフラグが残らないようにする。
        private static void PrepareExtractedPatch()
        {
            var type = typeof(NativeCursorIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_defaultCursorApplied", Hidden).SetValue(null, false);
            DewSave.platformSettings = new DewPlatformSettings();
        }

        private static bool RunPrefix(CursorManager instance, out object state)
        {
            var args = new object[] { instance, false };
            var result = (bool)typeof(NativeCursorIdlePerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            state = args[1];
            return result;
        }

        private static void RunPostfix(object state, bool runOriginal)
            => typeof(NativeCursorIdlePerf).GetMethod("Postfix", Hidden).Invoke(null, new[] { state, runOriginal });

        [Fact]
        public void TheDefaultCursorResetRunsOnceWhileTheSoftwareCursorIsDisabled()
        {
            // 無効状態（インスタンスフラグ側）で ApplyCursor を続けて呼ぶと、本体が動くのは
            // 最初の1回だけ。以後の同じ要求の繰り返しは省き、省いたフレームの Postfix は
            // 何も確定しない。
            PrepareExtractedPatch();
            var instance = new CursorManager { disableSoftwareCursor = true };
            Assert.True(RunPrefix(instance, out var state));   // 最初の1回は本体を通す
            Assert.True((bool)state);                          // Postfix への確定候補
            RunPostfix(state, true);                           // 本体が完走したフレーム
            Assert.False(RunPrefix(instance, out state));      // 同じ無効状態の再呼び出しは省く
            Assert.False((bool)state);                         // 省いたフレームは候補を作らない
            RunPostfix(state, false);                          // 省いたフレームの Postfix は何もしない
            Assert.False(RunPrefix(instance, out _));          // 状態が変わらない限り省き続ける
        }

        [Fact]
        public void AnEnabledCallMakesTheNextDisabledCallRunTheOriginalAgain()
        {
            // 無効状態（プラットフォーム設定側）で1回動かして省略が始まったあと、有効状態の
            // 呼び出しを1回はさむと印が落ち、次の無効呼び出しで本体が再び動く。
            PrepareExtractedPatch();
            DewSave.platformSettings.gameplay.disableSoftwareCursor = true;
            var instance = new CursorManager { disableSoftwareCursor = false };
            Assert.True(RunPrefix(instance, out var state));   // 設定側の無効で最初の1回は本体
            RunPostfix(state, true);
            Assert.False(RunPrefix(instance, out _));          // 繰り返しは省く
            DewSave.platformSettings.gameplay.disableSoftwareCursor = false;
            Assert.True(RunPrefix(instance, out state));       // 有効経路は常に本体を通す
            Assert.False((bool)state);                         // 有効経路は候補を作らない
            DewSave.platformSettings.gameplay.disableSoftwareCursor = true;
            Assert.True(RunPrefix(instance, out state));       // 印は落としてあるので再び本体
            RunPostfix(state, true);
            Assert.False(RunPrefix(instance, out _));          // 以後の繰り返しは省く
        }

        [Fact]
        public void TheMarkerStaysUnsetWhenTheOriginalDidNotComplete()
        {
            // ほかのMODの Prefix に本体を差し替えられたフレーム（__runOriginal=false）と
            // 本体が例外で中断したフレーム（Harmony は Postfix を実行しない）では印を
            // 立てない。次のフレームは本体へ戻り、パッチなしの毎フレーム再試行と同じ動きになる。
            PrepareExtractedPatch();
            var instance = new CursorManager { disableSoftwareCursor = true };
            Assert.True(RunPrefix(instance, out var state));   // 本体へ渡すが
            RunPostfix(state, false);                          // ほかの Prefix が差し替えた
            Assert.True(RunPrefix(instance, out state));       // 印は立っていないので本体へ戻る
            Assert.True(RunPrefix(instance, out state));       // Postfix なし＝例外で中断した
            Assert.True(RunPrefix(instance, out _));           // まだ印はないので省かない
            Assert.True(RunPrefix(instance, out state));
            RunPostfix(state, true);                           // 完走したフレームだけ印を確定
            Assert.False(RunPrefix(instance, out _));          // 以後の繰り返しは省く
        }

        [Fact]
        public void FramesWhileTheSettingIsOffDropTheConfirmedReset()
        {
            // 最適化有効・ソフトウェアカーソル無効で印を確定したあと、最適化を無効にした
            // 間に本体が有効経路でカーソル画像を設定し、最適化を再有効化する遷移。最初の
            // 無効呼び出しが古い印に見過ごされず本体へ戻り、既定カーソルへの戻しが再度
            // 発行される（起動を必要としない再現）。
            PrepareExtractedPatch();
            var instance = new CursorManager { disableSoftwareCursor = true };
            Assert.True(RunPrefix(instance, out var state));
            RunPostfix(state, true);                           // 既定カーソルへの戻しを確定
            bool saved = NativePerfOptions.CursorIdle;
            try
            {
                NativePerfOptions.CursorIdle = false;
                instance.disableSoftwareCursor = false;        // 本体が有効経路で画像を設定する間
                Assert.True(RunPrefix(instance, out state));   // 無効の間は本体だけが走る
                NativePerfOptions.CursorIdle = true;           // 最適化を再有効化
                instance.disableSoftwareCursor = true;         // 無効状態での呼び出し
                Assert.True(RunPrefix(instance, out state));   // 印は捨ててあるので必ず本体
                RunPostfix(state, true);
                Assert.False(RunPrefix(instance, out _));      // 以後の繰り返しは省く
            }
            finally { NativePerfOptions.CursorIdle = saved; }
        }
    }
}
