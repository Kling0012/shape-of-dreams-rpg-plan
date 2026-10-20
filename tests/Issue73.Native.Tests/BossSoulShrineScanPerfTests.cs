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
    /// （#980）ボスソウルの神殿のボス存在走査の LINQ 列挙子の確保を省く
    /// NativeBossSoulShrineScanPerf の検証。境界（BossSoulShrineScanPerfBoundary.cs）の模倣
    /// Shrine_BossSoul に対して: (a) Transpiler は末尾の Enumerable.Any 呼び出しを 1 つだけ
    /// BossScan へ差し替え、命令数は増減しない、(b) 実際に Harmony で当てた神殿は、集合が空・
    /// 雑魚のみ・ボスの派生クラス・null を含む・混在のどれでも当てる前と同じ可否を返す、
    /// (c) 設定を切っても同じ結果のままネイティブの Any へ戻る、(d) 述語がボスの is 判定
    /// だけでなくなった本体（is 判定とフラグの複合・別の型への is 判定・述語がフィールドに
    /// キャッシュされた形）では差し替えを行わず警告が 1 回だけ出て、挙動は本体の述語のまま、
    /// (e) 型名が解決できないとき TryPrepare は false を返す（この修正だけが外れる）。
    /// </summary>
    public sealed class BossSoulShrineScanPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // パッチ側の静的な欄を読み書きするため、毎テストで無効化と警告の抑制を解除し、模倣型に
        // 向け直して、Dispose で設定を既定（有効）へ戻す。テスト間で持ち越すものはない
        // （実体集合は毎テスト作り直す）。
        public BossSoulShrineScanPerfTests()
        {
            ResetPatchState();
            NativePerfOptions.BossSoulShrineScan = true;
            Log.Warnings.Clear();
            ShrineBossSoulActorManager.allEntities.Clear();
            Assert.True(NativeBossSoulShrineScanPerf.TryPrepare("Shrine_BossSoul", "ShrineBossSoulBoss"));
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.BossSoulShrineScan = true;
            Log.Warnings.Clear();
            ShrineBossSoulActorManager.allEntities.Clear();
        }

        private static void ResetPatchState()
        {
            var type = typeof(NativeBossSoulShrineScanPerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
        }

        private static MethodBase CanInteractOf(Type shrine)
            => shrine.GetMethod("CanInteract", Hidden);

        private static IEnumerable<CodeInstruction> Transpile(MethodBase target)
            => (IEnumerable<CodeInstruction>)typeof(NativeBossSoulShrineScanPerf).GetMethod("Transpiler", Hidden)
                .Invoke(null, new object[] { PatchProcessor.GetOriginalInstructions(target) });

        private static int CountCall(IEnumerable<CodeInstruction> code, string method)
        {
            int count = 0;
            foreach (var instruction in code)
                if (instruction.operand is MethodInfo info && info.Name == method) count++;
            return count;
        }

        private static void LoadEntities(params Entity[] entities)
        {
            ShrineBossSoulActorManager.allEntities.Clear();
            foreach (var entity in entities)
                ShrineBossSoulActorManager.allEntities.Add(entity);
        }

        private static Entity Interactor()
            => new Entity { owner = new DewPlayer { guid = "player-1" } };

        // (a) 差し替えは Any の呼び出し 1 箇所だけ。命令数は変わらず、警告も出ない。
        [Fact]
        public void TheTranspilerSwapsOnlyTheAnyCall()
        {
            var target = CanInteractOf(typeof(Shrine_BossSoul));
            var before = PatchProcessor.GetOriginalInstructions(target).ToList();
            var after = Transpile(target).ToList();
            Assert.Empty(Log.Warnings);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(1, CountCall(before, "Any"));
            Assert.Equal(0, CountCall(after, "Any"));
            Assert.Equal(1, CountCall(after, "BossScan"));
            for (int i = 0; i < before.Count; i++)
                if (before[i].operand is MethodInfo m && m.Name == "Any")
                    Assert.Equal("BossScan", ((MethodInfo)after[i].operand).Name);
        }

        // (b) パッチを当てた神殿の可否。空・雑魚のみ・ボスの派生クラス・null を含む・混在の
        // どの集合でも、当てる前の本体と同じ答えを返す（null は is 判定でも
        // IsInstanceOfType でも一致しない）。
        [Fact]
        public void ThePatchedShrineAnswersLikeTheVanillaBody()
        {
            var shrine = new Shrine_BossSoul();
            var interactor = Interactor();
            bool Run()
            {
                var target = CanInteractOf(typeof(Shrine_BossSoul));
                return (bool)target.Invoke(shrine, new object[] { interactor });
            }
            // 当てる前の本体の答えを確定する（値も意味の担保として固定する）。
            LoadEntities();
            bool empty = Run();
            LoadEntities(new Entity(), new Entity());
            bool nonBossOnly = Run();
            LoadEntities(new ShrineBossSoulBoss());
            bool boss = Run();
            LoadEntities(new ShrineBossSoulBossDerived());
            bool bossDerived = Run();
            LoadEntities(null, new Entity());
            bool withNull = Run();
            LoadEntities(new Entity(), new ShrineBossSoulBoss(), new Entity());
            bool mixed = Run();
            Assert.True(empty);
            Assert.True(nonBossOnly);
            Assert.False(boss);
            Assert.False(bossDerived);
            Assert.True(withNull);
            Assert.False(mixed);

            var harmony = new Harmony("issue73.bosssoul." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(CanInteractOf(typeof(Shrine_BossSoul)),
                    transpiler: new HarmonyMethod(typeof(NativeBossSoulShrineScanPerf).GetMethod("Transpiler", Hidden)));
                Assert.Empty(Log.Warnings); // 差し替えが通っている
                LoadEntities();
                Assert.Equal(empty, Run());
                LoadEntities(new Entity(), new Entity());
                Assert.Equal(nonBossOnly, Run());
                LoadEntities(new ShrineBossSoulBoss());
                Assert.Equal(boss, Run());
                LoadEntities(new ShrineBossSoulBossDerived());
                Assert.Equal(bossDerived, Run());
                LoadEntities(null, new Entity());
                Assert.Equal(withNull, Run());
                LoadEntities(new Entity(), new ShrineBossSoulBoss(), new Entity());
                Assert.Equal(mixed, Run());
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (c) 設定を切ったとき。差し替えは当たったままだが、BossScan はネイティブの Any へ
        // 戻るため、どの集合でも答えは本体と同じまま変わらない。
        [Fact]
        public void ADisabledOptionKeepsTheVanillaAnswers()
        {
            var shrine = new Shrine_BossSoul();
            var interactor = Interactor();
            bool Run()
                => (bool)CanInteractOf(typeof(Shrine_BossSoul)).Invoke(shrine, new object[] { interactor });

            var harmony = new Harmony("issue73.bosssoul." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(CanInteractOf(typeof(Shrine_BossSoul)),
                    transpiler: new HarmonyMethod(typeof(NativeBossSoulShrineScanPerf).GetMethod("Transpiler", Hidden)));
                Assert.Empty(Log.Warnings);
                NativePerfOptions.BossSoulShrineScan = false;
                LoadEntities();
                Assert.True(Run());
                LoadEntities(new Entity(), new Entity());
                Assert.True(Run());
                LoadEntities(new ShrineBossSoulBossDerived());
                Assert.False(Run());
                LoadEntities(null, new Entity());
                Assert.True(Run());
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (d) 述語が「is 判定とフラグの複合」に変わった本体。差し替えは行われず（IL 照合で
        // Any が残り BossScan は入らない）警告は 1 回だけ。実際に当てても挙動は本体の述語の
        // まま: isActive=false のボスは「いない」のと同じ扱いになり、誤って差し替わっていれば
        // _bossType だけの判定（使用不可）に変わって見えるはずである。
        [Fact]
        public void ACompoundPredicateBodyIsLeftAlone()
        {
            Assert.True(NativeBossSoulShrineScanPerf.TryPrepare("Shrine_BossSoulCompound", "ShrineBossSoulBoss"));
            var target = CanInteractOf(typeof(Shrine_BossSoulCompound));
            var shrine = new Shrine_BossSoulCompound();
            var interactor = Interactor();
            var before = PatchProcessor.GetOriginalInstructions(target).ToList();

            LoadEntities(new ShrineBossSoulBoss { isActive = false });
            bool dormantBossUsable = (bool)target.Invoke(shrine, new object[] { interactor });
            Assert.True(dormantBossUsable); // 本体の述語では isActive=false のボスは引っかからない

            var transpiled = Transpile(target).ToList();
            Assert.Equal(CountCall(before, "Any"), CountCall(transpiled, "Any")); // Any は残る
            Assert.Equal(0, CountCall(transpiled, "BossScan"));                    // 差し替わらない
            Assert.Single(Log.Warnings);
            Assert.Contains("boss soul shrine scan", Log.Warnings[0]);
            Transpile(target).ToList();
            Assert.Single(Log.Warnings); // 2 回目の照合でも警告は増えない

            var harmony = new Harmony("issue73.bosssoul." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(target,
                    transpiler: new HarmonyMethod(typeof(NativeBossSoulShrineScanPerf).GetMethod("Transpiler", Hidden)));
                Assert.True((bool)target.Invoke(shrine, new object[] { interactor })); // 本体の述語どおり
                LoadEntities(new ShrineBossSoulBoss());
                Assert.False((bool)target.Invoke(shrine, new object[] { interactor })); // 活きたボスは不可
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (d) 述語が同じ形でも別の型への is 判定になっている本体。_bossType の型は集合にいるが
        // 述語の型はいない、という場面で差し替えを見送れるか: 誤って差し替わっていれば
        // 「ボスがいる」（使用不可）に変わるはずである。
        [Fact]
        public void AnOtherBossPredicateBodyIsLeftAlone()
        {
            Assert.True(NativeBossSoulShrineScanPerf.TryPrepare("Shrine_BossSoulOtherBoss", "ShrineBossSoulBoss"));
            var target = CanInteractOf(typeof(Shrine_BossSoulOtherBoss));
            var shrine = new Shrine_BossSoulOtherBoss();
            var interactor = Interactor();

            LoadEntities(new ShrineBossSoulBoss());
            bool vanilla = (bool)target.Invoke(shrine, new object[] { interactor });
            Assert.True(vanilla); // 述語の型（Rival）はいない → 使用可

            var transpiled = Transpile(target).ToList();
            Assert.Equal(1, CountCall(transpiled, "Any"));
            Assert.Equal(0, CountCall(transpiled, "BossScan"));
            Assert.Single(Log.Warnings);

            var harmony = new Harmony("issue73.bosssoul." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(target,
                    transpiler: new HarmonyMethod(typeof(NativeBossSoulShrineScanPerf).GetMethod("Transpiler", Hidden)));
                Assert.True((bool)target.Invoke(shrine, new object[] { interactor })); // 本体の述語どおり
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (d) 述語が静的フィールドに置かれた本体。IL に述語の ldftn + newobj が現れないため
        // 述語の中身を突き止められず、差し替えは見送られる。挙動はネイティブの Any のまま。
        [Fact]
        public void ACachedPredicateBodyIsLeftAlone()
        {
            Assert.True(NativeBossSoulShrineScanPerf.TryPrepare("Shrine_BossSoulCachedPredicate", "ShrineBossSoulBoss"));
            var target = CanInteractOf(typeof(Shrine_BossSoulCachedPredicate));
            var shrine = new Shrine_BossSoulCachedPredicate();
            var interactor = Interactor();

            LoadEntities(new ShrineBossSoulBoss());
            bool vanilla = (bool)target.Invoke(shrine, new object[] { interactor });
            Assert.False(vanilla); // 述語（is Boss）どおり使用不可

            var transpiled = Transpile(target).ToList();
            Assert.Equal(1, CountCall(transpiled, "Any"));
            Assert.Equal(0, CountCall(transpiled, "BossScan"));
            Assert.Single(Log.Warnings);

            var harmony = new Harmony("issue73.bosssoul." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(target,
                    transpiler: new HarmonyMethod(typeof(NativeBossSoulShrineScanPerf).GetMethod("Transpiler", Hidden)));
                Assert.False((bool)target.Invoke(shrine, new object[] { interactor })); // 本体の述語どおり
                LoadEntities();
                Assert.True((bool)target.Invoke(shrine, new object[] { interactor }));
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (e) 型名が解決できないとき、TryPrepare は false を返す（この修正だけが外れて、
        // 残りの MOD は動き続ける）。警告は最初の 1 回だけ。
        [Fact]
        public void UnresolvableTypeNamesFailPrepare()
        {
            Assert.False(NativeBossSoulShrineScanPerf.TryPrepare("Shrine_BossSoul_NotInThisGame", "ShrineBossSoulBoss"));
            Assert.Single(Log.Warnings);
            Assert.False(NativeBossSoulShrineScanPerf.TryPrepare("Shrine_BossSoul", "ShrineBossSoulBoss_NotInThisGame"));
            Assert.Single(Log.Warnings); // 警告は 1 回だけ（2 回目では増えない）
        }
    }
}
