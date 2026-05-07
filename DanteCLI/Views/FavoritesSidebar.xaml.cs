using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DanteCLI.Models;
using DanteCLI.ViewModels;

namespace DanteCLI.Views;

public partial class FavoritesSidebar : UserControl
{
    private string _query = "";

    public FavoritesSidebar()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        AppState.Shared.Favorites.CollectionChanged += (_, _) =>
            Dispatcher.Invoke(Refresh);
    }

    private void Refresh()
    {
        var src = AppState.Shared.Favorites
            .Where(f => string.IsNullOrEmpty(_query)
                        || f.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)
                        || f.Path.Contains(_query, StringComparison.OrdinalIgnoreCase)
                        || f.Tags.Any(t => t.Contains(_query, StringComparison.OrdinalIgnoreCase)))
            .Select(f => new FavoriteRowVM(f))
            .ToList();
        List.ItemsSource = src;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text;
        Refresh();
    }

    private void List_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (List.SelectedItem is FavoriteRowVM vm) AppState.Shared.OpenFavorite(vm.Source);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var fav = new Favorite
        {
            Name = "Novo favorito",
            Path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        AppState.Shared.Favorites.Add(fav);
    }

    private sealed class FavoriteRowVM
    {
        public FavoriteRowVM(Favorite source) { Source = source; }
        public Favorite Source { get; }
        public string Name => Source.Name;
        public string DisplayPath => Source.DisplayPath;
        public string Emoji => Source.Emoji ?? "📁";
    }
}
