using System.Net;

namespace MakroChef.Mcp.OAuth;

/// <summary>Catches the browser redirect at the end of /authorize on a loopback HTTP listener,
/// per RFC 8252 (native app OAuth). Disposable so the port is always released.</summary>
public sealed class LoopbackRedirectListener : IDisposable
{
    private HttpListener? _listener;

    public string Start()
    {
        var port = GetFreeLoopbackPort();
        var prefix = $"http://127.0.0.1:{port}/callback/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        return prefix;
    }

    public async Task<string> WaitForCodeAsync(string expectedState, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_listener is null)
        {
            throw new InvalidOperationException("Call Start() first.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        HttpListenerContext context;
        try
        {
            var contextTask = _listener.GetContextAsync();
            await using (cts.Token.Register(() => _listener.Stop()))
            {
                context = await contextTask;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            throw new TimeoutException($"Не отримано редірект від OAuth-сервера за {timeout.TotalSeconds:F0}с.");
        }

        var query = context.Request.QueryString;
        var error = query["error"];
        var code = query["code"];
        var state = query["state"];

        var responseBody = error is null
            ? "<html><body>Готово, можна закрити цю вкладку.</body></html>"
            : $"<html><body>Помилка авторизації: {error}</body></html>";

        var buffer = System.Text.Encoding.UTF8.GetBytes(responseBody);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.OutputStream.Close();

        if (error is not null)
        {
            throw new InvalidOperationException($"OAuth-сервер повернув помилку: {error}");
        }

        if (state != expectedState)
        {
            throw new InvalidOperationException("Параметр state не збігається — можлива CSRF-атака, зупиняюсь.");
        }

        return code ?? throw new InvalidOperationException("Редірект не містив параметра code.");
    }

    private static int GetFreeLoopbackPort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    public void Dispose()
    {
        if (_listener is { IsListening: true })
        {
            _listener.Stop();
        }

        _listener?.Close();
    }
}
