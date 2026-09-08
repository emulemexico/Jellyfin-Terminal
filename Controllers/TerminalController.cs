using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Jellyfin.Plugin.Terminal.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Terminal.Controllers;

public sealed class TerminalInputRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;

    public string GetText() => !string.IsNullOrEmpty(Command) ? Command : Data;
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

    [HttpGet("Info")]
    public IActionResult GetInfo([FromQuery] string? sessionId, [FromQuery] string? api_key, [FromQuery] string? userId)
    {
        if (!IsAuthorizedAdmin(api_key, userId, out var username))
        {
            return Forbid();
        }

        var session = TerminalSessionManager.GetOrCreateSession(sessionId ?? "default");
        var os = RuntimeInformation.OSDescription;

        return Ok(new
        {
            os = os,
            cwd = session.CurrentDirectory,
            user = username,
            isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        });
    }

    [HttpPost("Execute")]
    public async Task<IActionResult> Execute([FromQuery] string? sessionId, [FromQuery] string? api_key, [FromQuery] string? userId, [FromBody] TerminalInputRequest? request)
    {
        if (!IsAuthorizedAdmin(api_key, userId, out _))
        {
            return Forbid();
        }

        var session = TerminalSessionManager.GetOrCreateSession(sessionId ?? "default");
        var commandText = request?.GetText() ?? string.Empty;

        var result = await session.ExecuteCommandAsync(commandText, Plugin.Instance?.Configuration.CustomShellPath, _logger).ConfigureAwait(false);

        return Ok(new
        {
            output = result.Output,
            error = result.Error,
            exitCode = result.ExitCode,
            cwd = result.NewCwd
        });
    }
}