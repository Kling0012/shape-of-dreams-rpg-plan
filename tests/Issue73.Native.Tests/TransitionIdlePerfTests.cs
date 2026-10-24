using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1136）場面転換がないフレームの TransitionManager.FrameUpdate の定常状態
    /// （state == Normal かつ screenFadeCg.alpha == 0f）の本体を丸ごと省く
    /// NativeTransitionIdlePerf の検証。ValidateBody は本体 FrameUpdate（r1.4.1.2）が呼ぶ
    /// 15個の呼び出し multiset を IL の命令列から数えるだけなので、境界の模倣
    /// （TransitionManager・CanvasGroup）と同じ名前の呼び出し先から作った CodeInstruction 列を
    /// 直接渡して確かめる。ちょうど15個のときだけ true を返し、呼び出しの種類が1つ欠ける形・
    /// 追跡していない呼び出しが1つ増える形・同じ種類の回数が1つ増える/1つ減る形は false を
    /// 返すこと。Prefix は毎フレームの生値だけから省くかどうかを決める: 定常状態だけ
    /// false（本体を省く）を返し、フェード中（Loading、または alpha が 0 より大きい）・
    /// フェード用 CanvasGroup がない・設定が無効のときは true（本体を通す）こと、読み出しで
    /// 例外が出たフレームは true を返して以後は無効になる（例外の元が消えても定常状態で
    /// 本体を通し続ける）ことを確かめる。
    /// </summary>
    public sealed class TransitionIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly bool _savedOption;

        // パッチ側の静的な欄（設定と _disabled・_warned）を毎テストで初期化する。設定は既定の
        // 有効とし、Dispose で戻す。この欄を書くテストはこのアセンブリ内ではこのクラスだけ
        // （アセンブリ全体でテストの並列化を止めてあるため、並列クラスが途中で値を変える
        // 筋道もない）。今後この欄を書くテストを足すときは、このクラスと同じ [Collection] に
        // 入れて直列に走らせること。
        public TransitionIdlePerfTests()
        {
            _savedOption = NativePerfOptions.TransitionIdle;
            NativePerfOptions.TransitionIdle = true;
            SetDisabled(false);
            SetWarned(false);
        }

        public void Dispose()
        {
            NativePerfOptions.TransitionIdle = _savedOption;
            SetDisabled(false);
            SetWarned(false);
        }

        private static void SetDisabled(bool value)
            => typeof(NativeTransitionIdlePerf).GetField("_disabled", Hidden).SetValue(null, value);

        private static void SetWarned(bool value)
            => typeof(NativeTransitionIdlePerf).GetField("_warned", Hidden).SetValue(null, value);

        private static bool IsDisabled()
            => (bool)typeof(NativeTransitionIdlePerf).GetField("_disabled", Hidden).GetValue(null);

        // ---- ValidateBody（本体 FrameUpdate の形検査） ----

        // ValidateBody は呼び出し先を DeclaringType の名前と メソッド名 だけで数えるため、
        // 見本の呼び出し先には境界の模倣（NativeBoundary.cs の TransitionManager・CanvasGroup）
        // と、本体側に該当する境界のない種類（LogicBehaviour・Component・GameObject・Mathf・
        // Time・Input・Debug）の名前だけを揃えた受け口を使う。中身は空でよい（数えるのは名前）。
        private static class Il
        {
            internal class LogicBehaviour
            {
                internal void FrameUpdate() { }
            }
            internal class Component
            {
                internal GameObject gameObject { get; set; }
            }
            internal class GameObject
            {
                internal void SetActive(bool value) { }
            }
            internal static class Mathf
            {
                internal static float MoveTowards(float current, float target, float maxDelta) => target;
            }
            internal static class Time
            {
                internal static float unscaledDeltaTime => 0f;
            }
            internal static class Input
            {
                internal static bool GetKeyDown(string key) => false;
            }
            // 追跡していない呼び出しの見本（拒否の見本が1つ足す）。本体が読み込み中のログを
            // 足すような変更を代表にする。
            internal static class Debug
            {
                internal static void Log(object message) { }
            }
        }

        private static MethodInfo StateGetter()
            => AccessTools.Property(typeof(TransitionManager), "state").GetGetMethod(true);

        private static MethodInfo AlphaGetter()
            => AccessTools.Property(typeof(CanvasGroup), "alpha").GetGetMethod(true);

        // 本体 FrameUpdate（r1.4.1.2、150バイト）が呼ぶ15個の呼び出しと同じ multiset を綴る。
        // 呼び出し以外の命令（引数の読み込み・文字列・返り）は ValidateBody が数えないことを
        // 確かめるためにも混ぜておく。
        private static List<CodeInstruction> NativeShape()
        {
            var code = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ldarg_0) };
            void Add(MethodInfo method, int count)
            {
                for (int i = 0; i < count; i++) code.Add(new CodeInstruction(OpCodes.Call, method));
            }
            Add(AccessTools.Method(typeof(Il.LogicBehaviour), nameof(Il.LogicBehaviour.FrameUpdate)), 1);
            Add(StateGetter(), 3);
            Add(AlphaGetter(), 2);
            Add(AccessTools.Property(typeof(CanvasGroup), "alpha").GetSetMethod(true), 1);
            Add(AccessTools.Property(typeof(CanvasGroup), "blocksRaycasts").GetSetMethod(true), 1);
            Add(AccessTools.Property(typeof(Il.Component), "gameObject").GetGetMethod(true), 1);
            Add(AccessTools.Method(typeof(Il.GameObject), nameof(Il.GameObject.SetActive)), 1);
            Add(AccessTools.Method(typeof(Il.Mathf), nameof(Il.Mathf.MoveTowards)), 1);
            Add(AccessTools.Property(typeof(Il.Time), "unscaledDeltaTime").GetGetMethod(true), 1);
            Add(AccessTools.Method(typeof(Il.Input), nameof(Il.Input.GetKeyDown)), 2);
            Add(AccessTools.Method(typeof(TransitionManager), nameof(TransitionManager.TryGetNewTip)), 1);
            code.Add(new CodeInstruction(OpCodes.Ldstr, "tip"));
            code.Add(new CodeInstruction(OpCodes.Ret));
            return code;
        }

        // 種類ごと全部取り除いた形（呼び出しの種類が1つ欠ける）。
        private static List<CodeInstruction> ShapeWithout(MethodInfo method)
        {
            var code = NativeShape();
            code.RemoveAll(instruction => instruction.operand == method);
            return code;
        }

        // 1回だけ取り除いた形（同じ種類の回数が1つ減る）。
        private static List<CodeInstruction> ShapeWithoutOne(MethodInfo method)
        {
            var code = NativeShape();
            code.RemoveAt(code.FindIndex(instruction => instruction.operand == method));
            return code;
        }

        // 1回だけ多くした形（同じ種類の回数が1つ増える）。
        private static List<CodeInstruction> ShapeWithOneMore(MethodInfo method)
        {
            var code = NativeShape();
            code.Insert(code.FindLastIndex(instruction => instruction.operand == method) + 1,
                new CodeInstruction(OpCodes.Call, method));
            return code;
        }

        // 追跡していない種類の呼び出しを1つ足した形（呼び出しの種類が1つ増える）。
        private static List<CodeInstruction> ShapeWithExtraKind(MethodInfo method)
        {
            var code = NativeShape();
            code.Insert(code.Count - 1, new CodeInstruction(OpCodes.Call, method));
            return code;
        }

        [Fact]
        public void ValidateBodyAcceptsTheNativeFrameUpdateShape()
        {
            // 期待どおりの15呼び出し（11種類）なら本体の形と認めて true。
            Assert.True(NativeTransitionIdlePerf.ValidateBody(NativeShape()));
        }

        [Fact]
        public void ValidateBodyRejectsAShapeWithAMissingKindOfCall()
        {
            // TryGetNewTip がまるごと欠けた形（呼び出し14個・種類10）。
            Assert.False(NativeTransitionIdlePerf.ValidateBody(
                ShapeWithout(AccessTools.Method(typeof(TransitionManager), nameof(TransitionManager.TryGetNewTip)))));
        }

        [Fact]
        public void ValidateBodyRejectsAShapeWithAnExtraKindOfCall()
        {
            // 追跡していないログ呼び出しが1つ増えた形（呼び出し16個・種類12）。
            Assert.False(NativeTransitionIdlePerf.ValidateBody(
                ShapeWithExtraKind(AccessTools.Method(typeof(Il.Debug), nameof(Il.Debug.Log)))));
        }

        [Fact]
        public void ValidateBodyRejectsAShapeWithOneFewerStateRead()
        {
            // get_state が3回より1回少ない形（回数が違う）。
            Assert.False(NativeTransitionIdlePerf.ValidateBody(ShapeWithoutOne(StateGetter())));
        }

        [Fact]
        public void ValidateBodyRejectsAShapeWithOneMoreAlphaRead()
        {
            // get_alpha が2回より1回多い形（回数が違う、逆向き）。
            Assert.False(NativeTransitionIdlePerf.ValidateBody(ShapeWithOneMore(AlphaGetter())));
        }

        // ---- Prefix（毎フレームの省略判定） ----

        private static bool RunPrefix(TransitionManager instance)
            => (bool)typeof(NativeTransitionIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { instance });

        private static TransitionManager NewManager(TransitionManager.StateType state, CanvasGroup fade)
            => new TransitionManager { state = state, screenFadeCg = fade };

        [Fact]
        public void ThePrefixSkipsTheBodyOnlyInTheIdleSteadyState()
        {
            // 定常状態（Normal かつ alpha == 0）だけが false（本体を省く）。判定は毎フレーム
            // 生値からやり直すので、同じ値が続くフレームも省いたまま。
            var manager = NewManager(TransitionManager.StateType.Normal, new CanvasGroup { alpha = 0f });
            Assert.False(RunPrefix(manager));
            Assert.False(RunPrefix(manager));

            manager.screenFadeCg.alpha = 0.5f;                 // 減衰中（Normal かつ alpha > 0）
            Assert.True(RunPrefix(manager));                   // フェードが見える間は本体を通す

            manager.state = TransitionManager.StateType.Loading;
            manager.screenFadeCg.alpha = 0f;                   // alpha が 0 に戻っていても
            Assert.True(RunPrefix(manager));                   // Loading は本体を通す
        }

        [Fact]
        public void ANullFadeCanvasGroupKeepsTheNativeBody()
        {
            var manager = NewManager(TransitionManager.StateType.Normal, null);
            Assert.True(RunPrefix(manager));                   // フェード先がないときは本体を通す
        }

        [Fact]
        public void TheOptionOffKeepsTheNativeBody()
        {
            var manager = NewManager(TransitionManager.StateType.Normal, new CanvasGroup { alpha = 0f });
            NativePerfOptions.TransitionIdle = false;
            Assert.True(RunPrefix(manager));                   // 設定が無効なら定常状態でも本体を通す
            NativePerfOptions.TransitionIdle = true;
            Assert.False(RunPrefix(manager));                  // 有効に戻れば再び省く
        }

        [Fact]
        public void AnExceptionDisablesThePatchForTheRestOfTheSession()
        {
            var manager = NewManager(TransitionManager.StateType.Normal, new CanvasGroup { alpha = 0f });
            Assert.False(RunPrefix(manager));                  // 例外の前は定常状態を省く（比較対象）

            manager.screenFadeCg.throwOnRead = new InvalidOperationException("alpha unreadable");
            Assert.True(RunPrefix(manager));                   // 例外を呑んでそのフレームは本体を通す
            Assert.True(IsDisabled());                         // 以後は無効になる

            manager.screenFadeCg.throwOnRead = null;           // 例外の元が消えても
            Assert.True(RunPrefix(manager));                   // 定常状態でも本体を通し続ける
        }
    }
}
