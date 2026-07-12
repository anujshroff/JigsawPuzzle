using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using JigsawPuzzle.Game;

namespace JigsawPuzzle.Controls;

/// <summary>
/// The play surface: renders all pieces (pre-baked bitmaps) back-to-front, hit-tests along
/// the true piece outlines, and drags whole connectivity groups. On release it asks the game
/// to snap-and-link, and raises <see cref="Solved"/> once every piece is connected.
/// </summary>
public sealed class PuzzleCanvas : Control
{
    private static readonly IBrush BackgroundBrush = new ImmutableLinearGradientBrush(
        [
            new ImmutableGradientStop(0, Color.FromRgb(0x22, 0x23, 0x33)),
            new ImmutableGradientStop(1, Color.FromRgb(0x12, 0x12, 0x1A)),
        ],
        startPoint: new RelativePoint(0.5, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0.5, 1, RelativeUnit.Relative));

    private PuzzleGame? _game;
    private List<PuzzlePiece>? _dragGroup;
    private Point _lastPointer;

    /// <summary>Raised once, when the last link completes the puzzle.</summary>
    public event Action? Solved;

    public PuzzleGame? Game
    {
        get => _game;
        set
        {
            _game = value;
            _dragGroup = null;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        // Full-bounds background also makes the whole control hit-testable.
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        if (_game is null)
            return;

        foreach (var piece in _game.Pieces)
            context.DrawImage(piece.Bitmap, new Rect(piece.CurrentPos.X, piece.CurrentPos.Y, piece.Size.Width, piece.Size.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_game is null || _game.IsSolved || _dragGroup is not null)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(this);
        var hit = HitTest(position);
        if (hit is null)
            return;

        _dragGroup = _game.GroupMembers(hit);
        RaiseToTop(_dragGroup);
        _lastPointer = position;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragGroup is null || _game is null)
            return;

        var position = e.GetPosition(this);
        var dx = position.X - _lastPointer.X;
        var dy = position.Y - _lastPointer.Y;
        _lastPointer = position;
        if (dx == 0 && dy == 0)
            return;

        // Keep the group's bounding box inside the canvas.
        var (minX, minY, maxX, maxY) = GroupBounds(_dragGroup);
        dx = ClampDelta(dx, -minX, Bounds.Width - maxX);
        dy = ClampDelta(dy, -minY, Bounds.Height - maxY);

        foreach (var piece in _dragGroup)
            piece.CurrentPos = new Point(piece.CurrentPos.X + dx, piece.CurrentPos.Y + dy);

        // Live snap: as soon as the group is close enough it clicks in and the drag settles.
        if (_game.TrySnap(_dragGroup[0]))
        {
            _dragGroup = null;
            e.Pointer.Capture(null);
            if (_game.IsSolved)
                Solved?.Invoke();
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragGroup is null)
            return;
        e.Pointer.Capture(null);
        EndDrag();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag(); // e.g. window deactivated mid-drag — settle the group where it is
    }

    private void EndDrag()
    {
        if (_dragGroup is null || _game is null)
            return;
        var dragged = _dragGroup[0];
        _dragGroup = null;

        _game.TrySnap(dragged);
        InvalidateVisual();
        if (_game.IsSolved)
            Solved?.Invoke();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_game is null)
            return;
        foreach (var group in _game.Groups())
        {
            var (minX, minY, maxX, maxY) = GroupBounds(group);
            var dx = ClampDelta(0, -minX, Bounds.Width - maxX);
            var dy = ClampDelta(0, -minY, Bounds.Height - maxY);
            if (dx == 0 && dy == 0)
                continue;
            foreach (var piece in group)
                piece.CurrentPos = new Point(piece.CurrentPos.X + dx, piece.CurrentPos.Y + dy);
        }
        InvalidateVisual();
    }

    /// <summary>Topmost piece whose actual outline contains the point.</summary>
    private PuzzlePiece? HitTest(Point point)
    {
        var pieces = _game!.Pieces;
        for (var i = pieces.Count - 1; i >= 0; i--)
        {
            var piece = pieces[i];
            var local = new Point(point.X - piece.CurrentPos.X, point.Y - piece.CurrentPos.Y);
            if (local.X < 0 || local.Y < 0 || local.X > piece.Size.Width || local.Y > piece.Size.Height)
                continue;
            if (piece.Geometry.FillContains(local))
                return piece;
        }
        return null;
    }

    /// <summary>Moves the group to the end of the draw order (topmost), preserving its internal order.</summary>
    private void RaiseToTop(List<PuzzlePiece> group)
    {
        var members = new HashSet<PuzzlePiece>(group);
        var pieces = _game!.Pieces;
        pieces.RemoveAll(members.Contains);
        pieces.AddRange(group);
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) GroupBounds(List<PuzzlePiece> group)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var piece in group)
        {
            minX = Math.Min(minX, piece.CurrentPos.X);
            minY = Math.Min(minY, piece.CurrentPos.Y);
            maxX = Math.Max(maxX, piece.CurrentPos.X + piece.Size.Width);
            maxY = Math.Max(maxY, piece.CurrentPos.Y + piece.Size.Height);
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>Clamp that tolerates an inverted range (group larger than the canvas).</summary>
    private static double ClampDelta(double value, double low, double high)
        => low <= high ? Math.Clamp(value, low, high) : Math.Clamp(value, high, low);
}
