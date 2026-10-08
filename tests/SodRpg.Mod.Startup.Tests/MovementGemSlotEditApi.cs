using UnityEngine;
namespace SodRpg.Mod
{
    // Game-boundary doubles for the real MovementGemSlotEdit.cs (compiled into this
    // project). Only the members that production file touches are modeled; the patch
    // class installs onto EditSkillManager.SetMode with real Harmony detours.
    public enum HeroSkillLocation { Q, W, E, R, Identity, Movement }

    public sealed class HeroSkill
    {
        public int MovementMaxGemCount;
        public int GetMaxGemCount(HeroSkillLocation type) => type == HeroSkillLocation.Movement ? MovementMaxGemCount : 0;
    }

    public sealed class EditSkillManager
    {
        public enum ModeType { None = 0, Regular = 1, EquipGem = 2, EquipSkill = 3, Sell = 9, EditSkillShrine = 20 }

        public ModeType mode { get; private set; }

        private void SetMode(ModeType newMode) { mode = newMode; }
    }

    public sealed class UI_InGame_SkillButtons : Component
    {
        public UI_InGame_SkillButton[] skillButtons;
        public Transform[] adjustedItems;
    }

    public class UI_InGame_SkillButton : Component
    {
        public HeroSkillLocation skillType;
        public GameObject skillActivationKeyObject;
        protected override UnityEngine.Object Clone() => new UI_InGame_SkillButton
        {
            skillType = skillType,
            skillActivationKeyObject = skillActivationKeyObject
        };
        internal override void RemapCloneReferences(System.Collections.Generic.Dictionary<GameObject, GameObject> copies)
        {
            if (skillActivationKeyObject != null && copies.TryGetValue(skillActivationKeyObject, out var copy))
                skillActivationKeyObject = copy;
        }
    }
}
