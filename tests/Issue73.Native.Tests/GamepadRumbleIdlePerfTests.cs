using System.Collections.Generic;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1118）振動が止まっているフレームのゲームパッド振動オフの毎フレーム送信を省く
    /// NativeGamepadRumbleIdlePerf のスキップ判定の検証。判定は引数だけから決まる純粋な
    /// 静的関数（ShouldSkip・DeriveSentZero）に切り出してあり、Prefix/Postfix は状態を
    /// 読んでこの関数へ渡すだけ（Harmony 経路はテストから駆動しない）。ここでは
    /// (1) リストが空で前回の本体実行が確実に (0,0) を送ったときだけ本体を飛ばす、
    /// (2) ゲームパッドモードかつ timeScale 0.001 以上でシェイクが残るとき前回送信を
    /// (0,0) 扱いにしない、(3) 最後のシェイクが消えたフレームに本体を実行する、の3つと、
    /// シェイク発生 → 減衰 → 消滅 → 待機と進めたフレーム列で、本体を実行するか飛ばすかの
    /// 順序が本体どおりの送信列（非ゼロの送信は全て同じフレームに起こり、消滅フレームに
    /// 最後の (0,0) が1回だけ送られ、以後の待機フレームは何も送らない）になることを
    /// 確かめる。送信値の模擬（VanillaFoldAndSend）は本体 DoRumbleFrameUpdate の構造
    /// （期限切れを畳み込みで捨て、リスト空・非ゲームパッド・一時停止では (0,0)、それ以外は
    /// エンベロープ加算）の転写で、判定関数とは独立に書いている。両者の突き合わせにより、
    /// 飛ばしたフレームに非ゼロの送信や消滅フレームの最終送信が紛れ込まないことを検証する。
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
        public void AnUnvouchedIdleStateRunsTheBody()
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

        // 本体 DoRumbleFrameUpdate の構造の転写。期限切れのシェイクを畳み込みで捨て、
        // リストが空・非ゲームパッド・一時停止では (0, 0) を送り、それ以外は残ったシェイクの
        // エンベロープ（attack で立上がり、sustain を保ち、decay で減る）を加算する。
        // 判定関数（ShouldSkip・DeriveSentZero）とは独立に書いており、この突き合わせが
        // 検証の本体。戻り値は送った値（両モーター同じ）。リストは畳み込みで破壊的に更新する。
        private static float VanillaFoldAndSend(List<ShakeInstance> shakes, bool gamepadMode, float timeScale, float now)
        {
            shakes.RemoveAll(s => now >= s.startTime + s.attackTime + s.sustainTime + s.decayTime);
            if (!gamepadMode || timeScale < 0.001f || shakes.Count == 0) return 0f;
            float v = 0f;
            foreach (var s in shakes)
            {
                float t = now - s.startTime;
                if (t < s.attackTime) v += s.amplitude * (t / s.attackTime);
                else if (t < s.attackTime + s.sustainTime) v += s.amplitude;
                else v += s.amplitude * (1f - (t - s.attackTime - s.sustainTime) / s.decayTime);
            }
            return v;
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
            var sent = new float[frames];              // 実行したとき本体が送った値（飛ばしは未定義）

            for (int f = 0; f < frames; f++)
            {
                float now = f * dt;
                if (f == 1)
                    shakes.Add(new ShakeInstance { startTime = now, attackTime = 0.05f, sustainTime = 0.1f, decayTime = 0.08f, amplitude = 0.5f });

                ran[f] = !NativeGamepadRumbleIdlePerf.ShouldSkip(shakes.Count, lastSentZero);
                if (!ran[f]) continue;                 // スキップ: 何も送らない。フラグは前回のまま。

                sent[f] = VanillaFoldAndSend(shakes, gamepadMode: true, timeScale: 1f, now);
                lastSentZero = NativeGamepadRumbleIdlePerf.DeriveSentZero(shakes.Count, gamepadMode: true, timeScale: 1f);
            }

            // f0: 初期フラグが保証しないため本体が走り (0, 0) を送る。f1〜f14: シェイクがあるので
            // 毎フレーム本体（f1 は attack の始まりで値 0、f2〜f14 は非ゼロ）。f15: 最後のシェイクが
            // 畳み込みで消えるフレーム。この frames 0〜15 が本体（毎フレーム送信）と同じ送信列で、
            // (3) 消滅フレームにも本体が走り、最後の (0, 0) が1回だけ送られる。
            for (int f = 0; f <= 15; f++) Assert.True(ran[f], $"frame {f} must run the body");
            Assert.Equal(0f, sent[0]);                 // 初回の保証されない待機も (0, 0) を送って整える
            Assert.Equal(0f, sent[1]);                 // 発生直後は attack 0 で値 0（本体も同じ）
            for (int f = 2; f <= 14; f++) Assert.True(sent[f] > 0f, $"frame {f} must send non-zero");
            Assert.Equal(0f, sent[15]);                // 消滅フレームの最終 (0, 0)（(2) のフラグが立たなかった保証）

            // f16〜f19: リストが空で前回の送信が (0, 0) と保証された待機。(1) 本体を飛ばし、
            // 何も送らない。スキップは一度始まると待機が続く限り戻らない（順序の保証）。
            for (int f = 16; f < frames; f++) Assert.False(ran[f], $"frame {f} must skip the body");
            Assert.True(lastSentZero);                 // 待機に入ったあとも導出は (0, 0) を保証し続ける
            Assert.Empty(shakes);                      // 飛ばしたフレームで畳み込みは不要（リストは空のまま）
        }
    }
}
