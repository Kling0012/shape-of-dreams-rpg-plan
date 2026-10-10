using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace SodRpg.Mod
{
    // Dew.UI: SkillButtons.OnStateChanged は hiddenWhenExpanded を alpha=0 / raycast無効にし、
    // FrameUpdate は adjustedItems の位置を毎フレーム戻す。SetActive と遅延 Place だけでは直らない。
    // 回避 HUD は MovementSkillIndicator なので、技列が無い場合は本体の編集用列を一度だけ複製する。
    // GemGroup は Start でモード変更を購読するため、編集開始後に作った列には現在のモードを適用する。
    // 装着・外し・交換は本体 EditSkillManager の Cmd 経路に任せる。失敗時はこの表示機能だけを止める。
    internal static class MovementGemSlotEdit
    {
        // プレハブに回避の技ボタン自体が無いとき、既存の技列を複製して回避用に作る列の名前（二重作成の防止）。
        internal const string CloneName = "DreamforgeMovementSkillColumn";

        private const int MaxColumns = 12;
        private const int MaxAncestors = 8;

        private static readonly Transform[] _columns = new Transform[MaxColumns];
        private static readonly float[] _xs = new float[MaxColumns];
        private static readonly float[] _gaps = new float[MaxColumns];
        private static readonly GameObject[] _pending = new GameObject[MaxAncestors];
        // この画面の間に表示へ戻した、回避の技列とその祖先。閉じるとき（または枠が0のとき）だけ元へ戻す。
        private static readonly List<GameObject> _activated = new List<GameObject>(MaxAncestors);
        private static readonly Vector3[] _corners = new Vector3[4];
        private struct FadeState
        {
            internal CanvasGroup Group;
            internal float Alpha;
            internal bool Interactable, BlocksRaycasts;
        }
        private static readonly List<FadeState> _fades = new List<FadeState>(MaxAncestors);
        private static readonly System.Reflection.MethodInfo _groupState = AccessTools.Method(typeof(UI_InGame_SkillButton_GemGroup), "OnStateChanged");
        private static readonly System.Reflection.MethodInfo _setTarget = AccessTools.Method(typeof(UI_InGame_SkillButton), "SetTarget");
        private static UI_InGame_SkillButton _movement;
        private static Transform _placedColumn;
        private static Vector3 _originalPosition;
        private static bool _warned, _failed, _shownLogged;

        internal static bool IsAvailable => !_failed && _placedColumn != null && _movement != null && _movement.gameObject.activeInHierarchy
            && DewPlayer.local != null && DewPlayer.local.hero != null
            && DewPlayer.local.hero.Skill.GetMaxGemCount(HeroSkillLocation.Movement) > 0;

        internal static void ResetForTest()
        {
            Hide();
            _warned = _failed = _shownLogged = false;
            _movement = null;
        }

        internal static void OnModeChanged(EditSkillManager manager)
        {
            if (_failed || manager == null) return;
            if (manager.mode == EditSkillManager.ModeType.None) { Hide(); return; }
            var player = DewPlayer.local;
            var hero = player == null ? null : player.hero;
            var skill = hero == null ? null : hero.Skill;
            if (skill == null) return;
            int cap = skill.GetMaxGemCount(HeroSkillLocation.Movement);
            if (cap < 1) { Hide(); return; }
            var buttons = ManagerBase<UI_InGame_SkillButtons>.softInstance;
            var array = buttons == null ? null : buttons.skillButtons;
            if (array == null || array.Length == 0) return;
            UI_InGame_SkillButton movement = null;
            foreach (var button in array)
                if (button != null && button.skillType == HeroSkillLocation.Movement) { movement = button; break; }
            if (movement == null) movement = FindOrCloneColumn(buttons, array);
            if (movement == null) return;
            _movement = movement;
            Show(manager, buttons, buttons.skillButtons, movement, cap);
            // SetMode 内の初期選択は列作成より先に走る。回避だけに枠がある場合も選択を確定する。
            if (DewInput.currentMode == InputMode.Gamepad && !manager.selectedSkillSlot.HasValue && !manager.selectedGemSlot.HasValue)
                manager.SelectAnyRelevantSlot();
        }

        private static void Show(EditSkillManager manager, UI_InGame_SkillButtons buttons, UI_InGame_SkillButton[] array, UI_InGame_SkillButton movement, int cap)
        {
            // 技ボタンから上へ、隠れている祖先を外側から順にこの画面の間だけ表示に戻す。
            // 装着画面の別モードへ移っても記録は残し、画面を閉じたときにまとめて元へ戻す。
            int hidden = 0;
            for (Transform t = movement.transform; t != null && hidden < MaxAncestors; t = t.parent)
                if (!t.gameObject.activeSelf) _pending[hidden++] = t.gameObject;
            for (int i = hidden - 1; i >= 0; i--)
            {
                _pending[i].SetActive(true);
                _activated.Add(_pending[i]);
            }
            // 本体の fade tween が SetMode 後にも alpha を書くので、対象の tween を止めてから戻す。
            var hiddenGroups = buttons.hiddenWhenExpanded;
            if (hiddenGroups != null)
                foreach (var cg in hiddenGroups)
                {
                    if (cg == null || !IsAncestor(cg.transform, movement.transform)) continue;
                    bool saved = false;
                    foreach (var state in _fades) if (state.Group == cg) { saved = true; break; }
                    if (!saved) _fades.Add(new FadeState { Group = cg, Alpha = 0f,
                        Interactable = false, BlocksRaycasts = false });
                    cg.DOKill();
                    cg.alpha = 1f;
                    cg.interactable = cg.blocksRaycasts = true;
                }
            var group = movement.transform.parent.gameObject.GetComponentInChildren<UI_InGame_SkillButton_GemGroup>(true);
            if (group == null || _groupState == null || _setTarget == null)
                throw new MissingComponentException("movement column requires native GemGroup and skill handlers");
            if (group.groups == null || cap > group.groups.Length)
                throw new InvalidOperationException("movement gem count exceeds native UI capacity");
            if (group.activeGemSlots == null && cap > 0)
                group.groups[cap - 1].SetActive(false);
            group.LogicUpdate(0f);
            _groupState.Invoke(group, new object[] { manager.mode });
            _setTarget.Invoke(movement, new object[] { DewPlayer.local.hero.Skill.GetSkill(HeroSkillLocation.Movement) });
            var editor = movement.gameObject.GetComponent<UI_InGame_SkillButton_EditSkill>();
            if (editor != null)
                AccessTools.Method(typeof(UI_InGame_SkillButton_EditSkill), "UpdateStatus").Invoke(editor, null);
            Canvas.ForceUpdateCanvases();
            Place(buttons, array, movement);
            if (_shownLogged || group.activeGemSlots == null || group.activeGemSlots.Length != cap) return;
            _shownLogged = true;
            Log.Info("Client: Movement essence slots shown in the edit screen (" + cap + " slot(s))");
        }

        private static Transform FindCopiedTransform(Transform source, Transform copy, Transform target)
        {
            if (source == target) return copy;
            for (int i = 0; i < source.childCount; i++)
                if (IsAncestor(source.GetChild(i), target))
                    return FindCopiedTransform(source.GetChild(i), copy.GetChild(i), target);
            throw new InvalidOperationException("cannot locate cloned fade group");
        }

        private static bool IsAncestor(Transform ancestor, Transform child)
        {
            for (var t = child; t != null; t = t.parent) if (t == ancestor) return true;
            return false;
        }

        internal static void UpdatePlacement(UI_InGame_SkillButtons buttons)
        {
            if (!IsAvailable || _placedColumn == null) return;
            Place(buttons, buttons.skillButtons, _movement);
        }

        private static void Hide()
        {
            if (_placedColumn != null) _placedColumn.localPosition = _originalPosition;
            _placedColumn = null;
            foreach (var state in _fades)
            {
                if (state.Group == null) continue;
                // None モードでは本体の復帰 tween を妨げない。失敗時・枠消失時は保存値へ戻す。
                var manager = ManagerBase<EditSkillManager>.softInstance;
                if (manager != null && manager.mode == EditSkillManager.ModeType.None) continue;
                state.Group.DOKill();
                state.Group.alpha = state.Alpha;
                state.Group.interactable = state.Interactable;
                state.Group.blocksRaycasts = state.BlocksRaycasts;
            }
            _fades.Clear();
            for (int i = 0; i < _activated.Count; i++)
            {
                var go = _activated[i];
                if (go != null) go.SetActive(false);
            }
            _activated.Clear();
        }

        // 技ボタンがプレハブに無い場合の保険。既に作った列があれば再利用し、無ければ既存の技列（優先は Q）を
        // 複製して回避用に作る。複製した列も本体の GemGroup・GemSlot の構成をそのまま持つので、以後は本体に任せる。
        private static UI_InGame_SkillButton FindOrCloneColumn(UI_InGame_SkillButtons buttons, UI_InGame_SkillButton[] array)
        {
            UI_InGame_SkillButton template = null, any = null;
            foreach (var button in array)
            {
                if (button == null || button.skillType == HeroSkillLocation.Movement) continue;
                if (any == null) any = button;
                if (button.skillType == HeroSkillLocation.Q) { template = button; break; }
            }
            if (template == null) template = any;
            if (template == null || template.transform.parent == null) return null;
            var templateColumn = template.transform.parent;
            var parent = templateColumn.parent;
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i).gameObject;
                var reuse = child.GetComponentInChildren<UI_InGame_SkillButton>(true);
                if (reuse != null && reuse.skillType == HeroSkillLocation.Movement)
                    return RegisterButton(buttons, array, reuse);
            }
            GameObject clone;
            try
            {
                // 元の列の子ではなく、同じ行の兄弟として複製する。
                // Awake/OnEnable が元の Q として走らないよう、非表示の状態で複製してから skillType を設定する。
                bool wasActive = templateColumn.gameObject.activeSelf;
                templateColumn.gameObject.SetActive(false);
                try { clone = UnityEngine.Object.Instantiate(templateColumn.gameObject, parent); }
                finally { templateColumn.gameObject.SetActive(wasActive); }
            }
            catch (Exception ex)
            {
                WarnFailed(new InvalidOperationException("cannot clone a skill column: " + ex.Message));
                return null;
            }
            clone.name = CloneName;
            clone.SetActive(false);
            _activated.Add(clone);
            // hiddenWhenExpanded は元列の参照だけを持つ。複製列にコピーされた fade 状態も解除する。
            var hidden = buttons.hiddenWhenExpanded;
            if (hidden != null)
                foreach (var cg in hidden)
                {
                    if (cg == null || !IsAncestor(templateColumn, cg.transform)) continue;
                    var copied = FindCopiedTransform(templateColumn, clone.transform, cg.transform).gameObject.GetComponent<CanvasGroup>();
                    if (copied == null) continue;
                    copied.alpha = 1f;
                    copied.interactable = copied.blocksRaycasts = true;
                }
            var cloned = clone.GetComponentInChildren<UI_InGame_SkillButton>(true);
            if (cloned == null)
            {
                UnityEngine.Object.Destroy(clone);
                WarnFailed(new MissingComponentException("cloned skill column has no button"));
                return null;
            }
            cloned.skillType = HeroSkillLocation.Movement;
            // 複製した列のキー表示は元の技のものが残るので、誤解を避けるために消しておく。
            if (cloned.skillActivationKeyObject != null) cloned.skillActivationKeyObject.SetActive(false);
            // 複製した列はレイアウトグループの子でも本体の技列数や並びを変えない。
            var layout = clone.GetComponent<LayoutElement>();
            if (layout == null) layout = clone.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            return RegisterButton(buttons, array, cloned);
        }

        private static UI_InGame_SkillButton RegisterButton(UI_InGame_SkillButtons buttons, UI_InGame_SkillButton[] array, UI_InGame_SkillButton movement)
        {
            // 本体 Tooltip / FloatingGem は配列を skill enum で添字参照する。
            int index = (int)HeroSkillLocation.Movement;
            var grown = new UI_InGame_SkillButton[Math.Max(array.Length, index + 1)];
            Array.Copy(array, grown, array.Length);
            grown[index] = movement;
            buttons.skillButtons = grown;
            return movement;
        }

        private static void Place(UI_InGame_SkillButtons buttons, UI_InGame_SkillButton[] array, UI_InGame_SkillButton movement)
        {
            // 本体 FrameUpdate の後にも適用し、adjustedItems の SmoothDamp に戻されないようにする。
            var column = movement.transform.parent;
            if (column == null) return;
            if (_placedColumn != column)
            {
                _placedColumn = column;
                _originalPosition = column.localPosition;
            }
            int count = 0;
            foreach (var button in array)
            {
                if (count >= MaxColumns) break;
                if (button == null || button == movement || button.transform.parent == null) continue;
                if (!button.gameObject.activeInHierarchy) continue;
                _columns[count++] = button.transform.parent;
            }
            if (count < 2) return;
            for (int i = 0; i < count; i++) _xs[i] = _columns[i].position.x;
            Array.Sort(_xs, 0, count);
            float spacing = MedianGap(_xs, count, _gaps);
            if (spacing <= 0.0001f) return;
            float y = 0f;
            for (int i = 0; i < count; i++) y += _columns[i].position.y;
            y /= count;
            // 技列と同じキャンバスの世界座標で収まりを見る（1920x1080 のオーバーレイではピクセルに一致する）。
            var canvas = buttons.transform.root as RectTransform;
            if (canvas == null)
            {
                column.position = new Vector3(_xs[count - 1] + spacing, y, column.position.z);
                return;
            }
            canvas.GetWorldCorners(_corners);
            column.position = new Vector3(ChoosePlacementX(_xs, count, spacing, _corners[0].x, _corners[2].x), y, column.position.z);
        }

        // 並んだ x 座標（昇順）から隣り合う間隔の中央値を返す。遠く離れた列が混ざっても、
        // 間隔の中央値は1つの外れ値に引きずられない。
        internal static float MedianGap(float[] sortedXs, int count, float[] scratch)
        {
            if (count < 2) return 0f;
            int gaps = 0;
            for (int i = 1; i < count; i++)
            {
                float gap = sortedXs[i] - sortedXs[i - 1];
                if (gap <= 0.0001f) continue;
                if (gaps < scratch.Length) scratch[gaps++] = gap;
            }
            if (gaps == 0) return 0f;
            Array.Sort(scratch, 0, gaps);
            return gaps % 2 == 1 ? scratch[gaps / 2] : (scratch[gaps / 2 - 1] + scratch[gaps / 2]) * 0.5f;
        }

        // 右端の列の右隣に置く。画面右端を超えるときは左隣、それも収まらないときは既存の列の間で
        // 一番広い隙間の中央へ置く。画面内に収まり、既存の列の上に重ねないことを優先する。
        internal static float ChoosePlacementX(float[] sortedXs, int count, float spacing, float leftBound, float rightBound)
        {
            float x = sortedXs[count - 1] + spacing;
            if (x <= rightBound - spacing) return x;
            float alt = sortedXs[0] - spacing;
            if (alt >= leftBound + spacing * 0.5f) return alt;
            float gapCenter = sortedXs[0], widest = 0f;
            for (int i = 1; i < count; i++)
            {
                float gap = sortedXs[i] - sortedXs[i - 1];
                if (gap > widest)
                {
                    widest = gap;
                    gapCenter = (sortedXs[i] + sortedXs[i - 1]) * 0.5f;
                }
            }
            return gapCenter;
        }

        internal static void WarnFailed(Exception ex)
        {
            _failed = true;
            try { Hide(); }
            catch { /* 表示の復帰に失敗しても本体や他のMOD機能へ例外を返さない。 */ }
            if (_warned) return;
            _warned = true;
            Log.Warn("Movement essence slot display disabled; other MOD features remain active: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(UI_InGame_SkillButtons), "FrameUpdate")]
    internal static class MovementGemSlotPlacementPatch
    {
        private static void Postfix(UI_InGame_SkillButtons __instance)
        {
            try { MovementGemSlotEdit.UpdatePlacement(__instance); }
            catch (Exception ex) { MovementGemSlotEdit.WarnFailed(ex); }
        }
    }

    // 本体の探索は Identity で折り返す。表示できた画面だけ Movement まで範囲を延ばす。
    [HarmonyPatch(typeof(EditSkillManager), "TryGetNextRelevantSkillLocation")]
    internal static class MovementGemSlotSkillNavigationPatch
    {
        private static bool Prefix(EditSkillManager __instance, bool next, bool skipStart, bool canWrap,
            ref HeroSkillLocation loc, ref bool __result)
        {
            if (!MovementGemSlotEdit.IsAvailable) return true;
            try
            {
                int start = (int)(__instance.selectedSkillSlot ?? HeroSkillLocation.Q);
                int current = start;
                for (int i = 0; i < 6; i++)
                {
                    if ((i != 0 || !skipStart) && __instance.IsSlotSelectable((HeroSkillLocation)current))
                    {
                        loc = (HeroSkillLocation)current;
                        __result = true;
                        return false;
                    }
                    current += next ? 1 : -1;
                    if (current < 0 || current > (int)HeroSkillLocation.Movement)
                    {
                        if (!canWrap) { __result = false; return false; }
                        current = next ? 0 : (int)HeroSkillLocation.Movement;
                    }
                }
                loc = (HeroSkillLocation)start;
                __result = __instance.IsSlotSelectable(loc);
                return false;
            }
            catch (Exception ex) { MovementGemSlotEdit.WarnFailed(ex); return true; }
        }
    }

    [HarmonyPatch(typeof(EditSkillManager), "TryGetNextRelevantGemSlot")]
    internal static class MovementGemSlotGemNavigationPatch
    {
        private static bool Prefix(EditSkillManager __instance, bool next, bool skipStart, bool canWrap,
            ref GemLocation loc, ref bool __result)
        {
            if (!MovementGemSlotEdit.IsAvailable) return true;
            try
            {
                var skill = DewPlayer.local.hero.Skill;
                var start = __instance.selectedGemSlot ?? new GemLocation(HeroSkillLocation.Q, 0);
                var current = start;
                // 空の列も1ステップ使う。各列の本体上限から探索の上限を決め、0枠でも有限にする。
                int limit = 0;
                for (int i = 0; i <= (int)HeroSkillLocation.Movement; i++)
                    limit += Math.Max(1, skill.GetMaxGemCount((HeroSkillLocation)i));
                for (int i = 0; i < limit; i++)
                {
                    int cap = skill.GetMaxGemCount(current.skill);
                    if ((i != 0 || !skipStart) && current.index >= 0 && current.index < cap
                        && __instance.IsSlotSelectable(current))
                    {
                        loc = current;
                        __result = true;
                        return false;
                    }
                    current.index += next ? 1 : -1;
                    if (current.index >= 0 && current.index < cap) continue;
                    int column = (int)current.skill + (next ? 1 : -1);
                    if (column < 0 || column > (int)HeroSkillLocation.Movement)
                    {
                        if (!canWrap) { __result = false; return false; }
                        column = next ? 0 : (int)HeroSkillLocation.Movement;
                    }
                    current.skill = (HeroSkillLocation)column;
                    current.index = next ? 0 : skill.GetMaxGemCount(current.skill) - 1;
                }
                loc = start;
                __result = start.index >= 0 && start.index < skill.GetMaxGemCount(start.skill)
                    && __instance.IsSlotSelectable(start);
                return false;
            }
            catch (Exception ex) { MovementGemSlotEdit.WarnFailed(ex); return true; }
        }
    }

    [HarmonyPatch(typeof(EditSkillManager), "SetMode")]
    internal static class MovementGemSlotEditPatch
    {
        // 画面の開閉（SetMode）のたびにだけ動く。毎フレームの処理と割り当ては増やさない。
        private static void Postfix(EditSkillManager __instance)
        {
            try { MovementGemSlotEdit.OnModeChanged(__instance); }
            catch (Exception ex) { MovementGemSlotEdit.WarnFailed(ex); }
        }
    }
}
