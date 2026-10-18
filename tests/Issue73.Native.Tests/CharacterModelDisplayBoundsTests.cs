using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#770）NativeCharacterModelDisplayBounds の検証。CharacterModelDisplay の境界は
    /// 本体 Dew.Core の逆コンパイル（2026-10-18 確認）と同じ式で LogicUpdate の bounds
    /// 走査・Setup のリスト埋め直し・2つのゲッターを書いたもの（検証対象の実ソースは
    /// csproj でそのままリンク）。確かめるのは: 再計算が本体の走査と同じ結果になること、
    /// 1/30秒の間引き、設定解除・失敗時にリストが本体へ戻ること、Setup の埋め直しを
    /// 次の Tick で取り込むこと、ゲッターの Prefix が返り値を書き換えないこと。
    /// </summary>
    public sealed class CharacterModelDisplayBoundsTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly AccessTools.FieldRef<CharacterModelDisplay, List<Renderer>> RenderersField =
            AccessTools.FieldRefAccess<CharacterModelDisplay, List<Renderer>>("_currentRenderers");
        private static readonly AccessTools.FieldRef<CharacterModelDisplay, Bounds> BoundsField =
            AccessTools.FieldRefAccess<CharacterModelDisplay, Bounds>("_bounds");

        private static Renderer Box(float cx, float cy, float cz, float sx, float sy, float sz)
            => new Renderer { bounds = new Bounds(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz)) };

        // テストごとに機能の静的状態を初期化する（States はインスタンスごとに別れる）。
        private static void ResetFeature()
        {
            var feature = typeof(NativeCharacterModelDisplayBounds);
            feature.GetField("_prepared", Hidden).SetValue(null, false);
            feature.GetField("_disabled", Hidden).SetValue(null, false);
            feature.GetField("_warned", Hidden).SetValue(null, false);
            Assert.True(NativeCharacterModelDisplayBounds.Prepare());
        }

        private static void CenterPrefix(CharacterModelDisplay display)
            => typeof(NativeCharacterModelDisplayBoundsCenter).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { display });

        private static void AbovePrefix(CharacterModelDisplay display)
            => typeof(NativeCharacterModelDisplayBoundsAbove).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { display });

        [Fact]
        public void RefreshRecomputesTheSameBoundsAsTheNativeWalk()
        {
            ResetFeature();
            Time.timeAsDouble = 100.0;
            var patchPath = new CharacterModelDisplay();
            var nativePath = new CharacterModelDisplay();
            List<Renderer> renderers = new List<Renderer> { Box(0, 1, 0, 1, 2, 1), Box(3, 4, -2, 2, 2, 2) };
            RenderersField(patchPath).AddRange(renderers);
            RenderersField(nativePath).AddRange(renderers);

            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(patchPath); // リストを預かる
            Assert.Empty(RenderersField(patchPath)); // 本体の走査は空リストで飛ばされる
            nativePath.LogicUpdate(1f / 30f); // 本体の30Hz走査（境界は逆コンパイルと同じ式）

            NativeCharacterModelDisplayBounds.Refresh(patchPath); // 読み駆動の再計算

            Assert.Equal(BoundsField(nativePath), BoundsField(patchPath)); // 本体と同じ結果
            Assert.NotEqual(renderers[0].bounds, BoundsField(patchPath)); // 全員を囲んでいる
        }

        [Fact]
        public void RefreshKeepsTheStoredBoundsWhenTheWalkHasNothingToEncapsulate()
        {
            ResetFeature();
            Time.timeAsDouble = 100.0;
            var adopted = new CharacterModelDisplay();
            RenderersField(adopted).AddRange(new List<Renderer> { null, null }); // 破棄済みのモデル
            var neverAdopted = new CharacterModelDisplay();
            Bounds sentinel = new Bounds(new Vector3(7, 8, 9), new Vector3(1, 1, 1));
            BoundsField(adopted) = sentinel;
            BoundsField(neverAdopted) = sentinel;

            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(adopted);
            Time.timeAsDouble += 1.0;
            NativeCharacterModelDisplayBounds.Refresh(adopted);
            NativeCharacterModelDisplayBounds.Refresh(neverAdopted); // 預かっていない表示

            // 対象がなければ _bounds を更新しない（本体は空リストで走査を飛ばすのと同じ）
            Assert.Equal(sentinel, BoundsField(adopted));
            Assert.Equal(sentinel, BoundsField(neverAdopted));
        }

        [Fact]
        public void RefreshIsThrottledToTheNativeLogicTickInterval()
        {
            ResetFeature();
            // 本体 LogicUpdateManager.DoLogicUpdates の間引き閾値（逆コンパイルで確認した定数）。
            Assert.Equal(0.03333333507180214, NativeCharacterModelDisplayBounds.LogicTickInterval);

            Time.timeAsDouble = 200.0;
            var display = new CharacterModelDisplay();
            Renderer moved = Box(0, 1, 0, 1, 2, 1);
            RenderersField(display).Add(moved);
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display);

            NativeCharacterModelDisplayBounds.Refresh(display); // 預かった直後の最初の読みは再計算する
            Bounds first = BoundsField(display);
            Assert.Equal(moved.bounds, first);

            moved.bounds = new Bounds(new Vector3(50, 1, 0), new Vector3(1, 2, 1)); // 動いた
            Time.timeAsDouble += 0.01; // 間隔内
            NativeCharacterModelDisplayBounds.Refresh(display);
            Assert.Equal(first, BoundsField(display)); // 間引き: 更新しない

            Time.timeAsDouble += 0.024; // 計 0.034 ≥ 1/30
            NativeCharacterModelDisplayBounds.Refresh(display);
            Assert.Equal(moved.bounds, BoundsField(display)); // 間隔を過ぎたら再計算する
        }

        [Fact]
        public void TheRefilledListIsAdoptedOnTheNextLogicTick()
        {
            ResetFeature();
            Time.timeAsDouble = 300.0;
            var display = new CharacterModelDisplay();
            Renderer old = Box(0, 1, 0, 1, 2, 1);
            RenderersField(display).Add(old);
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display);
            NativeCharacterModelDisplayBounds.Refresh(display);
            Assert.Equal(old.bounds, BoundsField(display));

            // Setup は現在フィールドが指す（差し替え後の空）リストを埋め直す
            Renderer fresh = Box(10, 20, 30, 2, 2, 2);
            display.NextSetupRenderers = new List<Renderer> { fresh };
            display.Setup("skin2");

            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display); // 次のTick: 新しいリストを取り込む
            Time.timeAsDouble += 1.0;
            NativeCharacterModelDisplayBounds.Refresh(display);
            Assert.Equal(fresh.bounds, BoundsField(display)); // 新しいリストから計算する
            Assert.Empty(RenderersField(display)); // フィールドは再び空
        }

        [Fact]
        public void TurningTheOptionOffReturnsTheParkedListToTheNativeWalk()
        {
            ResetFeature();
            Time.timeAsDouble = 400.0;
            var display = new CharacterModelDisplay();
            List<Renderer> original = RenderersField(display); // フィールドが最初から指しているリスト
            original.Add(Box(0, 1, 0, 1, 2, 1));
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display);
            Assert.Empty(RenderersField(display));

            var saved = NativePerfOptions.CharacterModelDisplayBounds;
            NativePerfOptions.CharacterModelDisplayBounds = false;
            try
            {
                NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display); // 設定解除の次のTick
                Assert.Same(original, RenderersField(display)); // 元の30Hz走査が見るリストへ戻る
                Bounds before = BoundsField(display);
                original[0].bounds = new Bounds(new Vector3(9, 9, 9), new Vector3(1, 1, 1));
                Time.timeAsDouble += 1.0;
                NativeCharacterModelDisplayBounds.Refresh(display);
                Assert.Equal(before, BoundsField(display)); // 設定OFFでは _bounds を更新しない
            }
            finally { NativePerfOptions.CharacterModelDisplayBounds = saved; }
        }

        [Fact]
        public void TheDisabledFallbackReturnsTheListToTheNativeWalk()
        {
            ResetFeature();
            Time.timeAsDouble = 500.0;
            var display = new CharacterModelDisplay();
            List<Renderer> original = RenderersField(display); // フィールドが最初から指しているリスト
            original.Add(Box(0, 1, 0, 1, 2, 1));
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display);

            typeof(NativeCharacterModelDisplayBounds).GetField("_disabled", Hidden).SetValue(null, true);
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display); // 失敗後の次のTick
            Assert.Same(original, RenderersField(display)); // 本体へ戻る
            Bounds before = BoundsField(display);
            original[0].bounds = new Bounds(new Vector3(9, 9, 9), new Vector3(1, 1, 1));
            Time.timeAsDouble += 1.0;
            NativeCharacterModelDisplayBounds.Refresh(display);
            Assert.Equal(before, BoundsField(display)); // 無効化中は _bounds を更新しない
        }

        [Fact]
        public void AListRefilledBySetupIsNotOverwrittenWhenTheFeatureTurnsOff()
        {
            ResetFeature();
            Time.timeAsDouble = 600.0;
            var display = new CharacterModelDisplay();
            Renderer old = Box(0, 1, 0, 1, 2, 1);
            RenderersField(display).Add(old);
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display);

            Renderer fresh = Box(10, 20, 30, 2, 2, 2);
            display.NextSetupRenderers = new List<Renderer> { fresh };
            display.Setup("skin2"); // 埋め直し済み
            List<Renderer> refilled = RenderersField(display);

            var saved = NativePerfOptions.CharacterModelDisplayBounds;
            NativePerfOptions.CharacterModelDisplayBounds = false;
            try
            {
                NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display);
                Assert.Same(refilled, RenderersField(display)); // 古いスナップショットで上書きしない
                Bounds before = BoundsField(display);
                fresh.bounds = new Bounds(new Vector3(-10, 0, 0), new Vector3(1, 1, 1));
                Time.timeAsDouble += 1.0;
                NativeCharacterModelDisplayBounds.Refresh(display);
                Assert.Equal(before, BoundsField(display)); // スナップショットは捨てられている
            }
            finally { NativePerfOptions.CharacterModelDisplayBounds = saved; }
        }

        [Fact]
        public void GetterPrefixesLeaveTheResultToTheNativeExpressions()
        {
            ResetFeature();
            Time.timeAsDouble = 700.0;
            var display = new CharacterModelDisplay();
            display.transform.position = new Vector3(11, 0, -13);
            Renderer body = Box(0, 2, 0, 2, 4, 2); // center (0,2,0)、extents (1,2,1)
            RenderersField(display).Add(body);
            NativeCharacterModelDisplayBounds.BeforeLogicUpdate(display);

            // 本体の30Hz走査と同じ入力からの比較対象
            var native = new CharacterModelDisplay();
            native.transform.position = new Vector3(11, 0, -13);
            RenderersField(native).Add(body);
            native.LogicUpdate(1f / 30f);

            AbovePrefix(display);
            CenterPrefix(display);
            Vector3 above = display.GetAbovePosition();
            Vector3 center = display.GetCenterPosition();
            Vector3 nativeAbove = native.GetAbovePosition();
            Vector3 nativeCenter = native.GetCenterPosition();
            Assert.Equal(nativeAbove.x, above.x); // パッチ経路も本体の式の結果そのもの
            Assert.Equal(nativeAbove.y, above.y);
            Assert.Equal(nativeAbove.z, above.z);
            Assert.Equal(nativeCenter.x, center.x);
            Assert.Equal(nativeCenter.y, center.y);
            Assert.Equal(nativeCenter.z, center.z);
            // 本体 GetAbovePosition の式: center + up * extents.y、x/z は transform.position
            Assert.Equal(4f, above.y);
            Assert.Equal(11f, above.x);
            Assert.Equal(-13f, above.z);
            Assert.Equal(2f, center.y);

            // 間引き中: レンダラーが動いても Prefix は _bounds を作り直さないので、
            // 返り値は前回の _bounds から本体の式が計算した値のまま
            body.bounds = new Bounds(new Vector3(100, 100, 100), new Vector3(2, 2, 2));
            AbovePrefix(display);
            CenterPrefix(display);
            above = display.GetAbovePosition();
            center = display.GetCenterPosition();
            Assert.Equal(4f, above.y);
            Assert.Equal(2f, center.y);

            // 設定OFF: Prefix は何も更新しないので、返り値は完全に本体のまま
            var saved = NativePerfOptions.CharacterModelDisplayBounds;
            NativePerfOptions.CharacterModelDisplayBounds = false;
            try
            {
                Time.timeAsDouble += 1.0;
                AbovePrefix(display);
                CenterPrefix(display);
                above = display.GetAbovePosition();
                center = display.GetCenterPosition();
                Assert.Equal(4f, above.y);
                Assert.Equal(2f, center.y);
            }
            finally { NativePerfOptions.CharacterModelDisplayBounds = saved; }
        }
    }
}
