using System;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    // Issue #510 の再現（続行・協力取引・ホスト版の警告文が言語切替に追従しない）。
    // 3つともリンク・抽出した製品コードが警告を設定した後に表示言語（Loc.Japanese）を切り替え、
    // UI が読む公開プロパティが現在の言語で解決されることを確認する。現状は Loc.T の結果を
    // 文字列で保存しているため、次の出来事まで元の言語のままになる。
    public sealed class Issue510ReproductionTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly bool _japanese = Loc.Japanese;

        public void Dispose() => Loc.Japanese = _japanese;

        private static object Call(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static ClientSession Session() => new ClientSession { Profile = Profile.CreateNew(17) };

        [Fact]
        public void Continue_warning_follows_the_display_language_after_it_is_set()
        {
            var session = Session();
            Loc.Japanese = true;
            Call(session, "WarnUnconfirmedContinue", "issue510");
            Assert.Contains("本体の再開保存を確認できません", session.ContinueWarning);

            Loc.Japanese = false;
            Assert.Contains("Native resume save is unconfirmed", session.ContinueWarning);
        }

        [Fact]
        public void Coop_trade_warning_follows_the_display_language_after_it_is_set()
        {
            var session = Session();
            Loc.Japanese = true;
            Call(session, "DeferCoopSettlement", "issue510");
            Assert.Contains("結果照会を再試行します", session.CoopTradeWarning);

            Loc.Japanese = false;
            Assert.Contains("The result will be checked again", session.CoopTradeWarning);
        }

        [Fact]
        public void Host_version_warning_follows_the_display_language_after_it_is_set()
        {
            var session = Session();
            Loc.Japanese = true;
            // 互換分岐（ClientSession.Hello.cs の OnHello）: 版が違うホストからの返信で警告が設定される。
            Call(session, "OnHello", new DreamforgeHelloMsg { modVer = "issue510-other" });
            Assert.NotNull(session.HostVersionWarning);
            Assert.Contains("ホストの Dreamforge に差があります", session.HostVersionWarning);

            Loc.Japanese = false;
            Assert.Contains("The host's Dreamforge differs", session.HostVersionWarning);
        }
    }
}
