using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

// Production build receipt/deduplication and movement adapter run unchanged. The native
// skill double records only this adapter's owned bonuses; Unity/Mirror are not exercised.
namespace SodRpg.Mod
{
    internal sealed class Hero_Husk : Hero { }
    internal partial class Hero { public bool IsNullOrInactive() => !isActive; }
    internal sealed partial class ClientSession
    {
        internal static string HeroKeyOf(Hero hero) => hero.GetType().Name;
    }
    internal sealed class SkillBonus
    {
        public int addedCharge;
        internal SkillTrigger Owner;
        public void Stop() { Owner?.MovementBonuses.Remove(this); Owner = null; }
    }
    internal partial class SkillTrigger
    {
        internal readonly List<SkillBonus> MovementBonuses = new List<SkillBonus>();
        public SkillBonus AddSkillBonus(SkillBonus bonus)
        {
            bonus.Owner = this;
            MovementBonuses.Add(bonus);
            return bonus;
        }
    }
    internal sealed partial class HostAuthority
    {
        internal class ReceivedBuild
        {
            public Build Build;
            public string Encoded, Summary, HeroKey;
            public bool ApplyFailed;
        }
        internal sealed partial class HeroRuntime
        {
            public SkillBonus MovementChargeBonus;
            public SkillTrigger MovementChargeOwner;
            public int MovementChargeApplied, MovementChargeDesired;
            public bool MovementChargeBroken;
        }
        private readonly Dictionary<DewPlayer, ReceivedBuild> _builds = new Dictionary<DewPlayer, ReceivedBuild>();
        private readonly Dictionary<DewPlayer, BuildTransferReceiver> _incomingBuilds = new Dictionary<DewPlayer, BuildTransferReceiver>();
        private readonly Dictionary<Hero, float> _applyRetryAt = new Dictionary<Hero, float>();
        private bool _pressureDirty;
        internal int MovementBuildApplications, MovementBuildAcknowledgments;
        private void Apply(Hero hero, ReceivedBuild received)
        {
            if (!_runtimes.TryGetValue(hero, out var runtime))
                _runtimes.Add(hero, runtime = new HeroRuntime { Hero = hero });
            runtime.HeroKey = hero.GetType().Name;
            runtime.AppliedBuild = received;
            ApplyMovementCharges(runtime, received.Build);
            MovementBuildApplications++;
        }
        private void SendApplied(DewPlayer player, Hero hero, ReceivedBuild build) => MovementBuildAcknowledgments++;
        internal void QueueMovementBuild(DewPlayer player, string encoded)
        {
            foreach (var part in BuildTransfer.Split(encoded))
                ReceiveBuildUpdate(DreamforgeBuildMsg.FromPart(part), player);
        }
        internal void ProcessMovementBuilds(double now) => ProcessBuildUpdates(now);
        internal int MovementBonusFor(Hero hero) => _runtimes[hero].MovementChargeDesired;
    }
}

namespace SodRpg.Core.Tests
{
    public sealed class MovementChargeBuildUpdateTests
    {
        private const string HeroKey = "Hero_Husk";
        private const string FirstClusterStar = "husk.mem.killing-flow.c1.e1";

        private static (string Legacy, string Authored, string EquivalentAuthored, string Refunded) EquivalentPackets()
        {
            var profile = Profile.CreateNew(71UL);
            var hero = profile.Hero(HeroKey);
            hero.Kills = 1000000;
            hero.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            TreeTestPaths.Connect(profile, HeroKey, FirstClusterStar);
            // Both this retained choice rank and the authored entrance grant the same
            // Killing Flow damage. Swapping them preserves the complete wire summary.
            Rules.AddTalentRank(profile, HeroKey, "h.husk.dark", 0);
            string Encode() => HostBuildValidation.Encode(Build.Compute(profile, HeroKey, 0), profile, HeroKey, 0);
            string legacy = Encode();
            Rules.RemoveTalentRank(profile, HeroKey, "h.husk.dark");
            string refunded = Encode();
            Rules.AddTalentRank(profile, HeroKey, FirstClusterStar);
            string authored = Encode();
            Rules.RemoveTalentRank(profile, HeroKey, FirstClusterStar);
            Rules.AddTalentRank(profile, HeroKey, "husk.mem.killing-flow.c2.e1");
            string equivalent = Encode();
            Assert.True(HostBuildValidation.TryAccept(legacy, HeroKey, out var before, out var beforeReason), beforeReason);
            Assert.True(HostBuildValidation.TryAccept(authored, HeroKey, out var after, out var afterReason), afterReason);
            Assert.True(HostBuildValidation.TryAccept(equivalent, HeroKey, out var same, out var sameReason), sameReason);
            Assert.Equal(before.Encode(), after.Encode());
            Assert.Equal(after.Encode(), same.Encode());
            Assert.Equal(0, Build.MovementChargeBonus(HeroKey, before));
            Assert.Equal(1, Build.MovementChargeBonus(HeroKey, after));
            Assert.Equal(1, Build.MovementChargeBonus(HeroKey, same));
            return (legacy, authored, equivalent, refunded);
        }

        private static void WithHost(Action<HostAuthority, DewPlayer, SkillTrigger> test)
        {
            StarClusters.RegisterGeneratedHero(HeroKey);
            var hero = new Hero_Husk();
            var movement = new SkillTrigger();
            hero.Skill.Skills[HeroSkillLocation.Movement] = movement;
            var player = new DewPlayer { hero = hero, guid = "movement-build-peer" };
            DewPlayer.gamePlayers.Add(player);
            try { test(new HostAuthority(), player, movement); }
            finally
            {
                DewPlayer.gamePlayers.Remove(player);
                StarClusters.RegisterAuthored(HeroKey, Array.Empty<AuthoredStarDef>());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Equal_wire_summary_still_applies_changed_movement_eligibility(bool remove)
        {
            WithHost((host, player, movement) =>
            {
                var packets = EquivalentPackets();
                string first = remove ? packets.Authored : packets.Legacy;
                string second = remove ? packets.Legacy : packets.Authored;
                host.QueueMovementBuild(player, first);
                host.ProcessMovementBuilds(0);
                Assert.Equal(remove ? 1 : 0, host.MovementBonusFor(player.hero));
                // Refund and purchase inside the existing 0.5-second coalescing window:
                // the intermediate lower-point build must not mask this regression.
                host.QueueMovementBuild(player, packets.Refunded);
                host.ProcessMovementBuilds(0.1);
                Assert.Equal(1, host.MovementBuildApplications);
                host.QueueMovementBuild(player, second);
                host.ProcessMovementBuilds(0.5);
                Assert.Equal(2, host.MovementBuildApplications);
                Assert.Equal(remove ? 0 : 1, host.MovementBonusFor(player.hero));
                Assert.Equal(remove ? 0 : 1, movement.MovementBonuses.Sum(b => b.addedCharge));
                host.QueueMovementBuild(player, second);
                host.ProcessMovementBuilds(1);
                Assert.Equal(2, host.MovementBuildApplications);
                Assert.Equal(3, host.MovementBuildAcknowledgments);
            });
        }

        [Fact]
        public void Equal_summary_and_equal_movement_bonus_still_deduplicate()
        {
            WithHost((host, player, movement) =>
            {
                var packets = EquivalentPackets();
                host.QueueMovementBuild(player, packets.Authored);
                host.ProcessMovementBuilds(0);
                host.QueueMovementBuild(player, packets.EquivalentAuthored);
                host.ProcessMovementBuilds(0.5);
                host.QueueMovementBuild(player, packets.EquivalentAuthored);
                host.ProcessMovementBuilds(1);
                Assert.Equal(1, host.MovementBuildApplications);
                Assert.Equal(3, host.MovementBuildAcknowledgments);
                Assert.Single(movement.MovementBonuses);
                Assert.Equal(1, movement.MovementBonuses[0].addedCharge);
            });
        }
    }
}
