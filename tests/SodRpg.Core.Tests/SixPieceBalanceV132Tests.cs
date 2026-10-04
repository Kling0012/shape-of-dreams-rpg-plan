using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BalanceSim;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// v1.32：48組すべての6つ装着の効果が、合意した帯（中央値の0.5〜1.5倍）に収まっていることを
    /// BalanceSim と同じ補助（tools/BalanceSim/SetBalance.cs）で確かめる。固定シードの決定的な
    /// 計測なので、Content が変わらなければ常に同じ結果になる。
    /// </summary>
    [Collection("SixPieceSets")]
    public class SixPieceBalanceV132Tests
    {
        [Fact]
        public void All_48_sets_are_measured_against_the_shared_baseline()
        {
            var results = SetBalance.MeasureAll();
            Assert.Equal(48, results.Count);
            Assert.Equal(Content.Sets.Count, results.Count);
            foreach (var set in Content.Sets)
                Assert.Contains(results, r => r.Id == set.Id);
            // 基準は全組で同一（最良代替品6枠の代理値）。
            Assert.All(results, r => Assert.Equal(results[0].Baseline, r.Baseline));
        }

        [Fact]
        public void Measurement_is_deterministic()
        {
            var first = SetBalance.MeasureAll();
            var second = SetBalance.MeasureAll();
            Assert.Equal(first.Select(r => r.Id), second.Select(r => r.Id));
            Assert.Equal(
                first.Select(r => r.SixBonusGain.ToString("R", CultureInfo.InvariantCulture)),
                second.Select(r => r.SixBonusGain.ToString("R", CultureInfo.InvariantCulture)));
        }

        [Fact]
        public void Every_six_piece_bonus_stays_within_the_agreed_band()
        {
            var results = SetBalance.MeasureAll();
            double median = SetBalance.MedianSixBonusGain(results);
            Assert.True(median > 0, "6点ボーナス利得の中央値が正であること");

            var outside = results
                .Where(r => SetBalance.Verdict(r.SixBonusGain, median) != "ok")
                .Select(r => $"{r.Id} {r.SixBonusGain.ToString("0.000", CultureInfo.InvariantCulture)}"
                    + $"（中央値 {median.ToString("0.000", CultureInfo.InvariantCulture)} の"
                    + $" {(r.SixBonusGain / median).ToString("0.00", CultureInfo.InvariantCulture)}倍）")
                .ToList();
            Assert.True(outside.Count == 0,
                "6つ装着の効果が帯の外にあるセット: " + string.Join("、", outside));
        }
    }
}
