using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Only native transport, clock and entity storage are doubled. The queues, admission,
// equipment snapshots, scope pools and dividend save scheduling come from linked Mod files.
namespace Mirror
{
    internal static class NetworkServer
    {
        public static bool active;
        // ホスト側 TickCoopTrade の抽出（#400 再現）に必要な本体 Mirror API の境界。
        public static NetworkConnectionToClient localConnection;
        public static void ReplaceHandler<T>(Action<NetworkConnectionToClient, T> handler) where T : struct, NetworkMessage { }
    }
    internal static class NetworkClient
    {
        public static bool active;
        public static NetworkConnection connection;
        // CustomRpc と同じ意味の配送記録。SendHook で配送後の例外も注入できる（所見04・07）。
        public static readonly List<object> Sent = new List<object>();
        public static Action<object> SendHook;
        public static void Send<T>(T message) where T : struct, NetworkMessage { SendHook?.Invoke(message); Sent.Add(message); }
        public static void ReplaceHandler<T>(Action<T> handler) { }
    }
    internal interface NetworkMessage { }
    internal sealed class NetworkWriter { public void WriteString(string value) { } }
    internal sealed class NetworkReader { public string ReadString() => null; }
    internal static class Writer<T> where T : struct, NetworkMessage
    {
        public static Action<NetworkWriter, T> write;
    }
    internal static class Reader<T> where T : struct, NetworkMessage
    {
        public static Func<NetworkReader, T> read;
    }
    internal class NetworkConnection
    {
        public readonly List<object> Sent = new List<object>();
        public int connectionId;
        // NativePlayerTimerIdlePerf（リンク）が送信の届く窓の判定で読む本体 Mirror API。
        // 実物は public bool isReady で既定 false（未接続）。テストから代入して
        // 途中参加の前後の送否を動かす。
        public bool isReady;
        public void Send<T>(T message) where T : struct, NetworkMessage => Sent.Add(message);
    }
    internal sealed class NetworkConnectionToClient : NetworkConnection { }
    // NativeStatsCalcRpcPerf が typeof で参照する本体 Mirror API。実物の syncVarDirtyBits は
    // protected で、MOD は Prepare で作った getter デリゲート経由でのみ読む。境界では
    // テストから代入できる自動プロパティにする（既定 false / 0 は実機の未接続状態と同じ）。
    // StatsCalcRpcPerfTests が Prefix/Capture の送る・省く判定を動かすために書き換える。
    // SendRPCInternal は送信側の形検査（ValidateRpcBody）が NetworkBehaviour のメンバーとして
    // 数えるため、このクラスに置く。本体では引数は writer と rpc 名の文字列。
    // （#866）実機の NetworkBehaviour は UnityEngine.MonoBehaviour の子孫で transform を
    // 継承する。ダッシュ掃引の境界（DashSweepStepPerfBoundary.cs）の転写が base.transform を
    // Component.get_transform として呼ぶため、ここでも Component からの継承にする。
    internal class NetworkBehaviour : UnityEngine.Component
    {
        public bool isServer { get; set; }
        public bool isClient { get; set; }
        public ulong syncVarDirtyBits { get; set; }
        public void SendRPCInternal(NetworkWriter writer, string rpcName) { }
        // （#876）実機 Mirror の weaver 生成セッターが呼ぶ NetworkBehaviour.GeneratedSyncVarSetter。
        // 等値（EqualityComparer<T>.Default。float では float.Equals と同じく NaN==NaN・0==-0 を
        // 等値とみたす。SyncVarEqual 相当）を通ったときだけ字段を書き、同期ダーティビットを
        // OR で立て、フック（第4引数）があれば呼ぶ。StatsRatioIdlePerfBoundary の Network_
        // セッターがこの形で呼ぶことで、NativeStatsRatioIdlePerf.Prepare のフック検査
        // （GeneratedSyncVarSetter 呼び出しの直前が ldnull であること）を実ソースのまま通す。
        internal void GeneratedSyncVarSetter<T>(T value, ref T field, ulong dirtyBit, Action<T> hook)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            syncVarDirtyBits |= dirtyBit;
            hook?.Invoke(value);
        }
    }
    // 同じく形検査が Mirror.NetworkWriterPool の Get・Return という名前で数えるための境界。
    // 呼び出しの有無は IL で確かめるため、中身は空でよい。
    internal static class NetworkWriterPool
    {
        public static NetworkWriter Get() => null;
        public static void Return(NetworkWriter writer) { }
    }
}
 namespace UnityEngine
 {
     internal static class Time { public static int frameCount; public static float unscaledTime; public static float time; public static double timeAsDouble; public static float deltaTime; }
    // TrackRun の Limbo 深度の読み出しに必要な本体 API。ハーネスでは Limbo なし(0)を返す。
    internal static class Object { public static T FindObjectOfType<T>() where T : class => null; }

    // 星図の差分更新（#440）のための Unity IMGUI 境界。GUILayout / GUILayoutOption は
    // ProfileSlotUiBoundary.cs の既存の定義をそのまま使う。GUIStyle.CalcHeight は行数と
    // 文字数だけから決まる決定論的な値を返し、呼び出し回数を数えて部分再計算を検証する。
    internal enum EventType { Layout, Repaint, Used, KeyDown, MouseDown, MouseUp }
    internal sealed class Event
    {
        public static Event current = new Event();
        public EventType type;
        public Vector2 mousePosition;
        // #771: 検索欄の IME 再現で使うキー入力の境界（ExtractNativePersistence は本文を改めない）。
        public KeyCode keyCode;
        public char character;
        public int button;
        public void Use() => type = EventType.Used;
    }
    internal sealed class GUIContent { public string text, tooltip; }
    internal sealed class GUIStyle
    {
        public static int CalcHeightCalls;
        public static void ResetCalcHeightCalls() => CalcHeightCalls = 0;
        // 決定論的な高さ: 行数と文字数だけで決まり、同じ本文・同じ幅なら常に同じ値。
        internal static float Measure(string text) => 16f + 6f * (text ?? "").Split('\n').Length + 0.4f * (text ?? "").Length;
        public float CalcHeight(GUIContent content, float width)
        {
            CalcHeightCalls++;
            return Measure(content.text);
        }
    }
    internal struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2();
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public float sqrMagnitude => x * x + y * y;
        // 本体 UnityEngine.Vector2 と同じ式（BossSegmentPlacement の線分の長さが参照）。
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public static float Distance(Vector2 a, Vector2 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
    internal struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
        public float xMax => x + width;
        public bool Contains(Vector2 point)
            => point.x >= x && point.x < xMax && point.y >= y && point.y < y + height;
    }
    internal struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    internal class Texture2D { }
    internal static class Mathf
    {
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        // #1010: Notify の抽出が通知の行数（int）に使う本体 API。
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Floor(float value) => (float)Math.Floor(value);
        // NativePerfPatches.cs が nameof / AccessTools.Method で参照する本体 API。
        public static float MoveTowards(float current, float target, float maxDelta)
            => Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
        // NativePositionSyncIdlePerf（位置同期のゲート置き換え）が参照する本体 API。本体と同じ式。
        public static float Abs(float value) => Math.Abs(value);
        // BossSegmentPlacement（リンク。#890 のボス予告線分の矩形・回転）が参照する本体 API。
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        public static float DeltaAngle(float current, float target)
        {
            float delta = target - current;
            delta -= Mathf.Floor(delta / 360f) * 360f;
            return delta > 180f ? delta - 360f : delta;
        }
    }
    internal static class GUI
    {
        // スクロール位置はそのまま返し、描画・入力は行わない（Button は常に押されていない）。
        public static Vector2 BeginScrollView(Rect position, Vector2 scroll, Rect content) => scroll;
        public static void EndScrollView() { }
        public static bool Button(Rect rect, GUIContent content, GUIStyle style) => false;
        // #771: 検索欄の IME 再現のための、IMGUI の有効状態と名前付きコントロールの境界。
        // 本物と同じく SetNextControlName は「次に描くコントロール」への名前付けで、
        // GetNameOfFocusedControl はキーボードフォーカス中のコントロールの名前を返す。
        public static bool enabled = true;
        private static string _nextControlName;
        private static readonly Dictionary<int, string> ControlNames = new Dictionary<int, string>();
        public static void SetNextControlName(string name) => _nextControlName = name;
        public static string GetNameOfFocusedControl()
            => ControlNames.TryGetValue(GUIUtility.keyboardControl, out string name) ? name : "";
        internal static void NameNextDrawnControl(int id)
        {
            if (_nextControlName == null) return;
            ControlNames[id] = _nextControlName;
            _nextControlName = null;
        }
        internal static void ResetNamedControls()
        {
            _nextControlName = null;
            ControlNames.Clear();
        }
    }
    internal static class GUILayoutUtility
    {
        // テストは幅を変えて全行再計測を引き起こせる。既定は本物の StarClusterSize と同じ幅。
        public static Rect ClusterListArea = new Rect(0f, 0f, 270f, 226f);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight,
            params GUILayoutOption[] options) => ClusterListArea;
    }
 }
// The extractor preserves ClientSession.cs imports; scene event wiring is outside this harness.
namespace UnityEngine.SceneManagement
{
    internal struct Scene { }
}
// FxVolume が GetComponent で掴む URP のポストプロセス Volume（2026-10-21 確認）。
// NativeMirageVolumeIdle は _volume の null 判定だけに使う。
namespace UnityEngine.Rendering
{
    internal class Volume
    {
        public float weight;
        public bool enabled;
    }
}
namespace SodRpg.Mod
{
    internal class Actor
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public int GetInstanceID() => _id;
        // 本体では Actor のプロパティ。NativeSleepPositionPerf（#751）の IL 照合が
        // typeof(Actor) の getter を探すため、フィールドではなくプロパティにする。
        public UnityEngine.Vector3 position { get; set; }
        public Actor parentActor;
        public readonly Dictionary<string, string> persistentSyncedData = new Dictionary<string, string>();
        public readonly List<(DewPlayer Target, object Message)> Sent = new List<(DewPlayer, object)>();
        public void CustomRpc_SendMessageToClient(DewPlayer target, object message) => Sent.Add((target, message));
        public void CustomRpc_SendMessageToAllClients(object message) => Sent.Add((null, message));
        public Action<object> BeforeSendToServer;
        public void CustomRpc_SendMessageToServer(object message)
        {
            BeforeSendToServer?.Invoke(message);
            Sent.Add((null, message));
        }
        // --- #857 NativeCustomRpcDispatchPerf: 本体 Dew.Core Actor.cs（r1.4.1.2 逆コンパイル）
        // の CustomRpc の登録・解除・受信の切り分けの写し。辞書の形・列挙の順・例外の飲み込み
        // （復元失敗は次のエントリへ、ハンドラの例外は1件ずつ Debug.LogException）まで
        // 本体どおりに動かし、Prefix との結果比較の基準にする。 ---
        private Dictionary<Type, List<RpcHandler>> _clientRpcHandlers = new Dictionary<Type, List<RpcHandler>>();
        private Dictionary<Type, List<RpcHandler>> _serverRpcHandlers = new Dictionary<Type, List<RpcHandler>>();
        private DewPlayer _serverRpcCaller;
        private class RpcHandler
        {
            public Action<object> wrappedAction;
            public object originalAction;
        }
        public void CustomRpc_RegisterClientMessageHandler<T>(Action<T> handler)
        {
            if (!_clientRpcHandlers.TryGetValue(typeof(T), out var value))
            {
                value = new List<RpcHandler>();
                _clientRpcHandlers[typeof(T)] = value;
            }
            value.Add(new RpcHandler
            {
                wrappedAction = obj =>
                {
                    if (obj is T typed) handler(typed);
                },
                originalAction = handler
            });
        }
        public void CustomRpc_UnregisterClientMessageHandler<T>()
        {
            _clientRpcHandlers.Remove(typeof(T));
        }
        public void CustomRpc_UnregisterClientMessageHandler<T>(Action<T> handler)
        {
            // 本体は List.FilterInPlace（一致する物だけ残す）。同じ意味の RemoveAll で写す。
            if (_clientRpcHandlers.TryGetValue(typeof(T), out var value))
                value.RemoveAll(h => !h.originalAction.Equals(handler));
        }
        public void CustomRpc_RegisterServerMessageHandler<T>(string name, Action<T, DewPlayer> handler)
        {
            if (!_serverRpcHandlers.TryGetValue(typeof(T), out var value))
            {
                value = new List<RpcHandler>();
                _serverRpcHandlers[typeof(T)] = value;
            }
            value.Add(new RpcHandler
            {
                wrappedAction = obj =>
                {
                    if (!(_serverRpcCaller == null) && obj is T arg) handler(arg, _serverRpcCaller);
                },
                originalAction = handler
            });
        }
        public void CustomRpc_UnregisterServerMessageHandler<T>(Action<T, DewPlayer> handler)
        {
            if (_serverRpcHandlers.TryGetValue(typeof(T), out var value))
                value.RemoveAll(h => !h.originalAction.Equals(handler));
        }
        // 本体 HandleRpc_Imp の写し。登録済みハンドラの辞書を最初から最後まで列挙し、
        // Type.Name が電文の型名と一致したら FromJson で復元してその型のハンドラを
        // 生の List に対して foreach する（List の変更を検知して例外で止まるのも含め本体と同じ）。
        private void HandleRpc_Imp(string type, string serialized, DewPlayer caller)
        {
            _serverRpcCaller = caller;
            foreach (KeyValuePair<Type, List<RpcHandler>> item in (caller != null) ? _serverRpcHandlers : _clientRpcHandlers)
            {
                object obj;
                try
                {
                    if (item.Key.Name != type) continue;
                    obj = DewPersistence.FromJson(serialized, item.Key);
                }
                catch
                {
                    continue;
                }
                foreach (RpcHandler item2 in item.Value)
                {
                    try { item2.wrappedAction(obj); }
                    catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
                }
            }
        }
        public Action<DamageData, Entity, ReactionChain> DamageSink;
        public void DealDamage(DamageData damage, Entity target, ReactionChain chain) => DamageSink?.Invoke(damage, target, chain);
        public void DoBasicAttackHit() { }
        public void ApplyElemental() { }
        public void InvokeOnApplyElemental() { }
        public void InvokeOnAbilityInstanceBeforePrepare() { }
        public void InvokeOnDealDamage() { }
        public void InvokeOnKill() { }
        public void ClearPooledEventsAndProcessors() { }
    }
    internal class Entity : Actor
    {
        public uint netId;
        public bool isActive = true;
        // Skoll の回復・バースト（HostAuthority.BossGlacialCore.cs の抽出）が読む本体 API。
        public float creationTime;
        // 敵担当制（HostAuthority.EnemyOwnership.cs）は本体と同じく Status 側を正とする。
        public float currentHealth { get => Status.currentHealth; set => Status.currentHealth = value; }
        public float maxHealth => Status.maxHealth;
        public UnityEngine.Vector3 agentPosition => position;
        public DewPlayer owner;
        public EntityAI AI;
        public bool anyBoss;
        public bool isDead => Status.isDead;
        public readonly EntityControl Control = new EntityControl();
        public readonly EntityVisual Visual = new EntityVisual();
        public EntityStatus Status = new EntityStatus();
        public readonly ProcessorList<DataProcessor<DamageData, Actor, Entity>> dealtDamageProcessor
            = new ProcessorList<DataProcessor<DamageData, Actor, Entity>>();
        public event Action<EventInfoDamage> EntityEvent_OnTakeDamage;
        internal void RaiseEntityEventOnTakeDamage(EventInfoDamage info) => EntityEvent_OnTakeDamage?.Invoke(info);
        // （#798）本体 KillTracker の掃除・停止が登録解除する死亡イベント。
        public event Action<EventInfoKill> EntityEvent_OnDeath;
        public EntityRelation GetRelation(Entity other) => this is Hero && other is Hero ? EntityRelation.Ally : EntityRelation.Enemy;
        public bool IsAnyBoss() => anyBoss || this is Monster monster
            && (monster.type == Monster.MonsterType.MiniBoss || monster.type == Monster.MonsterType.Boss);
        public bool CanAggroTo(Entity target) => true;
        public UnityEngine.Vector3 GetAIPosition(Entity viewer) => position;    }
    // 敵担当制が対象外にする本体のEntity派生。
    internal class PropEntity : Entity { }
    // 本体の StatBonus と同じ形。部屋の兆しの付与・解除をそのまま観測できるよう、適用中の一覧も持つ。
    internal class StatBonus
    {
        public float attackDamagePercentage, abilityPowerPercentage, movementSpeedPercentage,
            abilityHasteFlat, armorFlat, attackSpeedPercentage, maxHealthPercentage;
    }
    // （#876）マナの同期字段と weaver 生成セッターの対は StatsRatioIdlePerfBoundary.cs の
    // partial 追加分に置く（NativeStatsRatioIdlePerf の検証だけが読む）。
    internal partial class EntityStatus : NetworkBehaviour
    {
        private float _currentHealth = 100f;
        private bool _statsDirty = true;
        public float currentHealth { get => _currentHealth; set => _currentHealth = value; }
        // 本体と同じくプロパティにする: NativeStatsRatioIdlePerf の Prepare は maxHealth を
        // AccessTools.PropertyGetter で解決する（字段だと null になりパッチが下りる）。
        public float maxHealth { get; set; } = 100;
        // 敵担当制のHP割合維持を観察できるよう、再計算は処理器つきの基本値から最大HPを作り直す。
        public float baseMaxHealth = 100f;
        public bool isAlive = true, isUndetectableByNonAllies;
        public float currentShield;
        public int fireStack, lightStack, darkStack;
        public bool hasCold;
        public bool isDead, isInConversation, hasInvulnerable, hasDamageImmunity;
        public readonly FinalStats finalStats = new FinalStats();
        public readonly ProcessorList<DataProcessor<FinalStats>> finalStatsProcessors
            = new ProcessorList<DataProcessor<FinalStats>>();
        public readonly List<StatBonus> AppliedBonuses = new List<StatBonus>();
        // 本体の加算済みボーナス（bonusStats）と同じ形。ボス持ち分が人数補正分を除外するために読む。
        public readonly StatBonus bonusStats = new StatBonus();
        public void AddStatBonus(StatBonus bonus)
        {
            AppliedBonuses.Add(bonus);
            bonusStats.attackDamagePercentage += bonus.attackDamagePercentage;
            bonusStats.abilityPowerPercentage += bonus.abilityPowerPercentage;
            bonusStats.movementSpeedPercentage += bonus.movementSpeedPercentage;
            bonusStats.abilityHasteFlat += bonus.abilityHasteFlat;
            bonusStats.armorFlat += bonus.armorFlat;
            bonusStats.attackSpeedPercentage += bonus.attackSpeedPercentage;
            bonusStats.maxHealthPercentage += bonus.maxHealthPercentage;
        }
        public void RemoveStatBonus(StatBonus bonus)
        {
            AppliedBonuses.Remove(bonus);
            bonusStats.attackDamagePercentage -= bonus.attackDamagePercentage;
            bonusStats.abilityPowerPercentage -= bonus.abilityPowerPercentage;
            bonusStats.movementSpeedPercentage -= bonus.movementSpeedPercentage;
            bonusStats.abilityHasteFlat -= bonus.abilityHasteFlat;
            bonusStats.armorFlat -= bonus.armorFlat;
            bonusStats.attackSpeedPercentage -= bonus.attackSpeedPercentage;
            bonusStats.maxHealthPercentage -= bonus.maxHealthPercentage;
        }
        public void MarkStatsDirty() => _statsDirty = true;
        public void SetHealth(float value) => currentHealth = value;
        public void CalculateStatsIfDirty()
        {
            if (!_statsDirty) return;
            _statsDirty = false;
            var stats = new FinalStats { maxHealth = baseMaxHealth, armor = finalStats.armor };
            foreach (var processor in finalStatsProcessors.InOrder()) processor(ref stats);
            maxHealth = stats.maxHealth;
            finalStats.maxHealth = stats.maxHealth;
        }
        // 本体 EntityStatus.CalculateStats と、その末尾で毎回呼ばれる weaver 生成の空 ClientRpc
        // RpcInvokeStatsCalculated。NativeStatsCalcRpcPerf.SharedPrepare と
        // NativeStatsRatioIdlePerf.Prepare がこのメソッドを typeof で解決する。前者は
        // 送信側の形検査（NetworkWriterPool.Get・SendRPCInternal・NetworkWriterPool.Return の
        // 3呼び出しのみ）と通知呼び出し1つの検査に通る形で再現する。後者のために
        // （#876）本体と同じ比率維持の書き換え（入口の比率 × 新しい最大値を weaver セッターで
        // 書く）もここに入れる: 最大値が不変の再計算では float32 の割り算→掛け算の往復で
        // 1 ulp ずれた値が字段に書き戻され、dirty ビットが立つ。StatsRatioIdlePerfTests は
        // Prefix → この本体 → Postfix の順で通して、その書き戻しが入口の状態に戻ることを
        // 確かめる。StatsCalcRpcPerfTests は Capture/Prefix を直接動かすため、この本体は
        // 実行しない（通知呼び出しの数だけが意味を持つ）。
        public void CalculateStats()
        {
            float healthRatio = currentHealth / maxHealth;
            float manaRatio = currentMana / maxMana;
            maxHealth = baseMaxHealth;
            maxMana = baseMaxMana;
            Network_currentHealth = healthRatio * maxHealth;
            Network_currentMana = manaRatio * maxMana;
            RpcInvokeStatsCalculated();
        }
        internal void RpcInvokeStatsCalculated()
        {
            NetworkWriter writer = NetworkWriterPool.Get();
            SendRPCInternal(writer, "System.Void EntityStatus::RpcInvokeStatsCalculated()");
            NetworkWriterPool.Return(writer);
        }
        // ミストの行動不能保険（HostAuthority.CrowdControlRecovery.cs）が走査する本体の効果リスト。
        private readonly List<BasicEffect> _basicEffects = new List<BasicEffect>();
        public bool HasElemental(ElementalType type) => false;
        public bool TryGetStatusEffect<T>(out T effect) where T : class { effect = null; return false; }
        private void RemoveBasicEffect(BasicEffect effect) { _basicEffects.Remove(effect); effect.isAlive = false; }
    }
    internal class Monster : Entity
    {
        public enum MonsterType { Lesser, Normal, MiniBoss, Boss }
        public MonsterType type;
        public bool disableLoot;
    }
    internal sealed class BossMonster : Monster { }
    // NativeRedGiantDamageTextPerf の受け口。本体 Mon_RedGiant（Dew.Core.dll r1.4.0.13・
    // r1.4.1.2、2026-10-20 に ilspycmd で IL 確認）のうちパッチが触る範囲の模倣。
    // ActiveLogicUpdate の中身は本体末尾の転写で、C# の this 欄読み出し（ldarg.0 + ldfld）
    // が実際の IL と同じ命令並びになる。欄は Prepare が探す3つの SyncVar 相当。
    // ActiveLogicUpdateWrongShape は欄の読み出しが直接並ばない形（一度局所変数へ受ける）で、
    // Transpiler の照合外しの確認用。
    internal class Mon_RedGiant : Monster
    {
        public TMPro.TMP_Text dpsText;
        internal float _totalDamage, _dps, _maxSingleDmg;
        internal void ActiveLogicUpdate(float dt)
        {
            dpsText.text = string.Format("Total: {0:#,##0}\r\nDPS: {1:#,##0}\r\nMax Single Dmg: {2:#,##0}", _totalDamage, _dps, _maxSingleDmg);
        }
        internal void ActiveLogicUpdateWrongShape(float dt)
        {
            float total = _totalDamage;
            dpsText.text = string.Format("Total: {0:#,##0}\r\nDPS: {1:#,##0}\r\nMax Single Dmg: {2:#,##0}", total, _dps, _maxSingleDmg);
        }
    }
    // Hero_Mist の継承元。本体では非シールド。
    internal class Hero : Entity
    {
        public readonly HeroSkill Skill = new HeroSkill();
        public bool isInCombat, isKnockedOut;
        public readonly EntityAbility Ability;
        public readonly DataProcessorGroup<HealData, Actor, Entity> takenHealProcessor = new DataProcessorGroup<HealData, Actor, Entity>();
        public Hero() { Ability = new EntityAbility(this); }
        public void ApplyCooldownReductionByRatio(SkillTrigger skill, float ratio, bool ignore) => throw new NotSupportedException();
        // NativeWeaponFxPerf が nameof(Hero.FrameUpdate) で対象を解決するための本体 API。
        public void FrameUpdate() { }
    }
    internal sealed class HeroSkill
    {
        public readonly Dictionary<HeroSkillLocation, SkillTrigger> Slots = new Dictionary<HeroSkillLocation, SkillTrigger>();
        public SkillTrigger GetSkill(HeroSkillLocation location) => Slots.TryGetValue(location, out var skill) ? skill : null;
    }
    internal sealed class EntityAbility
    {
        public Entity entity;
        public EntityAbility(Hero hero) { entity = hero; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RemoveAbility(int index) => ((Hero)entity).Skill.Slots.Remove((HeroSkillLocation)index);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SetAbility(int index, AbilityTrigger ability) => ((Hero)entity).Skill.Slots[(HeroSkillLocation)index] = (SkillTrigger)ability;
    }
    internal enum HeroSkillLocation { Q, W, E, R, Identity, Movement }
    internal enum SkillType { Normal, Ultimate }
    internal enum ElementalType { Fire, Cold, Light, Dark }
    internal enum EntityRelation { Enemy, Ally }
    internal class TriggerConfig
    {
        public int maxCharges;
    }
    internal class AbilityTrigger : Actor
    {
        public Entity owner;
        public TriggerConfig[] configs;
        public float[] currentUnscaledCooldownTimes;
        public int[] currentCharges;
        public void LogicUpdate(float dt) { }
        public virtual float GetCooldownTimeMultiplier(int configIndex) => 1f;
    }
    internal class SkillTrigger : AbilityTrigger
    {
        public SkillType type;
        public float currentConfigUnscaledCooldownTime, currentConfigUnscaledMaxCooldownTime;
        public void OnCastComplete() { }
        // 本体 Actor の transform / position（position は transform.position の読み書き）。
        // Ai_EmbraceNewIdentity.UpdatePositions の転写がこの形を通る。
        public UnityEngine.Transform transform = new UnityEngine.Transform();
        public UnityEngine.Vector3 position => transform.position;
        // Shrine_Chaos.GetTargetInfo の Prefix が参照する本体 API。
        public int level;
        public bool isLevelUpEnabled = true;
    }
    internal sealed class St_D_CircleOfLife : SkillTrigger { }
    internal sealed class St_D_TheKillingFlow : SkillTrigger { }
    internal sealed class St_U_Hysteria : SkillTrigger { }
    internal sealed class TestQ : SkillTrigger { }
    internal sealed class TestW : SkillTrigger { }
    internal sealed class AttackTrigger : AbilityTrigger
    {
        public void CallAttackCompleteBeforePrepareRoutines() { }
        public void CallAttackCompleteRoutines() { }
    }
    internal class AbilityInstance : Actor { public Gem gem; public CastInfo info; }
    internal sealed class Gem : Actor
    {
        // NativePerfPatches.cs の Gem クールダウン系パッチが参照する本体 API。
        public bool isServer;
        public void LogicUpdate(float dt) { }
        public float Network_currentCooldown { get; set; }
        public void SetSyncVarDirtyBit(ulong dirtyBit) { }
        // Shrine_Chaos.GetTargetInfo(GemLocation, Gem) の Prefix が参照する本体 API。
        public Hero owner;
        public int quality;
    }
    internal sealed class ElementalStatusEffect : Actor { public bool isActive; }
    internal sealed class Summon : Entity
    {
        public CastInfo info;
        public Hero hero;
        public T FindFirstAncestorOfType<T>() where T : class => null;
    }
    internal sealed class Ai_D_ChargedAnguillian_Lightning : Actor { }
    internal sealed class Ai_D_IcyVeins_Damage : Actor { }
    internal sealed class Ai_D_BeautifulThreat_Feather : Actor { }
    internal sealed class Se_HunterBuff { public bool enableGoldAndExpDrops; }
    // NativePerfPatches.cs の NativeAccessoryOffsetPerf が typeof / nameof で参照する本体 API
    // （Dew.Core/Accessory_AttachPoint.cs）。このプロジェクトでは検索そのものは実行せず、
    // 本体の線形探索の模倣は Startup.Tests の AccessoryOffsetCacheTests が持つ。
    internal class Accessory_AttachPoint
    {
        public readonly List<AttachOffset> customOffsets = new List<AttachOffset>();
        public AttachOffset FindOffset(string entityOrSkinName) => null;

        public sealed class AttachOffset
        {
            public string entityName;
        }
    }
    // ここから下はミストの行動不能修正（NativeMistLifetime.cs / HostAuthority.CrowdControlRecovery.cs）用の
    // 本体 API。移動・中断・効果保持の記録だけを模倣し、時間は _tickNow で進める。
    internal sealed class EntityVisual { public bool isSpawning; public EntityModel model; }
    internal sealed class Hero_Mist : Hero { }
    internal sealed class Ai_Q_Fleche_Dash { public void OnCancel() { } }
    internal class EntityControl
    {
        public Entity entity;
        // 本体の Transform。NativeEntityRotationIdlePerf の Prefix が読み、検証では向きを設定して使う。
        public UnityEngine.Transform transform;
        // 本体の向きダンパーの内部速度（EntityControl._desiredAngleCv）。Prefix の FieldRef が読み、
        // 検証でも設定して使う。
        public float _desiredAngleCv;
        // 本体の攻撃目標。召喚体の反撃判定（HostAuthority.SummonOwnership.cs）が読む。
        public Entity attackTarget;
        public Displacement ongoingDisplacement;
        public int Cancels;
        public float outerRadius;
        public int MoveToDestinationCalls;
        public UnityEngine.Vector3 LastDestination;
        public void CancelOngoingDisplacement() { Cancels++; ongoingDisplacement = null; }
        private void CancelOngoingDisplacementLocal() { Cancels++; ongoingDisplacement = null; }
        public void MoveToDestination(UnityEngine.Vector3 destination, bool immediately)
        {
            MoveToDestinationCalls++;
            LastDestination = destination;
        }

        // （#813）本体 Dew.Core EntityControl.PositionSyncData（ネストした struct、
        // public フィールド。2026-10-19 逆コンパイル確認）。
        internal struct PositionSyncData
        {
            public double timestamp;
            public UnityEngine.Vector3 position;
            public UnityEngine.Vector3 velocity;
            public float desiredAngle;
        }

        // NativePositionSyncIdlePerf が AccessTools.Field で参照する本体フィールド。
        internal PositionSyncData _positionSyncData;

        // ゲートが受理した回数（本体は SyncVar へ書き込む。ここでは判定の結果だけ数える）。
        internal int GateAccepts, UserCodeAccepts;

        // 本体 HasMeaningfulSyncDataChange と同じ命令並び（逆コンパイル、2026-10-19 確認）。
        // 出力される IL の形は this/_positionSyncData を読む NativeBoundary 側の見本と
        // NativePerfPatches 側の指紋（定数・フィールド読み出し・呼び出し・比較・減算の向き）が
        // 同じ本体の形であることを前提にしている。
        private bool HasMeaningfulSyncDataChange(in PositionSyncData data)
        {
            if (!(data.timestamp - _positionSyncData.timestamp >= 1.0) && !((data.position - _positionSyncData.position).sqrMagnitude > 0.0001f) && !((data.velocity - _positionSyncData.velocity).sqrMagnitude > 0.0001f))
            {
                return Mathf.Abs(Mathf.DeltaAngle(data.desiredAngle, _positionSyncData.desiredAngle)) > 0.1f;
            }
            return true;
        }

        // 検証の否側の見本: タイムスタンプの比較だけ > に変えた書き換え（境界値が変わる）。
        private bool HasMeaningfulSyncDataChangeStrictTimestamp(in PositionSyncData data)
        {
            if (!(data.timestamp - _positionSyncData.timestamp > 1.0) && !((data.position - _positionSyncData.position).sqrMagnitude > 0.0001f) && !((data.velocity - _positionSyncData.velocity).sqrMagnitude > 0.0001f))
            {
                return Mathf.Abs(Mathf.DeltaAngle(data.desiredAngle, _positionSyncData.desiredAngle)) > 0.1f;
            }
            return true;
        }

        // 検証の否側の見本: タイムスタンプの減算の向きだけ逆にした書き換え。
        private bool HasMeaningfulSyncDataChangeSwappedSubtraction(in PositionSyncData data)
        {
            if (!(_positionSyncData.timestamp - data.timestamp >= 1.0) && !((data.position - _positionSyncData.position).sqrMagnitude > 0.0001f) && !((data.velocity - _positionSyncData.velocity).sqrMagnitude > 0.0001f))
            {
                return Mathf.Abs(Mathf.DeltaAngle(data.desiredAngle, _positionSyncData.desiredAngle)) > 0.1f;
            }
            return true;
        }

        // 本体 UpdatePositionSyncData のうち指紋の対象であるゲート呼び出し2箇所
        // （ldarg.0 / ldarga.s data / call）だけを残した見本。
        internal void UpdatePositionSyncData(PositionSyncData data)
        {
            if (HasMeaningfulSyncDataChange(in data)) GateAccepts++;
            if (HasMeaningfulSyncDataChange(in data)) GateAccepts++;
        }

        // 本体 UserCode_CmdPositionSyncData__PositionSyncData のうちゲート呼び出し1箇所
        // （ldarg.0 / ldarga.s data / call）だけを残した見本。
        private void UserCode_CmdPositionSyncData__PositionSyncData(PositionSyncData data)
        {
            if (HasMeaningfulSyncDataChange(in data)) UserCodeAccepts++;
        }

        // 検証の否側の見本: ゲートに渡すのが引数でなくローカルの写し（ldloca.s になる）。
        private void UpdatePositionSyncDataOnLocalCopy(PositionSyncData data)
        {
            PositionSyncData copy = data;
            if (HasMeaningfulSyncDataChange(in copy)) GateAccepts++;
        }
    }
    internal class Displacement
    {
        public float elapsedTime, cancelTime;
        public Action onCancel, onFinish;
    }
    internal sealed class DispByTarget : Displacement
    {
        public bool isFriendly;
        public Entity target;
    }
    // NativeProxyPositionIdlePerf の受け口。本体 Dew.Core DewPhysics.UpdateProxyPosition /
    // ProxyCollider（2026-10-19 逆コンパイル確認）の、パッチが触る範囲の模倣:
    // プロキシの transform への (x, z, 0) 代入だけ。Transform.position の getter/setter は
    // 境界の UnityEngine.Transform（CharacterModelDisplayBoundsBoundary）を使う。
    internal sealed class ProxyCollider
    {
        public UnityEngine.Transform transform;
    }
    // （#988）partial: StealthRevealOverlapPerfBoundary.cs が円クエリ系の本体 API
    // （EntityFilter・TryGetEntity・OverlapCircleAllEntities）を足す。
    internal static partial class DewPhysics
    {
        // 本体と同じ代入（引数の y は捨てて 0 を書く）。
        internal static void UpdateProxyPosition(ProxyCollider proxy, UnityEngine.Vector3 position)
        {
            proxy.transform.position = new UnityEngine.Vector3(position.x, position.z, 0f);
        }
    }
    // NativePickupIdlePerf の受け口。本体 Dew.Core PickupInstance（2026-10-20 逆コンパイル確認）の、
    // パッチが触る範囲の模倣: 毎フレーム更新の対象判定（Network_target）と速度の2フィールド。
    // Network_target は本体では SyncVar の生成 getter だが、ゲートが読むのは null 判定だけなので
    // 単純なプロパティで足りる。ActiveFrameUpdate は本体では protected override だが、
    // NativePerfPatch.Find の DeclaredMethod 解決に必要なのは宣言と本体だけで、ここでは内部メソッドでよい。
    internal class PickupInstance : Actor
    {
        public Hero Network_target { get; set; }
        internal Vector2 _currentFlatVelocity;
        internal float _currentYVelocity;
        internal void ActiveFrameUpdate() { }
    }
    // NativeMerchantPropIdlePerf の受け口。本体 Dew.Core PropEnt_Merchant_Base（2026-10-21
    // 逆コンパイル確認）のうち、Prepare が探す宣言と引数の形に加えて、ActiveLogicUpdate の
    // 中身（base 呼び出しより後の props への位置合わせループ）を転写する。本体では
    // protected override で、各 props の transform に商人本体の位置と向きを
    // SetPositionAndRotation で書き込む（DashSweepStepPerfBoundary.cs の Transform の
    // 受け口に落ちる）。ActiveLogicUpdateTwoSites は SetPositionAndRotation の呼び出し箇所が
    // 2つ並んだ形の拒否の見本（実行はせず、Transpiler に渡す IL の取り出しだけに使う）。
    internal abstract class PropEnt_Merchant_Base
    {
        public GameObject[] props;
        public Transform transform;

        protected void ActiveLogicUpdate(float dt)
        {
            if (props == null)
            {
                return;
            }
            GameObject[] array = props;
            foreach (GameObject gameObject in array)
            {
                if (gameObject != null)
                {
                    gameObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
                }
            }
        }

        internal void ActiveLogicUpdateTwoSites(float dt)
        {
            props[0].transform.SetPositionAndRotation(transform.position, transform.rotation);
            props[1].transform.SetPositionAndRotation(transform.position, transform.rotation);
        }
    }
    // NativeGenericTransformIdlePerf の受け口。本体 Dew.Core GenericTransformSync（2026-10-21
    // 逆コンパイル確認）の、パッチが触る範囲の模倣: FrameUpdate の宣言と、サーバーでない端末での
    // _pos / _rot の transform への転写（同期変数が NaN を含むとき本体はその書き込みを飛ばす）。
    // 実機と同じく Mirror.NetworkBehaviour の子孫（isServer と transform を継承）。
    // GenericTransformIdlePerfTests（ProxyPositionIdlePerfTests.cs）が Prefix の省略判定を
    // この模倣に対して直接動かす。
    internal class GenericTransformSync : NetworkBehaviour
    {
        internal Vector3 _pos;
        internal Quaternion _rot;

        internal void FrameUpdate()
        {
            if (isServer)
            {
                return;
            }
            if (!float.IsNaN(_pos.x) && !float.IsNaN(_pos.y) && !float.IsNaN(_pos.z))
            {
                transform.position = _pos;
            }
            if (!float.IsNaN(_rot.x) && !float.IsNaN(_rot.y) && !float.IsNaN(_rot.z) && !float.IsNaN(_rot.w))
            {
                transform.rotation = _rot;
            }
        }
    }
    // NativeTimescaleIdlePerf の受け口。本体 Dew.Core TimescaleManager（2026-10-20 逆コンパイル確認）
    // のうちパッチが触る範囲の模倣: FrameUpdate の宣言と、Postfix が読む effectTimescale。
    // 実機の weaver 生成セッター Network<effectTimescale>k__BackingField は <> 入りのため
    // C# で宣言できず、Prepare のビット抽出は本体 DLL でのみ解決される。この境界で
    // Prepare を呼ぶと fail-soft の警告1回で下りる（名前解決の成否は検証対象にしない）。
    // TimescaleIdlePerfTests は Prepare を通らず、実機と同じ意味の書き口
    // Network_effectTimescale（等値なら書かず、異値なら字段を書いて dirty ビットを OR で立てる。
    // #876 の StatsRatioIdlePerfBoundary と同じ GeneratedSyncVarSetter 経由）で値を動かし、
    // ビット定数（実機の IL 抽出と同じ役割）はテストから 1UL << 2 で差し込む。
    internal class TimescaleManager : NetworkBehaviour
    {
        internal const ulong EffectBit = 1UL << 2;
        private float _effectTimescale;
        public void FrameUpdate() { }
        public float effectTimescale => _effectTimescale;
        internal void Network_effectTimescale(float value)
            => GeneratedSyncVarSetter(value, ref _effectTimescale, EffectBit, null);
    }
    internal static class EntityLifetimeExtensions
    {
        // 本体の拡張と同じ意味: 消失・非アクティブ・死亡・KO を無効対象とする。
        internal static bool IsNullInactiveDeadOrKnockedOut(this Entity entity)
            => entity == null || !entity.isActive || entity.currentHealth <= 0
               || entity is Hero hero && hero.isKnockedOut;
    }
    // 本体の EntityCheck.IsAnyBoss と同じ意味（BossMonster または Boss/MiniBoss 型）。
    internal static class EntityCheck
    {
        internal static bool IsAnyBoss(this Entity entity)
            => entity is BossMonster || entity is Monster monster
               && (monster.type == Monster.MonsterType.Boss || monster.type == Monster.MonsterType.MiniBoss);
    }
    // CoopOmens.cs の公開ラッパーと同じ式。本体では public だが、この境界の Entity は
    // internal なので public 署名を作れず、同じ意味の internal 模倣として置く。
    internal static class CoopOmens
    {
        internal static bool Enabled { get => HostAuthority.CoopOmensEnabled; set => HostAuthority.CoopOmensEnabled = value; }
        internal static bool IsSharedObjective(Entity entity) => NetworkServer.active
            && HostAuthority.NativeInstance?.IsCoopOmenWard(entity) == true;
    }
    internal class BasicEffect { public bool isAlive = true; public StatusEffect parent; }
    internal sealed class StunEffect : BasicEffect { }
    internal sealed class RootEffect : BasicEffect { }
    internal class StatusEffect : Actor
    {
        public Entity victim;
        public bool isActive = true;
        public float creationTime;
        public float? maxDuration, remainingDuration;
        public void SetTimer(float duration) { maxDuration = duration; remainingDuration = duration; }
        public void SetTimer(float duration, float usePrevious) { maxDuration = duration; }
        public void StopBasicEffect(BasicEffect effect) => effect.isAlive = false;
        // NativePerfPatches.cs の StatusDuration 系パッチが参照する本体 API。
        public void FrameUpdate() { }
        public bool isServer;
        public float? Network_remainingDuration { get; set; }
        public void SetSyncVarDirtyBit(ulong dirtyBit) { }
    }
    internal sealed class Se_GenericEffectContainer : StatusEffect { public string id; }
    // NativeMirageVolumeIdle の受け口。本体 Dew.Core Se_MirageSkin_Delusion_Delusional（2026-10-21
    // 逆コンパイル確認）の、パッチが触る範囲の模倣: ActiveLogicUpdate の宣言だけ（Prepare の
    // DeclaredMethod 探索用。中身は呼ばれないので空）。
    internal sealed class Se_MirageSkin_Delusion_Delusional : StatusEffect
    {
        internal void ActiveLogicUpdate(float dt) { }
    }
    // 本体 Dew.Core FxVolume／FxInterpolatedEffectBase（2026-10-21 逆コンパイル確認）の、
    // パッチが読む範囲の模倣: currentValue・targetStrength・UpdateVolume の宣言と、
    // Prepare が FieldRef で掴む _volume／_contextMultiplier。Volume は下の UnityEngine.Rendering 側。
    // updateVolumeCalls は本体にはない記録で、ApplyVolume が呼び出しを省いたかどうかを
    // テスト（MirageVolumeIdlePerfTests）が数えるためのもの。
    internal class FxVolume
    {
        public float currentValue;
        public float targetStrength = 1f;
        private UnityEngine.Rendering.Volume _volume;
        private float _contextMultiplier = 1f;
        public int updateVolumeCalls;
        public void UpdateVolume() { updateVolumeCalls++; }
    }
    internal static class ManagerBase<T> { public static T softInstance; }
    // （#763）本体と同じ意味のカメラ角度。実物は get が public のプロパティで、セッターが
    // entityCamAngleRotation へ Euler(0, 角度, 0) を同じ式で保持する（CameraManager.cs:122-132）。
    // 角度・保持値ともシリアライズ対象外のため、境界も同じ不変だけを保つ。
    internal class CameraManager
    {
        public bool isPlayingCutscene;
        public int zoomSteps = 5;
        public Vector3 farZoomBody = new Vector3(0f, 12f, -14f);
        public Vector3 midZoombody = new Vector3(0f, 10f, -10f);
        public Vector3 closeZoomBody = new Vector3(0f, 8f, -6f);
        public List<CameraModifierBase> _cameraModifiers = new List<CameraModifierBase>();
        public bool isSpectating { get; private set; }
        public int currentZoomIndex { get; private set; }
        private float _entityCamAngle;
        [NonSerialized] public Quaternion entityCamAngleRotation = Quaternion.identity;
        public float entityCamAngle
        {
            get { return _entityCamAngle; }
            set { _entityCamAngle = value; entityCamAngleRotation = Quaternion.Euler(0f, _entityCamAngle, 0f); }
        }
    }
    // NativeActorLifetime.cs は未リンク。この境界では警告の受け口だけが必要。
    internal static class NativeActorLifetime { internal static void WarnOnce(string kind, Exception error) { } }
    internal struct ReactionChain { }
    [Flags]
    internal enum DamageAttribute { None = 0, IsCrit = 1, IgnoreArmor = 2, IgnoreShield = 4, DamageShieldOnly = 8, IgnoreDamageImmunity = 16 }
    // 本体のダメージ情報と同じ形。敵担当制の受ダメ補正はこの生データに直接掛かる。
    internal struct DamageData
    {
        public enum SourceType { Normal, Pure }
        public float amount;
        public Actor actor;
        public DamageAttribute attributes;
        public SourceType source;
        public float damageToShieldMultiplier;
        public bool isBlockedByImmunity;
        public float currentAmount => Math.Max(0f, amount);
        public bool HasAttr(DamageAttribute attr) => attr != 0 && (attributes & attr) == attr;
        public DamageData ApplyReduction(float ratio)
        {
            amount *= 1f - Math.Max(0f, Math.Min(1f, ratio));
            return this;
        }
        public DamageData ApplyAmplification(float ratio)
        {
            amount *= 1f + ratio;
            return this;
        }
        public DamageData(float value)
        {
            amount = value;
            actor = null;
            attributes = 0;
            source = SourceType.Normal;
            damageToShieldMultiplier = 0f;
            isBlockedByImmunity = false;
        }
    }
    // 本体の回復処理（takenHealProcessor）の形。倍率は生データに反映する。
    internal struct HealData
    {
        public float amount;
        public void ApplyReduction(float ratio) => amount *= 1f - ratio;
        public void ApplyAmplification(float ratio) => amount *= 1f + ratio;
    }
    internal delegate void DataProcessor<TData, TActor, TTarget>(ref TData data, TActor actor, TTarget target);
    internal sealed class DataProcessorGroup<TData, TActor, TTarget>
    {
        public readonly List<DataProcessor<TData, TActor, TTarget>> Processors = new List<DataProcessor<TData, TActor, TTarget>>();
        public void Add(DataProcessor<TData, TActor, TTarget> processor, int priority) => Processors.Add(processor);
        public void Remove(DataProcessor<TData, TActor, TTarget> processor) => Processors.Remove(processor);
    }
    // 本体 CastInfo（IEquatable<CastInfo>）と同じ5字段と等値判定。NativeSampleUpdateIdlePerf が
    // 再送を省くかの比較に使い、本体と同じ参照比較と成分ごとの等値を持つ。
    internal struct CastInfo
    {
        public Entity caster;
        public Entity target;
        public Vector3 point;
        public float angle;
        public float animSelectValue;
        public bool Equals(CastInfo other) =>
            ReferenceEquals(caster, other.caster) && ReferenceEquals(target, other.target)
            && point == other.point && angle == other.angle && animSelectValue == other.animSelectValue;
    }
    // 本体 SampleCastInfoContext（サンプリング文脈の構造体）。NativeSampleUpdateIdlePerf が
    // 文脈の受け渡し先（UserCode_TpcSetSampleContext）を探すときの引数型
    // Nullable<SampleCastInfoContext> として必要なだけで、中身は検証しない。
    internal struct SampleCastInfoContext { }
    internal struct EventInfoCast { public AbilityInstance instance; public CastInfo info; }
    internal struct EventInfoAbilityInstance { public AbilityInstance instance; public Actor actor; }
    internal struct EventInfoApplyElemental { public Actor actor; public Entity victim; public ElementalType type; public int addedStack; }
    internal struct EventInfoDamage { public Actor actor; public Entity victim; public DamageData damage; }
    internal struct EventInfoKill { public Actor actor; public Entity victim; }
    internal struct EventInfoAttackEffect { public Actor actor; public Entity victim; public ReactionChain chain; }
    internal sealed class DewPlayer
    {
        public static DewPlayer local;
        public static readonly List<DewPlayer> gamePlayers = new List<DewPlayer>();
        public static readonly List<DewPlayer> lobbyPlayers = new List<DewPlayer>();
        public static Action<DewPlayer> onLobbyPlayerAdded, onLobbyPlayerRemoved;
        public static Action<DewPlayer> onGamePlayerAdded, onGamePlayerRemoved;
        public static readonly List<DewPlayer> allHumanPlayers = new List<DewPlayer>();
        public string playerName;
        public uint netId;
        public string guid;
        public bool isHumanPlayer = true;
        public int gold, dreamDust;
        public Hero hero;
        public NetworkConnectionToClient connectionToClient;
        public Action BeforeCurrencyChange, AfterCurrencyChange;
        // NativeLobbyAccessoryIdle（リンク）が記録・比較するロビー装備の入力（#833）。本体では
        // selectedAccessories は Mirror の SyncList<string>、ownedItems は List<string>。
        // IsAllowedToUseItem の本体は名前の接頭辞ごとに DewResources の資源を引き、server 生成で
        // なければ無条件に許可、server 生成なら ownedItems の保持を見る。資源辞書と
        // generatedFromServer は起動後に不変（PR #833 の理論）のため、この境界では接頭辞を持つ
        // 名前の許可を ownedItems の保持だけで表す。
        public bool isEveryInfoSet;
        public string selectedSkin = "";
        public string selectedHeroType = "Hero_Mist";
        public readonly SyncList<string> selectedAccessories = new SyncList<string>();
        public List<string> ownedItems = new List<string>();
        public bool IsAllowedToUseItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
            {
                return false;
            }
            if (itemName.StartsWith("Acc_") || itemName.StartsWith("Nametag_") || itemName.StartsWith("Emote_") || itemName.StartsWith("Skin_"))
            {
                return ownedItems.Contains(itemName);
            }
            return false;
        }
        public void SpendGold(int amount)
        {
            BeforeCurrencyChange?.Invoke();
            gold -= amount;
            AfterCurrencyChange?.Invoke();
        }
        public void SpendDreamDust(int amount)
        {
            BeforeCurrencyChange?.Invoke();
            dreamDust -= amount;
            AfterCurrencyChange?.Invoke();
        }
        public void EarnDreamDust(int amount)
        {
            BeforeCurrencyChange?.Invoke();
            dreamDust += amount;
            AfterCurrencyChange?.Invoke();
        }
    }
    // NativeChatMuteAllocPerf（リンク）が typeof で探す本体のチャット受信のミュート判定。
    // 実物は allHumanPlayers.Find(ラムダ) で送信者を探し、見つからなければミュート扱いとする。
    // ミュート一覧の中身（IsPlayerMuted）はパッチがネイティブ呼び出しのまま残すので、
    // この境界では判決の形だけを実物どおりに模倣する。一覧の中身は mutedPlayers として
    // 外から差し替えられる形にし、既定は空（誰もミュートしていない）。テストはこの一覧で
    // ミュート済み（true）・未ミュート（false）の両方を本体と Prefix の両方に同じように出す。
    internal class ChatManager
    {
        public readonly HashSet<DewPlayer> mutedPlayers = new HashSet<DewPlayer>();
        public bool IsPlayerMuted(DewPlayer player) => mutedPlayers.Contains(player);
        public bool IsPlayerMutedByNetId(uint playerNetId)
        {
            foreach (DewPlayer p in DewPlayer.allHumanPlayers)
                if (p.netId == playerNetId) return IsPlayerMuted(p);
            return true;
        }
    }
    // NativePlayerTimerIdlePerf（リンク）の Prepare が typeof で探す本体の画面内タイマーの
    // ハンドル。実機ではグローバル名前空間の公開クラスで、ChargingChannel だけが
    // DewPlayer.ShowOnScreenTimer に渡す（実 DLL 逆コンパイル 2026-10-19）。ここには実物の
    // 字段の形だけを置く（中身は呼ばれないので空）。
    internal class NetworkedOnScreenTimerHandle
    {
        public string localeKey;
        public string skillKey;
        public Func<float> valueGetter;
        internal int _id;
    }
    internal static class NetworkedManagerBase<T>
    {
        public static T softInstance;
        public static T instance => softInstance;
    }
    internal static class SingletonDewNetworkBehaviour<T> { public static T softInstance; }
    internal sealed class Room { public bool isActive, didClearRoom; public readonly List<Section> sections = new List<Section>(); }
    internal enum WorldNodeType { Combat, ExitBoss, Shop, Event }
    internal sealed class WorldNode { public WorldNodeType type; public bool isWellRoom; }
    // 部屋の兆し（HostAuthority.RoomOmens.cs）が資格判定で読む本体の部屋構成。
    internal enum SectionCombatAreaType { None, Yes }
    internal sealed class RoomMonsters { public bool isMarkedAsCombatArea; public bool isBossSpawn; public SectionCombatAreaType combatAreaSettings; }
    internal sealed class Section { public RoomMonsters monsters; }
    internal struct EventInfoLoadRoom { }
    internal sealed class GameManager
    {
        public string runId;
        public bool isGameConcluded, isGameTimePaused;
        public double elapsedGameTime;
        public Zone difficulty;
        public readonly Dictionary<string, DewPersistence.PlayerData> playerRejoinData = new Dictionary<string, DewPersistence.PlayerData>();
        public float GetAdjustedGoldAmount_Cost(float amount) => 1f;
        public void WrapUpAndShowResult(DewGameResult.ResultType type) => throw new NotSupportedException();
    }
    internal sealed class DewGameResult
    {
        public enum ResultType { Conceded, GameOver, PureWhiteDream, StarlessPath, UnknownFate }
        public ResultType result;
    }
    internal enum GameState { InLobby, Playing }
    internal sealed class GameSettingsManager
    {
        public GameState state = GameState.Playing;
        public readonly Dictionary<string, string> customData = new Dictionary<string, string>();
    }
    internal sealed class ActorManager
    {
        public Actor serverActor;
        public readonly List<Hero> allHeroes = new List<Hero>();
        public readonly List<Entity> allEntities = new List<Entity>();
    }
    // #112: 「ロビーに戻る」の確認で呼ばれる本体の入口。isEndingSession がtrue のEndSession 系
    // (メニュー・デスクトップ復帰など)は精算対象外。仮想プロパティで判別時の例外も注入できる。
    internal class DewNetworkManager
    {
        public static DewNetworkManager softInstance;
        public virtual bool isEndingSession { get; set; }
        public bool hasSessionEnded;
        // NativeLobbyAccessoryIdle（リンク）が続きデータの変化を見る本体の静的設定（#833）。
        // 実物は public static DewNetworkStartSettings startSettings = new DewNetworkStartSettings()。
        public static DewNetworkStartSettings startSettings = new DewNetworkStartSettings();
        public void RestartSession() { }
    }
    internal sealed class ZoneManager
    {
        public int currentZoneIndex;
        public bool isInAnyTransition, isVoting, isSidetracking;
        public int clearedCombatRooms, currentHuntLevel, currentRoomIndex;
        public Zone currentZone;
        public Room currentRoom;
        public readonly List<WorldNode> nodes = new List<WorldNode>();
        public int currentNodeIndex = -1;
        public WorldNode currentNode;
        public Action ReadyAfterTransition;
        public void CallOnReadyAfterTransition(Action action) => ReadyAfterTransition = action;
    }
    internal class GameMod_Limbo { public int depth; }
    internal sealed class Zone { public string name; }
    // BountyReportBatching.cs（リンク）の境界判定（BountyReportsBeforeBoundary）が対象に取る
    // 本体の結果確定入口。ハーネスでは Harmony の検証は行わないため、型とメソッドの形だけ。
    internal sealed class GameResultManager { public void WrapUp(DewGameResult.ResultType type) { } }
    // DewPersistence.SerializeGameData(LoadNodeSettings) の引数の型（本体と同じ名前）。
    internal sealed class LoadNodeSettings { }
    internal static class DewPersistence
    {
        internal sealed class GameData
        {
            public readonly Dictionary<string, string> serverActorData = new Dictionary<string, string>();
            public readonly List<PlayerData> players = new List<PlayerData>();
        }
        internal sealed class PlayerData { public string playerGuid; }
        public static GameData SerializeGameData() => new GameData();
        public static GameData SerializeGameData(LoadNodeSettings settings) => new GameData();
        public static void ApplyGameData(GameData data, Action onFinish = null) => onFinish?.Invoke();
        // NativeCustomRpcDispatchPerf 抽出が呼ぶ本体の JSON 受け口（実物は Newtonsoft 経路）。
        // 検証では実際に JSON を復元し、壊れた電文では本物と同じく例外を出す。
        public static object FromJson(string json, Type type) => Newtonsoft.Json.JsonConvert.DeserializeObject(json, type);
    }
    internal static class DewSave
    {
        public static string profileContinuePath;
        public static Action onSaveEnded;
        // NativeCursorIdlePerf 抽出が読む本体のプラットフォーム設定（実物は public static フィールド）。
        // 先の実体（gameplay.disableSoftwareCursor）は CursorIdlePerfBoundary.cs の境界。
        public static DewPlatformSettings platformSettings = new DewPlatformSettings();
    }
    internal sealed class InGameUIManager { public static InGameUIManager instance; public bool isDoingEnding; }
    internal sealed class Primus_Ending { public static void StartPrimusDeath() { } }
    internal static class Log
    {
        public static Action<string> ErrorSink;
        public static Action<string> InfoSink;
        public static void Info(string message) => InfoSink?.Invoke(message);
        public static void Error(string message)
        {
            if (ErrorSink != null) ErrorSink(message);
            else throw new InvalidOperationException(message);
        }
        public static readonly List<string> Warnings = new List<string>();
        public static void Warn(string message) { Warnings.Add(message); }
    }
    // ClientSession.TrackRun がラン開始時に呼ぶ近接分類ログのリセット（#542）。
    // このハーネスは TravelerCombatNative.cs をコンパイルしない（近接協力の本体は Core.Tests 側）ので、
    // 何もしない模倣を置く。Log と同じ扱い。
    internal static class TravelerCombatNative
    {
        internal static void ResetReporting() { }
    }
    internal static class NativeAttributedMemoryCast
    {
        internal sealed class Cast { public MemoryActivationIdentity Identity; public SkillTrigger Skill; }
        public static Cast Current;
    }
    internal static class NativeMemoryPayloadScope
    {
        internal sealed class Scope { public Actor Source; public Type ChildType; }
        public static Scope Current;
    }
    internal sealed partial class HostAuthority
    {
        internal static HostAuthority NativeInstance;
        // HostAuthority.MeleeCoop.cs is not compiled here; hello replies carry the "not sent" value.
        internal int MeleeCoopWire => 0;
        internal static readonly RunGrowthLedger RunGrowthLedger = new RunGrowthLedger();
        private Actor _registeredOn;
        private ZoneManager _zone;
        private ActorManager _am;
        private bool _roomHasVariant;
        private readonly Random _rng = new Random(1);
        private readonly Dictionary<Monster, MonsterRuntime> _monsters = new Dictionary<Monster, MonsterRuntime>();
        private readonly Dictionary<Monster, NightmareAffix> _nightmares = new Dictionary<Monster, NightmareAffix>();
        internal sealed class MonsterRuntime
        {
            public Monster Monster;
            public Variant Variant;
            public Behavior Behavior;
            public string KillEventId, KillEventStreamId, ClassificationVariant;
            public long GraphEpoch, SegmentEpoch, RoomEpoch;
            public uint KillEventNetId, SyncNetId;
            public bool ClassificationQueued;
            public NightmareAffix ClassificationNightmare;
            public MonsterOwnership Ownership, ClassificationOwnership;
            public StatBonus OmenBonus;
            public bool PressureApplied, SpawnProcessed;
            // 初期生成群の分類（CoopOmens）が読む登録時刻。
            public float QueuedAt;
        }
        internal sealed class Variant { public string Id; }
        internal sealed class Behavior { public bool CueQueued; }
        private double ShardDropMultiplierForKill() => 1;
        // HostAuthority.Waypoints.cs（リンク外）と同じ意味: 回復量の倍率を生データに反映する。
        private static void ScaleWaypointRecovery(ref HealData data, double multiplier)
        {
            if (multiplier < 1) data.ApplyReduction((float)(1 - multiplier));
            else if (multiplier > 1) data.ApplyAmplification((float)(multiplier - 1));
        }
        // CoopOmens.cs（リンク）が呼ぶ本体側の生成処理とC07補正。この境界では
        // 生成分類済み・補正なしを前提に何もしない。
        private void ProcessSpawns() { }
        internal void ApplyAuthoredFinalNativeDamage(ref DamageData damage, Actor actor, Entity target) { }
        internal static bool Alive(Entity entity) => entity != null && entity.isActive && entity.currentHealth > 0;
        private void SendMonsterBehaviorCue(MonsterRuntime runtime, bool force, DewPlayer target = null)
        {
            // Native cue transport is not needed in these scenarios. Unexpected calls fail rather than inventing behavior.
            throw new NotSupportedException("Behavior cue transport is outside the harness.");
        }
        // 軽量化の検証用: 本体側は接頭辞で Hero 発の攻撃のときだけ呼ぶ。呼び出し回数と結果を記録する。
        internal static int GuaranteedBasicQueries;
        internal static bool GuaranteedBasicResult;
        internal static bool IsGuaranteedBasicV129(Actor actor, Entity from) { GuaranteedBasicQueries++; return GuaranteedBasicResult; }
        internal void TryWeakspotBasic(Hero hero, Entity to, ref bool critical) { }
        internal float NativeShieldBeforeDamage(Entity target) => target?.Status.currentShield ?? 0;
        internal static bool AllNormalMemoriesReady(Entity entity) => true;
        internal void OnNativeMemoryUsed(SkillTrigger skill, bool ready) => throw new NotSupportedException();
        internal void OnNativeElementApplied(EventInfoApplyElemental info) => throw new NotSupportedException();
        private static bool BossNativeEquippedSkill(Hero hero, SkillTrigger skill) => hero.Skill.Slots.ContainsValue(skill);
        private long ModShieldEquipmentEpoch(HeroRuntime rt) => throw new NotSupportedException();
        private bool BossEnsure(HeroRuntime rt) => throw new NotSupportedException();
        internal void ObserveBossGeneratedDispatch(NativeAttributedDamagePacket.Packet packet, DamageData damage) { }
        private void ObserveBossGeneratedDamage(EventInfoDamage info) { }
        private void PublishBossNativeDamage(EventInfoDamage info) { }
        private void RegisterExactNativeMemoryAdapters() { }
        private bool BindExactNativePayload(EventInfoAbilityInstance info) => false;
        private static bool RequiresExactNativeProjectileScope(Actor actor) => false;
        private void BindExactNativeBasicSource(AttackTrigger attack, AbilityInstance instance, Hero hero, MemoryActivationIdentity identity) => throw new NotSupportedException();
        internal void BindResolveBeforePrepare(AttackTrigger attack, EventInfoCast cast) => throw new NotSupportedException();
        internal void PublishAttributedBasicFired(AttackTrigger attack, AbilityInstance instance, CastInfo info) => throw new NotSupportedException();
        private bool TryGetExactNativeDirectPayload(Actor actor, Entity target, ReactionChain chain, out MemoryActivationIdentity identity) { identity = default; return false; }
        private bool ExactNativeChainMatches(Actor actor, ReactionChain chain) => true;
        private bool IsPairReactionSource(Actor actor) => false;
        private void OnIdentityStrikeBasicHit(Hero hero, Entity target, long activation, bool critical = false, long victimLifetime = 0) => throw new NotSupportedException();
        private void PublishMemoryActivation(MemoryActivationEvent notification, Hero hero, Entity victim, float damage) => MemoryActivationPublished?.Invoke(notification, hero, victim, damage);
        private readonly Dictionary<Actor, ReactionChain> _attributedNativeChains = new Dictionary<Actor, ReactionChain>();
        private void ClearNativeEndingActor(Actor actor) { }
        private void ResetNativeEndingAdapters() { }
        private int _gimmickDamageDepth, _pairDamageDepth, _reactionEffectDepth;
        private bool _reflectingDamage, _shattering;
        private static readonly HeroSkillLocation[] LinkSkills = { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R, HeroSkillLocation.Identity, HeroSkillLocation.Movement };
        private SkillTrigger FindMemory(Hero hero, string name)
        {
            foreach (var skill in hero.Skill.Slots.Values) if (skill.GetType().Name == name) return skill;
            return null;
        }
        private readonly Dictionary<int, MemoryActivationIdentity> _nativeBaptismEndings = new Dictionary<int, MemoryActivationIdentity>();
        private readonly Dictionary<Hero, HeroRuntime> _runtimes = new Dictionary<Hero, HeroRuntime>();
        // HostAuthority.cs（リンク外）の単発報告の送信。ここでは境界として呼び出しの記録だけを残す。
        internal readonly List<(HeroRuntime Rt, BountyReportKind Kind, int Value)> SentBountyReports
            = new List<(HeroRuntime, BountyReportKind, int)>();
        private void SendBountyReport(HeroRuntime rt, BountyReportKind kind, int value) =>
            SentBountyReports.Add((rt, kind, value));
        private readonly Dictionary<DewPlayer, ReceivedBuild> _builds = new Dictionary<DewPlayer, ReceivedBuild>();
        private readonly Dictionary<DewPlayer, BuildTransferReceiver> _incomingBuilds = new Dictionary<DewPlayer, BuildTransferReceiver>();
        private readonly Dictionary<DewPlayer, BuildUpdateCoalescer> _buildUpdates = new Dictionary<DewPlayer, BuildUpdateCoalescer>();
        private readonly HashSet<DewPlayer> _buildRejectionsLogged = new HashSet<DewPlayer>();
        private readonly List<DewPlayer> _buildUpdatePlayers = new List<DewPlayer>();
        private readonly Dictionary<Hero, float> _applyRetryAt = new Dictionary<Hero, float>();
        private float _tickNow;
        private float _nextGemSlotCheck;
        // BountyReportBatching.cs の間引きフラッシュ（TickBountyReports）が使う次回送信時刻。
        private float _nextBountyFlush;
        private bool _pressureDirty;
        // 協力取引ホスト（HostAuthority.CoopTrade.cs は未リンク）。NativePersistence.targets が
        // 本物のハンドラ群を抽出するため、ここはその実行に必要な状態の形だけを用意する。
        // HostAuthority.CoopTrade.cs の CoopProfileResendInterval と同じ値。
        private const float CoopProfileResendInterval = 15f;
        private sealed class CoopPeerState
        {
            public float Seen;
            public bool Safe, Fusion;
            public int ConnectionId = int.MinValue;
            public readonly BuildTransferReceiver Transfer = new BuildTransferReceiver(CoopTradeTransport.MaxProfileChars);
            public readonly CoopTradeSendQueue Outgoing = new CoopTradeSendQueue();
            public string Envelope;
        }
        private sealed class CoopHostTrade
        {
            public string Id;
            public DewPlayer First, Second;
            public CoopTradeOffer FirstOffer = new CoopTradeOffer(), SecondOffer = new CoopTradeOffer();
            public Profile FirstProfile, SecondProfile;
            public string FirstOfferAck, SecondOfferAck;
            public bool Accepted, FirstConfirmed, SecondConfirmed, Preparing, FirstPrepared, SecondPrepared;
            public int Revision;
            public int FirstProfileSentRevision = -1, SecondProfileSentRevision = -1;
            public float FirstProfileSentAt = -1000f, SecondProfileSentAt = -1000f;
            public float ActiveAt;
            public CoopTradeReceipt Receipt;
        }
        private readonly Dictionary<DewPlayer, CoopPeerState> _coopHostPeers = new Dictionary<DewPlayer, CoopPeerState>();
        private NetworkConnectionToClient _coopServerConnection;
        private bool _coopServerRegistered;
        private GameSettingsManager _coopAdvertisedSettings;
        private float _nextCoopAdvertisement;
        private CoopTradeJournal _coopJournal;
        private bool _coopJournalRequiresExisting;
        private CoopHostTrade _coopHostTrade;
        private string _coopHostFailure;
        private float _nextCoopJournalRetry;
        private float _nextCoopHostTick;
        internal sealed class ReceivedBuild { public Build Build; }
        private readonly Dictionary<Hero, AuthoredState> _authoredMechanisms = new Dictionary<Hero, AuthoredState>();
        private readonly Dictionary<HeroRuntime, object> _gimmickV129 = new Dictionary<HeroRuntime, object>();
        private readonly Dictionary<Hero, BridgeState> _bridgeSuccessEffects = new Dictionary<Hero, BridgeState>();
        private readonly Dictionary<Hero, PrimedState> _memoryPrimedRelay = new Dictionary<Hero, PrimedState>();
        private bool HasOwnSummons(HeroRuntime runtime) => false;
        private KeystonePayload TransformAuthoredPayload(Hero hero, KeystonePayload payload, string source, string receiver, KeystoneSourceKind kind) => throw new NotSupportedException();
        // SkollBurstBoundary.cs が BossVisuals / Skoll が触る分（Boss・ShieldEquipmentEpoch）を足す。
        internal sealed partial class HeroRuntime
        {
            public Hero Hero;
            public ReceivedBuild AppliedBuild;
            // HostAuthority.cs の HeroRuntime と同じ、集計報告の未送信カウンタ（BountyReportBatching.cs）。
            public string PendingBountyRun;
            public int PendingBountyShields, PendingBountyHeals;
            public readonly int[] PendingElementalKills = new int[16];
            public readonly List<PendingGimmick> PendingGimmicks = new List<PendingGimmick>();
            public readonly Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>> Summons =
                new Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>>();
            public readonly Prunable Gimmicks = new Prunable(), PairCombos = new Prunable();
            // HostAuthority.cs の HeroRuntime と同じ行動不能保険の観測状態。
            public BasicEffect OrphanCrowdControl;
            public float OrphanCrowdControlSince = -1f;
            public BasicEffect FrozenCrowdControl;
            public float FrozenCrowdControlRemaining, FrozenCrowdControlSince;
        }
        internal sealed class PendingGimmick { public AuthoredPending Authored; }
        internal struct AuthoredPending { public bool Enabled; public object Legacy; public MemoryActivationEvent Notification; }
        internal sealed class Prunable
        {
            public void PruneAttribution(MemoryActivationAttribution attribution) => throw new NotSupportedException();
            public void ForgetActivation(object activation) { }
            public void PruneAttributedActivations(MemoryActivationAttribution attribution) => throw new NotSupportedException();
        }
        internal sealed class AuthoredState { public readonly Dictionary<string, Channel> Channels = new Dictionary<string, Channel>(); }
        internal sealed class Channel { public readonly HashSet<long> Counted = new HashSet<long>(); public AuthoredMechanismEntry Entry; }
        internal sealed class BridgeState { public PairComboRuntime Runtime; }
        internal sealed class PrimedState { public MemoryPrimedRuntime Primed; public RelayWindowRuntime Relay; }
        internal sealed class AuthoredGimmickDispatch
        {
            public AuthoredGimmickDispatch(HostAuthority host) { }
            public void Clear() { }
        }
    }
    // Native Infinity is unavailable by default. Tests may enable save agreement and supply
    // shared snapshots for an exploring participant; native graph/choice operations are out of scope.
    internal static partial class InfinityMode
    {
        internal static bool Available { get; set; }
        private static bool _restoring, _newInfinity, _refresh, _hunterAdjustSuspended;
        private static int _hunterMoveCounter;
        // InfinityMode.cs の区画一致キャッシュと同じ形（抽出された ReleaseMatchedZone が触る）。
        private static Zone _matchedZone;
        private static string _matchedZoneId;
        private static InfinityRunState _initial;
        private static InfinityChoice _choice, _ackWaitingChoice;
        private static string _choiceText;
        private static readonly Dictionary<string, long> Acks = new Dictionary<string, long>();
        internal static bool NativeEnvelopePresent => false;
        internal static InfinityRunState State => null;
        private static void ClearPendingTravel() { }
        internal static void InterceptionFailed(string hook, Exception error) => Log.Warn(hook + ": " + error.Message);
        internal static bool Enabled { get; set; }
        internal static bool NativeSaveAgreement { get; set; }
        internal static void WriteEnvelope() { }
        internal static bool IsTechnicalRefresh => false;
        internal static InfinityChoice CurrentChoice => _choice;
        internal const string ChoiceKey = "dreamforge.infinity.choice";
        internal static bool CanAdvance => throw new NotSupportedException();
        internal static void Tick() { }
        internal static void DisableFeature(string reason) => throw new NotSupportedException(reason);
        internal static void CompleteReturn(InfinityRunState state) => throw new NotSupportedException();
        // #528: ゲストの TickInfinity が AcknowledgeLocal を呼んだ回数を数える観測点。
        // 本体では DreamforgeInfinityAckMsg をサーバーへ送るが、境界では送信そのものを再現しない。
        internal static int AcknowledgeCalls;
        internal static InfinityChoice LastAcknowledgedChoice;
        internal static void AcknowledgeLocal(InfinityChoice choice) { AcknowledgeCalls++; LastAcknowledgedChoice = choice; }
        internal static bool PartyAcknowledged(InfinityChoice choice) => throw new NotSupportedException();
        internal static bool Regenerate(string intent) => throw new NotSupportedException();
        internal static void PublishChoice(InfinityChoice choice) => _choice = choice;
        // 本体では RoomMod_SpawnWell の修飾子で判定する。境界ではノードのフラグで代用する。
        internal static bool IsWellRoom(WorldNode node) => node != null && node.isWellRoom;
        internal sealed class InfinityChoice
        {
            public string RunId;
            public long Revision, SegmentEpoch, GraphEpoch;
            public bool Boundary, Secure;
            public Pact Pact;
            public RunChoiceSnapshot Before;
            public string BeforeChoices;
            public bool PersonalTimedOut;
            public float PersonalAckGraceSeconds = 30f;
            public List<InfinityPersonalChoice> PersonalChoices;
        }
    }
    internal static class ContentFingerprint { internal const string Value = "native-test-content"; }
    internal sealed partial class ClientSession
    {
        private const float SweepDelaySeconds = 1.5f;
        private float _roomSweepPendingAt;
        public Profile Profile;
        public Hero LocalHero;
        // ClientSession.cs(リンク外)の実装と同じ意味: ロビー復帰済みの遠征は精算が保留の間だけ活性。
        public bool RunActive => ContinueReady && _nativeContinueCheckpoint == null
            && Profile.Run != null && ActiveRunId != null && Profile.Run.RunId == ActiveRunId
            && (!Profile.LobbyReturnedRunIds.Contains(ActiveRunId) || LobbyReturnPending);
        public string ActiveRunId { get; internal set; }
        public bool HasPendingTrades => _trades.PendingCount > 0 || CoopTradeLocked;
        // CoopTradeLocked is extracted from the production session.
        private bool _coopNativeSaving;
        private string CoopUnavailable() => Loc.T("トレードは安全な時・保存完了後のみ利用できます。", "Trade is available only at a safe moment, after saving completes.");
        // 協力取引クライアント（ClientSession.CoopTrade.cs は未リンク）。NativePersistence.targets が
        // 本物の判定・送信・受信メソッドを抽出するため、ここはその実行に必要な状態の形だけを用意する。
        internal string CoopTradeJournalPath;
        private readonly List<CoopTradePeer> _coopPeers = new List<CoopTradePeer>();
        private readonly BuildTransferReceiver _coopTransfer = new BuildTransferReceiver(CoopTradeTransport.MaxProfileChars);
        private readonly CoopTradeSendQueue _coopSendQueue = new CoopTradeSendQueue();
        private NetworkConnection _coopConnection;
        private GameSettingsManager _coopSettings;
        private string _coopAdvertisement;
        private float _coopAdvertisementAt = -100f;
        private float _nextCoopProbe, _coopAnsweredAt = -100f;
        private string _coopHostKey, _coopFrozenProfile;
        private CoopTradeOffer _coopPendingOffer;
        private string _coopOfferInFlight;
        private float _coopOfferSentAt, _nextCoopOfferRetry;
        private bool _coopSettlementUnsaved, _coopDisabled, _coopHostFusion;
        private long _coopPrepareSaveRevision;
        private string _coopWaitingId, _coopWaitReason;
        private float _coopWaitingAt, _coopWaitReasonAt;
        private bool _coopWaitReleased;
        public CoopTradeView CoopTrade { get; set; }
        // Warning storage/getter are extracted from production in NativePersistence.targets.
        internal static string CoopHostJournalPath => _hostSession?.CoopTradeJournalPath;
        internal static bool CoopHostSafe => _hostSession?.CoopLocalSafe == true;
        internal static bool CoopHostNativeSaving => _hostSession?._coopNativeSaving == true;
        internal static void ReceiveLocalCoopTrade(DreamforgeCoopTradeState state) => _hostSession?.OnCoopTradeState(state);
        private sealed class CoopSendException : Exception
        {
            public CoopSendException(Exception cause) : base(cause.Message, cause) { }
        }
        // ClientSession.cs(リンク外)の実装と同じ意味。
        public bool HasHeldTrades => _trades.HeldCount > 0;
        public bool CanEditLoadout => Profile.Run == null || Profile.Run.AwaitingChoice || Profile.Run.GearWindow || !InGame;
        public bool InGame => NetworkedManagerBase<GameManager>.softInstance != null;
        private float _nextSave = float.MaxValue;
        private bool _buildDirty;
        private bool _allocationSavePending;
        private float _allocationBuildDue;
        private Actor _clientRpcOn;
        private string _continueReceiptRunId, _continueReceiptCheckpointId, _continueReceiptResumeSession;
        private string _sentContinueReceiptRunId, _sentContinueReceiptCheckpointId, _sentContinueReceiptResumeSession;
        private Actor _sentContinueReceiptActor;
        private float _helloFirstSent = -1f, _nextHello;
        private bool _helloAnswered, _hostCompatibilityWarned, _hostInfinityAvailable;
        private int _hostMeleeCoop;
        // ClientSession.Hello.cs と同じ保管（#510: Txt 保持＋getter で解決）。ResetHello の抽出が書く。
        private Txt _hostVersionWarning;
        public string HostVersionWarning => _hostVersionWarning?.ToString();
        private Build _buildCache;
        private float _buildCacheTime;
        private string _buildCacheHero;
        private Hero _lastHero;
        private int _sentDreamLevel;
        private float _nextBuildSend;
        private string _lastSentBuild;
        private Actor _lastBuildActor;
        private Hero _lastBuildHero;
        private float _lastBuildHeartbeat;
        private ZoneManager _zone;
        private AsyncProfileWriter _writer;
        private ProfileStore _store;
        private readonly TradeLedger _trades = new TradeLedger();
        private long _hostLedgerId;
        private double _nextLedgerProbeAt;
        // 取引の輸送・照会・結果反映は NativePersistence.targets が本物のメソッドを抽出する。
        private readonly List<PendingTrade> _dueTradeQueries = new List<PendingTrade>();
        public bool HostConfirmed;
        private long _infinityObservedClears;
        private long _infinityAcknowledgedRevision = -1, _infinityAcknowledgedGraph = -1;
        private bool _infinityAcknowledgedBoundary, _infinityResultStarted;
        private float _nextInfinityAck;
        private string _infinityInitializedRun;
        private RunChoiceSnapshot _infinityMirroredSnapshot;
        private long _infinityPendingSaveRevision;
        private string _infinityPendingSaveRun;
        private AsyncProfileWriter _infinityPendingSaveWriter;
        private float _infinityPendingSaveStarted, _nextInfinitySaveRetry;
        private string _infinitySaveHoldReleasedRun;
        private bool _infinityPendingPublication;
        private bool _dirty, _saveErrorFromWriteFailure;
        private string _writeFailureText, _writeFailureSource;
        private LocLanguage _writeFailureLanguage;
        private Exception _saveErrorCause;
        private string _saveErrorText;
        private AsyncProfileWriter _writeFailureKnownWriter;
        private long _writeFailureKnownSerial;
        private int _buildCacheFrame = -1, _saveCount;
        private double _saveMsTotal;
        public string SaveError => _saveErrorCause != null ? _saveErrorCause.Message : _saveErrorText;
        private readonly Action<GameEvent> _notify;
        private float _nextDreamEventNotice;
        private string _curseSyncedKey = "";
        private readonly Dictionary<uint, NightmareAffix> Nightmare = new Dictionary<uint, NightmareAffix>();
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public ClientSession(Action<GameEvent> notify = null)
        {
            _notify = e => { Events.Add(e); notify?.Invoke(e); };
        }
        public event Action ProfileChanged;
        private int _lastHuntLevel = -1;
        private readonly RoomCounter _rooms = new RoomCounter();
        private string CurseKey() => "";
        private void SendCurseClear() { }
        private void NotifyPersonalDreamEvent() { }
        private void ClearVariants() { }
        private void ClearMonsterCues() { }
        private void ClearBossDisplay() { }
        private void RequireOrdinaryRun()
        {
            if (Profile.Run?.Infinity != null) throw new NotSupportedException("Infinity is outside the harness.");
        }
        internal static void ValidateHostInfinityContinue() { }
        internal static bool HostInfinityBoundarySettled => HostAuthority.InfinityBoundarySettled;
        internal static long HostInfinityRetireBeforeSegment(long current) => HostAuthority.InfinityRetireBeforeSegment(current);
        private bool TryInfinitySecure(out string error)
        {
            RequireOrdinaryRun();
            error = null;
            return false;
        }
        private bool TryInfinityDelve(Pact pact, out string error)
        {
            RequireOrdinaryRun();
            error = null;
            return false;
        }
        private void InitializeInfinityRun() { }
        private void EnsureReachableBounties() => Rules.ReplaceUnreachableBounties(Profile, solo: false);
        public float PressureHealthMultiplier { get; internal set; } = 1f;
        public float PressureDamageMultiplier { get; internal set; } = 1f;
        public float NightmareGearMultiplier { get; private set; }
        public float NightmarePressureHealthMultiplier { get; private set; }
    }
}
// 本体 Unity.TextMeshPro の TMP_Text のうちパッチが触る範囲（text の入れ替え）だけの模倣。
// 実物と同じグローバル名前空間に置く（Unity.TextMeshPro への参照は無いので名前だけ衝突しない）。
// テストが「代入が起きた回数」を見られるよう、set_text の呼ばれた回数も数える。
namespace TMPro
{
    internal class TMP_Text
    {
        private string _text;
        public string text { get => _text; set { _text = value; TextWrites++; } }
        internal int TextWrites;
    }
}
