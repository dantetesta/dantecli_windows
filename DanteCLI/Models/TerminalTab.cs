using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DanteCLI.Models;

public enum TabKind { Terminal, Editor }

public sealed class TerminalTab : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();

    private string _title = "zsh";
    public string Title { get => _title; set => Set(ref _title, value); }

    private string _colorHex = TabColors.Neutral;
    public string ColorHex { get => _colorHex; set => Set(ref _colorHex, value); }

    private string? _emoji;
    public string? Emoji { get => _emoji; set => Set(ref _emoji, value); }

    private TabKind _kind = TabKind.Terminal;
    public TabKind Kind { get => _kind; set => Set(ref _kind, value); }

    /// Working directory the spawn happened in (terminal kind only).
    public string? WorkingDirectory { get; set; }

    /// Command to feed into the shell after spawn.
    public string? InitialCommand { get; set; }

    /// File path open in the editor (editor kind only).
    public string? FileUrl { get; set; }

    /// Dirty flag for editor.
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
