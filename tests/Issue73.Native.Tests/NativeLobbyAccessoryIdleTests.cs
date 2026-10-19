using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#833）変わっていないロビーのアクセサリ再走査の省略（NativeLobbyAccessoryIdle）の検証。
    /// 境界（NativeLobbyAccessoryIdleBoundary.cs）へ転写した本体 PlayLobby_Character.LogicUpdate を
    /// パッチ有効側の本体として動かし、(1) 入力がすべて同じ2回目以降のTickで Prefix が省略に転じ、
    /// model.accessories の参照も UpdateAccessories の呼び出しも起こらないこと、(2) 挙げた入力を
    /// 1つでも変えると本体に戻ること、(3) 本体が走った後の Postfix で記録が撮り直されて再び省略に
    /// 戻ること、(4) 設定OFF・例外のときは常に本体に戻ること、(5) 他MODの Prefix が本体を止めたTick
    /// （__runOriginal が偽）では記録しないこと、を確かめる。
    /// </summary>
    public sealed class NativeLobbyAccessoryIdleTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 触った静的状態（DewPlayer.lobbyPlayers・DewNetworkManager.startSettings・設定・フェイルソフトの旗）を
        // テストごとに戻す。フェイルソフトの _disabled は一度立つと戻らないため、ここで明示的に戻す。
        private sealed class LobbyScope : IDisposable
        {
            private readonly List<DewPlayer> _players = new List<DewPlayer>(DewPlayer.lobbyPlayers);
            private readonly DewNetworkStartSettings _startSettings = DewNetworkManager.startSettings;
            private readonly DewPersistence.GameData _continueData = DewNetworkManager.startSettings.continueData;
            private readonly bool _option = NativePerfOptions.LobbyAccessoryIdle;

            public void Dispose()
            {
                DewPlayer.lobbyPlayers.Clear();
                DewPlayer.lobbyPlayers.AddRange(_players);
                // 参照を戻すだけでは続きデータの汚染が残るため、中身も戻す
                DewNetworkManager.startSettings = _startSettings;
                _startSettings.continueData = _continueData;
                NativePerfOptions.LobbyAccessoryIdle = _option;
                typeof(NativeLobbyAccessoryIdle).GetField("_disabled", Hidden).SetValue(null, false);
            }
        }

        // 記録との比較で読む model.skinType で例外を起こす境界。
        private sealed class ThrowingSkinModel : CharacterModelDisplay
        {
            public override string skinType => throw new InvalidOperationException("skinType boundary");
        }

        // 1ロジックTick。返り値 true = 本体が走った、false = Prefix が省略した。
        // Harmony と同じく、Postfix は省略後も必ず呼ばれる（そのTickの __runOriginal は偽）。
        private static bool Tick(PlayLobby_Character c)
        {
            if (NativeLobbyAccessoryIdle.BeforeLogicUpdate(c))
            {
                c.LogicUpdate(1f / 30f);
                NativeLobbyAccessoryIdle.AfterLogicUpdate(c, true);
                return true;
            }
            NativeLobbyAccessoryIdle.AfterLogicUpdate(c, false);
            return false;
        }

        // 他MODの先行 Prefix が本体を止めたTick。この実装の Prefix は本体を許可した（真を返した）
        // にもかかわらず、本体は走らない（__runOriginal は偽）。
        private static void ForeignCancelledTick(PlayLobby_Character c)
        {
            Assert.True(NativeLobbyAccessoryIdle.BeforeLogicUpdate(c));
            NativeLobbyAccessoryIdle.AfterLogicUpdate(c, false);
        }

        private static DewPlayer Player(string skin, params string[] accessories)
        {
            var p = new DewPlayer { isEveryInfoSet = true, selectedSkin = skin, selectedHeroType = "Hero_Mist" };
            p.ownedItems.Add(skin);
            foreach (string a in accessories)
            {
                p.selectedAccessories.Add(a);
                p.ownedItems.Add(a);
            }
            return p;
        }

        private static PlayLobby_Character Character(params DewPlayer[] players)
        {
            DewPlayer.lobbyPlayers.Clear();
            DewPlayer.lobbyPlayers.AddRange(players);
            return new PlayLobby_Character { playerIndex = 0, model = new CharacterModelDisplay() };
        }

        // 変更前のTickは省略になっていること、変更したTickは本体が走ること、その後の Postfix で
        // 記録が撮り直されて次のTickから再び省略になること、の3点を1組で確かめる。
        private static void AssertChangedInputRuns(PlayLobby_Character c, Action change)
        {
            Assert.False(Tick(c));
            change();
            Assert.True(Tick(c));
            Assert.False(Tick(c));
        }

        [Fact]
        public void StableInputSkipsTheSecondTickAndLeavesTheModelAlone()
        {
            using (new LobbyScope())
            {
                var c = Character(Player("Skin_Mist_Default", "Acc_A", "Acc_B"));
                Assert.True(Tick(c)); // 初回は記録が無いため本体が走る
                Assert.Equal("Skin_Mist_Default", c.model.skinType);
                Assert.Equal(new[] { "Acc_A", "Acc_B" }, c.model.accessories);
                Assert.Equal(1, c.model.UpdateAccessoriesCount);
                List<string> accessories = c.model.accessories;

                Assert.False(Tick(c)); // 入力が1つも変わっていないTickは省略
                Assert.False(Tick(c)); // その後も省略が続く
                Assert.Same(accessories, c.model.accessories); // 本体が作り直さないため参照は同じ
                Assert.Equal(1, c.model.UpdateAccessoriesCount); // UpdateAccessories も呼ばれない
                Assert.Equal("Skin_Mist_Default", c.model.skinType);
            }
        }

        [Fact]
        public void SkinSideInputChangesRunTheBodyAgain()
        {
            using (new LobbyScope())
            {
                var p = Player("Skin_Mist_Default", "Acc_A");
                p.ownedItems.Add("Skin_Mist_Golden"); // あらかじめ持っておく（この時点では履いていない）
                var c = Character(p);
                Assert.True(Tick(c));

                // model.skinType への外部からの書き換え: 次のTickで本体が追従して戻す
                AssertChangedInputRuns(c, () => c.model.skinType = "foreign");
                Assert.Equal("Skin_Mist_Default", c.model.skinType);

                // selectedSkin の変更（所有済み）: スキンが切り替わる
                AssertChangedInputRuns(c, () => p.selectedSkin = "Skin_Mist_Golden");
                Assert.Equal("Skin_Mist_Golden", c.model.skinType);

                // selectedSkin の変更（未所有）: 既定スキンへ戻る
                AssertChangedInputRuns(c, () => p.selectedSkin = "Skin_Mist_Lost");
                Assert.Equal("Skin_Mist_Default", c.model.skinType);

                // selectedSkin の変更（未所有・出力は同じ既定）: 出力が変わらなくても本体は走る
                AssertChangedInputRuns(c, () => p.selectedSkin = "Skin_Mist_Other");
                Assert.Equal("Skin_Mist_Default", c.model.skinType);

                // selectedHeroType の変更: 既定スキンの算出先が変わる
                AssertChangedInputRuns(c, () => p.selectedHeroType = "Hero_Vext");
                Assert.Equal("Skin_Vext_Default", c.model.skinType);

                // isEveryInfoSet が偽: text は forceSetup（""）に戻る
                AssertChangedInputRuns(c, () => p.isEveryInfoSet = false);
                Assert.Equal("", c.model.skinType);
            }
        }

        [Fact]
        public void AccessorySideInputChangesRunTheBodyAgain()
        {
            using (new LobbyScope())
            {
                var p = Player("Skin_Mist_Default", "Acc_A", "Acc_B");
                var c = Character(p);
                Assert.True(Tick(c));

                // selectedAccessories の要素を同じ内容の別参照に入れ替える: 本体は走るが結果は同じため
                // model.accessories は作り直されず、記録だけが新しい参照に更新される
                string copy = new string("Acc_A".ToCharArray());
                AssertChangedInputRuns(c, () => p.selectedAccessories[0] = copy);
                Assert.Equal(1, c.model.UpdateAccessoriesCount);

                // selectedAccessories への追加（所有と一緒に変える）: フィルタ結果が変わり作り直される
                AssertChangedInputRuns(c, () =>
                {
                    p.ownedItems.Add("Acc_C");
                    p.selectedAccessories.Add("Acc_C");
                });
                Assert.Equal(new[] { "Acc_A", "Acc_B", "Acc_C" }, c.model.accessories);
                Assert.Equal(2, c.model.UpdateAccessoriesCount);

                // ownedItems への追加（フィルタ結果は同じ）: 記録が古いため本体に戻るが、書き換えは起きない
                AssertChangedInputRuns(c, () => p.ownedItems.Add("Acc_D"));
                Assert.Equal(2, c.model.UpdateAccessoriesCount);

                // ownedItems のリスト参照の差し替え（同じ内容）: 本体に戻って記録が撮り直される
                AssertChangedInputRuns(c, () => p.ownedItems = new List<string>(p.ownedItems));

                // model.accessories を同じ内容の別リストで差し替える: 本体が走り、記録が新しい参照へ
                // 追従する（SequenceEqual が等しいため作り直しは起きない）
                AssertChangedInputRuns(c, () => c.model.accessories = new List<string> { "Acc_A", "Acc_B", "Acc_C" });
                Assert.Equal(2, c.model.UpdateAccessoriesCount);
            }
        }

        [Fact]
        public void SessionAndRosterInputChangesRunTheBodyAgain()
        {
            using (new LobbyScope())
            {
                var p = Player("Skin_Mist_Default", "Acc_A");
                var c = Character(p);
                Assert.True(Tick(c));

                // continueData が現れる: text が空になりスキンが落ちる
                AssertChangedInputRuns(c, () => DewNetworkManager.startSettings.continueData = new DewPersistence.GameData());
                Assert.Equal("", c.model.skinType);

                // startSettings のインスタンス差し替え: continueData が無くなりスキンが復帰する
                AssertChangedInputRuns(c, () => DewNetworkManager.startSettings = new DewNetworkStartSettings());
                Assert.Equal("Skin_Mist_Default", c.model.skinType);

                // playerIndex が範囲外: スキンもアクセサリも更新しない本体が走り、null の枠として記録される
                AssertChangedInputRuns(c, () => c.playerIndex = 5);
                Assert.Equal("", c.model.skinType);

                // playerIndex が範囲内に戻る: 元のプレイヤーの枠として再開する
                AssertChangedInputRuns(c, () => c.playerIndex = 0);
                Assert.Equal("Skin_Mist_Default", c.model.skinType);

                // ロビー人数の変化: 枠のプレイヤーが同じでも件数が変われば本体に戻る
                AssertChangedInputRuns(c, () => DewPlayer.lobbyPlayers.Add(Player("Skin_Vext_Default")));
                Assert.Equal(1, c.model.UpdateAccessoriesCount);

                // 枠のプレイヤーの差し替え: 同じ内容でも参照が違えば本体に戻る
                AssertChangedInputRuns(c, () => DewPlayer.lobbyPlayers[0] = Player("Skin_Mist_Default", "Acc_A"));
                Assert.Equal(1, c.model.UpdateAccessoriesCount);

                // forceSetup の設定: text が forceSetup に固定される
                AssertChangedInputRuns(c, () => c.forceSetup = "Skin_Forced");
                Assert.Equal("Skin_Forced", c.model.skinType);
            }
        }

        [Fact]
        public void AnotherPrefixCancellingTheOriginalDoesNotRecord()
        {
            using (new LobbyScope())
            {
                var p = Player("Skin_Mist_Default", "Acc_A");
                var c = Character(p);
                Assert.True(Tick(c));
                Assert.False(Tick(c)); // 省略できる状態を作る

                p.ownedItems.Add("Acc_C");
                p.selectedAccessories.Add("Acc_C");
                // この実装の Prefix は本体を許可したが、先行する他MODの Prefix が本体を止めたTick。
                // 本体が反映していない入力を「片付いた状態」として記録してはならない。
                ForeignCancelledTick(c);
                Assert.Equal(new[] { "Acc_A" }, c.model.accessories); // 本体は走っていない

                // 記録が撮られていないため、次のTickは本体に戻る（省略が止まったままにならない）
                Assert.True(Tick(c));
                Assert.Equal(new[] { "Acc_A", "Acc_C" }, c.model.accessories);
                Assert.False(Tick(c)); // 改めて記録され、次から省略
            }
        }

        [Fact]
        public void TheSettingOffRunsTheOriginalAlwaysAndRecordsNothing()
        {
            using (new LobbyScope())
            {
                var p = Player("Skin_Mist_Default", "Acc_A");
                var c = Character(p);
                Assert.True(Tick(c));
                Assert.False(Tick(c)); // 省略できる状態を作る

                NativePerfOptions.LobbyAccessoryIdle = false;
                Assert.True(NativeLobbyAccessoryIdle.BeforeLogicUpdate(c)); // 設定OFFでは常に本体
                Assert.True(NativeLobbyAccessoryIdle.BeforeLogicUpdate(c));
                p.ownedItems.Add("Acc_C");
                p.selectedAccessories.Add("Acc_C");
                c.LogicUpdate(1f / 30f); // 設定OFFでも本体自体は毎Tick走る
                NativeLobbyAccessoryIdle.AfterLogicUpdate(c, true); // OFFの間は記録しない
                Assert.Equal(new[] { "Acc_A", "Acc_C" }, c.model.accessories);

                NativePerfOptions.LobbyAccessoryIdle = true;
                Assert.True(Tick(c)); // OFF中の変化が記録されていないため、最初のTickは本体に戻る
                Assert.False(Tick(c)); // 記録が撮り直されて再び省略
            }
        }

        [Fact]
        public void AnExceptionDisablesThePatchFailSoftAndItStaysDisabled()
        {
            using (new LobbyScope())
            {
                var c = Character(Player("Skin_Mist_Default", "Acc_A"));
                Assert.True(Tick(c));
                Assert.False(Tick(c)); // 省略できる状態を作る
                var normal = c.model;

                // Prefix 側: 記録との比較の読み出し（model.skinType）で例外が起きたときは、そのTickの
                // 本体を許可し、以後は例外を起こさなくても常に本体に戻る
                c.model = new ThrowingSkinModel();
                Assert.True(NativeLobbyAccessoryIdle.BeforeLogicUpdate(c));
                Assert.True(NativeLobbyAccessoryIdle.BeforeLogicUpdate(c));
                NativeLobbyAccessoryIdle.AfterLogicUpdate(c, true); // Postfix も例外を起こさず抜ける
                c.model = normal;

                // Postfix 側（Record）: 記録の読み出しで例外が起きても伝播しない。Prefix が本体を
                // 許可した直後（ShouldSkip が skinType より前で偽になるよう forceSetup を変える）で見る
                ResetPatchDisabled();
                c.forceSetup = "x";
                Assert.True(NativeLobbyAccessoryIdle.BeforeLogicUpdate(c)); // まだ例外に触らない
                c.model = new ThrowingSkinModel();
                NativeLobbyAccessoryIdle.AfterLogicUpdate(c, true); // Record の読み出しの例外を飲む
                c.model = normal;
                c.forceSetup = "";

                // 例外源を取り除いても、フェイルソフトの無効化は戻らない
                Assert.True(Tick(c));
                Assert.True(Tick(c));
            }
        }

        private static void ResetPatchDisabled()
            => typeof(NativeLobbyAccessoryIdle).GetField("_disabled", Hidden).SetValue(null, false);
    }
}
