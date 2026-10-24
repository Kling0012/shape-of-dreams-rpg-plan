using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cecil = Mono.Cecil.Cil;
using HarmonyLib;
using Mono.Cecil;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1131）責任の宝石の被弾演出RPC受け口のキャッシュ化 NativeGemResponsibilityHitPerf の検証。
    /// 境界（NativeBoundary.cs・NativeEmbracePositionBoundary.cs）の Gem_C_Responsibility・
    /// DewAudioSource・FxCameraShake・曲線のスタブを使い、Prefix を直接呼び出して、
    /// strength から pitchMultiplier・volumeMultiplier・amplitude・localScale への書き込み値と
    /// FxPlayNew(fx, e) の呼び出し引数が曲線の評価結果から本体（r1.4.1.2 の
    /// UserCode_RpcPlayHit__Entity__Single）と同じ式で導かれること、同じ fxHit では再走査が
    /// 起きないこと、参照が変わったときだけ作り直すこと、fxHit が無いとき・設定が無効のときは
    /// 本体経路へ戻ること、例外ではこの修正だけを無効化して戻すことを確かめる。
    /// あわせて Prepare の指紋検査（ValidateShape）が本体の IL の形を受け入れ、
    /// 走査の型の替え間違い・includeInactive 明示の3引数・localScale へ別の曲線の結果・
    /// fxHit 以外の対象の走査・曲線の割当て替え・FxPlayNew の引数替えを弾くことを確かめる。
    /// 検査に渡す見本 IL（ShapeIl）が実物の本体 IL と同じ形であることは、実 DLL
    /// （Dew.Contents.dll r1.4.1.2）を Mono.Cecil で読んで、指紋検査が見る窓の並びを
    /// 見本と突き合わせ、検査全体が実物の命令列（ループの組み立てや dup を含む）を
    /// 受け入れることで確かめる（ゲームの DLL が無い環境ではこの2件は飛ばす）。
    /// </summary>
    public sealed class GemResponsibilityHitPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly bool _savedOption;

        public GemResponsibilityHitPerfTests()
        {
            SetField("_disabled", false);
            SetField("_warned", false);
            SetField("_cachedFx", null);
            SetField("_cachedAudio", null);
            SetField("_cachedShake", null);
            _savedOption = NativePerfOptions.GemResponsibilityHitCache;
            NativePerfOptions.GemResponsibilityHitCache = true;
            Log.Warnings.Clear();
        }

        public void Dispose() => NativePerfOptions.GemResponsibilityHitCache = _savedOption;

        private static void SetField(string name, object value)
            => typeof(NativeGemResponsibilityHitPerf).GetField(name, Hidden).SetValue(null, value);

        private static bool PatchDisabled()
            => (bool)typeof(NativeGemResponsibilityHitPerf).GetField("_disabled", Hidden).GetValue(null);

        private static bool RunPrefix(Gem_C_Responsibility gem, Entity e, float strength)
            => (bool)typeof(NativeGemResponsibilityHitPerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { gem, e, strength });

        private static AnimationCurve Curve(Func<float, float> evaluator)
            => new AnimationCurve { evaluator = evaluator };

        // 本体と同じ構成（音源2つと揺れ1つ）の fxHit。曲線は strength から判別できる値を返す。
        private static Gem_C_Responsibility NewGem(GameObject fx, out DewAudioSource audio1,
            out DewAudioSource audio2, out FxCameraShake shake)
        {
            audio1 = new DewAudioSource();
            audio2 = new DewAudioSource();
            shake = new FxCameraShake();
            if (fx != null)
            {
                fx.children.Add(audio1);
                fx.children.Add(audio2);
                fx.children.Add(shake);
            }
            return new Gem_C_Responsibility
            {
                fxHit = fx,
                pitchCurve = Curve(s => 0.5f + s),
                volumeCurve = Curve(s => 2f * s),
                shakeAmpCurve = Curve(s => 3f - s),
            };
        }

        private static GameObject NewFx()
            => new GameObject { transform = new Transform() };

        [Fact]
        public void PrefixWritesCurveResultsAndPlaysWithNativeArguments()
        {
            Gem_C_Responsibility gem = NewGem(NewFx(), out DewAudioSource audio1,
                out DewAudioSource audio2, out FxCameraShake shake);
            GameObject fx = gem.fxHit;
            var victim = new Entity();
            int scaleWrites = fx.transform.localScaleWrites;

            // 1回目: 曲線の評価結果が各書き込み先へ入り、演出は fxHit と被害者で再生される。
            Assert.False(RunPrefix(gem, victim, 0.25f));
            Assert.Equal(0.75f, audio1.pitchMultiplier);
            Assert.Equal(0.75f, audio2.pitchMultiplier);
            Assert.Equal(0.5f, audio1.volumeMultiplier);
            Assert.Equal(0.5f, audio2.volumeMultiplier);
            Assert.Equal(2.75f, shake.amplitude);
            Assert.Equal(0.5f, fx.transform.localScale.x);
            Assert.Equal(0.5f, fx.transform.localScale.y);
            Assert.Equal(0.5f, fx.transform.localScale.z);
            Assert.Equal(scaleWrites + 1, fx.transform.localScaleWrites);
            Assert.Equal(1, gem.fxPlayNewCalls);
            Assert.Same(fx, gem.lastPlayedEffect);
            Assert.Same(victim, gem.lastPlayedAttach);
            Assert.Equal(2, fx.componentScans);

            // 2回目（同じ fxHit）: 値だけが作り直され、子の走査は起きない。
            Assert.False(RunPrefix(gem, victim, 0.5f));
            Assert.Equal(1f, audio1.pitchMultiplier);
            Assert.Equal(1f, audio1.volumeMultiplier);
            Assert.Equal(2.5f, shake.amplitude);
            Assert.Equal(1f, fx.transform.localScale.x);
            Assert.Equal(scaleWrites + 2, fx.transform.localScaleWrites);
            Assert.Equal(2, gem.fxPlayNewCalls);
            Assert.Same(fx, gem.lastPlayedEffect);
            Assert.Same(victim, gem.lastPlayedAttach);
            Assert.Equal(2, fx.componentScans);
        }

        [Fact]
        public void PrefixRebuildsCacheOnlyWhenFxReferenceChanges()
        {
            Gem_C_Responsibility gem = NewGem(NewFx(), out DewAudioSource old1,
                out DewAudioSource old2, out FxCameraShake oldShake);
            GameObject oldFx = gem.fxHit;
            var victim = new Entity();
            Assert.False(RunPrefix(gem, victim, 0.25f));

            GameObject newFx = NewFx();
            var newAudio = new DewAudioSource();
            var newShake = new FxCameraShake();
            newFx.children.Add(newAudio);
            newFx.children.Add(newShake);
            gem.fxHit = newFx;

            Assert.False(RunPrefix(gem, victim, 0.5f));
            Assert.Equal(2, oldFx.componentScans);
            Assert.Equal(2, newFx.componentScans);
            // 新しい側だけが書き換わり、古い側は前の値のまま。
            Assert.Equal(0.75f, old1.pitchMultiplier);
            Assert.Equal(0.5f, old2.volumeMultiplier);
            Assert.Equal(2.75f, oldShake.amplitude);
            Assert.Equal(1f, newAudio.pitchMultiplier);
            Assert.Equal(1f, newAudio.volumeMultiplier);
            Assert.Equal(2.5f, newShake.amplitude);
            Assert.Equal(1f, newFx.transform.localScale.x);
            Assert.Same(newFx, gem.lastPlayedEffect);
            Assert.Same(victim, gem.lastPlayedAttach);
        }

        [Fact]
        public void PrefixFallsBackWhenFxHitIsNull()
        {
            Gem_C_Responsibility gem = NewGem(null, out DewAudioSource audio1,
                out DewAudioSource audio2, out FxCameraShake shake);
            var victim = new Entity();

            Assert.True(RunPrefix(gem, victim, 0.25f));
            Assert.Equal(1f, audio1.pitchMultiplier);
            Assert.Equal(1f, audio2.volumeMultiplier);
            Assert.Equal(3f, shake.amplitude);
            Assert.Equal(0, gem.fxPlayNewCalls);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void PrefixFallsBackWhenOptionDisabled()
        {
            NativePerfOptions.GemResponsibilityHitCache = false;
            Gem_C_Responsibility gem = NewGem(NewFx(), out DewAudioSource audio1,
                out DewAudioSource audio2, out FxCameraShake shake);
            GameObject fx = gem.fxHit;
            var victim = new Entity();

            Assert.True(RunPrefix(gem, victim, 0.25f));
            Assert.Equal(1f, audio1.pitchMultiplier);
            Assert.Equal(1f, audio2.volumeMultiplier);
            Assert.Equal(3f, shake.amplitude);
            Assert.Equal(0, fx.componentScans);
            Assert.Equal(0, fx.transform.localScaleWrites);
            Assert.Equal(0, gem.fxPlayNewCalls);
            Assert.False(PatchDisabled());
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void PrefixDisablesItselfAndFallsBackOnException()
        {
            Gem_C_Responsibility gem = NewGem(NewFx(), out DewAudioSource audio1,
                out DewAudioSource audio2, out FxCameraShake shake);
            gem.volumeCurve.evaluator = _ => throw new InvalidOperationException("boundary boom");
            var victim = new Entity();

            // 曲線の評価で失敗したとき: 書き込みは一部でも済んでおらず、本体経路へ戻す。
            Assert.True(RunPrefix(gem, victim, 0.25f));
            Assert.True(PatchDisabled());
            Assert.Equal(1f, audio1.pitchMultiplier);
            Assert.Equal(1f, audio2.volumeMultiplier);
            Assert.Equal(3f, shake.amplitude);
            Assert.Equal(0, gem.fxPlayNewCalls);
            Assert.Single(Log.Warnings);
            Assert.Contains("gem responsibility hit cache", Log.Warnings[0]);

            // 無効化された後は、以後の呼び出しは何もしないで本体経路へ戻る。
            gem.volumeCurve.evaluator = s => 2f * s;
            Assert.True(RunPrefix(gem, victim, 0.25f));
            Assert.Equal(1f, audio1.pitchMultiplier);
            Assert.Equal(0, gem.fxPlayNewCalls);
            Assert.Single(Log.Warnings);
        }

        // ---- Prepare の指紋検査（ValidateShape）の受理と拒否 ----

        private static readonly FieldInfo PitchCurveField
            = AccessTools.Field(typeof(Gem_C_Responsibility), nameof(Gem_C_Responsibility.pitchCurve));
        private static readonly FieldInfo VolumeCurveField
            = AccessTools.Field(typeof(Gem_C_Responsibility), nameof(Gem_C_Responsibility.volumeCurve));
        private static readonly FieldInfo ShakeAmpCurveField
            = AccessTools.Field(typeof(Gem_C_Responsibility), nameof(Gem_C_Responsibility.shakeAmpCurve));
        private static readonly FieldInfo FxHitField
            = AccessTools.Field(typeof(Gem_C_Responsibility), nameof(Gem_C_Responsibility.fxHit));
        private static readonly FieldInfo FxActivateField
            = AccessTools.Field(typeof(Gem_C_Responsibility), nameof(Gem_C_Responsibility.fxActivateInit));
        private static readonly FieldInfo PitchField
            = AccessTools.Field(typeof(DewAudioSource), nameof(DewAudioSource.pitchMultiplier));
        private static readonly FieldInfo VolumeField
            = AccessTools.Field(typeof(DewAudioSource), nameof(DewAudioSource.volumeMultiplier));
        private static readonly FieldInfo AmplitudeField
            = AccessTools.Field(typeof(FxCameraShake), nameof(FxCameraShake.amplitude));
        private static readonly MethodInfo EvaluateMethod
            = AccessTools.Method(typeof(AnimationCurve), nameof(AnimationCurve.Evaluate), new[] { typeof(float) });
        private static readonly MethodInfo OneGetter
            = AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.one));
        private static readonly MethodInfo MultiplyMethod
            = AccessTools.Method(typeof(Vector3), "op_Multiply", new[] { typeof(Vector3), typeof(float) });
        private static readonly MethodInfo LocalScaleSetter
            = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.localScale));
        private static readonly MethodInfo PlayMethod
            = AccessTools.Method(typeof(Gem_C_Responsibility), nameof(Gem_C_Responsibility.FxPlayNew));

        // 境界に置いた走査拡張の見本（GetComponentsNonAllocExtension）から、引数の数と型引数を
        // 指定して具体化した呼び出しを取り出す。
        private static MethodInfo ScanOverload(int parameterCount, Type argument)
        {
            foreach (MethodInfo method in typeof(GetComponentsNonAllocExtension).GetMethods())
                if (method.Name == "GetComponentsInChildrenNonAlloc" && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == parameterCount)
                    return method.MakeGenericMethod(argument);
            throw new InvalidOperationException("scan stand-in overload missing");
        }

        // 本体 r1.4.1.2 の UserCode_RpcPlayHit__Entity__Single の IL のうち指紋検査が見る窓
        // （曲線の評価3組・子走査2回・書き込み3種・localScale・FxPlayNew）を同じ並びで組み立てる。
        // 並びは ilspycmd で取り出した実物の本体 IL の該当部分（spec に記録）と同じで、
        // 列挙子の現在要素を dup で複製して2回のフィールド書き込みに使うところと、FxPlayNew が
        // this に対する call で第1引数に fxHit を渡すところも実物どおり。各引数は拒否の見本の
        // ための1箇所の変更だけを表す。
        private static List<CodeInstruction> ShapeIl(bool secondScanAudio = false,
            bool explicitIncludeInactive = false, bool scaleFromPitch = false, bool scanOtherObject = false,
            bool swapCurveWrites = false, bool playWithStrength = false)
        {
            MethodInfo audioScan = ScanOverload(explicitIncludeInactive ? 3 : 2, typeof(DewAudioSource));
            MethodInfo shakeScan = ScanOverload(2, secondScanAudio ? typeof(DewAudioSource) : typeof(FxCameraShake));
            FieldInfo scanReceiver = scanOtherObject ? FxActivateField : FxHitField;
            return new List<CodeInstruction>
            {
                // 3つの曲線を strength（ldarg.2）だけで評価し、結果は 0/1/2 のローカルへ。
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, PitchCurveField),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Callvirt, EvaluateMethod),
                new CodeInstruction(OpCodes.Stloc_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, VolumeCurveField),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Callvirt, EvaluateMethod),
                new CodeInstruction(OpCodes.Stloc_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, ShakeAmpCurveField),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Callvirt, EvaluateMethod),
                new CodeInstruction(OpCodes.Stloc_2),
                // 子走査2回: ldarg.0 / ldfld <受け手> / ldloca / call GetComponentsInChildrenNonAlloc<T>。
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, scanReceiver),
                new CodeInstruction(OpCodes.Ldloca_S, null),
                new CodeInstruction(OpCodes.Call, audioScan),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, FxHitField),
                new CodeInstruction(OpCodes.Ldloca_S, null),
                new CodeInstruction(OpCodes.Call, shakeScan),
                // ループ内の書き込み: 列挙子の現在要素を dup で複製して（実物と同じく）
                // pitchMultiplier と volumeMultiplier の両方に使う。割当ては swapCurveWrites
                // で替えられる。
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Stfld, swapCurveWrites ? VolumeField : PitchField),
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Stfld, swapCurveWrites ? PitchField : VolumeField),
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Stfld, AmplitudeField),
                // fxHit.transform.localScale = Vector3.one * <volumeCurve の結果>。
                // scaleFromPitch は volume の代わりに音程曲線の結果を渡す見本に替える。
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, FxHitField),
                new CodeInstruction(OpCodes.Call, OneGetter),
                new CodeInstruction(scaleFromPitch ? OpCodes.Ldloc_0 : OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Call, MultiplyMethod),
                new CodeInstruction(OpCodes.Callvirt, LocalScaleSetter),
                // FxPlayNew(fxHit, e)。実物は this に対する call なので ldarg.0 が2つ並ぶ。
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, FxHitField),
                new CodeInstruction(playWithStrength ? OpCodes.Ldarg_2 : OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, PlayMethod),
                new CodeInstruction(OpCodes.Ret),
            };
        }

        [Fact]
        public void ShapeAcceptsNativeR1412Body()
            => Assert.True(NativeGemResponsibilityHitPerf.ValidateShape(ShapeIl()));

        [Fact]
        public void ShapeRejectsWrongScanType()
            => Assert.False(NativeGemResponsibilityHitPerf.ValidateShape(ShapeIl(secondScanAudio: true)));

        [Fact]
        public void ShapeRejectsExplicitIncludeInactiveScan()
            => Assert.False(NativeGemResponsibilityHitPerf.ValidateShape(ShapeIl(explicitIncludeInactive: true)));

        [Fact]
        public void ShapeRejectsScaleFromPitchCurve()
            => Assert.False(NativeGemResponsibilityHitPerf.ValidateShape(ShapeIl(scaleFromPitch: true)));

        [Fact]
        public void ShapeRejectsScanOfAnotherObject()
            => Assert.False(NativeGemResponsibilityHitPerf.ValidateShape(ShapeIl(scanOtherObject: true)));

        [Fact]
        public void ShapeRejectsSwappedCurveAssignments()
            => Assert.False(NativeGemResponsibilityHitPerf.ValidateShape(ShapeIl(swapCurveWrites: true)));

        [Fact]
        public void ShapeRejectsPlayingWithStrengthArgument()
            => Assert.False(NativeGemResponsibilityHitPerf.ValidateShape(ShapeIl(playWithStrength: true)));

        // ---- 実 DLL（Dew.Contents.dll r1.4.1.2）の本体 IL との突き合わせ ----
        // レビュー指摘 F1: 見本 IL（ShapeIl）が手組みであるため、実物の本体 IL と同じ形で
        // あることを確かめる。本体の IL は ilspycmd -il で取り出して spec
        // （docs/specs/perf-loop/20261024-1529.md）にも記録してある。

        // 本体のマネージド DLL の場所は tests/repro の抽出スクリプトと同じ SOD_MANAGED で
        // 受け取る。ゲームの DLL が無い環境（CI など）では確認のしようが無いため、
        // この2件は何も確かめずに終える（検証記録では未実行として扱う）。
        private static Cecil.MethodBody NativeBody()
        {
            string managed = Environment.GetEnvironmentVariable("SOD_MANAGED");
            string dll = string.IsNullOrEmpty(managed) ? null : Path.Combine(managed, "Dew.Contents.dll");
            if (dll == null || !File.Exists(dll)) return null;
            ModuleDefinition module = ModuleDefinition.ReadModule(dll);
            TypeDefinition type = module.GetType(nameof(Gem_C_Responsibility));
            Assert.NotNull(type);
            MethodDefinition method = type.Methods.FirstOrDefault(
                m => m.Name == "UserCode_RpcPlayHit__Entity__Single");
            Assert.NotNull(method);
            return method.Body;
        }

        // Cecil と System.Reflection.Emit は同じ ECMA の短名（"ldarg.s" など）で命令を
        // 表すため、名前で対応付ける。
        private static readonly Dictionary<string, OpCode> EmitOpCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode))
            .GroupBy(f => ((OpCode)f.GetValue(null)).ToString())
            .ToDictionary(g => g.Key, g => (OpCode)g.First().GetValue(null));

        private static readonly Dictionary<string, FieldInfo> BoundaryFields
            = new Dictionary<string, FieldInfo>
        {
            [nameof(Gem_C_Responsibility) + "::" + nameof(Gem_C_Responsibility.pitchCurve)] = PitchCurveField,
            [nameof(Gem_C_Responsibility) + "::" + nameof(Gem_C_Responsibility.volumeCurve)] = VolumeCurveField,
            [nameof(Gem_C_Responsibility) + "::" + nameof(Gem_C_Responsibility.shakeAmpCurve)] = ShakeAmpCurveField,
            [nameof(Gem_C_Responsibility) + "::" + nameof(Gem_C_Responsibility.fxHit)] = FxHitField,
            [nameof(DewAudioSource) + "::" + nameof(DewAudioSource.pitchMultiplier)] = PitchField,
            [nameof(DewAudioSource) + "::" + nameof(DewAudioSource.volumeMultiplier)] = VolumeField,
            [nameof(FxCameraShake) + "::" + nameof(FxCameraShake.amplitude)] = AmplitudeField,
        };

        private static readonly Dictionary<string, MethodInfo> BoundaryMethods
            = new Dictionary<string, MethodInfo>
        {
            // 本体の FxPlayNew は基本型 DewNetworkBehaviour のメソッドで、境界では
            // Gem_C_Responsibility に置いてある（指紋検査は名前と引数の数で見る）。
            ["DewNetworkBehaviour::FxPlayNew"] = PlayMethod,
            ["AnimationCurve::" + nameof(AnimationCurve.Evaluate)] = EvaluateMethod,
            ["Vector3::get_one"] = OneGetter,
            ["Vector3::op_Multiply"] = MultiplyMethod,
            ["Transform::set_localScale"] = LocalScaleSetter,
        };

        // 実物の命令列のオペランドを境界のスタブへ読み替えて Harmony の命令列に直す。指紋検査
        // が解釈する既知の字段と呼び出しは対応する境界の MemberInfo に置き換え、それ以外
        // （列挙子の呼び出し・get_transform・分岐・.ctor など）は検査の対象外なのでオペランド
        // 無しで並べる。ローカルは番号を保って LocalBuilder に写す（指紋検査の LocalIndex が
        // LocalVariableInfo と短い形式のオペコードで番号を読むため）。
        private static List<CodeInstruction> RemapToBoundary(Cecil.MethodBody body)
        {
            var host = new DynamicMethod("nativeBody", typeof(void), new[] { typeof(object) },
                typeof(GemResponsibilityHitPerfTests).Module);
            ILGenerator il = host.GetILGenerator();
            var locals = new LocalBuilder[body.Variables.Count];
            for (int i = 0; i < locals.Length; i++) locals[i] = il.DeclareLocal(typeof(object));
            var code = new List<CodeInstruction>(body.Instructions.Count);
            foreach (Cecil.Instruction ins in body.Instructions)
            {
                object operand = null;
                if (ins.Operand is Cecil.VariableDefinition variable) operand = locals[variable.Index];
                else if (ins.Operand is FieldReference field
                    && BoundaryFields.TryGetValue(field.DeclaringType.Name + "::" + field.Name,
                        out FieldInfo mappedField))
                {
                    operand = mappedField;
                }
                else if (ins.Operand is MethodReference method) operand = BoundaryMethod(method);
                if (!EmitOpCodes.TryGetValue(ins.OpCode.Name, out OpCode opcode))
                    throw new InvalidOperationException("unknown native opcode: " + ins.OpCode.Name);
                code.Add(new CodeInstruction(opcode, operand));
            }
            return code;
        }

        private static object BoundaryMethod(MethodReference method)
        {
            if (method is GenericInstanceMethod generic
                && generic.ElementMethod.Name == "GetComponentsInChildrenNonAlloc"
                && generic.ElementMethod.Parameters.Count == 2)
            {
                // 型引数の名前で境界の走査の具体化を選ぶ（DewAudioSource / FxCameraShake）。
                switch (generic.GenericArguments[0].Name)
                {
                    case nameof(DewAudioSource): return ScanOverload(2, typeof(DewAudioSource));
                    case nameof(FxCameraShake): return ScanOverload(2, typeof(FxCameraShake));
                }
            }
            return BoundaryMethods.TryGetValue(
                method.DeclaringType.Name + "::" + method.Name, out MethodInfo mapped) ? mapped : null;
        }

        // 指紋検査が解釈できる命令（窓の語彙）だけを「種類:要素」の形に直す。引数の読み込み・
        // dup・曲線の結果のローカル（0〜2）・既知の字段の読み書き・既知の呼び出しを残し、
        // それ以外（列挙子の呼び出し・get_transform・ハンドルのローカル・ldloca・分岐・
        // ret/pop など）は落とす。
        private static readonly HashSet<string> WindowFieldNames = new HashSet<string>
        {
            nameof(Gem_C_Responsibility.pitchCurve), nameof(Gem_C_Responsibility.volumeCurve),
            nameof(Gem_C_Responsibility.shakeAmpCurve), nameof(Gem_C_Responsibility.fxHit),
            nameof(DewAudioSource.pitchMultiplier), nameof(DewAudioSource.volumeMultiplier),
            nameof(FxCameraShake.amplitude),
        };

        private static readonly HashSet<string> WindowMethodNames = new HashSet<string>
        {
            nameof(AnimationCurve.Evaluate), "get_one", "op_Multiply", "set_localScale", "FxPlayNew",
        };

        private static readonly string[] ShortLocalOpcodes =
        {
            "ldloc.0", "ldloc.1", "ldloc.2", "ldloc.3", "stloc.0", "stloc.1", "stloc.2", "stloc.3",
        };

        private static IEnumerable<string> WindowOfShape(IEnumerable<CodeInstruction> code)
        {
            foreach (CodeInstruction ins in code)
            {
                string op = ins.opcode.ToString();
                if (op == "ldarg.0" || op == "ldarg.1" || op == "ldarg.2" || op == "dup")
                { yield return op; continue; }
                if (TryLocal(op, (ins.operand as LocalVariableInfo)?.LocalIndex, out string descriptor))
                { yield return descriptor; continue; }
                if (ins.operand is FieldInfo field && WindowFieldNames.Contains(field.Name))
                { yield return (op.StartsWith("stfld") ? "stfld" : "ldfld") + ":" + field.Name; continue; }
                if (ins.operand is MethodInfo method && op.StartsWith("call")
                    && WindowMethodNames.Contains(method.Name))
                { yield return "call:" + method.Name; continue; }
                if (ins.operand is MethodInfo scan && scan.Name == "GetComponentsInChildrenNonAlloc"
                    && op.StartsWith("call") && scan.IsGenericMethod)
                { yield return "call:GetComponentsInChildrenNonAlloc:" + scan.GetGenericArguments()[0].Name; }
            }
        }

        private static IEnumerable<string> WindowOfNative(Cecil.MethodBody body)
        {
            foreach (Cecil.Instruction ins in body.Instructions)
            {
                string op = ins.OpCode.Name;
                if (op == "ldarg.0" || op == "ldarg.1" || op == "ldarg.2" || op == "dup")
                { yield return op; continue; }
                if (TryLocal(op, (ins.Operand as Cecil.VariableDefinition)?.Index, out string descriptor))
                { yield return descriptor; continue; }
                if (ins.Operand is FieldReference field && WindowFieldNames.Contains(field.Name))
                { yield return (op.StartsWith("stfld") ? "stfld" : "ldfld") + ":" + field.Name; continue; }
                if (ins.Operand is GenericInstanceMethod scan
                    && scan.ElementMethod.Name == "GetComponentsInChildrenNonAlloc" && op.StartsWith("call"))
                { yield return "call:GetComponentsInChildrenNonAlloc:" + scan.GenericArguments[0].Name; continue; }
                if (ins.Operand is MethodReference method && op.StartsWith("call")
                    && WindowMethodNames.Contains(method.Name))
                { yield return "call:" + method.Name; }
            }
        }

        // 窓に残すローカルは曲線の結果（0〜2）だけ。短い形式（stloc.0 など）は番号をオペコード
        // から、長い形式（ldloc.s など）は渡された番号から読む（ldloca は "ldloc." で始まら
        // ないため対象外）。
        private static bool TryLocal(string op, int? longIndex, out string descriptor)
        {
            descriptor = null;
            bool load = op == "ldloc" || op == "ldloc.s" || op.StartsWith("ldloc.");
            bool store = op == "stloc" || op == "stloc.s" || op.StartsWith("stloc.");
            if (!load && !store) return false;
            if (Array.IndexOf(ShortLocalOpcodes, op) >= 0) longIndex = op[op.Length - 1] - '0';
            if (!longIndex.HasValue || longIndex.Value > 2) return false;
            descriptor = (load ? "ldloc:" : "stloc:") + longIndex.Value;
            return true;
        }

        [Fact]
        public void ShapeAcceptsActualDewContentsR1412BodyIl()
        {
            Cecil.MethodBody body = NativeBody();
            if (body == null) return;
            Assert.True(NativeGemResponsibilityHitPerf.ValidateShape(RemapToBoundary(body)),
                "Prepare の指紋検査は実 DLL の本体 IL（ループの組み立てや dup を含む全体）を"
                + "受け入れるべきである");
        }

        [Fact]
        public void ShapeIlWindowMatchesActualDewContentsR1412BodyIl()
        {
            Cecil.MethodBody body = NativeBody();
            if (body == null) return;
            Assert.Equal(WindowOfShape(ShapeIl()), WindowOfNative(body));
        }
    }
}
