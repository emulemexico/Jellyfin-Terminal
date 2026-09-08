using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Terminal.Services;

public sealed class TerminalSession : IDisposable
{
    private readonly Process _process;
    private readonly CancellationTokenSource _cts;
    private readonly Channel<byte[]> _channel;
    private readonly ILogger _logger;
    private bool _disposed;

    public string SessionId { get; }
    public ChannelReader<byte[]> Reader => _channel.Reader;

    public TerminalSession(string sessionId, ILogger logger, string? customShell = null)
    {
        SessionId = sessionId;
        _logger = logger;
        _cts = new CancellationTokenSource();
        _channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

        string shell;
        string arguments = string.Empty;

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

        _process = new Process { StartInfo = startInfo };

        try
        {
            _process.Start();
            _logger.LogInformation("Terminal: Proceso iniciado [{Shell}] para sesion {SessionId}", shell, sessionId);

            Task.Run(() => ReadStreamLoopAsync(_process.StandardOutput.BaseStream, _cts.Token));
            Task.Run(() => ReadStreamLoopAsync(_process.StandardError.BaseStream, _cts.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Terminal: Error al iniciar shell [{Shell}]", shell);
            var errBytes = Encoding.UTF8.GetBytes($"\r\n\x1b[1;31m[Error al iniciar {shell}: {ex.Message}]\x1b[0m\r\n");
            _channel.Writer.TryWrite(errBytes);
            _channel.Writer.TryComplete();
        }
    }

    private async Task ReadStreamLoopAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read == 0) break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                await _channel.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Terminal: Lectura de stream finalizada");
        }
    }

    public async Task WriteInputAsync(string data)
    {
        if (_disposed || _process.HasExited) return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await _process.StandardInput.BaseStream.WriteAsync(bytes).ConfigureAwait(false);
            await _process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Terminal: Error al escribir entrada");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _channel.Writer.TryComplete();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }

        _process.Dispose();
        _cts.Dispose();
    }
}

public static class TerminalSessionManager
{
    private static readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();

    public static TerminalSession GetOrCreateSession(string sessionId, ILogger logger, string? customShell = null)
    {
        return _sessions.GetOrAdd(sessionId, id => new TerminalSession(id, logger, customShell));
    }

    public static void CloseSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.Dispose();
        }
    }
}