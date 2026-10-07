using UnityEngine;

namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        [ConsoleCommand("Dreamforge (host test): spawn native prototype 0=Seed of Shelter, 1=Seedling", "dreamforge_native_give")]
        private void NativeDreamGiveCommand(int kind)
        {
            if (!DevAllowed() || !MakeSureServer()) return;
            if (kind != 0 && kind != 1)
            {
                Debug.Log("[DreamforgeRPG] native prototype kind: 0=gem, 1=memory");
                return;
            }
            bool created = NativeDreamContent.Give(_session.LocalHero, kind == 1);
            Debug.Log(created ? "[DreamforgeRPG] native prototype spawned; pick up and equip through the native UI"
                : "[DreamforgeRPG] native prototype unavailable; requires dev.flag + native-dream.flag, a living hero, and successful registration (see warning)");
        }

        [ConsoleCommand("Dreamforge: show development native prototype registration", "dreamforge_native_status")]
        private void NativeDreamStatusCommand()
        {
            if (!CommandAllowed()) return;
            Debug.Log($"[DreamforgeRPG] native prototypes enabled={NativeDreamContent.Enabled} gem={NativeDreamContent.ReadyGem} memory={NativeDreamContent.ReadyMemory}");
        }
    }
}
