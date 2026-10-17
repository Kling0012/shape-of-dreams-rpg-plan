namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        partial void ApplyRoomOmensConfig()
        {
            HostAuthority.RoomOmensEnabled = config.roomOmens;
            CoopOmens.Enabled = config.coopRoomOmens;
        }
    }
}
