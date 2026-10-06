using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SodRpg.Core;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed class RelicRowCacheTests : IDisposable
    {
        private readonly bool _japanese = Loc.Japanese;
        private readonly float _time = Time.unscaledTime;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "relic-row-cache-" + Guid.NewGuid().ToString("N"));
        private ClientSession _session;

        public void Dispose()
        {
            _session?.FlushSaves();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            Loc.Japanese = _japanese;
            Time.unscaledTime = _time;
        }

        [Theory]
        [InlineData(false, 410f, 0.1f)]
        [InlineData(false, 410f, 0.31f)]
        [InlineData(false, 431f, 0.1f)]
        [InlineData(false, 431f, 0.31f)]
        [InlineData(true, 410f, 0.1f)]
        [InlineData(true, 410f, 0.31f)]
        [InlineData(true, 431f, 0.1f)]
        [InlineData(true, 431f, 0.31f)]
        public void Memory_well_refreshes_warmed_gear_and_forge_rows(bool japanese, float id, float delay)
        {
            Loc.Japanese = japanese;
            var profile = Profile.CreateNew(176);
            var relic = Loot.RollRelic(new Rng(123), Rarity.Epic, 10, Slot.Weapon);
            Assert.False(relic.HasFixedPowers);
            profile.Stash.Add(relic);
            profile.Hero("hero").Equipped[(int)relic.Slot] = relic.Uid;
            Rules.BeginRun(profile, "row-cache", heroKey: "hero");
            profile.Run.Bounties.Clear();
            int wellCost = SodRpg.Core.Tests.EventBalanceTestData.Number("memoryWell", "epicTuning");
            profile.AddMaterial(Materials.Tuning, Math.Max(100, wellCost));
            DreamforgeUi ui = null;
            _session = new ClientSession(e => ui.Notify(e)) { Profile = profile };
            typeof(ClientSession).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_session, new ProfileStore(new RealFileSystem(), Path.Combine(_directory, "profile.json"), 176));
            ui = new DreamforgeUi(_session);
            Time.unscaledTime = 100;

            string beforeName = relic.DisplayName;
            Assert.Contains(beforeName, Assert.Single(ui.CachedRelicRows(profile.Stash, id)));
            var beforePower = relic.Powers[0].Power;
            int beforeCount = relic.Powers.Count;
            var beforeRarity = relic.Rarity;
            var beforeScore = relic.Score;
            int beforeTuning = profile.Material(Materials.Tuning);

            // The event button's production sequence; no artificial profile/row invalidation.
            profile.Run.OfferedEvent = DreamEvent.MemoryWell;
            foreach (var e in Rules.UseEvent(profile, DreamEvent.MemoryWell)) _session.Emit(e);
            _session.MarkDirty(false);
            _session.SaveNow();
            Assert.NotEqual(beforePower, relic.Powers[0].Power);
            Assert.Equal(beforeCount, relic.Powers.Count);
            Assert.Equal(beforeRarity, relic.Rarity);
            Assert.Equal(beforeScore, relic.Score);
            Assert.Equal(beforeTuning - wellCost, profile.Material(Materials.Tuning));
            Assert.NotEqual(beforeName, relic.DisplayName);

            // The first read can occur before or after the 0.3-second sorting refresh.
            Time.unscaledTime = 100 + delay;
            string row = Assert.Single(ui.CachedRelicRows(profile.Stash, id));
            Assert.Contains(relic.DisplayName, row);
            Assert.DoesNotContain(beforeName, row);
            Time.unscaledTime = 101;
            Assert.Same(row, Assert.Single(ui.CachedRelicRows(profile.Stash, id)));
        }

        [Theory]
        [InlineData(Materials.Shard, 410f)]
        [InlineData(Materials.Shard, 431f)]
        [InlineData(Materials.Tuning, 410f)]
        [InlineData(Materials.Tuning, 431f)]
        public void Material_only_changes_reuse_sort_and_row_caches(string material, float id)
        {
            var profile = Profile.CreateNew(176);
            var relic = Loot.RollRelic(new Rng(123), Rarity.Epic, 10, Slot.Weapon);
            profile.Stash.Add(relic);
            var ui = new DreamforgeUi(new ClientSession { Profile = profile });
            int enumerations = 0;
            IEnumerable<Relic> Source()
            {
                enumerations++;
                foreach (var item in profile.Stash) yield return item;
            }
            Time.unscaledTime = 100;
            string before = Assert.Single(ui.CachedRelicRows(Source(), id));
            Assert.Equal(1, enumerations);
            profile.AddMaterial(material, 500);
            Time.unscaledTime = 100.1f;
            Assert.Same(before, Assert.Single(ui.CachedRelicRows(Source(), id)));
            Assert.Equal(1, enumerations);
            Time.unscaledTime = 100.31f;
            Assert.Same(before, Assert.Single(ui.CachedRelicRows(Source(), id)));
            Assert.Equal(2, enumerations); // Only the existing periodic sorting refresh.
        }

        [Fact]
        public void Relic_without_powers_keeps_a_stable_cached_title()
        {
            var profile = Profile.CreateNew(176);
            var relic = Loot.RollRelic(new Rng(123), Rarity.Common, 10, Slot.Weapon);
            Assert.Empty(relic.Powers);
            profile.Stash.Add(relic);
            var ui = new DreamforgeUi(new ClientSession { Profile = profile });
            Time.unscaledTime = 100;
            string before = Assert.Single(ui.CachedRelicRows(profile.Stash, 410f));
            Assert.Contains(relic.DisplayName, before);
            Time.unscaledTime = 100.1f;
            Assert.Same(before, Assert.Single(ui.CachedRelicRows(profile.Stash, 410f)));
        }
    }
}
