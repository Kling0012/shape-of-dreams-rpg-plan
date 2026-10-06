using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    public static partial class Gimmicks
    {
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
            bool ja = Loc.Japanese;
            string n = value.ToString("0.#######", CultureInfo.InvariantCulture), text;
            string duration = Duration(def, BuffDuration).ToString("0.#######", CultureInfo.InvariantCulture);
            string woundDuration = Duration(def, 3f).ToString("0.#######", CultureInfo.InvariantCulture);
            string primedDuration = Duration(def, 5f).ToString("0.#######", CultureInfo.InvariantCulture);
            string ricochetRadius = Radius(def, 8f).ToString("0.#######", CultureInfo.InvariantCulture);
            string allyRadius = Radius(def, 10f).ToString("0.#######", CultureInfo.InvariantCulture);
            int targets = TargetLimit(def);
            switch (def.Effect)
            {
                case GimmickEffect.Wound:
                    text = ja ? "継続ダメージ +攻撃力・魔力の高い方の" + n + "%（3秒ごと）：当てた敵に" + woundDuration + "秒間与える（重ならず、強い方で時間を延長）"
                        : "Damage over time +" + n + "% of the higher of attack damage or ability power per 3 seconds: damage the hit enemy for " + woundDuration + " seconds (does not stack; refreshes the stronger wound)"; break;
                case GimmickEffect.Daze:
                    string seconds = Duration(def, (float)value / 10f).ToString("0.#######", CultureInfo.InvariantCulture);
                    text = ja ? "スタン +" + seconds + "秒：当てた敵が対象（ミニボス・ボスには無効。同じ敵に5秒に1回）"
                        : "Stun +" + seconds + " seconds: affects the hit enemy (ineffective against minibosses and bosses; once per enemy every 5 seconds)"; break;
                case GimmickEffect.Ricochet:
                    text = ja ? "跳弾ダメージ +当てたダメージの" + n + "%：" + ricochetRadius + "m以内の別の敵" + targets + "体までに与える"
                        : "Ricochet damage +" + n + "% of the hit damage: damage up to " + targets + " other enemies within " + ricochetRadius + "m"; break;
                case GimmickEffect.Siphon:
                    text = ja ? "HP回復 +その記憶の直接ダメージの" + n + "%：自分を回復（継続ダメージは対象外。1回につき自分の最大HPの1.5%まで）"
                        + (def.Arg == 1 ? "。" + allyRadius + "m以内の味方旅人も半分回復（各自の最大HPの1.5%まで）" : "")
                        : "Healing +" + n + "% of that memory's direct damage: heal yourself (excluding damage over time; at most 1.5% of your maximum health per heal)"
                        + (def.Arg == 1 ? "; allied travelers within " + allyRadius + "m receive half as much, capped at 1.5% of their own maximum health" : ""); break;
                case GimmickEffect.Rampart:
                    text = ja ? "障壁 +命中した敵1体につき最大HPの" + n + "%：同じ発動で当たった別々の敵を数え、" + duration + "秒間張る（" + targets + "体まで、1体につき最大2%。この障壁は通常の星の障壁とは別に1つまで。残量と新しい量の大きい方を保って時間を更新。増幅後も最大HPの10%が上限）"
                        : "Shield +" + n + "% of maximum health per distinct enemy hit by this cast: lasts " + duration + " seconds (up to " + targets + " enemies and 2% per enemy; one pool separate from ordinary star shields, keeping the larger remaining or new amount and refreshing duration; capped at 10% of maximum health after amplification)"; break;
                case GimmickEffect.Primed:
                    text = ja ? "次の通常攻撃の追加ダメージ +攻撃力・魔力の高い方の" + n + "%（重ならず" + primedDuration + "秒で消える。次の通常攻撃への上乗せが複数あっても最大の1つだけ使い、残りは消費しない）"
                        : "Next basic attack extra damage +" + n + "% of the higher of attack damage or ability power (does not stack; expires after " + primedDuration + " seconds; consumes only the largest next-basic-attack bonus and leaves the others ready)"; break;
                case GimmickEffect.Crescendo:
                    string minimum = Duration(def, 8f).ToString("0.#######", CultureInfo.InvariantCulture);
                    string factor = Duration(def, 1.5f).ToString("0.#######", CultureInfo.InvariantCulture);
                    text = ja ? "記憶ダメージ +" + n + "%：その記憶が対象（同じ発動では1回、5回まで、最大40%。" + minimum + "秒かその記憶のクールダウンの" + factor + "倍の長い方だけ続く。ほかの記憶を使っても保持。記憶ダメージ増加との合計は最大120%）"
                        : "Memory damage +" + n + "%: affects that memory (once per cast, up to 5 stacks and 40%; lasts the longer of " + minimum + " seconds or " + factor + " times that memory's cooldown; retained when using other memories; combined memory damage bonuses are capped at 120%)"; break;
                case GimmickEffect.ElementEdge:
                    text = ja ? "追加ダメージ +属性1種類につき攻撃力・魔力の高い方の" + n + "%：当てた敵の火・冷気・光・闇を数える（4種類まで、最大160%。属性は消費しない）"
                        : "Extra damage +" + n + "% of the higher of attack damage or ability power per element: count Fire, Cold, Light and Dark on the hit enemy (up to 4 types and 160%; does not consume elements)"; break;
                case GimmickEffect.PackMend:
                    text = ja ? "召喚獣のHP回復 +各自の最大HPの" + n + "%：自分の生存する召喚獣すべてが対象（旅人は対象外）"
                        : "Summon healing +" + n + "% of each summon’s maximum health: affects all your living summons (does not heal travelers)"; break;
                case GimmickEffect.Sap:
                    text = ja ? "敵の与ダメージ -" + n + "%：当てた敵に" + duration + "秒間適用（ミニボス・ボスは半分。重ならず、強い方で時間を延長）"
                        : "Enemy damage dealt -" + n + "%: applies to the hit enemy for " + duration + " seconds (halved for minibosses and bosses; does not stack; refreshes the stronger effect)"; break;
                default:
                    text = ja ? "会心率 +" + n + "パーセントポイント：当てた敵に対する自分の通常攻撃・記憶に適用（" + duration + "秒。重ならず、強い方で時間を延長。確定会心は変えない）"
                        : "Critical chance +" + n + " percentage points: applies to your basic attacks and memories against the hit enemy for " + duration + " seconds (does not stack; refreshes the stronger effect; guaranteed critical hits remain unchanged)"; break;
            }
            string trigger = def.Trigger == GimmickTrigger.OnUse ? (ja ? "を使うと発動" : " is used")
                : def.Trigger == GimmickTrigger.OnKill ? (ja ? "で敵を倒すと発動" : " kills an enemy")
                : def.Trigger == GimmickTrigger.OnCrit ? (ja ? "が会心すると発動" : " critically hits")
                : (ja ? "が当たると発動" : " hits");
            float cd = Math.Max(MinimumCooldown(def.Effect), Math.Min(def.Cooldown, MaxCooldown));
            string interval = cd > 0 ? (ja ? "この星ごとに" + cd.ToString("0.#######", CultureInfo.InvariantCulture) + "秒に1回"
                : "once every " + cd.ToString("0.#######", CultureInfo.InvariantCulture) + " seconds per star")
                : (ja ? "間隔制限なし" : "no cooldown");
            string maximumStun = Duration(def, 0.8f).ToString("0.#######", CultureInfo.InvariantCulture);
            string cap = def.Effect == GimmickEffect.Daze ? (ja ? "スタンは最大" + maximumStun + "秒" : "stun capped at " + maximumStun + " seconds")
                : (ja ? "効果量は最大" + Cap(def.Effect) + "%" : "effect capped at " + Cap(def.Effect) + "%");
            return text + (ja ? "。" : ". ") + (triggerText ?? ((ja ? "" : "Triggered when ") + memory + trigger))
                + (ja ? "（" + interval + "・" + cap + "。星の追加ダメージでは発動しない）"
                    : " (" + interval + "; " + cap + "; not triggered by extra damage from stars).");
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
                    state.BuffUntil = now + Gimmicks.Duration(state.Entry.Def,
                        Math.Max(8f, Gimmicks.Finite(cooldown) && cooldown > 0 ? cooldown * 1.5f : 0));
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
                state.Victims?.RemoveAll(v => v.VictimId == victimId);
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
                || !Gimmicks.Finite(duration) || duration <= 0 || duration > 12f) return;
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
