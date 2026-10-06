using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Issue #149 段階8：Power調整表と実定義の一致、無調整移行での指紋不変を確認する。</summary>
    public class PowersBalanceTableTests
    {
        private static readonly Lazy<Dictionary<string, object>> Constants = new Lazy<Dictionary<string, object>>(() =>
            typeof(PowersBalance).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly)
                .ToDictionary(f => f.Name, f => f.GetRawConstantValue()));

        [Fact]
        public void Power_tuning_constants_match_the_canonical_table()
        {
            var root = EquipmentBalanceInputs.Raw("tools/balance/powers.json");
            int checkedValues = 0;
            foreach (var section in root.EnumerateObject())
            {
                if (section.Name == "schemaVersion") continue;
                foreach (var row in section.Value.EnumerateObject())
                {
                    Assert.Contains(row.Name, Constants.Value.Keys);
                    var actual = Constants.Value[row.Name];
                    var value = row.Value;
                    if (actual is int)
                    {
                        Assert.True(value.ValueKind == JsonValueKind.Number && Math.Abs(AsDecimal(value) - Math.Round(AsDecimal(value))) == 0m,
                            $"{section.Name}.{row.Name}: expected an integer table value for an int constant");
                        Assert.Equal((int)AsDecimal(value), (int)actual);
                    }
                    else
                    {
                        decimal expected = AsDecimal(value);
                        if (actual is float) Assert.Equal((float)expected, (float)actual);
                        else Assert.Equal((double)expected, (double)actual, 15);
                    }
                    checkedValues++;
                }
            }
            // 表に無い定数・表にしかない行は生成段階で拒否済み。ここでは公開定数の全数も確認する。
            Assert.Equal(Constants.Value.Count, checkedValues);
        }

        [Fact]
        public void Unchanged_power_table_adds_no_content_fingerprint_records()
        {
            // 無調整移行：採用値が移行前と同じなので、指紋入力は空のままであること。
            Assert.Empty(PowersBalance.ContentFingerprintRecords);
            Assert.All(typeof(PowerRuntime).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(float)).Select(f => f.Name),
                name => Assert.Contains(name, Constants.Value.Keys));
        }

        private static decimal AsDecimal(JsonElement value) =>
            decimal.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
