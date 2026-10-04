using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MpHostFixesV131Tests
    {
        private sealed class Effect { public float Amount; }
        private sealed class Handle { public bool Active = true; public Effect Shield; public float Seconds; public int Destroys; }
        private sealed class Adapter : IPowerShieldAdapter<Handle, Effect>
        {
            public bool IsActive(Handle h) => h != null && h.Active;
            public Effect EffectOf(Handle h) => h?.Shield;
            public float Amount(Effect e) => e.Amount;
            public void Refresh(Handle h, float s) => h.Seconds = s;
            public void Destroy(Handle h) { h.Destroys++; h.Active = false; }
        }
        private static long Key(int target, int power) => ((long)target << 32) | (uint)power;

        // Finding 3: pooled handle recycled for another hero's shield.
        [Fact]
        public void RecycledHandleOfAnotherHeroIsNotRefreshedOrDestroyed()
        {
            var ledger = new PowerShieldLedger<Handle, Effect>(new Adapter());
            var pooled = new Handle { Shield = new Effect { Amount = 10 } };
            ledger.Offer(Key(1, 5), pooled, 4f);
            // The shield expires, the pool hands the same object to hero B with a different underlying shield.
            pooled.Shield = new Effect { Amount = 50 };
            var fresh = new Handle { Shield = new Effect { Amount = 10 } };
            var outcome = ledger.Offer(Key(1, 5), fresh, 4f);
            Assert.Equal(PowerShieldOutcome.Replaced, outcome);
            Assert.True(pooled.Active); Assert.Equal(0, pooled.Destroys); Assert.Equal(0f, pooled.Seconds);
            Assert.True(fresh.Active);
        }

        [Fact]
        public void NewShieldReusingTheCachedHandleIsNotDestroyedImmediately()
        {
            var ledger = new PowerShieldLedger<Handle, Effect>(new Adapter());
            var pooled = new Handle { Shield = new Effect { Amount = 10 } };
            ledger.Offer(Key(1, 5), pooled, 4f);
            pooled.Active = false;
            // Same pooled object returns with an equal-amount new shield.
            pooled.Active = true; pooled.Shield = new Effect { Amount = 10 };
            Assert.Equal(PowerShieldOutcome.Replaced, ledger.Offer(Key(1, 5), pooled, 4f));
            Assert.True(pooled.Active); Assert.Equal(0, pooled.Destroys);
        }

        [Fact]
        public void StrongestOnlyStillKeepsLiveStrongerShieldAndReplacesWeaker()
        {
            var ledger = new PowerShieldLedger<Handle, Effect>(new Adapter());
            var first = new Handle { Shield = new Effect { Amount = 10 } };
            ledger.Offer(1, first, 4f);
            var weaker = new Handle { Shield = new Effect { Amount = 6 } };
            Assert.Equal(PowerShieldOutcome.KeptExisting, ledger.Offer(1, weaker, 7f));
            Assert.True(first.Active); Assert.Equal(7f, first.Seconds); Assert.False(weaker.Active);
            var stronger = new Handle { Shield = new Effect { Amount = 20 } };
            Assert.Equal(PowerShieldOutcome.Replaced, ledger.Offer(1, stronger, 4f));
            Assert.False(first.Active); Assert.True(stronger.Active);
        }

        // Finding 9: reload must release live shields, and not destroy recycled ones.
        [Fact]
        public void DestroyAllReleasesOnlyActivationsThatAreStillOurs()
        {
            var ledger = new PowerShieldLedger<Handle, Effect>(new Adapter());
            var live = new Handle { Shield = new Effect { Amount = 5 } };
            var recycled = new Handle { Shield = new Effect { Amount = 5 } };
            var expired = new Handle { Shield = new Effect { Amount = 5 } };
            ledger.Offer(1, live, 4f); ledger.Offer(2, recycled, 4f); ledger.Offer(3, expired, 4f);
            recycled.Shield = new Effect { Amount = 9 };
            expired.Active = false;
            Assert.Equal(1, ledger.DestroyAll());
            Assert.False(live.Active); Assert.True(recycled.Active); Assert.Equal(0, recycled.Destroys);
            Assert.Equal(0, ledger.Count);
        }

        [Fact]
        public void DestroyAllContinuesAfterOneFailure()
        {
            var failing = new FailingAdapter();
            var ledger = new PowerShieldLedger<Handle, Effect>(failing);
            var a = new Handle { Shield = new Effect { Amount = 1 } };
            var b = new Handle { Shield = new Effect { Amount = 1 } };
            ledger.Offer(1, a, 1f); ledger.Offer(2, b, 1f);
            failing.Throw = true;
            int errors = 0;
            ledger.DestroyAll(_ => errors++);
            Assert.Equal(2, errors);
            Assert.Equal(0, ledger.Count);
        }
        private sealed class FailingAdapter : IPowerShieldAdapter<Handle, Effect>
        {
            public bool Throw;
            public bool IsActive(Handle h) => h.Active;
            public Effect EffectOf(Handle h) => h.Shield;
            public float Amount(Effect e) => e.Amount;
            public void Refresh(Handle h, float s) { }
            public void Destroy(Handle h) { if (Throw) throw new InvalidOperationException(); h.Active = false; }
        }

        [Fact]
        public void PruneForgetsDeadEntriesSoAReplacementIsNotComparedToThem()
        {
            var ledger = new PowerShieldLedger<Handle, Effect>(new Adapter());
            var h = new Handle { Shield = new Effect { Amount = 9 } };
            ledger.Offer(1, h, 1f); h.Active = false;
            ledger.Prune();
            Assert.Equal(0, ledger.Count);
        }

        // Finding 4: one hero's generated scope must not suppress a teammate's incoming hit.
        private sealed class Hero { }
        [Fact]
        public void GeneratedDepthOfOneHeroDoesNotSuppressAnother()
        {
            var depth = new ScopedDepth<Hero>(); var a = new Hero(); var b = new Hero();
            depth.Enter(a);
            Assert.Equal(1, depth.Total - depth.Foreign(a));   // A is still suppressed (own recursion)
            Assert.Equal(0, depth.Total - depth.Foreign(b));   // B's genuine incoming hit passes
            depth.Enter(b);
            Assert.Equal(1, depth.Total - depth.Foreign(b));
            depth.Exit(b); depth.Exit(a);
            Assert.Equal(0, depth.Total); Assert.Equal(0, depth.Of(a));
        }

        // Finding 5: receivers follow the roster, not accepted Builds.
        [Fact]
        public void WaypointRosterIncludesHeroWithoutBuildAndDropsInactive()
        {
            var modded = new Hero(); var vanilla = new Hero(); var gone = new Hero();
            var tracked = new List<Hero> { modded, gone };
            var add = new List<Hero>(); var remove = new List<Hero>();
            WaypointRoster.Diff(tracked, new[] { modded, vanilla }, h => true, add, remove);
            Assert.Equal(new[] { vanilla }, add);
            Assert.Equal(new[] { gone }, remove);
            add.Clear(); remove.Clear();
            WaypointRoster.Diff(tracked, new[] { modded, vanilla }, h => h != vanilla, add, remove);
            Assert.Empty(add);
        }

        // Findings 7 and 8: owned effects are released in isolation; foreign ones are untouched.
        private sealed class Owner { }
        private sealed class Curse { public bool Live = true; public bool Throws; }
        [Fact]
        public void ReleaseAllDestroysOwnedLiveEffectsAndIsolatesFailures()
        {
            var reg = new OwnedEffectRegistry<Owner, Curse>();
            var p1 = new Owner(); var p2 = new Owner();
            var c1 = new Curse { Throws = true }; var c2 = new Curse(); var c3 = new Curse { Live = false };
            var natural = new Curse();   // never registered: a native curse or skin must survive
            reg.Add(p1, c1); reg.Add(p1, c2); reg.Add(p2, c3);
            int errors = 0;
            int destroyed = reg.ReleaseAll(c => c.Live, c => { if (c.Throws) throw new InvalidOperationException(); c.Live = false; }, _ => errors++);
            Assert.Equal(1, destroyed); Assert.Equal(1, errors);
            Assert.False(c2.Live); Assert.True(natural.Live);
            Assert.Equal(0, reg.KeyCount);
        }

        [Fact]
        public void ClearAfterReloadStillLiftsTheCurseOfTheRemotePlayer()
        {
            var reg = new OwnedEffectRegistry<Owner, Curse>();
            var remote = new Owner(); var curse = new Curse();
            reg.Add(remote, curse);
            // Detach releases first, so the later profile-driven clear has nothing left to miss.
            reg.ReleaseAll(c => c.Live, c => c.Live = false);
            Assert.False(curse.Live);
            Assert.Equal(0, reg.Release(remote, c => c.Live, c => c.Live = false));
        }

        // Finding 10: summon kills credit the owner exactly once, non-hero non-summons credit nobody.
        private sealed class Node { public string Kind; public Node Owner; }
        [Fact]
        public void SummonKillResolvesToOwnerHero()
        {
            var hero = new Node { Kind = "hero" };
            var summon = new Node { Kind = "summon", Owner = hero };
            var monster = new Node { Kind = "monster" };
            var orphan = new Node { Kind = "summon" };
            Func<Node, Node> Resolve = n => ActorOwnership.ResolveHero(n, x => x.Kind == "hero", x => x.Kind == "summon", x => x.Owner);
            Assert.Same(hero, Resolve(hero));
            Assert.Same(hero, Resolve(summon));
            Assert.Null(Resolve(monster));
            Assert.Null(Resolve(orphan));
            Assert.Null(Resolve(null));
        }

        // Finding 11: only real, positive restoration or accepted shields advance bounties.
        [Theory]
        [InlineData(25f, true, true)]
        [InlineData(0f, true, false)]      // overheal only
        [InlineData(-3f, true, false)]
        [InlineData(25f, false, false)]    // self heal is not "another traveler"
        [InlineData(float.NaN, true, false)]
        public void SupportHealCredit(float restored, bool otherAlly, bool expected)
            => Assert.Equal(expected, SupportBountyCredit.AllyHeal(restored, otherAlly));

        [Theory]
        [InlineData(true, 12f, true, true)]
        [InlineData(false, 12f, true, false)]   // rejected candidate
        [InlineData(true, 0f, true, false)]
        [InlineData(true, 12f, false, false)]
        public void SupportShieldCredit(bool accepted, float amount, bool friendly, bool expected)
            => Assert.Equal(expected, SupportBountyCredit.ShieldGrant(accepted, amount, friendly));

        // Sync finding 5: the host's own Secure/Delve choice is not an input.
        [Fact]
        public void DreamOmenAcceptedForMatchingRunAndGenerationRegardlessOfHostChoice()
        {
            Assert.True(DreamOmenGate.Accept("run", 3, "run", 3, (int)DreamEvent.Merchant, null, -1));
            Assert.False(DreamOmenGate.Accept("run", 4, "run", 3, (int)DreamEvent.Merchant, null, -1));
            Assert.False(DreamOmenGate.Accept("run", 3, "other", 3, (int)DreamEvent.Merchant, null, -1));
            Assert.False(DreamOmenGate.Accept("run", 3, "run", 3, (int)DreamEvent.None, null, -1));
            Assert.False(DreamOmenGate.Accept("run", 3, "run", 3, 9999, null, -1));
            Assert.False(DreamOmenGate.Accept("run", 3, "run", 3, (int)DreamEvent.Merchant, "run", 3));
            Assert.True(DreamOmenGate.Accept("run", 4, "run", 4, (int)DreamEvent.Merchant, "run", 3));
        }
    }
}
