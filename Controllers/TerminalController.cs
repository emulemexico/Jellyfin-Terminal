using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Terminal.Controllers;

/// <summary>
/// WebSockets controller providing interactive shell access for administrators.
/// </summary>
[ApiController]
[Route("Terminal")]
public class TerminalController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly ILogger<TerminalController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalController"/> class.
    /// </summary>
    public TerminalController(IUserManager userManager, ILogger<TerminalController> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// WebSocket endpoint for terminal session.
    /// </summary>
    [HttpGet("Socket")]
    public async Task GetSocket([FromQuery] string? api_key, [FromQuery] string? userId)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await HttpContext.Response.WriteAsync("Se requiere una conexion WebSocket.").ConfigureAwait(false);
            return;
        }

        // 1. Validar parametros de conexion
        if (string.IsNullOrWhiteSpace(api_key) || string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var userGuid))
        {
            _logger.LogWarning("Terminal: intento de conexion rechazado por credenciales ausentes o invalidas.");
            HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        // 2. Obtener usuario y verificar permisos de Administrador
        var user = _userManager.GetUserById(userGuid);
        if (user == null)
        {
            _logger.LogWarning("Terminal: usuario {UserId} no encontrado.", userId);
            HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        var userDto = _userManager.GetUserDto(user, string.Empty);
        if (userDto?.Policy?.IsAdministrator != true)
        {
            _logger.LogWarning("Terminal: acceso denegado. El usuario {Username} ({UserId}) no es administrador.", user.Username, userId);
            HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        // 3. Aceptar conexion WebSocket
        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        _logger.LogInformation("Terminal: conexion WebSocket aceptada para el administrador {Username}.", user.Username);

        await RunSessionAsync(webSocket, user.Username, HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private async Task RunSessionAsync(WebSocket webSocket, string username, CancellationToken cancellationToken)
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
            shell = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            arguments = "";
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
                var errMsg = $"\r\n\x1b[1;31m[Error: no se pudo iniciar el shell {shell}]\x1b[0m\r\n";
                await SendTextAsync(webSocket, errMsg, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Terminal: excepcion al iniciar el shell {Shell}", shell);
            var errMsg = $"\r\n\x1b[1;31m[Error al iniciar shell {shell}: {ex.Message}]\x1b[0m\r\n";
            await SendTextAsync(webSocket, errMsg, cancellationToken).ConfigureAwait(false);
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
            // Ignorar errores al terminar el proceso
        }

        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Sesion finalizada", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignorar errores al cerrar el websocket
            }
        }
    }

    private static async Task SendTextAsync(WebSocket webSocket, string text, CancellationToken token)
    {
        if (webSocket.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
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