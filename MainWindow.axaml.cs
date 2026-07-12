using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JigsawPuzzle.Game;

namespace JigsawPuzzle;

public partial class MainWindow : Window
{
    private static readonly int[] PieceTargets = [20, 50, 100];

    private readonly ImageLibrary _library = new();
    private PuzzleGame? _game;
    private string? _currentImagePath;
    private DispatcherTimer? _solvedTimer;
    private int _difficulty = 1; // index into PieceTargets

    public MainWindow()
    {
        InitializeComponent();

        NewPuzzleButton.Click += (_, _) => StartNewGame(nextImage: true);
        TidyButton.Click += (_, _) =>
        {
            if (_game is not null)
            {
                _game.TidySingles(Board.Bounds.Size, Random.Shared);
                Board.InvalidateVisual();
            }
        };
        FolderButton.Click += async (_, _) => await PickFolderAsync();
        OverlayPickButton.Click += async (_, _) => await PickFolderAsync();
        EasyButton.Click += (_, _) => SelectDifficulty(0);
        MediumButton.Click += (_, _) => SelectDifficulty(1);
        HardButton.Click += (_, _) => SelectDifficulty(2);
        UpdateButton.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(UpdateChecker.ReleasesPage));
        Board.Solved += OnSolved;

        Loaded += async (_, _) =>
        {
            _ = ShowUpdateNoticeAsync();

            // Optional CLI argument: a folder to load directly (skips the picker).
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && Directory.Exists(args[1]))
            {
                try { await Task.Run(() => _library.LoadFolder(args[1])); } catch { /* fall through to picker */ }
            }

            if (_library.Count > 0)
                StartNewGame(nextImage: true);
            else
                await PickFolderAsync();
        };
    }

    private async Task PickFolderAsync()
    {
        var picks = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder containing images (jpg, jpeg, png)",
            AllowMultiple = false,
        });
        var folder = picks.Count > 0 ? picks[0].TryGetLocalPath() : null;

        if (folder is null)
        {
            // Cancelled — keep any running game; otherwise prompt again.
            if (_game is null)
                ShowOverlay("Pick a folder with images (.jpg, .jpeg, .png) to start.", showButton: true);
            return;
        }

        try
        {
            // Recursive scan can take a moment on big trees — keep the UI responsive.
            await Task.Run(() => _library.LoadFolder(folder));
        }
        catch (Exception ex)
        {
            ShowOverlay($"Could not read that folder:\n{ex.Message}", showButton: true);
            return;
        }

        if (_library.Count == 0)
        {
            ShowOverlay($"No images found in:\n{folder}\n\nPick a folder containing .jpg, .jpeg or .png files.", showButton: true);
            return;
        }

        _currentImagePath = null;
        StartNewGame(nextImage: true);
    }

    /// <summary>Starts a puzzle: next random image, or the current one re-cut when <paramref name="nextImage"/> is false.</summary>
    private void StartNewGame(bool nextImage)
    {
        _solvedTimer?.Stop();
        _solvedTimer = null;

        if (_library.Count == 0)
        {
            if (_game is null)
                ShowOverlay("Pick a folder with images (.jpg, .jpeg, .png) to start.", showButton: true);
            return;
        }

        var path = !nextImage && _currentImagePath is not null ? _currentImagePath : _library.NextRandom();
        while (path is not null)
        {
            if (TryStartGame(path))
            {
                HideOverlay();
                return;
            }
            _library.Exclude(path); // unreadable image — drop it and try another
            path = _library.NextRandom();
        }
        ShowOverlay("None of the images in this folder could be loaded.\nPick another folder.", showButton: true);
    }

    private bool TryStartGame(string path)
    {
        var canvasSize = Board.Bounds.Size;
        if (canvasSize.Width < 100 || canvasSize.Height < 100)
            canvasSize = new Size(1200, 700); // pre-layout fallback

        Bitmap scaled;
        try
        {
            using var stream = File.OpenRead(path);
            // Decode capped at 2048px wide to bound memory, then scale once to exact board size.
            using var decoded = Bitmap.DecodeToWidth(stream, 2048, BitmapInterpolationMode.HighQuality);

            // Board occupies ~65% of the canvas, leaving room to scatter pieces around it.
            var scale = Math.Min(canvasSize.Width * 0.65 / decoded.Size.Width,
                                 canvasSize.Height * 0.65 / decoded.Size.Height);
            var boardPixels = new PixelSize(
                Math.Max(2, (int)Math.Round(decoded.Size.Width * scale)),
                Math.Max(2, (int)Math.Round(decoded.Size.Height * scale)));
            scaled = decoded.CreateScaledBitmap(boardPixels, BitmapInterpolationMode.HighQuality);
        }
        catch
        {
            return false;
        }

        try
        {
            var target = PieceTargets[Math.Clamp(_difficulty, 0, PieceTargets.Length - 1)];
            var (rows, cols) = PieceFactory.ComputeGrid(target, scaled.Size.Width / scaled.Size.Height);
            var pieces = PieceFactory.CreatePieces(scaled, rows, cols);

            var minCell = Math.Min(scaled.Size.Width / cols, scaled.Size.Height / rows);
            var game = new PuzzleGame(pieces, rows, cols, snapTolerance: Math.Max(10, 0.18 * minCell));
            game.ArrangeInGrid(canvasSize, Random.Shared);

            _game?.Dispose();
            _game = game;
            Board.Game = game;
            _currentImagePath = path;
            return true;
        }
        finally
        {
            scaled.Dispose(); // pieces hold their own baked bitmaps
        }
    }

    private async Task ShowUpdateNoticeAsync()
    {
#if DEBUG
        await Task.CompletedTask; // dev builds carry the default 1.0.0 version — skip the check
#else
        var latest = await UpdateChecker.CheckAsync();
        if (latest is null)
            return;
        var display = latest.Build >= 0 ? latest.ToString(3) : latest.ToString(2);
        UpdateButton.Content = $"Update v{display} available";
        UpdateButton.IsVisible = true;
#endif
    }

    private void SelectDifficulty(int index)
    {
        if (_difficulty == index)
            return;
        _difficulty = index;
        EasyButton.Classes.Set("selected", index == 0);
        MediumButton.Classes.Set("selected", index == 1);
        HardButton.Classes.Set("selected", index == 2);
        if (_currentImagePath is not null)
            StartNewGame(nextImage: false); // re-cut the current image with the new grid
    }

    private void OnSolved()
    {
        ShowOverlay("Solved!", showButton: false);
        _solvedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _solvedTimer.Tick += (_, _) => StartNewGame(nextImage: true); // StartNewGame stops the timer
        _solvedTimer.Start();
    }

    private void ShowOverlay(string text, bool showButton)
    {
        OverlayText.Text = text;
        OverlayPickButton.IsVisible = showButton;
        Overlay.IsVisible = true;
    }

    private void HideOverlay() => Overlay.IsVisible = false;
}
