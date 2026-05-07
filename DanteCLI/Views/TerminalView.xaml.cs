using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DanteCLI.Models;
using DanteCLI.Services;

namespace DanteCLI.Views;

/// <summary>
/// Terminal output view backed by a single <see cref="TextBox"/>. Output is buffered
/// in a <see cref="StringBuilder"/> and flushed to the UI on a 33ms timer to keep
/// the dispatcher responsive even with chatty shells. Control characters (CR/LF/BS)
/// and ANSI escape sequences are normalized; this is **not** a full VT emulator —
/// it's the smallest renderer that produces legible output for cmd.exe and most
/// non-interactive shell commands.
/// </summary>
public partial class TerminalView : UserControl
{
    private const int MaxBufferLength = 200_000;

    private PtySession? _session;
    private TerminalTab? _tab;
    private readonly StringBuilder _buffer = new();
    private readonly object _pendingLock = new();
    private string _pending = string.Empty;
    private DispatcherTimer? _flushTimer;

    public TerminalView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Bind(TerminalTab tab) => _tab = tab;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();

        await EnsureStartedAsync();
        OutputView.Focus();
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _flushTimer?.Stop();
        await StopAsync();
    }

    public async Task ForceShutdownAsync()
    {
        _flushTimer?.Stop();
        await StopAsync();
    }

    private async Task EnsureStartedAsync()
    {
        if (_session is not null || _tab is null) return;
        _session = new PtySession();
        _session.Output += OnPtyOutput;
        _session.Exited += (_, _) => Append("\n[processo encerrado]\n");

        var shell = ResolveShell();
        try
        {
            await _session.StartAsync(shell, args: null, cwd: _tab.WorkingDirectory);
            if (!string.IsNullOrEmpty(_tab.InitialCommand))
                _session.WriteInput(_tab.InitialCommand + "\r\n");
        }
        catch (Exception ex)
        {
            Append($"\n[erro ao iniciar shell: {ex.Message}]\n");
        }
    }

    private async Task StopAsync()
    {
        if (_session is null) return;
        var s = _session;
        _session = null;
        try { await s.DisposeAsync(); } catch { }
    }

    private static string ResolveShell()
    {
        var configured = ViewModels.AppState.Shared.Settings.DefaultShell;
        if (!string.IsNullOrWhiteSpace(configured) && System.IO.File.Exists(configured))
            return configured;
        // cmd.exe is more reliable than pwsh without ConPTY.
        return Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\cmd.exe");
    }

    // -------- Output pipeline --------

    private void OnPtyOutput(object? sender, ReadOnlyMemory<byte> data)
    {
        var text = Encoding.UTF8.GetString(data.Span);
        lock (_pendingLock) { _pending += text; }
    }

    private void FlushPending()
    {
        string chunk;
        lock (_pendingLock)
        {
            if (_pending.Length == 0) return;
            chunk = _pending;
            _pending = string.Empty;
        }
        Append(NormalizeForDisplay(chunk));
    }

    private static readonly Regex AnsiEscape = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(\x07|\x1B\\)|\x1B[@-_]",
        RegexOptions.Compiled);

    private static string NormalizeForDisplay(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // 1. Strip ANSI / OSC / CSI escape sequences.
        s = AnsiEscape.Replace(s, "");

        // 2. CRLF normalization
        s = s.Replace("\r\n", "\n");

        // 3. Process backspace (\b) by removing the previous character.
        if (s.Contains('\b') || s.Contains('\r') || s.IndexOf('') >= 0)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\b':
                        if (sb.Length > 0 && sb[^1] != '\n') sb.Length--;
                        break;
                    case '\r':
                        // Standalone CR — eat. Real terminals would move cursor to col 0;
                        // for our flat text rendering, dropping it produces sane output.
                        break;
                    case '': // BEL
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            s = sb.ToString();
        }

        // 4. Strip remaining non-printables except tab/newline.
        var clean = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\n' || c == '\t' || !char.IsControl(c)) clean.Append(c);
        }
        return clean.ToString();
    }

    private void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _buffer.Append(text);
        if (_buffer.Length > MaxBufferLength)
        {
            // Trim from start to last 80% of MaxBufferLength to avoid frequent trims.
            var keep = (int)(MaxBufferLength * 0.8);
            _buffer.Remove(0, _buffer.Length - keep);
        }
        OutputView.Text = _buffer.ToString();
        OutputView.CaretIndex = OutputView.Text.Length;
        OutputView.ScrollToEnd();
    }

    // -------- Input pipeline --------

    private void Output_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_session is null) { e.Handled = true; return; }
        _session.WriteInput(e.Text);
        e.Handled = true;
    }

    private void Output_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session is null) { e.Handled = true; return; }

        // Allow Ctrl+C / Ctrl+V to flow through for copy/paste of selection.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.C:
                    if (!string.IsNullOrEmpty(OutputView.SelectedText))
                    {
                        try { Clipboard.SetText(OutputView.SelectedText); } catch { }
                        e.Handled = true;
                        return;
                    }
                    // No selection — send Ctrl+C interrupt to shell.
                    _session.WriteInput("\x03");
                    e.Handled = true;
                    return;
                case Key.V:
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            var text = Clipboard.GetText();
                            _session.WriteInput(text);
                        }
                    }
                    catch { }
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Enter:  _session.WriteInput("\r\n"); e.Handled = true; break;
            case Key.Back:   _session.WriteInput("\b"); e.Handled = true; break;
            case Key.Tab:    _session.WriteInput("\t"); e.Handled = true; break;
            case Key.Escape: _session.WriteInput("\x1B"); e.Handled = true; break;
            case Key.Up:     _session.WriteInput("\x1B[A"); e.Handled = true; break;
            case Key.Down:   _session.WriteInput("\x1B[B"); e.Handled = true; break;
            case Key.Right:  _session.WriteInput("\x1B[C"); e.Handled = true; break;
            case Key.Left:   _session.WriteInput("\x1B[D"); e.Handled = true; break;
            case Key.Home:   _session.WriteInput("\x1B[H"); e.Handled = true; break;
            case Key.End:    _session.WriteInput("\x1B[F"); e.Handled = true; break;
            case Key.Delete: _session.WriteInput("\x1B[3~"); e.Handled = true; break;
        }
    }
}
