using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private readonly GUIContent[] _loadoutButtons = { new GUIContent(), new GUIContent(), new GUIContent(), new GUIContent(), new GUIContent() };
        private readonly string[] _loadoutNames = new string[StarLoadouts.Count];
        private readonly string[] _loadoutCustomNames = new string[StarLoadouts.Count];
        private readonly GUILayoutOption[] _loadoutNameWidth = { GUILayout.Width(220f) };
        private readonly GUILayoutOption[] _loadoutButtonWidth = { GUILayout.Width(210f) };
        private static readonly string[] LoadoutNumbers = { "1", "2", "3", "4", "5" };
        private HeroState _loadoutHero;
        private LocLanguage _loadoutJapanese;
        private int _loadoutEditSlot;
        private string _loadoutNameEdit;

        private void RefreshLoadoutLabels(HeroState hero)
        {
            bool changed = _loadoutHero != hero || _loadoutJapanese != Loc.Language;
            for (int i = 0; i < StarLoadouts.Count; i++)
            {
                string custom = hero.StarLoadouts[i]?.Name;
                if (!changed && custom == _loadoutCustomNames[i]) continue;
                _loadoutCustomNames[i] = custom;
                string name = StarLoadouts.DisplayName(hero, i, Loc.Japanese);
                _loadoutNames[i] = name;
                string shortName = name.Length > 8 ? name.Substring(0, char.IsHighSurrogate(name[7]) ? 7 : 8) + "…" : name;
                _loadoutButtons[i].text = (i + 1) + "  " + shortName;
                _loadoutButtons[i].tooltip = name;
            }
            if (changed)
            {
                _loadoutEditSlot = hero.ActiveStarLoadout;
                _loadoutNameEdit = _loadoutNames[_loadoutEditSlot];
            }
            _loadoutHero = hero;
            _loadoutJapanese = Loc.Language;
        }

        private void DrawStarLoadouts(Profile profile, string heroKey)
        {
            var hero = profile.Hero(heroKey);
            RefreshLoadoutLabels(hero);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _s.CanEditTalents && profile.Run == null;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < StarLoadouts.Count; i++)
                if (GUILayout.Button(_loadoutButtons[i], i == hero.ActiveStarLoadout ? _st.TabSel : _st.Tab, _loadoutButtonWidth))
                    ChangeStarLoadout(profile, heroKey, i);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("編集する枠", "Edit slot", "编辑槽位"), _st.Small);
            for (int i = 0; i < StarLoadouts.Count; i++)
                if (GUILayout.Button(LoadoutNumbers[i], i == _loadoutEditSlot ? _st.TabSel : _st.Tab))
                {
                    _loadoutEditSlot = i;
                    _loadoutNameEdit = _loadoutNames[i];
                    GUIUtility.ExitGUI();
                }
            _loadoutNameEdit = GUILayout.TextField(_loadoutNameEdit, StarLoadouts.MaxNameLength, _loadoutNameWidth);
            if (GUILayout.Button(Loc.T("名前を変更", "Rename", "重命名"), _st.Button))
            {
                StarLoadouts.Rename(profile, heroKey, _loadoutEditSlot, _loadoutNameEdit);
                RefreshLoadoutLabels(hero);
                _loadoutNameEdit = _loadoutNames[_loadoutEditSlot];
                _s.MarkDirty(false);
                _s.SaveNow();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(Loc.T("現在の構成をこの枠にコピー", "Copy current allocation to this slot", "将当前配置复制到此槽位"), _st.Button))
            {
                StarLoadouts.CopyCurrent(profile, heroKey, _loadoutEditSlot);
                _s.MarkDirty(false);
                _s.SaveNow();
                SetStatus(Loc.T($"現在の構成を「{_loadoutNames[_loadoutEditSlot]}」にコピーしました。",
                    $"Copied the current allocation to “{_loadoutNames[_loadoutEditSlot]}”.", $"已将当前配置复制到“{_loadoutNames[_loadoutEditSlot]}”。"));
                GUIUtility.ExitGUI();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUI.enabled = enabled;
        }

        private void ChangeStarLoadout(Profile profile, string heroKey, int index)
        {
            if (!_s.CanEditTalents || profile.Run != null) return;
            var notes = new List<string>();
            StarLoadouts.Switch(profile, heroKey, index, notes);
            foreach (string note in notes) Log.Warn(note);
            if (notes.Count > 0) SetStatus(string.Join("\n", notes));
            _loadoutEditSlot = index;
            _loadoutNameEdit = _loadoutNames[index];
            _allocationRefund = null;
            _starChoiceId = null;
            _starDirty = true;
            _starSumDirty = true;
            CancelStarDrag();
            _s.MarkDirty(true);
            _s.SaveNow();
            GUIUtility.ExitGUI();
        }
    }
}
