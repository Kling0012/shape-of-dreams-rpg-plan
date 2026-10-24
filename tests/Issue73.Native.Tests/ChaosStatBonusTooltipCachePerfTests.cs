using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1167）混沌の神殿の加護のツールチップ文を値が変わらない間使い回す変更の、結果が
    /// 変わらないことの検証。NativeChaosStatBonusTooltipCachePerf の Prefix と Postfix と
    /// TryPrepare を、境界（ChaosShrineHitScanPerfBoundary.cs）の模倣
    /// Se_Shrine_Chaos_StatBonus に対して TryPrepare で読み取りを組み直して確かめる:
    /// (a) 同じ入力の2回目は本体（模倣の GetCustomTooltipDescription）を呼ばず、前回と同じ
    ///     文字列をそのまま返す、
    /// (b) 入力のどれか（synced ボーナスの float・int、SyncVar の float・int、言語データの
    ///     参照）を変えると作り直し、新しい値が文に現れる、
    /// (c) Prefix がキャッシュを返した呼び出しでは Postfix はスナップショットを取り直さない
    ///     （Harmony の仕様で Postfix は省いた後でも呼ばれるため、目印で省す）、
    /// (d) 設定が無効なら常に本体を走らせる、
    /// (e) 型が解決できない／必須字段が無いとき TryPrepare は false を返し、警告は1回だけ。
    /// </summary>
    public sealed class ChaosStatBonusTooltipCachePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo PrefixMethod = typeof(NativeChaosStatBonusTooltipCachePerf).GetMethod("Prefix", Hidden);
        private static readonly MethodInfo PostfixMethod = typeof(NativeChaosStatBonusTooltipCachePerf).GetMethod("Postfix", Hidden);

        private readonly DewLocalizationData _savedData;

        // Prefix と Postfix は静的な欄（読み取りの組み立て・無効化・警告・キャッシュの目印）を
        // 読み書きするため、毎テストで模倣型に向け直して無効化も解き、Dispose で設定と言語
        // データを既定へ戻す。テスト間で持ち越すものはない（値は毎テスト新しいインスタンス）。
        public ChaosStatBonusTooltipCachePerfTests()
        {
            var type = typeof(NativeChaosStatBonusTooltipCachePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            type.GetField("_servedFromCache", Hidden).SetValue(null, false);
            NativePerfOptions.ChaosStatBonusTooltipCache = true;
            _savedData = DewLocalization.currentData;
            DewLocalization.currentData = new DewLocalizationData("en");
            Assert.True(NativeChaosStatBonusTooltipCachePerf.TryPrepare("Se_Shrine_Chaos_StatBonus"));
        }

        public void Dispose()
        {
            var type = typeof(NativeChaosStatBonusTooltipCachePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_servedFromCache", Hidden).SetValue(null, false);
            NativePerfOptions.ChaosStatBonusTooltipCache = true;
            DewLocalization.currentData = _savedData;
        }

        // Harmony が GetCustomTooltipDescription を呼ぶ1回分と同じ形: Prefix →（Prefix が
        // true を返したときだけ本体）→ Postfix（Prefix が書いた __result を渡す）。
        private static string CallTooltip(Se_Shrine_Chaos_StatBonus shrine)
        {
            var args = new object[] { shrine, null };
            bool run = (bool)PrefixMethod.Invoke(null, args);
            string result = run ? shrine.GetCustomTooltipDescription() : (string)args[1];
            PostfixMethod.Invoke(null, new object[] { shrine, result });
            return result;
        }

        // Prefix の静的なキャッシュ表（ConditionalWeakTable）から効果インスタンスの項を取り出す。
        // GetOrCreateValue は鍵を1つ取る公開メソッド（項が無ければ作る）。
        private static object EntryOf(object shrine)
        {
            var cache = typeof(NativeChaosStatBonusTooltipCachePerf).GetField("_cache", Hidden).GetValue(null);
            return cache.GetType().GetMethod("GetOrCreateValue").Invoke(cache, new[] { shrine });
        }

        private static object FieldOf(object entry, string name)
            => entry.GetType().GetField(name, Hidden).GetValue(entry);

        // (a) 同じ入力の2回目は本体を呼ばず、キャッシュの文字列（同じ参照）を返す。
        [Fact]
        public void TheSameInputReturnsTheCachedStringWithoutRebuilding()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus { currentAddedGemSlotQ = 1 };
            string first = CallTooltip(shrine);
            Assert.Equal(1, shrine.tooltipDescriptionBuilds);
            string second = CallTooltip(shrine);
            Assert.Same(first, second);
            Assert.Equal(1, shrine.tooltipDescriptionBuilds);
        }

        // (b) synced ボーナスの float（syncedRegularChaosBonus 側）を変えると作り直す。
        [Fact]
        public void AChangedRegularBonusFloatRebuildsTheText()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            shrine.syncedRegularChaosBonus._maxHealthFlat = 12.5f;
            string second = CallTooltip(shrine);
            Assert.NotEqual(first, second);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.Contains("hp=12.5", second);
        }

        // (b) synced ボーナスの float（syncedCorruptedChaosBonus 側）を変えても作り直す。
        [Fact]
        public void AChangedCorruptedBonusFloatRebuildsTheText()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            shrine.syncedCorruptedChaosBonus._critChanceFlat = 3.25f;
            string second = CallTooltip(shrine);
            Assert.NotEqual(first, second);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.Contains("crit=3.25", second);
        }

        // (b) SyncVar の float（効果自身の字段）を変えても作り直す。
        [Fact]
        public void AChangedSyncVarFloatRebuildsTheText()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            shrine.currentDodgeHaste = 4.5f;
            string second = CallTooltip(shrine);
            Assert.NotEqual(first, second);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.Contains("dh=4.5", second);
        }

        // (b) synced ボーナスの int を変えても作り直す。
        [Fact]
        public void AChangedBonusIntRebuildsTheText()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            shrine.syncedCorruptedChaosBonus._everyFourAttackStartIndexFlat = 3;
            string second = CallTooltip(shrine);
            Assert.NotEqual(first, second);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.Contains("e4=3", second);
        }

        // (b) SyncVar の int を変えても作り直す。
        [Fact]
        public void AChangedSyncVarIntRebuildsTheText()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            shrine.currentAddedGemSlotQ = 2;
            string second = CallTooltip(shrine);
            Assert.NotEqual(first, second);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.Contains("gq=2", second);
        }

        // (b) 言語データの参照を替えても作り直す（言語の切り替え。値はどちらも変わらない）。
        [Fact]
        public void AChangedLocaleRebuildsTheText()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            Assert.StartsWith("en:", first);
            DewLocalization.currentData = new DewLocalizationData("ja");
            string second = CallTooltip(shrine);
            Assert.NotEqual(first, second);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.StartsWith("ja:", second);
        }

        // (c) Prefix がキャッシュを返した呼び出しでは、Postfix は（Harmony の仕様で呼ばれるが）
        // スナップショットを取り直さない。実機の1呼び出しの中で値が変わることは無いが、
        // 省いたことを見るために Prefix と Postfix の間で値を変える: スナップショットを
        // 取り直すなら entry の gem スロットの値（ints[5]）が 7 に替わり、次の呼び出しが
        // キャッシュを返してしまう。省くなら entry は古い値のまま残り、次の呼び出しで
        // 変わった値を見て作り直す。
        [Fact]
        public void ThePostfixSkipsTheSnapshotOnACacheHit()
        {
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            var args = new object[] { shrine, null };
            Assert.False((bool)PrefixMethod.Invoke(null, args));
            Assert.Same(first, (string)args[1]);
            shrine.currentAddedGemSlotQ = 7;
            PostfixMethod.Invoke(null, new object[] { shrine, args[1] });
            object entry = EntryOf(shrine);
            Assert.Equal(0, ((int[])FieldOf(entry, "ints"))[5]);
            Assert.Same(first, (string)FieldOf(entry, "text"));
            string rebuilt = CallTooltip(shrine);
            Assert.NotEqual(first, rebuilt);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.Contains("gq=7", rebuilt);
        }

        // (d) ChaosStatBonusTooltipCache が false なら、入力が同じでも毎回本体を走らせる。
        [Fact]
        public void ADisabledOptionAlwaysRunsTheOriginal()
        {
            NativePerfOptions.ChaosStatBonusTooltipCache = false;
            var shrine = new Se_Shrine_Chaos_StatBonus();
            Assert.Equal(CallTooltip(shrine), CallTooltip(shrine));
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
        }

        // (e) 対象の型が解決できないとき TryPrepare は false を返す（この修正だけが外れて、
        // 残りの MOD は動き続ける）。
        [Fact]
        public void AnUnresolvableTypeFailsPrepare()
        {
            Assert.False(NativeChaosStatBonusTooltipCachePerf.TryPrepare("Se_Shrine_Chaos_StatBonus_NotInThisGame"));
        }

        // (e) 必須の字段が無い型（本体更新で字段の名前が替わった場合に相当）でも false を
        // 返し、警告は1回だけ出る（2回目の失敗では出ない）。
        [Fact]
        public void AMissingFieldFailsPrepareAndWarnsOnce()
        {
            int warnings = Log.Warnings.Count;
            Assert.False(NativeChaosStatBonusTooltipCachePerf.TryPrepare("Se_Shrine_Chaos_StatBonus_MissingTooltipField"));
            Assert.Equal(warnings + 1, Log.Warnings.Count);
            Assert.Contains("chaos stat bonus tooltip cache", Log.Warnings[Log.Warnings.Count - 1]);
            Assert.False(NativeChaosStatBonusTooltipCachePerf.TryPrepare("Se_Shrine_Chaos_StatBonus_MissingTooltipField"));
            Assert.Equal(warnings + 1, Log.Warnings.Count);
        }
    }
}
