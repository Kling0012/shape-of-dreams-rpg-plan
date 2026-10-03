using System;
using System.Collections.Generic;
using System.Linq;
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
            var loaded = ReadBody(body, notes ?? new List<string>());
            loaded.LoadedVersion = (int)Math.Max(0, Math.Min(int.MaxValue, version));
            return loaded;
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
                var eq = new List<object>(Content.SlotCount);
                foreach (var uid in h.Equipped) eq.Add(uid);
                var tal = new JsonObject();
                foreach (var t in h.Talents) tal.Add(t.Key, (long)t.Value);
                var choices = new JsonObject();
                foreach (var choice in h.TalentChoices) choices.Add(choice.Key, (long)choice.Value);
                heroes.Add(kv.Key, new JsonObject().Add("equipped", eq).Add("talents", tal).Add("talentChoices", choices)
                    .Add("keystone", h.Keystone).Add("kills", (long)h.Kills).Add("starXp", (long)h.StarXp));
            }
            var codex = new List<object>();
            foreach (var c in p.Codex) codex.Add(c);
            var s = p.Stats;
            var stats = new JsonObject()
                .Add("runs", (long)s.Runs).Add("victories", (long)s.Victories).Add("defeats", (long)s.Defeats)
                .Add("relicsFound", (long)s.RelicsFound).Add("legendariesFound", (long)s.LegendariesFound)
                .Add("relicsAwakened", (long)s.RelicsAwakened).Add("variantsSlain", (long)s.VariantsSlain)
                .Add("bestHeatSecured", (long)s.BestHeatSecured).Add("kills", (long)s.Kills)
                .Add("nightmares", (long)s.NightmaresSlain).Add("bestVictoryStartDepth", (long)s.BestVictoryStartDepth)
                .Add("pactsSworn", (long)s.PactsSworn).Add("eventsUsed", (long)s.EventsUsed).Add("bountiesDone", (long)s.BountiesDone);

            var pendingSalvage = new List<object>();
            foreach (var pending in p.PendingSalvage)
                pendingSalvage.Add(WriteRelic(pending.Relic).Add("returnTarget", (long)pending.ReturnTarget));

            JsonObject run = null;
            if (p.Run != null)
            {
                var r = p.Run;
                run = new JsonObject()
                    .Add("runId", r.RunId).Add("heat", (long)r.Heat).Add("satchel", WriteRelics(r.Satchel))
                    .Add("heroKey", r.HeroKey).Add("starSecureRewarded", r.StarSecureRewarded)
                    .Add("dreamDepth", (long)r.DreamDepth)
                    .Add("activeWaypoint", (long)r.ActiveWaypoint).Add("pendingWaypoint", (long)r.PendingWaypoint)
                    .Add("offeredWaypoints", r.OfferedWaypoints.Select(w => (object)(long)w).ToList())
                    .Add("waypointChosen", r.WaypointChosen).Add("waypointGeneration", (long)r.WaypointGeneration)
                    .Add("waypointRoom", (long)r.WaypointRoom).Add("waypointRelicsInRoom", (long)r.WaypointRelicsInRoom)
                    .Add("waypointLootRooms", r.WaypointLootRooms.OrderBy(x => x).Select(x => (object)(long)x).ToList())
                    .Add("waypointSlotCursor", (long)r.WaypointSlotCursor)
                    .Add("deferredWaypointRelics", WriteRelics(r.DeferredWaypointRelics))
                    .Add("deferredWaypointShards", (long)r.DeferredWaypointShards).Add("deferredWaypointTuning", (long)r.DeferredWaypointTuning)
                    .Add("shards", (long)r.SatchelShards).Add("tuning", (long)r.SatchelTuning)
                    .Add("roomsCleared", (long)r.RoomsCleared).Add("lostRecovered", r.LostRecovered)
                    .Add("securedCount", (long)r.SecuredCount).Add("kills", (long)r.Kills)
                    .Add("peakHeat", (long)r.PeakHeat)
                    .Add("relicsFound", (long)r.RelicsFound).Add("relicsSecured", (long)r.RelicsSecured).Add("shardsSecured", (long)r.ShardsSecured).Add("levelAtStart", (long)r.LevelAtStart).Add("daily", (long)r.DailyId).Add("startDepth", (long)r.StartDepth).Add("rerollsUsed", (long)r.RerollsUsed).Add("event", (long)r.OfferedEvent).Add("limbo", (long)r.LimboDepth)
                    // 最小JSONは整数のみ扱うため、小数の補正はカルチャ非依存の文字列で保存する。
                    .Add("eventDropBonus", r.EventDropBonus.ToString("R", CultureInfo.InvariantCulture))
                    .Add("eventLuck", r.EventLuck.ToString("R", CultureInfo.InvariantCulture))
                    .Add("bounties", WriteBounties(r.Bounties))
                    .Add("pacts", WritePacts(r.Pacts)).Add("offeredPacts", WritePacts(r.OfferedPacts)).Add("awaitingChoice", r.AwaitingChoice).Add("gearWindow", r.GearWindow);
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
                .Add("startDepth", (long)p.StartDepth)
                .Add("lastDreamDepth", (long)p.LastDreamDepth)
                .Add("materials", mats)
                .Add("stash", WriteRelics(p.Stash))
                .Add("lostAndFound", WriteRelics(p.LostAndFound))
                .Add("pendingSalvage", pendingSalvage)
                .Add("retuneOffer", WriteRetuneOffer(p.RetuneOffer))
                .Add("heroes", heroes)
                .Add("codex", codex)
                .Add("feats", p.Feats.Select(f => (object)f).ToList())
                .Add("featsClaimed", p.FeatsClaimed.Select(f => (object)f).ToList())
                .Add("upgrades", WriteUpgrades(p))
                .Add("hints", p.SeenHints.Select(h => (object)(long)h).ToList())
                .Add("hintsOff", p.HintsOff)
                .Add("starterGranted", p.StarterGranted)
                .Add("starterV119Granted", p.StarterV119Granted)
                .Add("starterUids", p.StarterUids.Select(u => (object)u).ToList())
                .Add("stats", stats)
                .Add("run", run);
        }

        private static JsonObject WriteUpgrades(Profile p)
        {
            var o = new JsonObject();
            foreach (var kv in p.Upgrades)
                if (kv.Value > 0) o.Add(Workshop.Get(kv.Key).Key, (long)kv.Value);
            return o;
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
            foreach (var r in relics) list.Add(WriteRelic(r));
            return list;
        }

        private static JsonObject WriteRelic(Relic r)
        {
            var aff = new List<object>();
            foreach (var a in r.Affixes) aff.Add(new List<object> { (long)a.Stat, (long)a.Value });
            var pw = new List<object>();
            foreach (var x in r.Powers) pw.Add(new List<object> { (long)x.Power, (long)x.Value });
            return new JsonObject()
                .Add("uid", r.Uid).Add("base", r.BaseId).Add("unique", r.UniqueId)
                .Add("rarity", (long)r.Rarity).Add("ilvl", (long)r.ItemLevel)
                .Add("enhance", (long)r.Enhance).Add("retunes", (long)r.Retunes).Add("locked", r.Locked)
                .Add("awaken", (long)r.AwakenPoints).Add("awakened", r.Awakened).Add("awakenLevel", (long)r.AwakenLevel)
                .Add("milestones", (long)r.EnhanceMilestones).Add("limitBreaks", (long)r.LimitBreaks)
                .Add("affixes", aff).Add("powers", pw);
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
            p.StartDepth = Clamp(Long(b, "startDepth"), 0, Content.MaxHeat);
            p.LastDreamDepth = Clamp(Long(b, "lastDreamDepth"), 0, DreamDepth.Maximum);
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
            ReadPendingSalvage(b, p.PendingSalvage, notes);
            p.RetuneOffer = ReadRetuneOffer(b, notes);
            if (b.TryGet("heroes", out object ho) && ho is JsonObject heroes)
            {
                foreach (var kv in heroes.Properties)
                {
                    if (!(kv.Value is JsonObject hj)) continue;
                    var h = p.Hero(kv.Key);
                    if (hj.TryGet("equipped", out object eo) && eo is List<object> eq)
                    {
                        for (int i = 0; i < Content.SlotCount && i < eq.Count; i++)
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
                            {
                                if (Rules.BelongsTo(def, kv.Key)) h.Talents[t.Key] = (int)Math.Min(def.MaxRank, rank);
                                else notes.Add($"{kv.Key}: 旅人の刻印へ移行したため汎用ノードのポイントを戻しました: {t.Key}");
                            }
                            else
                                notes.Add("未知の専門化ノードを除外: " + t.Key);
                        }
                    }
                    if (hj.TryGet("talentChoices", out object choicesObj) && choicesObj is JsonObject choices)
                        foreach (var choice in choices.Properties)
                            if (h.Talents.ContainsKey(choice.Key) && Content.TryGetTalent(choice.Key, out var def)
                                && def.IsChoice && choice.Value is long option && option >= 0 && option < def.Choices.Count)
                                h.TalentChoices[choice.Key] = (int)option;
                    // A corrupt/new choice allocation must never silently select an effect.
                    var invalidChoices = new List<string>();
                    foreach (var allocation in h.Talents)
                        if (Content.TryGetTalent(allocation.Key, out var def) && def.IsChoice
                            && !h.TalentChoices.ContainsKey(allocation.Key)) invalidChoices.Add(allocation.Key);
                    foreach (string id in invalidChoices)
                    {
                        h.Talents.Remove(id);
                        notes.Add(Loc.T("選択のない星を払い戻しました: ", "Refunded a star without a valid choice: ") + id);
                    }
                    h.Kills = Clamp(Long(hj, "kills"), 0, int.MaxValue);
                    h.StarXp = hj.TryGet("starXp", out object sx)
                        ? Clamp(sx is long xp ? xp : 0, 0, int.MaxValue)
                        : StarProgression.LegacyXp(h.Kills);
                    string key = hj.TryGet("keystone", out object ko) ? ko as string : null;
                    if (key != null && Content.TryGetTalent(key, out var kdef) && kdef.IsKeystone && Rules.BelongsTo(kdef, kv.Key)) h.Keystone = key;
                }
            }
            if (b.TryGet("codex", out object co) && co is List<object> codex)
                foreach (var c in codex)
                    if (c is string s) p.Codex.Add(s);
            if (b.TryGet("feats", out object fe) && fe is List<object> feats)
                foreach (var f in feats)
                    if (f is string id) p.Feats.Add(id);
            if (b.TryGet("featsClaimed", out object fc) && fc is List<object> featsClaimed)
                foreach (var f in featsClaimed)
                    if (f is string id) p.FeatsClaimed.Add(id);
            if (b.TryGet("hints", out object ho2) && ho2 is List<object> hints)
                foreach (var h in hints)
                    if (h is long hv && hv >= 0 && hv < 1000) p.SeenHints.Add((int)hv);
            p.HintsOff = Bool(b, "hintsOff", false);
            p.StarterGranted = Bool(b, "starterGranted", false);
            p.StarterV119Granted = Bool(b, "starterV119Granted", false);
            if (b.TryGet("starterUids", out object so2) && so2 is List<object> sus)
                foreach (var u in sus)
                    if (u is string us && p.FindStash(us) != null) p.StarterUids.Add(us);
            if (b.TryGet("upgrades", out object uo) && uo is JsonObject ups)
            {
                foreach (var kv in ups.Properties)
                {
                    if (Workshop.TryGetByKey(kv.Key, out var def) && kv.Value is long lv && lv > 0)
                        p.Upgrades[def.Id] = (int)Math.Min(def.MaxLevel, lv);
                    else if (Workshop.TryGetRetired(kv.Key, out var old) && kv.Value is long olv && olv > 0)
                    {
                        var refund = Workshop.Refund(old, (int)olv);
                        p.AddMaterial(Materials.Shard, refund.Shards);
                        p.AddMaterial(Materials.Tuning, refund.Tuning);
                        notes.Add($"廃止した工房強化「{old.Name.Ja}」の費用を返しました（欠片{refund.Shards}・調律石{refund.Tuning}）");
                    }
                    else
                        notes.Add("未知の工房強化を除外: " + kv.Key);
                }
            }
            if (b.TryGet("stats", out object so) && so is JsonObject st)
            {
                p.Stats.Runs = Clamp(Long(st, "runs"), 0, int.MaxValue);
                p.Stats.Victories = Clamp(Long(st, "victories"), 0, int.MaxValue);
                p.Stats.Defeats = Clamp(Long(st, "defeats"), 0, int.MaxValue);
                p.Stats.RelicsFound = Clamp(Long(st, "relicsFound"), 0, int.MaxValue);
                p.Stats.LegendariesFound = Clamp(Long(st, "legendariesFound"), 0, int.MaxValue);
                p.Stats.RelicsAwakened = Clamp(Long(st, "relicsAwakened"), 0, int.MaxValue);
                p.Stats.VariantsSlain = Clamp(Long(st, "variantsSlain"), 0, int.MaxValue);
                p.Stats.BestHeatSecured = Clamp(Long(st, "bestHeatSecured"), 0, Content.MaxHeat);
                p.Stats.Kills = Clamp(Long(st, "kills"), 0, int.MaxValue);
                p.Stats.NightmaresSlain = Clamp(Long(st, "nightmares"), 0, int.MaxValue);
                p.Stats.PactsSworn = Clamp(Long(st, "pactsSworn"), 0, int.MaxValue);
                p.Stats.EventsUsed = Clamp(Long(st, "eventsUsed"), 0, int.MaxValue);
                p.Stats.BountiesDone = Clamp(Long(st, "bountiesDone"), 0, int.MaxValue);
                p.Stats.BestVictoryStartDepth = st.TryGet("bestVictoryStartDepth", out object bv) && bv is long bvl ? Clamp(bvl, -1, Content.MaxHeat) : -1;
            }
            if (b.TryGet("run", out object ro) && ro is JsonObject rj)
            {
                var run = new RunState
                {
                    RunId = Str(rj, "runId"),
                    HeroKey = Str(rj, "heroKey"),
                    StarSecureRewarded = Bool(rj, "starSecureRewarded", false),
                    Heat = Loot.ClampHeat(Clamp(Long(rj, "heat"), 0, Content.MaxHeat)),
                    DreamDepth = Clamp(Long(rj, "dreamDepth"), 0, DreamDepth.Maximum),
                    ActiveWaypoint = ReadWaypoint(rj, "activeWaypoint"),
                    PendingWaypoint = ReadWaypoint(rj, "pendingWaypoint"),
                    WaypointChosen = Bool(rj, "waypointChosen", false),
                    WaypointGeneration = Clamp(Long(rj, "waypointGeneration"), 0, int.MaxValue),
                    WaypointRoom = rj.TryGet("waypointRoom", out _) ? Clamp(Long(rj, "waypointRoom"), -1, int.MaxValue) : -1,
                    WaypointRelicsInRoom = Clamp(Long(rj, "waypointRelicsInRoom"), 0, int.MaxValue),
                    WaypointSlotCursor = Clamp(Long(rj, "waypointSlotCursor"), 0, int.MaxValue),
                    DeferredWaypointShards = Clamp(Long(rj, "deferredWaypointShards"), 0, int.MaxValue),
                    DeferredWaypointTuning = Clamp(Long(rj, "deferredWaypointTuning"), 0, int.MaxValue),
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
                    StartDepth = Clamp(Long(rj, "startDepth"), 0, Content.MaxHeat),
                    RerollsUsed = Clamp(Long(rj, "rerollsUsed"), 0, 100),
                    LimboDepth = Clamp(Long(rj, "limbo"), 0, 50),
                    OfferedEvent = Enum.IsDefined(typeof(DreamEvent), (int)Long(rj, "event")) ? (DreamEvent)(int)Long(rj, "event") : DreamEvent.None,
                    EventDropBonus = Bonus(rj, "eventDropBonus"),
                    EventLuck = Bonus(rj, "eventLuck"),
                    AwaitingChoice = Bool(rj, "awaitingChoice", false),
                    GearWindow = Bool(rj, "gearWindow", false),
                };
                ReadRelics(rj, "satchel", run.Satchel, notes);
                ReadBounties(rj, run.Bounties, notes);
                ReadPacts(rj, "pacts", run.Pacts, notes);
                ReadPacts(rj, "offeredPacts", run.OfferedPacts, notes);
                ReadRelics(rj, "deferredWaypointRelics", run.DeferredWaypointRelics, notes);
                if (rj.TryGet("waypointLootRooms", out object roomsObject) && roomsObject is List<object> lootRooms)
                    foreach (var room in lootRooms)
                        if (room is long roomId && roomId >= 0 && roomId <= int.MaxValue && run.WaypointLootRooms.Count < 4096)
                            run.WaypointLootRooms.Add((int)roomId);
                if (run.DeferredWaypointRelics.Count > Waypoints.MaximumDeferredRelics)
                    run.DeferredWaypointRelics.RemoveRange(Waypoints.MaximumDeferredRelics, run.DeferredWaypointRelics.Count - Waypoints.MaximumDeferredRelics);
                if (rj.TryGet("offeredWaypoints", out object wo) && wo is List<object> waypoints)
                    foreach (var w in waypoints)
                        if (w is long id && id > 0 && id <= Waypoints.All.Count && run.OfferedWaypoints.Count < Waypoints.Offered
                            && !run.OfferedWaypoints.Contains((Waypoint)id)) run.OfferedWaypoints.Add((Waypoint)id);
                p.Run = run;
            }
            // Preserve connected allocations; refund only this Traveler when the budget or graph is invalid.
            foreach (var kv in p.Heroes)
            {
                bool overBudget = Rules.FreePoints(p, kv.Key) < 0;
                if (!overBudget && Rules.TalentsConnected(kv.Value, kv.Key)) continue;
                Rules.ResetTalents(p, kv.Key);
                notes.Add(overBudget
                    ? Loc.T($"{kv.Key}: 星の経験と刻印の費用に合わせ、刻印を無料で振り直せるようにしました。",
                        $"{kv.Key}: Star point allocation exceeded the current budget and was reset for free.")
                    : Loc.T($"{kv.Key}: 始まりの星につながらない星があったため、この旅人の星を無料で振り直せるようにしました。",
                        $"{kv.Key}: Some allocated stars were disconnected from the starting star; this Traveler's stars were reset for free."));
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
                    into.Add(ReadRelic(j, seen));
                }
                catch (LedgerFormatException ex)
                {
                    notes.Add(key + ": " + ex.Message + " → 除外");
                }
            }
        }

        private static object WriteRetuneOffer(RetuneOffer o)
        {
            if (o == null) return null;
            var opts = new List<object>();
            foreach (var a in o.Options) opts.Add(new List<object> { (long)a.Stat, (long)a.Value });
            return new JsonObject().Add("uid", o.Uid).Add("index", (long)o.Index).Add("options", opts);
        }

        private static RetuneOffer ReadRetuneOffer(JsonObject parent, List<string> notes)
        {
            if (!parent.TryGet("retuneOffer", out object o) || !(o is JsonObject j)) return null;
            var offer = new RetuneOffer { Uid = j.TryGet("uid", out object u) ? u as string : null, Index = Clamp(Long(j, "index"), 0, 16) };
            if (j.TryGet("options", out object oo) && oo is List<object> list)
            {
                foreach (var a in list)
                {
                    if (a is List<object> pair && pair.Count == 2 && pair[0] is long sid && pair[1] is long v && Enum.IsDefined(typeof(Stat), (int)sid))
                        offer.Options.Add(new StatLine((Stat)(int)sid, (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, v))));
                }
            }
            if (string.IsNullOrEmpty(offer.Uid) || offer.Options.Count == 0)
            {
                notes.Add("retuneOffer: 形式が不正 → 除外");
                return null;
            }
            return offer;
        }

        private static void ReadPendingSalvage(JsonObject parent, List<PendingSalvage> into, List<string> notes)
        {
            if (!parent.TryGet("pendingSalvage", out object o) || !(o is List<object> list)) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                try
                {
                    if (!(item is JsonObject j)) throw new LedgerFormatException("遺物の形式が不正");
                    if (!j.TryGet("returnTarget", out object target) || !(target is long value)
                        || (value != (long)SalvageReturnTarget.Stash && value != (long)SalvageReturnTarget.LostAndFound))
                        throw new LedgerFormatException("預かり品の戻し先が不正");
                    into.Add(new PendingSalvage(ReadRelic(j, seen), (SalvageReturnTarget)value));
                }
                catch (LedgerFormatException ex)
                {
                    notes.Add("pendingSalvage: " + ex.Message + " → 除外");
                }
            }
        }

        private static Relic ReadRelic(JsonObject j, HashSet<string> seen)
        {
            Rarity rarity = (Rarity)Clamp(Long(j, "rarity"), 0, (int)Rarity.Legendary);
            // 限界突破（v1.27）。古い保存には無いので0。強化値はその遺物の上限で切る。
            int limitBreaks = Clamp(Long(j, "limitBreaks"), 0, Content.MaxLimitBreaks(rarity));
            var r = new Relic
            {
                Uid = Str(j, "uid"),
                BaseId = Str(j, "base"),
                UniqueId = j.TryGet("unique", out object u) ? u as string : null,
                Rarity = rarity,
                ItemLevel = Clamp(Long(j, "ilvl"), 1, Content.MaxItemLevel),
                Enhance = Clamp(Long(j, "enhance"), 0, Content.MaxEnhanceFor(rarity, limitBreaks)),
                LimitBreaks = limitBreaks,
                Retunes = Clamp(Long(j, "retunes"), 0, Content.MaxRetunes),
                Locked = Bool(j, "locked", false),
                AwakenPoints = Clamp(Long(j, "awaken"), 0, Content.AwakenThreshold),
                AwakenLevel = j.TryGet("awakenLevel", out _)
                    ? Clamp(Long(j, "awakenLevel"), 0, Content.MaxAwakenLevel)
                    : Bool(j, "awakened", false) ? Content.LegacyAwakenLevel : 0, // v1.26 までの覚醒は覚醒Ⅱ
                EnhanceMilestones = Clamp(Long(j, "milestones"), 0, Content.MaxEnhanceMilestones),
            };
            if (string.IsNullOrEmpty(r.Uid) || !Content.TryGetBase(r.BaseId, out _))
                throw new LedgerFormatException("未知の基礎ID: " + r.BaseId);
            if (r.UniqueId != null && !Content.TryGetUnique(r.UniqueId, out _))
                throw new LedgerFormatException("未知の固有品ID: " + r.UniqueId);
            if (!seen.Add(r.Uid)) throw new LedgerFormatException("Uidの重複: " + r.Uid);
            // 段と覚醒の力をそろえる（v1.26 までの覚醒は覚醒Ⅱの累計から続ける）。
            if (r.AwakenPoints < Content.AwakenThresholdFor(r.AwakenLevel)) r.AwakenPoints = Content.AwakenThresholdFor(r.AwakenLevel);
            if (Content.AwakenLevelFor(r.AwakenPoints) > r.AwakenLevel) r.AwakenLevel = Content.AwakenLevelFor(r.AwakenPoints);
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
            return r;
        }

        private static string Str(JsonObject o, string key) => o.TryGet(key, out object v) ? v as string : null;

        private static Waypoint ReadWaypoint(JsonObject o, string key)
        {
            long value = Long(o, key);
            return value > 0 && value <= Waypoints.All.Count ? (Waypoint)value : Waypoint.None;
        }

        private static long Long(JsonObject o, string key) => o.TryGet(key, out object v) && v is long l ? l : 0;

        private static double Bonus(JsonObject o, string key)
        {
            if (!double.TryParse(Str(o, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 0;
            return value;
        }

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
