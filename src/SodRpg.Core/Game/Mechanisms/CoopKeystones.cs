using System;
using System.Globalization;

namespace SodRpg.Core.Game
{
    public enum CoopKeystoneKind { SharedSights, ShieldWall, FrostResonance, ShadowCue, ParrySignal, PackGuard, GoldenTrust, ResonantStardust, SharedCrystalField }

    // Coop quantities are ordinary typed keystone settings, carried by the existing codec.
    public static class CoopKeystones
    {
        public static string Id(CoopKeystoneKind kind)
        {
            switch (kind)
            {
                case CoopKeystoneKind.SharedSights: return "lacerta.key.shared-sights";
                case CoopKeystoneKind.ShieldWall: return "vesper.key.shield-wall";
                case CoopKeystoneKind.FrostResonance: return "cetus.key.frost-resonance";
                case CoopKeystoneKind.ShadowCue: return "husk.key.shadow-cue";
                case CoopKeystoneKind.ParrySignal: return "mist.key.parry-signal";
                case CoopKeystoneKind.PackGuard: return "nachia.key.pack-guard";
                case CoopKeystoneKind.GoldenTrust: return "aurena.key.golden-trust";
                case CoopKeystoneKind.ResonantStardust: return "yubar.key.resonant-stardust";
                case CoopKeystoneKind.SharedCrystalField: return "bismuth.key.shared-crystal-field";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static bool TryKind(string id, out CoopKeystoneKind kind)
        {
            for (int i = 0; i <= (int)CoopKeystoneKind.SharedCrystalField; i++)
                if (Id((CoopKeystoneKind)i) == id) { kind = (CoopKeystoneKind)i; return true; }
            kind = default; return false;
        }

        public static float Value(KeystoneDefinition key, string name)
        {
            KeystoneTransform setting = null;
            for (int i = 0; i < key.Upside.Count; i++)
            {
                var t = key.Upside[i];
                if (t.Scope.PayloadKind != KeystonePayloadKind.Coop || t.Scope.TargetEffectIds.Count != 1
                    || t.Scope.TargetEffectIds[0] != name) continue;
                if (setting != null) throw new ArgumentException("Duplicate Coop setting: " + name);
                setting = t;
            }
            if (setting == null || setting.Operation != KeystoneOperation.Set || setting.Seconds <= 0)
                throw new ArgumentException("Missing or invalid Coop setting: " + key.KeystoneId + "/" + name);
            return (float)setting.Seconds;
        }

        public static string Describe(KeystoneDefinition key)
        {
            if (!TryKind(key.KeystoneId, out var kind)) throw new ArgumentException("Unknown Coop keystone.");
            string V(string name) => Value(key, name).ToString("0.##", CultureInfo.InvariantCulture);
            string text;
            switch (kind)
            {
                case CoopKeystoneKind.SharedSights:
                    text = Loc.T($"命中した敵に{V("markSeconds")}秒の印を付け、味方からのダメージを{V("allyDamagePercent")}%増やす。味方が印の敵を倒すと、自分の次の記憶の命中1回のダメージが{V("memoryDamagePercent")}%増える。",
                        $"Hits mark enemies for {V("markSeconds")}s, increasing allied damage by {V("allyDamagePercent")}%. An ally killing a marked enemy empowers your next memory hit by {V("memoryDamagePercent")}%. "); break;
                case CoopKeystoneKind.ShieldWall:
                    text = Loc.T($"{V("radius")}m以内の味方へ自分の防御の{V("armorPercent")}%を加算する。挑発系の記憶を使うと、その味方を狙う敵も自分へ引きつける。",
                        $"Allies within {V("radius")}m gain {V("armorPercent")}% of your armor. Taunting memories also draw enemies targeting those allies to you. "); break;
                case CoopKeystoneKind.FrostResonance:
                    text = Loc.T($"自分が冷気を当てた敵は、味方の冷気以外のダメージを{V("allyDamagePercent")}%多く受ける。味方の命中で冷気・凍結の持続が{V("extendSeconds")}秒延びる。",
                        $"Enemies chilled or frozen by you take {V("allyDamagePercent")}% more non-cold allied damage. Allied hits extend the cold or freeze by {V("extendSeconds")}s. "); break;
                case CoopKeystoneKind.ShadowCue:
                    text = Loc.T($"自分が{V("absenceSeconds")}秒以上当てていない敵への味方の命中で、その敵への次のアイデンティティ攻撃が確定会心になる。移動のチャージを{V("charges")}回復。内部クールダウン{V("cooldownSeconds")}秒。",
                        $"An ally hitting an enemy you have not hit for {V("absenceSeconds")}s makes your next identity hit on it critical and restores {V("charges")} movement charge. Cooldown: {V("cooldownSeconds")}s. "); break;
                case CoopKeystoneKind.ParrySignal:
                    text = Loc.T($"パリィ成功で{V("radius")}m以内の味方の次の命中1回が{V("damagePercent")}%強くなる。有効時間{V("seconds")}秒。",
                        $"A successful parry empowers the next hit of allies within {V("radius")}m by {V("damagePercent")}%, lasting {V("seconds")}s. "); break;
                case CoopKeystoneKind.PackGuard:
                    text = Loc.T($"召喚体はHP割合が最も低い味方を狙う敵を優先する。味方を狙う敵から召喚体がダメージを受けると魔力+{V("powerPercent")}%、{V("seconds")}秒（更新）。",
                        $"Summons prioritize enemies targeting the ally with the lowest health ratio. When such enemies damage your summons, gain {V("powerPercent")}% ability power for {V("seconds")}s (refreshes). "); break;
                case CoopKeystoneKind.GoldenTrust:
                    text = Loc.T($"回復した味方の与ダメージを{V("seconds")}秒間{V("damagePercent")}%増やす。その味方の与ダメージの{V("healPercent")}%で自分を回復する。",
                        $"Allies you heal deal {V("damagePercent")}% more damage for {V("seconds")}s. Their damage heals you for {V("healPercent")}%. "); break;
                case CoopKeystoneKind.ResonantStardust:
                    text = Loc.T($"直近{V("contactSeconds")}秒以内に味方が当てた敵への通常攻撃が{V("damagePercent")}%強くなり、周囲{V("radius")}mへ魔力の{V("powerPercent")}%の星屑ダメージを与える。内部クールダウン{V("cooldownSeconds")}秒。",
                        $"Basic attacks against enemies hit by allies within {V("contactSeconds")}s deal {V("damagePercent")}% more damage and scatter {V("powerPercent")}% ability-power stardust within {V("radius")}m. Cooldown: {V("cooldownSeconds")}s. "); break;
                default:
                    text = Loc.T($"領域内の味方へスキル加速+{V("haste")}、与ダメージ+{V("allyDamagePercent")}%。領域内の味方が当てた敵への自分のダメージが{V("seconds")}秒間{V("damagePercent")}%増える。",
                        $"Allies in your field gain {V("haste")} skill haste and {V("allyDamagePercent")}% damage. Enemies hit by allies in the field take {V("damagePercent")}% more damage from you for {V("seconds")}s. "); break;
            }
            return text + Loc.T($"協力距離は{CoopBalance.FullRange}mまで全量、{CoopBalance.OuterRange}mで0、範囲外は{CoopBalance.GraceSeconds}秒の猶予。ソロでは発動しない。",
                $"Cooperation is full within {CoopBalance.FullRange}m, falls to zero at {CoopBalance.OuterRange}m and has a {CoopBalance.GraceSeconds}s exit grace. Inactive solo.");
        }
    }
}
