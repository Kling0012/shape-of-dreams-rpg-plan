using System;
using System.Collections.Generic;
using System.Linq;
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
    /// (b) 入力の 23 値（synced ボーナスの float・int、SyncVar の float・int）それぞれ
    ///     を1つずつ変えると作り直し、新しい値が文に現れる（<see cref="InputMutations"/> の
    ///     23 行で全値を機械的に列挙し、行数が読み取りの値の数と一致することも確かめる）、
    ///     言語データの参照を替えても作り直す、
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

        // (b) 入力 23 値（float 13・int 10）の1行分: 字段の置き場所つきの名前と、その値だけを
        // 非零へ替える操作と、替えた値が文に現れることを見る目印。並びは NativePerfPatches.cs の
        // _floatGetters・_intGetters と同じ順（float 13 → int 10）。模倣型が internal なので
        // 操作はこの非公開の表に置き、[Theory] には名前だけ（string）を渡す。
        private static readonly (string Name, Action<Se_Shrine_Chaos_StatBonus> Change, string Expected)[] MutationRows =
        {
            ("syncedRegularChaosBonus._maxHealthFlat", s => s.syncedRegularChaosBonus._maxHealthFlat = 12.5f, "hp=12.5"),
            ("syncedRegularChaosBonus._armorFlat", s => s.syncedRegularChaosBonus._armorFlat = 2.25f, "ar=2.25"),
            ("syncedRegularChaosBonus._attackDamageFlat", s => s.syncedRegularChaosBonus._attackDamageFlat = 3.5f, "ad=3.5"),
            ("syncedRegularChaosBonus._abilityPowerFlat", s => s.syncedRegularChaosBonus._abilityPowerFlat = 4.5f, "ap=4.5"),
            ("syncedRegularChaosBonus._attackSpeedPercentage", s => s.syncedRegularChaosBonus._attackSpeedPercentage = 5.5f, "as=5.5"),
            ("syncedRegularChaosBonus._abilityHasteFlat", s => s.syncedRegularChaosBonus._abilityHasteFlat = 6.5f, "ah=6.5"),
            ("syncedCorruptedChaosBonus._attackRangeFlat", s => s.syncedCorruptedChaosBonus._attackRangeFlat = 1.5f, "rng=1.5"),
            ("syncedCorruptedChaosBonus._critChanceFlat", s => s.syncedCorruptedChaosBonus._critChanceFlat = 3.25f, "crit=3.25"),
            ("syncedCorruptedChaosBonus._fireEffectAmpFlat", s => s.syncedCorruptedChaosBonus._fireEffectAmpFlat = 7.5f, "fire=7.5"),
            ("syncedCorruptedChaosBonus._coldEffectAmpFlat", s => s.syncedCorruptedChaosBonus._coldEffectAmpFlat = 8.5f, "cold=8.5"),
            ("syncedCorruptedChaosBonus._movementSpeedPercentage", s => s.syncedCorruptedChaosBonus._movementSpeedPercentage = 9.5f, "ms=9.5"),
            ("syncedCorruptedChaosBonus._abilityHasteFlat", s => s.syncedCorruptedChaosBonus._abilityHasteFlat = 10.5f, "cah=10.5"),
            ("currentDodgeHaste", s => s.currentDodgeHaste = 4.5f, "dh=4.5"),
            ("syncedCorruptedChaosBonus._everyFourAttackStartIndexFlat", s => s.syncedCorruptedChaosBonus._everyFourAttackStartIndexFlat = 3, "e4=3"),
            ("currentDodgeAddedCharges", s => s.currentDodgeAddedCharges = 2, "dc=2"),
            ("currentAddedDarkMaxStack", s => s.currentAddedDarkMaxStack = 3, "dark=3"),
            ("currentAddedLightMaxStack", s => s.currentAddedLightMaxStack = 4, "light=4"),
            ("currentAddedMaxLevel", s => s.currentAddedMaxLevel = 5, "lvl=5"),
            ("currentAddedGemSlotQ", s => s.currentAddedGemSlotQ = 2, "gq=2"),
            ("currentAddedGemSlotW", s => s.currentAddedGemSlotW = 6, "gw=6"),
            ("currentAddedGemSlotE", s => s.currentAddedGemSlotE = 7, "ge=7"),
            ("currentAddedGemSlotR", s => s.currentAddedGemSlotR = 8, "gr=8"),
            ("currentAddedGemSlotIdentity", s => s.currentAddedGemSlotIdentity = 9, "gi=9"),
        };

        public static IEnumerable<object[]> InputMutations
            => MutationRows.Select(r => new object[] { r.Name });

        // (b) の機械的な塞ぎ: 変更の行数が、Prefix が比較に使う読み取り（_floatGetters・
        // _intGetters）の数と一致する。読み取りへ値を足して行を足し忘れても（その逆でも）、
        // 値を変える行が1つ欠けて作り直しに失敗するか、この行数の検証が失敗する。
        [Fact]
        public void TheMutationRowsCoverExactlyTheCompiledGetters()
        {
            var type = typeof(NativeChaosStatBonusTooltipCachePerf);
            int floats = ((Array)type.GetField("_floatGetters", Hidden).GetValue(null)).Length;
            int ints = ((Array)type.GetField("_intGetters", Hidden).GetValue(null)).Length;
            Assert.Equal(floats + ints, MutationRows.Length);
        }

        // (b) 23 値それぞれを1つずつ変えると作り直し、新しい値が文に現れる。
        [Theory]
        [MemberData(nameof(InputMutations))]
        public void AChangedInputValueRebuildsTheText(string name)
        {
            var row = MutationRows.Single(r => r.Name == name);
            var shrine = new Se_Shrine_Chaos_StatBonus();
            string first = CallTooltip(shrine);
            row.Change(shrine);
            string second = CallTooltip(shrine);
            Assert.NotEqual(first, second);
            Assert.Equal(2, shrine.tooltipDescriptionBuilds);
            Assert.Contains(row.Expected, second);
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
