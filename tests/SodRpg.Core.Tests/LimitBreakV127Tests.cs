using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.27：限界突破。強化の上限を、レア度に応じて +10〜+20 まで広げる。</summary>
    public class LimitBreakV127Tests
    {
        private static (Profile P, Relic R) WithRelic(Rarity rarity, int seed = 7, Slot slot = Slot.Weapon)
        {
            var p = Profile.CreateNew((ulong)seed);
            Relic r;
            do { r = Loot.RollRelic(new Rng((ulong)seed++), rarity, 5, slot); }
            while (rarity == Rarity.Legendary && r.Powers.Count == 0); // v1.29：レジェンドはセットの部位（固有効果なし）も引くので、評価には普通の固有品を使う
            p.Stash.Add(r);
            p.AddMaterial(Materials.Shard, 100000);
            p.AddMaterial(Materials.Tuning, 100);
            return (p, r);
        }

        private static Relic AddRelic(Profile p, Rarity rarity, Slot slot, int seed)
        {
            var rng = new Rng((ulong)seed);
            Relic m;
            do { m = Loot.RollRelic(rng, rarity, 5, slot); }
            while (p.FindStash(m.Uid) != null); // Fixtures must represent distinct physical relics.

            p.Stash.Add(m);
            return m;
        }

        /// <summary>限界突破の回数だけ進めて、強化をその時の上限まで上げる。</summary>
        private static void MaxOut(Profile p, Relic r)
        {
            r.Enhance = Content.MaxEnhanceFor(r);
            Rules.GrantEnhanceMilestones(new Rng(7), r);
        }

        [Fact]
        public void Break_limits_follow_rarity_and_extend_the_cap_in_steps_of_five()
        {
            Assert.Equal(0, Content.MaxLimitBreaks(Rarity.Common));
            Assert.Equal(0, Content.MaxLimitBreaks(Rarity.Uncommon));
            Assert.Equal(1, Content.MaxLimitBreaks(Rarity.Rare));
            Assert.Equal(2, Content.MaxLimitBreaks(Rarity.Epic));
            Assert.Equal(3, Content.MaxLimitBreaks(Rarity.Legendary));

            Assert.Equal(5, Content.MaxEnhanceFor(Rarity.Rare, 0));
            Assert.Equal(10, Content.MaxEnhanceFor(Rarity.Rare, 1));
            Assert.Equal(10, Content.MaxEnhanceFor(Rarity.Rare, 3)); // レアの回数上限で切れる
            Assert.Equal(15, Content.MaxEnhanceFor(Rarity.Epic, 2));
            Assert.Equal(20, Content.MaxEnhanceFor(Rarity.Legendary, 3));
            Assert.Equal(5, Content.MaxEnhanceFor(Rarity.Legendary, -1)); // 負の回数は0扱い

            var (p, r) = WithRelic(Rarity.Rare, 61);
            Assert.Equal(5, Content.MaxEnhanceFor(r));
            r.LimitBreaks = 1;
            Assert.Equal(10, Content.MaxEnhanceFor(r));
        }

        [Fact]
        public void Break_costs_tuning_and_shards_per_attempt()
        {
            Assert.Equal(5, Content.LimitBreakTuningCost(1));
            Assert.Equal(10, Content.LimitBreakTuningCost(2));
            Assert.Equal(20, Content.LimitBreakTuningCost(3));
            Assert.Equal(200, Content.LimitBreakShardCost(1));
            Assert.Equal(400, Content.LimitBreakShardCost(2));
            Assert.Equal(800, Content.LimitBreakShardCost(3));

            // +6以降の強化の費用（+6〜+10。+11〜+15は1.5倍、+16〜+20は2倍）
            int[] base5 = { 180, 230, 290, 360, 440 };
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(base5[i], Content.EnhanceCost(5 + i));
                Assert.Equal(base5[i] * 3 / 2, Content.EnhanceCost(10 + i));
                Assert.Equal(base5[i] * 2, Content.EnhanceCost(15 + i));
            }
            Assert.Equal(int.MaxValue, Content.EnhanceCost(20));
            Assert.Equal(int.MaxValue, Content.EnhanceCost(99));
            // +5までの費用は変わらない
            Assert.Equal(20 + 35 + 60 + 90 + 130, Enumerable.Range(0, 5).Sum(i => Content.EnhanceCost(i)));
        }

        [Fact]
        public void Break_consumes_the_material_and_charges_both_currencies()
        {
            var (p, r) = WithRelic(Rarity.Epic, 11);
            MaxOut(p, r);
            var m = AddRelic(p, Rarity.Epic, Slot.Weapon, 12);
            p.Materials[Materials.Tuning] = 0;
            p.Materials[Materials.Shard] = 0;

            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, m.Uid)); // 調律石不足
            p.AddMaterial(Materials.Tuning, 10);
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, m.Uid)); // 欠片不足
            p.AddMaterial(Materials.Shard, 400);

            var e = Rules.LimitBreak(p, r.Uid, m.Uid);
            Assert.Equal(EventKind.LevelUp, e.Kind);
            Assert.Equal(1, r.LimitBreaks);
            Assert.Equal(0, p.Material(Materials.Tuning));
            Assert.Equal(0, p.Material(Materials.Shard));
            Assert.Null(p.FindStash(m.Uid)); // 素材は消える
            Assert.Equal(10, Content.MaxEnhanceFor(r));

            // 2回目は調律石20・欠片800
            p.AddMaterial(Materials.Shard, 10000);
            MaxOut(p, r);
            var m2 = AddRelic(p, Rarity.Epic, Slot.Weapon, 13);
            p.Materials[Materials.Tuning] = 20;
            p.Materials[Materials.Shard] = 800;
            Rules.LimitBreak(p, r.Uid, m2.Uid);
            Assert.Equal(2, r.LimitBreaks);
            Assert.Equal(0, p.Material(Materials.Tuning));
            Assert.Equal(0, p.Material(Materials.Shard));

            // エピックは2回まで。上限に達したら材料があってもできない
            p.AddMaterial(Materials.Shard, 10000);
            MaxOut(p, r);
            var m3 = AddRelic(p, Rarity.Epic, Slot.Weapon, 14);
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, m3.Uid));
            Assert.Equal(2, r.LimitBreaks);
            Assert.NotNull(p.FindStash(m3.Uid));
        }

        [Fact]
        public void Materials_need_the_same_slot_and_rarity_and_must_be_usable()
        {
            var (p, r) = WithRelic(Rarity.Rare, 21);
            MaxOut(p, r);
            var sameSlotRare = AddRelic(p, Rarity.Rare, Slot.Weapon, 31);
            var sameSlotEpic = AddRelic(p, Rarity.Epic, Slot.Weapon, 32);
            var otherSlot = AddRelic(p, Rarity.Rare, Slot.Charm, 33);
            var lower = AddRelic(p, Rarity.Uncommon, Slot.Weapon, 34);
            var locked = AddRelic(p, Rarity.Rare, Slot.Weapon, 35);
            locked.Locked = true;
            var equipped = AddRelic(p, Rarity.Rare, Slot.Weapon, 36);
            p.Hero("Hero_Lacerta").Equipped[(int)Slot.Weapon] = equipped.Uid;
            var offered = AddRelic(p, Rarity.Rare, Slot.Weapon, 37);
            Rules.Retune(p, offered.Uid, 0); // 再調律の候補中

            Assert.Equal(new[] { sameSlotRare.Uid, sameSlotEpic.Uid },
                Rules.LimitBreakCandidates(p, r).Select(x => x.Uid)); // 使える物だけ・弱い順
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, otherSlot.Uid));
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, lower.Uid));
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, locked.Uid));
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, equipped.Uid));
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, offered.Uid));
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p, r.Uid, r.Uid)); // 自分自身は素材にできない

            // 上限に達していない遺物は限界突破できない
            var (p2, r2) = WithRelic(Rarity.Rare, 41);
            var m = AddRelic(p2, Rarity.Rare, Slot.Weapon, 42);
            Rules.Enhance(p2, r2.Uid); // +1
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p2, r2.Uid, m.Uid));

            // コモン・アンコモンは限界突破できない
            var (p3, c) = WithRelic(Rarity.Common, 43);
            MaxOut(p3, c);
            Assert.Empty(Rules.LimitBreakCandidates(p3, c));
            Assert.Throws<InvalidOperationException>(() => Rules.LimitBreak(p3, c.Uid, null));
        }

        [Fact]
        public void Enhancement_checkpoints_scale_relic_stats_and_powers()
        {
            Assert.Equal(126, Content.EnhanceScalePct(5));
            Assert.Equal(140, Content.EnhanceScalePct(10));
            Assert.Equal(150, Content.EnhanceScalePct(15));
            Assert.Equal(156, Content.EnhanceScalePct(20));
            Assert.Equal(156, Content.EnhanceScalePct(99));

            Assert.Equal(120, Content.EnhancePowerScalePct(5));
            Assert.Equal(132, Content.EnhancePowerScalePct(10));
            Assert.Equal(140, Content.EnhancePowerScalePct(15));
            Assert.Equal(145, Content.EnhancePowerScalePct(20));
            Assert.Equal(145, Content.EnhancePowerScalePct(99));

            // 遺物を通した伸びも同じ式
            var (p, lg) = WithRelic(Rarity.Legendary, 51);
            lg.Enhance = 20;
            lg.LimitBreaks = 3;
            var aff = lg.Affixes[0];
            var stat = lg.EffectiveStats().First(s => s.Stat == aff.Stat);
            Assert.Equal((aff.Value * 156 + 50) / 100, stat.Value);
            var pw = lg.Powers[0];
            var power = lg.EffectivePowers().First(x => x.Power == pw.Power);
            Assert.Equal((pw.Value * 145 + 50) / 100, power.Value);

            // 強化は限界突破後の上限まで進み、そこで止まる
            var (p2, r2) = WithRelic(Rarity.Rare, 52);
            MaxOut(p2, r2);
            Rules.LimitBreak(p2, r2.Uid, AddRelic(p2, Rarity.Rare, Slot.Weapon, 53).Uid);
            MaxOut(p2, r2);
            Assert.Equal(10, r2.Enhance);
            Assert.Throws<InvalidOperationException>(() => Rules.Enhance(p2, r2.Uid));
        }

        [Fact]
        public void Plus_ten_and_fifteen_add_affixes_and_twenty_boosts_one_power()
        {
            // レアは1回突破して +10。節目3で特性が1行増える
            var (p, r) = WithRelic(Rarity.Rare, 61);
            MaxOut(p, r);
            Rules.LimitBreak(p, r.Uid, AddRelic(p, Rarity.Rare, Slot.Weapon, 62).Uid);
            int affixes = r.Affixes.Count;
            Assert.Equal(2, r.EnhanceMilestones); // +3 と +5
            MaxOut(p, r);
            Assert.Equal(3, r.EnhanceMilestones);
            Assert.Equal(affixes + 1, r.Affixes.Count);
            Assert.Throws<InvalidOperationException>(() => Rules.Enhance(p, r.Uid)); // レアは+10まで。節目4には届かない
            Assert.Equal(3, r.EnhanceMilestones);

            // エピックは2回突破して +15。+10 と +15 で特性が1行ずつ増える
            var (p2, ep) = WithRelic(Rarity.Epic, 63);
            MaxOut(p2, ep);
            Rules.LimitBreak(p2, ep.Uid, AddRelic(p2, Rarity.Epic, Slot.Weapon, 64).Uid);
            int epicAffixes = ep.Affixes.Count;
            Assert.Equal(2, ep.EnhanceMilestones); // +3 と +5
            MaxOut(p2, ep); // +10 → 節目3
            Assert.Equal(3, ep.EnhanceMilestones);
            Assert.Equal(epicAffixes + 1, ep.Affixes.Count);
            Rules.LimitBreak(p2, ep.Uid, AddRelic(p2, Rarity.Epic, Slot.Weapon, 65).Uid);
            MaxOut(p2, ep); // +15 → 節目4
            Assert.Equal(4, ep.EnhanceMilestones);
            Assert.Equal(epicAffixes + 2, ep.Affixes.Count);
            Assert.Equal(15, ep.Enhance);

            // 伝説は3回突破して +20。節目5で固有効果1つの値が1.2倍
            var (p3, lg) = WithRelic(Rarity.Legendary, 66);
            MaxOut(p3, lg);
            int power0 = lg.Powers[0].Value, power1 = lg.Powers[1].Value;
            for (int i = 0; i < 3; i++)
            {
                Rules.LimitBreak(p3, lg.Uid, AddRelic(p3, Rarity.Legendary, Slot.Weapon, 70 + i).Uid);
                MaxOut(p3, lg);
            }
            Assert.Equal(20, lg.Enhance);
            Assert.Equal(5, lg.EnhanceMilestones);
            // The source boost is applied before the aggregate cap. The second power is unchanged.
            Assert.Equal((power0 * 120 + 50) / 100, lg.Powers[0].Value);
            Assert.Equal(power1, lg.Powers[1].Value);
        }

        [Fact]
        public void Fountain_and_forge_shrine_enhance_to_the_relics_own_cap()
        {
            var p = Profile.CreateNew(71);
            Rules.BeginRun(p, "lb");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            var target = Loot.RollRelic(new Rng(71), Rarity.Rare, 5, Slot.Weapon);
            target.Enhance = 5;
            target.LimitBreaks = 1;
            target.EnhanceMilestones = 2;
            var filler = Loot.RollRelic(new Rng(72), Rarity.Common, 1, Slot.Charm);
            p.Run.Satchel.Add(target);
            p.Run.Satchel.Add(filler);
            p.Run.SatchelShards = 100;

            p.Run.OfferedEvent = DreamEvent.ForgeShrine;
            Assert.True(DreamEvents.CanUse(p, DreamEvent.ForgeShrine, out _));
            Rules.UseEvent(p, DreamEvent.ForgeShrine);
            Assert.Equal(6, target.Enhance); // その遺物の今の上限（+10）まで強化できる

            p.Run.OfferedEvent = DreamEvent.Fountain;
            target.Enhance = 9;
            Assert.True(DreamEvents.CanUse(p, DreamEvent.Fountain, out _));
            Rules.UseEvent(p, DreamEvent.Fountain);
            Assert.Equal(10, target.Enhance);
            Assert.Equal(3, target.EnhanceMilestones); // +10 の節目

            // 上限に達したら、どちらの出来事も使えない
            Assert.False(DreamEvents.CanUse(p, DreamEvent.ForgeShrine, out _));
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Fountain, out _));
        }

        [Fact]
        public void Save_keeps_limit_breaks_and_old_saves_load_as_zero()
        {
            var (p, r) = WithRelic(Rarity.Rare, 81);
            MaxOut(p, r);
            Rules.LimitBreak(p, r.Uid, AddRelic(p, Rarity.Rare, Slot.Weapon, 82).Uid);
            MaxOut(p, r);

            var back = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var loaded = back.FindStash(r.Uid);
            Assert.Equal(1, loaded.LimitBreaks);
            Assert.Equal(10, loaded.Enhance);
            Assert.Equal(r.EnhanceMilestones, loaded.EnhanceMilestones);

            // v1.27 より前の保存（limitBreaks の鍵なし）は0。強化値は +5 で切る
            var legacy = Reread(ProfileCodec.Write(p), body => StripKey(body, "limitBreaks"));
            var oldR = legacy.FindStash(r.Uid);
            Assert.Equal(0, oldR.LimitBreaks);
            Assert.Equal(5, oldR.Enhance);
            Assert.Equal(3, oldR.EnhanceMilestones); // 節目の数はそのまま（保存時に既に +10 まで強化していたので3）
        }

        [Fact]
        public void Load_clamps_enhance_breaks_and_milestones_to_their_caps()
        {
            var (p, r) = WithRelic(Rarity.Rare, 85);
            MaxOut(p, r);
            Rules.LimitBreak(p, r.Uid, AddRelic(p, Rarity.Rare, Slot.Weapon, 86).Uid); // 上限 +10

            // 保存を書き換えて、上限を超える強化値・回数・節目を入れる
            var q = Reread(ProfileCodec.Write(p), body => EditRelic(body, r.Uid, relic =>
            {
                relic["enhance"] = 12;
                relic["limitBreaks"] = 9;
                relic["milestones"] = 9;
            }));
            var loaded = q.FindStash(r.Uid);
            Assert.Equal(10, loaded.Enhance); // その遺物の上限（レアは1回までなので +10）
            Assert.Equal(1, loaded.LimitBreaks);
            Assert.Equal(5, loaded.EnhanceMilestones);

            // 突破していない遺物は +5 で切れる
            var q2 = Reread(ProfileCodec.Write(p), body => EditRelic(body, r.Uid, relic =>
            {
                relic["enhance"] = 7;
                relic["limitBreaks"] = 0;
            }));
            Assert.Equal(5, q2.FindStash(r.Uid).Enhance);
        }

        [Theory]
        [InlineData(20, 4)]
        [InlineData(20, 5)]
        [InlineData(0, 5)]
        public void Legacy_milestone_power_is_not_applied_twice(int enhance, int milestones)
        {
            var (p, r) = WithRelic(Rarity.Legendary, 91);
            r.LimitBreaks = 3;
            r.Enhance = enhance;
            r.EnhanceMilestones = milestones;
            var first = r.Powers[0];
            int boosted = (first.Value * 120 + 50) / 100;
            r.Powers[0] = new PowerLine(first.Power, boosted);
            var legacy = Reread(ProfileCodec.Write(p), body => StripKey(body, "milestonePowerApplied"));
            var loaded = legacy.FindStash(r.Uid);
            Assert.True(loaded.MilestonePowerApplied);
            Assert.Equal(5, loaded.EnhanceMilestones);
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.Equal(0, Rules.ApplyEnhanceMilestones(legacy));
            loaded.Enhance = 20;
            Assert.Null(Rules.GrantEnhanceMilestones(new Rng(91), loaded));
            Assert.Equal(boosted, loaded.Powers[0].Value);
        }

        [Fact]
        public void Explicit_power_history_restores_wire_milestone_evidence()
        {
            var (p, r) = WithRelic(Rarity.Legendary, 92);
            r.LimitBreaks = 3;
            r.Enhance = 20;
            Rules.GrantEnhanceMilestones(new Rng(92), r);
            int boosted = r.Powers[0].Value;
            var loadedProfile = Reread(ProfileCodec.Write(p), body => EditRelic(body, r.Uid, relic =>
            {
                relic["enhance"] = 0L;
                relic["milestones"] = 4L;
            }));
            var loaded = loadedProfile.FindStash(r.Uid);
            Assert.True(loaded.MilestonePowerApplied);
            Assert.Equal(5, loaded.EnhanceMilestones);
            Assert.Equal(boosted, loaded.Powers[0].Value);
            Assert.Equal(0, Rules.ApplyEnhanceMilestones(loadedProfile));
        }

        // ── 保存の書き換え補助（チェックサムを作り直す） ──

        private static Profile Reread(string save, Func<object, object> edit)
        {
            var root = (JsonObject)Json.Parse(save);
            Assert.True(root.TryGet("body", out object bodyObj));
            var body = edit(bodyObj);
            string checksum;
            using (var sha = SHA256.Create())
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(body))))
                    .Replace("-", "").ToLowerInvariant();
            return ProfileCodec.Read(Json.Write(new JsonObject().Add("format", ProfileCodec.Format)
                .Add("version", (long)Profile.CurrentVersion).Add("checksum", checksum).Add("body", body)), new List<string>());
        }

        private static object StripKey(object value, string key)
        {
            if (value is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var kv in obj.Properties)
                    if (kv.Key != key) copy.Add(kv.Key, StripKey(kv.Value, key));
                return copy;
            }
            if (value is List<object> list) return list.Select(v => StripKey(v, key)).ToList();
            return value;
        }

        private static object EditRelic(object value, string uid, Action<Dictionary<string, object>> edit)
        {
            if (value is JsonObject obj)
            {
                var copy = new JsonObject();
                var dict = new Dictionary<string, object>();
                foreach (var kv in obj.Properties) dict[kv.Key] = EditRelic(kv.Value, uid, edit);
                if (dict.TryGetValue("uid", out object id) && (string)id == uid) edit(dict);
                foreach (var kv in dict) copy.Add(kv.Key, kv.Value);
                return copy;
            }
            if (value is List<object> list) return list.Select(v => EditRelic(v, uid, edit)).ToList();
            return value;
        }
    }
}
