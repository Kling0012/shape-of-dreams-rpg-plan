using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class CulinaryPickupSnapTests
    {
        [Fact]
        public void SnapsOnlyWhenTheLivingOwnerIsFarAway()
        {
            Assert.True(CulinaryPickupSnap.ShouldSnap(true, CulinaryPickupSnap.SnapDistance + 0.1f));
            Assert.True(CulinaryPickupSnap.ShouldSnap(true, 300f));
            Assert.False(CulinaryPickupSnap.ShouldSnap(true, CulinaryPickupSnap.SnapDistance));
            Assert.False(CulinaryPickupSnap.ShouldSnap(true, 0.5f));
        }

        [Fact]
        public void NeverSnapsToADeadOwnerOrABadDistance()
        {
            Assert.False(CulinaryPickupSnap.ShouldSnap(false, 300f));
            Assert.False(CulinaryPickupSnap.ShouldSnap(true, float.NaN));
            Assert.False(CulinaryPickupSnap.ShouldSnap(true, float.PositiveInfinity));
        }
    }
}
