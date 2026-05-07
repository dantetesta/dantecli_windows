using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DanteCLI.Models;
using Microsoft.Win32;

namespace DanteCLI.Views;

public partial class FavoriteEditorDialog : Window
{
    public Favorite Result { get; private set; }
    public bool RequestedDelete { get; private set; }

    private readonly bool _isNew;

    public FavoriteEditorDialog(Favorite initial, bool isNew)
    {
        InitializeComponent();
        _isNew = isNew;
        Result = new Favorite
        {
            Id = initial.Id,
            Name = initial.Name,
            Path = initial.Path,
            Tags = new List<string>(initial.Tags),
            ColorHex = initial.ColorHex,
            Emoji = initial.Emoji,
            InitialCommand = initial.InitialCommand,
            CreatedAt = initial.CreatedAt,
        };
        HeaderText.Text = isNew ? "Novo Favorito" : "Editar Favorito";
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

        NameBox.Text = Result.Name;
        PathBox.Text = Result.Path;
        EmojiPreview.Text = string.IsNullOrEmpty(Result.Emoji) ? "🙂" : Result.Emoji;
        EmojiPreview.Opacity = string.IsNullOrEmpty(Result.Emoji) ? 0.4 : 1.0;
        CommandBox.Text = Result.InitialCommand ?? "";
        BuildPalette();
        RefreshTags();
    }

    private void EmojiPickerButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new EmojiPickerWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Result.Emoji = dlg.Selected;
            EmojiPreview.Text = string.IsNullOrEmpty(Result.Emoji) ? "🙂" : Result.Emoji;
            EmojiPreview.Opacity = string.IsNullOrEmpty(Result.Emoji) ? 0.4 : 1.0;
        }
    }

    private void BuildPalette()
    {
        var items = new List<UIElement>();
        foreach (var (hex, _) in Models.TabColors.All)
        {
            var btn = new Button
            {
                Width = 22, Height = 22,
                Margin = new Thickness(2),
                BorderThickness = new Thickness(Result.ColorHex == hex ? 2 : 0),
                BorderBrush = Brushes.Black,
                Background = new SolidColorBrush(TabChip.ColorFromHex(hex)),
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            btn.Click += (_, _) =>
            {
                Result.ColorHex = (string)btn.Tag;
                BuildPalette();
            };
            items.Add(btn);
        }
        ColorPalette.ItemsSource = items;
    }

    private void RefreshTags()
    {
        var items = new List<UIElement>();
        foreach (var tag in Result.Tags)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(TabChip.ColorFromHex(Result.ColorHex))
                { Opacity = 0.18 },
                Padding = new Thickness(8, 3, 6, 3),
                Margin = new Thickness(2),
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock { Text = "#" + tag, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var x = new Button { Content = "✕", FontSize = 9, Margin = new Thickness(4,0,0,0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2) };
            x.Click += (_, _) => { Result.Tags.Remove(tag); RefreshTags(); };
            stack.Children.Add(x);
            border.Child = stack;
            items.Add(border);
        }
        TagsList.ItemsSource = items;
    }

    private void PickFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Escolher pasta",
            InitialDirectory = string.IsNullOrEmpty(PathBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : PathBox.Text,
        };
        if (dlg.ShowDialog() == true)
        {
            PathBox.Text = dlg.FolderName;
            if (string.IsNullOrEmpty(NameBox.Text))
                NameBox.Text = System.IO.Path.GetFileName(dlg.FolderName);
        }
    }

    private void AddTagButton_Click(object sender, RoutedEventArgs e) => TryAddTag();
    private void TagInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { TryAddTag(); e.Handled = true; }
    }

    private void TryAddTag()
    {
        var t = TagInputBox.Text.Trim().Replace("#", "");
        if (!string.IsNullOrEmpty(t) && !Result.Tags.Contains(t)) Result.Tags.Add(t);
        TagInputBox.Text = "";
        RefreshTags();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Result.Name = NameBox.Text.Trim();
        Result.Path = PathBox.Text.Trim();
        // Emoji is updated by EmojiPickerButton_Click; nothing else to do here.
        Result.InitialCommand = string.IsNullOrEmpty(CommandBox.Text) ? null : CommandBox.Text;
        if (string.IsNullOrEmpty(Result.Name) || string.IsNullOrEmpty(Result.Path))
        {
            MessageBox.Show("Preencha nome e caminho.", "Favorito", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show($"Deletar favorito \"{Result.Name}\"?",
            "Confirmar", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;
        RequestedDelete = true;
        DialogResult = true;
        Close();
    }
}
