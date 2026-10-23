using System;
using System.Collections.Generic;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;

internal static class Program
{
    private static readonly List<object> Results = new List<object>();
    private static int failed;
    private static void Check(string name, bool passed, string observation)
    {
        if (!passed) failed++;
        Results.Add(new { name, passed, observation });
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {observation}");
    }
    private static int Main()
    {
        try
        {
            Time.time = 10f;
            var neutral = new HostAuthority(DreamPressure.Neutral);
            var control = new Hero();
            var single = neutral.Register();
            neutral.Hit(single, control);
            neutral.Hit(single, control);
            Check("control/neutral-single-enemy-cooldown", Math.Abs(control.received - 20f) < 0.001f,
                $"two same-time hits on one enemy: received={control.received}; health={control.currentHealth}");

            var pressure = DreamPressure.ForPlayer(Content.MaxDreamLevel, StarProgression.MaxPoints)
                .WithRunModifiers(DreamDepth.Maximum).WithInfinityPressure(InfinityRunState.MaximumPressureStage);
            var host = new HostAuthority(pressure);
            var hero = new Hero();
            for (int i = 0; i < 3; i++) host.Hit(host.Register(), hero);
            Check("thorned/three-reflectors-at-legal-high-pressure-survive", hero.currentHealth > 0f,
                $"level={Content.MaxDreamLevel}; stars={StarProgression.MaxPoints}; depth={pressure.Depth}; "
                + $"infinity={pressure.InfinityStage}; waypoint=1; damageMultiplier={pressure.DamageMultiplier:F4}; "
                + $"pre-processor reflection=20 per enemy; received={hero.received:F4}; health={hero.currentHealth:F4}; "
                + "initialHealth=1000; three distinct enemies at the same Time.time; no mitigation");

            foreach (float pressureValue in new[] { 0f, 0.9f, 0.95f, 1f })
            {
                ManagerBase<GraphicsManager>.instance.perfPressureStrength = pressureValue;
                var spawner = new Ai_QR_Innocence_Spawner();
                spawner.Run();
                Check($"pure-soul/pressure-{pressureValue:F2}-creates-projectile", spawner.spawned > 0 && spawner.destroyed,
                    $"server=true; players=1; clampedCount={spawner.clampedCount}; offset=0; target available; "
                    + $"spawned={spawner.spawned}; destroyed={spawner.destroyed}; coroutine drained without real-time waits");
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = failed > 0 ? "REPRODUCED" : "NOT_REPRODUCED",
                total = Results.Count, passed = Results.Count - failed, failed, cases = Results
            }, new JsonSerializerOptions { WriteIndented = true }));
            return failed > 0 ? 1 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("HARNESS ERROR: " + error);
            return 2;
        }
    }
}
