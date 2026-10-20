using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#911）チャット受信ごとのミュート判定（NativeChatMuteAllocPerf）の検証。
    /// Prefix の判決を、模倣の ChatManager.IsPlayerMutedByNetId（NativeBoundary.cs）と
    /// 同じ入力で比べる。本体は allHumanPlayers から最初に一致した送信者を探し、
    /// 見つからなければミュート扱い（true）、見つかればミュート一覧（IsPlayerMuted）の
    /// 結果。ミュート一覧の中身はパッチがネイティブ呼び出しのまま残すので、この境界では
    /// ChatManager.mutedPlayers を差し替えて両方の判決を出す。試す入力は、一覧に無い
    /// netId・一覧にありミュート済み・一覧にあり未ミュート・同じ netId の重複の4つ。
    /// 設定 OFF と失敗後の _disabled では Prefix が true を返して本体に戻る。
    /// </summary>
    public sealed class ChatMuteAllocPerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool RunPrefix(ChatManager chat, uint playerNetId, out bool result)
        {
            object[] args = new object[] { chat, playerNetId, false };
            bool runNative = (bool)typeof(NativeChatMuteAllocPerf).GetMethod("Prefix", Hidden)
                .Invoke(null, args);
            result = (bool)args[2];
            return runNative;
        }

        // パッチ適用後と同じ呼び出し形: Prefix が true を返したときだけ本体が走る。
        private static bool PatchedIsPlayerMutedByNetId(ChatManager chat, uint playerNetId)
        {
            if (RunPrefix(chat, playerNetId, out bool result)) return chat.IsPlayerMutedByNetId(playerNetId);
            return result;
        }

        private static DewPlayer NewPlayer(uint netId)
            => new DewPlayer { netId = netId };

        // allHumanPlayers を差し替えて、ほかのテストへの影響を残さない。
        private static void WithPlayers(Action run, params DewPlayer[] players)
        {
            var saved = new List<DewPlayer>(DewPlayer.allHumanPlayers);
            DewPlayer.allHumanPlayers.Clear();
            DewPlayer.allHumanPlayers.AddRange(players);
            try { run(); }
            finally
            {
                DewPlayer.allHumanPlayers.Clear();
                DewPlayer.allHumanPlayers.AddRange(saved);
            }
        }

        // 設定と静的状態を保存・復元して、ほかのテストへの影響を残さない。
        private static void WithPatch(bool optionEnabled, Action run)
        {
            var type = typeof(NativeChatMuteAllocPerf);
            var disabled = type.GetField("_disabled", Hidden);
            var warned = type.GetField("_warned", Hidden);
            Assert.NotNull(disabled);
            Assert.NotNull(warned);
            object savedDisabled = disabled.GetValue(null);
            object savedWarned = warned.GetValue(null);
            bool savedOption = NativePerfOptions.ChatMuteCheckAlloc;
            disabled.SetValue(null, false);
            warned.SetValue(null, false);
            NativePerfOptions.ChatMuteCheckAlloc = optionEnabled;
            try { run(); }
            finally
            {
                disabled.SetValue(null, savedDisabled);
                warned.SetValue(null, savedWarned);
                NativePerfOptions.ChatMuteCheckAlloc = savedOption;
            }
        }

        [Fact]
        public void SenderNotAmongHumanPlayersCountsAsMuted()
        {
            var chat = new ChatManager();
            WithPatch(true, () => WithPlayers(() =>
            {
                // 一覧に無い送信者: 本体はミュート扱い（true）。
                Assert.True(chat.IsPlayerMutedByNetId(999));
                // Prefix は本体を飛ばして（false を返し）同じ判決を __result に入れる。
                Assert.False(RunPrefix(chat, 999, out bool result));
                Assert.True(result);
                Assert.True(PatchedIsPlayerMutedByNetId(chat, 999));
            }, NewPlayer(11)));
        }

        [Fact]
        public void MutedAndUnmutedSendersMatchBodyVerdict()
        {
            var chat = new ChatManager();
            // 一覧に載る2人。ミュート一覧に入れるのは一方だけ。
            DewPlayer muted = NewPlayer(21);
            DewPlayer clean = NewPlayer(22);
            chat.mutedPlayers.Add(muted);
            WithPatch(true, () => WithPlayers(() =>
            {
                // 一覧にありミュート済み: 本体も Prefix も true。
                Assert.True(chat.IsPlayerMutedByNetId(21));
                Assert.False(RunPrefix(chat, 21, out bool mutedResult));
                Assert.True(mutedResult);
                Assert.True(PatchedIsPlayerMutedByNetId(chat, 21));
                // 一覧にあり未ミュート: 本体も Prefix も false。
                Assert.False(chat.IsPlayerMutedByNetId(22));
                Assert.False(RunPrefix(chat, 22, out bool cleanResult));
                Assert.False(cleanResult);
                Assert.False(PatchedIsPlayerMutedByNetId(chat, 22));
            }, muted, clean));
        }

        [Fact]
        public void DuplicateNetIdUsesFirstMatchLikeBody()
        {
            var chat = new ChatManager();
            // 同じ netId を持つ2人。ミュート一覧に入れるのは先頭に置く方だけ。
            DewPlayer firstMuted = NewPlayer(31);
            DewPlayer firstClean = NewPlayer(31);
            chat.mutedPlayers.Add(firstMuted);
            WithPatch(true, () => WithPlayers(() =>
            {
                // 先頭がミュート済みの並び: 本体の List.Find と同じ最初一致で true。
                Assert.True(chat.IsPlayerMutedByNetId(31));
                Assert.False(RunPrefix(chat, 31, out bool firstResult));
                Assert.True(firstResult);
                Assert.True(PatchedIsPlayerMutedByNetId(chat, 31));
                // 重複の順序を入れ替えて先頭を未ミュートにする: 両方とも最初一致の false。
                DewPlayer.allHumanPlayers.Clear();
                DewPlayer.allHumanPlayers.AddRange(new[] { firstClean, firstMuted });
                Assert.False(chat.IsPlayerMutedByNetId(31));
                Assert.False(RunPrefix(chat, 31, out bool secondResult));
                Assert.False(secondResult);
                Assert.False(PatchedIsPlayerMutedByNetId(chat, 31));
            }, firstMuted, firstClean));
        }

        [Fact]
        public void OptionDisabledRunsNativeBody()
        {
            var chat = new ChatManager();
            WithPatch(false, () => WithPlayers(() =>
            {
                // 設定 OFF: Prefix は true を返して本体に戻し、判決は本体と一致する。
                Assert.True(RunPrefix(chat, 41, out bool result));
                Assert.False(result);
                Assert.Equal(chat.IsPlayerMutedByNetId(41), PatchedIsPlayerMutedByNetId(chat, 41));
                // 一覧に無い送信者でも本体に戻る（ミュート扱いは本体が決める）。
                Assert.True(RunPrefix(chat, 999, out bool absentResult));
                Assert.False(absentResult);
                Assert.True(PatchedIsPlayerMutedByNetId(chat, 999));
            }, NewPlayer(41)));
        }

        [Fact]
        public void DisabledAfterFailureRunsNativeBody()
        {
            var chat = new ChatManager();
            WithPatch(true, () => WithPlayers(() =>
            {
                // 実行中の失敗で _disabled が立った後: Prefix は true を返して本体に戻す。
                typeof(NativeChatMuteAllocPerf).GetField("_disabled", Hidden).SetValue(null, true);
                Assert.True(RunPrefix(chat, 51, out bool result));
                Assert.False(result);
                Assert.Equal(chat.IsPlayerMutedByNetId(51), PatchedIsPlayerMutedByNetId(chat, 51));
            }, NewPlayer(51)));
        }
    }
}
