using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DanteCLI.Models;
using DanteCLI.Services;

namespace DanteCLI.ViewModels;

public sealed class AppState : INotifyPropertyChanged
{
    public static AppState Shared { get; } = new();

    public ObservableCollection<TerminalTab> Tabs { get; } = new();
    public ObservableCollection<Favorite> Favorites { get; } = new();
    public ObservableCollection<AIProvider> AIProviders { get; } = new();

    private readonly FavoritesStore _favoritesStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly AIProviderStore _aiStore = new();

    private AppSettings _settings = new();
    public AppSettings Settings
    {
        get => _settings;
        set { _settings = value; OnChanged(nameof(Settings)); _settingsStore.Save(value); }
    }

    private TerminalTab? _activeTab;
    public TerminalTab? ActiveTab
    {
        get => _activeTab;
        set { _activeTab = value; OnChanged(nameof(ActiveTab)); }
    }

    private bool _sidebarVisible = true;
    public bool SidebarVisible { get => _sidebarVisible; set { _sidebarVisible = value; OnChanged(nameof(SidebarVisible)); } }

    private SplitWorkspace? _splitWorkspace;
    public SplitWorkspace? SplitWorkspace
    {
        get => _splitWorkspace;
        set { _splitWorkspace = value; OnChanged(nameof(SplitWorkspace)); }
    }

    private AppState()
    {
        foreach (var f in _favoritesStore.Load()) Favorites.Add(f);
        foreach (var p in _aiStore.Load()) AIProviders.Add(p);
        _settings = _settingsStore.Load();

        Favorites.CollectionChanged += (_, _) => _favoritesStore.Save(Favorites.ToList());
        AIProviders.CollectionChanged += (_, _) => _aiStore.Save(AIProviders.ToList());

        if (Tabs.Count == 0) NewTab();
    }

    public TerminalTab NewTab(string? title = null, string? path = null,
                              string? initialCommand = null, string colorHex = TabColors.Neutral,
                              string? emoji = null)
    {
        var tab = new TerminalTab
        {
            Title = title ?? (path is null ? "shell" : Path.GetFileName(path) ?? "shell"),
            ColorHex = colorHex,
            Emoji = emoji,
            Kind = TabKind.Terminal,
            WorkingDirectory = path,
            InitialCommand = initialCommand,
        };
        Tabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    public TerminalTab OpenFileInEditor(string filePath)
    {
        // Reuse existing editor tab pointing at this file if any.
        var existing = Tabs.FirstOrDefault(t => t.IsEditor && string.Equals(t.FileUrl, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            ActiveTab = existing;
            return existing;
        }
        var tab = new TerminalTab
        {
            Title = Path.GetFileName(filePath),
            ColorHex = TabColors.Blue,
            Emoji = "📄",
            Kind = TabKind.Editor,
            FileUrl = filePath,
        };
        Tabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    public void CloseTab(TerminalTab tab)
    {
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        Tabs.RemoveAt(idx);
        // Drop tab from any active split workspace
        if (SplitWorkspace is { } ws && ws.TabIds.Contains(tab.Id))
        {
            var remaining = ws.TabIds.Where(id => id != tab.Id).ToList();
            SplitWorkspace = remaining.Count >= 2
                ? new SplitWorkspace(remaining, ws.Layout)
                : null;
        }
        if (Tabs.Count == 0) NewTab();
        else if (ActiveTab == tab) ActiveTab = Tabs[Math.Max(0, idx - 1)];
    }

    public void OpenFavorite(Favorite f) =>
        NewTab(title: f.Name, path: f.Path, initialCommand: f.InitialCommand,
               colorHex: f.ColorHex, emoji: f.Emoji);

    public void DuplicateTab(TerminalTab tab) =>
        NewTab(title: tab.Title, path: tab.WorkingDirectory,
               colorHex: tab.ColorHex, emoji: tab.Emoji);

    public Favorite FavoriteFromTab(TerminalTab tab)
    {
        var path = tab.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new Favorite
        {
            Name = string.IsNullOrWhiteSpace(tab.Title) ? Path.GetFileName(path) : tab.Title,
            Path = path,
            ColorHex = tab.ColorHex,
            Emoji = tab.Emoji,
        };
    }

    // -------- Split workspace --------

    public void EnterSplitWorkspace(IEnumerable<Guid> ids, SplitLayout layout)
    {
        var trimmed = ids.Take(layout.Capacity).ToList();
        if (trimmed.Count < 2) return;
        SplitWorkspace = new SplitWorkspace(trimmed, layout);
        if (Tabs.FirstOrDefault(t => t.Id == trimmed[0]) is { } first) ActiveTab = first;
    }

    public void ExitSplitWorkspace() => SplitWorkspace = null;

    // -------- AI launch --------

    public void LaunchAI(AIProvider provider, bool inNewTab)
    {
        var cmd = provider.FullCommand;
        if (inNewTab)
        {
            var cwd = ActiveTab?.WorkingDirectory ??
                      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            NewTab(title: provider.Name, path: cwd, initialCommand: cmd,
                   colorHex: provider.ColorHex, emoji: provider.Emoji);
        }
        else
        {
            // Inject in active terminal — TerminalView listens to event below.
            InjectIntoActiveTerminal?.Invoke(this, cmd + "\r\n");
        }
    }

    public event EventHandler<string>? InjectIntoActiveTerminal;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
