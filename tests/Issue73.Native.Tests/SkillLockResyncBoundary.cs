using System;
using System.Collections.Generic;
using System.Globalization;
using Mirror;

// （#1123）スキル再同期の安全装置（NativeSkillLockRecovery.cs・NativeCooldownSafety.cs・
// HostAuthority.SkillLock.cs をリンク）を Issue73.Native.Tests で動かすための本体 API 境界。
// NativeBoundary.cs の既存スタブ（Actor・Entity・Hero・DewPlayer・TriggerConfig・AbilityTrigger 等）を
// partial で拡張し、この経路が必要な本体の私有字段・同期 RPC・UI 参照を足す。
// RPC の中身は観測用の記録になる: RpcLog は要素ごとの3種（ResendAuthoritative が呼ぶ分）、
// ArrayRpcs は全配列 RPC（RpcSyncAfterFrameDelayStates_Imp）。両方の送出が見えるので、
// 全配列 RPC が送れたときに要素ごとの再送が省けたこと・送れなかったときに戻ることを検証できる。
namespace Mirror
{
    internal partial class SyncList<T>
    {
        // 本体 Mirror SyncList<T>.Operation の境界。NativeSkillLockRecovery が
        // 同値 SET の積み直しに使う OP_SET だけを扱う。
        internal enum Operation { OP_ADD, OP_CLEAR, OP_DIRTY, OP_INSERT, OP_REMOVEAT, OP_SET }

        // 観測: AddOperation（同値再送の積み直し）の呼び出し記録。
        public readonly List<string> Operations = new List<string>();

        internal void AddOperation(Operation operation, int index, T oldItem, T newItem, bool keepWaiting)
            => Operations.Add(operation + " " + index + " " + newItem);
    }

    // 本体 Mirror SyncDictionary<TKey, TValue> の境界。EntityAbility._synedAbilities が
    // この形で、NativeSkillLockRecovery.ResendHero が TryGetValue とインデクサで読み書きする。
    internal class SyncDictionary<TKey, TValue>
    {
        // （#1174）本体 Mirror SyncIDictionary が Callback で購読者へ渡す3つ組（操作・鍵・値）。
        // 本体の列挙子はこれより多いが、NativeEntityAbilitySubscribeDedup の Prefix は
        // 購読の Target と Method だけを見るため、シグネチャが合っていれば十分。
        internal enum Operation { OP_ADD, OP_CLEAR, OP_REMOVE, OP_SET }

        // （#1174）本体の Callback（event の裏欄）に相当する購読欄。Prefix はこの欄を
        // 反射で読み書きするため、同じ名前のデリゲート欄として境界に置く。
        public Action<Operation, TKey, TValue> Callback;

        private readonly Dictionary<TKey, TValue> items = new Dictionary<TKey, TValue>();

        public TValue this[TKey key] { get => items[key]; set => items[key] = value; }

        public bool TryGetValue(TKey key, out TValue value) => items.TryGetValue(key, out value);
    }
}

namespace SodRpg.Mod
{
    // 本体 Dew.Core の SyncedNetworkBehaviour（NetworkBehaviour の子孫）の境界。
    internal class SyncedNetworkBehaviour : Mirror.NetworkBehaviour { }

    // 診断スナップショット（Describe）が読む本体のマネージャー・UI。実行は Reflection で解決するため、
    // 型と参照される成员があればよく、既定の null・空のままでよい。
    internal static partial class ManagerBase<T>
    {
        // 実機の ManagerBase<T>.instance（生成済みインスタンス）。境界の softInstance と同じ扱い。
        public static T instance;
    }
    internal class EditSkillManager { public object currentProvider; }
    internal class UI_InGame_SkillButtons { public UI_InGame_SkillButton[] skillButtons = new UI_InGame_SkillButton[0]; }
    internal class UI_InGame_SkillButton
    {
        // 本体では HeroSkillLocation 型の公開プロパティ。HasStaleUiTarget が (int) で比較する。
        public HeroSkillLocation skillType;
        internal SkillTrigger _target;
    }
    internal class ControlManager
    {
        public Entity controllingEntity;
        public bool isCharacterControlEnabled = true, isMainSkillDisabled, isDodgeDisabled;
    }

    // TriggerConfig の Validator 面（Describe のみが読む）。
    internal sealed class SkillConfigSelfValidator
    {
        public bool Evaluate(Entity owner) => true;
    }

    // 本体の IsNullOrInactive は拡張メソッドで null も受け取れる（空きスロットはここで落とす）。
    internal static class SkillTriggerBoundaryExtensions
    {
        public static bool IsNullOrInactive(this AbilityTrigger trigger) => trigger == null || !trigger.isActive;
    }

    internal partial class TriggerConfig
    {
        public bool isActive = true;
        public float manaCost;
        public readonly SkillConfigSelfValidator selfValidator = new SkillConfigSelfValidator();
    }

    internal partial class Entity
    {
        // 実機では Actor は NetworkBehaviour の子孫で isServer を継ぐ。境界の Actor は継がないため、
        // この経路が読む所有権・状態面をここに置く。
        public bool isServer;
        public bool isOwned = true;
        public float currentMana;
        public void SetDirty() { }
        // NativeEntityOwnerSyncVarPerf（リンク）の Prefix が読む NetworkBehaviour 継承面。
        // 実機の isClient は netIdentity.isClient、netIdentity は identity の
        // InitializeNetworkBehaviours で設定される。テストでは Prefix を通さないため形だけ。
        public bool isClient;
        public NetworkIdentity netIdentity;
        // NativeProjectileTargetSyncVarPerf（リンク）の Prefix が標的 SyncVar の一致判定で読む
        // NetworkBehaviour のフィールド。実物は InitializeNetworkBehaviours が NetworkBehaviours
        // の添字を入れる。ProjectileTargetSyncVarPerfTests が Prefix を直接呼ぶ一致判定で使う
        // （相手の netId は NativeBoundary.cs 側の Entity 公開フィールド）。
        public byte ComponentIndex;
    }

    internal partial class AbilityInstance
    {
        // DescribeInstances（診断スナップショット）が読む所有権面。実機は NetworkBehaviour 継承。
        public uint netId;
        public bool isActive = true;
    }

    internal sealed partial class HeroSkill
    {
        // 本体 HeroSkill の6スロット。HostAuthority.SkillLock.ResendHeroSkills が読む。
        public SkillTrigger Q, W, E, R, Identity, Movement;
    }

    internal sealed partial class EntityAbility
    {
        public int Network_abilityCastLockBitmap;
        // NativeSkillLockRecovery が FieldRef で解決する本体の私有字段。
        public readonly Mirror.SyncDictionary<int, SyncedNetworkBehaviour> _synedAbilities
            = new Mirror.SyncDictionary<int, SyncedNetworkBehaviour>();
        public bool IsAbilityCastLocked(int index)
        {
            if (index < 0) return false;
            if (index > 64) throw new InvalidOperationException("Ability index out of bounds");
            return (Network_abilityCastLockBitmap & (1L << index)) != 0;
        }
        public void SetDirty() { }

        // （#1174）本体 EntityAbility.OnStartClient のうち NativeEntityAbilitySubscribeDedup が
        // 触る範囲の転写。本体は NetworkBehaviour.OnStartClient の上書きで、初期化のあと
        // _synedAbilities.Callback += OnAbilityChanged と再購読するだけ（外す処理は本体の
        // どこにもない。実 DLL r1.4.1.2 の逆コンパイルで確認済み）。境界では継承なしの
        // 同じ名前・同じ引数（無し）のメソッドとして置く。Prepare と Prefix はこの形で解決する。
        public void OnStartClient()
        {
            _synedAbilities.Callback += OnAbilityChanged;
        }

        // 本体は渡された3つ組を ClientEvent_OnAbilityAdded / Removed と
        // OnLocalHeroAbilityChanged へ配る。境界では呼ばれた回数と引数の観測だけを記録する。
        private void OnAbilityChanged(Mirror.SyncDictionary<int, SyncedNetworkBehaviour>.Operation op, int key, SyncedNetworkBehaviour value)
        {
            AbilityChangedCalls.Add(op + " " + key);
        }

        // テストが変化1つあたりの配信回数を数えるための観測欄。
        public readonly List<string> AbilityChangedCalls = new List<string>();
    }

    internal partial class EntityControl
    {
        public readonly List<object> ongoingChannels = new List<object>();
        public bool Network_blockAbility, Network_blockAbilityCancelable, Network_blockDodge, Network_blockDodgeCancelable;
        public bool isAirborne, isDashing, isDodging;
        public void SetDirty() { }
    }

    internal partial class EntityStatus
    {
        public int basicEffectMask;
        public void SetDirty() { }
    }

    internal partial class AbilityTrigger
    {
        // --- NativeSkillLockRecovery / NativeCooldownSafety が Reflection で解決する本体の私有字段。 ---
        public readonly Mirror.SyncList<bool> _configCastLocks = new Mirror.SyncList<bool>();
        public readonly Mirror.SyncList<bool> _configCooldownLocks = new Mirror.SyncList<bool>();
        public bool _isConfigDirty;
        public float[] currentMinimumDelays;
        // --- 実機では NetworkBehaviour から継ぐ所有権・状態面。 ---
        public uint netId;
        public bool isActive = true;
        public bool isServer;
        public int abilityIndex;
        public int currentConfigIndex;
        public bool Network_isCasting;
        public void SetDirty() { }
        public virtual bool CanBeCast() => true;
        public virtual bool CanBeReserved() => true;

        // --- 観測: 要素ごとの権威再送 RPC（NativeCooldownSafety.ResendAuthoritative が呼ぶ3種）。
        // 送出順まで含めて文字列で残し、値の比較には構造化した記録も使う。 ---
        public readonly List<string> RpcLog = new List<string>();
        public readonly List<(int index, float time)> CooldownTimeRpcs = new List<(int index, float time)>();
        public readonly List<(int index, int charge)> ChargeRpcs = new List<(int index, int charge)>();
        public readonly List<(int index, float delay)> MinimumDelayRpcs = new List<(int index, float delay)>();
        // 観測: 全配列 RPC。throwOnArrayRpc で送出時の例外を注入できる。
        public readonly List<(float[] times, int[] charges, float[] delays)> ArrayRpcs
            = new List<(float[] times, int[] charges, float[] delays)>();
        public bool throwOnArrayRpc;

        internal void RpcSetCooldownTime(int configIndex, float time)
        {
            RpcLog.Add("time " + configIndex + " " + time.ToString("R", CultureInfo.InvariantCulture));
            CooldownTimeRpcs.Add((configIndex, time));
        }

        internal void RpcSetCharge(int configIndex, int charge)
        {
            RpcLog.Add("charge " + configIndex + " " + charge);
            ChargeRpcs.Add((configIndex, charge));
        }

        internal void RpcSetMinimumDelay(int configIndex, float delay)
        {
            RpcLog.Add("mindelay " + configIndex + " " + delay.ToString("R", CultureInfo.InvariantCulture));
            MinimumDelayRpcs.Add((configIndex, delay));
        }

        internal void RpcSyncAfterFrameDelayStates_Imp(float[] times, int[] charges, float[] delays)
        {
            if (throwOnArrayRpc) throw new InvalidOperationException("injected array rpc failure");
            ArrayRpcs.Add((times, charges, delays));
        }
    }
}
