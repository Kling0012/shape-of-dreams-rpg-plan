using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1118）振動が止まっているフレームのゲームパッド振動オフの毎フレーム送信を省く
    /// NativeGamepadRumbleIdlePerf のスキップ判定の検証。判定は引数だけから決まる純粋な
    /// 静的関数（ShouldSkip・DeriveSentZero）に切り出してあり、Prefix/Postfix は状態を
    /// 読んでこの関数へ渡すだけ。純粋関数はここから直接駆動し、Prefix/Postfix/Prepare の
    /// 配線は下の GamepadRumbleIdlePerfWiringTests から reflection で駆動する。ここでは
    /// (1) リストが空で前回の本体実行が確実に (0,0) を送ったときだけ本体を飛ばす、
    /// (2) ゲームパッドモードかつ timeScale 0.001 以上でシェイクが残るとき前回送信を
    /// (0,0) 扱いにしない、(3) 最後のシェイクが消えたフレームに本体を実行する、の3つと、
    /// シェイク発生 → 減衰 → 消滅 → 待機と進めたフレーム列で、本体を実行するか飛ばすかの
    /// 順序が本体どおりの送信列（非ゼロの送信は全て同じフレームに起こり、消滅フレームに
    /// 最後の (0,0) が1回だけ送られ、以後の待機フレームは何も送らない）になることを
    /// 確かめる。送信値の模擬（VanillaFoldAndSend）は本体 DoRumbleFrameUpdate の逆コンパイル
    /// （decompiled/sod-decomp/Dew.Core/InputManager.cs）からの転写で、判定関数とは独立に
    /// 書いている。両者の突き合わせにより、飛ばしたフレームに非ゼロの送信や消滅フレームの
    /// 最終送信が紛れ込まないことを検証する。
    /// </summary>
    public sealed class GamepadRumbleIdlePerfTests
    {
        // --- ShouldSkip: 本体を飛ばすか（true=飛ばす） ---

        [Fact]
        public void TheIdleStateIsSkipped()
        {
            // (1) リストが空で、前回の本体実行が確実に (0, 0) を送った（Postfix が保証した）
            // フレームだけが飛ばし対象。
            Assert.True(NativeGamepadRumbleIdlePerf.ShouldSkip(0, lastSentZero: true));
        }

        [Fact]
        public void AnyRemainingShakeRunsTheBody()
        {
            // シェイクが1つでも残っていれば (0, 0) と分からないので本体へ通す。
            Assert.False(NativeGamepadRumbleIdlePerf.ShouldSkip(1, lastSentZero: true));
            Assert.False(NativeGamepadRumbleIdlePerf.ShouldSkip(3, lastSentZero: true));
        }

        [Fact]
        public void AnUnvoucheredIdleStateRunsTheBody()
        {
            // リストが空でも、前回の送信が (0, 0) と保証されていなければ本体へ通す。
            // 初期フラグ（false）の初回フレームと、(3) 最後のシェイクが消えた直後の
            // フラグ更新前のフレームがこの形。
            Assert.False(NativeGamepadRumbleIdlePerf.ShouldSkip(0, lastSentZero: false));
        }

        // --- DeriveSentZero: このフレームに本体が (0,0) を送ったかの導出 ---

        [Fact]
        public void GamepadShakesOnAnUnpausedFrameAreNotDerivedZero()
        {
            // (2) ゲームパッドモードかつ timeScale 0.001 以上でシェイクが残るとき、
            // 送った値は (0, 0) とは限らないのでフラグを立てない（次のフレームは本体へ）。
            // しきい値の 0.001 ちょうどは一時停止扱いにしない（本体の比較と同じ）。
            Assert.False(NativeGamepadRumbleIdlePerf.DeriveSentZero(1, gamepadMode: true, timeScale: 1f));
            Assert.False(NativeGamepadRumbleIdlePerf.DeriveSentZero(2, gamepadMode: true, timeScale: 0.001f));
        }

        [Fact]
        public void AnEmptyListIsDerivedZeroEvenInGamepadMode()
        {
            // リストが空なら畳み込みは何も足さないので、モードと timeScale に関係なく (0, 0)。
            Assert.True(NativeGamepadRumbleIdlePerf.DeriveSentZero(0, gamepadMode: true, timeScale: 1f));
        }

        [Fact]
        public void ANonGamepadModeIsDerivedZeroEvenWithShakes()
        {
            // ゲームパッド以外の入力モード（キーボードとマウス）は両方の値を 0 に強制する。
            Assert.True(NativeGamepadRumbleIdlePerf.DeriveSentZero(2, gamepadMode: false, timeScale: 1f));
        }

        [Fact]
        public void APausedFrameIsDerivedZeroEvenWithShakes()
        {
            // timeScale が 0.001 未満（一時停止）では加算を飛ばすので (0, 0)。
            Assert.True(NativeGamepadRumbleIdlePerf.DeriveSentZero(1, gamepadMode: true, timeScale: 0.0009f));
        }

        // --- ライフサイクル: 判定関数をフレーム列で駆動し、送信列が本体どおりか ---

        // 本体 InputManager.DoRumbleFrameUpdate の転写。出典は実 DLL の逆コンパイル
        // （decompiled/sod-decomp/Dew.Core/InputManager.cs）。逆順に走査し、経過時間
        // （振動強度 1 未満では 0.5 + 強度 * 0.5 で割って早める）が attack + sustain +
        // decay を超えたシェイクを（一時停止中でも）リストから捨て、timeScale 0.001 未満
        // なら加算を飛ばし、それ以外は低周波モーターに振幅 * 0.1 * エンベロープ *
        // (2.5 * 強度) を足す。エンベロープは attack 中も sustain 中も全力値 1 で
        // （attack へのランプは無い）、decay で線形に 0 へ下がる。高周波モーターは
        // 振幅 * 0f なので常に 0。ループのあとゲームパッド以外の入力モードなら両値を
        // 0 に強制する。ここでは強度 1（時間伸縮なし・係数 2.5）で使い、送った値の組を
        // 返す（本体は Gamepad.current が null なら何も送らないが、スキップの可否は
        // リストとフラグだけから決まり送信の有無に依存しないため、接続ありで模擬する）。
        // 判定関数（ShouldSkip・DeriveSentZero）とは独立に転写しており、この突き合わせが
        // 検証の本体。リストは本体と同じく破壊的に更新する。
        private static (float low, float high) VanillaFoldAndSend(
            List<ShakeInstance> shakes, bool gamepadMode, float timeScale, float strength, float now)
        {
            float low = 0f, high = 0f;
            float scale = 2.5f * strength;
            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                ShakeInstance s = shakes[i];
                float t = now - s.startTime;
                if (strength < 1f) t /= 0.5f + strength * 0.5f;
                if (t > s.attackTime + s.sustainTime + s.decayTime)
                {
                    shakes.RemoveAt(i);
                    continue;
                }
                if (timeScale < 0.001f) continue;
                float env = (t < s.attackTime) ? 1f
                    : (t < s.attackTime + s.sustainTime)
                        ? 1f
                        : 1f - (t - s.attackTime - s.sustainTime) / s.decayTime;
                low += s.amplitude * 0.1f * env * scale;
                high += s.amplitude * 0f * env * scale;
            }
            if (!gamepadMode) { low = 0f; high = 0f; }
            return (low, high);
        }

        [Fact]
        public void TheShakeLifecycleKeepsTheVanillaSendSequence()
        {
            // 1フレームを 1/60 秒として 20 フレーム進める。f1 でシェイクが発生し（attack 0.05 +
            // sustain 0.1 + decay 0.08 = 0.23 秒の窓）、f15 の畳み込みで期限切れになる。毎フレーム
            // 「追加 → Prefix の判定（ShouldSkip。読むリストは前回の本体実行で畳み込み済み）→
            // 実行なら本体の畳み込みと送信 → Postfix の導出（DeriveSentZero）」の順で回す。
            const int frames = 20;
            const float dt = 1f / 60f;
            var shakes = new List<ShakeInstance>();
            bool lastSentZero = false;                 // パッチの初期フラグ（保守的に保証しない）
            var ran = new bool[frames];                // そのフレームに本体を実行したか
            var sent = new (float Low, float High)[frames]; // 実行したとき本体が送った値（飛ばしは未定義）

            for (int f = 0; f < frames; f++)
            {
                float now = f * dt;
                if (f == 1)
                    shakes.Add(new ShakeInstance { startTime = now, attackTime = 0.05f, sustainTime = 0.1f, decayTime = 0.08f, amplitude = 0.5f });

                ran[f] = !NativeGamepadRumbleIdlePerf.ShouldSkip(shakes.Count, lastSentZero);
                if (!ran[f]) continue;                 // スキップ: 何も送らない。フラグは前回のまま。

                sent[f] = VanillaFoldAndSend(shakes, gamepadMode: true, timeScale: 1f, strength: 1f, now);
                lastSentZero = NativeGamepadRumbleIdlePerf.DeriveSentZero(shakes.Count, gamepadMode: true, timeScale: 1f);
            }

            // f0: 初期フラグが保証しないため本体が走り (0, 0) を送る。f1〜f14: シェイクがあるので
            // 毎フレーム本体。f1 は attack の最初から全力値（本体の attack はランプでなく、低周波
            // モーターに振幅 * 0.1 * 2.5 = 0.125。高周波モーターは常に 0）、f2〜f14 も非ゼロ。
            // f15: 最後のシェイクが畳み込みで消えるフレーム。この frames 0〜15 が本体（毎フレーム
            // 送信）と同じ送信列で、(3) 消滅フレームにも本体が走り、最後の (0, 0) が1回だけ送られる。
            for (int f = 0; f <= 15; f++) Assert.True(ran[f], $"frame {f} must run the body");
            Assert.Equal((0f, 0f), sent[0]);           // 初回の保証されない待機も (0, 0) を送って整える
            Assert.True(sent[1].Low > 0f);             // 発生フレームは attack から全力（本体の転写どおり）
            Assert.Equal(0f, sent[1].High);            // 高周波モーターは常に 0（振幅 * 0f）
            for (int f = 2; f <= 14; f++)
            {
                Assert.True(sent[f].Low > 0f, $"frame {f} must send a non-zero low motor speed");
                Assert.Equal(0f, sent[f].High);
            }
            Assert.Equal((0f, 0f), sent[15]);          // 消滅フレームの最終 (0, 0)（(2) のフラグが立たなかった保証）

            // f16〜f19: リストが空で前回の送信が (0, 0) と保証された待機。(1) 本体を飛ばし、
            // 何も送らない。スキップは一度始まると待機が続く限り戻らない（順序の保証）。
            for (int f = 16; f < frames; f++) Assert.False(ran[f], $"frame {f} must skip the body");
            Assert.True(lastSentZero);                 // 待機に入ったあとも導出は (0, 0) を保証し続ける
            Assert.Empty(shakes);                      // 飛ばしたフレームで畳み込みは不要（リストは空のまま）
        }
    }

    /// <summary>
    /// （#1118）NativeGamepadRumbleIdlePerf の Prefix/Postfix/Prepare の配線の検証。
    /// 境界（NativeBoundary.cs）の InputManager.DoRumbleFrameUpdate（本体の逆コンパイル
    /// decompiled/sod-decomp/Dew.Core/InputManager.cs の転写）を本体に見立てて、Harmony は
    /// 介さずメソッドを直接呼ぶ（Harmony の意味論では Prefix の false は本体不実行に、
    /// Postfix は本体実行の有無にかかわらず常に実行になる）。確かめるのは、
    /// (1) リストが空でフラグが true のときだけ Prefix が false を返し、シェイクが現れれば
    /// 本体へ戻ること、(2) Postfix が DewInput.currentMode と Time.timeScale を正しく読んで
    /// 渡すこと、(3) 例外で _disabled になり警告1回で本体へ戻ること（fail-soft。Prefix と
    /// Postfix の両方で）、(4) 設定が無効のとき Prefix は常に本体を走らせ、Postfix は
    /// フラグを書かないこと。
    /// </summary>
    public sealed class GamepadRumbleIdlePerfWiringTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly float _savedTime, _savedTimeScale;
        private readonly InputMode _savedMode;

        // パッチ側の静的な欄（無効化・警告・フラグ）を毎テストで初期化し、Prepare を境界の
        // InputManager に対して通す（実機では Harmony が呼ぶのと同じ準備。_shakes の
        // FieldRef がここで決まる）。Dispose で時刻・timeScale・モード・設定を既定へ戻す。
        // テスト間で持ち越すものはない（InputManager の実体は毎テスト作り直す）。
        public GamepadRumbleIdlePerfWiringTests()
        {
            _savedTime = Time.time;
            _savedTimeScale = Time.timeScale;
            _savedMode = DewInput.currentMode;
            ResetPatchState();
            NativePerfOptions.GamepadRumbleIdle = true;
            Time.time = 10f;
            Time.timeScale = 1f;
            SetMode(InputMode.KeyboardAndMouse);
            Log.Warnings.Clear();
            Assert.True((bool)typeof(NativeGamepadRumbleIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.GamepadRumbleIdle = true;
            Time.time = _savedTime;
            Time.timeScale = _savedTimeScale;
            SetMode(_savedMode);
            Log.Warnings.Clear();
        }

        private static void ResetPatchState()
        {
            var type = typeof(NativeGamepadRumbleIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            type.GetField("_lastSentZero", Hidden).SetValue(null, false);
        }

        private static bool PrefixOf(InputManager manager)
            => (bool)typeof(NativeGamepadRumbleIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { manager });

        private static void PostfixOf(InputManager manager)
            => typeof(NativeGamepadRumbleIdlePerf).GetMethod("Postfix", Hidden)
                .Invoke(null, new object[] { manager });

        private static bool LastSentZero =>
            (bool)typeof(NativeGamepadRumbleIdlePerf).GetField("_lastSentZero", Hidden).GetValue(null);

        private static bool Disabled =>
            (bool)typeof(NativeGamepadRumbleIdlePerf).GetField("_disabled", Hidden).GetValue(null);

        private static void SetFlag(bool value)
            => typeof(NativeGamepadRumbleIdlePerf).GetField("_lastSentZero", Hidden).SetValue(null, value);

        // 実物は public static InputMode currentMode { get; private set; } のため、境界の
        // private setter へ reflection で書く。
        private static void SetMode(InputMode mode)
            => typeof(DewInput).GetProperty("currentMode", Hidden).SetValue(null, mode);

        private static ShakeInstance Shake(float now) => new ShakeInstance
            { startTime = now, attackTime = 0.05f, sustainTime = 0.1f, decayTime = 0.08f, amplitude = 0.5f };

        // (1) 空リスト + フラグ true だけが Prefix の false（本体を飛ばす）。初期フラグや
        // シェイクの存在するときは本体へ通る。飛ばしフレームでも Postfix は走り、フラグは
        // true のまま待機が続く。本体（境界スタブ）の送信記録とも突き合わせる。
        [Fact]
        public void ThePrefixSkipsOnlyTheVouchedIdleStateAndReturnsWhenAShakeAppears()
        {
            var manager = new InputManager();

            // 初期フラグは保証しないため、空リストでも本体へ通る。
            Assert.True(PrefixOf(manager));

            // 本体が走って (0, 0) を送り、Postfix が空リストから (0, 0) を導出する。
            manager.DoRumbleFrameUpdate();
            Assert.Equal(1, manager.sendCount);
            Assert.Equal(0f, manager.sentLow);
            Assert.Equal(0f, manager.sentHigh);
            PostfixOf(manager);
            Assert.True(LastSentZero);

            // 保証された待機: Prefix は false（本体を飛ばす）。飛ばしフレームでも Postfix は
            // 走り、フラグは true のまま（待機が続く限り飛ばし続ける）。
            Assert.False(PrefixOf(manager));
            PostfixOf(manager);
            Assert.True(LastSentZero);

            // シェイクが現れたフレームは本体へ戻る（フラグが立っていても）。
            manager._shakes.Add(Shake(Time.time));
            Assert.True(PrefixOf(manager));
            Assert.Empty(Log.Warnings);
        }

        // (2) Postfix は DewInput.currentMode と Time.timeScale を読んで DeriveSentZero へ
        // 渡す。ゲームパッドモードでシェイクが残る通常フレームだけフラグが立たず、
        // 非ゲームパッドモードと一時停止（timeScale 0.001 未満）では立つ。
        [Fact]
        public void ThePostfixDerivesFromTheModeAndTimescaleItReads()
        {
            var manager = new InputManager();
            manager._shakes.Add(Shake(Time.time));

            SetMode(InputMode.Gamepad);
            Time.timeScale = 1f;
            PostfixOf(manager);
            Assert.False(LastSentZero);

            SetMode(InputMode.KeyboardAndMouse);
            PostfixOf(manager);
            Assert.True(LastSentZero);

            SetMode(InputMode.Gamepad);
            Time.timeScale = 0.0009f;
            PostfixOf(manager);
            Assert.True(LastSentZero);
            Assert.Empty(Log.Warnings);
        }

        // (3) Prefix で例外（_shakes の読み出しに失敗する形）: true を返して本体へ戻り、
        // _disabled になって警告は1回だけ。以降の Prefix は状態に関係なく本体へ通す。
        [Fact]
        public void AnExceptionInThePrefixDisablesThePatchFailSoft()
        {
            var broken = new InputManager { _shakes = null };
            Assert.True(PrefixOf(broken));
            Assert.True(Disabled);
            Assert.Single(Log.Warnings);

            var idle = new InputManager();
            SetFlag(true);
            Assert.True(PrefixOf(idle));
            Assert.Single(Log.Warnings);
        }

        // (3) Postfix で例外: 例外を投げ返さず _disabled にして警告1回。
        [Fact]
        public void AnExceptionInThePostfixDisablesThePatchFailSoft()
        {
            var broken = new InputManager { _shakes = null };
            PostfixOf(broken);
            Assert.True(Disabled);
            Assert.Single(Log.Warnings);
        }

        // (4) 設定が無効のとき: Postfix は導出できる状態でもフラグを書かず（導出なら true に
        // 変わる非ゲームパッドの状態で確かめる）、Prefix は保証された待機状態でも本体を走らせる。
        [Fact]
        public void TheDisabledConfigRunsVanillaAndLeavesTheFlagAlone()
        {
            NativePerfOptions.GamepadRumbleIdle = false;
            var manager = new InputManager();
            manager._shakes.Add(Shake(Time.time));
            SetMode(InputMode.KeyboardAndMouse);

            SetFlag(false);
            PostfixOf(manager);
            Assert.False(LastSentZero);      // 導出なら true だが、設定が無効なので書かない

            SetFlag(true);
            Assert.True(PrefixOf(manager));  // 設定が無効なら保証済み待機でも本体へ
            Assert.Empty(Log.Warnings);
        }
    }
}
