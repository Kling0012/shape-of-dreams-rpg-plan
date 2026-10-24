using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// 満チャージの Ultimate も、本体の権威値の再通知を通す。
    /// ホストの値だけからゲストの受信状態を判断して省略しない。
    /// </summary>
    public sealed class UltChargeRpcIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static SkillTrigger Ultimate(int maxCharges, int charges)
            => new SkillTrigger
            {
                type = SkillType.Ultimate,
                configs = new[] { new TriggerConfig { maxCharges = maxCharges } },
                currentCharges = new[] { charges },
            };

        // 設定とサーバ状態を呼び出し前の値へ戻す。
        private static (bool Option, bool Server) Arm(bool server = true, bool option = true)
        {
            var saved = (Option: NativePerfOptions.UltChargeRpcIdle, Server: Mirror.NetworkServer.active);
            typeof(NativeUltChargeRpcIdlePerf).GetField("_warned", Hidden).SetValue(null, false);
            NativePerfOptions.UltChargeRpcIdle = option;
            Mirror.NetworkServer.active = server;
            return saved;
        }

        private static void Restore((bool Option, bool Server) saved)
        {
            NativePerfOptions.UltChargeRpcIdle = saved.Option;
            Mirror.NetworkServer.active = saved.Server;
        }

        private static bool RunPrefix(SkillTrigger skill)
            => (bool)typeof(NativeUltChargeRpcIdlePerf).GetMethod("Prefix", Hidden).Invoke(null, new object[] { skill });

        [Fact]
        public void PrefixKeepsUltimatesAlreadyAtMaxCharges()
        {
            var saved = Arm();
            try
            {
                Assert.True(RunPrefix(Ultimate(3, 3))); // 満チャージでも本体の再通知を通す
                Assert.True(RunPrefix(Ultimate(2, 5))); // 上限超過でも本体へ
                Assert.True(RunPrefix(Ultimate(3, 2)));  // 1つ足りない → ループに実変化があるので本体
                Assert.True(RunPrefix(Ultimate(3, 0)));  // 空からの充電も本体
            }
            finally { Restore(saved); }
        }

        [Fact]
        public void PrefixRunsTheOriginalForNormalSkillsEvenAtMaxCharges()
        {
            var saved = Arm();
            try
            {
                var normal = Ultimate(3, 3);
                normal.type = SkillType.Normal;
                Assert.True(RunPrefix(normal)); // Ultimate 以外の再送判定は本体任せ
            }
            finally { Restore(saved); }
        }

        [Fact]
        public void PrefixRunsTheOriginalWhenNotOnTheServer()
        {
            var saved = Arm(server: false);
            try
            {
                Assert.True(RunPrefix(Ultimate(3, 3))); // サーバでなければ本体（[Server] 警告も本体任せ）
            }
            finally { Restore(saved); }
        }

        [Fact]
        public void PrefixRunsTheOriginalWithoutConfigsOrCharges()
        {
            var saved = Arm();
            try
            {
                var noConfigs = Ultimate(3, 3);
                noConfigs.configs = null;
                var emptyConfigs = Ultimate(3, 3);
                emptyConfigs.configs = new TriggerConfig[0];
                var noCharges = Ultimate(3, 3);
                noCharges.currentCharges = null;
                var emptyCharges = Ultimate(3, 3);
                emptyCharges.currentCharges = new int[0];
                Assert.True(RunPrefix(noConfigs));      // 配列がなければ装備前の初期状態 → 本体
                Assert.True(RunPrefix(emptyConfigs));
                Assert.True(RunPrefix(noCharges));
                Assert.True(RunPrefix(emptyCharges));
            }
            finally { Restore(saved); }
        }

        [Fact]
        public void PrefixRunsTheOriginalWhileTheSettingIsOff()
        {
            var saved = Arm(option: false);
            try
            {
                Assert.True(RunPrefix(Ultimate(3, 3))); // 設定 OFF なら満チャージでも本体
                NativePerfOptions.UltChargeRpcIdle = true;
                Assert.True(RunPrefix(Ultimate(3, 3))); // ON に戻しても本体へ
            }
            finally { Restore(saved); }
        }

        // 壊れた config の扱いも本体へ戻す。
        [Fact]
        public void ABrokenConfigStillDefersToTheOriginal()
        {
            var saved = Arm();
            try
            {
                var broken = Ultimate(3, 3);
                broken.configs = new TriggerConfig[] { null };
                Assert.True(RunPrefix(broken)); // Prefix は config を読まず本体へ戻す
                Assert.True(RunPrefix(Ultimate(3, 3))); // 満チャージも本体へ
                Assert.True(RunPrefix(broken));
            }
            finally { Restore(saved); }
        }
    }
}
