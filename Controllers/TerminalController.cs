using System;
using System.IO;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Jellyfin.Plugin.Terminal.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Terminal.Controllers;

public sealed class TerminalInputRequest
{
    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
}

[ApiController]
[Route("Terminal")]
public class TerminalController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly ILogger<TerminalController> _logger;

    public TerminalController(IUserManager userManager, ILogger<TerminalController> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    private bool IsAuthorizedAdmin(string? api_key, string? userId, out string username)
    {
        username = "Unknown";
        if (string.IsNullOrWhiteSpace(api_key) || string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var userGuid))
        {
            return false;
        }

        var user = _userManager.GetUserById(userGuid);
        if (user == null) return false;

        username = user.Username;
        var dto = _userManager.GetUserDto(user, string.Empty);
        return dto?.Policy?.IsAdministrator == true;
    }

    [HttpGet("Stream")]
    public async Task GetStream([FromQuery] string sessionId, [FromQuery] string? api_key, [FromQuery] string? userId)
    {
        if (!IsAuthorizedAdmin(api_key, userId, out var username))
        {
            HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = Guid.NewGuid().ToString("N");
        }

        _logger.LogInformation("Terminal: Stream abierto para {Username} (Sesion: {SessionId})", username, sessionId);

        Response.ContentType = "text/plain; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["X-Accel-Buffering"] = "no";

        var session = TerminalSessionManager.GetOrCreateSession(sessionId, _logger, Plugin.Instance?.Configuration.CustomShellPath);

        try
        {
            await foreach (var chunk in session.Reader.ReadAllAsync(HttpContext.RequestAborted).ConfigureAwait(false))
            {
                await Response.Body.WriteAsync(chunk, HttpContext.RequestAborted).ConfigureAwait(false);
                await Response.Body.FlushAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            TerminalSessionManager.CloseSession(sessionId);
            _logger.LogInformation("Terminal: Stream finalizado (Sesion: {SessionId})", sessionId);
        }
    }

    [HttpPost("Input")]
    public async Task<IActionResult> PostInput([FromQuery] string sessionId, [FromQuery] string? api_key, [FromQuery] string? userId, [FromBody] TerminalInputRequest request)
    {
        if (!IsAuthorizedAdmin(api_key, userId, out _))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrEmpty(request?.Data))
        {
            return BadRequest();
        }

        var session = TerminalSessionManager.GetOrCreateSession(sessionId, _logger, Plugin.Instance?.Configuration.CustomShellPath);
        await session.WriteInputAsync(request.Data).ConfigureAwait(false);

        return Ok();
    }

    [HttpPost("Close")]
    public IActionResult PostClose([FromQuery] string sessionId, [FromQuery] string? api_key, [FromQuery] string? userId)
    {
        if (!IsAuthorizedAdmin(api_key, userId, out _))
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            TerminalSessionManager.CloseSession(sessionId);
        }

        return Ok();
    }
}