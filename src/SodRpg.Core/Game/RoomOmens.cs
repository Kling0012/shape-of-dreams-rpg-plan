using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SodRpg.Core.Game
{
    public enum RoomOmen
    {
        None, Vigor, Swiftness, Focus, Fortitude, Healing, Fortune,
        Sluggishness, Fragility, Weariness, Drought, RestlessEnemies, SturdyEnemies,
        // 追加分は番号を後ろへ足す（ネットワークで送る番号を変えないため）。
        Vitality, Alacrity, Feebleness, Dulling, ResonantWard,
    }

    public static class RoomOmens
    {
        private const string DropMarker = "|omenDrop:";
        private sealed class KillModifier { internal RoomOmen Omen; }
        private static readonly ConditionalWeakTable<object, KillModifier> KillModifiers = new ConditionalWeakTable<object, KillModifier>();
        public static bool Enabled { get; set; } = true;
        public static void SetKillModifier(object owner, RoomOmen omen)
        {
            if (omen == RoomOmen.None) KillModifiers.Remove(owner);
            else KillModifiers.GetValue(owner, _ => new KillModifier()).Omen = omen;
        }
        public static string EncodeKillEventId(object owner, string id) => Enabled && KillModifiers.TryGetValue(owner, out var modifier)
            ? EncodeKillEventId(id, modifier.Omen) : id;
        private static readonly RoomOmen[] Blessings =
        {
            RoomOmen.Vigor, RoomOmen.Swiftness, RoomOmen.Focus, RoomOmen.Fortitude, RoomOmen.Healing, RoomOmen.Fortune,
            RoomOmen.Vitality, RoomOmen.Alacrity,
        };
        private static readonly RoomOmen[] Trials =
        {
            RoomOmen.Sluggishness, RoomOmen.Fragility, RoomOmen.Weariness, RoomOmen.Drought,
            RoomOmen.RestlessEnemies, RoomOmen.SturdyEnemies, RoomOmen.Feebleness, RoomOmen.Dulling,
        };
        public static bool IsTrial(RoomOmen omen) => omen == RoomOmen.ResonantWard || Array.IndexOf(Trials, omen) >= 0;
        public static bool IsValid(RoomOmen omen) => omen == RoomOmen.None || Array.IndexOf(Blessings, omen) >= 0 || IsTrial(omen);
        public static double TrialScale(string key) => RoomOmenBalance.TrialScale(key);

        // Replay the small room sequence without touching the profile's loot RNG or save format.
        public static RoomOmen Roll(ulong seed, int roomNumber)
        {
            if (roomNumber < 0) return RoomOmen.None;
            var rng = new Rng(seed ^ 0x726f6f6d6f6d656eUL);
            var previous = RoomOmen.None;
            for (int room = 0; room <= roomNumber; room++)
            {
                var pool = rng.Chance(RoomOmenBalance.BlessingChance) ? Blessings : Trials;
                int excluded = Array.IndexOf(pool, previous);
                int index = rng.Range(0, excluded >= 0 ? pool.Length - 2 : pool.Length - 1);
                if (excluded >= 0 && index >= excluded) index++;
                previous = pool[index];
                if (room == roomNumber) break;
            }
            return previous;
        }

        // A separate stream leaves Roll's released pools and every solo room unchanged.
        public static RoomOmen RollCoop(ulong seed, int roomNumber, int humanPlayers, bool enabled = true)
        {
            if (!enabled || humanPlayers < 2 || roomNumber < 0) return RoomOmen.None;
            var rng = new Rng(seed ^ 0x636f6f706f6d656eUL ^ (ulong)roomNumber);
            return rng.Chance(RoomOmenBalance.CoopOmenChance) ? RoomOmen.ResonantWard : RoomOmen.None;
        }

        public static double Value(RoomOmen omen)
        {
            switch (omen)
            {
                case RoomOmen.Vigor: return RoomOmenBalance.Vigor;
                case RoomOmen.Swiftness: return RoomOmenBalance.Swiftness;
                case RoomOmen.Focus: return RoomOmenBalance.Focus;
                case RoomOmen.Fortitude: return RoomOmenBalance.Fortitude;
                case RoomOmen.Healing: return RoomOmenBalance.Healing;
                case RoomOmen.Fortune: return RoomOmenBalance.Fortune;
                case RoomOmen.Sluggishness: return RoomOmenBalance.Sluggishness;
                case RoomOmen.Fragility: return RoomOmenBalance.Fragility;
                case RoomOmen.Weariness: return RoomOmenBalance.Weariness;
                case RoomOmen.Drought: return RoomOmenBalance.Drought;
                case RoomOmen.RestlessEnemies: return RoomOmenBalance.RestlessEnemies;
                case RoomOmen.SturdyEnemies: return RoomOmenBalance.SturdyEnemies;
                case RoomOmen.Vitality: return RoomOmenBalance.Vitality;
                case RoomOmen.Alacrity: return RoomOmenBalance.Alacrity;
                case RoomOmen.Feebleness: return RoomOmenBalance.Feebleness;
                case RoomOmen.Dulling: return RoomOmenBalance.Dulling;
                default: return 0;
            }
        }

        public static double DropBonus(RoomOmen omen) => IsTrial(omen) ? RoomOmenBalance.TrialDropBonus
            : omen == RoomOmen.Fortune ? Value(omen) / 100 : 0;

        // Freeze only the reward modifier in the existing opaque death identity, including delayed settlement.
        // Pressure/shard suffixes are appended afterwards to preserve released readers.
        public static string EncodeKillEventId(string id, RoomOmen omen) => DropBonus(omen) > 0
            ? id + DropMarker + DropBonus(omen).ToString("0.######", CultureInfo.InvariantCulture) + "|" : id;

        public static double DropBonusFromEventId(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            int marker = id.IndexOf(DropMarker, StringComparison.Ordinal);
            if (marker < 0) return 0;
            int start = marker + DropMarker.Length, end = id.IndexOf('|', start);
            return end > start && end - start <= 10
                && double.TryParse(id.Substring(start, end - start), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out double value) && value >= 0 && value <= 1 ? value : 0;
        }

        public static string Describe(RoomOmen omen)
        {
            if (omen == RoomOmen.ResonantWard)
                return Loc.T("部屋の兆し：共鳴の盾（2 人以上で同時に攻撃すると盾が割れる）・遺物+",
                    "Room Omen: Resonant Ward (Two or more players attacking together break the ward) · relics +", "房间预兆：共鸣之盾（2人以上同时攻击会打破护盾）·遗物+")
                    + (RoomOmenBalance.TrialDropBonus * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
            string name, effect;
            switch (omen)
            {
                case RoomOmen.Vigor: name = Loc.T("鋭気", "Vigor", "锐气"); effect = Loc.T("攻撃力・魔力", "attack & power", "攻击力与法术强度"); break;
                case RoomOmen.Swiftness: name = Loc.T("疾駆", "Swiftness", "疾行"); effect = Loc.T("移動速度", "movement speed", "移动速度"); break;
                case RoomOmen.Focus: name = Loc.T("集中", "Focus", "专注"); effect = Loc.T("スキル加速", "skill haste", "技能加速"); break;
                case RoomOmen.Fortitude: name = Loc.T("堅牢", "Fortitude", "坚牢"); effect = Loc.T("防御", "armor", "护甲"); break;
                case RoomOmen.Healing: name = Loc.T("癒し", "Healing", "治愈"); effect = Loc.T("受ける回復", "healing received", "受到的治疗"); break;
                case RoomOmen.Fortune: name = Loc.T("幸運", "Fortune", "幸运"); effect = Loc.T("遺物ドロップ率", "relic drop chance", "遗物掉落率"); break;
                case RoomOmen.Sluggishness: name = Loc.T("鈍重", "Sluggishness", "迟重"); effect = Loc.T("移動速度", "movement speed", "移动速度"); break;
                case RoomOmen.Fragility: name = Loc.T("脆弱", "Fragility", "脆弱"); effect = Loc.T("防御", "armor", "护甲"); break;
                case RoomOmen.Weariness: name = Loc.T("倦怠", "Weariness", "倦怠"); effect = Loc.T("攻撃速度", "attack speed", "攻击速度"); break;
                case RoomOmen.Drought: name = Loc.T("枯渇", "Drought", "枯竭"); effect = Loc.T("受ける回復", "healing received", "受到的治疗"); break;
                case RoomOmen.RestlessEnemies: name = Loc.T("昂る敵", "Restless enemies", "躁动之敌"); effect = Loc.T("敵の移動速度", "enemy movement speed", "敌人的移动速度"); break;
                case RoomOmen.SturdyEnemies: name = Loc.T("頑強な敵", "Sturdy enemies", "顽抗之敌"); effect = Loc.T("敵の最大HP", "enemy max HP", "敌人的最大生命"); break;
                case RoomOmen.Vitality: name = Loc.T("活力", "Vitality", "活力"); effect = Loc.T("最大HP", "max HP", "最大生命"); break;
                case RoomOmen.Alacrity: name = Loc.T("俊敏", "Alacrity", "敏捷"); effect = Loc.T("攻撃速度", "attack speed", "攻击速度"); break;
                case RoomOmen.Feebleness: name = Loc.T("虚弱", "Feebleness", "虚弱"); effect = Loc.T("最大HP", "max HP", "最大生命"); break;
                case RoomOmen.Dulling: name = Loc.T("鈍化", "Dulling", "钝化"); effect = Loc.T("スキル加速", "skill haste", "技能加速"); break;
                default: return null;
            }
            double value = Value(omen);
            string units = omen == RoomOmen.Focus || omen == RoomOmen.Fortitude || omen == RoomOmen.Fragility || omen == RoomOmen.Dulling ? "" : "%";
            string text = Loc.T("部屋の兆し：", "Room Omen: ", "房间预兆：") + name + Loc.T("（", " (", "（") + effect
                + (value >= 0 ? "+" : "−") + Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture) + units + Loc.T("）", ")", "）");
            if (IsTrial(omen)) text += Loc.T("・遺物+", " · relics +", "·遗物+")
                + (RoomOmenBalance.TrialDropBonus * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
            return text;
        }
    }
}
