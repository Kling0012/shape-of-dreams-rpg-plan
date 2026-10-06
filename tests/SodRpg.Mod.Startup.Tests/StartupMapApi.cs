using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mirror
{
    public sealed class SyncList<T> : List<T>
    {
        public enum Operation : byte { OP_ADD, OP_CLEAR, OP_INSERT, OP_REMOVEAT, OP_SET }
        public delegate void SyncListChanged(Operation op, int index, T oldItem, T newItem);
        public event SyncListChanged Callback;
        public new T this[int index]
        {
            get => base[index];
            set
            {
                var old = base[index];
                if (EqualityComparer<T>.Default.Equals(old, value)) return;
                base[index] = value;
                Callback?.Invoke(Operation.OP_SET, index, old, value);
            }
        }
        public new void Clear()
        {
            base.Clear();
            Callback?.Invoke(Operation.OP_CLEAR, 0, default, default);
        }
        public new void Add(T item)
        {
            base.Add(item);
            Callback?.Invoke(Operation.OP_ADD, Count - 1, default, item);
        }
        public new void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items) Add(item);
        }
        // Models Mirror OnDeserializeAll, which silently replaces its backing objects.
        public void LoadSnapshot(IEnumerable<T> items)
        {
            base.Clear();
            base.AddRange(items);
        }
    }
}
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static float Angle(Vector2 a, Vector2 b)
        {
            double length = Math.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
            if (length == 0) return 0;
            return (float)(Math.Acos(Math.Max(-1, Math.Min(1, (a.x * b.x + a.y * b.y) / length))) * 180 / Math.PI);
        }
    }
    public class Object
    {
        protected virtual Object Clone() => throw new NotSupportedException();
        public static T Instantiate<T>(T original, Transform parent) where T : Object
        {
            var clone = (T)original.Clone();
            if (clone is Component component) component.transform.SetParent(parent);
            return clone;
        }
        public static void Destroy(Object target)
        {
            if (target is GameObject go) go.transform.SetParent(null);
        }
    }
    public sealed class GameObject : Object
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public readonly RectTransform transform;
        public Component component;
        public GameObject() { transform = new RectTransform(this); }
        public void SetActive(bool value) => activeSelf = value;
    }
    public class Transform : Object
    {
        public readonly GameObject gameObject;
        public Transform parent;
        public readonly List<Transform> children = new List<Transform>();
        public Vector3 position;
        public Transform(GameObject owner) { gameObject = owner; }
        public int childCount => children.Count;
        public Transform GetChild(int index) => children[index];
        public void SetParent(Transform value)
        {
            parent?.children.Remove(this);
            parent = value;
            parent?.children.Add(this);
        }
    }
    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, anchoredPosition;
        public RectTransform(GameObject owner) : base(owner) { }
    }
    public class Component : Object
    {
        public readonly GameObject gameObject;
        public RectTransform transform => gameObject.transform;
        public bool isActiveAndEnabled => gameObject.activeInHierarchy;
        public Component() { gameObject = new GameObject(); gameObject.component = this; }
    }
    public sealed class Material : Object { }
    public static class Canvas { public static void ForceUpdateCanvases() { } }
}
namespace UnityEngine.UI
{
    public sealed class Button { public bool interactable; }
    public sealed class LayoutElement { public bool ignoreLayout; }
}
namespace SodRpg.Mod
{
    public enum HunterStatus { None, AboutToBeTaken, Level1, Level2, Level3 }
    public enum VoteType { None, NextNode }
    public static class SingletonBehaviour<T> { public static T instance; }
    public sealed class DewQuest
    {
        public sealed class ReachNodeGoal { public int nodeIndex; }
        private void ApplyOverrides(ReachNodeGoal g)
        {
            var zone = NetworkedManagerBase<ZoneManager>.instance;
            var node = zone.nodes[g.nodeIndex];
            if (node.status == WorldNodeStatus.HasVisited) node.status = WorldNodeStatus.Revealed;
            zone.nodes[g.nodeIndex] = node;
            zone.visitedNodesSaveData[g.nodeIndex] = null;
        }
    }
    public enum WorldDisplayStatus { Hidden, Shown }
    public sealed class UILineRenderer { public Material material; }
    public sealed class InGameUIManager
    {
        public static InGameUIManager instance;
        public WorldDisplayStatus isWorldDisplayed = WorldDisplayStatus.Shown;
        public readonly List<RectTransform> fullWorldMapNodeItems = new List<RectTransform>();
        public readonly List<RectTransform> miniWorldMapNodeItems = new List<RectTransform>();
        public Vector2 GetWorldNodeUIPos(int index)
            => fullWorldMapNodeItems[index] == null ? new Vector2(-1000, -1000) : (Vector2)fullWorldMapNodeItems[index].position;
        private void ClientEventOnNodesChanged() { }
    }
    public sealed class UI_InGame_WorldMap : Component
    {
        public Action<int, int> onHoveringNodeChanged;
        public UI_InGame_World_NodeItem nodePrefab = new UI_InGame_World_NodeItem();
        public UI_InGame_World_Edge edgePrefab = new UI_InGame_World_Edge();
        public Transform nodeParent = new GameObject().transform;
        public bool isMain = true;
        public RectTransform gamepadCursor = new GameObject().transform;
        public int hoveringNode { get; private set; } = -1;
        private int _snappedNodeIndex;
        private float _snapNodeTimer;
        private Vector2 _cursorCv;
        public int? ValidatedTravel;
        private void RefreshNodes()
        {
            var zone = NetworkedManagerBase<ZoneManager>.instance;
            for (int i = nodeParent.childCount - 1; i >= 0; i--) Object.Destroy(nodeParent.GetChild(i).gameObject);
            for (int i = 0; i < zone.nodes.Count; i++) Object.Instantiate(nodePrefab, nodeParent).Setup(i, this);
        }
        public void TravelToNode(int index)
        {
            var zone = NetworkedManagerBase<ZoneManager>.instance;
            if (!zone.IsNodeConnected(zone.currentNodeIndex, index)) return;
            ValidatedTravel = index;
        }
        private void MoveSelection(Vector2 direction)
        { HoverNode(Math.Min(NetworkedManagerBase<ZoneManager>.instance.nodes.Count - 1, hoveringNode + 1)); }
        private int FindClosestNodeIndex(Vector2 screenPos, float maxDist = -1) => 0;
        public void HoverNode(int index, bool force = false)
        {
            int previous = hoveringNode;
            hoveringNode = index;
            if (previous != index || force) onHoveringNodeChanged?.Invoke(previous, index);
        }
        private void OnDisable() { }
    }
    public sealed class UI_InGame_World_NodeItem : Component
    {
        private Vector2 _originalPos;
        public bool isMiniMapVariant;
        public readonly UnityEngine.UI.Button button = new UnityEngine.UI.Button();
        public readonly GameObject canTraverseObject = new GameObject();
        public int index { get; private set; }
        public WorldNodeData node { get; private set; }
        private UI_InGame_WorldMap _parent;
        protected override Object Clone() => new UI_InGame_World_NodeItem { isMiniMapVariant = isMiniMapVariant };
        public void Setup(int i, UI_InGame_WorldMap parent)
        {
            index = i; _parent = parent;
            node = NetworkedManagerBase<ZoneManager>.instance.nodes[i];
            _originalPos = node.position;
            transform.position = _originalPos;
            var cache = isMiniMapVariant ? InGameUIManager.instance.miniWorldMapNodeItems : InGameUIManager.instance.fullWorldMapNodeItems;
            while (cache.Count <= i) cache.Add(null);
            cache[i] = transform;
        }
        public void ShowTooltip(UI_TooltipManager tooltipManager)
            => tooltipManager.ShowWorldNodeTooltip(new TooltipSettings(), index);
    }
    public sealed class UI_InGame_World_Edge : Component
    {
        private UI_InGame_World_NodeItem _a, _b;
        private UI_InGame_WorldMap _parent;
        public readonly UILineRenderer lineRenderer = new UILineRenderer();
        public readonly Material matAdjacentCantMove = new Material(), matHover = new Material(), matNormal = new Material();
        protected override Object Clone() => new UI_InGame_World_Edge();
        public void Setup(UI_InGame_World_NodeItem a, UI_InGame_World_NodeItem b, UI_InGame_WorldMap parent)
        {
            _a = a; _b = b; _parent = parent;
            parent.onHoveringNodeChanged += UpdateStatus;
            UpdateStatus(-1, -1);
        }
        public void UpdateStatus(int current, int hovering) { lineRenderer.material = matNormal; }
    }
    public sealed class TooltipSettings { }
    public sealed class UI_TooltipManager
    {
        public int? ShownNode;
        public readonly GameObject worldNodeTooltip = new GameObject();
        public readonly List<object> currentObjects = new List<object>();
        public void ShowWorldNodeTooltip(TooltipSettings settings, int nodeIndex)
        {
            ShownNode = nodeIndex;
            currentObjects.Clear();
            currentObjects.Add(nodeIndex);
            worldNodeTooltip.SetActive(true);
        }
        public void Hide() { ShownNode = null; currentObjects.Clear(); worldNodeTooltip.SetActive(false); }
    }
    public class UI_Tooltip_BaseObj : Component { public object currentObject { get; set; } }
    public sealed class UI_Tooltip_WorldNode_CanTravelObject : UI_Tooltip_BaseObj
    {
        public readonly UnityEngine.UI.LayoutElement layoutElement = new UnityEngine.UI.LayoutElement();
        public void OnSetup() { gameObject.SetActive(true); }
    }
    public sealed class UI_Tooltip_WorldNode_Description : UI_Tooltip_BaseObj
    {
        public bool TooFarMessageVisible;
        public void OnSetup()
        {
            var zone = NetworkedManagerBase<ZoneManager>.instance;
            TooFarMessageVisible = zone.GetNodeDistance(zone.currentNodeIndex, (int)currentObject) > 1;
        }
    }
}
