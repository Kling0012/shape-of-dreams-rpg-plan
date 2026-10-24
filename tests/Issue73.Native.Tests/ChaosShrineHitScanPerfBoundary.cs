// NativeChaosShrineHitScanPerf・NativeChaosStatBonusTooltipCachePerf（NativePerfPatches.cs）を
// Issue73.Native.Tests で検証するための本体 API 境界。Se_Shrine_Chaos_StatBonus をグローバル
// 名前空間に置くのは、Prepare の AccessTools.TypeByName が実機の Dew.Core からこの名前
// （FullName）で解決するのと同じ経路をテストでも通すため。NativePerfPatches.cs は MOD 側からは
// この型を参照しないので、テストプロジェクトはこの境界をこのファイルだけで持つ。
//
// 本体の Se_Shrine_Chaos_StatBonus.ActorEventOnDealDamage は、混沌の神殿を持つ英雄が
// ダメージを与えるたびに、相手の Light/Dark の属性状態（StackedStatusEffect）を
// TryGetStatusEffect で走査して見つけ、maxStack へ Mathf.Max(maxStack, 5 + ボーナス)
// を書く。ここにはその書き込みの形だけを転写する: 走査は読み取りだけで結果に関与せず、
// 省略の対象そのものだからだ。maxStack の既定は 5（本体と同じ）。
using System;
using System.Collections.Generic;
using System.Globalization;
using SodRpg.Mod;

// 本体の StatBonus のうち、加護のツールチップ説明文（GetCustomTooltipDescription）が読む
// 字段だけを転写した型。NativeBoundary.cs の StatBonus（部屋の兆し用）は字段が部分的に
// 重なる別運用なので、混沌専用にこの名前で置く。名前の先頭の _ は実機の字段名と同じ。
internal sealed class ChaosShrineStatBonus
{
    public float _maxHealthFlat, _armorFlat, _attackDamageFlat, _abilityPowerFlat,
        _attackSpeedPercentage, _abilityHasteFlat, _attackRangeFlat, _critChanceFlat,
        _fireEffectAmpFlat, _coldEffectAmpFlat, _movementSpeedPercentage;
    public int _everyFourAttackStartIndexFlat;
}

internal sealed class Se_Shrine_Chaos_StatBonus
{
    public int currentAddedLightMaxStack;
    public int currentAddedDarkMaxStack;

    // ツールチップ説明文が読む残りの入力（本体の SyncVar・synced なボーナス。reflection-dump の
    // Se_Shrine_Chaos_StatBonus.txt と同じ字段名）。NativeChaosStatBonusTooltipCachePerf は
    // これら全て（float 13・int 10）と言語データをスナップショットの入力に数える。
    public float currentDodgeHaste;
    public int currentDodgeAddedCharges, currentAddedMaxLevel;
    public int currentAddedGemSlotQ, currentAddedGemSlotW, currentAddedGemSlotE,
        currentAddedGemSlotR, currentAddedGemSlotIdentity;
    public ChaosShrineStatBonus syncedRegularChaosBonus = new ChaosShrineStatBonus();
    public ChaosShrineStatBonus syncedCorruptedChaosBonus = new ChaosShrineStatBonus();

    // テストから「本体（この転写）が何回呼ばれたか」を見るための計数。本体側には無い。
    public int tooltipDescriptionBuilds;

    internal void ActorEventOnDealDamage(ShrineChaosVictimStatuses victim)
    {
        if (victim.light != null)
            victim.light.maxStack = UnityEngine.Mathf.Max(victim.light.maxStack, 5 + currentAddedLightMaxStack);
        if (victim.dark != null)
            victim.dark.maxStack = UnityEngine.Mathf.Max(victim.dark.maxStack, 5 + currentAddedDarkMaxStack);
    }

    // 本体の GetCustomTooltipDescription（Dew.Core/Se_Shrine_Chaos_StatBonus.cs）の転写。
    // 本体は各値を Mathf.Approximately で判定して非ゼロの項だけ string.Format で連ねるが、
    // ここでは読む入力すべて（23 値と言語データ）を必ず文に含める: キャッシュの検証に必要な
    // 性質は「入力が変われば文が変わる・変わらなければ同じ文」で、零の省略の字形はその
    // 判定に影響しないからだ。言語データは本体と同じく DewLocalization.data を経由して引く
    // （言語を替えると参照と文が替わる）。
    public string GetCustomTooltipDescription()
    {
        tooltipDescriptionBuilds++;
        var r = syncedRegularChaosBonus;
        var c = syncedCorruptedChaosBonus;
        return string.Concat(
            DewLocalization.data.GetUIValue("Se_Shrine_Chaos_StatBonus_Desc"),
            " hp=", r._maxHealthFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " ar=", r._armorFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " ad=", r._attackDamageFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " ap=", r._abilityPowerFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " as=", r._attackSpeedPercentage.ToString("0.##", CultureInfo.InvariantCulture),
            " ah=", r._abilityHasteFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " rng=", c._attackRangeFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " crit=", c._critChanceFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " fire=", c._fireEffectAmpFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " cold=", c._coldEffectAmpFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " ms=", c._movementSpeedPercentage.ToString("0.##", CultureInfo.InvariantCulture),
            " cah=", c._abilityHasteFlat.ToString("0.##", CultureInfo.InvariantCulture),
            " e4=", c._everyFourAttackStartIndexFlat.ToString(CultureInfo.InvariantCulture),
            " dh=", currentDodgeHaste.ToString("0.##", CultureInfo.InvariantCulture),
            " dc=", currentDodgeAddedCharges.ToString(CultureInfo.InvariantCulture),
            " dark=", currentAddedDarkMaxStack.ToString(CultureInfo.InvariantCulture),
            " light=", currentAddedLightMaxStack.ToString(CultureInfo.InvariantCulture),
            " lvl=", currentAddedMaxLevel.ToString(CultureInfo.InvariantCulture),
            " gq=", currentAddedGemSlotQ.ToString(CultureInfo.InvariantCulture),
            " gw=", currentAddedGemSlotW.ToString(CultureInfo.InvariantCulture),
            " ge=", currentAddedGemSlotE.ToString(CultureInfo.InvariantCulture),
            " gr=", currentAddedGemSlotR.ToString(CultureInfo.InvariantCulture),
            " gi=", currentAddedGemSlotIdentity.ToString(CultureInfo.InvariantCulture));
    }
}

// （#1167）必須の字段を1つ（currentDodgeHaste）欠いた同じ形の型。本体更新で字段の名前が
// 替わったときに相当する。NativeChaosStatBonusTooltipCachePerf.TryPrepare は false を返し、
// 警告は1回だけ出て、この修正だけが外れる（fail-soft）。TryPrepare が失敗するので
// GetCustomTooltipDescription が呼ばれることは無い。
internal sealed class Se_Shrine_Chaos_StatBonus_MissingTooltipField
{
    public int currentAddedLightMaxStack;
    public int currentAddedDarkMaxStack;
    public int currentDodgeAddedCharges, currentAddedMaxLevel;
    public int currentAddedGemSlotQ, currentAddedGemSlotW, currentAddedGemSlotE,
        currentAddedGemSlotR, currentAddedGemSlotIdentity;
    public ChaosShrineStatBonus syncedRegularChaosBonus = new ChaosShrineStatBonus();
    public ChaosShrineStatBonus syncedCorruptedChaosBonus = new ChaosShrineStatBonus();

    public string GetCustomTooltipDescription() => string.Empty;
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

// 言語ごとのローカライズ辞書データの境界（実機の PerLanguageLocalizationData の役割）。
// 言語が替わると実機では別のインスタンスが渡るので、ここでも言語ごとに別インスタンスを
// 作り、文の先頭に言語名を付ける（どの言語で作られた文かが結果から分かる）。
internal sealed class DewLocalizationData
{
    public readonly string language;

    public DewLocalizationData(string language)
    {
        this.language = language;
    }

    public string GetUIValue(string key) => language + ":" + key;
}

internal static class DewLocalization
{
    // 実機の DewLocalization.data と同じ意味の静的プロパティ: 言語ごとの辞書データの stable な
    // 参照を返し、言語を切り替えると参照が替わる。NativeChaosStatBonusTooltipCachePerf はこの
    // 参照を入力の一部に数えるので、テストから差し替えられるように背後の欄を公開する。
    public static DewLocalizationData currentData = new DewLocalizationData("en");

    public static DewLocalizationData data => currentData;

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
