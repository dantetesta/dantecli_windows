using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DanteCLI.Models;
using DanteCLI.ViewModels;

namespace DanteCLI.Views;

public partial class TabChip : UserControl
{
    public event EventHandler<TerminalTab>? Selected;
    public event EventHandler<TerminalTab>? Closed;

    private TerminalTab? _tab;
    private bool _isActive;
    private bool _hovering;

    public TabChip()
    {
        InitializeComponent();
        ViewModels.AppState.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModels.AppState.SplitWorkspace))
                Dispatcher.BeginInvoke(new Action(UpdateMenuVisibility));
        };
    }

    public void Bind(TerminalTab tab, bool isActive)
    {
        if (_tab is not null) _tab.PropertyChanged -= OnTabChanged;
        _tab = tab;
        _isActive = isActive;
        tab.PropertyChanged += OnTabChanged;
        Render();
        UpdateMenuVisibility();
    }

    private void UpdateMenuVisibility()
    {
        if (_tab is null) return;
        var state = ViewModels.AppState.Shared;
        var canAdd = state.SplitWorkspaceHasVacantSlot && !state.TabIsInSplit(_tab.Id);
        AddToSplitMenuItem.Visibility = canAdd ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(Render));
    }

    private void Render()
    {
        if (_tab is null) return;
        TitleBlock.Text = _tab.Title;
        TitleBlock.FontWeight = _isActive ? FontWeights.SemiBold : FontWeights.Normal;
        DirtyDot.Visibility = _tab.IsDirty ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(_tab.Emoji))
        {
            EmojiBlock.Text = _tab.Emoji;
            EmojiBlock.Visibility = Visibility.Visible;
            ColorDot.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmojiBlock.Visibility = Visibility.Collapsed;
            ColorDot.Visibility = Visibility.Visible;
            ColorDot.Fill = new SolidColorBrush(ColorFromHex(_tab.ColorHex));
        }
        CloseButton.Visibility = _isActive || _hovering ? Visibility.Visible : Visibility.Hidden;

        var c = ColorFromHex(_tab.ColorHex);
        byte alpha = _isActive ? (byte)0x47 : (_hovering ? (byte)0x29 : (byte)0x1A);
        Root.Background = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true; Render();
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false; Render();
    }

    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            StartRename();
            e.Handled = true;
        }
    }

    private void Root_Click(object sender, MouseButtonEventArgs e)
    {
        if (TitleEdit.Visibility == Visibility.Visible) return;
        if (_tab is not null) Selected?.Invoke(this, _tab);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is not null) Closed?.Invoke(this, _tab);
        e.Handled = true;
    }

    // -------- Context menu handlers --------

    private void RenameMenu_Click(object sender, RoutedEventArgs e) => StartRename();

    private void ColorMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is not null && sender is MenuItem mi && mi.Tag is string hex)
            _tab.ColorHex = hex;
    }

    private void EmojiPickerMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is null) return;
        var dlg = new EmojiPickerWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _tab.Emoji = dlg.Selected;  // null when cleared
    }

    private void AddToSplitMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is null) return;
        AppState.Shared.AddTabToSplit(_tab.Id);
    }

    private void FavoriteMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is null) return;
        var fav = AppState.Shared.FavoriteFromTab(_tab);
        var dlg = new FavoriteEditorDialog(fav, isNew: true) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            AppState.Shared.Favorites.Add(dlg.Result);
    }

    private void DuplicateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is not null) AppState.Shared.DuplicateTab(_tab);
    }

    private void CloseMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is not null) Closed?.Invoke(this, _tab);
    }

    // -------- Inline rename --------

    private void StartRename()
    {
        if (_tab is null) return;
        TitleBlock.Visibility = Visibility.Collapsed;
        TitleEdit.Visibility = Visibility.Visible;
        TitleEdit.Text = _tab.Title;
        TitleEdit.Focus();
        TitleEdit.SelectAll();
    }

    private void TitleEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
    }

    private void TitleEdit_LostFocus(object sender, RoutedEventArgs e) => CommitRename();

    private void CommitRename()
    {
        if (_tab is null) return;
        var trimmed = (TitleEdit.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(trimmed)) _tab.Title = trimmed;
        TitleEdit.Visibility = Visibility.Collapsed;
        TitleBlock.Visibility = Visibility.Visible;
    }

    private void CancelRename()
    {
        TitleEdit.Visibility = Visibility.Collapsed;
        TitleBlock.Visibility = Visibility.Visible;
    }

    public static Color ColorFromHex(string hex)
    {
        if (hex.StartsWith("#")) hex = hex[1..];
        if (hex.Length != 6) return Colors.Gray;
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Color.FromArgb(255, r, g, b);
    }
}
