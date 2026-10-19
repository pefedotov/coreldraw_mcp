using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CorelMcp.Json;
using CorelMcp.Config;
using CorelMcp.Mcp;
using CorelMcp.Tools;
using Xunit;

namespace CorelMcpAddon.Tests
{
    /// <summary>
    /// The parts of the HTTP transport that decide who may talk to the endpoint and how much
    /// work one request can cost: bearer token, body limit, browser-origin rejection and
    /// non-JSON payloads. Plus the property the previous single-threaded accept loop broke —
    /// a long tool call must not block <c>/health</c>.
    /// </summary>
    public class HttpServerPolicyTests
    {
        /// <summary>A running endpoint plus the client that talks to it.</summary>
        private sealed class Endpoint : IDisposable
        {
            public HttpServer Server { get; private set; }
            public HttpClient Client { get; private set; }
            public string BaseUrl { get; private set; }

            public static Endpoint Start(Action<AddonConfig> configure = null, Action<ToolRegistry> register = null)
            {
                int port = FreePort();

                var registry = new ToolRegistry();
                registry.Define("echo", "Echoes a value.",
                    Schema.Required(("value", Schema.Str("Value."))),
                    args => new JsonObject { ["echoed"] = args["value"].AsString() });
                register?.Invoke(registry);

                var config = new AddonConfig { Port = port, BindAddress = "127.0.0.1" };
                configure?.Invoke(config);

                var server = new HttpServer(config, new McpProtocol(registry));
                server.Start();

                return new Endpoint
                {
                    Server = server,
                    Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                    BaseUrl = "http://127.0.0.1:" + port
                };
            }

            public async Task<HttpResponseMessage> Post(string json, string contentType = "application/json")
            {
                var content = new StringContent(json, Encoding.UTF8, contentType);
                return await Client.PostAsync(BaseUrl + "/mcp", content);
            }

            public void Dispose()
            {
                Server.Stop();
                Client.Dispose();
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private const string Ping = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";

        [Fact]
        public async Task MissingBearerToken_Returns401()
        {
            using (var endpoint = Endpoint.Start(config => config.AuthToken = "secret"))
            {
                var response = await endpoint.Post(Ping);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.True(response.Headers.Contains("WWW-Authenticate"),
                    "401 must advertise the scheme so a client knows what to send");
            }
        }

        [Fact]
        public async Task BearerToken_IsAcceptedWhenItMatches()
        {
            using (var endpoint = Endpoint.Start(config => config.AuthToken = "secret"))
            {
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint.BaseUrl + "/mcp")
                {
                    Content = new StringContent(Ping, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", "Bearer secret");

                var response = await endpoint.Client.SendAsync(request);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [Fact]
        public async Task WrongBearerToken_Returns401()
        {
            using (var endpoint = Endpoint.Start(config => config.AuthToken = "secret"))
            {
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint.BaseUrl + "/mcp")
                {
                    Content = new StringContent(Ping, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", "Bearer nope");

                var response = await endpoint.Client.SendAsync(request);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        [Fact]
        public async Task BodyLargerThanMaxBodyBytes_Returns413()
        {
            using (var endpoint = Endpoint.Start(config => config.MaxBodyBytes = 128))
            {
                var response = await endpoint.Post(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\",\"params\":{\"pad\":\"" +
                    new string('x', 4096) + "\"}}");

                Assert.Equal((HttpStatusCode)413, response.StatusCode);
            }
        }

        [Fact]
        public async Task BodyWithinMaxBodyBytes_IsAccepted()
        {
            using (var endpoint = Endpoint.Start(config => config.MaxBodyBytes = 4096))
            {
                var response = await endpoint.Post(Ping);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [Fact]
        public async Task ForeignOrigin_Returns403()
        {
            using (var endpoint = Endpoint.Start())
            {
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint.BaseUrl + "/mcp")
                {
                    Content = new StringContent(Ping, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Origin", "https://evil.example");

                var response = await endpoint.Client.SendAsync(request);

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"),
                    "a rejected origin must not receive CORS permission");
            }
        }

        [Fact]
        public async Task LoopbackOrigin_IsAcceptedAndEchoed()
        {
            using (var endpoint = Endpoint.Start())
            {
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint.BaseUrl + "/mcp")
                {
                    Content = new StringContent(Ping, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Origin", "http://localhost:3000");

                var response = await endpoint.Client.SendAsync(request);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("http://localhost:3000",
                    string.Join("", response.Headers.GetValues("Access-Control-Allow-Origin")));
            }
        }

        [Fact]
        public async Task NonJsonContentType_Returns415()
        {
            using (var endpoint = Endpoint.Start())
            {
                var response = await endpoint.Post(Ping, "text/plain");

                Assert.Equal((HttpStatusCode)415, response.StatusCode);
            }
        }

        [Fact]
        public async Task Health_AnswersWhileALongToolCallIsRunning()
        {
            using (var endpoint = Endpoint.Start(register: registry => registry.Define(
                "slow", "Sleeps, standing in for a long CorelDRAW call.",
                Schema.Object(new JsonObject()),
                args =>
                {
                    Thread.Sleep(2000);
                    return new JsonObject { ["slept"] = true };
                })))
            {
                var call = endpoint.Post(
                    "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\"," +
                    "\"params\":{\"name\":\"slow\",\"arguments\":{}}}");

                // Give the call a moment to reach the tool handler before probing health.
                await Task.Delay(300);

                var stopwatch = Stopwatch.StartNew();
                var health = await endpoint.Client.GetAsync(endpoint.BaseUrl + "/health");
                stopwatch.Stop();

                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
                Assert.True(stopwatch.ElapsedMilliseconds < 1500,
                    "/health took " + stopwatch.ElapsedMilliseconds +
                    " ms while a tool call was running; the accept loop is still blocking");

                var finished = await call;
                Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
            }
        }
    }
}
