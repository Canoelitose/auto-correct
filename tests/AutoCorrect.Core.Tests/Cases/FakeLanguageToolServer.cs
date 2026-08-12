using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AutoCorrect.Core.Tests.Cases;

/// <summary>
/// Minimal stand-in for languagetool-standalone so the engine can be exercised end to end
/// without a Java runtime.
/// </summary>
internal sealed class FakeLanguageToolServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public FakeLanguageToolServer(Func<string, (int Status, string Body)> checkHandler)
    {
        Port = FindFreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(checkHandler));
    }

    public int Port { get; }

    public string CheckEndpoint => $"http://127.0.0.1:{Port}/v2/check";

    /// <summary>Form body of the last /v2/check request, for asserting on the request shape.</summary>
    public string? LastRequestBody { get; private set; }

    public int RequestCount { get; private set; }

    private async Task AcceptLoopAsync(Func<string, (int Status, string Body)> checkHandler)
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
                var path = context.Request.Url?.AbsolutePath ?? "";
                int status;
                string body;

                if (path.EndsWith("/languages", StringComparison.Ordinal))
                {
                    status = 200;
                    body = """[{"name":"German (Swiss)","code":"de","longCode":"de-CH"}]""";
                }
                else
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    LastRequestBody = await reader.ReadToEndAsync();
                    RequestCount++;
                    (status, body) = checkHandler(LastRequestBody);
                }

                var payload = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload);
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException)
            {
                // Client vanished, nothing to do.
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
}
