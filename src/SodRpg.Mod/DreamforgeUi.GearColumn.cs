using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private static readonly string[] KnownHeroes =
        {
            "Hero_Lacerta", "Hero_Mist", "Hero_Aurena", "Hero_Bismuth", "Hero_Vesper", "Hero_Yubar", "Hero_Nachia", "Hero_Husk", "Hero_Cetus",
        };

        private Vector2 _scrollGear;

        /// <summary>
        /// 装備タブの左の欄（旅人・6つの装着枠・今回の強さ・狙い系統）。欄の中身をまるごと1つの
        /// スクロールに入れる。払い戻しパネルを表示していると残りの高さが約80px足りないことがあり、
        /// IMGUI ははみ出しを下端から順に切るので、従来は下端の狙い系統のボタンが枠の外に出て
        /// 押せなかった（#147 の残り）。固定の部分を残さず全部スクロールの中に置くことで、
        /// どのボタンもスクロールして押せる。内側に別のスクロールは作らない（入れ子は二重操作になるだけ）。
        /// </summary>
        internal void DrawGearColumn(Profile p, string hero)
        {
            GUILayout.BeginVertical(_st.Panel, GUILayout.Width(320), GUILayout.ExpandHeight(true));
            _scrollGear = GUILayout.BeginScrollView(_scrollGear, GUILayout.ExpandHeight(true));
            HeroPicker();
            {
                int kills = p.Hero(hero).Kills;
                int lv = Mastery.Level(kills);
                int next = Mastery.ToNext(kills);
                bool keyLocked = lv < HeroSigils.KeystoneMastery && HeroSigils.HasTree(hero);
                GUILayout.Label(Loc.T(
                    $"熟練度 {lv}「{Mastery.Title(lv)}」" + (next > 0 ? $" <color=#aaa>あと{next}体倒すと上がります</color>" : "")
                        + (keyLocked ? $"\n<color=#aaa>熟練度{HeroSigils.KeystoneMastery}で到達刻印を選べます。</color>" : ""),
                    $"Mastery {lv} \"{Mastery.Title(lv)}\"" + (next > 0 ? $" <color=#aaa>{next} kills to next</color>" : "")
                        + (keyLocked ? $"\n<color=#aaa>Keystones unlock at mastery {HeroSigils.KeystoneMastery}.</color>" : "")), _st.Small);
            }
            foreach (Slot slot in Content.SlotOrder)
            {
                var r = Rules.EquippedRelic(p, hero, slot);
                string label = Content.SlotName(slot) + "： " + (r != null ? UiStyles.RelicTitle(r) : Loc.T("<color=#777>（なし）</color>", "<color=#777>(empty)</color>"));
                GUILayout.BeginHorizontal();
                IconSlot(r, 30);
                if (GUILayout.Button(label, _slot == slot ? _st.RowSel : _st.Row, GUILayout.Height(30)))
                {
                    _slot = slot;
                    _selected = r?.Uid;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("現在の強さ", "Current build"), _st.Header);
            var build = _s.CurrentBuild(hero);
            if (build.Stats.Count == 0 && build.Powers.Count == 0 && build.BossMoves.Count == 0) GUILayout.Label(Loc.T("まだ何も装着していません。真ん中の一覧から遺物を選び、「装着する」を押してください。", "Nothing equipped yet. Pick a relic from the middle list and press Equip."), _st.Small);
            foreach (var kv in build.Stats) if (kv.Value != 0) GUILayout.Label(Content.FormatStat(kv.Key, kv.Value), _st.Small);
            foreach (var kv in build.Powers) GUILayout.Label(UiStyles.Colored(Content.FormatPower(kv.Key, kv.Value), "#e0b0ff"), _st.Small);
            foreach (var move in build.BossMoves)
                GUILayout.Label(UiStyles.Colored(BossBuildCodec.Describe(move), "#e0b0ff"), _st.Small);
            foreach (var kv in build.Sets)
            {
                var set = Content.GetSet(kv.Key);
                if (set == null) continue;
                GUILayout.Label(UiStyles.Colored($"《{set.Name}》 {kv.Value}/{(set.HasSixPiece ? 6 : 3)}", "#ff8a3d") + "  <color=#ddd>" + set.Progress(kv.Value) + "</color>\n<color=#ccd>" + set.Describe() + "</color>", _st.Small);
                DrawSetLinkProgress(set, kv.Value);
            }
            foreach (var kv in build.Lines)
            {
                if (kv.Value < 2) continue;
                string bonus = string.Join("・", Content.SetBonus(kv.Key, kv.Value).Select(x => Content.FormatStat(x.Stat, x.Value)));
                GUILayout.Label(UiStyles.Colored(Loc.T($"〈{Content.LineName(kv.Key)}×{kv.Value}〉{bonus}", $"<{Content.LineName(kv.Key)} x{kv.Value}> {bonus}"), "#9fe0c0"), _st.Small);
            }
            // 連携（v1.26）：いま条件を満たしている連携だけを出す（ゲームの外では判定できないので出さない）。
            var marks = LinkMarks();
            if (marks != null)
            {
                foreach (var active in build.Links)
                {
                    if (!Links.Satisfied(active, _linkHeroKey, _linkMemories, _linkEssences, _linkAllies)) continue;
                    GUILayout.Label(UiStyles.Colored(Links.Describe(active, marks), "#7fd8ff"), _st.Small);
                }
            }
            if (!_s.CanEditLoadout)
                GUILayout.Label(Loc.T("遠征中は、確保地点に着いてから次の戦闘で敵を倒すまで、装備を変えられます。", "During an expedition, you can change gear from the moment you reach a secure point until you slay an enemy in the next fight."), _st.Warn);
            GUILayout.Label(Loc.T("同じ系統（破壊・生命・想像）の遺物を2・3・4・6個とそろえるほど、系統のボーナスが重なります。", "Equip 2, 3, 4 or 6 relics of the same line (Destruction, Life, Imagination) for stacking line bonuses."), _st.Small);
            FocusPicker();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void FocusPicker()
        {
            var p = _s.Profile;
            GUILayout.Label(Loc.T("狙い系統：選んだ系統の遺物が2倍出やすくなります（遠征の前に選びます。「なし」なら今日の夢の系統が出やすくなります）", "Focus: relics of the chosen line drop twice as often (choose before an expedition; with None, today's dream decides)"), _st.Small);
            GUILayout.BeginHorizontal();
            GUI.enabled = p.Run == null || !_s.InGame;
            if (GUILayout.Button(Loc.T("なし", "None"), p.Focus == null ? _st.ButtonSel : _st.Button)) SetFocus(null);
            foreach (Line l in Enum.GetValues(typeof(Line)))
                if (GUILayout.Button(Content.LineName(l).ToString(), p.Focus == l ? _st.ButtonSel : _st.Button)) SetFocus(l);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void SetFocus(Line? l)
        {
            try
            {
                Rules.SetFocus(_s.Profile, l, _s.InGame && _s.Profile.Run != null);
                _s.MarkDirty(false);
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(ex.Message);
            }
        }

        private readonly List<string> _pickerHeroes = new List<string>();
        private static readonly GUILayoutOption[] PickerCaptionWidth = { GUILayout.Width(60) };
        private static readonly GUILayoutOption[] PickerArrowWidth = { GUILayout.Width(30) };
        private static readonly GUILayoutOption[] PickerNameWidth = { GUILayout.Width(110) };
        private readonly GUIContent _pickerName = new GUIContent();
        private string _pickerNamedHero;
        private void HeroPicker()
        {
            if (_s.LocalHero != null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("旅人：", "Traveler:"), _st.Small, PickerCaptionWidth);
            _pickerHeroes.Clear();
            _pickerHeroes.AddRange(KnownHeroes);
            foreach (string key in _s.Profile.Heroes.Keys)
                if (key != "default" && !_pickerHeroes.Contains(key)) _pickerHeroes.Add(key);
            int idx = Math.Max(0, _pickerHeroes.IndexOf(HeroKey));
            if (GUILayout.Button("<", _st.Button, PickerArrowWidth))
                _heroSel = _pickerHeroes[(idx - 1 + _pickerHeroes.Count) % _pickerHeroes.Count];
            string selectedHero = HeroKey;
            if (_pickerNamedHero != selectedHero)
            {
                _pickerNamedHero = selectedHero;
                _pickerName.text = "<b>" + HeroName(selectedHero) + "</b>";
            }
            GUILayout.Label(_pickerName, _st.Label, PickerNameWidth);
            if (GUILayout.Button(">", _st.Button, PickerArrowWidth)) _heroSel = _pickerHeroes[(idx + 1) % _pickerHeroes.Count];
            GUILayout.EndHorizontal();
        }
    }
}
