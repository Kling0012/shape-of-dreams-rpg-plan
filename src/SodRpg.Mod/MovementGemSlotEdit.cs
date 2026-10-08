using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // エッセンスの装着画面は本体の HUD 技ボタン（UI_InGame_SkillButtons）を EditSkillManager が拡大したもので、
    // 枠の並びは各技ボタンの UI_InGame_SkillButton_GemGroup が GetMaxGemCount の現在値で描く。逆コンパイルでは
    // Movement をコードで除外する箇所は無く、本体はプレハブ側で回避の技ボタンを既定で隠している（実機報告と一致）。
    // ここでは装着画面が開くたび（EditSkillManager.SetMode の後）だけ、回避に枠が1つ以上あるとき技ボタンの列を
    // 表示に戻し、既存の技列の間隔を読んでその右隣（収まらないときは左隣）へ置く。枠への装着・外し・入れ替えは
    // すべて本体経路（CmdEquipGem / CmdSwapSlotGem / CmdUnequipGem）なので、協力プレイでも本体が同期する。
    // 失敗したらこの表示だけを諦めて警告1回。MOD全体や他の機能は止めない。
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
        private static bool _warned, _failed, _shownLogged;

        internal static void ResetForTest()
        {
            _warned = _failed = _shownLogged = false;
            _activated.Clear();
        }

        internal static void OnModeChanged(EditSkillManager manager)
        {
            if (_failed || manager == null) return;
            if (manager.mode == EditSkillManager.ModeType.None) { Hide(); return; }
            var player = DewPlayer.local;
            var hero = player == null ? null : player.hero;
            var skill = hero == null ? null : hero.Skill;
            if (skill == null) return;
            var buttons = ManagerBase<UI_InGame_SkillButtons>.softInstance;
            var array = buttons == null ? null : buttons.skillButtons;
            if (array == null || array.Length == 0) return;
            UI_InGame_SkillButton movement = null;
            foreach (var button in array)
                if (button != null && button.skillType == HeroSkillLocation.Movement) { movement = button; break; }
            if (movement == null) movement = FindOrCloneColumn(buttons, array);
            if (movement == null) return;
            if (skill.GetMaxGemCount(HeroSkillLocation.Movement) < 1) { Hide(); return; }
            Show(manager, buttons, array, movement, skill.GetMaxGemCount(HeroSkillLocation.Movement));
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
            Place(buttons, array, movement);
            // 開いた直後は本体が技列を拡大位置へ動かしているので、落ち着いたところでもう一度だけ合わせる。
            Dew.CallDelayed(() =>
            {
                try
                {
                    if (!_failed && manager != null && manager.mode != EditSkillManager.ModeType.None)
                        Place(buttons, array, movement);
                }
                catch (Exception ex) { WarnFailed(ex); }
            }, 20);
            if (_shownLogged) return;
            _shownLogged = true;
            Log.Info("Client: Movement essence slots shown in the edit screen (" + cap + " slot(s))");
        }

        private static void Hide()
        {
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
            Transform parent = null;
            foreach (var button in array)
            {
                if (button == null || button.skillType == HeroSkillLocation.Movement) continue;
                if (any == null) any = button;
                if (parent == null && button.transform.parent != null) parent = button.transform.parent;
                if (button.skillType == HeroSkillLocation.Q) { template = button; break; }
            }
            if (template == null) template = any;
            if (template == null || template.transform.parent == null) return null;
            if (parent == null) parent = template.transform.parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i).gameObject;
                var reuse = child.GetComponentInChildren<UI_InGame_SkillButton>();
                if (reuse != null && reuse.skillType == HeroSkillLocation.Movement) return reuse;
            }
            GameObject clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(template.transform.parent.gameObject, parent);
            }
            catch (Exception ex)
            {
                WarnFailed(new InvalidOperationException("cannot clone a skill column: " + ex.Message));
                return null;
            }
            clone.name = CloneName;
            clone.SetActive(false);
            var cloned = clone.GetComponentInChildren<UI_InGame_SkillButton>();
            if (cloned == null)
            {
                UnityEngine.Object.Destroy(clone);
                WarnFailed(new MissingComponentException("cloned skill column has no button"));
                return null;
            }
            cloned.skillType = HeroSkillLocation.Movement;
            // 複製した列のキー表示は元の技のものが残るので、誤解を避けるために消しておく。
            if (cloned.skillActivationKeyObject != null) cloned.skillActivationKeyObject.SetActive(false);
            var grown = new UI_InGame_SkillButton[array.Length + 1];
            Array.Copy(array, grown, array.Length);
            grown[array.Length] = cloned;
            buttons.skillButtons = grown;
            return cloned;
        }

        private static void Place(UI_InGame_SkillButtons buttons, UI_InGame_SkillButton[] array, UI_InGame_SkillButton movement)
        {
            // 本体の配置管理（adjustedItems）に入っている列は毎フレーム本体が動かすので触らない。
            var column = movement.transform.parent;
            if (column == null) return;
            if (buttons.adjustedItems != null)
                foreach (var item in buttons.adjustedItems)
                    if (item == column) return;
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
            Hide();
            if (_warned) return;
            _warned = true;
            Log.Warn("Movement essence slot display disabled; other MOD features remain active: " + ex.Message);
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
