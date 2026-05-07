using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DanteCLI.Models;
using DanteCLI.ViewModels;

namespace DanteCLI.Views;

public partial class EmojiPickerWindow : Window
{
    public string? Selected { get; private set; }
    public bool ClearedOnPurpose { get; private set; }

    private string _categoryName;
    private string _query = "";

    public EmojiPickerWindow()
    {
        InitializeComponent();
        // Default category is "Sugeridos pra Dev" (skip Recentes if empty)
        _categoryName = AppState.Shared.Settings.RecentEmojis.Count > 0
            ? "Recentes"
            : EmojiCatalog.Categories[1].Name;
        Loaded += (_, _) =>
        {
            BuildCategoryButtons();
            BuildEmojiGrid();
            SearchBox.Focus();
        };
    }

    private IEnumerable<EmojiCategory> EffectiveCategories
    {
        get
        {
            var recents = AppState.Shared.Settings.RecentEmojis;
            foreach (var cat in EmojiCatalog.Categories)
            {
                if (cat.Name == "Recentes")
                {
                    if (recents.Count > 0)
                        yield return new EmojiCategory("Recentes", "🕒", recents);
                }
                else yield return cat;
            }
        }
    }

    private void BuildCategoryButtons()
    {
        var items = new List<UIElement>();
        foreach (var cat in EffectiveCategories)
        {
            var b = new Button
            {
                Content = cat.Symbol,
                FontSize = 14,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Background = _categoryName == cat.Name
                    ? new SolidColorBrush(Color.FromArgb(0x2F, 0x0A, 0x84, 0xFF))
                    : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = cat.Name,
                Tag = cat.Name,
            };
            b.Click += (_, _) =>
            {
                _categoryName = (string)b.Tag;
                BuildCategoryButtons();
                BuildEmojiGrid();
            };
            items.Add(b);
        }
        CategoriesList.ItemsSource = items;
    }

    private void BuildEmojiGrid()
    {
        var emojis = string.IsNullOrEmpty(_query)
            ? (EffectiveCategories.FirstOrDefault(c => c.Name == _categoryName)?.Emojis
               ?? EmojiCatalog.Categories[1].Emojis)
            : EmojiCatalog.Categories.SelectMany(c => c.Emojis)
                .Where(e => e.Contains(_query)).Distinct().ToList();

        var items = new List<UIElement>();
        foreach (var e in emojis)
        {
            var b = new Button
            {
                Content = e,
                FontSize = 20,
                Width = 36, Height = 36,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = e,
            };
            b.Click += (_, _) =>
            {
                Selected = (string)b.Tag;
                AppState.Shared.RememberEmoji(Selected!);
                DialogResult = true;
                Close();
            };
            items.Add(b);
        }
        EmojiGrid.ItemsSource = items;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text?.Trim() ?? "";
        BuildEmojiGrid();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Selected = null;
        ClearedOnPurpose = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
