using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using DanteCLI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DanteCLI.Views;

public sealed partial class FilesSidebar : UserControl
{
    public FilesSidebar()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var rootPath = AppState.Shared.Settings.FileBrowserRoot;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            rootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RootLabel.Text = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(RootLabel.Text)) RootLabel.Text = rootPath;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RootPath.Text = rootPath.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? "~" + rootPath[home.Length..]
            : rootPath;

        Tree.RootNodes.Clear();
        foreach (var node in BuildChildren(rootPath))
            Tree.RootNodes.Add(node);
    }

    private static System.Collections.Generic.IEnumerable<TreeViewNode> BuildChildren(string dir)
    {
        IEnumerable<string> dirs;
        IEnumerable<string> files;
        try
        {
            dirs = Directory.EnumerateDirectories(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            files = Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException) { yield break; }

        foreach (var d in dirs)
        {
            var node = new TreeViewNode { Content = "📁 " + Path.GetFileName(d), HasUnrealizedChildren = true };
            node.Children.Add(new TreeViewNode { Content = "..." });
            yield return node;
        }
        foreach (var f in files)
            yield return new TreeViewNode { Content = "📄 " + Path.GetFileName(f) };
    }

    private async void ChooseRoot_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(App.Window);
        InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            AppState.Shared.Settings.FileBrowserRoot = folder.Path;
            Refresh();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private System.Collections.Generic.IEnumerable<TreeViewNode> Wrap(System.Collections.Generic.IEnumerable<TreeViewNode> seq) => seq;
}
