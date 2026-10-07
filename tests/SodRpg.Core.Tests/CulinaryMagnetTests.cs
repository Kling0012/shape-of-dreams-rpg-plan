using System;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class CulinaryMagnetTests
    {
        private static MagnetTarget At(float x, float z = 0f) => new MagnetTarget(x, 0f, z);

        [Theory]
        [InlineData("Pickup_Ingredient", true)]
        [InlineData("Pickup_CulinaryIngredient", true)]
        [InlineData("Pickup_Food", true)]
        [InlineData("Pickup_DreamDust", false)]
        [InlineData("Pickup_RegenOrb", false)]
        [InlineData("Pickup_BaseGoldOrb", false)]
        [InlineData("Ingredient", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IngredientTypeNameOnlyMatchesIngredientPickups(string name, bool expected) =>
            Assert.Equal(expected, CulinaryMagnet.IsIngredientTypeName(name));

        [Fact]
        public void NearestLivingTravelerIsChosen()
        {
            var targets = new[] { At(8f), At(3f), At(5f) };
            Assert.Equal(1, CulinaryMagnet.SelectTarget(0f, 0f, 0f, targets));
        }

        [Fact]
        public void NobodyInRangeMeansNoPull()
        {
            Assert.Equal(-1, CulinaryMagnet.SelectTarget(0f, 0f, 0f, new[] { At(CulinaryMagnet.StartRadius + 0.1f) }));
            Assert.Equal(-1, CulinaryMagnet.SelectTarget(0f, 0f, 0f, Array.Empty<MagnetTarget>()));
            Assert.Equal(-1, CulinaryMagnet.SelectTarget(0f, 0f, 0f, null));
        }

        [Fact]
        public void RangeEdgeIsInclusiveAndTiesGoToTheLowerIndex()
        {
            Assert.Equal(0, CulinaryMagnet.SelectTarget(0f, 0f, 0f, new[] { At(CulinaryMagnet.StartRadius) }));
            Assert.Equal(0, CulinaryMagnet.SelectTarget(0f, 0f, 0f, new[] { At(4f), At(-4f) }));
        }

        [Fact]
        public void NonFinitePositionsAreIgnored()
        {
            var targets = new[] { At(float.NaN), At(2f) };
            Assert.Equal(1, CulinaryMagnet.SelectTarget(0f, 0f, 0f, targets));
            Assert.Equal(-1, CulinaryMagnet.SelectTarget(float.NaN, 0f, 0f, new[] { At(1f) }));
        }

        [Fact]
        public void SpeedRampsUpAndIsCapped()
        {
            Assert.Equal(CulinaryMagnet.MinSpeed, CulinaryMagnet.Speed(0f));
            Assert.Equal(CulinaryMagnet.MinSpeed, CulinaryMagnet.Speed(-1f));
            Assert.True(CulinaryMagnet.Speed(0.2f) > CulinaryMagnet.Speed(0.1f));
            Assert.Equal(CulinaryMagnet.MaxSpeed, CulinaryMagnet.Speed(100f));
        }

        [Fact]
        public void StepMovesTowardTargetWithoutOvershooting()
        {
            var next = CulinaryMagnet.Step(0f, 0f, 0f, At(10f), 0.1f, 0f);
            Assert.Equal(CulinaryMagnet.MinSpeed * 0.1f, next.X, 3);
            Assert.Equal(0f, next.Z, 3);
            var arrived = CulinaryMagnet.Step(9.9f, 0f, 0f, At(10f), 0.1f, 0f);
            Assert.Equal(10f, arrived.X);
        }

        [Fact]
        public void StepFollowsADiagonalAndHeight()
        {
            var next = CulinaryMagnet.Step(0f, 3f, 0f, new MagnetTarget(6f, 0f, 8f), 0.1f, 0f);
            Assert.True(next.X > 0f && next.Z > next.X && next.Y < 3f);
        }

        [Fact]
        public void StepWithBadInputLeavesThePickupWhereItIs()
        {
            var next = CulinaryMagnet.Step(1f, 2f, 3f, At(float.NaN), 0.1f, 0f);
            Assert.Equal((1f, 2f, 3f), (next.X, next.Y, next.Z));
            next = CulinaryMagnet.Step(1f, 2f, 3f, At(10f), 0f, 0f);
            Assert.Equal((1f, 2f, 3f), (next.X, next.Y, next.Z));
            next = CulinaryMagnet.Step(1f, 2f, 3f, At(10f), float.PositiveInfinity, 0f);
            Assert.Equal((1f, 2f, 3f), (next.X, next.Y, next.Z));
        }

        [Fact]
        public void AFarPickupReachesTheTravelerWithinAFewSeconds()
        {
            float x = 0f, pulled = 0f;
            var target = At(CulinaryMagnet.StartRadius);
            int ticks = 0;
            while (x < CulinaryMagnet.StartRadius && ticks < 1000)
            {
                x = CulinaryMagnet.Step(x, 0f, 0f, target, 0.05f, pulled).X;
                pulled += 0.05f;
                ticks++;
            }
            Assert.Equal(CulinaryMagnet.StartRadius, x);
            Assert.True(ticks * 0.05f < 2f, "pulled in under 2 s");
        }
    }
}
