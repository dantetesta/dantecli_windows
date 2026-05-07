using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DanteCLI.Models;
using DanteCLI.ViewModels;
using DanteCLI.Views;

namespace DanteCLI;

public partial class MainWindow : Window
{
    private readonly Dictionary<TerminalTab, FrameworkElement> _tabBodies = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "DANTE CLI";

        var state = AppState.Shared;
        state.Tabs.CollectionChanged += Tabs_CollectionChanged;
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.ActiveTab)) ShowActiveTab();
        };

        Closing += MainWindow_Closing;

        RebuildTabStrip();
        ShowActiveTab();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Tear down all live terminal sessions before unwinding the dispatcher.
        var terminals = _tabBodies.Values.OfType<TerminalView>().ToList();
        if (terminals.Count == 0) return;

        e.Cancel = true; // wait for cleanup, then close ourselves
        Closing -= MainWindow_Closing;
        try
        {
            await Task.WhenAll(terminals.Select(t => t.ForceShutdownAsync()));
        }
        catch { }
        finally
        {
            // Re-trigger close once cleanup is done (no longer cancelled).
            Application.Current.Dispatcher.BeginInvoke(new Action(Close));
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
        ShowActiveTab();
    }

    private void RebuildTabStrip()
    {
        var state = AppState.Shared;
        var items = new List<TabChip>();
        foreach (var tab in state.Tabs)
        {
            var chip = new TabChip();
            chip.Bind(tab, isActive: state.ActiveTab == tab);
            chip.Selected += (_, t) => { state.ActiveTab = t; RebuildTabStrip(); ShowActiveTab(); };
            chip.Closed   += (_, t) => state.CloseTab(t);
            items.Add(chip);
        }
        TabStrip.ItemsSource = items;
    }

    private void ShowActiveTab()
    {
        var state = AppState.Shared;
        TabsHost.Children.Clear();
        if (state.ActiveTab is null) return;

        if (!_tabBodies.TryGetValue(state.ActiveTab, out var body))
        {
            var view = new TerminalView();
            view.Bind(state.ActiveTab);
            body = view;
            _tabBodies[state.ActiveTab] = body;
        }
        TabsHost.Children.Add(body);
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        AppState.Shared.NewTab();
    }
}
