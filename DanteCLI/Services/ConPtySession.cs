using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace DanteCLI.Services;

/// <summary>
/// Real Win32 ConPTY session. Spawns a child process attached to a pseudoconsole
/// so the child (cmd, powershell, wsl, whatever) believes it has a real TTY:
/// it echoes typed characters, honors VT escape sequences, supports cursor
/// addressing and TUI apps.
///
/// API surface intentionally matches <c>PtySession</c> so call sites only need
/// to swap the type. The <c>Output</c> event delivers the **raw VT byte stream**
/// — consumers must feed it to a real VT emulator (xterm.js in WebView2) rather
/// than strip escape sequences.
///
/// Requires Windows 10 1809 (October 2018) or newer for
/// <c>CreatePseudoConsole</c>.
/// </summary>
public sealed class ConPtySession : IAsyncDisposable
{
    public event EventHandler<ReadOnlyMemory<byte>>? Output;
    public event EventHandler? Exited;

    private SafeFileHandle? _inputWriteSide;     // we write user input here
    private SafeFileHandle? _outputReadSide;     // we read child output here
    private FileStream? _inputStream;            // long-lived; created once
    private FileStream? _outputStream;           // long-lived; created once
    private IntPtr _hPseudoConsole = IntPtr.Zero;
    private IntPtr _attributeList = IntPtr.Zero;
    private PROCESS_INFORMATION _pi;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private Task? _exitWatcher;
    private bool _disposed;
    private int _cols = 120;
    private int _rows = 30;

    public bool IsRunning => _hPseudoConsole != IntPtr.Zero && !_disposed;

    public Task StartAsync(string shell, string[]? args, string? cwd, int cols = 120, int rows = 30)
    {
        if (_hPseudoConsole != IntPtr.Zero)
            throw new InvalidOperationException("Already started");

        _cols = Math.Max(2, cols);
        _rows = Math.Max(2, rows);

        // 1. Two pipes: one for child stdin (we write), one for child stdout/err (we read).
        if (!CreatePipe(out SafeFileHandle inputReadSide, out SafeFileHandle inputWriteSide, IntPtr.Zero, 0))
            throw new InvalidOperationException("CreatePipe(input) failed: " + Marshal.GetLastWin32Error());
        if (!CreatePipe(out SafeFileHandle outputReadSide, out SafeFileHandle outputWriteSide, IntPtr.Zero, 0))
            throw new InvalidOperationException("CreatePipe(output) failed: " + Marshal.GetLastWin32Error());

        // 2. Create the pseudoconsole; it owns the child-facing ends.
        var size = new COORD { X = (short)_cols, Y = (short)_rows };
        int hr = CreatePseudoConsole(size, inputReadSide, outputWriteSide, 0, out IntPtr hPC);
        if (hr != 0)
            throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");
        _hPseudoConsole = hPC;

        // ConPTY duplicates these handles internally — we close ours.
        inputReadSide.Dispose();
        outputWriteSide.Dispose();

        _inputWriteSide = inputWriteSide;
        _outputReadSide = outputReadSide;

        // Long-lived streams over the pipe handles. FileStream takes ownership
        // of the SafeFileHandle by default, so we keep these alive for the
        // lifetime of the session and dispose them in DisposeAsync.
        _inputStream = new FileStream(_inputWriteSide, FileAccess.Write, bufferSize: 1, isAsync: false);
        _outputStream = new FileStream(_outputReadSide, FileAccess.Read, bufferSize: 4096, isAsync: true);

        // 3. STARTUPINFOEX with PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE pointing at hPC.
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = (uint)Marshal.SizeOf<STARTUPINFOEX>();

        IntPtr size2 = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size2);
        _attributeList = Marshal.AllocHGlobal(size2);
        if (!InitializeProcThreadAttributeList(_attributeList, 1, 0, ref size2))
            throw new InvalidOperationException("InitializeProcThreadAttributeList failed");

        if (!UpdateProcThreadAttribute(
                _attributeList,
                0,
                (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPseudoConsole,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            throw new InvalidOperationException("UpdateProcThreadAttribute failed");

        startupInfo.lpAttributeList = _attributeList;

        // 4. Build command line (quoted shell + args).
        var cmdLine = QuoteArg(shell);
        if (args is not null)
            foreach (var a in args)
                cmdLine += " " + QuoteArg(a);

        // 5. Inherit no other handles; child only sees the pseudoconsole pipes.
        if (!CreateProcessW(
                null,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                cwd,
                ref startupInfo,
                out _pi))
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateProcess failed for '{shell}': Win32 error {err}");
        }

        _cts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpOutputAsync(_cts.Token));
        _exitWatcher = Task.Run(WatchExitAsync);

        return Task.CompletedTask;
    }

    public void Resize(int cols, int rows)
    {
        if (_hPseudoConsole == IntPtr.Zero) return;
        _cols = Math.Max(2, cols);
        _rows = Math.Max(2, rows);
        var size = new COORD { X = (short)_cols, Y = (short)_rows };
        ResizePseudoConsole(_hPseudoConsole, size);
    }

    public void WriteInput(ReadOnlySpan<byte> data)
    {
        var stream = _inputStream;
        if (stream is null) return;
        try
        {
            stream.Write(data);
            stream.Flush();
        }
        catch (IOException) { /* pipe closed */ }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    public void WriteInput(string text) => WriteInput(Encoding.UTF8.GetBytes(text));

    private async Task PumpOutputAsync(CancellationToken ct)
    {
        var stream = _outputStream;
        if (stream is null) return;
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }

            if (n <= 0) break;
            var copy = new byte[n];
            Buffer.BlockCopy(buffer, 0, copy, 0, n);
            try { Output?.Invoke(this, new ReadOnlyMemory<byte>(copy)); } catch { }
        }
    }

    private async Task WatchExitAsync()
    {
        if (_pi.hProcess == IntPtr.Zero) return;
        await Task.Run(() => WaitForSingleObject(_pi.hProcess, INFINITE)).ConfigureAwait(false);
        try { Exited?.Invoke(this, EventArgs.Empty); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();

        try
        {
            if (_hPseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsole(_hPseudoConsole);
                _hPseudoConsole = IntPtr.Zero;
            }
        }
        catch { }

        if (_pumpTask is not null) { try { await _pumpTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { } }
        if (_exitWatcher is not null) { try { await _exitWatcher.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { } }

        try { _inputStream?.Dispose(); } catch { }
        try { _outputStream?.Dispose(); } catch { }
        _inputStream = null;
        _outputStream = null;
        // FileStream owns the SafeFileHandle so disposing the stream already
        // closed it; null out the refs so we don't double-dispose.
        _inputWriteSide = null;
        _outputReadSide = null;

        if (_attributeList != IntPtr.Zero)
        {
            try { DeleteProcThreadAttributeList(_attributeList); } catch { }
            try { Marshal.FreeHGlobal(_attributeList); } catch { }
            _attributeList = IntPtr.Zero;
        }

        if (_pi.hProcess != IntPtr.Zero)
        {
            try { TerminateProcess(_pi.hProcess, 0); } catch { }
            try { CloseHandle(_pi.hThread); } catch { }
            try { CloseHandle(_pi.hProcess); } catch { }
            _pi = default;
        }
    }

    private static string QuoteArg(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
        var sb = new StringBuilder("\"");
        for (int i = 0; i < s.Length; i++)
        {
            int backslashes = 0;
            while (i < s.Length && s[i] == '\\') { backslashes++; i++; }
            if (i == s.Length)
            {
                sb.Append('\\', backslashes * 2);
                break;
            }
            if (s[i] == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(s[i]);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ---------- Win32 P/Invoke ----------

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint INFINITE = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr Attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
}
