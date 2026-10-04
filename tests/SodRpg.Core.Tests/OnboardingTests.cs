using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class OnboardingTests
    {
        private static List<Hint> Hints(IEnumerable<GameEvent> ev) => ev.Where(e => e.Kind == EventKind.Hint).Select(e => e.HintId.Value).ToList();

        [Fact]
        public void Every_hint_has_text_in_both_languages()
        {
            foreach (Hint h in Enum.GetValues(typeof(Hint)))
            {
                var d = Onboarding.Get(h);
                Assert.NotNull(d);
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    Assert.False(string.IsNullOrWhiteSpace(d.Title.ToString()));
                    Assert.False(string.IsNullOrWhiteSpace(d.Body.ToString()));
                }
            }
            Loc.Japanese = true;
        }

        [Fact]
        public void Hints_show_once_and_can_be_turned_off()
        {
            var p = Profile.CreateNew(1);
            Assert.Single(Hints(Rules.HintOnce(p, Hint.Welcome)));
            Assert.Empty(Hints(Rules.HintOnce(p, Hint.Welcome)));
            p.HintsOff = true;
            Assert.Empty(Hints(Rules.HintOnce(p, Hint.FirstDrop)));
        }

        [Fact]
        public void A_first_run_walks_through_the_main_hints()
        {
            var p = Profile.CreateNew(3);
            var seen = new List<Hint>();
            seen.AddRange(Hints(Rules.BeginRun(p, "r", heroKey: "Hero_Vesper")));
            p.Run.Bounties.Clear();
            for (int i = 0; i < 6; i++) seen.AddRange(Hints(Rules.OnKill(p, MonsterTier.Boss, 5, heroKey: "Hero_Vesper")));
            seen.AddRange(Hints(Rules.ReachSecurePoint(p)));
            seen.AddRange(Hints(Rules.Delve(p)));
            seen.AddRange(Hints(Rules.ReachSecurePoint(p)));
            seen.AddRange(Hints(Rules.Secure(p)));
            seen.AddRange(Hints(Rules.OnKill(p, MonsterTier.Normal, 5, NightmareAffix.Swift)));
            seen.AddRange(Hints(Rules.EndRun(p, victory: false)));
            foreach (var h in new[] { Hint.FirstDrop, Hint.FirstSecurePoint, Hint.FirstDelve, Hint.FirstSecure, Hint.FirstNightmare, Hint.TalentPoints, Hint.FirstDefeat, Hint.ForgeReady })
                Assert.Contains(h, seen);
            Assert.Equal(seen.Count, seen.Distinct().Count()); // どれも一度だけ
        }

        [Fact]
        public void Starter_kit_is_granted_once_with_one_relic_per_slot()
        {
            var p = Profile.CreateNew(1);
            var kit = Onboarding.GrantStarterKit(p);
            Assert.Equal(Content.SlotCount, kit.Count);
            Assert.Equal(Content.SlotCount, kit.Select(r => r.Slot).Distinct().Count());
            Assert.All(kit, r => Assert.Equal(Rarity.Uncommon, r.Rarity));
            Assert.Empty(Onboarding.GrantStarterKit(p));
            Assert.Equal(Content.SlotCount, p.Stash.Count);
        }

        /// <summary>初期装備に銘品が含まれる最初のシード（なければ失敗）。</summary>
        private static ulong SeedWithNamedStarter()
        {
            for (ulong seed = 1; seed < 20000; seed++)
            {
                var p = Profile.CreateNew(seed);
                Onboarding.GrantStarterKit(p);
                if (p.Stash.Any(r => r.NamedId != null)) return seed;
            }
            throw new InvalidOperationException("銘品を含む初期装備のシードが見つからない");
        }

        [Fact]
        public void Starter_named_relic_is_recorded_in_the_codex_with_its_mini_set()
        {
            var p = Profile.CreateNew(SeedWithNamedStarter());
            var kit = Onboarding.GrantStarterKit(p);
            Assert.All(kit, r => Assert.Contains(r.CodexId, p.Codex));
            var named = kit.First(r => r.NamedId != null);
            Assert.StartsWith("n:", named.CodexId, StringComparison.Ordinal);
            Assert.Contains(NamedItems.CodexId(named.NamedId), p.Codex);

            var state = new CodexState(p.Codex, new HashSet<Power>());
            Assert.True(state.IsFound(CodexQuery.Entries(CodexCategory.Named).Single(e => e.Id == named.CodexId)));
            string miniSetId = NamedItems.TryGetNamed(named.NamedId, out var def) ? def.MiniSetId : null;
            if (miniSetId != null)
                Assert.True(state.IsFound(CodexQuery.Entries(CodexCategory.MiniSets).Single(e => e.Id == miniSetId)));
        }

        [Fact]
        public void Extra_slot_starters_also_record_named_relics_in_the_codex()
        {
            for (ulong seed = 1; seed < 20000; seed++)
            {
                var p = Profile.CreateNew(seed);
                p.StarterGranted = true; // 旧プロフィール：3枠の追加配布だけが走る
                Onboarding.GrantNewSlotStarters(p);
                var named = p.Stash.FirstOrDefault(r => r.NamedId != null);
                if (named == null) continue;
                Assert.Contains(named.CodexId, p.Codex);
                return;
            }
            Assert.Fail("銘品を含む追加3枠のシードが見つからない");
        }

        [Fact]
        public void Backfill_adds_missing_named_codex_ids_for_already_granted_starters_once()
        {
            var p = Profile.CreateNew(SeedWithNamedStarter());
            Onboarding.GrantStarterKit(p);
            // 旧版の状態：土台IDだけが載り、銘品IDがない
            var namedIds = p.Stash.Where(r => r.NamedId != null).Select(r => r.CodexId).ToList();
            foreach (var id in namedIds) p.Codex.Remove(id);
            foreach (var r in p.Stash.Where(r => r.NamedId != null)) p.Codex.Add(r.BaseId);
            Assert.True(Onboarding.BackfillStarterCodex(p));
            Assert.All(namedIds, id => Assert.Contains(id, p.Codex));
            Assert.False(Onboarding.BackfillStarterCodex(p)); // 二度目は何も足さない
            Assert.Equal(Content.SlotCount, p.StarterUids.Count); // 二重配布はしない
            Assert.Empty(Onboarding.GrantStarterKit(p));
        }

        [Fact]
        public void Starter_kit_auto_equips_only_into_an_empty_traveler()
        {
            var p = Profile.CreateNew(1);
            Onboarding.GrantStarterKit(p);
            Assert.True(Onboarding.AutoEquipStarter(p, "Hero_Vesper"));
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
                Assert.Equal(slot, p.FindStash(p.Hero("Hero_Vesper").Equipped[(int)slot]).Slot);
            Assert.False(Onboarding.AutoEquipStarter(p, "Hero_Vesper")); // もう装備済み

            var other = Loot.RollRelic(new Rng(77), Rarity.Rare, 5, Slot.Weapon);
            p.Stash.Add(other);
            Rules.Equip(p, "Hero_Mist", other.Uid);
            Assert.False(Onboarding.AutoEquipStarter(p, "Hero_Mist")); // 自分で装備したキャラには触らない
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
                if (slot != Slot.Weapon) Assert.Null(p.Hero("Hero_Mist").Equipped[(int)slot]);
        }

        [Fact]
        public void Onboarding_state_roundtrips()
        {
            var p = Profile.CreateNew(1);
            Onboarding.GrantStarterKit(p);
            Rules.HintOnce(p, Hint.Welcome);
            Rules.HintOnce(p, Hint.FirstDrop);
            p.HintsOff = true;
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.True(q.StarterGranted);
            Assert.True(q.StarterV119Granted);
            Assert.Equal(p.StarterUids, q.StarterUids);
            Assert.Equal(p.SeenHints, q.SeenHints);
            Assert.True(q.HintsOff);
        }

        [Fact]
        public void Async_writer_coalesces_and_writes_the_latest_revision()
        {
            var fs = new InMemoryFileSystem();
            var store = new ProfileStore(fs, "/s/p.json", 1);
            var p = store.Load();
            var w = new AsyncProfileWriter(store);
            for (int i = 0; i < 50; i++)
            {
                p.AddMaterial(Materials.Shard, 1);
                w.Enqueue(p);
            }
            Assert.True(w.Flush());
            Assert.Null(w.LastError);
            Assert.Equal(p.Revision, w.WrittenRevision);
            var q = new ProfileStore(fs, "/s/p.json", 1).Load();
            Assert.Equal(50, q.Material(Materials.Shard));
            Assert.Equal(p.Revision, q.Revision);
        }

        [Fact]
        public void Async_writer_reports_errors_and_recovers()
        {
            var mem = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(mem);
            var store = new ProfileStore(fs, "/s/p.json", 1);
            var p = store.Load();
            var w = new AsyncProfileWriter(store);
            fs.Arm(0, FaultMode.IoError);
            w.Enqueue(p);
            Assert.True(w.Flush());
            Assert.NotNull(w.LastError);
            fs.Disarm();
            w.Enqueue(p);
            Assert.True(w.Flush());
            Assert.Null(w.LastError);
            Assert.Equal(p.Revision, new ProfileStore(mem, "/s/p.json", 1).Load().Revision);
        }
    }
}
