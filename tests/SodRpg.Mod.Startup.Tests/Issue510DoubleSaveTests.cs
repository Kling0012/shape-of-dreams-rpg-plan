using System.Reflection;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // Issue #510 の再現（終了時に保存が2回走る）。DreamforgeMod は製品コードをそのままコンパイルした
    // ものを使い、Unity の終了順序と同じく同じコンポーネントへ OnApplicationQuit → OnDestroy を呼ぶ。
    // 終了時の保存（SaveNow + FlushSaves）は1組だけであるべきで、OnDestroy → Stop → Unwire が
    // もう1組呼ぶ現状は二重保存（シリアライズ・書き込み・.bak 置き換えの重複）として再現される。
    public sealed class Issue510DoubleSaveTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Fact]
        public void Quit_sequence_performs_the_terminal_save_only_once()
        {
            var mod = new DreamforgeMod();
            var session = new ClientSession("issue510-quit", _ => { });
            // Start() が成功した直後と同じ状態: 実行中で、セッションが結び付いている。
            typeof(DreamforgeMod).GetField("_session", Hidden).SetValue(mod, session);
            typeof(DreamforgeMod).GetField("_running", Hidden).SetValue(mod, true);

            // Unity は終了時、同じコンポーネントの OnApplicationQuit を呼んでから OnDestroy を呼ぶ。
            typeof(DreamforgeMod).GetMethod("OnApplicationQuit", Hidden).Invoke(mod, null);
            typeof(DreamforgeMod).GetMethod("OnDestroy", Hidden).Invoke(mod, null);

            Assert.Equal(1, session.SaveNowCount);
            Assert.Equal(1, session.FlushSavesCount);
        }
    }
}
