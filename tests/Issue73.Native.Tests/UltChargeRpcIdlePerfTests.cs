using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#841）満チャージのウルトの無意味なチャージ再送を省く NativeUltChargeRpcIdlePerf の検証。
    /// 境界（NativeBoundary.cs）の SkillTrigger が Prefix の読む欄（configs・currentCharges・type）
    /// を持つため、判定は実ソースの Prefix を反射で直接呼んで確かめる。本体 ChargeUltimate の
    /// 状態変化は currentCharges[0] &lt; configs[0].maxCharges のループ内でだけ起きるので、
    /// 本体を省く（false）のは「設定 ON・サーバ上・Ultimate・チャージ数が maxCharges 以上」が
    /// すべて揃ったときだけ。それ以外（チャージ中・Normal・サーバでない・configs か charges が
    /// null か空・設定 OFF・例外後の _disabled）はすべて true で元の処理を通す。
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

        // Prefix の静的欄（_disabled・_warned）は例外で一度立つと戻らないので毎回初期化し、
        // 設定とサーバ状態はテストごとに並べ替える。呼び出し時点の値を返すので finally で戻す。
        private static (bool Option, bool Server) Arm(bool server = true, bool option = true)
        {
            var saved = (Option: NativePerfOptions.UltChargeRpcIdle, Server: Mirror.NetworkServer.active);
            typeof(NativeUltChargeRpcIdlePerf).GetField("_disabled", Hidden).SetValue(null, false);
            typeof(NativeUltChargeRpcIdlePerf).GetField("_warned", Hidden).SetValue(null, false);
            NativePerfOptions.UltChargeRpcIdle = option;
            Mirror.NetworkServer.active = server;
            return saved;
        }

        private static void Restore((bool Option, bool Server) saved)
        {
            NativePerfOptions.UltChargeRpcIdle = saved.Option;
            Mirror.NetworkServer.active = saved.Server;
            typeof(NativeUltChargeRpcIdlePerf).GetField("_disabled", Hidden).SetValue(null, false);
        }

        private static bool RunPrefix(SkillTrigger skill)
            => (bool)typeof(NativeUltChargeRpcIdlePerf).GetMethod("Prefix", Hidden).Invoke(null, new object[] { skill });

        [Fact]
        public void PrefixSkipsOnlyUltimatesAlreadyAtMaxCharges()
        {
            var saved = Arm();
            try
            {
                Assert.False(RunPrefix(Ultimate(3, 3))); // ちょうど満チャージ → 再送だけの本体を省く
                Assert.False(RunPrefix(Ultimate(2, 5))); // 上限が縮んで超過した窓も同じ省く判定
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
                Assert.False(RunPrefix(Ultimate(3, 3))); // ON に戻すと省く
            }
            finally { Restore(saved); }
        }

        // Prefix 内の例外は本体を止めない: その呼び出しは本体に戻し、警告を1回出し、以降の呼び出しを
        // 全て本体へ戻す（_disabled）。configs[0] が null だと maxCharges 読み取りで例外になる。
        [Fact]
        public void AnExceptionInsideThePrefixLatchesBackToTheOriginal()
        {
            var saved = Arm();
            try
            {
                var broken = Ultimate(3, 3);
                broken.configs = new TriggerConfig[] { null };
                Assert.True(RunPrefix(broken)); // 例外の起きた呼び出し自体は本体に戻す
                Assert.Contains(Log.Warnings, w => w.Contains("ultimate charge idle"));
                Assert.True(RunPrefix(Ultimate(3, 3))); // 例外後は満チャージでも本体
                Assert.True(RunPrefix(broken));
            }
            finally { Restore(saved); }
        }
    }
}
