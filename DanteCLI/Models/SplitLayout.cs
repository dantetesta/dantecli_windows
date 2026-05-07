using System.Collections.Generic;

namespace DanteCLI.Models;

public enum SplitCategory { Horizontal, Vertical, Grid }

public sealed record SplitLayout(int Cols, int Rows)
{
    public int Capacity => Cols * Rows;

    public string Label => (Cols, Rows) switch
    {
        ( > 1, 1) => $"{Cols} lado a lado",
        (1, > 1) => $"{Rows} empilhados",
        _        => $"{Cols} × {Rows} ({Capacity} painéis)",
    };

    public SplitCategory Category =>
        Rows == 1 ? SplitCategory.Horizontal :
        Cols == 1 ? SplitCategory.Vertical :
        SplitCategory.Grid;

    public static IReadOnlyList<SplitLayout> Presets { get; } = new SplitLayout[]
    {
        new(2, 1), new(3, 1), new(4, 1), new(5, 1), new(6, 1),
        new(1, 2), new(1, 3), new(1, 4), new(1, 5), new(1, 6),
        new(2, 2), new(3, 2), new(2, 3), new(3, 3), new(4, 3), new(3, 4), new(4, 4),
    };
}

public sealed record SplitWorkspace(IReadOnlyList<System.Guid> TabIds, SplitLayout Layout);
