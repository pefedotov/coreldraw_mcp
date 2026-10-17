using CorelMcp.Json;
using Xunit;
using CorelMcp.Mcp;

namespace CorelMcpAddon.Tests
{
    public class JsonRpcTests
    {
        [Fact]
        public void OkResponse_SerialisesWithJsonRpcVersionAndId()
        {
            var response = JsonRpcResponse.Ok(7, new JsonObject { ["hello"] = "world" });
            var json = JsonObject.Parse(response.ToJson().ToString());

            Assert.Equal("2.0", json["jsonrpc"].Value<string>());
            Assert.Equal(7, json["id"].Value<int>());
            Assert.Equal("world", json["result"]["hello"].Value<string>());
            Assert.Null(json["error"]);
        }

        [Fact]
        public void ErrorResponse_CarriesCodeAndMessage()
        {
            var response = JsonRpcResponse.Fail(1,
                new JsonRpcError(JsonRpcErrors.MethodNotFound, "nope"));
            var json = JsonObject.Parse(response.ToJson().ToString());

            Assert.Equal(JsonRpcErrors.MethodNotFound, json["error"]["code"].Value<int>());
            Assert.Equal("nope", json["error"]["message"].Value<string>());
            Assert.Null(json["result"]);
        }

        [Fact]
        public void Request_WithId_IsNotANotification()
        {
            var request = JsonRpcRequest.FromJson(JsonValue.Parse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}"));
            Assert.False(request.IsNotification);
        }

        [Fact]
        public void Request_WithoutId_IsANotification()
        {
            var request = JsonRpcRequest.FromJson(JsonValue.Parse(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
            Assert.True(request.IsNotification);
        }
    }
}
