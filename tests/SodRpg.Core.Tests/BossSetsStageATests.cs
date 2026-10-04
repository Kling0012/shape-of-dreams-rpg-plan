using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #48 段階A：ボス限定セット（森の悪魔「荒ぶる樹界」）の Core 側。
    /// 装備数による 2/3/6 段階の切り替え、ヒステリー連携の段階値、汎用プール除外と
    /// 該当ボス撃破のみの入手、b:/z: 節の Protocol 16 往復と上限64・未知ID・重複の拒否。
    /// Mod 側の戦闘runtime（速度補正の置換・解除、状態破棄）は段階Aのテストリンクでは
    /// 参照できないため、ここには含めない（BossCombatState は HostAuthority の private 入れ子）。
    /// </summary>
    public class BossSetsStageATests
    {
        private const string Hero = "Hero_A";
        private const string SetId = "set.boss_demon";
        private static readonly string[] PieceIds =
        {
            "set.boss_demon.weapon", "set.boss_demon.armor", "set.boss_demon.charm",
            "set.boss_demon.head", "set.boss_demon.hands", "set.boss_demon.feet",
        };

        private static Relic Piece(string uniqueId, ulong seed)
        {
            Content.TryGetUnique(uniqueId, out var u);
            return Loot.RollUnique(new Rng(seed), u, 5);
        }

        private static Profile Equipped(int pieces)
        {
            var p = Profile.CreateNew(48);
            ulong seed = 900;
            for (int i = 0; i < pieces; i++)
            {
                var r = Piece(PieceIds[i], seed++);
                p.Stash.Add(r);
                Rules.Equip(p, Hero, r.Uid);
            }
            return p;
        }

        private static HashSet<string> MoveIds(Build b) =>
            new HashSet<string>(b.BossMoves.Select(e => e.ProfileId), StringComparer.Ordinal);

        private static int? RewardStage(Build b)
        {
            var entry = b.BossRewards.SingleOrDefault(r => r.SetId == SetId);
            return entry?.Stage;
        }

        [Fact]
        public void The_demon_set_is_registered_with_six_exclusive_pieces_and_stages()
        {
            Assert.True(BossSets.TryGetSet("Mon_Forest_BossDemon", out var set));
            Assert.Equal(SetId, set.Id);
            // 旧Skollの定義は撤去済み。未知の型名では何も引けない。
            Assert.False(BossSets.TryGetSet("Mon_SnowMountain_BossSkoll", out _));
            Assert.False(BossSets.TryGetSet(null, out _));
            var pieces = Content.Uniques.Where(u => u.SetId == SetId).ToList();
            Assert.Equal(6, pieces.Count);
            Assert.Equal(6, pieces.Select(u => Content.GetBase(u.BaseId).Slot).Distinct().Count());
            Assert.All(pieces, u => Assert.True(BossSets.IsExclusive(u)));
            Assert.Equal(new[] { 2, 3, 6 }, set.BossStages.Select(s => s.RequiredPieces));
            Assert.All(set.BossStages.Select(s => s.ProfileId), id => Assert.True(BossProfiles.TryGetMove(id, out var p) && p.SetId == SetId));
            Assert.All(pieces, u => Assert.True(BossProfiles.TryGetMove(u.BossMove, out var p) && p.SetId == SetId));
            // 汎用セットの部位は除外対象ではない。
            Assert.False(BossSets.IsExclusive(Content.Uniques.First(u => u.SetId == "set.gale")));
        }

        [Fact]
        public void Link_stages_are_two_four_six_for_the_hysteria_memory()
        {
            var set = Content.GetSet(SetId);
            Assert.Equal(BossProfiles.DemonRewardId, set.BossReward);
            Assert.True(BossProfiles.TryGetReward(set.BossReward, out var reward));
            Assert.Equal(SetId, reward.SetId);
            Assert.Equal("St_U_Hysteria", reward.Requires);
            Assert.Equal(new[] { 2, 4, 6 }, set.LinkStages.Select(s => s.RequiredPieces));
            Assert.All(set.LinkStages, s =>
            {
                Assert.Equal(LinkKind.BossReward, s.Link.Kind);
                Assert.Equal(new[] { "St_U_Hysteria" }, s.Link.Requires);
            });
            Assert.Null(set.SelectLinkStage(1));
            Assert.Equal(1, (int)set.SelectLinkStage(2).Link.Value);
            Assert.Equal(1, (int)set.SelectLinkStage(3).Link.Value);
            Assert.Equal(2, (int)set.SelectLinkStage(4).Link.Value);
            Assert.Equal(2, (int)set.SelectLinkStage(5).Link.Value);
            Assert.Equal(3, (int)set.SelectLinkStage(6).Link.Value);
        }

        [Theory]
        [InlineData(1, new string[] { "boss_demon.weapon" }, null)]
        [InlineData(2, new[] { "boss_demon.weapon", "boss_demon.armor", "boss_demon.stage2" }, 1)]
        [InlineData(3, new[] { "boss_demon.weapon", "boss_demon.armor", "boss_demon.charm", "boss_demon.stage2", "boss_demon.stage3" }, 1)]
        [InlineData(4, new[] { "boss_demon.weapon", "boss_demon.armor", "boss_demon.charm", "boss_demon.head", "boss_demon.stage2", "boss_demon.stage3" }, 2)]
        [InlineData(5, new[] { "boss_demon.weapon", "boss_demon.armor", "boss_demon.charm", "boss_demon.head", "boss_demon.hands", "boss_demon.stage2", "boss_demon.stage3" }, 2)]
        [InlineData(6, new[] { "boss_demon.weapon", "boss_demon.armor", "boss_demon.charm", "boss_demon.head", "boss_demon.hands", "boss_demon.feet", "boss_demon.stage2", "boss_demon.stage3", "boss_demon.stage6" }, 3)]
        public void Piece_count_switches_the_carrying_stages_and_reward_stage(int pieces, string[] expectedMoves, int? expectedStage)
        {
            var b = Build.Compute(Equipped(pieces), Hero, 0);
            Assert.Equal(pieces, b.Sets[SetId]);
            Assert.Equal(new HashSet<string>(expectedMoves, StringComparer.Ordinal), MoveIds(b));
            Assert.Equal(expectedStage, RewardStage(b));
            // セット段階のchannelは固定値（装備数で係数が変わらない）。
            foreach (var entry in b.BossMoves)
            {
                Assert.True(BossProfiles.TryGetMove(entry.ProfileId, out var profile));
                for (int i = 0; i < profile.Channels.Count; i++)
                    Assert.Equal(profile.Channels[i].ValueMilli, entry.Channels[i].ValueMilli);
            }
        }

        [Fact]
        public void Boss_pieces_carry_their_move_in_one_authored_effect_slot()
        {
            var r = Piece("set.boss_demon.weapon", 51);
            Assert.Equal(1, r.AuthoredEffectCount);
            var move = r.EffectiveBossMove();
            Assert.Equal(SetId, move.SetId);
            Assert.Equal("boss_demon.weapon", move.ProfileId);
            var channel = Assert.Single(move.Channels);
            Assert.Equal("DemonImpact", channel.ChannelId);
            Assert.Equal(25000, channel.ValueMilli);
            // 強化・覚醒・限界突破で係数は伸びるが、Demonのcap（上限80%）の内側に留まる。
            r.Enhance = 20;
            r.EnhanceMilestones = 5;
            r.AwakenLevel = 3;
            Assert.Equal(78300, r.EffectiveBossMove().Channels[0].ValueMilli);
            // 係数以外のchannel（持続時間など）は強化で変わらない。
            var armor = Piece("set.boss_demon.armor", 52);
            armor.Enhance = 20;
            armor.AwakenLevel = 3;
            Assert.Equal(6000, armor.EffectiveBossMove().Channels[0].ValueMilli);
            // 空Power配列を理由に汎用Powerは付かない。
            Assert.Empty(r.Powers);
        }

        [Fact]
        public void Generic_pool_never_yields_boss_limited_pieces()
        {
            var rng = new Rng(77);
            for (int i = 0; i < 3000; i++)
            {
                var rolled = Loot.RollRelic(rng, Rarity.Legendary, 10);
                Assert.False(rolled.UniqueId != null && rolled.UniqueId.StartsWith(SetId + ".", StringComparison.Ordinal),
                    "boss-limited piece appeared in the generic legendary pool: " + rolled.UniqueId);
            }
        }

        [Fact]
        public void Drop_chance_formula_and_registered_source_only()
        {
            Assert.Equal(0.10, BossSets.DropChance(false, 0), 6);
            Assert.Equal(0.15, BossSets.DropChance(true, 0), 6);
            Assert.Equal(0.13, BossSets.DropChance(false, 3), 6);
            Assert.Equal(0.15, BossSets.DropChance(false, 5), 6);
            Assert.Equal(0.15, BossSets.DropChance(false, 9), 6); // 深さは0〜5に固定
            Assert.Equal(0.20, BossSets.DropChance(true, 5), 6);
            // 未登録のボス型名は抽選前に弾かれ、乱数を消費しない。
            var rng = new Rng(9);
            ulong before = rng.State;
            Assert.Null(BossSets.RollDrop(rng, "Mon_SnowMountain_BossSkoll", true, 5, 10));
            Assert.Null(BossSets.RollDrop(rng, null, true, 5, 10));
            Assert.Equal(before, rng.State);
        }

        [Fact]
        public void RollDrop_returns_one_of_the_six_pieces_when_won()
        {
            var valid = new HashSet<string>(PieceIds, StringComparer.Ordinal);
            int won = 0, lost = 0;
            for (ulong seed = 1; seed <= 4000 && (won == 0 || lost == 0 || won < 30); seed++)
            {
                var relic = BossSets.RollDrop(new Rng(seed), "Mon_Forest_BossDemon", false, 0, 10);
                if (relic == null) { lost++; continue; }
                won++;
                Assert.True(valid.Contains(relic.UniqueId), relic.UniqueId);
                Assert.Equal(Rarity.Legendary, relic.Rarity);
            }
            Assert.True(won >= 20 && lost >= 100, $"expected a meaningful sample, won={won} lost={lost}");
        }

        private static IList<Relic> BossKillDrops(int kills, string bossTypeName, bool nightmare, int depth, int itemLevel = 12)
        {
            var found = new List<Relic>();
            for (ulong seed = 1; seed <= (ulong)kills; seed++)
            {
                var p = Profile.CreateNew(600 + seed);
                Rules.BeginRun(p, "run", heroKey: Hero);
                p.Run.Bounties.Clear();
                Rules.OnKill(p, MonsterTier.Boss, itemLevel, NightmareAffix.None, Hero, bossTypeName: bossTypeName,
                    bossDropNightmare: nightmare, bossDropDepth: depth);
                foreach (var relic in p.Run.Satchel)
                    if (relic.UniqueId != null && relic.UniqueId.StartsWith(SetId + ".", StringComparison.Ordinal))
                        found.Add(relic);
            }
            return found;
        }

        [Fact]
        public void Only_the_matching_boss_kill_grants_the_exclusive_pieces()
        {
            // 通常・基礎深度 p=10%：300撃破で有意な当選、ただし確定保証ではない。
            var normal = BossKillDrops(300, "Mon_Forest_BossDemon", false, 0);
            Assert.InRange(normal.Count, 10, 60);
            Assert.All(normal, r => Assert.StartsWith(SetId + ".", r.UniqueId, StringComparison.Ordinal));
            // 未登録の型名（旧Skoll）・ボス以外の撃破では1つも出ない。
            Assert.Empty(BossKillDrops(300, "Mon_SnowMountain_BossSkoll", false, 0));
            var notBoss = new List<Relic>();
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var p = Profile.CreateNew(9000 + seed);
                Rules.BeginRun(p, "run", heroKey: Hero);
                p.Run.Bounties.Clear();
                Rules.OnKill(p, MonsterTier.Normal, 12, NightmareAffix.None, Hero, bossTypeName: "Mon_Forest_BossDemon");
                foreach (var relic in p.Run.Satchel)
                    if (relic.UniqueId != null && relic.UniqueId.StartsWith(SetId + ".", StringComparison.Ordinal))
                        notBoss.Add(relic);
            }
            Assert.Empty(notBoss);
        }

        [Fact]
        public void Nightmare_and_depth_raise_the_exclusive_drop_rate()
        {
            var plain = BossKillDrops(400, "Mon_Forest_BossDemon", false, 0);
            var pushed = BossKillDrops(400, "Mon_Forest_BossDemon", true, 5);
            // 10% vs 20%：同シード数で2倍近く。確率的変動を吸収できる範囲でだけ検証する。
            Assert.InRange(pushed.Count, plain.Count * 3 / 2, plain.Count * 5 / 2 + 20);
        }

        [Fact]
        public void Six_pieces_round_trip_through_the_protocol_with_boss_sections()
        {
            var p = Equipped(6);
            var client = Build.Compute(p, Hero, 0);
            Assert.Equal(9, client.BossMoves.Count);
            var reward = Assert.Single(client.BossRewards);
            Assert.Equal(3, reward.Stage);
            string encoded = client.Encode();
            Assert.Contains(";b:", encoded, StringComparison.Ordinal);
            string bossSection = encoded.Substring(encoded.IndexOf(";b:", StringComparison.Ordinal) + 3).Split(';')[0];
            Assert.Equal(
                "set.boss_demon:boss_demon.armor:DemonStride=6000," +
                "set.boss_demon:boss_demon.charm:DemonSeed=20000," +
                "set.boss_demon:boss_demon.feet:DemonArrival=20000," +
                "set.boss_demon:boss_demon.hands:DemonDelay=25000," +
                "set.boss_demon:boss_demon.head:DemonVolley=12000," +
                "set.boss_demon:boss_demon.stage2:DemonStomp=50000," +
                "set.boss_demon:boss_demon.stage3:DemonGrove=45000," +
                "set.boss_demon:boss_demon.stage6:DemonMarch=90000+DemonReplant=38000," +
                "set.boss_demon:boss_demon.weapon:DemonImpact=25000",
                bossSection);
            Assert.Contains(";z:set.boss_demon:boss_demon.hysteria:3", encoded, StringComparison.Ordinal);
            var decoded = Build.Decode(encoded);
            Assert.NotNull(decoded);
            Assert.Equal(encoded, decoded.Encode());
            Assert.Equal(client.BossMoves.Count, decoded.BossMoves.Count);
            Assert.Equal(reward.Stage, decoded.BossRewards.Single(r => r.SetId == SetId).Stage);
            // ホストは同じ装備入力から同一のb:/z:節を再導出して受理する。
            string submission = HostBuildValidation.Encode(client, p, Hero, 0);
            Assert.True(HostBuildValidation.TryAccept(submission, Hero, out var accepted, out var reason), reason);
            Assert.Equal(encoded, accepted.Encode());
        }

        [Fact]
        public void Codec_rejects_unknown_duplicate_and_oversized_boss_sections()
        {
            var fixedMove = BossBuildCodec.FixedMove("boss_demon.stage2");
            // 未知ID。
            var unknown = new Build();
            unknown.BossMoves.Add(new BossMoveEntry(SetId, "boss_demon.unknown", fixedMove.Channels));
            Assert.Throws<FormatException>(() => unknown.Encode());
            Assert.Null(Build.Decode("h:0;d:1;a:0;b:set.boss_demon:boss_demon.unknown:DemonStomp=50000"));
            // 重複キー。
            var duplicate = new Build();
            duplicate.BossMoves.Add(BossBuildCodec.FixedMove("boss_demon.stage2"));
            duplicate.BossMoves.Add(BossBuildCodec.FixedMove("boss_demon.stage2"));
            Assert.Throws<FormatException>(() => duplicate.Encode());
            // 65件（上限64）。
            var oversized = new Build();
            for (int i = 0; i <= BossProfiles.MaxEntries; i++) oversized.BossMoves.Add(fixedMove);
            Assert.Throws<InvalidOperationException>(() => oversized.Encode());
            var oversizedRewards = new Build();
            for (int i = 0; i <= BossProfiles.MaxEntries; i++)
                oversizedRewards.BossRewards.Add(new BossRewardEntry(SetId, BossProfiles.DemonRewardId, 3));
            Assert.Throws<InvalidOperationException>(() => oversizedRewards.Encode());
        }

        [Fact]
        public void Codec_rejects_mutated_channels_and_invalid_reward_stages()
        {
            // セット段階は固定値。係数の申告は受けない。
            var mutated = new Build();
            mutated.BossMoves.Add(new BossMoveEntry(SetId, "boss_demon.stage2",
                new[] { new BossChannelValue("DemonStomp", 50001) }));
            Assert.Throws<FormatException>(() => mutated.Encode());
            // 部品の係数も0・負・cap超過は拒否。
            Assert.Throws<FormatException>(() => new Build
            {
                BossMoves = { new BossMoveEntry(SetId, "boss_demon.weapon", new[] { new BossChannelValue("DemonImpact", 0) }) },
            }.Encode());
            Assert.Throws<FormatException>(() => new Build
            {
                BossMoves = { new BossMoveEntry(SetId, "boss_demon.weapon", new[] { new BossChannelValue("DemonImpact", 80001) }) },
            }.Encode());
            // 報酬段階は1〜3、かつセットの報酬profileと一致していること。
            Assert.Throws<FormatException>(() => new Build
            {
                BossRewards = { new BossRewardEntry(SetId, BossProfiles.DemonRewardId, 0) },
            }.Encode());
            Assert.Throws<FormatException>(() => new Build
            {
                BossRewards = { new BossRewardEntry(SetId, BossProfiles.DemonRewardId, 4) },
            }.Encode());
            Assert.Throws<FormatException>(() => new Build
            {
                BossRewards = { new BossRewardEntry("set.gale", BossProfiles.DemonRewardId, 1) },
            }.Encode());
            // 連携節（l:）にはBossRewardを書けない。報酬はz:節だけ。
            Assert.Null(Build.Decode("h:0;d:1;a:0;l:6:1000:St_U_Hysteria"));
        }

        [Fact]
        public void Retired_old_boss_piece_ids_are_dropped_on_load_as_unknown_uniques()
        {
            // 旧Skollの部位IDは段階Aで撤去済み。保存された旧品は既存の未知Unique除外に流れる。
            var p = Profile.CreateNew(48);
            var r = Piece("set.boss_demon.weapon", 71);
            p.Stash.Add(r);
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(p));
            var body = (JsonObject)root.Properties.First(x => x.Key == "body").Value;
            var stash = (List<object>)body.Properties.First(x => x.Key == "stash").Value;
            var old = (JsonObject)stash[0];
            var swapped = new JsonObject();
            foreach (var kv in old.Properties) swapped.Add(kv.Key, kv.Key == "unique" ? "set.skoll.weapon" : kv.Value);
            stash[0] = swapped;
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(Json.Write(root), notes);
            Assert.Null(loaded.Stash.FirstOrDefault(x => x.UniqueId == "set.skoll.weapon"));
            Assert.Contains(notes, n => n.Contains("未知の固有品ID: set.skoll.weapon", StringComparison.Ordinal));
        }
    }
}
