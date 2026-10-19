// NativeChaosShrineHitScanPerf（NativePerfPatches.cs）を Issue73.Native.Tests で検証するための
// 本体 API 境界。Se_Shrine_Chaos_StatBonus をグローバル名前空間に置くのは、Prepare の
// AccessTools.TypeByName が実機の Dew.Core からこの名前（FullName）で解決するのと同じ
// 経路をテストでも通すため。NativePerfPatches.cs は MOD 側からはこの型を参照しないので、
// テストプロジェクトはこの境界をこのファイルだけで持つ。
//
// 本体の Se_Shrine_Chaos_StatBonus.ActorEventOnDealDamage は、混沌の神殿を持つ英雄が
// ダメージを与えるたびに、相手の Light/Dark の属性状態（StackedStatusEffect）を
// TryGetStatusEffect で走査して見つけ、maxStack へ Mathf.Max(maxStack, 5 + ボーナス)
// を書く。ここにはその書き込みの形だけを転写する: 走査は読み取りだけで結果に関与せず、
// 省略の対象そのものだからだ。maxStack の既定は 5（本体と同じ）。
internal sealed class Se_Shrine_Chaos_StatBonus
{
    public int currentAddedLightMaxStack;
    public int currentAddedDarkMaxStack;

    internal void ActorEventOnDealDamage(ShrineChaosVictimStatuses victim)
    {
        if (victim.light != null)
            victim.light.maxStack = UnityEngine.Mathf.Max(victim.light.maxStack, 5 + currentAddedLightMaxStack);
        if (victim.dark != null)
            victim.dark.maxStack = UnityEngine.Mathf.Max(victim.dark.maxStack, 5 + currentAddedDarkMaxStack);
    }
}

// 被弾者から見つかった Light/Dark の属性状態。null は「その属性状態が見つからない」。
internal sealed class StackedStatusEffect
{
    public int maxStack = 5;
}

internal sealed class ShrineChaosVictimStatuses
{
    public StackedStatusEffect light;
    public StackedStatusEffect dark;
}
