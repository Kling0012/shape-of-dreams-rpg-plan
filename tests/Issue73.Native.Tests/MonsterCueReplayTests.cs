using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #904 の軽量化（参加者への行動合図の再送から cue=0 を省く）の境界。
    /// 送否の判定は <c>HostAuthority.ShouldSendMonsterBehaviorCue</c> に純粋関数として切り出してあり、
    /// ここで (a) 判定表、(b) ゲストの cue 辞書モデルでの変更前後の等価、
    /// (c) FlushMonsterSync の予算超過時の持ち越しを確かめる。
    /// ハーネスは HostAuthority.Monsters.cs をコンパイルしないため、判定をこの関数に寄せて検証する。
    /// </summary>
    public sealed class MonsterCueReplayTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public MonsterCueReplayTests()
        {
            NetworkServer.active = true;
            Time.frameCount = 1;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            HostAuthority.NativeInstance = null;
        }

        public void Dispose()
        {
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            HostAuthority.NativeInstance = null;
        }

        /// <summary>(a) 対象指定の再送は cue=0 と state なしを省き、ブロードキャストは cue=0 も従来どおり送る。</summary>
        [Fact]
        public void PerPeerReplayOmitsZeroCuesWhileBroadcastsStillCarryThem()
        {
            // 対象指定（per-peer 再送）: cue=0 は送らず、0 以外は送る。state なし（cue は定義上 0）も送らない。
            Assert.False(HostAuthority.ShouldSendMonsterBehaviorCue(true, 0, perPeerReplay: true));
            Assert.False(HostAuthority.ShouldSendMonsterBehaviorCue(false, 0, perPeerReplay: true));
            foreach (int cue in new[] { 1, 2, 3, 4 })
                Assert.True(HostAuthority.ShouldSendMonsterBehaviorCue(true, cue, perPeerReplay: true));
            // ブロードキャスト: cue=0 も従来どおり送る（全ゲストのエントリを掃除する役目は残る）。state なしは送らない。
            Assert.True(HostAuthority.ShouldSendMonsterBehaviorCue(true, 0, perPeerReplay: false));
            Assert.True(HostAuthority.ShouldSendMonsterBehaviorCue(true, 3, perPeerReplay: false));
            Assert.False(HostAuthority.ShouldSendMonsterBehaviorCue(false, 0, perPeerReplay: false));
        }

        /// <summary>
        /// (b) ゲストの cue 辞書のモデルに「ブロードキャスト列のあとに再送列」を適用したとき、
        /// 省略の前後で最終状態が同じになる。ブロードキャスト列は実際の QueueMonsterCue →
        /// FlushMonsterSync が送った列を使い、変更後の再送列の送否は製品と同じ純粋関数に聞く。
        /// </summary>
        [Fact]
        public void BroadcastColumnThenReplayColumnReachesTheSameGuestCueStateWithAndWithoutTheZeroOmission()
        {
            var actor = new Actor();
            var host = Host(actor);
            // ブロードキャスト列: 活動中の合図の後に、行動の終わった 2・3 への除去 0 が続く。
            (uint Id, int Cue)[] broadcasts = { (1, 2), (2, 3), (3, 1), (4, 2), (2, 0), (3, 0) };
            foreach (var b in broadcasts) Call(host, "QueueMonsterCue", b.Id, b.Cue);
            Call(host, "FlushMonsterSync", 32);
            var column = actor.Sent.Select(s => s.Message).OfType<DreamforgeMonsterCueMsg>().ToArray();
            // 同一 netId の中間値は最新値で置き換わる（2 の 3 は 0 に、3 の 1 は 0 に併合）。
            Assert.Equal(new (uint, int)[] { (1, 2), (2, 0), (3, 0), (4, 2) }, column.Select(m => (m.netId, m.cue)));
            // 再送列の対象: 1〜4 は上の最終値（2 は行動終了で state なし、3 は計算値が 0）、5 は最初から state なし。
            var monsters = new[]
            {
                (Id: 1u, HasState: true, Cue: 2),
                (Id: 2u, HasState: false, Cue: 0),
                (Id: 3u, HasState: true, Cue: 0),
                (Id: 4u, HasState: true, Cue: 2),
                (Id: 5u, HasState: false, Cue: 0),
            };
            // 変更前の再送列: 対象ごとに必ず1通（state なしは cue=0、ありは計算値）。
            var before = monsters
                .Select(m => new DreamforgeMonsterCueMsg { netId = m.Id, cue = m.HasState ? m.Cue : 0 })
                .ToArray();
            // 変更後の再送列: 送否は製品と同じ純粋関数が決める。
            var after = monsters
                .Where(m => HostAuthority.ShouldSendMonsterBehaviorCue(m.HasState, m.Cue, perPeerReplay: true))
                .Select(m => new DreamforgeMonsterCueMsg { netId = m.Id, cue = m.Cue })
                .ToArray();
            Assert.All(after, msg => Assert.NotEqual(0, msg.cue)); // 省いたのは 0 だけ
            var beforeIds = new HashSet<uint>(before.Select(m => m.netId));
            Assert.All(after, msg => Assert.Contains(msg.netId, beforeIds)); // 変更後の再送列は変更前の部分集合

            Dictionary<uint, int> GuestOf(IEnumerable<DreamforgeMonsterCueMsg> replay)
            {
                var guest = new Dictionary<uint, int>();
                foreach (var msg in column.Concat(replay)) ApplyCue(guest, msg); // reliable ordered: ブロードキャスト列 → 再送列
                return guest;
            }
            var beforeGuest = GuestOf(before);
            var afterGuest = GuestOf(after);
            // どちらも「0 以外の最新値」だけが残る: 2 の古い 3 は除去 0 で消え、5 は最初から無い。
            Assert.Equal(new Dictionary<uint, int> { [1] = 2, [4] = 2 }, beforeGuest);
            Assert.Equal(beforeGuest, afterGuest); // 省略の前後でゲストの最終状態は同じ
        }

        /// <summary>
        /// (c) FlushMonsterSync は予算を超えた分を捨てず次のフラッシュへ持ち越す。
        /// ためている間に再送だけが cue=0 を運ぶ状態は作れないので、除去の 0 は必ずゲストへ届く。
        /// </summary>
        [Fact]
        public void CueZerosBeyondTheFlushBudgetAreCarriedToLaterFlushesNotDropped()
        {
            var actor = new Actor();
            var host = Host(actor);
            for (uint id = 1; id <= 6; id++) Call(host, "QueueMonsterCue", id, 0); // 行動の終わったモンスターの除去 0
            Call(host, "FlushMonsterSync", 2);
            Assert.Equal(2, actor.Sent.Count); // 予算の 2 通だけが今のフレームで出る
            Call(host, "FlushMonsterSync", 2);
            Assert.Equal(4, actor.Sent.Count); // 残りは捨てずに次のフラッシュへ持ち越す
            Call(host, "FlushMonsterSync", 32);
            var cues = actor.Sent.Select(s => Assert.IsType<DreamforgeMonsterCueMsg>(s.Message)).ToArray();
            Assert.Equal(6, cues.Length);
            Assert.All(cues, msg => Assert.Equal(0, msg.cue)); // 全件が届き、ゲストの古いエントリを確実に掃除する
        }

        // ClientSession.OnMonsterCue と同じ意味: 0 はエントリ削除、0 以外は値の設定。
        private static void ApplyCue(Dictionary<uint, int> guest, DreamforgeMonsterCueMsg msg)
        {
            if (msg.cue == 0) guest.Remove(msg.netId);
            else guest[msg.netId] = msg.cue;
        }

        private static HostAuthority Host(Actor actor)
        {
            var host = new HostAuthority();
            HostAuthority.NativeInstance = host;
            Set(host, "_registeredOn", actor);
            return host;
        }

        private static Type ObjectType(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;
        private static void Set(object target, string name, object value) => ObjectType(target).GetField(name, Hidden).SetValue(Instance(target), value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return ObjectType(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
}
