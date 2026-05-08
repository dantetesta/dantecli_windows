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
    private const double WorkspaceHeaderHeight = 26;
    private readonly Dictionary<TerminalTab, FrameworkElement> _tabBodies = new();
    /// Header overlays (live tabs in workspace), keyed by tab.
    private readonly Dictionary<TerminalTab, WorkspaceHeader> _headers = new();
    /// Border overlays per cell (drawn around full cell, not interactive).
    private readonly Dictionary<TerminalTab, Border> _borders = new();
    /// Vacant-slot placeholders, keyed by the dead UUID in workspace.TabIds.
    private readonly Dictionary<Guid, FrameworkElement> _vacantSlots = new();

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
            if (_headers.TryGetValue(tab, out var hd))
            {
                TabsCanvas.Children.Remove(hd);
                _headers.Remove(tab);
            }
            if (_borders.TryGetValue(tab, out var bd))
            {
                TabsCanvas.Children.Remove(bd);
                _borders.Remove(tab);
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

        // Place tab bodies
        foreach (var tab in state.Tabs)
        {
            if (!_tabBodies.TryGetValue(tab, out var body)) continue;
            var rect = ComputeFrame(tab, w, h);
            PlaceAt(body, rect);
        }

        // Sync chrome (headers + borders + vacant placeholders) with active workspace
        SyncWorkspaceOverlays(w, h);
    }

    private void SyncWorkspaceOverlays(double width, double height)
    {
        var state = AppState.Shared;

        // Hide all when not in workspace
        if (state.SplitWorkspace is null)
        {
            foreach (var hd in _headers.Values) hd.Visibility = Visibility.Collapsed;
            foreach (var bd in _borders.Values) bd.Visibility = Visibility.Collapsed;
            foreach (var vs in _vacantSlots.Values) vs.Visibility = Visibility.Collapsed;
            return;
        }

        var ws = state.SplitWorkspace;
        var liveById = state.Tabs.ToDictionary(t => t.Id);
        var seenTabs = new HashSet<TerminalTab>();
        var seenVacantIds = new HashSet<Guid>();

        for (int i = 0; i < ws.TabIds.Count; i++)
        {
            var id = ws.TabIds[i];
            var cell = WorkspaceCellFrame(i, width, height, ws.Layout);
            if (liveById.TryGetValue(id, out var tab))
            {
                seenTabs.Add(tab);
                // Header
                if (!_headers.TryGetValue(tab, out var hd))
                {
                    hd = new WorkspaceHeader(tab);
                    _headers[tab] = hd;
                    TabsCanvas.Children.Add(hd);
                    Panel.SetZIndex(hd, 2);
                }
                hd.Render(tab, isFocused: state.ActiveTab == tab);
                PlaceAt(hd, new Rect(cell.X, cell.Y, cell.Width, WorkspaceHeaderHeight));
                hd.Visibility = Visibility.Visible;

                // Border
                if (!_borders.TryGetValue(tab, out var bd))
                {
                    bd = new Border
                    {
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        IsHitTestVisible = false,
                    };
                    _borders[tab] = bd;
                    TabsCanvas.Children.Add(bd);
                    Panel.SetZIndex(bd, 3);
                }
                var focused = state.ActiveTab == tab;
                bd.BorderBrush = focused
                    ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x66, 0x88, 0x88, 0x88));
                bd.BorderThickness = new Thickness(focused ? 2 : 1);
                PlaceAt(bd, cell);
                bd.Visibility = Visibility.Visible;
            }
            else
            {
                seenVacantIds.Add(id);
                if (!_vacantSlots.TryGetValue(id, out var vs))
                {
                    vs = MakeVacantSlot(id);
                    _vacantSlots[id] = vs;
                    TabsCanvas.Children.Add(vs);
                    Panel.SetZIndex(vs, 1);
                }
                PlaceAt(vs, cell);
                vs.Visibility = Visibility.Visible;
            }
        }

        // Remove headers/borders for tabs no longer in workspace
        foreach (var (tab, hd) in _headers.Where(kv => !seenTabs.Contains(kv.Key)).ToList())
        {
            TabsCanvas.Children.Remove(hd);
            _headers.Remove(tab);
        }
        foreach (var (tab, bd) in _borders.Where(kv => !seenTabs.Contains(kv.Key)).ToList())
        {
            TabsCanvas.Children.Remove(bd);
            _borders.Remove(tab);
        }
        // Remove vacant slots no longer present
        foreach (var (vid, vs) in _vacantSlots.Where(kv => !seenVacantIds.Contains(kv.Key)).ToList())
        {
            TabsCanvas.Children.Remove(vs);
            _vacantSlots.Remove(vid);
        }
    }

    private FrameworkElement MakeVacantSlot(Guid slotId)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            AllowDrop = true,
            Tag = slotId,
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "➕", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.5 });
        stack.Children.Add(new TextBlock { Text = "Slot vazio", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(new TextBlock { Text = "Arraste outra aba pra cá", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) });
        border.Child = stack;
        border.Drop += (_, e) =>
        {
            if (e.Data.GetData("DanteSplitTabId") is string s && Guid.TryParse(s, out var src))
                AppState.Shared.PlaceAtVacantSlot(src, slotId);
        };
        border.DragEnter += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("DanteSplitTabId") ? DragDropEffects.Move : DragDropEffects.None;
            border.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0A, 0x84, 0xFF));
            e.Handled = true;
        };
        border.DragLeave += (_, _) =>
            border.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        return border;
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
            {
                var cell = WorkspaceCellFrame(idx, w, h, ws.Layout);
                // Reserve header strip at the top of the cell
                return new Rect(cell.X,
                                cell.Y + WorkspaceHeaderHeight,
                                cell.Width,
                                Math.Max(0, cell.Height - WorkspaceHeaderHeight));
            }
            // Off-workspace: offscreen
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
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.SettingsWindow { Owner = this };
        dlg.ShowDialog();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.AboutWindow { Owner = this };
        dlg.ShowDialog();
    }
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

/// <summary>Opaque, colored header bar for a single workspace cell. Drag handle,
/// title, ATIVO badge, and a "remove from split" button (which keeps the tab alive).</summary>
public sealed class WorkspaceHeader : Border
{
    private readonly TextBlock _emojiBlock;
    private readonly System.Windows.Shapes.Ellipse _colorDot;
    private readonly TextBlock _titleBlock;
    private readonly Border _activeBadge;
    private readonly TextBlock _activeBadgeText;
    private readonly Button _removeButton;
    private readonly TextBlock _dragHandle;
    private TerminalTab? _tab;
    private Point _dragStart;

    public WorkspaceHeader(TerminalTab tab)
    {
        _tab = tab;
        Padding = new Thickness(8, 0, 4, 0);
        Cursor = Cursors.Hand;
        AllowDrop = true;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _dragHandle = new TextBlock
        {
            Text = "⋮⋮",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Opacity = 0.7,
        };
        Grid.SetColumn(_dragHandle, 0);
        grid.Children.Add(_dragHandle);

        _emojiBlock = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        Grid.SetColumn(_emojiBlock, 1);
        grid.Children.Add(_emojiBlock);

        _colorDot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        Grid.SetColumn(_colorDot, 2);
        grid.Children.Add(_colorDot);

        _titleBlock = new TextBlock
        {
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(_titleBlock, 3);
        grid.Children.Add(_titleBlock);

        _activeBadgeText = new TextBlock
        {
            Text = "ATIVO", FontSize = 9, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _activeBadge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _activeBadgeText,
        };
        Grid.SetColumn(_activeBadge, 4);
        grid.Children.Add(_activeBadge);

        _removeButton = new Button
        {
            Content = "✕",
            FontSize = 11,
            Width = 22, Height = 22,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            ToolTip = "Remover do split (não fecha a aba)",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _removeButton.Click += (_, _) =>
        {
            if (_tab is not null) AppState.Shared.RemoveFromSplit(_tab.Id);
        };
        Grid.SetColumn(_removeButton, 5);
        grid.Children.Add(_removeButton);

        Child = grid;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnClick;
        Drop += OnDrop;
        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
    }

    public void Render(TerminalTab tab, bool isFocused)
    {
        _tab = tab;
        _titleBlock.Text = tab.Title;
        _titleBlock.FontWeight = isFocused ? FontWeights.SemiBold : FontWeights.Medium;

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
            _colorDot.Fill = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        }

        var c = TabChip.ColorFromHex(tab.ColorHex);
        byte bgAlpha = isFocused ? (byte)0xFF : (byte)0xD9;
        Background = new SolidColorBrush(Color.FromArgb(bgAlpha, c.R, c.G, c.B));

        var fg = ContrastingForeground(c);
        _titleBlock.Foreground = new SolidColorBrush(fg);
        _emojiBlock.Foreground = new SolidColorBrush(fg);
        _dragHandle.Foreground = new SolidColorBrush(Color.FromArgb(0xC0, fg.R, fg.G, fg.B));
        _removeButton.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, fg.R, fg.G, fg.B));

        _activeBadge.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        _activeBadgeText.Foreground = new SolidColorBrush(c);
        _activeBadge.Visibility = isFocused ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Color ContrastingForeground(Color bg)
    {
        // Luminance heuristic — text is white over dark bg, black over light bg.
        var lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return lum > 0.6 ? Color.FromRgb(0x10, 0x10, 0x18) : Colors.White;
    }

    // -------- Drag source --------
    private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_tab is null) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var p = e.GetPosition(null);
        var diff = _dragStart - p;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var data = new DataObject();
        data.SetData("DanteSplitTabId", _tab.Id.ToString());
        try { DragDrop.DoDragDrop(this, data, DragDropEffects.Move); } catch { }
    }

    private void OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_tab is not null) AppState.Shared.ActiveTab = _tab;
    }

    // -------- Drop target --------
    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("DanteSplitTabId"))
        {
            e.Effects = DragDropEffects.Move;
            // Subtle highlight
            Opacity = 0.85;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        Opacity = 1.0;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        Opacity = 1.0;
        if (_tab is null) return;
        if (e.Data.GetData("DanteSplitTabId") is string s && Guid.TryParse(s, out var src))
        {
            if (src != _tab.Id) AppState.Shared.SwapSplitSlots(src, _tab.Id);
        }
    }
}
