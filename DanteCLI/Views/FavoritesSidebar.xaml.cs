using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is FavoriteRowVM vm) AppState.Shared.OpenFavorite(vm.Source);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var fav = new Favorite
        {
            Name = "Novo favorito",
            Path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        var dlg = new FavoriteEditorDialog(fav, isNew: true) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) AppState.Shared.Favorites.Add(dlg.Result);
    }

    private void OpenInTab_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is FavoriteRowVM vm) AppState.Shared.OpenFavorite(vm.Source);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not FavoriteRowVM vm) return;
        var dlg = new FavoriteEditorDialog(vm.Source, isNew: false) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            if (dlg.RequestedDelete)
            {
                AppState.Shared.Favorites.Remove(vm.Source);
            }
            else
            {
                var idx = AppState.Shared.Favorites.IndexOf(vm.Source);
                if (idx >= 0) AppState.Shared.Favorites[idx] = dlg.Result;
            }
            Refresh();
        }
    }

    private void RevealInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not FavoriteRowVM vm) return;
        try { System.Diagnostics.Process.Start("explorer.exe", vm.Source.Path); } catch { }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not FavoriteRowVM vm) return;
        var ok = MessageBox.Show($"Deletar \"{vm.Source.Name}\"?", "Confirmar",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ok == MessageBoxResult.OK)
            AppState.Shared.Favorites.Remove(vm.Source);
    }

    public sealed class FavoriteRowVM
    {
        public FavoriteRowVM(Favorite source) { Source = source; }
        public Favorite Source { get; }
        public string Name => Source.Name;
        public string DisplayPath => Source.DisplayPath;
        public string Emoji => Source.Emoji ?? "📁";
    }
}
