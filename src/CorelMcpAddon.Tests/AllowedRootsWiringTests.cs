using System;
using CorelMcp;
using CorelMcp.Config;
using CorelMcp.Json;
using CorelMcp.Tools;
using Xunit;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// The wiring, not the rule: every tool that takes a file path must apply AllowedRoots
    /// <em>before</em> it reaches CorelDRAW. These run without a CorelDRAW instance on purpose —
    /// a handler that forgot the check would fall through to CorelBridge and fail with
    /// "CorelBridge is not initialised", so the assertion on the message doubles as proof that
    /// no COM call was attempted.
    /// </summary>
    public class AllowedRootsWiringTests
    {
        private static readonly AddonConfig Restricted =
            new AddonConfig { AllowedRoots = @"C:\allowed" };

        private static ToolDescriptor Tool(string name)
        {
            ToolDescriptor descriptor;
            if (!ToolCatalog.Build().TryGet(name, out descriptor))
                throw new InvalidOperationException("Tool '" + name + "' is not registered.");
            return descriptor;
        }

        private static void AssertRejected(string toolName, string argumentsJson)
        {
            var previous = AddonConfig.Current;
            try
            {
                AddonConfig.Current = Restricted;
                var ex = Assert.Throws<ToolArgumentException>(
                    () => Tool(toolName).Handler(JsonObject.Parse(argumentsJson)));
                Assert.Contains("AllowedRoots", ex.Message);
            }
            finally
            {
                AddonConfig.Current = previous;
            }
        }

        [Theory]
        [InlineData("open_document", "{\"path\":\"D:\\\\elsewhere\\\\drawing.cdr\"}")]
        [InlineData("save_document_as", "{\"path\":\"D:\\\\elsewhere\\\\drawing.cdr\"}")]
        [InlineData("import_image", "{\"path\":\"D:\\\\elsewhere\\\\photo.jpg\"}")]
        [InlineData("import_svg", "{\"path\":\"D:\\\\elsewhere\\\\logo.svg\"}")]
        [InlineData("export_pdf", "{\"path\":\"D:\\\\elsewhere\\\\out.pdf\"}")]
        [InlineData("export", "{\"path\":\"D:\\\\elsewhere\\\\out.png\",\"format\":\"png\",\"dpi\":150}")]
        [InlineData("export_png", "{\"path\":\"D:\\\\elsewhere\\\\out.png\",\"dpi\":150}")]
        [InlineData("export_svg", "{\"path\":\"D:\\\\elsewhere\\\\out.svg\"}")]
        [InlineData("export_dxf", "{\"path\":\"D:\\\\elsewhere\\\\out.dxf\"}")]
        [InlineData("set_fill_pattern",
            "{\"pattern\":\"D:\\\\elsewhere\\\\tile.png\",\"patternType\":\"bitmap\"}")]
        public void PathOutsideAllowedRoots_IsRejectedBeforeCorelDRAW(string toolName, string argumentsJson)
        {
            AssertRejected(toolName, argumentsJson);
        }

        [Fact]
        public void BitmapPatternName_IsNotTreatedAsAPath()
        {
            var previous = AddonConfig.Current;
            try
            {
                AddonConfig.Current = Restricted;

                // "Samples 5" is a library name, not a path: the handler must get past the path
                // check and only then fail on the missing CorelDRAW bridge.
                var ex = Assert.ThrowsAny<Exception>(
                    () => Tool("set_fill_pattern").Handler(JsonObject.Parse(
                        "{\"pattern\":\"Samples 5\",\"patternType\":\"twoColor\"}")));

                Assert.NotEqual(typeof(ToolArgumentException), ex.GetType());
            }
            finally
            {
                AddonConfig.Current = previous;
            }
        }

        [Fact]
        public void UnknownRoots_DoNotBlockPathsWhenTheListIsEmpty()
        {
            var previous = AddonConfig.Current;
            try
            {
                AddonConfig.Current = new AddonConfig { AllowedRoots = "" };

                // Same call as the rejected case above; with no roots it reaches the bridge.
                var ex = Assert.ThrowsAny<Exception>(
                    () => Tool("open_document").Handler(JsonObject.Parse(
                        "{\"path\":\"D:\\\\elsewhere\\\\drawing.cdr\"}")));

                Assert.NotEqual(typeof(ToolArgumentException), ex.GetType());
            }
            finally
            {
                AddonConfig.Current = previous;
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task ExportOutsideRoots_OverHttp_ComesBackAsAToolError()
        {
            // The same rejection, seen the way a client sees it: over the real HTTP transport.
            int port;
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var previous = AddonConfig.Current;
            var config = new AddonConfig
            {
                Port = port,
                BindAddress = "127.0.0.1",
                AllowedRoots = @"C:\allowed"
            };
            AddonConfig.Current = config;

            var server = new CorelMcp.Mcp.HttpServer(config, new CorelMcp.Mcp.McpProtocol(ToolCatalog.Build()));
            server.Start();
            try
            {
                using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) })
                {
                    var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{" +
                               "\"name\":\"export_png\",\"arguments\":{\"path\":\"D:\\\\elsewhere\\\\x.png\",\"dpi\":150}}}";
                    var response = await client.PostAsync(
                        "http://127.0.0.1:" + port + "/mcp",
                        new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"));
                    var json = JsonObject.Parse(await response.Content.ReadAsStringAsync());

                    Assert.True(json["result"]["isError"].Value<bool>());
                    Assert.Contains("AllowedRoots", json["result"]["content"][0]["text"].Value<string>());
                }
            }
            finally
            {
                server.Stop();
                AddonConfig.Current = previous;
            }
        }

        [Fact]
        public void ServerVersion_IsTheAssemblyVersion()
        {
            var expected = typeof(CorelMcp.Mcp.McpProtocol).Assembly.GetName().Version.ToString(3);

            Assert.Equal(expected, CorelMcp.Mcp.McpProtocol.ServerVersion);
            Assert.Matches(@"^\d+\.\d+\.\d+$", CorelMcp.Mcp.McpProtocol.ServerVersion);
        }
    }
}
