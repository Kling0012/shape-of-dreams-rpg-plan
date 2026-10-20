using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#915）混沌の神殿で選んだ報酬の解決キャッシュ（ShrineChaosChoiceCache と5つの
    /// Prefix）の検証。境界（ChaosShrineHitScanPerfBoundary.cs）の転写 Shrine_Chaos は
    /// 本体（Dew.Core/Shrine_Chaos.cs）と同じ連鎖 rewards[guid][int.Parse(GetCustomData(guid))]
    /// で報酬を解決する。パッチ適用後と同じ呼び出し形（Prefix が true を返したときだけ
    /// 本体が走る）の結果が、常に転写本体の結果と一致することを確かめる:
    /// (a) 報酬型ごとのテキスト・色・対象型（最初の呼び出しと2回目のキャッシュ命中の両方）、
    /// (b) customData の差し替え・報酬配列の差し替え・プレイヤーの切り替えで解決し直すこと、
    /// (c) 解析できない・範囲外の値と鍵の消失は本体に任せること（例外の型も本体と同じ）、
    /// (d) GetTargetInfo の2つの多重定義（isLevelUpEnabled が false のときと端数の切り捨て、
    ///     player 引数を使わないこと）、(e) 設定 OFF と owner が null のときは本体に戻り、
    ///     owner が null でもキャッシュを無効にしないこと。
    /// </summary>
    public sealed class ChaosShrineChoiceCachePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Type[] PatchTypes =
        {
            typeof(NativeShrineChaosIndicatorTextPerf),
            typeof(NativeShrineChaosIndicatorColorPerf),
            typeof(NativeShrineChaosTargetTypesPerf),
            typeof(NativeShrineChaosSkillTargetInfoPerf),
            typeof(NativeShrineChaosGemTargetInfoPerf),
        };

        private readonly object[] _savedDisabled = new object[PatchTypes.Length];
        private readonly object[] _savedWarned = new object[PatchTypes.Length];
        private readonly bool _savedOption;
        private readonly DewPlayer _savedLocal;

        // Prefix は静的な _disabled/_warned と NativePerfOptions.ChaosShrineChoice と
        // DewPlayer.local を読む。毎テスト解除・復元して、ほかのテストへの影響を残さない。
        // キャッシュ本体（ConditionalWeakTable）は神殿インスタンスを鍵にするため、
        // 毎テスト新しい Shrine_Chaos を使えば持ち越しがない。
        public ChaosShrineChoiceCachePerfTests()
        {
            for (int i = 0; i < PatchTypes.Length; i++)
            {
                _savedDisabled[i] = PatchTypes[i].GetField("_disabled", Hidden).GetValue(null);
                _savedWarned[i] = PatchTypes[i].GetField("_warned", Hidden).GetValue(null);
                PatchTypes[i].GetField("_disabled", Hidden).SetValue(null, false);
                PatchTypes[i].GetField("_warned", Hidden).SetValue(null, false);
            }
            _savedOption = NativePerfOptions.ChaosShrineChoice;
            _savedLocal = DewPlayer.local;
            NativePerfOptions.ChaosShrineChoice = true;
        }

        public void Dispose()
        {
            for (int i = 0; i < PatchTypes.Length; i++)
            {
                PatchTypes[i].GetField("_disabled", Hidden).SetValue(null, _savedDisabled[i]);
                PatchTypes[i].GetField("_warned", Hidden).SetValue(null, _savedWarned[i]);
            }
            NativePerfOptions.ChaosShrineChoice = _savedOption;
            DewPlayer.local = _savedLocal;
        }

        // ---- Prefix の実行（パッチ適用後と同じ呼び出し形の前提） ----

        private static bool PrefixText(Shrine_Chaos shrine, out string result)
        {
            object[] args = new object[] { shrine, null };
            bool run = (bool)typeof(NativeShrineChaosIndicatorTextPerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            result = (string)args[1];
            return run;
        }

        private static bool PrefixColor(Shrine_Chaos shrine, out UnityEngine.Color result)
        {
            object[] args = new object[] { shrine, default(UnityEngine.Color) };
            bool run = (bool)typeof(NativeShrineChaosIndicatorColorPerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            result = (UnityEngine.Color)args[1];
            return run;
        }

        private static bool PrefixTargetTypes(Shrine_Chaos shrine, DewPlayer player, out EditSkillTargetType result)
        {
            object[] args = new object[] { shrine, player, EditSkillTargetType.None };
            bool run = (bool)typeof(NativeShrineChaosTargetTypesPerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            result = (EditSkillTargetType)args[2];
            return run;
        }

        private static bool PrefixSkillInfo(Shrine_Chaos shrine, DewPlayer player, SkillTrigger target, out EditSkillTargetInfo result)
        {
            object[] args = new object[] { shrine, player, target, null };
            bool run = (bool)typeof(NativeShrineChaosSkillTargetInfoPerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            result = (EditSkillTargetInfo)args[3];
            return run;
        }

        private static bool PrefixGemInfo(Shrine_Chaos shrine, DewPlayer player, Gem target, out EditSkillTargetInfo result)
        {
            object[] args = new object[] { shrine, player, target, null };
            bool run = (bool)typeof(NativeShrineChaosGemTargetInfoPerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            result = (EditSkillTargetInfo)args[3];
            return run;
        }

        // パッチ適用後と同じ呼び出し形: Prefix が true を返したときだけ本体（転写）が走る。
        private static string PatchedText(Shrine_Chaos shrine)
            => PrefixText(shrine, out var s) ? shrine.GetEditSkillIndicatorRawText() : s;

        private static UnityEngine.Color PatchedColor(Shrine_Chaos shrine)
            => PrefixColor(shrine, out var c) ? shrine.GetEditSkillIndicatorColor() : c;

        private static EditSkillTargetType PatchedTargetTypes(Shrine_Chaos shrine, DewPlayer player)
            => PrefixTargetTypes(shrine, player, out var t) ? shrine.GetTargetTypes(player) : t;

        private static EditSkillTargetInfo PatchedSkillInfo(Shrine_Chaos shrine, DewPlayer player, SkillTrigger target)
            => PrefixSkillInfo(shrine, player, target, out var i) ? shrine.GetTargetInfo(player, HeroSkillLocation.Q, target) : i;

        private static EditSkillTargetInfo PatchedGemInfo(Shrine_Chaos shrine, DewPlayer player, Gem target)
            => PrefixGemInfo(shrine, player, target, out var i) ? shrine.GetTargetInfo(player, default(GemLocation), target) : i;

        // ---- 準備: 本体の OnLateStartServer / EnterEditSkill と同じ形で状態を作る ----

        private static DewPlayer Player(string guid) => new DewPlayer { guid = guid };

        private static ChaosReward Reward(ChaosRewardType type, float quantity)
            => new ChaosReward { type = type, quantity = quantity };

        private static Shrine_Chaos Shrine(DewPlayer player, params ChaosReward[] rewards)
        {
            var shrine = new Shrine_Chaos();
            shrine.rewards[player.guid] = rewards;
            return shrine;
        }

        private static SkillTrigger SkillOf(DewPlayer owner, int level, bool levelUpEnabled)
            => new SkillTrigger { level = level, isLevelUpEnabled = levelUpEnabled, owner = new Hero { owner = owner } };

        private static Gem GemOf(DewPlayer owner, int quality)
            => new Gem { quality = quality, owner = new Hero { owner = owner } };

        private static void AssertSameInfo(EditSkillTargetInfo expected, EditSkillTargetInfo actual)
        {
            Assert.Equal(expected.nextLevel, actual.nextLevel);
            Assert.Equal(expected.rejectReasonRawText, actual.rejectReasonRawText);
        }

        // (a) 報酬型ごと: テキスト・色・対象型のすべてで、最初の呼び出し（キャッシュに載る）と
        // 2回目（キャッシュ命中）の両方が転写本体と一致する。編集系ではない報酬型は
        // null / default / None を返す分岐である。引数は ChaosRewardType の値（0〜7）。
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void EachRewardTypeMatchesTheBody(int typeValue)
        {
            var type = (ChaosRewardType)typeValue;
            var player = Player("p1");
            var shrine = Shrine(player, Reward(type, 2f), Reward(ChaosRewardType.UpgradeSkill, 1f));
            shrine.EnterEditSkill(player, "0");
            DewPlayer.local = player;
            for (int call = 1; call <= 2; call++)
            {
                Assert.Equal(shrine.GetEditSkillIndicatorRawText(), PatchedText(shrine));
                Assert.Equal(shrine.GetEditSkillIndicatorColor(), PatchedColor(shrine));
                Assert.Equal(shrine.GetTargetTypes(player), PatchedTargetTypes(shrine, player));
            }
            // 有効な選択状態では Prefix が本体を省いて結果を返す（false）。
            Assert.False(PrefixText(shrine, out _));
            Assert.False(PrefixColor(shrine, out _));
            Assert.False(PrefixTargetTypes(shrine, player, out _));
        }

        // (b) customData の差し替え: 文字列は差し替われば参照も変わるのでキャッシュは外れ、
        // 新しい添字で解決し直す。結果は転写本体と一致する。同じ内容の別参照でも一致する。
        [Fact]
        public void CustomDataSwapReResolvesLikeTheBody()
        {
            var player = Player("p1");
            var shrine = Shrine(player, Reward(ChaosRewardType.UpgradeSkill, 1f), Reward(ChaosRewardType.UpgradeGem, 1f));
            shrine.EnterEditSkill(player, "0");
            DewPlayer.local = player;
            Assert.Equal(EditSkillTargetType.Skill, PatchedTargetTypes(shrine, player));
            shrine.EnterEditSkill(player, "1");
            Assert.Equal(EditSkillTargetType.Gem, PatchedTargetTypes(shrine, player));
            Assert.Equal(shrine.GetTargetTypes(player), PatchedTargetTypes(shrine, player));
            // 同じ内容で参照だけ違う文字列（インターンされない作り方）にも一致する。
            shrine.EnterEditSkill(player, new string('1', 1));
            Assert.Equal(shrine.GetTargetTypes(player), PatchedTargetTypes(shrine, player));
            Assert.Equal(EditSkillTargetType.Gem, PatchedTargetTypes(shrine, player));
        }

        // (b) 報酬配列の差し替え: 同じ鍵で新しい配列を入れたらキャッシュは外れて、
        // 新しい配列の中身で解決し直す。
        [Fact]
        public void RewardArraySwapReResolvesLikeTheBody()
        {
            var player = Player("p1");
            var shrine = Shrine(player, Reward(ChaosRewardType.UpgradeSkill, 1f));
            shrine.EnterEditSkill(player, "0");
            Assert.Equal(EditSkillTargetType.Skill, PatchedTargetTypes(shrine, player));
            shrine.rewards[player.guid] = new[] { Reward(ChaosRewardType.UpgradeGem, 1f) };
            Assert.Equal(EditSkillTargetType.Gem, PatchedTargetTypes(shrine, player));
            Assert.Equal(shrine.GetTargetTypes(player), PatchedTargetTypes(shrine, player));
        }

        // (b) プレイヤーの切り替え: local が替わっても、引数のプレイヤーが替わっても、
        // それぞれの報酬で解決し直して転写本体と一致する。元のプレイヤーへ戻っても一致する。
        [Fact]
        public void PlayerSwitchReResolvesLikeTheBody()
        {
            var a = Player("a");
            var b = Player("b");
            var shrine = new Shrine_Chaos();
            shrine.rewards[a.guid] = new[] { Reward(ChaosRewardType.UpgradeSkill, 3f) };
            shrine.rewards[b.guid] = new[] { Reward(ChaosRewardType.UpgradeGem, 5f) };
            shrine.EnterEditSkill(a, "0");
            shrine.EnterEditSkill(b, "0");
            DewPlayer.local = a;
            Assert.Equal("InGame_SelectSkillToUpgrade", PatchedText(shrine));
            Assert.Equal(shrine.GetEditSkillIndicatorRawText(), PatchedText(shrine));
            DewPlayer.local = b;
            Assert.Equal("InGame_SelectGemToUpgrade", PatchedText(shrine));
            Assert.Equal(shrine.GetEditSkillIndicatorColor(), PatchedColor(shrine));
            // GetTargetTypes は引数のプレイヤーで解決する。交互に呼んでも一致する。
            Assert.Equal(shrine.GetTargetTypes(a), PatchedTargetTypes(shrine, a));
            Assert.Equal(shrine.GetTargetTypes(b), PatchedTargetTypes(shrine, b));
            Assert.Equal(shrine.GetTargetTypes(a), PatchedTargetTypes(shrine, a));
        }

        // (c) 解析できない・範囲外の customData: Prefix は本体に任せる（true）。本体は
        // vanilla と同じ例外を出す（null は ArgumentNullException、非数値は FormatException、
        // 添字の範囲外は IndexOutOfRangeException）。
        [Theory]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("-1")]
        [InlineData("2")]
        public void UnusableCustomDataRunsTheBody(string customData)
        {
            Type expected = customData == null ? typeof(ArgumentNullException)
                : customData == "abc" ? typeof(FormatException)
                : typeof(IndexOutOfRangeException);
            var player = Player("p1");
            var shrine = Shrine(player, Reward(ChaosRewardType.UpgradeSkill, 1f), Reward(ChaosRewardType.UpgradeGem, 1f));
            shrine.EnterEditSkill(player, "0");
            DewPlayer.local = player;
            Assert.False(PrefixText(shrine, out _));
            // 有効な選択から、使えない値へ差し替える: この状態では毎回本体に任せる。
            shrine.EnterEditSkill(player, customData);
            Assert.True(PrefixText(shrine, out _));
            Assert.True(PrefixColor(shrine, out _));
            Assert.True(PrefixTargetTypes(shrine, player, out _));
            Assert.True(PrefixSkillInfo(shrine, player, SkillOf(player, 1, true), out _));
            Assert.True(PrefixGemInfo(shrine, player, GemOf(player, 1), out _));
            Assert.Throws(expected, () => shrine.GetEditSkillIndicatorRawText());
            Assert.Throws(expected, () => shrine.GetEditSkillIndicatorColor());
            Assert.Throws(expected, () => shrine.GetTargetTypes(player));
        }

        // (c) 報酬の鍵が無いプレイヤー（参加前・使用後）: Prefix は本体に任せて、本体は
        // vanilla と同じ KeyNotFoundException を出す。使用後（RemovePlayerAndCheckEmpty で
        // 鍵が外れたあと）も同じである。
        [Fact]
        public void AMissingOrRemovedRewardsKeyRunsTheBody()
        {
            var player = Player("p1");
            var other = Player("p2");
            var shrine = new Shrine_Chaos();
            shrine.rewards[other.guid] = new[] { Reward(ChaosRewardType.UpgradeSkill, 1f) };
            shrine.EnterEditSkill(player, "0");
            DewPlayer.local = player;
            Assert.True(PrefixText(shrine, out _));
            Assert.Throws<KeyNotFoundException>(() => shrine.GetEditSkillIndicatorRawText());
            // 有効な選択のあとに鍵が外れても、以後は毎回本体に任せる。
            shrine.rewards[player.guid] = new[] { Reward(ChaosRewardType.UpgradeSkill, 1f) };
            Assert.False(PrefixTargetTypes(shrine, player, out _));
            shrine.rewards.Remove(player.guid);
            Assert.True(PrefixTargetTypes(shrine, player, out _));
            Assert.Throws<KeyNotFoundException>(() => shrine.GetTargetTypes(player));
            // 引数が null のときも本体に任せる（本体は NullReferenceException）。
            Assert.True(PrefixTargetTypes(shrine, null, out _));
            Assert.Throws<NullReferenceException>(() => shrine.GetTargetTypes(null));
        }

        // (d) スキル側 GetTargetInfo: nextLevel は level + (int)quantity（端数は切り捨て）、
        // isLevelUpEnabled が false のときだけ拒否理由が付く。player 引数は本体も Prefix も
        // 使わないので、所有者と違うプレイヤーを渡しても結果は同じ。
        [Theory]
        [InlineData(true, 4, 2f, 6, null)]
        [InlineData(false, 4, 2f, 6, "InGame_Message_CantUpgradeThisSkill")]
        [InlineData(true, 3, 2.9f, 5, null)]
        [InlineData(false, 0, 0.5f, 0, "InGame_Message_CantUpgradeThisSkill")]
        public void SkillTargetInfoMatchesTheBody(bool levelUpEnabled, int level, float quantity, int expected, string expectedReject)
        {
            var player = Player("p1");
            var shrine = Shrine(player, Reward(ChaosRewardType.UpgradeSkill, quantity));
            shrine.EnterEditSkill(player, "0");
            var target = SkillOf(player, level, levelUpEnabled);
            var bystander = Player("bystander");
            // 最初の呼び出しと2回目（キャッシュ命中）の両方で本体と一致する。
            for (int call = 1; call <= 2; call++)
            {
                EditSkillTargetInfo body = shrine.GetTargetInfo(bystander, HeroSkillLocation.Q, target);
                Assert.False(PrefixSkillInfo(shrine, bystander, target, out var viaPrefix));
                AssertSameInfo(body, viaPrefix);
                Assert.Equal(expected, viaPrefix.nextLevel);
                Assert.Equal(expectedReject, viaPrefix.rejectReasonRawText);
            }
            AssertSameInfo(shrine.GetTargetInfo(bystander, HeroSkillLocation.Q, target), PatchedSkillInfo(shrine, bystander, target));
        }

        // (d) ジェム側 GetTargetInfo: nextLevel は quality + (int)quantity（端数は切り捨て）。
        // こちらには拒否理由が無い。最初の呼び出しと2回目の両方で本体と一致する。
        [Theory]
        [InlineData(3, 2f, 5)]
        [InlineData(1, 0.9f, 1)]
        [InlineData(7, 4.5f, 11)]
        public void GemTargetInfoMatchesTheBody(int quality, float quantity, int expected)
        {
            var player = Player("p1");
            var shrine = Shrine(player, Reward(ChaosRewardType.UpgradeGem, quantity));
            shrine.EnterEditSkill(player, "0");
            var gem = GemOf(player, quality);
            var bystander = Player("bystander");
            for (int call = 1; call <= 2; call++)
            {
                EditSkillTargetInfo body = shrine.GetTargetInfo(bystander, default(GemLocation), gem);
                Assert.False(PrefixGemInfo(shrine, bystander, gem, out var viaPrefix));
                AssertSameInfo(body, viaPrefix);
                Assert.Equal(expected, viaPrefix.nextLevel);
                Assert.Null(viaPrefix.rejectReasonRawText);
            }
            AssertSameInfo(shrine.GetTargetInfo(bystander, default(GemLocation), gem), PatchedGemInfo(shrine, bystander, gem));
        }

        // (e) 設定 OFF: 5つとも常に本体に任せる（true）。パッチ適用後の呼び出し形の
        // 結果も本体と一致する。
        [Fact]
        public void ADisabledOptionRunsTheBody()
        {
            NativePerfOptions.ChaosShrineChoice = false;
            var player = Player("p1");
            var shrine = Shrine(player, Reward(ChaosRewardType.UpgradeGem, 2f));
            shrine.EnterEditSkill(player, "0");
            DewPlayer.local = player;
            var skill = SkillOf(player, 5, false);
            var gem = GemOf(player, 2);
            Assert.True(PrefixText(shrine, out _));
            Assert.True(PrefixColor(shrine, out _));
            Assert.True(PrefixTargetTypes(shrine, player, out _));
            Assert.True(PrefixSkillInfo(shrine, player, skill, out _));
            Assert.True(PrefixGemInfo(shrine, player, gem, out _));
            Assert.Equal(shrine.GetEditSkillIndicatorRawText(), PatchedText(shrine));
            Assert.Equal(shrine.GetEditSkillIndicatorColor(), PatchedColor(shrine));
            Assert.Equal(shrine.GetTargetTypes(player), PatchedTargetTypes(shrine, player));
            AssertSameInfo(shrine.GetTargetInfo(player, HeroSkillLocation.Q, skill), PatchedSkillInfo(shrine, player, skill));
            AssertSameInfo(shrine.GetTargetInfo(player, default(GemLocation), gem), PatchedGemInfo(shrine, player, gem));
        }

        // (e) owner が null のときは本体に任せ、軽量化を無効にせず警告も出さない。
        // 本体は同じ入力で NullReferenceException を出すが、以後の有効な呼び出しでは
        // スキル側・ジェム側ともキャッシュを使える。
        [Fact]
        public void ANullOwnerRunsTheBodyWithoutDisablingTheCache()
        {
            var player = Player("p1");
            var shrine = Shrine(player, Reward(ChaosRewardType.UpgradeSkill, 1f));
            shrine.EnterEditSkill(player, "0");
            DewPlayer.local = player;
            var broken = new SkillTrigger { owner = null };
            var brokenGem = new Gem { owner = null };
            Assert.True(PrefixSkillInfo(shrine, player, broken, out _));
            Assert.True(PrefixGemInfo(shrine, player, brokenGem, out _));
            foreach (var type in new[] { typeof(NativeShrineChaosSkillTargetInfoPerf), typeof(NativeShrineChaosGemTargetInfoPerf) })
            {
                Assert.False((bool)type.GetField("_disabled", Hidden).GetValue(null));
                Assert.False((bool)type.GetField("_warned", Hidden).GetValue(null));
            }
            Assert.Throws<NullReferenceException>(() => shrine.GetTargetInfo(player, HeroSkillLocation.Q, broken));
            Assert.Throws<NullReferenceException>(() => shrine.GetTargetInfo(player, default(GemLocation), brokenGem));
            // 以後の有効な呼び出しではキャッシュを使い、結果は本体と一致する。
            var target = SkillOf(player, 2, true);
            var gem = GemOf(player, 1);
            Assert.False(PrefixSkillInfo(shrine, player, target, out _));
            Assert.False(PrefixGemInfo(shrine, player, gem, out _));
            AssertSameInfo(shrine.GetTargetInfo(player, HeroSkillLocation.Q, target), PatchedSkillInfo(shrine, player, target));
            AssertSameInfo(shrine.GetTargetInfo(player, default(GemLocation), gem), PatchedGemInfo(shrine, player, gem));
            Assert.False(PrefixText(shrine, out _));
        }
    }
}
