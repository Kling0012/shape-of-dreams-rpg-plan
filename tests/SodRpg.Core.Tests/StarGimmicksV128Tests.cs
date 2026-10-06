using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.28「記憶の仕掛け」のルートデータ。設計表 docs/specs/v1.28-memory-gimmicks-table.md どおりに
    /// 星2（mid2・3段で表の値は1段あたり）・星4（mid・同じ）・頂点（cap・1段で表の値のまま）へ
    /// 仕掛けが置かれているかを確かめる。仕掛け専用の星は能力値・連携・固有効果を持たず、
    /// Build の能力値・固有効果の予算にも数えない。頂点は「記憶の冴え」と併持してよい。
    /// </summary>
    public class StarGimmicksV128Tests
    {
        // 設計表の「none」ルート（8ルート）。仕掛けを置かず、常時の効果のまま残す。
        private static readonly HashSet<string> NoneRoutes = new HashSet<string>
        {
            "St_M_NimbleDodge", "St_M_Flicker", "St_M_FlashStep", "St_M_FastFeet",
            "St_M_DreamyWaltz", "St_M_FeatheryDash", "St_M_Sprint", "St_D_CircleOfLife",
        };

        [Fact]
        public void Every_non_none_route_has_gimmicks_at_stars_two_four_and_seven()
        {
            var routes = HeroStarRoutes.All.Where(t => t.RouteId != null && t.RouteMemory != null)
                .GroupBy(t => t.RouteMemory).ToList();
            Assert.Equal(62, routes.Count); // 仕掛けあり54ルート＋none 8ルート
            Assert.Equal(54, routes.Count(g => !NoneRoutes.Contains(g.Key)));
            foreach (var route in routes)
            {
                var stars = route.OrderBy(t => t.RouteOrder).ToList();
                Assert.Equal(7, stars.Count(t => t.RouteOrder <= 7));
                if (NoneRoutes.Contains(route.Key))
                {
                    Assert.DoesNotContain(stars, t => t.Gimmick != null);
                    continue;
                }
                foreach (int order in new[] { 2, 4, 7 })
                {
                    var star = stars.Single(t => t.RouteOrder == order);
                    Assert.NotNull(star.Gimmick);
                    Assert.True(star.Gimmick.Value > 0, star.Id);
                    Assert.Equal(order == 7 ? 1 : 3, star.MaxRank); // 頂点は1段、中段は3段
                }
            }
        }


        [Fact]
        public void Caps_carry_their_gimmick_and_at_most_the_keen_memory_damage_link()
        {
            var caps = HeroStarRoutes.All.Where(t => t.Gimmick != null && t.RouteOrder == 7).ToList();
            Assert.Equal(54, caps.Count);
            foreach (var t in caps)
            {
                Assert.False(t.IsPowerNode);
                if (t.LinkPerRank != null)
                {
                    // 冴えを残す頂点：「記憶の冴え」と仕掛けの両方（設計表で冴えのあった17ルート）
                    Assert.Equal(LinkKind.MemoryDamage, t.LinkPerRank.Kind);
                }
                else
                {
                    Assert.Equal(0, t.PerRank);
                }
            }
            Assert.Equal(17, caps.Count(t => t.LinkPerRank != null));
        }


        [Fact]
        public void Bismuth_is_back_to_six_routes_without_the_explosion_artist()
        {
            Assert.Equal(6, HeroStarRoutes.All.Where(t => t.HeroKey == "Hero_Bismuth" && t.RouteId != null)
                .Select(t => t.RouteId).Distinct().Count());
            Assert.DoesNotContain(HeroStarRoutes.All, t => t.RouteMemory == "St_D_ExplosionArtist");
            // アイデンティティはプリズムの視界のみ。エッセンスの枠の星もルートと一緒に消える
            Assert.Single(HeroStarRoutes.All.Where(t => t.HeroKey == "Hero_Bismuth" && t.RouteMemory != null)
                .Select(t => t.RouteMemory).Where(m => m.StartsWith("St_D_", StringComparison.Ordinal)).Distinct());
            Assert.Equal(2, HeroStarRoutes.All.Count(t => t.HeroKey == "Hero_Bismuth" && t.RouteOrder == 8));
        }


        [Fact]
        public void Intervals_survive_only_on_the_eight_rows_that_need_them()
        {
            var withCd = HeroStarRoutes.All.Where(t => t.Gimmick != null && t.Gimmick.Cooldown > 0).ToList();
            Assert.Equal(8, withCd.Count);
            Assert.All(withCd, t => Assert.InRange(t.Gimmick.Cooldown, 0.5f, 4f));
            Assert.Contains(withCd, t => Math.Abs(t.Gimmick.Cooldown - 1.5f) < 0.001f); // 蛇の追い咬み
            // クールダウンを戻す側（Recharge・Reload・RechargeOther）はすべて間隔なし
            var restoring = HeroStarRoutes.All.Where(t => t.Gimmick != null
                && (t.Gimmick.Effect == GimmickEffect.Recharge || t.Gimmick.Effect == GimmickEffect.Reload
                    || t.Gimmick.Effect == GimmickEffect.RechargeOther)).ToList();
            Assert.DoesNotContain(restoring, t => t.Gimmick.Cooldown > 0);
        }


        [Fact]
        public void Gimmick_stars_add_nothing_to_the_stat_and_power_budgets()
        {
            foreach (var star in HeroStarRoutes.All.Where(t => t.Gimmick != null && t.RouteOrder == 2))
            {
                var p = Profile.CreateNew(1);
                var h = p.Hero(star.HeroKey);
                h.StarXp = StarProgression.TotalXpForPoints(60);
                TreeTestPaths.Connect(p, star.HeroKey, star.Id);
                var before = Build.Compute(p, star.HeroKey, 0);
                h.Talents[star.Id] = star.MaxRank;
                var after = Build.Compute(p, star.HeroKey, 0);
                foreach (var stat in before.Stats.Keys.Concat(after.Stats.Keys).Distinct())
                    Assert.Equal(before.Get(stat), after.Get(stat));
                Assert.Equal(before.Powers.Keys, after.Powers.Keys);
                foreach (var power in before.Powers.Keys.Where(power => !StarDamageScaling.IsDamage(power)))
                    Assert.Equal(before.Get(power), after.Get(power));
                var entry = Assert.Single(after.Gimmicks, g => g.StarId == star.Id);
                Assert.Equal(star.Gimmick.Value * star.MaxRank, entry.Def.Value);
                Assert.Equal(star.RouteMemory, entry.Memory);
            }
        }

        [Fact]
        public void Build_collects_route_gimmicks_with_value_times_ranks()
        {
            foreach (var route in HeroStarRoutes.All.Where(t => t.RouteId != null)
                .GroupBy(t => t.RouteMemory).Where(g => !NoneRoutes.Contains(g.Key)))
            {
                var p = Profile.CreateNew(1);
                var hero = route.First().HeroKey;
                var h = p.Hero(hero);
                h.StarXp = StarProgression.TotalXpForPoints(60);
                var stars = route.OrderBy(t => t.RouteOrder).ToList();
                TreeTestPaths.Connect(p, hero, stars[0].Id);
                foreach (var s in stars.Where(t => t.RouteOrder <= 7))
                    h.Talents[s.Id] = s.MaxRank;
                var b = Build.Compute(p, hero, 0);
                foreach (var s in stars.Where(t => t.Gimmick != null))
                {
                    // Reload・RechargeOther の受け渡し上限は Gimmicks.cs の側で決まる
                    if (Gimmicks.Cap(s.Gimmick.Effect) == 0) continue;
                    var entry = Assert.Single(b.Gimmicks, g => g.StarId == s.Id);
                    Assert.Equal(s.Gimmick.Value * s.MaxRank, entry.Def.Value);
                    Assert.Equal(s.Gimmick.Trigger, entry.Def.Trigger);
                    Assert.Equal(s.Gimmick.Effect, entry.Def.Effect);
                    Assert.Equal(s.Gimmick.Arg, entry.Def.Arg);
                    Assert.Equal(s.Gimmick.Cooldown, entry.Def.Cooldown, 3);
                }
            }
        }

    }
}
