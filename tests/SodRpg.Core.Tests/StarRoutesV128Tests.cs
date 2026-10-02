using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.28「記憶の冴え」：ルートの「同調」はすべて置き換え、ゼロを残す。
    /// 「守り」は盾役の記憶（シールド・挑発・タックル・HP／防御ベース）と、
    /// それ自体がダメージを出さない記憶（冴えが乗らない）のルートだけに残す。
    /// </summary>
    public class StarRoutesV128Tests
    {
        // 守りを残すルートの一覧（RouteMemory の型名で指定）。
        private static readonly HashSet<string> GuardRoutes = new HashSet<string>
        {
            // Vesper：決意（HPベース＋回復）、重装タックル（シールド突進）、太陽の洗礼（挑発）、エルの聖域（味方シールド）
            "St_D_Resolve", "St_M_Charge", "St_R_BaptismOfSun", "St_R_SanctuaryOfEl",
            // Lacerta：速攻回避（回避のみ・ダメージなし）
            "St_M_NimbleDodge",
            // Cetus：氷の血脈（自己シールド）、フロストチャージ（HPタックル）、寒気を受け入れよ（味方シールド）、
            // 下がれ！（味方シールド・防御）、礼儀注入（防御・シールドの近接型）
            "St_D_IcyVeins", "St_M_FrostyCharge", "St_Q_EmbracingTheChill", "St_R_BackOff", "St_R_FrozenFists",
            // Yubar：変光星（テレポート回避・ダメージなし）、平静（無敵チャネリング・ダメージなし）
            "St_M_Flicker", "St_R_Tranquility",
            // Husk：瞬歩（回避のみ・ダメージなし）
            "St_M_FlashStep",
            // Mist：アンガルド（シールド付きの受け）、クイックフット（回避のみ）、パリィ（受け流し）
            "St_D_AstridsMasterpieceEnGarde", "St_M_FastFeet", "St_R_Parry",
            // Nachia：生命の循環（回復・ダメージなし）、夢幻のワルツ（味方シールド・ダメージなし）、
            // 自然の囁き（号令・ダメージなし）、蛇の祝福（防御バフ・回復）
            "St_D_CircleOfLife", "St_M_DreamyWaltz", "St_R_NaturesWhisper", "St_R_SerpentineBlessing",
            // Aurena：フェザーダッシュ（回避のみ・ダメージなし）
            "St_M_FeatheryDash",
            // Bismuth：歪な疾走（回避のみ・ダメージなし）
            "St_M_Sprint",
        };

        // 移動の記憶のうち、回避そのものがダメージを出すものだけ「記憶の冴え」を許す。
        private static readonly HashSet<string> DamagingMovements = new HashSet<string>
        {
            "St_M_Charge",      // 重装タックル：衝突で最大HP15%のダメージ
            "St_M_FrostyCharge", // フロストチャージ：突進で気ダメージ
        };

        [Fact]
        public void Routes_keep_no_attune_links()
        {
            Assert.DoesNotContain(HeroStarRoutes.All,
                t => t.LinkPerRank != null && t.LinkPerRank.Kind == LinkKind.Attune);
        }

        [Fact]
        public void Guard_links_remain_only_on_the_tank_or_non_damaging_allowlist()
        {
            var guarded = HeroStarRoutes.All
                .Where(t => t.RouteId != null && t.LinkPerRank != null && t.LinkPerRank.Kind == LinkKind.Guard)
                .ToArray();
            Assert.NotEmpty(guarded);
            foreach (var t in guarded)
                Assert.True(GuardRoutes.Contains(t.RouteMemory), t.Id + " / " + t.RouteMemory);
        }

        [Fact]
        public void Movement_routes_use_memory_damage_only_when_the_dodge_itself_deals_damage()
        {
            foreach (var t in HeroStarRoutes.All.Where(t => t.RouteMemory != null
                && t.RouteMemory.StartsWith("St_M_", StringComparison.Ordinal) && t.LinkPerRank != null))
            {
                if (DamagingMovements.Contains(t.RouteMemory))
                    Assert.True(t.LinkPerRank.Kind == LinkKind.Guard || t.LinkPerRank.Kind == LinkKind.MemoryDamage, t.Id);
                else
                    Assert.Equal(LinkKind.Guard, t.LinkPerRank.Kind);
            }
        }

        [Fact]
        public void Memory_damage_stays_inside_the_per_route_budget()
        {
            foreach (var route in HeroStarRoutes.All.Where(t => t.RouteId != null).GroupBy(t => t.RouteId))
            {
                var md = route.Where(t => t.LinkPerRank != null && t.LinkPerRank.Kind == LinkKind.MemoryDamage).ToArray();
                int mid = md.Where(t => t.RouteOrder < 7).Sum(t => t.LinkPerRank.Value * t.MaxRank);
                int top = md.Where(t => t.RouteOrder == 7).Sum(t => t.LinkPerRank.Value * t.MaxRank);
                Assert.InRange(mid, 0, Links.Cap(LinkKind.MemoryDamage, 1)); // 頂点以外で 40 まで
                Assert.InRange(top, 0, 20);                                  // 頂点はさらに 20 まで
                foreach (var t in md)
                {
                    if (t.RouteOrder == 7)
                        Assert.True(t.LinkPerRank.Value >= 15 && t.LinkPerRank.Value <= 20, t.Id); // 頂点は1段 15〜20
                    else
                        Assert.True(t.LinkPerRank.Value >= 3 && t.LinkPerRank.Value <= 5, t.Id);   // 中段は1段 3〜5
                }
            }
        }
    }
}
