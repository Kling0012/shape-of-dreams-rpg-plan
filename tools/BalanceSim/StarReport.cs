using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal static class StarReport
{
    public static string Render(Options o, StarSimulation sim, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        text.AppendLine("# Dreamforge RPG 星振りシミュレーション（v1.31）");
        text.AppendLine();
        text.AppendLine("## 条件と仮定");
        text.AppendLine();
        text.AppendLine($"- 新規プロフィールに星経験 StarProgression.TotalXpForPoints({StarProgression.MaxPoints}) を与え（ポイント {StarProgression.MaxPoints}、図鑑ボーナスなし）、節目 {string.Join("/", sim.Checkpoints.Select(Culture))} まで `Rules.AddTalentRank` / `Rules.SetKeystone` で実際に購入します。計算はすべて `Build.Compute` の実経路です。");
        text.AppendLine($"- ツリーは実行時に登録されているもの（`HeroSigils.TreeFor`）をそのまま使い、authored 星群の追加・差し替えにコードの変更は要りません。対象は HeroStarRoutes に現れる旅人 {sim.Heroes.Count} 人（{string.Join("・", sim.Heroes.Select(HeroName))}）。");
        text.AppendLine($"- 熟練度は最初から満タン（撃破数 1,000,000 → Mastery.Level=10、核の前提 HeroSigils.KeystoneMastery={HeroSigils.KeystoneMastery} を充足）。星の払い戻しはせず、節目をまたいで同じ割り振りを続けます。");
        text.AppendLine($"- 夢のレベルは入力仮定 --dream-level={o.DreamLevel}（上限 {Content.MaxDreamLevel}）。{StarProgression.MaxPoints}ポイント（星経験 {StarProgression.TotalXpForPoints(StarProgression.MaxPoints).ToString("N0", CultureInfo.InvariantCulture)}）に要する遊戯量を考えると、終盤の節目では夢レベルが天井に張り付く想定です。");
        text.AppendLine("- 戦略は3種。能力値優先＝プロキシ増分/ポイントが最大の購入を毎回選ぶ。記憶特化＝星数最大の星群（authored 星群がなければ記憶ルート）を登録順で完成させ、閉じている間は核以外の最安のつなぎ星を買い、完成後は能力値優先。核先行＝核の前提（6ランク＋到達）を最安で満たして `Rules.SetKeystone`、残りは能力値優先。");
        text.AppendLine("- 力の代理値（プロキシ）は本体が悪夢化抽選の強さに使う式（攻撃力%か魔力%の大きい方＋最大HP%の半分）をクランプ前のまま使います。戦闘の出力ではありません。仕掛け・連携・固有効果は条件付きのまま Host へ渡されるため、件数（効果の幅）のみ別掲します。");
        text.AppendLine("- 夢の圧は `DreamPressure.ForPlayer(夢Lv, 使用ポイント).WithRunModifiers(深度)` の実式、深度の効果は `DreamDepth` / `Nightmares` の実式です。ゲームの定数は一切変更していません。");
        text.AppendLine("- 乱数を使わない決定的なシミュレーションです（--seed は使いません）。");
        if (o.StarMaxPoints < StarProgression.MaxPoints)
            text.AppendLine($"- 軽量実行（--star-max-points={o.StarMaxPoints}）：購入手順は通常実行と同じですが、この節目で停止します。以降の節目は未測定です。");
        text.AppendLine($"- シミュレーション実行時間（レポート整形・ファイル書込・ビルドを除く）：{elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)} 秒。");
        text.AppendLine();

        text.AppendLine("## 実行時の星図");
        text.AppendLine();
        text.AppendLine("| 旅人 | ツリーの星 | 星群 | 核 | 出自 |");
        text.AppendLine("| --- | ---: | ---: | ---: | --- |");
        foreach (var group in sim.Results.GroupBy(r => r.HeroKey, StringComparer.Ordinal))
        {
            var first = group.First();
            text.AppendLine($"| {HeroName(first.HeroKey)} | {first.TreeStars} | {first.Clusters} | {first.Keystones} | " +
                (first.AuthoredTree ? "authored 登録" : "内蔵ベースライン") + " |");
        }
        text.AppendLine();

        foreach (var strategy in StarStrategyNames.All)
        {
            text.AppendLine($"## 成長：{StarStrategyNames.Ja(strategy)}（{StarStrategyNames.Key(strategy)}）");
            text.AppendLine();
            var rows = sim.Results.Where(r => r.Strategy == strategy).ToList();
            if (strategy == StarStrategy.MemoryFocus)
            {
                text.AppendLine("記憶特化の対象（星数最大の星群、なければ記憶ルート）。");
                text.AppendLine();
                text.AppendLine("| 旅人 | 対象 | 種類 | 星数 |");
                text.AppendLine("| --- | --- | --- | ---: |");
                foreach (var r in rows)
                {
                    var focus = r.Focus;
                    text.AppendLine(focus == null
                        ? $"| {HeroName(r.HeroKey)} | — | — | 0 |"
                        : $"| {HeroName(r.HeroKey)} | {focus.Key}（{focus.Memory}） | {(focus.IsAuthoredCluster ? "星群" : "ルート")} | {focus.Stars} |");
                }
                text.AppendLine();
            }
            text.AppendLine("| 旅人 | 節目 | 使用 | 残 | 攻% | 魔% | HP% | 防御 | 機急 | 攻速% | プロキシ | 仕掛 | 連携 | 固有 | 核 |");
            text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
            foreach (var r in rows)
                foreach (var c in r.Checkpoints)
                    text.AppendLine($"| {HeroName(r.HeroKey)} | {c.Target} | {c.Spent} | {c.Unspent} | {c.AttackPct} | {c.PowerPct} | {c.MaxHealthPct} | {c.Armor} | {c.Haste} | {c.AttackSpeedPct} | {Number(c.Proxy)} | {c.Gimmicks} | {c.Links} | {c.Powers} | {(c.Keystone.Length == 0 ? "—" : c.Keystone)} |");
            text.AppendLine();
            text.AppendLine("プロキシの旅人平均（節目ごと）。");
            text.AppendLine();
            text.AppendLine("| 節目 | 平均プロキシ | 前節目比 | 平均残ポイント |");
            text.AppendLine("| --- | ---: | ---: | ---: |");
            double previous = 0;
            foreach (int target in sim.Checkpoints)
            {
                double average = rows.Select(r => r.Checkpoints.First(c => c.Target == target).Proxy).Average();
                double unspent = rows.Select(r => r.Checkpoints.First(c => c.Target == target).Unspent).Average();
                text.AppendLine($"| {target} | {Number(average)} | {(target == 0 ? "—" : Plus(average - previous))} | {Number(unspent)} |");
                previous = average;
            }
            text.AppendLine();
        }

        text.AppendLine($"## 夢の圧（深度0〜{DreamDepth.Maximum}、夢Lv={o.DreamLevel}）");
        text.AppendLine();
        text.AppendLine($"`DreamPressure.ForPlayer` の実式。星項は {DreamPressure.HealthPerStarPoint.ToString("G", CultureInfo.InvariantCulture)}×使用ポイント（被ダメージは {DreamPressure.DamagePerStarPoint.ToString("G", CultureInfo.InvariantCulture)}×）、レベル項は夢Lv{DreamPressure.FreeDreamLevels}を超えた分 ×{DreamPressure.HealthPerLevel.ToString("G", CultureInfo.InvariantCulture)}（同 {DreamPressure.DamagePerLevel.ToString("G", CultureInfo.InvariantCulture)}）、深度項は DreamDepth.HealthMultiplier / DamageMultiplier。");
        text.AppendLine();
        text.AppendLine("敵HP倍率（深度ごと）。");
        text.AppendLine();
        string depthHeaders = string.Concat(Enumerable.Range(0, DreamDepth.Maximum + 1).Select(d => $" 深度{d} |"));
        string depthSeparators = string.Concat(Enumerable.Repeat(" ---: |", DreamDepth.Maximum + 1));
        text.AppendLine("| 節目 | 必要星XP | 換算確保数 |" + depthHeaders);
        text.AppendLine("| --- | ---: | ---: |" + depthSeparators);
        foreach (int points in sim.Checkpoints)
        {
            int xp = StarProgression.TotalXpForPoints(points);
            string secures = StarProgression.SecureXp == 0 ? "—" : Culture(xp / StarProgression.SecureXp);
            var row = new StringBuilder($"| {points} | {xp} | {secures} ");
            for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
                row.Append($"| {Number(DreamPressure.ForPlayer(o.DreamLevel, points).WithRunModifiers(depth).HealthMultiplier)} ");
            row.Append("|");
            text.AppendLine(row.ToString());
        }
        text.AppendLine();
        text.AppendLine("敵与ダメージ倍率（深度ごと）。");
        text.AppendLine();
        text.AppendLine("| 節目 |" + depthHeaders);
        text.AppendLine("| --- |" + depthSeparators);
        foreach (int points in sim.Checkpoints)
        {
            var row = new StringBuilder($"| {points} ");
            for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
                row.Append($"| {Number(DreamPressure.ForPlayer(o.DreamLevel, points).WithRunModifiers(depth).DamageMultiplier)} ");
            row.Append("|");
            text.AppendLine(row.ToString());
        }
        text.AppendLine();
        text.AppendLine("項目の分解（深度0）。星項・レベル項をかけ合わせる前の各自的増分。");
        text.AppendLine();
        text.AppendLine("| 節目 | 星項HP | 星項被DMG | レベル項HP | レベル項被DMG | 合計HP倍率 | 合計被DMG倍率 |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (int points in sim.Checkpoints)
        {
            var p = DreamPressure.ForPlayer(o.DreamLevel, points);
            double levelsOverFree = Math.Max(0, p.AverageDreamLevel - DreamPressure.FreeDreamLevels);
            text.AppendLine($"| {points} | {Plus(DreamPressure.HealthPerStarPoint * p.AverageSpentStarPoints)} | {Plus(DreamPressure.DamagePerStarPoint * p.AverageSpentStarPoints)} | {Plus(DreamPressure.HealthPerLevel * levelsOverFree)} | {Plus(DreamPressure.DamagePerLevel * levelsOverFree)} | {Number(p.HealthMultiplier)} | {Number(p.DamageMultiplier)} |");
        }
        text.AppendLine();
        text.AppendLine("夢レベルの感度（深度0、節目ごとの敵HP倍率 / 被ダメージ倍率）。");
        text.AppendLine();
        text.AppendLine("| 節目 | 夢Lv10 HP / 被DMG | 夢Lv20 HP / 被DMG | 夢Lv30 HP / 被DMG |");
        text.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (int points in sim.Checkpoints)
        {
            string Cell(int level)
            {
                var p = DreamPressure.ForPlayer(level, points);
                return $"{Number(p.HealthMultiplier)} / {Number(p.DamageMultiplier)}";
            }
            text.AppendLine($"| {points} | {Cell(10)} | {Cell(20)} | {Cell(30)} |");
        }
        text.AppendLine();

        text.AppendLine("## 深度の圧と悪夢化（実式）");
        text.AppendLine();
        text.AppendLine("`DreamDepth` の倍率と `Nightmares.DepthBonus` / `Nightmares.Chance` の実式を列挙します。");
        text.AppendLine();
        text.AppendLine("| 深度 | 敵HP倍率 | 敵与DMG倍率 | 通常敵追加HP% | 通常敵追加攻% | 追加防御 | ボス追加HP% | レア運 | 覚醒倍率 | 星XP倍率 | 追加部屋 | Lesser悪夢率 | Normal悪夢率 | MiniBoss悪夢率 | 接辞数 |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        for (int depth = 0; depth <= DreamDepth.Maximum; depth++)
        {
            var bonus = Nightmares.DepthBonus(MonsterTier.Normal, depth);
            int hp = Line(bonus, Stat.MaxHealthPct);
            int attack = Line(bonus, Stat.AttackPct);
            int armor = Line(bonus, Stat.Armor);
            var boss = Nightmares.DepthBonus(MonsterTier.Boss, depth);
            int bossHp = Line(boss, Stat.MaxHealthPct);
            int affixes = Nightmares.AffixCount(depth);
            text.AppendLine($"| {depth} | {Number(DreamDepth.HealthMultiplier(depth))} | {Number(DreamDepth.DamageMultiplier(depth))} | {Plus(hp)} | {Plus(attack)} | {Plus(armor)} | {Plus(bossHp)} | {Number(DreamDepth.RarityLuck(depth))} | {Number(DreamDepth.AwakeningMultiplier(depth))} | {Number(DreamDepth.StarXpMultiplier(depth))} | {DreamDepth.ExtraZoneNodes(depth)} | {Percent(Nightmares.Chance(MonsterTier.Lesser, depth))} | {Percent(Nightmares.Chance(MonsterTier.Normal, depth))} | {Percent(Nightmares.Chance(MonsterTier.MiniBoss, depth))} | {affixes} |");
        }
        text.AppendLine();
        var final = sim.Results.Select(r => r.Checkpoints.Last()).ToList();
        text.AppendLine($"{o.StarMaxPoints}ポイントの節目での `Nightmares.GearChanceMult`（悪夢化率の装備強さ倍率）は全旅人・全戦略で {Number(final.Min(c => c.GearChanceMult))}〜{Number(final.Max(c => c.GearChanceMult))}。");
        text.AppendLine();

        text.AppendLine("## コアAPIの不足と測定の限界");
        text.AppendLine();
        text.AppendLine("- 戦闘の出力・勝率・時間あたりの発動率の公開シミュレーションAPIはありません。プロキシは Build の能力値だけから出しており、仕掛け・連携・固有効果・核の実際の価値は件数でしか見えません。");
        text.AppendLine("- 選択の星は両選択肢を評価して良い方を買いますが、実際のプレイヤーの好み・装備との相互作用はモデル外です。");
        text.AppendLine("- 夢レベルと星ポイントの対応は本体のプレイ速度に依存するため、ここでは夢レベルを入力仮定として固定し、感度表で補います。");
        text.AppendLine("- 悪夢化の抽選・接頭効果の戦闘的影響は行わず、`Nightmares.Chance` / `DepthBonus` の式と装備倍率の範囲のみ示します。");
        text.AppendLine("- authored 星群の登録状態は実行時のものです。この表の「出自」列で確認できます。");
        return text.ToString();
    }

    private static string HeroName(string heroKey) => Links.Name(heroKey).Ja;
    private static string Culture(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Plus(double value) => (value >= 0 ? "+" : "") + value.ToString("0.000", CultureInfo.InvariantCulture);
    private static string Percent(double value) => (100 * value).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    private static int Line(List<StatLine> lines, Stat stat)
    {
        foreach (var line in lines)
            if (line.Stat == stat) return line.Value;
        return 0;
    }
}

