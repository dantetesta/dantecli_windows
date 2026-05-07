using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DanteCLI.Models;

namespace DanteCLI.Views;

public partial class TabChip : UserControl
{
    public event EventHandler<TerminalTab>? Selected;
    public event EventHandler<TerminalTab>? Closed;

    private TerminalTab? _tab;
    private bool _isActive;
    private bool _hovering;

    public TabChip() { InitializeComponent(); }

    public void Bind(TerminalTab tab, bool isActive)
    {
        _tab = tab;
        _isActive = isActive;
        TitleBlock.Text = tab.Title;
        TitleBlock.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        if (!string.IsNullOrWhiteSpace(tab.Emoji))
        {
            EmojiBlock.Text = tab.Emoji;
            EmojiBlock.Visibility = Visibility.Visible;
            ColorDot.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmojiBlock.Visibility = Visibility.Collapsed;
            ColorDot.Visibility = Visibility.Visible;
            ColorDot.Fill = new SolidColorBrush(ColorFromHex(tab.ColorHex));
        }
        CloseButton.Visibility = isActive || _hovering ? Visibility.Visible : Visibility.Hidden;
        UpdateBackground();
    }

    private void UpdateBackground()
    {
        if (_tab is null) return;
        var c = ColorFromHex(_tab.ColorHex);
        byte alpha = _isActive ? (byte)0x47 : (_hovering ? (byte)0x29 : (byte)0x1A);
        Root.Background = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        if (_tab is not null) Bind(_tab, _isActive);
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        if (_tab is not null) Bind(_tab, _isActive);
    }

    private void Root_Click(object sender, MouseButtonEventArgs e)
    {
        if (_tab is not null) Selected?.Invoke(this, _tab);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tab is not null) Closed?.Invoke(this, _tab);
        e.Handled = true;
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
