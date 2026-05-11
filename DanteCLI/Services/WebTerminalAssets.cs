using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace DanteCLI.Services;

/// <summary>
/// Extracts the embedded xterm.js + bridge assets to a disk folder once per
/// process. WebView2's <c>SetVirtualHostNameToFolderMapping</c> needs a real
/// folder to serve from. The folder lives under <c>%LOCALAPPDATA%/DanteCLI/WebTerminal/</c>
/// so it survives across runs but gets refreshed when the embedded version differs.
/// </summary>
internal static class WebTerminalAssets
{
    private static readonly string[] Assets =
    {
        "terminal.html",
        "xterm.js",
        "xterm.css",
        "xterm-addon-fit.js",
        "xterm-addon-web-links.js",
    };

    private static string? _folder;
    private static readonly object _lock = new();

    public static string EnsureExtracted()
    {
        if (_folder is not null) return _folder;
        lock (_lock)
        {
            if (_folder is not null) return _folder;

            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DanteCLI",
                "WebTerminal");
            Directory.CreateDirectory(root);

            var assembly = Assembly.GetExecutingAssembly();
            // Embedded resource names look like "DanteCLI.WebTerminal.terminal.html".
            var prefix = assembly.GetName().Name + ".WebTerminal.";

            foreach (var name in Assets)
            {
                var resName = prefix + name;
                var outPath = Path.Combine(root, name);
                // Always overwrite — assets ship with the .exe so they're authoritative.
                using var src = assembly.GetManifestResourceStream(resName);
                if (src is null)
                    throw new InvalidOperationException($"Embedded resource missing: {resName}");
                using var dst = File.Create(outPath);
                src.CopyTo(dst);
            }

            _folder = root;
            return _folder;
        }
    }
}
