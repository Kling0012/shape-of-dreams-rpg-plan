using Xunit;

// Test collections stay serialized: StarClusters/HeroSigils/Rules hold one process-wide registry keyed by
// real hero IDs, and tests temporarily re-register heroes (see [Collection("Generated hero registry")]).
// Any parallel class reading TreeFor/AllocationValidationForHero would observe another test's registration.
// Loc.Japanese alone no longer requires this (it is [ThreadStatic] since #151); wall time is recovered by
// running the three test projects in parallel (tools/test_changed.py) instead of parallelizing inside one.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
