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
            ["Hero_Bismuth"] = new[] { "St_D_PrismaticEyes", "St_M_Sprint", "St_QR_Innocence", "St_QR_InfernalTales", "St_QR_ValiantHeart", "St_QR_DistortedMind" }, // v1.28：華麗なる芸術家のルートは外した
        };

        [Fact]
        public void Every_shipped_memory_has_exactly_one_complete_branch_for_its_owner()
        {
            Assert.Equal(532, HeroStarRoutes.All.Count); // v1.27：540（Bismuth に共通アイデンティティのルート）→ v1.28：芸術家のルートとその枠の星を外し62ルート
            Assert.Equal(Memories.Keys.OrderBy(x => x), HeroStarRoutes.All.Select(t => t.HeroKey).Distinct().OrderBy(x => x));
            foreach (var hero in Memories)
            {
                var branches = HeroStarRoutes.All.Where(t => t.HeroKey == hero.Key && t.RouteId != null)
                    .GroupBy(t => t.RouteMemory).ToArray();
                Assert.Equal(hero.Value.OrderBy(x => x), branches.Select(g => g.Key).OrderBy(x => x));
                Assert.Equal(hero.Key == "Hero_Bismuth" ? 1 : 2, branches.Count(g => g.Key.StartsWith("St_D_", StringComparison.Ordinal))); // Bismuth のアイデンティティは1つ
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
                    }
                    Assert.Equal(21, nodes.Sum(t => t.MaxRank * t.RankCost));
                    // 目安は半分ずつ。記憶の仕組みに合わせて 2〜4 の幅を許す（v1.27 のレビューで調整）
                    Assert.InRange(nodes.Take(6).Count(t => t.LinkPerRank != null), 2, 4);
                    Assert.True(nodes[6].LinkPerRank != null || nodes[6].IsPowerNode || nodes[6].Gimmick != null); // v1.28：頂点は仕掛けだけの星もある
                }
            }
        }

        [Fact]
        public void Inner_bridges_spend_three_ranks_on_combos_and_outer_bridges_keep_stats()
        {
            foreach (string hero in Memories.Keys)
            {
                var ring = HeroStarRoutes.All.Where(t => t.HeroKey == hero && t.IsDreamRing).ToArray();
                Assert.Equal(hero == "Hero_Bismuth" ? 6 : 7, ring.Count(t => PairCombos.ForBridge(t.Id) != null));
                Assert.All(ring, t =>
                {
                    Assert.Equal(PairCombos.ForBridge(t.Id) == null ? 5 : 3, t.MaxRank);
                    Assert.Equal(PairCombos.ForBridge(t.Id) == null ? 1 : 0, t.PerRank > 0 ? 1 : 0);
                    Assert.Equal(1, t.RankCost);
                    Assert.Equal(2, t.Tier);
                    Assert.Null(t.RouteId);
                    Assert.Null(t.RouteMemory);
                    Assert.Null(t.LinkPerRank);
                    Assert.False(t.IsKeystone);
                    Assert.False(t.IsPowerNode);
                });
                Assert.Equal(hero == "Hero_Bismuth" ? 28 : 26, ring.Sum(t => t.MaxRank * t.RankCost));
            }
        }

        [Fact]
        public void Added_ids_and_localized_names_are_safe_for_registry_and_display()
        {
            Assert.Equal(HeroSigils.All.Count, HeroSigils.All.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
            var peopleWord = new Regex(@"^(Vesper|Lacerta|Cetus|Yubar|Husk|Mist|Nachia|Aurena|Bismuth|Astrid|El|Fenrir)$", RegexOptions.IgnoreCase);
            string[] japanesePeople = { "ヴェスパー", "ラセルタ", "ケトゥス", "ユバール", "空殻", "ミスト", "ナキア", "アウレナ", "ビスマス", "アストリッド", "エルの", "フェンリル" };
            foreach (var node in HeroStarRoutes.All)
            {
                Assert.Matches(@"^[a-z0-9.\-]+$", node.Id);
                Assert.True(Content.TryGetTalent(node.Id, out var registered));
                Assert.Same(node, registered);
                Assert.Matches(@"[\u3040-\u30ff\u3400-\u9fff]", node.Name.Ja);
                Assert.Matches(@"[A-Za-z]", node.Name.En);
                // 旅人の名前が単語として残っていないか（ハイフン結合の「Mist-Rain＝霧雨」は別の語）
                foreach (var word in node.Name.En.Split(' '))
                    Assert.False(peopleWord.IsMatch(word.Trim('\'', '-', '.')), node.Name.En);
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
                    if (link.Kind != LinkKind.MemoryDamage)
                        Assert.InRange(link.Value, 1, Links.Cap(link.Kind, 1) / node.MaxRank);
                    // Identities never emit OnSkillUsed, and Moonlight Pact's Lone-Wolf
                    // constellation can disable its cast: OnSkillUse-driven links must not
                    // appear there. v1.28 memory damage only needs the memory equipped,
                    // so it stays valid for identity passives and Fenrir's attacks.
                    if (node.RouteMemory.StartsWith("St_D_", StringComparison.Ordinal)
                        || node.RouteMemory == "St_Q_MoonlightPact")
                        Assert.True(link.Kind != LinkKind.MemoryHaste && link.Kind != LinkKind.MemorySurge, node.Id);
                }
                foreach (var kind in links.GroupBy(t => t.LinkPerRank.Kind))
                {
                    if (kind.Key != LinkKind.MemoryDamage)
                        Assert.InRange(kind.Sum(t => t.LinkPerRank.Value * t.MaxRank), 1, Links.Cap(kind.Key, 1));
                }
            }
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
                foreach (var stat in tree.Where(t => !t.IsKeystone && !t.IsPowerNode && t.LinkPerRank == null && t.PerRank != 0 && t.Stat != Stat.EssenceSlotIdentity).GroupBy(t => t.Stat)) // 枠の星は2本取っても +1 に抑える（EssenceSlots）
                {
                    int total = stat.Sum(t => t.PerRank * t.MaxRank);
                    Assert.InRange(total, 1, Content.StatCap(stat.Key));
                }
            }
        }

        [Fact]
        public void Star_map_has_no_dodge_triggered_effects()
        {
            var banned = new[] { Power.EchoingDodge, Power.Sprint, Power.Whirlwind, Power.PerfectRead };
            foreach (var t in HeroSigils.All.Concat(HeroStarRoutes.All))
            {
                Assert.DoesNotContain(t.Power, banned);
                Assert.DoesNotContain(t.RankPower, banned);
                if (t.Power == Power.ShadowStep || t.RankPower == Power.ShadowStep)
                {
                    Assert.Equal("Hero_Husk", t.HeroKey);
                    Assert.Equal("h.husk.key2", t.Id);
                    Assert.True(t.IsKeystone);
                    Assert.Equal(Power.ShadowStep, t.Power);
                    Assert.Equal(Power.None, t.RankPower);
                }
                if (t.LinkPerRank != null && t.LinkPerRank.Requires.Any(r => r.StartsWith("St_M_", StringComparison.Ordinal)))
                    Assert.True(t.LinkPerRank.Kind == LinkKind.Guard || t.LinkPerRank.Kind == LinkKind.MemoryDamage, t.Id);
            }
        }
    }
}
