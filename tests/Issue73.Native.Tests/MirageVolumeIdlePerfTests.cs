using System;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1021）ミラージュスキンの妄想デバフでのボリューム再適用省略（NativeMirageVolumeIdle）
    /// の検証。ApplyVolume の判定を、UpdateVolume の呼び出し回数を数える模倣の FxVolume で
    /// 直接動かす。初回は呼ぶこと、currentValue・targetStrength・_contextMultiplier の3値が
    /// 前回と同じなら2回目以降を省くこと、3値のどれか1つでも変われば呼ぶこと、
    /// _volume が null なら毎回呼ぶこと、設定 OFF のときは毎回呼ぶことを確かめる。
    /// </summary>
    public sealed class MirageVolumeIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 模倣の FxVolume を、本体と同じ初期値（targetStrength=1・_contextMultiplier=1）と
        // 非 null の _volume で作る。currentValue は呼び出し側で0以外に整える。
        private static FxVolume NewVolume()
        {
            var volume = new FxVolume();
            typeof(FxVolume).GetField("_volume", Hidden).SetValue(volume, new UnityEngine.Rendering.Volume());
            return volume;
        }

        private static void SetContextMultiplier(FxVolume volume, float value)
            => typeof(FxVolume).GetField("_contextMultiplier", Hidden).SetValue(volume, value);

        // パッチの静的欄を整える（FieldRef は本体では Prepare が用意する）とともに、
        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        // 適用記録（AppliedStates）は実体ごとなので、毎回新しい FxVolume で初期状態から始める。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeMirageVolumeIdle);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            var volumeRef = type.GetField("_volume", Hidden);
            var contextRef = type.GetField("_contextMultiplier", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            Assert.NotNull(volumeRef);
            Assert.NotNull(contextRef);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.MirageVolumeIdle;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            if (volumeRef.GetValue(null) == null)
                volumeRef.SetValue(null, AccessTools.FieldRefAccess<FxVolume, UnityEngine.Rendering.Volume>("_volume"));
            if (contextRef.GetValue(null) == null)
                contextRef.SetValue(null, AccessTools.FieldRefAccess<FxVolume, float>("_contextMultiplier"));
            NativePerfOptions.MirageVolumeIdle = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.MirageVolumeIdle = savedOption;
            }
        }

        [Fact]
        public void ApplyVolumeCallsUpdateVolumeOnFirstApply()
        {
            WithPatch(true, () =>
            {
                var volume = NewVolume();
                volume.currentValue = 0.25f;
                // 初回は適用記録が空（0,0,0）で実値と一致しないため呼ぶ。
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(1, volume.updateVolumeCalls);
            });
        }

        [Fact]
        public void ApplyVolumeSkipsWhenAllThreeInputsAreUnchanged()
        {
            WithPatch(true, () =>
            {
                var volume = NewVolume();
                volume.currentValue = 0.25f;
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(1, volume.updateVolumeCalls);
                // 3値（currentValue・targetStrength・_contextMultiplier）が同じ: 2回目以降は省く。
                NativeMirageVolumeIdle.ApplyVolume(volume);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(1, volume.updateVolumeCalls);
            });
        }

        [Fact]
        public void ApplyVolumeCallsWhenAnyOfTheThreeInputsChanges()
        {
            WithPatch(true, () =>
            {
                var volume = NewVolume();
                volume.currentValue = 0.25f;
                NativeMirageVolumeIdle.ApplyVolume(volume);
                int calls = volume.updateVolumeCalls;

                // currentValue が変わる: 呼ぶ。その後同じ値が続くなら省く。
                volume.currentValue = 0.5f;
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(calls + 1, volume.updateVolumeCalls);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(calls + 1, volume.updateVolumeCalls);

                // targetStrength が変わる: 呼ぶ。その後同じ値が続くなら省く。
                volume.targetStrength = 0.75f;
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(calls + 2, volume.updateVolumeCalls);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(calls + 2, volume.updateVolumeCalls);

                // _contextMultiplier が変わる: 呼ぶ。その後同じ値が続くなら省く。
                SetContextMultiplier(volume, 0.5f);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(calls + 3, volume.updateVolumeCalls);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                Assert.Equal(calls + 3, volume.updateVolumeCalls);
            });
        }

        [Fact]
        public void ApplyVolumeAlwaysCallsWhileVolumeComponentIsMissing()
        {
            WithPatch(true, () =>
            {
                var volume = new FxVolume(); // GetComponent 前の状態: _volume は null のまま。
                volume.currentValue = 0.25f;
                NativeMirageVolumeIdle.ApplyVolume(volume);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                // _volume が null なら3値が同じでも毎回呼ぶ（初期化前の Volume に書き漏らさない）。
                Assert.Equal(3, volume.updateVolumeCalls);
            });
        }

        [Fact]
        public void ApplyVolumeAlwaysCallsWhenOptionOff()
        {
            WithPatch(false, () =>
            {
                var volume = NewVolume();
                volume.currentValue = 0.25f;
                NativeMirageVolumeIdle.ApplyVolume(volume);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                NativeMirageVolumeIdle.ApplyVolume(volume);
                // 設定 OFF（nativeMirageVolumeIdle=false）: 3値が静止していても毎回呼ぶ。
                Assert.Equal(3, volume.updateVolumeCalls);
            });
        }
    }
}
