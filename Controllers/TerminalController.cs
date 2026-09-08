using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Terminal.Controllers;

/// <summary>
/// WebSockets controller providing interactive shell access for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Terminal")]
public class TerminalController : ControllerBase
{
    private readonly ILogger<TerminalController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalController"/> class.
    /// </summary>
    public TerminalController(ILogger<TerminalController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// WebSocket endpoint for terminal session.
    /// </summary>
    [HttpGet("Socket")]
    public async Task GetSocket()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await HttpContext.Response.WriteAsync("Se requiere una conexión WebSocket.").ConfigureAwait(false);
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var username = User.Identity?.Name ?? "Admin";
        _logger.LogInformation("Sesión de Terminal iniciada por el administrador {Username}.", username);

        await RunSessionAsync(webSocket, HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private async Task RunSessionAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        string shell;
        string arguments = string.Empty;

        var customShell = Plugin.Instance?.Configuration.CustomShellPath;
        if (!string.IsNullOrWhiteSpace(customShell) && System.IO.File.Exists(customShell))
        {
            shell = customShell;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            shell = Environment.GetEnvironmentVariable("COMSPEC") ?? "powershell.exe";
        }
        else
        {
            shell = System.IO.File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
            arguments = "-i";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                _logger.LogError("No se pudo iniciar el shell: {Shell}", shell);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción al iniciar el shell {Shell}", shell);
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linkedCts.Token;

        var outputTask = Task.Run(() => StreamToWebSocketAsync(process.StandardOutput.BaseStream, webSocket, token), token);
        var errorTask = Task.Run(() => StreamToWebSocketAsync(process.StandardError.BaseStream, webSocket, token), token);
        var inputTask = Task.Run(() => WebSocketToProcessInputAsync(webSocket, process.StandardInput.BaseStream, linkedCts, token), token);

        await Task.WhenAny(outputTask, errorTask, inputTask).ConfigureAwait(false);

        linkedCts.Cancel();

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignorar errores al forzar la salida del proceso
        }

        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Sesión cerrada", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignorar excepciones al cerrar el socket
            }
        }
    }

    private static async Task StreamToWebSocketAsync(Stream stream, WebSocket webSocket, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (!token.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, bytesRead),
                    WebSocketMessageType.Text,
                    true,
                    token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private static async Task WebSocketToProcessInputAsync(WebSocket webSocket, Stream processInput, CancellationTokenSource cts, CancellationToken token)
    {
        var buffer = new byte[2048];
        try
        {
            while (!token.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.Count > 0)
                {
                    await processInput.WriteAsync(buffer.AsMemory(0, result.Count), token).ConfigureAwait(false);
                    await processInput.FlushAsync(token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            cts.Cancel();
        }
    }
}