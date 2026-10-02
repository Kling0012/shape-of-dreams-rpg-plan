using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarRoutesV127Tests
    {
        // Independent allowlist transcribed from shipped travelers.json loadouts and
        // memories.json ownership. Bismuth's loadoutTrait has exactly one identity.
        private static readonly Dictionary<string, string[]> Memories = new Dictionary<string, string[]>
        {
            ["Hero_Vesper"] = new[] { "St_D_Resolve", "St_D_MercyOfEl", "St_M_Charge", "St_Q_CruelSun", "St_Q_Discipline", "St_R_BaptismOfSun", "St_R_SanctuaryOfEl" },
            ["Hero_Lacerta"] = new[] { "St_D_DoubleTap", "St_D_SalamanderPowder", "St_M_NimbleDodge", "St_Q_HandCannon", "St_Q_IncendiaryRounds", "St_R_PrecisionShot", "St_R_QuickTrigger" },
            ["Hero_Cetus"] = new[] { "St_D_IcyVeins", "St_D_ChargedAnguillian", "St_M_FrostyCharge", "St_Q_EmbracingTheChill", "St_Q_BigBorealChunk", "St_R_BackOff", "St_R_FrozenFists" },
            ["Hero_Yubar"] = new[] { "St_D_ConvergencePoint", "St_D_ExoticMatter", "St_M_Flicker", "St_Q_EtherealInfluence", "St_Q_SuperNova", "St_R_Cataclysm", "St_R_Tranquility" },
            ["Hero_Husk"] = new[] { "St_D_TheKillingFlow", "St_D_ScarOfTheWind", "St_M_FlashStep", "St_Q_Laceration", "St_Q_DeathMark", "St_R_AnnihilationStance", "St_R_Deception" },
            ["Hero_Mist"] = new[] { "St_D_AstridsMasterpieceEnGarde", "St_D_AstridsMasterpiecePriorite", "St_M_FastFeet", "St_Q_Fleche", "St_Q_Lunge", "St_R_Parry", "St_R_UnbreakableDetermination" },
            ["Hero_Nachia"] = new[] { "St_D_HeartOfThePack", "St_D_CircleOfLife", "St_M_DreamyWaltz", "St_Q_SylvanCall", "St_Q_MoonlightPact", "St_R_NaturesWhisper", "St_R_SerpentineBlessing" },
            ["Hero_Aurena"] = new[] { "St_D_DisintegratingClaw", "St_D_BeautifulThreat", "St_M_FeatheryDash", "St_Q_GoldenBurst", "St_Q_Reduction", "St_R_DangerousTheory", "St_R_ChainReaction" },
            ["Hero_Bismuth"] = new[] { "St_D_PrismaticEyes", "St_D_ExplosionArtist", "St_M_Sprint", "St_QR_Innocence", "St_QR_InfernalTales", "St_QR_ValiantHeart", "St_QR_DistortedMind" },
        };

        [Fact]
        public void Every_shipped_memory_has_exactly_one_complete_branch_for_its_owner()
        {
            Assert.Equal(540, HeroStarRoutes.All.Count); // v1.27：Bismuth に共通のアイデンティティのルート、エッセンスの枠の星 3×9
            Assert.Equal(Memories.Keys.OrderBy(x => x), HeroStarRoutes.All.Select(t => t.HeroKey).Distinct().OrderBy(x => x));
            foreach (var hero in Memories)
            {
                var branches = HeroStarRoutes.All.Where(t => t.HeroKey == hero.Key && t.RouteId != null)
                    .GroupBy(t => t.RouteMemory).ToArray();
                Assert.Equal(hero.Value.OrderBy(x => x), branches.Select(g => g.Key).OrderBy(x => x));
                Assert.Equal(2, branches.Count(g => g.Key.StartsWith("St_D_", StringComparison.Ordinal)));
                Assert.Equal(5, branches.Count(g => !g.Key.StartsWith("St_D_", StringComparison.Ordinal)));
                foreach (var branch in branches)
                {
                    Assert.True(Links.IsMemory(branch.Key), branch.Key);
                    // 頂点の先のエッセンスの枠の星（8番目）は別に確かめる
                    var slot = branch.Where(t => t.RouteOrder == 8).ToArray();
                    Assert.Equal(branch.Key.StartsWith("St_D_", StringComparison.Ordinal) || branch.Key.StartsWith("St_M_", StringComparison.Ordinal) ? 1 : 0, slot.Length);
                    var nodes = branch.Where(t => t.RouteOrder <= 7).OrderBy(t => t.RouteOrder).ToArray();
                    Assert.Equal(7, nodes.Length);
                    Assert.Single(nodes.Select(t => t.RouteId).Distinct());
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        Assert.False(node.IsKeystone);
                        Assert.False(node.IsDreamRing);
                        Assert.Equal(2, node.Tier);
                        Assert.Equal(i + 1, node.RouteOrder);
                        Assert.Equal(i == 6 ? 1 : 3, node.MaxRank);
                        Assert.Equal(i == 6 ? 3 : 1, node.RankCost);
                        Assert.Equal(i == 0 ? null : nodes[i - 1].Id, node.PrerequisiteId);
                    }
                    Assert.Equal(21, nodes.Sum(t => t.MaxRank * t.RankCost));
                    // 目安は半分ずつ。記憶の仕組みに合わせて 2〜4 の幅を許す（v1.27 のレビューで調整）
                    Assert.InRange(nodes.Take(6).Count(t => t.LinkPerRank != null), 2, 4);
                    Assert.True(nodes[6].LinkPerRank != null || nodes[6].IsPowerNode);
                }
            }
        }

        [Fact]
        public void Rings_offer_eight_distinct_five_rank_stats_without_route_dependencies()
        {
            var expected = new[] { Stat.AttackPct, Stat.PowerPct, Stat.MaxHealthPct, Stat.Armor,
                Stat.MaxHealthFlat, Stat.AttackSpeedPct, Stat.Tenacity, Stat.HealthRegen }; // 記憶加速はエッセンスで手に入りやすいので外した
            foreach (string hero in Memories.Keys)
            {
                var ring = HeroStarRoutes.All.Where(t => t.HeroKey == hero && t.IsDreamRing).ToArray();
                Assert.Equal(expected.OrderBy(x => x), ring.Select(t => t.Stat).OrderBy(x => x));
                Assert.All(ring, t =>
                {
                    Assert.Equal(5, t.MaxRank);
                    Assert.Equal(1, t.RankCost);
                    Assert.Equal(2, t.Tier);
                    Assert.Null(t.RouteId);
                    Assert.Null(t.RouteMemory);
                    Assert.Null(t.PrerequisiteId);
                    Assert.Null(t.LinkPerRank);
                    Assert.False(t.IsKeystone);
                    Assert.False(t.IsPowerNode);
                });
                Assert.Equal(40, ring.Sum(t => t.MaxRank * t.RankCost));
            }
        }

        [Fact]
        public void Added_ids_and_localized_names_are_safe_for_registry_and_display()
        {
            Assert.Equal(HeroSigils.All.Count, HeroSigils.All.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
            var people = new Regex(@"\b(Vesper|Lacerta|Cetus|Yubar|Husk|Mist|Nachia|Aurena|Bismuth|Astrid|El|Fenrir)\b", RegexOptions.IgnoreCase);
            string[] japanesePeople = { "ヴェスパー", "ラセルタ", "ケトゥス", "ユバール", "空殻", "ミスト", "ナキア", "アウレナ", "ビスマス", "アストリッド", "エルの", "フェンリル" };
            foreach (var node in HeroStarRoutes.All)
            {
                Assert.Matches(@"^[a-z0-9.\-]+$", node.Id);
                Assert.True(Content.TryGetTalent(node.Id, out var registered));
                Assert.Same(node, registered);
                Assert.Matches(@"[\u3040-\u30ff\u3400-\u9fff]", node.Name.Ja);
                Assert.Matches(@"[A-Za-z]", node.Name.En);
                Assert.False(people.IsMatch(node.Name.En), node.Name.En);
                Assert.DoesNotContain(japanesePeople, node.Name.Ja.Contains);
                Assert.DoesNotContain("St_", node.Name.En);
                Assert.DoesNotContain("St_", node.Name.Ja);
            }
        }

        [Fact]
        public void Links_target_only_their_memory_and_remain_inside_single_memory_caps()
        {
            foreach (var route in HeroStarRoutes.All.Where(t => t.RouteId != null).GroupBy(t => t.RouteId))
            {
                var links = route.Where(t => t.LinkPerRank != null).ToArray();
                foreach (var node in links)
                {
                    var link = node.LinkPerRank;
                    Assert.Equal(new[] { node.RouteMemory }, link.Requires);
                    Assert.True(Links.Validate(link), node.Id);
                    Assert.InRange(link.Value, 1, Links.Cap(link.Kind, 1) / node.MaxRank);
                    // Identities never emit OnSkillUsed. Moonlight Pact's passive-only
                    // constellation must retain useful stars as well.
                    if (node.RouteMemory.StartsWith("St_D_", StringComparison.Ordinal)
                        || node.RouteMemory == "St_Q_MoonlightPact")
                        Assert.True(link.Kind == LinkKind.Attune || link.Kind == LinkKind.Guard, node.Id);
                }
                foreach (var kind in links.GroupBy(t => t.LinkPerRank.Kind))
                    Assert.InRange(kind.Sum(t => t.LinkPerRank.Value * t.MaxRank), 1, Links.Cap(kind.Key, 1));
            }
        }

        [Theory]
        [InlineData("Hero_Vesper", 7, 60, 239)]
        [InlineData("Hero_Lacerta", 7, 60, 239)]
        [InlineData("Hero_Cetus", 7, 60, 238)]
        [InlineData("Hero_Yubar", 7, 60, 238)]
        [InlineData("Hero_Husk", 7, 60, 238)]
        [InlineData("Hero_Mist", 7, 60, 238)]
        [InlineData("Hero_Nachia", 7, 60, 238)]
        [InlineData("Hero_Aurena", 7, 60, 238)]
        [InlineData("Hero_Bismuth", 7, 60, 238)]
        public void Whole_tree_has_more_choices_than_the_point_budget(string hero, int routes, int addedStars, int capacity)
        {
            var added = HeroStarRoutes.All.Where(t => t.HeroKey == hero).ToArray();
            Assert.Equal(routes, added.Where(t => t.RouteId != null).Select(t => t.RouteId).Distinct().Count());
            Assert.Equal(addedStars, added.Length);
            var tree = HeroSigils.TreeFor(hero).ToArray();
            Assert.Equal(addedStars + 13, tree.Length);
            Assert.Equal(2, tree.Count(t => t.IsKeystone));
            // Both keystones count in the conservative power audit below, but only
            // one can be purchased: capacity must not count an impossible second key.
            int available = tree.Where(t => !t.IsKeystone).Sum(t => t.MaxRank * t.RankCost) + Content.KeystoneCost;
            Assert.Equal(capacity, available);
            Assert.True(available > StarProgression.MaxPoints + 4);
        }

        [Fact]
        public void Whole_tree_max_ranks_leave_twenty_percent_power_headroom_and_do_not_clip_stats()
        {
            foreach (string hero in Memories.Keys)
            {
                var tree = HeroSigils.TreeFor(hero).ToArray();
                foreach (var power in tree.Where(t => t.IsKeystone || t.IsPowerNode)
                    .GroupBy(t => t.IsKeystone ? t.Power : t.RankPower))
                {
                    int total = power.Sum(t => t.IsKeystone ? t.PowerValue : t.PerRank * t.MaxRank);
                    Assert.True((long)total * 5 <= (long)Content.PowerCap(power.Key) * 4,
                        hero + " / " + power.Key + " = " + total);
                }
                foreach (var stat in tree.Where(t => !t.IsKeystone && !t.IsPowerNode && t.LinkPerRank == null && t.Stat != Stat.EssenceSlotIdentity).GroupBy(t => t.Stat)) // 枠の星は2本取っても +1 に抑える（EssenceSlots）
                {
                    int total = stat.Sum(t => t.PerRank * t.MaxRank);
                    Assert.InRange(total, 1, Content.StatCap(stat.Key));
                }
            }
        }

        [Theory]
        [InlineData("St_Q_Discipline", Stat.AttackPct)]
        [InlineData("St_M_Charge", Stat.MaxHealthPct)]
        [InlineData("St_R_PrecisionShot", Stat.PowerPct)]
        [InlineData("St_R_FrozenFists", Stat.AttackPct)]
        [InlineData("St_Q_DeathMark", Stat.PowerPct)]
        [InlineData("St_R_AnnihilationStance", Stat.PowerPct)]
        [InlineData("St_D_AstridsMasterpieceEnGarde", Stat.PowerPct)]
        [InlineData("St_D_AstridsMasterpiecePriorite", Stat.AttackPct)]
        [InlineData("St_M_DreamyWaltz", Stat.MaxHealthPct)]
        [InlineData("St_D_DisintegratingClaw", Stat.AttackPct)]
        [InlineData("St_Q_GoldenBurst", Stat.PowerPct)]
        [InlineData("St_QR_Innocence", Stat.PowerPct)]
        [InlineData("St_QR_InfernalTales", Stat.PowerPct)]
        [InlineData("St_QR_ValiantHeart", Stat.AttackPct)]
        [InlineData("St_QR_DistortedMind", Stat.AttackPct)]
        public void Memory_scaling_stars_follow_the_shipped_damage_or_shield_markers(string memory, Stat scaling)
        {
            Assert.Contains(HeroStarRoutes.All, t => t.RouteMemory == memory && t.LinkPerRank == null
                && !t.IsPowerNode && t.Stat == scaling && t.PerRank > 0);
        }

        [Fact]
        public void Double_shot_route_rewards_the_shots_after_a_memory_not_cooldown_recovery()
        {
            // v1.27：クールダウン短縮はエッセンスで手に入りやすいので、記憶の後の2発に乗る過負荷にした
            var build = BuildThroughMechanic("Hero_Lacerta", "St_D_DoubleTap");
            Assert.True(build.Get(Power.Overload) > 0);
            Assert.Equal(0, build.Get(Power.CriticalEcho));
        }

        [Fact]
        public void Parry_route_rewards_actual_damage_negation_and_expires_without_stacking()
        {
            var build = BuildThroughMechanic("Hero_Mist", "St_R_Parry");
            var runtime = new PowerRuntime(build, 0);
            Assert.False(runtime.TakePerfectRead(1, false));
            Assert.Equal(0, runtime.Current(1).AttackSpeedPct);
            Assert.True(runtime.TakePerfectRead(1, true));
            Assert.Equal(9, runtime.Current(1).AttackSpeedPct);
            Assert.False(runtime.TakePerfectRead(1.5f, true));
            Assert.True(runtime.TakePerfectRead(3, true));
            Assert.Equal(9, runtime.Current(5).AttackSpeedPct);
            Assert.Equal(0, runtime.Current(7).AttackSpeedPct);
        }

        private static Build BuildThroughMechanic(string hero, string memory)
        {
            var profile = Profile.CreateNew(1);
            var state = profile.Hero(hero);
            foreach (var core in HeroSigils.TreeFor(hero).Where(t => t.Tier == 1 && !t.IsKeystone).Take(2))
                state.Talents[core.Id] = 3;
            var route = HeroStarRoutes.All.Where(t => t.HeroKey == hero && t.RouteMemory == memory)
                .OrderBy(t => t.RouteOrder).ToArray();
            var mechanic = route.First(t => t.IsPowerNode);
            foreach (var node in route.TakeWhile(t => t.RouteOrder <= mechanic.RouteOrder))
                state.Talents[node.Id] = node == mechanic ? node.MaxRank : 1;
            return Build.Compute(profile, hero, 0);
        }
    }
}
