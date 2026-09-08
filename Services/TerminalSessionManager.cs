using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Terminal.Services;

public sealed class TerminalSession
{
    public string SessionId { get; }
    public string CurrentDirectory { get; set; }

    public TerminalSession(string sessionId)
    {
        SessionId = sessionId;
        CurrentDirectory = Directory.GetCurrentDirectory();
    }

    public async Task<(string Output, string Error, int ExitCode, string NewCwd)> ExecuteCommandAsync(string command, string? customShell, ILogger logger)
    {
        command = command.Trim();

        if (string.IsNullOrEmpty(command))
        {
            return (string.Empty, string.Empty, 0, CurrentDirectory);
        }

        // Manejo nativo de comando cd
        if (command.Equals("cd", StringComparison.OrdinalIgnoreCase) || command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            var targetDir = command.Length > 3 ? command.Substring(3).Trim() : string.Empty;
            if (string.IsNullOrEmpty(targetDir))
            {
                targetDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(targetDir)) targetDir = "/";
            }
            else if (targetDir.StartsWith("~"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                targetDir = Path.Combine(home, targetDir.Substring(1).TrimStart('/', '\\'));
            }

            try
            {
                var resolved = Path.GetFullPath(Path.Combine(CurrentDirectory, targetDir));
                if (Directory.Exists(resolved))
                {
                    CurrentDirectory = resolved;
                    return (string.Empty, string.Empty, 0, CurrentDirectory);
                }
                else
                {
                    return (string.Empty, $"cd: no existe el directorio: {targetDir}\r\n", 1, CurrentDirectory);
                }
            }
            catch (Exception ex)
            {
                return (string.Empty, $"cd: {ex.Message}\r\n", 1, CurrentDirectory);
            }
        }

        string shell;
        string args;

        if (!string.IsNullOrWhiteSpace(customShell) && File.Exists(customShell))
        {
            shell = customShell;
            args = $"-c \"{command.Replace("\"", "\\\"")}\"";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            shell = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            args = $"/c {command}";
        }
        else
        {
            shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
            args = $"-c \"{command.Replace("\"", "\\\"")}\"";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            WorkingDirectory = CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outTask, errTask, process.WaitForExitAsync()).ConfigureAwait(false);

            return (outTask.Result, errTask.Result, process.ExitCode, CurrentDirectory);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Terminal: Error al ejecutar comando [{Command}]", command);
            return (string.Empty, $"Error al ejecutar comando: {ex.Message}\r\n", -1, CurrentDirectory);
        }
    }
}

public static class TerminalSessionManager
{
    private static readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();

    public static TerminalSession GetOrCreateSession(string sessionId)
    {
        return _sessions.GetOrAdd(sessionId, id => new TerminalSession(id));
    }
}