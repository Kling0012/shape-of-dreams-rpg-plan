using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Only session state and coloring are doubled; probability rules come from Core.
    internal sealed class ClientSession
    {
        internal Profile Profile = new Profile();
        internal float PressureHealthMultiplier = 1;
        internal float PressureDamageMultiplier = 1;
    }

    internal static class UiStyles
    {
        internal static string Colored(string text, string color) => $"<color={color}>{text}</color>";
    }

    internal sealed partial class DreamforgeUi
    {
        private readonly ClientSession _s;
        internal DreamforgeUi(ClientSession session) { _s = session; }
    }

    internal class Monster { }
    internal sealed class BossMonster : Monster { }
    internal static class InfinityMode
    {
        internal static InfinityRunState State => null;
    }

    internal sealed partial class HostAuthority
    {
        private DreamPressure _pressure;
        private readonly Build _build;
        private readonly Rng _rng;
        private readonly Waypoints.Totals ActiveWaypointTotals;

        internal HostAuthority(Build build, DreamPressure pressure, Waypoint waypoint, ulong seed)
        {
            _build = build;
            _pressure = pressure;
            _rng = new Rng(seed);
            ActiveWaypointTotals = Waypoints.Sum(waypoint);
        }

        // One present party member is sufficient to reproduce PartyGearChanceMult's maximum.
        private double PartyGearChanceMult() => Nightmares.GearChanceMult(_build);
        internal NightmareAffix Roll(MonsterTier tier, int depth, double multiplier) =>
            RollWaypointNightmare(tier == MonsterTier.Boss ? new BossMonster() : new Monster(), tier, depth, multiplier);
    }
}
