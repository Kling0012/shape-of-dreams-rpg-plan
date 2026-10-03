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

        private static string DescribeV129(GimmickDef def, string memory, int ranks)
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
                    text = ja ? "当てた敵に3秒あたり攻撃力か魔力の高い方の" + n + "%の継続ダメージを" + woundDuration + "秒間与える（重ならず、大きい方で時間を延長）"
                        : "deal " + n + "% of the higher of attack damage or ability power per 3 seconds to the hit enemy for " + woundDuration + " seconds (does not stack; refreshes the stronger wound)"; break;
                case GimmickEffect.Daze:
                    string seconds = Duration(def, (float)value / 10f).ToString("0.#######", CultureInfo.InvariantCulture);
                    text = ja ? "当てた敵を" + seconds + "秒スタンさせる（ミニボス・ボスには無効。同じ敵に5秒に1回）"
                        : "stun the hit enemy for " + seconds + " seconds (ineffective against minibosses and bosses; once per enemy every 5 seconds)"; break;
                case GimmickEffect.Ricochet:
                    text = ja ? "当てたダメージの" + n + "%を" + ricochetRadius + "m以内の別の敵" + targets + "体までに与える"
                        : "deal " + n + "% of the hit damage to up to " + targets + " other enemies within " + ricochetRadius + "m"; break;
                case GimmickEffect.Siphon:
                    text = ja ? "その記憶の直接ダメージの" + n + "%を自分のHPとして回復（継続ダメージは対象外。1回につき自分の最大HPの1.5%まで）"
                        + (def.Arg == 1 ? "。" + allyRadius + "m以内の味方旅人も半分回復（各自の最大HPの1.5%まで）" : "")
                        : "heal for " + n + "% of that memory's direct damage (excluding damage over time; at most 1.5% of your maximum health per heal)"
                        + (def.Arg == 1 ? "; allied travelers within " + allyRadius + "m receive half as much, capped at 1.5% of their own maximum health" : ""); break;
                case GimmickEffect.Rampart:
                    text = ja ? "同じ発動で当たった敵1体につき最大HPの" + n + "%の障壁を" + duration + "秒間張る（" + targets + "体まで、1体につき最大2%。通常の星の障壁とは別に1つまで、残量と新しい量の大きい方を維持して時間を更新。増幅後も最大HPの10%が上限）"
                        : "gain a " + duration + "-second shield equal to " + n + "% of maximum health per distinct enemy hit by this cast (up to " + targets + " enemies and 2% per enemy; one Rampart pool separate from ordinary star shields, keeping the larger remaining or new amount and refreshing duration; capped at 10% of maximum health after amplification)"; break;
                case GimmickEffect.Primed:
                    text = ja ? "次の通常攻撃に攻撃力か魔力の高い方の" + n + "%の追加ダメージ（重ならず" + primedDuration + "秒で消える。ほかの次の通常攻撃への上乗せとは最大の1つだけを使い、残りは消費しない）"
                        : "prime the next basic attack for " + n + "% of the higher of attack damage or ability power as extra damage (does not stack; expires after " + primedDuration + " seconds; consumes only the largest next-basic-attack bonus and leaves the others ready)"; break;
                case GimmickEffect.Crescendo:
                    string minimum = Duration(def, 8f).ToString("0.#######", CultureInfo.InvariantCulture);
                    string factor = Duration(def, 1.5f).ToString("0.#######", CultureInfo.InvariantCulture);
                    text = ja ? "その記憶のダメージ+" + n + "%（同じ発動では1回、5回まで、最大40%。" + minimum + "秒かその記憶のクールダウンの" + factor + "倍の長い方だけ続く。ほかの記憶を使っても保持。記憶の冴えとの合計は最大120%）"
                        : "gain +" + n + "% damage for that memory (once per cast, up to 5 stacks and 40%; lasts the longer of " + minimum + " seconds or " + factor + " times that memory's cooldown; retained when using other memories; combined memory damage bonuses are capped at 120%)"; break;
                case GimmickEffect.ElementEdge:
                    text = ja ? "当てた敵に乗っている火・冷気・光・闇の1種類につき、攻撃力か魔力の高い方の" + n + "%の追加ダメージ（4種類まで、最大160%。属性は消費しない）"
                        : "deal " + n + "% of the higher of attack damage or ability power as extra damage per Fire, Cold, Light or Dark element on the enemy (up to 4 types and 160%; does not consume elements)"; break;
                case GimmickEffect.PackMend:
                    text = ja ? "自分の生存する召喚獣すべてを各自の最大HPの" + n + "%回復（旅人は対象外）"
                        : "heal all your living summons for " + n + "% of each summon’s maximum health (does not heal travelers)"; break;
                case GimmickEffect.Sap:
                    text = ja ? "当てた敵の与えるダメージを" + duration + "秒間" + n + "%減らす（ミニボス・ボスは半分。重ならず大きい方で時間を延長）"
                        : "reduce the hit enemy's damage dealt by " + n + "% for " + duration + " seconds (halved for minibosses and bosses; does not stack; refreshes the stronger effect)"; break;
                default:
                    text = ja ? "当てた敵に対する自分の通常攻撃・記憶の会心率+" + n + "%（" + duration + "秒。重ならず大きい方で時間を延長。確定会心は変えない）"
                        : "gain +" + n + "% critical chance for your basic attacks and memories against the hit enemy for " + duration + " seconds (does not stack; refreshes the stronger effect; guaranteed critical hits remain unchanged)"; break;
            }
            string name = ja ? "『" + Links.Name(memory).Ja + "』" : Links.Name(memory).En;
            string trigger = def.Trigger == GimmickTrigger.OnUse ? (ja ? "を使うと、" : " is used: ")
                : def.Trigger == GimmickTrigger.OnKill ? (ja ? "で敵を倒すと、" : " kills an enemy: ")
                : def.Trigger == GimmickTrigger.OnCrit ? (ja ? "が会心すると、" : " critically hits: ")
                : (ja ? "が当たると、" : " hits: ");
            float cd = Math.Max(MinimumCooldown(def.Effect), Math.Min(def.Cooldown, MaxCooldown));
            string interval = cd > 0 ? (ja ? "。この星全体で" + cd.ToString("0.#######", CultureInfo.InvariantCulture) + "秒に1回"
                : "; once every " + cd.ToString("0.#######", CultureInfo.InvariantCulture) + " seconds per star") : "";
            string maximumStun = Duration(def, 0.8f).ToString("0.#######", CultureInfo.InvariantCulture);
            string cap = def.Effect == GimmickEffect.Daze ? (ja ? "。スタンは最大" + maximumStun + "秒" : "; stun capped at " + maximumStun + " seconds")
                : (ja ? "。効果量上限" + Cap(def.Effect) + "%" : "; effect value capped at " + Cap(def.Effect) + "%");
            return name + trigger + text + interval + cap + (ja ? "。仕掛けのダメージからは発動しない" : "; cannot trigger from gimmick damage.");
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
            Math.Max(0, Math.Min(Cap(GimmickEffect.ElementEdge), value)) * ((fire ? 1 : 0) + (cold ? 1 : 0) + (light ? 1 : 0) + (dark ? 1 : 0));

        public static float ElementEdgePercent(float value, bool fire, bool cold, bool light, bool dark) => !Finite(value) ? 0f :
            Math.Max(0, Math.Min(Cap(GimmickEffect.ElementEdge), value)) * ((fire ? 1 : 0) + (cold ? 1 : 0) + (light ? 1 : 0) + (dark ? 1 : 0));
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
            if (!Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            long result = 0;
            foreach (var state in _entries)
                if (state.Entry.Memory == memory && state.Entry.Def.Effect == GimmickEffect.Crescendo && state.BuffActive)
                    result = Math.Max(result, Math.Min(40 * Gimmicks.PreciseValueScale, state.Stacks * state.Entry.Def.ValuePrecise));
            return result / (float)Gimmicks.PreciseValueScale;
        }

        public float CombinedMemoryDamagePercent(string memory, float now, float otherPercent)
        {
            float crescendo = CrescendoPercent(memory, now);
            // Existing awakened links keep their established caps when Crescendo contributes nothing.
            return crescendo == 0 ? Math.Max(0, otherPercent)
                : Math.Min(Math.Min(120f, (float)Links.EquippedCap(LinkKind.MemoryDamage, 3)), Math.Max(0, otherPercent) + crescendo);
        }

        public float WeakspotPercent(int victimId, float now) => TargetPercent(GimmickEffect.Weakspot, victimId, now);
        public float SapPercent(int victimId, float now, bool boss) => TargetPercent(GimmickEffect.Sap, victimId, now) * (boss ? 0.5f : 1f);

        private float TargetPercent(GimmickEffect effect, int victimId, float now)
        {
            if (!Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            long strongest = 0;
            foreach (var state in _entries)
                if (state.Entry.Def.Effect == effect && state.Victims != null)
                    foreach (var victim in state.Victims)
                        if (victim.VictimId == victimId) strongest = Math.Max(strongest, state.Entry.Def.ValuePrecise);
            return strongest / (float)Gimmicks.PreciseValueScale;
        }

        public void ForgetActivation(long activationId)
        {
            foreach (var state in _entries)
            {
                state.CastVictims.Remove(activationId);
                state.SeenCasts.Remove(activationId);
            }
        }

        public void ForgetVictim(int victimId)
        {
            _dazeUntil.Remove(victimId);
            foreach (var state in _entries)
                state.Victims?.RemoveAll(v => v.VictimId == victimId);
        }

        public void ClearTransient()
        {
            _dazeUntil.Clear();
            foreach (var state in _entries)
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
        private sealed class Wound { public float Amount, Until, NextTick, Remaining; public bool Magic; }
        private readonly Dictionary<int, Wound> _wounds = new Dictionary<int, Wound>();
        public readonly struct Tick
        {
            public Tick(int victimId, float damage, bool magic) { VictimId = victimId; Damage = damage; Magic = magic; }
            public int VictimId { get; }
            public float Damage { get; }
            public bool Magic { get; }
        }
        public void Apply(int victimId, float now, float totalDamage, bool magic, float duration = 3f, float maximumTotalDamage = float.MaxValue)
        {
            if (victimId == 0 || !Gimmicks.Finite(now) || !Gimmicks.Finite(totalDamage) || totalDamage <= 0
                || !Gimmicks.Finite(duration) || duration <= 0 || duration > 12f) return;
            if (!Gimmicks.Finite(maximumTotalDamage) || maximumTotalDamage <= 0) return;
            if (!_wounds.TryGetValue(victimId, out var wound) || now >= wound.Until)
                _wounds[victimId] = wound = new Wound { NextTick = now + 0.5f };
            if (totalDamage >= wound.Amount) { wound.Amount = totalDamage; wound.Magic = magic; }
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
                    if (damage > 0) ticks.Add(new Tick(pair.Key, damage, wound.Magic));
                    wound.Remaining = Math.Max(0, wound.Remaining - damage);
                    wound.NextTick += 0.5f;
                }
                if (now >= wound.Until)
                {
                    // Settle the final fractional interval without resetting the half-second tick phase.
                    if (wound.Remaining > 0) ticks.Add(new Tick(pair.Key, wound.Remaining, wound.Magic));
                    remove.Add(pair.Key);
                }
            }
            foreach (int id in remove) _wounds.Remove(id);
        }
        public void Forget(int victimId) => _wounds.Remove(victimId);
        public void Clear() => _wounds.Clear();
    }
}
