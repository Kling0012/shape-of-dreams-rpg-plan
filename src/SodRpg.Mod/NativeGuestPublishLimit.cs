using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

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
            internal GameObject Prefab;
        }

        private sealed class PrefabAudit
        {
            internal bool Passed;
            internal string Reason;
            internal GameObject Prefab;
            internal ObjectPoolItem Pool;
        }

        private sealed class ReferenceReaders
        {
            internal Func<Actor, UnityEngine.Object>[] Read;
            internal string Failure;
        }

        private delegate void AddObserverDelegate(NetworkIdentity identity, NetworkConnectionToClient connection);
        private static AddObserverDelegate _addObserver;

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
        private static readonly Dictionary<Type, Registration> PendingRegistrations = new Dictionary<Type, Registration>(8);
        private static readonly Dictionary<Type, PrefabAudit> PrefabAudits = new Dictionary<Type, PrefabAudit>(8);
        private static readonly Dictionary<Type, ReferenceReaders> References = new Dictionary<Type, ReferenceReaders>(32);
        private static readonly List<Actor> PromotionPath = new List<Actor>(32);
        private static readonly List<NetworkIdentity> PromotionOrder = new List<NetworkIdentity>(32);
        private static readonly List<Actor> PromotionVisited = new List<Actor>(32);
        // 繰り上げ済みの親は、途中参加で子の方が先に列挙されても先に送れるよう区別する。
        private static readonly ConditionalWeakTable<NetworkIdentity, object> Promoted = new ConditionalWeakTable<NetworkIdentity, object>();
        private static int _promotedCount;
        private static readonly List<NetworkIdentity> Recovery = new List<NetworkIdentity>(32);
        private static bool _promoting;
        private static bool _warnedPromotion;
        private static bool _warnedReferences;
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
            if (!NativePerfOptions.GuestPublishLimit || !_gatesReady
                || (Registrations.Count == 0 && PendingRegistrations.Count == 0)) return;
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
                if (!Registrations.TryGetValue(resolved, out var registration)
                    && !PendingRegistrations.TryGetValue(resolved, out registration)) return;
                if (Promoted.TryGetValue(instance.netIdentity, out _)) return;
                // 許可は最初の生成でプレハブを監査してから確定する。失敗結果も型ごとに保持する。
                if (!EnsurePrefabAudit(instance, registration)) return;
                PendingRegistrations.Remove(resolved);
                Registrations[resolved] = registration;
                // 権限付きの個体（ownerConnection がある）は所有者への同期が要るため対象外。
                if (ownerConnection != null) return;
                // 非公開候補を参照する子は通常公開し、送信直前に親を先に公開する。
                if (HasHiddenReference(instance)) return;
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

        private static bool EnsurePrefabAudit(AbilityInstance instance, Registration registration)
        {
            var type = registration.Type;
            if (PrefabAudits.TryGetValue(type, out var cached))
            {
                if (!cached.Passed) return false;
                var manager = ManagerBase<SpawnManager>.instance;
                if (cached.Prefab == null || manager == null
                    || !manager.activeInstanceToPool.TryGetValue(instance.gameObject, out var pool)
                    || !ReferenceEquals(pool, cached.Pool)
                    || !manager.pool.TryGetValue(cached.Prefab, out var currentPool)
                    || !ReferenceEquals(pool, currentPool))
                {
                    RejectPrefab(type, cached, "generation prefab changed or unavailable");
                    return false;
                }
                return true;
            }
            var audit = new PrefabAudit();
            PrefabAudits[type] = audit;
            try
            {
                // Dew.CreateAbilityInstance<T> は GetByType で元を取得する。親の variant processor は
                // 再実行しない。実際の variant は SpawnManager の生成済み個体と pool の対応から引く。
                var declared = registration.Prefab;
                if (declared == null)
                {
                    var resource = DewResources.GetByType(type);
                    declared = resource is Component component ? component.gameObject : resource as GameObject;
                }
                if (declared == null || declared.GetComponent(type) == null)
                    throw new InvalidOperationException("type prefab unavailable");
                var manager = ManagerBase<SpawnManager>.instance;
                if (manager == null || !manager.activeInstanceToPool.TryGetValue(instance.gameObject, out var sourcePool))
                    throw new InvalidOperationException("generation prefab cannot be resolved (unpooled instance)");
                GameObject actual = null;
                foreach (var entry in manager.pool)
                    if (ReferenceEquals(entry.Value, sourcePool))
                    {
                        if (actual != null) throw new InvalidOperationException("generation prefab is ambiguous");
                        actual = entry.Key;
                    }
                if (actual == null || (registration.Prefab != null && actual != registration.Prefab))
                    throw new InvalidOperationException("generation prefab unavailable or differs from supplied prefab");
                if (!AuditComponents(declared, type, out var reason)
                    || (actual != declared && !AuditComponents(actual, type, out reason))
                    || !AuditComponents(instance.gameObject, type, out reason))
                {
                    RejectPrefab(type, audit, reason);
                    return false;
                }
                audit.Prefab = actual;
                audit.Pool = sourcePool;
                audit.Passed = true;
                return true;
            }
            catch (Exception ex)
            {
                RejectPrefab(type, audit, "prefab audit failed: " + ex.Message);
                return false;
            }
        }

        private static void RejectPrefab(Type type, PrefabAudit audit, string reason)
        {
            audit.Passed = false;
            audit.Reason = reason;
            Registrations.Remove(type);
            PendingRegistrations.Remove(type);
            Log.Warn("Guest publish limit: prefab rejected: " + type.Name + ": " + reason);
        }

        private static bool AuditComponents(GameObject prefab, Type actorType, out string reason)
        {
            reason = null;
            int behaviours = 0, actors = 0, identities = 0;
            // 非アクティブな子も検査する。配列・反射用の確保は初回監査の中だけ。
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null) { reason = "missing component script"; return false; }
                var type = component.GetType();
                if (component is NetworkBehaviour && ++behaviours > 1)
                    { reason = "multiple NetworkBehaviours: " + type.FullName; return false; }
                if (type == actorType && component.gameObject == prefab) actors++;
                else if (type == typeof(NetworkIdentity) && component.gameObject == prefab) identities++;
                else if (!IsAllowedComponent(component))
                    { reason = "component not allowed: " + type.FullName; return false; }
                if (HasPersistentListener(component, new HashSet<object>(), 0))
                    { reason = "persistent UnityEvent listener: " + type.FullName; return false; }
            }
            if (actors != 1 || identities != 1 || behaviours != 1)
                { reason = "prefab requires one root actor and one root NetworkIdentity"; return false; }
            return true;
        }

        private static bool IsAllowedComponent(Component component)
            => component is Transform || component is Renderer || component is MeshFilter
                || component is ParticleSystem || component is Light || component is AudioSource
                || component is Collider || component is Rigidbody
                || component.GetType() == Type.GetType("UnityEngine.Animator, UnityEngine.AnimationModule", false);

        private static bool HasPersistentListener(object value, HashSet<object> visited, int depth)
        {
            if (value == null) return false;
            if (value is UnityEventBase unityEvent) return unityEvent.GetPersistentEventCount() > 0;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is Delegate) return false;
            if (!(value is Component) && !(value is IList) && !type.IsSerializable) return false;
            if (!type.IsValueType && !visited.Add(value)) return false;
            if (depth > 32) throw new InvalidOperationException("serialized event nesting exceeds audit limit");
            if (value is IList list)
            {
                foreach (var item in list)
                    if (HasPersistentListener(item, visited, depth + 1)) return true;
                return false;
            }
            for (var current = type; current != null && current != typeof(UnityEngine.Object); current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsNotSerialized) continue;
                    bool isEvent = typeof(UnityEventBase).IsAssignableFrom(field.FieldType);
                    if (!isEvent && !field.IsPublic && !field.IsDefined(typeof(SerializeField), false)
                        && !field.IsDefined(typeof(SerializeReference), false)) continue;
                    // 別アセットへの参照はその中まで辿らず、Component 自体と直列化された値だけを調べる。
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                    if (HasPersistentListener(field.GetValue(value), visited, depth + 1)) return true;
                }
            return false;
        }

        private static ReferenceReaders GetReferences(Type type)
        {
            if (References.TryGetValue(type, out var cached)) return cached;
            var readers = new List<Func<Actor, UnityEngine.Object>>();
            cached = new ReferenceReaders();
            References[type] = cached;
            try
            {
                var actor = Expression.Parameter(typeof(Actor), "actor");
                for (var current = type; current != null && current != typeof(NetworkBehaviour); current = current.BaseType)
                    foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (field.IsDefined(typeof(SyncVarAttribute), false))
                            AddReferenceReader(Expression.Field(Expression.Convert(actor, current), field),
                                field.FieldType, actor, readers, 0);
                cached.Read = readers.ToArray();
            }
            catch (Exception ex)
            {
                cached.Failure = "synced actor references unavailable: " + type.FullName + ": " + ex.Message;
            }
            return cached;
        }

        private static void AddReferenceReader(Expression value, Type type, ParameterExpression actor,
            List<Func<Actor, UnityEngine.Object>> readers, int depth)
        {
            if (typeof(NetworkBehaviour).IsAssignableFrom(type) || typeof(NetworkIdentity).IsAssignableFrom(type)
                || typeof(GameObject).IsAssignableFrom(type))
            {
                readers.Add(Expression.Lambda<Func<Actor, UnityEngine.Object>>(
                    Expression.Convert(value, typeof(UnityEngine.Object)), actor).Compile());
                return;
            }
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return;
            if (!type.IsValueType || depth > 16)
                throw new InvalidOperationException("unsupported SyncVar reference shape: " + type.FullName);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                AddReferenceReader(Expression.Field(value, field), field.FieldType, actor, readers, depth + 1);
        }

        private static Actor ReadActor(Func<Actor, UnityEngine.Object> reader, Actor source)
        {
            var value = reader(source);
            if (value == null) return null;
            if (value is Actor actor) return actor;
            if (value is Component component) return component.GetComponent<Actor>();
            return value is GameObject obj ? obj.GetComponent<Actor>() : null;
        }

        private static bool HasHiddenReference(Actor actor)
        {
            if (_hiddenCount == 0) return false;
            var references = GetReferences(actor.GetType());
            if (references.Failure != null) throw new InvalidOperationException(references.Failure);
            for (int i = 0; i < references.Read.Length; i++)
            {
                var parent = ReadActor(references.Read[i], actor);
                if (parent != null && parent.netIdentity != null && IsHidden(parent.netIdentity)) return true;
            }
            return false;
        }

        internal static bool PreparePublication(NetworkIdentity identity, NetworkConnection connection)
        {
            if (!AllowObserver(identity, connection)) return false;
            if ((_hiddenCount == 0 && _promotedCount == 0) || _promoting || identity == null || connection == null
                || !connection.isReady || IsLocal(connection)) return true;
            try
            {
                var actor = identity.GetComponent<Actor>();
                if (actor == null) return true;
                PromotionPath.Clear();
                PromotionOrder.Clear();
                PromotionVisited.Clear();
                CollectParents(actor);
                // 無関係な非公開候補があるだけでは、通常個体の observer を増やさない。
                for (int i = 0; i < PromotionOrder.Count; i++)
                    if (IsHidden(PromotionOrder[i]) || Promoted.TryGetValue(PromotionOrder[i], out _))
                    {
                        PublishPromoted();
                        break;
                    }
            }
            catch (Exception ex)
            {
                // 子の Spawn は止めない。全候補を通常公開へ戻して、この機能だけを停止する。
                DisableAll("parent publication failed; individual publication retained");
                if (!_warnedPromotion)
                {
                    _warnedPromotion = true;
                    Log.Warn("Guest publish limit: parent publication failed: " + ex.Message);
                }
                try { RecoverHidden(); }
                catch (Exception recoveryEx)
                {
                    if (!_warnedReferences)
                    {
                        _warnedReferences = true;
                        Log.Warn("Guest publish limit: normal publication recovery failed: " + recoveryEx.Message);
                    }
                }
            }
            finally
            {
                PromotionPath.Clear();
                PromotionOrder.Clear();
                PromotionVisited.Clear();
            }
            return true;
        }

        private static void CollectParents(Actor actor, bool referenced = false)
        {
            if (PromotionPath.Contains(actor))
            {
                // caster と owner など通常個体の相互参照は再訪しない。非公開個体同士の循環は拒む。
                if (IsHidden(actor.netIdentity)) throw new InvalidOperationException("cyclic hidden actor references");
                return;
            }
            if (PromotionPath.Count >= 128) throw new InvalidOperationException("actor reference depth exceeded");
            var identity = actor.netIdentity;
            if (identity == null) return;
            if (PromotionVisited.Contains(actor)) return;
            if (PromotionVisited.Count >= 512) throw new InvalidOperationException("actor reference capacity exceeded");
            PromotionPath.Add(actor);
            var references = GetReferences(actor.GetType());
            if (references.Failure != null) throw new InvalidOperationException(references.Failure);
            for (int i = 0; i < references.Read.Length; i++)
            {
                var parent = ReadActor(references.Read[i], actor);
                if (parent != null && parent != actor) CollectParents(parent, true);
            }
            PromotionPath.RemoveAt(PromotionPath.Count - 1);
            PromotionVisited.Add(actor);
            if (referenced)
            {
                if (identity.netId == 0 || identity.serverOnly)
                    throw new InvalidOperationException("referenced actor cannot be published");
                PromotionOrder.Add(identity);
            }
        }

        private static void PublishPromoted()
        {
            _promoting = true;
            try
            {
                for (int i = 0; i < PromotionOrder.Count; i++) Promote(PromotionOrder[i]);
                for (int i = 0; i < PromotionOrder.Count; i++)
                {
                    var parent = PromotionOrder[i];
                    // AddObserver -> AddToObserving -> ShowForConnection -> SendSpawnMessage。
                    // 観測集合への追加と、祖先から順に送ることの両方を本体経路で保つ。
                    foreach (var connection in NetworkServer.connections.Values)
                        if (connection != null && connection.isReady && !IsLocal(connection))
                            _addObserver(parent, connection);
                }
            }
            finally { _promoting = false; }
        }

        private static void Promote(NetworkIdentity identity)
        {
            if (Hidden.Remove(identity)) _hiddenCount--;
            if (!Promoted.TryGetValue(identity, out _))
            {
                Promoted.Add(identity, TheMark);
                _promotedCount++;
            }
        }

        private static void RecoverHidden()
        {
            Recovery.Clear();
            foreach (var identity in NetworkServer.spawned.Values)
                if (identity != null && (IsHidden(identity) || Promoted.TryGetValue(identity, out _))) Recovery.Add(identity);
            for (int i = 0; i < Recovery.Count; i++) Promote(Recovery[i]);
            _promoting = true;
            try
            {
                for (int i = 0; i < Recovery.Count; i++)
                    foreach (var connection in NetworkServer.connections.Values)
                    {
                        if (connection == null || !connection.isReady || IsLocal(connection)) continue;
                        try { _addObserver(Recovery[i], connection); }
                        catch (Exception ex)
                        {
                            if (_warnedReferences) continue;
                            _warnedReferences = true;
                            Log.Warn("Guest publish limit: normal publication recovery failed: " + ex.Message);
                        }
                    }
            }
            finally { _promoting = false; Recovery.Clear(); }
        }

        private static bool IsLocal(NetworkConnection connection)
            => connection is LocalConnectionToClient || ReferenceEquals(connection, NetworkServer.localConnection);

        internal static bool BindAddObserver(MethodInfo method)
        {
            try
            {
                _addObserver = (AddObserverDelegate)Delegate.CreateDelegate(typeof(AddObserverDelegate), method);
                return true;
            }
            catch (Exception ex)
            {
                GateUnavailable("NetworkIdentity.AddObserver delegate unavailable: " + ex.Message);
                return false;
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
            if (identity == null || IsHidden(identity) || Promoted.TryGetValue(identity, out _)) return;
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
            if (identity == null) return;
            if (_hiddenCount > 0 && Hidden.Remove(identity)) _hiddenCount--;
            if (_promotedCount > 0 && Promoted.Remove(identity)) _promotedCount--;
        }

        private static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(Quaternion value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void DisableType(Type type, string reason)
        {
            if (type == null) return;
            bool removed = Registrations.Remove(type);
            removed |= PendingRegistrations.Remove(type);
            if (removed) Log.Warn("Guest publish limit: type registration removed: " + type.Name + ": " + reason);
        }

        private static void DisableAll(string reason)
        {
            if (!_warnedAll)
            {
                _warnedAll = true;
                Log.Warn("Guest publish limit: " + reason);
            }
            Registrations.Clear();
            PendingRegistrations.Clear();
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
        //   - referencesReviewed: 型と購読先で、Spawn 時の SyncVar 以外の参照・RPC・ゲスト通知の
        //     安全性を確認したか。プレハブ構成は初回監査、生成時の参照は繰り上げ公開で補う。
        // 登録要求は初回生成の監査まで保留する。true は要求の受理で、RegisteredTypeCount は
        // 監査に合格した型だけを数える。prefab を省くと DewResources と SpawnManager から取得する。
        internal static bool TryRegister(Type type, int settingsVersion, bool rpcsDisplayOnlyVerified,
            bool referencesReviewed, out string reason, GameObject prefab = null)
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
                    { reason = "references outside spawn SyncVars and guest events not reviewed"; return false; }
                if (!_gatesReady) { reason = "publish gates unavailable"; return false; }
                if (PrefabAudits.TryGetValue(type, out var audit) && !audit.Passed)
                    { reason = audit.Reason ?? "prefab audit pending or failed"; return false; }
                if (audit != null && prefab != null && prefab != audit.Prefab)
                    { reason = "prefab differs from cached audit"; return false; }
                var destination = audit != null && audit.Passed ? Registrations : PendingRegistrations;
                if (!destination.TryGetValue(type, out var registration))
                    destination[type] = registration = new Registration { Type = type };
                registration.SettingsVersion = settingsVersion;
                registration.Prefab = prefab;
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
            return NativeGuestPublishLimit.BindAddObserver((MethodInfo)_target);
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

    // Show 以外の直接送信（所有者変更など）も含め、payload が作られる前に参照先を公開する。
    [HarmonyPatch]
    internal static class NativeGuestPublishLimitSpawnMessagePatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(NetworkServer), "SendSpawnMessage",
                new[] { typeof(NetworkIdentity), typeof(NetworkConnection) }, ref _warned);
            if (_target == null)
            {
                NativeGuestPublishLimit.GateUnavailable("NetworkServer.SendSpawnMessage unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static bool Prefix(NetworkIdentity identity, NetworkConnection conn)
            => NativeGuestPublishLimit.PreparePublication(identity, conn);
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
