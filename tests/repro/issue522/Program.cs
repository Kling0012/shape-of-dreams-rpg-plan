using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;

internal static class Program
{
    private static int _failed;
    private static readonly List<object> Results = new List<object>();

    private static void Check(string name, Action action)
    {
        HostAuthority.Enemies.Clear();
        CoopDistanceNative.Clear();
        Actor.ParentReads = 0;
        Log.Warnings.Clear();
        Content.DisplayMeleeRangeConversion = false;
        Time.time = 0;
        NetworkedManagerBase<ActorManager>.softInstance = new ActorManager();
        NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager { currentRoom = new Room() };
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
        finally { Content.DisplayMeleeRangeConversion = false; }
    }

    private static void Equal<T>(T expected, T actual, string detail)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{detail}; expected={expected}, actual={actual}");
    }

    private static Hero Melee(Hero hero)
    {
        var attack = hero.Ability.attackAbility;
        attack.configs = new[] { new AttackTriggerConfig() };
        attack.currentConfigIndex = 0;
        attack.currentConfig = attack.configs[0];
        attack.currentConfig.spawnedInstanceRef.asset = new MeleeAttackInstance();
        return hero;
    }

    private static HostAuthority.HeroRuntime Runtime(Hero hero, int speed = 0, int range = 0)
    {
        var build = new Build();
        build.Stats[Stat.AttackSpeedPct] = speed;
        build.Stats[Stat.AttackRangePct] = range;
        return new HostAuthority.HeroRuntime
        {
            Hero = hero, Powers = new PowerRuntime(build, 0),
            BaseBonus = new StatBonus { attackSpeedPercentage = speed, attackRangePercentage = range }
        };
    }

    private static string Labels(string field)
    {
        return string.Join(" ", typeof(DreamforgeConfig).GetField(field).GetCustomAttributes(false)
            .OfType<LabelTextAttribute>().Select(a => a.Text));
    }

    private static void Contains(string expected, string actual, string detail)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{detail}; expected fragment={expected}; actual={actual}");
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static void DisplayMatchesAppliedSpeed(int speed, bool japanese)
    {
        Loc.Japanese = japanese;
        var rt = Runtime(Melee(new Hero_Vesper()), speed, 30);
        var host = new HostAuthority();
        host.Scan(rt);
        var ui = new DreamforgeUi(new ClientSession { LocalHero = rt.Hero }, new DreamforgeConfig());
        ui.Draw();
        string text = Content.FormatStat(Stat.AttackRangePct, 30);
        float effective = rt.BaseBonus.attackSpeedPercentage - speed;
        Contains("+" + Number(effective) + "%", text,
            $"speed={speed}%, range=30%, multiplier={CoopBalance.RangeToSpeedMultiplier}, cap={Content.StatCap(Stat.AttackSpeedPct)}%, applied speed={rt.BaseBonus.attackSpeedPercentage}%");
    }

    private static void HitShouldSkipBasicTraversal(bool enabled, Hero hero)
    {
        var host = new HostAuthority();
        host.ConfigureMeleeCoop(true, true, enabled, true);
        var rt = Runtime(hero);
        Actor chain = null;
        for (int i = 0; i < 128; i++) chain = new Actor { parentActor = chain };
        var victim = new Entity { Relation = EntityRelation.Enemy };
        // MemorySource's own traversal is required independently of Break. Measure it first.
        var memorySource = typeof(HostAuthority).GetMethod("MemorySource", BindingFlags.NonPublic | BindingFlags.Instance);
        Actor.ParentReads = 0;
        memorySource.Invoke(host, new object[] { chain });
        int requiredMemoryReads = Actor.ParentReads;
        Actor.ParentReads = 0;
        host.Hit(rt, new EventInfoDamage { actor = chain, victim = victim, damage = new DamageData(100) });
        Equal(requiredMemoryReads, Actor.ParentReads,
            $"Break enabled={enabled}, hero={hero.GetType().Name}: ineligible hit must not repeat the ancestry traversal");
    }

    public static int Main()
    {
        bool previousLanguage = Loc.Japanese;
        Check("control/component-melee-four-travelers", () =>
        {
            foreach (Hero hero in new Hero[] { new Hero_Vesper(), new Hero_Husk(), new Hero_Mist(), new Hero_Aurena() })
                Equal(true, TravelerCombatNative.IsMelee(Melee(hero)), hero.GetType().Name);
            var ranged = Melee(new Hero_Aurena());
            ranged.Ability.attackAbility.currentConfig.spawnedInstanceRef.asset = new AttackProjectile();
            Equal(false, TravelerCombatNative.IsMelee(ranged), "ranged Aurena");
        });
        Check("control/native-conversion-break-guard", () =>
        {
            var host = new HostAuthority();
            var rt = Runtime(Melee(new Hero_Vesper()), 70, 30);
            var enemy = new Entity { Relation = EntityRelation.Enemy, agentPosition = new Vector3(2, 0, 0) };
            HostAuthority.Enemies.Add(enemy);
            host.Scan(rt);
            Equal(80f, rt.BaseBonus.attackSpeedPercentage, "applied capped attack speed");
            Equal(0f, rt.BaseBonus.attackRangePercentage, "range removed");
            var damage = new DamageData(100) { actor = enemy };
            host.Guard(rt, ref damage, enemy);
            Equal(88f, damage.currentAmount, "guard reduction");
            host.Hit(rt, new EventInfoDamage { actor = new AttackTrigger(), victim = enemy, damage = new DamageData(100) });
            Equal(1, enemy.takenDamageProcessor.Entries.Count, "basic hit registers Break");
            var ally = Melee(new Hero_Husk());
            var allied = new DamageData(100) { actor = ally };
            enemy.takenDamageProcessor.Entries[0](ref allied, ally, enemy);
            Equal(112f, allied.currentAmount, "ally amplification");
        });
        foreach (bool japanese in new[] { true, false })
        {
            string language = japanese ? "ja" : "en";
            Check("display/uncapped-control/" + language, () => DisplayMatchesAppliedSpeed(0, japanese));
            Check("display/speed70-range30/" + language, () => DisplayMatchesAppliedSpeed(70, japanese));
            Check("display/speed80-range30/" + language, () => DisplayMatchesAppliedSpeed(80, japanese));
            Check("description/falloff-disabled/" + language, () =>
            {
                Loc.Japanese = japanese;
                Equal(0f, CoopDistance.Coefficient(14, false), "disabled falloff actually cuts off outside 10m");
                Contains(japanese ? "無効" : "disabled", CoopDistance.Description,
                    "distance description must explain the disabled 10m cutoff");
            });
        }
        Check("description/break-config-numbers", () =>
        {
            string text = Labels("meleeBreak");
            Contains(Number(CoopBalance.BreakDamagePercent) + "%", text, "Break setting must disclose allied damage bonus");
            Contains(Number(CoopBalance.BreakSeconds), text, "Break setting must disclose duration");
        });
        Check("description/guard-config-numbers", () =>
        {
            string text = Labels("meleeProximityGuard");
            Contains(Number(CoopBalance.MeleeGuardReductionPercent) + "%", text, "guard setting must disclose reduction");
            Contains(Number(CoopBalance.MeleeGuardRange) + "m", text, "guard setting must disclose radius");
        });
        Check("description/conversion-config-cap", () =>
        {
            Contains(Content.StatCap(Stat.AttackSpeedPct) + "%", Labels("meleeRangeToSpeed"), "range conversion setting must disclose speed cap");
        });
        Check("settings/hello-conveys-host-choices", () =>
        {
            var host = new HostAuthority();
            host.ConfigureMeleeCoop(true, true, true, true);
            var options = new JsonSerializerOptions { IncludeFields = true };
            string enabled = JsonSerializer.Serialize(host.ReplyForTest(), options);
            host.ConfigureMeleeCoop(false, false, false, false);
            string disabled = JsonSerializer.Serialize(host.ReplyForTest(), options);
            if (enabled == disabled)
                throw new InvalidOperationException("Host hello is identical with all four features ON/OFF: " + enabled);
        });
        Check("settings/client-link-host-off-local-on-14m", () =>
        {
            var host = new HostAuthority();
            host.ConfigureMeleeCoop(true, false, true, true);
            var hero = Melee(new Hero_Vesper());
            var ally = Melee(new Hero_Husk());
            ally.agentPosition = new Vector3(14, 0, 0);
            NetworkedManagerBase<ActorManager>.softInstance.allHeroes.AddRange(new[] { hero, ally });
            var ui = new DreamforgeUi(new ClientSession { LocalHero = hero }, new DreamforgeConfig { coopDistanceFalloff = true });
            Equal(CoopDistanceNative.Coefficient(hero, ally), ui.LinkCoefficient("Hero_Husk"),
                "host disabled falloff at 14m, client enabled falloff: actual vs LinkMarks display");
        });
        Check("settings/client-range-host-off-local-on", () =>
        {
            var host = new HostAuthority();
            host.ConfigureMeleeCoop(false, true, true, true);
            var rt = Runtime(Melee(new Hero_Vesper()), 0, 30);
            host.Scan(rt);
            Equal(30f, rt.BaseBonus.attackRangePercentage, "host preserves range");
            var ui = new DreamforgeUi(new ClientSession { LocalHero = rt.Hero }, new DreamforgeConfig { meleeRangeToSpeed = true });
            ui.Draw();
            Equal(false, Content.DisplayMeleeRangeConversion,
                "host conversion OFF must show range, even when local conversion is ON");
        });
        Check("performance/break-disabled", () => HitShouldSkipBasicTraversal(false, Melee(new Hero_Vesper())));
        Check("performance/ranged-traveler", () => HitShouldSkipBasicTraversal(true, new Hero_Cetus()));
        Check("performance/guard-reuses-scan", () =>
        {
            var host = new HostAuthority();
            var rt = Runtime(Melee(new Hero_Mist()));
            var enemy = new Entity { Relation = EntityRelation.Enemy, agentPosition = new Vector3(2, 0, 0) };
            HostAuthority.Enemies.Add(enemy);
            host.Scan(rt);
            Equal(true, rt.MeleeGuard, "scan established guard eligibility");
            var reference = rt.Hero.Ability.attackAbility.currentConfig.spawnedInstanceRef;
            int before = reference.Reads;
            var damage = new DamageData(100) { actor = enemy };
            host.Guard(rt, ref damage, enemy);
            Equal(88f, damage.currentAmount, "guard remains effective");
            Equal(0, reference.Reads - before, "guard should use the last scan without resolving attack asset per hit");
        });
        Loc.Japanese = previousLanguage;
        Console.WriteLine(JsonSerializer.Serialize(new { total = Results.Count, failed = _failed, passed = Results.Count - _failed, cases = Results }, new JsonSerializerOptions { WriteIndented = true }));
        return _failed == 0 ? 0 : 1;
    }
}
