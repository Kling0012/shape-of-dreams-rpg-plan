using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1204）撃倒中の英雄の体力固定（0.01f）で立つ _currentHealth の dirty ビットの
    /// 再送を、参加者がすでに持っている値のときだけ省く NativeHeroKnockoutHealthIdlePerf の
    /// Prefix／Postfix／TryPrepare の検証。境界（HeroKnockoutHealthIdlePerfBoundary.cs の
    /// Se_HeroKnockedOut・NativeBoundary.cs と StatsRatioIdlePerfBoundary.cs の EntityStatus）
    /// の weaver セッター（等値なら書かず、違う値なら字段を書いて dirty ビットを立てる）が
    /// 本体と同じ意味のため、Prepare のビット抽出（セッターの IL から唯一の ldc.i8 を読む）を
    /// 実ソースのまま通す。Prefix → 模倣の ActiveLogicUpdate（ピンの書き戻し）→ Postfix の窓を
    /// 通して、
    /// (a) 作成直後（まだ一度も送っていない）bit は残すこと、
    /// (b) 送信の後、回復チャンクで立った bit は下げること、
    /// (c) tick 開始時に bit が立っていなければ、固定の書き戻しで立った bit は残すこと、
    /// (d) 復帰・死亡など同じtickの後半で体力が 0.01f 以外へ書き換わるときは bit を残すこと、
    /// (e) 他の SyncVar の bit を変えないこと、
    /// (f) 設定を無効にすると何もしないこと、
    /// (g) パッチ先がないときは警告1回で外れること、
    /// に加え、Mirror の syncInterval で送信が間引かれた tick が連続しても、参加者を
    /// 0.01f へ戻す唯一の送信（収束用の bit）が失われないことを確かめる（#1204 指摘）。
    /// </summary>
    public sealed class HeroKnockoutHealthIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly bool _savedOption = NativePerfOptions.HeroKnockoutHealthIdle;

        // 抽出部分の静的欄を整えてから TryPrepare を通す（StatsRatioIdlePerfTests.Arm と同じ
        // 並べ替え。印（Delivered／Converging）は効果インスタンスごとの鍵なので、毎テスト新しい
        // 効果を作るだけで汚れは残らない）。通えばビット定数は境界セッターの IL から読まれた
        // 値で埋まる。
        public HeroKnockoutHealthIdlePerfTests()
        {
            var type = typeof(NativeHeroKnockoutHealthIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            type.GetField("_target", Hidden).SetValue(null, null);
            foreach (var field in new[] { "_currentHealth", "_dirtyBits", "_setDirtyBits" })
            {
                type.GetField(field, Hidden).SetValue(null, null);
            }
            type.GetField("_healthBit", Hidden).SetValue(null, 0UL);
            NativePerfOptions.HeroKnockoutHealthIdle = true;
            Assert.True(NativeHeroKnockoutHealthIdlePerf.TryPrepare("Se_HeroKnockedOut"));
        }

        public void Dispose()
        {
            typeof(NativeHeroKnockoutHealthIdlePerf).GetField("_disabled", Hidden).SetValue(null, false);
            NativePerfOptions.HeroKnockoutHealthIdle = _savedOption;
        }

        private static ulong HealthBit
            => (ulong)typeof(NativeHeroKnockoutHealthIdlePerf).GetField("_healthBit", Hidden).GetValue(null);

        // 撃倒された英雄と、その英雄へ張り付いた撃倒効果。ホスト側（isServer）で始める。
        private static Se_HeroKnockedOut NewEffect()
        {
            var victim = new Hero();
            victim.Status.isServer = true;
            return new Se_HeroKnockedOut { victim = victim };
        }

        // Prefix → 模倣の ActiveLogicUpdate（ピンの書き戻し）→ Postfix。Harmony は介さず、
        // 実ソースの Prefix/Postfix と本体の処理をこの順（本体実行あり）で動かす。
        // 開始時の bit の状態は Prefix が効果ごとの状態欄へ書き、Postfix が同じtick分を
        // 読む（__state 1つだけを挟む実際の PATCH 署名と同じ意味の並び）。
        private static void RunPinnedTick(Se_HeroKnockedOut effect)
        {
            var type = typeof(NativeHeroKnockoutHealthIdlePerf);
            object[] prefixArgs = { effect, false };
            Assert.True((bool)type.GetMethod("Prefix", Hidden).Invoke(null, prefixArgs));
            effect.ActiveLogicUpdate(0.1f);
            type.GetMethod("Postfix", Hidden).Invoke(null,
                new[] { effect, prefixArgs[1], true });
        }

        // 同じロジックtickの後半のバケツ（効果のtickより後）で復帰・死亡が体力を書く場面。
        // 送信はtickの間に走るので、この書き込みはそのまま次の送信へ出る。
        private static void RunPinnedTickWithALateBucketWrite(Se_HeroKnockedOut effect, float lateValue)
        {
            var type = typeof(NativeHeroKnockoutHealthIdlePerf);
            object[] prefixArgs = { effect, false };
            Assert.True((bool)type.GetMethod("Prefix", Hidden).Invoke(null, prefixArgs));
            effect.ActiveLogicUpdate(0.1f);
            effect.victim.Status.Network_currentHealth = lateValue;
            type.GetMethod("Postfix", Hidden).Invoke(null,
                new[] { effect, prefixArgs[1], true });
        }

        // Mirror の送信が bit を消した（参加者へ届いた）ことにする。境界には Mirror の
        // 送信輪が無いため、テストが直接クリアする。
        private static void Deliver(EntityStatus status) => status.syncVarDirtyBits &= ~HealthBit;

        // (a) 作成直後の最初の 0.01f（作成時の SetHealth で立った bit）は、送信が確認できる
        // まで毎tickのピンでも下がらない。
        [Fact]
        public void TheCreationPinKeepsItsBitUntilARealSend()
        {
            var effect = NewEffect();
            var status = effect.victim.Status;
            status.Network_currentHealth = 0.01f;
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            RunPinnedTick(effect);
            RunPinnedTick(effect);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(0.01f, status.currentHealth);
        }

        // (b) 作成の 0.01f が送られた後は、回復チャンク（LogicUpdate の自然回復）とピンの
        // 書き戻しで立つ bit が下がる。参加者はすでに 0.01f を持っているため、送っても
        // 同じ値が繰り返されるだけの再送である。
        [Fact]
        public void AChunkAfterTheDeliveredPinDropsItsBit()
        {
            // まず窓なしで通して、チャンク＋ピンの書き込みが bit を立てたまま残ること
            // （省いている再送の実在）を確かめる。
            var vanilla = NewEffect();
            var vanillaStatus = vanilla.victim.Status;
            vanillaStatus.Network_currentHealth = 0.01f;
            Deliver(vanillaStatus);
            vanillaStatus.Network_currentHealth = 2.01f;
            vanilla.ActiveLogicUpdate(0.1f);
            Assert.NotEqual(0UL, vanillaStatus.syncVarDirtyBits & HealthBit);

            var effect = NewEffect();
            var status = effect.victim.Status;
            status.Network_currentHealth = 0.01f;
            RunPinnedTick(effect);
            Deliver(status);
            RunPinnedTick(effect);
            Assert.Equal(0UL, status.syncVarDirtyBits & HealthBit);
            // 回復チャンク: 固定値から 2 動いて bit が立ち、ピンが 0.01f へ戻す。
            status.Network_currentHealth = 2.01f;
            RunPinnedTick(effect);
            Assert.Equal(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(0.01f, status.currentHealth);
        }

        // (c) tick 開始時に bit が立っていなければ、ピンの書き戻しで立った bit は残る。
        // tick の間に 0.01f 以外の値（tick 順序の外で乗った回復など）が書かれ、送信で
        // 参加者へ届いて bit が消えた直後がその形で、この書き戻しが参加者を 0.01f へ
        // 戻す唯一の送信になる。
        [Fact]
        public void AWriteBackRaisedAtAClearEntryKeepsItsBit()
        {
            var effect = NewEffect();
            var status = effect.victim.Status;
            status.Network_currentHealth = 0.01f;
            RunPinnedTick(effect);
            Deliver(status);
            RunPinnedTick(effect);
            // 0.01f 以外の値が書かれ、送信で届いて bit が消える。
            status.Network_currentHealth = 50f;
            Deliver(status);
            // このtickの開始時の bit は立っていない。書き戻しで立った bit は残る。
            RunPinnedTick(effect);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(0.01f, status.currentHealth);
        }

        // （#1204 指摘）Mirror の送信は syncInterval で間引かれるため、bit が立ったままの
        // tick が連続しうる。収束待ちの間は毎tickのピンが bit を下げず、送信が来て初めて
        // bit は消え、以後のチャンクは再び省略される。
        [Fact]
        public void AThinnedSendDoesNotLoseTheConvergenceBit()
        {
            var effect = NewEffect();
            var status = effect.victim.Status;
            status.Network_currentHealth = 0.01f;
            RunPinnedTick(effect);
            Deliver(status);
            RunPinnedTick(effect);
            status.Network_currentHealth = 50f;
            Deliver(status);
            RunPinnedTick(effect);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            // 送信が来ないまま次のtickが連続する（開始時の bit は立っている）。
            RunPinnedTick(effect);
            RunPinnedTick(effect);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            // 送信が届いて初めて bit は消え、次のtickからは再び省略に戻る。
            Deliver(status);
            RunPinnedTick(effect);
            status.Network_currentHealth = 2.01f;
            RunPinnedTick(effect);
            Assert.Equal(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(0.01f, status.currentHealth);
        }

        // (d) 同じtickの後半のバケツで復帰（500）や死亡（0）が体力を書き換えたときは、
        // bit も値も本体が置いたままにする。
        [Fact]
        public void AReviveOrDeathInTheSameTickKeepsItsBit()
        {
            var effect = NewEffect();
            var status = effect.victim.Status;
            status.Network_currentHealth = 0.01f;
            RunPinnedTick(effect);
            Deliver(status);
            RunPinnedTick(effect);
            status.Network_currentHealth = 2.01f;
            RunPinnedTickWithALateBucketWrite(effect, 500f);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(500f, status.currentHealth);
            // 復帰後の値が送られ、ピンが 0.01f へ戻る収束も送られて定常に戻ってから、
            // 死亡（0）で同じ形を確かめる。
            Deliver(status);
            RunPinnedTick(effect);
            Deliver(status);
            RunPinnedTick(effect);
            status.Network_currentHealth = 2.01f;
            RunPinnedTickWithALateBucketWrite(effect, 0f);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Assert.Equal(0f, status.currentHealth);
        }

        // (e) 省略の窓が bit を落とすのは体力の bit だけ。位置・マナなど他の SyncVar の
        // 未送信の bit はそのまま残る。
        [Fact]
        public void TheSkipTouchesOnlyTheHealthBit()
        {
            var effect = NewEffect();
            var status = effect.victim.Status;
            status.Network_currentHealth = 0.01f;
            RunPinnedTick(effect);
            Deliver(status);
            RunPinnedTick(effect);
            const ulong positionBit = 1UL << 3;
            status.syncVarDirtyBits |= positionBit;
            status.Network_currentMana = 30f;
            ulong otherBits = status.syncVarDirtyBits;
            status.Network_currentHealth = 2.01f;
            RunPinnedTick(effect);
            Assert.Equal(otherBits, status.syncVarDirtyBits);
        }

        // (f) 設定を無効にすると、チャンクで立った bit も立ったままにする。無効の間は
        // 何も観察しないので、有効化し直した直後の最初の上げも（参加者が何を持っているか
        // 不明なため）送信が確認できるまで残す。
        [Fact]
        public void ADisabledOptionTouchesNothingAndLearnsNothing()
        {
            var effect = NewEffect();
            var status = effect.victim.Status;
            status.Network_currentHealth = 0.01f;
            RunPinnedTick(effect);
            Deliver(status);
            RunPinnedTick(effect);
            NativePerfOptions.HeroKnockoutHealthIdle = false;
            status.Network_currentHealth = 2.01f;
            RunPinnedTick(effect);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            NativePerfOptions.HeroKnockoutHealthIdle = true;
            // 印は無効の間に捨てられているので、最初の上げは残す。
            RunPinnedTick(effect);
            Assert.NotEqual(0UL, status.syncVarDirtyBits & HealthBit);
            Deliver(status);
            RunPinnedTick(effect);
            status.Network_currentHealth = 2.01f;
            RunPinnedTick(effect);
            Assert.Equal(0UL, status.syncVarDirtyBits & HealthBit);
        }

        // (g) パッチ先の型がないとき TryPrepare は false を返し（この修正だけが外れて、
        // 残りの MOD は動き続ける）、警告は1回だけ出る（2回目の失敗では出ない）。
        [Fact]
        public void AMissingEffectTypeFailsPrepareAndWarnsOnce()
        {
            var type = typeof(NativeHeroKnockoutHealthIdlePerf);
            type.GetField("_target", Hidden).SetValue(null, null);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            int warnings = Log.Warnings.Count;
            Assert.False(NativeHeroKnockoutHealthIdlePerf.TryPrepare("Se_HeroKnockedOut_NotInThisGame"));
            Assert.Equal(warnings + 1, Log.Warnings.Count);
            Assert.Contains("hero knockout health idle", Log.Warnings[Log.Warnings.Count - 1]);
            Assert.False(NativeHeroKnockoutHealthIdlePerf.TryPrepare("Se_HeroKnockedOut_NotInThisGame"));
            Assert.Equal(warnings + 1, Log.Warnings.Count);
        }
    }
}
