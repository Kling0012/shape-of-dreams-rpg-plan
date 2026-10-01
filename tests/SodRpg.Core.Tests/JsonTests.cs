using System.Collections.Generic;
using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class JsonTests
    {
        [Fact]
        public void RoundTrip_PreservesKeyOrderAndEscapes()
        {
            var obj = new JsonObject()
                .Add("z", "a\"b\\c\n\t\u0001日本語")
                .Add("a", 42L)
                .Add("list", new List<object> { 1L, "x", null, true })
                .Add("nested", new JsonObject().Add("k", -7L));

            string text = Json.Write(obj);
            string again = Json.Write(Json.Parse(text));

            Assert.Equal(text, again);
            Assert.StartsWith("{\"z\":", text);
        }

        [Theory]
        [InlineData("{\"a\":1,\"a\":2}")]      // キー重複
        [InlineData("{\"a\":1.5}")]            // 小数
        [InlineData("{\"a\":1e3}")]            // 指数
        [InlineData("{\"a\":1} trailing")]     // 末尾の余分
        [InlineData("{\"a\":")]                // 途中で切れた
        [InlineData("{\"a\":\"abc")]           // 文字列が閉じない
        [InlineData("[1,2,")]
        [InlineData("")]
        [InlineData("{\"a\":99999999999999999999}")] // long範囲外
        public void Parse_RejectsMalformedInput(string text)
        {
            Assert.Throws<LedgerFormatException>(() => Json.Parse(text));
        }

        [Fact]
        public void Parse_RejectsTooDeepNesting()
        {
            string deep = new string('[', 200) + new string(']', 200);
            Assert.Throws<LedgerFormatException>(() => Json.Parse(deep));
        }
    }
}
