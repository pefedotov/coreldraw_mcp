using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using CorelMcp.Json;
using CorelMcp.Config;

namespace CorelMcp.Mcp
{
    /// <summary>
    /// Minimal MCP "Streamable HTTP" endpoint backed by <see cref="HttpListener"/>.
    /// Bound to an explicit loopback prefix, which needs no URL ACL / admin rights.
    /// </summary>
    public class HttpServer
    {
        private readonly AddonConfig _config;
        private readonly McpProtocol _protocol;

        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private string _sessionId;

        public long RequestCount;

        public HttpServer(AddonConfig config, McpProtocol protocol)
        {
            _config = config;
            _protocol = protocol;
        }

        public string Prefix { get { return _config.HttpPrefix; } }

        public void Start()
        {
            if (_running) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add(_config.HttpPrefix);
            _listener.Start();
            _running = true;

            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "CorelMcp.HttpListener" };
            _thread.Start();

            Log.Info("MCP endpoint listening on " + _config.HttpPrefix + "mcp");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }

            var thread = _thread;
            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(2000); } catch { }
            }
            _thread = null;
            Log.Info("MCP endpoint stopped.");
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running) Log.Error("HTTP accept failed", ex);
                    break;
                }

                try
                {
                    Handle(context);
                }
                catch (Exception ex)
                {
                    Log.Error("HTTP request handling failed", ex);
                    TryWriteStatus(context, 500, "Internal server error");
                }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var path = (request.Url.AbsolutePath ?? "/").TrimEnd('/');
            if (path.Length == 0) path = "/";

            Log.Info(request.HttpMethod + " " + request.Url.AbsolutePath);

            if (request.HttpMethod == "OPTIONS")
            {
                WriteCors(context);
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            if (path == "/health" && request.HttpMethod == "GET")
            {
                TryWriteStatus(context, 200, "ok");
                return;
            }

            if (path != "/mcp" && path != "/")
            {
                TryWriteStatus(context, 404, "Not found. The MCP endpoint is /mcp (POST); GET /health reports readiness.");
                return;
            }

            if (!IsAuthorised(request))
            {
                context.Response.StatusCode = 401;
                context.Response.AddHeader("WWW-Authenticate", "Bearer");
                TryWriteStatus(context, 401, "Unauthorized: this server requires a bearer token. Configure AuthToken in config.json and send Authorization: Bearer <token>.");
                return;
            }

            switch (request.HttpMethod)
            {
                case "POST":
                    HandlePost(context);
                    return;

                case "GET":
                    // Server-initiated SSE stream is not implemented; 405 is spec-compliant.
                    context.Response.AddHeader("Allow", "POST, OPTIONS, DELETE");
                    TryWriteStatus(context, 405, "This server does not offer a standalone SSE stream. Send MCP requests as POST /mcp.");
                    return;

                case "DELETE":
                    // Session termination: nothing to clean up (stateless), acknowledge.
                    _sessionId = null;
                    TryWriteStatus(context, 200, "session terminated");
                    return;

                default:
                    context.Response.AddHeader("Allow", "POST, OPTIONS, DELETE");
                    TryWriteStatus(context, 405, "Method not allowed. Send MCP requests as POST /mcp.");
                    return;
            }
        }

        private void HandlePost(HttpListenerContext context)
        {
            string body;
            // MCP payloads are UTF-8 by specification. Request.ContentEncoding must not be
            // used here: HttpListener defaults it to ISO-8859-1 when the request carries no
            // charset, which silently mangles non-ASCII text (e.g. Cyrillic tool arguments).
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            if (body.Length > _config.MaxBodyBytes)
            {
                TryWriteStatus(context, 413, "Request body too large.");
                return;
            }

            JsonValue parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(body) ? null : JsonValue.Parse(body);
            }
            catch (JsonParseException ex)
            {
                WriteJson(context, JsonRpcResponse.Fail(null,
                    new JsonRpcError(JsonRpcErrors.ParseError, "Invalid JSON: " + ex.Message)).ToJson());
                return;
            }

            if (parsed == null)
            {
                WriteJson(context, JsonRpcResponse.Fail(null,
                    new JsonRpcError(JsonRpcErrors.InvalidRequest, "Empty request body.")).ToJson());
                return;
            }

            Interlocked.Increment(ref RequestCount);

            if (parsed.Type == JsonType.Array)
            {
                var responses = new JsonArray();
                foreach (var element in (JsonArray)parsed)
                {
                    var request = JsonRpcRequest.FromJson(element);
                    if (request != null && string.Equals(request.Method, "initialize", StringComparison.Ordinal))
                        _sessionId = Guid.NewGuid().ToString("N");
                    var response = _protocol.Handle(request);
                    if (response != null) responses.Add(response.ToJson());
                }
                if (responses.Count == 0)
                {
                    context.Response.StatusCode = 202;
                    context.Response.Close();
                    return;
                }
                WriteJson(context, responses);
                return;
            }

            var single = JsonRpcRequest.FromJson(parsed);
            if (single != null && string.Equals(single.Method, "initialize", StringComparison.Ordinal))
                _sessionId = Guid.NewGuid().ToString("N");

            var result = _protocol.Handle(single);
            if (result == null)
            {
                // Notification: no response body.
                context.Response.StatusCode = 202;
                context.Response.Close();
                return;
            }
            WriteJson(context, result.ToJson());
        }

        private bool IsAuthorised(HttpListenerRequest request)
        {
            var expected = _config.AuthToken;
            if (string.IsNullOrEmpty(expected)) return true;

            var header = request.Headers["Authorization"];
            if (string.IsNullOrEmpty(header)) return false;

            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            return string.Equals(header.Substring(prefix.Length).Trim(), expected, StringComparison.Ordinal);
        }

        private void WriteJson(HttpListenerContext context, JsonValue payload)
        {
            string json = (payload ?? JsonValue.Null).ToString(JsonFormatting.Compact);

            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentEncoding = Encoding.UTF8;
            if (_sessionId != null) context.Response.AddHeader("Mcp-Session-Id", _sessionId);
            WriteCors(context);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Flush();
            context.Response.Close();
        }

        private void WriteCors(HttpListenerContext context)
        {
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.AddHeader("Access-Control-Allow-Methods", "POST, GET, DELETE, OPTIONS");
            context.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, Mcp-Session-Id, Accept");
        }

        private static void TryWriteStatus(HttpListenerContext context, int status, string message)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
                context.Response.StatusCode = status;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentEncoding = Encoding.UTF8;
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
            catch
            {
                try { context.Response.Abort(); } catch { }
            }
        }
    }
}
