using System.Collections.Generic;
using System.Linq;
using DanteCLI.Models;
using DanteCLI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DanteCLI.Views;

public sealed partial class FavoritesSidebar : UserControl
{
    private string _query = "";

    public FavoritesSidebar()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        AppState.Shared.Favorites.CollectionChanged += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var src = AppState.Shared.Favorites
            .Where(f => string.IsNullOrEmpty(_query)
                        || f.Name.Contains(_query, System.StringComparison.OrdinalIgnoreCase)
                        || f.Path.Contains(_query, System.StringComparison.OrdinalIgnoreCase)
                        || f.Tags.Any(t => t.Contains(_query, System.StringComparison.OrdinalIgnoreCase)))
            .Select(f => new FavoriteRowVM(f))
            .ToList();
        List.ItemsSource = src;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _query = sender.Text ?? "";
        Refresh();
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FavoriteRowVM vm)
        {
            AppState.Shared.OpenFavorite(vm.Source);
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: open editor sheet (next iteration)
        var fav = new Favorite { Name = "Novo favorito", Path = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) };
        AppState.Shared.Favorites.Add(fav);
    }

    private sealed class FavoriteRowVM
    {
        public FavoriteRowVM(Favorite source) { Source = source; }
        public Favorite Source { get; }
        public string Name => Source.Name;
        public string DisplayPath => Source.DisplayPath;
        public string Emoji => Source.Emoji ?? "";
        public Visibility HasEmoji => string.IsNullOrEmpty(Source.Emoji) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoEmoji => string.IsNullOrEmpty(Source.Emoji) ? Visibility.Visible : Visibility.Collapsed;
    }
}
