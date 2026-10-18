using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    // C-II: TryPeek のレート判定（フレーム上限・トークンバケット）はチャンク JSON のバイト数に依存する。
    // 本体の軽量化（判定ごとの GetByteCount を Json 組み立て時の JsonBytes キャッシュへ置き換え）が
    // 判定結果を変えないことを、変更前と同じ意味論（判定のたびに Encoding.UTF8.GetByteCount を数えるモデル）
    // と同じタイムラインで突き合わせて確かめる。トークン枯渇により同じチャンクが複数フレーム保留される
    // 経路（キャッシュが使われる経路）と、Retry 後に index 0 から組み立て直す経路の両方を通す。
    public sealed class CoopTradeSendQueueRateTests : IDisposable
    {
        private readonly int _previousFrame = UnityEngine.Time.frameCount;
        private readonly float _previousTime = UnityEngine.Time.unscaledTime;

        public void Dispose()
        {
            UnityEngine.Time.frameCount = _previousFrame;
            UnityEngine.Time.unscaledTime = _previousTime;
        }

        private static string Payload(int fullChunks, int lastChars)
        {
            int length = fullChunks * BuildTransfer.ChunkChars + lastChars;
            var text = new StringBuilder(length);
            for (int i = 0; i < length; i++) text.Append((char)('A' + i % 26));
            return text.ToString();
        }

        [Fact]
        public void TryPeek_matches_uncached_byte_count_decisions_while_rate_limits_hold_and_release_chunks()
        {
            // 12 チャンク（11 full + 1 部分チャンク）。フレーム上限で途中のチャンクが保留され、
            // 凍結時間のフレームではトークン不足だけで保留され、補充後に同じチャンクが送られる。
            var driver = new Driver(new DreamforgeCoopTradeState { kind = "profile", id = "rate-1", revision = 7 },
                Payload(11, BuildTransfer.ChunkChars - 500));
            Assert.Equal(12, driver.ChunkCount);

            // フレーム 100: 5 チャンク送った後、6 チャンク目はフレーム上限・トークンともに不足で保留。
            Assert.Equal(5, driver.Flush(100, 100.0f));
            // フレーム 101〜102: 時間を進めないのでトークンが補充されず、同じチャンクが保留され続ける
            // （キャッシュされた JsonBytes を毎フレーム読む経路）。
            Assert.Equal(0, driver.Flush(101, 100.0f));
            Assert.Equal(0, driver.Flush(102, 100.0f));
            // フレーム 103: 0.5 秒進んでトークン上限まで補充。チャンク 5〜9 を送り、10 を保留。
            Assert.Equal(5, driver.Flush(103, 100.5f));
            // フレーム 104〜106: フレーム上限は空いているがトークン不足だけで保留（純トークンの複数フレーム保留）。
            Assert.Equal(0, driver.Flush(104, 100.5f));
            Assert.Equal(0, driver.Flush(105, 100.5f));
            Assert.Equal(0, driver.Flush(106, 100.5f));
            // フレーム 107: 補充後に残りの full チャンクと部分チャンクを送り、転送が完了する。
            Assert.Equal(2, driver.Flush(107, 101.0f));
            Assert.False(driver.Pending);

            driver.VerifySentEra(era: 0);
        }

        [Fact]
        public void TryPeek_rebuilds_from_index_zero_with_a_fresh_id_after_retry()
        {
            // 9 チャンク（8 full + 1 部分チャンク）。5 チャンク送ったところで送信失敗の Retry が起こり、
            // 5 秒のゲートのあとに新しい転送 ID で index 0 から組み立て直される。
            var driver = new Driver(new DreamforgeCoopTradeState { kind = "profile", id = "rate-2", revision = 3 },
                Payload(8, 500));
            Assert.Equal(9, driver.ChunkCount);

            Assert.Equal(5, driver.Flush(200, 200.0f));
            string firstId = driver.EraTransferId(0);
            Assert.True(driver.Contains(firstId));

            driver.Retry(200.0f);
            Assert.False(driver.Contains(firstId));

            // ゲート中（unscaledTime < retryAt）はどのフレームでも送らない。
            Assert.Equal(0, driver.Flush(201, 200.5f));
            Assert.Equal(0, driver.Flush(202, 201.5f));
            Assert.Equal(0, driver.Flush(203, 204.5f));

            // ゲート解除後のフレーム 204: 新しい ID で index 0 から組み立て直して 5 チャンク送る。
            Assert.Equal(5, driver.Flush(204, 205.0f));
            string secondId = driver.EraTransferId(1);
            Assert.NotEqual(firstId, secondId);
            Assert.True(driver.Contains(secondId));

            // フレーム 205: 残りの full 3 チャンクと部分チャンクを送り、転送が完了する。
            Assert.Equal(4, driver.Flush(205, 205.5f));
            Assert.False(driver.Pending);

            // Retry 前に送れた 5 チャンク（era 0）と、組み立て直し後の全 9 チャンク（era 1）。
            Assert.Equal(5, driver.EraSentCount(0));
            string reassembled = driver.VerifySentEra(era: 1);
            string expected = Payload(8, 500);
            Assert.Equal(expected, reassembled);
        }

        // 実物の CoopTradeSendQueue と、変更前の意味論で判定する影のモデルを同じ呼び出し列で動かす。
        // モデルは判定のたびに Encoding.UTF8.GetByteCount(json) + 4 を与え、TryPeek と同じ
        // フレーム上限・トークンバケット・Retry ゲートの規則を適用する。どちらかが違うフレームで
        // 送ったり保留したりしたら、JsonBytes キャッシュが GetByteCount(Json) からずれたことになる。
        private sealed class Driver
        {
            private readonly CoopTradeSendQueue _queue = new CoopTradeSendQueue();
            private readonly RateModel _model = new RateModel();
            private readonly DreamforgeCoopTradeState _template;
            private readonly string _encoded;
            private readonly int[] _chunkBytes;
            private readonly List<string> _sent = new List<string>();
            private readonly List<int> _sentEra = new List<int>();
            private int _nextChunk, _era;
            private bool _completed;

            internal int ChunkCount => _chunkBytes.Length;
            internal bool Pending => _queue.Pending;

            internal Driver(DreamforgeCoopTradeState envelope, string encoded)
            {
                _encoded = encoded;
                // TryPeek が envelope を書き換えるため、検証用に登録時点のコピーを取っておく。
                _template = JsonConvert.DeserializeObject<DreamforgeCoopTradeState>(JsonConvert.SerializeObject(envelope));
                int count = (encoded.Length + BuildTransfer.ChunkChars - 1) / BuildTransfer.ChunkChars;
                _chunkBytes = new int[count];
                for (int i = 0; i < count; i++)
                    _chunkBytes[i] = System.Text.Encoding.UTF8.GetByteCount(CoopTradeTransport.Encode(CloneForChunk(i))) + 4;
                _queue.Enqueue(envelope, encoded);
            }

            // 1 フレーム分の送信フラッシュ。FlushCoopPayloads と同じ while ループで回し、
            // 呼び出しのたびに実物とモデルの判定を突き合わせる。
            internal int Flush(int frame, float now)
            {
                UnityEngine.Time.frameCount = frame;
                UnityEngine.Time.unscaledTime = now;
                int sent = 0;
                while (_queue.Pending)
                {
                    bool expected = _completed ? false : _model.Admits(frame, now, _chunkBytes[_nextChunk]);
                    bool actual = _queue.TryPeek(out string json);
                    Assert.Equal(expected, actual);
                    if (!actual) break;
                    // 送り出した JSON の実バイト数が、その送信に課金されたコスト（モデル値）と一致する。
                    Assert.Equal(_chunkBytes[_nextChunk], System.Text.Encoding.UTF8.GetByteCount(json) + 4);
                    _sent.Add(json);
                    _sentEra.Add(_era);
                    sent++;
                    _queue.Sent();
                    if (++_nextChunk == _chunkBytes.Length) _completed = true;
                }
                if (_completed) Assert.False(_queue.TryPeek(out _));
                return sent;
            }

            internal void Retry(float now)
            {
                UnityEngine.Time.unscaledTime = now;
                _queue.Retry();
                _model.RetryAt(now);
                _nextChunk = 0;
                _completed = false;
                _era++;
            }

            internal bool Contains(string id) => _queue.Contains(id);

            internal int EraSentCount(int era)
            {
                int count = 0;
                for (int i = 0; i < _sentEra.Count; i++) if (_sentEra[i] == era) count++;
                return count;
            }

            internal string EraTransferId(int era)
            {
                for (int i = 0; i < _sentEra.Count; i++)
                    if (_sentEra[i] == era)
                        return CoopTradeTransport.Decode<DreamforgeCoopTradeState>(_sent[i]).transferId;
                return null;
            }

            // 指定 era に送られたチャンク列を検証し、組み立て直した全文を返す。
            internal string VerifySentEra(int era)
            {
                string transferId = EraTransferId(era);
                Assert.NotNull(transferId);
                var reassembled = new StringBuilder();
                int seen = 0;
                for (int i = 0; i < _sent.Count; i++)
                {
                    if (_sentEra[i] != era) continue;
                    var decoded = CoopTradeTransport.Decode<DreamforgeCoopTradeState>(_sent[i]);
                    Assert.Equal(seen, decoded.index);
                    Assert.Equal(transferId, decoded.transferId);
                    Assert.Equal(_chunkBytes.Length, decoded.count);
                    Assert.Equal(_encoded.Length, decoded.totalLength);
                    Assert.Equal(_template.kind, decoded.kind);
                    Assert.Equal(_template.id, decoded.id);
                    Assert.Equal(_template.revision, decoded.revision);
                    int offset = seen * BuildTransfer.ChunkChars;
                    Assert.Equal(_encoded.Substring(offset, Math.Min(BuildTransfer.ChunkChars, _encoded.Length - offset)), decoded.data);
                    if (seen > 0)
                    {
                        // 2 チャンク目以降は転送 ID が既知なので、JSON 文字列そのものを組み立てて一致を見る。
                        var expected = CloneForChunk(seen);
                        expected.transferId = transferId;
                        Assert.Equal(CoopTradeTransport.Encode(expected), _sent[i]);
                    }
                    reassembled.Append(decoded.data);
                    seen++;
                }
                Assert.Equal(_chunkBytes.Length, seen);
                return reassembled.ToString();
            }

            // チャンク i の送信 JSON の内容を検証用に再現する。transferId は Guid の "N" 形式と
            // 同じ 32 文字で埋める（バイト数は実 ID と一致する）。
            private DreamforgeCoopTradeState CloneForChunk(int chunkIndex)
            {
                var clone = JsonConvert.DeserializeObject<DreamforgeCoopTradeState>(JsonConvert.SerializeObject(_template));
                int offset = chunkIndex * BuildTransfer.ChunkChars;
                clone.data = _encoded.Substring(offset, Math.Min(BuildTransfer.ChunkChars, _encoded.Length - offset));
                clone.transferId = new string('0', 32);
                clone.index = chunkIndex;
                clone.count = _chunkBytes.Length;
                clone.totalLength = _encoded.Length;
                return clone;
            }
        }

        // 変更前の TryPeek と同じ規則・同じ型で、判定のたびに与えられたバイト数（GetByteCount 相当）を使う影のモデル。
        private sealed class RateModel
        {
            private int _frame = -1, _frameBytes;
            private double _tokens = CoopTradeSendQueue.BytesPerFrame;
            private float _at, _retryAt;

            internal void RetryAt(float now) { _retryAt = now + 5f; }

            internal bool Admits(int frame, float now, int bytes)
            {
                if (now < _retryAt) return false;
                if (_frame != frame) { _frame = frame; _frameBytes = 0; }
                _tokens = Math.Min(CoopTradeSendQueue.BytesPerFrame,
                    _tokens + Math.Max(0f, now - _at) * CoopTradeSendQueue.BytesPerSecond);
                _at = now;
                if (_frameBytes + bytes > CoopTradeSendQueue.BytesPerFrame || bytes > _tokens) return false;
                _frameBytes += bytes;
                _tokens -= bytes;
                return true;
            }
        }
    }
}
