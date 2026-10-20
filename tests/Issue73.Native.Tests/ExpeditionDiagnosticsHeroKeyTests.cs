using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    // #888: DiagnoseExpedition の軽量化（HeroKeyOf の重複計算を1回にまとめる）で出力が変わらないことを、
    // hero が null のときと null 以外のときの両方で確かめる。変更前は hero= 項と Profile.Heroes の検索キーが
    // それぞれ HeroKeyOf(hero) を計算していた。両方が同じ値を使うことを、出力文字列と equipped/missing/polaris
    // （検索で見つけた状態から数える項）で検証する。
    public sealed class ExpeditionDiagnosticsHeroKeyTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string HeroKey = "Hero_Mist"; // Hero_Mist の型名 = HeroKeyOf の値
        private const string Stage = "test-diagnose-hero";

        public ExpeditionDiagnosticsHeroKeyTests() => ResetStatics();
        public void Dispose() => ResetStatics();

        [Fact]
        public void Hero_present_uses_HeroKeyOf_for_output_and_state_search()
        {
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "diag-run" };
            var profile = Profile.CreateNew(888);
            var relic = Loot.RollRelic(new Rng(888), Rarity.Epic, 60);
            profile.Stash.Add(relic);
            Rules.Equip(profile, HeroKey, relic.Uid);
            var hero = new Hero_Mist();
            var session = new ClientSession { Profile = profile, LocalHero = hero };
            var lines = Capture(() => Call(session, "DiagnoseExpedition", Stage));
            var line = Assert.Single(lines);
            // 変更前と同じ値: hero= 項は HeroKeyOf(hero)、続く heroInstance も同じ書式。
            Assert.Contains(" hero=" + ClientSession.HeroKeyOf(hero) + " heroInstance=" + hero.GetInstanceID() + " ", line);
            Assert.Equal(HeroKey, ClientSession.HeroKeyOf(hero));
            // 検索キーも同じ値: HeroKeyOf(hero) の状態を見つけて装備を数える（在庫にあるので missing=0）。
            Assert.Contains(" equipped=1 missing=0 polaris=0 ", line);
        }

        [Fact]
        public void Hero_null_reports_none_and_counts_no_equipment()
        {
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "diag-run" };
            var profile = Profile.CreateNew(888);
            var relic = Loot.RollRelic(new Rng(888), Rarity.Epic, 60);
            profile.Stash.Add(relic);
            Rules.Equip(profile, HeroKey, relic.Uid);
            var session = new ClientSession { Profile = profile, LocalHero = null };
            var lines = Capture(() => Call(session, "DiagnoseExpedition", Stage));
            var line = Assert.Single(lines);
            // 変更前と同じ値: hero が null のとき hero=none で検索も行わない。
            Assert.Contains(" hero=none heroInstance=0 ", line);
            Assert.Contains(" equipped=0 missing=0 polaris=0 ", line);
            Assert.Equal(new[] { HeroKey }, profile.Heroes.Keys.ToArray());
        }

        private static List<string> Capture(Action act)
        {
            var logs = new List<string>();
            Log.InfoSink = logs.Add;
            try { act(); }
            finally { Log.InfoSink = null; }
            return logs.Where(l => l.StartsWith("diag449 " + Stage + " ", StringComparison.Ordinal)).ToList();
        }

        private static void ResetStatics()
        {
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = false;
            UnityEngine.Time.frameCount = 0;
            UnityEngine.Time.unscaledTime = 0;
            InfinityMode.NativeSaveAgreement = false;
            InfinityMode.Available = false;
            NetworkedManagerBase<GameManager>.softInstance = null;
            typeof(ClientSession).GetField("_hostSession", Hidden).SetValue(null, null);
            Log.InfoSink = null;
        }

        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    }
}
