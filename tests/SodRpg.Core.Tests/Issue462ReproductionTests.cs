using System;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    [CollectionDefinition("Native negotiation and level slots", DisableParallelization = true)]
    public sealed class NativeNegotiationAndLevelSlotsCollection { }

    // Issue #462 の再現。Hello とレベル枠の処理は csproj がリンクした製品コードを使う。
    [Collection("Native negotiation and level slots")]
    public sealed class Issue462ReproductionTests
    {
        private readonly ITestOutputHelper _output;
        public Issue462ReproductionTests(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData("version")]
        [InlineData("protocol")]
        [InlineData("content")]
        public void Lobby_only_peer_with_a_mismatch_is_visible_in_host_warnings(string difference)
        {
            var host = new HostAuthority();
            var actor = host.RegisterNegotiation();
            var peer = new DewPlayer { playerName = "issue462-lobby-peer" };
            DewPlayer.lobbyPlayers.Add(peer);
            try
            {
                Assert.DoesNotContain(peer, DewPlayer.gamePlayers);
                var hello = MatchingHello();
                if (difference == "version") hello.modVer = "issue462-other-version";
                if (difference == "protocol") hello.protocol = Protocol.Version - 1;
                if (difference == "content") hello.content = "issue462-other-registry";

                host.ReceiveNegotiation(hello, peer);

                // Reply + mismatch storage prove Hello was accepted before checking the visible list.
                Assert.IsType<DreamforgeHelloMsg>(Assert.Single(actor.ClientMessages));
                var recorded = (System.Collections.Generic.Dictionary<DewPlayer, SodRpg.Core.Game.Txt>)typeof(HostAuthority)
                    .GetField("_versionMismatches", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                Assert.True(recorded.ContainsKey(peer), "Hello must record the mismatch for this lobby peer");
                _output.WriteLine("difference={0}, lobby member=true, game member=false, stored warnings={1}, visible warnings={2}",
                    difference, recorded.Count, HostAuthority.VersionWarnings.Count);
                Assert.Single(HostAuthority.VersionWarnings);
            }
            finally
            {
                DewPlayer.lobbyPlayers.Remove(peer);
                host.DetachNegotiation();
            }
        }

        [Fact]
        public void Matching_lobby_peer_has_no_warning()
        {
            var host = new HostAuthority();
            var actor = host.RegisterNegotiation();
            var peer = new DewPlayer { playerName = "issue462-matching-peer" };
            DewPlayer.lobbyPlayers.Add(peer);
            try
            {
                host.ReceiveNegotiation(MatchingHello(), peer);
                Assert.IsType<DreamforgeHelloMsg>(Assert.Single(actor.ClientMessages));
                Assert.Empty(HostAuthority.VersionWarnings);
            }
            finally
            {
                DewPlayer.lobbyPlayers.Remove(peer);
                host.DetachNegotiation();
            }
        }

        [Fact]
        public void Warning_follows_the_language_toggle_and_a_changed_difference()
        {
            bool japanese = SodRpg.Core.Game.Loc.Japanese;
            var host = new HostAuthority();
            host.RegisterNegotiation();
            var peer = new DewPlayer { playerName = "issue462-toggle-peer" };
            DewPlayer.lobbyPlayers.Add(peer);
            try
            {
                SodRpg.Core.Game.Loc.Japanese = true;
                var hello = MatchingHello();
                hello.modVer = "issue462-other-version";
                host.ReceiveNegotiation(hello, peer);
                Assert.Contains("版が違う", Assert.Single(HostAuthority.VersionWarnings));

                SodRpg.Core.Game.Loc.Japanese = false;
                Assert.Contains("version differs", Assert.Single(HostAuthority.VersionWarnings));

                // 中国語表示でも警告は中国語に出る（#992。修正前は英語に落ちた）。次の確認は英語に戻す。
                SodRpg.Core.Game.Loc.Language = SodRpg.Core.Game.LocLanguage.Chinese;
                Assert.Contains("版本不同", Assert.Single(HostAuthority.VersionWarnings));
                SodRpg.Core.Game.Loc.Japanese = false;

                // 版は同じになったが内容がまだ違う：古い項目を残さず、いまの差を出す。
                hello = MatchingHello();
                hello.content = "issue462-other-registry";
                host.ReceiveNegotiation(hello, peer);
                string text = Assert.Single(HostAuthority.VersionWarnings);
                Assert.DoesNotContain("version differs", text);
                Assert.Contains("content", text);
            }
            finally
            {
                SodRpg.Core.Game.Loc.Japanese = japanese;
                DewPlayer.lobbyPlayers.Remove(peer);
                host.DetachNegotiation();
            }
        }

        [Fact]
        public void Departed_peer_is_excluded_when_the_warning_list_is_rebuilt()
        {
            var host = new HostAuthority();
            host.RegisterNegotiation();
            var peer = new DewPlayer { playerName = "issue462-departed-peer" };
            DewPlayer.lobbyPlayers.Add(peer);
            try
            {
                var hello = MatchingHello();
                hello.modVer = "issue462-other-version";
                host.ReceiveNegotiation(hello, peer);
                Assert.Single(HostAuthority.VersionWarnings);

                // DewPlayer removes the roster entry before raising the native notification.
                DewPlayer.lobbyPlayers.Remove(peer);
                Assert.DoesNotContain(peer, DewPlayer.gamePlayers);
                DewPlayer.onLobbyPlayerRemoved?.Invoke(peer);
                Assert.Empty(HostAuthority.VersionWarnings);

                // OnStateChanged then adds the same player to gamePlayers, without a new Hello.
                DewPlayer.gamePlayers.Add(peer);
                DewPlayer.onGamePlayerAdded?.Invoke(peer);
                Assert.Single(HostAuthority.VersionWarnings);

                DewPlayer.gamePlayers.Remove(peer);
                DewPlayer.onGamePlayerRemoved?.Invoke(peer);
                Assert.Empty(HostAuthority.VersionWarnings);
            }
            finally
            {
                DewPlayer.lobbyPlayers.Remove(peer);
                DewPlayer.gamePlayers.Remove(peer);
                host.DetachNegotiation();
            }
        }

        [Theory]
        [InlineData(20, 5, 5)]
        [InlineData(50, 8, 8)]
        public void Level_slots_recover_on_the_next_host_tick_after_the_hud_cap_is_raised(int level, int ceiling, int expected)
        {
            // Isolate the suspected host-side loss: model only the runtime's 4 -> 8 readiness signal.
            // This does not execute Unity HUD cloning or validate the appearance of its slots.
            int previousCap = LevelEssenceSettings.RuntimeCapLimit;
            bool previousBonus = LevelEssenceSettings.RuntimeBonusAvailable;
            var previousSettings = LevelEssenceSettings.Current;
            float previousTime = UnityEngine.Time.unscaledTime;
            var host = new HostAuthority();
            var hero = new Hero();
            hero.Skill.hero = hero;
            hero.Status.level = level;
            var peer = new DewPlayer { hero = hero };
            var locations = new[] { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R };
            foreach (var location in locations) hero.Skill.SetMaxGemCount(location, 3);
            var settings = LevelEssenceSettings.Create(true, true, true, true, true, 10, ceiling);
            var tick = typeof(HostAuthority).GetMethod("TickLevelGemSlots", BindingFlags.Instance | BindingFlags.NonPublic);
            DewPlayer.gamePlayers.Add(peer);
            try
            {
                LevelEssenceSettings.Current = settings;
                LevelEssenceSettings.RuntimeBonusAvailable = true;
                LevelEssenceSettings.RuntimeCapLimit = 4;
                UnityEngine.Time.unscaledTime = 100;
                tick.Invoke(host, null);
                foreach (var location in locations) Assert.Equal(4, hero.Skill.GetMaxGemCount(location));

                LevelEssenceSettings.RuntimeCapLimit = 8;
                UnityEngine.Time.unscaledTime = 101.01f;
                tick.Invoke(host, null);
                foreach (var location in locations) Assert.Equal(expected, hero.Skill.GetMaxGemCount(location));
                _output.WriteLine("level={0}, requested cap={1}: Q/W/E/R before HUD=4, next eligible host tick={2}",
                    level, ceiling, expected);
            }
            finally
            {
                host.DetachLevelForTest();
                DewPlayer.gamePlayers.Remove(peer);
                LevelEssenceSettings.RuntimeCapLimit = previousCap;
                LevelEssenceSettings.RuntimeBonusAvailable = previousBonus;
                LevelEssenceSettings.Current = previousSettings;
                UnityEngine.Time.unscaledTime = previousTime;
            }
        }

        private static DreamforgeHelloMsg MatchingHello() => new DreamforgeHelloMsg
        {
            protocol = Protocol.Version, modVer = HostAuthority.ModVersion, content = ContentFingerprint.Value,
            continueCheckpoints = true, infinityAvailable = true,
        };
    }
}
