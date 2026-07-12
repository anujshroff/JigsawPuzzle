using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace JigsawPuzzle.Game;

/// <summary>
/// Cuts a board bitmap into classic jigsaw pieces: computes the grid, generates one shared
/// bezier tab curve per interior edge, builds each piece's outline geometry, and pre-renders
/// ("bakes") each piece into its own bitmap so the canvas can draw pieces with a single
/// DrawImage each frame.
/// </summary>
public static class PieceFactory
{
    /// <summary>Tab overhang as a fraction of the smaller cell dimension (template max |y| is ~0.32 with jitter).</summary>
    private const double TabOverhangFactor = 0.35;

    private static readonly IBrush OutlineBrush = new ImmutableSolidColorBrush(Color.FromArgb(90, 0, 0, 0));
    private static readonly IBrush ShadowNearBrush = new ImmutableSolidColorBrush(Color.FromArgb(60, 0, 0, 0));
    private static readonly IBrush ShadowFarBrush = new ImmutableSolidColorBrush(Color.FromArgb(32, 0, 0, 0));

    /// <summary>Grid whose piece count approximates the target while keeping pieces roughly square.</summary>
    public static (int Rows, int Cols) ComputeGrid(int targetPieces, double aspect)
    {
        var cols = (int)Math.Clamp(Math.Round(Math.Sqrt(targetPieces * aspect)), 2, 30);
        var rows = (int)Math.Clamp(Math.Round((double)targetPieces / cols), 2, 30);
        return (rows, cols);
    }

    /// <summary>
    /// Creates all pieces for <paramref name="board"/> (already scaled to its on-screen size,
    /// 96 DPI so logical units == pixels), in row-major order (Index = row * cols + col).
    /// </summary>
    public static List<PuzzlePiece> CreatePieces(Bitmap board, int rows, int cols)
    {
        var boardW = board.Size.Width;
        var boardH = board.Size.Height;
        var cellW = boardW / cols;
        var cellH = boardH / rows;
        var minCell = Math.Min(cellW, cellH);
        var tabScale = minCell;                       // template y is scaled by the smaller cell side
        var inflate = TabOverhangFactor * minCell + 1; // uniform render-bounds inflation (+1 for the outline stroke)

        var rng = Random.Shared;

        // One curve per interior edge, shared by both adjacent pieces so they interlock exactly.
        // hEdges[r, c]: boundary between (r-1, c) and (r, c), traced left→right (valid r = 1..rows-1).
        // vEdges[r, c]: boundary between (r, c-1) and (r, c), traced top→bottom (valid c = 1..cols-1).
        var hEdges = new Point[rows, cols][];
        var vEdges = new Point[rows, cols][];
        for (var r = 1; r < rows; r++)
            for (var c = 0; c < cols; c++)
                hEdges[r, c] = TransformEdge(MakeEdgeTemplate(rng), new Point(c * cellW, r * cellH), new Vector(cellW, 0), tabScale);
        for (var c = 1; c < cols; c++)
            for (var r = 0; r < rows; r++)
                vEdges[r, c] = TransformEdge(MakeEdgeTemplate(rng), new Point(c * cellW, r * cellH), new Vector(0, cellH), tabScale);

        var pieces = new List<PuzzlePiece>(rows * cols);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var tl = new Point(c * cellW, r * cellH);
                var tr = new Point((c + 1) * cellW, r * cellH);
                var br = new Point((c + 1) * cellW, (r + 1) * cellH);
                var bl = new Point(c * cellW, (r + 1) * cellH);
                var origin = new Point(tl.X - inflate, tl.Y - inflate);
                var size = new Size(cellW + 2 * inflate, cellH + 2 * inflate);

                var geometry = BuildGeometry(
                    top: r == 0 ? null : hEdges[r, c],
                    right: c == cols - 1 ? null : vEdges[r, c + 1],
                    bottom: r == rows - 1 ? null : hEdges[r + 1, c],
                    left: c == 0 ? null : vEdges[r, c],
                    tl, tr, br, bl, origin);

                var bitmap = BakePiece(board, geometry, origin, size);

                pieces.Add(new PuzzlePiece
                {
                    Index = r * cols + c,
                    Row = r,
                    Col = c,
                    CorrectPos = origin,
                    CurrentPos = origin,
                    Geometry = geometry,
                    Bitmap = bitmap,
                    Size = size,
                });
            }
        }
        return pieces;
    }

    /// <summary>
    /// Classic jigsaw knob in unit edge space (0,0)→(1,0): 13 points forming 4 cubic bezier
    /// segments (shoulder, bulb-left, bulb-right, shoulder). The bulb (x≈0.34–0.66) is wider
    /// than the neck (0.42–0.58), giving the keyhole silhouette. Knob side and jitter are random.
    /// </summary>
    private static Point[] MakeEdgeTemplate(Random rng)
    {
        var s = rng.Next(2) == 0 ? 1.0 : -1.0;
        double J() => (rng.NextDouble() - 0.5) * 0.04; // ±0.02 jitter

        Point P(double x, double y) => new(x + J(), (y + J()) * s);

        return
        [
            new Point(0, 0),                                   // A0 (exact so corners meet)
            P(0.22, -0.02), P(0.35, 0.04), P(0.42, 0.06),      // shoulder → neck-left
            P(0.30, 0.14), P(0.34, 0.30), P(0.50, 0.30),       // bulb-left → bulb apex
            P(0.66, 0.30), P(0.70, 0.14), P(0.58, 0.06),       // bulb-right → neck-right
            P(0.65, 0.04), P(0.78, -0.02),                     // shoulder controls
            new Point(1, 0),                                   // A4 (exact)
        ];
    }

    /// <summary>
    /// Maps a unit-space edge template onto the edge from <paramref name="start"/> along
    /// <paramref name="direction"/>: x follows the edge, y extends along the perpendicular
    /// scaled by <paramref name="tabScale"/> (the smaller cell side, so tabs stay proportionate).
    /// </summary>
    private static Point[] TransformEdge(Point[] template, Point start, Vector direction, double tabScale)
    {
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var nx = -direction.Y / length;
        var ny = direction.X / length;

        var result = new Point[template.Length];
        for (var i = 0; i < template.Length; i++)
        {
            var t = template[i];
            result[i] = new Point(
                start.X + t.X * direction.X + t.Y * tabScale * nx,
                start.Y + t.X * direction.Y + t.Y * tabScale * ny);
        }
        return result;
    }

    /// <summary>
    /// Closed piece outline in piece-local coordinates, walking clockwise:
    /// top L→R, right T→B, bottom R→L (reversed), left B→T (reversed).
    /// Null sides are straight border edges.
    /// </summary>
    private static StreamGeometry BuildGeometry(
        Point[]? top, Point[]? right, Point[]? bottom, Point[]? left,
        Point tl, Point tr, Point br, Point bl, Point origin)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(Local(tl, origin), isFilled: true);
            EmitForward(g, top, tr, origin);
            EmitForward(g, right, br, origin);
            EmitReversed(g, bottom, bl, origin);
            EmitReversed(g, left, tl, origin);
            g.EndFigure(isClosed: true);
        }
        return geometry;
    }

    private static void EmitForward(StreamGeometryContext g, Point[]? curve, Point end, Point origin)
    {
        if (curve is null)
        {
            g.LineTo(Local(end, origin));
            return;
        }
        for (var i = 0; i + 3 < curve.Length; i += 3)
            g.CubicBezierTo(Local(curve[i + 1], origin), Local(curve[i + 2], origin), Local(curve[i + 3], origin));
    }

    /// <summary>Traces a stored curve backwards (a reversed cubic swaps endpoints and control order).</summary>
    private static void EmitReversed(StreamGeometryContext g, Point[]? curve, Point end, Point origin)
    {
        if (curve is null)
        {
            g.LineTo(Local(end, origin));
            return;
        }
        for (var i = curve.Length - 1; i - 3 >= 0; i -= 3)
            g.CubicBezierTo(Local(curve[i - 1], origin), Local(curve[i - 2], origin), Local(curve[i - 3], origin));
    }

    private static Point Local(Point p, Point origin) => new(p.X - origin.X, p.Y - origin.Y);

    /// <summary>Pre-renders one piece: the board clipped to the piece outline, plus a subtle definition stroke.</summary>
    private static RenderTargetBitmap BakePiece(Bitmap board, Geometry localGeometry, Point origin, Size size)
    {
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(size.Width)),
            Math.Max(1, (int)Math.Ceiling(size.Height)));

        var rtb = new RenderTargetBitmap(pixelSize); // default 96 DPI → logical units == pixels
        using (var ctx = rtb.CreateDrawingContext())
        {
            var boundsInBoard = new Rect(origin.X, origin.Y, size.Width, size.Height);
            var src = boundsInBoard.Intersect(new Rect(board.Size));
            var dest = new Rect(src.X - origin.X, src.Y - origin.Y, src.Width, src.Height);

            // Layered drop shadow (cheap blur substitute) baked under the piece for depth.
            using (ctx.PushTransform(Matrix.CreateTranslation(0, 2)))
                ctx.DrawGeometry(ShadowNearBrush, null, localGeometry);
            using (ctx.PushTransform(Matrix.CreateTranslation(1, 4)))
                ctx.DrawGeometry(ShadowFarBrush, null, localGeometry);

            using (ctx.PushGeometryClip(localGeometry))
                ctx.DrawImage(board, src, dest);

            ctx.DrawGeometry(null, new Pen(OutlineBrush, 1), localGeometry);
        }
        return rtb;
    }
}
