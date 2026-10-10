using UnityEngine;
namespace SodRpg.Mod
{
    // Game-boundary doubles for the real MovementGemSlotEdit.cs. Model only the
    // native members used by the production patch; existing tests install real Harmony.
    public enum HeroSkillLocation { Q, W, E, R, Identity, Movement }
    public enum InputMode { KeyboardAndMouse, Gamepad }
    public static class DewInput { public static InputMode currentMode; }
    public struct GemLocation
    {
        public HeroSkillLocation skill;
        public int index;
        public GemLocation(HeroSkillLocation skill, int index) { this.skill = skill; this.index = index; }
    }
    public sealed class HeroSkill
    {
        public int MovementMaxGemCount;
        public int GetMaxGemCount(HeroSkillLocation type) => type == HeroSkillLocation.Movement ? MovementMaxGemCount : 0;
        public object GetSkill(HeroSkillLocation type) => null;
    }
    public sealed class EditSkillManager
    {
        public enum ModeType { None = 0, Regular = 1, EquipGem = 2, EquipSkill = 3, Sell = 9, EditSkillShrine = 20 }
        public ModeType mode { get; private set; }
        public HeroSkillLocation? selectedSkillSlot;
        public GemLocation? selectedGemSlot;
        public void SelectAnyRelevantSlot() { }
        public bool IsSlotSelectable(HeroSkillLocation location) => mode != ModeType.None;
        public bool IsSlotSelectable(GemLocation location) => mode != ModeType.None;
        private bool TryGetNextRelevantSkillLocation(bool next, bool skipStart, bool canWrap, out HeroSkillLocation loc)
        {
            loc = HeroSkillLocation.Q;
            return false;
        }
        private bool TryGetNextRelevantGemSlot(bool next, bool skipStart, bool canWrap, out GemLocation loc)
        {
            loc = default;
            return false;
        }
        private void SetMode(ModeType newMode)
        {
            ManagerBase<EditSkillManager>.softInstance = this;
            mode = newMode;
        }
    }
    public sealed class UI_InGame_SkillButtons : Component
    {
        public UI_InGame_SkillButton[] skillButtons;
        public Transform[] adjustedItems;
        public CanvasGroup[] hiddenWhenExpanded;
        public void FrameUpdate() { }
    }
    public sealed class UI_InGame_SkillButton_EditSkill : Component
    {
        private void UpdateStatus() { }
    }
    public sealed class UI_InGame_GemSlot { }
    public sealed class UI_InGame_SkillButton_GemGroup : Component
    {
        public GameObject[] groups = { new GameObject(), new GameObject(), new GameObject(), new GameObject() };
        public UI_InGame_GemSlot[] activeGemSlots;
        public void LogicUpdate(float dt)
        {
            int cap = DewPlayer.local.hero.Skill.GetMaxGemCount(HeroSkillLocation.Movement);
            activeGemSlots = new UI_InGame_GemSlot[cap];
        }
        private void OnStateChanged(EditSkillManager.ModeType mode) { }
        protected override UnityEngine.Object Clone() => new UI_InGame_SkillButton_GemGroup();
    }
    public class UI_InGame_SkillButton : Component
    {
        public HeroSkillLocation skillType;
        public GameObject skillActivationKeyObject;
        private void SetTarget(object skill) { }
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
namespace UnityEngine
{
    public sealed class CanvasGroup : Component
    {
        public float alpha;
        public bool interactable, blocksRaycasts;
    }
}
namespace DG.Tweening
{
    public static class ShortcutExtensions
    {
        public static void DOKill(this UnityEngine.CanvasGroup group) { }
    }
}
