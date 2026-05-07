using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using DanteCLI.Models;
using DanteCLI.Services;

namespace DanteCLI.Views;

public partial class TerminalView : UserControl
{
    private PtySession? _session;
    private TerminalTab? _tab;
    private Paragraph _paragraph = new();

    public TerminalView()
    {
        InitializeComponent();
        OutputView.Document.Blocks.Clear();
        OutputView.Document.Blocks.Add(_paragraph);
        Loaded += async (_, _) => await EnsureStartedAsync();
        Unloaded += async (_, _) => await StopAsync();
        InputCapture.Focusable = true;
        Focus();
    }

    public void Bind(TerminalTab tab) => _tab = tab;

    private async System.Threading.Tasks.Task EnsureStartedAsync()
    {
        if (_session is not null || _tab is null) return;
        _session = new PtySession();
        _session.Output += OnPtyOutput;
        _session.Exited += (_, _) => Dispatcher.Invoke(() => Append("\n[processo encerrado]\n"));
        await _session.StartAsync(ResolveShell(), null, _tab.WorkingDirectory);
        if (!string.IsNullOrEmpty(_tab.InitialCommand))
            _session.WriteInput(_tab.InitialCommand + "\r");
        InputCapture.Focus();
    }

    private async System.Threading.Tasks.Task StopAsync()
    {
        if (_session is null) return;
        await _session.DisposeAsync();
        _session = null;
    }

    private static string ResolveShell()
    {
        var configured = ViewModels.AppState.Shared.Settings.DefaultShell;
        if (!string.IsNullOrWhiteSpace(configured) && System.IO.File.Exists(configured)) return configured;

        var pwsh = Environment.ExpandEnvironmentVariables("%ProgramFiles%\\PowerShell\\7\\pwsh.exe");
        if (System.IO.File.Exists(pwsh)) return pwsh;

        var winPwsh = Environment.ExpandEnvironmentVariables(
            "%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe");
        if (System.IO.File.Exists(winPwsh)) return winPwsh;

        return Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\cmd.exe");
    }

    private static readonly Regex AnsiRegex =
        new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07|\x1B[@-_]", RegexOptions.Compiled);

    private void OnPtyOutput(object? sender, ReadOnlyMemory<byte> data)
    {
        var text = Encoding.UTF8.GetString(data.Span);
        var clean = AnsiRegex.Replace(text, "");
        Dispatcher.Invoke(() => Append(clean));
    }

    private void Append(string text)
    {
        _paragraph.Inlines.Add(new Run(text));
        Scroller.ScrollToEnd();
    }

    private void InputCapture_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        _session?.WriteInput(e.Text);
        e.Handled = true;
    }

    private void InputCapture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session is null) return;
        switch (e.Key)
        {
            case Key.Enter:  _session.WriteInput("\r"); e.Handled = true; break;
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

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Default scroll behavior is fine. Let it bubble.
    }
}
