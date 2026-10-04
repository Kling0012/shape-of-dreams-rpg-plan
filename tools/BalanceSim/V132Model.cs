using System;
using SodRpg.Core.Game;

namespace BalanceSim;

/// <summary>
/// v1.32 星図の追加効果（巡る富・遠征の鍛錬）を測るための純粋なモデル（--mode v132stars）。
/// コアの定数・式（CurrencyStars / DreamDepth / Build.DamageTakenPerDelvePct / Content.EnhanceCost など）は
/// そのまま使い、本体の逆コンパイルで確認できた部分は同じ形に、確認できない部分は仮定として定数にしてある。
/// ゲームデータ・コアの値は一切書き換えない。
/// </summary>
public static class V132Model
{
    // ───── 撃破ゴールド（本体の形：GameManager.GetKillGoldAmount → Pickup_BaseGoldOrb.GrantGold）─────

    /// <summary>ゾーン index（0始まり）での撃破ゴールド倍率。本体 DewGameplayExperienceSettings の既定式
    /// killGoldMultiplierByZoneIndex "1 + 0.4*x" と globalGoldEconomyMultiplierByZoneIndex "1 + 0.20*x" の積。</summary>
    public static double ZoneGoldMultiplier(int zoneIndex) =>
        (1d + 0.4d * Math.Max(0, zoneIndex)) * (1d + 0.20d * Math.Max(0, zoneIndex));

    /// <summary>種別ごとの撃破ゴールドの基礎値（ゾーン0・ぶれなし・1人）。本体のアセット値（ges.killGold）は
    /// 逆コンパイルでは読めないため、商人価格 Economy.MerchantGoldBase(60+15*深度) と釣り合う代表的な値の仮定。</summary>
    public static int KillGoldBase(MonsterTier tier) => tier switch
    {
        MonsterTier.Lesser => 1,
        MonsterTier.Normal => 2,
        MonsterTier.MiniBoss => 10,
        _ => 25,
    };

    /// <summary>エリート（MiniBoss・Boss）撃破ゴールドに「黄金の嗅覚」が効く種類か。</summary>
    public static bool IsElite(MonsterTier tier) => tier == MonsterTier.MiniBoss || tier == MonsterTier.Boss;

    /// <summary>
    /// 本体の GameManager.GetKillGoldAmount と同じ丸め方の基礎撃破ゴールド（共有ドロップの額）。
    /// deviationUnit は [0,1) の乱数で、本体の killGoldDeviation = 0.1 の ±10% のぶれを引く。
    /// </summary>
    public static int BaseKillGold(MonsterTier tier, int zoneIndex, double deviationUnit, double roll) =>
        CurrencyStars.RandomRound(
            KillGoldBase(tier) * ZoneGoldMultiplier(zoneIndex) * (1d + KillGoldDeviation * (2d * deviationUnit - 1d)),
            roll);

    /// <summary>本体の killGoldDeviation（DewGameplayExperienceSettings の既定 0.1）。</summary>
    public const double KillGoldDeviation = 0.1;

    /// <summary>
    /// 各人への支給額。本体の Pickup_BaseGoldOrb.GrantGold と同じく、人数で割った額にプロファイルの倍率と
    /// その旅人の monsterKillGoldMultiplier（巡る富の +12% を含む）を掛けて確率丸めする（1人遠征を仮定）。
    /// </summary>
    public static int PlayerKillGold(int baseKillGold, float killGoldMultiplier, double roll) =>
        CurrencyStars.RandomRound(baseKillGold * Math.Max(0f, killGoldMultiplier), roll);

    /// <summary>
    /// 「黄金の嗅覚」の上乗せ。ホストの HostAuthority.GrantEliteKillGold と同じ式
    /// （CurrencyStars.EliteKillGoldBonus に1人・プロファイル倍率1・この旅人の倍率を渡して確率丸め）。
    /// </summary>
    public static int EliteKillGoldBonus(int baseKillGold, float killGoldMultiplier, int percent, double roll) =>
        CurrencyStars.RandomRound(
            CurrencyStars.EliteKillGoldBonus(baseKillGold, 1, 1f, killGoldMultiplier, percent), roll);

    // ───── 夢のダスト（自分で拾う分だけ）─────

    /// <summary>戦闘部屋あたりのダストの拾得回数（本体のダストの石・置かれたダスト束の想定）。</summary>
    public const int RoomDustPickups = 4;
    /// <summary>ダスト1回の拾得量（部屋）。本体の星「爽やかな朝露」（1ゾーンにつき+100）が1ゾーン5部屋なら
    /// 環境ダストは1ゾーン約100という規模感に合わせた仮定。</summary>
    public const int RoomDustPerPickup = 5;
    /// <summary>ボス撃破後のダストの拾得回数（ボスの魂・浄化の祭壇など）。</summary>
    public const int BossDustPickups = 4;
    /// <summary>ボス撃破後のダスト1回の拾得量。本体の星「破壊的検査」（ボス撃破で+40）と同規模の仮定。</summary>
    public const int BossDustPerPickup = 10;

    /// <summary>1回の拾得で手に入る量（星のぶん込み）。贈り物は対象外（1人遠征なので起こらない）。</summary>
    public static int PickupDust(int amount, Build build, bool delving, double roll) =>
        amount + CurrencyStars.DreamDustBonusForPickup(build, delving, amount, isGivenByOtherPlayer: false, roll);

    /// <summary>部屋あたり・ボスあたりの基礎ダスト量（星なし）。</summary>
    public static int RoomDust => RoomDustPickups * RoomDustPerPickup;
    public static int BossDust => BossDustPickups * BossDustPerPickup;

    // ───── 遠征の鍛錬（RunGrowth）の1部屋ぶんの模型 ─────

    /// <summary>戦闘部屋1つで失うHP（最大HPに対する%、夢の深度0・潜行0のとき）。「被弾して戦う」旅人の代表的な値の仮定。</summary>
    public const double DamageTakenPerRoomPct = 25;
    /// <summary>戦闘部屋1つで障壁が吸収する量（最大HPに対する%、同上）。障壁型の旅人の代表的な値の仮定。</summary>
    public const double ShieldAbsorbedPerRoomPct = 20;
    /// <summary>ボス戦で失うHP・障壁が吸収する量（部屋の2倍を仮定）。</summary>
    public const double BossRoomFactor = 2;
    /// <summary>戦闘部屋1つでのパリィの成功回数（St_R_Parry は使用ごとに成功すれば Se_R_Parry_End を作る）。</summary>
    public const double ParriesPerRoom = 2;
    /// <summary>ボス戦でのパリィの成功回数。</summary>
    public const double ParriesPerBoss = 6;
    /// <summary>撃破のうち基本攻撃でとどめを刺す割合（雑魚は主に基本攻撃で倒す仮定）。</summary>
    public const double BasicAttackKillShare = 0.5;
    /// <summary>基本攻撃の会心率（空殻の2つのアイデンティティは移動後に次の基本攻撃を会心にするため、やや高め）。</summary>
    public const double CritChance = 0.25;
    /// <summary>撃破のうち「会心の基本攻撃でとどめ」の割合。</summary>
    public static double CritBasicKillShare => BasicAttackKillShare * CritChance;

    /// <summary>夢の深度と潜行（熱度）で補正した、1戦闘部屋で失うHP（最大HPに対する%）。
    /// 深度は本体のモンスターの攻撃力倍率（DreamDepth.DamageMultiplier）、潜行はコアの被ダメージ増加
    /// （Build.DamageTakenPerDelvePct×熱度）をそのまま掛ける。</summary>
    public static double DamageTakenPct(double basePct, int dreamDepth, int heat) =>
        basePct * DreamDepth.DamageMultiplier(dreamDepth) * (1d + Build.DamageTakenPerDelvePct * Math.Max(0, heat) / 100d);

    /// <summary>同上（障壁が吸収した量）。吸収量も受けたダメージに比例すると仮定する。</summary>
    public static double ShieldAbsorbedPct(double basePct, int dreamDepth, int heat) =>
        DamageTakenPct(basePct, dreamDepth, heat);

    /// <summary>夢の深度 d のときの1ゾーンの戦闘部屋数。コアの DreamDepth.ExtraZoneNodes（深度ごとに+2部屋）。</summary>
    public static int RoomsPerZone(int rooms, int dreamDepth) => rooms + DreamDepth.ExtraZoneNodes(dreamDepth);

    /// <summary>ゾーンごとのスタック数から、上限に初めて届いたゾーン（1始まり）。届かないときは 0。</summary>
    public static int FirstZoneAtCap(int[] stacksAfterZone, int cap)
    {
        if (stacksAfterZone == null) return 0;
        for (int i = 0; i < stacksAfterZone.Length; i++)
            if (stacksAfterZone[i] >= cap) return i + 1;
        return 0;
    }

    /// <summary>1戦闘部屋ぶんの出来事（種別・深度・熱度から決まる決定的な平均値）。既定の模型の値。</summary>
    public static double RoomUnits(RunGrowthTrigger trigger, int depth, int heat, double killsPerRoom) => trigger switch
    {
        RunGrowthTrigger.DamageTakenMaxHpPct => DamageTakenPct(DamageTakenPerRoomPct, depth, heat),
        RunGrowthTrigger.ShieldAbsorbedMaxHpPct => ShieldAbsorbedPct(ShieldAbsorbedPerRoomPct, depth, heat),
        RunGrowthTrigger.ParrySuccess => ParriesPerRoom,
        _ => killsPerRoom * CritBasicKillShare,
    };

    /// <summary>ゾーン末尾のボス1体ぶんの出来事。会心の基本攻撃でのとどめはボスでは数えない。</summary>
    public static double BossUnits(RunGrowthTrigger trigger, int depth, int heat) => trigger switch
    {
        RunGrowthTrigger.DamageTakenMaxHpPct => DamageTakenPct(DamageTakenPerRoomPct * BossRoomFactor, depth, heat),
        RunGrowthTrigger.ShieldAbsorbedMaxHpPct => ShieldAbsorbedPct(ShieldAbsorbedPerRoomPct * BossRoomFactor, depth, heat),
        RunGrowthTrigger.ParrySuccess => ParriesPerBoss,
        _ => 0,
    };

    /// <summary>方針 policy のとき、ゾーン zone の戦闘部屋での熱度（潜行深度）。Simulation の入口の処理と同じ遷移。</summary>
    public static int HeatDuringZone(string policy, int zone)
    {
        int secureHeat = policy switch { "delve1" => 1, "greedy" => 3, _ => 0 };
        int heat = 0;
        for (int z = 2; z <= zone; z++)
            heat = heat >= secureHeat ? 0 : heat + 1;
        return heat;
    }


    // ───── 下流（欠片 → 強化）─────

    /// <summary>欠片 shards で買える強化の回数（+0 → +1 を1回目として Content.EnhanceCost の累積で数える）。</summary>
    public static int EnhanceStepsAffordable(long shards)
    {
        int steps = 0;
        long spent = 0;
        while (true)
        {
            long cost = Content.EnhanceCost(steps);
            if (cost == int.MaxValue || spent + cost > shards) return steps;
            spent += cost;
            steps++;
        }
    }

    /// <summary>変化率（%）。before が 0 以下のときは 0（分母なし）。</summary>
    public static double PercentChange(double before, double after)
        => before > 0 ? (after - before) / before * 100d : 0d;
}
