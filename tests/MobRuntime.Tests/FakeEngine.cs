// Deterministic adapter doubles only. These do NOT establish game API compatibility,
// Unity asset serialization, Unity destroyed-object semantics, or real RPC delivery.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UnityEngine
{
    public class Object { public string name; }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject?.transform;
        public T GetComponent<T>() where T : class => gameObject?.GetComponent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class => gameObject?.GetComponentInChildren<T>(includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();
        private readonly List<GameObject> _children = new List<GameObject>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public GameObject(string objectName = "object")
        {
            name = objectName; transform = new Transform { gameObject = this }; _components.Add(transform);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var result = new T { gameObject = this }; _components.Add(result); return result;
        }
        public void AddChild(GameObject child) { _children.Add(child); child.transform.parent = transform; }
        public T GetComponent<T>() where T : class => _components.OfType<T>().FirstOrDefault();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class =>
            _components.OfType<T>().Concat(_children.SelectMany(child => child.GetComponentsInChildren<T>(includeInactive))).ToArray();
    }
    public class Transform : Component
    {
        public Transform parent;
        public bool IsChildOf(Transform other) => ReferenceEquals(this, other) || parent != null && parent.IsChildOf(other);
    }
    public struct Vector3 { public float x, y, z; public float sqrMagnitude => x * x + y * y + z * z; }
    public struct Bounds { public Vector3 center, size; }
    public class Collider : Component { }
    public class Collider2D : Component { }
    public class Rigidbody : Component { }
    public class Rigidbody2D : Component { }
    public class Renderer : Component { public Material[] sharedMaterials = Array.Empty<Material>(); }
    public class MeshRenderer : Renderer { }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh; public Transform[] bones = Array.Empty<Transform>(); public Bounds localBounds; public Transform rootBone;
    }
    public class Mesh : Object { public int vertexCount = 3; }
    public class Material : Object { public Shader shader; }
    public class Shader : Object { public bool isSupported = true; }
    public class Avatar : Object { public bool isValid = true; public bool isHuman; }
    public class RuntimeAnimatorController : Object { }
    public class Animator : Behaviour { public bool applyRootMotion; public Avatar avatar; public RuntimeAnimatorController runtimeAnimatorController; }
    public class AnimationClip : Object { public AnimationEvent[] events = Array.Empty<AnimationEvent>(); public bool legacy; public float length = 1; }
    public class AnimationEvent { }
    public static class Application
    {
        // Fixture values, not values inferred for Shape of Dreams.
        public static string unityVersion = "2022.3.62f1";
        public static string version = "fixture-game-1";
        public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer;
    }
    public enum RuntimePlatform { WindowsPlayer, LinuxPlayer, OSXPlayer, WindowsEditor }
    public static class Time { public static float unscaledTime; }
    public class AssetBundle : Object
    {
        public static readonly Dictionary<string, Func<AssetBundle>> Factories = new Dictionary<string, Func<AssetBundle>>(StringComparer.Ordinal);
        public static readonly List<AssetBundle> Loaded = new List<AssetBundle>();
        public static int LoadAttempts;
        public readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>(StringComparer.Ordinal);
        public bool? UnloadedWithDestroy;
        public static AssetBundle LoadFromFile(string path)
        {
            LoadAttempts++;
            if (!Factories.TryGetValue(path, out var factory)) return null;
            var bundle = factory(); Loaded.Add(bundle); return bundle;
        }
        public T LoadAsset<T>(string path) where T : Object => Assets.TryGetValue(path, out var asset) ? asset as T : null;
        public T[] LoadAllAssets<T>() where T : Object => Assets.Values.OfType<T>().ToArray();
        public void Unload(bool all) { UnloadedWithDestroy = all; }
    }
}

namespace Mirror
{
    public class NetworkIdentity : UnityEngine.Component { }
    public static class NetworkServer { public static bool active; }
    public static class NetworkClient { public static bool active; }
}

public class Actor : UnityEngine.MonoBehaviour
{
    public readonly Dictionary<Type, Delegate> ClientHandlers = new Dictionary<Type, Delegate>();
    public readonly Dictionary<Type, Delegate> ServerHandlers = new Dictionary<Type, Delegate>();
    public Action<object> SendServer;
    public Action<DewPlayer, object> SendClient;
    public Action<object> SendAll;
    public uint netId;
    public void CustomRpc_RegisterClientMessageHandler<T>(Action<T> callback) => ClientHandlers.Add(typeof(T), callback);
    public void CustomRpc_RegisterServerMessageHandler<T>(string name, Action<T, DewPlayer> callback) => ServerHandlers.Add(typeof(T), callback);
    public void CustomRpc_UnregisterClientMessageHandler<T>(Action<T> callback) => ClientHandlers.Remove(typeof(T));
    public void CustomRpc_UnregisterServerMessageHandler<T>(Action<T, DewPlayer> callback) => ServerHandlers.Remove(typeof(T));
    public void CustomRpc_SendMessageToServer<T>(T message) => SendServer?.Invoke(message);
    public void CustomRpc_SendMessageToClient<T>(DewPlayer player, T message) => SendClient?.Invoke(player, message);
    public void CustomRpc_SendMessageToAllClients<T>(T message) => SendAll?.Invoke(message);
    public void ReceiveClient(object message) { if (ClientHandlers.TryGetValue(message.GetType(), out var callback)) callback.DynamicInvoke(message); }
    public void ReceiveServer(object message, DewPlayer player) { if (ServerHandlers.TryGetValue(message.GetType(), out var callback)) callback.DynamicInvoke(message, player); }
}
public class Entity : Actor
{
    public bool isActive = true;
    public bool isAlive = true;
    public DewPlayer owner;
    public EntityVisual Visual;
    public Action<EventInfoKill> EntityEvent_OnDeath;
}
public class Monster : Entity { }
public sealed class Mon_Fixture_Crab : Monster { }
public sealed class DewPlayer
{
    public static DewPlayer local;
    public static List<DewPlayer> gamePlayers = new List<DewPlayer>();
    public bool isHumanPlayer = true;
}
public sealed class ActorManager
{
    public Actor serverActor = new Actor();
    public readonly List<Entity> allEntities = new List<Entity>();
    public Action<Entity> ClientEvent_OnEntityAdd, ClientEvent_OnEntityRemove;
    public void Add(Entity entity) { allEntities.Add(entity); ClientEvent_OnEntityAdd?.Invoke(entity); }
    public void Remove(Entity entity) { allEntities.Remove(entity); ClientEvent_OnEntityRemove?.Invoke(entity); }
}
public sealed class ZoneManager
{
    public Action<EventInfoLoadZone> ClientEvent_OnZoneLoaded;
    public Action<EventInfoLoadRoom> ClientEvent_OnRoomLoaded;
}
public static class NetworkedManagerBase<T> where T : class { public static T softInstance; }
public sealed class EventInfoKill { public Entity victim; }
public sealed class EventInfoLoadZone { }
public sealed class EventInfoLoadRoom { }
public struct AnimationClipWithSpeed { public UnityEngine.AnimationClip clip; public float speed; }
public struct EntityModelCustomMapping { public string id; public UnityEngine.GameObject target; }
public class EntityAnimation { public enum LocomotionType { Simple, FourDirections, EightDirections } }
public class EntityModel : UnityEngine.MonoBehaviour
{
    public bool isInitialized;
    public UnityEngine.Renderer[] bodyRenderers;
    public UnityEngine.Transform healthBarPosition;
    public List<EntityModelCustomMapping> customMappings = new List<EntityModelCustomMapping>();
    public AnimationClipWithSpeed idle, stagger, death;
    public EntityAnimation.LocomotionType locomotion;
    public UnityEngine.AnimationClip runForwardClip, runBackwardClip, runLeftClip, runRightClip,
        runForwardLeftClip, runForwardRightClip, runBackwardLeftClip, runBackwardRightClip;
}
public sealed class EntityVisual : UnityEngine.MonoBehaviour
{
    public EntityModel model;
    public EntityModel NativeModel;
    public int CustomLoads, NativeLoads;
    public bool ThrowCustomAfterMutation, ThrowNative, LeavePreviousModel, LeaveUninitializedModel;
    public void LoadModelLocal(EntityModel prefab)
    {
        if (prefab.isInitialized) throw new InvalidOperationException("fresh prefab required");
        CustomLoads++;
        if (LeavePreviousModel) return;
        model = new EntityModel { isInitialized = !LeaveUninitializedModel, name = "owned-model-" + CustomLoads };
        if (ThrowCustomAfterMutation) throw new InvalidOperationException("injected partial load failure");
    }
    public void LoadModelDefaultLocal()
    {
        NativeLoads++;
        if (ThrowNative) throw new InvalidOperationException("injected native restore failure");
        model = NativeModel;
    }
}
namespace SodRpg.Mod
{
    internal static class Log
    {
        internal static readonly List<string> Warnings = new List<string>();
        internal static void Warn(string text) => Warnings.Add(text);
    }
}
