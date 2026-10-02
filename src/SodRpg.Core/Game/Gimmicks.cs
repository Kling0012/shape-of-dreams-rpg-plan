using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>記憶の仕掛けのきっかけ（v1.28）。仕掛けは、その星のルートの記憶に反応する。</summary>
    public enum GimmickTrigger
    {
        None = 0,
        /// <summary>その記憶を使ったとき。</summary>
        OnUse = 1,
        /// <summary>その記憶のダメージが敵に当たったとき。</summary>
        OnHit = 2,
        /// <summary>その記憶のダメージで敵を倒したとき。</summary>
        OnKill = 3,
        /// <summary>その記憶のダメージが会心したとき。</summary>
        OnCrit = 4,
    }

    /// <summary>記憶の仕掛けで起きること（v1.28）。値の意味は docs/specs/v1.28-memory-gimmicks.md。</summary>
    public enum GimmickEffect
    {
        None = 0,
        /// <summary>属性を付ける。Arg：0 火・1 冷気・2 光・3 闇。Value：100 ごとに1つ、残りは%の確率でもう1つ。</summary>
        Element = 1,
        /// <summary>周り4mに、攻撃力か魔力の高い方の Value% の追加ダメージ。</summary>
        Burst = 2,
        /// <summary>自分に最大HPの Value% の障壁（4秒）。</summary>
        Shield = 3,
        /// <summary>自分を最大HPの Value% 回復。Arg=1 なら近くの味方も。</summary>
        Heal = 4,
        /// <summary>その記憶の残りクールダウンを Value% 縮める。</summary>
        Recharge = 5,
        /// <summary>4秒間、攻撃速度 +Value%（重ならず時間を延長）。</summary>
        Quicken = 6,
        /// <summary>4秒間、攻撃力・魔力 +Value%（重ならず時間を延長）。</summary>
        Empower = 7,
        /// <summary>当てた敵は4秒間、自分から受けるダメージが Value% 増える（重ならず時間を延長）。</summary>
        Expose = 8,
        /// <summary>当てたダメージの Value% を、0.3秒後にもう一度与える。</summary>
        Echo = 9,
    }

    /// <summary>記憶の仕掛け1つ分の定義（v1.28）。</summary>
    public sealed class GimmickDef
    {
        public GimmickTrigger Trigger { get; set; }
        public GimmickEffect Effect { get; set; }
        /// <summary>1段あたりの値。</summary>
        public int Value { get; set; }
        /// <summary>効果の補足（属性の種類、味方も回復するか）。</summary>
        public int Arg { get; set; }
        /// <summary>内部の間隔（秒）。0 なら制限なし。</summary>
        public float Cooldown { get; set; }
    }

    /// <summary>装着中の星の仕掛け。Def.Value は取得した段数を掛けた値。</summary>
    public sealed class GimmickEntry
    {
        public string StarId { get; set; }
        public string Memory { get; set; }
        public GimmickDef Def { get; set; }
    }

    /// <summary>ホストへ渡す発動。対象の解決と追加ダメージの連鎖防止はホストが行う。</summary>
    public struct GimmickRequest
    {
        public GimmickEntry Entry { get; set; }
        public int VictimId { get; set; }
        public float Damage { get; set; }
    }

    /// <summary>記憶の仕掛けの説明と、通信・計算で共用する上限。</summary>
    public static class Gimmicks
    {
        public const int MaxEntries = 32;
        public const int MaxStarIdLength = 96;
        public const float MaxCooldown = 60f;
        public const float BuffDuration = 4f;

        public static int Cap(GimmickEffect effect)
        {
            switch (effect)
            {
                case GimmickEffect.Element: return 600;
                case GimmickEffect.Burst:
                case GimmickEffect.Echo: return 1000;
                case GimmickEffect.Empower: return 200;
                case GimmickEffect.Shield:
                case GimmickEffect.Heal:
                case GimmickEffect.Recharge:
                case GimmickEffect.Quicken:
                case GimmickEffect.Expose: return 100;
                default: return 0;
            }
        }

        private static bool ValidDef(GimmickDef def)
        {
            if (def == null || def.Trigger < GimmickTrigger.OnUse || def.Trigger > GimmickTrigger.OnCrit
                || Cap(def.Effect) == 0 || def.Value <= 0 || !Finite(def.Cooldown) || def.Cooldown < 0) return false;
            return def.Effect == GimmickEffect.Element ? def.Arg >= 0 && def.Arg <= 3
                : def.Effect == GimmickEffect.Heal ? def.Arg >= 0 && def.Arg <= 1 : def.Arg == 0;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>通信で使う区切りを含まない、長さ1〜96の星の識別子。</summary>
        public static bool ValidStarId(string starId)
        {
            if (string.IsNullOrEmpty(starId) || starId.Length > MaxStarIdLength) return false;
            foreach (char c in starId)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')
                    && c != '_' && c != '.' && c != '-') return false;
            return true;
        }

        /// <summary>不正な条件は拒否し、効果量と間隔を共通上限へ収めた独立したコピーを返す。</summary>
        public static GimmickEntry Clamp(GimmickEntry entry)
        {
            if (entry == null || !ValidStarId(entry.StarId) || !Links.IsMemory(entry.Memory) || !ValidDef(entry.Def)) return null;
            return new GimmickEntry
            {
                StarId = entry.StarId,
                Memory = entry.Memory,
                Def = new GimmickDef
                {
                    Trigger = entry.Def.Trigger,
                    Effect = entry.Def.Effect,
                    Value = Math.Min(entry.Def.Value, Cap(entry.Def.Effect)),
                    Arg = entry.Def.Arg,
                    Cooldown = Math.Min(entry.Def.Cooldown, MaxCooldown)
                }
            };
        }

        /// <summary>1段の定義から、段数込みの効果・間隔・上限を説明する。</summary>
        public static string Describe(GimmickDef def, string memoryTypeName, int ranks = 1)
        {
            if (!ValidDef(def) || !Links.IsMemory(memoryTypeName) || ranks <= 0) return "";
            int value = (int)Math.Min((long)def.Value * ranks, Cap(def.Effect));
            string n = value.ToString(CultureInfo.InvariantCulture);
            bool ja = Loc.Japanese;
            string memory = ja ? "『" + Links.Name(memoryTypeName).Ja + "』" : Links.Name(memoryTypeName).En;
            string trigger;
            switch (def.Trigger)
            {
                case GimmickTrigger.OnUse: trigger = ja ? memory + "を使うと、" : "When you use " + memory + ", "; break;
                case GimmickTrigger.OnHit: trigger = ja ? memory + "が当たると、" : "When " + memory + " hits, "; break;
                case GimmickTrigger.OnKill: trigger = ja ? memory + "で敵を倒すと、" : "When " + memory + " kills an enemy, "; break;
                default: trigger = ja ? memory + "が会心すると、" : "When " + memory + " critically hits, "; break;
            }
            string effect;
            switch (def.Effect)
            {
                case GimmickEffect.Element:
                    string element = ja ? (def.Arg == 0 ? "火" : def.Arg == 1 ? "冷気" : def.Arg == 2 ? "光" : "闇")
                        : (def.Arg == 0 ? "fire" : def.Arg == 1 ? "cold" : def.Arg == 2 ? "light" : "darkness");
                    int whole = value / 100, chance = value % 100;
                    string stacks = ja ? (whole == 0 ? chance + "%の確率で1つ"
                        : whole + "つ" + (chance == 0 ? "" : "（さらに" + chance + "%の確率でもう1つ）"))
                        : (whole == 0 ? "1 stack with a " + chance + "% chance"
                        : whole + (whole == 1 ? " stack" : " stacks") + (chance == 0 ? "" : " (plus a " + chance + "% chance of 1 more)"));
                    effect = ja ? (def.Trigger == GimmickTrigger.OnUse ? "近くの敵" : "当てた敵") + "に" + element + "を" + stacks + "付ける"
                        : "apply " + stacks + " of " + element + " to " + (def.Trigger == GimmickTrigger.OnUse ? "nearby enemies" : "the hit enemy");
                    break;
                case GimmickEffect.Burst:
                    effect = ja ? (def.Trigger == GimmickTrigger.OnUse ? "自分" : "当てた敵") + "の周り4mに、攻撃力か魔力の高い方の" + n + "%の追加ダメージ（魔力が高ければ魔法）"
                        : "deal " + n + "% of the higher of attack damage or ability power as extra damage within 4m of "
                            + (def.Trigger == GimmickTrigger.OnUse ? "yourself" : "the hit enemy") + " (magic damage if ability power is higher)";
                    break;
                case GimmickEffect.Shield:
                    effect = ja ? "自分に最大HPの" + n + "%の障壁を張る（4秒）" : "gain a shield equal to " + n + "% of maximum health for 4 seconds";
                    break;
                case GimmickEffect.Heal:
                    effect = ja ? "自分" + (def.Arg == 1 ? "と10m以内の味方" : "") + "を最大HPの" + n + "%回復"
                        : "heal yourself" + (def.Arg == 1 ? " and allies within 10m" : "") + " for " + n + "% of maximum health";
                    break;
                case GimmickEffect.Recharge:
                    effect = ja ? "その記憶の残りクールダウンを" + n + "%縮める" : "reduce that memory's remaining cooldown by " + n + "%";
                    break;
                case GimmickEffect.Quicken:
                    effect = ja ? "4秒間、攻撃速度+" + n + "%" : "gain +" + n + "% attack speed for 4 seconds";
                    break;
                case GimmickEffect.Empower:
                    effect = ja ? "4秒間、攻撃力・魔力+" + n + "%" : "gain +" + n + "% attack damage and ability power for 4 seconds";
                    break;
                case GimmickEffect.Expose:
                    effect = ja ? "当てた敵が4秒間、自分から受けるダメージ+" + n + "%" : "the hit enemy takes " + n + "% more damage from you for 4 seconds";
                    break;
                default:
                    effect = ja ? "当てたダメージの" + n + "%を0.3秒後にもう一度与える" : "deal " + n + "% of the hit damage again after 0.3 seconds";
                    break;
            }
            string cooldown = Math.Min(def.Cooldown, MaxCooldown).ToString("R", CultureInfo.InvariantCulture);
            string interval = def.Cooldown == 0
                ? (ja ? "間隔制限なし" : "no cooldown")
                : (ja ? "この星全体で" + cooldown + "秒に1回" : "once every " + cooldown + (def.Cooldown == 1f ? " second" : " seconds") + " per star, shared across enemies and triggers");
            string cap = def.Effect == GimmickEffect.Element
                ? (ja ? "効果量上限" + Cap(def.Effect) / 100 + "つ" : "capped at " + Cap(def.Effect) / 100 + " stacks")
                : (ja ? "効果量上限" + Cap(def.Effect) + "%" : "capped at " + Cap(def.Effect) + "%");
            string stacking = def.Effect == GimmickEffect.Quicken || def.Effect == GimmickEffect.Empower || def.Effect == GimmickEffect.Expose
                ? (ja ? "・同時には最大値1つ、重ならず発動した星の時間を延長" : "; only the strongest active value applies, refreshing each star without stacking") : "";
            return trigger + effect + (ja ? "（" + interval + "・" + cap + stacking + "。仕掛けのダメージからは発動しない）"
                : " (" + interval + "; " + cap + stacking + "; cannot trigger from gimmick damage).");
        }
    }

    /// <summary>1人分の間隔と4秒の非加算効果。時計と対象の寿命はホストから渡す。</summary>
    public sealed class GimmickRuntime
    {
        private sealed class ActiveEntry
        {
            public GimmickEntry Entry;
            public bool HasFired;
            public float LastFired;
            public bool BuffActive;
            public float BuffUntil;
            public List<VictimWindow> Victims;
        }

        private struct VictimWindow
        {
            public int VictimId;
            public float Until;
        }

        private List<ActiveEntry> _entries = new List<ActiveEntry>();

        /// <summary>同じ定義を再設定しても間隔は戻らない。外した星・変更した星の効果は解除する。</summary>
        public void SetBuild(IReadOnlyList<GimmickEntry> entries)
        {
            var next = new List<ActiveEntry>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count && next.Count < Gimmicks.MaxEntries; i++)
                {
                    GimmickEntry entry = Gimmicks.Clamp(entries[i]);
                    if (entry == null) continue;
                    bool duplicate = false;
                    for (int j = 0; j < next.Count; j++)
                        if (next[j].Entry.StarId == entry.StarId) { duplicate = true; break; }
                    if (duplicate) continue;
                    ActiveEntry retained = null;
                    for (int j = 0; j < _entries.Count; j++)
                    {
                        ActiveEntry previous = _entries[j];
                        if (previous.Entry.StarId != entry.StarId) continue;
                        retained = Same(previous.Entry, entry) ? previous : new ActiveEntry
                        {
                            Entry = entry,
                            HasFired = previous.HasFired,
                            LastFired = previous.LastFired
                        };
                        break;
                    }
                    next.Add(retained ?? new ActiveEntry { Entry = entry });
                }
            }
            _entries = next;
        }

        private static bool Same(GimmickEntry a, GimmickEntry b) =>
            a.StarId == b.StarId && a.Memory == b.Memory && a.Def.Trigger == b.Def.Trigger
            && a.Def.Effect == b.Def.Effect && a.Def.Value == b.Def.Value && a.Def.Arg == b.Def.Arg && a.Def.Cooldown == b.Def.Cooldown;

        /// <summary>期限ちょうどで効果を解除し、敵ごとの期限も取り除く。</summary>
        public void PruneExpired(float now)
        {
            if (!Gimmicks.Finite(now)) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                ActiveEntry state = _entries[i];
                if (state.BuffActive && now >= state.BuffUntil) state.BuffActive = false;
                if (state.Victims == null) continue;
                for (int j = state.Victims.Count - 1; j >= 0; j--)
                    if (now >= state.Victims[j].Until) state.Victims.RemoveAt(j);
            }
        }

        /// <summary>該当する発動を末尾へ追加。追加ダメージは起点にならず、間隔は星全体で共有する。</summary>
        public void Fire(GimmickTrigger trigger, string memory, float now, int victimId, float damage, bool generated, List<GimmickRequest> results)
        {
            if (generated || results == null || !Gimmicks.Finite(now) || trigger < GimmickTrigger.OnUse || trigger > GimmickTrigger.OnCrit) return;
            PruneExpired(now);
            for (int i = 0; i < _entries.Count; i++)
            {
                ActiveEntry state = _entries[i];
                GimmickDef def = state.Entry.Def;
                if (def.Trigger != trigger || state.Entry.Memory != memory) continue;
                if (state.HasFired && def.Cooldown > 0 && now < state.LastFired + def.Cooldown) continue;
                if ((def.Effect == GimmickEffect.Expose || def.Effect == GimmickEffect.Echo) && victimId == 0) continue;
                if (def.Effect == GimmickEffect.Echo && (!Gimmicks.Finite(damage) || damage <= 0)) continue;
                state.HasFired = true;
                state.LastFired = now;
                if (def.Effect == GimmickEffect.Quicken || def.Effect == GimmickEffect.Empower)
                {
                    state.BuffActive = true;
                    state.BuffUntil = now + Gimmicks.BuffDuration;
                }
                else if (def.Effect == GimmickEffect.Expose)
                {
                    if (state.Victims == null) state.Victims = new List<VictimWindow>();
                    int found = -1;
                    for (int j = 0; j < state.Victims.Count; j++)
                        if (state.Victims[j].VictimId == victimId) { found = j; break; }
                    var window = new VictimWindow { VictimId = victimId, Until = now + Gimmicks.BuffDuration };
                    if (found < 0) state.Victims.Add(window);
                    else state.Victims[found] = window;
                }
                results.Add(new GimmickRequest
                {
                    Entry = state.Entry,
                    VictimId = victimId,
                    Damage = Gimmicks.Finite(damage) && damage > 0 ? damage : 0
                });
            }
        }

        public int QuickenPercent(float now) => BuffPercent(GimmickEffect.Quicken, now);
        public int EmpowerPercent(float now) => BuffPercent(GimmickEffect.Empower, now);

        private int BuffPercent(GimmickEffect effect, float now)
        {
            if (!Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            int strongest = 0;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].BuffActive && _entries[i].Entry.Def.Effect == effect)
                    strongest = Math.Max(strongest, _entries[i].Entry.Def.Value);
            return strongest;
        }

        public int ExposePercent(int victimId, float now)
        {
            if (!Gimmicks.Finite(now)) return 0;
            PruneExpired(now);
            int strongest = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                ActiveEntry state = _entries[i];
                if (state.Entry.Def.Effect != GimmickEffect.Expose || state.Victims == null) continue;
                for (int j = 0; j < state.Victims.Count; j++)
                    if (state.Victims[j].VictimId == victimId) strongest = Math.Max(strongest, state.Entry.Def.Value);
            }
            return strongest;
        }
    }
}
