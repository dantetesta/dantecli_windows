using System;
using System.Text;
using System.Text.RegularExpressions;
using DanteCLI.Models;
using DanteCLI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.System;

namespace DanteCLI.Views;

/// <summary>
/// Minimal terminal: spawns a shell via ConPTY, renders raw output stripped of most
/// ANSI escape sequences. Phase 1 — no full VT emulation. Replace with a real VT
/// engine (e.g. VtNetCore) once core flows work.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    private PtySession? _session;
    private TerminalTab? _tab;
    private readonly DispatcherQueue _dispatcher;

    public TerminalView()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Loaded += async (_, _) => await EnsureStartedAsync();
        Unloaded += async (_, _) => await StopAsync();
    }

    public void Bind(TerminalTab tab)
    {
        _tab = tab;
    }

    public async System.Threading.Tasks.Task EnsureStartedAsync()
    {
        if (_session is not null || _tab is null) return;
        _session = new PtySession();
        _session.Output += OnPtyOutput;
        _session.Exited += (_, _) => _dispatcher.TryEnqueue(() =>
            AppendText("\n[processo encerrado]\n"));

        var shell = ResolveShell();
        await _session.StartAsync(shell, args: null, cwd: _tab.WorkingDirectory);

        if (!string.IsNullOrEmpty(_tab.InitialCommand))
        {
            _session.WriteInput($"{_tab.InitialCommand}\r");
        }

        InputCapture.Focus(FocusState.Programmatic);
    }

    public async System.Threading.Tasks.Task StopAsync()
    {
        if (_session is null) return;
        await _session.DisposeAsync();
        _session = null;
    }

    private static string ResolveShell()
    {
        var configured = AppState.Shared.Settings.DefaultShell;
        if (!string.IsNullOrWhiteSpace(configured) && System.IO.File.Exists(configured))
            return configured;

        var pwsh = Environment.ExpandEnvironmentVariables("%ProgramFiles%\\PowerShell\\7\\pwsh.exe");
        if (System.IO.File.Exists(pwsh)) return pwsh;

        var winPwsh = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe");
        if (System.IO.File.Exists(winPwsh)) return winPwsh;

        return Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\cmd.exe");
    }

    private void OnPtyOutput(object? sender, ReadOnlyMemory<byte> data)
    {
        var text = Encoding.UTF8.GetString(data.Span);
        var clean = StripAnsi(text);
        _dispatcher.TryEnqueue(() => AppendText(clean));
    }

    private static readonly Regex _ansiRegex =
        new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07|\x1B[@-_]", RegexOptions.Compiled);

    private static string StripAnsi(string s) => _ansiRegex.Replace(s, "");

    private void AppendText(string text)
    {
        OutputParagraph.Inlines.Add(new Run { Text = text });
        Scroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
    }

    private void InputCapture_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        // Forward typed characters to the PTY, then suppress the actual TextBox edit.
        if (args.NewText.Length > sender.Text.Length)
        {
            var diff = args.NewText[sender.Text.Length..];
            _session?.WriteInput(diff);
        }
        args.Cancel = true;
        sender.Text = "";
    }

    private void InputCapture_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_session is null) return;
        var key = e.Key;

        switch (key)
        {
            case VirtualKey.Enter: _session.WriteInput("\r"); e.Handled = true; break;
            case VirtualKey.Back:  _session.WriteInput(""); e.Handled = true; break;
            case VirtualKey.Tab:   _session.WriteInput("\t"); e.Handled = true; break;
            case VirtualKey.Escape: _session.WriteInput(""); e.Handled = true; break;
            case VirtualKey.Up:    _session.WriteInput("[A"); e.Handled = true; break;
            case VirtualKey.Down:  _session.WriteInput("[B"); e.Handled = true; break;
            case VirtualKey.Right: _session.WriteInput("[C"); e.Handled = true; break;
            case VirtualKey.Left:  _session.WriteInput("[D"); e.Handled = true; break;
            case VirtualKey.Home:  _session.WriteInput("[H"); e.Handled = true; break;
            case VirtualKey.End:   _session.WriteInput("[F"); e.Handled = true; break;
            case VirtualKey.Delete: _session.WriteInput("[3~"); e.Handled = true; break;
        }
    }
}
