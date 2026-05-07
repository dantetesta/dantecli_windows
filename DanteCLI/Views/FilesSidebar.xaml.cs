using System;
using System.Collections.Generic;
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
            ? "~" + rootPath[home.Length..] : rootPath;

        Tree.Items.Clear();
        foreach (var item in BuildItems(rootPath))
            Tree.Items.Add(item);
    }

    private IEnumerable<TreeViewItem> BuildItems(string dir)
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
            yield return MakeFolderItem(d);
        foreach (var f in files)
            yield return MakeFileItem(f);
    }

    private TreeViewItem MakeFolderItem(string path)
    {
        var item = new TreeViewItem { Header = "📁 " + Path.GetFileName(path), Tag = path };
        item.Items.Add(null!);
        item.Expanded += DirectoryItem_Expanded;
        item.ContextMenu = BuildFolderMenu(path);
        item.MouseDoubleClick += (s, e) =>
        {
            if (s is TreeViewItem t && t == item)
            {
                e.Handled = true;
                AppState.Shared.NewTab(title: Path.GetFileName(path), path: path,
                    colorHex: Models.TabColors.Blue, emoji: "📁");
            }
        };
        return item;
    }

    private TreeViewItem MakeFileItem(string path)
    {
        var item = new TreeViewItem { Header = "📄 " + Path.GetFileName(path), Tag = path };
        item.ContextMenu = BuildFileMenu(path);
        item.MouseDoubleClick += (s, e) =>
        {
            if (s is TreeViewItem t && t == item)
            {
                e.Handled = true;
                AppState.Shared.OpenFileInEditor(path);
            }
        };
        return item;
    }

    private void DirectoryItem_Expanded(object sender, RoutedEventArgs e)
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

    private ContextMenu BuildFolderMenu(string path)
    {
        var m = new ContextMenu();
        AddItem(m, "Abrir terminal aqui", () =>
            AppState.Shared.NewTab(title: Path.GetFileName(path), path: path,
                colorHex: Models.TabColors.Blue, emoji: "📁"));
        AddItem(m, "Adicionar aos Favoritos", () =>
        {
            var fav = new Models.Favorite
            {
                Name = Path.GetFileName(path),
                Path = path,
                ColorHex = Models.TabColors.Blue,
                Emoji = "📁",
            };
            var dlg = new FavoriteEditorDialog(fav, isNew: true) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) AppState.Shared.Favorites.Add(dlg.Result);
        });
        m.Items.Add(new Separator());
        AddItem(m, "Colar arquivos copiados", () => PasteInto(path));
        AddItem(m, "Novo arquivo", () => CreateChild(path, isDirectory: false));
        AddItem(m, "Nova pasta", () => CreateChild(path, isDirectory: true));
        m.Items.Add(new Separator());
        AddItem(m, "Revelar no Explorer", () =>
            System.Diagnostics.Process.Start("explorer.exe", path));
        AddItem(m, "Copiar caminho", () => Clipboard.SetText(path));
        m.Items.Add(new Separator());
        AddItem(m, "Renomear", () => RenameInline(path));
        AddItem(m, "Mover pra Lixeira", () => Recycle(path));
        return m;
    }

    private ContextMenu BuildFileMenu(string path)
    {
        var m = new ContextMenu();
        AddItem(m, "Abrir no editor", () => AppState.Shared.OpenFileInEditor(path));
        var dir = Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddItem(m, "Abrir terminal na pasta", () =>
            AppState.Shared.NewTab(title: Path.GetFileName(dir), path: dir,
                colorHex: Models.TabColors.Neutral));
        m.Items.Add(new Separator());
        AddItem(m, "Copiar arquivo (Ctrl+C)", () => CopyFiles(new[] { path }, cut: false));
        AddItem(m, "Recortar arquivo (Ctrl+X)", () => CopyFiles(new[] { path }, cut: true));
        m.Items.Add(new Separator());
        AddItem(m, "Revelar no Explorer", () =>
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""));
        AddItem(m, "Copiar caminho", () => Clipboard.SetText(path));
        m.Items.Add(new Separator());
        AddItem(m, "Renomear", () => RenameInline(path));
        AddItem(m, "Mover pra Lixeira", () => Recycle(path));
        return m;
    }

    private static void AddItem(ContextMenu m, string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        m.Items.Add(item);
    }

    private static void CopyFiles(IEnumerable<string> paths, bool cut)
    {
        try
        {
            var data = new System.Windows.DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { });
            var coll = new System.Collections.Specialized.StringCollection();
            foreach (var p in paths) coll.Add(p);
            data.SetFileDropList(coll);
            // Mark as cut/copy via "Preferred DropEffect"
            var drop = new byte[] { (byte)(cut ? 0x02 : 0x05), 0, 0, 0 };
            var ms = new MemoryStream(drop);
            data.SetData("Preferred DropEffect", ms);
            Clipboard.SetDataObject(data, copy: true);
        }
        catch { }
    }

    private void PasteInto(string folder)
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return;
            var coll = Clipboard.GetFileDropList();
            bool isCut = false;
            if (Clipboard.GetDataObject() is { } obj && obj.GetDataPresent("Preferred DropEffect"))
            {
                if (obj.GetData("Preferred DropEffect") is MemoryStream ms)
                {
                    var b = ms.ToArray();
                    if (b.Length > 0 && (b[0] & 0x02) != 0) isCut = true;
                }
            }
            foreach (var src in coll)
            {
                if (string.IsNullOrEmpty(src)) continue;
                var name = Path.GetFileName(src);
                var dest = Path.Combine(folder, name);
                if (File.Exists(dest) || Directory.Exists(dest))
                    dest = MakeUnique(folder, name);
                try
                {
                    if (Directory.Exists(src)) CopyDir(src, dest, isCut);
                    else if (isCut) File.Move(src, dest);
                    else File.Copy(src, dest);
                }
                catch { /* skip failed */ }
            }
            Refresh();
        }
        catch { }
    }

    private static string MakeUnique(string dir, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            var cand = Path.Combine(dir, $"{stem} {i}{ext}");
            if (!File.Exists(cand) && !Directory.Exists(cand)) return cand;
        }
    }

    private static void CopyDir(string src, string dest, bool move)
    {
        if (move) { Directory.Move(src, dest); return; }
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)), move: false);
    }

    private void CreateChild(string folder, bool isDirectory)
    {
        var baseName = isDirectory ? "Nova pasta" : "Novo arquivo.txt";
        var url = Path.Combine(folder, baseName);
        if (File.Exists(url) || Directory.Exists(url)) url = MakeUnique(folder, baseName);
        try
        {
            if (isDirectory) Directory.CreateDirectory(url);
            else File.WriteAllText(url, "");
        }
        catch { }
        Refresh();
    }

    private void RenameInline(string path)
    {
        // Quick approach: prompt via simple dialog
        var dlg = new TextInputDialog("Renomear", "Novo nome:", Path.GetFileName(path)) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value))
        {
            var newPath = Path.Combine(Path.GetDirectoryName(path)!, dlg.Value);
            try
            {
                if (Directory.Exists(path)) Directory.Move(path, newPath);
                else File.Move(path, newPath);
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falha: " + ex.Message);
            }
        }
    }

    private void Recycle(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Falha: " + ex.Message);
        }
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
