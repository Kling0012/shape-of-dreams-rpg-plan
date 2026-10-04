using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class LinkActivationV127Tests
    {
        private static LinkDef Link(LinkKind kind, int value, string memory) =>
            new LinkDef { Kind = kind, Value = value, Requires = new[] { memory } };

        [Fact]
        public void Same_memory_haste_adds_once_and_never_exceeds_full_cooldown()
        {
            var a = Link(LinkKind.MemoryHaste, 40, "St_Q_Fleche");
            var b = Link(LinkKind.MemoryHaste, 35, "St_Q_Fleche");
            var other = Link(LinkKind.MemoryHaste, 80, "St_R_Parry");
            Assert.Equal(75, PowerRuntime.LinkHastePercent(new[] { a, b, other }, "St_Q_Fleche"));
            Assert.Equal(80, PowerRuntime.LinkHastePercent(new[] { a, b, other }, "St_R_Parry"));
            Assert.Equal(0, PowerRuntime.LinkHastePercent(new[] { a, b }, "St_R_Parry"));
            b.ValueMilli = int.MaxValue;
            Assert.Equal(100, PowerRuntime.LinkHastePercent(new[] { a, b }, "St_Q_Fleche"));
            Assert.Equal(100, PowerRuntime.LinkHastePercent(new[] { b, a }, "St_Q_Fleche"));
        }

        [Fact]
        public void Unequipping_strong_surge_preserves_only_the_still_equipped_source()
        {
            var strong = Link(LinkKind.MemorySurge, 40, "St_Q_Fleche");
            var weak = Link(LinkKind.MemorySurge, 12, "St_R_Parry");
            var runtime = new PowerRuntime(new Build(), 0);
            runtime.OnLinkSurge(0, strong);
            runtime.OnLinkSurge(1, weak);
            Assert.Equal(40, runtime.Current(2).AttackPct);
            Assert.Equal(40, runtime.Current(2).PowerPct);
            runtime.RetainLinkSurges(new[] { weak }, 2);
            Assert.Equal(12, runtime.Current(2).AttackPct);
            runtime.RetainLinkSurges(Array.Empty<LinkDef>(), 3);
            Assert.Equal(0, runtime.Current(3).PowerPct);
            // Re-equipping without casting cannot resurrect the cancelled surge.
            runtime.RetainLinkSurges(new[] { strong, weak }, 4);
            Assert.Equal(0, runtime.Current(4).AttackPct);
        }

        [Fact]
        public void Surge_refresh_is_per_source_and_build_change_clears_it()
        {
            var strong = Link(LinkKind.MemorySurge, 40, "St_Q_Fleche");
            var weak = Link(LinkKind.MemorySurge, 12, "St_R_Parry");
            var runtime = new PowerRuntime(new Build(), 0);
            runtime.OnLinkSurge(0, strong);
            runtime.OnLinkSurge(4, weak);
            Assert.Equal(12, runtime.Current(5).AttackPct);
            runtime.OnLinkSurge(8, weak);
            Assert.Equal(12, runtime.Current(10).PowerPct);
            Assert.Equal(0, runtime.Current(13).PowerPct);
            runtime.OnLinkSurge(14, strong);
            runtime.SetBuild(new Build());
            Assert.Equal(0, runtime.Current(14).AttackPct);
        }
    }
}
