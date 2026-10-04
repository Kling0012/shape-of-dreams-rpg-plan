using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public readonly struct MonsterClassification
    {
        public NightmareAffix Nightmare { get; }
        public string VariantId { get; }

        public MonsterClassification(NightmareAffix nightmare, string variantId)
        {
            VariantId = Variants.Get(variantId)?.Id;
            Nightmare = VariantId == null ? Nightmares.Sanitize((int)nightmare) : NightmareAffix.None;
        }
    }

    /// <summary>Authority replacement invalidates live tags and Build confirmation, not recorded deaths.</summary>
    public sealed class MonsterAuthorityState
    {
        private readonly HashSet<ulong> _retired = new HashSet<ulong>();
        private readonly Dictionary<uint, MonsterClassification> _classifications = new Dictionary<uint, MonsterClassification>();
        public bool HasAuthority { get; private set; }
        public ulong AuthorityGeneration { get; private set; }
        public bool BuildResendRequired { get; private set; } = true;

        public bool Observe(ulong generation, out bool changed)
        {
            changed = false;
            if (_retired.Contains(generation) || (HasAuthority && AuthorityGeneration != 0 && generation == 0)) return false;
            if (HasAuthority && AuthorityGeneration == generation) return true;
            if (HasAuthority) _retired.Add(AuthorityGeneration);
            HasAuthority = true;
            AuthorityGeneration = generation;
            _classifications.Clear();
            BuildResendRequired = true;
            changed = true;
            return true;
        }

        public bool Set(ulong generation, uint netId, NightmareAffix nightmare, string variantId)
        {
            if (netId == 0 || !Observe(generation, out _)) return false;
            if (!string.IsNullOrEmpty(variantId) && Variants.Get(variantId) == null) return false;
            _classifications[netId] = new MonsterClassification(nightmare, variantId);
            return true;
        }

        public bool TryGet(uint netId, out MonsterClassification classification) =>
            _classifications.TryGetValue(netId, out classification);

        public void Remove(uint netId) => _classifications.Remove(netId);
        public void BuildSent() => BuildResendRequired = false;

        public void ResetConnection()
        {
            _retired.Clear();
            _classifications.Clear();
            HasAuthority = false;
            AuthorityGeneration = 0;
            BuildResendRequired = true;
        }
    }
}
