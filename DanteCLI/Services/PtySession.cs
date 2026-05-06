using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pty.Net;

namespace DanteCLI.Services;

/// <summary>
/// Wraps a pseudo-console (ConPTY) session. Use <see cref="StartAsync"/> to spawn a shell;
/// subscribe to <see cref="Output"/> for stdout/stderr (interleaved); call <see cref="WriteInput"/>
/// to feed keystrokes.
/// </summary>
public sealed class PtySession : IAsyncDisposable
{
    public event EventHandler<ReadOnlyMemory<byte>>? Output;
    public event EventHandler? Exited;

    private IPtyConnection? _pty;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    public bool IsRunning => _pty is not null;

    public async Task StartAsync(string shell, string[]? args, string? cwd, int cols = 120, int rows = 30)
    {
        if (_pty is not null) throw new InvalidOperationException("Already started");

        var options = new PtyOptions
        {
            Name = "DanteCLI",
            App = shell,
            CommandLine = args ?? Array.Empty<string>(),
            Cols = cols,
            Rows = rows,
            Cwd = cwd ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment = BuildEnvironment(),
        };

        _pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        _cts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoop(_cts.Token));

        _pty.ProcessExited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
    }

    public void Resize(int cols, int rows)
    {
        if (_pty is null) return;
        try { _pty.Resize(cols, rows); } catch { /* ignore */ }
    }

    public void WriteInput(ReadOnlySpan<byte> data)
    {
        if (_pty is null) return;
        _pty.WriterStream.Write(data);
        _pty.WriterStream.Flush();
    }

    public void WriteInput(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        WriteInput(bytes);
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        if (_pty is null) return;
        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try
            {
                n = await _pty.ReaderStream.ReadAsync(buffer.AsMemory(), ct);
            }
            catch (Exception) { break; }
            if (n <= 0) break;
            Output?.Invoke(this, new ReadOnlyMemory<byte>(buffer, 0, n).ToArray());
        }
    }

    private static System.Collections.Generic.Dictionary<string, string> BuildEnvironment()
    {
        var env = new System.Collections.Generic.Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            if (e.Key is string k && e.Value is string v)
                env[k] = v;
        }
        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        env["DANTE_CLI"] = "1";
        return env;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_readLoop is not null) { try { await _readLoop; } catch { } }
        try { _pty?.Dispose(); } catch { }
        _pty = null;
    }
}
