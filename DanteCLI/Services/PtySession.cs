using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanteCLI.ViewModels;

namespace DanteCLI.Services;

/// <summary>
/// Minimal terminal session backed by <see cref="Process"/> with redirected stdin/stdout/stderr.
/// **No ConPTY** in this MVP — interactive TUIs (htop, vim, nano) won't render correctly.
/// Replace with a proper ConPTY implementation (Pty.Net, or hand-rolled CreatePseudoConsole P/Invoke)
/// once the rest of the app is solid.
/// </summary>
public sealed class PtySession : IAsyncDisposable
{
    public event EventHandler<ReadOnlyMemory<byte>>? Output;
    public event EventHandler? Exited;

    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task? _readStdout;
    private Task? _readStderr;

    public bool IsRunning => _process is { HasExited: false };

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

        psi.Environment["TERM"] = "xterm-256color";
        psi.Environment["COLORTERM"] = "truecolor";
        psi.Environment["DANTE_CLI"] = "1";

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Could not spawn shell");
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);

        _cts = new CancellationTokenSource();
        _readStdout = Task.Run(() => PumpAsync(_process.StandardOutput.BaseStream, _cts.Token));
        _readStderr = Task.Run(() => PumpAsync(_process.StandardError.BaseStream, _cts.Token));

        return Task.CompletedTask;
    }

    public void Resize(int cols, int rows)
    {
        // No-op until ConPTY: redirected pipes don't have a concept of TTY size.
    }

    public void WriteInput(ReadOnlySpan<byte> data)
    {
        if (_process is null) return;
        try
        {
            _process.StandardInput.BaseStream.Write(data);
            _process.StandardInput.BaseStream.Flush();
        }
        catch (IOException) { /* pipe closed */ }
    }

    public void WriteInput(string text) => WriteInput(Encoding.UTF8.GetBytes(text));

    private async Task PumpAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = await stream.ReadAsync(buffer.AsMemory(), ct); }
            catch { break; }
            if (n <= 0) break;
            var copy = new byte[n];
            Buffer.BlockCopy(buffer, 0, copy, 0, n);
            Output?.Invoke(this, new ReadOnlyMemory<byte>(copy));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_readStdout is not null) { try { await _readStdout; } catch { } }
        if (_readStderr is not null) { try { await _readStderr; } catch { } }
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
    }
}
