using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#876）最大値が不変のステータス再計算で、体力・マナの比率書き換え（入口の比率 ×
    /// 新しい最大値）が float32 の往復計算で 1 ulp ずれた値を字段に書き、dirty ビットを
    /// 立てるノイズを、入口の値への書き戻しで消す NativeStatsRatioIdlePerf の検証。
    /// 境界の EntityStatus（NativeBoundary.cs + StatsRatioIdlePerfBoundary.cs）が本体と同じ
    /// 比率書き換えと weaver セッター（GeneratedSyncVarSetter(value, ref field, bit, null) 呼び出しの
    /// IL にビット定数の ldc.i8 とフック引数の ldnull を持つ対）のため、Prepare の形検査
    /// （セッターの IL からビット定数を読む照合・フック検査・再計算内の体力・マナの書き込みが
    /// 各ちょうど1つであること）を実ソースのまま通す。
    /// Prefix → 境界の CalculateStats（本体の比率書き換え）→ Postfix の窓を通して、
    /// (1) 最大値が同じとき、書き戻した値が入口の値と一致し、dirty ビットが入口の状態に戻ること、
    /// (2) 入口ですでに立っていたビットは残ること、
    /// (3) 最大値が変わったときと、アンダーフローで0になったときは何もしないこと、
    /// (4) 予測した再書き込みの結果が非有限（最大値0で本体が NaN を書く異常状態）のときは
    ///     書き戻さず本体どおりに通すこと、
    /// (5) 窓の後半（比率書き換えの後）に外部コードが書いた値は最終状態として採用され、
    ///     ビットも落ちないこと、
    /// (6) マナ側のアンダーフロー（非正規化数→0）も体力側と同じ扱いであること、
    /// (7) weaver セッターがフックを持つ形は Prepare のフック検査で拒否されること、
    /// を確かめる。61.7f（最大値100）は 61.7/100*100 が 1 ulp ずれる値（実測）。
    /// </summary>
    public sealed class StatsRatioIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly bool _savedServerActive = Mirror.NetworkServer.active;
        private readonly bool _savedOption = NativePerfOptions.StatsRatioIdle;

        public StatsRatioIdlePerfTests()
        {
            // Prefix はサーバでのみ窓を残す（本体の CalculateStats と同じ前提）。
            Mirror.NetworkServer.active = true;
            NativePerfOptions.StatsRatioIdle = true;
        }

        public void Dispose()
        {
            Mirror.NetworkServer.active = _savedServerActive;
            NativePerfOptions.StatsRatioIdle = _savedOption;
        }

        // 抽出部分の静的欄を整えてから Prepare を通す（StatsCalcRpcPerfTests.Arm と同じ並べ替え）。
        // 通ればビット定数（_healthBit/_manaBit）は境界セッターの IL から読まれた値で埋まる。
        private static void Arm()
        {
            var type = typeof(NativeStatsRatioIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            foreach (var field in new[] { "_calc", "_plainHealthSetter", "_plainManaSetter",
                "_networkHealthSetter", "_networkManaSetter", "_currentHealth", "_currentMana", "_maxHealth",
                "_maxMana", "_sendHealth", "_sendMana", "_dirtyBits", "_setDirtyBits" })
            {
                type.GetField(field, Hidden).SetValue(null, null);
            }
            type.GetField("_healthBit", Hidden).SetValue(null, 0UL);
            type.GetField("_manaBit", Hidden).SetValue(null, 0UL);
            Assert.True((bool)type.GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        private static ulong HealthBit
            => (ulong)typeof(NativeStatsRatioIdlePerf).GetField("_healthBit", Hidden).GetValue(null);
        private static ulong ManaBit
            => (ulong)typeof(NativeStatsRatioIdlePerf).GetField("_manaBit", Hidden).GetValue(null);

        // 最大値100・基礎最大値100（＝最大値が不変の再計算）の実体。体力・マナの初期値は呼び出し側で。
        private static EntityStatus NewStatus()
            => new EntityStatus { isServer = true, syncVarDirtyBits = 0UL };

        // Prefix → 境界の CalculateStats（本体の比率書き換え）→ Postfix。Harmony は介さず、
        // 実ソースの Prefix/Postfix と本体の処理をこの順（本体実行あり）で動かす。
        private static void RunPatchedRecalculation(EntityStatus status)
        {
            var type = typeof(NativeStatsRatioIdlePerf);
            object[] prefixArgs = { status, null };
            Assert.True((bool)type.GetMethod("Prefix", Hidden).Invoke(null, prefixArgs));
            status.CalculateStats();
            type.GetMethod("Postfix", Hidden).Invoke(null, new[] { status, prefixArgs[1], true });
        }

        // (1) 最大値が同じ再計算。まず同じ値の組をパッチなしで通して、本体の比率書き換えが
        // 実際に 1 ulp のずれを書き dirty ビットを立てること（消しているノイズの実在）を
        // 確かめてから、同じ組を窓付きで通す。
        [Fact]
        public void AMaxUnchangedRecalculationRestoresTheEntryValuesAndBits()
        {
            Arm();
            var vanilla = NewStatus();
            vanilla.currentHealth = 61.7f;
            vanilla.currentMana = 61.7f;
            vanilla.CalculateStats();
            Assert.NotEqual(BitConverter.SingleToInt32Bits(61.7f), BitConverter.SingleToInt32Bits(vanilla.currentHealth));
            Assert.NotEqual(0UL, vanilla.syncVarDirtyBits & HealthBit);
            Assert.NotEqual(0UL, vanilla.syncVarDirtyBits & ManaBit);

            var patched = NewStatus();
            patched.currentHealth = 61.7f;
            patched.currentMana = 61.7f;
            // 入口には体力・マナ以外の未送信のビット（位置など）が立っている同期窓。
            patched.syncVarDirtyBits = 1UL << 3;
            ulong entryBits = patched.syncVarDirtyBits;
            int entryHealthBits = BitConverter.SingleToInt32Bits(patched.currentHealth);
            int entryManaBits = BitConverter.SingleToInt32Bits(patched.currentMana);
            RunPatchedRecalculation(patched);
            Assert.Equal(entryHealthBits, BitConverter.SingleToInt32Bits(patched.currentHealth));
            Assert.Equal(entryManaBits, BitConverter.SingleToInt32Bits(patched.currentMana));
            // 体力・マナのビットは入口で立っていなかったので落ちて、他のビットはそのまま残る
            // → ビット全体が入口の状態に戻る。
            Assert.Equal(entryBits, patched.syncVarDirtyBits);
        }

        // (2) 入口ですでに立っていた体力・マナのビット（前の再計算のまだ未送信の変更）は、
        // 書き戻しによっても落ちない。ビットが立ったまま value が入口の値に戻るので、
        // その未送信の変更は入口の値で送られる。
        [Fact]
        public void BitsRaisedBeforeTheRecalculationSurviveTheRestore()
        {
            Arm();
            var status = NewStatus();
            status.currentHealth = 61.7f;
            status.currentMana = 61.7f;
            status.syncVarDirtyBits = HealthBit | ManaBit | (1UL << 3);
            ulong entryBits = status.syncVarDirtyBits;
            int entryHealthBits = BitConverter.SingleToInt32Bits(status.currentHealth);
            int entryManaBits = BitConverter.SingleToInt32Bits(status.currentMana);
            RunPatchedRecalculation(status);
            Assert.Equal(entryHealthBits, BitConverter.SingleToInt32Bits(status.currentHealth));
            Assert.Equal(entryManaBits, BitConverter.SingleToInt32Bits(status.currentMana));
            Assert.Equal(entryBits, status.syncVarDirtyBits);
        }

        // (3a) 最大値が動いた再計算。比率維持の書き換え（0.4×150・0.625×120）はそのまま立ち、
        // dirty ビットも落ちない（本物の変化として送られる）。
        [Fact]
        public void ARecalculationThatMovesTheMaxLeavesTheRatioRewriteAlone()
        {
            Arm();
            var status = NewStatus();
            status.currentHealth = 40f;
            status.currentMana = 50f;
            status.maxMana = 80f;
            status.baseMaxHealth = 150f;
            status.baseMaxMana = 120f;
            RunPatchedRecalculation(status);
            Assert.Equal(BitConverter.SingleToInt32Bits(40f / 100f * 150f), BitConverter.SingleToInt32Bits(status.currentHealth));
            Assert.Equal(BitConverter.SingleToInt32Bits(50f / 80f * 120f), BitConverter.SingleToInt32Bits(status.currentMana));
            Assert.Equal(150f, status.maxHealth);
            Assert.Equal(120f, status.maxMana);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & ManaBit);
        }

        // (3b) 本体の比率書き換えが最小の正の非正規化数を0にアンダーフローさせた再計算。
        // その0は死亡判定（LogicUpdate の currentHealth <= 0）に使われるため書き戻さず、
        // ビットもそのまま。同時に走るマナの通常のノイズは書き戻る（対象が体力側だけであること）。
        [Fact]
        public void AVanillaUnderflowToZeroStands()
        {
            Arm();
            var status = NewStatus();
            status.currentHealth = float.Epsilon;
            status.currentMana = 61.7f;
            RunPatchedRecalculation(status);
            Assert.Equal(0, BitConverter.SingleToInt32Bits(status.currentHealth));
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(BitConverter.SingleToInt32Bits(61.7f), BitConverter.SingleToInt32Bits(status.currentMana));
            Assert.Equal(0UL, status.syncVarDirtyBits & ManaBit);
        }

        // (4) 最大値がちょうど0の異常な再計算（境界は本体の Mathf.Max(…,1f) クランプを転写して
        // いないため、入口61.7/最大0 は +Inf になり、+Inf*0 で本体の書き換えは NaN を書く）。
        // 予測した再書き込みの結果が非有限のときは書き戻さないため、NaN が本体どおり最終状態として
        // 立ち（以前の実装は最後の有限値で置き換えていた）、ビットも落ちない。マナ側（最大値100）の
        // 通常のノイズは書き戻る。
        [Fact]
        public void AVanillaNonFiniteRewriteStands()
        {
            Arm();
            var status = NewStatus();
            status.currentHealth = 61.7f;
            status.currentMana = 61.7f;
            status.maxHealth = 0f;
            status.baseMaxHealth = 0f;
            RunPatchedRecalculation(status);
            Assert.Equal(BitConverter.SingleToInt32Bits(float.NaN), BitConverter.SingleToInt32Bits(status.currentHealth));
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(BitConverter.SingleToInt32Bits(61.7f), BitConverter.SingleToInt32Bits(status.currentMana));
            Assert.Equal(0UL, status.syncVarDirtyBits & ManaBit);
        }

        // (5) 窓の後半（比率書き換えの後・Postfix の前）に外部コードが weaver セッターで書いた値は、
        // 本体での最終状態として採用・送信される。書き戻しは「再計算直後の値が入口の値から予測した
        // 比率再書き込みの結果と一致する場合に限る」ため、この書き込みを入口の値へ戻さず、
        // ビットも落とさない。同時に走るマナの通常のノイズは書き戻る。
        [Fact]
        public void AWriteAfterTheRewriteStandsAsTheFinalState()
        {
            Arm();
            var status = NewStatus();
            status.currentHealth = 61.7f;
            status.currentMana = 61.7f;
            var type = typeof(NativeStatsRatioIdlePerf);
            object[] prefixArgs = { status, null };
            Assert.True((bool)type.GetMethod("Prefix", Hidden).Invoke(null, prefixArgs));
            status.CalculateStats();
            // 比率書き換えの後に走り得る購読者（finalStats 系・ホスト側の RPC 配送）が
            // セッターを通って書くところを模す。
            status.Network_currentHealth = 123.4f;
            type.GetMethod("Postfix", Hidden).Invoke(null, new[] { status, prefixArgs[1], true });
            Assert.Equal(BitConverter.SingleToInt32Bits(123.4f), BitConverter.SingleToInt32Bits(status.currentHealth));
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(BitConverter.SingleToInt32Bits(61.7f), BitConverter.SingleToInt32Bits(status.currentMana));
            Assert.Equal(0UL, status.syncVarDirtyBits & ManaBit);
        }

        // (6) マナ側のアンダーフロー（最小の正の非正規化数が割り算で0に潰れる）。本体は 0 を書いて
        // 送信するため、体力側（AVanillaUnderflowToZeroStands）と同じ >0→<=0 の扱いで書き戻さず、
        // ビットもそのまま。同時に走る体力の通常のノイズは書き戻る。
        [Fact]
        public void AVanillaManaUnderflowToZeroStands()
        {
            Arm();
            var status = NewStatus();
            status.currentHealth = 61.7f;
            status.currentMana = float.Epsilon;
            RunPatchedRecalculation(status);
            Assert.Equal(0, BitConverter.SingleToInt32Bits(status.currentMana));
            Assert.NotEqual(0UL, status.syncVarDirtyBits & ManaBit);
            Assert.Equal(BitConverter.SingleToInt32Bits(61.7f), BitConverter.SingleToInt32Bits(status.currentHealth));
            Assert.Equal(0UL, status.syncVarDirtyBits & HealthBit);
        }

        // (7) Prepare のフック検査。GeneratedSyncVarSetter 呼び出しの直前に ldnull 以外
        // （デリゲート）を積む形は拒否され、ldnull（フックなし）の形は通る。現行の実機 weaver 出力は
        // 両セッターともフックなし（GeneratedSyncVarSetter(value, ref field, <bit>, null)。逆コンパイル確認）。
        private sealed class HookedSetterShape : Mirror.NetworkBehaviour
        {
            private float _field;
            internal void Hooked(float value) => GeneratedSyncVarSetter(value, ref _field, 1UL, v => _ = v);
            internal void Unhooked(float value) => GeneratedSyncVarSetter(value, ref _field, 1UL, null);
        }

        [Fact]
        public void PrepareRejectsASetterThatGainedASyncVarHook()
        {
            Arm();
            var checker = typeof(NativeStatsRatioIdlePerf).GetMethod("SetterHasNoHook", Hidden);
            var shapeType = typeof(HookedSetterShape);
            // フックなし（ldnull）の形は通る。
            Assert.True((bool)checker.Invoke(null, new object[] { shapeType.GetMethod("Unhooked", Hidden) }));
            // フック付き（デリゲートを積む）形は拒否される。
            Assert.False((bool)checker.Invoke(null, new object[] { shapeType.GetMethod("Hooked", Hidden) }));
        }
    }
}
