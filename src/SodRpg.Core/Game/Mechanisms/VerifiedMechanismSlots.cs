using System;
using System.Collections.Generic;
using System.Text;

namespace SodRpg.Core.Game
{
    public enum VerifiedMechanismMemoryCategory { Identity, Movement, Normal, Ultimate }

    /// <summary>Allocation-time possibilities only; actual native equipment remains authoritative.</summary>
    public static class VerifiedMechanismSlots
    {
        private readonly struct Entry
        {
            public Entry(string owner, VerifiedMechanismMemoryCategory category) { Owner = owner; Category = category; }
            public string Owner { get; }
            public VerifiedMechanismMemoryCategory Category { get; }
        }

        private static readonly IReadOnlyList<MechanismMemorySlot> None = Array.Empty<MechanismMemorySlot>();
        private static readonly IReadOnlyList<MechanismMemorySlot> Identity = Array.AsReadOnly(new[] { MechanismMemorySlot.Identity });
        private static readonly IReadOnlyList<MechanismMemorySlot> Movement = Array.AsReadOnly(new[] { MechanismMemorySlot.Movement });
        // EditSkillManager.HandleSkillToSkill and HeroSkill.EquipSkill/CanReplaceSkill permit
        // swaps among Q/W/E/R without a category restriction, but lock Identity and Movement.
        private static readonly IReadOnlyList<MechanismMemorySlot> Editable = Array.AsReadOnly(new[]
        {
            MechanismMemorySlot.Q, MechanismMemorySlot.W, MechanismMemorySlot.E, MechanismMemorySlot.R
        });

        // Checked against RawData/ja-JP/memories.json: rarity, type, traveler and
        // travelerMemoryLocation. Scope is the 62 HeroStarRoutes memories and the
        // AuthoredStarContract common-memory whitelist, not an inferred ID prefix.
        // Normal/Ultimate describes native category; original Q/R/QR is not a slot lock.
        private static readonly Dictionary<string, Entry> Memories = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            ["St_D_Resolve"] = new Entry("Hero_Vesper", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_MercyOfEl"] = new Entry("Hero_Vesper", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_Charge"] = new Entry("Hero_Vesper", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_CruelSun"] = new Entry("Hero_Vesper", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_Discipline"] = new Entry("Hero_Vesper", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_BaptismOfSun"] = new Entry("Hero_Vesper", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_SanctuaryOfEl"] = new Entry("Hero_Vesper", VerifiedMechanismMemoryCategory.Ultimate),
            ["St_D_DoubleTap"] = new Entry("Hero_Lacerta", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_SalamanderPowder"] = new Entry("Hero_Lacerta", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_NimbleDodge"] = new Entry("Hero_Lacerta", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_HandCannon"] = new Entry("Hero_Lacerta", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_IncendiaryRounds"] = new Entry("Hero_Lacerta", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_PrecisionShot"] = new Entry("Hero_Lacerta", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_QuickTrigger"] = new Entry("Hero_Lacerta", VerifiedMechanismMemoryCategory.Normal),
            ["St_D_IcyVeins"] = new Entry("Hero_Cetus", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_ChargedAnguillian"] = new Entry("Hero_Cetus", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_FrostyCharge"] = new Entry("Hero_Cetus", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_EmbracingTheChill"] = new Entry("Hero_Cetus", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_BigBorealChunk"] = new Entry("Hero_Cetus", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_BackOff"] = new Entry("Hero_Cetus", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_FrozenFists"] = new Entry("Hero_Cetus", VerifiedMechanismMemoryCategory.Normal),
            ["St_D_ConvergencePoint"] = new Entry("Hero_Yubar", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_ExoticMatter"] = new Entry("Hero_Yubar", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_Flicker"] = new Entry("Hero_Yubar", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_EtherealInfluence"] = new Entry("Hero_Yubar", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_SuperNova"] = new Entry("Hero_Yubar", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_Cataclysm"] = new Entry("Hero_Yubar", VerifiedMechanismMemoryCategory.Ultimate),
            ["St_R_Tranquility"] = new Entry("Hero_Yubar", VerifiedMechanismMemoryCategory.Normal),
            ["St_D_TheKillingFlow"] = new Entry("Hero_Husk", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_ScarOfTheWind"] = new Entry("Hero_Husk", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_FlashStep"] = new Entry("Hero_Husk", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_Laceration"] = new Entry("Hero_Husk", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_DeathMark"] = new Entry("Hero_Husk", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_AnnihilationStance"] = new Entry("Hero_Husk", VerifiedMechanismMemoryCategory.Ultimate),
            ["St_R_Deception"] = new Entry("Hero_Husk", VerifiedMechanismMemoryCategory.Normal),
            ["St_D_AstridsMasterpieceEnGarde"] = new Entry("Hero_Mist", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_AstridsMasterpiecePriorite"] = new Entry("Hero_Mist", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_FastFeet"] = new Entry("Hero_Mist", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_Fleche"] = new Entry("Hero_Mist", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_Lunge"] = new Entry("Hero_Mist", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_Parry"] = new Entry("Hero_Mist", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_UnbreakableDetermination"] = new Entry("Hero_Mist", VerifiedMechanismMemoryCategory.Ultimate),
            ["St_D_HeartOfThePack"] = new Entry("Hero_Nachia", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_CircleOfLife"] = new Entry("Hero_Nachia", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_DreamyWaltz"] = new Entry("Hero_Nachia", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_SylvanCall"] = new Entry("Hero_Nachia", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_MoonlightPact"] = new Entry("Hero_Nachia", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_NaturesWhisper"] = new Entry("Hero_Nachia", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_SerpentineBlessing"] = new Entry("Hero_Nachia", VerifiedMechanismMemoryCategory.Ultimate),
            ["St_D_DisintegratingClaw"] = new Entry("Hero_Aurena", VerifiedMechanismMemoryCategory.Identity),
            ["St_D_BeautifulThreat"] = new Entry("Hero_Aurena", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_FeatheryDash"] = new Entry("Hero_Aurena", VerifiedMechanismMemoryCategory.Movement),
            ["St_Q_GoldenBurst"] = new Entry("Hero_Aurena", VerifiedMechanismMemoryCategory.Normal),
            ["St_Q_Reduction"] = new Entry("Hero_Aurena", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_DangerousTheory"] = new Entry("Hero_Aurena", VerifiedMechanismMemoryCategory.Normal),
            ["St_R_ChainReaction"] = new Entry("Hero_Aurena", VerifiedMechanismMemoryCategory.Ultimate),
            ["St_D_PrismaticEyes"] = new Entry("Hero_Bismuth", VerifiedMechanismMemoryCategory.Identity),
            ["St_M_Sprint"] = new Entry("Hero_Bismuth", VerifiedMechanismMemoryCategory.Movement),
            ["St_QR_Innocence"] = new Entry("Hero_Bismuth", VerifiedMechanismMemoryCategory.Normal),
            ["St_QR_InfernalTales"] = new Entry("Hero_Bismuth", VerifiedMechanismMemoryCategory.Normal),
            ["St_QR_ValiantHeart"] = new Entry("Hero_Bismuth", VerifiedMechanismMemoryCategory.Normal),
            ["St_QR_DistortedMind"] = new Entry("Hero_Bismuth", VerifiedMechanismMemoryCategory.Normal),
            ["St_C_GlacialStomp"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_FlashFreeze"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_BeamOfLight"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_Purgatory"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_L_SmallMoltenCore"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_SparklingWaterGun"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_Pew"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_Starfall"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_DarkBolt"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_C_MassProtection"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_E_MassCleanse"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_L_CoinExplosion"] = new Entry(null, VerifiedMechanismMemoryCategory.Normal),
            ["St_U_ShoutOfOblivion"] = new Entry(null, VerifiedMechanismMemoryCategory.Ultimate),
        };
        private static readonly Dictionary<string, IReadOnlyList<string>> HeroMemories = CreateHeroMemories();
        public static readonly string Fingerprint = CreateFingerprint();

        private static Dictionary<string, IReadOnlyList<string>> CreateHeroMemories()
        {
            var heroes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Memories) if (row.Value.Owner != null) heroes.Add(row.Value.Owner);
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (string hero in heroes)
            {
                var memories = new List<string>();
                foreach (var row in Memories)
                    if (row.Value.Owner == hero || row.Value.Owner == null && AuthoredStarContract.IsVerifiedCommonMemory(row.Key))
                        memories.Add(row.Key);
                memories.Sort(StringComparer.Ordinal);
                result.Add(hero, memories.AsReadOnly());
            }
            return result;
        }

        private static string CreateFingerprint()
        {
            var ids = new List<string>(Memories.Keys);
            ids.Sort(StringComparer.Ordinal);
            var text = new StringBuilder();
            foreach (string id in ids)
            {
                var entry = Memories[id];
                text.Append(id).Append('|').Append(entry.Owner).Append('|').Append(entry.Category).Append('|');
                foreach (var slot in Slots(entry.Category)) text.Append(slot).Append(',');
                text.Append('|').Append(entry.Owner != null || AuthoredStarContract.IsVerifiedCommonMemory(id)).Append(';');
            }
            return StarClusters.RegistryHash(text.ToString());
        }

        public static IReadOnlyList<string> ForHero(string heroKey) =>
            heroKey != null && HeroMemories.TryGetValue(heroKey, out var memories) ? memories : Array.Empty<string>();


        public static bool TryGetCategory(string heroKey, string memory, out VerifiedMechanismMemoryCategory category)
        {
            category = default;
            if (heroKey == null || !HeroMemories.ContainsKey(heroKey) || memory == null || !Memories.TryGetValue(memory, out var entry)
                || (entry.Owner != null ? entry.Owner != heroKey : !AuthoredStarContract.IsVerifiedCommonMemory(memory))) return false;
            category = entry.Category;
            return true;
        }

        public static IReadOnlyList<MechanismMemorySlot> ForMemory(string heroKey, string memory)
        {
            if (!TryGetCategory(heroKey, memory, out var category)) return None;
            return Slots(category);
        }

        private static IReadOnlyList<MechanismMemorySlot> Slots(VerifiedMechanismMemoryCategory category)
        {
            switch (category)
            {
                case VerifiedMechanismMemoryCategory.Identity: return Identity;
                case VerifiedMechanismMemoryCategory.Movement: return Movement;
                default: return Editable;
            }
        }
    }
}
