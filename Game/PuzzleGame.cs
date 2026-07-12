using Avalonia;

namespace JigsawPuzzle.Game;

/// <summary>
/// State of one puzzle: the pieces (in draw order), their connectivity groups, and the
/// snap-and-link rules. The puzzle is solved when every piece is in one connected group —
/// there is no fixed board location.
/// </summary>
public sealed class PuzzleGame : IDisposable
{
    private readonly PuzzlePiece[] _byIndex;
    private readonly DisjointSet _groups;
    private readonly int _rows;
    private readonly int _cols;

    public PuzzleGame(List<PuzzlePiece> pieces, int rows, int cols, double snapTolerance)
    {
        Pieces = pieces;
        _rows = rows;
        _cols = cols;
        SnapTolerance = snapTolerance;
        _groups = new DisjointSet(pieces.Count);
        _byIndex = new PuzzlePiece[pieces.Count];
        foreach (var piece in pieces)
            _byIndex[piece.Index] = piece;
    }

    /// <summary>Pieces in draw order: first is drawn at the back, last on top.</summary>
    public List<PuzzlePiece> Pieces { get; }

    /// <summary>Max per-axis positional error (logical px) at which neighbors link.</summary>
    public double SnapTolerance { get; }

    public bool IsSolved => _groups.SetCount == 1;

    /// <summary>All pieces connected to <paramref name="piece"/>, in current draw order.</summary>
    public List<PuzzlePiece> GroupMembers(PuzzlePiece piece)
    {
        var root = _groups.Find(piece.Index);
        return Pieces.Where(p => _groups.Find(p.Index) == root).ToList();
    }

    /// <summary>Current connectivity groups (each a list of pieces).</summary>
    public IEnumerable<List<PuzzlePiece>> Groups()
        => Pieces.GroupBy(p => _groups.Find(p.Index)).Select(g => g.ToList());

    /// <summary>
    /// Links the group containing <paramref name="dragged"/> to any grid neighbors within
    /// snap tolerance, aligning the dragged group exactly onto the stationary one, and
    /// cascades until no more merges happen. Returns true if anything linked.
    /// </summary>
    public bool TrySnap(PuzzlePiece dragged)
    {
        var mergedAny = false;
        while (TrySnapOnce(dragged))
            mergedAny = true;
        return mergedAny;
    }

    private bool TrySnapOnce(PuzzlePiece dragged)
    {
        var root = _groups.Find(dragged.Index);
        var members = Pieces.Where(p => _groups.Find(p.Index) == root).ToList();

        foreach (var piece in members)
        {
            foreach (var neighbor in Neighbors(piece))
            {
                if (_groups.Find(neighbor.Index) == root)
                    continue;

                var errorX = (neighbor.CurrentPos.X - piece.CurrentPos.X) - (neighbor.CorrectPos.X - piece.CorrectPos.X);
                var errorY = (neighbor.CurrentPos.Y - piece.CurrentPos.Y) - (neighbor.CorrectPos.Y - piece.CorrectPos.Y);
                if (Math.Abs(errorX) > SnapTolerance || Math.Abs(errorY) > SnapTolerance)
                    continue;

                // Move the dragged group onto the stationary neighbor group for an exact fit.
                foreach (var member in members)
                    member.CurrentPos = new Point(member.CurrentPos.X + errorX, member.CurrentPos.Y + errorY);
                _groups.Union(piece.Index, neighbor.Index);
                return true;
            }
        }
        return false;
    }

    private IEnumerable<PuzzlePiece> Neighbors(PuzzlePiece piece)
    {
        if (piece.Row > 0)
            yield return _byIndex[piece.Index - _cols];
        if (piece.Row < _rows - 1)
            yield return _byIndex[piece.Index + _cols];
        if (piece.Col > 0)
            yield return _byIndex[piece.Index - 1];
        if (piece.Col < _cols - 1)
            yield return _byIndex[piece.Index + 1];
    }

    /// <summary>
    /// Lays the pieces out in a snug tray at the left of the canvas, in random order,
    /// leaving the remaining ~25% of the window free as working room.
    /// </summary>
    public void ArrangeInGrid(Size canvasSize, Random rng)
    {
        for (var i = Pieces.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (Pieces[i], Pieces[j]) = (Pieces[j], Pieces[i]);
        }

        var slots = Slots(canvasSize, Pieces.Count);
        for (var i = 0; i < Pieces.Count; i++)
            Pieces[i].CurrentPos = slots[i];
    }

    /// <summary>
    /// Re-deals every still-loose piece (group of one) into tidy grid slots in random order,
    /// preferring slots that don't overlap any assembled group. Linked groups stay put.
    /// </summary>
    public void TidySingles(Size canvasSize, Random rng)
    {
        var singles = new List<PuzzlePiece>();
        var clusterBounds = new List<Rect>();
        foreach (var group in Groups())
        {
            if (group.Count == 1)
            {
                singles.Add(group[0]);
                continue;
            }
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var piece in group)
            {
                minX = Math.Min(minX, piece.CurrentPos.X);
                minY = Math.Min(minY, piece.CurrentPos.Y);
                maxX = Math.Max(maxX, piece.CurrentPos.X + piece.Size.Width);
                maxY = Math.Max(maxY, piece.CurrentPos.Y + piece.Size.Height);
            }
            clusterBounds.Add(new Rect(minX, minY, maxX - minX, maxY - minY));
        }
        if (singles.Count == 0)
            return;

        for (var i = singles.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (singles[i], singles[j]) = (singles[j], singles[i]);
        }

        // Slot density based on the full piece count (same look as the start layout).
        var slots = Slots(canvasSize, Pieces.Count);
        var pieceSize = Pieces[0].Size;
        var freeSlots = slots
            .Where(slot => !clusterBounds.Any(b => b.Intersects(new Rect(slot, pieceSize))))
            .ToList();
        if (freeSlots.Count < singles.Count)
            freeSlots = slots; // not enough clear space — overlap is unavoidable

        for (var i = 0; i < singles.Count; i++)
            singles[i].CurrentPos = freeSlots[i % freeSlots.Count];
    }

    /// <summary>
    /// Positions of <paramref name="count"/> tray slots, packed row-major from the top-left
    /// into at most the left ~75% of the canvas so the rest stays free as working room.
    /// Pitch adapts to the tray but never exceeds a snug piece-plus-gap spacing; on tight
    /// canvases neighboring pieces overlap like fanned cards.
    /// </summary>
    private List<Point> Slots(Size canvasSize, int count)
    {
        const double margin = 10;
        const double gap = 6;

        // All pieces share the same render-bounds size (uniform tab inflation).
        var pieceW = Pieces[0].Size.Width;
        var pieceH = Pieces[0].Size.Height;
        var trayW = Math.Max(pieceW, canvasSize.Width * 0.75 - margin);
        var trayH = Math.Max(pieceH, canvasSize.Height - margin * 2);

        // Slot grid shaped like the tray measured in piece-sized units.
        var capacityRatio = trayW * pieceH / (trayH * pieceW);
        var gridCols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * capacityRatio)));
        var gridRows = Math.Max(1, (int)Math.Ceiling((double)count / gridCols));
        var pitchX = Math.Min(trayW / gridCols, pieceW + gap);
        var pitchY = Math.Min(trayH / gridRows, pieceH + gap);

        var slots = new List<Point>(gridCols * gridRows);
        for (var i = 0; i < gridCols * gridRows; i++)
        {
            var r = i / gridCols;
            var c = i % gridCols;
            slots.Add(new Point(
                Math.Clamp(margin + c * pitchX, 0, Math.Max(0, canvasSize.Width - pieceW)),
                Math.Clamp(margin + r * pitchY, 0, Math.Max(0, canvasSize.Height - pieceH))));
        }
        return slots;
    }

    public void Dispose()
    {
        foreach (var piece in Pieces)
            piece.Bitmap.Dispose();
    }
}
