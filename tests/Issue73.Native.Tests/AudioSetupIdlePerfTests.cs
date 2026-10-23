using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1092）戦闘音を鳴らすたびの同じ音源の初期設定の省略（NativeAudioSetupIdlePerf）の検証。
    /// 本体 Dew.Core r1.4.1.2 逆コンパイルの形に基づく: SetupAudioSource が書く定数は引数
    /// (type, space, rolloff) だけから決まり、優先度だけが force なら 0、それ以外は私的な
    /// GetNextDynamicPriority（設定の分岐はこの私的メソッドの中にあり、無効なら輪を進めず
    /// 1 を返す）から決まる。Prefix は初回・組やマネージャーの変化・非 Game 型を本体に返し、
    /// 同じ組の再呼び出しのときだけ本体を飛ばして優先度を同じ式で代入する。非 Game 型の呼び
    /// 出しはキャッシュを捨て、設定をオフにしている間は世代を上げて以前の記録と照合させない。
    /// 記録は本体が完了した後だけ（Postfix。本体が例外で中断したとき Harmony は Postfix を
    /// 実行しない）に行う。各場合の Prefix の戻り値・priority の値・GetNextDynamicPriority の
    /// 呼び出し回数を確かめる。
    /// </summary>
    public sealed class AudioSetupIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 抽出部分の静的欄を整え、Prepare を通して私的メソッドへの委譲を結び直す
        //（本番では Prepare が Harmony のパッチ適用前に同じことを行う）。
        private static void PreparePatch()
        {
            var type = typeof(NativeAudioSetupIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
            type.GetField("_generation", Hidden).SetValue(null, 1);
            type.GetField("_offBumped", Hidden).SetValue(null, false);
            var prepared = (bool)type.GetMethod("Prepare", Hidden).Invoke(null, null);
            Assert.True(prepared);
        }

        // Prefix を呼び、Harmony と同じ順で Postfix（本体が実行されたときだけ記録する）も走らせる。
        private static bool Play(AudioManager manager, AudioSource source, AudioType type,
            AudioSpaceType space, DewAudioRollOffType rolloff, bool force)
        {
            var prefix = typeof(NativeAudioSetupIdlePerf).GetMethod("Prefix", Hidden);
            Assert.NotNull(prefix);
            object[] args = { manager, source, type, space, rolloff, force, false };
            bool runsOriginal = (bool)prefix.Invoke(null, args);
            var postfix = typeof(NativeAudioSetupIdlePerf).GetMethod("Postfix", Hidden);
            Assert.NotNull(postfix);
            postfix.Invoke(null, new object[] { manager, source, type, space, rolloff, args[6], runsOriginal });
            return runsOriginal;
        }

        // 本体が例外で中断した場合の再現（Harmony はそのとき Postfix を実行しない）。
        private static bool PlayBodyInterrupted(AudioManager manager, AudioSource source, AudioType type,
            AudioSpaceType space, DewAudioRollOffType rolloff, bool force)
        {
            var prefix = typeof(NativeAudioSetupIdlePerf).GetMethod("Prefix", Hidden);
            Assert.NotNull(prefix);
            object[] args = { manager, source, type, space, rolloff, force, false };
            return (bool)prefix.Invoke(null, args);
        }

        [Fact]
        public void FirstCallRunsTheOriginalAndTheSameTupleReplaySkipsItAndAssignsPriority()
        {
            PreparePatch();
            AudioManager.NextDynamicPriorityCalls = 0;
            var manager = new AudioManager();
            var source = new AudioSource();

            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 初回は本体に返す
            Assert.Equal(0, AudioManager.NextDynamicPriorityCalls); // 優先度は本体の役目

            Assert.False(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 同じ組の2回目は本体を飛ばす
            Assert.Equal(1, AudioManager.NextDynamicPriorityCalls); // 私的メソッドは1回だけ
            Assert.Equal(101, source.priority); // 値は私的メソッドの返り値（100 + 呼び出し回数）

            Assert.False(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 3回目も同じ組なら飛ばし続ける
            Assert.Equal(2, AudioManager.NextDynamicPriorityCalls);
            Assert.Equal(102, source.priority); // 優先度は呼び出しのたびに再計算される
        }

        [Fact]
        public void ChangedTupleTypeSpaceRolloffOrManagerRunsTheOriginal()
        {
            PreparePatch();
            var manager = new AudioManager();
            var source = new AudioSource();
            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 初回（記録）
            Assert.False(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 同じ組は省略

            Assert.True(Play(manager, source, AudioType.GameSelf, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 型の変化
            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Global,
                DewAudioRollOffType.Default, false)); // 空間の変化
            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Loud, false)); // ロールオフの変化
            Assert.True(Play(new AudioManager(), source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // マネージャーの変化
        }

        [Fact]
        public void ForceCriticalPriorityAssignsZeroWithoutCallingTheRing()
        {
            PreparePatch();
            AudioManager.NextDynamicPriorityCalls = 0;
            var manager = new AudioManager();
            var source = new AudioSource();
            Assert.True(Play(manager, source, AudioType.GameBoss, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 初回は本体に返す（記録）

            Assert.False(Play(manager, source, AudioType.GameBoss, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, true)); // force でも同じ組なら飛ばす
            Assert.Equal(0, AudioManager.NextDynamicPriorityCalls); // 本体どおり輪は呼ばない
            Assert.Equal(0, source.priority); // 優先度は 0

            Assert.False(Play(manager, source, AudioType.GameBoss, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // force でない呼び出しに戻す
            Assert.Equal(1, AudioManager.NextDynamicPriorityCalls);
            Assert.Equal(101, source.priority);
        }

        [Fact]
        public void NonGameTypeInBetweenDropsTheCacheSoTheNextGameSoundCallRunsTheOriginal()
        {
            PreparePatch();
            var manager = new AudioManager();
            var source = new AudioSource();
            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 戦闘音の組を記録
            Assert.False(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 同じ組は省略

            // プール済みの音源は SFX 型でも使い回される（本体はミキサーグループ等を書き換える）
            Assert.True(Play(manager, source, AudioType.SFX, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false));

            // 古い記録が残っていても同じ組で飛ばさない（記録は捨てられている）
            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false));
            Assert.False(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 書き直された後は再び省略する
        }

        [Fact]
        public void TurningTheSettingOffRunsTheOriginalAndInvalidatesEntriesRecordedBefore()
        {
            PreparePatch();
            var savedOption = NativePerfOptions.AudioSetupIdle;
            try
            {
                var manager = new AudioManager();
                var source = new AudioSource();
                Assert.True(Play(manager, source, AudioType.GameSelf, AudioSpaceType.Normal,
                    DewAudioRollOffType.Default, false)); // 記録
                Assert.False(Play(manager, source, AudioType.GameSelf, AudioSpaceType.Normal,
                    DewAudioRollOffType.Default, false)); // 省略

                NativePerfOptions.AudioSetupIdle = false;
                Assert.True(Play(manager, source, AudioType.GameSelf, AudioSpaceType.Normal,
                    DewAudioRollOffType.Default, false)); // オフの間は常に本体

                NativePerfOptions.AudioSetupIdle = true;
                Assert.True(Play(manager, source, AudioType.GameSelf, AudioSpaceType.Normal,
                    DewAudioRollOffType.Default, false)); // オフの前に残った記録では飛ばさない
                Assert.False(Play(manager, source, AudioType.GameSelf, AudioSpaceType.Normal,
                    DewAudioRollOffType.Default, false)); // 再記録の後は再び省略する
            }
            finally { NativePerfOptions.AudioSetupIdle = savedOption; }
        }

        [Fact]
        public void InterruptedBodyIsNotTreatedAsApplied()
        {
            PreparePatch();
            var manager = new AudioManager();
            var source = new AudioSource();
            Assert.True(PlayBodyInterrupted(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 本体が例外で中断した場合（記録されない）

            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 中断した組でもう一度本体に返す
            Assert.False(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 完了の後は再び省略する
        }

        [Fact]
        public void AThrowingTypeDisablesThePatchFailSoft()
        {
            PreparePatch();
            var manager = new AudioManager();
            var source = new AudioSource();
            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 記録

            // 未知の型: NeedsSetupEverytime が本体と同じ例外を投げるため、修正は警告1回で外れる
            Assert.True(Play(manager, source, (AudioType)99, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false));

            Assert.True(Play(manager, source, AudioType.GameOthers, AudioSpaceType.Normal,
                DewAudioRollOffType.Default, false)); // 同じ組でも飛ばさない（無効化後）
        }
    }
}
