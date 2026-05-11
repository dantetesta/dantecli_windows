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
///
/// Local-echo line discipline: because we use redirected pipes (no ConPTY), cmd.exe
/// does NOT echo typed characters back. So we maintain a per-line input buffer,
/// paint typed chars locally, and only flush the completed line to the child on Enter.
/// Backspace/Home/End edit the local buffer; arrow keys are ignored in this mode
/// (cmd doesn't honor them on piped stdin anyway).
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

    // Local-echo line discipline (see class summary)
    private readonly StringBuilder _inputLine = new();
    private int _inputAnchor; // index in _buffer where current input line begins

    public TerminalView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModels.AppState.Shared.InjectIntoActiveTerminal += OnInject;
    }

    private void OnInject(object? sender, string text)
    {
        if (ViewModels.AppState.Shared.ActiveTab == _tab && _session is not null)
            Dispatcher.BeginInvoke(new Action(() => _session?.WriteInput(text)));
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
        // Need the dispatcher loop to run once before focus can land reliably.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            OutputView.Focus();
            Keyboard.Focus(OutputView);
            OutputView.CaretIndex = OutputView.Text.Length;
        }), DispatcherPriority.Input);
    }

    private void UserControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Any click inside the terminal area refocuses the OutputView so typing works.
        OutputView.Focus();
        Keyboard.Focus(OutputView);
    }

    private void Output_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Caret always at the end so PageUp / arrow nav doesn't desync from input.
        OutputView.CaretIndex = OutputView.Text.Length;
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _flushTimer?.Stop();
        ViewModels.AppState.Shared.InjectIntoActiveTerminal -= OnInject;
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
        // Anchor follows the end of PTY output. If user was mid-type, their
        // local-echoed chars stay in _inputLine and will be flushed on Enter;
        // visually the typed chars appeared before this output, which is the
        // best we can do without a real terminal grid.
        _inputAnchor = _buffer.Length;
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
            var trim = _buffer.Length - keep;
            _buffer.Remove(0, trim);
            _inputAnchor = Math.Max(0, _inputAnchor - trim);
        }
        OutputView.Text = _buffer.ToString();
        OutputView.CaretIndex = OutputView.Text.Length;
        OutputView.ScrollToEnd();
    }

    // -------- Input pipeline (local-echo line discipline) --------

    private void Output_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_session is null) { e.Handled = true; return; }
        var text = e.Text;
        if (!string.IsNullOrEmpty(text))
        {
            _inputLine.Append(text);
            AppendEcho(text);
        }
        e.Handled = true;
    }

    private void Output_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session is null) { e.Handled = true; return; }

        // Ctrl-C copies selection if any; else interrupt the child.
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
                    _session.WriteInput("\x03");
                    _inputLine.Clear();
                    AppendEcho("^C\n");
                    _inputAnchor = _buffer.Length;
                    e.Handled = true;
                    return;
                case Key.V:
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            var pasted = Clipboard.GetText();
                            // Split on newline — send completed lines, keep tail in buffer.
                            var pieces = pasted.Replace("\r\n", "\n").Split('\n');
                            for (int i = 0; i < pieces.Length; i++)
                            {
                                _inputLine.Append(pieces[i]);
                                AppendEcho(pieces[i]);
                                if (i < pieces.Length - 1) SubmitLine();
                            }
                        }
                    }
                    catch { }
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Enter:
                SubmitLine();
                e.Handled = true;
                break;
            case Key.Back:
                if (_inputLine.Length > 0)
                {
                    _inputLine.Length--;
                    if (_buffer.Length > _inputAnchor)
                    {
                        _buffer.Length--;
                        OutputView.Text = _buffer.ToString();
                        OutputView.CaretIndex = OutputView.Text.Length;
                    }
                }
                e.Handled = true;
                break;
            case Key.Tab:
                _inputLine.Append('\t');
                AppendEcho("    ");
                e.Handled = true;
                break;
            case Key.Escape:
                // Drop current line.
                if (_inputLine.Length > 0)
                {
                    if (_buffer.Length >= _inputAnchor)
                        _buffer.Length = _inputAnchor;
                    _inputLine.Clear();
                    OutputView.Text = _buffer.ToString();
                    OutputView.CaretIndex = OutputView.Text.Length;
                }
                e.Handled = true;
                break;
            // Arrow keys / Home / End / Delete are ignored without ConPTY —
            // cmd.exe on piped stdin doesn't honor them and there's no
            // meaningful local action that would line up with the child's state.
            case Key.Up:
            case Key.Down:
            case Key.Left:
            case Key.Right:
            case Key.Home:
            case Key.End:
            case Key.Delete:
                e.Handled = true;
                break;
        }
    }

    private void SubmitLine()
    {
        if (_session is null) return;
        var line = _inputLine.ToString();
        _inputLine.Clear();
        _session.WriteInput(line + "\r\n");
        AppendEcho("\n");
        _inputAnchor = _buffer.Length;
    }

    /// <summary>
    /// Append text from local echo (typed chars) directly to the display buffer
    /// without going through the PTY flush path. Keeps caret pinned at the end.
    /// </summary>
    private void AppendEcho(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _buffer.Append(text);
        if (_buffer.Length > MaxBufferLength)
        {
            var keep = (int)(MaxBufferLength * 0.8);
            var trim = _buffer.Length - keep;
            _buffer.Remove(0, trim);
            _inputAnchor = Math.Max(0, _inputAnchor - trim);
        }
        OutputView.Text = _buffer.ToString();
        OutputView.CaretIndex = OutputView.Text.Length;
        OutputView.ScrollToEnd();
    }
}
