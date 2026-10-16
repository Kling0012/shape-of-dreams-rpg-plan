using SodRpg.Core.Game;
using UnityEngine.InputSystem;

namespace SodRpg.Mod
{
    /// <summary>ゲーム内のMOD設定画面に出る設定。保存はゲーム側（QuickSave/Mods）が行う。</summary>
    public class DreamforgeConfig : ModConfig
    {
        [LabelText("Menu key / メニューを開くキー")]
        public Key menuKey = Key.F6;

        [LabelText("Secure key / 確保するキー")]
        public Key secureKey = Key.F7;

        [LabelText("Delve key / 深く潜るキー")]
        public Key delveKey = Key.F8;

        [LabelText("Secure point panel key / 確保地点の画面を隠す・出すキー")]
        public Key securePanelKey = Key.F9;

        [LabelText("Japanese UI / 日本語表示")]
        public bool japanese = true;

        [LabelText("UI scale / 表示の大きさ")]
        public float uiScale = 1f;

        [LabelText("Show drops below Rare / レア未満の拾得通知")]
        public bool showToasts = true;

        [LabelText("Overflow relics: shards + Dream Dust /\n鞄からあふれた遺物で、欠片に加えてドリームダストも受け取る")]
        public bool overflowDreamDust = false;

        [LabelText("Room Omens: a blessing or trial in each combat room (the host's setting applies) /\n部屋の兆し：戦闘の部屋ごとに恵みか試練を付ける（ホストの設定が使われる）")]
        public bool roomOmens = true;

        [LabelText("Co-op-only Room Omens (host) / マルチ専用の兆し（ホスト）")]
        public bool coopRoomOmens = true;

        [LabelText("Melee: convert equipment attack range to attack speed (x" + CoopBalance.RangeToSpeedMultiplierText + ", total attack speed capped at " + TravelerCombat.AttackSpeedCapText + "%; host) /\n"
            + "近接：装備の射程を攻撃速度へ変換（射程1%につき" + CoopBalance.RangeToSpeedMultiplierText + "%、攻撃速度は合計" + TravelerCombat.AttackSpeedCapText + "%まで。ホスト）")]
        public bool meleeRangeToSpeed = true;

        [LabelText("Coop: distance falloff " + CoopBalance.FullRangeText + "-" + CoopBalance.OuterRangeText + "m and " + CoopBalance.GraceSecondsText + "s grace; off: cut beyond " + CoopBalance.FullRangeText + "m (host) /\n"
            + "味方距離効果の減衰（" + CoopBalance.FullRangeText + "～" + CoopBalance.OuterRangeText + "m）と猶予" + CoopBalance.GraceSecondsText + "秒。オフでは" + CoopBalance.FullRangeText + "mを超えると切れる（ホスト）")]
        public bool coopDistanceFalloff = true;

        [LabelText("Melee: Break - allies deal +" + CoopBalance.BreakDamagePercentText + "% damage to the hit enemy for " + CoopBalance.BreakSecondsText + "s (host) /\n"
            + "近接：崩し（当てた敵へ" + CoopBalance.BreakSecondsText + "秒間、ほかの味方の与ダメージ+" + CoopBalance.BreakDamagePercentText + "%。ホスト）")]
        public bool meleeBreak = true;

        [LabelText("Melee: take " + CoopBalance.MeleeGuardReductionPercentText + "% less damage with an enemy within " + CoopBalance.MeleeGuardRangeText + "m (host) /\n"
            + "近接：" + CoopBalance.MeleeGuardRangeText + "m以内に敵がいる間、被ダメージ" + CoopBalance.MeleeGuardReductionPercentText + "%軽減（ホスト）")]
        public bool meleeProximityGuard = true;

        [LabelText("Auto casting: native speed in multiplayer (host, default ON) / 自動詠唱：マルチでは本体と同じ速さ（ホスト、既定ON）")]
        public bool autoCastCoopNativeCadence = true;

        [LabelText("HUD / 左のパネルの表示")]
        public HudMode hudMode = HudMode.Full;

        [LabelText("Background FPS cap (0 = off, 20-60) / ゲームが裏にあるときのFPS上限（0で無効、20〜60）")]
        [UnityEngine.Range(0, 60)]
        public int backgroundFps = 20;

        [LabelText("Lighter rendering / 描画の軽量化（Off：なし／Light：軽め／Strong：強め／Max：最大）")]
        public LightweightMode lightweight = LightweightMode.Off;

        [LabelText("Skip unchanged network state / 変わっていない通信を省く")]
        public bool netSkipUnchanged = true;

        [LabelText("Batch frequent network updates / 頻繁な通信をまとめる")]
        public bool netCoalesce = true;

        [LabelText("Send personal replies only to their owner / 個人への返事は本人にだけ送る")]
        public bool netOwnerOnly = true;

        [LabelText("Level essence slots: add essence slots as the hero levels up (the host's setting applies) /\nレベル連動のエッセンス枠：英雄のレベルに応じて記憶のエッセンス枠を増やす（ホストの設定が使われる）")]
        public bool levelEssenceSlots = true;

        [LabelText("Level essence slots - Q memory / レベル連動の枠：Qの記憶を対象にする")]
        public bool levelEssenceQ = true;

        [LabelText("Level essence slots - W memory / レベル連動の枠：Wの記憶を対象にする")]
        public bool levelEssenceW = true;

        [LabelText("Level essence slots - E memory / レベル連動の枠：Eの記憶を対象にする")]
        public bool levelEssenceE = true;

        [LabelText("Level essence slots - R memory / レベル連動の枠：Rの記憶を対象にする")]
        public bool levelEssenceSlotsR = true;

        [LabelText("Level essence slots - +1 every N hero levels /\nレベル連動の枠：英雄の何レベルごとに +1")]
        [UnityEngine.Range(1, 99)]
        public int levelEssenceEveryLevels = LevelEssenceSettings.DefaultEveryLevels;

        [LabelText("Essence slot cap per memory, including Corrupted Chaos /\n破損した混沌と合わせた1つの記憶のエッセンス枠の上限")]
        [UnityEngine.Range(4, 8)]
        public int levelEssenceCap = LevelEssenceSettings.MinCeiling;

        [LabelText("Smaller packets with supported peers / 対応した相手への通信を小さくする")]
        public bool netCompact = true;
    }

    public enum HudMode
    {
        Full = 0,
        Compact = 1,
        Off = 2,
    }

    public enum LightweightMode
    {
        Off = 0,
        Light = 1,
        Strong = 2,
        Max = 3,
    }
}
