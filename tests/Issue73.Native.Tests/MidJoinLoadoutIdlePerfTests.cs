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
    /// (3) 設定が無効のときは「空 → 空」も通る、(4) Prepare の空判定の組み立て
    /// （HeroLoadoutData のインスタンス字段（公開・非公開とも）全てから空判定を
    /// コンパイルする）は実機と同じ字段構成で通り、字段が増えた（非公開字段の追加を
    /// 含む）・名前が変わった・減った型でも同じ「全字段が既定値か」の判定を組み立て、
    /// 未知の字段が非既定なら空とは判定しない。
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

        // (4) 空判定の組み立て。Prepare は名前一覧との照合ではなく、型の全インスタンス字段
        // （公開・非公開とも）から空判定をコンパイルする。実機と同じ8項目の型は既定の new を
        // 空と判定し、字段が増えた（非公開字段の追加を含む）・名前が変わった・減った型でも
        // 同じ「全字段が既定値か」の判定が組み立てられ、未知の字段が非既定なら空から外れる
        // （ゲーム更新や他MODの追加字段があっても、飛ばしてよい代入の範囲は広がらない）。
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
        // 公開8項目はそのままに非公開字段が1つ増えただけの型（#1144 レビュー F1）。
        // Mirror の weaver は非公開字段への [SyncVar] も同期対象にできるため、空判定は
        // 非公開字段も実行時に見る。追加字段が既定値でなければ空とは判定されない。
        private sealed class LoadoutWithHiddenSyncVar
        {
            public int skillQ, skillR, skillTrait, skillMovement;
            public List<LoadoutStarItem> cDestruction, cLife, cImagination, cFlexible;
            internal int syncedQuietly;
        }

        [Fact]
        public void TheEmptyCheckCoversEveryInstanceFieldWhateverItsShape()
        {
            // 境界（実機と同じ8項目）: 既定の new は空。null は空ではない。
            var vanilla = NativeMidJoinLoadoutIdlePerf.BuildEmptyCheck(typeof(HeroLoadoutData));
            Assert.True(vanilla(new HeroLoadoutData()));
            Assert.False(vanilla(null));
            Assert.False(vanilla(new HeroLoadoutData { skillQ = 1 }));
            Assert.True(vanilla(NullListsLoadout())); // 星のリストが null の空も同じ扱い

            // 字段が増えた型: 追加字段が既定の間は空、非既定なら空でない。
            var extra = NativeMidJoinLoadoutIdlePerf.BuildEmptyCheck(typeof(LoadoutWithExtraField));
            Assert.True(extra(new LoadoutWithExtraField()));
            Assert.False(extra(new LoadoutWithExtraField { addedLater = 3 }));

            // 非公開字段（weaver が同期対象にできる）も見る。
            var hidden = NativeMidJoinLoadoutIdlePerf.BuildEmptyCheck(typeof(LoadoutWithHiddenSyncVar));
            Assert.True(hidden(new LoadoutWithHiddenSyncVar()));
            Assert.False(hidden(new LoadoutWithHiddenSyncVar { syncedQuietly = 1 }));

            // 名前が変わっても・減っても、同じ「全字段が既定値か」の判定になる。
            var renamed = NativeMidJoinLoadoutIdlePerf.BuildEmptyCheck(typeof(LoadoutWithRenamedField));
            Assert.True(renamed(new LoadoutWithRenamedField()));
            Assert.False(renamed(new LoadoutWithRenamedField
            {
                cStars = new List<LoadoutStarItem> { new LoadoutStarItem() },
            }));
            var missing = NativeMidJoinLoadoutIdlePerf.BuildEmptyCheck(typeof(LoadoutMissingAField));
            Assert.True(missing(new LoadoutMissingAField()));
        }
    }
}
