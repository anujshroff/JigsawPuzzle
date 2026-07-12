namespace JigsawPuzzle.Game;

/// <summary>Scans a folder for images and hands out random picks, avoiding immediate repeats.</summary>
public sealed class ImageLibrary
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png"];

    private readonly List<string> _files = [];
    private string? _lastPick;

    public int Count => _files.Count;

    /// <summary>Scans the folder and every subfolder beneath it, skipping inaccessible directories.</summary>
    public void LoadFolder(string folder)
    {
        _files.Clear();
        _lastPick = null;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };
        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                _files.Add(file);
        }
    }

    /// <summary>Random image path, never repeating the previous pick while more than one image exists.</summary>
    public string? NextRandom()
    {
        if (_files.Count == 0)
            return null;
        string pick;
        do
        {
            pick = _files[Random.Shared.Next(_files.Count)];
        } while (_files.Count > 1 && pick == _lastPick);
        _lastPick = pick;
        return pick;
    }

    /// <summary>Drop a file that failed to decode so it is not offered again.</summary>
    public void Exclude(string path) => _files.Remove(path);
}
