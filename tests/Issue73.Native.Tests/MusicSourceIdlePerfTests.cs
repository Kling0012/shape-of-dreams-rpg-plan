using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1124）BGM 音源への同じ音量・ピッチの毎ロジックtick再書き込みを省く
    /// NativeMusicSourceIdlePerf の GuardedVolume/GuardedPitch の検証。書き込み回数は
    /// 境界（NativeBoundary.cs）の模倣 AudioSource のカウンタ（VolumeWrites/PitchWrites、
    /// 音源インスタンスごとの欄）で数える。各テストは自分が作った音源のカウンタだけを
    /// 見るので、xUnit が既定でテストクラスを並列に走らせても数が混ざらない。確かめる点:
    /// (1) 初回（キャッシュ NaN）は必ず書く、(2) 同値の再呼び出しは
    /// 書き込みを省く、(3) 異値は書く、(4) 設定が無効のときは同値でも書いてキャッシュを
    /// 更新する（有効へ戻したあと実際の値とずれない）、(5) 音源ごとのキャッシュは独立、
    /// (6) 2つの対象メソッドの片方だけが付け替わった状態では毎回書く（どちらのメソッドの
    /// 書き込みもガードを通るまでは省かない。この間もキャッシュは更新される）。
    /// Transpiler 本体（IL の付け替え）はゲーム内の Harmony 適用時にしか走らないため、
    /// ここではガード関数を直接呼ぶ。フラグ（_logicPatched/_fadePatched）は本来 Transpiler
    /// が立てるが、テストでは反射で直接制御する。
    /// </summary>
    public sealed class MusicSourceIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly bool _savedOption;

        // パッチ側の静的な欄を毎テストで初期化する。フラグは両方立てて（実機で2メソッド
        // とも付け替わった状態）、設定は既定の有効とする。Dispose で設定を戻し、フラグは
        // 適用前の false へ戻す。カウンタは音源インスタンスごとの欄なので戻す必要がない
        // （毎テスト新しい音源を作り、その音源の欄はゼロから始まる。キャッシュ表の欄も
        // 毎テスト新規になる）。
        // この静的な欄（設定と両フラグ）を書くテストはこのアセンブリ内ではこのクラスだけ
        // （設定を映す ApplyNativePerfConfig を呼ぶテストもない）なので、並列クラスが
        // 途中で値を変える筋道はない。今後この欄を書くテストを足すときは、このクラスと
        // 同じ [Collection] に入れて直列に走らせること。
        public MusicSourceIdlePerfTests()
        {
            _savedOption = NativePerfOptions.MusicSourceIdle;
            NativePerfOptions.MusicSourceIdle = true;
            SetFlags(true, true);
        }

        public void Dispose()
        {
            NativePerfOptions.MusicSourceIdle = _savedOption;
            SetFlags(false, false);
        }

        private static void SetFlags(bool logic, bool fade)
        {
            var type = typeof(NativeMusicSourceIdlePerf);
            type.GetField("_logicPatched", Hidden).SetValue(null, logic);
            type.GetField("_fadePatched", Hidden).SetValue(null, fade);
        }

        private static void GuardedVolume(AudioSource source, float value)
            => typeof(NativeMusicSourceIdlePerf).GetMethod("GuardedVolume", Hidden)
                .Invoke(null, new object[] { source, value });

        private static void GuardedPitch(AudioSource source, float value)
            => typeof(NativeMusicSourceIdlePerf).GetMethod("GuardedPitch", Hidden)
                .Invoke(null, new object[] { source, value });

        // (1)(2)(3) 初回は必ず書き、同値の再呼び出しだけを省き、異値は書く。音量と
        // ピッチの両方で確かめる（キャッシュも別の欄）。
        [Fact]
        public void TheGuardWritesFirstAndDifferentValuesAndSkipsOnlySameValues()
        {
            var source = new AudioSource();
            const float v = 0.37f;
            const float p = 1.29f;

            GuardedVolume(source, v);
            GuardedPitch(source, p);
            Assert.Equal(1, source.VolumeWrites); // 初回（キャッシュ NaN）は必ず書く
            Assert.Equal(1, source.PitchWrites);
            Assert.Equal(v, source.volume);
            Assert.Equal(p, source.pitch);

            GuardedVolume(source, v);
            GuardedPitch(source, p);
            Assert.Equal(1, source.VolumeWrites); // 同値の2回目は省く
            Assert.Equal(1, source.PitchWrites);

            GuardedVolume(source, 0.8f);
            GuardedPitch(source, 0.9f);
            Assert.Equal(2, source.VolumeWrites); // 異値は書く
            Assert.Equal(2, source.PitchWrites);
            Assert.Equal(0.8f, source.volume);
            Assert.Equal(0.9f, source.pitch);
        }

        // (4) 設定が無効のときは同値でも毎回書き、キャッシュも更新する。有効へ戻した
        // あとの同値書き込みは省けるが、その間も実値は書いた値のままなのでずれない。
        [Fact]
        public void TheGuardWritesSameValuesWhileDisabledAndKeepsTheCacheFresh()
        {
            var source = new AudioSource();
            NativePerfOptions.MusicSourceIdle = false;

            GuardedVolume(source, 0.5f);
            GuardedVolume(source, 0.5f);
            GuardedPitch(source, 1.1f);
            GuardedPitch(source, 1.1f);
            Assert.Equal(2, source.VolumeWrites); // 無効の間は同値でも毎回書く
            Assert.Equal(2, source.PitchWrites);
            Assert.Equal(0.5f, source.volume);
            Assert.Equal(1.1f, source.pitch);

            NativePerfOptions.MusicSourceIdle = true;
            GuardedVolume(source, 0.5f);
            GuardedPitch(source, 1.1f);
            Assert.Equal(2, source.VolumeWrites); // 戻しても実際の値とずれない
            Assert.Equal(2, source.PitchWrites);
            GuardedVolume(source, 0.75f);
            GuardedPitch(source, 0.95f);
            Assert.Equal(3, source.VolumeWrites); // 異値は引き続き書く
            Assert.Equal(3, source.PitchWrites);
        }

        // (6) 2つの対象メソッド（LogicUpdate/DoCrossFade）の片方だけが付け替わった状態では
        // 毎回書く（付け替わっていない側の直接書き込みをキャッシュが見られないため）。
        // 逆向きも含めて両方の半適用状態で確かめる。この間もキャッシュは更新されるので、
        // 両方揃った後に同値を省いても実値とずれない。
        [Fact]
        public void TheGuardWritesEveryCallWhileOnlyOneMethodIsRepointed()
        {
            var source = new AudioSource();
            SetFlags(false, true); // DoCrossFade だけが付け替わった想定
            GuardedVolume(source, 0.5f);
            GuardedVolume(source, 0.5f);
            GuardedPitch(source, 1f);
            GuardedPitch(source, 1f);
            Assert.Equal(2, source.VolumeWrites); // キャッシュ一致でも省かない
            Assert.Equal(2, source.PitchWrites);

            SetFlags(true, false); // LogicUpdate だけが付け替わった想定（逆向きの半適用）
            GuardedVolume(source, 0.5f);
            GuardedPitch(source, 1f);
            Assert.Equal(3, source.VolumeWrites); // 逆向きでもやはり毎回書く
            Assert.Equal(3, source.PitchWrites);

            SetFlags(true, true); // 両方揃った
            GuardedVolume(source, 0.5f);
            GuardedPitch(source, 1f);
            Assert.Equal(3, source.VolumeWrites); // この時点で初めて省く（実値は 0.5/1 のまま）
            Assert.Equal(3, source.PitchWrites);
            GuardedVolume(source, 0.6f);
            Assert.Equal(4, source.VolumeWrites);
            Assert.Equal(0.6f, source.volume);
        }

        // (5) 音源ごとのキャッシュは独立する。一方の音源への書き込みは、もう一方の同じ値の
        // 書き込み（初回）を省かせず、省ける状態も崩さない。カウンタも音源ごとの欄なので、
        // どちらの音源に書いたかが分かる。
        [Fact]
        public void TheCacheIsIndependentPerSource()
        {
            var a = new AudioSource();
            var b = new AudioSource();
            GuardedVolume(a, 0.5f);
            GuardedPitch(a, 1f);
            Assert.Equal(1, a.VolumeWrites);
            Assert.Equal(1, a.PitchWrites);
            Assert.Equal(0, b.VolumeWrites); // b にはまだ書いていない
            Assert.Equal(0, b.PitchWrites);

            GuardedVolume(b, 0.5f); // 別の音源は別のキャッシュ（初回 NaN）なので書く
            GuardedPitch(b, 1f);
            Assert.Equal(1, b.VolumeWrites);
            Assert.Equal(1, b.PitchWrites);

            GuardedVolume(a, 0.5f); // a のキャッシュは b への書き込みの影響を受けない
            GuardedPitch(a, 1f);
            Assert.Equal(1, a.VolumeWrites);
            Assert.Equal(1, a.PitchWrites);
            Assert.Equal(1, b.VolumeWrites);
            Assert.Equal(1, b.PitchWrites);
            Assert.Equal(0.5f, a.volume);
            Assert.Equal(0.5f, b.volume);
        }
    }
}
