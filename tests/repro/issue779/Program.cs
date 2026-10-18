using System;
using SodRpg.Mod;

internal static class Program
{
    private static int failed;
    private static int passed;

    private static void Check(string name, Action action)
    {
        try
        {
            Log.Lines.Clear();
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = true;
            NetworkedManagerBase<ZoneManager>.instance = null;
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    private static void Equal(int expected, int actual, string observation)
    {
        if (expected != actual)
            throw new InvalidOperationException(observation + ": expected=" + expected + ", actual=" + actual);
    }

    private static void Unavailable(SkillTrigger skill)
    {
        if (skill.CanBeReserved() || skill.CanBeCast())
            throw new InvalidOperationException("Fixture does not represent a black, uncastable skill");
    }

    private static void Scan(ClientSession client, int from, int through)
    {
        for (int now = from; now <= through; now++) client.ScanAt(now);
    }

    private static int Main()
    {
        Check("ready but uncharged already requests resync at 10 seconds", () =>
        {
            var client = new ClientSession();
            client.Hero.Skill.Q.currentCharges[0] = 0;
            Unavailable(client.Hero.Skill.Q);
            Scan(client, 0, 9);
            Equal(0, client.RequestCount, "requests before threshold");
            client.ScanAt(10);
            Equal(1, client.RequestCount, "requests at threshold");
        });

        Check("NaN cooldown already requests resync at 10 seconds", () =>
        {
            var client = new ClientSession();
            client.Hero.Skill.Q.currentUnscaledCooldownTimes[0] = float.NaN;
            Scan(client, 0, 10);
            Equal(1, client.RequestCount, "requests");
        });

        Check("normal progressing cooldown remains excluded", () =>
        {
            var client = new ClientSession();
            var skill = client.Hero.Skill.Q;
            skill.currentCharges[0] = 0;
            for (int now = 0; now <= 30; now++)
            {
                skill.currentUnscaledCooldownTimes[0] = 60 - now;
                client.ScanAt(now);
            }
            Equal(0, client.RequestCount, "requests during normal cooldown");
        });

        Check("undercharge lasting less than 10 seconds remains excluded", () =>
        {
            var client = new ClientSession();
            client.Hero.Skill.Q.currentCharges[0] = 0;
            Scan(client, 0, 9);
            client.Hero.Skill.Q.currentCharges[0] = 1;
            Scan(client, 10, 30);
            Equal(0, client.RequestCount, "requests after transient undercharge");
        });

        Check("legitimate long cast does not cause a resync", () =>
        {
            var client = new ClientSession();
            client.Hero.Skill.Q.Network_isCasting = true;
            Scan(client, 0, 30);
            Equal(0, client.RequestCount, "requests during a cast without contradictory charge state");
        });

        Check("REPRO stale cast flag hides zero charges and zero timers for 30 seconds", () =>
        {
            var client = new ClientSession();
            var skill = client.Hero.Skill.Q;
            skill.currentCharges[0] = 0;
            skill.Network_isCasting = true;
            Unavailable(skill);
            Scan(client, 0, 30);
            if (client.RequestCount == 0)
                throw new InvalidOperationException("expected a resync for the persistent charge mismatch; actual requests=0");
        });

        Check("REPRO remaining channel suppresses all undercharge scans for 30 seconds", () =>
        {
            var client = new ClientSession();
            client.Hero.Skill.Q.currentCharges[0] = 0;
            client.Hero.Control.ongoingChannels.Add(new object());
            Unavailable(client.Hero.Skill.Q);
            Scan(client, 0, 30);
            if (client.RequestCount == 0)
                throw new InvalidOperationException("expected a resync for the persistent charge mismatch; actual requests=0");
        });

        Check("REPRO config cast lock with full charge and zero timers is never scanned", () =>
        {
            var client = new ClientSession();
            var skill = client.Hero.Skill.Q;
            // AbilityTrigger.UpdateCastAndCooldownLocks derives this lock from a
            // live instance's shouldLockCast. The instance's lifecycle is not simulated.
            skill.configCastLocked = true;
            Unavailable(skill);
            Scan(client, 0, 30);
            if (client.RequestCount == 0)
                throw new InvalidOperationException("expected a resync for a persistent config lock; actual requests=0");
        });

        Check("REPRO diagnostic log is suppressed after recovery and a second black episode", () =>
        {
            var client = new ClientSession();
            client.Hero.Skill.Q.currentCharges[0] = 0;
            Scan(client, 0, 10);
            client.Hero.Skill.Q.currentCharges[0] = 1;
            client.ScanAt(11);
            client.Hero.Skill.Q.currentCharges[0] = 0;
            Scan(client, 12, 22);
            Equal(2, client.RequestCount, "requests across two episodes");
            Equal(2, Log.Lines.Count, "one diagnostic log per black episode");
        });

        Console.WriteLine("RESULT passed=" + passed + " failed=" + failed);
        return failed == 0 ? 0 : 1;
    }
}
