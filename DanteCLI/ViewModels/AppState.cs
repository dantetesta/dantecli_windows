using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

    private AppState()
    {
        // load
        foreach (var f in _favoritesStore.Load()) Favorites.Add(f);
        foreach (var p in _aiStore.Load()) AIProviders.Add(p);
        _settings = _settingsStore.Load();

        Favorites.CollectionChanged += (_, _) => _favoritesStore.Save(Favorites.ToList());
        AIProviders.CollectionChanged += (_, _) => _aiStore.Save(AIProviders.ToList());

        if (Tabs.Count == 0)
        {
            NewTab();
        }
    }

    public TerminalTab NewTab(string? title = null, string? path = null,
                              string? initialCommand = null, string colorHex = TabColors.Neutral,
                              string? emoji = null)
    {
        var tab = new TerminalTab
        {
            Title = title ?? (path is null ? "shell" : System.IO.Path.GetFileName(path) ?? "shell"),
            ColorHex = colorHex,
            Emoji = emoji,
            Kind = TabKind.Terminal,
            WorkingDirectory = path,
            InitialCommand = initialCommand
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
        if (Tabs.Count == 0) NewTab();
        else if (ActiveTab == tab) ActiveTab = Tabs[Math.Max(0, idx - 1)];
    }

    public void OpenFavorite(Favorite f)
    {
        NewTab(title: f.Name, path: f.Path, initialCommand: f.InitialCommand,
               colorHex: f.ColorHex, emoji: f.Emoji);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
