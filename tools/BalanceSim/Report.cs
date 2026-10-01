using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal static class Report
{
    private static readonly string[] MetricNames =
    ["コモン発見", "アンコモン発見", "レア発見", "エピック発見", "伝説発見", "確保遺物",
     "欠片純増減", "調律石純増減", "夢のレベル", "装備平均レア度", "装備平均強化", "装着枠数", "達成依頼", "全滅（0/1）"];

    public static string Render(Options o, Simulation sim, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        text.AppendLine("# Dreamforge RPG バランスシミュレーション");
        text.AppendLine();
        text.AppendLine("## 条件と仮定");
        text.AppendLine();
        text.AppendLine($"- 新規プロフィールから {o.Runs} 回遠征 × {o.Players} 人。seed={o.Seed}、policy={o.Policy}。");
        text.AppendLine($"- 遠征は {o.Zones} ゾーン、各 {o.Rooms} 戦闘部屋。部屋ごとに Lesser {o.Lesser} 体、Normal {o.Normal} 体、MiniBoss は確率 {Number(o.MiniBoss)} で1体。各ゾーン末尾に Boss {o.Bosses} 体。これは本体の実測ではなく仮定です。");
        text.AppendLine($"- 2ゾーン目以降の入口で確保地点。secure は毎回確保、delve1 は深度1、greedy は深度3に達した次の入口で確保。それ未満なら契約なしで潜行します。踏破時は必ず確保します。");
        text.AppendLine($"- 全滅率は遠征ごとに {Percent(o.Wipe)}。全滅するゾーンと、そのゾーン内の戦闘部屋を一様に選び、その部屋の突破後・ボス前に全滅します。以降の撃破はありません。装備や深度で全滅率は変えません。");
        text.AppendLine($"- アイテムレベルはゾーン z で min({Content.MaxItemLevel}, {o.ItemLevel} + (z−1)×{o.ItemLevelPerZone})。周回による上昇は仮定しません。");
        text.AppendLine($"- 撃破・部屋突破・確保の依頼は Rules が実際の条件で判定します。本体行動の6種（Chaos、商人、強化、合成、分解、ハンター）は依頼ごとに確率 {Percent(o.Bounty)} で達成を予定し、遠征全体から一様に選んだ戦闘部屋の突破後に必要回数の OnGameAction を呼びます。予定部屋までに全滅した場合は未達です。悪夢化・契約の依頼は未達です。");
        text.AppendLine("- 今日の夢・契約・悪夢化・Limbo・確保地点の出来事・工房購入・製作・合成・再調律・初期装備配布は使いません。遺失物回収と上限超過時の自動分解はコアの規則に従います。");
        text.AppendLine($"- 遠征の合間だけ自動装備します。レア度を優先し、同じなら ItemLevel が高い品を選び、同値は現在の装備を維持します。装備以外の弱い品から分解し、保管庫を最大 {Content.StashCapacity - Content.SatchelCapacity} 個に整理して次回の鞄1つ分を空けます。その後、装備の低い強化値から欠片の許す限り強化します。セット部位は特別に保護しません。");
        text.AppendLine("- 遺物の発見数は新規ドロップのみ（回収品を除く）。確保数には回収品を含み、上限超過で自動分解された品を含みません。素材の純増減は遠征前から装備整理・強化後までの差です。");
        text.AppendLine("- レア度の数値はコモン=0、アンコモン=1、レア=2、エピック=3、伝説=4。装備平均は装着している枠だけで計算し、未装着なら0とします。装着枠数を併記します。");
        text.AppendLine("- レア・エピックの節目はそれ以上のドロップ発見時点。セット完成は同じセットの異なる3部位の同時所持（装備は保管庫への参照なので二重計上しない）。遺失物・未確保品は所持判定に含めません。全+3は空き枠のない3枠が+3以上です。");
        text.AppendLine("- 百分位は昇順標本の位置 (n−1)×p を線形補間。節目の中央値・P10・P90は到達者のみで計算します。未到達者を除くため、未到達割合が高い節目の値は全員の到達時間を表しません。");
        text.AppendLine($"- シミュレーション実行時間（レポート整形・ファイル書込・ビルドを除く）：{elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)} 秒。");
        text.AppendLine();
        text.AppendLine("## 遠征ごとの平均（1人あたり）");
        text.AppendLine();
        text.AppendLine("| 遠征 | コモン | アンコモン | レア | エピック | 伝説 | 確保数 | 欠片増減 | 調律石増減 | 夢Lv | 装備レア度 | 装備強化 | 装着枠数 | 達成依頼 | 全滅率 |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        for (int run = 0; run < o.Runs; run++)
        {
            text.Append($"| {run + 1} ");
            for (int metric = 0; metric < (int)Metric.Count; metric++)
            {
                double sum = 0;
                for (int player = 0; player < o.Players; player++) sum += sim.Samples[run, metric, player];
                double average = sum / o.Players;
                text.Append("| ").Append(metric == (int)Metric.Wiped ? Percent(average) : Number(average)).Append(' ');
            }
            text.AppendLine("|");
        }
        text.AppendLine();
        text.AppendLine("## 節目までの遠征回数（到達者のみ）");
        text.AppendLine();
        text.AppendLine("| 節目 | 到達人数 | 中央値 | P10 | P90 | 未到達割合 |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        var values = new double[o.Players];
        for (int milestone = 0; milestone < Simulation.MilestoneNames.Length; milestone++)
        {
            int reached = 0;
            for (int player = 0; player < o.Players; player++)
                if (sim.Milestones[milestone, player] > 0) values[reached++] = sim.Milestones[milestone, player];
            Array.Sort(values, 0, reached);
            text.AppendLine($"| {Simulation.MilestoneNames[milestone]} | {reached} | {Quantile(values, reached, 0.5)} | {Quantile(values, reached, 0.1)} | {Quantile(values, reached, 0.9)} | {Percent(1.0 - (double)reached / o.Players)} |");
        }
        text.AppendLine();
        text.AppendLine("## 保管庫・鞄の上限");
        text.AppendLine();
        text.AppendLine("満杯到達は「上限未満から上限へ達した操作」1回を数えます。満杯のまま追加しても再計上しません。自動分解は上限超過でコアが欠片にした遺物数で、意図的な保管庫整理の分解は含めません。");
        text.AppendLine();
        text.AppendLine("| 指標 | 全員合計 | 1人平均 | 中央値 | P10 | P90 |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        string[] capacityNames = ["鞄の満杯到達回数", "鞄の上限超過・自動分解数", "保管庫の満杯到達回数", "保管庫の上限超過・自動分解数"];
        for (int metric = 0; metric < capacityNames.Length; metric++)
        {
            long total = 0;
            for (int player = 0; player < o.Players; player++)
            {
                long value = sim.Capacity[metric, player];
                total += value;
                values[player] = value;
            }
            Array.Sort(values);
            text.AppendLine($"| {capacityNames[metric]} | {total} | {Number((double)total / o.Players)} | {Quantile(values, o.Players, 0.5)} | {Quantile(values, o.Players, 0.1)} | {Quantile(values, o.Players, 0.9)} |");
        }
        text.AppendLine();
        text.AppendLine("## 遠征ごとの分布（全プレイヤー）");
        text.AppendLine();
        text.AppendLine("| 遠征 | 指標 | 平均 | 中央値 | P10 | P90 |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
        for (int run = 0; run < o.Runs; run++)
            for (int metric = 0; metric < (int)Metric.Count; metric++)
            {
                double total = 0;
                for (int player = 0; player < o.Players; player++)
                {
                    values[player] = sim.Samples[run, metric, player];
                    total += values[player];
                }
                Array.Sort(values);
                text.AppendLine($"| {run + 1} | {MetricNames[metric]} | {Number(total / o.Players)} | {Quantile(values, o.Players, 0.5)} | {Quantile(values, o.Players, 0.1)} | {Quantile(values, o.Players, 0.9)} |");
            }
        text.AppendLine();
        text.AppendLine("## コアAPIの不足と測定の限界");
        text.AppendLine();
        text.AppendLine("- ゲーム本体の部屋構成・敵数・アイテムレベルのゾーン対応・戦闘勝敗の公開シミュレーションAPIはありません。これらは上記の仮定と引数で指定しました。");
        text.AppendLine("- 本体行動の発生確率はコアだけでは分かりません。ただし公開 OnGameAction があるため、依頼を直接書き換えずに仮定した行動を通せます。依頼報酬を再実装する必要はありません。");
        text.AppendLine("- 上限到達・自動分解数の構造化カウンターはありません。鞄への追加は公開イベントの Kind/Rarity と直前の個数、確保は直前の保管庫・鞄の個数から計数しています。通知文言には依存しません。");
        return text.ToString();
    }

    private static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Percent(double value) => (100 * value).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Quantile(double[] sorted, int count, double p)
    {
        if (count == 0) return "—";
        double position = (count - 1) * p;
        int lower = (int)position;
        int upper = Math.Min(lower + 1, count - 1);
        return Number(sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower));
    }
}
