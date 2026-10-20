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
using System;
using System.Collections.Generic;
using SodRpg.Mod;

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

// ShrineChaosChoiceCache（NativePerfPatches.cs）が参照する本体 API の境界。実機では
// Shrine_Chaos : EditSkillShrine で、選択中の報酬を rewards[guid][int.Parse(GetCustomData(guid))]
// で解決する。ここにはその形だけを置く: rewards の辞書と customData の文字列、解決結果の
// ChaosReward（type と quantity だけ Prefix が読む）、編集モードUIに返す型。DewLocalization の
// 返り値は未使用（呼ばれることだけが等価性の対象）なので null を返す。
internal enum ChaosRewardType
{
    MaxHealth,
    AttackDamage,
    AttackSpeed,
    AbilityPower,
    AbilityHaste,
    UpgradeGem,
    UpgradeSkill,
    Armor
}

internal struct ChaosReward
{
    public ChaosRewardType type;
    public float quantity;
}

[Flags]
internal enum EditSkillTargetType
{
    None = 0,
    Skill = 1,
    SkillEmptySlot = 2,
    Gem = 4,
    GemEmptySlot = 8
}

internal struct EditSkillTargetInfo
{
    public int? nextLevel;
    public string rejectReasonRawText;
}

internal struct GemLocation { }

internal static class DewLocalization
{
    public static string GetUIValue(string key) => null;
}

internal abstract class EditSkillShrine
{
    public static readonly UnityEngine.Color GenericGemIndicatorColor = new UnityEngine.Color(0.6156863f, 14f / 15f, 1f);
    public static readonly UnityEngine.Color GenericSkillIndicatorColor = new UnityEngine.Color(1f, 0.79607844f, 1f / 3f);

    public string GetCustomData(DewPlayer player) => null;
}

internal sealed class Shrine_Chaos : EditSkillShrine
{
    public Dictionary<string, ChaosReward[]> rewards = new Dictionary<string, ChaosReward[]>();

    public string GetEditSkillIndicatorRawText() => null;
    public UnityEngine.Color GetEditSkillIndicatorColor() => default;
    public EditSkillTargetType GetTargetTypes(DewPlayer player) => EditSkillTargetType.None;
    public EditSkillTargetInfo GetTargetInfo(DewPlayer player, HeroSkillLocation loc, SkillTrigger target) => default;
    public EditSkillTargetInfo GetTargetInfo(DewPlayer player, GemLocation loc, Gem target) => default;
}
