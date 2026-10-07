using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DreamEssencesTests
    {
        [Fact]
        public void Candidate_effects_at_every_quality_stay_within_existing_caps()
        {
            foreach (var def in DreamEssences.All)
                for (int quality = DreamEssences.MinQuality; quality <= DreamEssences.MaxQuality; quality++)
                {
                    var effect = DreamEssences.ToGimmick(def, quality);
                    Assert.True(Gimmicks.ValidDef(effect), def.Id);
                    Assert.InRange(effect.Value, 0, Gimmicks.Cap(effect.Effect));
                }
        }

        [Fact]
        public void Unknown_candidate_and_out_of_range_quality_cannot_produce_an_effect()
        {
            Assert.Null(DreamEssences.Find("unknown"));
            Assert.Null(DreamEssences.ToGimmick(null, 1));
            var shelter = DreamEssences.Find("seed_of_shelter");
            Assert.Null(DreamEssences.ToGimmick(shelter, 0));
            Assert.Null(DreamEssences.ToGimmick(shelter, 4));
        }
    }
}
