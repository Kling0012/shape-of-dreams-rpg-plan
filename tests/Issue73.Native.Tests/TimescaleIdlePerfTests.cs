using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#933）タイムスケールの同値再送をやめる NativeTimescaleIdlePerf /
    /// NativeTimescaleIdlePublishPerf の結果不変性の検証。境界の TimescaleManager
    /// （NativeBoundary.cs）が weaver セッターと同じ意味の書き口（等値なら書かず、異値なら
    /// 字段を書いて dirty ビットを立てる）を持つため、値の変化・ビットの昇格は実機と同じ
    /// セマンティクスで動く。Prepare のビット定数抽出は本体 DLL の
    /// Network&lt;effectTimescale&gt;k__BackingField の IL でのみ解決されるため検証対象外とし、
    /// テストは Prepare を通らず静的欄（_dirtyBits/_setDirtyBits デリゲート・_effectBit・
    /// _owner/_sent/_hasSent）を差し替えて、2つの Postfix のロジック（Harmony 不使用）を直接動かす。
    /// (a) フレーム末値が配信済みの基線と同値のとき effectTimescale のビットだけが落ち、
    ///     他のビットは残ること、
    /// (b) 基線と異なる値のときビットが落ちないこと、
    /// (c) 差分の serialize が値を実際に運んだときだけ基線が進み、ビットが落ちた差分や
    ///     初期状態（途中参加者のスポーン payload）では基線が進まないこと、
    /// (d) 管理対象（owner）の切替で基線が未知に戻り、同値でもビットを落とさないこと、
    /// (e) 設定が OFF の間・サーバが active でない間も基線が未知に戻り、ON へ戻った
    ///     直後の同値比較でビットを落とさないこと、
    /// (f) 基線が未知に戻る窓で管理対象への参照（_owner）も null に戻ること、
    /// (g) 境界 Mirror の syncVarDirtyBits が本体 Mirror.dll と同じ「プロパティ」の形を
    ///     保つこと（Prepare のデリゲート解決の前提）、
    /// を確かめる。特に (c) の初期状態の経路は、変更ビットが立ったまま途中参加が起きたとき
    /// 既存参加者への差分が消える事故（前ラウンドの指摘）の回帰検査になる。
    /// </summary>
    public sealed class TimescaleIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const ulong OtherBit = 1UL << 5;

        private readonly bool _savedServerActive = Mirror.NetworkServer.active;
        private readonly bool _savedOption = NativePerfOptions.TimescaleIdle;

        public TimescaleIdlePerfTests()
        {
            Mirror.NetworkServer.active = true;
            NativePerfOptions.TimescaleIdle = true;
        }

        public void Dispose()
        {
            Mirror.NetworkServer.active = _savedServerActive;
            NativePerfOptions.TimescaleIdle = _savedOption;
        }

        // Prepare が本体 DLL で埋める静的欄を、境界 NetworkBehaviour.syncVarDirtyBits に対する
        // 実ソースと同じデリゲートと固定のビット定数で埋める（StatsCalcRpcPerfTests と同じ
        // 並べ替え。Prepare 自体はこの境界では失敗するため呼ばない）。
        private static void Arm()
        {
            var type = typeof(NativeTimescaleIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            type.GetField("_target", Hidden).SetValue(null, null);
            type.GetField("_owner", Hidden).SetValue(null, null);
            type.GetField("_sent", Hidden).SetValue(null, 0f);
            type.GetField("_hasSent", Hidden).SetValue(null, false);
            type.GetField("_effectBit", Hidden).SetValue(null, TimescaleManager.EffectBit);
            var bits = typeof(Mirror.NetworkBehaviour).GetProperty("syncVarDirtyBits");
            type.GetField("_dirtyBits", Hidden).SetValue(null, (Func<Mirror.NetworkBehaviour, ulong>)Delegate.CreateDelegate(
                typeof(Func<Mirror.NetworkBehaviour, ulong>), bits.GetGetMethod()));
            type.GetField("_setDirtyBits", Hidden).SetValue(null, (Action<Mirror.NetworkBehaviour, ulong>)Delegate.CreateDelegate(
                typeof(Action<Mirror.NetworkBehaviour, ulong>), bits.GetSetMethod()));
        }

        private static float Sent
            => (float)typeof(NativeTimescaleIdlePerf).GetField("_sent", Hidden).GetValue(null);
        private static bool HasSent
            => (bool)typeof(NativeTimescaleIdlePerf).GetField("_hasSent", Hidden).GetValue(null);
        private static object Owner
            => typeof(NativeTimescaleIdlePerf).GetField("_owner", Hidden).GetValue(null);

        // FrameUpdate の Postfix（フレーム末の処理）を本体実行ありで動かす。
        private static void RunFrameEnd(TimescaleManager manager)
            => typeof(NativeTimescaleIdlePerf).GetMethod("Postfix", Hidden)
                .Invoke(null, new object[] { manager, true });

        // SerializeSyncVars の Postfix（差分・初期状態の serialize 直後）を本体実行ありで動かす。
        private static void RunSerialized(TimescaleManager manager, bool initialState)
            => typeof(NativeTimescaleIdlePublishPerf).GetMethod("Postfix", Hidden)
                .Invoke(null, new object[] { manager, true, initialState });

        // Mirror の差分配信は broadcast の後にビットを一掃する。その転写。
        private static void BroadcastCleared(TimescaleManager manager)
            => manager.syncVarDirtyBits = 0UL;

        // 差分で値を配信して基線を作る。値は weaver セッターと同じ意味の書き口で動かすため、
        // 変化すれば必ずビットが立つ。
        private static void Deliver(TimescaleManager manager, float value)
        {
            manager.Network_effectTimescale(value);
            RunSerialized(manager, initialState: false);
            BroadcastCleared(manager);
        }

        // (a) スロー窓の定常フレーム: 冒頭の 1f戻しと再適用が同じ値に落ち着くとき、
        // effectTimescale のビットだけが落ち、他の SyncVar のビット（ここでは OtherBit）は
        // そのまま残る。
        [Fact]
        public void AnUnchangedFrameValueDropsOnlyTheEffectBit()
        {
            Arm();
            var manager = new TimescaleManager();
            Deliver(manager, 0.1f);
            Assert.Equal(0.1f, Sent);
            // 定常フレーム: FrameUpdate の冒頭が 1f を書き、ループが 0.1f を書き直す。
            manager.Network_effectTimescale(1f);
            manager.Network_effectTimescale(0.1f);
            manager.syncVarDirtyBits |= OtherBit;
            RunFrameEnd(manager);
            Assert.Equal(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
            Assert.Equal(OtherBit, manager.syncVarDirtyBits & OtherBit);
        }

        // (b) 値が実際に変わったフレーム（スローの開始 0.1→0.05）ではビットは落ちず、
        // 基線も動かない。変化は次の差分で必ず送られる。
        [Fact]
        public void AChangedFrameValueKeepsTheEffectBit()
        {
            Arm();
            var manager = new TimescaleManager();
            Deliver(manager, 0.1f);
            manager.Network_effectTimescale(0.05f);
            manager.syncVarDirtyBits |= OtherBit;
            ulong entryBits = manager.syncVarDirtyBits;
            RunFrameEnd(manager);
            Assert.Equal(entryBits, manager.syncVarDirtyBits);
            Assert.Equal(0.1f, Sent);
        }

        // (c-1) 途中参加者の初期状態（スポーン payload）は基線を進めない。変更ビットが
        // 立ったまま参加が起きたフレームの次のフレーム末でも値は基線と異なるためビットは
        // 落ちず、既存参加者には次の差分で 0.1 が届く（前ラウンドの指摘の回帰検査）。
        [Fact]
        public void AnInitialStateSerializeDoesNotAdvanceTheBaseline()
        {
            Arm();
            var manager = new TimescaleManager();
            Deliver(manager, 1f);
            // スロー開始: 1→0.1 の変更でビットが立つが、このフレームではまだ差分が走らない。
            manager.Network_effectTimescale(0.1f);
            // 途中参加者のスポーン payload（initialState）。新しい参加者には両方の float が
            // 届くが、既存参加者の持つ値は変わらない。
            RunSerialized(manager, initialState: true);
            Assert.Equal(1f, Sent);
            Assert.True(HasSent);
            // 次のフレーム末: 0.1 は基線（既存参加者の持つ 1）と異なるためビットは残る。
            RunFrameEnd(manager);
            Assert.NotEqual(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
            // 次の差分配信で 0.1 が届き、初めて基線が進む。
            RunSerialized(manager, initialState: false);
            Assert.Equal(0.1f, Sent);
            BroadcastCleared(manager);
            // 以降の定常フレームは同値再送として落とされる。
            manager.Network_effectTimescale(1f);
            manager.Network_effectTimescale(0.1f);
            RunFrameEnd(manager);
            Assert.Equal(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
        }

        // (c-2) ビットが落ちた差分（同値再送を抑えた broadcast や他の SyncVar だけの差分）は
        // 値を運んでいないため基線を進めない。進めてしまった場合と区別するため、基線・基線の
        // 有効性ともに変化しないことを確かめる。
        [Fact]
        public void ADeltaWithoutTheEffectBitDoesNotAdvanceTheBaseline()
        {
            Arm();
            var manager = new TimescaleManager();
            Deliver(manager, 0.1f);
            // 定常フレーム: フリップでビットが立ち直すが、フレーム末の Postfix が落とす。
            manager.Network_effectTimescale(1f);
            manager.Network_effectTimescale(0.1f);
            RunFrameEnd(manager);
            Assert.Equal(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
            // 他の SyncVar だけが立った差分配信。
            manager.syncVarDirtyBits |= OtherBit;
            RunSerialized(manager, initialState: false);
            Assert.Equal(0.1f, Sent);
            Assert.True(HasSent);
            // 基線は 0.1 のままなので、次の定常フレームも引き続き落とされる。
            manager.Network_effectTimescale(1f);
            manager.Network_effectTimescale(0.1f);
            RunFrameEnd(manager);
            Assert.Equal(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
        }

        // (d) 管理対象の切替（owner の差し替え）では基線が未知に戻る。新しい対象の値が前の
        // 基線と偶然同じ値でもビットは落とさず、差分で配信されてから基線が結ばれる。
        [Fact]
        public void AnOwnerSwitchResetsTheBaseline()
        {
            Arm();
            var first = new TimescaleManager();
            Deliver(first, 0.1f);
            var second = new TimescaleManager();
            second.Network_effectTimescale(0.1f);
            second.syncVarDirtyBits |= OtherBit;
            RunFrameEnd(second);
            Assert.NotEqual(0UL, second.syncVarDirtyBits & TimescaleManager.EffectBit);
            Assert.Equal(OtherBit, second.syncVarDirtyBits & OtherBit);
            Assert.False(HasSent);
            RunSerialized(second, initialState: false);
            Assert.Equal(0.1f, Sent);
            Assert.True(HasSent);
            Assert.Equal(second, Owner);
        }

        // (e-1) 設定が OFF の間は基線が未知に戻る（レビュー指摘 F1 の回帰検査）。OFF の間は
        // 両 Postfix が素通りするため値の配信が起きても基線は追従せず、ON へ戻った直後の
        // フレーム末で古い基線との同値比較が新しい変更のビットを落とせないようにする。
        // 筋書き: 基線 0.1 → OFF の間に値が 1 へ変わって届き、また 0.1 へ戻る → ON に戻す。
        [Fact]
        public void ADisabledWindowResetsTheBaseline()
        {
            Arm();
            var manager = new TimescaleManager();
            Deliver(manager, 0.1f);
            Assert.True(HasSent);
            // 設定を OFF にする。フレーム末の Postfix も差分の serialize の Postfix も
            // 基線を未知に戻して抜ける。
            NativePerfOptions.TimescaleIdle = false;
            RunFrameEnd(manager);
            Assert.False(HasSent);
            // OFF の間: スローが終わって値が 1 へ変わり、素通しの差分で参加者へ届く。
            manager.Network_effectTimescale(1f);
            RunSerialized(manager, initialState: false);
            Assert.False(HasSent);
            BroadcastCleared(manager);
            // 新しいスローが始まって値が 0.1 へ戻り、ON に戻した直後のフレーム末を迎える。
            // 値 0.1 は古い基線 0.1 と同じだが、基線が未知のためビットは落ちない。
            manager.Network_effectTimescale(0.1f);
            NativePerfOptions.TimescaleIdle = true;
            RunFrameEnd(manager);
            Assert.NotEqual(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
            // 次の差分で 0.1 が配信されて初めて基線が結ばれ、以降の定常フレームは落とされる。
            RunSerialized(manager, initialState: false);
            Assert.True(HasSent);
            Assert.Equal(0.1f, Sent);
            BroadcastCleared(manager);
            manager.Network_effectTimescale(1f);
            manager.Network_effectTimescale(0.1f);
            RunFrameEnd(manager);
            Assert.Equal(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
        }

        // (e-2) サーバが active でない間も基線は未知に戻る。同じ TimescaleManager のインスタンス
        // を使ったままサーバを再開したとき、停止中に動いた値が古い基線と同じ値に戻っていても
        // ビットは落ちず、再開後の最初の差分で配信される。
        [Fact]
        public void AnInactiveServerWindowResetsTheBaseline()
        {
            Arm();
            var manager = new TimescaleManager();
            Deliver(manager, 0.1f);
            Assert.True(HasSent);
            Mirror.NetworkServer.active = false;
            RunFrameEnd(manager);
            Assert.False(HasSent);
            // 停止の間に値が 1 へ動いて 0.1 へ戻る（配信は起きない）。
            manager.Network_effectTimescale(1f);
            manager.Network_effectTimescale(0.1f);
            Mirror.NetworkServer.active = true;
            RunFrameEnd(manager);
            Assert.NotEqual(0UL, manager.syncVarDirtyBits & TimescaleManager.EffectBit);
            RunSerialized(manager, initialState: false);
            Assert.True(HasSent);
            Assert.Equal(0.1f, Sent);
        }

        // (f) 基線が未知に戻る窓（設定 OFF・サーバ停止）では、管理対象への参照（_owner）も
        // null に戻る。シーンの破棄で TimescaleManager が消えた後も、静的欄が破棄済みの
        // オブジェクトを次の管理対象が現れるまで握り続けないようにする確認。
        [Fact]
        public void AStandDownWindowReleasesTheOwnerReference()
        {
            Arm();
            var manager = new TimescaleManager();
            Deliver(manager, 0.1f);
            Assert.Equal(manager, Owner);
            // 設定 OFF の窓: フレーム末の Postfix も差分の serialize の Postfix も参照を離す。
            NativePerfOptions.TimescaleIdle = false;
            RunFrameEnd(manager);
            Assert.Null(Owner);
            manager.Network_effectTimescale(0.05f);
            RunSerialized(manager, initialState: false);
            Assert.Null(Owner);
            // サーバ停止の窓も同じ。
            NativePerfOptions.TimescaleIdle = true;
            Deliver(manager, 0.2f);
            Assert.Equal(manager, Owner);
            Assert.True(HasSent);
            Mirror.NetworkServer.active = false;
            RunFrameEnd(manager);
            Assert.Null(Owner);
            Assert.False(HasSent);
            // 再開後の最初の差分で、基線は新しい管理対象に結ばれる。
            Mirror.NetworkServer.active = true;
            manager.Network_effectTimescale(1f);
            RunSerialized(manager, initialState: false);
            Assert.Equal(manager, Owner);
            Assert.True(HasSent);
            Assert.Equal(1f, Sent);
        }

        // (g) 境界 Mirror の syncVarDirtyBits は、本体 Mirror.dll r1.4.1.2（2026-10-20 に
        // ilspycmd で確認: protected ulong syncVarDirtyBits { get; private set; }）と同じ
        // 「プロパティ」の形を保つ。Prepare の ResolveBitAccess は PropertyGetter/PropertySetter
        // で解決するため、欄（field）に変わると警告1回でこの軽量化が黙って効かなくなる。
        // 境界が実物と同じ形であることを固定しておく。
        [Fact]
        public void TheBoundaryMirrorKeepsSyncVarDirtyBitsAProperty()
        {
            var type = typeof(Mirror.NetworkBehaviour);
            var property = type.GetProperty("syncVarDirtyBits", Hidden);
            Assert.NotNull(property);
            Assert.NotNull(property.GetGetMethod(true));
            Assert.NotNull(property.GetSetMethod(true));
            Assert.Equal(typeof(ulong), property.PropertyType);
            Assert.Null(type.GetField("syncVarDirtyBits", Hidden));
        }
    }
}
