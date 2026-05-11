using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DanteCLI.Models;
using DanteCLI.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DanteCLI.Views;

/// <summary>
/// Terminal view backed by a real Win32 ConPTY (so the child shell echoes
/// natively and TUI apps work) and rendered inside WebView2 hosting xterm.js
/// (a full VT/ANSI terminal emulator).
///
/// Lifecycle:
///   1. ctor — start WebView2 init in the background.
///   2. CoreWebView2 ready — map virtual host, navigate to terminal.html.
///   3. JS posts {type:"ready", cols, rows} — start ConPTY with those dimensions.
///   4. ConPTY output → batched, base64'd, fed to xterm via ExecuteScriptAsync.
///   5. JS posts {type:"input", data} → forwarded raw to ConPTY stdin.
///   6. JS posts {type:"resize", cols, rows} → ResizePseudoConsole.
/// </summary>
public partial class TerminalView : UserControl
{
    private const string VirtualHost = "danteterm.local";

    private TerminalTab? _tab;
    private ConPtySession? _session;
    private bool _terminalReady;
    private int _cols = 120;
    private int _rows = 30;

    // Output batching: ConPTY emits many tiny chunks; batching cuts round-trips
    // from C# to JS dramatically without adding perceptible latency.
    private readonly ConcurrentQueue<byte[]> _outQueue = new();
    private DispatcherTimer? _flushTimer;
    private volatile bool _flushScheduled;

    public TerminalView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModels.AppState.Shared.InjectIntoActiveTerminal += OnInject;
    }

    public void Bind(TerminalTab tab) => _tab = tab;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            await InitWebViewAsync();
        }
        catch (Exception ex)
        {
            ShowFatal("Falha ao inicializar WebView2: " + ex.Message
                + "\n\nVerifique se o WebView2 Runtime está instalado (Win11/Win10 21H2+ já vem)."
                + "\nDownload manual: https://go.microsoft.com/fwlink/p/?LinkId=2124703");
        }
    }

    public async Task ForceShutdownAsync()
    {
        _flushTimer?.Stop();
        await StopSessionAsync().ConfigureAwait(false);
        try { WebView?.Dispose(); } catch { }
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ViewModels.AppState.Shared.InjectIntoActiveTerminal -= OnInject;
        _flushTimer?.Stop();
        await StopSessionAsync().ConfigureAwait(false);
    }

    private void OnInject(object? sender, string text)
    {
        if (ViewModels.AppState.Shared.ActiveTab != _tab) return;
        if (_session is null) return;
        try { _session.WriteInput(text); } catch { }
    }

    // ---------- WebView2 init ----------

    private async Task InitWebViewAsync()
    {
        var assetsFolder = WebTerminalAssets.EnsureExtracted();

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DanteCLI", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: null).ConfigureAwait(true);

        await WebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        var core = WebView.CoreWebView2;
        // Tighten the surface — we host trusted local content only.
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            assetsFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += OnWebMessage;

        WebView.Source = new Uri($"https://{VirtualHost}/terminal.html");

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _flushTimer.Tick += (_, _) => FlushOutQueue();
        _flushTimer.Start();
    }

    // ---------- JS → C# messages ----------

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString() ?? string.Empty; }
        catch { try { raw = e.WebMessageAsJson; } catch { return; } }
        if (string.IsNullOrEmpty(raw)) return;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();

            switch (type)
            {
                case "ready":
                    _cols = root.TryGetProperty("cols", out var c) ? c.GetInt32() : 120;
                    _rows = root.TryGetProperty("rows", out var r) ? r.GetInt32() : 30;
                    _terminalReady = true;
                    _ = StartSessionAsync();
                    break;
                case "input":
                    if (root.TryGetProperty("data", out var data) && _session is not null)
                    {
                        var text = data.GetString();
                        if (!string.IsNullOrEmpty(text))
                            _session.WriteInput(text);
                    }
                    break;
                case "resize":
                    if (_session is not null)
                    {
                        _cols = root.TryGetProperty("cols", out var cc) ? cc.GetInt32() : _cols;
                        _rows = root.TryGetProperty("rows", out var rr) ? rr.GetInt32() : _rows;
                        try { _session.Resize(_cols, _rows); } catch { }
                    }
                    break;
                case "link":
                    if (root.TryGetProperty("uri", out var uri))
                    {
                        var u = uri.GetString();
                        if (!string.IsNullOrEmpty(u)) TryOpenLink(u);
                    }
                    break;
            }
        }
        catch (JsonException) { /* ignore malformed */ }
    }

    private static void TryOpenLink(string uri)
    {
        try
        {
            if (!uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ---------- ConPTY lifecycle ----------

    private async Task StartSessionAsync()
    {
        if (_session is not null || _tab is null) return;

        var sess = new ConPtySession();
        sess.Output += OnPtyOutput;
        sess.Exited += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
                _ = WebView.CoreWebView2?.ExecuteScriptAsync(
                    "danteTerminal && danteTerminal.write(btoa('\\r\\n[processo encerrado]\\r\\n'))")));
        };
        _session = sess;

        var shell = ResolveShell();
        try
        {
            await sess.StartAsync(shell, args: null, cwd: _tab.WorkingDirectory, _cols, _rows)
                .ConfigureAwait(true);

            // Force UTF-8 console codepage so xterm.js can decode acentos/PT-BR/CJK
            // correctly. xterm.js's `term.write(Uint8Array)` always decodes UTF-8;
            // cmd.exe on a localized Windows defaults to legacy codepages (e.g.
            // 850/1252) which produces mojibake (�) on accented bytes.
            // The redirect silences the echoed "Active code page: 65001" line.
            var shellName = Path.GetFileName(shell).ToLowerInvariant();
            string? utf8Warmup = shellName switch
            {
                "cmd.exe" => "chcp 65001 > nul\r\n",
                "powershell.exe" or "pwsh.exe" => "chcp 65001 | Out-Null\r\n",
                _ => null
            };
            if (utf8Warmup is not null) sess.WriteInput(utf8Warmup);

            if (!string.IsNullOrEmpty(_tab.InitialCommand))
                sess.WriteInput(_tab.InitialCommand + "\r\n");
        }
        catch (Exception ex)
        {
            ShowFatal("Falha ao iniciar shell: " + ex.Message);
            _session = null;
        }
    }

    private async Task StopSessionAsync()
    {
        var sess = _session;
        _session = null;
        if (sess is null) return;
        try { await sess.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    private static string ResolveShell()
    {
        var configured = ViewModels.AppState.Shared.Settings.DefaultShell;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        // With real ConPTY we can now reach for pwsh / powershell if available
        // (they behave well under ConPTY). Default to cmd.exe for max compatibility.
        return Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\cmd.exe");
    }

    // ---------- Output pipe → xterm.js ----------

    private void OnPtyOutput(object? sender, ReadOnlyMemory<byte> data)
    {
        // Copy to a stable array (the underlying buffer is reused by the pump).
        var copy = new byte[data.Length];
        data.Span.CopyTo(copy);
        _outQueue.Enqueue(copy);
    }

    private void FlushOutQueue()
    {
        if (_outQueue.IsEmpty) return;
        if (!_terminalReady) return;
        if (_flushScheduled) return;
        _flushScheduled = true;

        try
        {
            // Concatenate all queued chunks into one base64 payload to minimize
            // round-trips into the JS host.
            using var ms = new MemoryStream();
            while (_outQueue.TryDequeue(out var chunk))
                ms.Write(chunk, 0, chunk.Length);
            if (ms.Length == 0) return;

            var b64 = Convert.ToBase64String(ms.ToArray());
            var script = $"window.danteTerminal && window.danteTerminal.write('{b64}')";
            _ = WebView.CoreWebView2?.ExecuteScriptAsync(script);
        }
        finally
        {
            _flushScheduled = false;
        }
    }

    private void ShowFatal(string message)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // Replace the WebView with a plain text error so the user sees something.
            var tb = new TextBlock
            {
                Text = message,
                Foreground = System.Windows.Media.Brushes.WhiteSmoke,
                Background = System.Windows.Media.Brushes.Transparent,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
                Margin = new Thickness(16)
            };
            if (Content is Grid g)
            {
                g.Children.Clear();
                g.Children.Add(tb);
            }
        }));
    }
}
