using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // 束0（docs/specs/memory-projectile-coalesce.md 8.2〜8.4）: 飛び道具・Actor をサーバでは
    // 本体どおりすべて生成・処理したまま、リモートの参加者へ公開する個体数だけを減らす基板。
    // 対象は型ごとの登録表で、この時点では登録0件。そのためすべての Actor は今までどおり
    // 公開され、ゲーム効果も見た目も変わらない。
    //   - 生成は NetworkServer.Spawn を本体どおり通す。Spawn・ローカルホストの初期化は省かない。
    //   - 登録した型だけ、同じフレーム・同じ表示用の鍵で数える。1〜4個目は通常公開、5個目は
    //     表示代表、6個目以降を非公開候補にする。代表は追加の演出を受け持たない。
    //   - 非公開候補はリモート接続の observer へ加えない。初回公開・途中参加・AOI の再構築・
    //     明示的な Show のすべてで同じ判定（AllowObserver）を通す。ローカルホスト接続は常に通常。
    //   - 失敗時・判断できないとき（鍵が作れない、容量超過、専用サーバ、例外）はその個体を
    //     通常公開し、型の登録を止めて警告を1回出す。MOD全体は止めない。
    //   - 既存の NativeProcCoalesce（エッセンス4型。サーバの個体も省く方式）とは独立。
    internal static class NativeGuestPublishLimit
    {
        // 1〜4個目は通常公開。5個目が表示代表で、ここまでの PublishedPerKey 個が公開される。
        internal const int NormalSlots = 4;
        internal const int PublishedPerKey = NormalSlots + 1;
        // フレーム集計の固定容量（2の累乗）。フレームごとに作り直さず、枠の刻印で使い回す。
        private const int FrameCapacity = 512;

        internal sealed class Registration
        {
            internal Type Type;
            internal int SettingsVersion;
        }

        internal enum Role { Individual, Representative, Hidden, Overflow }

        // 表示用の鍵（仕様書 8.4）。型、creator/source（parentActor）、caster、対象指定の mode と
        // target（target の有無と point・angle）、開始位置、向き、設定の版からなる。
        // 位置や向きは丸めずに値そのもので厳密に区別する。1cm 丸めは同等性の根拠にしない。
        private readonly struct Key : IEquatable<Key>
        {
            private readonly Type _type;
            private readonly int _parent, _caster, _target;
            private readonly float _pointX, _pointY, _pointZ, _angle;
            private readonly float _startX, _startY, _startZ;
            private readonly float _rotX, _rotY, _rotZ, _rotW;
            private readonly int _settingsVersion;

            internal Key(Type type, int parent, int caster, int target,
                float pointX, float pointY, float pointZ, float angle,
                float startX, float startY, float startZ,
                float rotX, float rotY, float rotZ, float rotW, int settingsVersion)
            {
                _type = type; _parent = parent; _caster = caster; _target = target;
                _pointX = pointX; _pointY = pointY; _pointZ = pointZ; _angle = angle;
                _startX = startX; _startY = startY; _startZ = startZ;
                _rotX = rotX; _rotY = rotY; _rotZ = rotZ; _rotW = rotW;
                _settingsVersion = settingsVersion;
            }

            public bool Equals(Key other)
                => _type == other._type && _parent == other._parent && _caster == other._caster
                    && _target == other._target && _settingsVersion == other._settingsVersion
                    && _pointX == other._pointX && _pointY == other._pointY && _pointZ == other._pointZ
                    && _angle == other._angle
                    && _startX == other._startX && _startY == other._startY && _startZ == other._startZ
                    && _rotX == other._rotX && _rotY == other._rotY && _rotZ == other._rotZ && _rotW == other._rotW;

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = RuntimeHelpers.GetHashCode(_type);
                    hash = hash * 397 ^ _parent; hash = hash * 397 ^ _caster; hash = hash * 397 ^ _target;
                    hash = hash * 397 ^ _pointX.GetHashCode(); hash = hash * 397 ^ _pointY.GetHashCode();
                    hash = hash * 397 ^ _pointZ.GetHashCode(); hash = hash * 397 ^ _angle.GetHashCode();
                    hash = hash * 397 ^ _startX.GetHashCode(); hash = hash * 397 ^ _startY.GetHashCode();
                    hash = hash * 397 ^ _startZ.GetHashCode();
                    hash = hash * 397 ^ _rotX.GetHashCode(); hash = hash * 397 ^ _rotY.GetHashCode();
                    hash = hash * 397 ^ _rotZ.GetHashCode(); hash = hash * 397 ^ _rotW.GetHashCode();
                    return hash * 397 ^ _settingsVersion;
                }
            }
        }

        private struct Slot
        {
            internal Key Key;
            internal int Stamp;   // frameCount + 1。0は未使用。前のフレームの枠は刻印違いだけで再利用される。
            internal byte Count;  // このフレーム・この鍵の生成数。上限の 255 に張り付いたまま非公開候補を保つ。
        }

        private static readonly Slot[] Slots = new Slot[FrameCapacity];
        private static readonly Dictionary<Type, Registration> Registrations = new Dictionary<Type, Registration>(8);
        // 非公開候補の区分。鍵は NetworkIdentity（弱参照）。破棄・プール返却で必ず外す。
        private static readonly object TheMark = new object();
        private static readonly ConditionalWeakTable<NetworkIdentity, object> Hidden = new ConditionalWeakTable<NetworkIdentity, object>();
        // AOI 再構築の接続集合から除くときの再利用バッファ。HashSet の列挙中の削除に備えて二段で扱う。
        private static readonly List<NetworkConnectionToClient> RebuildRemoval = new List<NetworkConnectionToClient>(8);
        private static int _hiddenCount;
        private static bool _gatesReady = true;    // 公開の判定経路のどれかが取り付かなければ false にして登録を拒む
        private static bool _warnedAll;
        private static bool _warnedGate;

        internal static int RegisteredTypeCount => Registrations.Count;
        internal static int HiddenCount => _hiddenCount;

        // NetworkServer.SpawnObject の Prefix から呼ぶ。Dew.CreateAbilityInstance は parentActor と
        // info を設定してから Spawn するため、この時点で鍵に必要な値が揃っている。区分は
        // 初回の observer 追加（同じ SpawnObject の中の RebuildObservers）より前に確定する。
        internal static void Classify(GameObject obj, NetworkConnection ownerConnection)
        {
            if (!NativePerfOptions.GuestPublishLimit || !_gatesReady || Registrations.Count == 0) return;
            Type resolved = null;
            try
            {
                if (!NetworkServer.active) return;
                // ローカル接続のない専用サーバではローカルホストの初期化を保持できないため、
                // この仕組みの前提が崩れる。全型の登録を止めて通常公開へ戻す。
                if (NetworkServer.localConnection == null)
                {
                    DisableAll("no local connection (dedicated server); individual publication retained");
                    return;
                }
                var instance = obj.GetComponent<AbilityInstance>();
                if (instance == null) return;
                resolved = instance.GetType();
                if (!Registrations.TryGetValue(resolved, out var registration)) return;
                // 権限付きの個体（ownerConnection がある）は所有者への同期が要るため対象外。
                if (ownerConnection != null) return;
                // 非公開候補を親に持つ子は、ゲストが親の netId を解決できないので通常公開する。
                var parent = instance.parentActor;
                if (parent != null && parent.netIdentity != null && IsHidden(parent.netIdentity)) return;
                if (!TryBuildKey(instance, registration.SettingsVersion, out var key))
                {
                    DisableType(resolved, "display key unavailable; individual publication retained");
                    return;
                }
                var role = CountKey(key);
                if (role == Role.Overflow)
                {
                    DisableType(resolved, "frame capacity exceeded; individual publication retained");
                    return;
                }
                if (role == Role.Hidden) MarkHidden(instance.netIdentity);
            }
            catch (Exception ex)
            {
                if (resolved != null) DisableType(resolved, "classification failed; individual publication retained: " + ex.Message);
                else DisableAll("classification failed; individual publication retained: " + ex.Message);
            }
        }

        private static bool TryBuildKey(AbilityInstance instance, int settingsVersion, out Key key)
        {
            key = default;
            var info = instance.info;
            if (info.caster == null) return false;
            Vector3 point = info.point;
            Vector3 start = instance.transform.position;
            Quaternion rotation = instance.transform.rotation;
            if (!IsFinite(point) || !IsFinite(start) || !IsFinite(rotation) || float.IsNaN(info.angle) || float.IsInfinity(info.angle))
                return false;
            var parent = instance.parentActor;
            key = new Key(instance.GetType(),
                parent == null ? 0 : parent.GetInstanceID(),
                info.caster.GetInstanceID(),
                info.target == null ? 0 : info.target.GetInstanceID(),
                point.x, point.y, point.z, info.angle,
                start.x, start.y, start.z,
                rotation.x, rotation.y, rotation.z, rotation.w,
                settingsVersion);
            return true;
        }

        private static Role CountKey(in Key key)
        {
            int stamp = Time.frameCount + 1;
            int hash = key.GetHashCode();
            for (int probe = 0; probe < FrameCapacity; probe++)
            {
                ref Slot slot = ref Slots[(hash + probe) & (FrameCapacity - 1)];
                if (slot.Stamp != stamp)
                {
                    slot.Key = key; slot.Stamp = stamp; slot.Count = 1;
                    return Role.Individual;
                }
                if (slot.Key.Equals(key))
                {
                    if (slot.Count < byte.MaxValue) slot.Count++;
                    return slot.Count <= NormalSlots ? Role.Individual
                        : slot.Count == PublishedPerKey ? Role.Representative
                        : Role.Hidden;
                }
            }
            return Role.Overflow;
        }

        private static void MarkHidden(NetworkIdentity identity)
        {
            if (identity == null) return;
            Hidden.Remove(identity);
            Hidden.Add(identity, TheMark);
            _hiddenCount++;
        }

        private static bool IsHidden(NetworkIdentity identity) => Hidden.TryGetValue(identity, out _);

        // 公開の判定。NetworkIdentity.AddObserver（初回公開のうち AOI がない経路と途中参加）、
        // DewInterestManagement.OnRebuildObservers の接続集合（AOI の初回・再構築）、
        // NetworkServer.ShowForConnection（明示的な Show）のすべてから同じこの関数を通す。
        internal static bool AllowObserver(NetworkIdentity identity, NetworkConnection connection)
        {
            if (_hiddenCount == 0 || identity == null || connection == null) return true;
            // ローカルホストの接続は常に通常どおり。OnStartClient・OnCreate・乱数・親イベントを
            // 本体と同じに保つ。
            if (connection is LocalConnectionToClient || ReferenceEquals(connection, NetworkServer.localConnection))
                return true;
            return !IsHidden(identity);
        }

        // AOI の再構築（InterestManagement.Rebuild）は NetworkIdentity.AddObserver を通らず
        // 接続集合を組み直すため、OnRebuildObservers の返しの集合からここで非公開候補への
        // 接続を除く。除かないと SpawnMessage は送られなくても observers への追加だけが残り、
        // ゲストに未知の netId への更新が届く。
        internal static void FilterRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers)
        {
            if (_hiddenCount == 0 || identity == null || newObservers == null || newObservers.Count == 0) return;
            if (!IsHidden(identity)) return;
            RebuildRemoval.Clear();
            foreach (var connection in newObservers)
                if (!AllowObserver(identity, connection)) RebuildRemoval.Add(connection);
            for (int i = 0; i < RebuildRemoval.Count; i++)
                newObservers.Remove(RebuildRemoval[i]);
        }

        // NetworkServer.DestroyObject の Postfix から呼ぶ。破棄でもプール返却（Reset）でも
        // この経路を通るので、再利用時に前の区分が残らないように必ず外す。
        internal static void Release(NetworkIdentity identity)
        {
            if (identity == null || _hiddenCount == 0) return;
            if (Hidden.Remove(identity)) _hiddenCount--;
        }

        private static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(Quaternion value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void DisableType(Type type, string reason)
        {
            if (type != null && Registrations.Remove(type))
                Log.Warn("Guest publish limit: type registration removed: " + type.Name + ": " + reason);
        }

        private static void DisableAll(string reason)
        {
            if (!_warnedAll)
            {
                _warnedAll = true;
                Log.Warn("Guest publish limit: " + reason);
            }
            Registrations.Clear();
        }

        internal static void GateUnavailable(string what)
        {
            _gatesReady = false;
            if (_warnedGate) return;
            _warnedGate = true;
            Log.Warn("Guest publish limit unavailable, individual publication retained: " + what);
        }

        // 型の登録時に確かめる判定（仕様書 8.3）。機械的に確かめられる条件を満たさない型と、
        // 登録側が確認を申し立てていない型は拒否する。この時点での呼び出し元はない（登録0件）。
        //   - rpcsDisplayOnlyVerified: その型が独自に宣言した ClientRpc・Command が表示だけだと
        //     確かめたか。未確認なら、RPC 名を挙げて拒否する。
        //   - referencesReviewed: 公開される Actor からこの型の個体への parentActor・CastInfo・
        //     SyncVar の参照がないかを型・プレハブ・購読先で確認したか。機械的には確かめられ
        //     ないため、確認の申し立てがない型は拒否する（子の parentActor は実行時の保証も別に
        //     ある: 親が非公開候補の子は通常公開へ戻す）。
        internal static bool TryRegister(Type type, int settingsVersion, bool rpcsDisplayOnlyVerified,
            bool referencesReviewed, out string reason)
        {
            reason = null;
            try
            {
                if (type == null) { reason = "type is null"; return false; }
                if (type.IsAbstract) { reason = "type is abstract"; return false; }
                if (!typeof(AbilityInstance).IsAssignableFrom(type))
                    { reason = "type is not an AbilityInstance"; return false; }
                if (typeof(Summon).IsAssignableFrom(type))
                    { reason = "summons keep individual publication"; return false; }
                var declaredRpcs = DeclaredRpcNames(type);
                if (declaredRpcs.Count > 0 && !rpcsDisplayOnlyVerified)
                {
                    declaredRpcs.Sort();
                    reason = "type declares RPCs not verified display-only: " + string.Join(", ", declaredRpcs);
                    return false;
                }
                if (!referencesReviewed)
                    { reason = "references from published actors (parentActor, CastInfo, SyncVar) not reviewed"; return false; }
                if (!_gatesReady) { reason = "publish gates unavailable"; return false; }
                if (!Registrations.TryGetValue(type, out var registration))
                    Registrations[type] = registration = new Registration { Type = type };
                registration.SettingsVersion = settingsVersion;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static List<string> DeclaredRpcNames(Type type)
        {
            var names = new List<string>();
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsDefined(typeof(ClientRpcAttribute), false)
                    || method.IsDefined(typeof(CommandAttribute), false)
                    || method.IsDefined(typeof(TargetRpcAttribute), false))
                    names.Add(method.Name);
            }
            return names;
        }
    }

    // 生成時の区分け。NetworkServer.SpawnObject はすべての Spawn 経路の本体処理で、この Prefix の
    // 時点で AbilityInstance の parentActor・info は設定済み。区分けは Spawn を止めない。
    [HarmonyPatch]
    internal static class NativeGuestPublishLimitClassifyPatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(NetworkServer), "SpawnObject",
                new[] { typeof(GameObject), typeof(NetworkConnection) }, ref _warned);
            if (_target == null)
            {
                NativeGuestPublishLimit.GateUnavailable("NetworkServer.SpawnObject unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Prefix(GameObject obj, NetworkConnection ownerConnection)
            => NativeGuestPublishLimit.Classify(obj, ownerConnection);
    }

    // observer への追加（初回公開のうち AOI がない経路と、途中参加の SpawnObserversForConnection）。
    [HarmonyPatch]
    internal static class NativeGuestPublishLimitObserverPatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(NetworkIdentity), "AddObserver",
                new[] { typeof(NetworkConnectionToClient) }, ref _warned);
            if (_target == null)
            {
                NativeGuestPublishLimit.GateUnavailable("NetworkIdentity.AddObserver unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static bool Prefix(NetworkIdentity __instance, NetworkConnectionToClient conn)
            => NativeGuestPublishLimit.AllowObserver(__instance, conn);
    }

    // AOI の再構築。DewInterestManagement は InterestManagement.Rebuild の中で接続集合を
    // 組み直すため、OnRebuildObservers の返しの集合をここで整える。
    [HarmonyPatch]
    internal static class NativeGuestPublishLimitRebuildPatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(DewInterestManagement), "OnRebuildObservers",
                new[] { typeof(NetworkIdentity), typeof(HashSet<NetworkConnectionToClient>) }, ref _warned);
            if (_target == null)
            {
                NativeGuestPublishLimit.GateUnavailable("DewInterestManagement.OnRebuildObservers unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers)
            => NativeGuestPublishLimit.FilterRebuildObservers(identity, newObservers);
    }

    // 明示的な Show。AddToObserving からの経路は上流で抑えられるため、ここは本体コードから
    // 直接呼ばれた場合の受け。SpawnMessage を出す前に同じ判定を通す。
    [HarmonyPatch]
    internal static class NativeGuestPublishLimitShowPatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(NetworkServer), "ShowForConnection",
                new[] { typeof(NetworkIdentity), typeof(NetworkConnection) }, ref _warned);
            if (_target == null)
            {
                NativeGuestPublishLimit.GateUnavailable("NetworkServer.ShowForConnection unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static bool Prefix(NetworkIdentity identity, NetworkConnection conn)
            => NativeGuestPublishLimit.AllowObserver(identity, conn);
    }

    // 区分の解除。破棄・プール返却のどちらも NetworkServer.DestroyObject を通る。
    // DestroyMode は本体で private なので、第1引数の形（NetworkIdentity と引数2つ）で探す。
    [HarmonyPatch]
    internal static class NativeGuestPublishLimitReleasePatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            try
            {
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(NetworkServer)))
                {
                    if (method.Name != "DestroyObject") continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(NetworkIdentity))
                    {
                        _target = method;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                NativePerfPatch.Warn(ref _warned, "NetworkServer.DestroyObject", ex.Message);
            }
            if (_target == null)
            {
                NativePerfPatch.Warn(ref _warned, "NetworkServer.DestroyObject", "native method unavailable");
                NativeGuestPublishLimit.GateUnavailable("NetworkServer.DestroyObject unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(NetworkIdentity identity) => NativeGuestPublishLimit.Release(identity);
    }
}
