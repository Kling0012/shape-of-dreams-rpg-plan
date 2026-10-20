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
// で解決する。ここにはその解決の連鎖と5つの本体メソッド（GetEditSkillIndicatorRawText /
// GetEditSkillIndicatorColor / GetTargetTypes / GetTargetInfo の2つ）を転写する
// （逆コンパイル Dew.Core/Shrine_Chaos.cs、Dew.Core/EditSkillShrine.cs と同じ制御流れ）。
// 例外の形も本体と同じにする: rewards への鍵が無ければ KeyNotFoundException、customData が
// 解析できなければ FormatException（null は ArgumentNullException）、添字が範囲外なら
// IndexOutOfRangeException。等価性テスト（ChaosShrineChoiceCachePerfTests.cs）は
// Prefix を経由した結果とこの転写本体の結果を比べる。
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
    // 本体はローカライズ辞書を引く。返り値同士の比較に使えるように、キーをそのまま返す
    // （キーごとに異なる文字列になれば、どのキーで呼ばれたかが結果から分かる）。
    public static string GetUIValue(string key) => key;
}

internal abstract class EditSkillShrine
{
    public static readonly UnityEngine.Color GenericGemIndicatorColor = new UnityEngine.Color(0.6156863f, 14f / 15f, 1f);
    public static readonly UnityEngine.Color GenericSkillIndicatorColor = new UnityEngine.Color(1f, 0.79607844f, 1f / 3f);

    // 本体は SyncDictionary<string, string>。GetValueOrDefault は鍵が無いとき null を返す
    // （鍵が無くても例外にはならない）。
    private readonly Dictionary<string, string> _playerIdToCustomData = new Dictionary<string, string>();

    public string GetCustomData(DewPlayer player)
        => _playerIdToCustomData.GetValueOrDefault(player.guid);

    // 本体の EnterEditSkill は _playerIdToCustomData[guid] = customData と、鍵ごとに文字列を
    // 差し替える（文字列は不変なので、差し替われば参照も変わる）。
    public void EnterEditSkill(DewPlayer player, string customData)
        => _playerIdToCustomData[player.guid] = customData;
}

internal sealed class Shrine_Chaos : EditSkillShrine
{
    public Dictionary<string, ChaosReward[]> rewards = new Dictionary<string, ChaosReward[]>();

    // 以下の5つは本体（Dew.Core/Shrine_Chaos.cs）の転写。報酬の解決連鎖
    // rewards[guid][int.Parse(GetCustomData(guid))] を毎回やり直す。
    public string GetEditSkillIndicatorRawText()
    {
        ChaosReward chaosReward = rewards[DewPlayer.local.guid][int.Parse(GetCustomData(DewPlayer.local))];
        if (chaosReward.type == ChaosRewardType.UpgradeSkill)
            return DewLocalization.GetUIValue("InGame_SelectSkillToUpgrade");
        if (chaosReward.type == ChaosRewardType.UpgradeGem)
            return DewLocalization.GetUIValue("InGame_SelectGemToUpgrade");
        return null;
    }

    public UnityEngine.Color GetEditSkillIndicatorColor()
    {
        ChaosReward chaosReward = rewards[DewPlayer.local.guid][int.Parse(GetCustomData(DewPlayer.local))];
        if (chaosReward.type == ChaosRewardType.UpgradeSkill)
            return EditSkillShrine.GenericSkillIndicatorColor;
        if (chaosReward.type == ChaosRewardType.UpgradeGem)
            return EditSkillShrine.GenericGemIndicatorColor;
        return default;
    }

    public EditSkillTargetType GetTargetTypes(DewPlayer player)
        => rewards[player.guid][int.Parse(GetCustomData(player))].type switch
        {
            ChaosRewardType.UpgradeSkill => EditSkillTargetType.Skill,
            ChaosRewardType.UpgradeGem => EditSkillTargetType.Gem,
            _ => EditSkillTargetType.None,
        };

    // 本体と同じく player 引数は使わず、target の所有者（target.owner.owner）で解決する。
    public EditSkillTargetInfo GetTargetInfo(DewPlayer player, HeroSkillLocation loc, SkillTrigger target)
    {
        EditSkillTargetInfo result = default;
        result.nextLevel = target.level + (int)rewards[target.owner.owner.guid][int.Parse(GetCustomData(target.owner.owner))].quantity;
        result.rejectReasonRawText = (!target.isLevelUpEnabled) ? DewLocalization.GetUIValue("InGame_Message_CantUpgradeThisSkill") : null;
        return result;
    }

    // 本体と同じく player 引数は使わず、target の所有者（target.owner.owner）で解決する。
    public EditSkillTargetInfo GetTargetInfo(DewPlayer player, GemLocation loc, Gem target)
    {
        EditSkillTargetInfo result = default;
        result.nextLevel = target.quality + (int)rewards[target.owner.owner.guid][int.Parse(GetCustomData(target.owner.owner))].quantity;
        return result;
    }
}
