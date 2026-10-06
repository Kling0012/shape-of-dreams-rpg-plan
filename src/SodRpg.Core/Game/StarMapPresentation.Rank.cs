using System;
using System.Globalization;

namespace SodRpg.Core.Game
{
    public static partial class StarMapPresentation
    {
        /// <summary>Append on allocation/language changes; authored descriptions remain the unranked base.</summary>
        public static string DamageRankNote(TalentDef star, int choice, int spent)
        {
            decimal multiplier = StarDamageScaling.Multiplier(spent);
            string factor = multiplier.ToString("0.###", CultureInfo.InvariantCulture);
            string note = Loc.T($"基礎値は上記。星由来のダメージ量だけ現在 ×{factor}（使用済み {spent} 点）。加速・CD・回復・軽減・確率・対象数は変わらない。",
                $"Base values above. Star damage quantities currently ×{factor} ({spent} spent points); haste, cooldowns, healing, reduction, chances and targets are unchanged.");
            if (star == null) return note;
            if (star.IsChoice)
            {
                if (choice < 0 || choice >= star.Choices.Count) return note;
                star = star.Choices[choice];
            }
            if (star.LinkPerRank != null && StarDamageScaling.IsDamage(star.LinkPerRank.Kind))
                return note + Quantity(StarDamageScaling.ScaleMilli(star.LinkPerRank.ValueMilli, spent) / (decimal)BuildPrecision.Scale, true);
            if (star.NativeModifier != null && StarDamageScaling.IsDamage(star.NativeModifier.Kind))
                return note + Quantity(StarDamageScaling.ScaleMilli(Math.Min(star.NativeModifier.Value.ValueMilli,
                    FractionalScopedModifiers.NativeCapValueMilli(star.NativeModifier.CapProfileId)), spent) / (decimal)BuildPrecision.Scale, true);
            if (star.IsPowerNode && StarDamageScaling.IsDamage(star.RankPower))
                return note + Loc.T("\n現在の1段のダメージ係数：", "\nCurrent damage coefficient per rank: ")
                    + ((int)(star.PerRank * multiplier)).ToString(CultureInfo.InvariantCulture) + "%";
            KeystonePayload payload = null;
            try
            {
                if (star.Mechanism != null && (star.Mechanism.Gimmick != null || star.Mechanism.Primed != null
                    || star.Mechanism.Relay != null || star.Mechanism.Bridge != null))
                    payload = AuthoredKeystoneComposer.MechanismPayload(star.Mechanism);
                else if (star.Gimmick != null && StarDamageScaling.IsDamage(star.Gimmick.Effect))
                    payload = AuthoredKeystoneComposer.GimmickPayload(star.Gimmick);
                if (payload != null && (StarDamageScaling.IsDamage(payload.Effect)
                    || payload.Kind == KeystonePayloadKind.MemoryPrimed || payload.Kind == KeystonePayloadKind.RelayWindow
                    || payload.Kind == KeystonePayloadKind.BridgeSuccess))
                    note += Quantity(Math.Min(payload.Value, payload.Caps.Value) * multiplier, false);
            }
            catch (ArgumentException) { /* Non-quantity payloads retain the base description and rank rule. */ }
            return note;
        }

        private static string Quantity(decimal value, bool perRank) => Loc.T(
            perRank ? "\n現在の1段の記憶ダメージ増幅：+" : "\n現在の基礎発動のダメージ係数：",
            perRank ? "\nCurrent memory damage per rank: +" : "\nCurrent base-activation damage coefficient: ")
            + value.ToString("0.###", CultureInfo.InvariantCulture) + "%";
    }
}
