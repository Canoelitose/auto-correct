using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AutoCorrect.Core.Tests.Cases;

/// <summary>
/// Minimal stand-in for an OpenAI compatible endpoint, so the streaming path can be exercised
/// without Ollama or llama.cpp.
///
/// Answers /v1/models for the availability probe and /v1/chat/completions with server-sent
/// events, flushing each chunk separately so a test can tell streaming from buffering.
/// </summary>
internal sealed class FakeLlmServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Func<string, ChatReply> _chatHandler;

    public FakeLlmServer(Func<string, ChatReply> chatHandler)
    {
        _chatHandler = chatHandler;
        Port = FindFreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Convenience for the common case: stream these tokens, then finish cleanly.</summary>
    public static FakeLlmServer Streaming(params string[] tokens) =>
        new(_ => ChatReply.WithTokens(tokens));

    public int Port { get; }

    public string Endpoint => $"http://127.0.0.1:{Port}/v1";

    /// <summary>JSON body of the last chat request, for asserting on the request shape.</summary>
    public string? LastRequestBody { get; private set; }

    public int ChatRequestCount { get; private set; }

    /// <summary>Set to false to make the availability probe fail.</summary>
    public bool ModelsAvailable { get; set; } = true;

    /// <summary>What GET /v1/models reports as installed.</summary>
    public IReadOnlyList<string> InstalledModels { get; set; } = ["test-model"];

    public int ModelListRequestCount { get; private set; }

    /// <summary>Authorization header of the last chat request, null when none was sent.</summary>
    public string? LastAuthorization { get; private set; }

    /// <summary>When set, requests without exactly this bearer token are answered with 401.</summary>
    public string? RequiredKey { get; set; }

    /// <summary>Blocks the response until the test releases it, to exercise timeouts.</summary>
    public ManualResetEventSlim? HoldResponse { get; set; }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            try
            {
                await HandleAsync(context);
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException)
            {
                // Client vanished mid-stream, which is exactly what a cancellation test does.
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";

        if (path.EndsWith("/models", StringComparison.Ordinal))
        {
            if (!ModelsAvailable)
            {
                context.Response.StatusCode = 503;
                context.Response.ContentLength64 = 0;
                return;
            }

            var entries = string.Join(",", InstalledModels.Select(m =>
                $"{{\"id\":{System.Text.Json.JsonSerializer.Serialize(m)}}}"));

            await WriteJsonAsync(context, 200, $"{{\"object\":\"list\",\"data\":[{entries}]}}");
            ModelListRequestCount++;
            return;
        }

        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            LastRequestBody = await reader.ReadToEndAsync();
        }

        LastAuthorization = context.Request.Headers["Authorization"];
        ChatRequestCount++;

        if (RequiredKey is not null && LastAuthorization != $"Bearer {RequiredKey}")
        {
            await WriteJsonAsync(context, 401, "{\"error\":\"unauthorized\"}");
            return;
        }
        var reply = _chatHandler(LastRequestBody);

        if (reply.Status != 200)
        {
            await WriteJsonAsync(context, reply.Status, reply.ErrorBody ?? "{}");
            return;
        }

        HoldResponse?.Wait(TimeSpan.FromSeconds(30));

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/event-stream";
        // Chunked, because the length of a generated answer is not known up front.
        context.Response.SendChunked = true;

        foreach (var token in reply.Tokens)
        {
            var escaped = System.Text.Json.JsonSerializer.Serialize(token);
            await WriteLineAsync(context, $"data: {{\"choices\":[{{\"delta\":{{\"content\":{escaped}}}}}]}}");
            await WriteLineAsync(context, "");
        }

        foreach (var raw in reply.RawLines)
        {
            await WriteLineAsync(context, raw);
        }

        if (reply.SendDone)
        {
            await WriteLineAsync(context, "data: [DONE]");
        }
    }

    private static async Task WriteLineAsync(HttpListenerContext context, string line)
    {
        var payload = Encoding.UTF8.GetBytes(line + "\n");
        await context.Response.OutputStream.WriteAsync(payload);

        // Without the flush the listener buffers everything and the test could not tell a
        // streaming response from one that arrives in a single lump.
        await context.Response.OutputStream.FlushAsync();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int status, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = payload.Length;
        await context.Response.OutputStream.WriteAsync(payload);
    }

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        HoldResponse?.Set();

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    /// <summary>What the fake server should answer with.</summary>
    internal sealed class ChatReply
    {
        public int Status { get; init; } = 200;

        public string? ErrorBody { get; init; }

        public IReadOnlyList<string> Tokens { get; init; } = [];

        /// <summary>Lines written verbatim after the tokens, for malformed-chunk tests.</summary>
        public IReadOnlyList<string> RawLines { get; init; } = [];

        public bool SendDone { get; init; } = true;

        public static ChatReply WithTokens(params string[] tokens) => new() { Tokens = tokens };

        public static ChatReply Error(int status, string body) => new() { Status = status, ErrorBody = body };
    }
}
