using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 遊びの内容（星・遺物・固有効果など）の指紋。同じ通信の版でも内容が違う MOD どうしを見分けるために使う
    /// 内容のIDと機構・上限・検証済み記憶属性から決まる。登録が変われば同じ通信の版でも再交渉する。
    /// </summary>
    public static class ContentFingerprint
    {
        private static string _value;
        private static string _capIdentity, _authoredIdentity;
        private static readonly object Gate = new object();

        public static string Value
        {
            get
            {
                string caps = FractionalScopedModifiers.CapRegistryFingerprint;
                string authored = StarClusters.AuthoredRegistryFingerprint;
                lock (Gate)
                {
                    if (_value == null || _capIdentity != caps || _authoredIdentity != authored)
                    {
                        _value = Compute(caps, authored);
                        _capIdentity = caps;
                        _authoredIdentity = authored;
                    }
                    return _value;
                }
            }
        }

        private static string Compute(string caps, string authored)
        {
            var ids = new List<string>();
            // Default forge values contribute no new records; stage0 tuple identity remains compatible.
            ids.AddRange(ForgeBalance.ContentFingerprintRecords);
            if (MemoryDamageBalance.ContentFingerprintRecord != null)
                ids.Add(MemoryDamageBalance.ContentFingerprintRecord);
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
            // v1.32: currency stars and RunGrowth. Registered growth definitions are part of the authored-registry fingerprint.
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
            ulong hash = 1469598103934665603UL; // FNV-1a 64
            foreach (string id in ids)
            {
                foreach (char c in id)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
                hash ^= '\n';
                hash *= 1099511628211UL;
            }
            return ids.Count + "-" + hash.ToString("x16");
        }

        /// <summary>相手の版と内容が自分と同じか。null（相手が古い版で送ってこない）は一致とみなさない。</summary>
        public static bool Matches(int protocol, string content, int expectedProtocol) =>
            protocol == expectedProtocol && string.Equals(content, Value, StringComparison.Ordinal);
    }
}
