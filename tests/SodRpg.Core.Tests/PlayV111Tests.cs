using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.11：新しい夢の出来事と偉業。</summary>
    public class PlayV111Tests
    {
        private static Profile AtEvent(DreamEvent e, ulong seed = 5)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "v111");
            p.Run.Bounties.Clear();
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = e;
            return p;
        }

        private static Relic Give(Profile p, Rarity r)
        {
            var relic = Loot.RollRelic(new Rng((ulong)(p.Run.Satchel.Count + 11)), r, 5);
            p.Run.Satchel.Add(relic);
            return relic;
        }

        [Fact]
        public void Forge_shrine_spends_shards_and_enhances_the_best_relic()
        {
            var p = AtEvent(DreamEvent.ForgeShrine);
            Assert.False(DreamEvents.CanUse(p, DreamEvent.ForgeShrine, out _)); // 欠片も遺物もない
            Give(p, Rarity.Common);
            var best = Give(p, Rarity.Epic);
            p.Run.SatchelShards = 45;
            Rules.UseEvent(p, DreamEvent.ForgeShrine);
            Assert.Equal(1, best.Enhance);
            Assert.Equal(5, p.Run.SatchelShards);
            Assert.Equal(DreamEvent.None, p.Run.OfferedEvent);
        }

        [Fact]
        public void Twin_mirror_copies_type_and_rarity_and_downgrades_legendaries()
        {
            var p = AtEvent(DreamEvent.TwinMirror);
            var leg = Loot.RollRelic(new Rng(3), Rarity.Legendary, 5);
            p.Run.Satchel.Add(leg);
            p.Run.SatchelShards = 60;
            Rules.UseEvent(p, DreamEvent.TwinMirror);
            Assert.Equal(2, p.Run.Satchel.Count);
            var copy = p.Run.Satchel.Single(r => r.Uid != leg.Uid);
            Assert.Equal(leg.BaseId, copy.BaseId);
            Assert.Equal(Rarity.Epic, copy.Rarity);
            Assert.Equal(0, p.Run.SatchelShards);
        }

        [Fact]
        public void Stargazer_and_lucky_star_last_until_the_next_secure()
        {
            var p = AtEvent(DreamEvent.Stargazer);
            Rules.UseEvent(p, DreamEvent.Stargazer);
            Assert.Equal(0.3, p.Run.EventDropBonus, 3);
            p.Run.OfferedEvent = DreamEvent.LuckyStar;
            Rules.UseEvent(p, DreamEvent.LuckyStar);
            Assert.Equal(0.4, p.Run.EventLuck, 3);
            Assert.True(Rules.KillModifiers(p.Run).DropBonus >= 0.3);
            Rules.Secure(p);
            Assert.Equal(0, p.Run.EventDropBonus);
            Assert.Equal(0, p.Run.EventLuck);
        }

        [Fact]
        public void Cauldron_melts_three_low_relics_into_one_better_one()
        {
            var p = AtEvent(DreamEvent.Cauldron);
            Give(p, Rarity.Common);
            Give(p, Rarity.Common);
            Assert.False(DreamEvents.CanUse(p, DreamEvent.Cauldron, out _));
            Give(p, Rarity.Uncommon);
            var keep = Give(p, Rarity.Epic);
            Rules.UseEvent(p, DreamEvent.Cauldron);
            Assert.Equal(2, p.Run.Satchel.Count);
            Assert.Contains(keep, p.Run.Satchel);
            Assert.Contains(p.Run.Satchel, r => r.Uid != keep.Uid && r.Rarity >= Rarity.Rare);
        }

        [Fact]
        public void Tapir_eats_relics_below_epic_for_salvage_value()
        {
            // v1.15：分解と同じ欠片（保管庫へ直接、潜行ボーナスの対象外）。3つごとに調律石1。エピック以上は食べない。
            var p = AtEvent(DreamEvent.Tapir);
            for (int i = 0; i < 4; i++) Give(p, Rarity.Common);
            var epic = Give(p, Rarity.Epic);
            int before = p.Material(Materials.Shard);
            Rules.UseEvent(p, DreamEvent.Tapir);
            Assert.Single(p.Run.Satchel);
            Assert.Contains(epic, p.Run.Satchel);
            Assert.Equal(before + 4 * Content.SalvageShards(Rarity.Common), p.Material(Materials.Shard));
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.Equal(1, p.Run.SatchelTuning);
        }

        [Fact]
        public void Courage_gate_deepens_the_delve_and_archive_grants_experience()
        {
            var p = AtEvent(DreamEvent.CourageGate);
            int heat = p.Run.Heat;
            Rules.UseEvent(p, DreamEvent.CourageGate);
            Assert.Equal(heat + 1, p.Run.Heat);
            Assert.Equal(40, p.Run.SatchelShards);

            var q = AtEvent(DreamEvent.Archive);
            int xp = q.DreamXp, lv = q.DreamLevel;
            Rules.UseEvent(q, DreamEvent.Archive);
            Assert.True(q.DreamLevel > lv || q.DreamXp > xp);
        }

        [Fact]
        public void Feats_are_recorded_then_claimed_once()
        {
            var p = Profile.CreateNew(7);
            Rules.BeginRun(p, "feat");
            var ev = new List<GameEvent>();
            for (int i = 0; i < 100; i++) ev.AddRange(Rules.OnKill(p, MonsterTier.Lesser, 1));
            Assert.Contains("feat.kills.1", p.Feats);
            Assert.Contains(ev, e => e.Text.Contains("夢の狩人見習い") || e.Text.Contains("Apprentice Hunter"));
            int unclaimed = Feats.Unclaimed(p);
            Assert.True(unclaimed >= 1);
            int before = p.Material(Materials.Shard);
            Rules.ClaimFeat(p, "feat.kills.1");
            Assert.Equal(before + 30, p.Material(Materials.Shard));
            Assert.Equal(unclaimed - 1, Feats.Unclaimed(p));
            Assert.Throws<InvalidOperationException>(() => Rules.ClaimFeat(p, "feat.kills.1"));
            Assert.Throws<InvalidOperationException>(() => Rules.ClaimFeat(p, "feat.kills.4"));
        }

        [Fact]
        public void Feats_and_new_counters_survive_save_and_load()
        {
            var p = Profile.CreateNew(8);
            p.Feats.Add("feat.runs.1");
            p.Feats.Add("feat.kills.1");
            p.FeatsClaimed.Add("feat.runs.1");
            p.Stats.PactsSworn = 3;
            p.Stats.EventsUsed = 4;
            p.Stats.BountiesDone = 5;
            Rules.BeginRun(p, "save");
            p.Run.EventDropBonus = 0.5;
            p.Run.EventLuck = 0.6;
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(p.Feats, q.Feats);
            Assert.Equal(p.FeatsClaimed, q.FeatsClaimed);
            Assert.Equal(3, q.Stats.PactsSworn);
            Assert.Equal(4, q.Stats.EventsUsed);
            Assert.Equal(5, q.Stats.BountiesDone);
            Assert.Equal(0.5, q.Run.EventDropBonus, 3);
            Assert.Equal(0.6, q.Run.EventLuck, 3);
        }

        [Fact]
        public void Every_feat_has_text_and_a_reward()
        {
            Assert.Equal(90, Feats.All.Count);
            Assert.Equal(Feats.All.Count, Feats.All.Select(f => f.Id).Distinct().Count());
            foreach (var f in Feats.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(f.Name.ToString()));
                Assert.False(string.IsNullOrWhiteSpace(Feats.Describe(f)));
                Assert.True(f.RewardShards > 0);
            }
        }
    }
}
