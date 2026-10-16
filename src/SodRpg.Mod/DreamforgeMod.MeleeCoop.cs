using System;

namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        partial void ApplyMeleeCoopSettings()
        {
            try
            {
                _host?.ConfigureMeleeCoop(config.meleeRangeToSpeed, config.coopDistanceFalloff,
                    config.meleeBreak, config.meleeProximityGuard);
            }
            catch (Exception ex) { Log.Warn("Melee/coop settings ignored: " + ex.Message); }
        }
    }
}
