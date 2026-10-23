using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SodRpg.Mod
{
    // customData は本体の SyncDictionary。旧版に未知のRPCを送らず、設定と実効上限を共有する。
    internal static class LevelEssenceRuntime
    {
        private const string SettingsKey = "Dreamforge.LevelEssence.v1";
        private sealed class HudState { }
        private static readonly ConditionalWeakTable<UI_InGame_SkillButton_GemGroup, HudState> HudStates =
            new ConditionalWeakTable<UI_InGame_SkillButton_GemGroup, HudState>();
        private static readonly System.Reflection.FieldInfo LastHudCount = AccessTools.Field(typeof(UI_InGame_SkillButton_GemGroup), "_lastAppliedGemCount");
        private static LevelEssenceSettings _display = LevelEssenceSettings.Default;
        private static string _lastRemote, _payload;
        private static float _nextCheck;
        private static bool _hudFailed, _hudCountLogged;
        private static int _readyTargets, _remoteCap = 4;
        private static bool _hudScanPending = true;

        static LevelEssenceRuntime()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => _hudScanPending = true;
        internal static bool HudLimited => _hudFailed || _readyTargets != LevelEssenceSettings.TargetAll;
        internal static LevelEssenceSettings DisplaySettings => _display;
        internal static int EffectiveCap => Math.Min(_display.Ceiling, LevelEssenceSettings.RuntimeCapLimit);

        internal static bool IsMemory(HeroSkillLocation loc) => loc == HeroSkillLocation.Q || loc == HeroSkillLocation.W
            || loc == HeroSkillLocation.E || loc == HeroSkillLocation.R;
        internal static int CapForLocation(HeroSkillLocation loc) => IsMemory(loc) ? EffectiveCap : 4;

        internal static void Configure(LevelEssenceSettings settings)
        {
            LevelEssenceSettings.RuntimeCapLimit = HudLimited ? 4 : 8;
            if (!NetworkClient.active || NetworkServer.active) SetDisplaySettings(settings);
            _hudScanPending = true;
            _payload = null;
            _nextCheck = 0f;
        }

        private static void SetDisplaySettings(LevelEssenceSettings settings)
        {
            if (ReferenceEquals(_display, settings)) return;
            _display = settings;
            _hudScanPending = true;
        }

        internal static void DisableHud(string reason)
        {
            if (_hudFailed) return;
            _hudFailed = true;
            LevelEssenceSettings.RuntimeCapLimit = 4;
            _payload = null;
            Log.Warn("Level essence HUD extension unavailable; effective cap limited to 4: " + reason);
        }

        internal static void Tick(bool force = false)
        {
            if (force) _hudScanPending = true;
            if (!force && Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            try
            {
                if (_hudScanPending && !_hudFailed)
                {
                    _hudScanPending = false;
                    foreach (var group in Resources.FindObjectsOfTypeAll<UI_InGame_SkillButton_GemGroup>())
                        if (group.gameObject.scene.IsValid()) EnsureHud(group);
                }
                var manager = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (!NetworkClient.active || NetworkServer.active)
                {
                    _lastRemote = null;
                    SetDisplaySettings(LevelEssenceSettings.Current ?? LevelEssenceSettings.Off);
                    LevelEssenceSettings.RuntimeCapLimit = HudLimited ? 4 : 8;
                    if (NetworkServer.active && manager != null)
                    {
                        if (_payload == null) _payload = string.Join(",", _display.Enabled ? "1" : "0",
                            _display.Targets.ToString(CultureInfo.InvariantCulture), _display.EveryLevels.ToString(CultureInfo.InvariantCulture),
                            _display.Ceiling.ToString(CultureInfo.InvariantCulture), EffectiveCap.ToString(CultureInfo.InvariantCulture));
                        if (!manager.customData.TryGetValue(SettingsKey, out string old) || old != _payload)
                            manager.customData[SettingsKey] = _payload;
                    }
                }
                else if (manager != null && manager.customData.TryGetValue(SettingsKey, out string value))
                {
                    if (_lastRemote == value)
                    {
                        LevelEssenceSettings.RuntimeCapLimit = Math.Min(HudLimited ? 4 : 8,
                            LevelEssenceSettings.ClampCeiling(_remoteCap));
                        return;
                    }
                    var parts = value.Split(',');
                    if (parts.Length != 5 || !int.TryParse(parts[0], out int enabled) || !int.TryParse(parts[1], out int targets)
                        || !int.TryParse(parts[2], out int every) || !int.TryParse(parts[3], out int cap)
                        || !int.TryParse(parts[4], out int effective)) return;
                    SetDisplaySettings(LevelEssenceSettings.Create(enabled != 0, (targets & 1) != 0, (targets & 2) != 0,
                        (targets & 4) != 0, (targets & 8) != 0, every, cap));
                    LevelEssenceSettings.RuntimeCapLimit = Math.Min(HudLimited ? 4 : 8, LevelEssenceSettings.ClampCeiling(effective));
                    _remoteCap = effective;
                    _lastRemote = value;
                }
                else
                {
                    _lastRemote = null;
                    SetDisplaySettings(LevelEssenceSettings.Current ?? LevelEssenceSettings.Off);
                    LevelEssenceSettings.RuntimeCapLimit = HudLimited ? 4 : 8;
                }
            }
            catch (Exception ex) { DisableHud(ex.Message); }
        }

        internal static void EnsureHud(UI_InGame_SkillButton_GemGroup group)
        {
            if (_hudFailed || group == null) return;
            if (HudStates.TryGetValue(group, out var state)) return;
            state = new HudState();
            HudStates.Add(group, state);
            try
            {
                var button = group.transform.parent.GetComponentInChildren<UI_InGame_SkillButton>(true);
                if (button == null || !IsMemory(button.skillType)) return;
                int target = button.skillType == HeroSkillLocation.Q ? 1 : button.skillType == HeroSkillLocation.W ? 2
                    : button.skillType == HeroSkillLocation.E ? 4 : 8;
                var original = group.groups;
                if (original == null || original.Length < 4) throw new InvalidOperationException("HUD has fewer than four groups");
                if (!_hudCountLogged)
                {
                    _hudCountLogged = true;
                    Log.Info("Level essence HUD: native groups=" + original.Length + "; validating slotIndex and extending to 8.");
                }
                // 既存の並びも検証。Prefabの個数や構成を推測で安全判定しない。
                for (int n = 4; n <= Math.Min(8, original.Length); n++) ValidateGroup(original[n - 1], n, button);
                if (original.Length < 8)
                {
                    var expanded = new GameObject[8];
                    Array.Copy(original, expanded, original.Length);
                    // OnEnableが旧slotIndexで宝石を購読しないよう、初期化は非表示の親の下で行う。
                    var staging = new GameObject("Dreamforge_EssenceStaging", typeof(RectTransform));
                    staging.SetActive(false);
                    staging.transform.SetParent(original[3].transform.parent, false);
                    var stagingRect = (RectTransform)staging.transform;
                    stagingRect.anchorMin = Vector2.zero;
                    stagingRect.anchorMax = Vector2.one;
                    stagingRect.sizeDelta = Vector2.zero;
                    for (int n = original.Length + 1; n <= 8; n++)
                    {
                        var template = original[3];
                        var clone = UnityEngine.Object.Instantiate(template, staging.transform, false);
                        clone.name = "Dreamforge_EssenceSlots_" + n;
                        clone.SetActive(false);
                        // 全部できる前に失敗しても、途中の複製は非表示のまま。
                        var slots = clone.GetComponentsInChildren<UI_InGame_GemSlot>(true);
                        Array.Sort(slots, (a, b) => a.slotIndex.CompareTo(b.slotIndex));
                        // Preserve the native four columns, anchors, pivots and scale.
                        // Measure visible child rectangles in their common parent's coordinates.
                        var first = (RectTransform)slots[0].transform;
                        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
                        var corners = new Vector3[4];
                        foreach (var slot in slots)
                        {
                            if (slot.transform.parent != first.parent)
                                throw new InvalidOperationException("HUD sockets do not share a parent");
                            // Include inactive frames while staging: native bounds helpers omit them.
                            foreach (var rect in slot.GetComponentsInChildren<RectTransform>(true))
                            {
                                rect.GetWorldCorners(corners);
                                for (int c = 0; c < corners.Length; c++)
                                {
                                    float y = first.parent.InverseTransformPoint(corners[c]).y;
                                    bottom = Math.Min(bottom, y);
                                    top = Math.Max(top, y);
                                }
                            }
                            slot.button = button;
                        }
                        float step = top - bottom + 4f;
                        if (float.IsNaN(step) || float.IsInfinity(step) || step <= 4f)
                            throw new InvalidOperationException("HUD socket bounds are unavailable");
                        foreach (var layout in clone.GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
                        for (int i = 4; i < n; i++)
                        {
                            var source = slots[i - 4];
                            var extra = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent, false)
                                .GetComponent<UI_InGame_GemSlot>();
                            extra.slotIndex = i;
                            extra.button = button;
                            // Extra sockets grow away from the skill buttons, never down into them.
                            ((RectTransform)extra.transform).anchoredPosition += new Vector2(0f, step);
                        }
                        ValidateGroup(clone, n, button);
                        clone.transform.SetParent(original[original.Length - 1].transform.parent, false);
                        expanded[n - 1] = clone;
                    }
                    if (LastHudCount == null) throw new InvalidOperationException("HUD refresh field is unavailable");
                    LastHudCount.SetValue(group, int.MinValue);
                    group.groups = expanded;
                }
                _readyTargets |= target;
                _payload = null;
            }
            catch (Exception ex) { DisableHud(ex.Message); }
        }

        private static void ValidateGroup(GameObject group, int count, UI_InGame_SkillButton button)
        {
            if (group == null) throw new InvalidOperationException("HUD group is missing");
            var slots = group.GetComponentsInChildren<UI_InGame_GemSlot>(true);
            if (slots.Length != count) throw new InvalidOperationException("HUD slot count differs from group index");
            int indices = 0;
            foreach (var slot in slots)
            {
                if (slot.slotIndex < 0 || slot.slotIndex >= count || (indices & (1 << slot.slotIndex)) != 0
                    || slot.button != button || slot.GetComponent<UI_InGame_GemSlot_EditSkill>() == null)
                    throw new InvalidOperationException("HUD slotIndex/button/edit handler is unsupported");
                indices |= 1 << slot.slotIndex;
            }
        }
    }
}
