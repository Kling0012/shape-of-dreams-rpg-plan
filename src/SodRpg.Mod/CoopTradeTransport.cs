using System;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;

namespace SodRpg.Mod
{
    // Explicit readers/writers avoid depending on Mirror's build-time weaver for MOD message types.
    public struct DreamforgeCoopTradeUp : NetworkMessage { public string Json; }
    public struct DreamforgeCoopTradeDown : NetworkMessage { public string Json; }

    // One FIFO per connection. Only an entirely unsent snapshot may be superseded.
    internal sealed class CoopTradeSendQueue
    {
        private sealed class Transfer
        {
            internal string Encoded, Id, Json;
            // Utf8 byte count of Json, set whenever Json is built. TryPeek runs every frame
            // while the rate limiter holds the same chunk; this avoids rescanning it each time.
            internal int JsonBytes;
            internal object Envelope;
            internal int Index, Count;
            internal bool Started;
        }
        internal const int BytesPerFrame = 64 * 1024;
        internal const int BytesPerSecond = 128 * 1024;
        private readonly Transfer[] _pending = new Transfer[4];
        private int _count, _frame = -1, _frameBytes;
        private double _tokens = BytesPerFrame;
        private float _at, _retryAt;
        private bool _flushing;
        private Transfer _sending;
        internal bool Pending => _count > 0;

        internal bool BeginFlush()
        {
            if (_flushing) return false;
            _flushing = true;
            return true;
        }
        internal void EndFlush() { _sending = null; _flushing = false; }

        internal void Reset()
        {
            Array.Clear(_pending, 0, _pending.Length);
            _count = 0;
            // Clearing a failed/cancelled transfer must not replenish this frame's budget.
        }

        internal string Enqueue(object envelope, string encoded)
        {
            if (encoded == null || encoded.Length == 0 || encoded.Length > CoopTradeTransport.MaxProfileChars)
                throw new InvalidOperationException("Trade transfer exceeds queue capacity.");
            for (int i = 0; i < _count; i++)
            {
                var old = _pending[i];
                bool same = envelope is DreamforgeCoopTradeState state && old.Envelope is DreamforgeCoopTradeState prior
                    && state.kind == prior.kind && state.id == prior.id;
                if (!same && envelope is DreamforgeCoopTradeCommand command && old.Envelope is DreamforgeCoopTradeCommand previous)
                    same = command.action == previous.action && command.id == previous.id;
                if (!same) continue;
                bool sameRevision = envelope is DreamforgeCoopTradeState current && old.Envelope is DreamforgeCoopTradeState priorState
                    && current.revision == priorState.revision;
                if (envelope is DreamforgeCoopTradeCommand currentCommand && old.Envelope is DreamforgeCoopTradeCommand priorCommand)
                    sameRevision = currentCommand.revision == priorCommand.revision && currentCommand.offer == priorCommand.offer;
                if (sameRevision && old.Encoded == encoded) return old.Id;
                if (!old.Started)
                {
                    var replacement = Create(envelope, encoded);
                    _pending[i] = replacement;
                    return replacement.Id;
                }
            }
            if (_count == _pending.Length) throw new InvalidOperationException("Trade send queue is full; retry remains pending.");
            var transfer = Create(envelope, encoded);
            _pending[_count++] = transfer;
            return transfer.Id;
        }

        private static Transfer Create(object envelope, string encoded) => new Transfer
        {
            Envelope = envelope, Encoded = encoded, Id = Guid.NewGuid().ToString("N"),
            Count = (encoded.Length + SodRpg.Core.Game.BuildTransfer.ChunkChars - 1) / SodRpg.Core.Game.BuildTransfer.ChunkChars,
        };

        internal void Retry()
        {
            // A failed send may already be queued in Mirror. Restart the head with a fresh ID.
            if (_count == 0) return;
            var transfer = _pending[0];
            transfer.Id = Guid.NewGuid().ToString("N");
            transfer.Index = 0; transfer.Json = null; transfer.Started = true;
            _retryAt = Time.unscaledTime + 5f;
        }

        internal bool Contains(string id)
        {
            for (int i = 0; i < _count; i++) if (_pending[i].Id == id) return true;
            return false;
        }

        internal bool TryPeek(out string json)
        {
            json = null;
            if (_count == 0) return false;
            float now = Time.unscaledTime;
            if (now < _retryAt) return false;
            if (_frame != Time.frameCount) { _frame = Time.frameCount; _frameBytes = 0; }
            _tokens = Math.Min(BytesPerFrame, _tokens + Math.Max(0, now - _at) * BytesPerSecond);
            _at = now;
            var transfer = _pending[0];
            if (transfer.Json == null)
            {
                int offset = transfer.Index * SodRpg.Core.Game.BuildTransfer.ChunkChars;
                string data = transfer.Encoded.Substring(offset, Math.Min(SodRpg.Core.Game.BuildTransfer.ChunkChars, transfer.Encoded.Length - offset));
                if (transfer.Envelope is DreamforgeCoopTradeCommand command)
                {
                    command.data = data; command.transferId = transfer.Id; command.index = transfer.Index;
                    command.count = transfer.Count; command.totalLength = transfer.Encoded.Length;
                }
                else if (transfer.Envelope is DreamforgeCoopTradeState state)
                {
                    state.data = data; state.transferId = transfer.Id; state.index = transfer.Index;
                    state.count = transfer.Count; state.totalLength = transfer.Encoded.Length;
                }
                transfer.Json = CoopTradeTransport.Encode(transfer.Envelope);
                transfer.JsonBytes = System.Text.Encoding.UTF8.GetByteCount(transfer.Json);
            }
            int bytes = transfer.JsonBytes + 4;
            if (_frameBytes + bytes > BytesPerFrame || bytes > _tokens) return false;
            // Delivery can fail after enqueueing in the transport; never replace a possibly started ID.
            transfer.Started = true;
            _sending = transfer;
            _frameBytes += bytes; _tokens -= bytes;
            json = transfer.Json;
            return true;
        }

        internal void Sent()
        {
            if (_count == 0 || !ReferenceEquals(_sending, _pending[0])) return;
            var transfer = _pending[0];
            _sending = null;
            transfer.Started = true; transfer.Json = null;
            if (++transfer.Index < transfer.Count) return;
            for (int i = 1; i < _count; i++) _pending[i - 1] = _pending[i];
            _pending[--_count] = null;
        }
    }

    internal static class CoopTradeTransport
    {
        internal const string CapabilityKey = "Dreamforge.CoopTrade.v1";
        // Profiles include stash/Continue economic receipts, not just six equipped relics.
        // Bound one on-demand transfer per peer; no buffers exist while trade is idle.
        internal const int MaxProfileChars = 4 * 1024 * 1024;
        internal static bool PayloadFits(string text) => text != null
            && 4L * ((System.Text.Encoding.UTF8.GetByteCount(text) + 2L) / 3L) <= MaxProfileChars;
        private static bool _initialized;
        internal static void Initialize()
        {
            if (_initialized) return;
            Writer<DreamforgeCoopTradeUp>.write = (writer, message) => writer.WriteString(message.Json);
            Reader<DreamforgeCoopTradeUp>.read = reader => new DreamforgeCoopTradeUp { Json = reader.ReadString() };
            Writer<DreamforgeCoopTradeDown>.write = (writer, message) => writer.WriteString(message.Json);
            Reader<DreamforgeCoopTradeDown>.read = reader => new DreamforgeCoopTradeDown { Json = reader.ReadString() };
            _initialized = true;
        }
        internal static string Encode(object message)
        {
            string json = JsonConvert.SerializeObject(message);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > BuildStringLimit)
                throw new InvalidOperationException("Trade message exceeds native string capacity.");
            return json;
        }
        private const int BuildStringLimit = 60000;
        internal static T Decode<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json) || json.Length > BuildStringLimit) return null;
            // A peer's unreadable packet is not a host storage failure, regardless of the decoder's exception type.
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch (Exception) { return null; }
        }
    }
}
