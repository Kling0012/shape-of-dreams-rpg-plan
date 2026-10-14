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
        Vitality, Alacrity, Feebleness, Dulling,
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
        public static bool IsTrial(RoomOmen omen) => Array.IndexOf(Trials, omen) >= 0;
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
            string name, effect;
            switch (omen)
            {
                case RoomOmen.Vigor: name = Loc.T("鋭気", "Vigor"); effect = Loc.T("攻撃力・魔力", "attack & power"); break;
                case RoomOmen.Swiftness: name = Loc.T("疾駆", "Swiftness"); effect = Loc.T("移動速度", "movement speed"); break;
                case RoomOmen.Focus: name = Loc.T("集中", "Focus"); effect = Loc.T("スキル加速", "skill haste"); break;
                case RoomOmen.Fortitude: name = Loc.T("堅牢", "Fortitude"); effect = Loc.T("防御", "armor"); break;
                case RoomOmen.Healing: name = Loc.T("癒し", "Healing"); effect = Loc.T("受ける回復", "healing received"); break;
                case RoomOmen.Fortune: name = Loc.T("幸運", "Fortune"); effect = Loc.T("遺物ドロップ率", "relic drop chance"); break;
                case RoomOmen.Sluggishness: name = Loc.T("鈍重", "Sluggishness"); effect = Loc.T("移動速度", "movement speed"); break;
                case RoomOmen.Fragility: name = Loc.T("脆弱", "Fragility"); effect = Loc.T("防御", "armor"); break;
                case RoomOmen.Weariness: name = Loc.T("倦怠", "Weariness"); effect = Loc.T("攻撃速度", "attack speed"); break;
                case RoomOmen.Drought: name = Loc.T("枯渇", "Drought"); effect = Loc.T("受ける回復", "healing received"); break;
                case RoomOmen.RestlessEnemies: name = Loc.T("昂る敵", "Restless enemies"); effect = Loc.T("敵の移動速度", "enemy movement speed"); break;
                case RoomOmen.SturdyEnemies: name = Loc.T("頑強な敵", "Sturdy enemies"); effect = Loc.T("敵の最大HP", "enemy max HP"); break;
                case RoomOmen.Vitality: name = Loc.T("活力", "Vitality"); effect = Loc.T("最大HP", "max HP"); break;
                case RoomOmen.Alacrity: name = Loc.T("俊敏", "Alacrity"); effect = Loc.T("攻撃速度", "attack speed"); break;
                case RoomOmen.Feebleness: name = Loc.T("虚弱", "Feebleness"); effect = Loc.T("最大HP", "max HP"); break;
                case RoomOmen.Dulling: name = Loc.T("鈍化", "Dulling"); effect = Loc.T("スキル加速", "skill haste"); break;
                default: return null;
            }
            double value = Value(omen);
            string units = omen == RoomOmen.Focus || omen == RoomOmen.Fortitude || omen == RoomOmen.Fragility || omen == RoomOmen.Dulling ? "" : "%";
            string text = Loc.T("部屋の兆し：", "Room Omen: ") + name + Loc.T("（", " (") + effect
                + (value >= 0 ? "+" : "−") + Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture) + units + Loc.T("）", ")");
            if (IsTrial(omen)) text += Loc.T("・遺物+", " · relics +")
                + (RoomOmenBalance.TrialDropBonus * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
            return text;
        }
    }
}
