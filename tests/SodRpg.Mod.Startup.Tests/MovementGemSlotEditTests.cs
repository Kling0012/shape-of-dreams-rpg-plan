using System;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // MovementGemSlotEdit は EditSkillManager.SetMode の後だけで動く。ここでは本物の Harmony フックを
    // スタブの SetMode に取り付け、装着画面の開閉ごとの挙動（表示・非表示・配置・失敗隔離）を確認する。
    public sealed class MovementGemSlotEditTests : IDisposable
    {
        private readonly Harmony owner = new Harmony("movement-edit-tests." + Guid.NewGuid().ToString("N"));
        private readonly EditSkillManager manager = new EditSkillManager();

        public MovementGemSlotEditTests()
        {
            MovementGemSlotEdit.ResetForTest();
            Log.Warnings.Clear();
            Log.Infos.Clear();
            DewPlayer.local = null;
            ManagerBase<UI_InGame_SkillButtons>.softInstance = null;
            owner.CreateClassProcessor(typeof(MovementGemSlotEditPatch)).Patch();
        }

        public void Dispose()
        {
            owner.UnpatchAll(owner.Id);
            MovementGemSlotEdit.ResetForTest();
            DewPlayer.local = null;
            ManagerBase<UI_InGame_SkillButtons>.softInstance = null;
        }

        private static void Open(EditSkillManager manager, EditSkillManager.ModeType mode)
        {
            var setMode = AccessTools.Method(typeof(EditSkillManager), "SetMode");
            Assert.NotNull(setMode);
            setMode.Invoke(manager, new object[] { mode });
        }

        private sealed class Hud
        {
            internal readonly UI_InGame_SkillButtons Buttons = new UI_InGame_SkillButtons();
            internal readonly GameObject Row = new GameObject();
            internal readonly UI_InGame_SkillButton Movement;

            internal Hud(float[] xs, bool withMovement = true)
            {
                var canvas = new GameObject();
                canvas.transform.position = new Vector3(960f, 540f, 0f);
                (canvas.transform as RectTransform).sizeDelta = new Vector2(1920f, 1080f);
                Buttons.gameObject.transform.SetParent(canvas.transform);
                Row.transform.SetParent(Buttons.transform);
                var buttons = new UI_InGame_SkillButton[withMovement ? xs.Length + 1 : xs.Length];
                for (int i = 0; i < xs.Length; i++)
                    buttons[i] = Column((HeroSkillLocation)i, xs[i], true);
                if (withMovement)
                {
                    Movement = Column(HeroSkillLocation.Movement, xs[xs.Length - 1] + 200f, false);
                    buttons[xs.Length] = Movement;
                }
                Buttons.skillButtons = buttons;
                Buttons.adjustedItems = new Transform[0];
                ManagerBase<UI_InGame_SkillButtons>.softInstance = Buttons;
            }

            private UI_InGame_SkillButton Column(HeroSkillLocation type, float x, bool columnActive)
            {
                var column = new GameObject();
                column.transform.SetParent(Row.transform);
                column.transform.position = new Vector3(x, 100f, 0f);
                column.SetActive(columnActive);
                var button = new UI_InGame_SkillButton { skillType = type };
                button.transform.SetParent(column.transform);
                button.transform.position = new Vector3(x, 100f, 0f);
                button.skillActivationKeyObject = new GameObject();
                button.skillActivationKeyObject.transform.SetParent(button.transform);
                return button;
            }
        }

        private static void LocalHeroWithSlots(int movementSlots)
        {
            DewPlayer.local = new DewPlayer { hero = new Hero() };
            DewPlayer.local.hero.Skill.MovementMaxGemCount = movementSlots;
        }

        [Fact]
        public void EditScreenShowsHiddenMovementColumnNextToSkillRow()
        {
            LocalHeroWithSlots(2);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f });

            Open(manager, EditSkillManager.ModeType.EquipGem);

            Assert.True(hud.Movement.gameObject.activeInHierarchy);
            // 既定の 1920x1080：右端の列の右隣（間隔 120）に収まる。
            Assert.Equal(1460f, hud.Movement.transform.parent.position.x, 3);
            Assert.Equal(100f, hud.Movement.transform.parent.position.y, 3);
            Assert.Contains(Log.Infos, m => m.Contains("Movement essence slots shown"));
        }

        [Fact]
        public void ClosingScreenOrLosingSlotsHidesTheColumnAgain()
        {
            LocalHeroWithSlots(1);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f });

            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.True(hud.Movement.gameObject.activeInHierarchy);

            // 装着画面の別モードへ移っても表示は保たれる。
            Open(manager, EditSkillManager.ModeType.EquipSkill);
            Assert.True(hud.Movement.gameObject.activeInHierarchy);

            Open(manager, EditSkillManager.ModeType.None);
            Assert.False(hud.Movement.gameObject.activeInHierarchy);

            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.True(hud.Movement.gameObject.activeInHierarchy);

            DewPlayer.local.hero.Skill.MovementMaxGemCount = 0;
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.False(hud.Movement.gameObject.activeInHierarchy);
        }

        [Fact]
        public void ColumnManagedByNativeLayoutKeepsItsPosition()
        {
            LocalHeroWithSlots(1);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f });
            hud.Buttons.adjustedItems = new[] { hud.Movement.transform.parent };

            Open(manager, EditSkillManager.ModeType.EquipGem);

            Assert.True(hud.Movement.gameObject.activeInHierarchy);
            Assert.Equal(1540f, hud.Movement.transform.parent.position.x, 3);
        }

        [Fact]
        public void OverflowingRowFallsBackToTheLeftSide()
        {
            LocalHeroWithSlots(1);
            var hud = new Hud(new[] { 1500f, 1620f, 1740f, 1860f, 1980f });

            Open(manager, EditSkillManager.ModeType.EquipGem);

            // 右隣（2100）は画面外：左隣（1380）へ置く。
            Assert.Equal(1380f, hud.Movement.transform.parent.position.x, 3);
        }

        [Fact]
        public void RowSpanningTheWholeCanvasKeepsPlacementOnScreen()
        {
            LocalHeroWithSlots(1);
            var hud = new Hud(new[] { 120f, 540f, 960f, 1380f, 1800f });

            Open(manager, EditSkillManager.ModeType.EquipGem);

            // 右も左も溢れる：列の間で一番広い隙間（420）の中央（330）へ置く。
            Assert.Equal(330f, hud.Movement.transform.parent.position.x, 3);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingMovementButtonClonesASiblingColumnAndItsOwnKeyLabel(bool templateActive)
        {
            LocalHeroWithSlots(2);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f }, withMovement: false);
            var template = hud.Buttons.skillButtons[0];
            template.transform.parent.gameObject.SetActive(templateActive);

            Open(manager, EditSkillManager.ModeType.EquipGem);

            var movement = Assert.Single(hud.Buttons.skillButtons, button => button.skillType == HeroSkillLocation.Movement);
            Assert.Equal(6, hud.Buttons.skillButtons.Length);
            Assert.Equal(6, hud.Row.transform.childCount);
            Assert.Same(hud.Row.transform, movement.transform.parent.parent);
            Assert.Equal(MovementGemSlotEdit.CloneName, movement.transform.parent.gameObject.name);
            Assert.True(movement.gameObject.activeInHierarchy);
            Assert.Equal(1460f, movement.transform.parent.position.x, 3);
            Assert.Equal(100f, movement.transform.parent.position.y, 3);
            Assert.NotSame(template.skillActivationKeyObject, movement.skillActivationKeyObject);
            Assert.False(movement.skillActivationKeyObject.activeSelf);
            Assert.True(template.skillActivationKeyObject.activeSelf);
            Assert.Equal(templateActive, template.transform.parent.gameObject.activeSelf);
            Assert.Equal(HeroSkillLocation.Q, template.skillType);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void MissingQButtonUsesAnotherSkillColumn()
        {
            LocalHeroWithSlots(1);
            var hud = new Hud(new[] { 860f, 980f, 1100f }, withMovement: false);
            for (int i = 0; i < hud.Buttons.skillButtons.Length; i++)
                hud.Buttons.skillButtons[i].skillType = (HeroSkillLocation)(i + 1);

            Open(manager, EditSkillManager.ModeType.EquipGem);

            var movement = Assert.Single(hud.Buttons.skillButtons, button => button.skillType == HeroSkillLocation.Movement);
            Assert.Same(hud.Row.transform, movement.transform.parent.parent);
            Assert.True(movement.gameObject.activeInHierarchy);
            Assert.Equal(1220f, movement.transform.parent.position.x, 3);
            Assert.Equal(HeroSkillLocation.W, hud.Buttons.skillButtons[0].skillType);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void ClonedColumnIsReusedAcrossModeChangesCloseReopenAndSlotLoss()
        {
            LocalHeroWithSlots(1);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f }, withMovement: false);
            Open(manager, EditSkillManager.ModeType.EquipGem);
            var movement = Assert.Single(hud.Buttons.skillButtons, button => button.skillType == HeroSkillLocation.Movement);

            for (int i = 0; i < 3; i++)
            {
                Open(manager, EditSkillManager.ModeType.EquipSkill);
                Assert.True(movement.gameObject.activeInHierarchy);
                Open(manager, EditSkillManager.ModeType.None);
                Assert.False(movement.gameObject.activeInHierarchy);
                Assert.True(hud.Buttons.skillButtons[0].gameObject.activeInHierarchy);
                Open(manager, EditSkillManager.ModeType.EquipGem);
                Assert.True(movement.gameObject.activeInHierarchy);
                Assert.Same(movement, Assert.Single(hud.Buttons.skillButtons, button => button.skillType == HeroSkillLocation.Movement));
            }

            DewPlayer.local.hero.Skill.MovementMaxGemCount = 0;
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.False(movement.gameObject.activeInHierarchy);
            DewPlayer.local.hero.Skill.MovementMaxGemCount = 1;
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.True(movement.gameObject.activeInHierarchy);
            Assert.Equal(6, hud.Row.transform.childCount);
            Assert.Equal(6, hud.Buttons.skillButtons.Length);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void InactiveMovementColumnMissingFromArrayIsReusedAndRegistered()
        {
            LocalHeroWithSlots(1);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f });
            hud.Buttons.skillButtons = hud.Buttons.skillButtons.Where(button => button != hud.Movement).ToArray();

            Open(manager, EditSkillManager.ModeType.EquipGem);

            Assert.Same(hud.Movement, Assert.Single(hud.Buttons.skillButtons, button => button.skillType == HeroSkillLocation.Movement));
            Assert.True(hud.Movement.gameObject.activeInHierarchy);
            Assert.Equal(6, hud.Row.transform.childCount);
            Open(manager, EditSkillManager.ModeType.None);
            Assert.False(hud.Movement.gameObject.activeInHierarchy);
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.True(hud.Movement.gameObject.activeInHierarchy);
            Assert.Equal(6, hud.Row.transform.childCount);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void ZeroSlotsDoesNotCloneOrDisableALaterFallback()
        {
            LocalHeroWithSlots(0);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f }, withMovement: false);

            Open(manager, EditSkillManager.ModeType.EquipGem);

            Assert.Equal(5, hud.Buttons.skillButtons.Length);
            Assert.Equal(5, hud.Row.transform.childCount);
            Assert.Empty(Log.Infos);
            Assert.Empty(Log.Warnings);
            DewPlayer.local.hero.Skill.MovementMaxGemCount = 1;
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.True(Assert.Single(hud.Buttons.skillButtons, button => button.skillType == HeroSkillLocation.Movement).gameObject.activeInHierarchy);
        }

        [Fact]
        public void CloneFailureWarnsOnceAndNeverThrows()
        {
            LocalHeroWithSlots(2);
            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f }, withMovement: false);
            // An unsupported component models an actual cloning failure, rather than making every clone fail.
            new Component().transform.SetParent(hud.Buttons.skillButtons[0].transform.parent);

            Open(manager, EditSkillManager.ModeType.EquipGem);
            Open(manager, EditSkillManager.ModeType.EquipGem);

            Assert.Single(Log.Warnings);
            Assert.Contains("Movement essence slot display disabled", Log.Warnings[0]);
            Assert.Equal(5, hud.Buttons.skillButtons.Length);
        }

        [Fact]
        public void ChildSearchModelExcludesInactiveDescendantsButAlwaysChecksReceiver()
        {
            var root = new GameObject();
            var child = new UI_InGame_SkillButton();
            child.transform.SetParent(root.transform);
            Assert.Same(child, root.GetComponentInChildren<UI_InGame_SkillButton>());
            root.SetActive(false);
            Assert.Null(root.GetComponentInChildren<UI_InGame_SkillButton>());
            Assert.Same(child, root.GetComponentInChildren<UI_InGame_SkillButton>(true));
            Assert.Same(child, child.gameObject.GetComponentInChildren<UI_InGame_SkillButton>());
        }

        [Fact]
        public void MissingHeroOrHudDoesNothingAndStaysQuiet()
        {
            DewPlayer.local = null;
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.Empty(Log.Warnings);

            LocalHeroWithSlots(1);
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.Empty(Log.Warnings);

            var hud = new Hud(new[] { 860f, 980f, 1100f, 1220f, 1340f });
            Open(manager, EditSkillManager.ModeType.EquipGem);
            Assert.True(hud.Movement.gameObject.activeInHierarchy);
        }

        [Fact]
        public void MedianGapIgnoresAFarAwayColumn()
        {
            float[] xs = { 860f, 980f, 1100f, 1220f, 1340f, 3000f };
            Assert.Equal(120f, MovementGemSlotEdit.MedianGap(xs, xs.Length, new float[8]), 3);
        }

        [Fact]
        public void MedianGapHandlesNoUsableGap()
        {
            float[] xs = { 500f, 500f };
            Assert.Equal(0f, MovementGemSlotEdit.MedianGap(xs, xs.Length, new float[8]), 3);
        }
    }
}
