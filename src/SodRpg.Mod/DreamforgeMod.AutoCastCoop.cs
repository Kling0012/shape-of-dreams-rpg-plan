namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        partial void ApplyAutoCastCoopConfig() => AutoCastCoopSettings.Configure(config.autoCastCoopNativeCadence);
        partial void TickAutoCastCoopConfig() => AutoCastCoopSettings.Tick();
    }
}
