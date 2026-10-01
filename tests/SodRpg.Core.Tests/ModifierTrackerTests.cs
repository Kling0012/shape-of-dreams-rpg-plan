using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>計画書 4章 A-1〜A-6 の純C#側の確認。ゲームの EntityStatus は FakeStats で代用する。</summary>
    public class ModifierTrackerTests
    {
        private sealed class FakeStats : IStatTarget
        {
            public readonly Dictionary<string, ModifierSpec> Active = new Dictionary<string, ModifierSpec>();
            public int ApplyCalls;
            public int RemoveCalls;

            public void ApplyModifier(ModifierSpec spec)
            {
                ApplyCalls++;
                Active.Add(spec.Id, spec); // 二重追加は例外にして検出する
            }

            public void RemoveModifier(string modifierId)
            {
                RemoveCalls++;
                Assert.True(Active.Remove(modifierId), "付いていない補正を外そうとした");
            }

            public double Stat(string key, double baseValue)
            {
                double add = Active.Values.Where(m => m.StatKey == key && m.Mode == ModifierMode.Add).Sum(m => m.Value);
                double mul = Active.Values.Where(m => m.StatKey == key && m.Mode == ModifierMode.Multiply).Aggregate(1.0, (a, m) => a * m.Value);
                return (baseValue + add) * mul;
            }
        }

        private static readonly Catalog Cat = PrototypeCatalog.Create();
        private static readonly ModifierSpec Charm = Cat.TryGetItem(PrototypeCatalog.CharmItemId, out var d) ? d.Modifiers[0] : null;

        [Fact]
        public void A1_ApplyChangesTheStatAndRemoveRestoresIt()
        {
            var stats = new FakeStats();
            var tracker = new ModifierTracker(stats);

            tracker.Apply(Charm);
            Assert.Equal(110, stats.Stat(PrototypeCatalog.TestStatKey, 100));

            tracker.Remove(Charm.Id);
            Assert.Equal(100, stats.Stat(PrototypeCatalog.TestStatKey, 100));
        }

        [Fact]
        public void A2_ApplyingTwiceDoesNotStackAndRemovingTwiceIsHarmless()
        {
            var stats = new FakeStats();
            var tracker = new ModifierTracker(stats);

            tracker.Apply(Charm);
            tracker.Apply(Charm);
            Assert.Equal(110, stats.Stat(PrototypeCatalog.TestStatKey, 100));
            Assert.Equal(1, stats.ApplyCalls);

            tracker.Remove(Charm.Id);
            tracker.Remove(Charm.Id);
            Assert.Equal(1, stats.RemoveCalls);
        }

        [Fact]
        public void A2_ChangedValueForTheSameIdReplacesTheOldOne()
        {
            var stats = new FakeStats();
            var tracker = new ModifierTracker(stats);
            tracker.Apply(Charm);

            tracker.Apply(new ModifierSpec(Charm.Id, Charm.StatKey, ModifierMode.Add, 25));

            Assert.Equal(125, stats.Stat(PrototypeCatalog.TestStatKey, 100));
            Assert.Single(stats.Active);
        }

        [Fact]
        public void A4_RemoveAllLeavesNothingBehindAndSparesOthersModifiers()
        {
            var stats = new FakeStats();
            var foreign = new ModifierSpec("othermod:buff", PrototypeCatalog.TestStatKey, ModifierMode.Add, 3);
            stats.ApplyModifier(foreign); // 他MODが付けた補正（このtrackerは追跡しない）
            var tracker = new ModifierTracker(stats);
            tracker.Apply(Charm);

            tracker.RemoveAll(); // OnDestroy 相当

            Assert.Empty(tracker.AppliedIds);
            Assert.Equal(new[] { "othermod:buff" }, stats.Active.Keys.ToArray());
        }

        [Fact]
        public void A3_ReloadAppliesTheModifierOnlyIfTheItemIsOwnedAndEquipped()
        {
            var ledger = new LedgerState("p1");
            var equipped = new[] { PrototypeCatalog.CharmItemId };

            // 所持していなければ、装着の選択があっても付かない
            var stats = new FakeStats();
            var tracker = new ModifierTracker(stats);
            tracker.Reconcile(CharmRules.DesiredModifiers(ledger, equipped, Cat));
            Assert.Empty(stats.Active);

            // 所持していても、装着していなければ付かない
            ledger.AddItem(new ItemRecord(PrototypeCatalog.CharmItemId, "i1", ""));
            tracker.Reconcile(CharmRules.DesiredModifiers(ledger, new string[0], Cat));
            Assert.Empty(stats.Active);

            // 所持して装着していれば、何度ロードし直しても1回だけ
            for (int i = 0; i < 3; i++)
                tracker.Reconcile(CharmRules.DesiredModifiers(ledger, equipped, Cat));
            Assert.Equal(110, stats.Stat(PrototypeCatalog.TestStatKey, 100));
            Assert.Equal(1, stats.ApplyCalls);
        }

        [Fact]
        public void A5_ReconnectRebuildsFromScratchWithoutLeftovers()
        {
            var ledger = new LedgerState("p1");
            ledger.AddItem(new ItemRecord(PrototypeCatalog.CharmItemId, "i1", ""));
            var equipped = new[] { PrototypeCatalog.CharmItemId };

            var stats = new FakeStats();
            var tracker = new ModifierTracker(stats);
            tracker.Reconcile(CharmRules.DesiredModifiers(ledger, equipped, Cat));

            // 切断: 追跡側を破棄して全除去 → 再接続で作り直し
            tracker.RemoveAll();
            Assert.Empty(stats.Active);
            var rebuilt = new ModifierTracker(stats);
            rebuilt.Reconcile(CharmRules.DesiredModifiers(ledger, equipped, Cat));

            Assert.Equal(110, stats.Stat(PrototypeCatalog.TestStatKey, 100));
            Assert.Single(stats.Active);
        }

        [Fact]
        public void Reconcile_RemovesModifiersThatAreNoLongerWanted()
        {
            var ledger = new LedgerState("p1");
            ledger.AddItem(new ItemRecord(PrototypeCatalog.CharmItemId, "i1", ""));
            var stats = new FakeStats();
            var tracker = new ModifierTracker(stats);
            tracker.Reconcile(CharmRules.DesiredModifiers(ledger, new[] { PrototypeCatalog.CharmItemId }, Cat));

            tracker.Reconcile(CharmRules.DesiredModifiers(ledger, new string[0], Cat)); // 装着解除

            Assert.Empty(stats.Active);
        }
    }
}
