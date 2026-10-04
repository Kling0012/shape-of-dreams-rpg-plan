using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>初期装備に銘品が選ばれたとき、銘品・所属組が図鑑に載ること（Issue #29）。</summary>
    public class StarterNamedCodexTests
    {
        private static CodexState State(Profile p) => new CodexState(p.Codex, CodexQuery.KnownPowers(p));

        /// <summary>初期配布（6枠）に銘品が入る乱数seedを探す。見つからなければ試験データの前提が崩れている。</summary>
        private static Profile NewProfileWithNamedStarter()
        {
            for (ulong seed = 1; seed < 2000; seed++)
            {
                var p = Profile.CreateNew(seed);
                var kit = Onboarding.GrantStarterKit(p);
                if (kit.Any(r => r.NamedId != null)) return Profile.CreateNew(seed);
            }
            throw new InvalidOperationException("銘品を含む初期配布のseedが見つからない");
        }

        /// <summary>旧プロフィール（3枠のみ配布済み）で、追加3枠に銘品が入るseedを探す。</summary>
        private static Profile LegacyProfileWithNamedExtraStarter(out List<string> extraCodexIds)
        {
            for (ulong seed = 1; seed < 2000; seed++)
            {
                var p = Profile.CreateNew(seed);
                p.StarterGranted = true;
                p.BestItemLevel = 1;
                var before = p.Stash.Select(r => r.Uid).ToHashSet();
                var probe = p.Clone();
                Onboarding.GrantNewSlotStarters(probe);
                var given = probe.Stash.Where(r => !before.Contains(r.Uid)).ToList();
                if (given.Any(r => r.NamedId != null))
                {
                    extraCodexIds = given.Select(r => r.CodexId).ToList();
                    return p;
                }
            }
            throw new InvalidOperationException("銘品を含む追加配布のseedが見つからない");
        }

        [Fact]
        public void Starter_kit_registers_named_relics_by_codex_id_and_discovers_the_mini_set()
        {
            var p = NewProfileWithNamedStarter();
            var kit = Onboarding.GrantStarterKit(p);
            Assert.All(kit, r => Assert.Contains(r.CodexId, p.Codex));
            foreach (var named in kit.Where(r => r.NamedId != null))
            {
                Assert.DoesNotContain(named.BaseId, p.Codex); // 土台IDでは登録しない
                Assert.StartsWith("n:", named.CodexId);
                Assert.True(NamedItems.TryGetNamed(named.NamedId, out var def));
                var set = CodexQuery.Entries(CodexCategory.MiniSets).First(e => e.NamedPieces.Any(n => n.Id == def.Id));
                Assert.True(State(p).IsFound(set));
                Assert.True(State(p).IsFound(CodexQuery.Entries(CodexCategory.Named).First(e => e.Id == named.CodexId)));
            }
        }

        [Fact]
        public void Starter_kit_keeps_ordinary_gear_registered_by_base_id()
        {
            var p = NewProfileWithNamedStarter();
            var kit = Onboarding.GrantStarterKit(p);
            var ordinary = kit.Where(r => r.NamedId == null).ToList();
            Assert.NotEmpty(ordinary);
            Assert.All(ordinary, r => Assert.Contains(r.BaseId, p.Codex));
            Assert.Empty(Onboarding.GrantStarterKit(p)); // 二重配布しない
        }

        [Fact]
        public void Named_starter_stays_discovered_after_save_and_reload()
        {
            var p = NewProfileWithNamedStarter();
            var kit = Onboarding.GrantStarterKit(p);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.All(kit, r => Assert.Contains(r.CodexId, q.Codex));
            Assert.Equal(0, Onboarding.BackfillStarterCodex(q));
        }

        [Fact]
        public void Owned_named_starter_gets_no_undiscovered_boost_but_other_named_items_do()
        {
            var p = NewProfileWithNamedStarter();
            var kit = Onboarding.GrantStarterKit(p);
            Assert.True(NamedItems.TryGetNamed(kit.First(r => r.NamedId != null).NamedId, out var owned));
            var other = NamedItems.All.First(n => n.Rarity == owned.Rarity && n.Id != owned.Id);
            var baseOwned = Content.GetBase(owned.BaseId);
            var baseOther = Content.GetBase(other.BaseId);

            int withCodex = Loot.NamedWeight(owned, baseOwned, null, p.Codex, p.Stash, null);
            int withoutCodex = Loot.NamedWeight(owned, baseOwned, null, new HashSet<string>(), p.Stash, null);
            Assert.Equal(withoutCodex, withCodex * NamedItems.NotInCodexMultiplier);

            int otherWeight = Loot.NamedWeight(other, baseOther, null, p.Codex, null, null);
            Assert.Equal(NamedItems.RarityWeight(other.Rarity) * NamedItems.NotInCodexMultiplier, otherWeight);
        }

        [Fact]
        public void Extra_slot_starters_register_named_relics_by_codex_id()
        {
            var p = LegacyProfileWithNamedExtraStarter(out var expected);
            Onboarding.GrantNewSlotStarters(p);
            Assert.All(expected, id => Assert.Contains(id, p.Codex));
            Assert.Contains(expected, id => id.StartsWith("n:", StringComparison.Ordinal));
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.All(expected, id => Assert.Contains(id, q.Codex));
        }

        [Fact]
        public void Backfill_repairs_old_data_that_registered_named_starters_by_base_id()
        {
            var p = NewProfileWithNamedStarter();
            var kit = Onboarding.GrantStarterKit(p);
            // 旧版の登録を再現：銘品の発見記録を消し、土台IDだけ載せる。
            foreach (var r in kit.Where(r => r.NamedId != null))
            {
                p.Codex.Remove(r.CodexId);
                p.Codex.Add(r.BaseId);
            }
            int missing = kit.Count(r => r.NamedId != null);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(missing, Onboarding.BackfillStarterCodex(q));
            Assert.All(kit, r => Assert.Contains(r.CodexId, q.Codex));
            Assert.Equal(0, Onboarding.BackfillStarterCodex(q)); // 二度目は何もしない
        }

        [Fact]
        public void Backfill_ignores_starters_that_are_no_longer_owned()
        {
            var p = NewProfileWithNamedStarter();
            var kit = Onboarding.GrantStarterKit(p);
            var gone = kit.First(r => r.NamedId != null);
            p.Codex.Remove(gone.CodexId);
            p.Stash.RemoveAll(r => r.Uid == gone.Uid); // 分解済みなど
            Assert.Equal(0, Onboarding.BackfillStarterCodex(p));
            Assert.DoesNotContain(gone.CodexId, p.Codex);
        }
    }
}
