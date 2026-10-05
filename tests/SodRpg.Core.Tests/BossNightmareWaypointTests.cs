using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #127：道標のどの組み合わせ・モードでもボスは悪夢化しない。通常の敵・エリートの悪夢化の確率は変わらない。
    /// ホストの出現処理（HostAuthority.ProcessSpawns → RollWaypointNightmare）が呼ぶのと同じ
    /// Nightmares.RollWaypoint を、全道標×全格×全深度で直接動かして確認する。
    /// </summary>
    public class BossNightmareWaypointTests
    {
        private static readonly MonsterTier[] NonBossTiers =
            { MonsterTier.Lesser, MonsterTier.Normal, MonsterTier.MiniBoss };

        private static IEnumerable<Waypoints.Totals> AllTotals()
        {
            yield return Waypoints.Sum(Waypoint.None); // 道標なし（ゾーン跨ぎ・失効後）
            foreach (var def in Waypoints.All) yield return def.Effects;
        }

        [Fact]
        public void Boss_never_becomes_a_nightmare_under_any_waypoint()
        {
            for (int depth = 0; depth <= 5; depth++)
                foreach (var totals in AllTotals())
                    for (ulong seed = 1; seed <= 128; seed++)
                    {
                        var rng = new Rng(seed);
                        Assert.Equal(NightmareAffix.None,
                            Nightmares.RollWaypoint(rng, MonsterTier.Boss, depth, 1.5, totals));
                        // 乱数も消費しない：ボスが出ただけで他の敵の抽選がずれない。
                        Assert.Equal(new Rng(seed).State, rng.State);
                    }
        }

        [Fact]
        public void Endless_night_still_nightmares_every_non_boss_enemy_with_one_trait()
        {
            var totals = Waypoints.Sum(Waypoint.EndlessNight);
            var seen = new HashSet<NightmareAffix>();
            foreach (var tier in NonBossTiers)
                for (int depth = 0; depth <= 5; depth++)
                    for (ulong seed = 1; seed <= 512; seed++)
                    {
                        var affix = Nightmares.RollWaypoint(new Rng(seed), tier, depth, 0, totals);
                        Assert.NotEqual(NightmareAffix.None, affix);
                        Assert.Equal(1, Nightmares.Count(affix));
                        Assert.Equal(affix, Nightmares.Sanitize((int)affix));
                        seen.Add(affix);
                    }
            // 20種すべての接頭が出る（選択範囲が1つも漏れていない）。
            Assert.Equal(Nightmares.AllAffixes.ToHashSet(), seen);
        }

        [Fact]
        public void Neutral_waypoints_keep_the_regular_nightmare_roll_unchanged()
        {
            var totals = Waypoints.Sum(Waypoint.WeaponRoad); // 悪夢化に介入しない道標
            foreach (var tier in NonBossTiers)
                for (int depth = 0; depth <= 5; depth++)
                    for (ulong seed = 1; seed <= 128; seed++)
                        Assert.Equal(
                            Nightmares.Roll(new Rng(seed), tier, depth, 1.25),
                            Nightmares.RollWaypoint(new Rng(seed), tier, depth, 1.25, totals));
            // 深さ0・道標なしなら通常の敵も悪夢化しない（既存の規則そのまま）。
            Assert.Equal(NightmareAffix.None,
                Nightmares.RollWaypoint(new Rng(1), MonsterTier.Normal, 0, 1, Waypoints.Sum(Waypoint.None)));
        }

        [Fact]
        public void Nightmare_hunt_keeps_its_delve_floor_but_excludes_bosses()
        {
            var totals = Waypoints.Sum(Waypoint.NightmareHunt);
            for (ulong seed = 1; seed <= 128; seed++)
                Assert.Equal(NightmareAffix.None,
                    Nightmares.RollWaypoint(new Rng(seed), MonsterTier.Boss, 0, 1, totals));
            // 悪夢狩りは深さ0でも通常の敵を深さ1として抽選する（既存の挙げ替えを維持）。
            Assert.Contains(Enumerable.Range(1, 512),
                seed => Nightmares.RollWaypoint(new Rng((ulong)seed), MonsterTier.Normal, 0, 1, totals)
                    != NightmareAffix.None);
        }

        [Fact]
        public void Host_spawn_simulation_over_a_waypoint_rotation_keeps_bosses_clean()
        {
            // 出発から数ゾーン、道標を替えながら出現処理を模す（#127 の再現経路：明けない夜のゾーンでボス）。
            var rotation = new[]
            {
                Waypoint.None, Waypoint.NightmareHunt, Waypoint.EndlessNight,
                Waypoint.BossHoard, Waypoint.EndlessNight, Waypoint.SupplyLine,
            };
            var rng = new Rng(0xDEADBEEFul);
            foreach (var waypoint in rotation)
            {
                var totals = Waypoints.Sum(waypoint);
                foreach (MonsterTier tier in Enum.GetValues(typeof(MonsterTier)))
                    for (int spawn = 0; spawn < 64; spawn++)
                    {
                        // ProcessSpawns と同じ丸め：未知の種別値はボスとして扱う。
                        var clamped = (MonsterTier)Math.Min((int)MonsterTier.Boss, (int)tier);
                        var affix = Nightmares.RollWaypoint(rng, clamped, 3, 1.2, totals);
                        if (clamped == MonsterTier.Boss) Assert.Equal(NightmareAffix.None, affix);
                    }
            }
        }
    }
}
