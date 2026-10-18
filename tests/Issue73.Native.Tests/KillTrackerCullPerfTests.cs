using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#798）撃破追跡の掃除走査を1秒に1回に間引く NativeKillTrackerCullPerf の検証。
    /// ValidateBody・Prefix は実ソースから抽出した本体そのものに、本体
    /// KillTracker.CullExpiredEntities と同じ命令並びの見本（KillTrackerCullPerfBoundary）を
    /// PatchProcessor.GetOriginalInstructions で読んで渡す。追跡辞書の読み出しと辞書の
    /// Remove 呼び出しを含む見本だけを受け付けること、同じ追跡器では1秒以内の2回目を
    /// 飛ばして1秒後と時刻の逆行では走査すること、設定 OFF の間は常に走査すること、
    /// 間引きで期限切れ項目が辞書に残っていても撃破の報告が変わらないことを確かめる。
    /// </summary>
    public sealed class KillTrackerCullPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static IList<CodeInstruction> InstructionsOf(string method)
        {
            var target = typeof(KillTracker).GetMethod(method, Hidden);
            Assert.NotNull(target);
            return PatchProcessor.GetOriginalInstructions(target);
        }

        private static void ResetPatchState()
        {
            typeof(NativeKillTrackerCullPerf).GetField("_warned", Hidden).SetValue(null, false);
            typeof(NativeKillTrackerCullPerf).GetField("_disabled", Hidden).SetValue(null, false);
        }

        private static bool Warned()
            => (bool)typeof(NativeKillTrackerCullPerf).GetField("_warned", Hidden).GetValue(null);

        private static bool RunPrefix(KillTracker tracker)
            => (bool)typeof(NativeKillTrackerCullPerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { tracker });

        [Fact]
        public void ValidateBodyAcceptsTheNativeCullLoopShape()
        {
            ResetPatchState();
            Assert.True(NativeKillTrackerCullPerf.ValidateBody(
                InstructionsOf(nameof(KillTracker.CullExpiredEntitiesNativeLike))));
            Assert.False(Warned()); // 本体と同じ形なら警告しない
        }

        [Fact]
        public void ValidateBodyRejectsAShapeWithoutDictionaryRemove()
        {
            ResetPatchState();
            Assert.False(NativeKillTrackerCullPerf.ValidateBody(
                InstructionsOf(nameof(KillTracker.CullExpiredEntitiesWithoutRemove))));
            Assert.True(Warned()); // 形が壊れていれば警告を1回出す
        }

        [Fact]
        public void ValidateBodyRejectsAShapeThatNeverReadsTheTrackingDict()
        {
            ResetPatchState();
            Assert.False(NativeKillTrackerCullPerf.ValidateBody(
                InstructionsOf(nameof(KillTracker.CullExpiredEntitiesOnForeignDictionary))));
        }

        [Fact]
        public void PrefixSkipsTheSecondScanOfTheSameTrackerWithinASecond()
        {
            ResetPatchState();
            var saved = Time.time;
            try
            {
                Time.time = 100f;
                var tracker = new KillTracker();
                Assert.True(RunPrefix(tracker)); // 初回の走査はする
                Time.time = 100.5f;
                Assert.False(RunPrefix(tracker)); // 同じ追跡器の1秒以内の2回目は飛ばす
                var other = new KillTracker();
                Assert.True(RunPrefix(other)); // 最終走査時刻は追跡器ごとに数える
                Assert.False(RunPrefix(other));
            }
            finally { Time.time = saved; }
        }

        [Fact]
        public void PrefixScansAgainAfterASecondAndWhenTimeGoesBackwards()
        {
            ResetPatchState();
            var saved = Time.time;
            try
            {
                Time.time = 100f;
                var tracker = new KillTracker();
                Assert.True(RunPrefix(tracker));
                Time.time = 100.9f;
                Assert.False(RunPrefix(tracker)); // まだ1秒経っていない
                Time.time = 101.5f;
                Assert.True(RunPrefix(tracker)); // 1秒経てば再び走査する
                Time.time = 101.9f;
                Assert.False(RunPrefix(tracker));
                Time.time = 50f; // 時刻の逆行（場面の再読み込み直後など）
                Assert.True(RunPrefix(tracker)); // 逆行時も走査を続ける
            }
            finally { Time.time = saved; }
        }

        [Fact]
        public void PrefixAlwaysScansWhileTheSettingIsOff()
        {
            ResetPatchState();
            var savedOption = NativePerfOptions.KillTrackerCull;
            var savedTime = Time.time;
            NativePerfOptions.KillTrackerCull = false;
            try
            {
                Time.time = 100f;
                var tracker = new KillTracker();
                Assert.True(RunPrefix(tracker));
                Time.time = 100.05f;
                Assert.True(RunPrefix(tracker)); // 設定 OFF の間は間引かない
                Assert.True(RunPrefix(tracker));
            }
            finally
            {
                NativePerfOptions.KillTrackerCull = savedOption;
                Time.time = savedTime;
            }
        }

        // 間引きの等価性の根拠: 本体 ReportKill（逆コンパイル、2026-10-19 確認）は TryGetValue の
        // 値で猶予を自分で再判定し、期限切れなら何もしない。そのため掃除が飛んで期限切れ項目が
        // _trackingDict に残っていても、報告の有無は変わらない。実物の DLL はこの環境で動かせない
        // ため、本体から写した ReportKillNativeLike の振る舞いで確かめる。
        [Fact]
        public void ReportKillDoesNotReportAnEntryThatExpiredWhileTheCullWasSkipped()
        {
            var tracker = new KillTracker { _gracePeriod = 8f };
            var victim = new Entity();
            int reports = 0;
            tracker._callback = _ => reports++;
            var saved = Time.time;
            try
            {
                Time.time = 100f;
                tracker._trackingDict[victim] = 100f; // 100秒時にダメージを与えた記録
                Time.time = 109.5f; // 猶予8秒を過ぎた。掃除は間引きで飛んだものとする
                tracker.ReportKillNativeLike(new EventInfoKill { victim = victim });
                Assert.Equal(0, reports); // 期限切れ項目は報告しない
                Assert.True(tracker._trackingDict.ContainsKey(victim)); // 項目も残る

                Time.time = 110f; // 対照: 猶予内の項目は報告して辞書から除く
                tracker._trackingDict[victim] = 109.5f;
                tracker.ReportKillNativeLike(new EventInfoKill { victim = victim });
                Assert.Equal(1, reports);
                Assert.False(tracker._trackingDict.ContainsKey(victim));
            }
            finally { Time.time = saved; }
        }

        // 飛ばされた掃除がするのは期限切れ・破壊済み項目の除去だけ。猶予内の項目は掃除しても
        // 残るため、間引きで失われる情報はなく、辞書は高々猶予＋1秒分の項目を保つ。
        [Fact]
        public void ThePostponedCullRemovesOnlyExpiredEntries()
        {
            var tracker = new KillTracker { _gracePeriod = 8f };
            var expired = new Entity();
            var fresh = new Entity();
            var saved = Time.time;
            try
            {
                Time.time = 100f;
                tracker._trackingDict[expired] = 100f;
                tracker._trackingDict[fresh] = 108f;
                Time.time = 108.5f; // expired は猶予切れ、fresh は猶予内
                tracker.CullExpiredEntitiesNativeLike(); // 飛んでいた掃除をここで実行
                Assert.False(tracker._trackingDict.ContainsKey(expired)); // 期限切れだけ除く
                Assert.True(tracker._trackingDict.ContainsKey(fresh)); // 猶予内は残す
            }
            finally { Time.time = saved; }
        }
    }
}
