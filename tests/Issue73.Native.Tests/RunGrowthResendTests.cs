using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// perf-loop/20261020-0249: ホストは値が変わらなくても鍛錬スタックを定期再送するため、ゲストの
    /// OnRunGrowth は同じ内容の再送を解析し直さない。NativePersistence.targets が ClientSession.RunGrowth.cs
    /// から抽出した本物の受信・リセットで、同一再送・runId の切り替え・Reset 後の再適用・
    /// 壊れた形式の再送・並び順だけ違う再送の結果が従来と同じであることを確かめる。
    /// </summary>
    public sealed class RunGrowthResendTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ClientSession Guest() => new ClientSession { LocalHero = new Hero { netId = 7 } };

        private static void Receive(ClientSession session, string runId, string stacks)
        {
            try
            {
                typeof(ClientSession).GetMethod("OnRunGrowth", Hidden).Invoke(session, new object[]
                {
                    new DreamforgeRunGrowthMsg { protocol = Protocol.Version, heroNetId = 7, runId = runId, stacks = stacks }
                });
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static void Reset(ClientSession session)
        {
            try { typeof(ClientSession).GetMethod("ResetRunGrowthDisplay", Hidden).Invoke(session, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static string RawCache(ClientSession session)
            => (string)typeof(ClientSession).GetField("_growthRawStacks", Hidden).GetValue(session);

        [Fact]
        public void Identical_resend_keeps_version_and_display_unchanged()
        {
            var session = Guest();
            Receive(session, "run-a", "mist.t1=3,husk.t2=1");
            Assert.Equal(1, session.RunGrowthVersion);
            Assert.Equal(3, session.GrowthStacks("mist.t1"));
            Assert.Equal(1, session.GrowthStacks("husk.t2"));

            Receive(session, "run-a", "mist.t1=3,husk.t2=1"); // ホストの定期再送（台帳の版は全員で共用）
            Assert.Equal(1, session.RunGrowthVersion);        // 解析し直さず、画面の作り直しも起きない
            Assert.Equal(3, session.GrowthStacks("mist.t1"));
            Assert.Equal(1, session.GrowthStacks("husk.t2"));
            Assert.Equal("mist.t1=3,husk.t2=1", RawCache(session));
        }

        [Fact]
        public void Run_id_change_rebuilds_the_display_even_with_the_same_stacks()
        {
            var session = Guest();
            Receive(session, "run-a", "mist.t1=3");
            Assert.Equal(1, session.RunGrowthVersion);

            Receive(session, "run-b", "mist.t1=3"); // 台帳の runId が変わった同じ内容
            Assert.Equal(2, session.RunGrowthVersion);
            Assert.Equal(3, session.GrowthStacks("mist.t1"));

            Receive(session, "run-b", "mist.t1=3"); // 新しい runId での同内容の再送は増やさない
            Assert.Equal(2, session.RunGrowthVersion);
        }

        [Fact]
        public void Reset_makes_the_same_payload_apply_again()
        {
            var session = Guest();
            Receive(session, "run-a", "mist.t1=3");
            Assert.Equal(1, session.RunGrowthVersion);

            Reset(session);                            // 接続の張り直し・遠征のやり直し
            Assert.Equal(2, session.RunGrowthVersion); // 表示が消えた分だけ版が増える
            Assert.Equal(0, session.GrowthStacks("mist.t1"));
            Assert.Null(RawCache(session));            // キャッシュも一緒に消える

            Receive(session, "run-a", "mist.t1=3");    // 同じ内容でも作り直す
            Assert.Equal(3, session.RunGrowthVersion);
            Assert.Equal(3, session.GrowthStacks("mist.t1"));
        }

        [Fact]
        public void Malformed_resend_warns_every_time_and_keeps_the_last_good_state()
        {
            var session = Guest();
            Receive(session, "run-a", "mist.t1=3");
            int version = session.RunGrowthVersion;
            int warnings = Log.Warnings.Count;

            Receive(session, "run-a", "mist.t1=3,broken"); // 「=」のない要素
            Receive(session, "run-a", "mist.t1=3,broken"); // 同じ壊れた内容の再送
            Assert.Equal(2, Log.Warnings.Count - warnings); // 適用に失敗した分はキャッシュしないので毎回警告
            Assert.Equal(version, session.RunGrowthVersion);
            Assert.Equal(3, session.GrowthStacks("mist.t1"));

            Receive(session, "run-a", "mist.t1=3"); // 壊れた再送の後も、正しい内容は前回の適用のまま増えない
            Assert.Equal(version, session.RunGrowthVersion);
            Assert.Equal(3, session.GrowthStacks("mist.t1"));
        }

        [Fact]
        public void Reordered_resend_keeps_the_display_and_extends_the_raw_cache()
        {
            var session = Guest();
            Receive(session, "run-a", "mist.t1=3,husk.t2=1");
            Assert.Equal(1, session.RunGrowthVersion);

            Receive(session, "run-a", "husk.t2=1,mist.t1=3"); // 解析結果は同じ並び順違い
            Assert.Equal(1, session.RunGrowthVersion);         // 画面の作り直しは起きない
            Assert.Equal("husk.t2=1,mist.t1=3", RawCache(session)); // この並び順も次回は早期 return で済む

            Receive(session, "run-a", "husk.t2=1,mist.t1=3");
            Assert.Equal(1, session.RunGrowthVersion);
            Assert.Equal(3, session.GrowthStacks("mist.t1"));
            Assert.Equal(1, session.GrowthStacks("husk.t2"));
        }
    }
}
