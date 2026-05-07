using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DanteCLI.Models;

public enum TabKind { Terminal, Editor }

public sealed class TerminalTab : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();

    private string _title = "shell";
    public string Title { get => _title; set => Set(ref _title, value); }

    private string _colorHex = TabColors.Neutral;
    public string ColorHex { get => _colorHex; set => Set(ref _colorHex, value); }

    private string? _emoji;
    public string? Emoji { get => _emoji; set => Set(ref _emoji, value); }

    private TabKind _kind = TabKind.Terminal;
    public TabKind Kind { get => _kind; set => Set(ref _kind, value); }

    public string? WorkingDirectory { get; set; }
    public string? InitialCommand { get; set; }

    /// File path open in the editor (editor kind only).
    public string? FileUrl { get; set; }

    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }

    public bool IsEditor => Kind == TabKind.Editor;
    public bool IsTerminal => Kind == TabKind.Terminal;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
