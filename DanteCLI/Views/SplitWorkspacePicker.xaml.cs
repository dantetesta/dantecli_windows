using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DanteCLI.Models;
using DanteCLI.ViewModels;

namespace DanteCLI.Views;

public partial class SplitWorkspacePicker : Window
{
    private SplitCategory _category = SplitCategory.Horizontal;
    private SplitLayout _layout = SplitLayout.Presets[0];
    private readonly HashSet<Guid> _selected = new();

    public SplitWorkspacePicker()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Pre-select active tab
            if (AppState.Shared.ActiveTab is { } a) _selected.Add(a.Id);
            // Restore current workspace
            if (AppState.Shared.SplitWorkspace is { } ws)
            {
                _selected.Clear();
                foreach (var id in ws.TabIds) _selected.Add(id);
                _layout = ws.Layout;
                _category = _layout.Category;
                CatHorizontal.IsChecked = _category == SplitCategory.Horizontal;
                CatVertical.IsChecked   = _category == SplitCategory.Vertical;
                CatGrid.IsChecked       = _category == SplitCategory.Grid;
            }
            BuildLayouts();
            BuildTabs();
            UpdateCount();
        };
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            _category = Enum.Parse<SplitCategory>(tag);
            _layout = SplitLayout.Presets.First(p => p.Category == _category);
            BuildLayouts();
            TrimSelectionIfNeeded();
            BuildTabs();
            UpdateCount();
        }
    }

    private void BuildLayouts()
    {
        LayoutHeader.Text = $"LAYOUT ({_layout.Capacity} painéis)";
        var items = new List<UIElement>();
        foreach (var preset in SplitLayout.Presets.Where(p => p.Category == _category))
        {
            var card = MakeLayoutCard(preset, isSelected: preset == _layout);
            items.Add(card);
        }
        LayoutsList.ItemsSource = items;
    }

    private UIElement MakeLayoutCard(SplitLayout preset, bool isSelected)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = isSelected ? new SolidColorBrush(Color.FromArgb(0x33, 0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)),
            BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)) : Brushes.Transparent,
            BorderThickness = new Thickness(isSelected ? 1.5 : 0),
            Padding = new Thickness(8),
            Margin = new Thickness(3),
            Width = 95, Height = 76,
            Cursor = Cursors.Hand,
            Tag = preset,
        };
        var stack = new StackPanel();
        stack.Children.Add(MakeGlyph(preset, isSelected));
        stack.Children.Add(new TextBlock
        {
            Text = preset.Label, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = isSelected ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)) : Brushes.Black,
        });
        border.Child = stack;
        border.MouseLeftButtonUp += (_, _) =>
        {
            _layout = preset;
            BuildLayouts();
            TrimSelectionIfNeeded();
            BuildTabs();
            UpdateCount();
        };
        return border;
    }

    private static FrameworkElement MakeGlyph(SplitLayout preset, bool active)
    {
        var grid = new UniformGrid
        {
            Rows = preset.Rows, Columns = preset.Cols,
            Width = 60, Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var fill = active
            ? new SolidColorBrush(Color.FromArgb(0xB3, 0x0A, 0x84, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x88, 0x66, 0x66, 0x66));
        for (int i = 0; i < preset.Cols * preset.Rows; i++)
            grid.Children.Add(new Rectangle { Fill = fill, RadiusX = 1, RadiusY = 1, Margin = new Thickness(1) });
        return grid;
    }

    private void BuildTabs()
    {
        TabsHeader.Text = $"ABAS PRA INCLUIR (até {_layout.Capacity})";
        var items = new List<UIElement>();
        foreach (var tab in AppState.Shared.Tabs)
        {
            var cb = new CheckBox
            {
                Margin = new Thickness(0, 2, 0, 2),
                IsChecked = _selected.Contains(tab.Id),
                IsEnabled = _selected.Contains(tab.Id) || _selected.Count < _layout.Capacity,
                Tag = tab.Id,
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(tab.Emoji))
                content.Children.Add(new TextBlock { Text = tab.Emoji, FontSize = 13, Margin = new Thickness(0,0,4,0), VerticalAlignment = VerticalAlignment.Center });
            else
                content.Children.Add(new Ellipse {
                    Width = 8, Height = 8,
                    Fill = new SolidColorBrush(TabChip.ColorFromHex(tab.ColorHex)),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            content.Children.Add(new TextBlock {
                Text = tab.Title, VerticalAlignment = VerticalAlignment.Center
            });
            cb.Content = content;
            cb.Checked += (_, _) =>
            {
                _selected.Add(tab.Id);
                BuildTabs(); UpdateCount();
            };
            cb.Unchecked += (_, _) =>
            {
                _selected.Remove(tab.Id);
                BuildTabs(); UpdateCount();
            };
            items.Add(cb);
        }
        TabsList.ItemsSource = items;
    }

    private void TrimSelectionIfNeeded()
    {
        if (_selected.Count <= _layout.Capacity) return;
        var ordered = AppState.Shared.Tabs.Select(t => t.Id).Where(_selected.Contains).Take(_layout.Capacity).ToList();
        _selected.Clear();
        foreach (var id in ordered) _selected.Add(id);
    }

    private void UpdateCount()
    {
        CountLabel.Text = $"{_selected.Count} de {_layout.Capacity} selecionada{(_selected.Count == 1 ? "" : "s")}";
        // Even with a single selection we can apply — missing slots get filled
        // with fresh shells automatically by AppState.EnterSplitWorkspace.
        ApplyButton.IsEnabled = _selected.Count >= 1;
        // Disable "Selecionar todos" when capacity is already reached or when
        // no tabs exist.
        var available = AppState.Shared.Tabs.Count;
        SelectAllButton.IsEnabled = available > 0 && _selected.Count < Math.Min(available, _layout.Capacity);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        // Take tabs in order, up to layout capacity.
        var ids = AppState.Shared.Tabs.Select(t => t.Id).Take(_layout.Capacity).ToList();
        _selected.Clear();
        foreach (var id in ids) _selected.Add(id);
        BuildTabs();
        UpdateCount();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var ordered = AppState.Shared.Tabs.Select(t => t.Id).Where(_selected.Contains).ToList();
        AppState.Shared.EnterSplitWorkspace(ordered, _layout);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
