using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class EnemyBalanceCompatibilityTests
    {
        [Fact]
        public void Unadjusted_enemy_tables_preserve_the_entire_previous_content_fingerprint()
        {
            // Intentional tuning adds a record; this regression protects only the no-adjustment cutover.
            if (PressureBalance.ContentFingerprintRecord != null || MonstersBalance.ContentFingerprintRecord != null
                || InfinityBalance.ContentFingerprintRecord != null) return;
            // Use current registry identities: other full-suite tests legitimately register new caps/heroes.
            string caps = FractionalScopedModifiers.CapRegistryFingerprint;
            string authored = StarClusters.AuthoredRegistryFingerprint;
            Assert.Equal(LegacyFingerprint(caps, authored), ContentFingerprint.Value);
        }

        // Frozen pre-stage5 algorithm, not a pinned fingerprint or a second game tuning source.
        private static string LegacyFingerprint(string caps, string authored)
        {
            var ids = new List<string>();
            ids.AddRange(ForgeBalance.ContentFingerprintRecords);
            if (MemoryDamageBalance.ContentFingerprintRecord != null)
                ids.Add(MemoryDamageBalance.ContentFingerprintRecord);
            if (StarProgressionBalance.ContentFingerprintRecord != null)
                ids.Add(StarProgressionBalance.ContentFingerprintRecord);
            ids.Add(GearBalance.ContentFingerprintRecord);
            ids.Add(SetBalanceValues.ContentFingerprintRecord);
            foreach (var b in Content.Bases) ids.Add("b:" + b.Id);
            foreach (var u in Content.Uniques)
            {
                ids.Add("u:" + u.Id);
                if (u.BossMove != null) ids.Add("boss-piece:" + u.Id + ":" + u.BaseId + ":" + u.SetId + ":" + u.BossMove);
            }
            foreach (var s in Content.Sets)
            {
                ids.Add("s:" + s.Id);
                if (!string.IsNullOrEmpty(s.BossTypeName)) ids.Add("boss-source:" + s.BossTypeName + ":" + s.Id);
                if (s.BossReward != null) ids.Add("boss-set-reward:" + s.Id + ":" + s.BossReward);
                foreach (var stage in s.BossStages) ids.Add("boss-set-stage:" + s.Id + ":" + stage.RequiredPieces + ":" + stage.ProfileId);
                foreach (var stage in s.LinkStages)
                    ids.Add("set-link:" + s.Id + ":" + stage.RequiredPieces + ":" + (int)stage.Link.Kind
                        + ":" + stage.Link.ValueMilli + ":" + string.Join(",", stage.Link.Requires));
            }
            foreach (var t in Content.Talents) ids.Add("t:" + t.Id);
            foreach (var t in HeroSigils.All) ids.Add("h:" + t.Id);
            foreach (var p in PairCombos.All) ids.Add("p:" + p.Id);
            ids.Add("power:" + Enum.GetValues(typeof(Power)).Length);
            ids.Add("stat:" + Enum.GetValues(typeof(Stat)).Length);
            ids.Add("gimmick:" + Enum.GetValues(typeof(GimmickEffect)).Length);
            ids.Add("run-growth:v1:" + Enum.GetValues(typeof(RunGrowthTrigger)).Length + ":" + RunGrowth.MaxEntries);
            ids.Add(StarRankBalance.ContentFingerprintRecord);
            ids.Add("currency-stars:v1:" + Content.PowerCap(Power.KillGoldPct) + "/" + Content.PowerCap(Power.EliteKillGoldPct)
                + "/" + Content.PowerCap(Power.DreamDustPct) + "/" + Content.PowerCap(Power.DreamDustDelvePct));
            ids.Add("boss-drop:" + BossSets.NormalDropPercent + ":" + BossSets.NightmareBonusPercent
                + ":" + BossSets.DepthBonusPercent + ":" + BossSets.MaxDropDepth + ":" + BossSets.MaxDropPercent);
            ids.AddRange(BossProfiles.FingerprintRecords());
            ids.Add("boss-scaling:damage-heal-shield-only;duration-fixed;demon-channel-cap;others-post-cap-3x;milestone:" + Content.LimitBreakPowerPct
                + ";max-enhance:" + Content.EnhancePowerScalePct(Content.MaxEnhanceFor(Rarity.Legendary, Content.MaxLimitBreaks(Rarity.Legendary)))
                + ";max-awaken:" + Content.AwakenPowerPctAt(Content.MaxAwakenLevel));
            for (int i = 0; i <= Content.EnhanceMilestoneFifth; i++) ids.Add("boss-enhance:" + i + ":" + Content.EnhancePowerScalePct(i));
            for (int i = 0; i <= Content.MaxAwakenLevel; i++) ids.Add("boss-awaken:" + i + ":" + Content.AwakenPowerPctAt(i));
            ids.Add("mechanisms:v13:" + caps + "/" + authored);
            ids.Add("mechanism-memory-facts:" + VerifiedMechanismSlots.Fingerprint);
            ids.Sort(StringComparer.Ordinal);
            ulong hash = 1469598103934665603UL;
            foreach (string id in ids)
            {
                foreach (char c in id) { hash ^= c; hash *= 1099511628211UL; }
                hash ^= '\n'; hash *= 1099511628211UL;
            }
            return ids.Count + "-" + hash.ToString("x16");
        }
    }
}
