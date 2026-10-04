using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class MobModelSnapshotTests
    {
        private static MobCompatibility Compatible() => MobModelManifestTests.Compatible();
        private static MobModelSnapshotState State()
        {
            var state = new MobModelSnapshotState(); state.Reset("session", 1, Compatible(), MobModelManifestTests.ValidManifest());
            return state;
        }
        private static MobModelAssignment A(uint netId = 10, long generation = 1, bool wisp = false) => new MobModelAssignment
        {
            NetId = netId, SpawnGeneration = generation,
            BaseMonsterType = wisp ? "Example.Monster_Wisp" : "Example.Monster_Skeleton", ModelId = wisp ? "lantern_wisp" : "ember_warden",
        };
        private static MobModelSnapshot S(long revision, params MobModelAssignment[] assignments) => new MobModelSnapshot
        {
            SessionNonce = "session", RoomEpoch = 1, Revision = revision, Enabled = true,
            Compatibility = Compatible(), Assignments = assignments,
        };
        private static bool Resolve(MobModelSnapshotState state, uint netId = 10, bool wisp = false) =>
            state.TryResolve(netId, wisp ? "Example.Monster_Wisp" : "Example.Monster_Skeleton", out _);

        [Fact]
        public void Late_spawn_is_retryable_without_reapplying_duplicate_snapshot()
        {
            var state = State(); var snapshot = S(1, A()); Assert.True(state.TryAccept(snapshot, out _));
            Assert.True(state.Enabled); Assert.Equal(1, state.ActiveCount);
            Assert.False(state.TryResolve(10, null, out _)); // Object is not present yet.
            Assert.False(state.TryAccept(snapshot, out _)); Assert.True(Resolve(state));
            Assert.True(Resolve(state)); Assert.Equal(1, state.ActiveCount);
        }

        [Fact]
        public void Actual_runtime_type_must_match_exactly_even_with_correct_net_id()
        {
            var state = State(); state.TryAccept(S(1, A()), out _);
            Assert.False(state.TryResolve(10, "Example.Monster_Wisp", out _));
            Assert.False(state.TryResolve(10, "example.Monster_Skeleton", out _));
            Assert.False(state.TryResolve(10, "Monster_Skeleton", out _));
            Assert.False(Resolve(state, 11)); Assert.True(Resolve(state));
        }

        [Fact]
        public void Old_room_session_revision_and_duplicate_cannot_change_current_state()
        {
            var state = State(); state.TryAccept(S(4, A()), out _);
            var incoming = S(5); incoming.SessionNonce = "old"; Assert.False(state.TryAccept(incoming, out _));
            incoming = S(5); incoming.RoomEpoch = 2; Assert.False(state.TryAccept(incoming, out _));
            Assert.False(state.TryAccept(S(3), out _)); Assert.False(state.TryAccept(S(4), out _));
            Assert.False(state.TryAccept(S(0), out _)); Assert.False(state.TryAccept(S(-1), out _));
            Assert.Equal(4, state.Revision); Assert.True(Resolve(state));
        }

        [Fact]
        public void Backfill_is_a_complete_set_and_omitted_generations_cannot_revive()
        {
            var state = State(); state.TryAccept(S(1, A(10, 1), A(20, 2, true)), out _);
            state.TryAccept(S(2, A(20, 2, true)), out _);
            Assert.False(Resolve(state)); Assert.True(Resolve(state, 20, true));
            Assert.True(state.TryAccept(S(3, A(10, 1), A(20, 2, true)), out _));
            Assert.False(Resolve(state)); Assert.Equal(1, state.ActiveCount);
            Assert.True(state.TryAccept(S(4, A(10, 3), A(20, 2, true)), out _));
            Assert.True(Resolve(state)); Assert.Equal(2, state.ActiveCount);
        }

        [Fact]
        public void Despawn_tombstone_rejects_delayed_assignment_but_accepts_new_host_generation()
        {
            var state = State(); state.TryAccept(S(1, A()), out _); state.MarkDespawned(10);
            Assert.False(Resolve(state)); Assert.True(state.TryAccept(S(2, A()), out _)); Assert.False(Resolve(state));
            Assert.True(state.TryAccept(S(3, A(10, 2, true)), out _));
            Assert.False(Resolve(state)); Assert.True(Resolve(state, 10, true));
            Assert.False(state.TryAccept(S(4, A()), out _)); Assert.True(Resolve(state, 10, true));
        }

        [Fact]
        public void Departing_old_object_does_not_tombstone_new_generation_already_received()
        {
            var state = State(); state.TryAccept(S(1, A()), out _);
            state.TryAccept(S(2, A(10, 2, true)), out _);
            state.MarkDespawned(10, 1); Assert.True(Resolve(state, 10, true));
            state.MarkDespawned(10, 2); Assert.False(Resolve(state, 10, true));
        }

        [Fact]
        public void Despawn_before_first_snapshot_blocks_ambiguous_net_id_for_whole_room()
        {
            var state = State(); state.MarkDespawned(10);
            Assert.False(state.RequiresFallback); // An unknown removal alone may have been an unmapped monster.
            Assert.True(state.TryAccept(S(1, A(), A(20, 2, true)), out _)); Assert.False(Resolve(state));
            Assert.True(state.RequiresFallback); Assert.False(state.Enabled); Assert.False(Resolve(state, 20, true));
            Assert.True(state.TryAccept(S(2, A(10, 3)), out _)); Assert.False(Resolve(state));
            state.Reset("new-room", 2, Compatible(), MobModelManifestTests.ValidManifest());
            var next = S(1, A()); next.SessionNonce = "new-room"; next.RoomEpoch = 2;
            Assert.True(state.TryAccept(next, out _)); Assert.True(Resolve(state)); Assert.False(state.RequiresFallback);
        }

        [Fact]
        public void Net_id_reuse_with_same_type_still_requires_a_new_spawn_generation()
        {
            var state = State(); state.TryAccept(S(1, A()), out _); state.MarkDespawned(10);
            state.TryAccept(S(2, A()), out _); Assert.False(Resolve(state));
            state.TryAccept(S(3, A(10, 2)), out _); Assert.True(Resolve(state));
        }

        [Fact]
        public void New_spawns_must_use_host_wide_increasing_generation()
        {
            var state = State(); state.TryAccept(S(1, A(10, 100)), out _);
            Assert.False(state.TryAccept(S(2, A(10, 100), A(20, 99, true)), out _));
            Assert.False(state.TryAccept(S(2, A(20, 100, true)), out _));
            Assert.True(state.TryAccept(S(2, A(10, 100), A(20, 101, true)), out _));
            Assert.True(Resolve(state, 20, true));
        }

        [Fact]
        public void Same_spawn_cannot_change_base_type_or_model()
        {
            var state = State(); state.TryAccept(S(1, A()), out _);
            Assert.False(state.TryAccept(S(2, A(10, 1, true)), out _)); Assert.True(Resolve(state));
            var alias = A(); alias.BaseMonsterType = "Example.Monster_SkeletonElite";
            Assert.False(state.TryAccept(S(2, alias), out _)); Assert.True(Resolve(state));
        }

        [Fact]
        public void Disabled_snapshot_reverts_all_and_recovery_preserves_living_generations()
        {
            var state = State(); state.TryAccept(S(1, A(), A(20, 2, true)), out _);
            var disabled = S(2); disabled.Enabled = false; disabled.Compatibility = null;
            Assert.True(state.TryAccept(disabled, out _)); Assert.False(state.Enabled); Assert.Equal(0, state.ActiveCount);
            Assert.False(Resolve(state)); Assert.False(Resolve(state, 20, true));
            Assert.True(state.TryAccept(S(3, A(), A(20, 2, true)), out _)); Assert.True(Resolve(state)); Assert.True(Resolve(state, 20, true));
        }

        [Fact]
        public void Despawns_during_disabled_or_transport_pause_remain_tombstoned()
        {
            var state = State(); state.TryAccept(S(1, A(), A(20, 2, true)), out _);
            state.Suspend(); state.MarkDespawned(10);
            Assert.False(state.Enabled); Assert.Equal(1, state.Revision); Assert.Equal("session", state.SessionNonce);
            Assert.False(state.TryAccept(S(1, A(), A(20, 2, true)), out _)); Assert.False(state.Enabled);
            Assert.True(state.TryAccept(S(2, A(), A(20, 2, true)), out _));
            Assert.False(Resolve(state)); Assert.True(Resolve(state, 20, true));
            var disabled = S(3); disabled.Enabled = false;
            state.TryAccept(disabled, out _); state.MarkDespawned(20);
            state.TryAccept(S(4, A(), A(20, 2, true)), out _); Assert.Equal(0, state.ActiveCount);
            state.TryAccept(S(5, A(10, 3), A(20, 4, true)), out _); Assert.Equal(2, state.ActiveCount);
        }

        [Fact]
        public void Transport_pause_does_not_retire_living_instances_but_requires_new_revision()
        {
            var state = State(); state.TryAccept(S(8, A()), out _); state.Suspend();
            Assert.False(Resolve(state)); Assert.False(state.TryAccept(S(8, A()), out _));
            Assert.True(state.TryAccept(S(9, A()), out _)); Assert.True(Resolve(state));
        }

        [Fact]
        public void New_complete_set_after_pause_retires_missing_instances()
        {
            var state = State(); state.TryAccept(S(1, A(), A(20, 2, true)), out _); state.Suspend();
            state.TryAccept(S(2, A(20, 2, true)), out _);
            state.TryAccept(S(3, A(), A(20, 2, true)), out _); Assert.False(Resolve(state));
        }

        [Fact]
        public void Every_enabled_snapshot_must_have_matching_compatibility()
        {
            var state = State(); state.TryAccept(S(1, A()), out _);
            var bad = S(2); bad.Compatibility = null; Assert.False(state.TryAccept(bad, out _));
            bad = S(2); bad.Compatibility.ContentHash = new string('f', 64); Assert.False(state.TryAccept(bad, out _));
            bad = S(2); bad.Compatibility.ModSha256 = new string('f', 64); Assert.False(state.TryAccept(bad, out _));
            bad = S(2); bad.Compatibility.Target = "StandaloneLinux64"; Assert.False(state.TryAccept(bad, out _));
            Assert.Equal(1, state.Revision); Assert.True(Resolve(state));
        }

        [Fact]
        public void Malformed_assignments_reject_the_entire_packet_without_partial_changes()
        {
            var state = State(); state.TryAccept(S(1, A()), out _);
            var bad = S(2); bad.Assignments = null; Assert.False(state.TryAccept(bad, out _));
            Assert.False(state.TryAccept(S(2, (MobModelAssignment)null), out _));
            Assert.False(state.TryAccept(S(2, A(0)), out _)); Assert.False(state.TryAccept(S(2, A(10, 0)), out _));
            Assert.False(state.TryAccept(S(2, A(10, -1)), out _));
            var unknown = A(20, 2); unknown.ModelId = "unknown"; Assert.False(state.TryAccept(S(2, A(), unknown), out _));
            unknown = A(); unknown.BaseMonsterType = "Monster_Skeleton"; Assert.False(state.TryAccept(S(2, unknown), out _));
            unknown = A(); unknown.ModelId = null; Assert.False(state.TryAccept(S(2, unknown), out _));
            unknown = A(); unknown.BaseMonsterType = null; Assert.False(state.TryAccept(S(2, unknown), out _));
            Assert.False(state.TryAccept(S(2, A(), A(10, 2)), out _));
            Assert.False(state.TryAccept(S(2, A(), A(20, 1, true)), out _));
            bad = S(2, A()); bad.Enabled = false; Assert.False(state.TryAccept(bad, out _));
            Assert.False(state.TryAccept(null, out _));
            Assert.True(Resolve(state)); Assert.Equal(1, state.ActiveCount); Assert.Equal(1, state.Revision);
        }

        [Fact]
        public void Pending_backfill_is_bounded_and_accepts_exact_limit()
        {
            var state = State(); var maximum = Enumerable.Range(1, MobModelSnapshotState.MaxAssignments)
                .Select(i => A((uint)i, i)).ToArray();
            Assert.True(state.TryAccept(S(1, maximum), out _)); Assert.Equal(300, state.ActiveCount);
            var oversized = Enumerable.Range(1, 301).Select(i => A((uint)i, i)).ToArray();
            Assert.False(state.TryAccept(S(2, oversized), out _)); Assert.Equal(300, state.ActiveCount);
        }

        [Fact]
        public void History_is_bounded_without_evicting_tombstones_and_reset_recovers()
        {
            var state = State();
            for (uint id = 1; id <= MobModelSnapshotState.MaxTrackedNetIds; id++) state.MarkDespawned(id);
            Assert.False(state.IsExhausted);
            state.MarkDespawned(MobModelSnapshotState.MaxTrackedNetIds + 1U);
            Assert.True(state.IsExhausted); Assert.True(state.RequiresFallback); Assert.False(state.Enabled);
            Assert.False(state.TryAccept(S(1, A()), out _));
            state.Reset("session", 2, Compatible(), MobModelManifestTests.ValidManifest());
            Assert.False(state.IsExhausted); var next = S(1, A()); next.RoomEpoch = 2;
            Assert.True(state.TryAccept(next, out _)); Assert.True(Resolve(state));
        }

        [Fact]
        public void Full_history_allows_existing_ids_but_new_id_exhaustion_requests_global_fallback()
        {
            var state = State(); state.TryAccept(S(1, A(1)), out _);
            for (uint id = 2; id <= MobModelSnapshotState.MaxTrackedNetIds; id++) state.MarkDespawned(id);
            Assert.True(state.TryAccept(S(2, A(1, 2)), out _)); Assert.True(Resolve(state, 1));
            Assert.False(state.TryAccept(S(3, A(5000, 3)), out _)); Assert.Equal(2, state.Revision);
            Assert.True(state.IsExhausted); Assert.True(state.RequiresFallback); Assert.False(Resolve(state, 1));
            Assert.False(state.TryAccept(S(3, A(1, 2)), out _));
        }

        [Fact]
        public void Mutable_inputs_outputs_and_catalog_cannot_rewrite_trusted_state()
        {
            var compatibility = Compatible(); var manifest = MobModelManifestTests.ValidManifest();
            var state = new MobModelSnapshotState(); state.Reset("session", 1, compatibility, manifest);
            compatibility.ContentHash = new string('f', 64); manifest.Models[0].Id = "other";
            var snapshot = S(1, A()); Assert.True(state.TryAccept(snapshot, out _));
            snapshot.Assignments[0].ModelId = "other"; snapshot.Assignments[0].SpawnGeneration = 999;
            snapshot.SessionNonce = "other"; snapshot.Compatibility.ContentHash = "other";
            Assert.True(state.TryResolve(10, "Example.Monster_Skeleton", out var resolved));
            Assert.Equal("ember_warden", resolved.ModelId); Assert.Equal(1, resolved.SpawnGeneration);
            resolved.ModelId = "other"; resolved.SpawnGeneration = 999;
            Assert.True(state.TryResolve(10, "Example.Monster_Skeleton", out resolved));
            Assert.Equal("ember_warden", resolved.ModelId); Assert.Equal(1, resolved.SpawnGeneration);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(19)]
        [InlineData(83)]
        [InlineData(2026)]
        public void Reordered_duplicate_complete_snapshots_converge_to_latest_host_state(int seed)
        {
            var random = new Random(seed);
            var live = new Dictionary<uint, MobModelAssignment>();
            var packets = new List<MobModelSnapshot>();
            long generation = 0;
            for (int revision = 1; revision <= 100; revision++)
            {
                uint id = (uint)random.Next(1, 6);
                if (random.Next(3) == 0) live.Remove(id);
                else live[id] = A(id, ++generation, random.Next(2) == 0);
                var snapshot = S(revision, live.Values.ToArray());
                if (revision % 7 == 0) { snapshot.Enabled = false; snapshot.Assignments = Array.Empty<MobModelAssignment>(); }
                packets.Add(snapshot); packets.Add(snapshot);
            }
            for (int i = packets.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1); var swap = packets[i]; packets[i] = packets[other]; packets[other] = swap;
            }
            var state = State(); MobModelSnapshot newest = null;
            foreach (var packet in packets)
            {
                bool shouldAccept = newest == null || packet.Revision > newest.Revision;
                Assert.Equal(shouldAccept, state.TryAccept(packet, out _));
                if (shouldAccept) newest = packet;
                Assert.Equal(newest.Revision, state.Revision);
                Assert.Equal(newest.Enabled, state.Enabled);
                for (uint id = 1; id <= 5; id++)
                {
                    var expected = newest.Enabled ? newest.Assignments.FirstOrDefault(a => a.NetId == id) : null;
                    Assert.Equal(expected != null && expected.ModelId == "ember_warden", Resolve(state, id));
                    Assert.Equal(expected != null && expected.ModelId == "lantern_wisp", Resolve(state, id, true));
                }
            }
            Assert.Equal(100, state.Revision);
        }

        [Fact]
        public void Clear_and_uninitialized_state_cannot_resolve_or_accept_packets()
        {
            var state = new MobModelSnapshotState(); Assert.False(state.TryAccept(S(1, A()), out _));
            state.MarkDespawned(10); state.Suspend(); Assert.False(Resolve(state));
            state.Reset("session", 1, Compatible(), MobModelManifestTests.ValidManifest()); state.TryAccept(S(1, A()), out _);
            state.Clear(); Assert.False(Resolve(state)); Assert.False(state.TryAccept(S(2, A()), out _));
            Assert.Equal(0, state.Revision); Assert.Equal(0, state.RoomEpoch); Assert.Equal("", state.SessionNonce);
        }

        [Fact]
        public void Reset_requires_verified_manifest_and_concrete_runtime_versions()
        {
            var state = new MobModelSnapshotState();
            Assert.Throws<ArgumentException>(() => state.Reset("", 1, Compatible(), MobModelManifestTests.ValidManifest()));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Reset("session", 0, Compatible(), MobModelManifestTests.ValidManifest()));
            Assert.Throws<ArgumentException>(() => state.Reset("session", 1, null, MobModelManifestTests.ValidManifest()));
            Assert.Throws<ArgumentNullException>(() => state.Reset("session", 1, Compatible(), null));
            var badManifest = MobModelManifestTests.ValidManifest(); badManifest.GameVersion = "other";
            Assert.Throws<ArgumentException>(() => state.Reset("session", 1, Compatible(), badManifest));
        }
    }
}
