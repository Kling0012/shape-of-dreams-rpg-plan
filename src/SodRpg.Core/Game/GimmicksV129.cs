using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    public static partial class Gimmicks
    {
        /// <summary>高揚（Crescendo）の効果時間：最低8秒か、記憶のクールダウンの1.5倍の長い方（補正前の基準）。</summary>
        public const float CrescendoMinimumSeconds = 8f;
        public const float CrescendoCooldownFactor = 1.5f;
        public static float CrescendoDurationBase(float cooldown) =>
            Math.Max(CrescendoMinimumSeconds, Finite(cooldown) && cooldown > 0 ? cooldown * CrescendoCooldownFactor : 0f);

        public static bool IsV129(GimmickEffect effect) => effect >= GimmickEffect.Wound && effect <= GimmickEffect.Weakspot;
        public static float MinimumCooldown(GimmickEffect effect) => effect == GimmickEffect.Ricochet ? 0.3f
            : effect == GimmickEffect.Siphon ? 0.5f : 0f;

        public static bool AllowedOnMemory(GimmickEffect effect, string memory)
        {
            if (!Links.IsMemory(memory)) return false;
            if (!IsV129(effect)) return true;
            if (memory.StartsWith("St_M_", StringComparison.Ordinal)) return false;
            if (effect == GimmickEffect.Daze && (memory == "St_Q_CruelSun" || memory == "St_Q_BigBorealChunk")) return false;
            if (effect == GimmickEffect.Primed && (memory == "St_D_PrismaticEyes" || memory.StartsWith("St_QR_", StringComparison.Ordinal))) return false;
            return true;
        }

        private static bool ValidV129Def(GimmickDef def)
        {
            bool hit = def.Trigger == GimmickTrigger.OnHit, crit = def.Trigger == GimmickTrigger.OnCrit;
            bool kill = def.Trigger == GimmickTrigger.OnKill, use = def.Trigger == GimmickTrigger.OnUse;
            if (def.Effect == GimmickEffect.Ricochet) return def.Arg >= 1 && def.Arg <= 2 && (hit || crit || kill);
            if (def.Effect == GimmickEffect.Siphon) return def.Arg >= 0 && def.Arg <= 1 && (hit || crit || kill);
            if (def.Arg != 0) return false;
            switch (def.Effect)
            {
                case GimmickEffect.Wound:
                case GimmickEffect.Daze:
                case GimmickEffect.ElementEdge: return hit || crit;
                case GimmickEffect.Rampart:
                case GimmickEffect.Crescendo:
                case GimmickEffect.Sap:
                case GimmickEffect.Weakspot: return hit;
                case GimmickEffect.Primed: return use || kill;
                case GimmickEffect.PackMend: return use || hit || kill;
                default: return false;
            }
        }

        private static string DescribeV129(GimmickDef def, string memory, int ranks, string triggerText = null)
        {
            decimal value = Math.Min(def.Value * ranks, Cap(def.Effect));
            string n = value.ToString("0.#######", CultureInfo.InvariantCulture), text;
            string duration = Duration(def, BuffDuration).ToString("0.#######", CultureInfo.InvariantCulture);
            string woundDuration = Duration(def, 3f).ToString("0.#######", CultureInfo.InvariantCulture);
            string primedDuration = Duration(def, 5f).ToString("0.#######", CultureInfo.InvariantCulture);
            string ricochetRadius = Radius(def, 8f).ToString("0.#######", CultureInfo.InvariantCulture);
            string allyRadius = Radius(def, CoopBalance.FullRange).ToString("0.#######", CultureInfo.InvariantCulture);
            int targets = TargetLimit(def);
            string maximumStun = Duration(def, Cap(GimmickEffect.Daze) / 10f).ToString("0.#######", CultureInfo.InvariantCulture);
            if (Loc.Japanese)
                return DescribeV129Ja(def, memory, n, duration, woundDuration, primedDuration, ricochetRadius, allyRadius, targets, maximumStun,
                    Duration(def, (float)value / 10f).ToString("0.#######", CultureInfo.InvariantCulture), triggerText);
            switch (def.Effect)
            {
                case GimmickEffect.Wound:
                    text = "Damage over time +" + n + "% of the higher of attack damage or ability power per 3 seconds: damage the hit enemy for " + woundDuration + " seconds (does not stack; refreshes the stronger wound)"; break;
                case GimmickEffect.Daze:
                    string seconds = Duration(def, (float)value / 10f).ToString("0.#######", CultureInfo.InvariantCulture);
                    text = "Stun +" + seconds + " seconds: affects the hit enemy (ineffective against minibosses and bosses; once per enemy every 5 seconds)"; break;
                case GimmickEffect.Ricochet:
                    text = "Ricochet damage +" + n + "% of the hit damage: damage up to " + targets + " other enemies within " + ricochetRadius + "m"; break;
                case GimmickEffect.Siphon:
                    text = "Healing +" + n + "% of that memory's direct damage: heal yourself (excluding damage over time; at most 1.5% of your maximum health per heal)"
                        + (def.Arg == 1 ? "; allied travelers within " + allyRadius + "m receive half as much, capped at 1.5% of their own maximum health" + (Radius(def, CoopBalance.FullRange) == CoopBalance.FullRange ? CoopDistance.Description : "") : ""); break;
                case GimmickEffect.Rampart:
                    text = "Shield +" + n + "% of maximum health per distinct enemy hit by this cast: lasts " + duration + " seconds (up to " + targets + " enemies and " + Cap(def.Effect) + "% per enemy; one pool separate from ordinary star shields, keeping the larger remaining or new amount and refreshing duration)"; break;
                case GimmickEffect.Primed:
                    text = "Next basic attack extra damage +" + n + "% of the higher of attack damage or ability power (does not stack; expires after " + primedDuration + " seconds; consumes only the largest next-basic-attack bonus and leaves the others ready)"; break;
                case GimmickEffect.Crescendo:
                    string minimum = Duration(def, CrescendoMinimumSeconds).ToString("0.#######", CultureInfo.InvariantCulture);
                    string factor = Duration(def, CrescendoCooldownFactor).ToString("0.#######", CultureInfo.InvariantCulture);
                    text = "Memory damage +" + n + "%: affects that memory (once per cast, up to 5 stacks and 40%; lasts the longer of " + minimum + " seconds or " + factor + " times that memory's cooldown; retained when using other memories; combined memory damage bonuses are capped at 120%)"; break;
                case GimmickEffect.ElementEdge:
                    text = "Extra damage +" + n + "% of the higher of attack damage or ability power per element: count Fire, Cold, Light and Dark on the hit enemy (up to 4 types; does not consume elements)"; break;
                case GimmickEffect.PackMend:
                    text = "Summon healing +" + n + "% of each summon’s maximum health: affects all your living summons (does not heal travelers)"; break;
                case GimmickEffect.Sap:
                    text = "Enemy damage dealt -" + n + "%: applies to the hit enemy for " + duration + " seconds (halved for minibosses and bosses; does not stack; refreshes the stronger effect)"; break;
                default:
                    text = "Critical chance +" + n + " percentage points: applies to your basic attacks and memories against the hit enemy for " + duration + " seconds (does not stack; refreshes the stronger effect; guaranteed critical hits remain unchanged)"; break;
            }
            string trigger = def.Trigger == GimmickTrigger.OnUse ? " is used"
                : def.Trigger == GimmickTrigger.OnKill ? " kills an enemy"
                : def.Trigger == GimmickTrigger.OnCrit ? " critically hits"
                : " hits";
            float cd = Math.Max(MinimumCooldown(def.Effect), Math.Min(def.Cooldown, MaxCooldown));
            string interval = cd > 0 ? "once every " + cd.ToString("0.#######", CultureInfo.InvariantCulture) + " seconds per star"
                : "no cooldown";
            string cap = def.Effect == GimmickEffect.Daze ? "stun capped at " + maximumStun + " seconds"
                : "effect capped at " + Cap(def.Effect) + "%";
            return text + ". " + (triggerText ?? ("Triggered when " + memory + trigger))
                + " (" + interval + "; " + cap + "; not triggered by extra damage from stars).";
        }

        /// <summary>V1.29 の仕掛けの日本語の説明。組み立ては <see cref="DescribeJa"/> と同じ（条件→結果、限度、重なり、最後の一文）。</summary>
        private static string DescribeV129Ja(GimmickDef def, string memory, string n, string duration, string woundDuration, string primedDuration,
            string ricochetRadius, string allyRadius, int targets, string maximumStun, string stunSeconds, string triggerText)
        {
            string condition = JaCondition(def, memory, triggerText);
            string enemy = JaHitEnemy(condition);
            string effect, extra = "", cap = "効果量は最大" + Cap(def.Effect) + "%まで";
            switch (def.Effect)
            {
                case GimmickEffect.Wound:
                    effect = enemy + "に、3秒ごとに攻撃力・魔力の高い方の" + n + "%となる継続ダメージを" + woundDuration + "秒間与える";
                    extra = "重ならず、強い方の継続ダメージが残って時間が延びる。";
                    break;
                case GimmickEffect.Daze:
                    effect = enemy + "を" + stunSeconds + "秒間スタンさせる";
                    extra = "ミニボス・ボスには効かず、同じ敵には5秒に1回まで。";
                    cap = "スタンは最大" + maximumStun + "秒まで";
                    break;
                case GimmickEffect.Ricochet:
                    effect = "当てたダメージの" + n + "%を、" + ricochetRadius + "m以内の別の敵" + targets + "体までに跳弾ダメージとして与える";
                    break;
                case GimmickEffect.Siphon:
                    effect = def.Arg == 1 ? "その記憶の直接ダメージの" + n + "%分、自分を回復し、" + allyRadius + "m以内の味方旅人にはその半分を回復する"
                        : "その記憶の直接ダメージの" + n + "%分、自分を回復する";
                    extra = "継続ダメージは対象外で、1回の回復量は" + (def.Arg == 1 ? "各自" : "自分") + "の最大HPの1.5%まで。"
                        + (def.Arg == 1 && Radius(def, CoopBalance.FullRange) == CoopBalance.FullRange ? CoopDistance.Description : "");
                    break;
                case GimmickEffect.Rampart:
                    effect = "同じ発動で命中した別々の敵1体につき、最大HPの" + n + "%の障壁を" + duration + "秒間得る";
                    extra = "数えるのは" + targets + "体までで、1体につき最大" + Cap(def.Effect) + "%まで。この障壁は通常の星の障壁とは別に1つだけで、残量と新しい量の大きい方を保ち、持続時間を更新する。";
                    break;
                case GimmickEffect.Primed:
                    effect = "次の通常攻撃に、攻撃力・魔力の高い方の" + n + "%の追加ダメージを乗せる";
                    extra = "重ならず、" + primedDuration + "秒で消える。次の通常攻撃への上乗せが複数あるときは、最大の1つだけを使い、残りは消費しない。";
                    break;
                case GimmickEffect.Crescendo:
                    string minimum = Duration(def, CrescendoMinimumSeconds).ToString("0.#######", CultureInfo.InvariantCulture);
                    string factor = Duration(def, CrescendoCooldownFactor).ToString("0.#######", CultureInfo.InvariantCulture);
                    effect = "その記憶のダメージが" + n + "%増える";
                    extra = "同じ発動では1回だけ増え、重ねられるのは5回まで、最大40%まで。効果は" + minimum + "秒か、その記憶のクールダウンの" + factor
                        + "倍のうち長い方だけ続き、ほかの記憶を使っても保たれる。記憶ダメージを増やすほかの効果との合計は最大120%まで。";
                    break;
                case GimmickEffect.ElementEdge:
                    effect = enemy + "に付いている火・冷気・光・闇の種類を数え、1種類につき攻撃力・魔力の高い方の" + n + "%の追加ダメージを与える";
                    extra = "数えるのは4種類までで、属性は消費しない。";
                    break;
                case GimmickEffect.PackMend:
                    effect = "自分の生存している召喚獣すべてを、それぞれの最大HPの" + n + "%回復する";
                    extra = "旅人は回復しない。";
                    break;
                case GimmickEffect.Sap:
                    effect = enemy + "の与ダメージを" + duration + "秒間" + n + "%下げる";
                    extra = "ミニボス・ボスでは効果が半分になる。重ならず、強い方の効果が残って時間が延びる。";
                    break;
                default:
                    effect = enemy + "に対する自分の通常攻撃と記憶の会心率が、" + duration + "秒間" + n + "%上がる";
                    extra = "重ならず、強い方の効果が残って時間が延びる。確定会心は変わらない。";
                    break;
            }
            float cd = Math.Max(MinimumCooldown(def.Effect), def.Cooldown);
            return JaSentence(condition, effect) + extra + JaLimits(cd, cap) + JaNoStarTrigger;
        }
        public static float SiphonHeal(float damage, float maximumHealth, float percent)
        {
            if (!Finite(damage) || !Finite(maximumHealth) || !Finite(percent) || damage <= 0 || maximumHealth <= 0 || percent <= 0) return 0;
            return Math.Min(damage * Math.Min(Cap(GimmickEffect.Siphon), percent) / 100f, maximumHealth * 0.015f);
        }

        public static float AddedCritProbability(float currentChance, float percent)
        {
            if (!Finite(currentChance) || !Finite(percent) || percent <= 0 || currentChance >= 1f) return 0f;
            return Math.Min(1f, Math.Min(Cap(GimmickEffect.Weakspot), percent) / 100f / (1f - Math.Max(0f, currentChance)));
        }

        public static int ElementEdgePercent(int value, bool fire, bool cold, bool light, bool dark) =>
            Math.Max(0, Math.Min((int)decimal.Ceiling(StarDamageScaling.EffectCeiling(GimmickEffect.ElementEdge)), value)) * ((fire ? 1 : 0) + (cold ? 1 : 0) + (light ? 1 : 0) + (dark ? 1 : 0));

        public static float ElementEdgePercent(float value, bool fire, bool cold, bool light, bool dark) => !Finite(value) ? 0f :
            Math.Max(0, Math.Min((float)StarDamageScaling.EffectCeiling(GimmickEffect.ElementEdge), value)) * ((fire ? 1 : 0) + (cold ? 1 : 0) + (light ? 1 : 0) + (dark ? 1 : 0));
    }

    public sealed partial class GimmickRuntime
    {
        private readonly Dictionary<int, float> _dazeUntil = new Dictionary<int, float>();

        private bool AcceptV129(ActiveEntry state, float now, int victimId, float damage, long cast, float cooldown,
            bool direct, bool boss, out int targetCount)
        {
            targetCount = 0;
            var effect = state.Entry.Def.Effect;
            if (!Gimmicks.IsV129(effect)) return true;
            if (effect != GimmickEffect.Primed && effect != GimmickEffect.PackMend && victimId == 0) return false;
            if ((effect == GimmickEffect.Ricochet || effect == GimmickEffect.Siphon) && (!Gimmicks.Finite(damage) || damage <= 0)) return false;
            if (effect == GimmickEffect.Siphon && !direct) return false;
            if (effect == GimmickEffect.Daze)
            {
                if (boss || _dazeUntil.TryGetValue(victimId, out float until) && now < until) return false;
                _dazeUntil[victimId] = now + 5f;
            }
            if (effect == GimmickEffect.Crescendo || effect == GimmickEffect.Rampart)
            {
                // Never guess cast identity from frame timing: hosts without attribution cannot trigger these effects.
                if (cast == 0) return false;
                if (effect == GimmickEffect.Rampart)
                {
                    if (!state.CastVictims.TryGetValue(cast, out var victims)) state.CastVictims[cast] = victims = new HashSet<int>();
                    if (victims.Count >= Gimmicks.TargetLimit(state.Entry.Def) || !victims.Add(victimId)) return false;
                    targetCount = victims.Count;
                }
                else
                {
                    if (state.SeenCasts.ContainsKey(cast)) return false;
                    state.Stacks = state.BuffActive && now < state.BuffUntil ? Math.Min(5, state.Stacks + 1) : 1;
                    state.BuffActive = true;
                    state.BuffUntil = now + Gimmicks.Duration(state.Entry.Def, Gimmicks.CrescendoDurationBase(cooldown));
                }
                state.SeenCasts[cast] = now;
            }
            return true;
        }

        public float CrescendoPercent(string memory, float now)
        {
            if (!_hasCrescendoEntries || !Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            decimal result = 0;
            foreach (var state in ActiveStates())
                if (state.Entry.Memory == memory && state.Entry.Def.Effect == GimmickEffect.Crescendo && state.BuffActive)
                    result = Math.Max(result, Math.Min(40m, state.Stacks * state.Entry.Def.EffectiveValueOrAuthored));
            return (float)result;
        }

        public float CombinedMemoryDamagePercent(string memory, float now, float otherPercent, float starPercent = 0f)
        {
            float crescendo = CrescendoPercent(memory, now);
            float stars = Math.Max(0, starPercent);
            // Only equipment/Crescendo belongs to the historical 120% aggregate cap.
            return crescendo == 0 ? Math.Max(0, otherPercent)
                : stars + Math.Min(120f, Math.Max(0, otherPercent - stars) + crescendo);
        }

        public float WeakspotPercent(int victimId, float now) => TargetPercent(GimmickEffect.Weakspot, victimId, now);
        public float SapPercent(int victimId, float now, bool boss) => TargetPercent(GimmickEffect.Sap, victimId, now) * (boss ? 0.5f : 1f);

        private float TargetPercent(GimmickEffect effect, int victimId, float now)
        {
            bool present = effect == GimmickEffect.Sap ? _hasSapEntries
                : effect == GimmickEffect.Weakspot ? _hasWeakspotEntries : _hasExposeEntries;
            if (!present || !Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            decimal strongest = 0;
            foreach (var state in ActiveStates())
                if (state.Entry.Def.Effect == effect && state.Victims != null)
                    foreach (var victim in state.Victims)
                        if (victim.VictimId == victimId) strongest = Math.Max(strongest, state.Entry.Def.EffectiveValueOrAuthored);
            return (float)strongest;
        }

        private readonly List<long> _retiredAttributionCasts = new List<long>();
        public void PruneAttribution(MemoryActivationAttribution attribution)
        {
            foreach (var state in ActiveStates())
            {
                _retiredAttributionCasts.Clear();
                foreach (long cast in state.SeenCasts.Keys)
                    if (cast < 0 && !attribution.IsActivationRetained(-cast)) _retiredAttributionCasts.Add(cast);
                foreach (long cast in _retiredAttributionCasts) { state.SeenCasts.Remove(cast); state.CastVictims.Remove(cast); }
            }
        }
        public void ForgetActivation(long activationId)
        {
            foreach (var state in ActiveStates())
            {
                state.CastVictims.Remove(activationId);
                state.SeenCasts.Remove(activationId);
            }
        }

        public void ForgetVictim(int victimId)
        {
            _dazeUntil.Remove(victimId);
            foreach (var state in ActiveStates())
            {
                var victims = state.Victims;
                if (victims == null) continue;
                for (int i = victims.Count - 1; i >= 0; i--)
                    if (victims[i].VictimId == victimId) victims.RemoveAt(i);
            }
        }

        public void ClearTransient()
        {
            _dazeUntil.Clear();
            foreach (var state in ActiveStates())
            {
                state.HasFired = false;
                state.BuffActive = false;
                state.Stacks = 0;
                state.Victims?.Clear();
                state.CastVictims.Clear();
                state.SeenCasts.Clear();
            }
        }
    }

    /// <summary>One non-stacking wound per victim. Refreshing preserves its tick phase and the greater damage rate.</summary>
    public sealed class GimmickWoundRuntime
    {
        private sealed class Wound { public float Amount, Until, NextTick, Remaining; public bool Magic; public string SourceMemory, EffectId; }
        private readonly Dictionary<int, Wound> _wounds = new Dictionary<int, Wound>();
        public readonly struct Tick
        {
            public Tick(int victimId, float damage, bool magic, string sourceMemory = null, string effectId = null)
            { VictimId = victimId; Damage = damage; Magic = magic; SourceMemory = sourceMemory; EffectId = effectId; }
            public int VictimId { get; }
            public float Damage { get; }
            public bool Magic { get; }
            public string SourceMemory { get; }
            public string EffectId { get; }
        }
        public void Apply(int victimId, float now, float totalDamage, bool magic, float duration = 3f, float maximumTotalDamage = float.MaxValue,
            string sourceMemory = null, string effectId = null)
        {
            if (victimId == 0 || !Gimmicks.Finite(now) || !Gimmicks.Finite(totalDamage) || totalDamage <= 0
                || !Gimmicks.Finite(duration) || duration <= 0 || duration > 3f * (1f + Gimmicks.MaxParameterPercent / 100f)) return;
            if (!Gimmicks.Finite(maximumTotalDamage) || maximumTotalDamage <= 0) return;
            if (!_wounds.TryGetValue(victimId, out var wound) || now >= wound.Until)
                _wounds[victimId] = wound = new Wound { NextTick = now + 0.5f };
            if (totalDamage >= wound.Amount)
            { wound.Amount = totalDamage; wound.Magic = magic; wound.SourceMemory = sourceMemory; wound.EffectId = effectId; }
            wound.Remaining = Math.Min(maximumTotalDamage, wound.Amount * duration / 3f);
            wound.Until = now + duration;
        }
        public void Update(float now, List<Tick> ticks)
        {
            if (!Gimmicks.Finite(now) || ticks == null) return;
            var remove = new List<int>();
            foreach (var pair in _wounds)
            {
                var wound = pair.Value;
                while (wound.NextTick <= now && wound.NextTick <= wound.Until)
                {
                    float damage = Math.Min(wound.Amount / 6f, wound.Remaining);
                    if (damage > 0) ticks.Add(new Tick(pair.Key, damage, wound.Magic, wound.SourceMemory, wound.EffectId));
                    wound.Remaining = Math.Max(0, wound.Remaining - damage);
                    wound.NextTick += 0.5f;
                }
                if (now >= wound.Until)
                {
                    // Settle the final fractional interval without resetting the half-second tick phase.
                    if (wound.Remaining > 0) ticks.Add(new Tick(pair.Key, wound.Remaining, wound.Magic, wound.SourceMemory, wound.EffectId));
                    remove.Add(pair.Key);
                }
            }
            foreach (int id in remove) _wounds.Remove(id);
        }
        public void Forget(int victimId) => _wounds.Remove(victimId);
        public void Clear() => _wounds.Clear();
    }
}
