using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // 第2段: 重いフレームのときだけ、数えた飛び道具系エッセンスの反応弾の生成を
    // 以降のフレームへ順序を保って分散する。抽選・対象選択・発動の情報は反応時点のまま。
    // 普段は即時。落としてはならない処理は捨てず、溜まりが続けば予算を段階的に広げる。
    internal static class NativeProcSpawnSpread
    {
        internal const float HeavyFrameSeconds = 1f / 30f;
        private const int Capacity = 320;
        // Native LockDestroyFor keeps the creating actor alive for 0.1 s; never defer past this age.
        private const float ForceSpawnAgeSeconds = 0.07f;
        private const int BaseDrain = 16;
        private const int MaxDrain = 128;
        private const int MaxLevel = 3;
        private const float WidenAfterSeconds = 0.6f;
        private const float NarrowAfterSeconds = 3f;

        private struct Deferred
        {
            internal SpawnShim Shim;
            internal Actor Parent;
            internal Actor Source;
            internal Gem Gem;
            internal Vector3 Position;
            internal Quaternion? Rotation;
            internal CastInfo Info;
            internal Delegate BeforePrepare;
            internal float QueuedAt;
        }

        private delegate void SpawnShim(ref Deferred item);

        private static readonly List<Deferred> Queue = new List<Deferred>(64);
        private static readonly Dictionary<Type, SpawnShim> PlainShims = new Dictionary<Type, SpawnShim>();
        private static readonly Dictionary<Type, SpawnShim> SourceShims = new Dictionary<Type, SpawnShim>();
        private static bool _heavy;
        private static bool _draining;
        private static bool _disabled;
        private static bool _warned;
        private static int _level;
        private static float _backlogSince = -1f;
        private static float _drainedSince = -1f;

        // 部屋ごとの診断（本体の開発者モードが有効なときだけログに出す）。
        private static Room _room;
        private static bool _diagnostics;
        private static int _roomSpawnDeferred;
        private static int _roomSpawnPeak;
        private static float _roomSpawnMaxDelay;
        private static int _roomPowerCarryEvents;
        private static int _roomPowerCarryPeak;
        private static float _roomPowerMaxDelay;

        internal static T Plain<T>(Actor self, Vector3 position, Quaternion? rotation, CastInfo info,
            Action<T> beforePrepare) where T : AbilityInstance
            => PlainSplit(self, position, rotation, info, beforePrepare, beforePrepare,
                typeof(T) == typeof(Ai_Gem_R_Spiral_Fireball) ? position : (Vector3?)null);

        // 第3段: repeatApply は、まとめ側の実体へ追加された通知を命中時にもう一度設定するための
        // delegate（作成時の設定から位置の変更などを除いたもの）。本体側の生成呼び出しは
        // 5引数の Plain のままなので、こちらは MOD 内からだけ使う。
        internal static T PlainSplit<T>(Actor self, Vector3 position, Quaternion? rotation, CastInfo info,
            Action<T> beforePrepare, Action<T> repeatApply, Vector3? start) where T : AbilityInstance
        {
            switch (NativeProcCoalesce.Offer(self, null, info, typeof(T), start, repeatApply, out var group))
            {
                case NativeProcCoalesce.Role.Appended:
                    return null;
                case NativeProcCoalesce.Role.Carrier:
                    beforePrepare = NativeProcCoalesce.Wrap(group, beforePrepare);
                    break;
            }
            if (!Defer()) return self.CreateAbilityInstance(position, rotation, info, beforePrepare);
            Enqueue(ShimFor(PlainShims, "DrainPlain", typeof(T)), self, null, null, position, rotation, info, beforePrepare);
            return null;
        }

        internal static T WithSource<T>(Gem self, Actor source, Vector3 position, Quaternion? rotation, CastInfo info,
            Action<T> beforePrepare) where T : AbilityInstance
        {
            // 照準・炭火の設定 callback は鎖・データ・強度だけを設定し、生成位置は変えない。
            Vector3? start = typeof(T) == typeof(Ai_Gem_R_Accuracy_Projectile)
                || typeof(T) == typeof(Ai_Gem_C_Charcoal_Projectile) ? position : (Vector3?)null;
            switch (NativeProcCoalesce.Offer(self, source, info, typeof(T), start, beforePrepare, out var group))
            {
                case NativeProcCoalesce.Role.Appended:
                    return null;
                case NativeProcCoalesce.Role.Carrier:
                    beforePrepare = NativeProcCoalesce.Wrap(group, beforePrepare);
                    break;
            }
            if (!Defer()) return self.CreateAbilityInstanceWithSource(source, position, rotation, info, beforePrepare);
            Enqueue(ShimFor(SourceShims, "DrainWithSource", typeof(T)), null, source, self, position, rotation, info, beforePrepare);
            return null;
        }

        internal static void Tick()
        {
            if (_disabled) return;
            // 生成の分散（Defer）もまとめ（NativeProcCoalesce.Offer）も診断の記録も
            // NetworkServer.active のときにだけ動く。参加者ではキューが空のまま、
            // 部屋ごとの集計もゼロのままなので、毎フレームの重いフレームの判定と
            // 部屋の追跡は結果のない仕事である。参加者では何もせず抜け、ホストに
            // 戻ったときは次の Tick で追跡を再開する。
            if (!NetworkServer.active) return;
            try
            {
                TickRoom();
                var graphics = ManagerBase<GraphicsManager>.instance;
                _heavy = (graphics != null && graphics.perfPressureStrength > 0f) || Time.unscaledDeltaTime > HeavyFrameSeconds;
                if (Queue.Count == 0)
                {
                    float now = Time.unscaledTime;
                    if (_drainedSince < 0f) _drainedSince = now;
                    else if (_level > 0 && now - _drainedSince >= NarrowAfterSeconds)
                    {
                        _level--;
                        _drainedSince = now;
                    }
                    return;
                }
                int limit = Math.Min(BaseDrain << _level, MaxDrain);
                long budget = Stopwatch.Frequency * (2 << _level) / 1000;
                long start = Stopwatch.GetTimestamp();
                int done = 0;
                _draining = true;
                try
                {
                    float drainNow = Time.unscaledTime;
                    bool overBudget = false;
                    // Within budget, or the oldest waiting spawn is close to the native 0.1 s LockDestroyFor
                    // window: those are always created this frame so no proc is ever skipped.
                    while (done < Queue.Count && ((!overBudget && done < limit) || drainNow - Queue[done].QueuedAt >= ForceSpawnAgeSeconds))
                    {
                        var item = Queue[done];
                        if (_diagnostics)
                        {
                            _roomSpawnDeferred++;
                            float age = Time.unscaledTime - item.QueuedAt;
                            if (age > _roomSpawnMaxDelay) _roomSpawnMaxDelay = age;
                        }
                        try { item.Shim(ref item); }
                        catch (Exception ex) { WarnOnce("deferred spawn failed; dropped like a failed native creation: " + ex.Message); }
                        done++;
                        if (!overBudget && (done >= limit || Stopwatch.GetTimestamp() - start >= budget)) overBudget = true;
                    }
                }
                finally { _draining = false; }
                Queue.RemoveRange(0, done);
                float now2 = Time.unscaledTime;
                if (Queue.Count > 0)
                {
                    // #1029: 持ち越しを観測した時点で空だった時刻の記録を解除し、
                    // 次に捌き切った時刻から 3 秒を数え直す（直後の縮小を防ぐ）。
                    _drainedSince = -1f;
                    if (_backlogSince < 0f) _backlogSince = now2;
                    else if (now2 - _backlogSince >= WidenAfterSeconds && _level < MaxLevel)
                    {
                        _level++;
                        _backlogSince = now2;
                    }
                }
                else
                {
                    _backlogSince = -1f;
                    if (_drainedSince < 0f) _drainedSince = now2;
                }
            }
            catch (Exception ex)
            {
                _disabled = true;
                WarnOnce("projectile spread disabled; native immediate creation retained: " + ex.Message);
            }
        }

        internal static void RecordPowerCarry(int leftover, float maxAge)
        {
            _roomPowerCarryEvents++;
            if (leftover > _roomPowerCarryPeak) _roomPowerCarryPeak = leftover;
            if (maxAge > _roomPowerMaxDelay) _roomPowerMaxDelay = maxAge;
        }

        private static bool Defer()
            => !_disabled && !_draining && NetworkServer.active && _heavy && Queue.Count < Capacity;

        private static void Enqueue(SpawnShim shim, Actor parent, Actor source, Gem gem, Vector3 position,
            Quaternion? rotation, CastInfo info, Delegate beforePrepare)
        {
            Queue.Add(new Deferred
            {
                Shim = shim, Parent = parent, Source = source, Gem = gem,
                Position = position, Rotation = rotation, Info = info,
                BeforePrepare = beforePrepare, QueuedAt = Time.unscaledTime,
            });
            if (Queue.Count > _roomSpawnPeak) _roomSpawnPeak = Queue.Count;
        }

        private static SpawnShim ShimFor(Dictionary<Type, SpawnShim> cache, string drain, Type type)
        {
            if (!cache.TryGetValue(type, out var shim))
            {
                var method = AccessTools.Method(typeof(NativeProcSpawnSpread), drain) ?? throw new MissingMethodException(drain);
                shim = (SpawnShim)Delegate.CreateDelegate(typeof(SpawnShim), method.MakeGenericMethod(type));
                cache.Add(type, shim);
            }
            return shim;
        }

        private static void DrainPlain<T>(ref Deferred item) where T : AbilityInstance
            => Dew.CreateAbilityInstance<T>(item.Position, item.Rotation, item.Parent, item.Info,
                (Action<T>)item.BeforePrepare);

        private static void DrainWithSource<T>(ref Deferred item) where T : AbilityInstance
            => item.Gem.CreateAbilityInstanceWithSource<T>(item.Source, item.Position, item.Rotation, item.Info,
                (Action<T>)item.BeforePrepare);

        private static void TickRoom()
        {
            var room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom;
            if (room == _room) return;
            if (_room != null) FlushRoomLog();
            _room = room;
            if (room == null) return;
            _roomSpawnDeferred = 0; _roomSpawnPeak = 0; _roomSpawnMaxDelay = 0f;
            _roomPowerCarryEvents = 0; _roomPowerCarryPeak = 0; _roomPowerMaxDelay = 0f;
            NativeProcCoalesce.ResetRoom();
            _diagnostics = ReadDeveloperMode();
        }

        private static bool ReadDeveloperMode()
        {
            try { return DewSave.platformSettings != null && DewSave.platformSettings.gameplay != null
                    && DewSave.platformSettings.gameplay.enableDeveloperMode; }
            catch { return false; }
        }

        private static void FlushRoomLog()
        {
            if (!_diagnostics) return;
            if (_roomSpawnPeak == 0 && _roomPowerCarryPeak == 0 && NativeProcCoalesce.RoomMerged == 0) return;
            Log.Info("Perf carryover room=" + _room.GetInstanceID()
                + ": deferredProjectiles total=" + _roomSpawnDeferred + " peak=" + _roomSpawnPeak
                + " maxDelay=" + (int)(_roomSpawnMaxDelay * 1000f) + "ms"
                + "; powerQueue carryEvents=" + _roomPowerCarryEvents + " peak=" + _roomPowerCarryPeak
                + " maxDelay=" + (int)(_roomPowerMaxDelay * 1000f) + "ms"
                + "; coalesced projectiles=" + NativeProcCoalesce.RoomMerged
                + " peakGroup=" + NativeProcCoalesce.RoomPeakGroup);
        }

        private static void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Log.Warn("Projectile spread: " + message);
        }
    }
}
