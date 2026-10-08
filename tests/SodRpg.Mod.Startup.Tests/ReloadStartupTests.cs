using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // 報告「再読み込み後の2回目の起動で Infinity patch was not installed が全クラス分出る」の再現。
    // ゲームは MOD の DLL を同じアセンブリ名の別インスタンスとして再読み込みする。2回目の起動を
    // 同じバイト列の再読み込みで再現し、初回と同じく全 Infinity クラスが導入されることを確認する。
    public sealed class ReloadStartupTests : IDisposable
    {
        private const string OwnerId = "reload-test-owner";
        private readonly Harmony first = new Harmony(OwnerId);

        public ReloadStartupTests()
        {
            NativeContractPatch.Reset();
            PerformanceTuner.Starts = 0;
            PerformanceTuner.FailStart = false;
            Log.Errors.Clear();
            Log.Warnings.Clear();
            Log.Infos.Clear();
            NetworkedManagerBase<GameManager>.softInstance = null;
            ResetInfinity();
        }

        [Fact]
        public void SecondStartupAfterInGameReloadInstallsEveryInfinityClass()
        {
            // 1回目：通常の起動。全パッチが入り、Infinity は有効のまま。
            var firstMod = new DreamforgeMod { harmony = first };
            Invoke(firstMod, "Awake");
            Assert.True(InfinityMode.Available, string.Join(" | ", Log.Warnings));
            int firstInstalled = InstalledClassCountOfOriginal();
            Assert.True(firstInstalled > 0);
            Invoke(firstMod, "OnDestroy");
            Assert.Empty(first.GetPatchedMethods());

            // 再読み込み：ゲームと同じく、同じ DLL バイト列をもう1つアセンブリとして読み込む。
            // 2つのコピーは同じ ModuleVersionId を持つため、Harmony の保存済み PatchInfo から
            // PatchMethod を遅延解決すると先に読み込んだ古い方の型が返る。
            var reloaded = Assembly.Load(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location));
            var reloadedModType = reloaded.GetType("SodRpg.Mod.DreamforgeMod", throwOnError: true);
            var reloadedLogType = reloaded.GetType("SodRpg.Mod.Log", throwOnError: true);
            var reloadedInfinityType = reloaded.GetType("SodRpg.Mod.InfinityMode", throwOnError: true);
            var reloadedMod = Activator.CreateInstance(reloadedModType);
            var harmonyField = reloadedModType.GetField(nameof(ModBehaviour.harmony), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            harmonyField.SetValue(reloadedMod, new Harmony(OwnerId));
            AccessTools.Method(reloadedModType, "Awake").Invoke(reloadedMod, null);

            // 2回目も全 Infinity クラスが導入され、機能は有効のまま。
            var reloadedWarnings = (List<string>)reloadedLogType.GetField("Warnings").GetValue(null);
            Assert.DoesNotContain(reloadedWarnings, m => m.Contains("Infinity patch was not installed"));
            Assert.DoesNotContain(reloadedWarnings, m => m.Contains("Patch class skipped"));
            Assert.DoesNotContain(reloadedWarnings, m => m.Contains("Patch class rollback failed"));
            var available = (bool)reloadedInfinityType.GetProperty("Available", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Assert.True(available, string.Join(" | ", reloadedWarnings));
            Assert.Equal(firstInstalled, InstalledClassCount(reloadedLogType));

            AccessTools.Method(reloadedModType, "OnDestroy").Invoke(reloadedMod, null);
        }

        [Fact]
        public void HasInstalledClassStillReportsFalseForAClassThatGenuinelyFailed()
        {
            // 安全弁：導入できていないクラスを「導入済み」と誤報しないこと（1件の失敗は従来どおり Infinity を止める）。
            var mod = new DreamforgeMod { harmony = first };
            Invoke(mod, "Awake");
            try
            {
                var hasInstalled = typeof(DreamforgeMod).GetMethod("HasInstalledClass", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.True((bool)hasInstalled.Invoke(mod, new object[] { typeof(InfinityMapRefresh) }));
                var rollback = typeof(DreamforgeMod).GetMethod("RollBackClass", BindingFlags.NonPublic | BindingFlags.Instance);
                rollback.Invoke(mod, new object[] { typeof(InfinityMapRefresh) });
                Assert.False((bool)hasInstalled.Invoke(mod, new object[] { typeof(InfinityMapRefresh) }));
            }
            finally
            {
                Invoke(mod, "OnDestroy");
            }
        }

        private static int InstalledClassCountOfOriginal()
        {
            var line = Log.Infos.First(m => m.StartsWith("Patches installed: "));
            return int.Parse(line.Substring("Patches installed: ".Length).Split(' ')[0]);
        }

        static int InstalledClassCount(Type reloadedLogType)
        {
            var infos = (List<string>)reloadedLogType.GetField("Infos").GetValue(null);
            var line = infos.First(m => m.StartsWith("Patches installed: "));
            return int.Parse(line.Substring("Patches installed: ".Length).Split(' ')[0]);
        }


        private static void Invoke(DreamforgeMod mod, string name)
            => AccessTools.Method(typeof(DreamforgeMod), name).Invoke(mod, null);

        private static void ResetInfinity()
        {
            var type = typeof(InfinityMode);
            foreach (var name in new[] { "_unavailable", "_permanentlyUnavailable", "_gameSeenSinceDisable", "_restoring", "_newInfinity", "_refresh", "_lastDisableLog",
                "_initial", "_runId", "_choice", "_choiceText" })
                type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
            type.GetProperty("UnavailableReason", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                !.GetSetMethod(true)!.Invoke(null, new object[] { null });
            ((HashSet<string>)type.GetField("DisableReasons", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Clear();
            type.GetProperty("LobbyStartGuardMissing", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                !.GetSetMethod(true)!.Invoke(null, new object[] { false });
            ((System.Collections.IDictionary)type.GetField("PresentationFailures", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Clear();
            type.GetProperty("Available", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        }

        public void Dispose()
        {
            NativeContractPatch.FailInstallationCleanup = false;
            ResetInfinity();
            foreach (var target in first.GetPatchedMethods().ToArray())
                first.Unpatch(target, HarmonyPatchType.All, first.Id);
            NativeContractPatch.Reset();
        }
    }
}
