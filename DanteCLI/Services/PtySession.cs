using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanteCLI.Services;

/// <summary>
/// Minimal terminal session backed by <see cref="Process"/> with redirected stdin/stdout/stderr.
/// **No ConPTY yet** — interactive TUIs that draw via cursor positioning won't render correctly.
/// Designed to be RELIABLE for non-interactive shell commands and basic prompts.
/// Replace with proper ConPTY (CreatePseudoConsole P/Invoke) for full TUI support.
/// </summary>
public sealed class PtySession : IAsyncDisposable
{
    public event EventHandler<ReadOnlyMemory<byte>>? Output;
    public event EventHandler? Exited;

    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task? _readStdout;
    private Task? _readStderr;
    private bool _disposed;

    public bool IsRunning => _process is { HasExited: false } && !_disposed;

    public Task StartAsync(string shell, string[]? args, string? cwd, int cols = 120, int rows = 30)
    {
        if (_process is not null) throw new InvalidOperationException("Already started");

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = cwd ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (args is not null)
            foreach (var a in args) psi.ArgumentList.Add(a);

        psi.Environment["TERM"] = "dumb"; // ask shells not to draw fancy prompts
        psi.Environment["COLORTERM"] = "";
        psi.Environment["DANTE_CLI"] = "1";
        psi.Environment["NO_COLOR"] = "1";

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Could not spawn shell");
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            try { Exited?.Invoke(this, EventArgs.Empty); } catch { }
        };

        _cts = new CancellationTokenSource();
        _readStdout = Task.Run(() => PumpAsync(_process.StandardOutput.BaseStream, _cts.Token));
        _readStderr = Task.Run(() => PumpAsync(_process.StandardError.BaseStream, _cts.Token));

        return Task.CompletedTask;
    }

    public void Resize(int cols, int rows) { /* no-op without ConPTY */ }

    public void WriteInput(ReadOnlySpan<byte> data)
    {
        if (_process is null || _process.HasExited) return;
        try
        {
            _process.StandardInput.BaseStream.Write(data);
            _process.StandardInput.BaseStream.Flush();
        }
        catch (IOException) { /* pipe closed */ }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    public void WriteInput(string text) => WriteInput(Encoding.UTF8.GetBytes(text));

    private async Task PumpAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = await stream.ReadAsync(buffer.AsMemory(), ct); }
            catch { break; }
            if (n <= 0) break;
            var copy = new byte[n];
            Buffer.BlockCopy(buffer, 0, copy, 0, n);
            try { Output?.Invoke(this, new ReadOnlyMemory<byte>(copy)); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        if (_readStdout is not null) { try { await _readStdout.WaitAsync(TimeSpan.FromSeconds(1)); } catch { } }
        if (_readStderr is not null) { try { await _readStderr.WaitAsync(TimeSpan.FromSeconds(1)); } catch { } }
        try { if (_process is not null && !_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { }
        try { _process?.Dispose(); } catch { }
        _process = null;
    }
}
