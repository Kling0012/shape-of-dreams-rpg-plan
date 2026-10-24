using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1144）途中参加の待機中に「空 → 空」のロードアウト代入だけを飛ばす
    /// NativeMidJoinLoadoutIdlePerf の検証。境界（NativeBoundary.cs）の DewPlayer には
    /// 実機と同じ形の weaver 生成セッターの模倣
    /// （Network_003CselectedLoadout_003Ek__BackingField。クラス既定の参照等値で比べ、
    /// 参照が変わるときだけ字段へ代入し書いた回数を数える）があるため、Prepare は通り、
    /// パッチを有効なまま確かめられる: (1) 空の現在値への新しい空の代入は Prefix が
    /// false を返してセッターまで届かない（字段も書いた回数も変わらない。何度繰り返しても
    /// 同じ。星のリストが null の「空」も両側で同じ扱い）、(2) 内容の変わる代入
    /// （空 → 非空・非空 → 空・null が絡む両方向）はすべて本体のセッターに通る、
    /// (3) 設定が無効のときは「空 → 空」も通る、(4) Prepare の字段照合
    /// （HeroLoadoutData の公開字段が空判定の8項目と数・名前で一致するか）は実機と同じ
    /// 字段構成で通り、字段が増えた・名前が変わった・減った型では false を返す
    /// （ゲーム更新で字段が増えたとき、警告1回で無効になる側）。
    /// </summary>
    public sealed class MidJoinLoadoutIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // パッチ側の静的な欄（無効化・警告）を毎テストで初期化し、Prepare を境界の模倣
        // セッターに対して通す（実機で Harmony が呼ぶのと同じ準備。weaver セッターの解決と
        // 字段照合の両方がここで走る）。Dispose で設定と警告を既定へ戻す。Harmony の
        // 当て外しは各テストの try/finally で完結する（このアセンブリはテストの並列走行を
        // 止めてあるので、他のテストへの影響はない）。
        public MidJoinLoadoutIdlePerfTests()
        {
            ResetPatchState();
            NativePerfOptions.MidJoinLoadoutIdle = true;
            Log.Warnings.Clear();
            Assert.True((bool)typeof(NativeMidJoinLoadoutIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.MidJoinLoadoutIdle = true;
            Log.Warnings.Clear();
        }

        private static void ResetPatchState()
        {
            var type = typeof(NativeMidJoinLoadoutIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
        }

        // パッチそのもの（Prefix）を実機と同じ対象（Prepare が解決したセッター）に当てる。
        private static Harmony PatchedSetter()
        {
            var setter = (MethodInfo)typeof(NativeMidJoinLoadoutIdlePerf).GetMethod("TargetMethod", Hidden)
                .Invoke(null, null);
            var harmony = new Harmony("issue73.midjoin." + Guid.NewGuid().ToString("N"));
            harmony.Patch(setter, prefix: new HarmonyMethod(typeof(NativeMidJoinLoadoutIdlePerf).GetMethod("Prefix", Hidden)));
            return harmony;
        }

        // 星のリスト4つすべてが null のロードアウト（IsEmpty は null と長さ0を同じ空と見る）。
        private static HeroLoadoutData NullListsLoadout()
            => new HeroLoadoutData { cDestruction = null, cLife = null, cImagination = null, cFlexible = null };

        // (1) 待機の毎フレームと同じく、空の現在値へ新しい空を代入し直しても Prefix が
        // false を返してセッターまで届かない。字段の参照も書いた回数も変わらず、警告も出ない。
        [Fact]
        public void ThePrefixSkipsTheEmptyOverEmptyAssignmentEveryFrame()
        {
            var player = new DewPlayer();
            var before = player.selectedLoadout;
            var harmony = PatchedSetter();
            try
            {
                Assert.Empty(Log.Warnings);
                for (int frame = 0; frame < 3; frame++)
                {
                    player.Network_003CselectedLoadout_003Ek__BackingField = new HeroLoadoutData();
                    Assert.Same(before, player.selectedLoadout);
                    Assert.Equal(0, player.selectedLoadoutWrites);
                }
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (1b) 星のリストが null の「空」も同じ空扱い。現在値が null リスト・入ってくる値が
        // null リストのどちらでも「空 → 空」として飛ばす（実機の CmdSetHeroLoadoutData_Imp が
        // null リストのロードアウトを届ける場面を含む）。
        [Fact]
        public void NullStarListsCountAsEmptyOnEitherSide()
        {
            var player = new DewPlayer { selectedLoadout = NullListsLoadout() };
            var before = player.selectedLoadout;
            var harmony = PatchedSetter();
            try
            {
                // 現在値が null リストの空 ← 新しい空: 飛ばす。
                player.Network_003CselectedLoadout_003Ek__BackingField = new HeroLoadoutData();
                Assert.Same(before, player.selectedLoadout);
                Assert.Equal(0, player.selectedLoadoutWrites);

                // 現在値が新しい空 ← null リストの空: 同じく飛ばす。
                player.selectedLoadout = new HeroLoadoutData();
                var settled = player.selectedLoadout;
                player.Network_003CselectedLoadout_003Ek__BackingField = NullListsLoadout();
                Assert.Same(settled, player.selectedLoadout);
                Assert.Equal(0, player.selectedLoadoutWrites);
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (2) 内容の変わる代入はすべて本体のセッターに通る。スキル int だけ・星リストだけの
        // 非空の両方、1回目の本当のクリア（非空 → 空）、null が絡む両方向（空 → null・
        // null → 空）のどれも字段が書き変わる。
        [Fact]
        public void EveryContentChangeReachesTheVanillaSetter()
        {
            var player = new DewPlayer();
            var harmony = PatchedSetter();
            try
            {
                Assert.Empty(Log.Warnings);

                // 空 → 非空（スキル int だけ）: 通って書く。
                var bySkill = new HeroLoadoutData { skillR = 7 };
                player.Network_003CselectedLoadout_003Ek__BackingField = bySkill;
                Assert.Same(bySkill, player.selectedLoadout);
                Assert.Equal(1, player.selectedLoadoutWrites);

                // 非空 → 非空（星リストだけの非空）: 通って書く。
                var byStars = new HeroLoadoutData
                {
                    cImagination = { new LoadoutStarItem { name = "Star", level = 2 } },
                };
                player.Network_003CselectedLoadout_003Ek__BackingField = byStars;
                Assert.Same(byStars, player.selectedLoadout);
                Assert.Equal(2, player.selectedLoadoutWrites);

                // 非空 → 空（1回目の本当のクリア）: 通る。
                player.Network_003CselectedLoadout_003Ek__BackingField = new HeroLoadoutData();
                Assert.Equal(3, player.selectedLoadoutWrites);
                Assert.NotNull(player.selectedLoadout);

                // 空 → null: 通る（null は空ではないので必ず本体に届く）。
                player.Network_003CselectedLoadout_003Ek__BackingField = null;
                Assert.Null(player.selectedLoadout);
                Assert.Equal(4, player.selectedLoadoutWrites);

                // null → 空: 通る。
                player.Network_003CselectedLoadout_003Ek__BackingField = new HeroLoadoutData();
                Assert.NotNull(player.selectedLoadout);
                Assert.Equal(5, player.selectedLoadoutWrites);
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (3) 設定が無効のときは「空 → 空」も含めてすべて本体に通る。参照の変わる代入は
        // 本体のセッターが書く（vanilla と同じ）。
        [Fact]
        public void ThePrefixHandsBackWhenTheConfigIsOff()
        {
            NativePerfOptions.MidJoinLoadoutIdle = false;
            var player = new DewPlayer();
            var harmony = PatchedSetter();
            try
            {
                var fresh = new HeroLoadoutData();
                player.Network_003CselectedLoadout_003Ek__BackingField = fresh;
                Assert.Same(fresh, player.selectedLoadout);
                Assert.Equal(1, player.selectedLoadoutWrites);
                Assert.Empty(Log.Warnings);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        // (4) 字段照合。実機と同じ8項目（境界の HeroLoadoutData）は通り、公開字段が
        // 増えた・名前が変わった（項目数は同じ）・減った型は false。ゲーム更新で
        // HeroLoadoutData に字段が追加されると Prepare はこの照合に失敗して警告1回で
        // パッチを無効にする（空判定が同期対象の一部だけを見る事態を防ぐ）。
        private sealed class LoadoutWithExtraField
        {
            public int skillQ, skillR, skillTrait, skillMovement;
            public List<LoadoutStarItem> cDestruction, cLife, cImagination, cFlexible;
            public int addedLater;
        }
        private sealed class LoadoutWithRenamedField
        {
            public int skillQ, skillR, skillTrait, skillMovement;
            public List<LoadoutStarItem> cDestruction, cLife, cStars, cFlexible;
        }
        private sealed class LoadoutMissingAField
        {
            public int skillQ, skillR, skillTrait;
            public List<LoadoutStarItem> cDestruction, cLife, cImagination, cFlexible;
        }

        [Fact]
        public void TheShapeGuardAcceptsTheVanillaFieldsAndRejectsAnyDrift()
        {
            Assert.True(NativeMidJoinLoadoutIdlePerf.LoadoutShapeMatches(typeof(HeroLoadoutData)));
            Assert.False(NativeMidJoinLoadoutIdlePerf.LoadoutShapeMatches(typeof(LoadoutWithExtraField)));
            Assert.False(NativeMidJoinLoadoutIdlePerf.LoadoutShapeMatches(typeof(LoadoutWithRenamedField)));
            Assert.False(NativeMidJoinLoadoutIdlePerf.LoadoutShapeMatches(typeof(LoadoutMissingAField)));
        }
    }
}
