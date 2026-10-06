using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>Json.Write appends unescaped runs in one call; every escape position must still round-trip.</summary>
    public class JsonStringEscapeTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("plain text 夢の星図")]
        [InlineData("\"")]
        [InlineData("\\")]
        [InlineData("\n")]
        [InlineData("\"leading and trailing\"")]
        [InlineData("a\"b\\c\nd\te\u0001f")]
        [InlineData("\\\\\"\"\n\n")]
        [InlineData("{\"snapshot\":\"{\\\"nested\\\":1}\"}")]
        public void Strings_round_trip_through_write_and_parse(string value)
        {
            string json = Json.Write(value);
            Assert.Equal(value, Json.Parse(json));
        }

        [Fact]
        public void Plain_strings_are_written_verbatim_between_quotes()
        {
            Assert.Equal("\"abc 夢\"", Json.Write("abc 夢"));
            Assert.Equal("\"a\\\"b\\\\c\\u000a\"", Json.Write("a\"b\\c\n"));
        }
    }
}
