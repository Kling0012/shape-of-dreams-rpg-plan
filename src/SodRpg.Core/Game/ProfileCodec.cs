using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SodRpg.Core.Internal;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// プロフィールの保存形式。{"format","version","checksum","body"} の形で、checksum は body の
    /// 正規化済みJSONの sha256。読めない遺物（未知の基礎IDなど）は捨てずに Notes へ記録して除外する。
    /// </summary>
    public static class ProfileCodec
    {
        public const string Format = "sodrpg.profile";

        public static string Write(Profile p)
        {
            var body = WriteBody(p);
            string bodyJson = Json.Write(body);
            var root = new JsonObject()
                .Add("format", Format)
                .Add("version", (long)Profile.CurrentVersion)
                .Add("checksum", "sha256:" + Sha256(bodyJson))
                .Add("body", body);
            return Json.Write(root);
        }

        public static Profile Read(string text, List<string> notes)
        {
            if (!(Json.Parse(text) is JsonObject root)) throw new LedgerFormatException("最上位がオブジェクトではありません。");
            if (Str(root, "format") != Format) throw new LedgerFormatException("形式が違います。");
            long version = Long(root, "version");
            if (version > Profile.CurrentVersion) throw new LedgerVersionException("新しすぎる版です: " + version);
            if (!root.TryGet("body", out object bodyObj) || !(bodyObj is JsonObject body)) throw new LedgerFormatException("body がありません。");
            string expected = Str(root, "checksum");
            string actual = "sha256:" + Sha256(Json.Write(body));
            if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw new LedgerFormatException("チェックサムが一致しません。");
            return ReadBody(body, notes ?? new List<string>());
        }

        private static JsonObject WriteBody(Profile p)
        {
            var mats = new JsonObject();
            foreach (var kv in p.Materials)
                if (kv.Value > 0) mats.Add(kv.Key, (long)kv.Value);
            var heroes = new JsonObject();
            foreach (var kv in p.Heroes)
            {
                var h = kv.Value;
                var eq = new List<object> { h.Equipped[0], h.Equipped[1], h.Equipped[2] };
                var tal = new JsonObject();
                foreach (var t in h.Talents) tal.Add(t.Key, (long)t.Value);
                heroes.Add(kv.Key, new JsonObject().Add("equipped", eq).Add("talents", tal).Add("keystone", h.Keystone).Add("kills", (long)h.Kills));
            }
            var codex = new List<object>();
            foreach (var c in p.Codex) codex.Add(c);
            var s = p.Stats;
            var stats = new JsonObject()
                .Add("runs", (long)s.Runs).Add("victories", (long)s.Victories).Add("defeats", (long)s.Defeats)
                .Add("relicsFound", (long)s.RelicsFound).Add("legendariesFound", (long)s.LegendariesFound)
                .Add("bestHeatSecured", (long)s.BestHeatSecured).Add("kills", (long)s.Kills)
                .Add("nightmares", (long)s.NightmaresSlain);

            JsonObject run = null;
            if (p.Run != null)
            {
                var r = p.Run;
                run = new JsonObject()
                    .Add("runId", r.RunId).Add("heat", (long)r.Heat).Add("satchel", WriteRelics(r.Satchel))
                    .Add("shards", (long)r.SatchelShards).Add("tuning", (long)r.SatchelTuning)
                    .Add("roomsCleared", (long)r.RoomsCleared).Add("lostRecovered", r.LostRecovered)
                    .Add("securedCount", (long)r.SecuredCount).Add("kills", (long)r.Kills)
                    .Add("peakHeat", (long)r.PeakHeat)
                    .Add("relicsFound", (long)r.RelicsFound).Add("relicsSecured", (long)r.RelicsSecured).Add("shardsSecured", (long)r.ShardsSecured).Add("levelAtStart", (long)r.LevelAtStart).Add("daily", (long)r.DailyId)
                    .Add("bounties", WriteBounties(r.Bounties))
                    .Add("pacts", WritePacts(r.Pacts)).Add("offeredPacts", WritePacts(r.OfferedPacts)).Add("awaitingChoice", r.AwaitingChoice);
            }

            return new JsonObject()
                .Add("revision", p.Revision)
                .Add("rng", p.RngState.ToString("x16", CultureInfo.InvariantCulture))
                .Add("dreamLevel", (long)p.DreamLevel)
                .Add("dreamXp", (long)p.DreamXp)
                .Add("epicPity", (long)p.EpicPity)
                .Add("bestItemLevel", (long)p.BestItemLevel)
                .Add("japanese", p.Japanese)
                .Add("focus", p.Focus.HasValue ? (long)p.Focus.Value : -1L)
                .Add("materials", mats)
                .Add("stash", WriteRelics(p.Stash))
                .Add("lostAndFound", WriteRelics(p.LostAndFound))
                .Add("heroes", heroes)
                .Add("codex", codex)
                .Add("stats", stats)
                .Add("run", run);
        }

        private static List<object> WritePacts(IEnumerable<Pact> pacts)
        {
            var list = new List<object>();
            foreach (var x in pacts) list.Add((long)x);
            return list;
        }

        private static void ReadPacts(JsonObject parent, string key, List<Pact> into, List<string> notes)
        {
            if (!parent.TryGet(key, out object o) || !(o is List<object> list)) return;
            foreach (var item in list)
            {
                if (item is long v && v != 0 && Enum.IsDefined(typeof(Pact), (int)v) && !into.Contains((Pact)(int)v)) into.Add((Pact)(int)v);
                else notes.Add("未知の契約を除外: " + item);
            }
        }

        private static List<object> WriteBounties(IEnumerable<Bounty> bounties)
        {
            var list = new List<object>();
            foreach (var b in bounties)
            {
                list.Add(new JsonObject()
                    .Add("kind", (long)b.Kind).Add("target", (long)b.Target).Add("progress", (long)b.Progress).Add("done", b.Done)
                    .Add("shards", (long)b.RewardShards).Add("tuning", (long)b.RewardTuning).Add("xp", (long)b.RewardXp));
            }
            return list;
        }

        private static void ReadBounties(JsonObject parent, List<Bounty> into, List<string> notes)
        {
            if (!parent.TryGet("bounties", out object o) || !(o is List<object> list)) return;
            foreach (var item in list)
            {
                if (!(item is JsonObject j)) continue;
                long kind = Long(j, "kind");
                if (!Enum.IsDefined(typeof(BountyKind), (int)kind))
                {
                    notes.Add("未知の依頼を除外: " + kind);
                    continue;
                }
                int target = Clamp(Long(j, "target"), 1, 100000);
                into.Add(new Bounty
                {
                    Kind = (BountyKind)(int)kind,
                    Target = target,
                    Progress = Clamp(Long(j, "progress"), 0, target),
                    Done = Bool(j, "done", false),
                    RewardShards = Clamp(Long(j, "shards"), 0, 1000),
                    RewardTuning = Clamp(Long(j, "tuning"), 0, 10),
                    RewardXp = Clamp(Long(j, "xp"), 0, 10000),
                });
            }
        }

        private static List<object> WriteRelics(IEnumerable<Relic> relics)
        {
            var list = new List<object>();
            foreach (var r in relics)
            {
                var aff = new List<object>();
                foreach (var a in r.Affixes) aff.Add(new List<object> { (long)a.Stat, (long)a.Value });
                var pw = new List<object>();
                foreach (var x in r.Powers) pw.Add(new List<object> { (long)x.Power, (long)x.Value });
                list.Add(new JsonObject()
                    .Add("uid", r.Uid).Add("base", r.BaseId).Add("unique", r.UniqueId)
                    .Add("rarity", (long)r.Rarity).Add("ilvl", (long)r.ItemLevel)
                    .Add("enhance", (long)r.Enhance).Add("retunes", (long)r.Retunes).Add("locked", r.Locked)
                    .Add("affixes", aff).Add("powers", pw));
            }
            return list;
        }

        private static Profile ReadBody(JsonObject b, List<string> notes)
        {
            var p = new Profile
            {
                Revision = Long(b, "revision"),
                RngState = ulong.Parse(Str(b, "rng"), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                DreamLevel = Clamp(Long(b, "dreamLevel"), 1, Content.MaxDreamLevel),
                DreamXp = Clamp(Long(b, "dreamXp"), 0, int.MaxValue),
                EpicPity = Clamp(Long(b, "epicPity"), 0, 1000),
                BestItemLevel = Clamp(Long(b, "bestItemLevel"), 1, Content.MaxItemLevel),
                Japanese = Bool(b, "japanese", true),
            };
            long focus = b.TryGet("focus", out object fo) && fo is long fl ? fl : -1;
            if (focus >= 0 && Enum.IsDefined(typeof(Line), (int)focus)) p.Focus = (Line)(int)focus;
            if (b.TryGet("materials", out object m) && m is JsonObject mats)
            {
                foreach (var kv in mats.Properties)
                {
                    if (kv.Key != Materials.Shard && kv.Key != Materials.Tuning)
                    {
                        notes.Add("未知の素材を除外: " + kv.Key);
                        continue;
                    }
                    if (kv.Value is long n && n > 0) p.Materials[kv.Key] = (int)Math.Min(int.MaxValue, n);
                }
            }
            ReadRelics(b, "stash", p.Stash, notes);
            ReadRelics(b, "lostAndFound", p.LostAndFound, notes);
            if (b.TryGet("heroes", out object ho) && ho is JsonObject heroes)
            {
                foreach (var kv in heroes.Properties)
                {
                    if (!(kv.Value is JsonObject hj)) continue;
                    var h = p.Hero(kv.Key);
                    if (hj.TryGet("equipped", out object eo) && eo is List<object> eq)
                    {
                        for (int i = 0; i < 3 && i < eq.Count; i++)
                        {
                            string uid = eq[i] as string;
                            var r = p.FindStash(uid);
                            h.Equipped[i] = r != null && (int)r.Slot == i ? uid : null;
                        }
                    }
                    if (hj.TryGet("talents", out object to) && to is JsonObject tal)
                    {
                        foreach (var t in tal.Properties)
                        {
                            if (Content.TryGetTalent(t.Key, out var def) && !def.IsKeystone && t.Value is long rank && rank > 0)
                                h.Talents[t.Key] = (int)Math.Min(def.MaxRank, rank);
                            else
                                notes.Add("未知の専門化ノードを除外: " + t.Key);
                        }
                    }
                    h.Kills = Clamp(Long(hj, "kills"), 0, int.MaxValue);
                    string key = hj.TryGet("keystone", out object ko) ? ko as string : null;
                    if (key != null && Content.TryGetTalent(key, out var kdef) && kdef.IsKeystone) h.Keystone = key;
                }
            }
            if (b.TryGet("codex", out object co) && co is List<object> codex)
                foreach (var c in codex)
                    if (c is string s) p.Codex.Add(s);
            if (b.TryGet("stats", out object so) && so is JsonObject st)
            {
                p.Stats.Runs = Clamp(Long(st, "runs"), 0, int.MaxValue);
                p.Stats.Victories = Clamp(Long(st, "victories"), 0, int.MaxValue);
                p.Stats.Defeats = Clamp(Long(st, "defeats"), 0, int.MaxValue);
                p.Stats.RelicsFound = Clamp(Long(st, "relicsFound"), 0, int.MaxValue);
                p.Stats.LegendariesFound = Clamp(Long(st, "legendariesFound"), 0, int.MaxValue);
                p.Stats.BestHeatSecured = Clamp(Long(st, "bestHeatSecured"), 0, Content.MaxHeat);
                p.Stats.Kills = Clamp(Long(st, "kills"), 0, int.MaxValue);
                p.Stats.NightmaresSlain = Clamp(Long(st, "nightmares"), 0, int.MaxValue);
            }
            if (b.TryGet("run", out object ro) && ro is JsonObject rj)
            {
                var run = new RunState
                {
                    RunId = Str(rj, "runId"),
                    Heat = Loot.ClampHeat(Clamp(Long(rj, "heat"), 0, Content.MaxHeat)),
                    SatchelShards = Clamp(Long(rj, "shards"), 0, int.MaxValue),
                    SatchelTuning = Clamp(Long(rj, "tuning"), 0, int.MaxValue),
                    RoomsCleared = Clamp(Long(rj, "roomsCleared"), 0, int.MaxValue),
                    LostRecovered = Bool(rj, "lostRecovered", false),
                    SecuredCount = Clamp(Long(rj, "securedCount"), 0, int.MaxValue),
                    Kills = Clamp(Long(rj, "kills"), 0, int.MaxValue),
                    PeakHeat = Clamp(Long(rj, "peakHeat"), 0, Content.MaxHeat),
                    RelicsFound = Clamp(Long(rj, "relicsFound"), 0, int.MaxValue),
                    RelicsSecured = Clamp(Long(rj, "relicsSecured"), 0, int.MaxValue),
                    ShardsSecured = Clamp(Long(rj, "shardsSecured"), 0, int.MaxValue),
                    LevelAtStart = Clamp(Long(rj, "levelAtStart"), 0, Content.MaxDreamLevel),
                    DailyId = DailyDream.Get((int)Long(rj, "daily")) != null ? (int)Long(rj, "daily") : 0,
                    AwaitingChoice = Bool(rj, "awaitingChoice", false),
                };
                ReadRelics(rj, "satchel", run.Satchel, notes);
                ReadBounties(rj, run.Bounties, notes);
                ReadPacts(rj, "pacts", run.Pacts, notes);
                ReadPacts(rj, "offeredPacts", run.OfferedPacts, notes);
                p.Run = run;
            }
            return p;
        }

        private static void ReadRelics(JsonObject parent, string key, List<Relic> into, List<string> notes)
        {
            if (!parent.TryGet(key, out object o) || !(o is List<object> list)) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                if (!(item is JsonObject j))
                {
                    notes.Add(key + ": 遺物の形式が不正なため除外");
                    continue;
                }
                try
                {
                    var r = new Relic
                    {
                        Uid = Str(j, "uid"),
                        BaseId = Str(j, "base"),
                        UniqueId = j.TryGet("unique", out object u) ? u as string : null,
                        Rarity = (Rarity)Clamp(Long(j, "rarity"), 0, (int)Rarity.Legendary),
                        ItemLevel = Clamp(Long(j, "ilvl"), 1, Content.MaxItemLevel),
                        Enhance = Clamp(Long(j, "enhance"), 0, Content.MaxEnhance),
                        Retunes = Clamp(Long(j, "retunes"), 0, Content.MaxRetunes),
                        Locked = Bool(j, "locked", false),
                    };
                    if (string.IsNullOrEmpty(r.Uid) || !Content.TryGetBase(r.BaseId, out _))
                        throw new LedgerFormatException("未知の基礎ID: " + r.BaseId);
                    if (r.UniqueId != null && !Content.TryGetUnique(r.UniqueId, out _))
                        throw new LedgerFormatException("未知の固有品ID: " + r.UniqueId);
                    if (!seen.Add(r.Uid)) throw new LedgerFormatException("Uidの重複: " + r.Uid);
                    if (j.TryGet("affixes", out object ao) && ao is List<object> affs)
                    {
                        foreach (var a in affs)
                        {
                            if (a is List<object> pair && pair.Count == 2 && pair[0] is long sid && pair[1] is long v
                                && Enum.IsDefined(typeof(Stat), (int)sid))
                            {
                                var stat = (Stat)(int)sid;
                                int cap = Math.Max(1, Content.StatCap(stat));
                                r.Affixes.Add(new StatLine(stat, Clamp(v, -cap, cap)));
                            }
                        }
                    }
                    if (j.TryGet("powers", out object po) && po is List<object> pws)
                    {
                        foreach (var x in pws)
                        {
                            if (x is List<object> pair && pair.Count == 2 && pair[0] is long pid && pair[1] is long v
                                && pid != 0 && Enum.IsDefined(typeof(Power), (int)pid))
                            {
                                var pw = (Power)(int)pid;
                                r.Powers.Add(new PowerLine(pw, Clamp(v, 0, Content.PowerCap(pw))));
                            }
                        }
                    }
                    into.Add(r);
                }
                catch (LedgerFormatException ex)
                {
                    notes.Add(key + ": " + ex.Message + " → 除外");
                }
            }
        }

        private static string Str(JsonObject o, string key) => o.TryGet(key, out object v) ? v as string : null;

        private static long Long(JsonObject o, string key) => o.TryGet(key, out object v) && v is long l ? l : 0;

        private static bool Bool(JsonObject o, string key, bool fallback) => o.TryGet(key, out object v) && v is bool b ? b : fallback;

        private static int Clamp(long v, int min, int max) => (int)Math.Max(min, Math.Min(max, v));

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte x in hash) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
