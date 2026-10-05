using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Internal;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #48 段階B：全13セット（Demonは段階Aのテストで扱う）。
    /// 各セットで装備数による 2/3/6 段階と報酬連携 2/4/6 段階の切替（白夜と暗月は同じ記憶で
    /// 部位数を別集計、Primus／Polarisは連携なし）、対応ボス撃破だけの専用ドロップ、保存往復を確認する。
    /// </summary>
    public class BossSetsStageB12Tests
    {
        private const string Hero = "Hero_A";

        private sealed class SetSpec
        {
            public string SetId, BossType, RewardId, Requires;
            public bool HasLink => RewardId != null;
        }

        private static readonly SetSpec[] Specs =
        {
            new SetSpec { SetId = BossProfiles.SkollSetId, BossType = "Mon_SnowMountain_BossSkoll", RewardId = BossProfiles.SkollRewardId, Requires = "Gem_U_GlacialCore" },
            new SetSpec { SetId = BossProfiles.InfernusSetId, BossType = "Mon_LavaLand_BossInfernus", RewardId = BossProfiles.InfernusRewardId, Requires = "Gem_U_EternalFlame" },
            new SetSpec { SetId = BossProfiles.WhiteNightSetId, BossType = "Mon_Ink_BossWhiteNight", RewardId = BossProfiles.WhiteNightRewardId, Requires = "St_U_BeamOfBalance" },
            new SetSpec { SetId = BossProfiles.DarkMoonSetId, BossType = "Mon_Ink_BossDarkMoon", RewardId = BossProfiles.DarkMoonRewardId, Requires = "St_U_BeamOfBalance" },
            new SetSpec { SetId = BossProfiles.NyxSetId, BossType = "Mon_Sky_BossNyx", RewardId = BossProfiles.NyxRewardId, Requires = "St_U_HerWorld" },
            new SetSpec { SetId = BossProfiles.ErebosSetId, BossType = "Mon_Special_BossErebos", RewardId = BossProfiles.ErebosRewardId, Requires = "Gem_U_LastStarlight" },
            new SetSpec { SetId = BossProfiles.SeekerSetId, BossType = "Mon_DarkCave_BossSeeker", RewardId = BossProfiles.SeekerRewardId, Requires = "Gem_U_SoulPrison" },
            new SetSpec { SetId = BossProfiles.AzurakSetId, BossType = "Mon_Despair_BossAzurak", RewardId = BossProfiles.AzurakRewardId, Requires = "St_U_Burrow" },
            new SetSpec { SetId = BossProfiles.PrimusSetId, BossType = "Mon_Primus_BossPrimusAeron", RewardId = null, Requires = null },
            new SetSpec { SetId = BossProfiles.LightSetId, BossType = "Mon_Special_BossLightElemental", RewardId = BossProfiles.LightRewardId, Requires = "St_U_WorldCracker" },
            new SetSpec { SetId = BossProfiles.MawSetId, BossType = "Mon_Special_BossMaw", RewardId = BossProfiles.MawRewardId, Requires = "St_U_BigChomp" },
            new SetSpec { SetId = BossProfiles.ObliviaxSetId, BossType = "Mon_Special_BossObliviax", RewardId = BossProfiles.ObliviaxRewardId, Requires = "St_U_ShoutOfOblivion" },
            new SetSpec { SetId = BossProfiles.PolarisSetId, BossType = "Mon_Special_BossPolaris", RewardId = null, Requires = null },
        };

        public static IEnumerable<object[]> SetIds() => Specs.Select(s => new object[] { s.SetId });

        private static SetSpec Spec(string setId) => Specs.First(s => s.SetId == setId);
        private static List<UniqueDef> Pieces(string setId)
        {
 var order = Content.SlotOrder;
 int Rank(Slot slot) { for (int i = 0; i < order.Count; i++) if (order[i] == slot) return i; return order.Count; }
            return Content.Uniques.Where(u => u.SetId == setId)
                .OrderBy(u => Rank(Content.GetBase(u.BaseId).Slot))
                .ToList();
        }

        private static Relic Piece(string uniqueId, ulong seed)
        {
            Content.TryGetUnique(uniqueId, out var u);
            return Loot.RollUnique(new Rng(seed), u, 5);
        }

        private static Profile Equipped(string setId, int pieces)
        {
            var p = Profile.CreateNew(48);
            var ids = Pieces(setId);
            ulong seed = 900;
            for (int i = 0; i < pieces; i++)
            {
                var r = Piece(ids[i].Id, seed++);
                p.Stash.Add(r);
                Rules.Equip(p, Hero, r.Uid);
            }
            return p;
        }

        private static int? RewardStage(Build b, string setId)
        {
            var entry = b.BossRewards.SingleOrDefault(r => r.SetId == setId);
            return entry?.Stage;
        }

        [Theory]
        [MemberData(nameof(SetIds))]
        public void Each_stage_B_set_is_registered_with_six_exclusive_pieces_and_stages(string setId)
        {
            var spec = Spec(setId);
            Assert.True(BossSets.TryGetSet(spec.BossType, out var set));
            Assert.Equal(setId, set.Id);
            var pieces = Pieces(setId);
            Assert.Equal(6, pieces.Count);
            Assert.Equal(6, pieces.Select(u => Content.GetBase(u.BaseId).Slot).Distinct().Count());
            Assert.All(pieces, u => Assert.True(BossSets.IsExclusive(u)));
            // ボスセットの部位に汎用の2/3/6効果行はない。
            Assert.Empty(set.TwoPiece);
            Assert.Empty(set.ThreePiece);
            Assert.Empty(set.SixPiece);
            Assert.Equal(new[] { 2, 3, 6 }, set.BossStages.Select(s => s.RequiredPieces));
            Assert.All(set.BossStages.Select(s => s.ProfileId),
                id => Assert.True(BossProfiles.TryGetMove(id, out var p) && p.SetId == setId));
            Assert.All(pieces, u => Assert.True(BossProfiles.TryGetMove(u.BossMove, out var p) && p.SetId == setId));
            if (spec.HasLink)
            {
                Assert.Equal(spec.RewardId, set.BossReward);
                Assert.True(BossProfiles.TryGetReward(set.BossReward, out var reward));
                Assert.Equal(setId, reward.SetId);
                Assert.Equal(spec.Requires, reward.Requires);
                Assert.Equal(new[] { 1, 2, 3 }, reward.Stages.Select(s => s.Stage));
                Assert.Equal(new[] { 2, 4, 6 }, set.LinkStages.Select(s => s.RequiredPieces));
                Assert.All(set.LinkStages, s =>
                {
                    Assert.Equal(LinkKind.BossReward, s.Link.Kind);
                    Assert.Equal(new[] { spec.Requires }, s.Link.Requires);
                });
                Assert.Null(set.SelectLinkStage(1));
                Assert.Equal(1, (int)set.SelectLinkStage(2).Link.Value);
                Assert.Equal(1, (int)set.SelectLinkStage(3).Link.Value);
                Assert.Equal(2, (int)set.SelectLinkStage(4).Link.Value);
                Assert.Equal(2, (int)set.SelectLinkStage(5).Link.Value);
                Assert.Equal(3, (int)set.SelectLinkStage(6).Link.Value);
            }
            else
            {
                // Primus／Polarisは本体報酬との連携なし。
                Assert.Null(set.BossReward);
                Assert.Empty(set.LinkStages);
                Assert.Null(set.SelectLinkStage(6));
                Assert.DoesNotContain(BossProfiles.Rewards, r => r.SetId == setId);
            }
        }

        [Theory]
        [MemberData(nameof(SetIds))]
        public void Piece_count_switches_the_carrying_stages_and_reward_stage(string setId)
        {
            var spec = Spec(setId);
            var pieceMoves = Pieces(setId).Select(u => u.BossMove).ToList();
            var stages = Content.GetSet(setId).BossStages.ToDictionary(s => s.RequiredPieces, s => s.ProfileId);
            for (int pieces = 1; pieces <= 6; pieces++)
            {
                var b = Build.Compute(Equipped(setId, pieces), Hero, 0);
                Assert.Equal(pieces, b.Sets[setId]);
                var expected = new HashSet<string>(pieceMoves.Take(pieces), StringComparer.Ordinal);
                foreach (var threshold in new[] { 2, 3, 6 })
                    if (pieces >= threshold) expected.Add(stages[threshold]);
                Assert.Equal(expected, new HashSet<string>(b.BossMoves.Select(e => e.ProfileId), StringComparer.Ordinal));
                // セット段階のchannelは固定値。段階効果の係数は装備数で変わらない。
                foreach (var entry in b.BossMoves)
                {
                    Assert.True(BossProfiles.TryGetMove(entry.ProfileId, out var profile));
                    Assert.Equal(profile.SetId, entry.SetId);
                    for (int i = 0; i < profile.Channels.Count; i++)
                        Assert.Equal(profile.Channels[i].ValueMilli, entry.Channels[i].ValueMilli);
                }
                if (!spec.HasLink) { Assert.Empty(b.BossRewards); continue; }
                int? expectedStage = pieces <= 1 ? (int?)null : pieces <= 3 ? 1 : pieces <= 5 ? 2 : 3;
                Assert.Equal(expectedStage, RewardStage(b, setId));
                if (expectedStage != null)
                    Assert.Equal(spec.RewardId, b.BossRewards.Single(r => r.SetId == setId).ProfileId);
            }
        }

        [Theory]
        [InlineData(BossProfiles.LightSetId)]
        [InlineData(BossProfiles.MawSetId)]
        [InlineData(BossProfiles.ObliviaxSetId)]
        [InlineData(BossProfiles.PolarisSetId)]
        public void B3_enhancement_and_awakening_stop_at_three_times_without_scaling_stages(string setId)
        {
            var profile = Equipped(setId, 6);
            var baseline = Build.Compute(profile, Hero, 0);
            foreach (var relic in profile.Stash)
            {
                relic.Enhance = 20;
                relic.EnhanceMilestones = 5;
                relic.AwakenLevel = 3;
                var move = relic.EffectiveBossMove();
                Assert.True(BossProfiles.TryGetMove(move.ProfileId, out var definition));
                for (int i = 0; i < definition.Channels.Count; i++)
                {
                    var channel = definition.Channels[i];
                    bool scales = channel.Kind == BossCoefficientKind.Damage ||
                        channel.Kind == BossCoefficientKind.Heal || channel.Kind == BossCoefficientKind.Shield;
                    Assert.Equal(channel.ValueMilli * (scales ? 3 : 1), move.Channels[i].ValueMilli);
                }
                Assert.Empty(relic.Powers);
            }
            var enhanced = Build.Compute(profile, Hero, 0);
            foreach (var stage in Content.GetSet(setId).BossStages)
            {
                var before = baseline.BossMoves.Single(m => m.ProfileId == stage.ProfileId);
                var after = enhanced.BossMoves.Single(m => m.ProfileId == stage.ProfileId);
                Assert.Equal(before.Channels.Select(c => c.ValueMilli), after.Channels.Select(c => c.ValueMilli));
            }
            Assert.Equal(baseline.BossRewards.Select(r => r.Stage), enhanced.BossRewards.Select(r => r.Stage));
        }

        [Fact]
        public void White_night_and_dark_moon_count_their_own_pieces_for_the_same_memory()
        {
            // 同じ St_U_BeamOfBalance でも部位数は各セットで独立。6枠なので衝突しない部位を選ぶ。
            Profile Wear(int whiteCount, params int[] darkIndexes)
            {
                var p = Profile.CreateNew(51);
                ulong seed = 700;
                void WearPieces(string setId, IEnumerable<int> indexes)
                {
                    var ids = Pieces(setId);
                    foreach (int i in indexes)
                    {
                        var r = Piece(ids[i].Id, seed++);
                        p.Stash.Add(r);
                        Rules.Equip(p, Hero, r.Uid);
                    }
                }
                WearPieces(BossProfiles.WhiteNightSetId, Enumerable.Range(0, whiteCount));
                WearPieces(BossProfiles.DarkMoonSetId, darkIndexes);
                return p;
            }
            var b = Build.Compute(Wear(4, 4, 5), Hero, 0);
            Assert.Equal(4, b.Sets[BossProfiles.WhiteNightSetId]);
            Assert.Equal(2, b.Sets[BossProfiles.DarkMoonSetId]);
            Assert.Equal(2, RewardStage(b, BossProfiles.WhiteNightSetId));
            Assert.Equal(1, RewardStage(b, BossProfiles.DarkMoonSetId));
            Assert.Equal(2, b.BossRewards.Count);
            // 2つの報酬profileは同じ記憶を要求しつつ、別profileとして並立する。
            var white = b.BossRewards.Single(r => r.SetId == BossProfiles.WhiteNightSetId);
            var dark = b.BossRewards.Single(r => r.SetId == BossProfiles.DarkMoonSetId);
            Assert.NotEqual(white.ProfileId, dark.ProfileId);
            Assert.All(new[] { white.ProfileId, dark.ProfileId }, id =>
            {
                Assert.True(BossProfiles.TryGetReward(id, out var reward));
                Assert.Equal("St_U_BeamOfBalance", reward.Requires);
            });
            // 白夜2 + 暗月4 でも数え方は同じまま逆転する。
            b = Build.Compute(Wear(2, 2, 3, 4, 5), Hero, 0);
            Assert.Equal(1, RewardStage(b, BossProfiles.WhiteNightSetId));
            Assert.Equal(2, RewardStage(b, BossProfiles.DarkMoonSetId));
        }

        [Fact]
        public void Primus_never_enters_the_reward_section_even_alongside_a_linked_set()
        {
            // 6枠の制約上、Demonのweapon/armorとPrimusのcharm/head/hands/feetを併用する。
            var p = Profile.CreateNew(53);
            ulong seed = 950;
            void Wear(string setId, params int[] indexes)
            {
                var ids = Pieces(setId);
                foreach (int i in indexes)
                {
                    var r = Piece(ids[i].Id, seed++);
                    p.Stash.Add(r);
                    Rules.Equip(p, Hero, r.Uid);
                }
            }
            Wear("set.boss_demon", 0, 1);
            Wear(BossProfiles.PrimusSetId, 2, 3, 4, 5);
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(2, b.Sets["set.boss_demon"]);
            Assert.Equal(4, b.Sets[BossProfiles.PrimusSetId]);
            // Primusはb:の部位/段階を持つが、z:の報酬連携には決して入らない。
            Assert.Equal(6, b.BossMoves.Count(e => e.SetId == BossProfiles.PrimusSetId));
            var reward = Assert.Single(b.BossRewards);
            Assert.Equal("set.boss_demon", reward.SetId);
            Assert.Equal(1, reward.Stage);
        }

        [Theory]
        [MemberData(nameof(SetIds))]
        public void RollDrop_from_the_registered_boss_yields_only_that_sets_pieces(string setId)
        {
            var spec = Spec(setId);
            var valid = new HashSet<string>(Pieces(setId).Select(u => u.Id), StringComparer.Ordinal);
            int won = 0, lost = 0;
            for (ulong seed = 1; seed <= 2500 && (won == 0 || lost == 0 || won < 15); seed++)
            {
                var relic = BossSets.RollDrop(new Rng(seed), spec.BossType, false, 0, 10);
                if (relic == null) { lost++; continue; }
                won++;
                Assert.True(valid.Contains(relic.UniqueId), relic.UniqueId);
                Assert.Equal(Rarity.Legendary, relic.Rarity);
            }
            Assert.True(won >= 15 && lost >= 100, $"expected a meaningful sample, won={won} lost={lost}");
        }

        [Fact]
        public void Only_the_matching_boss_kill_grants_each_stage_B_set()
        {
            var p = Profile.CreateNew(600);
            Rules.BeginRun(p, "run", heroKey: Hero);
            HashSet<string> BossPieceUids() => new HashSet<string>(p.Run.Satchel
                .Where(r => r.UniqueId != null && Content.TryGetUnique(r.UniqueId, out var u) && BossSets.IsExclusive(u))
                .Select(r => r.Uid), StringComparer.Ordinal);
            // 未登録の型名（Crawler）のボス撃破では1つも出ない。
            for (ulong seed = 1; seed <= 60; seed++)
            {
                p.Run.Bounties.Clear();
                Rules.OnKill(p, MonsterTier.Boss, 12, NightmareAffix.None, Hero, bossTypeName: "Mon_Despair_BossCrawler");
            }
            Assert.Empty(BossPieceUids());
            // 対応ボスの撃破だけがそのセットの部位を出す。他セットの部位は混ざらない。
            foreach (var spec in Specs)
            {
                var ids = new HashSet<string>(Pieces(spec.SetId).Select(u => u.Id), StringComparer.Ordinal);
                var before = BossPieceUids();
                int own = 0;
                for (ulong seed = 1; seed <= 120; seed++)
                {
                    p.Run.Bounties.Clear();
                    Rules.OnKill(p, MonsterTier.Boss, 12, NightmareAffix.None, Hero,
                        bossTypeName: spec.BossType, bossDropNightmare: false, bossDropDepth: 0);
                }
                foreach (var relic in p.Run.Satchel.Where(r => !before.Contains(r.Uid)))
                    if (relic.UniqueId != null && Content.TryGetUnique(relic.UniqueId, out var u) && BossSets.IsExclusive(u))
                    {
                        Assert.True(ids.Contains(u.Id), $"{spec.BossType} dropped a foreign piece: {u.Id}");
                        own++;
                    }
                // 通常・基礎深度 p=10%・120撃破で期待12。確定保証ではないので下限は緩く取る。
                Assert.True(own >= 4, $"{spec.BossType} dropped only {own} pieces in 120 kills");
            }
        }

        [Theory]
        [MemberData(nameof(SetIds))]
        public void Six_pieces_round_trip_through_the_profile_save_and_host_validation(string setId)
        {
            var spec = Spec(setId);
            var p = Equipped(setId, 6);
            var client = Build.Compute(p, Hero, 0);
            Assert.Equal(9, client.BossMoves.Count(e => e.SetId == setId));
            if (spec.HasLink)
                Assert.Equal(3, client.BossRewards.Single(r => r.SetId == setId).Stage);
            else
                Assert.Empty(client.BossRewards.Where(r => r.SetId == setId));
            string encoded = client.Encode();
            // 保存→読み込みで装備とb:/z:節がそのまま戻る。
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            var equipped = loaded.Hero(Hero).Equipped.Where(uid => uid != null).ToList();
            Assert.Equal(6, equipped.Count);
            Assert.Equal(new HashSet<string>(Pieces(setId).Select(u => u.Id), StringComparer.Ordinal),
                new HashSet<string>(equipped.Select(uid => loaded.FindStash(uid)?.UniqueId), StringComparer.Ordinal));
            Assert.Equal(encoded, Build.Compute(loaded, Hero, 0).Encode());
            // ホストは同じ装備入力から同一のbuildを再導出して受理する。
            string submission = HostBuildValidation.Encode(client, loaded, Hero, 0);
            Assert.True(HostBuildValidation.TryAccept(submission, Hero, out var accepted, out var reason), reason);
            Assert.Equal(encoded, accepted.Encode());
        }
    }
}
