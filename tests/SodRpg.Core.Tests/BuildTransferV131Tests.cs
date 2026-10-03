using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class BuildTransferV131Tests
    {
        [Fact]
        public void Entire_build_envelope_crosses_the_native_string_limit_and_round_trips_reordered_parts()
        {
            var build = LargeBuild();
            string encoded = build.Encode();
            Assert.True(encoded.Length > BuildTransfer.NativeStringBytes);
            var parts = BuildTransfer.Split(encoded);
            Assert.InRange(parts.Count, 2, BuildTransfer.MaxParts);
            Assert.Equal(encoded.Length, parts.Sum(p => p.Data.Length));
            Assert.Single(parts.Select(p => p.TransferId).Distinct());
            var receiver = new BuildTransferReceiver();
            var appliedReceiver = new BuildTransferReceiver();
            var settings = new JsonSerializerOptions { IncludeFields = true };
            string complete = null;
            string appliedComplete = null;
            foreach (var part in parts.Reverse())
            {
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(DreamforgeBuildMsg.FromPart(part), settings);
                Assert.InRange(json.Length, 1, BuildTransfer.NativeStringBytes);
                var message = JsonSerializer.Deserialize<DreamforgeBuildMsg>(json, settings);
                Assert.Equal(Protocol.Version, message.protocol);
                Assert.True(receiver.TryAccept(message.ToPart(), out complete));
                byte[] appliedJson = JsonSerializer.SerializeToUtf8Bytes(DreamforgeAppliedMsg.FromPart(part, uint.MaxValue), settings);
                Assert.InRange(appliedJson.Length, 1, BuildTransfer.NativeStringBytes);
                var applied = JsonSerializer.Deserialize<DreamforgeAppliedMsg>(appliedJson, settings);
                Assert.Equal(Protocol.Version, applied.protocol);
                Assert.Equal(uint.MaxValue, applied.heroNetId);
                Assert.True(appliedReceiver.TryAccept(applied.ToPart(), out appliedComplete));
                if (part.Index != 0) Assert.Null(complete);
            }
            Assert.Equal(encoded, complete);
            Assert.Equal(encoded, appliedComplete);
            var decoded = Build.Decode(complete);
            Assert.NotNull(decoded);
            Assert.Equal(build.Gimmicks.Count, decoded.Gimmicks.Count);
            Assert.Equal(build.Links.Count, decoded.Links.Count);
            Assert.Equal(build.PairCombos.Count, decoded.PairCombos.Count);
            Assert.Equal(encoded, decoded.Encode());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(BuildTransfer.ChunkChars)]
        [InlineData(BuildTransfer.ChunkChars + 1)]
        [InlineData(BuildTransfer.ChunkChars * 2)]
        public void Exact_boundaries_preserve_every_character_and_complete_only_once(int length)
        {
            string text = new string('x', length);
            var parts = BuildTransfer.Split(text);
            Assert.Equal((length + BuildTransfer.ChunkChars - 1) / BuildTransfer.ChunkChars, parts.Count);
            var receiver = new BuildTransferReceiver();
            for (int i = 0; i < parts.Count; i++)
            {
                Assert.Equal(i, parts[i].Index);
                Assert.Equal(parts.Count, parts[i].Count);
                Assert.Equal(length, parts[i].TotalLength);
                Assert.True(receiver.TryAccept(parts[i], out string complete));
                Assert.Equal(i == parts.Count - 1 ? text : null, complete);
            }
            Assert.False(receiver.TryAccept(parts[parts.Count - 1], out var replay));
            Assert.Null(replay);
        }

        [Fact]
        public void Maximum_payload_fits_the_derived_part_count_and_bounded_receiver()
        {
            string text = new string('x', BuildLimits.MaxEncodedChars);
            var parts = BuildTransfer.Split(text);
            Assert.Equal(BuildTransfer.MaxParts, parts.Count);
            Assert.All(parts, part => Assert.InRange(part.Data.Length, 1, BuildTransfer.ChunkChars));
            var receiver = new BuildTransferReceiver();
            string complete = null;
            foreach (var part in parts) Assert.True(receiver.TryAccept(part, out complete));
            Assert.Equal(text, complete);
            Assert.Throws<ArgumentException>(() => BuildTransfer.Split(text + "x"));
            Assert.Throws<ArgumentException>(() => BuildTransfer.Split(""));
            Assert.Throws<ArgumentException>(() => BuildTransfer.Split("\u00e9"));
            Assert.Throws<ArgumentNullException>(() => BuildTransfer.Split(null));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("id-null")]
        [InlineData("id-length")]
        [InlineData("id-uppercase")]
        [InlineData("id-invalid")]
        [InlineData("negative-index")]
        [InlineData("index-overflow")]
        [InlineData("zero-count")]
        [InlineData("huge-count")]
        [InlineData("wrong-count")]
        [InlineData("zero-total")]
        [InlineData("huge-total")]
        [InlineData("oversized-total")]
        [InlineData("null-data")]
        [InlineData("huge-data")]
        [InlineData("short-data")]
        [InlineData("non-ascii")]
        public void Malicious_metadata_and_payloads_are_rejected_before_starting_a_transfer(string failure)
        {
            var parts = BuildTransfer.Split(new string('x', BuildTransfer.ChunkChars + 1));
            var bad = Copy(parts[0]);
            switch (failure)
            {
                case "null": bad = null; break;
                case "id-null": bad.TransferId = null; break;
                case "id-length": bad.TransferId += "0"; break;
                case "id-uppercase": bad.TransferId = new string('A', BuildTransfer.TransferIdChars); break;
                case "id-invalid": bad.TransferId = new string('z', BuildTransfer.TransferIdChars); break;
                case "negative-index": bad.Index = -1; break;
                case "index-overflow": bad.Index = int.MaxValue; break;
                case "zero-count": bad.Count = 0; break;
                case "huge-count": bad.Count = int.MaxValue; break;
                case "wrong-count": bad.Count++; break;
                case "zero-total": bad.TotalLength = 0; break;
                case "huge-total": bad.TotalLength = int.MaxValue; break;
                case "oversized-total": bad.TotalLength = BuildLimits.MaxEncodedChars + 1; break;
                case "null-data": bad.Data = null; break;
                case "huge-data": bad.Data += "x"; break;
                case "short-data": bad.Data = bad.Data.Substring(1); break;
                case "non-ascii": bad.Data = "\u00e9" + bad.Data.Substring(1); break;
            }
            var receiver = new BuildTransferReceiver();
            Assert.False(receiver.TryAccept(bad, out var complete));
            Assert.Null(complete);
            Assert.True(receiver.TryAccept(parts[1], out complete));
            Assert.Null(complete);
            Assert.True(receiver.TryAccept(parts[0], out complete));
            Assert.Equal(new string('x', BuildTransfer.ChunkChars + 1), complete);
        }

        [Fact]
        public void Duplicate_or_inconsistent_parts_clear_the_incomplete_transfer()
        {
            var parts = BuildTransfer.Split(new string('x', BuildTransfer.ChunkChars + 2));
            var receiver = new BuildTransferReceiver();
            Assert.True(receiver.TryAccept(parts[0], out _));
            Assert.False(receiver.TryAccept(parts[0], out var duplicate));
            Assert.Null(duplicate);
            Assert.True(receiver.TryAccept(parts[1], out var incomplete));
            Assert.Null(incomplete);
            var inconsistent = Copy(parts[0]);
            inconsistent.TotalLength--;
            Assert.False(receiver.TryAccept(inconsistent, out var rejected));
            Assert.Null(rejected);
            Assert.True(receiver.TryAccept(parts[0], out incomplete));
            Assert.Null(incomplete);
            Assert.True(receiver.TryAccept(parts[1], out var complete));
            Assert.Equal(new string('x', BuildTransfer.ChunkChars + 2), complete);
        }

        [Fact]
        public void New_transfer_supersedes_an_incomplete_one_and_explicit_reset_clears_state()
        {
            var first = BuildTransfer.Split(new string('x', BuildTransfer.ChunkChars + 1));
            var second = BuildTransfer.Split(new string('y', BuildTransfer.ChunkChars + 1));
            var receiver = new BuildTransferReceiver();
            Assert.True(receiver.TryAccept(first[0], out _));
            Assert.True(receiver.TryAccept(second[1], out var incomplete));
            Assert.Null(incomplete);
            Assert.True(receiver.TryAccept(second[0], out var complete));
            Assert.Equal(new string('y', BuildTransfer.ChunkChars + 1), complete);
            receiver.Reset();
            Assert.True(receiver.TryAccept(second[1], out incomplete));
            Assert.Null(incomplete);
            receiver.Reset();
            Assert.True(receiver.TryAccept(second[0], out incomplete));
            Assert.Null(incomplete);
            Assert.True(receiver.TryAccept(second[1], out complete));
            Assert.Equal(new string('y', BuildTransfer.ChunkChars + 1), complete);
        }

        [Fact]
        public void Both_actual_message_envelopes_fit_native_utf8_limit_even_with_six_byte_json_escapes()
        {
            string data = new string('\0', BuildTransfer.ChunkChars);
            string id = new string('f', BuildTransfer.TransferIdChars);
            var part = new BuildTransferPart { Data = data, TransferId = id, Index = int.MinValue,
                Count = int.MinValue, TotalLength = int.MinValue };
            var outbound = DreamforgeBuildMsg.FromPart(part);
            var applied = DreamforgeAppliedMsg.FromPart(part, uint.MaxValue);
            outbound.protocol = applied.protocol = int.MinValue;
            object[] messages =
            {
                outbound, applied,
            };
            var settings = new JsonSerializerOptions { IncludeFields = true };
            foreach (var message in messages)
            {
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, settings);
                string escaped = Encoding.UTF8.GetString(json);
                Assert.Contains("\\u0000", escaped);
                Assert.InRange(json.Length, 1, BuildTransfer.NativeStringBytes);
                Assert.True(json.Length - data.Length * BuildTransfer.MaxJsonCharacterBytes <= BuildTransfer.MaxJsonEnvelopeBytes);
            }
            Assert.True(BuildTransfer.ChunkChars * BuildTransfer.MaxJsonCharacterBytes
                + BuildTransfer.MaxJsonEnvelopeBytes <= BuildTransfer.NativeStringBytes);
            Assert.True((BuildTransfer.ChunkChars + 1) * BuildTransfer.MaxJsonCharacterBytes
                + BuildTransfer.MaxJsonEnvelopeBytes > BuildTransfer.NativeStringBytes);
        }

        private static BuildTransferPart Copy(BuildTransferPart part) => new BuildTransferPart
        {
            Data = part.Data, TransferId = part.TransferId, Index = part.Index, Count = part.Count, TotalLength = part.TotalLength,
        };

        private static Build LargeBuild()
        {
            var build = new Build { SpentStarPoints = StarProgression.MaxPoints };
            var memories = HeroSigils.All.Select(t => t.RouteMemory).Where(Links.IsMemory)
                .Distinct().OrderByDescending(m => m.Length).Take(BuildLimits.MaxLinkRequirements).ToArray();
            for (int i = 0; i < BuildLimits.MaxGimmickEntries; i++)
                build.Gimmicks.Add(new GimmickEntry
                {
                    StarId = i.ToString(CultureInfo.InvariantCulture).PadLeft(BuildLimits.MaxStarIdLength, 'x'),
                    Memory = memories[0], Def = new GimmickDef
                    {
                        Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Expose, Value = 99.999m,
                        Cooldown = 59.1234567f, DurationPercent = Gimmicks.MaxParameterPercent,
                    },
                });
            for (int i = 0; i < BuildLimits.MaxLinkEntries; i++)
                build.Links.Add(new LinkDef { Kind = LinkKind.MemorySurge, Requires = memories,
                    ValueMilli = BuildLimits.MaxLinkValueMilli(LinkKind.MemorySurge, memories.Length) });
            foreach (var pair in PairCombos.All) build.PairCombos.Add(new PairComboEntry { Def = pair, Ranks = PairCombos.MaxRanks });
            return build;
        }
    }
}
