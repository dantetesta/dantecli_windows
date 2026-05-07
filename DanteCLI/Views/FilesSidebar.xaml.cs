using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DanteCLI.ViewModels;
using Microsoft.Win32;

namespace DanteCLI.Views;

public partial class FilesSidebar : UserControl
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

        RootLabel.Text = string.IsNullOrEmpty(Path.GetFileName(rootPath))
            ? rootPath
            : Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RootPath.Text = rootPath.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? "~" + rootPath[home.Length..]
            : rootPath;

        Tree.Items.Clear();
        foreach (var item in BuildItems(rootPath))
            Tree.Items.Add(item);
    }

    private static System.Collections.Generic.IEnumerable<TreeViewItem> BuildItems(string dir)
    {
        string[] dirs, files;
        try
        {
            dirs = Directory.GetDirectories(dir);
            files = Directory.GetFiles(dir);
        }
        catch (UnauthorizedAccessException) { yield break; }
        Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var d in dirs)
        {
            var item = new TreeViewItem { Header = "📁 " + Path.GetFileName(d), Tag = d };
            item.Items.Add(null);
            item.Expanded += DirectoryItem_Expanded;
            yield return item;
        }
        foreach (var f in files)
        {
            yield return new TreeViewItem { Header = "📄 " + Path.GetFileName(f), Tag = f };
        }
    }

    private static void DirectoryItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item) return;
        if (item.Items.Count == 1 && item.Items[0] == null)
        {
            item.Items.Clear();
            if (item.Tag is string path)
            {
                foreach (var child in BuildItems(path))
                    item.Items.Add(child);
            }
        }
        e.Handled = true;
    }

    private void ChooseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Escolher pasta",
            InitialDirectory = AppState.Shared.Settings.FileBrowserRoot
        };
        if (dlg.ShowDialog() == true)
        {
            AppState.Shared.Settings.FileBrowserRoot = dlg.FolderName;
            Refresh();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
}
