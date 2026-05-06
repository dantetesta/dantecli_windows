using System;
using System.Collections.Generic;

namespace DanteCLI.Models;

public sealed class AIProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = new();
    public string SfSymbol { get; set; } = "Sparkle";  // mapped to FontIcon glyph in WinUI
    public string ColorHex { get; set; } = TabColors.Purple;
    public string? Emoji { get; set; }
    public bool Enabled { get; set; } = true;

    public string FullCommand =>
        Args.Count == 0 ? Command : Command + " " + string.Join(" ", Args);

    public static List<AIProvider> Defaults() => new()
    {
        new() { Name = "Claude", Command = "claude", ColorHex = TabColors.Orange, Emoji = "🤖" },
        new() { Name = "Gemini", Command = "gemini", ColorHex = TabColors.Blue,   Emoji = "✨" },
        new() { Name = "Codex",  Command = "codex",  ColorHex = TabColors.Green,  Emoji = "💻" },
    };
}
