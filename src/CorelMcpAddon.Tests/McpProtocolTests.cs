using System;
using CorelMcp.Json;
using Xunit;
using CorelMcp.Mcp;
using CorelMcp.Tools;

namespace CorelMcpAddon.Tests
{
    public class McpProtocolTests
    {
        private static McpProtocol BuildProtocol()
        {
            var registry = new ToolRegistry();
            registry.Define("echo",
                "Echoes the provided value.",
                Schema.Required(("value", Schema.Str("Value to echo."))),
                args => new JsonObject { ["echoed"] = args["value"].AsString() });
            registry.Define("boom",
                "Always fails with a tool-argument error.",
                Schema.Object(new JsonObject()),
                args => { throw new ToolArgumentException("bad argument"); });
            return new McpProtocol(registry);
        }

        private static JsonRpcRequest Request(string json)
        {
            return JsonRpcRequest.FromJson(JsonValue.Parse(json));
        }

        [Fact]
        public void Initialize_EchoesClientProtocolVersionWhenSupported()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                "{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\"}}}"));

            Assert.Null(response.Error);
            Assert.Equal("2024-11-05", response.Result["protocolVersion"].Value<string>());
            Assert.Equal("coreldraw_mcp", response.Result["serverInfo"]["name"].Value<string>());
            Assert.NotNull(response.Result["capabilities"]["tools"]);
        }

        [Fact]
        public void Initialize_FallsBackToLatestVersionForUnknownClientVersion()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                "{\"protocolVersion\":\"1999-01-01\"}}"));

            Assert.Equal(McpProtocol.LatestProtocolVersion, response.Result["protocolVersion"].Value<string>());
        }

        [Fact]
        public void ToolsList_ReturnsRegisteredToolsWithSchemas()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"));

            var tools = (JsonArray)response.Result["tools"];
            Assert.Equal(2, tools.Count);
            Assert.Equal("boom", tools[0]["name"].Value<string>());
            Assert.Equal("echo", tools[1]["name"].Value<string>());
            Assert.Equal("object", tools[1]["inputSchema"]["type"].Value<string>());
        }

        [Fact]
        public void ToolsCall_UnknownTool_ReturnsInvalidParamsError()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"nope\"}}"));

            Assert.NotNull(response.Error);
            Assert.Equal(JsonRpcErrors.InvalidParams, response.Error.Code);
        }

        [Fact]
        public void ToolsCall_Success_ReturnsTextContent()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\"," +
                "\"params\":{\"name\":\"echo\",\"arguments\":{\"value\":\"hi\"}}}"));

            Assert.Null(response.Error);
            Assert.Null(response.Result["isError"]);
            Assert.Equal("text", response.Result["content"][0]["type"].Value<string>());
            var payload = JsonObject.Parse(response.Result["content"][0]["text"].Value<string>());
            Assert.Equal("hi", payload["echoed"].Value<string>());
        }

        [Fact]
        public void ToolsCall_ToolThrows_ReturnsErrorContent()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"boom\"}}"));

            Assert.Null(response.Error);
            Assert.True(response.Result["isError"].Value<bool>());
            var payload = JsonObject.Parse(response.Result["content"][0]["text"].Value<string>());
            Assert.Equal("bad argument", payload["error"].Value<string>());
        }

        [Fact]
        public void ToolsList_HidesDisabledToolsAndRejectsTheirCalls()
        {
            var protocol = BuildProtocol();
            protocol.SetDisabledTools(new[] { "boom" });

            var listed = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/list\"}"));
            var tools = (JsonArray)listed.Result["tools"];
            Assert.Equal(1, tools.Count);
            Assert.Equal("echo", tools[0]["name"].Value<string>());

            var call = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"boom\"}}"));
            Assert.NotNull(call.Error);
            Assert.Equal(JsonRpcErrors.InvalidParams, call.Error.Code);
        }

        [Fact]
        public void ToolsList_HidesNothingByDefault()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"tools/list\"}"));

            Assert.Equal(2, ((JsonArray)response.Result["tools"]).Count);
        }

        [Fact]
        public void UnknownMethod_ReturnsMethodNotFound()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"resources/list\"}"));

            Assert.Equal(JsonRpcErrors.MethodNotFound, response.Error.Code);
        }

        [Fact]
        public void Notification_ProducesNoResponse()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));

            Assert.Null(response);
        }

        [Fact]
        public void Ping_ReturnsEmptyResult()
        {
            var protocol = BuildProtocol();
            var response = protocol.Handle(Request("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"ping\"}"));

            Assert.Null(response.Error);
            Assert.NotNull(response.Result);
        }
    }
}
