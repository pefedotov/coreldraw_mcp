using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CorelMcp.Json;
using Xunit;
using CorelMcp.Config;
using CorelMcp.Mcp;
using CorelMcp.Tools;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// Exercises the real HttpListener transport end-to-end (no CorelDRAW required),
    /// which is exactly what an MCP client will talk to.
    /// </summary>
    public class HttpServerTests : IDisposable
    {
        private readonly HttpServer _server;
        private readonly HttpClient _client;
        private readonly string _baseUrl;

        public HttpServerTests()
        {
            int port = FreePort();

            var registry = new ToolRegistry();
            registry.Define("echo", "Echoes a value.",
                Schema.Required(("value", Schema.Str("Value."))),
                args => new JsonObject { ["echoed"] = args["value"].AsString() });

            var config = new AddonConfig { Port = port, BindAddress = "127.0.0.1", AuthToken = "" };
            _server = new HttpServer(config, new McpProtocol(registry));
            _server.Start();

            _baseUrl = "http://127.0.0.1:" + port;
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        public void Dispose()
        {
            _server.Stop();
            _client.Dispose();
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task<HttpResponseMessage> Post(string json)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _client.PostAsync(_baseUrl + "/mcp", content);
        }

        private static async Task<JsonObject> Body(HttpResponseMessage response)
        {
            return JsonObject.Parse(await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Initialize_OverHttp_ReturnsResultAndSessionHeader()
        {
            var response = await Post(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.Contains("Mcp-Session-Id"), "initialize must issue an Mcp-Session-Id header");

            var body = await Body(response);
            Assert.Equal("coreldraw_mcp", body["result"]["serverInfo"]["name"].Value<string>());
        }

        [Fact]
        public async Task ToolsList_OverHttp_ReturnsTool()
        {
            var body = await Body(await Post("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"));

            Assert.Equal("echo", body["result"]["tools"][0]["name"].Value<string>());
        }

        [Fact]
        public async Task ToolsCall_OverHttp_ReturnsTextContent()
        {
            var body = await Body(await Post(
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\"," +
                "\"params\":{\"name\":\"echo\",\"arguments\":{\"value\":\"ping\"}}}"));

            var payload = JsonObject.Parse(body["result"]["content"][0]["text"].Value<string>());
            Assert.Equal("ping", payload["echoed"].Value<string>());
        }

        [Fact]
        public async Task InvalidJson_ReturnsParseError()
        {
            var body = await Body(await Post("{not json"));

            Assert.Equal(JsonRpcErrors.ParseError, body["error"]["code"].Value<int>());
        }

        [Fact]
        public async Task UnknownPath_Returns404()
        {
            var response = await _client.GetAsync(_baseUrl + "/nope");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // The body is the only help a misconfigured client gets: it must name the endpoint.
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("/mcp", text);
            Assert.Contains("/health", text);
        }

        [Fact]
        public async Task Health_ReturnsOk()
        {
            var response = await _client.GetAsync(_baseUrl + "/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task GetMcp_Returns405WithoutSseStream()
        {
            var response = await _client.GetAsync(_baseUrl + "/mcp");
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Contains("POST", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Notification_Returns202WithNoBody()
        {
            var response = await Post("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
    }
}
