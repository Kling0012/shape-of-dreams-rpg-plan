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
        private Func<string, float> _linkTravelerCoefficient;
        private static readonly Line[] FocusLines = (Line[])Enum.GetValues(typeof(Line));
        private static readonly GUILayoutOption[] GearColumnSize = { GUILayout.Width(320), GUILayout.ExpandHeight(true) };
        private static readonly GUILayoutOption[] GearScrollSize = { GUILayout.ExpandHeight(true) };
        private static readonly GUILayoutOption[] GearSlotHeight = { GUILayout.Height(30) };
        private string _gearMasteryText, _gearMasteryHero;
        private int _gearMasteryKills = -1;
        private int _gearMasteryJa;
        private readonly List<string> _gearRows = new List<string>();
        private readonly Dictionary<string, string> _gearSetRows = new Dictionary<string, string>();
        private readonly List<string> _gearLineRows = new List<string>();
        private readonly GearSlotText[] _gearSlotTexts = new GearSlotText[Content.SlotOrder.Count];
        private sealed class GearSlotText
        {
            internal Relic Relic;
            internal string BaseId, UniqueId, NamedId, Text;
            internal int Enhance, Awaken, Fusion;
            internal SodRpg.Core.Game.Rarity Rarity;
            internal Power? FirstPower;
            internal int Japanese;
        }

        private string GearSlotLabel(Profile profile, string hero, Slot slot, int index, out Relic relic)
        {
            relic = Rules.EquippedRelic(profile, hero, slot);
            var row = _gearSlotTexts[index] ?? (_gearSlotTexts[index] = new GearSlotText());
            Power? first = relic != null && relic.Powers.Count > 0 ? relic.Powers[0].Power : (Power?)null;
            if (row.Text != null && ReferenceEquals(row.Relic, relic) && row.Japanese == Loc.CacheKey
                && row.BaseId == relic?.BaseId && row.UniqueId == relic?.UniqueId && row.NamedId == relic?.NamedId
                && row.Enhance == (relic?.Enhance ?? 0) && row.Awaken == (relic?.AwakenLevel ?? 0)
                && row.Fusion == (relic?.FusionLevel ?? 0) && row.Rarity == (relic?.Rarity ?? default)
                && row.FirstPower == first) return row.Text;
            row.Relic = relic; row.Japanese = Loc.CacheKey; row.BaseId = relic?.BaseId;
            row.UniqueId = relic?.UniqueId; row.NamedId = relic?.NamedId;
            row.Enhance = relic?.Enhance ?? 0; row.Awaken = relic?.AwakenLevel ?? 0;
            row.Fusion = relic?.FusionLevel ?? 0; row.Rarity = relic?.Rarity ?? default; row.FirstPower = first;
            return row.Text = Content.SlotName(slot) + "： " + (relic != null ? UiStyles.RelicTitle(relic)
                : Loc.T("<color=#777>（なし）</color>", "<color=#777>(empty)</color>", "<color=#777>（无）</color>"));
        }
        private readonly Dictionary<Stat, int> _gearStats = new Dictionary<Stat, int>();
        private readonly Dictionary<Power, int> _gearPowers = new Dictionary<Power, int>();
        private readonly Dictionary<string, int> _gearSets = new Dictionary<string, int>();
        private readonly Dictionary<Line, int> _gearLines = new Dictionary<Line, int>();
        private readonly List<BossMoveEntry> _gearMoves = new List<BossMoveEntry>();
        private int _gearRowsJa;
        private readonly List<GearLinkText> _gearLinkTexts = new List<GearLinkText>();
        private sealed class GearLinkText
        {
            internal LinkKind Kind;
            internal int Value;
            internal int Japanese;
            internal string[] Requires;
            internal float[] Coefficients;
            internal string Text;
        }

        private string GearLinkLabel(LinkDef link, int index, Func<string, bool> marks)
        {
            while (_gearLinkTexts.Count <= index) _gearLinkTexts.Add(new GearLinkText());
            var row = _gearLinkTexts[index];
            bool same = row.Text != null && row.Kind == link.Kind && row.Value == link.ValueMilli
                && row.Japanese == Loc.CacheKey && row.Requires.Length == link.Requires.Length;
            if (same)
                for (int i = 0; i < link.Requires.Length; i++)
                    if (row.Requires[i] != link.Requires[i]
                        || row.Coefficients[i] != (_linkTravelerCoefficient?.Invoke(link.Requires[i]) ?? 1f))
                    { same = false; break; }
            // Only satisfied links reach this path, so every requirement mark is a check.
            if (same) return row.Text;
            row.Kind = link.Kind; row.Value = link.ValueMilli; row.Japanese = Loc.CacheKey;
            if (row.Requires == null || row.Requires.Length != link.Requires.Length)
            {
                row.Requires = new string[link.Requires.Length];
                row.Coefficients = new float[link.Requires.Length];
            }
            for (int i = 0; i < link.Requires.Length; i++)
            {
                row.Requires[i] = link.Requires[i];
                row.Coefficients[i] = _linkTravelerCoefficient?.Invoke(link.Requires[i]) ?? 1f;
            }
            return row.Text = UiStyles.Colored(Links.Describe(link, marks, _linkTravelerCoefficient), "#7fd8ff");
        }

        private static bool SameGearValues<T>(Dictionary<T, int> previous, SortedDictionary<T, int> current)
        {
            if (previous.Count != current.Count) return false;
            foreach (var kv in previous)
                if (!current.TryGetValue(kv.Key, out int value) || value != kv.Value) return false;
            return true;
        }

        private static void CopyGearValues<T>(Dictionary<T, int> target, SortedDictionary<T, int> source)
        {
            target.Clear();
            foreach (var kv in source) target.Add(kv.Key, kv.Value);
        }

        private bool SameGearMoves(Build build)
        {
            if (_gearMoves.Count != build.BossMoves.Count) return false;
            for (int i = 0; i < _gearMoves.Count; i++)
            {
                var a = _gearMoves[i]; var b = build.BossMoves[i];
                if (a.SetId != b.SetId || a.ProfileId != b.ProfileId || a.Channels.Count != b.Channels.Count) return false;
                for (int j = 0; j < a.Channels.Count; j++)
                    if (a.Channels[j].ChannelId != b.Channels[j].ChannelId || a.Channels[j].ValueMilli != b.Channels[j].ValueMilli) return false;
            }
            return true;
        }

        private void RefreshGearRows(Build build)
        {
            if (_gearRowsJa == Loc.CacheKey && SameGearValues(_gearStats, build.Stats)
                && SameGearValues(_gearPowers, build.Powers) && SameGearValues(_gearSets, build.Sets)
                && SameGearValues(_gearLines, build.Lines) && SameGearMoves(build)) return;
            _gearRowsJa = Loc.CacheKey;
            _gearRows.Clear(); _gearSetRows.Clear(); _gearLineRows.Clear();
            CopyGearValues(_gearStats, build.Stats); CopyGearValues(_gearPowers, build.Powers);
            CopyGearValues(_gearSets, build.Sets); CopyGearValues(_gearLines, build.Lines);
            _gearMoves.Clear(); _gearMoves.AddRange(build.BossMoves);
            foreach (var kv in build.Stats) if (kv.Value != 0) _gearRows.Add(Content.FormatStat(kv.Key, kv.Value));
            foreach (var kv in build.Powers) _gearRows.Add(UiStyles.Colored(Content.FormatPowerBullets(kv.Key, kv.Value), "#e0b0ff"));
            foreach (var move in build.BossMoves)
                _gearRows.Add(UiStyles.Colored(BossBuildCodec.Describe(move), "#e0b0ff"));
            foreach (var kv in build.Sets)
            {
                var set = Content.GetSet(kv.Key);
                if (set != null) _gearSetRows[kv.Key] = UiStyles.Colored($"《{set.Name}》 {kv.Value}/{(set.HasSixPiece ? 6 : 3)}", "#ff8a3d")
                    + "  <color=#ddd>" + set.Progress(kv.Value) + "</color>\n<color=#ccd>" + set.Describe() + "</color>";
            }
            foreach (var kv in build.Lines)
            {
                if (kv.Value < 2) continue;
                string bonus = string.Join("・", Content.SetBonus(kv.Key, kv.Value).Select(x => Content.FormatStat(x.Stat, x.Value)));
                _gearLineRows.Add(UiStyles.Colored(Loc.T($"〈{Content.LineName(kv.Key)}×{kv.Value}〉{bonus}",
                    $"<{Content.LineName(kv.Key)} x{kv.Value}> {bonus}", $"〈{Content.LineName(kv.Key)}×{kv.Value}〉{bonus}"), "#9fe0c0"));
            }
        }

        /// <summary>
        /// 装備タブの左の欄（旅人・6つの装着枠・今回の強さ・狙い系統）。欄の中身をまるごと1つの
        /// スクロールに入れる。払い戻しパネルを表示していると残りの高さが約80px足りないことがあり、
        /// IMGUI ははみ出しを下端から順に切るので、従来は下端の狙い系統のボタンが枠の外に出て
        /// 押せなかった（#147 の残り）。固定の部分を残さず全部スクロールの中に置くことで、
        /// どのボタンもスクロールして押せる。内側に別のスクロールは作らない（入れ子は二重操作になるだけ）。
        /// </summary>
        internal void DrawGearColumn(Profile p, string hero)
        {
            GUILayout.BeginVertical(_st.Panel, GearColumnSize);
            _scrollGear = GUILayout.BeginScrollView(_scrollGear, GearScrollSize);
            HeroPicker();
            {
                int kills = p.Hero(hero).Kills;
                if (_gearMasteryText == null || _gearMasteryHero != hero || _gearMasteryKills != kills || _gearMasteryJa != Loc.CacheKey)
                {
                    _gearMasteryHero = hero; _gearMasteryKills = kills; _gearMasteryJa = Loc.CacheKey;
                    int lv = Mastery.Level(kills);
                    int next = Mastery.ToNext(kills);
                    bool keyLocked = lv < HeroSigils.KeystoneMastery && HeroSigils.HasTree(hero);
                    _gearMasteryText = Loc.T(
                        $"熟練度 {lv}「{Mastery.Title(lv)}」" + (next > 0 ? $" <color=#aaa>あと{next}体倒すと上がります</color>" : "")
                            + (keyLocked ? $"\n<color=#aaa>熟練度{HeroSigils.KeystoneMastery}で到達刻印を選べます。</color>" : ""),
                        $"Mastery {lv} \"{Mastery.Title(lv)}\"" + (next > 0 ? $" <color=#aaa>{next} kills to next</color>" : "")
                            + (keyLocked ? $"\n<color=#aaa>Keystones unlock at mastery {HeroSigils.KeystoneMastery}.</color>" : ""), $"熟练度 {lv}「{Mastery.Title(lv)}」" + (next > 0 ? $" <color=#aaa>再击败{next}个敌人后提升</color>" : "") + (keyLocked ? $"\n<color=#aaa>熟练度{HeroSigils.KeystoneMastery}后可选择刻印。</color>" : ""));
                }
                GUILayout.Label(_gearMasteryText, _st.Small);
            }
            for (int i = 0; i < Content.SlotOrder.Count; i++)
            {
                Slot slot = Content.SlotOrder[i];
                string label = GearSlotLabel(p, hero, slot, i, out var r);
                GUILayout.BeginHorizontal();
                IconSlot(r, 30);
                if (GUILayout.Button(label, _slot == slot ? _st.RowSel : _st.Row, GearSlotHeight))
                {
                    _slot = slot;
                    _selected = r?.Uid;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("現在の強さ", "Current build", "当前的强度"), _st.Header);
            var build = _s.DisplayBuild(hero);
            if (build.Stats.Count == 0 && build.Powers.Count == 0 && build.BossMoves.Count == 0) GUILayout.Label(Loc.T("まだ何も装着していません。真ん中の一覧から遺物を選び、「装着する」を押してください。", "Nothing equipped yet. Pick a relic from the middle list and press Equip.", "还没有装备任何物品。请从中间的列表选择遗物并按下“装备”。"), _st.Small);
            RefreshGearRows(build);
            foreach (string text in _gearRows) GUILayout.Label(text, _st.Small);
            foreach (var kv in build.Sets)
            {
                var set = Content.GetSet(kv.Key);
                if (set == null) continue;
                GUILayout.Label(_gearSetRows[kv.Key], _st.Small);
                DrawSetLinkProgress(set, kv.Value);
            }
            foreach (string text in _gearLineRows) GUILayout.Label(text, _st.Small);
            // 連携（v1.26）：いま条件を満たしている連携だけを出す（ゲームの外では判定できないので出さない）。
            var marks = LinkMarks();
            if (marks != null)
            {
                int linkIndex = 0;
                foreach (var active in build.Links)
                {
                    if (!Links.Satisfied(active, _linkHeroKey, _linkMemories, _linkEssences, _linkAllies)) continue;
                    GUILayout.Label(GearLinkLabel(active, linkIndex++, marks), _st.Small);
                }
            }
            if (!_s.CanEditLoadout)
                GUILayout.Label(Loc.T("遠征中は確保地点に着いてから次の戦闘で敵を倒すまで、装備を変えられます。", "During an expedition, you can change gear from the moment you reach a secure point until you slay an enemy in the next fight.", "远征中，从抵达存仓点起至下一场战斗击败敌人前，可以更换装备。"), _st.Warn);
            GUILayout.Label(Loc.T("同じ系統（破壊・生命・想像）の遺物を2・3・4・6個とそろえるほど、系統のボーナスが重なります。", "Equip 2, 3, 4 or 6 relics of the same line (Destruction, Life, Imagination) for stacking line bonuses.", "集齐2・3・4・6件同系（破坏・生命・想象）的遗物，可叠加系加成。"), _st.Small);
            FocusPicker();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void FocusPicker()
        {
            var p = _s.Profile;
            GUILayout.Label(Loc.T("狙い系統：選んだ系統の遺物が2倍出やすくなります（遠征の前に選びます。「なし」なら今日の夢の系統が出やすくなります）", "Focus: relics of the chosen line drop twice as often (choose before an expedition; with None, today's dream decides)", "目标系：所选系的遗物掉落概率翻倍（远征前选择。选“无”时更容易掉落今日之梦的系）"), _st.Small);
            GUILayout.BeginHorizontal();
            GUI.enabled = p.Run == null || !_s.InGame;
            if (GUILayout.Button(Loc.T("なし", "None", "无"), p.Focus == null ? _st.ButtonSel : _st.Button)) SetFocus(null);
            foreach (Line l in FocusLines)
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
        private int _pickerNamedJa;
        private void HeroPicker()
        {
            if (_s.LocalHero != null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("旅人：", "Traveler:", "旅人："), _st.Small, PickerCaptionWidth);
            _pickerHeroes.Clear();
            _pickerHeroes.AddRange(KnownHeroes);
            foreach (string key in _s.Profile.Heroes.Keys)
                if (key != "default" && !_pickerHeroes.Contains(key)) _pickerHeroes.Add(key);
            int idx = Math.Max(0, _pickerHeroes.IndexOf(HeroKey));
            if (GUILayout.Button("<", _st.Button, PickerArrowWidth))
                _heroSel = _pickerHeroes[(idx - 1 + _pickerHeroes.Count) % _pickerHeroes.Count];
            string selectedHero = HeroKey;
            if (_pickerNamedHero != selectedHero || _pickerNamedJa != Loc.CacheKey)
            {
                _pickerNamedHero = selectedHero;
                _pickerNamedJa = Loc.CacheKey;
                _pickerName.text = "<b>" + HeroName(selectedHero) + "</b>";
            }
            GUILayout.Label(_pickerName, _st.Label, PickerNameWidth);
            if (GUILayout.Button(">", _st.Button, PickerArrowWidth)) _heroSel = _pickerHeroes[(idx + 1) % _pickerHeroes.Count];
            GUILayout.EndHorizontal();
        }
    }
}
