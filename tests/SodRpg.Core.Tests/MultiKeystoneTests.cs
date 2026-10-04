using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v2.0.2 multi-keystones: a traveler holds up to three keystones depending on the star level
    /// (1 below 200, 2 from 200, 3 from 400). Every keystone keeps its own requirements and cost,
    /// no duplicates, and the C15 approval path works for the 2nd/3rd exactly like the 1st.
    /// </summary>
    public sealed class MultiKeystoneTests
    {
        private const string Hero = "Hero_Cetus";
        private const string Source = "St_D_IcyVeins";
        private static readonly KeystoneScope Echo = new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo });

        // key1 doubles Echo and key2 adds +50% on top, so a build with both proves every selected keystone applies.
        // key3 carries a grant plus a plain downside; keyRefund's drawback disables Echo stars, so selecting it
        // after buying the echo star must go through the C15 refund approval exactly like the 1st keystone would.
        private static KeystoneDefinition Key1() => AuthoredKeystoneCompiler.Compile("test.multi.key1", new[] { Source },
            new[] { new AuthoredKeystoneSpec { Percent = 100, Scope = Echo } },
            new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -10 } }, cost: 3);
        private static KeystoneDefinition Key2() => AuthoredKeystoneCompiler.Compile("test.multi.key2", new[] { Source },
            new[] { new AuthoredKeystoneSpec { Percent = 50, Scope = Echo } },
            new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -20 } }, cost: 5);
        private static KeystoneDefinition Key3() => AuthoredKeystoneCompiler.Compile("test.multi.key3", new[] { Source },
            new[] { new AuthoredKeystoneSpec { Grant = new AuthoredMechanismSpec {
                Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.multi.key3.heal", Source = MemorySelector.Parse(Source),
                Trigger = MemoryEventKind.Hit, Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Heal, Value = 1m } } } },
            new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -30 } }, cost: 7);
        private static KeystoneDefinition KeyRefund() => AuthoredKeystoneCompiler.Compile("test.multi.keyrefund", new[] { Source },
            new[] { new AuthoredKeystoneSpec { Grant = new AuthoredMechanismSpec {
                Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.multi.refund.heal", Source = MemorySelector.Parse(Source),
                Trigger = MemoryEventKind.Hit, Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Heal, Value = 2m } } } },
            new[] { new AuthoredKeystoneSpec { Disable = true, Scope = Echo } }, cost: 9);

        /// <summary>The Cetus route star carrying the test source memory; authored keystones anchor to it.</summary>
        private static string RouteAnchor() => HeroSigils.TreeFor(Hero).First(t => t.RouteMemory == Source && t.RouteOrder == 7).Id;

        private static AuthoredStarDef KeyDef(KeystoneDefinition key) => new AuthoredStarDef
        {
            HeroKey = Hero, LocalStarId = key.KeystoneId,
            ClusterId = "test.multi.keys", Region = new ClusterRegion { Kind = ClusterRegionKind.Keystone },
            AnchorId = RouteAnchor(), Shape = ClusterShape.Fan, KeystoneDefinition = key,
            Effect = new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("複数の刻印", "Multi Keystone"),
                KeystoneDefinition = key, RankCost = key.Cost },
        };

        private const string OuterAnchorId = "outer.multi.anchor";
        private static AuthoredStarDef Anchor() => new AuthoredStarDef
        {
            HeroKey = Hero, LocalStarId = OuterAnchorId, ClusterId = "outer.multi", Region = ClusterRegion.Outer,
            AnchorId = OuterAnchorId, Shape = ClusterShape.Fan,
            Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の守り", "Test armor"), Stat = Stat.Armor, Amount = 1 },
        };
        private static AuthoredStarDef EchoStar() => new AuthoredStarDef
        {
            HeroKey = Hero, LocalStarId = "outer.multi.echo", ClusterId = "outer.multi", Region = ClusterRegion.Outer,
            AnchorId = OuterAnchorId, Shape = ClusterShape.Fan,
            Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の反響", "Test echo"),
                Mechanism = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "test.multi.echo",
                    Source = MemorySelector.Parse(Source), Trigger = MemoryEventKind.Hit,
                    Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 20m } } },
        };

        private static IReadOnlyList<TalentDef> RegisterKeys(params KeystoneDefinition[] keys)
        {
            var defs = new List<AuthoredStarDef> { Anchor(), EchoStar() };
            foreach (var key in keys) defs.Add(KeyDef(key));
            return StarClusters.RegisterAuthored(Hero, defs).TreeFor(Hero);
        }

        private static readonly GimmickDef EchoEffect = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 20m };

        /// <summary>A profile at the given star level with the anchor route and tier-1 stars bought, so every test keystone is unlocked.</summary>
        private static Profile ProfileAt(int starLevel, params KeystoneDefinition[] keys)
        {
            RegisterKeys(keys);
            var profile = new Profile();
            var state = profile.Hero(Hero);
            state.StarXp = StarProgression.TotalXpForPoints(starLevel);
            state.Kills = 1000000;
            foreach (var node in HeroSigils.TreeFor(Hero).Where(t => !t.IsKeystone && t.Tier == 1))
                for (int i = 0; i < node.MaxRank; i++)
                    if (Rules.FreePoints(profile, Hero) > 0) Rules.AddTalentRank(profile, Hero, node.Id);
            string anchor = RouteAnchor();
            TreeTestPaths.Connect(profile, Hero, anchor);
            if (!profile.Hero(Hero).Talents.ContainsKey(anchor)) Rules.AddTalentRank(profile, Hero, anchor);
            TreeTestPaths.Connect(profile, Hero, "outer.multi.echo");
            if (!profile.Hero(Hero).Talents.ContainsKey("outer.multi.echo")) Rules.AddTalentRank(profile, Hero, "outer.multi.echo");
            Assert.True(Rules.KeystoneUnlocked(profile, Hero, HeroSigils.TreeFor(Hero).First(t => t.Id == keys[0].KeystoneId)));
            return profile;
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(199, 1)]
        [InlineData(200, 2)]
        [InlineData(399, 2)]
        [InlineData(400, 3)]
        [InlineData(500, 3)]
        public void Slot_counts_follow_the_star_level_thresholds(int level, int slots)
        {
            Assert.Equal(slots, KeystoneSlots.CountFor(level));
            Assert.Equal(KeystoneSlots.CountFor(level),
                KeystoneSlots.CountFor(StarProgression.Points(StarProgression.TotalXpForPoints(level))));
            int next = KeystoneSlots.NextUnlockLevel(level);
            if (slots < KeystoneSlots.Max) Assert.True(next > level);
            else Assert.Equal(-1, next);
        }

        [Fact]
        public void Keystones_fill_free_slots_and_every_cost_is_charged()
        {
            try
            {
                var p = ProfileAt(500, Key1(), Key2(), Key3());
                var state = p.Hero(Hero);
                int before = Rules.SpentPoints(state, Hero);
                Rules.SetKeystone(p, Hero, "test.multi.key1");
                Rules.SetKeystone(p, Hero, "test.multi.key2");
                Rules.SetKeystone(p, Hero, "test.multi.key3");
                Assert.Equal(3, state.KeystoneCount);
                Assert.Equal("test.multi.key1", state.Keystone); // 1つ目は従来どおり Keystone
                Assert.Equal(before + 3 + 5 + 7, Rules.SpentPoints(state, Hero));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Slots_cannot_be_exceeded_and_keystones_cannot_be_duplicated()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2(); var refund = Key3();
                var p = ProfileAt(199, key1, key2, refund);
                var state = p.Hero(Hero);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, Hero, key2.KeystoneId));

                state.StarXp = StarProgression.TotalXpForPoints(200);
                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, Hero, refund.KeystoneId));
                Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, Hero, key1.KeystoneId));

                state.StarXp = StarProgression.TotalXpForPoints(400);
                Rules.SetKeystone(p, Hero, refund.KeystoneId);
                Assert.Equal(3, state.KeystoneCount);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Second_keystone_drawback_refunds_need_the_same_c15_approval_as_the_first()
        {
            try
            {
                var key1 = Key1(); var refund = KeyRefund();
                var p = ProfileAt(400, key1, refund);
                var state = p.Hero(Hero);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                // echo 星は ProfileAt で購入済み。2つ目の代償がそれを無効にする場合は、1つ目と同じ承認が要る。
                int spent = Rules.SpentPoints(state, Hero);
                int echoCost = HeroSigils.TreeFor(Hero).First(t => t.Id == "outer.multi.echo").RankCost;

                var change = new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = refund.KeystoneId };
                var plan = Rules.PreviewAllocationChange(p, Hero, change);
                Assert.True(plan.CanApply);
                Assert.Contains("outer.multi.echo", plan.AffectedRefundIds);
                // 承認なしでは適用できず、プロフィールは変わらない。
                Assert.Throws<AllocationValidationException>(() => Rules.ApplyAllocationChange(p, Hero, change));
                Assert.Equal(spent, Rules.SpentPoints(state, Hero));
                // 1つ目と同じ承認の経路で、2つ目も適用できる。
                Rules.SetKeystone(p, Hero, refund.KeystoneId, plan.AffectedRefundIds);
                Assert.Equal(2, state.KeystoneCount);
                Assert.False(state.Talents.ContainsKey("outer.multi.echo"));
                Assert.Equal(spent - echoCost + refund.Cost, Rules.SpentPoints(state, Hero));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Removing_a_keystone_refunds_its_cost_and_keeps_the_others()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2(); var refund = Key3();
                var p = ProfileAt(500, key1, key2, refund);
                var state = p.Hero(Hero);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                Rules.SetKeystone(p, Hero, refund.KeystoneId);
                int spent = Rules.SpentPoints(state, Hero);

                Rules.RemoveKeystone(p, Hero, key2.KeystoneId);
                Assert.Equal(2, state.KeystoneCount);
                Assert.Equal(spent - key2.Cost, Rules.SpentPoints(state, Hero));
                Assert.Equal(key1.KeystoneId, state.Keystones[0]);
                Assert.Equal(refund.KeystoneId, state.Keystones[1]);

                Assert.Throws<InvalidOperationException>(() => Rules.RemoveKeystone(p, Hero, key2.KeystoneId));
                Rules.SetKeystone(p, Hero, key2.KeystoneId); // 空いた枠に選び直せる
                Assert.Equal(3, state.KeystoneCount);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Build_applies_every_keystone_and_charges_the_retained_power_once_each()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2();
                var p = ProfileAt(400, key1, key2);
                var state = p.Hero(Hero);
                TreeTestPaths.Connect(p, Hero, "outer.multi.echo");
                Assert.True(state.Talents.TryGetValue("outer.multi.echo", out int echoRanks) && echoRanks > 0);

                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                var one = Build.Compute(p, Hero, 0);
                Assert.Equal(40m, AuthoredKeystoneComposer.TransformAllocationPayload(one,
                    AuthoredKeystoneComposer.GimmickPayload(EchoEffect), Source).Value);

                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                var both = Build.Compute(p, Hero, 0);
                Assert.Equal(2, both.SelectedKeystones.Count);
                // 1つ目の2倍と2つ目の1.5倍が両方乗る（20 × 2.0 × 1.5）。
                Assert.Equal(60m, AuthoredKeystoneComposer.TransformAllocationPayload(both,
                    AuthoredKeystoneComposer.GimmickPayload(EchoEffect), Source).Value);
                Assert.Equal(one.SpentStarPoints + key2.Cost, both.SpentStarPoints);

                // 従来のPower刻印（ケトゥスの2つ）は、それぞれの保持Powerを1回ずつ入れる。
                var legacy = new Profile();
                var legacyState = legacy.Hero(Hero);
                legacyState.StarXp = StarProgression.TotalXpForPoints(400);
                legacyState.Kills = 1000000;
                foreach (var node in HeroSigils.TreeFor(Hero).Where(t => !t.IsKeystone && t.Tier == 1))
                    for (int i = 0; i < node.MaxRank; i++)
                        if (Rules.FreePoints(legacy, Hero) > 0) Rules.AddTalentRank(legacy, Hero, node.Id);
                var keys = HeroSigils.TreeFor(Hero).Where(t => t.IsKeystone).ToList();
                Rules.SetKeystone(legacy, Hero, keys[0].Id);
                var single = Build.Compute(legacy, Hero, 0);
                Assert.True(single.Get(keys[0].Power) >= keys[0].PowerValue);
                Assert.Equal(0, single.Get(keys[1].Power));
                Rules.SetKeystone(legacy, Hero, keys[1].Id);
                var dual = Build.Compute(legacy, Hero, 0);
                Assert.True(dual.Get(keys[0].Power) >= keys[0].PowerValue);
                Assert.True(dual.Get(keys[1].Power) >= keys[1].PowerValue);
                Assert.Equal(single.SpentStarPoints + Content.KeystoneCost, dual.SpentStarPoints);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Star_summary_lists_every_keystone()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2();
                var p = ProfileAt(200, key1, key2);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                var summary = StarSummary.Compute(p, Hero);
                Assert.Equal(2, summary.Keystone.Count);
                Assert.Contains(key1.KeystoneId, summary.Keystone.SelectMany(l => l.StarIds));
                Assert.Contains(key2.KeystoneId, summary.Keystone.SelectMany(l => l.StarIds));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Save_keeps_keystone_and_optional_keystones_and_old_saves_load_single()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2(); var refund = Key3();
                var p = ProfileAt(500, key1, key2, refund);
                var state = p.Hero(Hero);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                Rules.SetKeystone(p, Hero, refund.KeystoneId);

                var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
                var hero = loaded.Hero(Hero);
                Assert.Equal(3, hero.KeystoneCount);
                Assert.Equal(key1.KeystoneId, hero.Keystone);
                Assert.Equal(key2.KeystoneId, hero.Keystones[1]);
                Assert.Equal(refund.KeystoneId, hero.Keystones[2]);

                // 1つだけの保存は、v2.0.2 より前の形（keystones 欄なし）でもそのまま1つで読める。
                state.RemoveKeystone(key2.KeystoneId);
                state.RemoveKeystone(refund.KeystoneId);
                string json = ProfileCodec.Write(p);
                Assert.Contains(",\"keystones\":[]", json, StringComparison.Ordinal);
                var legacy = json.Replace(",\"keystones\":[]", "");
                var single = ProfileCodec.Read(legacy, new List<string>());
                Assert.Equal(1, single.Hero(Hero).KeystoneCount);
                Assert.Equal(key1.KeystoneId, single.Hero(Hero).Keystone);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Shrinking_slots_removes_the_latest_keystones_first_and_refunds_their_cost()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2(); var refund = Key3();
                var p = ProfileAt(500, key1, key2, refund);
                var state = p.Hero(Hero);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                Rules.SetKeystone(p, Hero, refund.KeystoneId);
                int spent = Rules.SpentPoints(state, Hero);

                state.StarXp = StarProgression.TotalXpForPoints(200); // データの都合で星のレベルが下がった
                var result = AuthoredStarMigration.Apply(state, HeroSigils.TreeFor(Hero), StarClusters.MigrationsFor(Hero));
                Assert.Equal(2, state.KeystoneCount);
                Assert.Equal(key1.KeystoneId, state.Keystones[0]);
                Assert.Equal(key2.KeystoneId, state.Keystones[1]);
                Assert.Contains(refund.KeystoneId, result.StarIds);
                Assert.Equal(refund.Cost, result.RefundCost);
                Assert.Equal(spent - refund.Cost, Rules.SpentPoints(state, Hero));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Host_accepts_up_to_the_allowed_slots_and_rejects_more()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2(); var refund = Key3();
                var p = ProfileAt(500, key1, key2, refund);
                var state = p.Hero(Hero);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                Rules.SetKeystone(p, Hero, refund.KeystoneId);
                var build = Build.Compute(p, Hero, 0);

                string submission = HostBuildValidation.Encode(build, p, Hero, 0);
                Assert.True(HostBuildValidation.TryAccept(submission, Hero, out var accepted, out string reason), reason);
                Assert.Equal(3, accepted.SelectedKeystones.Count);

                // 声明した星のレベルに対して枠が足りなければ拒否する。
                string cheated = submission.Replace(";S:500;", ";S:199;");
                Assert.NotEqual(submission, cheated);
                Assert.False(HostBuildValidation.TryAccept(cheated, Hero, out _, out string rejected));
                Assert.Equal("keystone-slots", rejected);

                string twoClaimed = submission.Replace(";S:500;", ";S:399;");
                Assert.False(HostBuildValidation.TryAccept(twoClaimed, Hero, out _, out string rejectedTwo));
                Assert.Equal("keystone-slots", rejectedTwo);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Wire_encoding_carries_every_keystone_and_round_trips()
        {
            try
            {
                var key1 = Key1(); var key2 = Key2(); var refund = Key3();
                var p = ProfileAt(500, key1, key2, refund);
                var state = p.Hero(Hero);
                Rules.SetKeystone(p, Hero, key1.KeystoneId);
                Rules.SetKeystone(p, Hero, key2.KeystoneId);
                Rules.SetKeystone(p, Hero, refund.KeystoneId);
                var build = Build.Compute(p, Hero, 0);
                Assert.Equal(new[] { key1.KeystoneId, key2.KeystoneId, refund.KeystoneId },
                    build.SelectedKeystones.Select(k => k.KeystoneId).ToArray());

                string encoded = build.Encode();
                var decoded = Build.Decode(encoded);
                Assert.NotNull(decoded);
                Assert.Equal(encoded, decoded.Encode());
                Assert.Equal(build.SelectedKeystones.Select(k => k.KeystoneId),
                    decoded.SelectedKeystones.Select(k => k.KeystoneId));
                Assert.Equal(AuthoredKeystoneCodec.Encode(build.SelectedKeystone), AuthoredKeystoneCodec.Encode(decoded.SelectedKeystone));

                // 1つだけなら従来どおり k: は1レコード。
                state.RemoveKeystone(key2.KeystoneId);
                state.RemoveKeystone(refund.KeystoneId);
                var single = Build.Compute(p, Hero, 0).Encode();
                Assert.Single(HeroSigils.TreeFor(Hero), t => t.IsKeystone && t.Id == key1.KeystoneId);
                var decodedSingle = Build.Decode(single);
                Assert.Single(decodedSingle.SelectedKeystones);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
