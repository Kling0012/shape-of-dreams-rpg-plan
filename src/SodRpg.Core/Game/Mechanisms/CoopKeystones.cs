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
                    text = Loc.T($"自分が命中した敵に{V("markSeconds")}秒の印を付け、味方からのダメージを{V("allyDamagePercent")}%増やす。味方が印の付いた敵を倒すと、自分の次の記憶の命中1回のダメージが{V("memoryDamagePercent")}%増える。",
                        $"Hits mark enemies for {V("markSeconds")}s, increasing allied damage by {V("allyDamagePercent")}%. An ally killing a marked enemy empowers your next memory hit by {V("memoryDamagePercent")}%. ", $"命中时标记敌人{V("markSeconds")}秒，使其受到的友方伤害增加{V("allyDamagePercent")}%。友方击杀被标记的敌人时，你的下一次记忆命中伤害增加{V("memoryDamagePercent")}%。"); break;
                case CoopKeystoneKind.ShieldWall:
                    text = Loc.T($"{V("radius")}m以内の味方に自分の防御の{V("armorPercent")}%を加える。挑発系の記憶を使うと、その味方を狙う敵も自分へ引きつける。",
                        $"Allies within {V("radius")}m gain {V("armorPercent")}% of your armor. Taunting memories also draw enemies targeting those allies to you. ", $"{V("radius")}米内的友方获得相当于你护甲{V("armorPercent")}%的护甲。嘲讽记忆也会将以这些友方为目标的敌人引向你。"); break;
                case CoopKeystoneKind.FrostResonance:
                    text = Loc.T($"自分が冷気を当てた敵は、味方の冷気以外のダメージを{V("allyDamagePercent")}%多く受ける。味方の命中で冷気・凍結の持続が{V("extendSeconds")}秒延びる。",
                        $"Enemies chilled or frozen by you take {V("allyDamagePercent")}% more non-cold allied damage. Allied hits extend the cold or freeze by {V("extendSeconds")}s. ", $"被你施加寒冷或冻结的敌人受到的友方非寒冷伤害增加{V("allyDamagePercent")}%。友方命中会使寒冷或冻结延长{V("extendSeconds")}秒。"); break;
                case CoopKeystoneKind.ShadowCue:
                    text = Loc.T($"自分が{V("absenceSeconds")}秒以上当てていない敵に味方が命中すると、その敵への次のアイデンティティ攻撃が確定会心になる。移動のチャージを{V("charges")}回復。内部クールダウン{V("cooldownSeconds")}秒。",
                        $"An ally hitting an enemy you have not hit for {V("absenceSeconds")}s makes your next identity hit on it critical and restores {V("charges")} movement charge. Cooldown: {V("cooldownSeconds")}s. ", $"友方命中你至少{V("absenceSeconds")}秒未命中的敌人时，你对其下一次专属攻击必定暴击，并恢复{V("charges")}次移动充能。冷却：{V("cooldownSeconds")}秒。"); break;
                case CoopKeystoneKind.ParrySignal:
                    text = Loc.T($"パリィ成功で{V("radius")}m以内の味方の次の命中1回が{V("damagePercent")}%強くなる。有効時間{V("seconds")}秒。",
                        $"A successful parry empowers the next hit of allies within {V("radius")}m by {V("damagePercent")}%, lasting {V("seconds")}s. ", $"成功格挡后，{V("radius")}米内友方的下一次命中伤害增加{V("damagePercent")}%，持续{V("seconds")}秒。"); break;
                case CoopKeystoneKind.PackGuard:
                    text = Loc.T($"召喚体はHP割合が最も低い味方を狙う敵を優先する。味方を狙う敵から召喚体がダメージを受けると、{V("seconds")}秒間魔力が{V("powerPercent")}%増える（更新）。",
                        $"Summons prioritize enemies targeting the ally with the lowest health ratio. When such enemies damage your summons, gain {V("powerPercent")}% ability power for {V("seconds")}s (refreshes). ", $"召唤物优先攻击以生命比例最低的友方为目标的敌人。当这些敌人伤害你的召唤物时，法术强度增加{V("powerPercent")}%，持续{V("seconds")}秒（刷新持续时间）。"); break;
                case CoopKeystoneKind.GoldenTrust:
                    text = Loc.T($"自分が回復した味方の与ダメージを{V("seconds")}秒間{V("damagePercent")}%増やす。その味方の与ダメージの{V("healPercent")}%で自分を回復する。",
                        $"Allies you heal deal {V("damagePercent")}% more damage for {V("seconds")}s. Their damage heals you for {V("healPercent")}%. ", $"被你治疗的友方造成的伤害增加{V("damagePercent")}%，持续{V("seconds")}秒。他们造成伤害的{V("healPercent")}%用于治疗你。"); break;
                case CoopKeystoneKind.ResonantStardust:
                    text = Loc.T($"直近{V("contactSeconds")}秒以内に味方が当てた敵への通常攻撃が{V("damagePercent")}%強くなり、周囲{V("radius")}mへ魔力の{V("powerPercent")}%の星屑ダメージを与える。内部クールダウン{V("cooldownSeconds")}秒。",
                        $"Basic attacks against enemies hit by allies within {V("contactSeconds")}s deal {V("damagePercent")}% more damage and scatter {V("powerPercent")}% ability-power stardust within {V("radius")}m. Cooldown: {V("cooldownSeconds")}s. ", $"通常攻击命中最近{V("contactSeconds")}秒内被友方命中的敌人时，伤害增加{V("damagePercent")}%，并散射相当于法术强度{V("powerPercent")}%的星屑伤害，范围为{V("radius")}米内。冷却：{V("cooldownSeconds")}秒。"); break;
                default:
                    text = Loc.T($"領域内の味方へスキル加速+{V("haste")}、与ダメージ+{V("allyDamagePercent")}%。領域内の味方が当てた敵への自分のダメージが{V("seconds")}秒間{V("damagePercent")}%増える。",
                        $"Allies in your field gain {V("haste")} skill haste and {V("allyDamagePercent")}% damage. Enemies hit by allies in the field take {V("damagePercent")}% more damage from you for {V("seconds")}s. ", $"领域内的友方获得{V("haste")}技能急速和{V("allyDamagePercent")}%伤害加成。你对被领域内友方命中的敌人造成的伤害增加{V("damagePercent")}%，持续{V("seconds")}秒。"); break;
            }
            return text + Loc.T($"協力距離は{CoopBalance.FullRange}mまで全量、{CoopBalance.OuterRange}mで0、範囲外は{CoopBalance.GraceSeconds}秒の猶予。ソロでは発動しない。",
                $"Cooperation is full within {CoopBalance.FullRange}m, falls to zero at {CoopBalance.OuterRange}m and has a {CoopBalance.GraceSeconds}s exit grace. Inactive solo.", $"协作效果在{CoopBalance.FullRange}米内全额生效，在{CoopBalance.OuterRange}米处降至零，离开范围后有{CoopBalance.GraceSeconds}秒宽限期。单人时不生效。");
        }
    }
}
