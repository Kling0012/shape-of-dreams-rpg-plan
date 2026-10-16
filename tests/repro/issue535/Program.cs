using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Mirror;
using SodRpg.Mod;
using UnityEngine;

// Issue #535 reproduction, before any fix. Three reported points:
//   §1 the host-side pickup snap (CulinaryPickupSnap.SnapDistance = 3f) preempts
//      the native synchronized pickup motor for ordinary drop distances, so
//      ingredients teleport to their owner instead of flying in;
//   §2 the coop native autocast interval (0.2 s) is gated on up-to-30 Hz logic
//      ticks, so the effective rate sits below the advertised 5 casts/s
//      (characterization only; triage keeps the code unchanged);
//   §3 CulinaryIngredientCap derives its Harmony owner id from the shared mod
//      loader id, so every loaded MOD copy patches under one owner and the old
//      copy's Stop removes the new copy's patches too.
// Production files are compiled into ModUnderTest/NativeApi (see the csproj);
// Harmony is the real Lib.Harmony, so patches, owner bookkeeping and the
// parameter-name binding behave as in the game.
internal static class Program
{
    private const string LoaderId = "DreamforgeRPG"; // stable id the mod loader passes to both copies

    private static int _failed;
    private static readonly List<object> Results = new List<object>();
    private static readonly List<Type> Copies = new List<Type>();

    private static void Check(string name, Action action)
    {
        Log.Warnings.Clear();
        UnityEngine.Transform.Writes.Clear();
        NetworkServer.active = true;
        try
        {
            action();
            Console.WriteLine("PASS " + name);
            Results.Add(new { name, status = "PASS" });
        }
        catch (Exception error)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
            Results.Add(new { name, status = "FAIL", error = error.Message });
        }
    }

    private static void Equal<T>(T expected, T actual, string detail)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{detail}; expected={expected}, actual={actual}");
    }

    private static void Contains(string expected, string actual, string detail)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{detail}; expected fragment={expected}; actual={actual}");
    }

    private static Type Magnet =>
        typeof(Culinary.ModUnderTest.AssemblyMarker).Assembly.GetType("SodRpg.Mod.CulinaryMagnet");

    private static object Call(Type type, string name, params object[] arguments) =>
        type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments);

    private static bool Enabled(Type type) =>
        (bool)type.GetField("_enabled", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static Type LoadCopy()
    {
        var assembly = Assembly.Load(File.ReadAllBytes(typeof(Culinary.ModUnderTest.AssemblyMarker).Assembly.Location));
        var type = assembly.GetType("SodRpg.Mod.CulinaryIngredientCap", throwOnError: true);
        Copies.Add(type);
        return type;
    }

    private static Hero Owner(float distance, bool knockedOut = false) =>
        new Hero { agentPosition = new Vector3(distance, 0, 0), isKnockedOut = knockedOut };

    // Mirrors the decompiled Gem_L_Culinary.OnKill: the pickup spawns at the
    // victim's position (origin here) while its owner stands 'distance' away.
    private static Ai_Culinary_Pickup Spawn(Hero owner)
    {
        var pickup = new Ai_Culinary_Pickup { info = new CastInfo { caster = owner } };
        pickup.OnCreate(); // real Harmony detour -> production AfterCreate
        return pickup;
    }

    private static void InstallMagnet()
    {
        Call(Magnet, "Install", new Harmony("dreamforge.repro.535.magnet"));
        Equal(true, Enabled(Magnet), "CulinaryMagnet installed");
    }

    public static int Main()
    {
        Check("control-magnet-raises-native-motor-values", () =>
        {
            InstallMagnet();
            var pickup = Spawn(Owner(8f));
            Equal(500f, pickup.attractionRange, "attractionRange raised for the synchronized native motor");
            Equal(0f, pickup.pickupDelay, "pickupDelay cleared");
            Equal(120f, pickup.maxVelocity, "maxVelocity raised");
            Equal(150f, pickup.velocityAccelerationMin, "velocityAccelerationMin raised");
            Equal(300f, pickup.velocityAccelerationMax, "velocityAccelerationMax raised");
        });

        Check("control-snap-keeps-close-or-dead-owner-pickups", () =>
        {
            InstallMagnet();
            Spawn(Owner(2.5f)); // inside SnapDistance: the native motor covers it
            Call(Magnet, "SnapToOwners");
            Spawn(Owner(8f, knockedOut: true)); // a knocked-out owner must not pull pickups
            Call(Magnet, "SnapToOwners");
            Equal(0, UnityEngine.Transform.Writes.Count, "no host transform write for close pickups or a downed owner");
        });

        Check("control-snaps-pickup-beyond-native-reach", () =>
        {
            InstallMagnet();
            Spawn(Owner(600f)); // beyond the raised attractionRange (500): left behind by a blink
            Call(Magnet, "SnapToOwners");
            Equal(1, UnityEngine.Transform.Writes.Count, "the snap insurance still covers pickups the motor cannot reach");
            Equal(600f, UnityEngine.Transform.Writes[0].x, "snapped onto the owner");
        });

        Check("snap-teleports-ordinary-drops-instead-of-native-flight", () =>
        {
            InstallMagnet();
            foreach (float distance in new float[] { 4f, 8f, 15f })
            {
                UnityEngine.Transform.Writes.Clear();
                Spawn(Owner(distance)); // ordinary kill distance; motor range is 500 m at 120 m/s
                Call(Magnet, "SnapToOwners");
                if (UnityEngine.Transform.Writes.Count > 0)
                    throw new InvalidOperationException(
                        $"a pickup dropped {distance.ToString("0.#", CultureInfo.InvariantCulture)} m from its living owner was host-teleported to the owner on the first frame; " +
                        "the native synchronized motor (attractionRange=500, maxVelocity=120) should carry it so the flight is visible (issue #535 §1)");
            }
        });

        Check("duplicate-mod-copies-share-one-harmony-owner", () =>
        {
            var first = LoadCopy();
            var second = LoadCopy();
            Equal(false, ReferenceEquals(first.Assembly, second.Assembly), "two independent MOD copies loaded");
            Call(first, "Install", new Harmony(LoaderId));
            Call(second, "Install", new Harmony(LoaderId));
            Equal(true, Enabled(first), "first copy installed");
            Equal(true, Enabled(second), "second copy installed");
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 40, stack = 1000 };
            effect.OnCreate(); // detour -> both copies record the native batch (40) and raise maxStack
            var hero = new Hero();
            hero.Status.Effect = effect;
            var pot = new Shrine_CookingPot();
            Equal(40, pot.GetIngredientCount(hero), "with both copies loaded the count is bounded to the native batch");
            var count = typeof(Shrine_CookingPot).GetMethod(nameof(Shrine_CookingPot.GetIngredientCount));
            var info = Harmony.GetPatchInfo(count);
            Equal(2, info.Postfixes.Count(p => p.owner == LoaderId + ".culinaryIngredientCap"),
                "both copies' postfixes are registered under the one shared owner id");
            Call(first, "Stop"); // the game unloads the older copy while the newer one keeps running
            Equal(true, Enabled(second), "the surviving copy still reports itself enabled");
            Equal(40, pot.GetIngredientCount(hero),
                "the surviving copy's ingredient cap must still bound the count after the old copy stops (issue #535 §3)");
        });

        Check("control-distinct-owner-ids-keep-second-copy-patched", () =>
        {
            var first = LoadCopy();
            var second = LoadCopy();
            Call(first, "Install", new Harmony(LoaderId + ".old"));
            Call(second, "Install", new Harmony(LoaderId + ".new"));
            Equal(true, Enabled(first), "first copy installed");
            Equal(true, Enabled(second), "second copy installed");
            var effect = new Se_Gem_L_Culinary_Stack { maxStack = 40, stack = 1000 };
            effect.OnCreate();
            var hero = new Hero();
            hero.Status.Effect = effect;
            var pot = new Shrine_CookingPot();
            Equal(40, pot.GetIngredientCount(hero), "bounded before the old copy stops");
            Call(first, "Stop");
            Equal(40, pot.GetIngredientCount(hero), "with distinct owner ids the surviving copy keeps its patches");
        });

        Check("control-argument-name-mismatch-fails-loud-at-patch-time", () =>
        {
            // The production postfixes inject target parameters by name (hero, value).
            // A game-side rename must surface as a patch-time error that Install
            // turns into a warning plus fail-soft disable, not a silent no-op.
            var target = typeof(Shrine_CookingPot).GetMethod(nameof(Shrine_CookingPot.GetIngredientCountRenamed));
            try
            {
                new Harmony("dreamforge.repro.535.names").Patch(target,
                    postfix: new HarmonyMethod(typeof(Program).GetMethod(nameof(CountPostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)));
                throw new InvalidOperationException("the renamed-parameter patch applied without an error; name binding did not fail loud");
            }
            catch (Exception ex)
            {
                Contains("hero", ex.Message, "Harmony must reject the unbindable parameter loudly");
            }
        });

        Check("autocast-coop-cadence-quantized-below-advertised-rate", () =>
        {
            AutoCastCadence.Compute(0f, true, true, true, out float interval, out int castsPerSecond);
            Equal(0.2f, interval, "coop native cadence interval");
            Equal(5, castsPerSecond, "advertised rolling per-second cap");
            // Mirrors NativeBismuthAutoCast.Scan's gate: cast on the first logic
            // tick where now - LastCastTime >= interval, ticks at up to 30 Hz.
            float ideal = SimulateCasts(interval, 1f / 30f);
            float slowed = SimulateCasts(interval, 0.0333f); // ticks 0.1% longer than 30 Hz
            Console.WriteLine("  interval=" + interval.ToString(CultureInfo.InvariantCulture) +
                "s, ideal 30Hz=" + ideal.ToString("0.00", CultureInfo.InvariantCulture) +
                "/s, 0.0333s ticks=" + slowed.ToString("0.00", CultureInfo.InvariantCulture) + "/s");
            if (ideal > castsPerSecond + 0.01f)
                throw new InvalidOperationException("ideal ticks must not exceed the advertised cap");
            if (slowed >= castsPerSecond - 0.01f)
                throw new InvalidOperationException($"with 0.0333 s ticks the effective rate {slowed.ToString("0.00", CultureInfo.InvariantCulture)}/s must stay below the advertised {castsPerSecond}/s (issue #535 §2 characterization)");
            if (slowed < 4.2f)
                throw new InvalidOperationException($"effective rate {slowed.ToString("0.00", CultureInfo.InvariantCulture)}/s fell outside the expected quantized range");
        });

        try { Call(Magnet, "Stop"); } catch { /* cleanup only */ }
        foreach (var copy in Copies) try { Call(copy, "Stop"); } catch { /* cleanup only */ }
        Console.WriteLine(JsonSerializer.Serialize(
            new { total = Results.Count, failed = _failed, passed = Results.Count - _failed, cases = Results },
            new JsonSerializerOptions { WriteIndented = true }));
        return _failed == 0 ? 0 : 1;
    }

    // Production-shaped postfix (AfterIngredientCount): injects the target's
    // first parameter by the name "hero".
    private static void CountPostfix(Hero hero, ref int __result)
    {
        if (hero != null && hero.Status.Effect != null)
            __result = Math.Min(__result, hero.Status.Effect.stack);
    }

    private static float SimulateCasts(float interval, float dt)
    {
        int casts = 0;
        float last = float.NegativeInfinity;
        for (int k = 0; k < 1800; k++) // 60 s of logic ticks
        {
            float now = k * dt;
            if (now - last < interval) continue;
            casts++;
            last = now;
        }
        return casts / 60f;
    }
}
