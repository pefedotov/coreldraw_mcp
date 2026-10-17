using System;
using System.Globalization;
using Xunit;
using CorelMcp.Json;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// Exercises the hand-written JSON layer. This is the safety net for the custom parser:
    /// the add-on ships no third-party JSON assembly, so correctness here is on us.
    /// </summary>
    public class JsonValueTests
    {
        // ---------- parsing ----------

        [Fact]
        public void ParsesNestedStructures()
        {
            var json = JsonObject.Parse(
                "{\"a\":{\"b\":[1,2,{\"c\":\"x\"}]},\"d\":true,\"e\":null}");

            Assert.Equal(JsonType.Object, json.Type);
            Assert.Equal(3, json.Count);
            Assert.Equal(JsonType.Array, json["a"]["b"].Type);
            Assert.Equal(3, json["a"]["b"].Count);
            Assert.Equal("x", json["a"]["b"][2]["c"].AsString());
            Assert.True(json["d"].Value<bool>());
            Assert.Equal(JsonType.Null, json["e"].Type);
        }

        [Fact]
        public void ParsesEmptyContainersAndWhitespace()
        {
            Assert.Equal(0, JsonObject.Parse("{}").Count);
            Assert.Equal(0, JsonObject.Parse("  {  }  ").Count);
            Assert.Equal(0, ((JsonArray)JsonValue.Parse("[]")).Count);
            Assert.Equal(0, ((JsonArray)JsonValue.Parse("[ ]")).Count);
        }

        [Fact]
        public void ParsesTopLevelScalars()
        {
            Assert.Equal(JsonType.String, JsonValue.Parse("\"hi\"").Type);
            Assert.Equal(5L, JsonValue.Parse("5").Value<long>());
            Assert.True(JsonValue.Parse("true").Value<bool>());
            Assert.Equal(JsonType.Null, JsonValue.Parse("null").Type);
        }

        [Fact]
        public void MissingMemberReturnsNull()
        {
            var json = JsonObject.Parse("{\"a\":1}");
            Assert.Null(json["absent"]);
            Assert.Null(json["a"]["deeper"]);
        }

        [Fact]
        public void DuplicateMembersLastOneWins()
        {
            var json = JsonObject.Parse("{\"a\":1,\"a\":2}");
            Assert.Equal(2, json["a"].Value<int>());
            Assert.Equal(1, json.Count);
        }

        [Fact]
        public void PreservesMemberOrder()
        {
            var json = JsonObject.Parse("{\"z\":1,\"a\":2,\"m\":3}");
            Assert.Equal(3, json.Keys.Count);
            Assert.Equal("z", json.Keys[0]);
            Assert.Equal("a", json.Keys[1]);
            Assert.Equal("m", json.Keys[2]);
            Assert.Equal("{\"z\":1,\"a\":2,\"m\":3}", json.ToString());
        }

        // ---------- numbers: integer vs float ----------

        [Fact]
        public void DistinguishesIntegersFromFloats()
        {
            Assert.Equal(JsonType.Integer, JsonValue.Parse("5").Type);
            Assert.Equal(JsonType.Integer, JsonValue.Parse("-3").Type);
            Assert.Equal(JsonType.Float, JsonValue.Parse("5.0").Type);
            Assert.Equal(JsonType.Float, JsonValue.Parse("1e3").Type);
            Assert.Equal(JsonType.Float, JsonValue.Parse("-0.5").Type);
        }

        [Fact]
        public void IntegralFloatKeepsItsDecimalPoint()
        {
            var json = new JsonObject { ["size"] = 300.0 };
            Assert.Equal("{\"size\":300.0}", json.ToString());

            // and must still parse back as a float, not an integer
            Assert.Equal(JsonType.Float, JsonObject.Parse(json.ToString())["size"].Type);
        }

        [Fact]
        public void NonFiniteNumbersBecomeJsonNull()
        {
            var json = new JsonObject { ["nan"] = double.NaN, ["inf"] = double.PositiveInfinity };
            Assert.Equal("{\"nan\":null,\"inf\":null}", json.ToString());
        }

        // ---------- ids survive a round trip ----------

        [Theory]
        [InlineData("1")]
        [InlineData("1.5")]
        [InlineData("\"abc\"")]
        [InlineData("null")]
        public void IdRoundTripsUnchanged(string id)
        {
            var text = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"ping\"}";
            var parsed = JsonValue.Parse(text);

            Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"ping\"}", parsed.ToString());
            Assert.Equal(parsed.ToString(), JsonValue.Parse(parsed.ToString()).ToString());
        }

        // ---------- strings and escaping ----------

        [Fact]
        public void UnescapesJsonEscapes()
        {
            var json = JsonObject.Parse(
                "{\"q\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u00e9\\uD83D\\uDE00\"}");
            var value = json["q"].AsString();

            Assert.Equal("\"\\/\b\f\n\r\t\u00e9\uD83D\uDE00", value);
        }

        [Fact]
        public void EscapesOnWrite()
        {
            var json = new JsonObject { ["q"] = "a\"b\\c\nd\te\u0001" };
            var text = json.ToString();

            Assert.Equal("{\"q\":\"a\\\"b\\\\c\\nd\\te\\u0001\"}", text);
            // the escaped form must parse back to the original
            Assert.Equal("a\"b\\c\nd\te\u0001", JsonObject.Parse(text)["q"].AsString());
        }

        [Fact]
        public void AsStringIsRawButToStringIsJson()
        {
            var value = JsonObject.Parse("{\"a\":\"text\"}")["a"];
            Assert.Equal("text", value.AsString());
            Assert.Equal("\"text\"", value.ToString());
        }

        // ---------- culture independence ----------

        [Fact]
        public void NumbersAreCultureIndependent()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                // A comma-decimal culture must not leak into JSON output or parsing.
                CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

                Assert.Equal("{\"v\":3.5}", new JsonObject { ["v"] = 3.5 }.ToString());
                Assert.Equal(3.5, JsonObject.Parse("{\"v\":3.5}")["v"].Value<double>());
                Assert.Equal(3.5, JsonValue.Parse("3.5").Value<double>());
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        // ---------- formatting ----------

        [Fact]
        public void IndentedOutputIsValidAndRoundTrips()
        {
            var json = JsonObject.Parse("{\"a\":[1,{\"b\":\"x\"}],\"c\":{}}");
            var indented = json.ToString(JsonFormatting.Indented);

            Assert.Contains("\n", indented);
            Assert.Equal(json.ToString(), JsonValue.Parse(indented).ToString());
        }

        [Fact]
        public void EmptyContainersStayOnOneLineWhenIndented()
        {
            var json = JsonObject.Parse("{\"a\":{},\"b\":[]}");
            var indented = json.ToString(JsonFormatting.Indented);

            Assert.Contains("\"a\": {}", indented);
            Assert.Contains("\"b\": []", indented);
        }

        // ---------- typed access ----------

        [Fact]
        public void ValueConvertsBetweenNumericTypes()
        {
            var integer = JsonValue.Parse("7");
            Assert.Equal(7, integer.Value<int>());
            Assert.Equal(7L, integer.Value<long>());
            Assert.Equal(7.0, integer.Value<double>());
            Assert.Equal("7", integer.AsString());

            var number = JsonValue.Parse("2.25");
            Assert.Equal(2.25, number.Value<double>());
            Assert.Equal("2.25", number.AsString());
        }

        [Fact]
        public void ValueOnContainerThrows()
        {
            Assert.Throws<InvalidCastException>(() => JsonObject.Parse("{}").Value<string>());
        }

        // ---------- implicit conversions ----------

        [Fact]
        public void NullableValuesBecomeJsonNull()
        {
            double? missing = null;
            var json = new JsonObject { ["a"] = missing, ["b"] = (double?)1.5 };

            Assert.Equal("{\"a\":null,\"b\":1.5}", json.ToString());
        }

        // ---------- malformed input ----------

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\" 1}")]
        [InlineData("[1,2")]
        [InlineData("\"unterminated")]
        [InlineData("{\"a\":1}trailing")]
        [InlineData("{\"a\":tru}")]
        [InlineData("nope")]
        [InlineData("{\"a\":\"\\q\"}")]
        [InlineData("{\"a\":\"\\u12\"}")]
        public void MalformedInputThrowsJsonParseException(string input)
        {
            Assert.Throws<JsonParseException>(() => JsonValue.Parse(input));
        }

        [Fact]
        public void ExcessiveNestingIsRejected()
        {
            var deep = new string('[', JsonValue.MaxDepth + 5) + new string(']', JsonValue.MaxDepth + 5);
            Assert.Throws<JsonParseException>(() => JsonValue.Parse(deep));
        }

        [Fact]
        public void NestingUpToTheLimitIsAccepted()
        {
            var ok = new string('[', JsonValue.MaxDepth) + new string(']', JsonValue.MaxDepth);
            Assert.Equal(JsonType.Array, JsonValue.Parse(ok).Type);
        }

        [Fact]
        public void NonObjectDocumentIsRejectedByJsonObjectParse()
        {
            Assert.Throws<JsonParseException>(() => JsonObject.Parse("[1,2]"));
        }
    }
}
