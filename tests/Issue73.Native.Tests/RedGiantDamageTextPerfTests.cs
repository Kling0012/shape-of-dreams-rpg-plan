using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#966）Mon_RedGiant の被ダメ計測テキストを値が変わらないフレームで再組み立てしない
    /// NativeRedGiantDamageTextPerf の検証。境界（NativeBoundary.cs）の模倣
    /// Mon_RedGiant.ActiveLogicUpdate は本体 Dew.Core.dll r1.4.0.13・r1.4.1.2 の末尾の転写で、
    /// IL は 2026-10-20 に ilspycmd で確認した（各 ldfld の前に ldarg.0 が入る
    /// ldfld×3 + box×3 + string.Format + TMP_Text.set_text）。実際に Harmony で当てて:
    /// (a) スポーン直後の 0/0/0 の最初のフレームでも text が本体と同じ文字列で設定される
    ///     こと（前ラウンド指摘 F1: 記憶の初期値 0 との同値誤判定で初回代入が飛ぶ回帰検査）、
    /// (b) 値が変わらないフレームでは text への再代入が起きないこと、
    /// (c) 値が変わったフレームでは本体と同じ string.Format の結果が同じフレームで入ること、
    /// (d) 設定を切ると毎フレーム代入して本体どおりであること、
    /// (e) 欄の読み出しが直接並ばない形（一度局所変数へ受ける）の本体では Transpiler は
    ///     命令を 1 つも変えず、警告は最初の 1 回だけ出ること（前ラウンド指摘 F2 の前提の
    ///     照合が外れた場合の fail-soft）、
    /// (f) 0 → -0 のフレームでは再び組み立てて本体と同じ "-0" 表示を入れること
    ///     （前ラウンド指摘 F1: float の == は 0 と -0 を同値と読むが、書式は
    ///     "0" と "-0" を分ける）。
    /// (g)（#1025）同じテキスト実体のまま ON → OFF → ON と設定を切り替えたとき、
    ///     OFF の間の書き込みがキャッシュに残らないせいで、再び ON に戻した
    ///     最初のフレーム（計測値が前回 ON 時と同じ組へ戻る）で古いキャッシュに
    ///     一致して return し、OFF 中に書いた表示が残ることの回帰検査。
    /// </summary>
    public sealed class RedGiantDamageTextPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // パッチ側の静的な欄を読み書きするため、毎テストで警告の抑制と fail-soft の停止を
        // 戻し、設定を既定（有効）へ戻す。ログの警告欄も毎テスト空に戻す。
        public RedGiantDamageTextPerfTests()
        {
            ResetPatchState();
            NativePerfOptions.RedGiantDamageText = true;
            Log.Warnings.Clear();
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.RedGiantDamageText = true;
            Log.Warnings.Clear();
        }

        private static void ResetPatchState()
        {
            var type = typeof(NativeRedGiantDamageTextPerf);
            type.GetField("_warned", Hidden).SetValue(null, false);
            type.GetField("_disabled", Hidden).SetValue(null, false);
        }

        private static bool PrepareRan()
            => (bool)typeof(NativeRedGiantDamageTextPerf).GetMethod("Prepare", Hidden).Invoke(null, null);

        // 境界の ActiveLogicUpdate にパッチを当てる。Prepare が通り（欄と対象の解決）、
        // 形の照合も通って警告が出ないことを毎回確かめる（指摘 F2: 照合が実際の IL の
        // 並びと合っていなければここで警告が出て失敗する）。
        private static Harmony Patch()
        {
            Assert.True(PrepareRan());
            var harmony = new Harmony("issue73.redgiant." + Guid.NewGuid().ToString("N"));
            harmony.CreateClassProcessor(typeof(NativeRedGiantDamageTextPerf)).Patch();
            Assert.Empty(Log.Warnings);
            return harmony;
        }

        private static Mon_RedGiant Spawn(float total, float dps, float max)
            => new Mon_RedGiant
            {
                dpsText = new TMPro.TMP_Text(),
                _totalDamage = total,
                _dps = dps,
                _maxSingleDmg = max,
            };

        private static void RunFrame(Mon_RedGiant giant) => giant.ActiveLogicUpdate(0f);

        // (a) スポーン直後の 0/0/0 のフレーム。本体はこのフレームで必ず
        // "Total: 0\r\nDPS: 0\r\nMax Single Dmg: 0" を代入する。パッチを当てても
        // 初回の代入が飛ばないこと（記憶の初期値は NaN で 0 と同値にならない）。
        [Fact]
        public void TheFirstZeroFrameSetsTheTextLikeVanilla()
        {
            var vanilla = Spawn(0f, 0f, 0f);
            RunFrame(vanilla);
            Assert.Equal(1, vanilla.dpsText.TextWrites);
            Assert.Equal("Total: 0\r\nDPS: 0\r\nMax Single Dmg: 0", vanilla.dpsText.text);

            var harmony = Patch();
            try
            {
                var patched = Spawn(0f, 0f, 0f);
                RunFrame(patched);
                Assert.Equal(1, patched.dpsText.TextWrites); // 初回も代入される
                Assert.Equal(vanilla.dpsText.text, patched.dpsText.text);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (b) 値が変わらないフレームの列。最初のフレームで1度代入された後は、
        // text への代入が起きないこと。表示文字列は本体と同じ。
        [Fact]
        public void AnUnchangedFrameDoesNotAssignAgain()
        {
            var harmony = Patch();
            try
            {
                var giant = Spawn(100f, 50f, 40f);
                RunFrame(giant);
                Assert.Equal(1, giant.dpsText.TextWrites);
                var first = giant.dpsText.text;
                RunFrame(giant);
                RunFrame(giant);
                Assert.Equal(1, giant.dpsText.TextWrites); // 同値フレームでは再代入しない
                Assert.Equal(first, giant.dpsText.text);

                var vanilla = Spawn(100f, 50f, 40f);
                RunFrame(vanilla);
                Assert.Equal(1, vanilla.dpsText.TextWrites);
                Assert.Equal(vanilla.dpsText.text, giant.dpsText.text);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (c) 値が変わったフレーム。変わったそのフレームで本体と同じ string.Format の
        // 結果が入ること（3つとも変える。どれか1つでも変われば組み立て直す）。
        [Fact]
        public void AChangedFrameFormatsAndAssignsLikeVanilla()
        {
            var harmony = Patch();
            try
            {
                var giant = Spawn(100f, 50f, 40f);
                RunFrame(giant);
                Assert.Equal(1, giant.dpsText.TextWrites);
                giant._totalDamage = 98765.5f;
                giant._dps = 25.5f;
                giant._maxSingleDmg = 1234.5f;
                RunFrame(giant);
                Assert.Equal(2, giant.dpsText.TextWrites); // 変わったフレームで代入される

                var vanilla = Spawn(98765.5f, 25.5f, 1234.5f);
                RunFrame(vanilla);
                Assert.Equal(vanilla.dpsText.text, giant.dpsText.text);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (d) 設定を切っている間は毎フレーム組み立てて代入する（本体どおり）こと。
        [Fact]
        public void ADisabledOptionAssignsEveryFrameLikeVanilla()
        {
            var harmony = Patch();
            try
            {
                NativePerfOptions.RedGiantDamageText = false;
                var giant = Spawn(100f, 50f, 40f);
                RunFrame(giant);
                RunFrame(giant);
                Assert.Equal(2, giant.dpsText.TextWrites); // OFF では毎回代入する

                var vanilla = Spawn(100f, 50f, 40f);
                RunFrame(vanilla);
                Assert.Equal(vanilla.dpsText.text, giant.dpsText.text);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (e) 欄の読み出しが直接並ばない形の本体では、Transpiler は命令を 1 つも変えず
        // （＝パッチが外れ）、警告は最初の 1 回だけ出る。2 回目の照合でも警告は増えない。
        [Fact]
        public void AWrongShapedBodyIsLeftAloneWithASingleWarning()
        {
            Assert.True(PrepareRan()); // 欄と対象の解決
            var bad = typeof(Mon_RedGiant).GetMethod("ActiveLogicUpdateWrongShape", Hidden);
            Assert.NotNull(bad);
            var transpiler = typeof(NativeRedGiantDamageTextPerf).GetMethod("Transpiler", Hidden);
            var before = PatchProcessor.GetOriginalInstructions(bad);
            string original = string.Join(",", before.Select(i => i.opcode.Name));
            var first = (IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before });
            Assert.Equal(original, string.Join(",", first.Select(i => i.opcode.Name))); // 変えていない
            Assert.Single(Log.Warnings); // 警告は 1 回
            Assert.Contains("red giant damage text", Log.Warnings[0]);
            Assert.True((bool)typeof(NativeRedGiantDamageTextPerf).GetField("_warned", Hidden).GetValue(null));
            var second = (IEnumerable<CodeInstruction>)transpiler.Invoke(
                null, new object[] { PatchProcessor.GetOriginalInstructions(bad) });
            Assert.Equal(original, string.Join(",", second.Select(i => i.opcode.Name)));
            Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない
        }

        // (f) 0 → -0 のフレーム。float の == では 0 と -0 は同値だが、書式
        // {0:#,##0} は "0" と "-0" を区別する（.NET Core 3.0 以降の書式）。
        // 本体は毎フレーム "-0" を代入するため、パッチ側もビット単位の比較で
        // このフレームを再組み立てすること（前ラウンド指摘 F1）。
        [Fact]
        public void AZeroToNegativeZeroFrameReformatsLikeVanilla()
        {
            var harmony = Patch();
            try
            {
                var giant = Spawn(0f, 0f, 0f);
                RunFrame(giant);
                Assert.Equal(1, giant.dpsText.TextWrites);
                Assert.Equal("Total: 0\r\nDPS: 0\r\nMax Single Dmg: 0", giant.dpsText.text);

                giant._dps = -0f;
                RunFrame(giant);
                Assert.Equal(2, giant.dpsText.TextWrites); // -0 は 0 と同値でも再代入する

                var vanilla = Spawn(0f, -0f, 0f);
                RunFrame(vanilla);
                Assert.Equal("Total: 0\r\nDPS: -0\r\nMax Single Dmg: 0", giant.dpsText.text);
                Assert.Equal(vanilla.dpsText.text, giant.dpsText.text); // 本体と同じ "-0" 表示
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (g)（#1025）同じテキスト実体のまま ON: 0/0/0 → OFF: 100/50/40 → ON: 0/0/0 の
        // 順に通す。ON の間は値が同じフレームで書き込みを省くが、OFF の間の書き込みは
        // キャッシュを更新しないため、3回目（再び ON の 0/0/0）では表示が 100/50/40 の
        // ままでもキャッシュ（0/0/0）と一致して return してしまう。3回目のフレームで
        // 本体と同じゼロ表示に書き直り、その後の同値フレームは再び省かれること。
        [Fact]
        public void ReEnablingAfterAnOffWindowRewritesTheVanillaZero()
        {
            var harmony = Patch();
            try
            {
                var giant = Spawn(0f, 0f, 0f);
                RunFrame(giant); // ON: 0/0/0。キャッシュも 0/0/0 になる。
                Assert.Equal(1, giant.dpsText.TextWrites);
                Assert.Equal("Total: 0\r\nDPS: 0\r\nMax Single Dmg: 0", giant.dpsText.text);

                NativePerfOptions.RedGiantDamageText = false;
                giant._totalDamage = 100f;
                giant._dps = 50f;
                giant._maxSingleDmg = 40f;
                RunFrame(giant); // OFF: 表示は 100/50/40 に変わるが、キャッシュは 0/0/0 のまま。
                Assert.Equal(2, giant.dpsText.TextWrites);
                Assert.Equal("Total: 100\r\nDPS: 50\r\nMax Single Dmg: 40", giant.dpsText.text);

                NativePerfOptions.RedGiantDamageText = true;
                giant._totalDamage = 0f;
                giant._dps = 0f;
                giant._maxSingleDmg = 0f;
                RunFrame(giant); // 再び ON の最初のフレーム。計測値 0/0/0 は古いキャッシュと
                // 同値でも、実際の表示（100/50/40）とは食い違うため書き直ること。

                var vanilla = Spawn(0f, 0f, 0f);
                RunFrame(vanilla);
                Assert.Equal(vanilla.dpsText.text, giant.dpsText.text); // 本体と同じゼロ表示
                Assert.Equal(3, giant.dpsText.TextWrites); // 3回目のフレームで代入される

                // 3回目の後の同値フレームは再び省く（書き込み省略の最適化は残る）。
                RunFrame(giant);
                RunFrame(giant);
                Assert.Equal(3, giant.dpsText.TextWrites);
                Assert.Equal(vanilla.dpsText.text, giant.dpsText.text);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }
    }
}
