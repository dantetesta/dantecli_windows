using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using DanteCLI.Models;

namespace DanteCLI.Views;

public partial class EditorView : UserControl
{
    private TerminalTab? _tab;
    private bool _suppressDirty;

    public EditorView()
    {
        InitializeComponent();
        Editor.TextChanged += (_, _) =>
        {
            if (_suppressDirty || _tab is null) return;
            _tab.IsDirty = true;
            UpdateDirtyBadge();
        };
        Editor.PreviewKeyDown += Editor_PreviewKeyDown;
    }

    public void Bind(TerminalTab tab)
    {
        _tab = tab;
        UpdatePath();
        ApplySyntax();
        _ = LoadAsync();
    }

    private void UpdatePath()
    {
        if (_tab?.FileUrl is null) { PathLabel.Text = ""; return; }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var p = _tab.FileUrl;
        PathLabel.Text = p.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? "~" + p[home.Length..] : p;
    }

    private void ApplySyntax()
    {
        if (_tab?.FileUrl is null) return;
        var lang = EditorLanguageExtensions.Detect(_tab.FileUrl);
        LangChip.Text = lang.Label();
        var name = lang.AvalonHighlight();
        Editor.SyntaxHighlighting = string.IsNullOrEmpty(name)
            ? null
            : HighlightingManager.Instance.GetDefinition(name);
    }

    private async Task LoadAsync()
    {
        if (_tab?.FileUrl is null) return;
        var path = _tab.FileUrl;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 5_000_000)
            {
                Editor.Text = $"[Arquivo grande ({info.Length / 1024} KB) — abertura desabilitada.]";
                Editor.IsReadOnly = true;
                return;
            }
            var text = await File.ReadAllTextAsync(path);
            if (text.Length >= 1 && text.AsSpan(0, Math.Min(text.Length, 8192)).IndexOf('\0') >= 0)
            {
                Editor.Text = "[Arquivo binário — abertura como texto desabilitada.]";
                Editor.IsReadOnly = true;
                return;
            }
            _suppressDirty = true;
            Editor.Text = text;
            _suppressDirty = false;
            if (_tab is not null) { _tab.IsDirty = false; UpdateDirtyBadge(); }
        }
        catch (Exception ex)
        {
            Editor.Text = $"[Erro ao carregar: {ex.Message}]";
            Editor.IsReadOnly = true;
        }
    }

    public bool Save()
    {
        if (_tab?.FileUrl is null) return false;
        try
        {
            File.WriteAllText(_tab.FileUrl, Editor.Text);
            _tab.IsDirty = false;
            UpdateDirtyBadge();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao salvar: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void UpdateDirtyBadge()
    {
        var dirty = _tab?.IsDirty ?? false;
        DirtyBadge.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = dirty;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Save();
            e.Handled = true;
        }
    }
}
