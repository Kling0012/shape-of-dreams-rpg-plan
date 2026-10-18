using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

// 敵担当制（HostAuthority.EnemyOwnership.cs / EnemyOwnershipSync.cs）のための本体 API 境界。
// NativeBoundary.cs の既存スタブに加えて、位置・AI・移動・ナビメッシュ・圧の最小限の形を置く。
// ここには本体の処理は書かない: 割り当て・強さ・保護・流れ弾・付け替え・同期は
// リンクされた実ソース側がそのまま実行する。
namespace UnityEngine
{
    internal struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        public float sqrMagnitude => x * x + y * y + z * z;
    }
}
namespace UnityEngine.AI
{
    internal enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
}
// 本体のグローバル名前空間の Dew。ナビメッシュ判定は常に到達可能とする境界。
internal static class Dew
{
    internal static UnityEngine.AI.NavMeshPathStatus GetNavMeshPathStatusCached(
        SodRpg.Mod.Entity from, SodRpg.Mod.Entity to, UnityEngine.Vector3 start, UnityEngine.Vector3 end)
        => UnityEngine.AI.NavMeshPathStatus.PathComplete;
}
namespace SodRpg.Mod
{
    // 本体の Vector3 拡張と同じ意味（XZ 平面で近さを見る本体の距離判定に合わせて XZ を返す）。
    internal static class Vector3Extensions
    {
        internal static Vector2 ToXY(this Vector3 value) => new Vector2(value.x, value.z);
    }

    // 本体のステータス再計算の形。最終最大HPは処理器の積み重ねで作り直される。
    internal class FinalStats
    {
        public float maxHealth = 100f;
        public float armor;
    }
    internal delegate void DataProcessor<TData>(ref TData data);
    internal sealed class ProcessorList<T>
    {
        public readonly List<T> Entries = new List<T>();
        public readonly List<int> Priorities = new List<int>();
        public void Add(T value, int priority = 0) { Entries.Add(value); Priorities.Add(priority); }
        public void Remove(T value)
        {
            int index = Entries.IndexOf(value);
            if (index >= 0) { Entries.RemoveAt(index); Priorities.RemoveAt(index); }
        }
        /// <summary>昇順優先度、同優先度は追加順（本体のProcessorListと同じ意味）。</summary>
        public IEnumerable<T> InOrder()
        {
            var order = new List<int>();
            for (int i = 0; i < Entries.Count; i++) order.Add(i);
            order.Sort((a, b) => Priorities[a] != Priorities[b] ? Priorities[a].CompareTo(Priorities[b]) : a.CompareTo(b));
            foreach (int i in order) yield return Entries[i];
        }
    }

    internal class EntityAIContext
    {
        public Entity targetEnemy;
    }
    internal class EntityAI
    {
        public static bool DisableAI;
        public Entity entity;
        public float detectionRange = 20f;
        public readonly EntityAIContext context = new EntityAIContext();
        public int AggroCalls;
        public void Aggro(Entity target)
        {
            if (Mirror.NetworkServer.active)
            {
                HostAuthority.NativeInstance?.SelectSummonOwnershipAggro(this, ref target);
                if (HostAuthority.NativeInstance?.SelectOwnershipAggro(this, ref target) == false) return;
            }
            AggroCalls++;
            context.targetEnemy = target;
        }
    }

    // RoomMonsters.SpawnMonsterImp が受ける本体の湧き設定。rule.isBossSpawn だけが担当判定に入る。
    internal sealed class SpawnMonsterSettings
    {
        public RoomMonsters rule;
    }

    internal sealed partial class HostAuthority
    {
        // 刻印の選択状態のみ境界で保持。盾壁の標的選択と挑発記録は実ソースを実行する。
        private sealed class CoopKeyState
        {
            internal Hero Hero;
            internal KeystoneDefinition Definition;
            internal float V(string name) => CoopKeystones.Value(Definition, name);
        }

        // HostAuthority.cs / HostAuthority.PressureDividend.cs（リンク外）が呼び出し元の定義宣言。
        partial void OwnershipEntityAdded(Entity entity);
        partial void OwnershipEntityRemoved(Entity entity);
        partial void OverrideOwnershipHealth(Monster monster, ref float multiplier);
        partial void RecordDynamicHealth(Entity enemy, double health, double appliedMultiplier);
        partial void ApplyDynamicKillReward(Monster monster, ref double multiplier);
        // AutoCastTargets.cs はこの境界ではリンク外。
        partial void ApplyAutoCastOwnershipPriority(Hero hero, Entity target, ref int priority);
        // HostAuthority.cs（リンク外）と同じ意味: 部屋の平均圧。所有圧への差し替え元になる。
        private DreamPressure _pressure = DreamPressure.Neutral;
        private void StagePressure() { }
        // 本体では部屋の圧を敵のランタイムに反映する。境界では適用済みフラグだけを進める。
        private void ApplyPressure(MonsterRuntime runtime) => runtime.PressureApplied = true;
    }
}
