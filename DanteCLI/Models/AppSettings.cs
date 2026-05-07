using System;
using System.Collections.Generic;

namespace DanteCLI.Models;

public enum SidebarMode { Favorites, Files }

public enum AppearanceMode { System, Light, Dark }

public enum TerminalScheme
{
    Dracula, SolarizedDark, SolarizedLight, TokyoNight, Nord, OneDark, AppleClassic
}

public sealed class AppSettings
{
    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;
    public string FontName { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 13;
    public TerminalScheme TerminalScheme { get; set; } = TerminalScheme.TokyoNight;
    public string DefaultShell { get; set; } = "";  // empty -> auto pwsh.exe
    public bool SendOSC7Hint { get; set; } = true;
    public SidebarMode SidebarMode { get; set; } = SidebarMode.Favorites;
    public string FileBrowserRoot { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public bool FileBrowserShowsHidden { get; set; } = false;
    public List<string> FileBrowserExpandedPaths { get; set; } = new();
    public string CustomBackgroundHex { get; set; } = "";
    public string CustomForegroundHex { get; set; } = "";
    public string CustomCursorHex { get; set; } = "";
    public bool UseCustomColors { get; set; } = false;

    public List<string> RecentEmojis { get; set; } = new();

    // Voice transcription
    public string GroqApiKey { get; set; } = "";
    public string VoiceLanguage { get; set; } = "pt";
    public bool VoiceAutoSubmit { get; set; } = false;
    public string VoiceModel { get; set; } = "whisper-large-v3-turbo";
}
