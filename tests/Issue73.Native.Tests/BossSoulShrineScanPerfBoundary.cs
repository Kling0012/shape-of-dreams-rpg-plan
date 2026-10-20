// NativeBossSoulShrineScanPerf（NativePerfPatches.cs）を Issue73.Native.Tests で検証するための
// 本体 API 境界。Shrine_BossSoul（と変形版の神殿）をグローバル名前空間に置くのは、Prepare の
// AccessTools.TypeByName が実機の Dew.Core からこの名前で解決するのと同じ経路をテストでも
// 通すため（ChaosShrineHitScanPerfBoundary.cs と同じ扱い）。ボス型に実機と同じ単純名
// 「BossMonster」は使わない: NativeBoundary.cs に SodRpg.Mod.BossMonster が既にあり、どちらに
// 解決されるか保証できないためである。ShrineBossSoulBoss の一意な名前で同じ継承関係を置く。
// NativePerfPatches.cs は MOD 側からはこの型を参照しないので、テストプロジェクトはこの境界を
// このファイルだけで持つ（Entity・DewPlayer は NativeBoundary.cs の既存の境界）。
using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Mod;

// 実機 ActorManager.instance.allEntities（HashSet<Entity>）の境界。実機では
// NetworkedManagerBase<ActorManager>.get_instance() から取得するが、パッチが差し替えるのは
// 末端の Any 呼び出しだけで、取得の形は本体 IL のまま残るため、ここでは静的な欄に簡約する。
internal static class ShrineBossSoulActorManager
{
    internal static readonly HashSet<Entity> allEntities = new HashSet<Entity>();
}

internal class ShrineBossSoulBoss : Entity { }

internal sealed class ShrineBossSoulBossDerived : ShrineBossSoulBoss { }

internal sealed class ShrineBossSoulBossRival : Entity { }

// 実機 Shrine.CanInteract の境界。本体側の判定（近接・種別など）はここでは触れないため、
// 神殿から true/false を切り替えられるただの値にする。_usedPlayerIds は実機では
// Mirror.SyncList<string> だが、パッチはこの部分の IL を見ないため List<string> の Contains
// で同じ意味を置く。
internal class ShrineBossSoulShrineBase
{
    internal bool baseAllowsInteraction = true;
    internal readonly List<string> _usedPlayerIds = new List<string>();

    public virtual bool CanInteract(Entity entity) => baseAllowsInteraction;
}

// 本体 Dew.Core r1.4.1.2 Shrine_BossSoul.CanInteract（逆コンパイル Shrine_BossSoul.cs）の転写。
// 等価性テストの「パッチ無効側」として動き、NativeBossSoulShrineScanPerf の Transpiler が
// 差し替える Any 呼び出しもこれと同じ形の IL になる（コンパイラは述語を ldftn + newobj の
// 遅延キャッシュで構築する。実機の IL と同じ形）。
internal class Shrine_BossSoul : ShrineBossSoulShrineBase
{
    public override bool CanInteract(Entity entity)
    {
        if (base.CanInteract(entity) && entity.owner != null && !_usedPlayerIds.Contains(entity.owner.guid))
            return !ShrineBossSoulActorManager.allEntities.Any(e => e is ShrineBossSoulBoss);
        return false;
    }
}

// （拒否の見本1）述語がボスの is 判定だけでない形。将来の更新でラムダが
// 「e is BossMonster && e.isActive」のように変わった場合の再現。
internal class Shrine_BossSoulCompound : ShrineBossSoulShrineBase
{
    public override bool CanInteract(Entity entity)
    {
        if (base.CanInteract(entity) && entity.owner != null && !_usedPlayerIds.Contains(entity.owner.guid))
            return !ShrineBossSoulActorManager.allEntities.Any(e => e is ShrineBossSoulBoss && e.isActive);
        return false;
    }
}

// （拒否の見本2）述語が同じ形でも別の型への is 判定になっている場合の再現。
internal class Shrine_BossSoulOtherBoss : ShrineBossSoulShrineBase
{
    public override bool CanInteract(Entity entity)
    {
        if (base.CanInteract(entity) && entity.owner != null && !_usedPlayerIds.Contains(entity.owner.guid))
            return !ShrineBossSoulActorManager.allEntities.Any(e => e is ShrineBossSoulBossRival);
        return false;
    }
}

// （拒否の見本3）述語が静的フィールドに置かれていて、本体の IL に述語の ldftn + newobj が
// 現れない形。この形では述語の中身を突き止められないため、差し替えは見送られる。
internal class Shrine_BossSoulCachedPredicate : ShrineBossSoulShrineBase
{
    internal static readonly Func<Entity, bool> cachedPredicate = e => e is ShrineBossSoulBoss;

    public override bool CanInteract(Entity entity)
    {
        if (base.CanInteract(entity) && entity.owner != null && !_usedPlayerIds.Contains(entity.owner.guid))
            return !ShrineBossSoulActorManager.allEntities.Any(cachedPredicate);
        return false;
    }
}
