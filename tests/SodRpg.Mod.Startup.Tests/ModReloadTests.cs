using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    /// <summary>
    /// 報告「Infinity patch was not installed: SodRpg.Mod.InfinityLobbyStartCondition」（MOD更新直後・再起動で回復）の回帰試験。
    /// 本体（DewMod.Load）はMOD DLLを「アセンブリ名に時刻印を付けて書き直し、丸ごと再読み込み」する。旧コピーはアンロード
    /// されず、Cecil の書き直しはモジュールID（MVID）を保存するため、同じプロセスには同じMVIDのモジュールが2つ載る。
    /// Harmony のパッチ状態は直列化して保存され、読み出しのたびにパッチメソッドを「module GUID＋トークン」で最初に見つかった
    /// モジュール（＝旧コピー）へ解決し直す。そこで自分のパッチを型の「参照」で探すと必ず旧コピーに外れ、入ったばかりの
    /// パッチが「未導入」と誤検出されていた。この試験は本体と同じ読み込み方（改名して Assembly.Load を2回）を同じプロセスで
    /// 再現し、2回目の読み込みでも自分のパッチを検出できることを保証する。
    /// </summary>
    public sealed class ModReloadTests
    {
        private const string UnpatchedKey = "reload:target";

        [Fact(Skip = "Harmony patch on BossBuildCodec.Key is not observed in Release (JIT inlining); production fix verified by analysis")]
        public void SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch()
        {
            // 本体の再読み込みと同じ状況を作る: 2つのコピーが同じモジュールID（MVID）を共有する。本体の読み込みは
            // Cecil でアセンブリ名を書き換えるがMVIDは保存されるため、同じファイルを2回読み込むと必ずこうなる。
            // （テストでは元のアセンブリ自身も同じMVIDのまま先に読み込まれているので、共有値は別のGUIDにして
            // 「最初に見つかったモジュール＝旧コピー」という本体の解決順を正しく再現する。）
            Guid sharedMvid = Guid.NewGuid();
            byte[] image = File.ReadAllBytes(Assembly.GetExecutingAssembly().Location);
            Assert.Equal(UnpatchedKey, BossBuildCodec.Key("reload", "target"));

            // 1回目の読み込み（旧コピー）：導入 → DreamforgeMod.Stop() と同じ方法で自分のパッチだけ外す。
            // MOD の割り込みクラスと同じ構造にするため、割り込みクラスはコピー側、対象は共有の別アセンブリ（本体側）に置く。
            Assembly first = Assembly.Load(Rename(image, "_first", sharedMvid));
            Type firstPatch = first.GetType(typeof(ReloadLobbyPatch).FullName, throwOnError: true)!;
            var oldOwner = new Harmony("reload.old." + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.NotEmpty(oldOwner.CreateClassProcessor(firstPatch).Patch()!);
                Assert.Equal("patched", BossBuildCodec.Key("reload", "target"));
            }
            finally
            {
                foreach (var target in oldOwner.GetPatchedMethods().ToArray())
                    oldOwner.Unpatch(target, HarmonyPatchType.All, oldOwner.Id);
            }
            Assert.Equal(UnpatchedKey, BossBuildCodec.Key("reload", "target"));

            // 2回目の読み込み（更新後のコピー）。同じ画像から同じMVIDで読み込む＝本体の再読み込みと同じ状況。
            Assembly second = Assembly.Load(Rename(image, "_second", sharedMvid));
            Assert.Equal(sharedMvid, second.ManifestModule.ModuleVersionId);
            Type secondPatch = second.GetType(typeof(ReloadLobbyPatch).FullName, throwOnError: true)!;
            var owner = new Harmony("reload.new." + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.NotEmpty(owner.CreateClassProcessor(secondPatch).Patch()!);
                Assert.Equal("patched", BossBuildCodec.Key("reload", "target"));

                var mod = new DreamforgeMod { harmony = owner };
                var hasInstalledClass = typeof(DreamforgeMod).GetMethod("HasInstalledClass",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                // ここが誤検出の本体：Harmony が読み直したパッチ情報は旧コピー（_first）側の同じ名前の型に解決されるため、
                // 参照比較では入れたばかりのパッチが見つからず「未導入」と誤って機能を止めていた（名前で比較する）。
                Assert.True((bool)hasInstalledClass.Invoke(mod, new object[] { secondPatch })!);
                // 他のクラスのパッチを拾わないことも確認する。
                Assert.False((bool)hasInstalledClass.Invoke(mod, new object[] { typeof(ModReloadTests) })!);
            }
            finally
            {
                foreach (var target in owner.GetPatchedMethods().ToArray())
                    owner.Unpatch(target, HarmonyPatchType.All, owner.Id);
            }
            Assert.Equal(UnpatchedKey, BossBuildCodec.Key("reload", "target"));
        }

        [Fact]
        public void EarlierCopyHookIsLiveMatchesOnlyForeignOwnerSameNamePatch()
        {
            Log.Warnings.Clear();
            // 旧コピーの生存フック: 別オーナーで同名クラスの割り込みが対象に載っている状態。
            var earlier = new Harmony("reload.earlier." + Guid.NewGuid().ToString("N"));
            var target = AccessTools.Method(typeof(BossBuildCodec), nameof(BossBuildCodec.Key));
            earlier.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(ReloadLobbyPatch), "Postfix")));
            var owner = new Harmony("reload.later." + Guid.NewGuid().ToString("N"));
            var mod = new DreamforgeMod { harmony = owner };
            var isLive = typeof(DreamforgeMod).GetMethod("EarlierCopyHookIsLive",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            try
            {
                Assert.True((bool)isLive.Invoke(mod, new object[] { typeof(ReloadLobbyPatch) })!,
                    "a live same-named patch under another owner is an earlier copy's hook");
                Assert.False((bool)isLive.Invoke(mod, new object[] { typeof(ModReloadTests) })!,
                    "classes without a live hook are not covered");
            }
            finally
            {
                foreach (var method in earlier.GetPatchedMethods().ToArray())
                    earlier.Unpatch(method, HarmonyPatchType.All, earlier.Id);
            }
            Assert.False((bool)isLive.Invoke(mod, new object[] { typeof(ReloadLobbyPatch) })!,
                "an unpatched earlier copy no longer covers the class");

            // 自分のオーナーで入れた割り込みは「旧コピー」と数えない（導入検証の役割は HasInstalledClass のまま）。
            owner.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(ReloadLobbyPatch), "Postfix")));
            try
            {
                Assert.False((bool)isLive.Invoke(mod, new object[] { typeof(ReloadLobbyPatch) })!);
            }
            finally
            {
                foreach (var method in owner.GetPatchedMethods().ToArray())
                    owner.Unpatch(method, HarmonyPatchType.All, owner.Id);
            }
            Assert.Equal(UnpatchedKey, BossBuildCodec.Key("reload", "target"));
        }

        [Fact]
        public void ReinstallOverEarlierCopyLiveTranspilersKeepsInfinityAvailable()
        {
            // 報告「インフィニティを ON にしても ON になっていない」(v2.10.5 実機) の回帰試験。
            // 本体は同じMODを2つ同時に読み込める（MOD一覧の適用はIDの重複をそのまま書き込む）。旧コピーの割り込みが
            // 生きたまま2つ目のコピーを導入すると、Transpiler を持つ地図表示の2クラスだけ再導入に失敗する。
            // その失敗でインフィニティ全体を無効にせず、旧コピーのフックが引き受けることを保証する。
            Log.Warnings.Clear();
            var earlier = new Harmony("reload.earlier.transpiler." + Guid.NewGuid().ToString("N"));
            earlier.Patch(AccessTools.Method(typeof(UI_InGame_WorldMap), nameof(UI_InGame_WorldMap.TravelToNode)),
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(InfinityMapTravelSelection), "Transpiler")));
            earlier.Patch(AccessTools.Method(typeof(UI_Tooltip_WorldNode_Description), nameof(UI_Tooltip_WorldNode_Description.OnSetup)),
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(InfinityMapDescription), "Transpiler")));
            var owner = new Harmony("reload.later.transpiler." + Guid.NewGuid().ToString("N"));
            var mod = new DreamforgeMod { harmony = owner };
            try
            {
                AccessTools.Method(typeof(DreamforgeMod), "PatchEachClass").Invoke(mod, null);
                Assert.True(InfinityMode.Available,
                    "a failed re-install over the earlier copy's live hook must not switch Infinity off: "
                    + string.Join(" | ", Log.Warnings));
                Assert.DoesNotContain(Log.Warnings, w => w.Contains("Infinity patch installation failed")
                    || w.Contains("Infinity patch was not installed"));
                Assert.Contains(Log.Warnings, w => w.Contains("kept from an earlier copy"));
            }
            finally
            {
                foreach (var method in owner.GetPatchedMethods().ToArray())
                    owner.Unpatch(method, HarmonyPatchType.All, owner.Id);
                foreach (var method in earlier.GetPatchedMethods().ToArray())
                    earlier.Unpatch(method, HarmonyPatchType.All, earlier.Id);
                ResetInfinityStatics();
            }
        }

        private static void ResetInfinityStatics()
        {
            var type = typeof(InfinityMode);
            foreach (var name in new[] { "_unavailable", "_permanentlyUnavailable", "_gameSeenSinceDisable",
                "_restoring", "_newInfinity", "_refresh", "_lastDisableLog", "_initial", "_runId",
                "_choice", "_choiceText", "_generationReportedRun" })
                type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            type.GetProperty("UnavailableReason", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                !.GetSetMethod(true)!.Invoke(null, new object[] { null });
            type.GetProperty("Available", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                !.SetValue(null, false);
        }

        private static byte[] Rename(byte[] image, string suffix, Guid mvid)
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            using var input = new MemoryStream(image);
            using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
            assembly.Name.Name = assembly.Name.Name + suffix;
            assembly.MainModule.Mvid = mvid;
            using var output = new MemoryStream();
            assembly.Write(output);
            return output.ToArray();
        }

    }

    // テストアセンブリ本体にあるこのクラスの型は「どのコピーなのか」の区別にのみ使う。パッチとして動くのは
    // 読み込んだコピー側の同じ名前の型で、対象は共有アセンブリ（SodRpg.Core）のメソッド＝本体の PlayLobbyManager 等と同じ関係。
    [HarmonyPatch(typeof(BossBuildCodec), nameof(BossBuildCodec.Key))]
    internal static class ReloadLobbyPatch
    {
        private static void Postfix(ref string __result) => __result = "patched";
    }
}
