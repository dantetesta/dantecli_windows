using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DanteCLI.Models;
using DanteCLI.ViewModels;
using DanteCLI.Views;
using Microsoft.Win32;

namespace DanteCLI;

public partial class MainWindow : Window
{
    /// Live UserControl for each tab. Key stays alive as long as tab exists in AppState.
    private readonly Dictionary<TerminalTab, FrameworkElement> _tabBodies = new();
    /// Chrome overlays (header + border) for workspace cells, keyed by tab.
    private readonly Dictionary<TerminalTab, FrameworkElement> _chromes = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "DANTE CLI";

        var state = AppState.Shared;
        state.Tabs.CollectionChanged += Tabs_CollectionChanged;
        state.Favorites.CollectionChanged += (_, _) => { /* sidebar listens itself */ };
        state.PropertyChanged += AppState_PropertyChanged;

        Closing += MainWindow_Closing;

        RebuildTabStrip();
        SyncTabBodies();
        Loaded += (_, _) => RelayoutCanvas();
    }

    private void AppState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.ActiveTab) ||
            e.PropertyName == nameof(AppState.SplitWorkspace))
        {
            RebuildTabStrip();
            RelayoutCanvas();
        }
    }

    private void Picker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            FavoritesPane.Visibility = tag == "files" ? Visibility.Collapsed : Visibility.Visible;
            FilesPane.Visibility     = tag == "files" ? Visibility.Visible    : Visibility.Collapsed;
        }
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildTabStrip();
        SyncTabBodies();
        RelayoutCanvas();
    }

    private void RebuildTabStrip()
    {
        var state = AppState.Shared;
        var items = new List<TabChip>();
        foreach (var tab in state.Tabs)
        {
            var chip = new TabChip();
            chip.Bind(tab, isActive: state.ActiveTab == tab);
            chip.Selected += (_, t) => { state.ActiveTab = t; };
            chip.Closed   += (_, t) => state.CloseTab(t);
            items.Add(chip);
        }
        TabStrip.ItemsSource = items;
    }

    private void SyncTabBodies()
    {
        var state = AppState.Shared;

        // Add new tab views
        foreach (var tab in state.Tabs)
        {
            if (_tabBodies.ContainsKey(tab)) continue;
            FrameworkElement view;
            if (tab.Kind == TabKind.Editor)
            {
                var editor = new EditorView();
                editor.Bind(tab);
                view = editor;
            }
            else
            {
                var term = new TerminalView();
                term.Bind(tab);
                view = term;
            }
            _tabBodies[tab] = view;
            TabsCanvas.Children.Add(view);
        }

        // Remove views for closed tabs
        var openSet = state.Tabs.ToHashSet();
        var toRemove = _tabBodies.Keys.Where(t => !openSet.Contains(t)).ToList();
        foreach (var tab in toRemove)
        {
            if (_tabBodies.TryGetValue(tab, out var body))
            {
                TabsCanvas.Children.Remove(body);
                if (body is TerminalView t) _ = t.ForceShutdownAsync();
            }
            _tabBodies.Remove(tab);
            if (_chromes.TryGetValue(tab, out var chrome))
            {
                TabsCanvas.Children.Remove(chrome);
                _chromes.Remove(tab);
            }
        }
    }

    private void TabsCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RelayoutCanvas();

    private void RelayoutCanvas()
    {
        if (!IsLoaded) return;
        var state = AppState.Shared;
        double w = TabsCanvas.ActualWidth;
        double h = TabsCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Synchronize chromes with workspace tabs
        SyncChromes();

        foreach (var tab in state.Tabs)
        {
            if (!_tabBodies.TryGetValue(tab, out var body)) continue;
            var rect = ComputeFrame(tab, w, h);
            PlaceAt(body, rect);
        }

        if (state.SplitWorkspace is { } ws)
        {
            for (int i = 0; i < ws.TabIds.Count; i++)
            {
                var id = ws.TabIds[i];
                var tab = state.Tabs.FirstOrDefault(t => t.Id == id);
                if (tab is null) continue;
                if (!_chromes.TryGetValue(tab, out var chrome)) continue;
                var cell = WorkspaceCellFrame(i, w, h, ws.Layout);
                PlaceAt(chrome, cell);
                ((WorkspaceChrome)chrome).Render(tab, isFocused: state.ActiveTab == tab);
                chrome.Visibility = Visibility.Visible;
            }
        }
        else
        {
            foreach (var ch in _chromes.Values) ch.Visibility = Visibility.Collapsed;
        }
    }

    private void SyncChromes()
    {
        var state = AppState.Shared;
        var workspaceTabs = state.SplitWorkspace?.TabIds.ToHashSet() ?? new HashSet<Guid>();
        // Add chromes for workspace tabs
        foreach (var tab in state.Tabs.Where(t => workspaceTabs.Contains(t.Id)))
        {
            if (_chromes.ContainsKey(tab)) continue;
            var chrome = new WorkspaceChrome(tab, () => state.ActiveTab = tab);
            _chromes[tab] = chrome;
            TabsCanvas.Children.Add(chrome);
            // Make sure chrome is on top of the body
            Panel.SetZIndex(chrome, 1);
        }
    }

    private static void PlaceAt(FrameworkElement element, Rect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = Math.Max(0, rect.Width);
        element.Height = Math.Max(0, rect.Height);
        element.Visibility = rect.Width > 0 && rect.Height > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private Rect ComputeFrame(TerminalTab tab, double w, double h)
    {
        var state = AppState.Shared;
        if (state.SplitWorkspace is { } ws)
        {
            var idx = ws.TabIds.ToList().IndexOf(tab.Id);
            if (idx >= 0)
                return WorkspaceCellFrame(idx, w, h, ws.Layout);
            // Off-workspace: hidden via zero-size + offscreen
            return new Rect(-w - 100, 0, w, h);
        }
        if (state.ActiveTab == tab)
            return new Rect(0, 0, w, h);
        return new Rect(-w - 100, 0, w, h);
    }

    private static Rect WorkspaceCellFrame(int index, double width, double height, SplitLayout layout)
    {
        var cols = Math.Max(1, layout.Cols);
        var rows = Math.Max(1, layout.Rows);
        var row = index / cols;
        var col = index % cols;
        const double spacing = 4;
        var cellW = (width  - spacing * (cols + 1)) / cols;
        var cellH = (height - spacing * (rows + 1)) / rows;
        var x = spacing + col * (cellW + spacing);
        var y = spacing + row * (cellH + spacing);
        return new Rect(x, y, Math.Max(0, cellW), Math.Max(0, cellH));
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var terminals = _tabBodies.Values.OfType<TerminalView>().ToList();
        if (terminals.Count == 0) return;

        e.Cancel = true;
        Closing -= MainWindow_Closing;
        try { await Task.WhenAll(terminals.Select(t => t.ForceShutdownAsync())); } catch { }
        Application.Current.Dispatcher.BeginInvoke(new Action(Close));
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AppState.Shared.NewTab();
    private void NewTab_Cmd(object sender, ExecutedRoutedEventArgs e) => AppState.Shared.NewTab();
    private void CloseTab_Cmd(object sender, ExecutedRoutedEventArgs e)
    {
        if (AppState.Shared.ActiveTab is { } a) AppState.Shared.CloseTab(a);
    }
    private void OpenFile_Cmd(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Abrir arquivo" };
        if (dlg.ShowDialog() == true) AppState.Shared.OpenFileInEditor(dlg.FileName);
    }
}

/// <summary>Header bar + border overlay for a workspace cell.</summary>
public sealed class WorkspaceChrome : Grid
{
    private readonly Border _headerBorder;
    private readonly TextBlock _emojiBlock;
    private readonly System.Windows.Shapes.Ellipse _colorDot;
    private readonly TextBlock _titleBlock;
    private readonly Border _activeBadge;
    private readonly Border _borderShape;

    public WorkspaceChrome(TerminalTab tab, Action onHeaderClick)
    {
        IsHitTestVisible = true;

        // Border overlay (visual only, behind everything)
        _borderShape = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Children.Add(_borderShape);

        // Header bar at the top
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        _emojiBlock = new TextBlock { FontSize = 11, Margin = new Thickness(0,0,5,0), VerticalAlignment = VerticalAlignment.Center };
        _colorDot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0,0,5,0), VerticalAlignment = VerticalAlignment.Center };
        _titleBlock = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _activeBadge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x0A, 0x84, 0xFF)),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(6, 0, 0, 0),
            Child = new TextBlock { Text = "ativo", FontSize = 9, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Color.FromRgb(0x0A,0x84,0xFF)) },
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(_emojiBlock);
        stack.Children.Add(_colorDot);
        stack.Children.Add(_titleBlock);
        stack.Children.Add(_activeBadge);

        _headerBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            Child = stack,
        };
        _headerBorder.MouseLeftButtonUp += (_, _) => onHeaderClick();
        Children.Add(_headerBorder);
    }

    public void Render(TerminalTab tab, bool isFocused)
    {
        _titleBlock.Text = tab.Title;
        _titleBlock.FontWeight = isFocused ? FontWeights.SemiBold : FontWeights.Normal;
        if (!string.IsNullOrEmpty(tab.Emoji))
        {
            _emojiBlock.Text = tab.Emoji;
            _emojiBlock.Visibility = Visibility.Visible;
            _colorDot.Visibility = Visibility.Collapsed;
        }
        else
        {
            _emojiBlock.Visibility = Visibility.Collapsed;
            _colorDot.Visibility = Visibility.Visible;
            _colorDot.Fill = new SolidColorBrush(TabChip.ColorFromHex(tab.ColorHex));
        }
        var c = TabChip.ColorFromHex(tab.ColorHex);
        byte alpha = isFocused ? (byte)0x37 : (byte)0x1A;
        _headerBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));

        _borderShape.BorderBrush = isFocused
            ? new SolidColorBrush(Color.FromArgb(0x99, 0x0A, 0x84, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88));
        _borderShape.BorderThickness = new Thickness(isFocused ? 1.5 : 0.5);
        _activeBadge.Visibility = isFocused ? Visibility.Visible : Visibility.Collapsed;
    }
}
