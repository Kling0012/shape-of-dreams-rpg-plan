using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed class BuildTransferPart
    {
        public string Data { get; set; }
        public string TransferId { get; set; }
        public int Index { get; set; }
        public int Count { get; set; }
        public int TotalLength { get; set; }
    }

    /// <summary>Lossless fragmentation below the native CustomRpc JSON string limit.</summary>
    public static class BuildTransfer
    {
        // Verified in the installed Mirror.NetworkWriterExtensions.WriteString implementation:
        // a UInt16 stores UTF-8 byte length plus one, reserving zero for null.
        public const int NativeStringBytes = ushort.MaxValue - 1;
        public const int TransferIdChars = 32;
        public const int MaxJsonCharacterBytes = 6;
        // Both message directions fit these seven fields: summary (longer than build), transferId,
        // index, count, totalLength, protocol and heroNetId. Include braces, commas, property quotes,
        // colons, both string-value quotes, Guid32, four signed Int32s and one UInt32.
        public const int MaxJsonEnvelopeBytes = 2 + 6
            + (7 + 10 + 5 + 5 + 11 + 8 + 9) + 7 * 3 + 4
            + TransferIdChars + 4 * BuildLimits.IntegerChars + 10;
        public const int ChunkChars = (NativeStringBytes - MaxJsonEnvelopeBytes) / MaxJsonCharacterBytes;
        public static int MaxParts => (BuildLimits.MaxEncodedChars + ChunkChars - 1) / ChunkChars;

        public static IReadOnlyList<BuildTransferPart> Split(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length == 0 || text.Length > BuildLimits.MaxEncodedChars || !Ascii(text))
                throw new ArgumentException("A build transfer requires a bounded nonempty ASCII payload.", nameof(text));
            int count = (text.Length + ChunkChars - 1) / ChunkChars;
            string transferId = Guid.NewGuid().ToString("N");
            var result = new BuildTransferPart[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * ChunkChars;
                result[i] = new BuildTransferPart
                {
                    Data = text.Substring(offset, Math.Min(ChunkChars, text.Length - offset)),
                    TransferId = transferId, Index = i, Count = count, TotalLength = text.Length,
                };
            }
            return Array.AsReadOnly(result);
        }

        internal static bool Ascii(string text)
        {
            foreach (char c in text) if (c > 127) return false;
            return true;
        }

        internal static bool Valid(BuildTransferPart part)
        {
            if (part == null || part.TransferId == null || part.TransferId.Length != TransferIdChars
                || part.TotalLength <= 0 || part.TotalLength > BuildLimits.MaxEncodedChars
                || part.Count <= 0 || part.Count > MaxParts || part.Index < 0 || part.Index >= part.Count
                || part.Count != (part.TotalLength + ChunkChars - 1) / ChunkChars
                || part.Data == null || part.Data.Length > ChunkChars) return false;
            foreach (char c in part.TransferId)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            int expected = Math.Min(ChunkChars, part.TotalLength - part.Index * ChunkChars);
            return part.Data.Length == expected && Ascii(part.Data);
        }
    }

    /// <summary>One bounded transfer per peer. A new valid ID supersedes any incomplete transfer.</summary>
    public sealed class BuildTransferReceiver
    {
        private string _transferId;
        private string _lastCompletedId;
        private string[] _parts;
        private int _totalLength;
        private int _received;

        public void Reset()
        {
            ClearPending();
            _lastCompletedId = null;
        }

        /// <summary>False rejects and clears an invalid transfer; true may still await remaining parts.</summary>
        public bool TryAccept(BuildTransferPart part, out string complete)
        {
            complete = null;
            if (!BuildTransfer.Valid(part) || part.TransferId == _lastCompletedId) return Reject();
            if (_transferId != part.TransferId)
            {
                ClearPending();
                _transferId = part.TransferId;
                _totalLength = part.TotalLength;
                _parts = new string[part.Count];
            }
            else if (_parts.Length != part.Count || _totalLength != part.TotalLength) return Reject();
            if (_parts[part.Index] != null) return Reject();
            _parts[part.Index] = part.Data;
            _received++;
            if (_received != _parts.Length) return true;
            string joined = string.Concat(_parts);
            if (joined.Length != _totalLength) return Reject();
            complete = joined;
            _lastCompletedId = _transferId;
            ClearPending();
            return true;
        }

        private bool Reject()
        {
            ClearPending();
            return false;
        }

        private void ClearPending()
        {
            _transferId = null;
            _parts = null;
            _totalLength = 0;
            _received = 0;
        }
    }
}
