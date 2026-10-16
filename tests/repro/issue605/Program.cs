using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using SodRpg.Mod;

internal static class Program
{
    private static readonly List<object> Results = new List<object>();
    private static int failed;

    private static SkillTrigger NewSkill(string slot, int charges = 3)
    {
        var hero = new Hero();
        var skill = new SkillTrigger { owner = hero };
        skill.configs[0].maxCharges = charges;
        skill.configs[0].addedCharges = charges;
        skill.currentCharges[0] = charges;
        switch (slot)
        {
            case "Q": hero.Skill.Q = skill; break;
            case "W": hero.Skill.W = skill; break;
            case "E": hero.Skill.E = skill; break;
            case "R": hero.Skill.R = skill; break;
            case "Movement": hero.Skill.Movement = skill; break;
            case "Identity": hero.Skill.Identity = skill; break;
            default: throw new ArgumentException(slot);
        }
        return skill;
    }

    private static string Observe(SkillTrigger skill)
        => $"charges={skill.currentConfigCurrentCharge}; recharge={skill.currentConfigCooldownTime:F2}; "
            + $"minimumDelay={skill.currentMinimumDelays[0]:F2}; cast={skill.CanBeCast()}; reserve={skill.CanBeReserved()}";

    private static void Check(string name, Func<(bool passed, string observation)> action)
    {
        var result = action();
        if (!result.passed) failed++;
        Results.Add(new { name, passed = result.passed, observation = result.observation });
        Console.WriteLine($"{(result.passed ? "PASS" : "FAIL")} {name}: {result.observation}");
    }

    private static int Main()
    {
        var owner = new Harmony("issue605.repro");
        Mirror.NetworkServer.active = true;
        try
        {
            Check("native/three-charges-usable-during-recharge", () =>
            {
                var skill = NewSkill("Q");
                skill.CompleteCast();
                skill.Tick(0.15f); // past native minimumDelay=0.1, before MOD floor=2.4
                var button = new UI_InGame_SkillButton(skill);
                button.FrameUpdate();
                return (skill.currentConfigCurrentCharge == 2 && skill.CanBeCast() && skill.CanBeReserved()
                    && skill.currentConfigCooldownTime > 0f && button.CooldownText == "", Observe(skill));
            });

            foreach (var patch in new[] { typeof(MemoryReuseOnCast), typeof(MemoryReuseCastGate), typeof(MemoryReuseCooldownDisplay) })
                owner.CreateClassProcessor(patch).Patch();
            if (NativeMemoryReuse.Disabled || Log.Warnings.Count > 0 || owner.GetPatchedMethods().Count() != 4)
                throw new InvalidOperationException("Required production patches did not all attach: " + string.Join("; ", Log.Warnings));

            foreach (string slot in new[] { "Q", "W", "E", "R" })
            {
                Check("stock/" + slot + "-remaining-charges-remain-usable", () =>
                {
                    var skill = NewSkill(slot);
                    skill.CompleteCast();
                    skill.Tick(0.15f);
                    return (skill.currentConfigCurrentCharge == 2 && skill.CanBeCast() && skill.CanBeReserved(),
                        "Expected cast/reserve=True once native 0.1s delay expired; " + Observe(skill));
                });
            }

            Check("display/spare-charges-hide-main-cooldown", () =>
            {
                var skill = NewSkill("Q");
                skill.CompleteCast();
                skill.Tick(0.15f);
                var button = new UI_InGame_SkillButton(skill);
                button.FrameUpdate();
                return (button.Charges == 2 && button.CooldownText == "" && button.CooldownFill == 0f,
                    $"Expected main cooldown text empty/fill=0 with 2 charges; text='{button.CooldownText}'; fill={button.CooldownFill:F4}; " + Observe(skill));
            });

            Check("control/charged-memory-unlocks-after-mod-interval", () =>
            {
                var skill = NewSkill("Q");
                skill.CompleteCast();
                skill.Tick(2.5f);
                return (skill.currentConfigCurrentCharge == 2 && skill.CanBeCast() && skill.CanBeReserved()
                    && skill.currentConfigCooldownTime > 0f, Observe(skill));
            });

            foreach (string slot in new[] { "Movement", "Identity" })
            {
                Check("control/" + slot + "-charges-still-usable", () =>
                {
                    var skill = NewSkill(slot);
                    skill.CompleteCast();
                    skill.Tick(0.15f);
                    return (skill.currentConfigCurrentCharge == 2 && skill.CanBeCast() && skill.CanBeReserved(), Observe(skill));
                });
            }

            Check("control/single-charge-retains-reuse-guard", () =>
            {
                var skill = NewSkill("Q", 1);
                skill.CompleteCast();
                skill.SetCharge(0, 1); // full refund cannot bypass the existing single-charge loop guard
                skill.Tick(0.15f);
                return (skill.currentConfigCurrentCharge == 1 && !skill.CanBeCast() && !skill.CanBeReserved(), Observe(skill));
            });

            Check("control/last-charge-requires-recharge", () =>
            {
                var skill = NewSkill("Q");
                skill.currentCharges[0] = 1;
                skill.CompleteCast();
                skill.Tick(2.5f);
                bool blocked = skill.currentConfigCurrentCharge == 0 && !skill.CanBeCast() && !skill.CanBeReserved();
                string empty = Observe(skill);
                skill.Tick(10f);
                return (blocked && skill.currentConfigCurrentCharge == 3 && skill.CanBeCast() && skill.CanBeReserved(),
                    "after last use: " + empty + "; after recovery: " + Observe(skill));
            });

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = failed > 0 ? "REPRODUCED" : "NOT_REPRODUCED",
                total = Results.Count,
                passed = Results.Count - failed,
                failed,
                cases = Results
            }, new JsonSerializerOptions { WriteIndented = true }));
            return failed > 0 ? 1 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("HARNESS ERROR: " + error);
            return 2;
        }
        finally
        {
            foreach (var method in owner.GetPatchedMethods().ToArray())
                owner.Unpatch(method, HarmonyPatchType.All, owner.Id);
            Mirror.NetworkServer.active = false;
        }
    }
}
