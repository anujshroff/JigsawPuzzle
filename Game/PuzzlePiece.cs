using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JigsawPuzzle.Game;

/// <summary>
/// One puzzle piece. Positions refer to the top-left of the piece's render bounds
/// (the cell rect uniformly inflated by the tab overhang), so the difference between
/// two pieces' <see cref="CorrectPos"/> equals their required relative on-screen offset.
/// </summary>
public sealed class PuzzlePiece
{
    /// <summary>Row-major index: Row * Cols + Col. Stable identity for the disjoint set.</summary>
    public required int Index { get; init; }

    public required int Row { get; init; }
    public required int Col { get; init; }

    /// <summary>Render-bounds top-left in board space when solved (board origin is arbitrary).</summary>
    public required Point CorrectPos { get; init; }

    /// <summary>Current render-bounds top-left on the canvas.</summary>
    public Point CurrentPos { get; set; }

    /// <summary>Piece outline in piece-local coordinates (origin = render-bounds top-left).</summary>
    public required Geometry Geometry { get; init; }

    /// <summary>Pre-rendered piece image, sized to the render bounds.</summary>
    public required RenderTargetBitmap Bitmap { get; init; }

    /// <summary>Render-bounds size in logical pixels.</summary>
    public required Size Size { get; init; }
}
