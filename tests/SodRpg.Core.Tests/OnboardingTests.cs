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
            seen.AddRange(Hints(Rules.BeginRun(p, "r")));
            p.Run.Bounties.Clear();
            for (int i = 0; i < 6; i++) seen.AddRange(Hints(Rules.OnKill(p, MonsterTier.Boss, 5)));
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
